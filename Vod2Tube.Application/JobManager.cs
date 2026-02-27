using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    public class JobManager : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<JobManager> _logger;

        // Stages ordered from lowest to highest priority (furthest along = highest priority)
        internal static readonly string[] StagePriority =
        [
            "Pending",
            "DownloadingVod",
            "PendingDownloadChat",
            "DownloadingChat",
            "PendingRenderingChat",
            "RenderingChat",
            "PendingCombining",
            "Combining",
            "PendingUpload",
            "Uploading"
        ];

        public JobManager(IServiceScopeFactory scopeFactory, ILogger<JobManager> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("JobManager started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    Pipeline? job = await FindHighestPriorityJobAsync(dbContext, stoppingToken);

                    if (job == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }

                    await ProcessJobToCompletionAsync(job, dbContext, scope.ServiceProvider, _logger, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in JobManager");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            _logger.LogInformation("JobManager stopped");
        }

        internal static async Task<Pipeline?> FindHighestPriorityJobAsync(AppDbContext dbContext, CancellationToken ct)
        {
            return await dbContext.Pipelines
                .Where(p => StagePriority.Contains(p.Stage) && !p.Failed)
                .OrderByDescending(p =>
                    p.Stage == "Uploading"            ? 9 :
                    p.Stage == "PendingUpload"        ? 8 :
                    p.Stage == "Combining"            ? 7 :
                    p.Stage == "PendingCombining"     ? 6 :
                    p.Stage == "RenderingChat"        ? 5 :
                    p.Stage == "PendingRenderingChat" ? 4 :
                    p.Stage == "DownloadingChat"      ? 3 :
                    p.Stage == "PendingDownloadChat"  ? 2 :
                    p.Stage == "DownloadingVod"       ? 1 :
                    /* Pending */                       0)
                .ThenBy(p => p.VodId)
                .FirstOrDefaultAsync(ct);
        }

        private static async Task SetStageAsync(AppDbContext dbContext, Pipeline job, string stage, CancellationToken ct)
        {
            job.Stage = stage;
            await dbContext.SaveChangesAsync(ct);
        }

        internal static async Task ProcessJobToCompletionAsync(Pipeline job, AppDbContext dbContext, IServiceProvider services, ILogger logger, CancellationToken ct)
        {
            var vodDownloader  = services.GetRequiredService<VodDownloader>();
            var chatDownloader = services.GetRequiredService<ChatDownloader>();
            var chatRenderer   = services.GetRequiredService<ChatRenderer>();
            var finalRenderer  = services.GetRequiredService<FinalRenderer>();
            var videoUploader  = services.GetRequiredService<VideoUploader>();

            logger.LogInformation("Processing job {VodId} from stage: {Stage}", job.VodId, job.Stage);

            try
            {
                if (job.Stage == "Pending" || job.Stage == "DownloadingVod")
                {
                    await SetStageAsync(dbContext, job, "DownloadingVod", ct);
                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach (var status in vodDownloader.RunAsync(job.VodId, ct))
                    {
                        if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                        {
                            lastUpdate = DateTime.UtcNow;
                            job.Description = status;
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                        }
                    }
                    job.VodFilePath = vodDownloader.GetOutputPath(job.VodId);
                    await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                }

                if (job.Stage == "PendingDownloadChat" || job.Stage == "DownloadingChat")
                {
                    await SetStageAsync(dbContext, job, "DownloadingChat", ct);
                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach (var status in chatDownloader.RunAsync(job.VodId, ct))
                    {
                        if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                        {
                            lastUpdate = DateTime.UtcNow;
                            job.Description = status;
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                        }
                    }
                    job.ChatTextFilePath = chatDownloader.GetOutputPath(job.VodId);
                    await dbContext.SaveChangesAsync(ct);
                    await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                }

                if (job.Stage == "PendingRenderingChat" || job.Stage == "RenderingChat")
                {
                    if (string.IsNullOrEmpty(job.VodFilePath))
                    {
                        await SetStageAsync(dbContext, job, "Pending", ct);
                        return;
                    }
                    if (string.IsNullOrEmpty(job.ChatTextFilePath))
                    {
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                        return;
                    }

                    await SetStageAsync(dbContext, job, "RenderingChat", ct);
                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach (var status in chatRenderer.RunAsync(job.VodId, job.ChatTextFilePath, job.VodFilePath, ct))
                    {
                        if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                        {
                            lastUpdate = DateTime.UtcNow;
                            job.Description = status;
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                        }
                    }
                    job.ChatVideoFilePath = chatRenderer.GetOutputPath(job.VodId);
                    await dbContext.SaveChangesAsync(ct);
                    await SetStageAsync(dbContext, job, "PendingCombining", ct);
                }

                if (job.Stage == "PendingCombining" || job.Stage == "Combining")
                {
                    if (string.IsNullOrEmpty(job.VodFilePath))
                    {
                        await SetStageAsync(dbContext, job, "Pending", ct);
                        return;
                    }
                    if (string.IsNullOrEmpty(job.ChatVideoFilePath))
                    {
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                        return;
                    }

                    await SetStageAsync(dbContext, job, "Combining", ct);
                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach (var status in finalRenderer.RunAsync(job.VodId, job.VodFilePath, job.ChatVideoFilePath, ct))
                    {
                        if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                        {
                            lastUpdate = DateTime.UtcNow;
                            job.Description = status;
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                        }
                    }
                    job.FinalVideoFilePath = finalRenderer.GetOutputPath(job.VodId);
                    await dbContext.SaveChangesAsync(ct);
                    await SetStageAsync(dbContext, job, "PendingUpload", ct);
                }

                if (job.Stage == "PendingUpload" || job.Stage == "Uploading")
                {
                    if (string.IsNullOrEmpty(job.FinalVideoFilePath))
                    {
                        job.Description = "Final video file missing, rerunning combining stage.";
                        await SetStageAsync(dbContext, job, "PendingCombining", ct);
                        return;
                    }

                    await SetStageAsync(dbContext, job, "Uploading", ct);
                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach (var status in videoUploader.RunAsync(job.VodId, job.FinalVideoFilePath, ct))
                    {
                        if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                        {
                            lastUpdate = DateTime.UtcNow;
                            job.Description = status;
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                        }
                    }
                    await SetStageAsync(dbContext, job, "Uploaded", ct);
                }

                logger.LogInformation("Job {VodId} completed", job.VodId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string failedAtStage = job.Stage;
                bool isPermanent = ex is PipelineJobException pje && pje.IsPermanent;
                string failMessage = $"Failed at stage '{failedAtStage}': {ex.Message}";

                job.FailCount++;
                job.Description = failMessage;

                if (isPermanent || job.FailCount >= 3)
                {
                    job.Failed = true;
                    job.FailReason = failMessage;
                    logger.LogError(ex, "Job {VodId} permanently failed at stage '{Stage}' (FailCount={FailCount})", job.VodId, failedAtStage, job.FailCount);
                }
                else
                {
                    logger.LogWarning(ex, "Job {VodId} failed at stage '{Stage}' (FailCount={FailCount}), will retry", job.VodId, failedAtStage, job.FailCount);
                }

                try { await dbContext.SaveChangesAsync(CancellationToken.None); }
                catch { /* best-effort */ }
            }
        }
    }
}

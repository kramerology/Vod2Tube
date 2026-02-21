using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    public class JobManager : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

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

        public JobManager(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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

                    await ProcessJobToCompletionAsync(job, dbContext, scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in JobManager: {ex}");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        internal static async Task<Pipeline?> FindHighestPriorityJobAsync(AppDbContext dbContext, CancellationToken ct)
        {
            return await dbContext.Pipelines
                .Where(p => StagePriority.Contains(p.Stage))
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
                .FirstOrDefaultAsync(ct);
        }

        private static async Task SetStageAsync(AppDbContext dbContext, Pipeline job, string stage, CancellationToken ct)
        {
            job.Stage = stage;
            await dbContext.SaveChangesAsync(ct);
        }

        internal static async Task ProcessJobToCompletionAsync(Pipeline job, AppDbContext dbContext, IServiceProvider services, CancellationToken ct)
        {
            var vodDownloader  = services.GetRequiredService<VodDownloader>();
            var chatDownloader = services.GetRequiredService<ChatDownloader>();
            var chatRenderer   = services.GetRequiredService<ChatRenderer>();
            var finalRenderer  = services.GetRequiredService<FinalRenderer>();
            var videoUploader  = services.GetRequiredService<VideoUploader>();

            Console.WriteLine($"Processing job {job.VodId} from stage: {job.Stage}");

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
                            catch (Exception ex) { Console.WriteLine($"Warning: failed to save progress for job {job.VodId}: {ex.Message}"); }
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
                            catch (Exception ex) { Console.WriteLine($"Warning: failed to save progress for job {job.VodId}: {ex.Message}"); }
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
                            catch (Exception ex) { Console.WriteLine($"Warning: failed to save progress for job {job.VodId}: {ex.Message}"); }
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
                            catch (Exception ex) { Console.WriteLine($"Warning: failed to save progress for job {job.VodId}: {ex.Message}"); }
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
                            catch (Exception ex) { Console.WriteLine($"Warning: failed to save progress for job {job.VodId}: {ex.Message}"); }
                        }
                    }
                    await SetStageAsync(dbContext, job, "Uploaded", ct);
                }

                Console.WriteLine($"Job {job.VodId} completed.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string failedAtStage = job.Stage;
                job.Stage = "Failed";
                job.Description = $"Failed at stage '{failedAtStage}': {ex.Message}";
                try { await dbContext.SaveChangesAsync(CancellationToken.None); }
                catch { /* best-effort */ }
                Console.WriteLine($"Job {job.VodId} failed at stage '{failedAtStage}': {ex}");
            }
        }
    }
}

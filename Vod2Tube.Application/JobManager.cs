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

        // Maximum number of total attempts before a transient failure is treated as permanent.
        internal const int MaxRetryAttempts = 3;

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

                    // If the job was paused during processing, wait for it to be unpaused
                    // before picking up any more work.
                    if (job.Paused)
                    {
                        _logger.LogInformation("Job {VodId} is paused; waiting for it to be unpaused", job.VodId);
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            if (!await IsJobPausedAsync(dbContext, job.VodId, stoppingToken))
                            {
                                job.Paused = false;
                                break;
                            }
                        }
                        if (!stoppingToken.IsCancellationRequested)
                            _logger.LogInformation("Job {VodId} unpaused; resuming processing", job.VodId);
                    }
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
                .Where(p => StagePriority.Contains(p.Stage) && !p.Failed && !p.Paused)
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
            if (job.Stage != stage)
                job.FailCount = 0;
            job.Stage = stage;
            await dbContext.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Returns true if the job's Paused flag is set in the database.
        /// Uses AsNoTracking so it does not overwrite any pending entity changes.
        /// </summary>
        internal static async Task<bool> IsJobPausedAsync(AppDbContext dbContext, string vodId, CancellationToken ct)
        {
            return await dbContext.Pipelines
                .AsNoTracking()
                .Where(p => p.VodId == vodId)
                .Select(p => p.Paused)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>
        /// Returns true if the job's Stage has been set to "Cancelled" in the database.
        /// Uses AsNoTracking so it does not overwrite any pending entity changes.
        /// </summary>
        internal static async Task<bool> IsJobCancelledAsync(AppDbContext dbContext, string vodId, CancellationToken ct)
        {
            return await dbContext.Pipelines
                .AsNoTracking()
                .Where(p => p.VodId == vodId)
                .Select(p => p.Stage)
                .FirstOrDefaultAsync(ct) == "Cancelled";
        }

        /// <summary>
        /// Checks whether the job has been paused externally.  If so, sets <see cref="Pipeline.Paused"/>
        /// on the in-memory entity, logs the event, and returns <c>true</c> so the caller can
        /// immediately return and stop further processing.
        /// </summary>
        private static async Task<bool> DetectAndApplyPauseAsync(AppDbContext dbContext, Pipeline job, ILogger logger, CancellationToken ct)
        {
            if (await IsJobPausedAsync(dbContext, job.VodId, ct))
            {
                job.Paused = true;
                logger.LogInformation("Job {VodId} paused at stage {Stage}", job.VodId, job.Stage);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks whether the job has been cancelled externally (Stage set to "Cancelled" in the
        /// database).  If so, logs the event and returns <c>true</c> so the caller can immediately
        /// return and stop further processing. The Stage is already "Cancelled" in the database.
        /// </summary>
        private static async Task<bool> DetectAndApplyCancelAsync(AppDbContext dbContext, Pipeline job, ILogger logger, CancellationToken ct)
        {
            if (await IsJobCancelledAsync(dbContext, job.VodId, ct))
            {
                logger.LogInformation("Job {VodId} cancelled at stage {Stage}", job.VodId, job.Stage);
                return true;
            }
            return false;
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
                // Guard against cancellation that arrived after the job was picked up.
                if (await DetectAndApplyCancelAsync(dbContext, job, logger, ct))
                    return;

                if (job.Stage == "Pending" || job.Stage == "DownloadingVod")
                {
                    await SetStageAsync(dbContext, job, "DownloadingVod", ct);

                    if (!string.IsNullOrEmpty(job.VodFilePath) && File.Exists(job.VodFilePath))
                    {
                        logger.LogInformation("VOD file already exists at {Path} for job {VodId}, skipping download", job.VodFilePath, job.VodId);
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.VodFilePath))
                        {
                            logger.LogWarning("VOD file path {Path} from database not found on disk for job {VodId}, re-downloading", job.VodFilePath, job.VodId);
                            job.VodFilePath = "";
                        }
                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in vodDownloader.RunAsync(job.VodId, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                using (logger.BeginScope(new Dictionary<string, object?> { ["IsProgress"] = true }))
                                    logger.LogInformation("{Status}", status);
                                try
                                {
                                    await dbContext.SaveChangesAsync(ct);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId);
                                }
                                if (await DetectAndApplyPauseAsync(dbContext, job, logger, ct))
                                    return;
                                if (await DetectAndApplyCancelAsync(dbContext, job, logger, ct))
                                    return;
                            }
                        }
                        Console.WriteLine(); // end the in-place progress line
                        string vodOutput = vodDownloader.GetOutputPath(job.VodId);
                        if (!File.Exists(vodOutput))
                            throw new InvalidOperationException($"VOD download completed but output file not found: {vodOutput}");
                        job.VodFilePath = vodOutput;
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                    }
                }

                if (job.Stage == "PendingDownloadChat" || job.Stage == "DownloadingChat")
                {
                    await SetStageAsync(dbContext, job, "DownloadingChat", ct);

                    if (!string.IsNullOrEmpty(job.ChatTextFilePath) && File.Exists(job.ChatTextFilePath))
                    {
                        logger.LogInformation("Chat file already exists at {Path} for job {VodId}, skipping download", job.ChatTextFilePath, job.VodId);
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.ChatTextFilePath))
                        {
                            logger.LogWarning("Chat file path {Path} from database not found on disk for job {VodId}, re-downloading", job.ChatTextFilePath, job.VodId);
                            job.ChatTextFilePath = "";
                        }
                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in chatDownloader.RunAsync(job.VodId, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                using (logger.BeginScope(new Dictionary<string, object?> { ["IsProgress"] = true }))
                                    logger.LogInformation("{Status}", status);
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                                if (await DetectAndApplyPauseAsync(dbContext, job, logger, ct))
                                    return;
                                if (await DetectAndApplyCancelAsync(dbContext, job, logger, ct))
                                    return;
                            }
                        }
                        Console.WriteLine(); // end the in-place progress line
                        job.ChatTextFilePath = chatDownloader.GetOutputPath(job.VodId);
                        if (!File.Exists(job.ChatTextFilePath))
                            throw new InvalidOperationException($"Chat download completed but output file not found: {job.ChatTextFilePath}");
                        await dbContext.SaveChangesAsync(ct);
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                    }
                }

                if (job.Stage == "PendingRenderingChat" || job.Stage == "RenderingChat")
                {
                    if (string.IsNullOrEmpty(job.VodFilePath) || !File.Exists(job.VodFilePath))
                    {
                        logger.LogWarning("VOD file missing for job {VodId}, rolling back to Pending", job.VodId);
                        job.VodFilePath = "";
                        await SetStageAsync(dbContext, job, "Pending", ct);
                        return;
                    }
                    if (string.IsNullOrEmpty(job.ChatTextFilePath) || !File.Exists(job.ChatTextFilePath))
                    {
                        logger.LogWarning("Chat text file missing for job {VodId}, rolling back to PendingDownloadChat", job.VodId);
                        job.ChatTextFilePath = "";
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                        return;
                    }

                    if (!string.IsNullOrEmpty(job.ChatVideoFilePath) && File.Exists(job.ChatVideoFilePath))
                    {
                        logger.LogInformation("Chat video already exists at {Path} for job {VodId}, skipping render", job.ChatVideoFilePath, job.VodId);
                        await SetStageAsync(dbContext, job, "PendingCombining", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.ChatVideoFilePath))
                        {
                            logger.LogWarning("Chat video path {Path} from database not found on disk for job {VodId}, re-rendering", job.ChatVideoFilePath, job.VodId);
                            job.ChatVideoFilePath = "";
                        }
                        await SetStageAsync(dbContext, job, "RenderingChat", ct);
                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in chatRenderer.RunAsync(job.VodId, job.ChatTextFilePath, job.VodFilePath, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                using (logger.BeginScope(new Dictionary<string, object?> { ["IsProgress"] = true }))
                                    logger.LogInformation("{Status}", status);
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                                if (await DetectAndApplyPauseAsync(dbContext, job, logger, ct))
                                    return;
                                if (await DetectAndApplyCancelAsync(dbContext, job, logger, ct))
                                    return;
                            }
                        }
                        Console.WriteLine(); // end the in-place progress line
                        job.ChatVideoFilePath = chatRenderer.GetOutputPath(job.VodId);
                        if (!File.Exists(job.ChatVideoFilePath))
                            throw new InvalidOperationException($"Chat render completed but output file not found: {job.ChatVideoFilePath}");
                        await dbContext.SaveChangesAsync(ct);
                        await SetStageAsync(dbContext, job, "PendingCombining", ct);
                    }
                }

                if (job.Stage == "PendingCombining" || job.Stage == "Combining")
                {
                    if (string.IsNullOrEmpty(job.VodFilePath) || !File.Exists(job.VodFilePath))
                    {
                        logger.LogWarning("VOD file missing for job {VodId}, rolling back to Pending", job.VodId);
                        job.VodFilePath = "";
                        await SetStageAsync(dbContext, job, "Pending", ct);
                        return;
                    }
                    if (string.IsNullOrEmpty(job.ChatVideoFilePath) || !File.Exists(job.ChatVideoFilePath))
                    {
                        logger.LogWarning("Chat video file missing for job {VodId}, rolling back to PendingRenderingChat", job.VodId);
                        job.ChatVideoFilePath = "";
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                        return;
                    }

                    if (!string.IsNullOrEmpty(job.FinalVideoFilePath) && File.Exists(job.FinalVideoFilePath))
                    {
                        logger.LogInformation("Final video already exists at {Path} for job {VodId}, skipping combine", job.FinalVideoFilePath, job.VodId);
                        await SetStageAsync(dbContext, job, "PendingUpload", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.FinalVideoFilePath))
                        {
                            logger.LogWarning("Final video path {Path} from database not found on disk for job {VodId}, re-combining", job.FinalVideoFilePath, job.VodId);
                            job.FinalVideoFilePath = "";
                        }
                        await SetStageAsync(dbContext, job, "Combining", ct);
                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in finalRenderer.RunAsync(job.VodId, job.VodFilePath, job.ChatVideoFilePath, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                using (logger.BeginScope(new Dictionary<string, object?> { ["IsProgress"] = true }))
                                    logger.LogInformation("{Status}", status);
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                                if (await DetectAndApplyPauseAsync(dbContext, job, logger, ct))
                                    return;
                                if (await DetectAndApplyCancelAsync(dbContext, job, logger, ct))
                                    return;
                            }
                        }
                        Console.WriteLine(); // end the in-place progress line
                        job.FinalVideoFilePath = finalRenderer.GetOutputPath(job.VodId);
                        if (!File.Exists(job.FinalVideoFilePath))
                            throw new InvalidOperationException($"Video combine completed but output file not found: {job.FinalVideoFilePath}");
                        await dbContext.SaveChangesAsync(ct);
                        await SetStageAsync(dbContext, job, "PendingUpload", ct);
                    }
                }

                if (job.Stage == "PendingUpload" || job.Stage == "Uploading")
                {
                    if (string.IsNullOrEmpty(job.FinalVideoFilePath) || !File.Exists(job.FinalVideoFilePath))
                    {
                        logger.LogWarning("Final video file missing for job {VodId}, rolling back to PendingCombining", job.VodId);
                        job.FinalVideoFilePath = "";
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
                            using (logger.BeginScope(new Dictionary<string, object?> { ["IsProgress"] = true }))
                                logger.LogInformation("{Status}", status);
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to save progress for job {VodId}", job.VodId); }
                        }
                    }
                    // After upload completes, check pause and cancellation before advancing stage.
                    if (await DetectAndApplyPauseAsync(dbContext, job, logger, ct))
                        return;
                    if (await DetectAndApplyCancelAsync(dbContext, job, logger, ct))
                        return;
                    Console.WriteLine(); // end the in-place progress line
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

                if (isPermanent || job.FailCount >= MaxRetryAttempts)
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

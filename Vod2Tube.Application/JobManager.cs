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
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in JobManager loop");
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
            if (job.Stage != stage)
                job.FailCount = 0;
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

            // Resolve friendly VOD metadata so log messages are human-readable.
            var vodMeta = await dbContext.TwitchVods.FirstOrDefaultAsync(v => v.Id == job.VodId, ct);
            string vodTitle   = vodMeta?.Title       ?? $"VOD {job.VodId}";
            string channelName = vodMeta?.ChannelName ?? "unknown channel";

            logger.LogInformation(
                "Starting job | VOD: '{VodTitle}' ({VodId}) | Channel: {ChannelName} | Stage: {Stage}",
                vodTitle, job.VodId, channelName, job.Stage);

            try
            {
                // ── Stage 1: Download VOD ───────────────────────────────────────────────
                if (job.Stage == "Pending" || job.Stage == "DownloadingVod")
                {
                    await SetStageAsync(dbContext, job, "DownloadingVod", ct);

                    if (!string.IsNullOrEmpty(job.VodFilePath) && File.Exists(job.VodFilePath))
                    {
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — VOD file already on disk, skipping download | Path: {Path}",
                            job.VodId, vodTitle, job.VodFilePath);
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.VodFilePath))
                        {
                            logger.LogWarning(
                                "[{VodId}] '{VodTitle}' — VOD file missing from disk, re-downloading | ExpectedPath: {Path}",
                                job.VodId, vodTitle, job.VodFilePath);
                            job.VodFilePath = "";
                        }

                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' (Channel: {ChannelName}) — Starting VOD download",
                            job.VodId, vodTitle, channelName);

                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in vodDownloader.RunAsync(job.VodId, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to persist progress for [{VodId}]", job.VodId); }
                            }
                            // Trace → Serilog Verbose; shown inline on console, excluded from file.
                            logger.LogTrace("[{VodId}] Downloading VOD | {Status}", job.VodId, status);
                        }

                        string vodOutput = vodDownloader.GetOutputPath(job.VodId);
                        if (!File.Exists(vodOutput))
                            throw new InvalidOperationException($"VOD download completed but output file not found: {vodOutput}");
                        job.VodFilePath = vodOutput;
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — VOD download complete | File: {Path}",
                            job.VodId, vodTitle, vodOutput);
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                    }
                }

                // ── Stage 2: Download Chat ──────────────────────────────────────────────
                if (job.Stage == "PendingDownloadChat" || job.Stage == "DownloadingChat")
                {
                    await SetStageAsync(dbContext, job, "DownloadingChat", ct);

                    if (!string.IsNullOrEmpty(job.ChatTextFilePath) && File.Exists(job.ChatTextFilePath))
                    {
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — Chat file already on disk, skipping download | Path: {Path}",
                            job.VodId, vodTitle, job.ChatTextFilePath);
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.ChatTextFilePath))
                        {
                            logger.LogWarning(
                                "[{VodId}] '{VodTitle}' — Chat file missing from disk, re-downloading | ExpectedPath: {Path}",
                                job.VodId, vodTitle, job.ChatTextFilePath);
                            job.ChatTextFilePath = "";
                        }

                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' (Channel: {ChannelName}) — Starting chat download",
                            job.VodId, vodTitle, channelName);

                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in chatDownloader.RunAsync(job.VodId, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to persist progress for [{VodId}]", job.VodId); }
                            }
                            logger.LogTrace("[{VodId}] Downloading chat | {Status}", job.VodId, status);
                        }

                        job.ChatTextFilePath = chatDownloader.GetOutputPath(job.VodId);
                        if (!File.Exists(job.ChatTextFilePath))
                            throw new InvalidOperationException($"Chat download completed but output file not found: {job.ChatTextFilePath}");
                        await dbContext.SaveChangesAsync(ct);
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — Chat download complete | File: {Path}",
                            job.VodId, vodTitle, job.ChatTextFilePath);
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                    }
                }

                // ── Stage 3: Render Chat ────────────────────────────────────────────────
                if (job.Stage == "PendingRenderingChat" || job.Stage == "RenderingChat")
                {
                    if (string.IsNullOrEmpty(job.VodFilePath) || !File.Exists(job.VodFilePath))
                    {
                        logger.LogWarning(
                            "[{VodId}] '{VodTitle}' — VOD file missing, rolling back to Pending",
                            job.VodId, vodTitle);
                        job.VodFilePath = "";
                        await SetStageAsync(dbContext, job, "Pending", ct);
                        return;
                    }
                    if (string.IsNullOrEmpty(job.ChatTextFilePath) || !File.Exists(job.ChatTextFilePath))
                    {
                        logger.LogWarning(
                            "[{VodId}] '{VodTitle}' — Chat file missing, rolling back to PendingDownloadChat",
                            job.VodId, vodTitle);
                        job.ChatTextFilePath = "";
                        await SetStageAsync(dbContext, job, "PendingDownloadChat", ct);
                        return;
                    }

                    if (!string.IsNullOrEmpty(job.ChatVideoFilePath) && File.Exists(job.ChatVideoFilePath))
                    {
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — Chat render already on disk, skipping | Path: {Path}",
                            job.VodId, vodTitle, job.ChatVideoFilePath);
                        await SetStageAsync(dbContext, job, "PendingCombining", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.ChatVideoFilePath))
                        {
                            logger.LogWarning(
                                "[{VodId}] '{VodTitle}' — Chat render missing from disk, re-rendering | ExpectedPath: {Path}",
                                job.VodId, vodTitle, job.ChatVideoFilePath);
                            job.ChatVideoFilePath = "";
                        }

                        await SetStageAsync(dbContext, job, "RenderingChat", ct);
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' (Channel: {ChannelName}) — Starting chat render",
                            job.VodId, vodTitle, channelName);

                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in chatRenderer.RunAsync(job.VodId, job.ChatTextFilePath, job.VodFilePath, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to persist progress for [{VodId}]", job.VodId); }
                            }
                            logger.LogTrace("[{VodId}] Rendering chat | {Status}", job.VodId, status);
                        }

                        job.ChatVideoFilePath = chatRenderer.GetOutputPath(job.VodId);
                        if (!File.Exists(job.ChatVideoFilePath))
                            throw new InvalidOperationException($"Chat render completed but output file not found: {job.ChatVideoFilePath}");
                        await dbContext.SaveChangesAsync(ct);
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — Chat render complete | File: {Path}",
                            job.VodId, vodTitle, job.ChatVideoFilePath);
                        await SetStageAsync(dbContext, job, "PendingCombining", ct);
                    }
                }

                // ── Stage 4: Combine (Final Render) ─────────────────────────────────────
                if (job.Stage == "PendingCombining" || job.Stage == "Combining")
                {
                    if (string.IsNullOrEmpty(job.VodFilePath) || !File.Exists(job.VodFilePath))
                    {
                        logger.LogWarning(
                            "[{VodId}] '{VodTitle}' — VOD file missing, rolling back to Pending",
                            job.VodId, vodTitle);
                        job.VodFilePath = "";
                        await SetStageAsync(dbContext, job, "Pending", ct);
                        return;
                    }
                    if (string.IsNullOrEmpty(job.ChatVideoFilePath) || !File.Exists(job.ChatVideoFilePath))
                    {
                        logger.LogWarning(
                            "[{VodId}] '{VodTitle}' — Chat video missing, rolling back to PendingRenderingChat",
                            job.VodId, vodTitle);
                        job.ChatVideoFilePath = "";
                        await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
                        return;
                    }

                    if (!string.IsNullOrEmpty(job.FinalVideoFilePath) && File.Exists(job.FinalVideoFilePath))
                    {
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — Final video already on disk, skipping combine | Path: {Path}",
                            job.VodId, vodTitle, job.FinalVideoFilePath);
                        await SetStageAsync(dbContext, job, "PendingUpload", ct);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(job.FinalVideoFilePath))
                        {
                            logger.LogWarning(
                                "[{VodId}] '{VodTitle}' — Final video missing from disk, re-combining | ExpectedPath: {Path}",
                                job.VodId, vodTitle, job.FinalVideoFilePath);
                            job.FinalVideoFilePath = "";
                        }

                        await SetStageAsync(dbContext, job, "Combining", ct);
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' (Channel: {ChannelName}) — Starting final video combine",
                            job.VodId, vodTitle, channelName);

                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in finalRenderer.RunAsync(job.VodId, job.VodFilePath, job.ChatVideoFilePath, ct))
                        {
                            if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                            {
                                lastUpdate = DateTime.UtcNow;
                                job.Description = status;
                                try { await dbContext.SaveChangesAsync(ct); }
                                catch (Exception ex) { logger.LogWarning(ex, "Failed to persist progress for [{VodId}]", job.VodId); }
                            }
                            logger.LogTrace("[{VodId}] Combining video | {Status}", job.VodId, status);
                        }

                        job.FinalVideoFilePath = finalRenderer.GetOutputPath(job.VodId);
                        if (!File.Exists(job.FinalVideoFilePath))
                            throw new InvalidOperationException($"Video combine completed but output file not found: {job.FinalVideoFilePath}");
                        await dbContext.SaveChangesAsync(ct);
                        logger.LogInformation(
                            "[{VodId}] '{VodTitle}' — Final video combine complete | File: {Path}",
                            job.VodId, vodTitle, job.FinalVideoFilePath);
                        await SetStageAsync(dbContext, job, "PendingUpload", ct);
                    }
                }

                // ── Stage 5: Upload ─────────────────────────────────────────────────────
                if (job.Stage == "PendingUpload" || job.Stage == "Uploading")
                {
                    if (string.IsNullOrEmpty(job.FinalVideoFilePath) || !File.Exists(job.FinalVideoFilePath))
                    {
                        logger.LogWarning(
                            "[{VodId}] '{VodTitle}' — Final video file missing, rolling back to PendingCombining",
                            job.VodId, vodTitle);
                        job.FinalVideoFilePath = "";
                        job.Description = "Final video file missing, rerunning combining stage.";
                        await SetStageAsync(dbContext, job, "PendingCombining", ct);
                        return;
                    }

                    await SetStageAsync(dbContext, job, "Uploading", ct);
                    logger.LogInformation(
                        "[{VodId}] '{VodTitle}' (Channel: {ChannelName}) — Starting YouTube upload",
                        job.VodId, vodTitle, channelName);

                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach (var status in videoUploader.RunAsync(job.VodId, job.FinalVideoFilePath, ct))
                    {
                        if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                        {
                            lastUpdate = DateTime.UtcNow;
                            job.Description = status;
                            try { await dbContext.SaveChangesAsync(ct); }
                            catch (Exception ex) { logger.LogWarning(ex, "Failed to persist progress for [{VodId}]", job.VodId); }
                        }
                        logger.LogTrace("[{VodId}] Uploading | {Status}", job.VodId, status);
                    }
                    await SetStageAsync(dbContext, job, "Uploaded", ct);
                }

                logger.LogInformation(
                    "Job complete ✓ | VOD: '{VodTitle}' ({VodId}) | Channel: {ChannelName}",
                    vodTitle, job.VodId, channelName);
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
                    logger.LogError(ex,
                        "Job permanently failed | VOD: '{VodTitle}' ({VodId}) | Channel: {ChannelName} | Stage: {Stage} | Attempt: {FailCount}",
                        vodTitle, job.VodId, channelName, failedAtStage, job.FailCount);
                }
                else
                {
                    logger.LogWarning(ex,
                        "Job failed (will retry) | VOD: '{VodTitle}' ({VodId}) | Channel: {ChannelName} | Stage: {Stage} | Attempt: {FailCount}/{MaxRetry}",
                        vodTitle, job.VodId, channelName, failedAtStage, job.FailCount, MaxRetryAttempts);
                }

                try { await dbContext.SaveChangesAsync(CancellationToken.None); }
                catch { /* best-effort */ }
            }
        }
    }
}

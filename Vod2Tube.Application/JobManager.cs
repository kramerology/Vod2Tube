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
                    Console.WriteLine($"Error in JobManager: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        internal static async Task<Pipeline?> FindHighestPriorityJobAsync(AppDbContext dbContext, CancellationToken ct)
        {
            // Walk from highest priority (furthest along) to lowest
            for (int i = StagePriority.Length - 1; i >= 0; i--)
            {
                var job = await dbContext.Pipelines
                    .Where(p => p.Stage == StagePriority[i])
                    .FirstOrDefaultAsync(ct);
                if (job != null)
                    return job;
            }
            return null;
        }

        private static async Task SetStageAsync(AppDbContext dbContext, Pipeline job, string stage, CancellationToken ct)
        {
            job.Stage = stage;
            await dbContext.SaveChangesAsync(ct);
        }

        private static async Task ProcessJobToCompletionAsync(Pipeline job, AppDbContext dbContext, IServiceProvider services, CancellationToken ct)
        {
            var vodDownloader  = services.GetRequiredService<VodDownloader>();
            var chatDownloader = services.GetRequiredService<ChatDownloader>();
            var chatRenderer   = services.GetRequiredService<ChatRenderer>();
            var finalRenderer  = services.GetRequiredService<FinalRenderer>();
            var videoUploader  = services.GetRequiredService<VideoUploader>();

            Console.WriteLine($"Processing job {job.VodId} from stage: {job.Stage}");

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
                        await dbContext.SaveChangesAsync(ct);
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
                        await dbContext.SaveChangesAsync(ct);
                    }
                }
                job.ChatTextFilePath = chatDownloader.GetOutputPath(job.VodId);
                await SetStageAsync(dbContext, job, "PendingRenderingChat", ct);
            }

            if (job.Stage == "PendingRenderingChat" || job.Stage == "RenderingChat")
            {
                await SetStageAsync(dbContext, job, "RenderingChat", ct);
                DateTime lastUpdate = DateTime.MinValue;
                await foreach (var status in chatRenderer.RunAsync(job.VodId, job.ChatTextFilePath, job.VodFilePath, ct))
                {
                    if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                    {
                        lastUpdate = DateTime.UtcNow;
                        job.Description = status;
                        await dbContext.SaveChangesAsync(ct);
                    }
                }
                job.ChatVideoFilePath = chatRenderer.GetOutputPath(job.VodId);
                await SetStageAsync(dbContext, job, "PendingCombining", ct);
            }

            if (job.Stage == "PendingCombining" || job.Stage == "Combining")
            {
                await SetStageAsync(dbContext, job, "Combining", ct);
                DateTime lastUpdate = DateTime.MinValue;
                await foreach (var status in finalRenderer.RunAsync(job.VodId, job.VodFilePath, job.ChatVideoFilePath, ct))
                {
                    if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                    {
                        lastUpdate = DateTime.UtcNow;
                        job.Description = status;
                        await dbContext.SaveChangesAsync(ct);
                    }
                }
                job.FinalVideoFilePath = finalRenderer.GetOutputPath(job.VodId);
                await SetStageAsync(dbContext, job, "PendingUpload", ct);
            }

            if (job.Stage == "PendingUpload" || job.Stage == "Uploading")
            {
                await SetStageAsync(dbContext, job, "Uploading", ct);
                DateTime lastUpdate = DateTime.MinValue;
                await foreach (var status in videoUploader.RunAsync(job.VodId, job.FinalVideoFilePath, ct))
                {
                    if (DateTime.UtcNow - lastUpdate >= TimeSpan.FromSeconds(2))
                    {
                        lastUpdate = DateTime.UtcNow;
                        job.Description = status;
                        await dbContext.SaveChangesAsync(ct);
                    }
                }
                await SetStageAsync(dbContext, job, "Uploaded", ct);
            }

            Console.WriteLine($"Job {job.VodId} completed.");
        }
    }
}

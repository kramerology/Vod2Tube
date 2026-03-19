using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vod2Tube.Application.Models;
using Vod2Tube.Infrastructure;
using DomainTwitchVod = Vod2Tube.Domain.TwitchVod;
using DomainPipeline = Vod2Tube.Domain.Pipeline;

namespace Vod2Tube.Application.Services
{
    public class PipelineService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<PipelineService> _logger;

        // Stages that represent an active (in-progress or queued) job
        private static readonly HashSet<string> ActiveStages = new(StringComparer.OrdinalIgnoreCase)
        {
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
        };

        public PipelineService(AppDbContext dbContext, ILogger<PipelineService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<List<PipelineJobDto>> GetActiveJobsAsync()
        {
            var pipelines = await _dbContext.Pipelines
                .AsNoTracking()
                .Where(p => ActiveStages.Contains(p.Stage) || (p.Failed && p.Stage != "Cancelled"))
                .ToListAsync();

            var vodIds = pipelines.Select(p => p.VodId).ToList();
            var vods = await _dbContext.TwitchVods
                .AsNoTracking()
                .Where(v => vodIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id);

            return pipelines
                .OrderByDescending(p => Array.IndexOf(JobManager.StagePriority, p.Stage))
                .ThenBy(p => p.VodId)
                .Select(p => MapToDto(p, vods.GetValueOrDefault(p.VodId)))
                .ToList();
        }

        public async Task<List<PipelineJobDto>> GetCompletedJobsAsync()
        {
            var pipelines = await _dbContext.Pipelines
                .AsNoTracking()
                .Where(p => p.Stage == "Uploaded" || p.Stage == "Cancelled")
                .OrderByDescending(p => p.VodId)
                .ToListAsync();

            var vodIds = pipelines.Select(p => p.VodId).ToList();
            var vods = await _dbContext.TwitchVods
                .AsNoTracking()
                .Where(v => vodIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id);

            return pipelines.Select(p => MapToDto(p, vods.GetValueOrDefault(p.VodId))).ToList();
        }

        public async Task<List<PipelineJobDto>> GetAllJobsAsync()
        {
            var pipelines = await _dbContext.Pipelines
                .AsNoTracking()
                .ToListAsync();

            var vodIds = pipelines.Select(p => p.VodId).ToList();
            var vods = await _dbContext.TwitchVods
                .AsNoTracking()
                .Where(v => vodIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id);

            _logger.LogInformation("Loaded {Count} VODs from database", pipelines.Count);

            return pipelines
                .OrderByDescending(p => Array.IndexOf(JobManager.StagePriority, p.Stage))
                .ThenBy(p => p.VodId)
                .Select(p => MapToDto(p, vods.GetValueOrDefault(p.VodId)))
                .ToList();
        }

        public async Task<bool> PauseJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null)
            {
                _logger.LogWarning("Pause requested for unknown VOD {VodId}", vodId);
                return false;
            }
            job.Paused = true;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Paused VOD {VodId}", vodId);
            return true;
        }

        public async Task<bool> ResumeJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null)
            {
                _logger.LogWarning("Resume requested for unknown VOD {VodId}", vodId);
                return false;
            }
            job.Paused = false;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Resumed VOD {VodId}", vodId);
            return true;
        }

        public async Task<bool> CancelJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null)
            {
                _logger.LogWarning("Cancel requested for unknown VOD {VodId}", vodId);
                return false;
            }
            job.Stage = "Cancelled";
            job.Paused = false;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Cancelled VOD {VodId}", vodId);
            return true;
        }

        public async Task<bool> RetryJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null)
            {
                _logger.LogWarning("Retry requested for unknown VOD {VodId}", vodId);
                return false;
            }
            job.Failed = false;
            job.FailCount = 0;
            job.FailReason = string.Empty;
            job.Description = string.Empty;
            job.Stage = "Pending";
            job.Paused = false;
            // Reset upload-related state so that a retry starts a fresh upload.
            job.YoutubeVideoId = string.Empty;
            job.ResumableUploadUri = null;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Retried VOD {VodId} — reset to Pending", vodId);
            return true;
        }

        private static PipelineJobDto MapToDto(DomainPipeline pipeline, DomainTwitchVod? vod)
        {
            return new PipelineJobDto
            {
                VodId = pipeline.VodId,
                Stage = pipeline.Stage,
                Description = pipeline.Description,
                Paused = pipeline.Paused,
                Failed = pipeline.Failed,
                FailReason = pipeline.FailReason,
                FailCount = pipeline.FailCount,
                YoutubeVideoId = pipeline.YoutubeVideoId,
                Title = vod?.Title ?? pipeline.VodId,
                ChannelName = vod?.ChannelName ?? string.Empty,
                CreatedAtUTC = vod?.CreatedAtUTC ?? DateTime.MinValue,
                Duration = vod?.Duration ?? TimeSpan.Zero,
                VodUrl = vod?.Url ?? string.Empty,
                AddedAtUTC = vod?.AddedAtUTC ?? DateTime.MinValue
            };
        }
    }
}

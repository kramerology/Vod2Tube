using Microsoft.EntityFrameworkCore;
using Vod2Tube.Application.Models;
using Vod2Tube.Infrastructure;
using DomainTwitchVod = Vod2Tube.Domain.TwitchVod;
using DomainPipeline = Vod2Tube.Domain.Pipeline;

namespace Vod2Tube.Application.Services
{
    public class PipelineService
    {
        private readonly AppDbContext _dbContext;

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

        public PipelineService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
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

        public async Task<bool> PauseJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null) return false;
            job.Paused = true;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResumeJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null) return false;
            job.Paused = false;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CancelJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null) return false;
            job.Stage = "Cancelled";
            job.Paused = false;
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RetryJobAsync(string vodId)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null) return false;
            job.Failed = false;
            job.FailCount = 0;
            job.FailReason = string.Empty;
            job.Description = string.Empty;
            job.Stage = "Pending";
            job.Paused = false;
            await _dbContext.SaveChangesAsync();
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

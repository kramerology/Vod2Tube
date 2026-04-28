using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Vod2Tube.Application.Models;
using Vod2Tube.Infrastructure;
using DomainTwitchVod = Vod2Tube.Domain.TwitchVod;
using DomainPipeline = Vod2Tube.Domain.Pipeline;
using DomainChannel = Vod2Tube.Domain.Channel;

namespace Vod2Tube.Application.Services
{
    public class PipelineService
    {
        private static readonly HashSet<string> TerminalStages = new(StringComparer.OrdinalIgnoreCase)
        {
            "Uploaded",
            "Cancelled"
        };

        private readonly AppDbContext _dbContext;
        private readonly ILogger<PipelineService> _logger;
        private readonly TwitchGraphQLService? _twitchGraphQLService;

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
            "Uploading",
            "PendingArchiving",
            "Archiving"
        };

        public PipelineService(AppDbContext dbContext, ILogger<PipelineService> logger)
            : this(dbContext, logger, null)
        {
        }

        public PipelineService(AppDbContext dbContext, ILogger<PipelineService> logger, TwitchGraphQLService? twitchGraphQLService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _twitchGraphQLService = twitchGraphQLService;
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

            _logger.LogDebug("Loaded {Count} VODs from database", vods.Count);

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
            job.Failed = false;
            job.FailCount = 0;
            job.FailReason = string.Empty;
            job.Description = string.Empty;
            await _dbContext.SaveChangesAsync();
            await QueueNextVodForChannelAsync(vodId);
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
            job.ResumableUploadUri = string.Empty;
            job.UploadedAtUTC = null;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Retried VOD {VodId} — reset to Pending", vodId);
            return true;
        }

        public async Task QueueNextVodForChannelAsync(string vodId)
        {
            string? channelName = await _dbContext.TwitchVods
                .Where(v => v.Id == vodId)
                .Select(v => v.ChannelName)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(channelName))
                return;

            DomainChannel? channel = await _dbContext.Channels
                .FirstOrDefaultAsync(c => c.ChannelName == channelName);

            if (channel == null || !channel.Active)
                return;

            await QueueNextVodForChannelAsync(channel);
        }

        public async Task<bool> QueueNextVodForChannelAsync(int channelId)
        {
            DomainChannel? channel = await _dbContext.Channels.FindAsync(channelId);
            if (channel == null)
                return false;

            return await QueueNextVodForChannelAsync(channel);
        }

        private async Task<bool> QueueNextVodForChannelAsync(DomainChannel channel)
        {
            channel.LastQueueCheckAtUTC = DateTime.UtcNow;

            if (!channel.Active)
            {
                await _dbContext.SaveChangesAsync();
                return false;
            }

            bool hasOutstandingJob = await _dbContext.Pipelines
                .Where(p => !TerminalStages.Contains(p.Stage))
                .Join(
                    _dbContext.TwitchVods,
                    pipeline => pipeline.VodId,
                    vod => vod.Id,
                    (pipeline, vod) => new { pipeline.Stage, vod.ChannelName })
                .AnyAsync(x => x.ChannelName == channel.ChannelName);

            if (hasOutstandingJob)
            {
                await _dbContext.SaveChangesAsync();
                return false;
            }

            if (_twitchGraphQLService == null)
            {
                await _dbContext.SaveChangesAsync();
                return false;
            }

            HashSet<string> existingVodIds = await _dbContext.TwitchVods
                .Where(v => v.ChannelName == channel.ChannelName)
                .Select(v => v.Id)
                .ToHashSetAsync(StringComparer.Ordinal);

            List<TwitchVod> vods = await _twitchGraphQLService.GetAllVodsAsync(channel.ChannelName);
            TwitchVod? nextVod = VodPopulator.SelectNextVodToQueue(vods, existingVodIds);
            if (nextVod == null)
            {
                await _dbContext.SaveChangesAsync();
                return false;
            }

            _dbContext.TwitchVods.Add(new DomainTwitchVod
            {
                Id = nextVod.Id,
                ChannelName = channel.ChannelName,
                Title = nextVod.Title,
                CreatedAtUTC = nextVod.PublishedAt,
                Duration = TimeSpan.FromSeconds(nextVod.LengthSeconds),
                Url = nextVod.Url,
                AddedAtUTC = DateTime.UtcNow,
            });

            _dbContext.Pipelines.Add(new DomainPipeline
            {
                VodId = nextVod.Id,
                Stage = "Pending",
            });

            channel.LastQueuedVodId = nextVod.Id;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Queued oldest unprocessed VOD {VodId} for channel {ChannelName}", nextVod.Id, channel.ChannelName);
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
                PercentComplete = pipeline.PercentComplete,
                EstimatedMinutesRemaining = pipeline.EstimatedMinutesRemaining,
                Title = vod?.Title ?? pipeline.VodId,
                ChannelName = vod?.ChannelName ?? string.Empty,
                CreatedAtUTC = vod?.CreatedAtUTC ?? DateTime.MinValue,
                Duration = vod?.Duration ?? TimeSpan.Zero,
                VodUrl = vod?.Url ?? string.Empty,
                AddedAtUTC = vod?.AddedAtUTC ?? DateTime.MinValue,
                VodFilePath = pipeline.VodFilePath,
                ChatTextFilePath = pipeline.ChatTextFilePath,
                ChatVideoFilePath = pipeline.ChatVideoFilePath,
                FinalVideoFilePath = pipeline.FinalVideoFilePath,
                ArchivedVodPath = pipeline.ArchivedVodPath,
                ArchivedChatJsonPath = pipeline.ArchivedChatJsonPath,
                ArchivedChatRenderPath = pipeline.ArchivedChatRenderPath,
                ArchivedFinalVideoPath = pipeline.ArchivedFinalVideoPath,
            };
        }

        /// <summary>
        /// Well-known stage-key constants accepted by <see cref="RetryFromStageAsync"/>.
        /// These must stay in sync with the values used by the React frontend.
        /// </summary>
        internal static class RetryStageKeys
        {
            internal const string DownloadVod  = "downloadvod";
            internal const string DownloadChat = "downloadchat";
            internal const string RenderChat   = "renderchat";
            internal const string Combine      = "combine";
            internal const string Archive      = "archive";
        }

        /// <summary>
        /// Resets the pipeline job back to a specific processing stage, clearing all file
        /// paths and state from subsequent stages.
        /// </summary>
        /// <param name="vodId">The VOD identifier.</param>
        /// <param name="stage">
        /// The stage key to retry from: <c>downloadVod</c>, <c>downloadChat</c>,
        /// <c>renderChat</c>, <c>combine</c>, or <c>archive</c>.
        /// </param>
        public async Task<bool> RetryFromStageAsync(string vodId, string stage)
        {
            var job = await _dbContext.Pipelines.FindAsync(vodId);
            if (job == null)
            {
                _logger.LogWarning("RetryFromStage requested for unknown VOD {VodId}", vodId);
                return false;
            }

            job.Failed = false;
            job.FailCount = 0;
            job.FailReason = string.Empty;
            job.Description = string.Empty;
            job.Paused = false;

            switch (stage.ToLowerInvariant())
            {
                case RetryStageKeys.DownloadVod:
                    job.Stage = "Pending";
                    job.VodFilePath = string.Empty;
                    job.ChatTextFilePath = string.Empty;
                    job.ChatVideoFilePath = string.Empty;
                    job.FinalVideoFilePath = string.Empty;
                    job.YoutubeVideoId = string.Empty;
                    job.ResumableUploadUri = string.Empty;
                    job.UploadedAtUTC = null;
                    job.ArchivedVodPath = string.Empty;
                    job.ArchivedChatJsonPath = string.Empty;
                    job.ArchivedChatRenderPath = string.Empty;
                    job.ArchivedFinalVideoPath = string.Empty;
                    break;
                case RetryStageKeys.DownloadChat:
                    job.Stage = "PendingDownloadChat";
                    job.ChatTextFilePath = string.Empty;
                    job.ChatVideoFilePath = string.Empty;
                    job.FinalVideoFilePath = string.Empty;
                    job.YoutubeVideoId = string.Empty;
                    job.ResumableUploadUri = string.Empty;
                    job.UploadedAtUTC = null;
                    job.ArchivedChatJsonPath = string.Empty;
                    job.ArchivedChatRenderPath = string.Empty;
                    job.ArchivedFinalVideoPath = string.Empty;
                    break;
                case RetryStageKeys.RenderChat:
                    job.Stage = "PendingRenderingChat";
                    job.ChatVideoFilePath = string.Empty;
                    job.FinalVideoFilePath = string.Empty;
                    job.YoutubeVideoId = string.Empty;
                    job.ResumableUploadUri = string.Empty;
                    job.UploadedAtUTC = null;
                    job.ArchivedChatRenderPath = string.Empty;
                    job.ArchivedFinalVideoPath = string.Empty;
                    break;
                case RetryStageKeys.Combine:
                    job.Stage = "PendingCombining";
                    job.FinalVideoFilePath = string.Empty;
                    job.YoutubeVideoId = string.Empty;
                    job.ResumableUploadUri = string.Empty;
                    job.UploadedAtUTC = null;
                    job.ArchivedFinalVideoPath = string.Empty;
                    break;
                case RetryStageKeys.Archive:
                    job.Stage = "PendingArchiving";
                    job.ArchivedVodPath = string.Empty;
                    job.ArchivedChatJsonPath = string.Empty;
                    job.ArchivedChatRenderPath = string.Empty;
                    job.ArchivedFinalVideoPath = string.Empty;
                    break;
                default:
                    _logger.LogWarning("RetryFromStage: unknown stage key '{Stage}' for VOD {VodId}", stage, vodId);
                    return false;
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("RetryFromStage: VOD {VodId} reset to stage '{Stage}' (key: {Key})", vodId, job.Stage, stage);
            return true;
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Vod2Tube.Application.Models;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application.Services
{
    public class ChannelService
    {
        private readonly AppDbContext _dbContext;

        public ChannelService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ChannelQueueStatusDto>> GetAllChannelsAsync()
        {
            var channels = await _dbContext.Channels
                .AsNoTracking()
                .OrderBy(c => c.ChannelName)
                .ToListAsync();

            var activeChannelNames = channels
                .Select(c => c.ChannelName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var outstandingJobs = await _dbContext.Pipelines
                .AsNoTracking()
                .Where(p => p.Stage != "Uploaded" && p.Stage != "Cancelled")
                .Join(
                    _dbContext.TwitchVods.AsNoTracking(),
                    pipeline => pipeline.VodId,
                    vod => vod.Id,
                    (pipeline, vod) => new
                    {
                        vod.ChannelName,
                        pipeline.VodId,
                        VodTitle = vod.Title,
                        VodCreatedAtUTC = vod.CreatedAtUTC,
                        pipeline.Stage,
                        pipeline.Failed,
                        pipeline.Paused,
                    })
                .Where(x => activeChannelNames.Contains(x.ChannelName))
                .ToListAsync();

            var currentJobsByChannel = outstandingJobs
                .GroupBy(x => x.ChannelName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(x => x.Failed)
                        .ThenBy(x => x.VodCreatedAtUTC)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var pipelineStatsByChannel = await _dbContext.Pipelines
                .AsNoTracking()
                .Join(
                    _dbContext.TwitchVods.AsNoTracking(),
                    pipeline => pipeline.VodId,
                    vod => vod.Id,
                    (pipeline, vod) => new
                    {
                        vod.ChannelName,
                        pipeline.Stage,
                        pipeline.VodFilePath,
                        pipeline.ChatTextFilePath,
                        pipeline.ChatVideoFilePath,
                        pipeline.FinalVideoFilePath,
                        pipeline.ArchivedVodPath,
                        pipeline.ArchivedChatJsonPath,
                        pipeline.ArchivedChatRenderPath,
                        pipeline.ArchivedFinalVideoPath,
                        pipeline.UploadedAtUTC,
                    })
                .Where(x => activeChannelNames.Contains(x.ChannelName))
                .GroupBy(x => x.ChannelName)
                .Select(group => new
                {
                    ChannelName = group.Key,
                    TotalVodsDownloaded = group.Count(x =>
                        x.Stage == "PendingDownloadChat"
                        || x.Stage == "DownloadingChat"
                        || x.Stage == "PendingRenderingChat"
                        || x.Stage == "RenderingChat"
                        || x.Stage == "PendingCombining"
                        || x.Stage == "Combining"
                        || x.Stage == "PendingUpload"
                        || x.Stage == "Uploading"
                        || x.Stage == "PendingArchiving"
                        || x.Stage == "Archiving"
                        || x.Stage == "Uploaded"
                        || x.VodFilePath != string.Empty
                        || x.ArchivedVodPath != string.Empty),
                    TotalVodsUploaded = group.Count(x => x.Stage == "Uploaded" && x.UploadedAtUTC != null),
                    LastUploadedAtUTC = group
                        .Where(x => x.Stage == "Uploaded" && x.UploadedAtUTC != null)
                        .Max(x => x.UploadedAtUTC),
                })
                .ToDictionaryAsync(x => x.ChannelName, StringComparer.OrdinalIgnoreCase);

            return channels.Select(channel =>
            {
                var currentJob = currentJobsByChannel.GetValueOrDefault(channel.ChannelName);
                var stats = pipelineStatsByChannel.GetValueOrDefault(channel.ChannelName);
                return new ChannelQueueStatusDto
                {
                    Id = channel.Id,
                    ChannelName = channel.ChannelName,
                    AddedAtUTC = channel.AddedAtUTC,
                    LastQueueCheckAtUTC = channel.LastQueueCheckAtUTC,
                    LastQueuedVodId = channel.LastQueuedVodId,
                    Active = channel.Active,
                    YouTubeAccountId = channel.YouTubeAccountId,
                    CurrentVodId = currentJob?.VodId,
                    CurrentVodTitle = currentJob?.VodTitle,
                    CurrentStage = currentJob?.Stage,
                    CurrentJobFailed = currentJob?.Failed ?? false,
                    CurrentJobPaused = currentJob?.Paused ?? false,
                    TotalVodsDownloaded = stats?.TotalVodsDownloaded ?? 0,
                    TotalVodsUploaded = stats?.TotalVodsUploaded ?? 0,
                    LastUploadedAtUTC = stats?.LastUploadedAtUTC,
                };
            }).ToList();
        }

        public async Task<Channel> AddNewChannelAsync(Channel channel)
        {
            channel.ChannelName = channel.ChannelName.Trim().ToLowerInvariant();
            channel.AddedAtUTC = DateTime.UtcNow;
            _dbContext.Channels.Add(channel);
            await _dbContext.SaveChangesAsync();
            return channel;
        }

        public async Task<Channel?> GetChannelByIdAsync(int id)
        {
            return await _dbContext.Channels.FindAsync(id);
        }

        public async Task<bool> UpdateChannelAsync(Channel channel)
        {
            var existing = await _dbContext.Channels.FindAsync(channel.Id);
            if (existing == null)
                return false;

            existing.ChannelName = channel.ChannelName.Trim().ToLowerInvariant();
            existing.Active = channel.Active;
            existing.YouTubeAccountId = channel.YouTubeAccountId;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteChannelAsync(int id)
        {
            var channel = await _dbContext.Channels.FindAsync(id);
            if (channel == null)
                return false;

            _dbContext.Channels.Remove(channel);
            await _dbContext.SaveChangesAsync();
            return true;
        }
    }
}
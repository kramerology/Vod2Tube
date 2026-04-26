using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    public class VodPopulator : BackgroundService
    {
        private static readonly string[] TerminalStages = ["Uploaded", "Cancelled"];

        internal static TwitchVod? SelectNextVodToQueue(IEnumerable<TwitchVod> vods, ISet<string> existingVodIds)
        {
            return vods
                .Where(v => !existingVodIds.Contains(v.Id))
                .OrderBy(v => v.PublishedAt)
                .FirstOrDefault();
        }

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VodPopulator> _logger;

        public VodPopulator(IServiceScopeFactory scopeFactory, ILogger<VodPopulator> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    TwitchGraphQLService twitchService = scope.ServiceProvider.GetRequiredService<TwitchGraphQLService>();
                    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    await dbContext.Database.MigrateAsync(stoppingToken);

                    List<Domain.Channel> channels = await dbContext.Channels
                        .Where(c => c.Active)
                        .OrderBy(c => c.ChannelName)
                        .ToListAsync(stoppingToken);

                    if (channels.Count == 0)
                    {
                        _logger.LogWarning("No active channels found in database. Waiting 5 minutes before checking again.");
                        continue;
                    }

                    _logger.LogInformation("Checking {Count} active channel(s) for the next VOD to queue", channels.Count);

                    foreach (Domain.Channel channel in channels)
                    {
                        channel.LastQueueCheckAtUTC = DateTime.UtcNow;

                        bool hasOutstandingJob = await dbContext.Pipelines
                            .Where(p => !TerminalStages.Contains(p.Stage))
                            .Join(
                                dbContext.TwitchVods,
                                pipeline => pipeline.VodId,
                                vod => vod.Id,
                                (pipeline, vod) => new { pipeline.Stage, vod.ChannelName })
                            .AnyAsync(x => x.ChannelName == channel.ChannelName, stoppingToken);

                        if (hasOutstandingJob)
                        {
                            _logger.LogDebug("Channel {ChannelName} already has an outstanding job; skipping queue population", channel.ChannelName);
                            continue;
                        }

                        List<string> existingVodIds = await dbContext.TwitchVods
                            .Where(v => v.ChannelName == channel.ChannelName)
                            .Select(v => v.Id)
                            .ToListAsync(stoppingToken);

                        List<TwitchVod> vods = await twitchService.GetAllVodsAsync(channel.ChannelName);
                        TwitchVod? nextVod = SelectNextVodToQueue(vods, existingVodIds.ToHashSet(StringComparer.Ordinal));

                        if (nextVod == null)
                        {
                            _logger.LogDebug("No new VODs found to queue for channel {ChannelName}", channel.ChannelName);
                            continue;
                        }

                        dbContext.TwitchVods.Add(new Domain.TwitchVod
                        {
                            Id = nextVod.Id,
                            ChannelName = channel.ChannelName,
                            Title = nextVod.Title,
                            CreatedAtUTC = nextVod.PublishedAt,
                            Duration = TimeSpan.FromSeconds(nextVod.LengthSeconds),
                            Url = nextVod.Url,
                            AddedAtUTC = DateTime.UtcNow
                        });

                        dbContext.Pipelines.Add(new Domain.Pipeline
                        {
                            VodId = nextVod.Id,
                            Stage = "Pending",
                        });

                        channel.LastQueuedVodId = nextVod.Id;
                        _logger.LogInformation("Queued oldest unprocessed VOD {VodId} for channel {ChannelName}", nextVod.Id, channel.ChannelName);
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in VodPopulator");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); 
                }





                //do something with twitchService
            }
        }
    }
}

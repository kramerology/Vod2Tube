using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    public class VodPopulator : BackgroundService
    {
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

                    await dbContext.Database.EnsureCreatedAsync();

                    List<string> channelNames = await dbContext.Channels
                        .Where(c => c.Active)
                        .Select(c => c.ChannelName)
                        .Distinct()
                        .ToListAsync(stoppingToken);

                    if (channelNames.Count == 0)
                    {
                        _logger.LogWarning("No active channels found in database — waiting 5 minutes before checking again");
                        continue;
                    }

                    _logger.LogInformation("VodPopulator scanning {Count} active channel(s): {Channels}",
                        channelNames.Count, string.Join(", ", channelNames));

                    foreach (string channel in channelNames)
                    {
                        List<TwitchVod> vods = await twitchService.GetAllVodsAsync(channel);

                        List<string> existingVodIds = await dbContext.TwitchVods
                            .Where(v => v.ChannelName == channel)
                            .Select(v => v.Id)
                            .ToListAsync(stoppingToken);


                        List<TwitchVod> newVods = vods.Where(v => !existingVodIds.Contains(v.Id))
                                                      .OrderBy(v => v.PublishedAt)
                                                      .ToList();

                        if (newVods.Count == 0)
                        {
                            _logger.LogInformation("Channel '{Channel}' — no new VODs found ({Total} total on record)", channel, existingVodIds.Count);
                        }
                        else
                        {
                            _logger.LogInformation("Channel '{Channel}' — {NewCount} new VOD(s) discovered (had {ExistingCount})", channel, newVods.Count, existingVodIds.Count);
                        }

                        foreach (TwitchVod vod in newVods)
                        {
                            _logger.LogInformation("  Queuing VOD '{Title}' ({VodId}) from '{Channel}', streamed {Date:yyyy-MM-dd}",
                                vod.Title, vod.Id, channel, vod.PublishedAt);

                            dbContext.TwitchVods.Add(new Domain.TwitchVod
                            {
                                Id = vod.Id,
                                ChannelName = channel,
                                Title = vod.Title,
                                CreatedAtUTC = vod.PublishedAt,
                                Duration = TimeSpan.FromSeconds(vod.LengthSeconds),
                                Url = vod.Url,
                                AddedAtUTC = DateTime.UtcNow
                            });


                            dbContext.Pipelines.Add(new Domain.Pipeline
                            {
                                VodId = vod.Id,
                                Stage = "Pending",

                            });
                        }


                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in VodPopulator");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); 
                }





                //do something with twitchService
            }
        }
    }
}

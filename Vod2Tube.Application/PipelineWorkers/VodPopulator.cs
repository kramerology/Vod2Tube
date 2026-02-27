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

                    List<string> channelNames = ["test"];

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

                        foreach (TwitchVod vod in newVods)
                        {
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

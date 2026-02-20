using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;
using static System.Formats.Asn1.AsnWriter;

namespace Vod2Tube.Application
{
    public class VodDownloader : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DirectoryInfo _tempDir = new("VodDownloadsTemp");
        private readonly DirectoryInfo _finalDir = new("VodDownloads");
        private readonly TwitchDownloadService _twitchDownloadService;

        private Timer _heartbeatTimer;
        public VodDownloader(IServiceScopeFactory scopeFactory, TwitchDownloadService twitchDownloadService)
        {
            _scopeFactory = scopeFactory;
            _twitchDownloadService = twitchDownloadService;
        }


        private async Task doHeartbeatAsync(CancellationToken stoppingToken, string vodId)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                while (!stoppingToken.IsCancellationRequested)
                {
                    AppDbContext heartbeatDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await heartbeatDbContext.Pipelines
                                            .Where(p => p.VodId == vodId)
                                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LeasedAtUTC, DateTime.UtcNow));
                    Console.WriteLine($"Heartbeat - extended lease for Vod {vodId}");

                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                }
            }
            catch(OperationCanceledException ex)
            {
                //Normal when stopping
            }
            catch(Exception ex)
            {
                ;
                //TODO: Something
            }
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

                    //Get first Pending vod from pipeline
                    //Download it using TwitchService
                    //Update DB with completed file

                    Pipeline? nextPipelineItem = await dbContext.Pipelines
                        .Where(p => p.Stage == "Pending")
                        .Where(p => p.LeasedAtUTC < DateTime.UtcNow.AddMinutes(10))
                        .OrderBy(p => p.VodId) 
                        .FirstOrDefaultAsync(stoppingToken);

                    if (nextPipelineItem is null)
                        continue;

                    nextPipelineItem.Stage = "Downloading";
                    nextPipelineItem.LeasedAtUTC = DateTime.UtcNow;
                    nextPipelineItem.LeasedBy = "VodDownloader";
                    nextPipelineItem.VodFilePath = String.Empty;

                    await dbContext.SaveChangesAsync(stoppingToken);


                     Domain.TwitchVod vod = await dbContext.TwitchVods
                        .Where(v => v.Id == nextPipelineItem.VodId)
                        .FirstAsync(stoppingToken);

                    if(vod is null)
                        throw new Exception($"Vod Id={vod.Id} not found in DB for pipeline item");

                    string tempFileName = $"{vod.Id}.tmp.mp4";
                    string finalFileName = $"{vod.Id}.mp4";

                    FileInfo tempFile  = new FileInfo(Path.Combine(_tempDir.FullName, tempFileName));
                    FileInfo finalFile = new FileInfo(Path.Combine(_finalDir.FullName, finalFileName));

                    try
                    {
                        Console.WriteLine($"Starting download for Vod {vod.Id}");

                        var heartbeatCts = new CancellationTokenSource();
                        var heartbeatTask = doHeartbeatAsync(heartbeatCts.Token, vod.Id);

                        DateTime lastUpdate = DateTime.MinValue;
                        await foreach (var status in _twitchDownloadService.DownloadVodNewAsync(vod.Id, _tempDir, finalFile, stoppingToken).WithCancellation(stoppingToken))
                        {
                            if (DateTime.UtcNow - lastUpdate < TimeSpan.FromSeconds(2))
                            {
                                continue;
                            }
                            lastUpdate = DateTime.UtcNow;
                            nextPipelineItem.Description = status;
                            try
                            {
                                await dbContext.SaveChangesAsync(stoppingToken);
                            }
                            catch(Exception ex)
                            {
                                //TODO: something
                            }
                      
                        }

                        heartbeatCts.Cancel();


                        /*   Task downloadTask = _twitchDownloadService.DownloadVodAsync(vod.Id, tempFile, finalFile, stoppingToken);

                           DateTime lastHeartbeat = DateTime.UtcNow;
                           while (!downloadTask.IsCompleted && !stoppingToken.IsCancellationRequested)
                           {
                               await Task.Delay(TimeSpan.FromMilliseconds(100));
                               if(DateTime.UtcNow - lastHeartbeat >= TimeSpan.FromMinutes(2))
                               {
                                   nextPipelineItem.LeasedAtUTC = DateTime.UtcNow;
                                   await dbContext.SaveChangesAsync(stoppingToken);
                                   lastHeartbeat = DateTime.UtcNow;
                                   Console.WriteLine($"Heartbeat - extended lease for Vod {vod.Id}");
                               }
                           }

                        if (stoppingToken.IsCancellationRequested)
                        {
                            //If we are cancelling, we should stop the download task if possible
                            continue;
                        }
                                             await downloadTask;
                        */


                    }
                    catch (Exception ex)
                    {
                        //TODO: Log
                        //TOOD: Mark as failed in pipeline?
                        tempFile.Delete();
                    }

                 /*   try
                    {
                        File.Move(tempFile.FullName, finalFile.FullName, true);
                    }
                    catch (Exception ex)
                    {
                        //TODO: Log 
                        //TODO: Panik ??
                        throw;
                    }*/

                    nextPipelineItem.LeasedAtUTC = DateTime.MinValue;
                    nextPipelineItem.LeasedBy = "";
                    nextPipelineItem.Stage = "VodDownloaded";
                    nextPipelineItem.Description = "Downloaded";
                    nextPipelineItem.VodFilePath = finalFile.FullName;

                    await dbContext.SaveChangesAsync(stoppingToken);
                    ;


                }
                catch (Exception ex)
                {
                    //TODO: Log the exception (consider using a logging framework)
                    Console.WriteLine($"Error in VodDownloader: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
            }
        }
    }
}

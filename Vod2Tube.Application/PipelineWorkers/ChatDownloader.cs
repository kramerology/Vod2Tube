using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    public class ChatDownloader : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DirectoryInfo _tempDir  = new("VodDownloadsTemp");
        private readonly DirectoryInfo _finalDir = new("VodDownloads");

        public ChatDownloader(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
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
            catch (OperationCanceledException ex)
            {
                //Normal when stopping
            }
            catch (Exception ex)
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
                    AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    TwitchDownloadService _twitchDownloadService = scope.ServiceProvider.GetRequiredService<TwitchDownloadService>();


                    Pipeline? nextPipelineItem = await dbContext.Pipelines
                                                                .Where(p => p.Stage == "VodDownloaded")
                                                                .Where(p => p.LeasedAtUTC < DateTime.UtcNow.AddMinutes(10))
                                                                .OrderBy(p => p.VodId)
                                                                .FirstOrDefaultAsync(stoppingToken);

                    if (nextPipelineItem is null)
                        continue;


                    nextPipelineItem.Stage       = "ChatDownloading";
                    nextPipelineItem.LeasedBy    = "ChatDownloader";
                    nextPipelineItem.Description = "Downloading Chat";
                    nextPipelineItem.LeasedAtUTC = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(stoppingToken);

                    FileInfo tempFile  = new(Path.Combine(_tempDir.FullName, $"{nextPipelineItem.VodId}.json.tmp"));
                    FileInfo finalFile = new(Path.Combine(_finalDir.FullName, $"{nextPipelineItem.VodId}.json"));

                    var heartbeatCts = new CancellationTokenSource();
                    var heartbeatTask = doHeartbeatAsync(heartbeatCts.Token, nextPipelineItem.VodId);

                    DateTime lastUpdate = DateTime.MinValue;
                    await foreach(var status in _twitchDownloadService.DownloadChatNewAsync(nextPipelineItem.VodId, _tempDir, finalFile, stoppingToken))
                    {
                        if(DateTime.UtcNow - lastUpdate < TimeSpan.FromSeconds(2))
                        {
                            continue;
                        }
                        lastUpdate = DateTime.UtcNow;
                        nextPipelineItem.Description = status;
                        try
                        {
                            await dbContext.SaveChangesAsync(stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            //TODO: something
                        }
                    }

                    heartbeatCts.Cancel();

                    nextPipelineItem.LeasedAtUTC      = DateTime.MinValue;
                    nextPipelineItem.LeasedBy         = "";
                    nextPipelineItem.Stage            = "ChatDownloaded";
                    nextPipelineItem.Description      = "Chat Downloaded";
                    nextPipelineItem.ChatTextFilePath = finalFile.FullName;

                    await dbContext.SaveChangesAsync(stoppingToken);


                }
                catch (Exception ex)
                {
                    // Log the exception (consider using a logging framework)
                    Console.WriteLine($"Error in VodDownloader: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
                }





                //do something with twitchService
            }
        }
    }
}

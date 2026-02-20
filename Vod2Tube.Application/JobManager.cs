using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    internal class JobManager : BackgroundService
    {
        private int _currentJob = 0;
        private readonly IServiceScopeFactory _scopeFactory;
        //ChannelService adds PENDING vods to the DB on its own

        //ORDER
        //Pending -> DownloadingVod -> DownloadingChat -> RenderingChat -> Combining -> Uploading
        //Pending -> DownloadingVod -> PendingDownloadChat -> DownloadingChat -> PendingRenderingChat -> RenderingChat -> PendingCombining -> Combining -> PendingUpload -> Uploading


        //JobManager
        //- Needs to select incomplete job closest to completion
        //Look for any 


        public JobManager(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                //TwitchGraphQLService twitchService = scope.ServiceProvider.GetRequiredService<TwitchGraphQLService>();
                AppDbContext dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

















            }
        }






    }
}

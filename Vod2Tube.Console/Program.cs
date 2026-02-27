using Vod2Tube;
using Vod2Tube.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vod2Tube.Infrastructure;
using Microsoft.EntityFrameworkCore;




// Download Vod Data -> Pending
//   Pending -> DownloadingVod -> DownloadingChat -> RenderingChat -> Combining -> Uploading







var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(o =>
        {
            o.TimestampFormat = "HH:mm:ss ";
            o.SingleLine = true;
        });
    })
    .ConfigureServices((context, services) =>
    {
        // Add configuration options
        // services.Configure<MyOptions>(context.Configuration.GetSection("MyOptions"));

        // Register your workers (BackgroundServices)
     //   services.AddHostedService<VodPopulator>();
        services.AddHostedService<JobManager>();

            services.AddHostedService<VodPopulator>();
            services.AddScoped<VodDownloader>();
            services.AddScoped<ChatDownloader>();
            services.AddScoped<ChatRenderer>();
            services.AddScoped<FinalRenderer>();
            services.AddScoped<VideoUploader>();
            services.AddScoped<TwitchGraphQLService>();
            services.AddScoped<TwitchDownloadService>();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite("Data Source=Vod2Tube.db");
        });


        // services.AddHostedService<AnotherWorker>();

        // Register any other services
        // services.AddSingleton<IMyDependency, MyDependency>();
    })
    .Build();

await host.RunAsync();

    Log.Information("Vod2Tube shut down cleanly");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Vod2Tube terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
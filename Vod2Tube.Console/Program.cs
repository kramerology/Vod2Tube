using Vod2Tube.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vod2Tube.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Download Vod Data -> Pending
//   Pending -> DownloadingVod -> DownloadingChat -> RenderingChat -> Combining -> Uploading

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/vod2tube.log", rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Vod2Tube starting up");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
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
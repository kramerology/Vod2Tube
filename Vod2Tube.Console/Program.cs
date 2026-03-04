using Vod2Tube.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vod2Tube.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Display;
using Vod2Tube.Console.Logging;

// Download Vod Data -> Pending
//   Pending -> DownloadingVod -> DownloadingChat -> RenderingChat -> Combining -> Uploading

// Console template: include a short source name so you can see which component emitted each line.
const string consoleTemplate =
    "{Timestamp:HH:mm:ss} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";

// File template: same structure, with full date; Verbose/Trace events are filtered out
// (they are frequent progress updates intended only for the console display).
const string fileTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{SourceContext:l}] {Message:lj}{NewLine}{Exception}";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()   // allow Trace (progress) to reach the console sink
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Sink(new InlineConsoleSink(new MessageTemplateTextFormatter(consoleTemplate)))
    .WriteTo.File("logs/vod2tube.log", rollingInterval: RollingInterval.Day,
        outputTemplate: fileTemplate,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
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
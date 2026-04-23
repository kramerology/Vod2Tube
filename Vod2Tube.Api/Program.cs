using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vod2Tube.Application;
using Vod2Tube.Application.PipelineWorkers;
using Vod2Tube.Application.Services;
using Vod2Tube.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()));

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddTransient<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Settings
builder.Services.AddOptions<AppSettings>();
builder.Services.AddSingleton<IConfigureOptions<AppSettings>, AppSettingsConfigurator>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddSingleton<ExecutableReadinessMonitor>();

builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<YouTubeAccountService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<TwitchGraphQLService>();
builder.Services.AddScoped<TwitchDownloadService>();
builder.Services.AddScoped<ChatDownloader>();
builder.Services.AddScoped<ChatRenderer>();
builder.Services.AddScoped<FinalRenderer>();
builder.Services.AddScoped<VideoUploader>();
builder.Services.AddScoped<VodDownloader>();
builder.Services.AddScoped<Archiver>();

builder.Services.AddHostedService(sp => sp.GetRequiredService<ExecutableReadinessMonitor>());
builder.Services.AddHostedService<VodPopulator>();
builder.Services.AddHostedService<JobManager>();

var app = builder.Build();

app.UseCors();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapControllers();

app.Run();

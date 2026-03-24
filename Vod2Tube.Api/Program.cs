using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<TwitchGraphQLService>();
builder.Services.AddScoped<TwitchDownloadService>();
builder.Services.AddScoped<ChatDownloader>();
builder.Services.AddScoped<ChatRenderer>();
builder.Services.AddScoped<FinalRenderer>();
builder.Services.AddScoped<VideoUploader>();
builder.Services.AddScoped<VodDownloader>();

builder.Services.AddHostedService<VodPopulator>();
builder.Services.AddHostedService<JobManager>();

var app = builder.Build();

app.UseCors();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ── Channels ──────────────────────────────────────────────────────────────────

var channels = app.MapGroup("/api/channels");

channels.MapGet("/", async (ChannelService svc) =>
    Results.Ok(await svc.GetAllChannelsAsync()));

channels.MapPost("/", async (Channel channel, ChannelService svc) =>
{
    var created = await svc.AddNewChannelAsync(channel);
    return Results.Created($"/api/channels/{created.Id}", created);
});

channels.MapPut("/{id:int}", async (int id, Channel channel, ChannelService svc) =>
{
    channel.Id = id;
    return await svc.UpdateChannelAsync(channel) ? Results.Ok(channel) : Results.NotFound();
});

channels.MapDelete("/{id:int}", async (int id, ChannelService svc) =>
    await svc.DeleteChannelAsync(id) ? Results.NoContent() : Results.NotFound());

channels.MapGet("/avatars", async (ChannelService channelSvc, TwitchGraphQLService twitchSvc) =>
{
    var all = await channelSvc.GetAllChannelsAsync();
    var logins = all.Select(c => c.ChannelName);
    var urls = await twitchSvc.GetProfileImageUrlsAsync(logins);
    return Results.Ok(urls);
});

// ── VODs / Pipeline ───────────────────────────────────────────────────────────

var vods = app.MapGroup("/api/vods");

vods.MapGet("/", async (PipelineService svc) =>
    Results.Ok(await svc.GetAllJobsAsync()));

vods.MapGet("/active", async (PipelineService svc) =>
    Results.Ok(await svc.GetActiveJobsAsync()));

vods.MapGet("/completed", async (PipelineService svc) =>
    Results.Ok(await svc.GetCompletedJobsAsync()));

vods.MapPost("/{vodId}/pause", async (string vodId, PipelineService svc) =>
    await svc.PauseJobAsync(vodId) ? Results.Ok() : Results.NotFound());

vods.MapPost("/{vodId}/resume", async (string vodId, PipelineService svc) =>
    await svc.ResumeJobAsync(vodId) ? Results.Ok() : Results.NotFound());

vods.MapPost("/{vodId}/cancel", async (string vodId, PipelineService svc) =>
    await svc.CancelJobAsync(vodId) ? Results.Ok() : Results.NotFound());

vods.MapPost("/{vodId}/retry", async (string vodId, PipelineService svc) =>
    await svc.RetryJobAsync(vodId) ? Results.Ok() : Results.NotFound());

vods.MapGet("/thumbnails", async (string ids, TwitchGraphQLService twitchSvc) =>
{
    var vodIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var urls = await twitchSvc.GetVodThumbnailUrlsAsync(vodIds);
    return Results.Ok(urls);
});

// ── Settings ──────────────────────────────────────────────────────────────────

var settings = app.MapGroup("/api/settings");

settings.MapGet("/", async (SettingsService svc) =>
    Results.Ok(await svc.GetSettingsAsync()));

settings.MapPut("/", async (AppSettings incoming, SettingsService svc) =>
{
    await svc.SaveSettingsAsync(incoming);
    return Results.Ok(await svc.GetSettingsAsync());
});

app.Run();

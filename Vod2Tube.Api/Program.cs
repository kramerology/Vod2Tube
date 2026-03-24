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

// ── Filesystem browser ────────────────────────────────────────────────────────

app.MapGet("/api/filesystem/browse", (string? path) =>
{
    try
    {
        // Resolve to absolute; if non-existent, walk up to nearest existing ancestor
        string current = string.IsNullOrWhiteSpace(path)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(path);

        while (!Directory.Exists(current))
        {
            string? parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current)
            {
                current = Directory.GetCurrentDirectory();
                break;
            }
            current = parent;
        }

        var dirs = Directory.EnumerateDirectories(current)
            .Select(d => new { name = Path.GetFileName(d), fullPath = d })
            .OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var files = Directory.EnumerateFiles(current)
            .Select(f => new { name = Path.GetFileName(f), fullPath = f })
            .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? parentPath = Path.GetDirectoryName(current);

        // Windows: expose logical drives so the user can switch drive letters
        string[]? drives = OperatingSystem.IsWindows()
            ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToArray()
            : null;

        return Results.Ok(new { currentPath = current, parentPath, directories = dirs, files, drives });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Json(new { error = "Access denied" }, statusCode: 403);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

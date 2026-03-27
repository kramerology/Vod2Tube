using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vod2Tube.Application;
using Vod2Tube.Application.PipelineWorkers;
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

vods.MapPost("/{vodId}/retry/{stage}", async (string vodId, string stage, PipelineService svc) =>
    await svc.RetryFromStageAsync(vodId, stage) ? Results.Ok() : Results.NotFound());

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

// ── YouTube Accounts ──────────────────────────────────────────────────────────

var accounts = app.MapGroup("/api/accounts");

accounts.MapGet("/", async (YouTubeAccountService svc) =>
{
    var all = await svc.GetAllAsync();
    var result = all.Select(a => new
    {
        a.Id,
        a.Name,
        a.AddedAtUTC,
        a.ChannelTitle,
        IsAuthorized = svc.IsAuthorized(a.Id),
    });
    return Results.Ok(result);
});

accounts.MapGet("/{id:int}", async (int id, YouTubeAccountService svc) =>
{
    var account = await svc.GetByIdAsync(id);
    if (account == null) return Results.NotFound();
    return Results.Ok(new
    {
        account.Id,
        account.Name,
        account.AddedAtUTC,
        account.ChannelTitle,
        IsAuthorized = svc.IsAuthorized(account.Id),
    });
});

accounts.MapPost("/", async (CreateAccountRequest req, YouTubeAccountService svc) =>
{
    try
    {
        var account = await svc.CreateAsync(req.Name, req.ClientSecretsJson);
        return Results.Created($"/api/accounts/{account.Id}", new
        {
            account.Id,
            account.Name,
            account.AddedAtUTC,
            account.ChannelTitle,
            IsAuthorized = false,
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

accounts.MapPut("/{id:int}", async (int id, UpdateAccountRequest req, YouTubeAccountService svc) =>
    await svc.UpdateAsync(id, req.Name) ? Results.Ok() : Results.NotFound());

accounts.MapDelete("/{id:int}", async (int id, YouTubeAccountService svc) =>
    await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

accounts.MapPost("/{id:int}/authorize", async (int id, YouTubeAccountService svc, HttpContext ctx) =>
{
    var scheme = ctx.Request.Scheme;
    var host = ctx.Request.Host;
    var redirectUri = $"{scheme}://{host}/api/accounts/oauth-callback";

    var url = await svc.GetAuthorizationUrlAsync(id, redirectUri);
    if (url == null) return Results.NotFound();
    return Results.Ok(new { authorizationUrl = url });
});

accounts.MapPost("/{id:int}/revoke", async (int id, YouTubeAccountService svc) =>
    await svc.RevokeAsync(id) ? Results.Ok() : Results.NotFound());

// OAuth callback — Google redirects here after the user grants access.
app.MapGet("/api/accounts/oauth-callback", async (string? code, string? state, string? error, YouTubeAccountService svc, HttpContext ctx) =>
{
    if (!string.IsNullOrEmpty(error))
    {
        return Results.Content(BuildOAuthResultPage(false, $"Google denied access: {error}"), "text/html");
    }

    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
    {
        return Results.Content(BuildOAuthResultPage(false, "Missing authorization code or state."), "text/html");
    }

    var scheme = ctx.Request.Scheme;
    var host = ctx.Request.Host;
    var redirectUri = $"{scheme}://{host}/api/accounts/oauth-callback";

    var (success, _, err) = await svc.HandleOAuthCallbackAsync(code, state, redirectUri);
    return Results.Content(BuildOAuthResultPage(success, success ? "Authorization successful! You can close this tab." : err ?? "Unknown error"), "text/html");
});

static string BuildOAuthResultPage(bool success, string message)
{
    var icon = success ? "&#10003;" : "&#10007;";
    var color = success ? "#22c55e" : "#ef4444";
    var heading = success ? "Authorization Complete" : "Authorization Failed";
    var encodedMessage = System.Net.WebUtility.HtmlEncode(message);
    var successJs = success ? "true" : "false";

    return $$"""
        <!DOCTYPE html>
        <html>
        <head>
            <title>Vod2Tube - YouTube Authorization</title>
            <style>
                body { font-family: 'Inter', -apple-system, system-ui, sans-serif; background: #0b1326; color: #dae2fd; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
                .card { background: #171f33; border-radius: 16px; padding: 48px; text-align: center; max-width: 440px; box-shadow: 0 10px 30px rgba(6,14,32,0.5); border: 1px solid rgba(255,255,255,0.05); }
                .icon { font-size: 64px; color: {{color}}; margin-bottom: 16px; }
                h1 { font-size: 22px; margin: 0 0 12px; font-weight: 800; }
                p { color: #c2c6d6; font-size: 14px; line-height: 1.6; margin: 0; }
                .close-hint { margin-top: 24px; font-size: 12px; color: #6b7280; }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon">{{icon}}</div>
                <h1>{{heading}}</h1>
                <p>{{encodedMessage}}</p>
                <p class="close-hint">You can safely close this tab and return to Vod2Tube.</p>
            </div>
            <script>
                if (window.opener) {
                    window.opener.postMessage({ type: 'vod2tube-oauth-complete', success: {{successJs}} }, '*');
                }
            </script>
        </body>
        </html>
        """;
}

// ── Filesystem browser ────────────────────────────────────────────────────────

app.MapGet("/api/filesystem/browse", (string? path, HttpContext ctx) =>
{
    // Restrict to loopback — this endpoint lists server directory contents and
    // Vod2Tube is designed to run as a local-only service.
    if (ctx.Connection.RemoteIpAddress is not { } remoteIp || !System.Net.IPAddress.IsLoopback(remoteIp))
        return Results.Json(new { error = "This endpoint is only accessible from localhost" }, statusCode: 403);

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

// Reveal a file or folder in the OS file manager (loopback only).
app.MapPost("/api/filesystem/reveal", (RevealRequest req, HttpContext ctx) =>
{
    if (ctx.Connection.RemoteIpAddress is not { } remoteIp || !System.Net.IPAddress.IsLoopback(remoteIp))
        return Results.Json(new { error = "This endpoint is only accessible from localhost" }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(req.Path))
        return Results.BadRequest(new { error = "Path is required" });

    try
    {
        string fullPath = Path.GetFullPath(req.Path);
        System.Diagnostics.ProcessStartInfo psi;

        if (OperatingSystem.IsWindows())
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return Results.NotFound(new { error = "Path does not exist" });

            // Explorer uses its own argument parser, not a shell — the /select, syntax
            // is well-defined and Windows paths cannot contain the '"' character, so
            // this is safe.  UseShellExecute=false prevents shell expansion entirely.
            psi = new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false,
                Arguments = File.Exists(fullPath) ? $"/select,\"{fullPath}\"" : $"\"{fullPath}\"",
            };
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return Results.NotFound(new { error = "Path does not exist" });

            // Use ArgumentList so the path is passed verbatim without shell interpretation.
            psi = new System.Diagnostics.ProcessStartInfo("open") { UseShellExecute = false };
            if (File.Exists(fullPath))
                psi.ArgumentList.Add("-R");
            psi.ArgumentList.Add(fullPath);
        }
        else
        {
            // Linux — open the containing directory
            string folder = File.Exists(fullPath) ? (Path.GetDirectoryName(fullPath) ?? fullPath) : fullPath;
            if (!Directory.Exists(folder))
                return Results.NotFound(new { error = "Path does not exist" });

            // Use ArgumentList so the path is passed verbatim without shell interpretation.
            psi = new System.Diagnostics.ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(folder);
        }

        System.Diagnostics.Process.Start(psi);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

record RevealRequest(string Path);
record CreateAccountRequest(string Name, string ClientSecretsJson);
record UpdateAccountRequest(string Name);

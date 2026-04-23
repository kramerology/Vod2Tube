using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vod2Tube.Application.Models;

namespace Vod2Tube.Application.Services;

public sealed class ExecutableReadinessMonitor : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecutableReadinessMonitor> _logger;
    private readonly Lock _statusLock = new();
    private ExecutableReadinessStatus _currentStatus = new()
    {
        IsReady = false,
        CheckedAtUtc = DateTimeOffset.MinValue,
        RequiredExecutables = []
    };

    public ExecutableReadinessMonitor(IServiceScopeFactory scopeFactory, ILogger<ExecutableReadinessMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ExecutableReadinessStatus CurrentStatus
    {
        get
        {
            lock (_statusLock)
            {
                return _currentStatus;
            }
        }
    }

    public bool IsReady => CurrentStatus.IsReady;

    public async Task<ExecutableReadinessStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var settings = await settingsService.GetSettingsAsync();

        var status = BuildStatus(settings);
        var previous = CurrentStatus;

        lock (_statusLock)
        {
            _currentStatus = status;
        }

        if (status.IsReady != previous.IsReady)
        {
            if (status.IsReady)
            {
                _logger.LogInformation("Executable readiness restored. All required tools are available.");
            }
            else
            {
                _logger.LogWarning(
                    "Executable readiness check failed. Missing tools: {MissingTools}",
                    string.Join(", ", status.RequiredExecutables.Where(x => !x.Exists).Select(x => x.DisplayName)));
            }
        }

        return status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshSafelyAsync(stoppingToken);
        }
    }

    private async Task RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executable readiness refresh failed.");
        }
    }

    internal static ExecutableReadinessStatus BuildStatus(AppSettings settings)
    {
        var executables = new[]
        {
            CreateRequirement(nameof(AppSettings.TwitchDownloaderCliPath), "TwitchDownloaderCLI", settings.TwitchDownloaderCliPath),
            CreateRequirement(nameof(AppSettings.FfmpegPath), "FFmpeg", settings.FfmpegPath),
            CreateRequirement(nameof(AppSettings.FfprobePath), "FFprobe", settings.FfprobePath),
            CreateRequirement(nameof(AppSettings.YtDlpPath), "yt-dlp", settings.YtDlpPath),
        };

        return new ExecutableReadinessStatus
        {
            IsReady = executables.All(x => x.Exists),
            CheckedAtUtc = DateTimeOffset.UtcNow,
            RequiredExecutables = executables
        };
    }

    private static ExecutableRequirementStatus CreateRequirement(string settingName, string displayName, string path)
    {
        var normalizedPath = path?.Trim() ?? string.Empty;

        return new ExecutableRequirementStatus
        {
            SettingName = settingName,
            DisplayName = displayName,
            Path = normalizedPath,
            Exists = !string.IsNullOrWhiteSpace(normalizedPath) && File.Exists(normalizedPath)
        };
    }
}

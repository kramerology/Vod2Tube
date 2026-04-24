using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ExecutableReadinessMonitor.BuildStatus"/>.
/// </summary>
public class ExecutableReadinessMonitorTests
{
    // =========================================================================
    // BuildStatus — IsReady
    // =========================================================================

    [Test]
    public async Task BuildStatus_AllPathsExist_IsReadyTrue()
    {
        string f1 = Path.GetTempFileName();
        string f2 = Path.GetTempFileName();
        string f3 = Path.GetTempFileName();
        string f4 = Path.GetTempFileName();
        try
        {
            var settings = new AppSettings
            {
                TwitchDownloaderCliPath = f1,
                FfmpegPath  = f2,
                FfprobePath = f3,
                YtDlpPath   = f4,
            };

            var status = ExecutableReadinessMonitor.BuildStatus(settings);

            await Assert.That(status.IsReady).IsTrue();
        }
        finally
        {
            File.Delete(f1); File.Delete(f2); File.Delete(f3); File.Delete(f4);
        }
    }

    [Test]
    public async Task BuildStatus_AllPathsMissing_IsReadyFalse()
    {
        var settings = new AppSettings
        {
            TwitchDownloaderCliPath = "/nonexistent/TwitchDownloaderCLI",
            FfmpegPath  = "/nonexistent/ffmpeg",
            FfprobePath = "/nonexistent/ffprobe",
            YtDlpPath   = "/nonexistent/yt-dlp",
        };

        var status = ExecutableReadinessMonitor.BuildStatus(settings);

        await Assert.That(status.IsReady).IsFalse();
    }

    [Test]
    public async Task BuildStatus_SomePathsMissing_IsReadyFalse()
    {
        string existingFile = Path.GetTempFileName();
        try
        {
            var settings = new AppSettings
            {
                TwitchDownloaderCliPath = existingFile,
                FfmpegPath  = existingFile,
                FfprobePath = "/nonexistent/ffprobe",
                YtDlpPath   = "/nonexistent/yt-dlp",
            };

            var status = ExecutableReadinessMonitor.BuildStatus(settings);

            await Assert.That(status.IsReady).IsFalse();
        }
        finally
        {
            File.Delete(existingFile);
        }
    }

    [Test]
    public async Task BuildStatus_EmptyPaths_IsReadyFalse()
    {
        var settings = new AppSettings
        {
            TwitchDownloaderCliPath = "",
            FfmpegPath  = "",
            FfprobePath = "",
            YtDlpPath   = "",
        };

        var status = ExecutableReadinessMonitor.BuildStatus(settings);

        await Assert.That(status.IsReady).IsFalse();
    }

    // =========================================================================
    // BuildStatus — RequiredExecutables
    // =========================================================================

    [Test]
    public async Task BuildStatus_RequiredExecutables_ContainsAllFourTools()
    {
        var status = ExecutableReadinessMonitor.BuildStatus(new AppSettings());

        await Assert.That(status.RequiredExecutables).Count().IsEqualTo(4);
    }

    [Test]
    public async Task BuildStatus_RequiredExecutables_HaveExpectedDisplayNames()
    {
        var status = ExecutableReadinessMonitor.BuildStatus(new AppSettings());

        var names = status.RequiredExecutables.Select(x => x.DisplayName).ToArray();
        await Assert.That(names).Contains("TwitchDownloaderCLI");
        await Assert.That(names).Contains("FFmpeg");
        await Assert.That(names).Contains("FFprobe");
        await Assert.That(names).Contains("yt-dlp");
    }

    [Test]
    public async Task BuildStatus_MissingExecutables_HaveExistsFalse()
    {
        var settings = new AppSettings
        {
            TwitchDownloaderCliPath = "/nonexistent/TwitchDownloaderCLI",
            FfmpegPath  = "/nonexistent/ffmpeg",
            FfprobePath = "/nonexistent/ffprobe",
            YtDlpPath   = "/nonexistent/yt-dlp",
        };

        var status = ExecutableReadinessMonitor.BuildStatus(settings);

        await Assert.That(status.RequiredExecutables.All(x => !x.Exists)).IsTrue();
    }

    [Test]
    public async Task BuildStatus_ExistingExecutables_HaveExistsTrue()
    {
        string f1 = Path.GetTempFileName();
        string f2 = Path.GetTempFileName();
        string f3 = Path.GetTempFileName();
        string f4 = Path.GetTempFileName();
        try
        {
            var settings = new AppSettings
            {
                TwitchDownloaderCliPath = f1,
                FfmpegPath  = f2,
                FfprobePath = f3,
                YtDlpPath   = f4,
            };

            var status = ExecutableReadinessMonitor.BuildStatus(settings);

            await Assert.That(status.RequiredExecutables.All(x => x.Exists)).IsTrue();
        }
        finally
        {
            File.Delete(f1); File.Delete(f2); File.Delete(f3); File.Delete(f4);
        }
    }

    // =========================================================================
    // BuildStatus — CheckedAtUtc
    // =========================================================================

    [Test]
    public async Task BuildStatus_CheckedAtUtc_IsRecent()
    {
        var before = DateTimeOffset.UtcNow;
        var status = ExecutableReadinessMonitor.BuildStatus(new AppSettings());
        var after = DateTimeOffset.UtcNow;

        await Assert.That(status.CheckedAtUtc).IsGreaterThanOrEqualTo(before);
        await Assert.That(status.CheckedAtUtc).IsLessThanOrEqualTo(after);
    }
}

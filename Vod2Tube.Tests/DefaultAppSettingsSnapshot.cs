using Microsoft.Extensions.Options;
using Vod2Tube.Application;

namespace Vod2Tube.Tests;

/// <summary>
/// Simple <see cref="IOptionsSnapshot{TOptions}"/> stub that returns
/// <see cref="AppSettings"/> values with directory paths rooted under a
/// unique temporary folder.  This prevents pipeline worker constructors
/// (which call <see cref="System.IO.Directory.CreateDirectory"/>) from
/// polluting the repository or CI workspace and avoids conflicts between
/// parallel test runs.
/// </summary>
internal sealed class DefaultAppSettingsSnapshot : IOptionsSnapshot<AppSettings>
{
    public static readonly DefaultAppSettingsSnapshot Instance = new();

    private readonly AppSettings _value;

    public DefaultAppSettingsSnapshot()
    {
        // Each instance gets its own temp root so that directory creation
        // is isolated per test (or per test class when Instance is reused).
        var root = Path.Combine(Path.GetTempPath(), $"Vod2Tube_Tests_{Guid.NewGuid():N}");
        _value = new AppSettings
        {
            VodDownloadTempDir = Path.Combine(root, "VodDownloadsTemp"),
            VodDownloadDir     = Path.Combine(root, "VodDownloads"),
            ChatRenderTempDir  = Path.Combine(root, "ChatRenderTemp"),
            ChatRenderDir      = Path.Combine(root, "ChatRenders"),
            FinalVideoDir      = Path.Combine(root, "FinalVideos"),
        };
    }

    public AppSettings Value => _value;
    public AppSettings Get(string? name) => _value;
}

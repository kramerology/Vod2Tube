using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for the <c>GetOutputPath</c> helpers on each pipeline worker:
/// <see cref="VodDownloader"/>, <see cref="ChatDownloader"/>,
/// <see cref="ChatRenderer"/>, and <see cref="FinalRenderer"/>.
/// </summary>
public class PipelineWorkersTests
{
    // =========================================================================
    // VodDownloader.GetOutputPath
    // =========================================================================

    /// <summary>
    /// <see cref="VodDownloader.GetOutputPath"/> should return a path whose filename
    /// is <c>{vodId}.mp4</c>.
    /// </summary>
    [Test]
    public async Task VodDownloader_GetOutputPath_ReturnsExpectedFilename()
    {
        var downloader = new VodDownloader(null!);

        var path = downloader.GetOutputPath("12345");

        await Assert.That(Path.GetFileName(path)).IsEqualTo("12345.mp4");
    }

    /// <summary>
    /// Different VOD ids should produce different output paths.
    /// </summary>
    [Test]
    public async Task VodDownloader_GetOutputPath_DifferentIds_ProduceDifferentPaths()
    {
        var downloader = new VodDownloader(null!);

        var path1 = downloader.GetOutputPath("aaa");
        var path2 = downloader.GetOutputPath("bbb");

        await Assert.That(path1).IsNotEqualTo(path2);
    }

    // =========================================================================
    // ChatDownloader.GetOutputPath
    // =========================================================================

    /// <summary>
    /// <see cref="ChatDownloader.GetOutputPath"/> should return a path whose filename
    /// is <c>{vodId}.json</c>.
    /// </summary>
    [Test]
    public async Task ChatDownloader_GetOutputPath_ReturnsExpectedFilename()
    {
        var downloader = new ChatDownloader(null!);

        var path = downloader.GetOutputPath("12345");

        await Assert.That(Path.GetFileName(path)).IsEqualTo("12345.json");
    }

    /// <summary>
    /// Different VOD ids should produce different output paths.
    /// </summary>
    [Test]
    public async Task ChatDownloader_GetOutputPath_DifferentIds_ProduceDifferentPaths()
    {
        var downloader = new ChatDownloader(null!);

        var path1 = downloader.GetOutputPath("aaa");
        var path2 = downloader.GetOutputPath("bbb");

        await Assert.That(path1).IsNotEqualTo(path2);
    }

    // =========================================================================
    // ChatRenderer.GetOutputPath
    // =========================================================================

    /// <summary>
    /// <see cref="ChatRenderer.GetOutputPath"/> should return a path whose filename
    /// is <c>{vodId}_chat.mp4</c>.
    /// </summary>
    [Test]
    public async Task ChatRenderer_GetOutputPath_ReturnsExpectedFilename()
    {
        var renderer = new ChatRenderer(null!);

        var path = renderer.GetOutputPath("12345");

        await Assert.That(Path.GetFileName(path)).IsEqualTo("12345_chat.mp4");
    }

    /// <summary>
    /// Different VOD ids should produce different output paths.
    /// </summary>
    [Test]
    public async Task ChatRenderer_GetOutputPath_DifferentIds_ProduceDifferentPaths()
    {
        var renderer = new ChatRenderer(null!);

        var path1 = renderer.GetOutputPath("aaa");
        var path2 = renderer.GetOutputPath("bbb");

        await Assert.That(path1).IsNotEqualTo(path2);
    }

    // =========================================================================
    // FinalRenderer.GetOutputPath
    // =========================================================================

    /// <summary>
    /// <see cref="FinalRenderer.GetOutputPath"/> should return a path whose filename
    /// is <c>{vodId}_final.mp4</c>.
    /// </summary>
    [Test]
    public async Task FinalRenderer_GetOutputPath_ReturnsExpectedFilename()
    {
        var renderer = new FinalRenderer(null!);

        var path = renderer.GetOutputPath("12345");

        await Assert.That(Path.GetFileName(path)).IsEqualTo("12345_final.mp4");
    }

    /// <summary>
    /// Different VOD ids should produce different output paths.
    /// </summary>
    [Test]
    public async Task FinalRenderer_GetOutputPath_DifferentIds_ProduceDifferentPaths()
    {
        var renderer = new FinalRenderer(null!);

        var path1 = renderer.GetOutputPath("aaa");
        var path2 = renderer.GetOutputPath("bbb");

        await Assert.That(path1).IsNotEqualTo(path2);
    }
}

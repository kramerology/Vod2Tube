using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Domain;

namespace Vod2Tube.Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="Pipeline"/> domain entity.
/// Verifies default values and property assignment.
/// </summary>
public class PipelineTests
{
    /// <summary>
    /// All string properties on a new <see cref="Pipeline"/> should default to
    /// empty string rather than null.
    /// </summary>
    [Test]
    public async Task Pipeline_DefaultStringProperties_AreEmpty()
    {
        var pipeline = new Pipeline();

        await Assert.That(pipeline.Stage).IsEqualTo(string.Empty);
        await Assert.That(pipeline.Description).IsEqualTo(string.Empty);
        await Assert.That(pipeline.VodFilePath).IsEqualTo(string.Empty);
        await Assert.That(pipeline.ChatTextFilePath).IsEqualTo(string.Empty);
        await Assert.That(pipeline.ChatVideoFilePath).IsEqualTo(string.Empty);
        await Assert.That(pipeline.FinalVideoFilePath).IsEqualTo(string.Empty);
        await Assert.That(pipeline.YoutubeVideoId).IsEqualTo(string.Empty);
        await Assert.That(pipeline.LeasedBy).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// All properties assigned to a <see cref="Pipeline"/> instance should round-trip
    /// correctly.
    /// </summary>
    [Test]
    public async Task Pipeline_PropertyAssignment_RoundTrips()
    {
        var now = DateTime.UtcNow;
        var pipeline = new Pipeline
        {
            VodId = "vod123",
            Stage = "Downloading",
            Description = "Downloading VOD",
            VodFilePath = "/data/vod123.mp4",
            ChatTextFilePath = "/data/vod123.json",
            ChatVideoFilePath = "/data/vod123_chat.mp4",
            FinalVideoFilePath = "/data/vod123_final.mp4",
            YoutubeVideoId = "ytXYZ",
            LeasedBy = "VodDownloader",
            LeasedAtUTC = now
        };

        await Assert.That(pipeline.VodId).IsEqualTo("vod123");
        await Assert.That(pipeline.Stage).IsEqualTo("Downloading");
        await Assert.That(pipeline.Description).IsEqualTo("Downloading VOD");
        await Assert.That(pipeline.VodFilePath).IsEqualTo("/data/vod123.mp4");
        await Assert.That(pipeline.ChatTextFilePath).IsEqualTo("/data/vod123.json");
        await Assert.That(pipeline.ChatVideoFilePath).IsEqualTo("/data/vod123_chat.mp4");
        await Assert.That(pipeline.FinalVideoFilePath).IsEqualTo("/data/vod123_final.mp4");
        await Assert.That(pipeline.YoutubeVideoId).IsEqualTo("ytXYZ");
        await Assert.That(pipeline.LeasedBy).IsEqualTo("VodDownloader");
        await Assert.That(pipeline.LeasedAtUTC).IsEqualTo(now);
    }

    /// <summary>
    /// The <see cref="Pipeline.Stage"/> property can be advanced through the expected
    /// processing stages without error.
    /// </summary>
    [Test]
    [Arguments("Pending")]
    [Arguments("DownloadingVod")]
    [Arguments("PendingDownloadChat")]
    [Arguments("DownloadingChat")]
    [Arguments("PendingRenderingChat")]
    [Arguments("RenderingChat")]
    [Arguments("PendingCombining")]
    [Arguments("Combining")]
    [Arguments("PendingUpload")]
    [Arguments("Uploading")]
    [Arguments("Uploaded")]
    public async Task Pipeline_Stage_AcceptsKnownStageValue(string stage)
    {
        var pipeline = new Pipeline { Stage = stage };
        await Assert.That(pipeline.Stage).IsEqualTo(stage);
    }
}

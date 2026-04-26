using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application.Services;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PipelineService"/>.
/// An isolated in-memory database is created for every test so tests do not
/// interfere with each other.
/// </summary>
public class PipelineServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AppDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static TwitchVod MakeVod(string id, string title = "Test VOD", string channel = "testchannel")
        => new() { Id = id, Title = title, ChannelName = channel };

    private static Pipeline MakePipeline(string vodId, string stage, bool failed = false, bool paused = false)
        => new() { VodId = vodId, Stage = stage, Failed = failed, Paused = paused };

    // =========================================================================
    // GetActiveJobsAsync
    // =========================================================================

    [Test]
    public async Task GetActiveJobsAsync_NoPipelines_ReturnsEmpty()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_NoPipelines_ReturnsEmpty));
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetActiveJobsAsync_PendingJob_IsIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_PendingJob_IsIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "Pending"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].VodId).IsEqualTo("v1");
    }

    [Test]
    public async Task GetActiveJobsAsync_AllActiveStages_AreIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_AllActiveStages_AreIncluded));
        string[] activeStages =
        [
            "Pending", "DownloadingVod", "PendingDownloadChat", "DownloadingChat",
            "PendingRenderingChat", "RenderingChat", "PendingCombining", "Combining",
            "PendingUpload", "Uploading"
        ];
        for (int i = 0; i < activeStages.Length; i++)
            ctx.Pipelines.Add(MakePipeline($"v{i}", activeStages[i]));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).Count().IsEqualTo(activeStages.Length);
    }

    [Test]
    public async Task GetActiveJobsAsync_UploadedJob_IsNotIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_UploadedJob_IsNotIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "Uploaded"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetActiveJobsAsync_CancelledJob_IsNotIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_CancelledJob_IsNotIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "Cancelled"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetActiveJobsAsync_FailedJobWithActiveStage_IsIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_FailedJobWithActiveStage_IsIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "DownloadingVod", failed: true));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].Failed).IsEqualTo(true);
    }

    [Test]
    public async Task GetActiveJobsAsync_FailedCancelledJob_IsNotIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_FailedCancelledJob_IsNotIncluded));
        // A job that was both Failed and Cancelled should appear in Completed, not Active.
        ctx.Pipelines.Add(MakePipeline("v1", "Cancelled", failed: true));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetActiveJobsAsync_JoinsVodTitle_WhenVodExists()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_JoinsVodTitle_WhenVodExists));
        ctx.TwitchVods.Add(MakeVod("v1", title: "Great Stream"));
        ctx.Pipelines.Add(MakePipeline("v1", "Pending"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result[0].Title).IsEqualTo("Great Stream");
        await Assert.That(result[0].ChannelName).IsEqualTo("testchannel");
    }

    [Test]
    public async Task GetActiveJobsAsync_FallsBackToVodId_WhenVodMissing()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_FallsBackToVodId_WhenVodMissing));
        ctx.Pipelines.Add(MakePipeline("orphan1", "Pending"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result[0].Title).IsEqualTo("orphan1");
        await Assert.That(result[0].ChannelName).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetActiveJobsAsync_OrdersHigherStageFirst()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetActiveJobsAsync_OrdersHigherStageFirst));
        ctx.Pipelines.Add(MakePipeline("low",  "Pending"));
        ctx.Pipelines.Add(MakePipeline("high", "Uploading"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetActiveJobsAsync();

        await Assert.That(result[0].VodId).IsEqualTo("high");
        await Assert.That(result[1].VodId).IsEqualTo("low");
    }

    // =========================================================================
    // GetCompletedJobsAsync
    // =========================================================================

    [Test]
    public async Task GetCompletedJobsAsync_NoPipelines_ReturnsEmpty()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetCompletedJobsAsync_NoPipelines_ReturnsEmpty));
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetCompletedJobsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetCompletedJobsAsync_UploadedJob_IsIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetCompletedJobsAsync_UploadedJob_IsIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "Uploaded"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetCompletedJobsAsync();

        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].VodId).IsEqualTo("v1");
    }

    [Test]
    public async Task GetCompletedJobsAsync_CancelledJob_IsIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetCompletedJobsAsync_CancelledJob_IsIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "Cancelled"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetCompletedJobsAsync();

        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0].Stage).IsEqualTo("Cancelled");
    }

    [Test]
    public async Task GetCompletedJobsAsync_ActiveJob_IsNotIncluded()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetCompletedJobsAsync_ActiveJob_IsNotIncluded));
        ctx.Pipelines.Add(MakePipeline("v1", "Pending"));
        ctx.Pipelines.Add(MakePipeline("v2", "Uploading"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetCompletedJobsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetCompletedJobsAsync_JoinsVodMetadata()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetCompletedJobsAsync_JoinsVodMetadata));
        ctx.TwitchVods.Add(MakeVod("v1", title: "Old Vod", channel: "streamer"));
        ctx.Pipelines.Add(MakePipeline("v1", "Uploaded"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.GetCompletedJobsAsync();

        await Assert.That(result[0].Title).IsEqualTo("Old Vod");
        await Assert.That(result[0].ChannelName).IsEqualTo("streamer");
    }

    // =========================================================================
    // PauseJobAsync
    // =========================================================================

    [Test]
    public async Task PauseJobAsync_ExistingJob_SetsPausedTrue()
    {
        await using var ctx = CreateInMemoryContext(nameof(PauseJobAsync_ExistingJob_SetsPausedTrue));
        ctx.Pipelines.Add(MakePipeline("v1", "DownloadingVod", paused: false));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.PauseJobAsync("v1");

        await Assert.That(result).IsEqualTo(true);
        var job = await ctx.Pipelines.FindAsync("v1");
        await Assert.That(job!.Paused).IsEqualTo(true);
    }

    [Test]
    public async Task PauseJobAsync_MissingJob_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(PauseJobAsync_MissingJob_ReturnsFalse));
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.PauseJobAsync("nonexistent");

        await Assert.That(result).IsEqualTo(false);
    }

    // =========================================================================
    // ResumeJobAsync
    // =========================================================================

    [Test]
    public async Task ResumeJobAsync_ExistingJob_SetsPausedFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResumeJobAsync_ExistingJob_SetsPausedFalse));
        ctx.Pipelines.Add(MakePipeline("v1", "DownloadingVod", paused: true));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.ResumeJobAsync("v1");

        await Assert.That(result).IsEqualTo(true);
        var job = await ctx.Pipelines.FindAsync("v1");
        await Assert.That(job!.Paused).IsEqualTo(false);
    }

    [Test]
    public async Task ResumeJobAsync_MissingJob_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResumeJobAsync_MissingJob_ReturnsFalse));
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.ResumeJobAsync("nonexistent");

        await Assert.That(result).IsEqualTo(false);
    }

    // =========================================================================
    // CancelJobAsync
    // =========================================================================

    [Test]
    public async Task CancelJobAsync_ExistingJob_SetsStageAndClearsFields()
    {
        await using var ctx = CreateInMemoryContext(nameof(CancelJobAsync_ExistingJob_SetsStageAndClearsFields));
        ctx.Pipelines.Add(new Pipeline
        {
            VodId = "v1",
            Stage = "Combining",
            Paused = true,
            Failed = true,
            FailCount = 2,
            FailReason = "Network error",
            Description = "Progress info"
        });
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.CancelJobAsync("v1");

        await Assert.That(result).IsEqualTo(true);
        var job = await ctx.Pipelines.FindAsync("v1");
        await Assert.That(job!.Stage).IsEqualTo("Cancelled");
        await Assert.That(job.Paused).IsEqualTo(false);
        await Assert.That(job.Failed).IsEqualTo(false);
        await Assert.That(job.FailCount).IsEqualTo(0);
        await Assert.That(job.FailReason).IsEqualTo(string.Empty);
        await Assert.That(job.Description).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task CancelJobAsync_MissingJob_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(CancelJobAsync_MissingJob_ReturnsFalse));
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.CancelJobAsync("nonexistent");

        await Assert.That(result).IsEqualTo(false);
    }

    [Test]
    public async Task CancelJobAsync_CancelledJob_MovesToCompletedNotActive()
    {
        await using var ctx = CreateInMemoryContext(nameof(CancelJobAsync_CancelledJob_MovesToCompletedNotActive));
        ctx.Pipelines.Add(MakePipeline("v1", "Pending"));
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        await svc.CancelJobAsync("v1");

        var active = await svc.GetActiveJobsAsync();
        var completed = await svc.GetCompletedJobsAsync();
        await Assert.That(active).IsEmpty();
        await Assert.That(completed).Count().IsEqualTo(1);
        await Assert.That(completed[0].Stage).IsEqualTo("Cancelled");
    }

    // =========================================================================
    // RetryJobAsync
    // =========================================================================

    [Test]
    public async Task RetryJobAsync_FailedJob_ResetsToPending()
    {
        await using var ctx = CreateInMemoryContext(nameof(RetryJobAsync_FailedJob_ResetsToPending));
        ctx.Pipelines.Add(new Pipeline
        {
            VodId = "v1",
            Stage = "DownloadingVod",
            Failed = true,
            FailCount = 2,
            FailReason = "Network error",
            Description = "Failed at stage 'DownloadingVod'",
            Paused = true
        });
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.RetryJobAsync("v1");

        await Assert.That(result).IsEqualTo(true);
        var job = await ctx.Pipelines.FindAsync("v1");
        await Assert.That(job!.Stage).IsEqualTo("Pending");
        await Assert.That(job.Failed).IsEqualTo(false);
        await Assert.That(job.FailCount).IsEqualTo(0);
        await Assert.That(job.FailReason).IsEqualTo(string.Empty);
        await Assert.That(job.Description).IsEqualTo(string.Empty);
        await Assert.That(job.Paused).IsEqualTo(false);
    }

    [Test]
    public async Task RetryJobAsync_ClearsUploadRelatedState()
    {
        await using var ctx = CreateInMemoryContext(nameof(RetryJobAsync_ClearsUploadRelatedState));
        ctx.Pipelines.Add(new Pipeline
        {
            VodId = "v1",
            Stage = "Uploading",
            Failed = true,
            YoutubeVideoId = "yt_abc",
            ResumableUploadUri = "https://upload.example.com/session/abc"
        });
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        await svc.RetryJobAsync("v1");

        var job = await ctx.Pipelines.FindAsync("v1");
        await Assert.That(job!.YoutubeVideoId).IsEqualTo(string.Empty);
        await Assert.That(job.ResumableUploadUri).IsNullOrEmpty();
    }

    [Test]
    public async Task RetryJobAsync_MissingJob_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(RetryJobAsync_MissingJob_ReturnsFalse));
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.RetryJobAsync("nonexistent");

        await Assert.That(result).IsEqualTo(false);
    }

    [Test]
    public async Task RetryJobAsync_RetriedJob_AppearsInActiveJobs()
    {
        await using var ctx = CreateInMemoryContext(nameof(RetryJobAsync_RetriedJob_AppearsInActiveJobs));
        ctx.Pipelines.Add(new Pipeline
        {
            VodId = "v1",
            Stage = "Cancelled",
            Failed = true,
            FailCount = 1,
            FailReason = "Transient error"
        });
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        await svc.RetryJobAsync("v1");

        var active = await svc.GetActiveJobsAsync();
        await Assert.That(active).Count().IsEqualTo(1);
        await Assert.That(active[0].Stage).IsEqualTo("Pending");
        await Assert.That(active[0].Failed).IsEqualTo(false);
    }

    [Test]
    public async Task QueueNextVodForChannelAsync_WithoutTwitchService_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(QueueNextVodForChannelAsync_WithoutTwitchService_ReturnsFalse));
        ctx.Channels.Add(new Channel
        {
            Id = 1,
            ChannelName = "alpha",
            Active = true,
            AddedAtUTC = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.QueueNextVodForChannelAsync(1);

        await Assert.That(result).IsEqualTo(false);
        var channel = await ctx.Channels.FindAsync(1);
        await Assert.That(channel!.LastQueueCheckAtUTC).IsNotNull();
        await Assert.That(await ctx.Pipelines.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task QueueNextVodForChannelAsync_ChannelWithOutstandingJob_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(QueueNextVodForChannelAsync_ChannelWithOutstandingJob_ReturnsFalse));
        ctx.Channels.Add(new Channel
        {
            Id = 1,
            ChannelName = "alpha",
            Active = true,
            AddedAtUTC = DateTime.UtcNow,
        });
        ctx.TwitchVods.Add(new TwitchVod
        {
            Id = "vod-1",
            ChannelName = "alpha",
            Title = "Queued VOD",
        });
        ctx.Pipelines.Add(new Pipeline
        {
            VodId = "vod-1",
            Stage = "Pending",
        });
        await ctx.SaveChangesAsync();
        var svc = new PipelineService(ctx, NullLogger<PipelineService>.Instance);

        var result = await svc.QueueNextVodForChannelAsync(1);

        await Assert.That(result).IsEqualTo(false);
        await Assert.That(await ctx.Pipelines.CountAsync()).IsEqualTo(1);
    }
}

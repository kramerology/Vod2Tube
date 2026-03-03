using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="JobManager.FindHighestPriorityJobAsync"/>
/// and <see cref="JobManager.ProcessJobToCompletionAsync"/>.
/// </summary>
public class JobManagerTests
{
    private static AppDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Builds a minimal <see cref="IServiceProvider"/> with all pipeline workers registered.
    /// Optionally substitutes the <see cref="VodDownloader"/> with a custom subclass.
    /// </summary>
    private static IServiceProvider CreateWorkerProvider(VodDownloader? vodDownloader = null, VideoUploader? videoUploader = null)
    {
        var svc = new ServiceCollection();
        svc.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("WorkerProvider"));
        var ds = new TwitchDownloadService(NullLogger<TwitchDownloadService>.Instance);
        svc.AddSingleton(ds);
        if (vodDownloader != null)
            svc.AddSingleton(vodDownloader);
        else
            svc.AddSingleton<VodDownloader>();
        svc.AddSingleton<ChatDownloader>();
        svc.AddSingleton<ChatRenderer>();
        svc.AddSingleton<FinalRenderer>();
        if (videoUploader != null)
            svc.AddSingleton(videoUploader);
        else
            svc.AddSingleton<VideoUploader>();
        return svc.BuildServiceProvider();
    }

    // =========================================================================
    // FindHighestPriorityJobAsync
    // =========================================================================

    [Test]
    public async Task FindHighestPriorityJob_NoPipelines_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_NoPipelines_ReturnsNull));

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindHighestPriorityJob_SinglePendingJob_ReturnsThatJob()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_SinglePendingJob_ReturnsThatJob));
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.VodId).IsEqualTo("v1");
    }

    [Test]
    public async Task FindHighestPriorityJob_PendingUploadBeatingPending_ReturnsPendingUpload()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_PendingUploadBeatingPending_ReturnsPendingUpload));
        ctx.Pipelines.Add(new Pipeline { VodId = "low",  Stage = "Pending" });
        ctx.Pipelines.Add(new Pipeline { VodId = "high", Stage = "PendingUpload" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("high");
    }

    [Test]
    public async Task FindHighestPriorityJob_UploadingIsHighestPriority()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_UploadingIsHighestPriority));
        ctx.Pipelines.Add(new Pipeline { VodId = "a", Stage = "Pending" });
        ctx.Pipelines.Add(new Pipeline { VodId = "b", Stage = "PendingRenderingChat" });
        ctx.Pipelines.Add(new Pipeline { VodId = "c", Stage = "Uploading" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("c");
    }

    [Test]
    public async Task FindHighestPriorityJob_UploadedJobsIgnored()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_UploadedJobsIgnored));
        ctx.Pipelines.Add(new Pipeline { VodId = "done",    Stage = "Uploaded" });
        ctx.Pipelines.Add(new Pipeline { VodId = "pending", Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("pending");
    }

    [Test]
    public async Task FindHighestPriorityJob_FailedJobsIgnored()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_FailedJobsIgnored));
        ctx.Pipelines.Add(new Pipeline { VodId = "broken",  Stage = "Failed" });
        ctx.Pipelines.Add(new Pipeline { VodId = "pending", Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("pending");
    }

    [Test]
    public async Task StagePriority_ContainsAllExpectedStagesInOrder()
    {
        string[] expected =
        [
            "Pending",
            "DownloadingVod",
            "PendingDownloadChat",
            "DownloadingChat",
            "PendingRenderingChat",
            "RenderingChat",
            "PendingCombining",
            "Combining",
            "PendingUpload",
            "Uploading"
        ];

        await Assert.That(JobManager.StagePriority).IsEquivalentTo(expected);
    }

    // =========================================================================
    // ProcessJobToCompletionAsync — crash-recovery rollback tests
    // =========================================================================

    [Test]
    public async Task ProcessJob_PendingRenderingChat_MissingVodFilePath_RollsBackToPending()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingRenderingChat_MissingVodFilePath_RollsBackToPending));
        var job = new Pipeline { VodId = "v1", Stage = "PendingRenderingChat", VodFilePath = "", ChatTextFilePath = "/chat.json" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Stage).IsEqualTo("Pending");
    }

    [Test]
    public async Task ProcessJob_PendingRenderingChat_MissingChatTextFilePath_RollsBackToPendingDownloadChat()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingRenderingChat_MissingChatTextFilePath_RollsBackToPendingDownloadChat));
        string tempVod = Path.GetTempFileName();
        try
        {
            var job = new Pipeline { VodId = "v1", Stage = "PendingRenderingChat", VodFilePath = tempVod, ChatTextFilePath = "" };
            ctx.Pipelines.Add(job);
            await ctx.SaveChangesAsync();

            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.Stage).IsEqualTo("PendingDownloadChat");
        }
        finally
        {
            File.Delete(tempVod);
        }
    }

    [Test]
    public async Task ProcessJob_PendingCombining_MissingVodFilePath_RollsBackToPending()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingCombining_MissingVodFilePath_RollsBackToPending));
        var job = new Pipeline { VodId = "v1", Stage = "PendingCombining", VodFilePath = "", ChatVideoFilePath = "/chat.mp4" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Stage).IsEqualTo("Pending");
    }

    [Test]
    public async Task ProcessJob_PendingCombining_MissingChatVideoFilePath_RollsBackToPendingRenderingChat()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingCombining_MissingChatVideoFilePath_RollsBackToPendingRenderingChat));
        string tempVod = Path.GetTempFileName();
        try
        {
            var job = new Pipeline { VodId = "v1", Stage = "PendingCombining", VodFilePath = tempVod, ChatVideoFilePath = "" };
            ctx.Pipelines.Add(job);
            await ctx.SaveChangesAsync();

            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.Stage).IsEqualTo("PendingRenderingChat");
        }
        finally
        {
            File.Delete(tempVod);
        }
    }

    [Test]
    public async Task ProcessJob_PendingUpload_MissingFinalVideoFilePath_RollsBackToPendingCombining()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingUpload_MissingFinalVideoFilePath_RollsBackToPendingCombining));
        var job = new Pipeline { VodId = "v1", Stage = "PendingUpload", FinalVideoFilePath = "" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Stage).IsEqualTo("PendingCombining");
    }

    // =========================================================================
    // ProcessJobToCompletionAsync — file-not-found-on-disk rollback tests
    // =========================================================================

    [Test]
    public async Task ProcessJob_PendingRenderingChat_VodFileNotOnDisk_RollsBackToPending()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingRenderingChat_VodFileNotOnDisk_RollsBackToPending));
        string missingVod = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        string missingChat = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var job = new Pipeline { VodId = "v1", Stage = "PendingRenderingChat", VodFilePath = missingVod, ChatTextFilePath = missingChat };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Stage).IsEqualTo("Pending");
        await Assert.That(job.VodFilePath).IsEqualTo("");
    }

    [Test]
    public async Task ProcessJob_PendingRenderingChat_ChatFileNotOnDisk_RollsBackToPendingDownloadChat()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingRenderingChat_ChatFileNotOnDisk_RollsBackToPendingDownloadChat));
        string tempVod = Path.GetTempFileName();
        try
        {
            string missingChat = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
            var job = new Pipeline { VodId = "v1", Stage = "PendingRenderingChat", VodFilePath = tempVod, ChatTextFilePath = missingChat };
            ctx.Pipelines.Add(job);
            await ctx.SaveChangesAsync();

            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.Stage).IsEqualTo("PendingDownloadChat");
            await Assert.That(job.ChatTextFilePath).IsEqualTo("");
        }
        finally
        {
            File.Delete(tempVod);
        }
    }

    [Test]
    public async Task ProcessJob_PendingCombining_VodFileNotOnDisk_RollsBackToPending()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingCombining_VodFileNotOnDisk_RollsBackToPending));
        string missingVod = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        string missingChatVideo = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        var job = new Pipeline { VodId = "v1", Stage = "PendingCombining", VodFilePath = missingVod, ChatVideoFilePath = missingChatVideo };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Stage).IsEqualTo("Pending");
        await Assert.That(job.VodFilePath).IsEqualTo("");
    }

    [Test]
    public async Task ProcessJob_PendingCombining_ChatVideoNotOnDisk_RollsBackToPendingRenderingChat()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingCombining_ChatVideoNotOnDisk_RollsBackToPendingRenderingChat));
        string tempVod = Path.GetTempFileName();
        try
        {
            string missingChatVideo = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
            var job = new Pipeline { VodId = "v1", Stage = "PendingCombining", VodFilePath = tempVod, ChatVideoFilePath = missingChatVideo };
            ctx.Pipelines.Add(job);
            await ctx.SaveChangesAsync();

            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.Stage).IsEqualTo("PendingRenderingChat");
            await Assert.That(job.ChatVideoFilePath).IsEqualTo("");
        }
        finally
        {
            File.Delete(tempVod);
        }
    }

    [Test]
    public async Task ProcessJob_PendingUpload_FinalVideoNotOnDisk_RollsBackToPendingCombining()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingUpload_FinalVideoNotOnDisk_RollsBackToPendingCombining));
        string missingFinal = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_final.mp4");
        var job = new Pipeline { VodId = "v1", Stage = "PendingUpload", FinalVideoFilePath = missingFinal };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Stage).IsEqualTo("PendingCombining");
        await Assert.That(job.FinalVideoFilePath).IsEqualTo("");
    }

    // =========================================================================
    // ProcessJobToCompletionAsync — output-already-exists skip tests
    // =========================================================================

    [Test]
    public async Task ProcessJob_DownloadingVod_OutputExists_SkipsToNextStage()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_DownloadingVod_OutputExists_SkipsToNextStage));
        var job = new Pipeline { VodId = "v1", Stage = "Pending" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        // Create output files for all stages so every stage is skipped
        var vodDownloader = new VodDownloader(null!);
        var chatDownloader = new ChatDownloader(null!);
        var chatRenderer = new ChatRenderer(null!);
        var finalRenderer = new FinalRenderer(null!);

        string vodOutputPath = vodDownloader.GetOutputPath("v1");
        string chatOutputPath = chatDownloader.GetOutputPath("v1");
        string chatVideoOutputPath = chatRenderer.GetOutputPath("v1");
        string finalOutputPath = finalRenderer.GetOutputPath("v1");

        Directory.CreateDirectory(Path.GetDirectoryName(vodOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(chatOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(chatVideoOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(finalOutputPath)!);

        File.WriteAllText(vodOutputPath, "fake vod");
        File.WriteAllText(chatOutputPath, "fake chat");
        File.WriteAllText(chatVideoOutputPath, "fake chat video");
        File.WriteAllText(finalOutputPath, "fake final video");
        try
        {
            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(videoUploader: new StubVideoUploader()), NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.VodFilePath).IsEqualTo(vodOutputPath);
            await Assert.That(job.Stage).IsEqualTo("Uploaded");
        }
        finally
        {
            File.Delete(vodOutputPath);
            File.Delete(chatOutputPath);
            File.Delete(chatVideoOutputPath);
            File.Delete(finalOutputPath);
        }
    }

    [Test]
    public async Task ProcessJob_DownloadingChat_OutputExists_SkipsToNextStage()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_DownloadingChat_OutputExists_SkipsToNextStage));

        // Create output files for all downstream stages
        var chatDownloader = new ChatDownloader(null!);
        var chatRenderer = new ChatRenderer(null!);
        var finalRenderer = new FinalRenderer(null!);

        string vodTempFile = Path.GetTempFileName();
        string chatOutputPath = chatDownloader.GetOutputPath("v2");
        string chatVideoOutputPath = chatRenderer.GetOutputPath("v2");
        string finalOutputPath = finalRenderer.GetOutputPath("v2");

        Directory.CreateDirectory(Path.GetDirectoryName(chatOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(chatVideoOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(finalOutputPath)!);

        File.WriteAllText(chatOutputPath, "fake chat");
        File.WriteAllText(chatVideoOutputPath, "fake chat video");
        File.WriteAllText(finalOutputPath, "fake final video");

        var job = new Pipeline { VodId = "v2", Stage = "PendingDownloadChat", VodFilePath = vodTempFile };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        try
        {
            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(videoUploader: new StubVideoUploader()), NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.ChatTextFilePath).IsEqualTo(chatOutputPath);
            await Assert.That(job.Stage).IsEqualTo("Uploaded");
        }
        finally
        {
            File.Delete(vodTempFile);
            File.Delete(chatOutputPath);
            File.Delete(chatVideoOutputPath);
            File.Delete(finalOutputPath);
        }
    }

    // =========================================================================
    // ProcessJobToCompletionAsync — worker failure → job marked Failed
    // =========================================================================

    [Test]
    public async Task ProcessJob_StageTransition_ResetsFailCount()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_StageTransition_ResetsFailCount));
        // Simulate a job that already has 2 failures recorded at a prior stage.
        var job = new Pipeline { VodId = "v1", Stage = "Pending", FailCount = 2 };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        // One failure at DownloadingVod — but the stage change from Pending → DownloadingVod
        // should reset FailCount to 0 first, so only 1 failure is counted here.
        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(new ThrowingVodDownloader()), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Failed).IsEqualTo(false);
        await Assert.That(job.FailCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProcessJob_WorkerThrows_MarksJobAsFailed_PreservingFailedStage()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_WorkerThrows_MarksJobAsFailed_PreservingFailedStage));
        var job = new Pipeline { VodId = "v1", Stage = "Pending" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        // Three retryable failures push FailCount to 3, which triggers permanent failure.
        for (int i = 0; i < 3; i++)
            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(new ThrowingVodDownloader()), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Failed).IsEqualTo(true);
        await Assert.That(job.FailCount).IsEqualTo(3);
        await Assert.That(job.FailReason).Contains("DownloadingVod");
    }

    [Test]
    public async Task ProcessJob_PermanentFailure_MarksJobFailedImmediately()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PermanentFailure_MarksJobFailedImmediately));
        var job = new Pipeline { VodId = "v1", Stage = "Pending" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(new PermanentlyFailingVodDownloader()), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Failed).IsEqualTo(true);
        await Assert.That(job.FailCount).IsEqualTo(1);
        await Assert.That(job.FailReason).Contains("permanent");
        await Assert.That(job.Stage).IsEqualTo("DownloadingVod");
    }

    [Test]
    public async Task ProcessJob_RetryableFailure_DoesNotMarkFailedUnderThreshold()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_RetryableFailure_DoesNotMarkFailedUnderThreshold));
        var job = new Pipeline { VodId = "v1", Stage = "Pending" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        // Two retryable failures — should NOT be permanently failed yet.
        for (int i = 0; i < 2; i++)
            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(new ThrowingVodDownloader()), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Failed).IsEqualTo(false);
        await Assert.That(job.FailCount).IsEqualTo(2);
    }

    [Test]
    public async Task FindHighestPriorityJob_FailedFlagJobsIgnored()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_FailedFlagJobsIgnored));
        ctx.Pipelines.Add(new Pipeline { VodId = "broken",  Stage = "Pending", Failed = true });
        ctx.Pipelines.Add(new Pipeline { VodId = "pending", Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("pending");
    }

    /// <summary>
    /// A <see cref="VodDownloader"/> stub that always throws from <see cref="RunAsync"/>.
    /// </summary>
    private sealed class ThrowingVodDownloader : VodDownloader
    {
        public ThrowingVodDownloader() : base(null!) { }

        public override IAsyncEnumerable<string> RunAsync(string vodId, CancellationToken ct)
            => throw new InvalidOperationException("Simulated download failure");
    }

    /// <summary>
    /// A <see cref="VodDownloader"/> stub that throws a permanent
    /// <see cref="PipelineJobException"/> from <see cref="RunAsync"/>.
    /// </summary>
    private sealed class PermanentlyFailingVodDownloader : VodDownloader
    {
        public PermanentlyFailingVodDownloader() : base(null!) { }

        public override IAsyncEnumerable<string> RunAsync(string vodId, CancellationToken ct)
            => throw new PipelineJobException("Simulated permanent failure", isPermanent: true);
    }

    /// <summary>
    /// A <see cref="VideoUploader"/> stub that completes immediately without performing a real upload.
    /// </summary>
    private sealed class StubVideoUploader : VideoUploader
    {
        public StubVideoUploader() : base(null!) { }

        public override async IAsyncEnumerable<string> RunAsync(string vodId, string finalFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return "Stub upload complete";
        }
    }
}

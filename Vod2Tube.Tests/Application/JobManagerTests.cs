using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;
using Vod2Tube.Application.Models;
using Vod2Tube.Application.PipelineWorkers;
using Vod2Tube.Application.Services;
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

    private static IOptionsSnapshot<AppSettings> DefaultOptions() => DefaultAppSettingsSnapshot.Instance;

    /// <summary>
    /// Builds a minimal <see cref="IServiceProvider"/> with all pipeline workers registered.
    /// Optionally substitutes the <see cref="VodDownloader"/> with a custom subclass.
    /// </summary>
    private static IServiceProvider CreateWorkerProvider(VodDownloader? vodDownloader = null, VideoUploader? videoUploader = null)
    {
        var svc = new ServiceCollection();
        svc.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("WorkerProvider"));
        svc.AddOptions<AppSettings>();
        svc.AddLogging();
        var ds = new TwitchDownloadService(NullLogger<TwitchDownloadService>.Instance, Options.Create(new AppSettings()));
        svc.AddSingleton(ds);
        svc.AddSingleton<YouTubeAccountService>();
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
        svc.AddSingleton<Archiver>();
        svc.AddSingleton<PipelineService>();
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
            "Uploading",
            "PendingArchiving",
            "Archiving"
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

        // Compute all output paths first, then create the job with them pre-populated in the DB
        var vodDownloader = new VodDownloader(null!, DefaultOptions());
        var chatDownloader = new ChatDownloader(null!, DefaultOptions());
        var chatRenderer = new ChatRenderer(null!, DefaultOptions());
        var finalRenderer = new FinalRenderer(null!, DefaultOptions());

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

        // All paths come from the database — the skip logic reads job.VodFilePath etc.
        var job = new Pipeline
        {
            VodId = "v1",
            Stage = "Pending",
            VodFilePath = vodOutputPath,
            ChatTextFilePath = chatOutputPath,
            ChatVideoFilePath = chatVideoOutputPath,
            FinalVideoFilePath = finalOutputPath
        };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();
        try
        {
            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(videoUploader: new StubVideoUploader()), NullLogger.Instance, CancellationToken.None);

            // After archiving the Archiver deletes all working copies, so the working-copy
            // paths should be empty once the job reaches "Uploaded".
            await Assert.That(job.VodFilePath).IsEqualTo("");
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
        var chatDownloader = new ChatDownloader(null!, DefaultOptions());
        var chatRenderer = new ChatRenderer(null!, DefaultOptions());
        var finalRenderer = new FinalRenderer(null!, DefaultOptions());

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

        // All downstream paths come from the database — the skip logic reads job.ChatTextFilePath etc.
        var job = new Pipeline
        {
            VodId = "v2",
            Stage = "PendingDownloadChat",
            VodFilePath = vodTempFile,
            ChatTextFilePath = chatOutputPath,
            ChatVideoFilePath = chatVideoOutputPath,
            FinalVideoFilePath = finalOutputPath
        };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        try
        {
            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(videoUploader: new StubVideoUploader()), NullLogger.Instance, CancellationToken.None);

            // After archiving the Archiver deletes all working copies, so the working-copy
            // paths should be empty once the job reaches "Uploaded".
            await Assert.That(job.ChatTextFilePath).IsEqualTo("");
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

    // =========================================================================
    // Pause support
    // =========================================================================

    [Test]
    public async Task FindHighestPriorityJob_PausedJob_IsNotReturned()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_PausedJob_IsNotReturned));
        ctx.Pipelines.Add(new Pipeline { VodId = "paused",  Stage = "Pending", Paused = true });
        ctx.Pipelines.Add(new Pipeline { VodId = "active",  Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("active");
    }

    [Test]
    public async Task FindHighestPriorityJob_AllJobsPaused_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_AllJobsPaused_ReturnsNull));
        ctx.Pipelines.Add(new Pipeline { VodId = "a", Stage = "Pending",       Paused = true });
        ctx.Pipelines.Add(new Pipeline { VodId = "b", Stage = "DownloadingVod", Paused = true });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task IsJobPausedAsync_ReturnsTrueWhenPausedInDb()
    {
        await using var ctx = CreateInMemoryContext(nameof(IsJobPausedAsync_ReturnsTrueWhenPausedInDb));
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Pending", Paused = true });
        await ctx.SaveChangesAsync();

        bool result = await JobManager.IsJobPausedAsync(ctx, "v1", CancellationToken.None);

        await Assert.That(result).IsEqualTo(true);
    }

    [Test]
    public async Task IsJobPausedAsync_ReturnsFalseWhenNotPaused()
    {
        await using var ctx = CreateInMemoryContext(nameof(IsJobPausedAsync_ReturnsFalseWhenNotPaused));
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Pending", Paused = false });
        await ctx.SaveChangesAsync();

        bool result = await JobManager.IsJobPausedAsync(ctx, "v1", CancellationToken.None);

        await Assert.That(result).IsEqualTo(false);
    }

    [Test]
    public async Task ProcessJob_PausedDuringDownloadingVod_ReturnEarlyWithJobPausedSet()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PausedDuringDownloadingVod_ReturnEarlyWithJobPausedSet));
        var job = new Pipeline { VodId = "v1", Stage = "Pending" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        // The stub downloader marks the job as paused in the DB on its first yield.
        var downloader = new PausingVodDownloader(ctx);

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(downloader), NullLogger.Instance, CancellationToken.None);

        await Assert.That(job.Paused).IsEqualTo(true);
        await Assert.That(job.Stage).IsEqualTo("DownloadingVod");
    }

    /// <summary>
    /// A <see cref="VodDownloader"/> stub that marks the job as paused in the database on its
    /// first status yield, simulating a pause request that arrives during processing.
    /// </summary>
    private sealed class PausingVodDownloader : VodDownloader
    {
        private readonly AppDbContext _ctx;

        public PausingVodDownloader(AppDbContext ctx) : base(null!, DefaultAppSettingsSnapshot.Instance)
        {
            _ctx = ctx;
        }

        public override async IAsyncEnumerable<ProgressStatus> RunAsync(string vodId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            // Simulate an external pause being written to the database before the first progress update.
            var pipeline = await _ctx.Pipelines.FindAsync(new object[] { vodId }, ct);
            if (pipeline != null)
            {
                pipeline.Paused = true;
                await _ctx.SaveChangesAsync(ct);
            }
            yield return ProgressStatus.Indeterminate("Downloading 1%");
        }
    }

    /// <summary>
    /// A <see cref="VodDownloader"/> stub that always throws from <see cref="RunAsync"/>.
    /// </summary>
    private sealed class ThrowingVodDownloader : VodDownloader
    {
        public ThrowingVodDownloader() : base(null!, DefaultAppSettingsSnapshot.Instance) { }

        public override IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, CancellationToken ct)
            => throw new InvalidOperationException("Simulated download failure");
    }

    /// <summary>
    /// A <see cref="VodDownloader"/> stub that throws a permanent
    /// <see cref="PipelineJobException"/> from <see cref="RunAsync"/>.
    /// </summary>
    private sealed class PermanentlyFailingVodDownloader : VodDownloader
    {
        public PermanentlyFailingVodDownloader() : base(null!, DefaultAppSettingsSnapshot.Instance) { }

        public override IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, CancellationToken ct)
            => throw new PipelineJobException("Simulated permanent failure", isPermanent: true);
    }

    /// <summary>
    /// A <see cref="VideoUploader"/> stub that completes immediately without performing a real upload.
    /// </summary>
    private sealed class StubVideoUploader : VideoUploader
    {
        public StubVideoUploader() : base(null!, null!) { }

        public override async IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, string finalFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return ProgressStatus.Indeterminate("Stub upload complete");
        }
    }

    // =========================================================================
    // Cancellation support
    // =========================================================================

    [Test]
    public async Task IsJobCancelledAsync_ReturnsTrueWhenStageIsCancelled()
    {
        await using var ctx = CreateInMemoryContext(nameof(IsJobCancelledAsync_ReturnsTrueWhenStageIsCancelled));
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Cancelled" });
        await ctx.SaveChangesAsync();

        bool result = await JobManager.IsJobCancelledAsync(ctx, "v1", CancellationToken.None);

        await Assert.That(result).IsEqualTo(true);
    }

    [Test]
    public async Task IsJobCancelledAsync_ReturnsFalseWhenStageIsNotCancelled()
    {
        await using var ctx = CreateInMemoryContext(nameof(IsJobCancelledAsync_ReturnsFalseWhenStageIsNotCancelled));
        ctx.Pipelines.Add(new Pipeline { VodId = "v1", Stage = "Pending" });
        await ctx.SaveChangesAsync();

        bool result = await JobManager.IsJobCancelledAsync(ctx, "v1", CancellationToken.None);

        await Assert.That(result).IsEqualTo(false);
    }

    [Test]
    public async Task FindHighestPriorityJob_CancelledJobIsIgnored()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_CancelledJobIsIgnored));
        ctx.Pipelines.Add(new Pipeline { VodId = "cancelled", Stage = "Cancelled" });
        ctx.Pipelines.Add(new Pipeline { VodId = "active",    Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("active");
    }

    [Test]
    public async Task ProcessJob_CancelledBeforeStart_ReturnEarlyAndStageRemainsAsSet()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_CancelledBeforeStart_ReturnEarlyAndStageRemainsAsSet));
        var job = new Pipeline { VodId = "v1", Stage = "Cancelled" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

        // Job should not have been transitioned to a processing stage.
        await Assert.That(job.Stage).IsEqualTo("Cancelled");
    }

    [Test]
    public async Task ProcessJob_CancelledDuringDownloadingVod_ReturnEarlyAndJobNotFailed()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_CancelledDuringDownloadingVod_ReturnEarlyAndJobNotFailed));
        var job = new Pipeline { VodId = "v1", Stage = "Pending" };
        ctx.Pipelines.Add(job);
        await ctx.SaveChangesAsync();

        // The stub downloader marks the job as cancelled in the DB on its first yield.
        var downloader = new CancellingVodDownloader(ctx);

        await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(downloader), NullLogger.Instance, CancellationToken.None);

        // Processing should have stopped: job is Cancelled (not uploaded) and not marked as failed.
        await Assert.That(job.Stage).IsEqualTo("Cancelled");
        await Assert.That(job.Failed).IsEqualTo(false);
    }


    // =========================================================================
    // Archiving stage
    // =========================================================================

    [Test]
    public async Task FindHighestPriorityJob_ArchivingBeatsUploading()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_ArchivingBeatsUploading));
        ctx.Pipelines.Add(new Pipeline { VodId = "archiving",  Stage = "Archiving" });
        ctx.Pipelines.Add(new Pipeline { VodId = "uploading",  Stage = "Uploading" });
        ctx.Pipelines.Add(new Pipeline { VodId = "pending",    Stage = "Pending" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("archiving");
    }

    [Test]
    public async Task FindHighestPriorityJob_PendingArchivingBeatsUploading()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_PendingArchivingBeatsUploading));
        ctx.Pipelines.Add(new Pipeline { VodId = "pending_archiving", Stage = "PendingArchiving" });
        ctx.Pipelines.Add(new Pipeline { VodId = "uploading",         Stage = "Uploading" });
        await ctx.SaveChangesAsync();

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result!.VodId).IsEqualTo("pending_archiving");
    }

    [Test]
    public async Task ProcessJob_PendingArchiving_ArchivesAndDeletesFilesAndTransitionsToUploaded()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingArchiving_ArchivesAndDeletesFilesAndTransitionsToUploaded));

        // Create real temp files to simulate pipeline outputs.
        string vodFile   = Path.GetTempFileName();
        string chatJson  = Path.GetTempFileName();
        string chatVideo = Path.GetTempFileName();
        string finalVideo = Path.GetTempFileName();

        try
        {
            File.WriteAllText(vodFile,    "fake vod");
            File.WriteAllText(chatJson,   "{}");
            File.WriteAllText(chatVideo,  "fake chat video");
            File.WriteAllText(finalVideo, "fake final video");

            var job = new Pipeline
            {
                VodId             = "v1",
                Stage             = "PendingArchiving",
                VodFilePath       = vodFile,
                ChatTextFilePath  = chatJson,
                ChatVideoFilePath = chatVideo,
                FinalVideoFilePath = finalVideo,
            };
            ctx.Pipelines.Add(job);
            await ctx.SaveChangesAsync();

            await JobManager.ProcessJobToCompletionAsync(job, ctx, CreateWorkerProvider(), NullLogger.Instance, CancellationToken.None);

            // Job should have progressed to Uploaded.
            await Assert.That(job.Stage).IsEqualTo("Uploaded");

            // All working files should have been deleted (archive is disabled by default).
            await Assert.That(File.Exists(vodFile)).IsEqualTo(false);
            await Assert.That(File.Exists(chatJson)).IsEqualTo(false);
            await Assert.That(File.Exists(chatVideo)).IsEqualTo(false);
            await Assert.That(File.Exists(finalVideo)).IsEqualTo(false);
        }
        finally
        {
            // Best-effort cleanup in case the test failed mid-way.
            foreach (var f in new[] { vodFile, chatJson, chatVideo, finalVideo })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    [Test]
    public async Task ProcessJob_PendingArchiving_CopiesFilesToArchiveDirAndDeletesOriginals()
    {
        await using var ctx = CreateInMemoryContext(nameof(ProcessJob_PendingArchiving_CopiesFilesToArchiveDirAndDeletesOriginals));

        string archiveDir = Path.Combine(Path.GetTempPath(), $"Vod2Tube_ArchiveTest_{Guid.NewGuid():N}");
        string vodFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(vodFile, "fake vod content");

            // Use an options snapshot that has archive enabled for VOD only.
            var archiveOptions = new DefaultAppSettingsSnapshot();
            archiveOptions.Value.ArchiveVodEnabled = true;
            archiveOptions.Value.ArchiveVodDir     = archiveDir;

            var job = new Pipeline
            {
                VodId       = "v1",
                Stage       = "PendingArchiving",
                VodFilePath = vodFile,
            };
            ctx.Pipelines.Add(job);
            await ctx.SaveChangesAsync();

            var svc = new ServiceCollection();
            svc.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("ArchiveCopyTest"));
            svc.AddLogging();
            svc.AddOptions<AppSettings>();
            var ds = new TwitchDownloadService(NullLogger<TwitchDownloadService>.Instance, Options.Create(new AppSettings()));
            svc.AddSingleton(ds);
            svc.AddSingleton<VodDownloader>();
            svc.AddSingleton<ChatDownloader>();
            svc.AddSingleton<ChatRenderer>();
            svc.AddSingleton<FinalRenderer>();
            svc.AddSingleton<YouTubeAccountService>();
            svc.AddSingleton<VideoUploader>();
            svc.AddSingleton(new Archiver(archiveOptions, NullLogger<Archiver>.Instance));
            svc.AddSingleton<PipelineService>();
            var provider = svc.BuildServiceProvider();

            await JobManager.ProcessJobToCompletionAsync(job, ctx, provider, NullLogger.Instance, CancellationToken.None);

            await Assert.That(job.Stage).IsEqualTo("Uploaded");

            // The original working file should have been deleted.
            await Assert.That(File.Exists(vodFile)).IsEqualTo(false);

            // The archive copy should exist.
            string expectedArchived = Path.Combine(archiveDir, Path.GetFileName(vodFile));
            await Assert.That(File.Exists(expectedArchived)).IsEqualTo(true);
        }
        finally
        {
            if (File.Exists(vodFile)) File.Delete(vodFile);
            if (Directory.Exists(archiveDir)) Directory.Delete(archiveDir, recursive: true);
        }
    }

    /// <summary>
    /// A <see cref="VodDownloader"/> stub that marks the job as cancelled in the database on its
    /// first status yield, simulating a cancel request that arrives during processing.
    /// </summary>
    private sealed class CancellingVodDownloader : VodDownloader
    {
        private readonly AppDbContext _ctx;

        public CancellingVodDownloader(AppDbContext ctx) : base(null!, DefaultAppSettingsSnapshot.Instance)
        {
            _ctx = ctx;
        }

        public override async IAsyncEnumerable<ProgressStatus> RunAsync(string vodId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            // Simulate an external cancellation being written to the database before the first progress update.
            var pipeline = await _ctx.Pipelines.FindAsync(new object[] { vodId }, ct);
            if (pipeline != null)
            {
                pipeline.Stage = "Cancelled";
                await _ctx.SaveChangesAsync(ct);
            }
            yield return ProgressStatus.Indeterminate("Downloading 1%");
        }
    }
}

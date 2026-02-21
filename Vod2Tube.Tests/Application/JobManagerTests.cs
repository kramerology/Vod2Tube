using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="JobManager.FindHighestPriorityJobAsync"/>.
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
    /// When there are no pipeline entries, FindHighestPriorityJobAsync should return null.
    /// </summary>
    [Test]
    public async Task FindHighestPriorityJob_NoPipelines_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(FindHighestPriorityJob_NoPipelines_ReturnsNull));

        var result = await JobManager.FindHighestPriorityJobAsync(ctx, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// A single Pending job should be returned when it is the only entry.
    /// </summary>
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

    /// <summary>
    /// A PendingUpload job should be selected over a Pending job.
    /// </summary>
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

    /// <summary>
    /// The job furthest along the pipeline should always be selected first.
    /// Uploading beats every other stage.
    /// </summary>
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

    /// <summary>
    /// Stage priority array should contain exactly the expected stages in the correct order.
    /// </summary>
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

        await Assert.That(JobManager.StagePriority).IsEqualTo(expected);
    }
}

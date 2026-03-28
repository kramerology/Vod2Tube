using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

using TwitchVod = Vod2Tube.Domain.TwitchVod;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Tests for <see cref="VideoUploader.ResolveYouTubeAccountIdAsync"/>
/// verifying correct account resolution and skip-when-unassigned behavior.
/// </summary>
public class VideoUploaderAccountResolutionTests
{
    private static AppDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// When the channel has an account assigned,
    /// the method should return the account ID.
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_ChannelHasAccount_ReturnsAccountId()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_ChannelHasAccount_ReturnsAccountId));

        ctx.Channels.Add(new Channel { ChannelName = "streamer1", Active = true, YouTubeAccountId = 5 });
        await ctx.SaveChangesAsync();

        var uploader = new VideoUploader(ctx, null!);
        var result = await uploader.ResolveYouTubeAccountIdAsync("streamer1", CancellationToken.None);

        await Assert.That(result).IsEqualTo(5);
    }

    /// <summary>
    /// When the channel name is null, the method should return null (skip upload).
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_NullChannelName_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_NullChannelName_ReturnsNull));

        var uploader = new VideoUploader(ctx, null!);
        var result = await uploader.ResolveYouTubeAccountIdAsync(null, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// When the channel name is empty, the method should return null (skip upload).
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_EmptyChannelName_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_EmptyChannelName_ReturnsNull));

        var uploader = new VideoUploader(ctx, null!);
        var result = await uploader.ResolveYouTubeAccountIdAsync("", CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// When no matching channel exists, the method should return null (skip upload).
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_ChannelNotFound_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_ChannelNotFound_ReturnsNull));

        var uploader = new VideoUploader(ctx, null!);
        var result = await uploader.ResolveYouTubeAccountIdAsync("unknown", CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// When the channel exists but has no YouTube account assigned, the method
    /// should return null (skip upload).
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_NoAccountAssigned_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_NoAccountAssigned_ReturnsNull));

        ctx.Channels.Add(new Channel { ChannelName = "noaccount", Active = true, YouTubeAccountId = null });
        await ctx.SaveChangesAsync();

        var uploader = new VideoUploader(ctx, null!);
        var result = await uploader.ResolveYouTubeAccountIdAsync("noaccount", CancellationToken.None);

        await Assert.That(result).IsNull();
    }
}

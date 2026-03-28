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
/// verifying correct account resolution and error messages.
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
    /// When the VOD exists and the channel has an account assigned,
    /// the method should return the account ID.
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_ChannelHasAccount_ReturnsAccountId()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_ChannelHasAccount_ReturnsAccountId));

        ctx.Channels.Add(new Channel { ChannelName = "streamer1", Active = true, YouTubeAccountId = 5 });
        ctx.TwitchVods.Add(new TwitchVod { Id = "vod123", ChannelName = "streamer1", Title = "Test", Url = "https://twitch.tv/videos/vod123" });
        await ctx.SaveChangesAsync();

        // Pass null for accountService — ResolveYouTubeAccountIdAsync doesn't use it
        var uploader = new VideoUploader(ctx, null!);
        var result = await uploader.ResolveYouTubeAccountIdAsync("vod123", CancellationToken.None);

        await Assert.That(result).IsEqualTo(5);
    }

    /// <summary>
    /// When the VOD does not exist, the method should throw.
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_VodNotFound_Throws()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_VodNotFound_Throws));

        var uploader = new VideoUploader(ctx, null!);

        await Assert.That(async () => await uploader.ResolveYouTubeAccountIdAsync("missing", CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }

    /// <summary>
    /// When the VOD exists but no matching channel exists, the method should throw.
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_ChannelNotFound_Throws()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_ChannelNotFound_Throws));

        ctx.TwitchVods.Add(new TwitchVod { Id = "vod456", ChannelName = "unknown", Title = "Test", Url = "https://twitch.tv/videos/vod456" });
        await ctx.SaveChangesAsync();

        var uploader = new VideoUploader(ctx, null!);

        await Assert.That(async () => await uploader.ResolveYouTubeAccountIdAsync("vod456", CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }

    /// <summary>
    /// When the channel exists but has no YouTube account assigned, the method should throw.
    /// </summary>
    [Test]
    public async Task ResolveYouTubeAccountIdAsync_NoAccountAssigned_Throws()
    {
        await using var ctx = CreateInMemoryContext(nameof(ResolveYouTubeAccountIdAsync_NoAccountAssigned_Throws));

        ctx.Channels.Add(new Channel { ChannelName = "noaccount", Active = true, YouTubeAccountId = null });
        ctx.TwitchVods.Add(new TwitchVod { Id = "vod789", ChannelName = "noaccount", Title = "Test", Url = "https://twitch.tv/videos/vod789" });
        await ctx.SaveChangesAsync();

        var uploader = new VideoUploader(ctx, null!);

        await Assert.That(async () => await uploader.ResolveYouTubeAccountIdAsync("vod789", CancellationToken.None))
            .ThrowsExactly<InvalidOperationException>();
    }
}

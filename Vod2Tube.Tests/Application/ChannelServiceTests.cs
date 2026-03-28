using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application.Services;
using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ChannelService"/>.
/// An isolated in-memory database is created for every test so tests do not
/// interfere with each other.
/// </summary>
public class ChannelServiceTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a fresh <see cref="AppDbContext"/> backed by an in-memory database
    /// whose name is unique to this test run.
    /// </summary>
    private static AppDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    // =========================================================================
    // AddNewChannelAsync
    // =========================================================================

    /// <summary>
    /// <see cref="ChannelService.AddNewChannelAsync"/> should persist the channel
    /// and return it with a non-zero identity.
    /// </summary>
    [Test]
    public async Task AddNewChannelAsync_ValidChannel_IsPersisted()
    {
        await using var ctx = CreateInMemoryContext(nameof(AddNewChannelAsync_ValidChannel_IsPersisted));
        var service = new ChannelService(ctx);

        var channel = new Channel { ChannelName = "streamer1", Active = true };
        var result = await service.AddNewChannelAsync(channel);

        await Assert.That(result.Id).IsGreaterThan(0);
        await Assert.That(result.ChannelName).IsEqualTo("streamer1");
    }

    /// <summary>
    /// <see cref="ChannelService.AddNewChannelAsync"/> should set
    /// <see cref="Channel.AddedAtUTC"/> automatically to a recent UTC timestamp.
    /// </summary>
    [Test]
    public async Task AddNewChannelAsync_SetsAddedAtUTC()
    {
        await using var ctx = CreateInMemoryContext(nameof(AddNewChannelAsync_SetsAddedAtUTC));
        var service = new ChannelService(ctx);
        var before = DateTime.UtcNow;

        var result = await service.AddNewChannelAsync(new Channel { ChannelName = "streamer2" });

        var after = DateTime.UtcNow;
        await Assert.That(result.AddedAtUTC).IsGreaterThanOrEqualTo(before);
        await Assert.That(result.AddedAtUTC).IsLessThanOrEqualTo(after);
    }

    /// <summary>
    /// Adding two channels should produce different identities.
    /// </summary>
    [Test]
    public async Task AddNewChannelAsync_TwoChannels_HaveDifferentIds()
    {
        await using var ctx = CreateInMemoryContext(nameof(AddNewChannelAsync_TwoChannels_HaveDifferentIds));
        var service = new ChannelService(ctx);

        var c1 = await service.AddNewChannelAsync(new Channel { ChannelName = "alpha" });
        var c2 = await service.AddNewChannelAsync(new Channel { ChannelName = "beta" });

        await Assert.That(c1.Id).IsNotEqualTo(c2.Id);
    }

    // =========================================================================
    // GetChannelByIdAsync
    // =========================================================================

    /// <summary>
    /// <see cref="ChannelService.GetChannelByIdAsync"/> should return the correct
    /// channel when it exists.
    /// </summary>
    [Test]
    public async Task GetChannelByIdAsync_ExistingId_ReturnsChannel()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetChannelByIdAsync_ExistingId_ReturnsChannel));
        var service = new ChannelService(ctx);

        var added = await service.AddNewChannelAsync(new Channel { ChannelName = "finder" });
        var found = await service.GetChannelByIdAsync(added.Id);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.ChannelName).IsEqualTo("finder");
    }

    /// <summary>
    /// <see cref="ChannelService.GetChannelByIdAsync"/> should return null when no
    /// channel with the given id exists.
    /// </summary>
    [Test]
    public async Task GetChannelByIdAsync_MissingId_ReturnsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(GetChannelByIdAsync_MissingId_ReturnsNull));
        var service = new ChannelService(ctx);

        var result = await service.GetChannelByIdAsync(9999);

        await Assert.That(result).IsNull();
    }

    // =========================================================================
    // UpdateChannelAsync
    // =========================================================================

    /// <summary>
    /// <see cref="ChannelService.UpdateChannelAsync"/> should apply the new name and
    /// active flag and return true when the channel exists.
    /// </summary>
    [Test]
    public async Task UpdateChannelAsync_ExistingChannel_UpdatesAndReturnsTrue()
    {
        await using var ctx = CreateInMemoryContext(nameof(UpdateChannelAsync_ExistingChannel_UpdatesAndReturnsTrue));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "old", Active = false });

        var updateResult = await service.UpdateChannelAsync(new Channel
        {
            Id = channel.Id,
            ChannelName = "new",
            Active = true
        });

        var updated = await service.GetChannelByIdAsync(channel.Id);

        await Assert.That(updateResult).IsTrue();
        await Assert.That(updated!.ChannelName).IsEqualTo("new");
        await Assert.That(updated.Active).IsTrue();
    }

    /// <summary>
    /// <see cref="ChannelService.UpdateChannelAsync"/> should return false when no
    /// channel with the given id exists.
    /// </summary>
    [Test]
    public async Task UpdateChannelAsync_MissingChannel_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(UpdateChannelAsync_MissingChannel_ReturnsFalse));
        var service = new ChannelService(ctx);

        var result = await service.UpdateChannelAsync(new Channel { Id = 9999, ChannelName = "ghost" });

        await Assert.That(result).IsFalse();
    }

    /// <summary>
    /// <see cref="ChannelService.UpdateChannelAsync"/> should persist
    /// <see cref="Channel.YouTubeAccountId"/> when it is set to a non-null value.
    /// </summary>
    [Test]
    public async Task UpdateChannelAsync_SetsYouTubeAccountId()
    {
        await using var ctx = CreateInMemoryContext(nameof(UpdateChannelAsync_SetsYouTubeAccountId));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "accttest", Active = true });

        await service.UpdateChannelAsync(new Channel
        {
            Id = channel.Id,
            ChannelName = "accttest",
            Active = true,
            YouTubeAccountId = 42
        });

        var updated = await service.GetChannelByIdAsync(channel.Id);

        await Assert.That(updated!.YouTubeAccountId).IsEqualTo(42);
    }

    /// <summary>
    /// <see cref="ChannelService.UpdateChannelAsync"/> should clear
    /// <see cref="Channel.YouTubeAccountId"/> when it is set back to null.
    /// </summary>
    [Test]
    public async Task UpdateChannelAsync_ClearsYouTubeAccountId()
    {
        await using var ctx = CreateInMemoryContext(nameof(UpdateChannelAsync_ClearsYouTubeAccountId));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "cleartest", Active = true });

        // First set it
        await service.UpdateChannelAsync(new Channel
        {
            Id = channel.Id,
            ChannelName = "cleartest",
            Active = true,
            YouTubeAccountId = 7
        });

        // Now clear it
        await service.UpdateChannelAsync(new Channel
        {
            Id = channel.Id,
            ChannelName = "cleartest",
            Active = true,
            YouTubeAccountId = null
        });

        var updated = await service.GetChannelByIdAsync(channel.Id);

        await Assert.That(updated!.YouTubeAccountId).IsNull();
    }

    /// <summary>
    /// A newly added channel should have <see cref="Channel.YouTubeAccountId"/> as null
    /// by default.
    /// </summary>
    [Test]
    public async Task AddNewChannelAsync_DefaultYouTubeAccountId_IsNull()
    {
        await using var ctx = CreateInMemoryContext(nameof(AddNewChannelAsync_DefaultYouTubeAccountId_IsNull));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "defaultacct" });

        await Assert.That(channel.YouTubeAccountId).IsNull();
    }

    /// <summary>
    /// Updating only the <see cref="Channel.Active"/> flag should leave the channel
    /// name unchanged.
    /// </summary>
    [Test]
    public async Task UpdateChannelAsync_TogglesActiveFlag_LeaveNameUnchanged()
    {
        await using var ctx = CreateInMemoryContext(nameof(UpdateChannelAsync_TogglesActiveFlag_LeaveNameUnchanged));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "stable", Active = true });
        await service.UpdateChannelAsync(new Channel { Id = channel.Id, ChannelName = "stable", Active = false });
        var updated = await service.GetChannelByIdAsync(channel.Id);

        await Assert.That(updated!.ChannelName).IsEqualTo("stable");
        await Assert.That(updated.Active).IsFalse();
    }

    // =========================================================================
    // DeleteChannelAsync
    // =========================================================================

    /// <summary>
    /// <see cref="ChannelService.DeleteChannelAsync"/> should remove the channel from
    /// the store and return true.
    /// </summary>
    [Test]
    public async Task DeleteChannelAsync_ExistingChannel_DeletesAndReturnsTrue()
    {
        await using var ctx = CreateInMemoryContext(nameof(DeleteChannelAsync_ExistingChannel_DeletesAndReturnsTrue));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "deleteme" });
        var deleteResult = await service.DeleteChannelAsync(channel.Id);
        var lookup = await service.GetChannelByIdAsync(channel.Id);

        await Assert.That(deleteResult).IsTrue();
        await Assert.That(lookup).IsNull();
    }

    /// <summary>
    /// <see cref="ChannelService.DeleteChannelAsync"/> should return false when no
    /// channel with the given id exists.
    /// </summary>
    [Test]
    public async Task DeleteChannelAsync_MissingChannel_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(DeleteChannelAsync_MissingChannel_ReturnsFalse));
        var service = new ChannelService(ctx);

        var result = await service.DeleteChannelAsync(9999);

        await Assert.That(result).IsFalse();
    }

    /// <summary>
    /// Deleting a channel a second time should return false (already gone).
    /// </summary>
    [Test]
    public async Task DeleteChannelAsync_AlreadyDeleted_ReturnsFalse()
    {
        await using var ctx = CreateInMemoryContext(nameof(DeleteChannelAsync_AlreadyDeleted_ReturnsFalse));
        var service = new ChannelService(ctx);

        var channel = await service.AddNewChannelAsync(new Channel { ChannelName = "once" });
        await service.DeleteChannelAsync(channel.Id);

        var secondDelete = await service.DeleteChannelAsync(channel.Id);
        await Assert.That(secondDelete).IsFalse();
    }
}

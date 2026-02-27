using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Vod2Tube.Application;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="TwitchGraphQLService"/>.
/// Tests focus on input-validation logic that can be exercised without a live
/// network connection.
/// </summary>
public class TwitchGraphQLServiceTests
{
    // =========================================================================
    // GetAllVodsAsync – argument validation
    // =========================================================================

    /// <summary>
    /// <see cref="TwitchGraphQLService.GetAllVodsAsync"/> should throw
    /// <see cref="ArgumentException"/> when <paramref name="channelLogin"/> is null.
    /// </summary>
    [Test]
    public async Task GetAllVodsAsync_NullChannelLogin_ThrowsArgumentException()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.GetAllVodsAsync(null!))
            .Throws<ArgumentException>();
    }

    /// <summary>
    /// <see cref="TwitchGraphQLService.GetAllVodsAsync"/> should throw
    /// <see cref="ArgumentException"/> when <paramref name="channelLogin"/> is empty.
    /// </summary>
    [Test]
    public async Task GetAllVodsAsync_EmptyChannelLogin_ThrowsArgumentException()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.GetAllVodsAsync(string.Empty))
            .Throws<ArgumentException>();
    }

    /// <summary>
    /// <see cref="TwitchGraphQLService.GetAllVodsAsync"/> should throw
    /// <see cref="ArgumentException"/> when <paramref name="channelLogin"/> is only
    /// whitespace.
    /// </summary>
    [Test]
    public async Task GetAllVodsAsync_WhitespaceChannelLogin_ThrowsArgumentException()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.GetAllVodsAsync("   "))
            .Throws<ArgumentException>();
    }

    /// <summary>
    /// <see cref="TwitchGraphQLService.GetAllVodsAsync"/> should throw
    /// <see cref="ArgumentOutOfRangeException"/> when <paramref name="pageSize"/> is zero.
    /// </summary>
    [Test]
    public async Task GetAllVodsAsync_PageSizeZero_ThrowsArgumentOutOfRangeException()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.GetAllVodsAsync("ninja", pageSize: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// <see cref="TwitchGraphQLService.GetAllVodsAsync"/> should throw
    /// <see cref="ArgumentOutOfRangeException"/> when <paramref name="pageSize"/> is
    /// negative.
    /// </summary>
    [Test]
    public async Task GetAllVodsAsync_NegativePageSize_ThrowsArgumentOutOfRangeException()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.GetAllVodsAsync("ninja", pageSize: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// <see cref="TwitchGraphQLService.GetAllVodsAsync"/> should throw
    /// <see cref="ArgumentOutOfRangeException"/> when <paramref name="pageSize"/> exceeds
    /// the maximum of 100.
    /// </summary>
    [Test]
    public async Task GetAllVodsAsync_PageSizeOver100_ThrowsArgumentOutOfRangeException()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.GetAllVodsAsync("ninja", pageSize: 101))
            .Throws<ArgumentOutOfRangeException>();
    }

    // =========================================================================
    // PopulateVodMomentsAsync – edge cases that do not require network
    // =========================================================================

    /// <summary>
    /// <see cref="TwitchGraphQLService.PopulateVodMomentsAsync"/> should return
    /// immediately without throwing when passed a null list.
    /// </summary>
    [Test]
    public async Task PopulateVodMomentsAsync_NullList_ReturnsWithoutThrowing()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.PopulateVodMomentsAsync(null!))
            .ThrowsNothing();
    }

    /// <summary>
    /// <see cref="TwitchGraphQLService.PopulateVodMomentsAsync"/> should return
    /// immediately without throwing when passed an empty list.
    /// </summary>
    [Test]
    public async Task PopulateVodMomentsAsync_EmptyList_ReturnsWithoutThrowing()
    {
        var service = new TwitchGraphQLService(NullLogger<TwitchGraphQLService>.Instance);

        await Assert.That(async () =>
            await service.PopulateVodMomentsAsync(new List<TwitchVod>()))
            .ThrowsNothing();
    }
}

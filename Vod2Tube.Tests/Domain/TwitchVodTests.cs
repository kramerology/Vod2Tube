using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Domain;

namespace Vod2Tube.Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="TwitchVod"/> domain entity.
/// Verifies default values and property assignment.
/// </summary>
public class TwitchVodTests
{
    /// <summary>
    /// A new <see cref="TwitchVod"/> should initialise all string properties to empty
    /// string rather than null.
    /// </summary>
    [Test]
    public async Task TwitchVod_DefaultStringProperties_AreEmpty()
    {
        var vod = new TwitchVod();

        await Assert.That(vod.Id).IsEqualTo(string.Empty);
        await Assert.That(vod.ChannelName).IsEqualTo(string.Empty);
        await Assert.That(vod.Title).IsEqualTo(string.Empty);
        await Assert.That(vod.Url).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// A new <see cref="TwitchVod"/> should default <see cref="TwitchVod.Duration"/> to
    /// <see cref="TimeSpan.Zero"/>.
    /// </summary>
    [Test]
    public async Task TwitchVod_DefaultDuration_IsZero()
    {
        var vod = new TwitchVod();
        await Assert.That(vod.Duration).IsEqualTo(TimeSpan.Zero);
    }

    /// <summary>
    /// All properties assigned to a <see cref="TwitchVod"/> instance should round-trip
    /// correctly.
    /// </summary>
    [Test]
    public async Task TwitchVod_PropertyAssignment_RoundTrips()
    {
        var createdAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var addedAt   = new DateTime(2024, 6, 2, 8, 30, 0, DateTimeKind.Utc);
        var duration  = TimeSpan.FromHours(3);

        var vod = new TwitchVod
        {
            Id = "123456789",
            ChannelName = "ninjakiwi",
            Title = "Epic Stream",
            CreatedAtUTC = createdAt,
            Duration = duration,
            Url = "https://www.twitch.tv/videos/123456789",
            AddedAtUTC = addedAt
        };

        await Assert.That(vod.Id).IsEqualTo("123456789");
        await Assert.That(vod.ChannelName).IsEqualTo("ninjakiwi");
        await Assert.That(vod.Title).IsEqualTo("Epic Stream");
        await Assert.That(vod.CreatedAtUTC).IsEqualTo(createdAt);
        await Assert.That(vod.Duration).IsEqualTo(duration);
        await Assert.That(vod.Url).IsEqualTo("https://www.twitch.tv/videos/123456789");
        await Assert.That(vod.AddedAtUTC).IsEqualTo(addedAt);
    }

    /// <summary>
    /// <see cref="TwitchVod.Duration"/> built from seconds should equal the expected
    /// <see cref="TimeSpan"/> value.
    /// </summary>
    [Test]
    [Arguments(0,    0,  0,  0)]
    [Arguments(3600, 1,  0,  0)]
    [Arguments(5400, 1, 30,  0)]
    [Arguments(90,   0,  1, 30)]
    public async Task TwitchVod_DurationFromSeconds_MatchesTimeSpan(
        int totalSeconds, int expectedHours, int expectedMinutes, int expectedSeconds)
    {
        var vod = new TwitchVod { Duration = TimeSpan.FromSeconds(totalSeconds) };

        await Assert.That(vod.Duration.Hours).IsEqualTo(expectedHours);
        await Assert.That(vod.Duration.Minutes).IsEqualTo(expectedMinutes);
        await Assert.That(vod.Duration.Seconds).IsEqualTo(expectedSeconds);
    }
}

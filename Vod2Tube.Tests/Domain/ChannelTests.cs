using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Domain;

namespace Vod2Tube.Tests.Domain;

/// <summary>
/// Unit tests for the <see cref="Channel"/> domain entity.
/// Verifies default values, property assignment, and data integrity.
/// </summary>
public class ChannelTests
{
    /// <summary>
    /// A new <see cref="Channel"/> should initialise <see cref="Channel.ChannelName"/>
    /// to an empty string rather than null.
    /// </summary>
    [Test]
    public async Task Channel_DefaultChannelName_IsEmptyString()
    {
        var channel = new Channel();
        await Assert.That(channel.ChannelName).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// A new <see cref="Channel"/> should have <see cref="Channel.Active"/> default to false.
    /// </summary>
    [Test]
    public async Task Channel_DefaultActive_IsFalse()
    {
        var channel = new Channel();
        await Assert.That(channel.Active).IsFalse();
    }

    /// <summary>
    /// A new <see cref="Channel"/> should have <see cref="Channel.Id"/> default to zero
    /// before being persisted.
    /// </summary>
    [Test]
    public async Task Channel_DefaultId_IsZero()
    {
        var channel = new Channel();
        await Assert.That(channel.Id).IsEqualTo(0);
    }

    /// <summary>
    /// Properties assigned to a <see cref="Channel"/> instance should be readable back
    /// with the same values.
    /// </summary>
    [Test]
    public async Task Channel_PropertyAssignment_RoundTrips()
    {
        var now = DateTime.UtcNow;
        var channel = new Channel
        {
            Id = 42,
            ChannelName = "testchannel",
            AddedAtUTC = now,
            Active = true
        };

        await Assert.That(channel.Id).IsEqualTo(42);
        await Assert.That(channel.ChannelName).IsEqualTo("testchannel");
        await Assert.That(channel.AddedAtUTC).IsEqualTo(now);
        await Assert.That(channel.Active).IsTrue();
    }

    /// <summary>
    /// <see cref="Channel.ChannelName"/> should accept and return Unicode content.
    /// </summary>
    [Test]
    public async Task Channel_ChannelName_AcceptsUnicode()
    {
        var channel = new Channel { ChannelName = "チャンネル🎮" };
        await Assert.That(channel.ChannelName).IsEqualTo("チャンネル🎮");
    }
}

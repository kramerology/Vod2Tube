using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="TwitchDownloadService"/>.
/// Because the download methods depend on external executables (TwitchDownloaderCLI
/// and ffprobe), only the pure helper logic that can be exercised in isolation is
/// tested here.
/// </summary>
public class TwitchDownloadServiceTests
{
    // =========================================================================
    // ParseFps – happy-path cases
    // =========================================================================

    /// <summary>
    /// A fractional FPS string such as "30000/1001" (NTSC 29.97) should be parsed
    /// to the correct double value.
    /// </summary>
    [Test]
    public async Task ParseFps_FractionalNtsc_ReturnsCorrectValue()
    {
        double fps = TwitchDownloadService.ParseFps("30000/1001");

        // 30000 / 1001 ≈ 29.970029…
        await Assert.That(fps).IsGreaterThan(29.96);
        await Assert.That(fps).IsLessThan(29.98);
    }

    /// <summary>
    /// The common "24000/1001" cinema fraction should resolve to roughly 23.976 fps.
    /// </summary>
    [Test]
    public async Task ParseFps_Fractional24000_1001_ReturnsCorrectValue()
    {
        double fps = TwitchDownloadService.ParseFps("24000/1001");

        await Assert.That(fps).IsGreaterThan(23.97);
        await Assert.That(fps).IsLessThan(23.98);
    }

    /// <summary>
    /// A whole-number FPS string like "60/1" should parse to exactly 60.
    /// </summary>
    [Test]
    public async Task ParseFps_FractionalWholeNumber_ReturnsExactValue()
    {
        double fps = TwitchDownloadService.ParseFps("60/1");

        await Assert.That(fps).IsEqualTo(60.0);
    }

    /// <summary>
    /// A plain decimal string such as "30" should parse to 30.
    /// </summary>
    [Test]
    public async Task ParseFps_PlainDecimal_ReturnsCorrectValue()
    {
        double fps = TwitchDownloadService.ParseFps("30");

        await Assert.That(fps).IsEqualTo(30.0);
    }

    /// <summary>
    /// A decimal with fraction notation like "60.0" should parse correctly.
    /// </summary>
    [Test]
    public async Task ParseFps_DecimalWithFraction_ReturnsCorrectValue()
    {
        double fps = TwitchDownloadService.ParseFps("60.0");

        await Assert.That(fps).IsEqualTo(60.0);
    }

    // =========================================================================
    // ParseFps – error cases
    // =========================================================================

    /// <summary>
    /// An empty string should cause <see cref="TwitchDownloadService.ParseFps"/> to
    /// throw a <see cref="FormatException"/>.
    /// </summary>
    [Test]
    public async Task ParseFps_EmptyString_ThrowsFormatException()
    {
        await Assert.That(() => TwitchDownloadService.ParseFps(string.Empty))
            .Throws<FormatException>();
    }

    /// <summary>
    /// A non-numeric string should cause <see cref="TwitchDownloadService.ParseFps"/> to
    /// throw a <see cref="FormatException"/>.
    /// </summary>
    [Test]
    public async Task ParseFps_NonNumericString_ThrowsFormatException()
    {
        await Assert.That(() => TwitchDownloadService.ParseFps("abc"))
            .Throws<FormatException>();
    }

    /// <summary>
    /// A fractional string whose numerator is non-numeric should throw
    /// <see cref="FormatException"/>.
    /// </summary>
    [Test]
    public async Task ParseFps_FractionWithNonNumericNumerator_ThrowsFormatException()
    {
        await Assert.That(() => TwitchDownloadService.ParseFps("abc/1001"))
            .Throws<FormatException>();
    }

    /// <summary>
    /// A fractional string whose denominator is non-numeric should throw
    /// <see cref="FormatException"/>.
    /// </summary>
    [Test]
    public async Task ParseFps_FractionWithNonNumericDenominator_ThrowsFormatException()
    {
        await Assert.That(() => TwitchDownloadService.ParseFps("30000/xyz"))
            .Throws<FormatException>();
    }

    /// <summary>
    /// Dividing by zero in a fractional string should result in positive or negative
    /// infinity rather than throwing, matching the natural behaviour of double division.
    /// The test documents this edge case explicitly.
    /// </summary>
    [Test]
    public async Task ParseFps_FractionWithZeroDenominator_ReturnsInfinity()
    {
        double fps = TwitchDownloadService.ParseFps("30/0");

        await Assert.That(double.IsInfinity(fps)).IsTrue();
    }
}

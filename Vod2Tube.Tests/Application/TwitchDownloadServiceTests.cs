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

    // =========================================================================
    // SelectVideoEncoder – encoder priority tests
    // =========================================================================

    /// <summary>
    /// When h264_amf is listed, it should be preferred over all other encoders.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_AmfPresent_ReturnsAmf()
    {
        string encoder = TwitchDownloadService.SelectVideoEncoder("h264_amf h264_nvenc h264_qsv libx264");

        await Assert.That(encoder).IsEqualTo("h264_amf");
    }

    /// <summary>
    /// When h264_amf is absent but h264_nvenc is present, NVENC should be selected.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_NvencPresent_NoAmf_ReturnsNvenc()
    {
        string encoder = TwitchDownloadService.SelectVideoEncoder("h264_nvenc h264_qsv libx264");

        await Assert.That(encoder).IsEqualTo("h264_nvenc");
    }

    /// <summary>
    /// When only h264_qsv is a hardware option, QSV should be selected.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_QsvPresent_NoAmfOrNvenc_ReturnsQsv()
    {
        string encoder = TwitchDownloadService.SelectVideoEncoder("h264_qsv libx264");

        await Assert.That(encoder).IsEqualTo("h264_qsv");
    }

    /// <summary>
    /// When no hardware encoders are present, libx264 should be used as the fallback.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_NoHardwareEncoders_ReturnsLibx264()
    {
        string encoder = TwitchDownloadService.SelectVideoEncoder("libx264 libx265 aac");

        await Assert.That(encoder).IsEqualTo("libx264");
    }

    /// <summary>
    /// An empty encoder list should fall back to libx264.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_EmptyString_ReturnsLibx264()
    {
        string encoder = TwitchDownloadService.SelectVideoEncoder(string.Empty);

        await Assert.That(encoder).IsEqualTo("libx264");
    }

    /// <summary>
    /// Priority is enforced regardless of the order in which encoders appear in the
    /// ffmpeg output: h264_amf must beat h264_nvenc even when nvenc is listed first.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_AmfAfterNvenc_StillReturnsAmf()
    {
        string encoder = TwitchDownloadService.SelectVideoEncoder("h264_nvenc h264_amf");

        await Assert.That(encoder).IsEqualTo("h264_amf");
    }

    /// <summary>
    /// Partial matches like 'libh264_amf_compat' must not trigger the h264_amf rule.
    /// </summary>
    [Test]
    public async Task SelectVideoEncoder_PartialMatchInLongerName_DoesNotMatch()
    {
        // "libh264_amf_compat" contains "h264_amf" as a substring but not as a whole word.
        string encoder = TwitchDownloadService.SelectVideoEncoder("libh264_amf_compat");

        await Assert.That(encoder).IsEqualTo("libx264");
    }

    // =========================================================================
    // IsValidVodId – accepted formats
    // =========================================================================

    /// <summary>
    /// A plain numeric VOD ID (the most common Twitch format) should be accepted.
    /// </summary>
    [Test]
    public async Task IsValidVodId_NumericId_ReturnsTrue()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId("2345678901")).IsTrue();
    }

    /// <summary>
    /// An alphanumeric ID with underscores and hyphens is valid.
    /// </summary>
    [Test]
    public async Task IsValidVodId_AlphanumericWithUnderscoreAndHyphen_ReturnsTrue()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId("abc_123-XYZ")).IsTrue();
    }

    // =========================================================================
    // IsValidVodId – rejected formats
    // =========================================================================

    /// <summary>
    /// A null VOD ID must be rejected.
    /// </summary>
    [Test]
    public async Task IsValidVodId_Null_ReturnsFalse()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId(null)).IsFalse();
    }

    /// <summary>
    /// An empty string must be rejected.
    /// </summary>
    [Test]
    public async Task IsValidVodId_Empty_ReturnsFalse()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId(string.Empty)).IsFalse();
    }

    /// <summary>
    /// A whitespace-only string must be rejected.
    /// </summary>
    [Test]
    public async Task IsValidVodId_Whitespace_ReturnsFalse()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId("   ")).IsFalse();
    }

    /// <summary>
    /// A VOD ID containing a space must be rejected because spaces would break the URL.
    /// </summary>
    [Test]
    public async Task IsValidVodId_ContainsSpace_ReturnsFalse()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId("123 456")).IsFalse();
    }

    /// <summary>
    /// A VOD ID containing a URL-special character (slash) must be rejected.
    /// </summary>
    [Test]
    public async Task IsValidVodId_ContainsSlash_ReturnsFalse()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId("123/456")).IsFalse();
    }

    /// <summary>
    /// A VOD ID containing shell-special characters must be rejected.
    /// </summary>
    [Test]
    public async Task IsValidVodId_ContainsSpecialChars_ReturnsFalse()
    {
        await Assert.That(TwitchDownloadService.IsValidVodId("123;rm -rf /")).IsFalse();
    }

    // =========================================================================
    // SegmentLength – configuration sanity check
    // =========================================================================

    /// <summary>
    /// <see cref="TwitchDownloadService.SegmentLength"/> should be exactly 5 minutes.
    /// This test documents the expected segment granularity for resumable rendering.
    /// </summary>
    [Test]
    public async Task SegmentLength_IsFiveMinutes()
    {
        await Assert.That(TwitchDownloadService.SegmentLength).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    // =========================================================================
    // Segment count calculation
    // =========================================================================

    /// <summary>
    /// A video shorter than one segment should produce exactly one segment.
    /// </summary>
    [Test]
    public async Task SegmentCount_DurationLessThanOneSegment_ReturnsOne()
    {
        double totalSeconds = 120; // 2 minutes
        int count = (int)Math.Ceiling(totalSeconds / TwitchDownloadService.SegmentLength.TotalSeconds);

        await Assert.That(count).IsEqualTo(1);
    }

    /// <summary>
    /// A video whose duration is an exact multiple of the segment length should
    /// produce the expected number of segments with no extra empty segment.
    /// </summary>
    [Test]
    public async Task SegmentCount_ExactMultiple_ReturnsExactCount()
    {
        double totalSeconds = TwitchDownloadService.SegmentLength.TotalSeconds * 3; // 15 minutes
        int count = (int)Math.Ceiling(totalSeconds / TwitchDownloadService.SegmentLength.TotalSeconds);

        await Assert.That(count).IsEqualTo(3);
    }

    /// <summary>
    /// A video with a remainder beyond the last full segment should produce one
    /// extra segment for the tail.
    /// </summary>
    [Test]
    public async Task SegmentCount_WithRemainder_ProducesExtraSegment()
    {
        double totalSeconds = TwitchDownloadService.SegmentLength.TotalSeconds * 2 + 30; // 10:30
        int count = (int)Math.Ceiling(totalSeconds / TwitchDownloadService.SegmentLength.TotalSeconds);

        await Assert.That(count).IsEqualTo(3);
    }
}

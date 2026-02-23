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
}

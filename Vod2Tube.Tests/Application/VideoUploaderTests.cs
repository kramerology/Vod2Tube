using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Vod2Tube.Application;

namespace Vod2Tube.Tests.Application;

/// <summary>
/// Unit tests for <see cref="VideoUploader.SanitizeString"/> and the
/// <see cref="VideoUploaderOptions"/> defaults.
/// </summary>
public class VideoUploaderTests
{
    // =========================================================================
    // VideoUploaderOptions — default values
    // =========================================================================

    [Test]
    public async Task VideoUploaderOptions_DefaultCategory_IsGamingCategory()
    {
        var options = new VideoUploaderOptions();
        await Assert.That(options.Category).IsEqualTo("20");
    }

    [Test]
    public async Task VideoUploaderOptions_DefaultPrivacyStatus_IsPrivate()
    {
        var options = new VideoUploaderOptions();
        await Assert.That(options.PrivacyStatus).IsEqualTo("private");
    }

    [Test]
    public async Task VideoUploaderOptions_DefaultMadeForKids_IsFalse()
    {
        var options = new VideoUploaderOptions();
        await Assert.That(options.MadeForKids).IsFalse();
    }

    [Test]
    public async Task VideoUploaderOptions_DefaultTags_IsEmpty()
    {
        var options = new VideoUploaderOptions();
        await Assert.That(options.Tags.Count).IsEqualTo(0);
    }

    [Test]
    public async Task VideoUploaderOptions_DefaultTitle_IsEmptyString()
    {
        var options = new VideoUploaderOptions();
        await Assert.That(options.Title).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task VideoUploaderOptions_DefaultDescription_IsEmptyString()
    {
        var options = new VideoUploaderOptions();
        await Assert.That(options.Description).IsEqualTo(string.Empty);
    }

    // =========================================================================
    // SanitizeString — null / empty / whitespace
    // =========================================================================

    /// <summary>
    /// An empty string should be replaced with the "Untitled Video" fallback.
    /// </summary>
    [Test]
    public async Task SanitizeString_EmptyString_ReturnsUntitledVideo()
    {
        await Assert.That(VideoUploader.SanitizeString(string.Empty)).IsEqualTo("Untitled Video");
    }

    /// <summary>
    /// A whitespace-only string should be replaced with the "Untitled Video" fallback.
    /// </summary>
    [Test]
    public async Task SanitizeString_WhitespaceOnly_ReturnsUntitledVideo()
    {
        await Assert.That(VideoUploader.SanitizeString("   ")).IsEqualTo("Untitled Video");
    }

    // =========================================================================
    // SanitizeString — emoji / non-Latin removal
    // =========================================================================

    /// <summary>
    /// Emoji characters should be stripped from the title.
    /// </summary>
    [Test]
    public async Task SanitizeString_TitleWithEmoji_RemovesEmoji()
    {
        var result = VideoUploader.SanitizeString("Epic Stream 🎮🔥");
        await Assert.That(result).IsEqualTo("Epic Stream");
    }

    /// <summary>
    /// A title that consists entirely of emoji should fall back to "Untitled Video".
    /// </summary>
    [Test]
    public async Task SanitizeString_EmojiOnlyTitle_ReturnsUntitledVideo()
    {
        var result = VideoUploader.SanitizeString("🎮🔥💯");
        await Assert.That(result).IsEqualTo("Untitled Video");
    }

    // =========================================================================
    // SanitizeString — disallowed characters (< >)
    // =========================================================================

    /// <summary>
    /// Angle brackets that YouTube disallows should be removed.
    /// </summary>
    [Test]
    public async Task SanitizeString_AngleBrackets_AreRemoved()
    {
        var result = VideoUploader.SanitizeString("Best <Stream> Ever");
        await Assert.That(result).IsEqualTo("Best Stream Ever");
    }

    /// <summary>
    /// A title that becomes empty after angle bracket removal should fall back to "Untitled Video".
    /// </summary>
    [Test]
    public async Task SanitizeString_OnlyAngleBrackets_ReturnsUntitledVideo()
    {
        var result = VideoUploader.SanitizeString("<>");
        await Assert.That(result).IsEqualTo("Untitled Video");
    }
    // =========================================================================
    // SanitizeString — whitespace normalisation
    // =========================================================================

    /// <summary>
    /// Multiple consecutive spaces should be collapsed to a single space.
    /// </summary>
    [Test]
    public async Task SanitizeString_MultipleSpaces_CollapsedToOne()
    {
        var result = VideoUploader.SanitizeString("Epic   Stream");
        await Assert.That(result).IsEqualTo("Epic Stream");
    }

    /// <summary>
    /// Leading and trailing whitespace should be trimmed.
    /// </summary>
    [Test]
    public async Task SanitizeString_LeadingTrailingSpaces_AreTrimmed()
    {
        var result = VideoUploader.SanitizeString("  Epic Stream  ");
        await Assert.That(result).IsEqualTo("Epic Stream");
    }

    // =========================================================================
    // SanitizeString — length capping
    // =========================================================================

    /// <summary>
    /// A title longer than 100 characters should be truncated to at most 100
    /// characters (TrimEnd may produce a shorter string if the 100th character
    /// is a trailing space).
    /// </summary>
    [Test]
    public async Task SanitizeString_TitleOver100Chars_TruncatedTo100()
    {
        var longTitle = new string('A', 150);
        var result = VideoUploader.SanitizeString(longTitle);
        await Assert.That(result.Length).IsEqualTo(100);
    }

    /// <summary>
    /// A title of exactly 100 characters should be returned unchanged.
    /// </summary>
    [Test]
    public async Task SanitizeString_TitleExactly100Chars_ReturnedUnchanged()
    {
        var title = new string('B', 100);
        var result = VideoUploader.SanitizeString(title);
        await Assert.That(result).IsEqualTo(title);
    }

    // =========================================================================
    // SanitizeString — normal / happy-path titles
    // =========================================================================

    /// <summary>
    /// A plain ASCII title should pass through without modification.
    /// </summary>
    [Test]
    public async Task SanitizeString_NormalAsciiTitle_ReturnedUnchanged()
    {
        var result = VideoUploader.SanitizeString("Awesome Gaming Stream");
        await Assert.That(result).IsEqualTo("Awesome Gaming Stream");
    }

    /// <summary>
    /// Extended Latin-1 characters (e.g. accented letters) should be preserved.
    /// </summary>
    [Test]
    public async Task SanitizeString_Latin1Characters_ArePreserved()
    {
        var result = VideoUploader.SanitizeString("Ñoño café");
        await Assert.That(result).IsEqualTo("Ñoño café");
    }
}

namespace Vod2Tube.Application;

/// <summary>
/// Shared helpers used by pipeline workers to validate and create
/// user-configured storage directories.
/// </summary>
internal static class WorkerPaths
{
    /// <summary>
    /// Validates that <paramref name="path"/> is non-empty/non-whitespace, then
    /// creates the directory (including any missing ancestors) and returns the
    /// resolved absolute path.  Throws <see cref="InvalidOperationException"/>
    /// with a clear message when the path is empty so that a misconfigured
    /// settings value surfaces as an actionable error rather than a cryptic BCL
    /// exception inside <see cref="System.IO.Directory.CreateDirectory"/>.
    /// </summary>
    internal static string EnsureDirectory(string path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                $"The '{settingName}' storage path is not configured. " +
                "Please set it to a valid directory path in Settings before starting the pipeline.");

        Directory.CreateDirectory(path);
        return path;
    }
}

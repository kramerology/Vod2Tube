using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using Vod2Tube.Application.Models;

namespace Vod2Tube.Application.PipelineWorkers
{
    /// <summary>
    /// Copies pipeline output files to their configured archive directories
    /// (based on the archive settings) and deletes files that are not being
    /// archived from working storage.
    /// </summary>
    public class Archiver
    {
        private readonly IOptionsSnapshot<AppSettings> _options;
        private readonly ILogger<Archiver> _logger;

        // Buffer size for file copies: 4 MiB gives good throughput.
        private const int CopyBufferSize = 4 * 1024 * 1024;

        public Archiver(IOptionsSnapshot<AppSettings> options, ILogger<Archiver> logger)
        {
            _options = options;
            _logger = logger;
        }










        /// <summary>
        /// Returns the archive destination paths that were actually written to disk during
        /// the most recent <see cref="RunAsync"/> call.  A non-empty path is returned for a
        /// given artifact only when <em>all three</em> of the following are true:
        /// <list type="bullet">
        ///   <item><description>archiving is enabled for that artifact type in <see cref="AppSettings"/>,</description></item>
        ///   <item><description>the corresponding archive directory is configured, and</description></item>
        ///   <item><description>the destination file exists on disk (i.e. the copy succeeded).</description></item>
        /// </list>
        /// For any artifact that does not satisfy all three conditions the corresponding
        /// string is empty, so callers and the UI can safely treat a non-empty value as a
        /// confirmed, accessible archive path.
        /// </summary>
        public ArchiveResult ComputeArchivePaths(
            string? vodFilePath,
            string? chatTextFilePath,
            string? chatVideoFilePath,
            string? finalVideoFilePath)
        {
            var s = _options.Value;
            return new ArchiveResult
            {
                ArchivedVodPath        = ResolveArchivedPath(s.ArchiveVodEnabled,        s.ArchiveVodDir,        vodFilePath),
                ArchivedChatJsonPath   = ResolveArchivedPath(s.ArchiveChatJsonEnabled,   s.ArchiveChatJsonDir,   chatTextFilePath),
                ArchivedChatRenderPath = ResolveArchivedPath(s.ArchiveChatRenderEnabled, s.ArchiveChatRenderDir, chatVideoFilePath),
                ArchivedFinalVideoPath = ResolveArchivedPath(s.ArchiveFinalVideoEnabled, s.ArchiveFinalVideoDir, finalVideoFilePath),
            };
        }

        /// <summary>
        /// Returns the archive destination path only when archiving is enabled, a directory
        /// is configured, a source path was set, AND the destination file actually exists on
        /// disk.  Returning an empty string for any other case prevents the UI from
        /// displaying a file-explorer button for a file that was never archived.
        /// </summary>
        private static string ResolveArchivedPath(bool enabled, string? archiveDir, string? sourceFilePath)
        {
            if (!enabled || string.IsNullOrWhiteSpace(archiveDir) || string.IsNullOrEmpty(sourceFilePath))
                return string.Empty;
            var dest = Path.Combine(archiveDir, Path.GetFileName(sourceFilePath));
            return File.Exists(dest) ? dest : string.Empty;
        }

        /// <summary>
        /// Archives enabled files and deletes all intermediate pipeline files.
        /// Yields <see cref="ProgressStatus"/> updates as each file is processed.
        /// </summary>
        public async IAsyncEnumerable<ProgressStatus> RunAsync(
            string vodId,
            string? vodFilePath,
            string? chatTextFilePath,
            string? chatVideoFilePath,
            string? finalVideoFilePath,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var s = _options.Value;

            // Build a list of (sourcePath, archiveDir, label) for every file that exists.
            // archiveDir is non-null only when archiving is enabled and configured.
            var files = new List<(string source, string? archiveDir, string label)>();

            if (!string.IsNullOrEmpty(vodFilePath) && File.Exists(vodFilePath))
                files.Add((vodFilePath,
                    s.ArchiveVodEnabled && !string.IsNullOrWhiteSpace(s.ArchiveVodDir) ? s.ArchiveVodDir : null,
                    "VOD download"));

            if (!string.IsNullOrEmpty(chatTextFilePath) && File.Exists(chatTextFilePath))
                files.Add((chatTextFilePath,
                    s.ArchiveChatJsonEnabled && !string.IsNullOrWhiteSpace(s.ArchiveChatJsonDir) ? s.ArchiveChatJsonDir : null,
                    "Chat JSON"));

            if (!string.IsNullOrEmpty(chatVideoFilePath) && File.Exists(chatVideoFilePath))
                files.Add((chatVideoFilePath,
                    s.ArchiveChatRenderEnabled && !string.IsNullOrWhiteSpace(s.ArchiveChatRenderDir) ? s.ArchiveChatRenderDir : null,
                    "Chat render"));

            if (!string.IsNullOrEmpty(finalVideoFilePath) && File.Exists(finalVideoFilePath))
                files.Add((finalVideoFilePath,
                    s.ArchiveFinalVideoEnabled && !string.IsNullOrWhiteSpace(s.ArchiveFinalVideoDir) ? s.ArchiveFinalVideoDir : null,
                    "Final video"));

            if (files.Count == 0)
            {
                yield return ProgressStatus.Indeterminate("No files to archive or clean up");
                yield break;
            }

            // Total bytes to copy (only files that will actually be copied).
            long totalArchiveBytes = files
                .Where(f => f.archiveDir != null)
                .Sum(f => new FileInfo(f.source).Length);

            long archivedBytes = 0;
            var startTime = DateTime.UtcNow;

            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var (source, archiveDir, label) = files[i];

                if (archiveDir != null)
                {
                    // Ensure the destination directory exists.
                    Directory.CreateDirectory(archiveDir);
                    string dest = Path.Combine(archiveDir, Path.GetFileName(source));
                    // Write to a temp file so that a crash/cancellation mid-copy never
                    // leaves a partially-written file at the final destination path.
                    string tmp = dest + ".tmp";

                    _logger.LogInformation("Archiving {Label} for job {VodId}: {Source} → {Dest}",
                        label, vodId, source, dest);

                    long fileSize = new FileInfo(source).Length;
                    long fileBytesWritten = 0;

                    // Explicit scope block so the streams are flushed and closed
                    // before the atomic rename below.
                    {
                        await using var srcStream = new FileStream(
                            source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
                        await using var dstStream = new FileStream(
                            tmp, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

                        var buffer = new byte[CopyBufferSize];
                        int bytesRead;
                        while ((bytesRead = await srcStream.ReadAsync(buffer, ct)) > 0)
                        {
                            await dstStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                            fileBytesWritten += bytesRead;

                            long totalDone = archivedBytes + fileBytesWritten;
                            double pct = totalArchiveBytes > 0 ? (double)totalDone / totalArchiveBytes * 100.0 : 0;

                            double? etaMinutes = null;
                            double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                            if (elapsed > 1 && totalDone > 0 && totalArchiveBytes > totalDone)
                            {
                                double bytesPerSec = totalDone / elapsed;
                                etaMinutes = (totalArchiveBytes - totalDone) / bytesPerSec / 60.0;
                            }

                            yield return ProgressStatus.WithProgress(
                                $"Archiving {label}: {FormatBytes(fileBytesWritten)} / {FormatBytes(fileSize)}",
                                pct,
                                etaMinutes);
                        }
                    } // srcStream and dstStream are flushed and closed here

                    // Atomically promote the temp file to the final destination,
                    // overwriting any pre-existing file at that path.
                    File.Move(tmp, dest, overwrite: true);
                    archivedBytes += fileSize;
                    _logger.LogInformation("Archived {Label} for job {VodId} to {Dest}", label, vodId, dest);
                }

                // Delete the source from working storage (whether or not it was archived).
                try
                {
                    File.Delete(source);
                    _logger.LogInformation("Deleted working copy of {Label} for job {VodId}: {Source}", label, vodId, source);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete working copy of {Label} for job {VodId}: {Source}", label, vodId, source);
                }

                // Yield a progress update after each file is handled.
                double overallPct = totalArchiveBytes > 0 ? (double)archivedBytes / totalArchiveBytes * 100.0 : 100.0;
                yield return ProgressStatus.WithProgress(
                    archiveDir != null ? $"Archived {label}" : $"Cleaned up {label}",
                    overallPct);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576L)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024L)         return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }
    }

    /// <summary>
    /// Holds the archive destination paths produced by <see cref="Archiver"/>.
    /// An empty string means that item was not archived (either archiving was disabled
    /// or no source file existed).
    /// </summary>
    public sealed record ArchiveResult
    {
        public string ArchivedVodPath { get; init; } = string.Empty;
        public string ArchivedChatJsonPath { get; init; } = string.Empty;
        public string ArchivedChatRenderPath { get; init; } = string.Empty;
        public string ArchivedFinalVideoPath { get; init; } = string.Empty;
    }
}

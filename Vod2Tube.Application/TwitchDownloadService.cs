using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vod2Tube.Application.Models;

namespace Vod2Tube.Application
{
    public class TwitchDownloadService
    {
        private readonly ILogger<TwitchDownloadService> _logger;
        private readonly IOptions<AppSettings> _options;
        private readonly Lazy<string> _cachedEncoder;

        private AppSettings Settings => _options.Value;

        public TwitchDownloadService(ILogger<TwitchDownloadService> logger, IOptions<AppSettings> options)
        {
            _logger = logger;
            _options = options;
            _cachedEncoder = new Lazy<string>(DetectVideoEncoder);
        }

        private string RunProcessWithOutput(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                _logger.LogError("Process '{FileName} {Arguments}' failed with exit code {ExitCode}. {Error}",
                    fileName, arguments, process.ExitCode, error);
                Environment.Exit(process.ExitCode);
            }

            return output;
        }

        internal static double ParseFps(string fpsStr)
        {
            if (fpsStr.Contains("/"))
            {
                var parts = fpsStr.Split('/');
                if (double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den))
                {
                    return num / den;
                }
            }
            else if (double.TryParse(fpsStr, out var val))
            {
                return val;
            }

            throw new FormatException($"Unable to parse FPS: {fpsStr}");
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="vodId"/> is a non-empty
        /// string consisting only of alphanumeric characters, underscores, and hyphens —
        /// the set of characters valid in a Twitch VOD ID.
        /// </summary>
        internal static bool IsValidVodId(string? vodId) =>
            !string.IsNullOrWhiteSpace(vodId) && Regex.IsMatch(vodId, @"^[a-zA-Z0-9_-]+$");

        /// <summary>
        /// Estimates the minutes remaining for a segmented operation given elapsed time and progress so far.
        /// Returns <c>null</c> when no segments have completed yet.
        /// </summary>
        internal static double? EstimateMinutesRemaining(Stopwatch elapsed, int completedItems, int totalItems)
        {
            if (completedItems <= 0 || totalItems <= 0)
                return null;
            int remaining = totalItems - completedItems;
            if (remaining <= 0)
                return 0;
            double minutesPerItem = elapsed.Elapsed.TotalMinutes / completedItems;
            return minutesPerItem * remaining;
        }

        /// <summary>
        /// Parses yt-dlp percentage and ETA from a progress line like
        /// <c>10.0% of 1.20GiB at 5.00MiB/s ETA 00:03:45 (frag 10/100)</c>.
        /// </summary>
        internal static (double? percent, double? etaMinutes) ParseYtDlpProgress(string progressLine)
        {
            double? percent = null;
            double? etaMinutes = null;

            var pctMatch = Regex.Match(progressLine, @"([\d.]+)%");
            if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                percent = pct;

            var etaMatch = Regex.Match(progressLine, @"ETA\s+(\d+):(\d+):(\d+)");
            if (etaMatch.Success)
            {
                int h = int.Parse(etaMatch.Groups[1].Value);
                int m = int.Parse(etaMatch.Groups[2].Value);
                int s = int.Parse(etaMatch.Groups[3].Value);
                etaMinutes = h * 60 + m + s / 60.0;
            }
            else
            {
                var etaShortMatch = Regex.Match(progressLine, @"ETA\s+(\d+):(\d+)");
                if (etaShortMatch.Success)
                {
                    int m = int.Parse(etaShortMatch.Groups[1].Value);
                    int s = int.Parse(etaShortMatch.Groups[2].Value);
                    etaMinutes = m + s / 60.0;
                }
            }

            return (percent, etaMinutes);
        }

        /// <summary>
        /// Parses TwitchDownloaderCLI chat download progress from a line like
        /// <c>Downloading 50.00% [250/500]</c>.
        /// </summary>
        internal static double? ParseChatDownloadPercent(string statusText)
        {
            var pctMatch = Regex.Match(statusText, @"([\d.]+)%");
            if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                return pct;
            return null;
        }


        /// <summary>
        /// Renders the chat JSON file into an MP4 video using TwitchDownloaderCLI.
        /// The render is split into 5-minute segments so that an interrupted render can be
        /// resumed without re-rendering segments that already exist on disk.  All completed
        /// segments are concatenated into <paramref name="finalFile"/> at the end.
        /// </summary>
        public async IAsyncEnumerable<ProgressStatus> RenderChatVideoAsync(
            FileInfo chatFile,
            FileInfo vodFile,
            DirectoryInfo tempDir,
            FileInfo finalFile,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(chatFile.FullName))
                throw new FileNotFoundException($"Chat file not found: {chatFile.FullName}");
            if (!File.Exists(vodFile.FullName))
                throw new FileNotFoundException($"VOD file not found: {vodFile.FullName}");

            int chatWidth  = Settings.ChatWidth;
            int fontSize   = Settings.ChatFontSize;
            int updateRate = Settings.ChatUpdateRate;
            string fpsStr  = RunProcessWithOutput(Settings.FfprobePath, $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 {vodFile.FullName}").Trim();
            string height  = RunProcessWithOutput(Settings.FfprobePath, $"-v error -select_streams v:0 -show_entries stream=height -of default=noprint_wrappers=1:nokey=1 {vodFile.FullName}").Trim();
            double fps     = ParseFps(fpsStr);

            double totalDuration = GetVideoDuration(vodFile.FullName);
            if (totalDuration <= 0)
                throw new InvalidOperationException($"Could not determine a valid duration for VOD file: {vodFile.FullName}");

            // Estimate the total number of segments for display purposes only.
            // Actual iteration is driven by time so the concat list exactly matches
            // the segments that were rendered/retained — no early-break truncation risk.
            int estimatedSegmentCount = (int)Math.Ceiling(totalDuration / SegmentLength.TotalSeconds);

            // Segments are stored alongside the output file in a dedicated subdirectory.
            string segmentsDir = Path.Combine(
                finalFile.DirectoryName ?? ".",
                Path.GetFileNameWithoutExtension(finalFile.Name) + "_chat_segments");
            Directory.CreateDirectory(segmentsDir);

            yield return ProgressStatus.WithProgress($"Rendering chat in {estimatedSegmentCount} segment(s)", 0);

            var segmentFiles = new List<string>();
            var renderStopwatch = Stopwatch.StartNew();

            const int maxSegmentAttempts = 3;
            int segmentIndex = 0;
            for (double startSec = 0; startSec < totalDuration; startSec += SegmentLength.TotalSeconds, segmentIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double segDuration = Math.Min(SegmentLength.TotalSeconds, totalDuration - startSec);

                // Defensive guard: the loop condition (startSec < totalDuration) ensures segDuration > 0
                // in all normal cases, but a tiny floating-point overshoot in the accumulator could
                // produce a zero or negative value.  Stop early rather than passing an invalid duration
                // to TwitchDownloaderCLI.  Because this check is before segmentFiles.Add the concat
                // list will not contain a path for this non-existent segment.
                if (segDuration <= 0)
                {
                    _logger.LogDebug(
                        "Computed non-positive segment duration {Duration} at index {Index}; stopping chat render loop.",
                        segDuration, segmentIndex);
                    break;
                }

                int displayNum     = segmentIndex + 1;

                string segmentFile    = Path.Combine(segmentsDir, $"segment_{segmentIndex:D4}.mp4");
                // Use ".part.mp4" (not ".mp4.tmp") so the final extension stays ".mp4".
                // TwitchDownloaderCLI's default --output-args has no explicit "-f" flag, so
                // FFmpeg infers the container from the file extension.  A ".tmp" extension
                // causes FFmpeg to exit immediately with "Unable to find a suitable output
                // format", which closes its stdin pipe and produces the
                // "The pipe has been ended." IOException inside TwitchDownloaderCLI.
                string segmentTmpFile = Path.Combine(segmentsDir, $"segment_{segmentIndex:D4}.part.mp4");
                segmentFiles.Add(segmentFile);

                if (File.Exists(segmentFile))
                {
                    _logger.LogInformation("Chat segment {Index}/{Total} already exists, skipping", displayNum, estimatedSegmentCount);
                    yield return ProgressStatus.WithProgress(
                        $"Chat segment {displayNum}/{estimatedSegmentCount} already rendered, skipping",
                        (double)displayNum / estimatedSegmentCount * 100);
                    continue;
                }

                // TwitchDownloaderCLI accepts times as "{value}s" for seconds.
                string beginningArg = FormatSegmentTimeArg(startSec);
                string endingArg    = FormatSegmentTimeArg(startSec + segDuration);

                // Each segment gets its own isolated temp directory so that stale or
                // partially-written emote cache files from a previous failed run cannot
                // cause TwitchDownloaderCLI's internal FFmpeg pipe to close unexpectedly
                // ("The pipe has been ended." / exit code -532462766).
                string segTempDir = Path.Combine(tempDir.FullName, $"seg_{segmentIndex:D4}");

                Exception? lastSegmentException = null;
                for (int attempt = 0; attempt < maxSegmentAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (attempt > 0)
                    {
                        yield return ProgressStatus.WithProgress(
                            $"Retrying chat segment {displayNum}/{estimatedSegmentCount} (attempt {attempt + 1}/{maxSegmentAttempts})",
                            (double)segmentIndex / estimatedSegmentCount * 100);
                        _logger.LogWarning("Retrying chat segment {Index}/{Total}, attempt {Attempt}/{MaxAttempts}",
                            displayNum, estimatedSegmentCount, attempt + 1, maxSegmentAttempts);
                    }

                    // Remove any leftover .tmp file from a previous attempt.
                    if (File.Exists(segmentTmpFile))
                        File.Delete(segmentTmpFile);

                    // Wipe and recreate the per-segment temp dir to guarantee a clean emote cache.
                    if (Directory.Exists(segTempDir))
                        Directory.Delete(segTempDir, recursive: true);
                    Directory.CreateDirectory(segTempDir);

                    string segArguments =
                        $"chatrender -i \"{chatFile.FullName}\" --temp-path \"{segTempDir}\" " +
                        $"-o \"{segmentTmpFile}\" --collision overwrite " +
                        $"--framerate {fps.ToString(CultureInfo.InvariantCulture)} --chat-height {height} " +
                        $"--chat-width {chatWidth} --font-size {fontSize} --update-rate {updateRate} " +
                        $"--ffmpeg-path \"{Settings.FfmpegPath}\" -b {beginningArg} -e {endingArg}";

                    yield return ProgressStatus.WithProgress(
                        $"Rendering chat segment {displayNum}/{estimatedSegmentCount}",
                        (double)segmentIndex / estimatedSegmentCount * 100,
                        EstimateMinutesRemaining(renderStopwatch, segmentIndex, estimatedSegmentCount));

                    // Buffer status messages: yield return is not permitted inside a try/catch block,
                    // so we collect them here and yield them after the try/catch completes.
                    var buffered = new List<ProgressStatus>();
                    lastSegmentException = null;
                    try
                    {
                        await foreach (var status in RunTwitchDownloaderCliAsync(segArguments, $"chat segment {displayNum}/{estimatedSegmentCount}", cancellationToken))
                            buffered.Add(status);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lastSegmentException = ex;
                    }
                    finally
                    {
                        try { Directory.Delete(segTempDir, recursive: true); }
                        catch (Exception ex) { _logger.LogDebug(ex, "Failed to remove segment temp directory {Path}", segTempDir); }
                    }

                    foreach (var s in buffered)
                        yield return s;

                    if (lastSegmentException == null)
                    {
                        // Atomic promotion: only a fully-rendered segment is used for resume detection.
                        File.Move(segmentTmpFile, segmentFile);
                        _logger.LogInformation("Chat segment {Index}/{Total} complete", displayNum, estimatedSegmentCount);
                        yield return ProgressStatus.WithProgress(
                            $"Chat segment {displayNum}/{estimatedSegmentCount} complete",
                            (double)displayNum / estimatedSegmentCount * 100,
                            EstimateMinutesRemaining(renderStopwatch, displayNum, estimatedSegmentCount));
                        break;
                    }

                    if (File.Exists(segmentTmpFile))
                        File.Delete(segmentTmpFile);

                    _logger.LogWarning(lastSegmentException,
                        "Chat segment {Index}/{Total} attempt {Attempt}/{MaxAttempts} failed",
                        displayNum, estimatedSegmentCount, attempt + 1, maxSegmentAttempts);
                }

                if (lastSegmentException != null)
                    throw new InvalidOperationException(
                        $"Chat segment {displayNum}/{estimatedSegmentCount} failed after {maxSegmentAttempts} attempt(s).", lastSegmentException);
            }

            // Concatenate all segments into the final output without re-encoding.
            yield return ProgressStatus.WithProgress("Concatenating chat segments into final video", 95);

            string concatListPath = Path.Combine(segmentsDir, "concat_list.txt");
            // ffmpeg concat demuxer requires forward-slash paths on all platforms.
            await File.WriteAllLinesAsync(
                concatListPath,
                segmentFiles.Select(f =>
                {
                    var normalizedPath = f.Replace('\\', '/');
                    var escapedPath = normalizedPath.Replace("'", "'\\''");
                    return $"file '{escapedPath}'";
                }),
                cancellationToken);

            // Remove any leftover partial output from a previous interrupted concat.
            if (File.Exists(finalFile.FullName))
                File.Delete(finalFile.FullName);

            string concatArguments =
                $"-f concat -safe 0 -i \"{concatListPath}\" " +
                $"-c copy -movflags +faststart \"{finalFile.FullName}\"";

            await foreach (var status in RunFfmpegAsync(concatArguments, "while concatenating chat segments", cancellationToken))
                yield return status;

            // Best-effort cleanup of the segments directory after a successful concatenation.
            // Use recursive deletion so the directory is removed even if individual file
            // deletes above failed for any reason.
            try { Directory.Delete(segmentsDir, recursive: true); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to remove chat segments directory {Path}", segmentsDir); }

            yield return ProgressStatus.WithProgress("Done rendering chat video", 100);
        }

        /// <summary>
        /// Runs TwitchDownloaderCLI with the given <paramref name="arguments"/>, yielding
        /// <see cref="ProgressStatus"/> objects as progress is emitted. Each object contains
        /// a message and an optional numeric progress value parsed from <c>[STATUS]</c> output lines.
        /// Throws <see cref="InvalidOperationException"/> if the process exits with a non-zero code.
        /// The process is killed when <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        private async IAsyncEnumerable<ProgressStatus> RunTwitchDownloaderCliAsync(
            string arguments,
            string errorContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Settings.TwitchDownloaderCliPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var statusRegex  = new Regex(@"^\[STATUS\] - (.*?)$");
            var statusQueue  = new System.Collections.Concurrent.ConcurrentQueue<ProgressStatus>();
            var errorOutput  = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = statusRegex.Match(e.Data);
                if (match.Success)
                    statusQueue.Enqueue(ProgressStatus.Indeterminate(match.Groups[1].Value));
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorOutput.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Kill the process tree when the caller cancels so no orphaned TwitchDownloaderCLI
            // processes survive beyond this enumerator.
            using var killOnCancel = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // process already exited or disposed
            });

            // Use CancellationToken.None so WaitForExitAsync waits for the process to
            // fully exit after Kill() rather than returning immediately on cancellation.
            var waitTask = process.WaitForExitAsync(CancellationToken.None);

            while (!waitTask.IsCompleted)
            {
                while (statusQueue.TryDequeue(out var status))
                    yield return status;

                // Do NOT pass cancellationToken to Task.Delay — if it threw OCE here the
                // enumerator would exit immediately, skipping Kill() and the waitTask drain.
                // Instead we poll for cancellation manually so we can clean up first.
                await Task.Delay(100);

                if (cancellationToken.IsCancellationRequested)
                {
                    // The killOnCancel registration may have already fired; calling Kill()
                    // again is safe — it is a no-op if the process has already exited.
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }

                    // Wait for the process to actually exit and for the output streams to
                    // drain before rethrowing, capped at 5 s to avoid blocking indefinitely.
                    try { await waitTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { /* process didn't exit in time; proceed to rethrow OCE */ }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            // Drain any remaining statuses after process exit
            while (statusQueue.TryDequeue(out var status))
                yield return status;

            await waitTask;

            if (process.ExitCode != 0)
            {
                string errorText = errorOutput.ToString();
                _logger.LogError(
                    "TwitchDownloaderCLI exited with code {ExitCode} for {Context}. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, errorContext, arguments, errorText);
                throw new InvalidOperationException(
                    $"TwitchDownloaderCLI exited with code {process.ExitCode} for {errorContext}.");
            }
        }

        public async IAsyncEnumerable<ProgressStatus> DownloadVodNewAsync(string vodId, DirectoryInfo tempDir, FileInfo finalFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Validate vodId before use in a URL to prevent malformed requests.
            if (!IsValidVodId(vodId))
                throw new ArgumentException($"Invalid VOD ID format: '{vodId}'", nameof(vodId));

            // yt-dlp is used for resumable Twitch VOD downloads.
            // Key flags:
            //   -c / --continue           resume a partial download instead of restarting
            //   --part                    write to a .part file while in progress (safe resume marker)
            //   --retries N               retry on transient network errors
            //   --fragment-retries N      retry failed HLS/DASH fragment fetches (Twitch uses HLS)
            //   --concurrent-fragments N  download N fragments in parallel for speed
            //   --newline                 emit each progress update on its own line (parseable)
            //   --progress                always show progress even when not connected to a TTY
            //   --merge-output-format mp4 ensure the final container is mp4

            if (tempDir == null)
                throw new ArgumentNullException(nameof(tempDir));

            if (!tempDir.Exists)
                tempDir.Create();

            string url = $"https://www.twitch.tv/videos/{vodId}";
            string arguments = $"--continue --part --retries 10 --fragment-retries infinite " +
                               $"--concurrent-fragments 4 --newline --progress " +
                               $"--merge-output-format mp4 " +
                               $"--paths temp:\"{tempDir.FullName}\" " +
                               $"-o \"{finalFile.FullName}\" \"{url}\"";

            var psi = new ProcessStartInfo
            {
                FileName = Settings.YtDlpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // yt-dlp progress lines look like:
            //   [download]  10.0% of 1.20GiB at 5.00MiB/s ETA 00:03:45 (frag 10/100)
            var progressRegex = new Regex(@"^\[download\]\s+(.+)$", RegexOptions.Compiled);
            var http416Regex = new Regex(@"HTTP Error 416.*?fragment\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<ProgressStatus>();
            var errorOutput = new StringBuilder();

            // Local helper: deletes the stale fragment file when an HTTP 416 error
            // is detected, allowing yt-dlp's built-in retry to re-download it fresh.
            void TryDeleteStaleFragment(string text)
            {
                var match416 = http416Regex.Match(text);
                if (!match416.Success || !tempDir.Exists)
                    return;

                string fragNum = match416.Groups[1].Value;
                try
                {
                    foreach (var fragFile in Directory.EnumerateFiles(finalFile.Directory.FullName))
                    {
                        var fileName = Path.GetFileName(fragFile);
                        // Match e.g. "2686750542.mp4.part-Frag146.part" or "…Frag146"
                        if (fileName.EndsWith($"Frag{fragNum}.part", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith($"Frag{fragNum}", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(fragFile);
                            _logger.LogInformation(
                                "Deleted stale fragment {File} to recover from HTTP 416",
                                fileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Failed to delete stale fragment for fragment {Fragment}",
                        fragNum);
                }
            }

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = progressRegex.Match(e.Data);
                if (match.Success)
                {
                    string line = match.Groups[1].Value;

                    // Suppress HTTP 416 retry messages from the UI — silently delete
                    // the stale fragment and let yt-dlp's retry handle the rest.
                    if (http416Regex.IsMatch(line))
                    {
                        TryDeleteStaleFragment(line);
                        return;
                    }

                    var (pct, eta) = ParseYtDlpProgress(line);
                    if (pct.HasValue)
                        statusQueue.Enqueue(ProgressStatus.WithProgress("Downloading VOD", pct.Value, eta));
                    else
                        statusQueue.Enqueue(ProgressStatus.Indeterminate(line));
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.AppendLine(e.Data);
                    TryDeleteStaleFragment(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Kill the yt-dlp process tree when the caller cancels, so no orphaned
            // download processes or .part file locks survive beyond this enumerator.
            using var killOnCancel = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // process already exited or disposed
            });

            // Use CancellationToken.None so WaitForExitAsync waits for the process to
            // fully exit after Kill() rather than returning immediately on cancellation.
            var waitTask = process.WaitForExitAsync(CancellationToken.None);

            while (!waitTask.IsCompleted)
            {
                while (statusQueue.TryDequeue(out var status))
                {
                    yield return status;
                }
                await Task.Delay(100, cancellationToken);
            }

            // Drain any remaining statuses after process exit
            while (statusQueue.TryDequeue(out var status))
            {
                yield return status;
            }

            await waitTask;

            if (process.ExitCode != 0)
            {
                string errorText = errorOutput.ToString();

                // Check for specific known errors (VOD deleted or unavailable)
                if (errorText.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase) ||
                    errorText.Contains("This video is not available", StringComparison.OrdinalIgnoreCase) ||
                    errorText.Contains("Unable to download the video", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("VOD {VodId} is invalid, deleted, or expired. This will be counted as a failure.", vodId);
                    throw new InvalidOperationException($"VOD {vodId} is invalid, deleted, or expired.");
                }

                _logger.LogError("yt-dlp exited with code {ExitCode} for VOD {VodId}. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, vodId, arguments, errorText);
                throw new InvalidOperationException($"yt-dlp exited with code {process.ExitCode} for VOD {vodId}.");
            }
        }

        private static readonly Regex _encoderNameRegex = new(@"\b(h264_amf|h264_nvenc|h264_qsv)\b", RegexOptions.Compiled);
        private static readonly Regex _ffmpegProgressRegex = new(@"frame=\s*(\d+).*time=(\S+)", RegexOptions.Compiled);
        internal static readonly TimeSpan SegmentLength = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Formats a time in seconds as a TwitchDownloaderCLI time argument string in the
        /// <c>{value.FFF}s</c> format with invariant decimal separator.
        /// Used for both the <c>-b</c> (beginning) and <c>-e</c> (ending) flags.
        /// </summary>
        internal static string FormatSegmentTimeArg(double seconds) =>
            seconds.ToString("F3", CultureInfo.InvariantCulture) + "s";

        /// <summary>
        /// Selects the best available H.264 encoder from the supplied ffmpeg encoder list output.
        /// Priority: h264_amf (AMD) → h264_nvenc (NVIDIA) → h264_qsv (Intel) → libx264 (software).
        /// </summary>
        internal static string SelectVideoEncoder(string availableEncoders)
        {
            var found = new HashSet<string>(
                _encoderNameRegex.Matches(availableEncoders).Select(m => m.Value));
            if (found.Contains("h264_amf"))   return "h264_amf";
            if (found.Contains("h264_nvenc")) return "h264_nvenc";
            if (found.Contains("h264_qsv"))   return "h264_qsv";
            return "libx264";
        }

        private string DetectVideoEncoder()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Settings.FfmpegPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi)!;
                // Read both streams before WaitForExit to avoid deadlock if either buffer fills.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string output = stdoutTask.GetAwaiter().GetResult();
                string stderr = stderrTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("Hardware encoder detection failed (exit code {ExitCode}), falling back to libx264. {Stderr}",
                        process.ExitCode, stderr.Trim());
                    return "libx264";
                }

                return SelectVideoEncoder(output);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hardware encoder detection failed, falling back to libx264");
                return "libx264";
            }
        }

        /// <summary>
        /// Runs ffmpeg with the given arguments, yielding progress status objects.
        /// Throws <see cref="InvalidOperationException"/> if ffmpeg exits with a non-zero code.
        /// </summary>
        private async IAsyncEnumerable<ProgressStatus> RunFfmpegAsync(string arguments, string errorContext,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Settings.FfmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // ffmpeg writes progress lines to stderr in the form:
            //   frame=  100 fps= 60 q=28.0 size=  2048kB time=00:00:01.67 ...
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<ProgressStatus>();
            var errorOutput = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                errorOutput.AppendLine(e.Data);
                var match = _ffmpegProgressRegex.Match(e.Data);
                if (match.Success)
                    statusQueue.Enqueue(ProgressStatus.Indeterminate($"Encoding frame {match.Groups[1].Value}, time {match.Groups[2].Value}"));
            };

            process.Start();
            process.BeginErrorReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore exceptions thrown when trying to kill the process (e.g., race conditions).
                }
            });

            var waitTask = process.WaitForExitAsync(CancellationToken.None);

            while (!waitTask.IsCompleted)
            {
                while (statusQueue.TryDequeue(out var status))
                    yield return status;
                await Task.Delay(100, cancellationToken);
            }

            // Drain any remaining statuses after process exit
            while (statusQueue.TryDequeue(out var status))
                yield return status;

            await waitTask;

            if (process.ExitCode != 0)
            {
                string errorText = errorOutput.ToString();
                _logger.LogError("ffmpeg exited with code {ExitCode} {Context}. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, errorContext, arguments, errorText);
                throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode} {errorContext}.");
            }
        }

        /// <summary>
        /// Returns the duration of the given media file in seconds using ffprobe.
        /// </summary>
        /// <param name="filePath">Absolute path to the media file to probe.</param>
        /// <returns>Duration in seconds as a <see cref="double"/>.</returns>
        /// <exception cref="FormatException">
        /// Thrown when ffprobe output cannot be parsed as a floating-point number.
        /// </exception>
        private double GetVideoDuration(string filePath)
        {
            var arguments =
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = Settings.FfprobePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start ffprobe for file: {filePath}");
            }

            string output = process.StandardOutput.ReadToEnd();
            string errorOutput = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffprobe exited with code {process.ExitCode} while probing '{filePath}'. " +
                    $"Arguments: {arguments}. Stderr: {errorOutput}");
            }

            output = output.Trim();
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                return duration;

            throw new FormatException($"Unable to parse duration from ffprobe output: '{output}' for file: {filePath}");
        }

        public async IAsyncEnumerable<ProgressStatus> CombineVideosAsync(FileInfo vodFile, FileInfo chatVideoFile, FileInfo outputFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(vodFile.FullName))
                throw new FileNotFoundException($"VOD file not found: {vodFile.FullName}");
            if (!File.Exists(chatVideoFile.FullName))
                throw new FileNotFoundException($"Chat video file not found: {chatVideoFile.FullName}");

            string encoder = _cachedEncoder.Value;

            string pixelFormat = encoder == "libx264" ? "yuv420p" : "nv12";

            string encoderArgs = encoder switch
            {
                "h264_amf"   => "-c:v h264_amf -rc:v vbr_peak -b:v 5M -maxrate 6M -bufsize 10M -usage transcoding -profile:v high -level 4.1 -qmin 18 -qmax 28",
                "h264_nvenc" => "-c:v h264_nvenc -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
                "h264_qsv"   => "-c:v h264_qsv -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
                _            => "-c:v libx264 -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
            };

            double totalDuration = GetVideoDuration(vodFile.FullName);
            if (totalDuration <= 0)
                throw new InvalidOperationException($"Could not determine a valid duration for VOD file: {vodFile.FullName}");

            int segmentCount = (int)Math.Ceiling(totalDuration / SegmentLength.TotalSeconds);

            // Segments are stored alongside the output file in a dedicated subdirectory.
            string segmentsDir = Path.Combine(
                outputFile.DirectoryName ?? ".",
                Path.GetFileNameWithoutExtension(outputFile.Name) + "_segments");
            Directory.CreateDirectory(segmentsDir);

            yield return ProgressStatus.WithProgress($"Combining videos using {encoder} in {segmentCount} segment(s)", 0);

            var segmentFiles = new List<string>(segmentCount);
            var combineStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < segmentCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double startSec    = i * SegmentLength.TotalSeconds;
                double segDuration = Math.Min(SegmentLength.TotalSeconds, totalDuration - startSec);

                // Guard against floating-point rounding causing a non-positive segment duration,
                // which would lead to an invalid "-t 0" (or negative) ffmpeg invocation.
                if (segDuration <= 0)
                {
                    _logger.LogDebug(
                        "Computed non-positive segment duration {Duration} at index {Index}; stopping segmentation loop.",
                        segDuration, i);
                    break;
                }

                string segmentFile    = Path.Combine(segmentsDir, $"segment_{i:D4}.mp4");
                string segmentTmpFile = segmentFile + ".tmp";
                segmentFiles.Add(segmentFile);

                if (File.Exists(segmentFile))
                {
                    _logger.LogInformation("Segment {Index}/{Total} already exists, skipping", i + 1, segmentCount);
                    yield return ProgressStatus.WithProgress(
                        $"Segment {i + 1}/{segmentCount} already rendered, skipping",
                        (double)(i + 1) / segmentCount * 100);
                    continue;
                }

                // Remove any leftover temp file from a previous interrupted run.
                if (File.Exists(segmentTmpFile))
                    File.Delete(segmentTmpFile);

                string startStr    = startSec.ToString("F3", CultureInfo.InvariantCulture);
                string durationStr = segDuration.ToString("F3", CultureInfo.InvariantCulture);

                // Use input-level -ss for fast seeking into both streams, then limit
                // output duration with -t so the segment covers exactly [start, start+duration).
                string segArguments =
                    $"-ss {startStr} -i \"{vodFile.FullName}\" " +
                    $"-ss {startStr} -i \"{chatVideoFile.FullName}\" " +
                    $"-t {durationStr} " +
                    $"-filter_complex \"[0:v][1:v]hstack=inputs=2,format={pixelFormat}\" " +
                    $"{encoderArgs} -c:a copy -f mp4 \"{segmentTmpFile}\"";

                yield return ProgressStatus.WithProgress(
                    $"Encoding segment {i + 1}/{segmentCount}",
                    (double)i / segmentCount * 100,
                    EstimateMinutesRemaining(combineStopwatch, i, segmentCount));
                await foreach (var status in RunFfmpegAsync(segArguments, $"while encoding segment {i + 1}/{segmentCount}", cancellationToken))
                    yield return status;

                // Atomic promotion: only the successfully-completed segment file is used for resume detection.
                File.Move(segmentTmpFile, segmentFile);
                _logger.LogInformation("Segment {Index}/{Total} complete", i + 1, segmentCount);
                yield return ProgressStatus.WithProgress(
                    $"Segment {i + 1}/{segmentCount} complete",
                    (double)(i + 1) / segmentCount * 100,
                    EstimateMinutesRemaining(combineStopwatch, i + 1, segmentCount));
            }

            // Concatenate all segments into the final output without re-encoding.
            yield return ProgressStatus.WithProgress("Concatenating segments into final video", 95);

            string concatListPath = Path.Combine(segmentsDir, "concat_list.txt");
            // ffmpeg concat demuxer requires forward-slash paths on all platforms.
            await File.WriteAllLinesAsync(
                concatListPath,
                segmentFiles.Select(f => $"file '{f.Replace('\\', '/')}'"),
                cancellationToken);

            // Remove any leftover partial output from a previous interrupted concat.
            // Without this, ffmpeg would prompt "Overwrite? [y/N]" on the inherited stdin
            // (the parent terminal), causing it to hang indefinitely with no log output.
            if (File.Exists(outputFile.FullName))
                File.Delete(outputFile.FullName);

            string concatArguments =
                $"-f concat -safe 0 -i \"{concatListPath}\" " +
                $"-c copy -movflags +faststart \"{outputFile.FullName}\"";

            await foreach (var status in RunFfmpegAsync(concatArguments, "while concatenating segments", cancellationToken))
                yield return status;

            // Best-effort cleanup of the segments directory after a successful concatenation.
            foreach (var f in segmentFiles)
            {
                try { File.Delete(f); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete segment file {Path}", f); }
            }
            try
            {
                File.Delete(concatListPath);
                Directory.Delete(segmentsDir);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to remove segments directory {Path}", segmentsDir); }

            yield return ProgressStatus.WithProgress("Done combining videos", 100);
        }

        public async IAsyncEnumerable<ProgressStatus> DownloadChatNewAsync(string vodId, DirectoryInfo tempDir, FileInfo finalFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string arguments = $"chatdownload --id \"{vodId}\" --temp-path \"{tempDir.FullName}\" -o \"{finalFile.FullName}\" --collision overwrite";


            var psi = new ProcessStartInfo
            {
                FileName = Settings.TwitchDownloaderCliPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var regex = new Regex(@"^\[STATUS\] - (.*?) \[\d+/\d+\]$");
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<ProgressStatus>();
            var errorOutput = new StringBuilder();
            var outputReceived = new TaskCompletionSource<bool>();
            var chatStopwatch = Stopwatch.StartNew();

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = regex.Match(e.Data);
                if (match.Success)
                {
                    string statusText = match.Groups[1].Value;
                    double? pct = ParseChatDownloadPercent(statusText);
                    if (pct.HasValue)
                        statusQueue.Enqueue(ProgressStatus.WithProgress("Downloading chat", pct.Value));
                    else
                        statusQueue.Enqueue(ProgressStatus.Indeterminate(statusText));
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var waitTask = process.WaitForExitAsync(cancellationToken);

            while (!waitTask.IsCompleted)
            {
                while (statusQueue.TryDequeue(out var status))
                {
                    yield return status;
                }
                await Task.Delay(100, cancellationToken);
            }

            // Drain any remaining statuses after process exit
            while (statusQueue.TryDequeue(out var status))
            {
                yield return status;
            }

            await waitTask;

            if (process.ExitCode != 0)
            {
                string errorText = errorOutput.ToString();

                // Check for specific known errors
                if (errorText.Contains("Invalid VOD, deleted/expired VOD possibly?", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("VOD {VodId} is invalid, deleted, or expired. This will be counted as a failure.", vodId);
                    throw new InvalidOperationException($"VOD {vodId} is invalid, deleted, or expired.");
                }

                _logger.LogError("TwitchDownloaderCLI chatdownload exited with code {ExitCode} for VOD {VodId}. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, vodId, arguments, errorText);
                throw new InvalidOperationException($"TwitchDownloaderCLI chatdownload exited with code {process.ExitCode} for VOD {vodId}.");
            }
        }
    }
}

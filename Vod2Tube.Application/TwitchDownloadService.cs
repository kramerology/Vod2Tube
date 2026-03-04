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

namespace Vod2Tube.Application
{
    public class TwitchDownloadService
    {
        private const string DefaultCliFileName = "E:\\Projects\\Vod2Tube\\Vod2Tube.Console\\bin\\Debug\\net10.0\\TwitchDownloaderCLI.exe";
        private const string _ffmpegPath        = "E:\\Programs\\ffmpeg\\ffmpeg.exe";
        private const string _ffprobePath       = "E:\\Programs\\ffmpeg\\ffprobe.exe";

        private readonly ILogger<TwitchDownloadService> _logger;
        private readonly Lazy<string> _cachedEncoder;

        public TwitchDownloadService(ILogger<TwitchDownloadService> logger)
        {
            _logger = logger;
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





        public async IAsyncEnumerable<string> RenderChatVideoAsync(FileInfo chatFile, FileInfo vodFile, DirectoryInfo tempDir, FileInfo finalFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(chatFile.FullName))
                throw new FileNotFoundException($"Chat file not found: {chatFile.FullName}");
            if (!File.Exists(vodFile.FullName))
                throw new FileNotFoundException($"VOD file not found: {vodFile.FullName}");

            int chatWidth  = 350;      // width of chat panel in pixels
            int fontSize   = 15;       // base font size for chat text //11 too small
            int updateRate = 0;        // render each frame exactly (no interpolation)
            string fpsStr  = RunProcessWithOutput(_ffprobePath, $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 {vodFile.FullName}").Trim();
            string height  = RunProcessWithOutput(_ffprobePath, $"-v error -select_streams v:0 -show_entries stream=height -of default=noprint_wrappers=1:nokey=1 {vodFile.FullName}").Trim();         
            double fps = ParseFps(fpsStr);
            int.TryParse(height, out int videoHeight);
            bool is4K = videoHeight >= 2160;

            string arguments = $"chatrender -i \"{chatFile.FullName}\" --temp-path \"{tempDir.FullName}\" -o \"{finalFile.FullName}\" --collision overwrite --framerate {fps.ToString(CultureInfo.InvariantCulture)} --chat-height {height} --chat-width {chatWidth} --font-size {fontSize} --update-rate {updateRate} --ffmpeg-path \"{_ffmpegPath}\" ";

            var psi = new ProcessStartInfo
            {
                FileName = DefaultCliFileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // TwitchDownloaderCLI emits lines like:
            //   [STATUS] - Rendering chat [1234/5000]
            // Capture both the full message and any X/Y progress fraction.
            var statusRegex   = new Regex(@"^\[STATUS\] - (.*?)$");
            var progressRegex = new Regex(@"\[(\d+)/(\d+)\]");
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var errorOutput = new StringBuilder();

            var startTime = DateTime.UtcNow;

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = statusRegex.Match(e.Data);
                if (!match.Success) return;

                string rawStatus = match.Groups[1].Value;

                // Enrich with % complete and ETA if a X/Y counter is present.
                var progressMatch = progressRegex.Match(rawStatus);
                if (progressMatch.Success
                    && long.TryParse(progressMatch.Groups[1].Value, out long done)
                    && long.TryParse(progressMatch.Groups[2].Value, out long total)
                    && total > 0)
                {
                    double pct     = (double)done / total * 100.0;
                    double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                    string etaStr  = (done > 0 && elapsed > 0)
                        ? FormatEta(TimeSpan.FromSeconds(elapsed / done * (total - done)))
                        : "–";
                    statusQueue.Enqueue($"{rawStatus} | {pct:F1}% | ETA {etaStr}");
                }
                else
                {
                    statusQueue.Enqueue(rawStatus);
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
                _logger.LogError("TwitchDownloaderCLI chatrender exited with code {ExitCode} for chat file {ChatFile}. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, chatFile.FullName, arguments, errorText);
                throw new InvalidOperationException($"TwitchDownloaderCLI chatrender exited with code {process.ExitCode} for chat file {chatFile.FullName}.");
            }
        }

        public async IAsyncEnumerable<string> DownloadVodNewAsync(string vodId, DirectoryInfo tempDir, FileInfo finalFile, CancellationToken cancellationToken = default)
        {
            string arguments = $"videodownload -u \"{vodId}\" --temp-path \"{tempDir.FullName}\" -o \"{finalFile.FullName}\" --collision overwrite --ffmpeg-path \"{_ffmpegPath}\" ";

            var psi = new ProcessStartInfo
            {
                FileName = DefaultCliFileName,
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
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var errorOutput = new StringBuilder();
            var outputReceived = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = regex.Match(e.Data);
                if (match.Success)
                {
                    statusQueue.Enqueue(match.Groups[1].Value);
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

                _logger.LogError("TwitchDownloaderCLI videodownload exited with code {ExitCode} for VOD {VodId}. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, vodId, arguments, errorText);
                throw new InvalidOperationException($"TwitchDownloaderCLI videodownload exited with code {process.ExitCode} for VOD {vodId}.");
            }
        }

        private static readonly Regex _encoderNameRegex = new(@"\b(h264_amf|h264_nvenc|h264_qsv)\b", RegexOptions.Compiled);
        private static readonly Regex _ffmpegProgressRegex = new(@"frame=\s*(\d+).*time=(\S+)", RegexOptions.Compiled);

        /// <summary>
        /// Returns a human-readable ETA string (e.g. "3m 12s", "45s").
        /// </summary>
        internal static string FormatEta(TimeSpan eta)
        {
            if (eta <= TimeSpan.Zero) return "0s";
            if (eta.TotalHours >= 1)
                return $"{(int)eta.TotalHours}h {eta.Minutes:D2}m";
            if (eta.TotalMinutes >= 1)
                return $"{(int)eta.TotalMinutes}m {eta.Seconds:D2}s";
            return $"{eta.Seconds}s";
        }

        /// <summary>
        /// Uses ffprobe to retrieve the duration of a video file.
        /// Returns <see cref="TimeSpan.Zero"/> on failure.
        /// </summary>
        private TimeSpan GetVideoDuration(string filePath)
        {
            try
            {
                var arguments =
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ffprobePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        _logger.LogWarning("Unable to start ffprobe to determine duration for {FilePath}", filePath);
                        return TimeSpan.Zero;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string errorOutput = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        _logger.LogWarning(
                            "ffprobe exited with code {ExitCode} while determining duration for {FilePath}. Error: {Error}",
                            process.ExitCode, filePath, errorOutput);
                        return TimeSpan.Zero;
                    }

                    output = output.Trim();
                    if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out double secs))
                        return TimeSpan.FromSeconds(secs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to determine duration for {FilePath}", filePath);
            }
            return TimeSpan.Zero;
        }


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
                    FileName = _ffmpegPath,
                    Arguments = "-hide_banner -encoders",
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

        public async IAsyncEnumerable<string> CombineVideosAsync(FileInfo vodFile, FileInfo chatVideoFile, FileInfo outputFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(vodFile.FullName))
                throw new FileNotFoundException($"VOD file not found: {vodFile.FullName}");
            if (!File.Exists(chatVideoFile.FullName))
                throw new FileNotFoundException($"Chat video file not found: {chatVideoFile.FullName}");

            string encoder = _cachedEncoder.Value;

            string pixelFormat = encoder == "libx264" ? "yuv420p" : "nv12";

            string encoderArgs = encoder switch
            {
                "h264_amf"   => $"-c:v h264_amf -rc:v vbr_peak -b:v 5M -maxrate 6M -bufsize 10M -usage transcoding -profile:v high -level 4.1 -qmin 18 -qmax 28",
                "h264_nvenc" => $"-c:v h264_nvenc -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
                "h264_qsv"   => $"-c:v h264_qsv -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
                _            => $"-c:v libx264 -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
            };

            string arguments = $"-i \"{vodFile.FullName}\" -i \"{chatVideoFile.FullName}\" -filter_complex \"[0:v][1:v]hstack=inputs=2,format={pixelFormat}\" {encoderArgs} -c:a copy \"{outputFile.FullName}\"";

            // Obtain the total duration of the VOD so we can report % complete.
            TimeSpan totalDuration = GetVideoDuration(vodFile.FullName);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
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
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var errorOutput = new StringBuilder();
            var startTime   = DateTime.UtcNow;

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                errorOutput.AppendLine(e.Data);
                var match = _ffmpegProgressRegex.Match(e.Data);
                if (!match.Success) return;

                long   frame      = long.TryParse(match.Groups[1].Value, out var f) ? f : 0;
                string timeStr    = match.Groups[2].Value;

                // Build a friendly status line including % and ETA when possible.
                string status;
                if (totalDuration > TimeSpan.Zero
                    && TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var currentTime))
                {
                    double pct     = Math.Min(currentTime.TotalSeconds / totalDuration.TotalSeconds * 100.0, 100.0);
                    double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                    string etaStr  = (pct > 0 && elapsed > 0)
                        ? FormatEta(TimeSpan.FromSeconds(elapsed / pct * (100.0 - pct)))
                        : "–";
                    status = $"Combining — frame {frame}, time {timeStr} | {pct:F1}% | ETA {etaStr}";
                }
                else
                {
                    status = $"Combining — frame {frame}, time {timeStr}";
                }

                statusQueue.Enqueue(status);
            };

            process.Start();
            process.BeginErrorReadLine();

            yield return $"Combining videos using {encoder}";

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
                _logger.LogError("ffmpeg exited with code {ExitCode} while combining videos. Arguments: {Arguments}. Stderr: {Stderr}",
                    process.ExitCode, arguments, errorText);
                throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode} while combining videos.");
            }
        }

        public async IAsyncEnumerable<string> DownloadChatNewAsync(string vodId, DirectoryInfo tempDir, FileInfo finalFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string arguments = $"chatdownload --id \"{vodId}\" --temp-path \"{tempDir.FullName}\" -o \"{finalFile.FullName}\" --collision overwrite";


            var psi = new ProcessStartInfo
            {
                FileName = DefaultCliFileName,
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
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var errorOutput = new StringBuilder();
            var outputReceived = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = regex.Match(e.Data);
                if (match.Success)
                {
                    statusQueue.Enqueue(match.Groups[1].Value);
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

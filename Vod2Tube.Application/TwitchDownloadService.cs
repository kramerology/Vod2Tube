using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Vod2Tube.Application
{
    public class TwitchDownloadService
    {
        private const string DefaultCliFileName = "E:\\Projects\\Vod2Tube\\Vod2Tube.Console\\bin\\Debug\\net8.0\\TwitchDownloaderCLI.exe";
        private const string _ffmpegPath        = "E:\\Programs\\ffmpeg\\ffmpeg.exe";
        private const string _ffprobePath       = "E:\\Programs\\ffmpeg\\ffprobe.exe";


        static string RunProcessWithOutput(string fileName, string arguments)
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
                Console.Error.WriteLine($"Error: '{fileName} {arguments}' failed with exit code {process.ExitCode}.");
                Console.Error.WriteLine(error);
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





        public async IAsyncEnumerable<string> RenderChatVideoAsync(FileInfo chatFile, FileInfo vodFile, DirectoryInfo tempDir, FileInfo finalFile, CancellationToken cancellationToken = default)
        {
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

            var regex = new Regex(@"^\[STATUS\] - (.*?)$");
            var statusQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
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

            process.Start();
            process.BeginOutputReadLine();

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

            process.Start();
            process.BeginOutputReadLine();

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
        }

        private static readonly Regex _encoderNameRegex = new(@"\b(h264_amf|h264_nvenc|h264_qsv)\b", RegexOptions.Compiled);
        private static readonly Regex _ffmpegProgressRegex = new(@"frame=\s*(\d+).*time=(\S+)", RegexOptions.Compiled);

        /// <summary>
        /// Selects the best available H.264 encoder from the supplied ffmpeg encoder list output.
        /// Priority: h264_amf (AMD) → h264_nvenc (NVIDIA) → h264_qsv (Intel) → libx264 (software).
        /// </summary>
        internal static string SelectVideoEncoder(string availableEncoders)
        {
            var found = new System.Collections.Generic.HashSet<string>(
                _encoderNameRegex.Matches(availableEncoders).Select(m => m.Value));
            if (found.Contains("h264_amf"))   return "h264_amf";
            if (found.Contains("h264_nvenc")) return "h264_nvenc";
            if (found.Contains("h264_qsv"))   return "h264_qsv";
            return "libx264";
        }

        private static string DetectVideoEncoder()
        {
            try
            {
                return SelectVideoEncoder(RunProcessWithOutput(_ffmpegPath, "-hide_banner -encoders"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: hardware encoder detection failed, falling back to libx264. ({ex.Message})");
                return "libx264";
            }
        }

        public async IAsyncEnumerable<string> CombineVideosAsync(FileInfo vodFile, FileInfo chatVideoFile, FileInfo outputFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string encoder = DetectVideoEncoder();

            string pixelFormat = encoder == "libx264" ? "yuv420p" : "nv12";

            string encoderArgs = encoder switch
            {
                "h264_amf"   => $"-c:v h264_amf -rc:v vbr_peak -b:v 5M -maxrate 6M -bufsize 10M -usage transcoding -profile:v high -level 4.1 -qmin 18 -qmax 28",
                "h264_nvenc" => $"-c:v h264_nvenc -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
                "h264_qsv"   => $"-c:v h264_qsv -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
                _            => $"-c:v libx264 -b:v 5M -maxrate 6M -bufsize 10M -profile:v high -level 4.1",
            };

            string arguments = $"-i \"{vodFile.FullName}\" -i \"{chatVideoFile.FullName}\" -filter_complex \"[0:v][1:v]hstack=inputs=2,format={pixelFormat}\" {encoderArgs} -c:a copy \"{outputFile.FullName}\"";

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

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = _ffmpegProgressRegex.Match(e.Data);
                if (match.Success)
                {
                    statusQueue.Enqueue($"Encoding frame {match.Groups[1].Value}, time {match.Groups[2].Value}");
                }
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

            process.Start();
            process.BeginOutputReadLine();

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
        }
    }
}

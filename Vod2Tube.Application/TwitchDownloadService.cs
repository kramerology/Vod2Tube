using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Vod2Tube.Application
{
    public class TwitchDownloadService
    {
        private const string DefaultCliFileName = "E:\\Projects\\Vod2Tube\\Vod2Tube\\Vod2Tube.Console\\bin\\Debug\\net8.0\\TwitchDownloaderCLI.exe";
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

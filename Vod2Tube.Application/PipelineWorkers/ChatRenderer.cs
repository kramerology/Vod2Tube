using Microsoft.Extensions.Options;
using Vod2Tube.Application.Models;

namespace Vod2Tube.Application
{
    public class ChatRenderer
    {
        private readonly TwitchDownloadService _downloadService;
        private readonly IOptionsSnapshot<AppSettings> _options;

        public ChatRenderer(TwitchDownloadService downloadService, IOptionsSnapshot<AppSettings> options)
        {
            _downloadService = downloadService;
            _options = options;
            var s = options.Value;
            WorkerPaths.EnsureDirectory(s.TempDir,      nameof(AppSettings.TempDir));
            WorkerPaths.EnsureDirectory(s.ChatRenderDir, nameof(AppSettings.ChatRenderDir));
        }

        public string GetOutputPath(string vodId)
        {
            var dir = new DirectoryInfo(_options.Value.ChatRenderDir);
            return Path.Combine(dir.FullName, $"{vodId}_chat.mp4");
        }

        public IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, string chatFilePath, string vodFilePath, CancellationToken ct)
        {
            var s = _options.Value;
            return _downloadService.RenderChatVideoAsync(
                new FileInfo(chatFilePath),
                new FileInfo(vodFilePath),
                new DirectoryInfo(s.TempDir),
                new FileInfo(GetOutputPath(vodId)),
                ct);
        }
    }
}

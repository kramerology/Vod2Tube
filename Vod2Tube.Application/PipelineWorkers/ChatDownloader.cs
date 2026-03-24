using Microsoft.Extensions.Options;
using Vod2Tube.Application.Models;

namespace Vod2Tube.Application
{
    public class ChatDownloader
    {
        private readonly TwitchDownloadService _downloadService;
        private readonly IOptionsSnapshot<AppSettings> _options;

        public ChatDownloader(TwitchDownloadService downloadService, IOptionsSnapshot<AppSettings> options)
        {
            _downloadService = downloadService;
            _options = options;
            var s = options.Value;
            Directory.CreateDirectory(s.VodDownloadTempDir);
            Directory.CreateDirectory(s.VodDownloadDir);
        }

        public string GetOutputPath(string vodId)
        {
            var dir = new DirectoryInfo(_options.Value.VodDownloadDir);
            return Path.Combine(dir.FullName, $"{vodId}.json");
        }

        public IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, CancellationToken ct)
        {
            var s = _options.Value;
            return _downloadService.DownloadChatNewAsync(
                vodId,
                new DirectoryInfo(s.VodDownloadTempDir),
                new FileInfo(GetOutputPath(vodId)),
                ct);
        }
    }
}

using Microsoft.Extensions.Options;
using Vod2Tube.Application.Models;

namespace Vod2Tube.Application
{
    public class FinalRenderer
    {
        private readonly TwitchDownloadService _downloadService;
        private readonly IOptionsSnapshot<AppSettings> _options;

        public FinalRenderer(TwitchDownloadService downloadService, IOptionsSnapshot<AppSettings> options)
        {
            _downloadService = downloadService;
            _options = options;
            Directory.CreateDirectory(options.Value.FinalVideoDir);
        }

        public string GetOutputPath(string vodId)
        {
            var dir = new DirectoryInfo(_options.Value.FinalVideoDir);
            return Path.Combine(dir.FullName, $"{vodId}_final.mp4");
        }

        public IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, string vodFilePath, string chatVideoFilePath,
            CancellationToken ct = default) =>
            _downloadService.CombineVideosAsync(
                new FileInfo(vodFilePath),
                new FileInfo(chatVideoFilePath),
                new FileInfo(GetOutputPath(vodId)),
                ct);
    }
}

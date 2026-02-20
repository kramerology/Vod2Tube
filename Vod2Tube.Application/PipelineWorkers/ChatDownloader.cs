namespace Vod2Tube.Application
{
    public class ChatDownloader
    {
        private readonly TwitchDownloadService _downloadService;
        private static readonly DirectoryInfo TempDir = new("VodDownloadsTemp");
        private static readonly DirectoryInfo FinalDir = new("VodDownloads");

        public ChatDownloader(TwitchDownloadService downloadService)
        {
            _downloadService = downloadService;
        }

        public string GetOutputPath(string vodId) =>
            Path.Combine(FinalDir.FullName, $"{vodId}.json");

        public IAsyncEnumerable<string> RunAsync(string vodId, CancellationToken ct) =>
            _downloadService.DownloadChatNewAsync(vodId, TempDir, new FileInfo(GetOutputPath(vodId)), ct);
    }
}

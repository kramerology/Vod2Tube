namespace Vod2Tube.Application
{
    public class VodDownloader
    {
        private readonly TwitchDownloadService _downloadService;
        private static readonly DirectoryInfo TempDir = new("VodDownloadsTemp");
        private static readonly DirectoryInfo FinalDir = new("VodDownloads");

        public VodDownloader(TwitchDownloadService downloadService)
        {
            _downloadService = downloadService;
        }

        public string GetOutputPath(string vodId) =>
            Path.Combine(FinalDir.FullName, $"{vodId}.mp4");

        public IAsyncEnumerable<string> RunAsync(string vodId, CancellationToken ct) =>
            _downloadService.DownloadVodNewAsync(vodId, TempDir, new FileInfo(GetOutputPath(vodId)), ct);
    }
}

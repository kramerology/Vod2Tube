namespace Vod2Tube.Application
{
    public class FinalRenderer
    {
        private readonly TwitchDownloadService _downloadService;
        private static readonly DirectoryInfo FinalDir = new("FinalVideos");

        static FinalRenderer()
        {
            FinalDir.Create();
        }

        public FinalRenderer(TwitchDownloadService downloadService)
        {
            _downloadService = downloadService;
        }

        public string GetOutputPath(string vodId) =>
            Path.Combine(FinalDir.FullName, $"{vodId}_final.mp4");

        public IAsyncEnumerable<string> RunAsync(string vodId, string vodFilePath, string chatVideoFilePath,
            CancellationToken ct = default) =>
            _downloadService.CombineVideosAsync(new FileInfo(vodFilePath), new FileInfo(chatVideoFilePath), new FileInfo(GetOutputPath(vodId)), ct);
    }
}

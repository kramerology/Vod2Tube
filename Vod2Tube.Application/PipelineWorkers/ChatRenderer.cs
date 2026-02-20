namespace Vod2Tube.Application
{
    public class ChatRenderer
    {
        private readonly TwitchDownloadService _downloadService;
        private static readonly DirectoryInfo TempDir = new("ChatRenderTemp");
        private static readonly DirectoryInfo FinalDir = new("ChatRenders");

        public ChatRenderer(TwitchDownloadService downloadService)
        {
            _downloadService = downloadService;
        }

        public string GetOutputPath(string vodId) =>
            Path.Combine(FinalDir.FullName, $"{vodId}_chat.mp4");

        public IAsyncEnumerable<string> RunAsync(string vodId, string chatFilePath, string vodFilePath, CancellationToken ct) =>
            _downloadService.RenderChatVideoAsync(new FileInfo(chatFilePath), new FileInfo(vodFilePath), TempDir, new FileInfo(GetOutputPath(vodId)), ct);
    }
}

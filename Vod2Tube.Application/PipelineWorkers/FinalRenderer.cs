namespace Vod2Tube.Application
{
    public class FinalRenderer
    {
        private static readonly DirectoryInfo FinalDir = new("FinalVideos");

        static FinalRenderer()
        {
            FinalDir.Create();
        }
        public string GetOutputPath(string vodId) =>
            Path.Combine(FinalDir.FullName, $"{vodId}_final.mp4");

        public async IAsyncEnumerable<string> RunAsync(string vodId, string vodFilePath, string chatVideoFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            // TODO: implement combining logic
            yield break;
        }
    }
}

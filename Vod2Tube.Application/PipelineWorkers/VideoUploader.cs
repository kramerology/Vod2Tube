namespace Vod2Tube.Application
{
    public class VideoUploader
    {
        public async IAsyncEnumerable<string> RunAsync(string vodId, string finalFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            // TODO: implement upload logic
            yield break;
        }
    }
}

using System.Reflection;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application
{
    public class VideoUploaderOptions
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "20"; // Gaming category
        public string PrivacyStatus { get; set; } = "private"; // private, public, or unlisted
        public List<string> Tags { get; set; } = new();
        public string? PlaylistId { get; set; }
    }

    public class VideoUploader
    {
        private readonly AppDbContext _dbContext;
        private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeUpload };
        private static readonly string ApplicationName = "Vod2Tube";
        private static readonly DirectoryInfo CredentialsDir = new("YouTubeCredentials");

        static VideoUploader()
        {
            if (!CredentialsDir.Exists)
            {
                CredentialsDir.Create();
            }
        }

        public VideoUploader(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async IAsyncEnumerable<string> RunAsync(string vodId, string finalFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "Initializing YouTube upload...";

            if (!File.Exists(finalFilePath))
            {
                throw new FileNotFoundException($"Video file not found: {finalFilePath}");
            }

            // Get VOD metadata from database
            var vodMetadata = await _dbContext.TwitchVods.FirstOrDefaultAsync(v => v.Id == vodId, ct);

            var options = new VideoUploaderOptions();
            if (vodMetadata != null)
            {
                options.Title = vodMetadata.Title;
                options.Description = $"{vodMetadata.Title}\n\nOriginal VOD: {vodMetadata.Url}\nStreamer: {vodMetadata.ChannelName}\nStreamed on: {vodMetadata.CreatedAtUTC:yyyy-MM-dd}";
                options.Tags = new List<string> { "twitch", "vod", vodMetadata.ChannelName };
            }
            else
            {
                options.Title = $"{vodId}";
                options.Description = $"Twitch VOD {vodId}";               
            }

            yield return "Authenticating with YouTube...";
            var youtubeService = await GetYouTubeServiceAsync(ct);

            yield return "Preparing video for upload...";
            var video = new Video
            {
                Snippet = new VideoSnippet
                {
                    Title = options.Title,
                    Description = options.Description,
                    Tags = options.Tags,
                    CategoryId = options.Category,
                },
                Status = new VideoStatus
                {
                    PrivacyStatus = options.PrivacyStatus,
                    MadeForKids = false,
                    SelfDeclaredMadeForKids = false
                }
            };

            using var fileStream = new FileStream(finalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var videosInsertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");

            long lastBytesUploaded = 0;
            long totalBytes = fileStream.Length;
            string? uploadedVideoId = null;

            videosInsertRequest.ProgressChanged += progress =>
            {
                if (progress.BytesSent != lastBytesUploaded)
                {
                    lastBytesUploaded = progress.BytesSent;
                }
            };

            videosInsertRequest.ResponseReceived += uploadedVideo =>
            {
                uploadedVideoId = uploadedVideo.Id;
                // Store the YouTube video ID in the database
                var pipeline = _dbContext.Pipelines.FirstOrDefault(p => p.VodId == vodId);
                if (pipeline != null)
                {
                    pipeline.YoutubeVideoId = uploadedVideo.Id;
                    _dbContext.SaveChanges();
                }
            };

            yield return $"Uploading video... 0%";

            var uploadTask = videosInsertRequest.UploadAsync(ct);

            while (!uploadTask.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);

                if (totalBytes > 0)
                {
                    double percentage = (double)lastBytesUploaded / totalBytes * 100;
                    double mbUploaded = lastBytesUploaded / (1024.0 * 1024.0);
                    double mbTotal = totalBytes / (1024.0 * 1024.0);
                    yield return $"Uploading video... {percentage:F1}% ({mbUploaded:F1} MB / {mbTotal:F1} MB)";
                }
            }

            var uploadStatus = await uploadTask;

            if (uploadStatus.Status == UploadStatus.Failed)
            {
                throw new Exception($"Upload failed: {uploadStatus.Exception?.Message}");
            }

            yield return "Upload completed successfully!";

            // Add to playlist if specified
            if (!string.IsNullOrEmpty(options.PlaylistId) && !string.IsNullOrEmpty(uploadedVideoId))
            {
                yield return "Adding video to playlist...";
                await AddVideoToPlaylistAsync(youtubeService, uploadedVideoId, options.PlaylistId, ct);
                yield return "Video added to playlist!";
            }
        }

        private async Task<YouTubeService> GetYouTubeServiceAsync(CancellationToken ct)
        {
            var clientSecretsPath = Path.Combine(CredentialsDir.FullName, "client_secrets.json");

            if (!File.Exists(clientSecretsPath))
            {
                throw new FileNotFoundException(
                    $"YouTube OAuth credentials not found. Please create a file at '{clientSecretsPath}' with your OAuth 2.0 client secrets from Google Cloud Console. " +
                    $"Visit https://console.cloud.google.com/apis/credentials to create OAuth 2.0 credentials."
                );
            }

            UserCredential credential;
            using (var stream = new FileStream(clientSecretsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    ct,
                    new FileDataStore(Path.Combine(CredentialsDir.FullName, "token_store"), true)
                );
            }

            return new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });
        }

        private async Task AddVideoToPlaylistAsync(YouTubeService service, string videoId, string playlistId, CancellationToken ct)
        {
            var playlistItem = new PlaylistItem
            {
                Snippet = new PlaylistItemSnippet
                {
                    PlaylistId = playlistId,
                    ResourceId = new ResourceId
                    {
                        Kind = "youtube#video",
                        VideoId = videoId
                    }
                }
            };

            await service.PlaylistItems.Insert(playlistItem, "snippet").ExecuteAsync(ct);
        }
    }
}

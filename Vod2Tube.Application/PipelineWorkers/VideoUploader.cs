using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using Vod2Tube.Application.Models;
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
        public bool MadeForKids { get; set; } = false; 
    }

    public class VideoUploader
    {
        private readonly AppDbContext _dbContext;
        private static readonly string[] Scopes = { YouTubeService.Scope.YoutubeUpload };
        private static readonly string ApplicationName = "Vod2Tube";
        private static readonly DirectoryInfo CredentialsDir = new("YouTubeCredentials");

        static VideoUploader()
        {
            CredentialsDir.Create();
        }
        public VideoUploader(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public virtual async IAsyncEnumerable<ProgressStatus> RunAsync(string vodId, string finalFilePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return ProgressStatus.Indeterminate("Initializing YouTube upload...");

            if (!File.Exists(finalFilePath))
            {
                throw new FileNotFoundException($"Video file not found: {finalFilePath}");
            }

            // Get VOD metadata and pipeline record from database
            var vodMetadata = await _dbContext.TwitchVods.FirstOrDefaultAsync(v => v.Id == vodId, ct);
            var pipeline = await _dbContext.Pipelines.FirstOrDefaultAsync(p => p.VodId == vodId, ct);

            var options = new VideoUploaderOptions();
            if (vodMetadata != null)
            {
                options.Title = vodMetadata.Title;
                options.Description = $"{vodMetadata.Title}\n\nOriginal VOD: {vodMetadata.Url}\nStreamer: {vodMetadata.ChannelName}\nStreamed on: {vodMetadata.CreatedAtUTC:yyyy-MM-dd}";
                options.Tags = new List<string> { "twitch", "vod", vodMetadata.ChannelName };
            }
            else
            {
                options.Title = $"VOD {vodId}";
                options.Description = $"Twitch VOD {vodId}";               
            }

            yield return ProgressStatus.Indeterminate("Authenticating with YouTube...");
            var youtubeService = await GetYouTubeServiceAsync(ct);

            yield return ProgressStatus.Indeterminate("Preparing video for upload...");
            var video = new Video
            {
                Snippet = new VideoSnippet
                {
                    Title = SanitizeString(options.Title),
                    Description = SanitizeString(options.Description),
                    Tags = options.Tags,
                    CategoryId = options.Category
                },
                Status = new VideoStatus
                {
                    PrivacyStatus = options.PrivacyStatus,
                    MadeForKids = options.MadeForKids,
                    SelfDeclaredMadeForKids = options.MadeForKids
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
            };

            // Determine whether to resume an existing upload session or start a new one
            string? savedUploadUri = pipeline?.ResumableUploadUri;
            Uri uploadUri;

            if (!string.IsNullOrEmpty(savedUploadUri))
            {
                yield return ProgressStatus.Indeterminate("Resuming interrupted upload...");
                uploadUri = new Uri(savedUploadUri);
            }
            else
            {
                yield return ProgressStatus.Indeterminate("Initiating upload session...");
                uploadUri = await videosInsertRequest.InitiateSessionAsync(ct);

                // Persist the upload URI so the upload can be resumed if interrupted
                if (pipeline != null)
                {
                    pipeline.ResumableUploadUri = uploadUri.AbsoluteUri;
                    string? saveWarning = null;
                    try
                    {
                        await _dbContext.SaveChangesAsync(ct);
                    }
                    catch (Exception)
                    {
                        // Best-effort save — the upload proceeds, but a restart cannot be resumed
                        pipeline.ResumableUploadUri = string.Empty;
                        _dbContext.Entry(pipeline).Property(p => p.ResumableUploadUri).IsModified = false;
                        saveWarning = "Warning: Failed to save upload session URI; upload cannot be resumed if interrupted.";
                    }

                    if (saveWarning != null)
                        yield return ProgressStatus.Indeterminate(saveWarning);
                }
            }

            yield return ProgressStatus.WithProgress("Uploading video...", 0);

            var uploadTask = videosInsertRequest.ResumeAsync(uploadUri, ct);
            var uploadStopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (!uploadTask.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);

                if (totalBytes > 0)
                {
                    double percentage = (double)lastBytesUploaded / totalBytes * 100;
                    double mbUploaded = lastBytesUploaded / (1024.0 * 1024.0);
                    double mbTotal = totalBytes / (1024.0 * 1024.0);
                    double? etaMinutes = null;
                    if (lastBytesUploaded > 0)
                    {
                        double bytesPerSecond = lastBytesUploaded / uploadStopwatch.Elapsed.TotalSeconds;
                        if (bytesPerSecond > 0)
                            etaMinutes = (totalBytes - lastBytesUploaded) / bytesPerSecond / 60.0;
                    }
                    yield return ProgressStatus.WithProgress(
                        $"Uploading video... {percentage:F1}% ({mbUploaded:F1} MB / {mbTotal:F1} MB)",
                        percentage, etaMinutes);
                }
            }

            if (totalBytes > 0)
            {
                double mbTotal = totalBytes / (1024.0 * 1024.0);
                yield return ProgressStatus.WithProgress(
                    $"Uploading video... 100.0% ({mbTotal:F1} MB / {mbTotal:F1} MB)", 100);
            }
            else
            {
                yield return ProgressStatus.WithProgress("Uploading video... 100%", 100);
            }

            var uploadStatus = await uploadTask;

            if (uploadStatus.Status == UploadStatus.Failed)
            {
                throw new Exception($"Upload failed: {uploadStatus.Exception?.Message ?? "Unknown error"}");
            }

            // Store the YouTube video ID and clear the resumable URI now that the upload is complete.
            // Only perform these DB updates when we have a confirmed video ID; if the video ID is
            // missing the upload outcome is ambiguous and the URI should be kept for a potential retry.
            if (pipeline != null && !string.IsNullOrEmpty(uploadedVideoId))
            {
                pipeline.YoutubeVideoId = uploadedVideoId;
                pipeline.ResumableUploadUri = string.Empty;
                await _dbContext.SaveChangesAsync(ct);
            }

            yield return ProgressStatus.WithProgress("Upload completed successfully!", 100);

            // Add to playlist if specified
            if (!string.IsNullOrEmpty(options.PlaylistId) && !string.IsNullOrEmpty(uploadedVideoId))
            {
                yield return ProgressStatus.Indeterminate("Adding video to playlist...");
                await AddVideoToPlaylistAsync(youtubeService, uploadedVideoId, options.PlaylistId, ct);
                yield return ProgressStatus.WithProgress("Video added to playlist!", 100);
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

        internal static string SanitizeString(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Untitled Video";
            }

            // Remove emojis and other Unicode symbols (keep basic Latin, Latin-1 Supplement, and common punctuation)
            var sanitized = new StringBuilder();
            foreach (var c in title)
            {
                // Keep alphanumeric, basic punctuation, and extended Latin characters
                if ((c >= 0x20 && c <= 0x7E) ||        // Basic Latin (printable ASCII)
                    (c >= 0xA0 && c <= 0xFF) ||        // Latin-1 Supplement
                    char.IsWhiteSpace(c))
                {
                    sanitized.Append(c);
                }
            }

            // Replace multiple spaces with single space and trim
            var result = Regex.Replace(sanitized.ToString(), @"\s+", " ").Trim();

            // Remove characters that YouTube doesn't allow: < > 
            result = Regex.Replace(result, @"[<>]", "");

            // Ensure the title isn't empty after sanitization
            if (string.IsNullOrWhiteSpace(result))
            {
                return "Untitled Video";
            }

            // YouTube title max length is 100 characters
            if (result.Length > 100)
            {
                result = result.Substring(0, 100).TrimEnd();
            }

            return result;
        }
    }
}

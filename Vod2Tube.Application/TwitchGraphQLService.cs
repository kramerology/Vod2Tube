using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vod2Tube.Application
{
    /// <summary>
    /// Represents a single archived VOD.
    /// </summary>
    public class TwitchVod
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public int LengthSeconds { get; set; }
        public string Url { get; set; } = string.Empty;
        public Game? Game { get; set; }

        public List<VideoMoment> Moments { get; set; } = new List<VideoMoment>();

        public override string ToString()
        {
            return Title;
        }
    }

    /// <summary>
    /// Fetches all archived VODs for a given channel via Twitch's GraphQL endpoint.
    /// </summary>
    public class TwitchGraphQLService
    {
        private const string GqlEndpoint = "https://gql.twitch.tv/gql";
        private readonly HttpClient _http;

        private const string VodsQuery = @"
query ChannelVideos($login: String!, $first: Int!, $after: Cursor, $types: [BroadcastType!]) {
  user(login: $login) {
    videos(first: $first, after: $after, types: $types, sort: TIME) {
      edges {
        cursor
        node {
          id
          title
          publishedAt
          lengthSeconds
          game {
            id
            slug
            displayName
            boxArtURL
          }
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
}";

        private const string MomentsQuery = @"
query VideoMoments($videoId: ID!) {
  video(id: $videoId) {
    id
    moments(momentRequestType: VIDEO_CHAPTER_MARKERS) {
      edges {
        node {
          id
          durationMilliseconds
          positionMilliseconds
          type
          description
          thumbnailURL
          details {
            __typename
            ... on GameChangeMomentDetails {
              game {
                id
                slug
                displayName
                boxArtURL
              }
            }
          }
        }
      }
    }
  }
}";

        public TwitchGraphQLService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Pages through and returns every ARCHIVE-type VOD for <paramref name="channelLogin"/>.
        /// </summary>
        /// <param name="channelLogin">e.g. "ninja"</param>
        /// <param name="pageSize">Max 100</param>
        public async Task<List<TwitchVod>> GetAllVodsAsync(string channelLogin, int pageSize = 100)
        {
            if (string.IsNullOrWhiteSpace(channelLogin))
                throw new ArgumentException("Channel login is required", nameof(channelLogin));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize), "Must be between 1 and 100");

            var result = new List<TwitchVod>();
            string? cursor = null;
            bool hasNext;

            do
            {
                var page = await QueryPageAsync(channelLogin, pageSize, cursor);

                foreach (var edge in page.Edges)
                {
                    var n = edge.Node;
                    result.Add(new TwitchVod
                    {
                        Id = n.Id,
                        Title = n.Title,
                        PublishedAt = n.PublishedAt,
                        LengthSeconds = n.LengthSeconds,
                        Url = $"https://www.twitch.tv/videos/{n.Id}",
                        Game = edge.Node.Game                     
                    });
                }

                hasNext = page.PageInfo.HasNextPage;
                cursor = page.PageInfo.EndCursor;
                await Task.Delay(500);
            }
            while (hasNext);

            await PopulateVodMomentsAsync(result);

            return result;
        }

        private async Task<VideosConnection> QueryPageAsync(string login, int limit, string? cursor)
        {
            var bodyObj = new
            {
                query     = VodsQuery,
                variables = new
                {
                    login = login,
                    first = limit,
                    after = cursor,
                    types = new[] { "ARCHIVE" }
                }
            };

            var json = JsonConvert.SerializeObject(bodyObj);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(GqlEndpoint, content);
            resp.EnsureSuccessStatusCode();

            var raw = await resp.Content.ReadAsStringAsync();
            var obj = JObject.Parse(raw);

            // Check for GraphQL errors
            if (obj["errors"] != null)
            {
                var errorMessage = obj["errors"]?[0]?["message"]?.ToString() ?? "Unknown GraphQL error";
                throw new InvalidOperationException($"GraphQL error: {errorMessage}");
            }

            var userToken = obj["data"]?["user"]
                ?? throw new InvalidOperationException($"Channel '{login}' not found.");
            var vidsToken = userToken["videos"]
                ?? throw new InvalidOperationException($"Unable to retrieve videos for channel '{login}'.");

            var videosConnection = vidsToken.ToObject<VideosConnection>();
            if (videosConnection == null)
            {
                throw new InvalidOperationException($"Failed to deserialize videos for channel '{login}'.");
            }

            return videosConnection;
        }

        public async Task PopulateVodMomentsAsync(List<TwitchVod> vods)
        {
            int batchSize = 35; //Max allowed from testing

            if (vods == null || vods.Count == 0)
                return;

            int numPopulated = 0;

            do
            {
                var batch = vods.Skip(numPopulated).Take(batchSize).Select(v => new
                {
                    query     = MomentsQuery,
                    variables = new { videoId = v.Id }
                }).ToList();

                var content = new StringContent(JsonConvert.SerializeObject(batch), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(GqlEndpoint, content);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var results = JsonConvert.DeserializeObject<List<BatchResponseItem>>(json);
                if (results == null)
                    return;

                // Map returned moments to the corresponding TwitchVod
                foreach (var item in results)
                {
                    var videoData = item.Data?.Video;
                    if (videoData == null)
                        continue;

                    var vod = vods.FirstOrDefault(v => v.Id == videoData.Id);
                    if (vod == null)
                        continue;

                    vod.Moments = videoData.Moments.Edges
                        .Select(e => new VideoMoment
                        {
                            Id = e.Node.Id,
                            DurationMilliseconds = e.Node.DurationMilliseconds,
                            PositionMilliseconds = e.Node.PositionMilliseconds,
                            Type = e.Node.Type,
                            Description = e.Node.Description,
                            ThumbnailUrl = e.Node.ThumbnailURL,
                            Details = e.Node.Details?.Typename == "GameChangeMomentDetails"
                                ? new GameChangeMomentDetails { Game = e.Node.Details.Game }
                                : null
                        })
                        .ToList();
                }

                numPopulated += batch.Count;

                await Task.Delay(500);
            } while (numPopulated < vods.Count);

 
            foreach(var vod in vods)
            {
                vod.Moments = vod.Moments.OrderBy(v => v.PositionMilliseconds).ToList();
            }

         
        }
    }

    public class VideoMoment
    {
        public string Id { get; set; } = string.Empty;
        public long DurationMilliseconds { get; set; }
        public long PositionMilliseconds { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public MomentDetails? Details { get; set; }
    }

    public abstract class MomentDetails { }

    public class GameChangeMomentDetails : MomentDetails
    {
        public Game? Game { get; set; }
    }

    public class Game
    {
        public string Id { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string BoxArtUrl { get; set; } = string.Empty;
    }

    // Batch response item shape
    internal class BatchResponseItem
    {
        [JsonProperty("data")]
        public BatchData? Data { get; set; }
    }

    internal class BatchData
    {
        [JsonProperty("video")]
        public VideoData? Video { get; set; }
    }

    internal class VideoData
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("moments")]
        public MomentConnection Moments { get; set; } = new MomentConnection();
    }

    internal class MomentConnection
    {
        [JsonProperty("edges")]
        public List<MomentEdge> Edges { get; set; } = new List<MomentEdge>();
    }

    internal class MomentEdge
    {
        [JsonProperty("node")]
        public MomentNode Node { get; set; } = new MomentNode();
    }

    internal class MomentNode
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("durationMilliseconds")]
        public long DurationMilliseconds { get; set; }

        [JsonProperty("positionMilliseconds")]
        public long PositionMilliseconds { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("thumbnailURL")]
        public string ThumbnailURL { get; set; } = string.Empty;

        [JsonProperty("details")]
        public MomentDetailsRaw? Details { get; set; }
    }

    internal class MomentDetailsRaw
    {
        [JsonProperty("__typename")]
        public string? Typename { get; set; }

        [JsonProperty("game")]
        public Game? Game { get; set; }
    }


    #region GraphQL paging types (internal)

    internal class VideosConnection
    {
        [JsonProperty("edges")] public List<VideoEdge> Edges { get; set; } = new List<VideoEdge>();
        [JsonProperty("pageInfo")] public PageInfo PageInfo { get; set; } = new PageInfo();
    }

    internal class VideoEdge
    {
        [JsonProperty("cursor")] public string Cursor { get; set; } = string.Empty;
        [JsonProperty("node")] public VideoNode Node { get; set; } = new VideoNode();
    }

    internal class VideoNode
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("title")] public string Title { get; set; } = string.Empty;
        [JsonProperty("publishedAt")] public DateTime PublishedAt { get; set; }
        [JsonProperty("lengthSeconds")] public int LengthSeconds { get; set; }
        [JsonProperty("game")] public Game? Game { get; set; }
    }



    internal class PageInfo
    {
        [JsonProperty("hasNextPage")] public bool HasNextPage { get; set; }
        [JsonProperty("endCursor")] public string? EndCursor { get; set; }
    }

    #endregion
}

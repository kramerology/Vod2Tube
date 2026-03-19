namespace Vod2Tube.Application.Models
{
    public class PipelineJobDto
    {
        public string VodId { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Paused { get; set; }
        public bool Failed { get; set; }
        public string FailReason { get; set; } = string.Empty;
        public int FailCount { get; set; }
        public string YoutubeVideoId { get; set; } = string.Empty;

        // From TwitchVod
        public string Title { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public DateTime CreatedAtUTC { get; set; }
        public TimeSpan Duration { get; set; }
        public string VodUrl { get; set; } = string.Empty;
        public DateTime AddedAtUTC { get; set; }
    }
}

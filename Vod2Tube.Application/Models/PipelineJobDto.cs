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

        /// <summary>Percentage complete of the current stage (0–100), or <c>null</c> when indeterminate.</summary>
        public double? PercentComplete { get; set; }

        /// <summary>Estimated minutes remaining for the current stage, or <c>null</c> when unknown.</summary>
        public double? EstimatedMinutesRemaining { get; set; }

        // From TwitchVod
        public string Title { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public DateTime CreatedAtUTC { get; set; }
        public TimeSpan Duration { get; set; }
        public string VodUrl { get; set; } = string.Empty;
        public DateTime AddedAtUTC { get; set; }
    }
}

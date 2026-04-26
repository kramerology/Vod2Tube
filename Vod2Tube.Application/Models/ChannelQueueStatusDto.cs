namespace Vod2Tube.Application.Models
{
    public class ChannelQueueStatusDto
    {
        public int Id { get; set; }

        public string ChannelName { get; set; } = string.Empty;

        public DateTime AddedAtUTC { get; set; }

        public DateTime? LastQueueCheckAtUTC { get; set; }

        public string? LastQueuedVodId { get; set; }

        public bool Active { get; set; }

        public int? YouTubeAccountId { get; set; }

        public string? CurrentVodId { get; set; }

        public string? CurrentVodTitle { get; set; }

        public string? CurrentStage { get; set; }

        public bool CurrentJobFailed { get; set; }

        public bool CurrentJobPaused { get; set; }
    }
}

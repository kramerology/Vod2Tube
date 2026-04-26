namespace Vod2Tube.Domain
{
    public class Channel
    {
        public int Id { get; set; } // Auto-generated Id

        public string ChannelName { get; set; } = string.Empty;

        public DateTime AddedAtUTC { get; set; }

        public DateTime? LastQueueCheckAtUTC { get; set; }

        public string? LastQueuedVodId { get; set; }

        public bool Active { get; set; }

        /// <summary>
        /// Optional FK to the YouTube account used for uploads from this channel.
        /// When null, no account is assigned and the upload stage is skipped.
        /// </summary>
        public int? YouTubeAccountId { get; set; }
    }
}


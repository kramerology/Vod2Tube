namespace Vod2Tube.Domain
{
    public class Channel
    {
        public int Id { get; set; } // Auto-generated Id

        public string ChannelName { get; set; } = string.Empty;

        public DateTime AddedAtUTC { get; set; }

        public bool Active { get; set; }
    }
}

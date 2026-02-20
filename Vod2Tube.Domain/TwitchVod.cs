using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vod2Tube.Domain
{
    public class TwitchVod
    {
        public string Id { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAtUTC { get; set; }
        public TimeSpan Duration { get; set; }
        public string Url { get; set; } = string.Empty;
        public DateTime AddedAtUTC { get; set; }
    }
}

using System;
using System.Globalization;

namespace Vod2Tube.Domain
{
    public class Pipeline
    {
        public string VodId { get; set; }

        public string Stage { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string VodFilePath { get; set; } = string.Empty;
        public string ChatTextFilePath { get; set; } = string.Empty;
        public string ChatVideoFilePath { get; set; } = string.Empty;
        public string FinalVideoFilePath { get; set; } = string.Empty;
        public string YoutubeVideoId { get; set; } = string.Empty;

        public bool Failed { get; set; } = false;
        public string FailReason { get; set; } = string.Empty;
        public int FailCount { get; set; } = 0;
    }
}

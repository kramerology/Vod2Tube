namespace Vod2Tube.Application
{
    /// <summary>
    /// Typed settings that are persisted in the Settings table and injected via
    /// <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{TOptions}"/>.
    /// </summary>
    public class AppSettings
    {
        // ── Tool paths ────────────────────────────────────────────────────────

        public string TwitchDownloaderCliPath { get; set; } = "TwitchDownloaderCLI";
        public string FfmpegPath { get; set; } = "ffmpeg";
        public string FfprobePath { get; set; } = "ffprobe";
        public string YtDlpPath { get; set; } = "yt-dlp";

        // ── Storage directories ───────────────────────────────────────────────

        public string VodDownloadTempDir { get; set; } = "VodDownloadsTemp";
        public string VodDownloadDir { get; set; } = "VodDownloads";
        public string ChatRenderTempDir { get; set; } = "ChatRenderTemp";
        public string ChatRenderDir { get; set; } = "ChatRenders";
        public string FinalVideoDir { get; set; } = "FinalVideos";

        // ── Chat rendering ────────────────────────────────────────────────────

        public int ChatWidth { get; set; } = 350;
        public int ChatFontSize { get; set; } = 15;
        public int ChatUpdateRate { get; set; } = 0;

        // ── Serialisation helpers ─────────────────────────────────────────────

        /// <summary>
        /// Applies a dictionary of raw key/value pairs (from the DB) onto an
        /// <see cref="AppSettings"/> instance.  Unknown keys are ignored.
        /// </summary>
        public static void ApplyDictionary(AppSettings opts, Dictionary<string, string> dict)
        {
            if (dict.TryGetValue(nameof(TwitchDownloaderCliPath), out var v)) opts.TwitchDownloaderCliPath = v;
            if (dict.TryGetValue(nameof(FfmpegPath),              out v))     opts.FfmpegPath              = v;
            if (dict.TryGetValue(nameof(FfprobePath),             out v))     opts.FfprobePath             = v;
            if (dict.TryGetValue(nameof(YtDlpPath),               out v))     opts.YtDlpPath               = v;

            if (dict.TryGetValue(nameof(VodDownloadTempDir), out v)) opts.VodDownloadTempDir = v;
            if (dict.TryGetValue(nameof(VodDownloadDir),     out v)) opts.VodDownloadDir     = v;
            if (dict.TryGetValue(nameof(ChatRenderTempDir),  out v)) opts.ChatRenderTempDir  = v;
            if (dict.TryGetValue(nameof(ChatRenderDir),      out v)) opts.ChatRenderDir      = v;
            if (dict.TryGetValue(nameof(FinalVideoDir),      out v)) opts.FinalVideoDir      = v;

            if (dict.TryGetValue(nameof(ChatWidth),      out v) && int.TryParse(v, out var i)) opts.ChatWidth      = i;
            if (dict.TryGetValue(nameof(ChatFontSize),   out v) && int.TryParse(v, out i))     opts.ChatFontSize   = i;
            if (dict.TryGetValue(nameof(ChatUpdateRate), out v) && int.TryParse(v, out i))     opts.ChatUpdateRate = i;
        }

        /// <summary>
        /// Converts the current settings to a flat key/value dictionary suitable
        /// for persistence in the DB.
        /// </summary>
        public Dictionary<string, string> ToDictionary() => new()
        {
            [nameof(TwitchDownloaderCliPath)] = TwitchDownloaderCliPath,
            [nameof(FfmpegPath)]              = FfmpegPath,
            [nameof(FfprobePath)]             = FfprobePath,
            [nameof(YtDlpPath)]               = YtDlpPath,

            [nameof(VodDownloadTempDir)] = VodDownloadTempDir,
            [nameof(VodDownloadDir)]     = VodDownloadDir,
            [nameof(ChatRenderTempDir)]  = ChatRenderTempDir,
            [nameof(ChatRenderDir)]      = ChatRenderDir,
            [nameof(FinalVideoDir)]      = FinalVideoDir,

            [nameof(ChatWidth)]      = ChatWidth.ToString(),
            [nameof(ChatFontSize)]   = ChatFontSize.ToString(),
            [nameof(ChatUpdateRate)] = ChatUpdateRate.ToString(),
        };
    }
}

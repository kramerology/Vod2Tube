namespace Vod2Tube.Application
{
    /// <summary>
    /// Typed settings that are persisted in the Settings table and injected via
    /// <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{TOptions}"/>.
    /// </summary>
    public class AppSettings
    {
        // ── Tool paths ────────────────────────────────────────────────────────
        // Defaults point to a "tools" folder next to the application binary.
        // Append .exe on Windows; no extension on Linux/macOS.

        private static string ToolPath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "tools",
                OperatingSystem.IsWindows() ? name + ".exe" : name);

        public string TwitchDownloaderCliPath { get; set; } = ToolPath("TwitchDownloaderCLI");
        public string FfmpegPath { get; set; } = ToolPath("ffmpeg");
        public string FfprobePath { get; set; } = ToolPath("ffprobe");
        public string YtDlpPath { get; set; } = ToolPath("yt-dlp");

        // ── Storage directories ───────────────────────────────────────────────
        // Defaults are placed under a "storage" sub-folder next to the
        // application binary so all data stays in one predictable location.

        public string TempDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage", "temp");
        public string VodDownloadDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage", "downloads");
        public string ChatRenderDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage", "renders");
        public string FinalVideoDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage", "output");

        // ── Chat rendering ────────────────────────────────────────────────────

        public int ChatWidth { get; set; } = 350;
        public int ChatFontSize { get; set; } = 15;
        public int ChatUpdateRate { get; set; } = 0;

        // ── Archiving ────────────────────────────────────────────────────────────────────
        // When enabled, the corresponding file is copied to the specified archive
        // directory after upload completes.  Files that are not archived are
        // deleted from the working storage directories.

        public bool ArchiveVodEnabled { get; set; } = false;
        public string ArchiveVodDir { get; set; } = string.Empty;

        public bool ArchiveChatJsonEnabled { get; set; } = false;
        public string ArchiveChatJsonDir { get; set; } = string.Empty;

        public bool ArchiveChatRenderEnabled { get; set; } = false;
        public string ArchiveChatRenderDir { get; set; } = string.Empty;

        public bool ArchiveFinalVideoEnabled { get; set; } = false;
        public string ArchiveFinalVideoDir { get; set; } = string.Empty;

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

            if (dict.TryGetValue(nameof(TempDir),       out v)) opts.TempDir       = v;
            if (dict.TryGetValue(nameof(VodDownloadDir), out v)) opts.VodDownloadDir = v;
            if (dict.TryGetValue(nameof(ChatRenderDir),  out v)) opts.ChatRenderDir  = v;
            if (dict.TryGetValue(nameof(FinalVideoDir),  out v)) opts.FinalVideoDir  = v;

            if (dict.TryGetValue(nameof(ChatWidth),      out v) && int.TryParse(v, out var i)) opts.ChatWidth      = i;
            if (dict.TryGetValue(nameof(ChatFontSize),   out v) && int.TryParse(v, out i))     opts.ChatFontSize   = i;
            if (dict.TryGetValue(nameof(ChatUpdateRate), out v) && int.TryParse(v, out i))     opts.ChatUpdateRate = i;

            if (dict.TryGetValue(nameof(ArchiveVodEnabled),       out v) && bool.TryParse(v, out var b)) opts.ArchiveVodEnabled       = b;
            if (dict.TryGetValue(nameof(ArchiveVodDir),           out v))                                opts.ArchiveVodDir           = v;
            if (dict.TryGetValue(nameof(ArchiveChatJsonEnabled),   out v) && bool.TryParse(v, out b))    opts.ArchiveChatJsonEnabled   = b;
            if (dict.TryGetValue(nameof(ArchiveChatJsonDir),       out v))                               opts.ArchiveChatJsonDir       = v;
            if (dict.TryGetValue(nameof(ArchiveChatRenderEnabled), out v) && bool.TryParse(v, out b))    opts.ArchiveChatRenderEnabled = b;
            if (dict.TryGetValue(nameof(ArchiveChatRenderDir),     out v))                               opts.ArchiveChatRenderDir     = v;
            if (dict.TryGetValue(nameof(ArchiveFinalVideoEnabled), out v) && bool.TryParse(v, out b))    opts.ArchiveFinalVideoEnabled = b;
            if (dict.TryGetValue(nameof(ArchiveFinalVideoDir),     out v))                               opts.ArchiveFinalVideoDir     = v;
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

            [nameof(TempDir)]       = TempDir,
            [nameof(VodDownloadDir)] = VodDownloadDir,
            [nameof(ChatRenderDir)]  = ChatRenderDir,
            [nameof(FinalVideoDir)]  = FinalVideoDir,

            [nameof(ChatWidth)]      = ChatWidth.ToString(),
            [nameof(ChatFontSize)]   = ChatFontSize.ToString(),
            [nameof(ChatUpdateRate)] = ChatUpdateRate.ToString(),

            [nameof(ArchiveVodEnabled)]       = ArchiveVodEnabled.ToString(),
            [nameof(ArchiveVodDir)]           = ArchiveVodDir,
            [nameof(ArchiveChatJsonEnabled)]   = ArchiveChatJsonEnabled.ToString(),
            [nameof(ArchiveChatJsonDir)]       = ArchiveChatJsonDir,
            [nameof(ArchiveChatRenderEnabled)] = ArchiveChatRenderEnabled.ToString(),
            [nameof(ArchiveChatRenderDir)]     = ArchiveChatRenderDir,
            [nameof(ArchiveFinalVideoEnabled)] = ArchiveFinalVideoEnabled.ToString(),
            [nameof(ArchiveFinalVideoDir)]     = ArchiveFinalVideoDir,
        };
    }
}

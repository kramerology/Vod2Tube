namespace Vod2Tube.Application.Models
{
    /// <summary>
    /// Universal progress object yielded by every pipeline stage.
    /// </summary>
    public sealed class ProgressStatus
    {
        /// <summary>Human-readable description of what is happening right now.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>Percentage complete (0–100), or <c>null</c> when indeterminate.</summary>
        public double? PercentComplete { get; init; }

        /// <summary>Estimated minutes remaining, or <c>null</c> when unknown.</summary>
        public double? EstimatedMinutesRemaining { get; init; }

        /// <summary>Creates a simple indeterminate status with only a message.</summary>
        public static ProgressStatus Indeterminate(string message) =>
            new() { Message = message };

        /// <summary>Creates a status with a known percentage and optional ETA.</summary>
        public static ProgressStatus WithProgress(string message, double percent, double? etaMinutes = null) =>
            new()
            {
                Message = message,
                PercentComplete = Math.Clamp(percent, 0, 100),
                EstimatedMinutesRemaining = etaMinutes is > 0 ? etaMinutes : null
            };
    }
}

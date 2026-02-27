namespace Vod2Tube.Application
{
    /// <summary>
    /// Thrown by a pipeline worker to signal that a job has failed.
    /// Set <see cref="IsPermanent"/> to <see langword="true"/> when the failure
    /// should not be retried (the job will be marked <c>Failed</c> immediately).
    /// Leave it <see langword="false"/> for transient failures that may succeed on
    /// a subsequent attempt (the job will be attempted up to 3 times in total
    /// before being marked <c>Failed</c>).
    /// </summary>
    public class PipelineJobException : Exception
    {
        /// <summary>
        /// When <see langword="true"/> the <see cref="JobManager"/> will mark the
        /// job as permanently failed without scheduling a retry.
        /// </summary>
        public bool IsPermanent { get; }

        public PipelineJobException(string message, bool isPermanent = false)
            : base(message)
        {
            IsPermanent = isPermanent;
        }

        public PipelineJobException(string message, Exception innerException, bool isPermanent = false)
            : base(message, innerException)
        {
            IsPermanent = isPermanent;
        }
    }
}

using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// The outcome and retry disposition of a non-native cleanup stage.
    /// </summary>
    public sealed class CleanupStageResult
    {
        private CleanupStageResult(CleanupFailureDisposition disposition, Exception exception)
        {
            if (disposition == CleanupFailureDisposition.None && exception != null)
                throw new ArgumentException("A successful cleanup result cannot contain an exception.", nameof(exception));
            if (disposition != CleanupFailureDisposition.None && exception == null)
                throw new ArgumentNullException(nameof(exception));

            Disposition = disposition;
            Exception = exception;
        }

        public CleanupFailureDisposition Disposition { get; }

        public Exception Exception { get; }

        public bool IsSuccess => Disposition == CleanupFailureDisposition.None;

        public static CleanupStageResult Succeeded()
        {
            return new CleanupStageResult(CleanupFailureDisposition.None, null);
        }

        public static CleanupStageResult RetryableFailure(Exception exception)
        {
            return new CleanupStageResult(CleanupFailureDisposition.Retryable, exception);
        }

        public static CleanupStageResult NonRetryableFailure(Exception exception)
        {
            return new CleanupStageResult(CleanupFailureDisposition.NonRetryable, exception);
        }
    }
}

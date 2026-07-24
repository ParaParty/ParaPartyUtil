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

        /// <summary>Gets whether a failed stage may safely be attempted again.</summary>
        public CleanupFailureDisposition Disposition { get; }

        /// <summary>Gets the failure that prevented completion, or <see langword="null"/> on success.</summary>
        public Exception Exception { get; }

        /// <summary>Gets whether the stage completed successfully.</summary>
        public bool IsSuccess => Disposition == CleanupFailureDisposition.None;

        /// <summary>Creates a result that marks the stage complete.</summary>
        /// <returns>A successful stage result.</returns>
        public static CleanupStageResult Succeeded()
        {
            return new CleanupStageResult(CleanupFailureDisposition.None, null);
        }

        /// <summary>Creates a failure result for a stage that remains safe to retry.</summary>
        /// <param name="exception">The failure reported by the stage.</param>
        /// <returns>A retryable failure result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
        public static CleanupStageResult RetryableFailure(Exception exception)
        {
            return new CleanupStageResult(CleanupFailureDisposition.Retryable, exception);
        }

        /// <summary>Creates a stable failure result for a stage that must not run again.</summary>
        /// <param name="exception">The failure reported by the stage.</param>
        /// <returns>A non-retryable failure result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
        public static CleanupStageResult NonRetryableFailure(Exception exception)
        {
            return new CleanupStageResult(CleanupFailureDisposition.NonRetryable, exception);
        }
    }
}

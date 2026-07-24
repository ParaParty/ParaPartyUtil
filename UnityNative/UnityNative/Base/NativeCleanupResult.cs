using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// A native cleanup result that never infers liveness from a pointer value.
    /// </summary>
    public sealed class NativeCleanupResult
    {
        private NativeCleanupResult(
            NativeResourceLiveness liveness,
            CleanupFailureDisposition disposition,
            Exception exception)
        {
            if (disposition == CleanupFailureDisposition.None &&
                (liveness != NativeResourceLiveness.Freed || exception != null))
            {
                throw new ArgumentException("Successful native cleanup must explicitly report Freed.");
            }

            if (disposition != CleanupFailureDisposition.None && exception == null)
                throw new ArgumentNullException(nameof(exception));
            if (disposition == CleanupFailureDisposition.Retryable && liveness != NativeResourceLiveness.KnownLive)
                throw new ArgumentException("Retryable native cleanup must explicitly prove that the resource is still live.");

            Liveness = liveness;
            Disposition = disposition;
            Exception = exception;
        }

        /// <summary>Gets the cleanup oracle's evidence about the native resource.</summary>
        public NativeResourceLiveness Liveness { get; }

        /// <summary>Gets whether another cleanup attempt is safe.</summary>
        public CleanupFailureDisposition Disposition { get; }

        /// <summary>Gets the native cleanup failure, or <see langword="null"/> on success.</summary>
        public Exception Exception { get; }

        /// <summary>Gets whether cleanup explicitly proved that the resource was freed.</summary>
        public bool IsSuccess => Disposition == CleanupFailureDisposition.None;

        /// <summary>Creates a successful result with explicit proof that the resource was freed.</summary>
        /// <returns>A successful native cleanup result.</returns>
        public static NativeCleanupResult Freed()
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.Freed,
                CleanupFailureDisposition.None,
                null);
        }

        /// <summary>Creates a retryable failure with explicit proof that the resource remains live.</summary>
        /// <param name="exception">The failure that occurred before native destruction.</param>
        /// <returns>A known-live, retryable result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
        public static NativeCleanupResult KnownLiveRetryableFailure(Exception exception)
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.KnownLive,
                CleanupFailureDisposition.Retryable,
                exception);
        }

        /// <summary>Creates a stable failure while preserving proof that the resource remains live.</summary>
        /// <param name="exception">The failure that makes retry unsupported.</param>
        /// <returns>A known-live, non-retryable result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
        public static NativeCleanupResult KnownLiveNonRetryableFailure(Exception exception)
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.KnownLive,
                CleanupFailureDisposition.NonRetryable,
                exception);
        }

        /// <summary>Creates a stable failure when native liveness cannot be proven.</summary>
        /// <param name="exception">The failure observed at or after an uncertain destruction boundary.</param>
        /// <returns>An unknown-liveness, non-retryable result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
        public static NativeCleanupResult LivenessUnknownFailure(Exception exception)
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.Unknown,
                CleanupFailureDisposition.NonRetryable,
                exception);
        }
    }
}

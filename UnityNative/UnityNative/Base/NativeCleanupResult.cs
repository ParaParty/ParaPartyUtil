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

        public NativeResourceLiveness Liveness { get; }

        public CleanupFailureDisposition Disposition { get; }

        public Exception Exception { get; }

        public bool IsSuccess => Disposition == CleanupFailureDisposition.None;

        public static NativeCleanupResult Freed()
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.Freed,
                CleanupFailureDisposition.None,
                null);
        }

        public static NativeCleanupResult KnownLiveRetryableFailure(Exception exception)
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.KnownLive,
                CleanupFailureDisposition.Retryable,
                exception);
        }

        public static NativeCleanupResult KnownLiveNonRetryableFailure(Exception exception)
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.KnownLive,
                CleanupFailureDisposition.NonRetryable,
                exception);
        }

        public static NativeCleanupResult LivenessUnknownFailure(Exception exception)
        {
            return new NativeCleanupResult(
                NativeResourceLiveness.Unknown,
                CleanupFailureDisposition.NonRetryable,
                exception);
        }
    }
}

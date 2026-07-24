using System;
using System.Collections.Generic;

namespace Paraparty.UnityNative.Base
{
    public enum NativeOwnershipKind
    {
        Owned = 0,
        Borrowed = 1,
    }

    public enum NativeLifecycleState
    {
        Active = 0,
        Disposing = 1,
        DisposeFaulted = 2,
        Disposed = 3,
        Transferred = 4,
    }

    [Flags]
    public enum CleanupStage
    {
        None = 0,
        Managed = 1 << 0,
        CallbackFence = 1 << 1,
        Native = 1 << 2,
        OwnerUnpublish = 1 << 3,
        All = Managed | CallbackFence | Native | OwnerUnpublish,
    }

    public enum CleanupFailureDisposition
    {
        None = 0,
        Retryable = 1,
        NonRetryable = 2,
    }

    public enum NativeResourceLiveness
    {
        KnownLive = 0,
        Freed = 1,
        Unknown = 2,
    }

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

    public sealed class NativeCleanupException : AggregateException
    {
        internal NativeCleanupException(
            long attemptEpoch,
            NativeLifecycleState lifecycleState,
            CleanupStage completedStages,
            CleanupStage failedStages,
            NativeResourceLiveness nativeLiveness,
            bool isRetryable,
            IEnumerable<Exception> innerExceptions)
            : base(
                CreateMessage(
                    attemptEpoch,
                    lifecycleState,
                    completedStages,
                    failedStages,
                    nativeLiveness,
                    isRetryable),
                innerExceptions)
        {
            AttemptEpoch = attemptEpoch;
            LifecycleState = lifecycleState;
            CompletedStages = completedStages;
            FailedStages = failedStages;
            NativeLiveness = nativeLiveness;
            IsRetryable = isRetryable;
        }

        public long AttemptEpoch { get; }

        public NativeLifecycleState LifecycleState { get; }

        public CleanupStage CompletedStages { get; }

        public CleanupStage FailedStages { get; }

        public NativeResourceLiveness NativeLiveness { get; }

        public bool IsRetryable { get; }

        private static string CreateMessage(
            long attemptEpoch,
            NativeLifecycleState lifecycleState,
            CleanupStage completedStages,
            CleanupStage failedStages,
            NativeResourceLiveness nativeLiveness,
            bool isRetryable)
        {
            return "Native cleanup attempt " + attemptEpoch +
                   " ended in " + lifecycleState +
                   ". Completed=" + completedStages +
                   ", Failed=" + failedStages +
                   ", NativeLiveness=" + nativeLiveness +
                   ", Retryable=" + isRetryable + ".";
        }
    }
}

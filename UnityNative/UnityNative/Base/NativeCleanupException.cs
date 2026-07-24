using System;
using System.Collections.Generic;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// A stable cleanup diagnosis observed by every caller of one attempt.
    /// </summary>
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

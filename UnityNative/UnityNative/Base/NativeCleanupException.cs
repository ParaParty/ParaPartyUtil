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

        /// <summary>Gets the monotonic identity of the cleanup attempt.</summary>
        public long AttemptEpoch { get; }

        /// <summary>Gets the lifecycle state recorded when the attempt completed.</summary>
        public NativeLifecycleState LifecycleState { get; }

        /// <summary>Gets the stages completed across this and earlier attempts.</summary>
        public CleanupStage CompletedStages { get; }

        /// <summary>Gets the stages that reported failures during the attempt.</summary>
        public CleanupStage FailedStages { get; }

        /// <summary>Gets the last proven native-resource liveness.</summary>
        public NativeResourceLiveness NativeLiveness { get; }

        /// <summary>Gets whether a later disposal attempt may safely retry incomplete stages.</summary>
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

using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Truthful cleanup telemetry. Liveness is never inferred from the pointer value.
    /// </summary>
    public sealed class NativeCleanupDiagnostic
    {
        internal NativeCleanupDiagnostic(
            string objectType,
            NativeLifecycleState lifecycleState,
            CleanupStage completedStages,
            NativeOwnershipKind ownership,
            NativeResourceLiveness nativeLiveness,
            long attemptEpoch,
            bool isFinalizer,
            Exception exception)
        {
            ObjectType = objectType;
            LifecycleState = lifecycleState;
            CompletedStages = completedStages;
            Ownership = ownership;
            NativeLiveness = nativeLiveness;
            AttemptEpoch = attemptEpoch;
            IsFinalizer = isFinalizer;
            Exception = exception;
        }

        /// <summary>Gets the managed type whose cleanup remained incomplete.</summary>
        public string ObjectType { get; }

        /// <summary>Gets the lifecycle state observed by the diagnostic sink.</summary>
        public NativeLifecycleState LifecycleState { get; }

        /// <summary>Gets the stages proven complete when the diagnostic was emitted.</summary>
        public CleanupStage CompletedStages { get; }

        /// <summary>Gets whether the wrapper owned or borrowed its native resource.</summary>
        public NativeOwnershipKind Ownership { get; }

        /// <summary>Gets the cleanup oracle's last liveness evidence.</summary>
        public NativeResourceLiveness NativeLiveness { get; }

        /// <summary>Gets the monotonic cleanup-attempt identity.</summary>
        public long AttemptEpoch { get; }

        /// <summary>Gets whether the diagnostic was emitted by finalizer execution.</summary>
        public bool IsFinalizer { get; }

        /// <summary>Gets the contained cleanup failure, if one was available.</summary>
        public Exception Exception { get; }
    }
}

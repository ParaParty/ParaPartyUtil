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

        public string ObjectType { get; }

        public NativeLifecycleState LifecycleState { get; }

        public CleanupStage CompletedStages { get; }

        public NativeOwnershipKind Ownership { get; }

        public NativeResourceLiveness NativeLiveness { get; }

        public long AttemptEpoch { get; }

        public bool IsFinalizer { get; }

        public Exception Exception { get; }
    }
}

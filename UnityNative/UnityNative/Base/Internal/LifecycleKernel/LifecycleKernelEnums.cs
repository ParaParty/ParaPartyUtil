namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal enum KernelOwnership
    {
        Owned,
        Borrowed
    }

    internal enum KernelLife
    {
        Active,
        Disposing,
        FaultRetryable,
        FaultStable,
        Disposed,
        Transferred
    }

    internal enum KernelPublication
    {
        Never,
        Published,
        Unpublished
    }

    internal enum KernelNativeAuthority
    {
        Absent,
        OwnedLive,
        BorrowedExternal,
        Unknown,
        Freed,
        TicketOwned
    }

    internal enum KernelAdmission
    {
        Open,
        Closing,
        Closed
    }

    internal enum KernelAttemptState
    {
        Idle,
        Running,
        RetryableFault,
        StableFault,
        Complete
    }

    internal enum KernelAttemptOutcome
    {
        Running,
        Success,
        RetryableFailure,
        StableFailure
    }

    internal enum KernelTransferState
    {
        None,
        Preparing,
        Pending,
        HandoffInUse,
        Committed,
        Completed,
        RollbackDraining,
        RollingBack,
        RollbackRetryable,
        RollbackStable,
        RolledBack
    }

    internal enum KernelResourceKind
    {
        GCHandle,
        UnmanagedMemory
    }

    internal enum KernelResourcePhase
    {
        Empty,
        Reserved,
        Live,
        Detached,
        Released,
        Mixed
    }

    internal enum KernelStage
    {
        Managed,
        NativeQuiesce,
        CallbackFence,
        NativeAuthority,
        LegacyUnpublishNotification,
        BaseResources
    }

    internal enum KernelCalloutKind
    {
        Cleanup,
        Tracker,
        TransferPreparation,
        Rollback
    }

    internal enum KernelAttemptKind
    {
        Disposal,
        Transfer,
        Rollback
    }

    internal enum KernelOrphanRecoveryOutcome
    {
        Recovered,
        RetryableRetained,
        StableRetained
    }

    internal enum KernelTransitionCode
    {
        Accepted,
        IllegalState,
        InvalidToken,
        StaleReceipt,
        SameCausalAttempt,
        CycleRisk,
        StableFailure,
        PendingWork
    }
}

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class LifecycleKernelSnapshot
    {
        internal KernelOwnership Ownership { get; set; }
        internal KernelLife Life { get; set; }
        internal KernelPublication Publication { get; set; }
        internal KernelNativeAuthority NativeAuthority { get; set; }
        internal KernelAdmission Admission { get; set; }
        internal KernelAttemptState DisposalAttempt { get; set; }
        internal KernelTransferState Transfer { get; set; }
        internal KernelResourcePhase GCHandlePhase { get; set; }
        internal KernelResourcePhase UnmanagedMemoryPhase { get; set; }
        internal long OwnerId { get; set; }
        internal long LifecycleId { get; set; }
        internal long PublicationGeneration { get; set; }
        internal long DisposalAttemptId { get; set; }
        internal long TransferAttemptId { get; set; }
        internal long RollbackAttemptId { get; set; }
        internal int LeaseCount { get; set; }
        internal int CallbackCount { get; set; }
        internal int TicketUseCount { get; set; }
        internal int CompletedStageCount { get; set; }
        internal int DisposalRecordCount { get; set; }
        internal int RollbackRecordCount { get; set; }
        internal bool WrapperRoot { get; set; }
        internal bool TrackerRoot { get; set; }
        internal bool OrphanRoot { get; set; }
        internal bool TicketRoot { get; set; }
        internal bool FinalizerSeen { get; set; }
    }
}

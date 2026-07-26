using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelTransferTests
{
    [TestMethod]
    public void TicketUseBlocksCommitAndRollbackDestructionUntilExactDrain()
    {
        var kernel = LifecycleKernelHarness.New();
        var ticket = kernel.BeginAndCommitTransfer();
        var use = kernel.Value(kernel.Accept("EnterTicketUse", ticket));
        kernel.Reject("IllegalState", "CommitTicket", ticket);
        var rollback = kernel.StartRollbackAttempt(ticket)[0];
        Assert.AreEqual("RollbackDraining", kernel.State("Transfer"));
        kernel.Reject("PendingWork", "FinishRollbackDrain", rollback);
        kernel.Accept("ReleaseTicketUse", use);
        kernel.Accept("FinishRollbackDrain", rollback);
        Assert.AreEqual("RollingBack", kernel.State("Transfer"));
        kernel.Accept("CompleteRollback", rollback, kernel.EnumValue("KernelAttemptOutcome", "Success"));
        Assert.AreEqual("RolledBack", kernel.State("Transfer"));
        Assert.AreEqual("Freed", kernel.State("NativeAuthority"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void RollbackJoinResultSurvivesRetryAndStableFailureCannotRetry()
    {
        var kernel = LifecycleKernelHarness.New();
        var ticket = kernel.BeginAndCommitTransfer();
        var first = kernel.StartRollbackAttempt(ticket)[0];
        kernel.Accept("JoinRollback", "rollback-waiter", null, null);
        var retryable = kernel.Value(kernel.Accept(
            "CompleteRollback", first,
            kernel.EnumValue("KernelAttemptOutcome", "RetryableFailure"))).ToString();
        Assert.AreEqual(retryable, kernel.Call("ObserveRollbackResult", 1L, "rollback-waiter"));

        var second = kernel.StartRollbackAttempt(ticket)[0];
        var stable = kernel.Value(kernel.Accept(
            "CompleteRollback", second,
            kernel.EnumValue("KernelAttemptOutcome", "StableFailure"))).ToString();
        var rejection = kernel.Reject("StableFailure", "StartRollback", ticket, null, null);
        Assert.AreEqual(stable, LifecycleKernelHarness.Text(rejection, "Detail"));
        Assert.AreEqual(retryable, kernel.Call("ObserveRollbackResult", 1L, "rollback-waiter"));
        Assert.AreEqual(2L, kernel.Number("RollbackRecordCount"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void PendingTicketCanCommitAndCompleteWithZeroRoots()
    {
        var kernel = LifecycleKernelHarness.New();
        var ticket = kernel.BeginAndCommitTransfer();
        kernel.Accept("CommitTicket", ticket);
        kernel.Accept("CompleteTicket", ticket);
        Assert.AreEqual("Completed", kernel.State("Transfer"));
        Assert.IsFalse(kernel.Flag("WrapperRoot"));
        Assert.IsFalse(kernel.Flag("TrackerRoot"));
        Assert.IsFalse(kernel.Flag("OrphanRoot"));
        Assert.IsFalse(kernel.Flag("TicketRoot"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void TransferRejectsTrackerResourceAndAdmissionWitnesses()
    {
        var tracked = LifecycleKernelHarness.New();
        tracked.Accept("RegisterTracker");
        tracked.Reject("IllegalState", "BeginTransfer", null, null);

        var resource = LifecycleKernelHarness.New();
        var kind = resource.EnumValue("KernelResourceKind", "GCHandle");
        resource.Accept("ReserveResource", kind);
        resource.Reject("IllegalState", "BeginTransfer", null, null);

        var admitted = LifecycleKernelHarness.New();
        admitted.Accept("EnterOperation");
        admitted.Reject("IllegalState", "BeginTransfer", null, null);
    }

    [TestMethod]
    public void LateRollbackCompletionCannotCompleteANewerAttempt()
    {
        var kernel = LifecycleKernelHarness.New();
        var ticket = kernel.BeginAndCommitTransfer();
        var first = kernel.StartRollbackAttempt(ticket)[0];
        var foreignKernel = LifecycleKernelHarness.New();
        var foreignTicket = foreignKernel.BeginAndCommitTransfer();
        var foreign = foreignKernel.StartRollbackAttempt(foreignTicket)[0];
        var beforeForeign = kernel.SnapshotFingerprint();
        kernel.Reject(
            "StaleReceipt", "CompleteRollback", foreign,
            kernel.EnumValue("KernelAttemptOutcome", "Success"));
        Assert.AreEqual(beforeForeign, kernel.SnapshotFingerprint());
        kernel.Accept(
            "CompleteRollback", first,
            kernel.EnumValue("KernelAttemptOutcome", "RetryableFailure"));
        var second = kernel.StartRollbackAttempt(ticket)[0];

        var before = kernel.SnapshotFingerprint();
        kernel.Reject(
            "StaleReceipt", "CompleteRollback", first,
            kernel.EnumValue("KernelAttemptOutcome", "Success"));
        Assert.AreEqual(before, kernel.SnapshotFingerprint());
        kernel.Accept(
            "CompleteRollback", second,
            kernel.EnumValue("KernelAttemptOutcome", "Success"));
        var terminal = kernel.SnapshotFingerprint();
        kernel.Reject(
            "StaleReceipt", "CompleteRollback", second,
            kernel.EnumValue("KernelAttemptOutcome", "Success"));
        Assert.AreEqual(terminal, kernel.SnapshotFingerprint());
        Assert.AreEqual("RolledBack", kernel.State("Transfer"));
        Assert.AreEqual("Freed", kernel.State("NativeAuthority"));
    }

    [DataTestMethod]
    [DataRow("CommitTransfer")]
    [DataRow("FailTransferPreparation")]
    [DataRow("AbortTransfer")]
    public void TransferPreparationResultsRequireTheirExactUnconsumedReceipt(string operation)
    {
        var owner = LifecycleKernelHarness.New();
        var own = owner.BeginTransferPreparation()[0];
        var foreign = LifecycleKernelHarness.New().BeginTransferPreparation()[0];

        var beforeForeign = owner.SnapshotFingerprint();
        owner.Reject("StaleReceipt", operation, foreign);
        Assert.AreEqual(beforeForeign, owner.SnapshotFingerprint());

        owner.Accept(operation, own);
        var afterConsumed = owner.SnapshotFingerprint();
        owner.Reject("StaleReceipt", operation, own);
        Assert.AreEqual(afterConsumed, owner.SnapshotFingerprint());
        owner.AssertValid();
    }

    [TestMethod]
    public void TransferFailureAndAbortRestoreKnownLiveOwnerAndRejectLateCommit()
    {
        var failed = LifecycleKernelHarness.New();
        var failure = failed.BeginTransferPreparation()[0];
        failed.Accept("FailTransferPreparation", failure);
        Assert.AreEqual("Active", failed.State("Life"));
        Assert.AreEqual("Open", failed.State("Admission"));
        Assert.AreEqual("None", failed.State("Transfer"));
        Assert.AreEqual("OwnedLive", failed.State("NativeAuthority"));
        var afterFailure = failed.SnapshotFingerprint();
        failed.Reject("StaleReceipt", "CommitTransfer", failure);
        Assert.AreEqual(afterFailure, failed.SnapshotFingerprint());
        failed.CompleteDisposal();

        var aborted = LifecycleKernelHarness.New();
        var abort = aborted.BeginTransferPreparation()[0];
        aborted.Accept("AbortTransfer", abort);
        var retry = aborted.BeginTransferPreparation()[0];
        var beforeLate = aborted.SnapshotFingerprint();
        aborted.Reject("StaleReceipt", "CommitTransfer", abort);
        Assert.AreEqual(beforeLate, aborted.SnapshotFingerprint());
        aborted.Accept("CommitTransfer", retry);
    }

    [TestMethod]
    public void PreparingTransferArbitratesReentryCalloutFinalizerAndDisposeWithoutMutation()
    {
        var kernel = LifecycleKernelHarness.New();
        var preparation = kernel.BeginTransferPreparation()[0];
        var preparing = kernel.SnapshotFingerprint();
        kernel.Reject("IllegalState", "StartDispose", null, null);
        Assert.AreEqual(preparing, kernel.SnapshotFingerprint());

        kernel.Accept("FinalizeWrapper");
        var finalized = kernel.SnapshotFingerprint();
        kernel.Reject("StaleReceipt", "CommitTransfer", preparation);
        Assert.AreEqual(finalized, kernel.SnapshotFingerprint());
        Assert.IsTrue(kernel.Flag("OrphanRoot"));
        kernel.AssertValid();
    }
}

using System.Threading;
using System.Threading.Tasks;
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
        kernel.Accept("JoinRollback", ticket, "rollback-waiter", null, null);
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

    [DataTestMethod]
    [DataRow("CommitTransfer", "transfer-commit:1", false)]
    [DataRow("FailTransferPreparation", "transfer-failure:1", true)]
    [DataRow("AbortTransfer", "transfer-abort:1", true)]
    public void ForeignTransferJoinObservesExactImmutableOutcomeAcrossRetry(
        string completion,
        string expected,
        bool canRetry)
    {
        var kernel = LifecycleKernelHarness.New();
        var owner = kernel.AdapterCall("RequestTransfer");
        kernel.AcceptAdapter(owner);
        var joined = kernel.AdapterCall("RequestTransfer");
        kernel.AcceptAdapter(joined);
        Assert.IsTrue((bool)LifecycleKernelHarness.Property(joined, "IsJoin"));
        Assert.IsNull(kernel.ObserveAdapterRequest(joined));

        kernel.Accept(completion, kernel.AdapterReceipt(owner));
        Assert.AreEqual(expected, kernel.ObserveAdapterRequest(joined));
        Assert.AreEqual(1L, kernel.Number("TransferRecordCount"));
        if (canRetry)
        {
            var retry = kernel.AdapterCall("RequestTransfer");
            kernel.AcceptAdapter(retry);
            Assert.AreEqual(2L, kernel.Number("TransferRecordCount"));
            Assert.AreEqual(expected, kernel.ObserveAdapterRequest(joined));
            kernel.Accept("AbortTransfer", kernel.AdapterReceipt(retry));
        }
        kernel.AssertValid();
    }

    [TestMethod]
    public void RollbackAuthorityIsAuthenticatedBeforeRunningStableAndTerminalReplay()
    {
        var foreignTicket = LifecycleKernelHarness.New().BeginAndCommitTransfer();
        var wrongPurpose = LifecycleKernelHarness.New().BeginTransferPreparation()[0];

        var running = LifecycleKernelHarness.New();
        var runningTicket = running.BeginAndCommitTransfer();
        var runningRequest = running.AdapterCall("RequestRollback", runningTicket);
        running.AcceptAdapter(runningRequest);
        AssertRollbackCredentialRejected(running, null);
        AssertRollbackCredentialRejected(running, foreignTicket);
        AssertRollbackCredentialRejected(running, wrongPurpose);
        running.AcceptAdapter(running.AdapterCall("RequestRollback", runningTicket));
        var runningReceipt = running.AdapterReceipt(runningRequest);
        running.Accept(
            "CompleteRollback", runningReceipt,
            running.EnumValue("KernelAttemptOutcome", "Success"));

        var stable = LifecycleKernelHarness.New();
        var stableTicket = stable.BeginAndCommitTransfer();
        var stableReceipt = stable.StartRollbackAttempt(stableTicket)[0];
        stable.Accept(
            "CompleteRollback", stableReceipt,
            stable.EnumValue("KernelAttemptOutcome", "StableFailure"));
        AssertRollbackCredentialRejected(stable, null);
        AssertRollbackCredentialRejected(stable, foreignTicket);
        AssertRollbackCredentialRejected(stable, wrongPurpose);
        stable.RejectAdapter("StableFailure", stable.AdapterCall("RequestRollback", stableTicket));

        var completed = LifecycleKernelHarness.New();
        var completedTicket = completed.BeginAndCommitTransfer();
        completed.Accept("CommitTicket", completedTicket);
        completed.Accept("CompleteTicket", completedTicket);
        AssertRollbackCredentialRejected(completed, null);
        AssertRollbackCredentialRejected(completed, foreignTicket);
        AssertRollbackCredentialRejected(completed, wrongPurpose);
        var completedReplay = completed.AcceptAdapter(
            completed.AdapterCall("RequestRollback", completedTicket));
        Assert.AreEqual("rollback-terminal", completed.Value(completedReplay));

        var rolledBack = LifecycleKernelHarness.New();
        var rolledBackTicket = rolledBack.BeginAndCommitTransfer();
        var rolledBackReceipt = rolledBack.StartRollbackAttempt(rolledBackTicket)[0];
        rolledBack.Accept(
            "CompleteRollback", rolledBackReceipt,
            rolledBack.EnumValue("KernelAttemptOutcome", "Success"));
        AssertRollbackCredentialRejected(rolledBack, null);
        AssertRollbackCredentialRejected(rolledBack, foreignTicket);
        AssertRollbackCredentialRejected(rolledBack, rolledBackReceipt);
        var rolledBackReplay = rolledBack.AcceptAdapter(
            rolledBack.AdapterCall("RequestRollback", rolledBackTicket));
        Assert.AreEqual("rollback-Success:1", rolledBack.Value(rolledBackReplay));

        running.AssertValid();
        stable.AssertValid();
        completed.AssertValid();
        rolledBack.AssertValid();
    }

    [TestMethod]
    public void AtomicTransferRequestJoinsActualAttemptAcrossRetryBarrier()
    {
        var kernel = LifecycleKernelHarness.New();
        var first = kernel.AdapterCall("RequestTransfer");
        kernel.AcceptAdapter(first);
        var ready = new ManualResetEventSlim();
        Task<object> pending;
        object second;

        using (kernel.EnterKernelLock())
        {
            pending = Task.Run(() =>
            {
                ready.Set();
                return kernel.AdapterCall("RequestTransfer");
            });
            Assert.IsTrue(ready.Wait(5000));
            kernel.Accept("FailTransferPreparation", kernel.AdapterReceipt(first));
            second = kernel.Accept("BeginTransfer", null, null);
        }

        var joined = pending.Result;
        kernel.AcceptAdapter(joined);
        Assert.IsTrue((bool)LifecycleKernelHarness.Property(joined, "IsJoin"));
        kernel.Accept("AbortTransfer", kernel.ReservationReceipt(second));
        Assert.AreEqual("transfer-abort:2", kernel.ObserveAdapterRequest(joined));
        Assert.AreEqual(2L, kernel.Number("TransferRecordCount"));
        kernel.AssertValid();
    }

    private static void AssertRollbackCredentialRejected(
        LifecycleKernelHarness kernel,
        object credential)
    {
        var before = kernel.SnapshotFingerprint();
        kernel.RejectAdapter("StaleReceipt", kernel.AdapterCall("RequestRollback", credential));
        Assert.AreEqual(before, kernel.SnapshotFingerprint());
    }
}

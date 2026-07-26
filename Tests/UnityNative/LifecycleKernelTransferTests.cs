using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelTransferTests
{
    [TestMethod]
    public void TicketUseBlocksCommitAndRollbackDestructionUntilExactDrain()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.BeginAndCommitTransfer();
        var use = kernel.Value(kernel.Accept("EnterTicketUse"));
        kernel.Reject("IllegalState", "CommitTicket");
        kernel.Accept("StartRollback", "rollback-use");
        Assert.AreEqual("RollbackDraining", kernel.State("Transfer"));
        kernel.Reject("PendingWork", "FinishRollbackDrain");
        kernel.Accept("ReleaseTicketUse", use);
        kernel.Accept("FinishRollbackDrain");
        Assert.AreEqual("RollingBack", kernel.State("Transfer"));
        kernel.Accept("CompleteRollback", kernel.EnumValue("KernelAttemptOutcome", "Success"));
        Assert.AreEqual("RolledBack", kernel.State("Transfer"));
        Assert.AreEqual("Freed", kernel.State("NativeAuthority"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void RollbackJoinResultSurvivesRetryAndStableFailureCannotRetry()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.BeginAndCommitTransfer();
        kernel.Accept("StartRollback", "rollback-1");
        kernel.Accept("JoinRollback", "rollback-waiter", "foreign", false);
        var retryable = kernel.Value(kernel.Accept(
            "CompleteRollback", kernel.EnumValue("KernelAttemptOutcome", "RetryableFailure"))).ToString();
        Assert.AreEqual(retryable, kernel.Call("ObserveRollbackResult", 1L, "rollback-waiter"));

        kernel.Accept("StartRollback", "rollback-2");
        var stable = kernel.Value(kernel.Accept(
            "CompleteRollback", kernel.EnumValue("KernelAttemptOutcome", "StableFailure"))).ToString();
        var rejection = kernel.Reject("StableFailure", "StartRollback", "rollback-3");
        Assert.AreEqual(stable, LifecycleKernelHarness.Text(rejection, "Detail"));
        Assert.AreEqual(retryable, kernel.Call("ObserveRollbackResult", 1L, "rollback-waiter"));
        Assert.AreEqual(2L, kernel.Number("RollbackRecordCount"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void PendingTicketCanCommitAndCompleteWithZeroRoots()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.BeginAndCommitTransfer();
        kernel.Accept("CommitTicket");
        kernel.Accept("CompleteTicket");
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
        tracked.Reject("IllegalState", "BeginTransfer", "tracked");

        var resource = LifecycleKernelHarness.New();
        var kind = resource.EnumValue("KernelResourceKind", "GCHandle");
        resource.Accept("ReserveResource", kind);
        resource.Reject("IllegalState", "BeginTransfer", "reserved");

        var admitted = LifecycleKernelHarness.New();
        admitted.Accept("EnterOperation");
        admitted.Reject("IllegalState", "BeginTransfer", "admitted");
    }
}

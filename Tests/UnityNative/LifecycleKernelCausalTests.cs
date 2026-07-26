using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelCausalTests
{
    [TestMethod]
    public void SameCausalDisposalAndRollbackReentryRejectBeforeMutation()
    {
        var dispose = LifecycleKernelHarness.New();
        dispose.Accept("StartDispose", "same-dispose");
        dispose.Reject("SameCausalAttempt", "StartDispose", "same-dispose");
        dispose.Reject("SameCausalAttempt", "JoinDispose", "waiter", "same-dispose", false);
        Assert.AreEqual(1L, dispose.Number("DisposalRecordCount"));

        var rollback = LifecycleKernelHarness.New();
        rollback.BeginAndCommitTransfer();
        rollback.Accept("StartRollback", "same-rollback");
        rollback.Reject("SameCausalAttempt", "StartRollback", "same-rollback");
        rollback.Reject("SameCausalAttempt", "JoinRollback", "waiter", "same-rollback", false);
        Assert.AreEqual(1L, rollback.Number("RollbackRecordCount"));
    }

    [TestMethod]
    public void CleanupCalloutRejectsForeignRunningJoinWhileOrdinaryCallerBindsExactAttempt()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.Accept("StartDispose", "owner-A");
        kernel.Reject("CycleRisk", "JoinDispose", "callout-B", "owner-B", true);
        kernel.Accept("JoinDispose", "ordinary-B", "owner-B", false);
        Assert.AreEqual(1L, kernel.Number("DisposalRecordCount"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void RollbackCalloutRejectsCrossOwnerCycleRisk()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.BeginAndCommitTransfer();
        kernel.Accept("StartRollback", "owner-A");
        kernel.Reject("CycleRisk", "JoinRollback", "callout-B", "owner-B", true);
        kernel.Accept("JoinRollback", "ordinary-B", "owner-B", false);
        kernel.AssertValid();
    }
}

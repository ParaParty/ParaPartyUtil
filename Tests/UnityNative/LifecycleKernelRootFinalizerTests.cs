using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelRootFinalizerTests
{
    [TestMethod]
    public void ExhaustiveLiveAndTerminalRootMatricesMatchV33()
    {
        var kernel = LifecycleKernelHarness.New();
        var liveAccepted = 0;
        var terminalAccepted = 0;
        for (var mask = 0; mask < 16; mask++)
        {
            var wrapper = (mask & 8) != 0;
            var tracker = (mask & 4) != 0;
            var orphan = (mask & 2) != 0;
            var ticket = (mask & 1) != 0;
            var live = kernel.Call("ValidateTicketRootVector", wrapper, tracker, orphan, ticket, false);
            var terminal = kernel.Call("ValidateTicketRootVector", wrapper, tracker, orphan, ticket, true);
            var expectedLive = mask == 2 || mask == 1;
            var expectedTerminal = mask == 0;
            Assert.AreEqual(expectedLive, LifecycleKernelHarness.Bool(live, "Accepted"), $"live mask {mask:X}");
            Assert.AreEqual(expectedTerminal, LifecycleKernelHarness.Bool(terminal, "Accepted"), $"terminal mask {mask:X}");
            if (LifecycleKernelHarness.Bool(live, "Accepted")) liveAccepted++;
            if (LifecycleKernelHarness.Bool(terminal, "Accepted")) terminalAccepted++;
        }
        Assert.AreEqual(2, liveAccepted);
        Assert.AreEqual(1, terminalAccepted);
    }

    [TestMethod]
    public void WrapperFinalizerPublishesOrphanBeforeOrdinaryRootRemoval()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.Accept("FinalizeWrapper");
        Assert.IsTrue(kernel.Flag("FinalizerSeen"));
        Assert.IsFalse(kernel.Flag("WrapperRoot"));
        Assert.IsTrue(kernel.Flag("OrphanRoot"));
        kernel.Reject("IllegalState", "EnterOperation");
        kernel.AssertValid();
    }

    [TestMethod]
    public void TicketFinalizerMovesExactRecoveryRootToOrphan()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.BeginAndCommitTransfer();
        kernel.Accept("FinalizeTicket");
        Assert.IsTrue(kernel.Flag("FinalizerSeen"));
        Assert.IsFalse(kernel.Flag("TicketRoot"));
        Assert.IsTrue(kernel.Flag("OrphanRoot"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void TrackerPreventsPrematureFinalizerHandoffAndTransfer()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.Accept("RegisterTracker");
        kernel.Reject("IllegalState", "FinalizeWrapper");
        kernel.Reject("IllegalState", "BeginTransfer", null, null);
        Assert.IsTrue(kernel.Flag("WrapperRoot"));
        Assert.IsTrue(kernel.Flag("TrackerRoot"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void RegistryStronglyRootsFinalizedOwnerAndDrainRemovesOnlyAfterTerminalProof()
    {
        var pair = LifecycleKernelHarness.CreateFinalizedOwnerWithRegistry();
        var registry = pair[0];
        var weak = (System.WeakReference)pair[1];
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();

        Assert.IsTrue(weak.IsAlive, "The domain service must retain a recoverable owner capability.");
        var before = (System.Array)LifecycleKernelHarness.CallOn(registry, "Snapshot");
        Assert.AreEqual(1, before.Length);
        Assert.IsNotNull(LifecycleKernelHarness.Property(before.GetValue(0), "RecoveryOwner"));

        var task = LifecycleKernelHarness.CallOn(
            registry, "DrainAsync", System.DateTime.UtcNow.AddMinutes(1));
        var result = LifecycleKernelHarness.TaskResult(task);
        Assert.AreEqual(1L, System.Convert.ToInt64(LifecycleKernelHarness.Property(result, "Recovered")));
        Assert.AreEqual(0L, System.Convert.ToInt64(LifecycleKernelHarness.Property(result, "Remaining")));
        Assert.AreEqual(0, ((System.Array)LifecycleKernelHarness.CallOn(registry, "Snapshot")).Length);
    }

    [TestMethod]
    public void EmergencyPublicationIsPreallocatedAndStillRecoverable()
    {
        var pair = LifecycleKernelHarness.CreateFinalizedOwnerWithRegistry(forcePrimaryPublishFailure: true);
        var registry = pair[0];
        var snapshot = (System.Array)LifecycleKernelHarness.CallOn(registry, "Snapshot");
        Assert.AreEqual(1, snapshot.Length);
        Assert.IsTrue(System.Convert.ToBoolean(
            LifecycleKernelHarness.Property(snapshot.GetValue(0), "Emergency")));

        var result = LifecycleKernelHarness.TaskResult(LifecycleKernelHarness.CallOn(
            registry, "DrainAsync", System.DateTime.UtcNow.AddMinutes(1)));
        Assert.AreEqual(1L, System.Convert.ToInt64(LifecycleKernelHarness.Property(result, "Recovered")));
        Assert.AreEqual(0L, System.Convert.ToInt64(LifecycleKernelHarness.Property(result, "Remaining")));
    }

    [TestMethod]
    public void RetryTargetsOneExactOwnerAndDuplicateReservationIsRejected()
    {
        var pair = LifecycleKernelHarness.CreateFinalizedOwnerWithRegistry();
        var registry = pair[0];
        var snapshot = (System.Array)LifecycleKernelHarness.CallOn(registry, "Snapshot");
        var owner = LifecycleKernelHarness.Property(snapshot.GetValue(0), "Owner");

        Assert.IsNull(LifecycleKernelHarness.CallOn(registry, "Reserve", owner));
        var result = LifecycleKernelHarness.TaskResult(LifecycleKernelHarness.CallOn(
            registry, "RetryAsync", owner, System.DateTime.UtcNow.AddMinutes(1)));
        Assert.AreEqual(1L, System.Convert.ToInt64(
            LifecycleKernelHarness.Property(result, "Recovered")));
        Assert.AreEqual(0L, System.Convert.ToInt64(
            LifecycleKernelHarness.Property(result, "Remaining")));
    }

    [TestMethod]
    public void TimeoutShutdownAndStableFailureRetainUnresolvedOwner()
    {
        var kernel = LifecycleKernelHarness.NewWithRegistry();
        var stage = kernel.ReserveDisposalStage("Managed");
        kernel.Accept("FailRunningStage", stage[1], true);
        kernel.Accept("FinalizeWrapper");

        var timedOut = LifecycleKernelHarness.TaskResult(kernel.RegistryCall(
            "DrainAsync", System.DateTime.UtcNow.AddSeconds(-1)));
        Assert.AreEqual(1L, System.Convert.ToInt64(
            LifecycleKernelHarness.Property(timedOut, "TimedOut")));
        Assert.AreEqual(1L, kernel.RegistryCount());

        var shutdown = LifecycleKernelHarness.TaskResult(kernel.RegistryCall(
            "ShutdownAsync", System.DateTime.UtcNow.AddMinutes(1)));
        Assert.AreEqual(1L, System.Convert.ToInt64(
            LifecycleKernelHarness.Property(shutdown, "StableRetained")));
        Assert.AreEqual(1L, kernel.RegistryCount());
        Assert.IsTrue(kernel.RegistryFlag("Shutdown"));
        Assert.IsNull(kernel.RegistryCall("Reserve", LifecycleKernelHarness.Identity(999999)));
    }
}

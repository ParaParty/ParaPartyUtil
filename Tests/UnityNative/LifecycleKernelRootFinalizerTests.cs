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
        kernel.Reject("IllegalState", "BeginTransfer", "tracked-transfer");
        Assert.IsTrue(kernel.Flag("WrapperRoot"));
        Assert.IsTrue(kernel.Flag("TrackerRoot"));
        kernel.AssertValid();
    }
}

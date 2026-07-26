using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelDisposalTests
{
    [DataTestMethod]
    [DataRow("Owned", false, "Absent")]
    [DataRow("Owned", true, "Freed")]
    [DataRow("Borrowed", true, "BorrowedExternal")]
    public void LegalDisposalWitnessesReachZeroRootTerminalState(
        string ownership,
        bool published,
        string terminalAuthority)
    {
        var kernel = LifecycleKernelHarness.New(ownership, published);
        kernel.CompleteDisposal();
        Assert.AreEqual("Disposed", kernel.State("Life"));
        Assert.AreEqual("Closed", kernel.State("Admission"));
        Assert.AreEqual(terminalAuthority, kernel.State("NativeAuthority"));
        Assert.AreEqual(6L, kernel.Number("CompletedStageCount"));
        Assert.IsFalse(kernel.Flag("WrapperRoot"));
        Assert.IsFalse(kernel.Flag("TrackerRoot"));
        Assert.IsFalse(kernel.Flag("OrphanRoot"));
        Assert.IsFalse(kernel.Flag("TicketRoot"));
        kernel.Reject("IllegalState", "EnterOperation");
        kernel.Reject("IllegalState", "EnterCallback");
        kernel.AssertValid();
    }

    [TestMethod]
    public void RetryAppendsAttemptWithoutOverwritingJoinedResult()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.Accept("StartDispose", "dispose-1");
        kernel.Accept("JoinDispose", "waiter-1", "foreign", false);
        kernel.Accept("CloseAdmission");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "Managed"));
        var firstResult = kernel.Value(kernel.Accept("FailRunningStage", false)).ToString();
        Assert.AreEqual(firstResult, kernel.Call("ObserveDisposeResult", 1L, "waiter-1"));

        kernel.Accept("StartDispose", "dispose-2");
        Assert.AreEqual(2L, kernel.Number("DisposalAttemptId"));
        Assert.AreEqual(2L, kernel.Number("DisposalRecordCount"));
        Assert.AreEqual(firstResult, kernel.Call("ObserveDisposeResult", 1L, "waiter-1"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void StableFailureReplaysExactResultAndForbidsRetry()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.Accept("StartDispose", "dispose-stable");
        kernel.Accept("JoinDispose", "stable-waiter", "foreign", false);
        kernel.Accept("CloseAdmission");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "Managed"));
        var exact = kernel.Value(kernel.Accept("FailRunningStage", true)).ToString();

        var rejection = kernel.Reject("StableFailure", "StartDispose", "dispose-retry");
        Assert.AreEqual(exact, LifecycleKernelHarness.Text(rejection, "Detail"));
        Assert.AreEqual(exact, kernel.Call("ObserveDisposeResult", 1L, "stable-waiter"));
        Assert.AreEqual(1L, kernel.Number("DisposalRecordCount"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void DestroyRequiresExactQuiescenceReceiptAndCommitsFreedWithUnpublished()
    {
        var kernel = LifecycleKernelHarness.New();
        kernel.Accept("StartDispose", "dispose-receipt");
        kernel.Accept("CloseAdmission");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "Managed"));
        kernel.Accept("CompleteManaged");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "NativeQuiesce"));
        var receipt = kernel.Value(kernel.Accept("RecordQuiescence"));
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "CallbackFence"));
        kernel.Accept("CompleteCallbackFence");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "NativeAuthority"));
        kernel.Reject("StaleReceipt", "CommitNativeAuthority", (object)null);
        kernel.Accept("CommitNativeAuthority", receipt);
        Assert.AreEqual("Freed", kernel.State("NativeAuthority"));
        Assert.AreEqual("Unpublished", kernel.State("Publication"));
        kernel.AssertValid();
    }
}

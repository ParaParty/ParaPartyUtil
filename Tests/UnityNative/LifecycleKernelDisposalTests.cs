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
        var attempt = kernel.ReserveDisposalStage("Managed");
        kernel.Accept("JoinDispose", "waiter-1", null, null);
        var firstResult = kernel.Value(kernel.Accept("FailRunningStage", attempt[1], false)).ToString();
        Assert.AreEqual(firstResult, kernel.Call("ObserveDisposeResult", 1L, "waiter-1"));

        kernel.Accept("StartDispose", null, null);
        Assert.AreEqual(2L, kernel.Number("DisposalAttemptId"));
        Assert.AreEqual(2L, kernel.Number("DisposalRecordCount"));
        Assert.AreEqual(firstResult, kernel.Call("ObserveDisposeResult", 1L, "waiter-1"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void StableFailureReplaysExactResultAndForbidsRetry()
    {
        var kernel = LifecycleKernelHarness.New();
        var attempt = kernel.ReserveDisposalStage("Managed");
        kernel.Accept("JoinDispose", "stable-waiter", null, null);
        var exact = kernel.Value(kernel.Accept("FailRunningStage", attempt[1], true)).ToString();

        var rejection = kernel.Reject("StableFailure", "StartDispose", null, null);
        Assert.AreEqual(exact, LifecycleKernelHarness.Text(rejection, "Detail"));
        Assert.AreEqual(exact, kernel.Call("ObserveDisposeResult", 1L, "stable-waiter"));
        Assert.AreEqual(1L, kernel.Number("DisposalRecordCount"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void DestroyRequiresExactQuiescenceReceiptAndCommitsFreedWithUnpublished()
    {
        var kernel = LifecycleKernelHarness.New();
        var native = kernel.ReserveDisposalStage("NativeAuthority");
        kernel.Reject("StaleReceipt", "CommitNativeAuthority", native[1], null);
        kernel.Accept("CommitNativeAuthority", native[1], native[2]);
        Assert.AreEqual("Freed", kernel.State("NativeAuthority"));
        Assert.AreEqual("Unpublished", kernel.State("Publication"));
        kernel.AssertValid();
    }

    [DataTestMethod]
    [DataRow("Managed")]
    [DataRow("NativeQuiesce")]
    [DataRow("CallbackFence")]
    [DataRow("NativeAuthority")]
    [DataRow("LegacyUnpublishNotification")]
    [DataRow("BaseResources")]
    public void EveryStageRejectsForeignStaleAndConsumedReservationsWithoutMutation(string stage)
    {
        var owner = LifecycleKernelHarness.New();
        var own = owner.ReserveDisposalStage(stage);
        var foreign = LifecycleKernelHarness.New().ReserveDisposalStage(stage);
        var beforeForeign = owner.SnapshotFingerprint();
        RejectStage(owner, stage, foreign[1], own[2]);
        Assert.AreEqual(beforeForeign, owner.SnapshotFingerprint());

        AcceptStage(owner, stage, own[1], own[2]);
        var afterCompletion = owner.SnapshotFingerprint();
        RejectStage(owner, stage, own[1], own[2]);
        Assert.AreEqual(afterCompletion, owner.SnapshotFingerprint());

        var retry = LifecycleKernelHarness.New();
        var first = retry.ReserveDisposalStage(stage);
        retry.Accept("FailRunningStage", first[1], false);
        var second = retry.ReserveDisposalStage(stage);
        var beforeStale = retry.SnapshotFingerprint();
        RejectStage(retry, stage, first[1], second[2]);
        Assert.AreEqual(beforeStale, retry.SnapshotFingerprint());
        if (stage == "NativeAuthority")
        {
            RejectStage(retry, stage, second[1], first[2]);
            Assert.AreEqual(beforeStale, retry.SnapshotFingerprint());
        }
        AcceptStage(retry, stage, second[1], second[2]);
        retry.AssertValid();
    }

    [DataTestMethod]
    [DataRow("Managed")]
    [DataRow("NativeQuiesce")]
    [DataRow("CallbackFence")]
    [DataRow("NativeAuthority")]
    [DataRow("LegacyUnpublishNotification")]
    [DataRow("BaseResources")]
    public void EveryStageFailureConsumesItsExactReservationWithoutMutationOnRejection(string stage)
    {
        var owner = LifecycleKernelHarness.New();
        var own = owner.ReserveDisposalStage(stage);
        var foreign = LifecycleKernelHarness.New().ReserveDisposalStage(stage);

        var beforeForeign = owner.SnapshotFingerprint();
        owner.Reject("StaleReceipt", "FailRunningStage", foreign[1], false);
        Assert.AreEqual(beforeForeign, owner.SnapshotFingerprint());

        owner.Accept("FailRunningStage", own[1], false);
        var afterConsumed = owner.SnapshotFingerprint();
        owner.Reject("StaleReceipt", "FailRunningStage", own[1], false);
        Assert.AreEqual(afterConsumed, owner.SnapshotFingerprint());

        var retry = owner.ReserveDisposalStage(stage);
        var beforeStale = owner.SnapshotFingerprint();
        owner.Reject("StaleReceipt", "FailRunningStage", own[1], false);
        Assert.AreEqual(beforeStale, owner.SnapshotFingerprint());
        owner.Accept("FailRunningStage", retry[1], false);
        owner.AssertValid();
    }

    [TestMethod]
    public void NativeAuthorityRetryReissuesCurrentQuiescenceAndReachesDisposedExactlyOnce()
    {
        var kernel = LifecycleKernelHarness.New();
        var first = kernel.ReserveDisposalStage("NativeAuthority");
        kernel.Accept("FailRunningStage", first[1], false);

        var second = kernel.ReserveDisposalStage("NativeAuthority");
        Assert.AreEqual(
            LifecycleKernelHarness.NestedNumber(first[2], "Generation", "Value"),
            LifecycleKernelHarness.NestedNumber(second[2], "Generation", "Value"));
        var beforeOldReceipt = kernel.SnapshotFingerprint();
        kernel.Reject("StaleReceipt", "CommitNativeAuthority", second[1], first[2]);
        Assert.AreEqual(beforeOldReceipt, kernel.SnapshotFingerprint());
        kernel.Accept("CommitNativeAuthority", second[1], second[2]);
        var afterDestroy = kernel.SnapshotFingerprint();
        kernel.Reject("StaleReceipt", "CommitNativeAuthority", second[1], second[2]);
        Assert.AreEqual(afterDestroy, kernel.SnapshotFingerprint());

        var legacy = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "LegacyUnpublishNotification"), second[0]));
        kernel.Accept("CompleteLegacyNotification", legacy);
        var resources = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "BaseResources"), second[0]));
        kernel.Accept("CompleteBaseResources", resources);
        kernel.Accept("FinishDispose", second[0]);
        Assert.AreEqual("Disposed", kernel.State("Life"));
        Assert.AreEqual("Freed", kernel.State("NativeAuthority"));
        kernel.AssertValid();
    }

    private static void AcceptStage(
        LifecycleKernelHarness kernel,
        string stage,
        object stageReceipt,
        object quiescenceReceipt)
    {
        switch (stage)
        {
            case "Managed":
                kernel.Accept("CompleteManaged", stageReceipt);
                break;
            case "NativeQuiesce":
                kernel.Accept("RecordQuiescence", stageReceipt);
                break;
            case "CallbackFence":
                kernel.Accept("CompleteCallbackFence", stageReceipt);
                break;
            case "NativeAuthority":
                kernel.Accept("CommitNativeAuthority", stageReceipt, quiescenceReceipt);
                break;
            case "LegacyUnpublishNotification":
                kernel.Accept("CompleteLegacyNotification", stageReceipt);
                break;
            case "BaseResources":
                kernel.Accept("CompleteBaseResources", stageReceipt);
                break;
            default:
                Assert.Fail("Unknown cleanup stage " + stage);
                break;
        }
    }

    private static void RejectStage(
        LifecycleKernelHarness kernel,
        string stage,
        object stageReceipt,
        object quiescenceReceipt)
    {
        switch (stage)
        {
            case "Managed":
                kernel.Reject("StaleReceipt", "CompleteManaged", stageReceipt);
                break;
            case "NativeQuiesce":
                kernel.Reject("StaleReceipt", "RecordQuiescence", stageReceipt);
                break;
            case "CallbackFence":
                kernel.Reject("StaleReceipt", "CompleteCallbackFence", stageReceipt);
                break;
            case "NativeAuthority":
                kernel.Reject("StaleReceipt", "CommitNativeAuthority", stageReceipt, quiescenceReceipt);
                break;
            case "LegacyUnpublishNotification":
                kernel.Reject("StaleReceipt", "CompleteLegacyNotification", stageReceipt);
                break;
            case "BaseResources":
                kernel.Reject("StaleReceipt", "CompleteBaseResources", stageReceipt);
                break;
            default:
                Assert.Fail("Unknown cleanup stage " + stage);
                break;
        }
    }
}

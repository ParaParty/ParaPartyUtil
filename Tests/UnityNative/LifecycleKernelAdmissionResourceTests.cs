using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelAdmissionResourceTests
{
    [TestMethod]
    public void ExactLeaseAndCallbackTokensDrainBeforeAdmissionCloses()
    {
        var kernel = LifecycleKernelHarness.New();
        var lease = kernel.Value(kernel.Accept("EnterOperation"));
        var callback = kernel.Value(kernel.Accept("EnterCallback"));
        var dispose = kernel.ReservationReceipt(kernel.Accept("StartDispose", null, null));
        kernel.Reject("PendingWork", "CloseAdmission", dispose);

        kernel.Accept("ReleaseOperation", lease);
        kernel.Reject("InvalidToken", "ReleaseOperation", lease);
        kernel.Reject("PendingWork", "CloseAdmission", dispose);
        kernel.Accept("ReleaseCallback", callback);
        kernel.Reject("InvalidToken", "ReleaseCallback", callback);
        kernel.Accept("CloseAdmission", dispose);
        Assert.AreEqual("Closed", kernel.State("Admission"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void LateAllocationCommitBecomesDetachedCompensationWork()
    {
        var kernel = LifecycleKernelHarness.New();
        var kind = kernel.EnumValue("KernelResourceKind", "UnmanagedMemory");
        var reservation = kernel.Value(kernel.Accept("ReserveResource", kind));
        var generation = LifecycleKernelHarness.NestedNumber(reservation, "Generation", "Value");

        var dispose = kernel.ReservationReceipt(kernel.Accept("StartDispose", null, null));
        kernel.Accept("CommitAllocation", kind, reservation);
        Assert.AreEqual("Detached", kernel.State("UnmanagedMemoryPhase"));
        kernel.Reject("StaleReceipt", "CommitAllocation", kind, reservation);

        kernel.Accept("CloseAdmission", dispose);
        var managed = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "Managed"), dispose));
        kernel.Accept("CompleteManaged", managed);
        var quiesce = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "NativeQuiesce"), dispose));
        var receipt = kernel.Value(kernel.Accept("RecordQuiescence", quiesce));
        var callback = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "CallbackFence"), dispose));
        kernel.Accept("CompleteCallbackFence", callback);
        var native = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "NativeAuthority"), dispose));
        kernel.Accept("CommitNativeAuthority", native, receipt);
        var legacy = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "LegacyUnpublishNotification"), dispose));
        kernel.Accept("CompleteLegacyNotification", legacy);
        var resources = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "BaseResources"), dispose));
        kernel.Accept("ReleaseResource", kind, generation);
        kernel.Reject("StaleReceipt", "ReleaseResource", kind, generation);
        kernel.Accept("CompleteBaseResources", resources);
        kernel.Accept("FinishDispose", dispose);
        Assert.AreEqual("Disposed", kernel.State("Life"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void ReplacementDetachesAndReleasesEveryGenerationExactlyOnce()
    {
        var kernel = LifecycleKernelHarness.New();
        var kind = kernel.EnumValue("KernelResourceKind", "GCHandle");
        var first = kernel.Value(kernel.Accept("ReserveResource", kind));
        var firstGeneration = LifecycleKernelHarness.NestedNumber(first, "Generation", "Value");
        kernel.Accept("CommitAllocation", kind, first);
        var second = kernel.Value(kernel.Accept("ReserveResource", kind));
        kernel.Accept("CommitAllocation", kind, second);
        Assert.AreEqual("Mixed", kernel.State("GCHandlePhase"));
        kernel.Accept("ReleaseResource", kind, firstGeneration);
        kernel.Reject("StaleReceipt", "ReleaseResource", kind, firstGeneration);
        Assert.AreEqual("Live", kernel.State("GCHandlePhase"));
        kernel.AssertValid();
    }
}

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
        kernel.Accept("StartDispose", "dispose-admission");
        kernel.Reject("PendingWork", "CloseAdmission");

        kernel.Accept("ReleaseOperation", lease);
        kernel.Reject("InvalidToken", "ReleaseOperation", lease);
        kernel.Reject("PendingWork", "CloseAdmission");
        kernel.Accept("ReleaseCallback", callback);
        kernel.Reject("InvalidToken", "ReleaseCallback", callback);
        kernel.Accept("CloseAdmission");
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

        kernel.Accept("StartDispose", "dispose-race");
        kernel.Accept("CommitAllocation", kind, reservation);
        Assert.AreEqual("Detached", kernel.State("UnmanagedMemoryPhase"));
        kernel.Reject("StaleReceipt", "CommitAllocation", kind, reservation);

        kernel.Accept("CloseAdmission");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "Managed"));
        kernel.Accept("CompleteManaged");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "NativeQuiesce"));
        var receipt = kernel.Value(kernel.Accept("RecordQuiescence"));
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "CallbackFence"));
        kernel.Accept("CompleteCallbackFence");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "NativeAuthority"));
        kernel.Accept("CommitNativeAuthority", receipt);
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "LegacyUnpublishNotification"));
        kernel.Accept("CompleteLegacyNotification");
        kernel.Accept("BeginStage", kernel.EnumValue("KernelStage", "BaseResources"));
        kernel.Accept("ReleaseResource", kind, generation);
        kernel.Reject("StaleReceipt", "ReleaseResource", kind, generation);
        kernel.Accept("CompleteBaseResources");
        kernel.Accept("FinishDispose");
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

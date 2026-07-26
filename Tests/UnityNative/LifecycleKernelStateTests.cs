using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelStateTests
{
    [DataTestMethod]
    [DataRow("Owned", false, "Never", "Absent")]
    [DataRow("Owned", true, "Published", "OwnedLive")]
    [DataRow("Borrowed", true, "Published", "BorrowedExternal")]
    public void ConstructionCreatesLegalModelRoots(
        string ownership,
        bool published,
        string publication,
        string authority)
    {
        var kernel = LifecycleKernelHarness.New(ownership, published);
        Assert.AreEqual("Active", kernel.State("Life"));
        Assert.AreEqual("Open", kernel.State("Admission"));
        Assert.AreEqual(publication, kernel.State("Publication"));
        Assert.AreEqual(authority, kernel.State("NativeAuthority"));
        Assert.IsTrue(kernel.Flag("WrapperRoot"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void PublishRejectsOutstandingReservationWithoutMutation()
    {
        var kernel = LifecycleKernelHarness.New(published: false);
        var kind = kernel.EnumValue("KernelResourceKind", "GCHandle");
        var reservation = LifecycleKernelHarness.Property(kernel.Accept("ReserveResource", kind), "Value");

        kernel.Reject("IllegalState", "Publish", new IntPtr(17));
        Assert.AreEqual("Never", kernel.State("Publication"));
        Assert.AreEqual("Reserved", kernel.State("GCHandlePhase"));

        kernel.Accept("AllocationFailed", kind, reservation);
        kernel.Accept("Publish", new IntPtr(17));
        Assert.AreEqual("Published", kernel.State("Publication"));
        Assert.AreEqual(1L, kernel.Number("PublicationGeneration"));
        kernel.AssertValid();
    }

    [TestMethod]
    public void BorrowedTransferAndDestructiveAuthorityAreRejected()
    {
        var kernel = LifecycleKernelHarness.New("Borrowed");
        kernel.Reject("IllegalState", "BeginTransfer", null, null);
        Assert.AreEqual("Active", kernel.State("Life"));
        Assert.AreEqual("BorrowedExternal", kernel.State("NativeAuthority"));
        Assert.AreEqual("Published", kernel.State("Publication"));
        kernel.AssertValid();
    }
}

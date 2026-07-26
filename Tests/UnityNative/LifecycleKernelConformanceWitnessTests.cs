using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelConformanceWitnessTests
{
    [TestMethod]
    public void ModelDimensionsHaveDeterministicReachableWitnesses()
    {
        var life = new HashSet<string>();
        var publication = new HashSet<string>();
        var authority = new HashSet<string>();
        var admission = new HashSet<string>();
        var attempt = new HashSet<string>();
        var transfer = new HashSet<string>();

        void Capture(LifecycleKernelHarness kernel)
        {
            life.Add(kernel.State("Life"));
            publication.Add(kernel.State("Publication"));
            authority.Add(kernel.State("NativeAuthority"));
            admission.Add(kernel.State("Admission"));
            attempt.Add(kernel.State("DisposalAttempt"));
            transfer.Add(kernel.State("Transfer"));
            kernel.AssertValid();
        }

        Capture(LifecycleKernelHarness.New("Owned", false));
        Capture(LifecycleKernelHarness.New("Borrowed", true));

        var disposing = LifecycleKernelHarness.New();
        disposing.Accept("StartDispose", null, null);
        Capture(disposing);

        var retryable = LifecycleKernelHarness.New();
        var retryableStage = retryable.ReserveDisposalStage("Managed");
        retryable.Accept("FailRunningStage", retryableStage[1], false);
        Capture(retryable);

        var stable = LifecycleKernelHarness.New();
        var stableStage = stable.ReserveDisposalStage("Managed");
        stable.Accept("FailRunningStage", stableStage[1], true);
        Capture(stable);

        var disposed = LifecycleKernelHarness.New();
        disposed.CompleteDisposal();
        Capture(disposed);

        var preparing = LifecycleKernelHarness.New();
        preparing.Accept("BeginTransfer", null, null);
        Capture(preparing);

        var pending = LifecycleKernelHarness.New();
        pending.BeginAndCommitTransfer();
        Capture(pending);

        var inUse = LifecycleKernelHarness.New();
        var inUseTicket = inUse.BeginAndCommitTransfer();
        inUse.Accept("EnterTicketUse", inUseTicket);
        Capture(inUse);

        var committed = LifecycleKernelHarness.New();
        var committedTicket = committed.BeginAndCommitTransfer();
        committed.Accept("CommitTicket", committedTicket);
        Capture(committed);
        committed.Accept("CompleteTicket", committedTicket);
        Capture(committed);

        var draining = LifecycleKernelHarness.New();
        var drainingTicket = draining.BeginAndCommitTransfer();
        draining.Accept("EnterTicketUse", drainingTicket);
        draining.StartRollbackAttempt(drainingTicket);
        Capture(draining);

        var rolling = LifecycleKernelHarness.New();
        var rollingTicket = rolling.BeginAndCommitTransfer();
        rolling.StartRollbackAttempt(rollingTicket);
        Capture(rolling);

        var rollbackRetry = LifecycleKernelHarness.New();
        var retryTicket = rollbackRetry.BeginAndCommitTransfer();
        var retryAttempt = rollbackRetry.StartRollbackAttempt(retryTicket)[0];
        rollbackRetry.Accept(
            "CompleteRollback", retryAttempt,
            rollbackRetry.EnumValue("KernelAttemptOutcome", "RetryableFailure"));
        Capture(rollbackRetry);

        var rollbackStable = LifecycleKernelHarness.New();
        var stableTicket = rollbackStable.BeginAndCommitTransfer();
        var stableAttempt = rollbackStable.StartRollbackAttempt(stableTicket)[0];
        rollbackStable.Accept(
            "CompleteRollback", stableAttempt,
            rollbackStable.EnumValue("KernelAttemptOutcome", "StableFailure"));
        Capture(rollbackStable);

        var rolledBack = LifecycleKernelHarness.New();
        var rolledBackTicket = rolledBack.BeginAndCommitTransfer();
        var rolledBackAttempt = rolledBack.StartRollbackAttempt(rolledBackTicket)[0];
        rolledBack.Accept(
            "CompleteRollback", rolledBackAttempt,
            rolledBack.EnumValue("KernelAttemptOutcome", "Success"));
        Capture(rolledBack);

        CollectionAssert.AreEquivalent(
            new[] { "Active", "Disposing", "FaultRetryable", "FaultStable", "Disposed", "Transferred" },
            new List<string>(life));
        CollectionAssert.AreEquivalent(new[] { "Never", "Published", "Unpublished" }, new List<string>(publication));
        CollectionAssert.AreEquivalent(
            new[] { "Absent", "OwnedLive", "BorrowedExternal", "Unknown", "Freed", "TicketOwned" },
            new List<string>(authority));
        CollectionAssert.AreEquivalent(new[] { "Open", "Closing", "Closed" }, new List<string>(admission));
        CollectionAssert.AreEquivalent(
            new[] { "Idle", "Running", "RetryableFault", "StableFault", "Complete" },
            new List<string>(attempt));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "None", "Preparing", "Pending", "HandoffInUse", "Committed", "Completed",
                "RollbackDraining", "RollingBack", "RollbackRetryable", "RollbackStable", "RolledBack"
            },
            new List<string>(transfer));
    }

    [TestMethod]
    public void ResourcePhasesHaveDeterministicReachableWitnesses()
    {
        var phases = new HashSet<string> { LifecycleKernelHarness.New().State("GCHandlePhase") };
        var kindOwner = LifecycleKernelHarness.New();
        var kind = kindOwner.EnumValue("KernelResourceKind", "GCHandle");
        var first = kindOwner.Value(kindOwner.Accept("ReserveResource", kind));
        phases.Add(kindOwner.State("GCHandlePhase"));
        kindOwner.Accept("CommitAllocation", kind, first);
        phases.Add(kindOwner.State("GCHandlePhase"));
        var second = kindOwner.Value(kindOwner.Accept("ReserveResource", kind));
        kindOwner.Accept("CommitAllocation", kind, second);
        phases.Add(kindOwner.State("GCHandlePhase"));

        var late = LifecycleKernelHarness.New();
        var lateKind = late.EnumValue("KernelResourceKind", "GCHandle");
        var reservation = late.Value(late.Accept("ReserveResource", lateKind));
        var generation = LifecycleKernelHarness.NestedNumber(reservation, "Generation", "Value");
        late.Accept("StartDispose", null, null);
        late.Accept("CommitAllocation", lateKind, reservation);
        phases.Add(late.State("GCHandlePhase"));
        late.Accept("ReleaseResource", lateKind, generation);
        phases.Add(late.State("GCHandlePhase"));

        CollectionAssert.AreEquivalent(
            new[] { "Empty", "Reserved", "Live", "Detached", "Released", "Mixed" },
            new List<string>(phases));
    }
}

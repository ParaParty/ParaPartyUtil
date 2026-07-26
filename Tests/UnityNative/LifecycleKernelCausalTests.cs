using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class LifecycleKernelCausalTests
{
    [TestMethod]
    public void FlowedAttemptCapabilityRejectsSameCausalDisposeTransferAndRollback()
    {
        var dispose = LifecycleKernelHarness.New();
        var disposeRequest = dispose.AdapterCall("RequestDispose");
        dispose.AcceptAdapter(disposeRequest);
        using (dispose.EnterAdapterScope(disposeRequest))
        {
            dispose.RejectAdapter(
                "SameCausalAttempt",
                dispose.AdapterCall("RequestDispose"));
        }

        var transfer = LifecycleKernelHarness.New();
        var transferRequest = transfer.AdapterCall("RequestTransfer");
        transfer.AcceptAdapter(transferRequest);
        using (transfer.EnterAdapterScope(transferRequest))
        {
            transfer.RejectAdapter(
                "SameCausalAttempt",
                transfer.AdapterCall("RequestTransfer"));
        }
        transfer.Accept("AbortTransfer", transfer.AdapterReceipt(transferRequest));

        var rollback = LifecycleKernelHarness.New();
        var ticket = rollback.BeginAndCommitTransfer();
        var rollbackRequest = rollback.AdapterCall("RequestRollback", ticket);
        rollback.AcceptAdapter(rollbackRequest);
        using (rollback.EnterAdapterScope(rollbackRequest))
        {
            rollback.RejectAdapter(
                "SameCausalAttempt",
                rollback.AdapterCall("RequestRollback", ticket));
        }
    }

    [TestMethod]
    public void OrdinaryForeignJoinObservesItsExactDisposeAndRollbackResults()
    {
        var dispose = LifecycleKernelHarness.New();
        var ownerDispose = dispose.AdapterCall("RequestDispose");
        dispose.AcceptAdapter(ownerDispose);
        var joinedDispose = dispose.AdapterCall("RequestDispose");
        dispose.AcceptAdapter(joinedDispose);
        Assert.IsTrue((bool)LifecycleKernelHarness.Property(joinedDispose, "IsJoin"));
        Assert.IsNull(dispose.ObserveAdapterRequest(joinedDispose));
        var disposeResult = dispose.Value(dispose.CompleteStartedDisposal(
            dispose.AdapterReceipt(ownerDispose))).ToString();
        Assert.AreEqual(disposeResult, dispose.ObserveAdapterRequest(joinedDispose));

        var rollback = LifecycleKernelHarness.New();
        var ticket = rollback.BeginAndCommitTransfer();
        var ownerRollback = rollback.AdapterCall("RequestRollback", ticket);
        rollback.AcceptAdapter(ownerRollback);
        var joinedRollback = rollback.AdapterCall("RequestRollback", ticket);
        rollback.AcceptAdapter(joinedRollback);
        Assert.IsNull(rollback.ObserveAdapterRequest(joinedRollback));
        var rollbackResult = rollback.Value(rollback.Accept(
            "CompleteRollback",
            rollback.AdapterReceipt(ownerRollback),
            rollback.EnumValue("KernelAttemptOutcome", "Success"))).ToString();
        Assert.AreEqual(rollbackResult, rollback.ObserveAdapterRequest(joinedRollback));
    }

    [TestMethod]
    public void CrossOwnerCalloutCapabilityRejectsDisposeTransferAndRollbackCycles()
    {
        var source = LifecycleKernelHarness.New();
        var sourceRequest = source.AdapterCall("RequestDispose");
        source.AcceptAdapter(sourceRequest);

        var disposeTarget = LifecycleKernelHarness.New();
        disposeTarget.AcceptAdapter(disposeTarget.AdapterCall("RequestDispose"));
        var transferTarget = LifecycleKernelHarness.New();
        transferTarget.AcceptAdapter(transferTarget.AdapterCall("RequestTransfer"));
        var rollbackTarget = LifecycleKernelHarness.New();
        var ticket = rollbackTarget.BeginAndCommitTransfer();
        rollbackTarget.AcceptAdapter(rollbackTarget.AdapterCall("RequestRollback", ticket));

        using (source.EnterAdapterScope(sourceRequest))
        using (source.EnterCallout("Cleanup"))
        {
            disposeTarget.RejectAdapter(
                "CycleRisk", disposeTarget.AdapterCall("RequestDispose"));
            transferTarget.RejectAdapter(
                "CycleRisk", transferTarget.AdapterCall("RequestTransfer"));
            rollbackTarget.RejectAdapter(
                "CycleRisk", rollbackTarget.AdapterCall("RequestRollback", ticket));
        }
    }

    [TestMethod]
    public void SuppressedFrameworkFlowRequiresExplicitCapabilityAndPreservesCycleRule()
    {
        var source = LifecycleKernelHarness.New();
        var sourceRequest = source.AdapterCall("RequestDispose");
        source.AcceptAdapter(sourceRequest);
        var disposeTarget = LifecycleKernelHarness.New();
        disposeTarget.AcceptAdapter(disposeTarget.AdapterCall("RequestDispose"));
        var transferTarget = LifecycleKernelHarness.New();
        transferTarget.AcceptAdapter(transferTarget.AdapterCall("RequestTransfer"));
        var rollbackTarget = LifecycleKernelHarness.New();
        var ticket = rollbackTarget.BeginAndCommitTransfer();
        rollbackTarget.AcceptAdapter(rollbackTarget.AdapterCall("RequestRollback", ticket));

        object capability;
        using (source.EnterAdapterScope(sourceRequest))
        using (source.EnterCallout("Cleanup"))
        {
            capability = source.CreateCalloutCapability("Cleanup");
            using (ExecutionContext.SuppressFlow())
            {
                var missing = Task.Run(
                    () => disposeTarget.AdapterCall("RequestDisposeFromFramework", (object)null)).Result;
                disposeTarget.RejectAdapter("InvalidToken", missing);
                var explicitDispose = Task.Run(
                    () => disposeTarget.AdapterCall("RequestDisposeFromFramework", capability)).Result;
                disposeTarget.RejectAdapter("CycleRisk", explicitDispose);

                var missingTransfer = Task.Run(
                    () => transferTarget.AdapterCall("RequestTransferFromFramework", (object)null)).Result;
                transferTarget.RejectAdapter("InvalidToken", missingTransfer);
                var explicitTransfer = Task.Run(
                    () => transferTarget.AdapterCall("RequestTransferFromFramework", capability)).Result;
                transferTarget.RejectAdapter("CycleRisk", explicitTransfer);

                var missingRollback = Task.Run(
                    () => rollbackTarget.AdapterCall(
                        "RequestRollbackFromFramework", ticket, (object)null)).Result;
                rollbackTarget.RejectAdapter("InvalidToken", missingRollback);
                var explicitRollback = Task.Run(
                    () => rollbackTarget.AdapterCall(
                        "RequestRollbackFromFramework", ticket, capability)).Result;
                rollbackTarget.RejectAdapter("CycleRisk", explicitRollback);
            }
        }
    }

    [TestMethod]
    public void AdapterTreatsDisposedTransferredAndRolledBackAsIdempotentTerminalResults()
    {
        var disposed = LifecycleKernelHarness.New();
        disposed.CompleteDisposal();
        disposed.AcceptAdapter(disposed.AdapterCall("RequestDispose"));

        var transferred = LifecycleKernelHarness.New();
        transferred.BeginAndCommitTransfer();
        transferred.AcceptAdapter(transferred.AdapterCall("RequestTransfer"));
        transferred.AcceptAdapter(transferred.AdapterCall("RequestDispose"));

        var rolledBack = LifecycleKernelHarness.New();
        var ticket = rolledBack.BeginAndCommitTransfer();
        var rollback = rolledBack.StartRollbackAttempt(ticket)[0];
        rolledBack.Accept(
            "CompleteRollback", rollback,
            rolledBack.EnumValue("KernelAttemptOutcome", "Success"));
        rolledBack.AcceptAdapter(rolledBack.AdapterCall("RequestRollback", ticket));
    }
}

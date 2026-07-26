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

    [DataTestMethod]
    [DataRow("same", "Success")]
    [DataRow("same", "RetryableFailure")]
    [DataRow("same", "StableFailure")]
    [DataRow("ordinary", "Success")]
    [DataRow("ordinary", "RetryableFailure")]
    [DataRow("ordinary", "StableFailure")]
    [DataRow("callout", "Success")]
    [DataRow("callout", "RetryableFailure")]
    [DataRow("callout", "StableFailure")]
    public void RunningRollbackPrecedesTransferredDisposeForEveryCallerClassAndOutcome(
        string caller,
        string outcome)
    {
        var target = LifecycleKernelHarness.New();
        var ticket = target.BeginAndCommitTransfer();
        var rollback = target.AdapterCall("RequestRollback", ticket);
        target.AcceptAdapter(rollback);
        object joined = null;

        if (caller == "same")
        {
            using (target.EnterAdapterScope(rollback))
                target.RejectAdapter("SameCausalAttempt", target.AdapterCall("RequestDispose"));
        }
        else if (caller == "ordinary")
        {
            joined = target.AdapterCall("RequestDispose");
            target.AcceptAdapter(joined);
            Assert.IsTrue((bool)LifecycleKernelHarness.Property(joined, "IsJoin"));
            Assert.IsNull(target.ObserveAdapterRequest(joined));
        }
        else
        {
            var source = LifecycleKernelHarness.New();
            var sourceRequest = source.AdapterCall("RequestDispose");
            source.AcceptAdapter(sourceRequest);
            using (source.EnterAdapterScope(sourceRequest))
            using (source.EnterCallout("Cleanup"))
                target.RejectAdapter("CycleRisk", target.AdapterCall("RequestDispose"));
        }

        var result = target.Value(target.Accept(
            "CompleteRollback",
            target.AdapterReceipt(rollback),
            target.EnumValue("KernelAttemptOutcome", outcome))).ToString();
        if (joined != null)
            Assert.AreEqual(result, target.ObserveAdapterRequest(joined));
        target.AssertValid();
    }

    [TestMethod]
    public void AtomicDisposeRequestJoinsActualAttemptAcrossRetryBarrier()
    {
        var kernel = LifecycleKernelHarness.New();
        var first = kernel.AdapterCall("RequestDispose");
        kernel.AcceptAdapter(first);
        var firstStage = kernel.Value(kernel.Accept(
            "BeginStage", kernel.EnumValue("KernelStage", "Managed"),
            kernel.AdapterReceipt(first)));
        var ready = new ManualResetEventSlim();
        Task<object> pending;
        object second;

        using (kernel.EnterKernelLock())
        {
            pending = Task.Run(() =>
            {
                ready.Set();
                return kernel.AdapterCall("RequestDispose");
            });
            Assert.IsTrue(ready.Wait(5000));
            kernel.Accept("FailRunningStage", firstStage, false);
            second = kernel.Accept("StartDispose", null, null);
        }

        var joined = pending.Result;
        kernel.AcceptAdapter(joined);
        Assert.IsTrue((bool)LifecycleKernelHarness.Property(joined, "IsJoin"));
        var result = kernel.Value(kernel.CompleteStartedDisposal(
            kernel.ReservationReceipt(second))).ToString();
        Assert.AreEqual(result, kernel.ObserveAdapterRequest(joined));
        Assert.AreEqual(2L, kernel.Number("DisposalRecordCount"));
    }

    [TestMethod]
    public void AtomicRollbackRequestJoinsActualAttemptAcrossRetryBarrier()
    {
        var kernel = LifecycleKernelHarness.New();
        var ticket = kernel.BeginAndCommitTransfer();
        var first = kernel.AdapterCall("RequestRollback", ticket);
        kernel.AcceptAdapter(first);
        var ready = new ManualResetEventSlim();
        Task<object> pending;
        object second;

        using (kernel.EnterKernelLock())
        {
            pending = Task.Run(() =>
            {
                ready.Set();
                return kernel.AdapterCall("RequestRollback", ticket);
            });
            Assert.IsTrue(ready.Wait(5000));
            kernel.Accept(
                "CompleteRollback", kernel.AdapterReceipt(first),
                kernel.EnumValue("KernelAttemptOutcome", "RetryableFailure"));
            second = kernel.Accept("StartRollback", ticket, null, null);
        }

        var joined = pending.Result;
        kernel.AcceptAdapter(joined);
        var result = kernel.Value(kernel.Accept(
            "CompleteRollback", kernel.ReservationReceipt(second),
            kernel.EnumValue("KernelAttemptOutcome", "Success"))).ToString();
        Assert.AreEqual(result, kernel.ObserveAdapterRequest(joined));
        Assert.AreEqual(2L, kernel.Number("RollbackRecordCount"));
    }

    [TestMethod]
    public void AtomicRequestsResolveIdleRunningAndTerminalAtKernelLock()
    {
        var idle = LifecycleKernelHarness.New();
        var idleReady = new ManualResetEventSlim();
        Task<object> idlePending;
        object owner;
        using (idle.EnterKernelLock())
        {
            idlePending = Task.Run(() =>
            {
                idleReady.Set();
                return idle.AdapterCall("RequestDispose");
            });
            Assert.IsTrue(idleReady.Wait(5000));
            owner = idle.Accept("StartDispose", null, null);
        }
        var idleJoined = idlePending.Result;
        idle.AcceptAdapter(idleJoined);
        Assert.IsTrue((bool)LifecycleKernelHarness.Property(idleJoined, "IsJoin"));
        idle.CompleteStartedDisposal(idle.ReservationReceipt(owner));

        var terminal = LifecycleKernelHarness.New();
        var ticket = terminal.BeginAndCommitTransfer();
        var rollback = terminal.StartRollbackAttempt(ticket)[0];
        var terminalReady = new ManualResetEventSlim();
        Task<object> terminalPending;
        using (terminal.EnterKernelLock())
        {
            terminalPending = Task.Run(() =>
            {
                terminalReady.Set();
                return terminal.AdapterCall("RequestDispose");
            });
            Assert.IsTrue(terminalReady.Wait(5000));
            terminal.Accept(
                "CompleteRollback", rollback,
                terminal.EnumValue("KernelAttemptOutcome", "Success"));
        }
        var terminalRequest = terminalPending.Result;
        terminal.AcceptAdapter(terminalRequest);
        Assert.IsFalse((bool)LifecycleKernelHarness.Property(terminalRequest, "IsJoin"));
        terminal.AssertValid();
    }
}

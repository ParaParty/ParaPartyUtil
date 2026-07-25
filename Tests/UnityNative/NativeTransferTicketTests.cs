using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative.Base;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class NativeTransferTicketTests
{
    [TestMethod]
    public void TransferRequiresExplicitSupportAndNoDeferredCleanupRequirements()
    {
        var unsupported = new TransferDisposable(false, CleanupStage.None);
        var requiredCleanup = new TransferDisposable(true, CleanupStage.Managed);
        var unsafeRollback = new TransferDisposable(true, CleanupStage.None, false);

        Assert.ThrowsException<NotSupportedException>(unsupported.CreateTransferTicket);
        Assert.ThrowsException<InvalidOperationException>(requiredCleanup.CreateTransferTicket);
        Assert.ThrowsException<InvalidOperationException>(unsafeRollback.CreateTransferTicket);

        unsupported.Dispose();
        requiredCleanup.Dispose();
        unsafeRollback.Dispose();
    }

    [TestMethod]
    public void TransferMovesPointerAndMakesWrapperTerminalWithoutDestroying()
    {
        var value = new TransferDisposable(true, CleanupStage.None);

        NativeTransferTicket ticket = value.CreateTransferTicket();

        Assert.AreEqual(NativeLifecycleState.Transferred, value.LifecycleState);
        Assert.IsTrue(value.IsDisposed);
        Assert.AreEqual(new IntPtr(0x1234), ticket.Pointer);
        Assert.AreEqual(NativeTransferState.Pending, ticket.State);
        Assert.AreEqual(0, value.CleanupCalls);
        Assert.ThrowsException<ObjectDisposedException>(() => value.EnterOperation());

        value.Dispose();
        Assert.AreEqual(0, value.CleanupCalls);

        Assert.IsTrue(ticket.TryRollback());
        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void ActiveLeasePreventsTransferWithoutClosingAdmission()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        using NativeOperationLease lease = value.EnterOperation();

        Assert.ThrowsException<InvalidOperationException>(value.CreateTransferTicket);
        Assert.AreEqual(new IntPtr(0x1234), lease.Pointer);
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
    }

    [TestMethod]
    public void TransferPreparationFailureLeavesWrapperActiveAndUsable()
    {
        var value = new TransferDisposable(true, CleanupStage.None)
        {
            PreparationResult = CleanupStageResult.RetryableFailure(new StageException()),
        };

        Assert.ThrowsException<InvalidOperationException>(value.CreateTransferTicket);

        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        Assert.AreEqual(0, value.CleanupCalls);
        using (NativeOperationLease lease = value.EnterOperation())
        {
            Assert.AreEqual(new IntPtr(0x1234), lease.Pointer);
        }

        value.Dispose();
        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void ReentrantDisposeDuringTransferPreparationFailsBeforeLifecycleMutation()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        value.PreparationAction = value.Dispose;

        Task<Exception> transfer = Task.Run(
            () => CaptureException(() => value.CreateTransferTicket()));

        Assert.IsTrue(
            transfer.Wait(TimeSpan.FromSeconds(5)),
            "Transfer preparation must reject self-disposal instead of waiting for itself.");
        Assert.IsInstanceOfType<InvalidOperationException>(transfer.Result);
        Assert.IsInstanceOfType<InvalidOperationException>(transfer.Result.InnerException);
        StringAssert.Contains(transfer.Result.InnerException.Message, "transfer preparations");
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        Assert.AreEqual(0, value.CleanupCalls);
        using (NativeOperationLease lease = value.EnterOperation())
        {
            Assert.AreEqual(new IntPtr(0x1234), lease.Pointer);
        }

        value.Dispose();
        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void FlowedChildCannotDisposeDuringTransferPreparation()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        Exception childFailure = null;
        value.PreparationAction = () =>
        {
            childFailure = Task.Run(() => CaptureException(value.Dispose)).Result;
            if (childFailure != null)
                throw childFailure;
        };

        Assert.ThrowsException<InvalidOperationException>(value.CreateTransferTicket);

        Assert.IsInstanceOfType<InvalidOperationException>(childFailure);
        StringAssert.Contains(childFailure.Message, "transfer preparations");
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        Assert.AreEqual(0, value.CleanupCalls);
        value.Dispose();
        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void ThrowingTransferPreparationDoesNotLeakExecutionMarker()
    {
        var value = new TransferDisposable(true, CleanupStage.None)
        {
            PreparationAction = () => throw new StageException(),
        };

        Assert.ThrowsException<InvalidOperationException>(value.CreateTransferTicket);

        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        Assert.AreEqual(0, value.CleanupCalls);
        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void NestedTransferPreparationRemainsRejectedAndRestoresAdmission()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        value.PreparationAction = () => value.CreateTransferTicket();

        InvalidOperationException failure =
            Assert.ThrowsException<InvalidOperationException>(value.CreateTransferTicket);

        Assert.IsInstanceOfType<ObjectDisposedException>(failure.InnerException);
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        Assert.AreEqual(0, value.CleanupCalls);
        using (NativeOperationLease lease = value.EnterOperation())
        {
            Assert.AreEqual(new IntPtr(0x1234), lease.Pointer);
        }

        value.Dispose();
    }

    [TestMethod]
    [DoNotParallelize]
    public void TicketConstructionFailureLeavesOwnershipWithActiveWrapper()
    {
        FieldInfo ticketCounter = typeof(NativeTransferTicket).GetField(
            "_lastTicketId",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(ticketCounter);
        long previous = (long)ticketCounter.GetValue(null);
        var value = new TransferDisposable(true, CleanupStage.None);

        try
        {
            ticketCounter.SetValue(null, long.MaxValue);

            Assert.ThrowsException<InvalidOperationException>(value.CreateTransferTicket);

            Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
            using (NativeOperationLease lease = value.EnterOperation())
            {
                Assert.AreEqual(new IntPtr(0x1234), lease.Pointer);
            }

            value.Dispose();
            Assert.AreEqual(1, value.CleanupCalls);
        }
        finally
        {
            ticketCounter.SetValue(null, previous);
        }
    }

    [TestMethod]
    public void DisposeWaitsForTransferPreparationBeforeDestroying()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        using var preparationEntered = new ManualResetEventSlim();
        using var releasePreparation = new ManualResetEventSlim();
        value.PreparationAction = () =>
        {
            preparationEntered.Set();
            releasePreparation.Wait(TimeSpan.FromSeconds(5));
        };

        Task<Exception> transfer = Task.Run(() => CaptureException(() => value.CreateTransferTicket()));
        Assert.IsTrue(preparationEntered.Wait(TimeSpan.FromSeconds(5)));
        Task dispose = Task.Run(value.Dispose);
        Assert.IsTrue(SpinWait.SpinUntil(
            () => value.LifecycleState == NativeLifecycleState.Disposing,
            TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, value.CleanupCalls);

        releasePreparation.Set();

        Assert.IsTrue(Task.WaitAll(new Task[] { transfer, dispose }, TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<ObjectDisposedException>(transfer.Result);
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void CommitAndRollbackHaveExactlyOneWinner()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        NativeTransferTicket ticket = value.CreateTransferTicket();
        using var barrier = new Barrier(3);

        Task<bool> commit = Task.Run(() => Race(barrier, ticket.TryCommit));
        Task<bool> rollback = Task.Run(() => Race(barrier, ticket.TryRollback));
        barrier.SignalAndWait(TimeSpan.FromSeconds(5));

        Assert.IsTrue(Task.WaitAll(new Task[] { commit, rollback }, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, new[] { commit.Result, rollback.Result }.Count(result => result));
        Assert.IsTrue(
            ticket.State == NativeTransferState.Committed ||
            ticket.State == NativeTransferState.RolledBack);
        Assert.AreEqual(ticket.State == NativeTransferState.RolledBack ? 1 : 0, value.CleanupCalls);
    }

    [TestMethod]
    public void RollbackAndCompletionHaveExactlyOneWinner()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        NativeTransferTicket ticket = value.CreateTransferTicket();
        using var barrier = new Barrier(3);

        Task<bool> rollback = Task.Run(() => Race(barrier, ticket.TryRollback));
        Task<bool> completion = Task.Run(() => Race(barrier, () => ticket.TryComplete(ticket.TicketId)));
        barrier.SignalAndWait(TimeSpan.FromSeconds(5));

        Assert.IsTrue(Task.WaitAll(new Task[] { rollback, completion }, TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, new[] { rollback.Result, completion.Result }.Count(result => result));
        Assert.IsTrue(
            ticket.State == NativeTransferState.RolledBack ||
            ticket.State == NativeTransferState.Completed);
        Assert.AreEqual(ticket.State == NativeTransferState.RolledBack ? 1 : 0, value.CleanupCalls);
    }

    [TestMethod]
    public void CompletionWinsBeforeOrAfterCommitAcknowledgement()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        NativeTransferTicket ticket = value.CreateTransferTicket();
        using var barrier = new Barrier(3);

        Task<bool> commit = Task.Run(() => Race(barrier, ticket.TryCommit));
        Task<bool> completion = Task.Run(() => Race(barrier, () => ticket.TryComplete(ticket.TicketId)));
        barrier.SignalAndWait(TimeSpan.FromSeconds(5));

        Assert.IsTrue(Task.WaitAll(new Task[] { commit, completion }, TimeSpan.FromSeconds(5)));
        Assert.IsTrue(completion.Result);
        Assert.AreEqual(NativeTransferState.Completed, ticket.State);
        Assert.AreEqual(NativeResourceLiveness.Freed, ticket.NativeLiveness);
        Assert.AreEqual(0, value.CleanupCalls);
    }

    [TestMethod]
    public void ThreeWayRacePreservesSingleOwnershipWinner()
    {
        for (int iteration = 0; iteration < 50; iteration++)
        {
            var value = new TransferDisposable(true, CleanupStage.None);
            NativeTransferTicket ticket = value.CreateTransferTicket();
            using var barrier = new Barrier(4);

            Task<bool> commit = Task.Run(() => Race(barrier, ticket.TryCommit));
            Task<bool> rollback = Task.Run(() => Race(barrier, ticket.TryRollback));
            Task<bool> completion = Task.Run(() => Race(barrier, () => ticket.TryComplete(ticket.TicketId)));
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(Task.WaitAll(
                new Task[] { commit, rollback, completion },
                TimeSpan.FromSeconds(5)));
            Assert.IsTrue(
                ticket.State == NativeTransferState.Committed ||
                ticket.State == NativeTransferState.RolledBack ||
                ticket.State == NativeTransferState.Completed);
            Assert.IsTrue(value.CleanupCalls == 0 || value.CleanupCalls == 1);
            if (value.CleanupCalls == 1)
                Assert.AreEqual(NativeTransferState.RolledBack, ticket.State);
        }
    }

    [TestMethod]
    public void StaleCompletionTicketIdCannotCompleteAnotherTransfer()
    {
        var first = new TransferDisposable(true, CleanupStage.None).CreateTransferTicket();
        var second = new TransferDisposable(true, CleanupStage.None).CreateTransferTicket();

        Assert.IsTrue(second.TicketId > first.TicketId);
        Assert.IsFalse(second.TryComplete(first.TicketId));
        Assert.AreEqual(NativeTransferState.Pending, second.State);
        Assert.IsTrue(second.TryComplete(second.TicketId));

        first.TryRollback();
    }

    [TestMethod]
    public void KnownLiveRollbackFailureCanRetryOnlyRollback()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        value.CleanupResults.Enqueue(
            NativeCleanupResult.KnownLiveRetryableFailure(new StageException()));
        value.CleanupResults.Enqueue(NativeCleanupResult.Freed());
        NativeTransferTicket ticket = value.CreateTransferTicket();

        NativeTransferCleanupException failure =
            Assert.ThrowsException<NativeTransferCleanupException>(() => ticket.TryRollback());

        Assert.IsTrue(failure.IsRetryable);
        Assert.AreEqual(NativeTransferState.RollbackFaulted, ticket.State);
        Assert.AreEqual(1L, ticket.RollbackAttemptEpoch);
        Assert.IsFalse(ticket.TryCommit());
        Assert.ThrowsException<InvalidOperationException>(() => _ = ticket.Pointer);

        Assert.IsTrue(ticket.TryRollback());
        Assert.AreEqual(NativeTransferState.RolledBack, ticket.State);
        Assert.AreEqual(2L, ticket.RollbackAttemptEpoch);
        Assert.AreEqual(2, value.CleanupCalls);
    }

    [TestMethod]
    public void UnknownRollbackFailureIsStableAndNeverRetriesDestroy()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        value.CleanupResults.Enqueue(
            NativeCleanupResult.LivenessUnknownFailure(new StageException()));
        NativeTransferTicket ticket = value.CreateTransferTicket();

        NativeTransferCleanupException first =
            Assert.ThrowsException<NativeTransferCleanupException>(() => ticket.TryRollback());
        NativeTransferCleanupException second =
            Assert.ThrowsException<NativeTransferCleanupException>(() => ticket.TryRollback());

        Assert.AreSame(first, second);
        Assert.IsFalse(first.IsRetryable);
        Assert.AreEqual(NativeResourceLiveness.Unknown, ticket.NativeLiveness);
        Assert.AreEqual(1, value.CleanupCalls);
        Assert.AreEqual(1L, ticket.RollbackAttemptEpoch);
        GC.SuppressFinalize(ticket);
    }

    [TestMethod]
    public void AbandonedPendingTicketRollsBackExactlyOnce()
    {
        var value = new TransferDisposable(true, CleanupStage.None);

        WeakReference weak = AbandonPendingTicket(value);
        ForceFinalization(weak);

        Assert.AreEqual(1, value.CleanupCalls);
    }

    [TestMethod]
    public void AbandonedCommittedCompletedAndRolledBackTicketsNeverRollbackAgain()
    {
        var committed = new TransferDisposable(true, CleanupStage.None);
        var completed = new TransferDisposable(true, CleanupStage.None);
        var rolledBack = new TransferDisposable(true, CleanupStage.None);

        WeakReference committedWeak = AbandonCommittedTicket(committed);
        WeakReference completedWeak = AbandonCompletedTicket(completed);
        WeakReference rolledBackWeak = AbandonRolledBackTicket(rolledBack);
        ForceFinalization(committedWeak);
        ForceFinalization(completedWeak);
        ForceFinalization(rolledBackWeak);

        Assert.AreEqual(0, committed.CleanupCalls);
        Assert.AreEqual(0, completed.CleanupCalls);
        Assert.AreEqual(1, rolledBack.CleanupCalls);
    }

    [TestMethod]
    public void ExplicitKnownLiveFailureGetsOneFinalizerRetry()
    {
        var value = new TransferDisposable(true, CleanupStage.None);
        value.CleanupResults.Enqueue(
            NativeCleanupResult.KnownLiveRetryableFailure(new StageException()));
        value.CleanupResults.Enqueue(NativeCleanupResult.Freed());

        WeakReference weak = AbandonAfterExplicitRollbackFailure(value);
        ForceFinalization(weak);

        Assert.AreEqual(2, value.CleanupCalls);
    }

    [TestMethod]
    public void ExplicitUnknownFailureDoesNotRetryAndReportsTruthfully()
    {
        Action<NativeTransferDiagnostic> previous = NativeTransferDiagnostics.Sink;
        var diagnostics = new ConcurrentQueue<NativeTransferDiagnostic>();
        using var received = new ManualResetEventSlim();
        var value = new TransferDisposable(true, CleanupStage.None);
        value.CleanupResults.Enqueue(
            NativeCleanupResult.LivenessUnknownFailure(new StageException()));

        try
        {
            NativeTransferDiagnostics.Sink = diagnostic =>
            {
                diagnostics.Enqueue(diagnostic);
                received.Set();
            };

            WeakReference weak = AbandonAfterExplicitRollbackFailure(value);
            ForceFinalization(weak);

            Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(diagnostics.TryDequeue(out NativeTransferDiagnostic diagnostic));
            Assert.AreEqual(NativeTransferState.RollbackFaulted, diagnostic.State);
            Assert.AreEqual(NativeResourceLiveness.Unknown, diagnostic.NativeLiveness);
            Assert.AreEqual(1L, diagnostic.RollbackAttemptEpoch);
            Assert.IsTrue(diagnostic.IsFinalizer);
            Assert.IsInstanceOfType<NativeTransferCleanupException>(diagnostic.Exception);
            Assert.AreEqual(1, value.CleanupCalls);
        }
        finally
        {
            NativeTransferDiagnostics.Sink = previous;
        }
    }

    [TestMethod]
    public void AbandonedPendingKnownLiveFailureReportsAfterOneAttempt()
    {
        Action<NativeTransferDiagnostic> previous = NativeTransferDiagnostics.Sink;
        var diagnostics = new ConcurrentQueue<NativeTransferDiagnostic>();
        using var received = new ManualResetEventSlim();
        var value = new TransferDisposable(true, CleanupStage.None);
        value.CleanupResults.Enqueue(
            NativeCleanupResult.KnownLiveRetryableFailure(new StageException()));

        try
        {
            NativeTransferDiagnostics.Sink = diagnostic =>
            {
                diagnostics.Enqueue(diagnostic);
                received.Set();
            };

            WeakReference weak = AbandonPendingTicket(value);
            ForceFinalization(weak);

            Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(diagnostics.TryDequeue(out NativeTransferDiagnostic diagnostic));
            Assert.AreEqual(NativeTransferState.RollbackFaulted, diagnostic.State);
            Assert.AreEqual(NativeResourceLiveness.KnownLive, diagnostic.NativeLiveness);
            Assert.AreEqual(1L, diagnostic.RollbackAttemptEpoch);
            Assert.AreEqual(1, value.CleanupCalls);
        }
        finally
        {
            NativeTransferDiagnostics.Sink = previous;
        }
    }

    [TestMethod]
    public void ThrowingTransferDiagnosticSinkCannotEscapeFinalizer()
    {
        Action<NativeTransferDiagnostic> previous = NativeTransferDiagnostics.Sink;
        var value = new TransferDisposable(true, CleanupStage.None)
        {
            ThrowFromCleanup = true,
        };

        try
        {
            NativeTransferDiagnostics.Sink = _ => throw new StageException();
            WeakReference weak = AbandonPendingTicket(value);

            ForceFinalization(weak);

            Assert.AreEqual(1, value.CleanupCalls);
        }
        finally
        {
            NativeTransferDiagnostics.Sink = previous;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonPendingTicket(TransferDisposable value)
    {
        NativeTransferTicket ticket = value.CreateTransferTicket();
        return new WeakReference(ticket);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonCommittedTicket(TransferDisposable value)
    {
        NativeTransferTicket ticket = value.CreateTransferTicket();
        Assert.IsTrue(ticket.TryCommit());
        return new WeakReference(ticket);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonCompletedTicket(TransferDisposable value)
    {
        NativeTransferTicket ticket = value.CreateTransferTicket();
        Assert.IsTrue(ticket.TryComplete(ticket.TicketId));
        return new WeakReference(ticket);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonRolledBackTicket(TransferDisposable value)
    {
        NativeTransferTicket ticket = value.CreateTransferTicket();
        Assert.IsTrue(ticket.TryRollback());
        return new WeakReference(ticket);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonAfterExplicitRollbackFailure(TransferDisposable value)
    {
        NativeTransferTicket ticket = value.CreateTransferTicket();
        Assert.ThrowsException<NativeTransferCleanupException>(() => ticket.TryRollback());
        return new WeakReference(ticket);
    }

    private static void ForceFinalization(WeakReference weak)
    {
        for (int attempt = 0; attempt < 10 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(10);
        }

        Assert.IsFalse(weak.IsAlive);
    }

    private static bool Race(Barrier barrier, Func<bool> action)
    {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException();
        return action();
    }

    private static Exception CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class TransferDisposable : DisposableNativeObject
    {
        private readonly bool _supportsTransfer;
        private readonly CleanupStage _requiredCleanup;
        private readonly bool _finalizerSafe;

        public TransferDisposable(
            bool supportsTransfer,
            CleanupStage requiredCleanup,
            bool finalizerSafe = true)
            : base(new IntPtr(0x1234))
        {
            _supportsTransfer = supportsTransfer;
            _requiredCleanup = requiredCleanup;
            _finalizerSafe = finalizerSafe;
        }

        public Queue<NativeCleanupResult> CleanupResults { get; } = new();

        public CleanupStageResult PreparationResult { get; set; } = CleanupStageResult.Succeeded();

        public Action PreparationAction { get; set; }

        public int CleanupCalls { get; private set; }

        public bool ThrowFromCleanup { get; set; }

        protected override bool SupportsNativeTransfer => _supportsTransfer;

        protected override bool IsTransferRollbackFinalizerSafe => _finalizerSafe;

        protected override CleanupStage RequiredCleanupBeforeTransfer => _requiredCleanup;

        protected override CleanupStageResult PrepareNativeTransfer()
        {
            PreparationAction?.Invoke();
            return PreparationResult;
        }

        protected override NativeCleanupResult CleanupNativeResource(IntPtr nativePointer)
        {
            CleanupCalls++;
            if (ThrowFromCleanup)
                throw new StageException();
            return CleanupResults.Count == 0 ? NativeCleanupResult.Freed() : CleanupResults.Dequeue();
        }
    }

    private sealed class StageException : Exception
    {
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative.Base;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class DisposableObjectLifecycleTests
{
    [TestMethod]
    public void SuccessfulDisposeCompletesEveryStageAndRepeatedDisposeIsNoOp()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);

        value.Dispose();
        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(CleanupStage.All, value.CompletedCleanupStages);
        Assert.AreEqual(NativeResourceLiveness.Freed, value.NativeLiveness);
        Assert.AreEqual(1L, value.AttemptEpoch);
        CollectionAssert.AreEqual(
            new[] { "managed", "fence", "native", "unpublish" },
            value.Calls);
    }

    [TestMethod]
    public void BorrowedOwnershipSkipsDestroyButStillInvalidatesWrapper()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Borrowed);

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(CleanupStage.All, value.CompletedCleanupStages);
        Assert.AreEqual(NativeResourceLiveness.KnownLive, value.NativeLiveness);
        Assert.AreEqual(0, value.NativeCalls);
        CollectionAssert.AreEqual(
            new[] { "managed", "fence", "unpublish" },
            value.Calls);
    }

    [TestMethod]
    public void ManagedAndFenceFailuresAreBothAttemptedAndFenceBlocksNative()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.ManagedResults.Enqueue(CleanupStageResult.RetryableFailure(new StageException("managed")));
        value.FenceResults.Enqueue(CleanupStageResult.RetryableFailure(new StageException("fence")));

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsTrue(failure.IsRetryable);
        Assert.AreEqual(CleanupStage.Managed | CleanupStage.CallbackFence, failure.FailedStages);
        Assert.AreEqual(CleanupStage.None, value.CompletedCleanupStages);
        Assert.AreEqual(1, value.ManagedCalls);
        Assert.AreEqual(1, value.FenceCalls);
        Assert.AreEqual(0, value.NativeCalls);
        Assert.AreEqual(0, value.UnpublishCalls);
    }

    [TestMethod]
    public void RetryRunsOnlyIncompleteRetryableStage()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.ManagedResults.Enqueue(CleanupStageResult.RetryableFailure(new StageException("managed")));
        value.ManagedResults.Enqueue(CleanupStageResult.Succeeded());

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsTrue(failure.IsRetryable);
        Assert.AreEqual(
            CleanupStage.CallbackFence | CleanupStage.Native | CleanupStage.OwnerUnpublish,
            value.CompletedCleanupStages);

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(2, value.ManagedCalls);
        Assert.AreEqual(1, value.FenceCalls);
        Assert.AreEqual(1, value.NativeCalls);
        Assert.AreEqual(1, value.UnpublishCalls);
        Assert.AreEqual(2L, value.AttemptEpoch);
    }

    [TestMethod]
    public void FenceRetryDelaysNativeUntilFenceCompletes()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.FenceResults.Enqueue(CleanupStageResult.RetryableFailure(new StageException("fence")));
        value.FenceResults.Enqueue(CleanupStageResult.Succeeded());

        Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.AreEqual(CleanupStage.Managed, value.CompletedCleanupStages);
        Assert.AreEqual(0, value.NativeCalls);

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.ManagedCalls);
        Assert.AreEqual(2, value.FenceCalls);
        Assert.AreEqual(1, value.NativeCalls);
        Assert.AreEqual(1, value.UnpublishCalls);
    }

    [TestMethod]
    public void KnownLiveNativeFailureCanRetryWithoutRepeatingCompletedStages()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.NativeResults.Enqueue(
            NativeCleanupResult.KnownLiveRetryableFailure(new StageException("destroy-before-delete")));
        value.NativeResults.Enqueue(NativeCleanupResult.Freed());

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsTrue(failure.IsRetryable);
        Assert.AreEqual(NativeResourceLiveness.KnownLive, failure.NativeLiveness);
        Assert.AreEqual(CleanupStage.Managed | CleanupStage.CallbackFence, value.CompletedCleanupStages);

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.ManagedCalls);
        Assert.AreEqual(1, value.FenceCalls);
        Assert.AreEqual(2, value.NativeCalls);
        Assert.AreEqual(1, value.UnpublishCalls);
    }

    [TestMethod]
    public void UnknownNativeLivenessCreatesStableFault()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.NativeResults.Enqueue(
            NativeCleanupResult.LivenessUnknownFailure(new StageException("destroy-after-delete-unknown")));

        NativeCleanupException first = Assert.ThrowsException<NativeCleanupException>(value.Dispose);
        NativeCleanupException second = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.AreSame(first, second);
        Assert.IsFalse(first.IsRetryable);
        Assert.AreEqual(NativeResourceLiveness.Unknown, first.NativeLiveness);
        Assert.AreEqual(NativeLifecycleState.DisposeFaulted, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCalls);
        Assert.AreEqual(1L, value.AttemptEpoch);
    }

    [TestMethod]
    public void ThrowingNativeHookDefaultsToStableUnknownLiveness()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned)
        {
            ThrowFromNative = true,
        };

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsFalse(failure.IsRetryable);
        Assert.AreEqual(NativeResourceLiveness.Unknown, failure.NativeLiveness);
        Assert.AreEqual(CleanupStage.Native, failure.FailedStages);
        Assert.AreEqual(1, failure.InnerExceptions.Count);
    }

    [TestMethod]
    public void RetryableOwnerUnpublishDoesNotRepeatNativeDestroy()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.UnpublishResults.Enqueue(CleanupStageResult.RetryableFailure(new StageException("registry")));
        value.UnpublishResults.Enqueue(CleanupStageResult.Succeeded());

        Assert.ThrowsException<NativeCleanupException>(value.Dispose);
        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCalls);
        Assert.AreEqual(2, value.UnpublishCalls);
    }

    [TestMethod]
    public void SameThreadReentrantDisposeDoesNotDeadlockOrStartAnotherAttempt()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.ManagedAction = value.Dispose;

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1L, value.AttemptEpoch);
        Assert.AreEqual(1, value.ManagedCalls);
    }

    [TestMethod]
    public void ConcurrentDisposeCallersObserveTheSameAttemptFailure()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        using var managedEntered = new ManualResetEventSlim();
        using var releaseManaged = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();

        value.ManagedAction = () =>
        {
            managedEntered.Set();
            releaseManaged.Wait(TimeSpan.FromSeconds(5));
        };
        value.ManagedResults.Enqueue(CleanupStageResult.RetryableFailure(new StageException("shared")));

        Task<Exception> first = Task.Run(() => CaptureException(value.Dispose));
        Assert.IsTrue(managedEntered.Wait(TimeSpan.FromSeconds(5)));
        Task<Exception> second = Task.Run(() =>
        {
            secondStarted.Set();
            return CaptureException(value.Dispose);
        });
        Assert.IsTrue(secondStarted.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(50);
        releaseManaged.Set();

        Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<NativeCleanupException>(first.Result);
        Assert.AreSame(first.Result, second.Result);
        Assert.AreEqual(1, value.ManagedCalls);
        Assert.AreEqual(1L, value.AttemptEpoch);
    }

    [TestMethod]
    public void NonRetryableManagedFailureStillCleansIndependentAndDependentStages()
    {
        var value = new ScriptedDisposable(NativeOwnershipKind.Owned);
        value.ManagedResults.Enqueue(CleanupStageResult.NonRetryableFailure(new StageException("managed")));

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsFalse(failure.IsRetryable);
        Assert.AreEqual(CleanupStage.Managed, failure.FailedStages);
        Assert.AreEqual(
            CleanupStage.CallbackFence | CleanupStage.Native | CleanupStage.OwnerUnpublish,
            value.CompletedCleanupStages);
        Assert.AreEqual(1, value.NativeCalls);
        Assert.AreEqual(1, value.UnpublishCalls);
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

    private sealed class StageException : Exception
    {
        public StageException(string message)
            : base(message)
        {
        }
    }

    private sealed class ScriptedDisposable : DisposableNativeObject
    {
        public ScriptedDisposable(NativeOwnershipKind ownership)
            : base(new IntPtr(0x1234), ownership)
        {
        }

        public Queue<CleanupStageResult> ManagedResults { get; } = new();

        public Queue<CleanupStageResult> FenceResults { get; } = new();

        public Queue<NativeCleanupResult> NativeResults { get; } = new();

        public Queue<CleanupStageResult> UnpublishResults { get; } = new();

        public List<string> Calls { get; } = new();

        public int ManagedCalls { get; private set; }

        public int FenceCalls { get; private set; }

        public int NativeCalls { get; private set; }

        public int UnpublishCalls { get; private set; }

        public Action ManagedAction { get; set; }

        public bool ThrowFromNative { get; set; }

        protected override CleanupStageResult CleanupManagedResources()
        {
            Calls.Add("managed");
            ManagedCalls++;
            ManagedAction?.Invoke();
            return Next(ManagedResults);
        }

        protected override CleanupStageResult FenceNativeCallbacks()
        {
            Calls.Add("fence");
            FenceCalls++;
            return Next(FenceResults);
        }

        protected override NativeCleanupResult CleanupNativeResource(IntPtr nativePointer)
        {
            Calls.Add("native");
            NativeCalls++;
            if (ThrowFromNative)
                throw new StageException("native throw");
            return NativeResults.Count == 0 ? NativeCleanupResult.Freed() : NativeResults.Dequeue();
        }

        protected override CleanupStageResult UnpublishOwner()
        {
            Calls.Add("unpublish");
            UnpublishCalls++;
            CleanupStageResult result = Next(UnpublishResults);
            if (result.IsSuccess)
                return base.UnpublishOwner();
            return result;
        }

        private static CleanupStageResult Next(Queue<CleanupStageResult> results)
        {
            return results.Count == 0 ? CleanupStageResult.Succeeded() : results.Dequeue();
        }
    }
}

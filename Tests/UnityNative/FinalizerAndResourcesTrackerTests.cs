using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative.Base;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class FinalizerAndResourcesTrackerTests
{
    [TestMethod]
    public void FinalizerDoesNotRunNativeStageWithoutSafeFenceDependency()
    {
        NativeCleanupDiagnostic diagnostic = CollectFinalizerDiagnostic(
            CleanupStage.Native | CleanupStage.OwnerUnpublish,
            NativeCleanupResult.Freed());

        Assert.AreEqual(NativeLifecycleState.DisposeFaulted, diagnostic.LifecycleState);
        Assert.AreEqual(CleanupStage.None, diagnostic.CompletedStages);
        Assert.AreEqual(NativeResourceLiveness.KnownLive, diagnostic.NativeLiveness);
        Assert.IsTrue(diagnostic.IsFinalizer);
    }

    [TestMethod]
    public void FinalizerReportsFreedWithoutFalselyReportingDisposed()
    {
        NativeCleanupDiagnostic diagnostic = CollectFinalizerDiagnostic(
            CleanupStage.CallbackFence | CleanupStage.Native | CleanupStage.OwnerUnpublish,
            NativeCleanupResult.Freed());

        Assert.AreEqual(NativeLifecycleState.DisposeFaulted, diagnostic.LifecycleState);
        Assert.AreEqual(
            CleanupStage.CallbackFence | CleanupStage.Native | CleanupStage.OwnerUnpublish,
            diagnostic.CompletedStages);
        Assert.AreEqual(NativeResourceLiveness.Freed, diagnostic.NativeLiveness);
    }

    [TestMethod]
    public void FinalizerReportsUnknownNativeLivenessTruthfully()
    {
        NativeCleanupDiagnostic diagnostic = CollectFinalizerDiagnostic(
            CleanupStage.CallbackFence | CleanupStage.Native | CleanupStage.OwnerUnpublish,
            NativeCleanupResult.LivenessUnknownFailure(new StageException("unknown")));

        Assert.AreEqual(NativeLifecycleState.DisposeFaulted, diagnostic.LifecycleState);
        Assert.AreEqual(CleanupStage.CallbackFence, diagnostic.CompletedStages);
        Assert.AreEqual(NativeResourceLiveness.Unknown, diagnostic.NativeLiveness);
        Assert.IsInstanceOfType<NativeCleanupException>(diagnostic.Exception);
    }

    [TestMethod]
    public void ThrowingDiagnosticSinkCannotEscapeFinalizer()
    {
        Action<NativeCleanupDiagnostic> previous = NativeCleanupDiagnostics.Sink;
        try
        {
            NativeCleanupDiagnostics.Sink = _ => throw new StageException("sink");
            WeakReference weak = CreateFinalizable(
                CleanupStage.None,
                NativeCleanupResult.Freed());

            ForceFinalization(weak);

            Assert.IsFalse(weak.IsAlive);
        }
        finally
        {
            NativeCleanupDiagnostics.Sink = previous;
        }
    }

    [TestMethod]
    public void FinalizerNeverWaitsForAnUnreachableLeakedLease()
    {
        NativeCleanupDiagnostic diagnostic = CollectFinalizerDiagnostic(
            CleanupStage.None,
            NativeCleanupResult.Freed(),
            true);

        Assert.AreEqual(NativeLifecycleState.DisposeFaulted, diagnostic.LifecycleState);
        Assert.AreEqual(NativeResourceLiveness.KnownLive, diagnostic.NativeLiveness);
    }

    [TestMethod]
    public void TrackerAttemptsEveryOwnerAndAggregatesFirstMiddleAndLastFailures()
    {
        var order = new List<string>();
        var tracker = new ResourcesTracker();
        tracker.T(new TrackerDisposable("first", order, StableFailure("first")));
        tracker.T(new TrackerDisposable("middle", order, StableFailure("middle")));
        tracker.T(new TrackerDisposable("last", order, StableFailure("last")));

        AggregateException failure = Assert.ThrowsException<AggregateException>(tracker.Dispose);

        CollectionAssert.AreEqual(new[] { "first", "middle", "last" }, order);
        Assert.AreEqual(3, failure.InnerExceptions.Count);
    }

    [TestMethod]
    public void TrackerRetryOnlyRevisitsRetainedFaultedOwners()
    {
        var order = new List<string>();
        var tracker = new ResourcesTracker();
        var retrying = new TrackerDisposable(
            "retry",
            order,
            CleanupStageResult.RetryableFailure(new StageException("retry")),
            CleanupStageResult.Succeeded());
        var successful = new TrackerDisposable("success", order, CleanupStageResult.Succeeded());
        tracker.T(retrying);
        tracker.T(successful);

        Assert.ThrowsException<AggregateException>(tracker.Dispose);
        tracker.Dispose();

        CollectionAssert.AreEqual(new[] { "retry", "success", "retry" }, order);
        Assert.AreEqual(2, retrying.ManagedCalls);
        Assert.AreEqual(1, successful.ManagedCalls);
        Assert.IsTrue(retrying.IsDisposed);
        Assert.IsTrue(successful.IsDisposed);
    }

    [TestMethod]
    public void TrackerClosesRegistrationBeforeCallingOwners()
    {
        var tracker = new ResourcesTracker();
        Exception trackingFailure = null;
        var owner = new TrackerDisposable("owner", new List<string>(), CleanupStageResult.Succeeded())
        {
            ManagedAction = () => trackingFailure = CaptureException(
                () => tracker.T(new TrackerDisposable(
                    "late",
                    new List<string>(),
                    CleanupStageResult.Succeeded()))),
        };
        tracker.T(owner);

        tracker.Dispose();

        Assert.IsInstanceOfType<ObjectDisposedException>(trackingFailure);
        Assert.IsTrue(owner.IsDisposed);
    }

    [TestMethod]
    public void ConcurrentTrackerCallersObserveTheSameAttemptFailure()
    {
        var tracker = new ResourcesTracker();
        using var entered = new ManualResetEventSlim();
        using var secondStarted = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = new TrackerDisposable(
            "owner",
            new List<string>(),
            StableFailure("failure"))
        {
            ManagedAction = () =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            },
        };
        tracker.T(owner);

        Task<Exception> first = Task.Run(() => CaptureException(tracker.Dispose));
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        Task<Exception> second = Task.Factory.StartNew(
            () =>
            {
                secondStarted.Set();
                return CaptureException(tracker.Dispose);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.IsTrue(secondStarted.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(100);
        release.Set();

        Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, TimeSpan.FromSeconds(5)));
        Assert.AreSame(first.Result, second.Result);
    }

    private static CleanupStageResult StableFailure(string message)
    {
        return CleanupStageResult.NonRetryableFailure(new StageException(message));
    }

    private static NativeCleanupDiagnostic CollectFinalizerDiagnostic(
        CleanupStage safeStages,
        NativeCleanupResult nativeResult,
        bool leakLease = false)
    {
        Action<NativeCleanupDiagnostic> previous = NativeCleanupDiagnostics.Sink;
        var diagnostics = new ConcurrentQueue<NativeCleanupDiagnostic>();
        using var received = new ManualResetEventSlim();

        try
        {
            NativeCleanupDiagnostics.Sink = diagnostic =>
            {
                if (diagnostic.ObjectType == typeof(FinalizableDisposable).FullName)
                {
                    diagnostics.Enqueue(diagnostic);
                    received.Set();
                }
            };

            WeakReference weak = leakLease
                ? CreateFinalizableWithLeakedLease(safeStages, nativeResult)
                : CreateFinalizable(safeStages, nativeResult);
            ForceFinalization(weak);
            Assert.IsTrue(received.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(diagnostics.TryDequeue(out NativeCleanupDiagnostic result));
            return result;
        }
        finally
        {
            NativeCleanupDiagnostics.Sink = previous;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFinalizable(
        CleanupStage safeStages,
        NativeCleanupResult nativeResult)
    {
        var value = new FinalizableDisposable(safeStages, nativeResult);
        return new WeakReference(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFinalizableWithLeakedLease(
        CleanupStage safeStages,
        NativeCleanupResult nativeResult)
    {
        var value = new FinalizableDisposable(safeStages, nativeResult);
        _ = value.EnterOperation();
        return new WeakReference(value);
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

    private sealed class FinalizableDisposable : DisposableNativeObject
    {
        private readonly CleanupStage _safeStages;
        private readonly NativeCleanupResult _nativeResult;

        public FinalizableDisposable(
            CleanupStage safeStages,
            NativeCleanupResult nativeResult)
            : base(new IntPtr(0x1234))
        {
            _safeStages = safeStages;
            _nativeResult = nativeResult;
        }

        protected override CleanupStage FinalizerSafeStages => _safeStages;

        protected override NativeCleanupResult CleanupNativeResource(IntPtr nativePointer)
        {
            return _nativeResult;
        }
    }

    private sealed class TrackerDisposable : DisposableObject
    {
        private readonly string _name;
        private readonly ICollection<string> _order;
        private readonly Queue<CleanupStageResult> _results;

        public TrackerDisposable(
            string name,
            ICollection<string> order,
            params CleanupStageResult[] results)
        {
            _name = name;
            _order = order;
            _results = new Queue<CleanupStageResult>(results);
        }

        public int ManagedCalls { get; private set; }

        public Action ManagedAction { get; set; }

        protected override CleanupStageResult CleanupManagedResources()
        {
            ManagedCalls++;
            _order.Add(_name);
            ManagedAction?.Invoke();
            return _results.Count == 0 ? CleanupStageResult.Succeeded() : _results.Dequeue();
        }
    }

    private sealed class StageException : Exception
    {
        public StageException(string message)
            : base(message)
        {
        }
    }
}

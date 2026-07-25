using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative.Base;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class NativeOperationLeaseTests
{
    [TestMethod]
    public void ActiveLeaseBlocksDisposeAndKeepsPointerValid()
    {
        var value = new LeaseDisposable(new IntPtr(0x1234));
        NativeOperationLease lease = value.EnterOperation();

        Task dispose;
        using (ExecutionContext.SuppressFlow())
        {
            dispose = Task.Run(value.Dispose);
        }
        Assert.IsTrue(SpinWait.SpinUntil(
            () => value.LifecycleState == NativeLifecycleState.Disposing,
            TimeSpan.FromSeconds(5)));

        Assert.AreEqual(new IntPtr(0x1234), lease.Pointer);
        Assert.ThrowsException<ObjectDisposedException>(() => value.EnterOperation());
        Assert.IsFalse(dispose.Wait(TimeSpan.FromMilliseconds(100)));

        lease.Dispose();

        Assert.IsTrue(dispose.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void LeaseReferenceTokenReleasesExactlyOnce()
    {
        var value = new LeaseDisposable(new IntPtr(0x1234));
        NativeOperationLease lease = value.EnterOperation();
        NativeOperationLease alias = lease;

        lease.Dispose();
        alias.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => _ = lease.Pointer);
        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void StaleLeaseCannotReleaseAYoungerToken()
    {
        var value = new LeaseDisposable(new IntPtr(0x1234));
        NativeOperationLease stale = value.EnterOperation();
        long staleToken = stale.LeaseToken;
        stale.Dispose();

        NativeOperationLease current = value.EnterOperation();
        Assert.IsTrue(current.LeaseToken > staleToken);
        Assert.AreEqual(stale.OwnerId, current.OwnerId);
        Assert.AreEqual(stale.LifecycleEpoch, current.LifecycleEpoch);

        stale.Dispose();
        Task dispose;
        using (ExecutionContext.SuppressFlow())
        {
            dispose = Task.Run(value.Dispose);
        }
        Assert.IsTrue(SpinWait.SpinUntil(
            () => value.LifecycleState == NativeLifecycleState.Disposing,
            TimeSpan.FromSeconds(5)));
        Assert.IsFalse(dispose.Wait(TimeSpan.FromMilliseconds(100)));

        current.Dispose();
        Assert.IsTrue(dispose.Wait(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void PublishNativePointerIsOneTimeAndRequiredForOperations()
    {
        var value = new LeaseDisposable();

        Assert.ThrowsException<InvalidOperationException>(() => value.EnterOperation());
        value.Publish(new IntPtr(0x4321));
        Assert.ThrowsException<InvalidOperationException>(() => value.Publish(new IntPtr(0x9999)));

        using NativeOperationLease lease = value.EnterOperation();
        Assert.AreEqual(new IntPtr(0x4321), lease.Pointer);
    }

    [TestMethod]
    public void GroupAcquireUsesStableOwnerOrderAndDeduplicatesSameOwner()
    {
        var first = new LeaseDisposable(new IntPtr(1));
        var second = new LeaseDisposable(new IntPtr(2));
        var order = new List<long>();
        var firstSource = new RecordingSource(first, order);
        var secondSource = new RecordingSource(second, order);

        using NativeOperationGroup group = NativeOperationGroup.Acquire(
            secondSource,
            firstSource,
            secondSource);

        long[] expected = new[] { first.NativeOperationOwnerId, second.NativeOperationOwnerId }
            .OrderBy(ownerId => ownerId)
            .ToArray();
        CollectionAssert.AreEqual(expected, order.ToArray());
        Assert.AreEqual(2, group.Leases.Count);
    }

    [TestMethod]
    public void GroupAcquireRollsBackEarlierLeasesWhenLaterAcquireFails()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        var source = new RecordingSource(value, new List<long>());
        var failing = new ThrowingSource(long.MaxValue);

        Assert.ThrowsException<StageException>(() => NativeOperationGroup.Acquire(source, failing));

        Task dispose = Task.Run(value.Dispose);
        Assert.IsTrue(dispose.Wait(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void CreatingThreadConfinementRejectsForeignOperationsAndExplicitDispose()
    {
        var value = new LeaseDisposable(
            new IntPtr(1),
            NativeAccessPolicy.CreatingThreadConfined);

        Exception enterFailure = Task.Run(() => CaptureException(() => value.EnterOperation())).Result;
        Exception disposeFailure = Task.Run(() => CaptureException(value.Dispose)).Result;

        Assert.IsInstanceOfType<InvalidOperationException>(enterFailure);
        Assert.IsInstanceOfType<InvalidOperationException>(disposeFailure);
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void OperationExceptionStillReleasesLeaseWithUsing()
    {
        var value = new LeaseDisposable(new IntPtr(1));

        Assert.ThrowsException<StageException>(() =>
        {
            using NativeOperationLease lease = value.EnterOperation();
            _ = lease.Pointer;
            throw new StageException();
        });

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void DisposeInsideLeaseFailsFastBeforeClosingAdmission()
    {
        var value = new LeaseDisposable(new IntPtr(1));

        Task test = Task.Run(() =>
        {
            using (NativeOperationLease lease = value.EnterOperation())
            {
                InvalidOperationException failure = Assert.ThrowsException<InvalidOperationException>(value.Dispose);
                StringAssert.Contains(failure.Message, "operation leases");
                Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);

                using (NativeOperationLease nested = value.EnterOperation())
                {
                    Assert.AreEqual(new IntPtr(1), nested.Pointer);
                }
            }
        });

        Assert.IsTrue(test.Wait(TimeSpan.FromSeconds(5)), "Self-dispose must fail instead of waiting for its own lease.");
        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void NestedLeaseKeepsSelfDisposeRejectedUntilOutermostRelease()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        NativeOperationLease outer = value.EnterOperation();
        NativeOperationLease inner = value.EnterOperation();

        Assert.ThrowsException<InvalidOperationException>(value.Dispose);
        inner.Dispose();
        Assert.ThrowsException<InvalidOperationException>(value.Dispose);
        outer.Dispose();

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void LogicalChildContextCannotDisposeAnInheritedLeaseOwner()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        using (NativeOperationLease lease = value.EnterOperation())
        {
            Task<Exception> callback = Task.Run(() => CaptureException(value.Dispose));

            Assert.IsTrue(callback.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsInstanceOfType<InvalidOperationException>(callback.Result);
            Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        }

        value.Dispose();
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void PointerAccessAttachesLeaseToAContextWithoutExecutionFlow()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        NativeOperationLease lease = value.EnterOperation();
        Task<Exception> callback;

        using (ExecutionContext.SuppressFlow())
        {
            callback = Task.Run(() =>
            {
                Assert.AreEqual(new IntPtr(1), lease.Pointer);
                return CaptureException(value.Dispose);
            });
        }

        Assert.IsTrue(callback.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<InvalidOperationException>(callback.Result);

        lease.Dispose();
        value.Dispose();
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void CallbackScopeWithoutLeaseObjectBreaksNoFlowNativeWaitCycle()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        NativeOperationLease callerLease = value.EnterOperation();
        Assert.AreEqual(new IntPtr(1), callerLease.Pointer);

        Task<Exception> callback = StartNoFlowCallback(value);
        if (!callback.Wait(TimeSpan.FromSeconds(5)))
        {
            callerLease.Dispose();
            callback.Wait(TimeSpan.FromSeconds(5));
            Assert.Fail("The callback tried to drain the native caller's lease.");
        }

        Assert.IsInstanceOfType<InvalidOperationException>(callback.Result);
        StringAssert.Contains(callback.Result.Message, "callback scopes");
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);

        using (NativeOperationLease next = value.EnterOperation())
        {
            Assert.AreEqual(new IntPtr(1), next.Pointer);
        }

        callerLease.Dispose();
        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void NestedCallbackScopesRejectDisposeUntilOutermostExit()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        NativeCallbackExecutionScope outer = value.EnterCallback();
        NativeCallbackExecutionScope inner = value.EnterCallback();

        Assert.ThrowsException<InvalidOperationException>(value.Dispose);
        inner.Dispose();
        Assert.ThrowsException<InvalidOperationException>(value.Dispose);
        outer.Dispose();

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void CallbackScopeFlowsToLogicalChildContext()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        using (NativeCallbackExecutionScope callbackScope = value.EnterCallback())
        {
            Task<Exception> child = Task.Run(() => CaptureException(value.Dispose));

            Assert.IsTrue(child.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsInstanceOfType<InvalidOperationException>(child.Result);
            Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        }

        value.Dispose();
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void DeactivatedCallbackMarkerDoesNotRejectInheritedChildContext()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        var releaseChild = new ManualResetEventSlim(false);
        NativeCallbackExecutionScope callbackScope = value.EnterCallback();
        Task<Exception> child = Task.Run(() =>
        {
            releaseChild.Wait(TimeSpan.FromSeconds(5));
            return CaptureException(value.Dispose);
        });

        callbackScope.Dispose();
        releaseChild.Set();

        Assert.IsTrue(child.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsNull(child.Result);
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void CallbackScopeRejectsOnlyItsOwner()
    {
        var first = new LeaseDisposable(new IntPtr(1));
        var second = new LeaseDisposable(new IntPtr(2));

        using (NativeCallbackExecutionScope callbackScope = first.EnterCallback())
        {
            second.Dispose();
            Assert.AreEqual(NativeLifecycleState.Disposed, second.LifecycleState);
            Assert.ThrowsException<InvalidOperationException>(first.Dispose);
            Assert.AreEqual(NativeLifecycleState.Active, first.LifecycleState);
        }

        first.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, first.LifecycleState);
    }

    [TestMethod]
    public void CallbackCommandGuardRejectsBeforeMutationAndNativeCall()
    {
        var value = new LeaseDisposable(new IntPtr(1));

        using (NativeCallbackExecutionScope callbackScope = value.EnterCallback())
        {
            InvalidOperationException failure =
                Assert.ThrowsException<InvalidOperationException>(value.ExecuteLifecycleCommand);

            StringAssert.Contains(failure.Message, "own native callbacks");
            Assert.AreEqual(0, value.CommandMutations);
            Assert.AreEqual(0, value.CommandNativeCalls);
            Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);
        }

        value.ExecuteLifecycleCommand();
        Assert.AreEqual(1, value.CommandMutations);
        Assert.AreEqual(1, value.CommandNativeCalls);
        value.Dispose();
    }

    [TestMethod]
    public void NestedAndFlowedCallbackCommandGuardsRemainOwnerScoped()
    {
        var first = new LeaseDisposable(new IntPtr(1));
        var second = new LeaseDisposable(new IntPtr(2));

        using (NativeCallbackExecutionScope outer = first.EnterCallback())
        using (NativeCallbackExecutionScope inner = first.EnterCallback())
        {
            Task<Exception> child = Task.Run(() => CaptureException(first.ExecuteLifecycleCommand));

            Assert.IsTrue(child.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsInstanceOfType<InvalidOperationException>(child.Result);
            Assert.ThrowsException<InvalidOperationException>(first.ExecuteLifecycleCommand);
            second.ExecuteLifecycleCommand();
        }

        Assert.AreEqual(0, first.CommandMutations);
        Assert.AreEqual(1, second.CommandMutations);
        first.Dispose();
        second.Dispose();
    }

    [TestMethod]
    public void SuppressedFlowCallbackCommandGuardRejectsSameOwner()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        Task<Exception> callback;

        using (ExecutionContext.SuppressFlow())
        {
            callback = Task.Run(() =>
            {
                using (NativeCallbackExecutionScope callbackScope = value.EnterCallback())
                {
                    return CaptureException(value.ExecuteLifecycleCommand);
                }
            });
        }

        Assert.IsTrue(callback.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<InvalidOperationException>(callback.Result);
        Assert.AreEqual(0, value.CommandMutations);
        Assert.AreEqual(0, value.CommandNativeCalls);
        value.Dispose();
    }

    [TestMethod]
    public void OrdinaryLeaseDoesNotTriggerCallbackCommandGuard()
    {
        var value = new LeaseDisposable(new IntPtr(1));

        using (NativeOperationLease lease = value.EnterOperation())
        {
            value.ExecuteLifecycleCommand();
            Assert.AreEqual(new IntPtr(1), lease.Pointer);
        }

        Assert.AreEqual(1, value.CommandMutations);
        Assert.AreEqual(1, value.CommandNativeCalls);
        value.Dispose();
    }

    [TestMethod]
    public void CallbackScopeCanBeDisposedFromAnotherThread()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        NativeCallbackExecutionScope callbackScope = value.EnterCallback();
        Task release;

        using (ExecutionContext.SuppressFlow())
        {
            release = Task.Run(callbackScope.Dispose);
        }

        Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
        callbackScope.Dispose();
        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void ForeignCallbackScopeDoesNotGrantConfinedOperationAccess()
    {
        var value = new LeaseDisposable(
            new IntPtr(1),
            NativeAccessPolicy.CreatingThreadConfined);
        Task<Exception[]> callback;

        using (ExecutionContext.SuppressFlow())
        {
            callback = Task.Run(() =>
            {
                using (NativeCallbackExecutionScope callbackScope = value.EnterCallback())
                {
                    Exception enterFailure = CaptureException(() =>
                    {
                        using NativeOperationLease operation = value.EnterOperation();
                    });
                    Exception disposeFailure = CaptureException(value.Dispose);
                    return new[] { enterFailure, disposeFailure };
                }
            });
        }

        Assert.IsTrue(callback.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsInstanceOfType<InvalidOperationException>(callback.Result[0]);
        Assert.IsInstanceOfType<InvalidOperationException>(callback.Result[1]);
        StringAssert.Contains(callback.Result[1].Message, "callback scopes");
        Assert.AreEqual(NativeLifecycleState.Active, value.LifecycleState);

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void CallbackExceptionStillDeactivatesScopeWithUsing()
    {
        var value = new LeaseDisposable(new IntPtr(1));

        Assert.ThrowsException<StageException>(() =>
        {
            using NativeCallbackExecutionScope callbackScope = value.EnterCallback();
            throw new StageException();
        });

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void AbandonedCallbackScopeFinalizerDeactivatesMarker()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        WeakReference weak = AbandonCallbackScope(value);

        ForceFinalization(weak);

        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.NativeCleanupCalls);
    }

    [TestMethod]
    public void LeaseCanStillBeReleasedFromAContextWithoutExecutionFlow()
    {
        var value = new LeaseDisposable(new IntPtr(1));
        NativeOperationLease lease = value.EnterOperation();
        Task release;

        using (ExecutionContext.SuppressFlow())
        {
            release = Task.Run(lease.Dispose);
        }

        Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
        value.Dispose();
        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
    }

    [TestMethod]
    public void DisposableNativeObjectHasNoReusableRawPointerSurface()
    {
        Type type = typeof(DisposableNativeObject);
        const BindingFlags reusableSurface =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Assert.IsNull(type.GetProperty("NativePtr", reusableSurface));
        Assert.IsFalse(type.GetFields(reusableSurface).Any(field =>
            field.FieldType == typeof(IntPtr) &&
            (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)));
        Assert.IsNull(type.Assembly.GetType("Paraparty.UnityNative.Base.INativePtrHolder"));
        Assert.IsNull(typeof(DisposableObject).GetProperty("IsEnabledDispose", reusableSurface));
        Assert.IsFalse(typeof(DisposableObject).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(bool))));
        Assert.IsFalse(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(bool))));
        Assert.AreEqual(typeof(IntPtr), typeof(NativeOperationLease).GetProperty("Pointer")?.PropertyType);
        Assert.AreEqual(0, typeof(NativeCallbackExecutionScope).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length);
    }

    private static Task<Exception> StartNoFlowCallback(LeaseDisposable value)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() =>
            {
                using (NativeCallbackExecutionScope callbackScope = value.EnterCallback())
                {
                    return CaptureException(value.Dispose);
                }
            });
        }
    }

    private static WeakReference AbandonCallbackScope(LeaseDisposable value)
    {
        var scope = value.EnterCallback();
        return new WeakReference(scope);
    }

    private static void ForceFinalization(WeakReference weak)
    {
        for (int attempt = 0; attempt < 10 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(weak.IsAlive, "The callback scope did not finalize within the bounded collection loop.");
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

    private sealed class LeaseDisposable : DisposableNativeObject
    {
        public LeaseDisposable()
        {
        }

        public LeaseDisposable(IntPtr pointer)
            : base(pointer)
        {
        }

        public LeaseDisposable(IntPtr pointer, NativeAccessPolicy accessPolicy)
            : base(pointer, NativeOwnershipKind.Owned, accessPolicy)
        {
        }

        public int NativeCleanupCalls { get; private set; }

        public int CommandMutations { get; private set; }

        public int CommandNativeCalls { get; private set; }

        public void Publish(IntPtr pointer)
        {
            PublishNativePointer(pointer);
        }

        public NativeCallbackExecutionScope EnterCallback()
        {
            return EnterNativeCallbackExecution();
        }

        public void ExecuteLifecycleCommand()
        {
            ThrowIfCurrentExecutionIsNativeCallback();
            CommandMutations++;
            CommandNativeCalls++;
        }

        protected override NativeCleanupResult CleanupNativeResource(IntPtr nativePointer)
        {
            NativeCleanupCalls++;
            return NativeCleanupResult.Freed();
        }
    }

    private sealed class RecordingSource : INativeOperationSource
    {
        private readonly INativeOperationSource _inner;
        private readonly ICollection<long> _order;

        public RecordingSource(INativeOperationSource inner, ICollection<long> order)
        {
            _inner = inner;
            _order = order;
        }

        public long NativeOperationOwnerId => _inner.NativeOperationOwnerId;

        public NativeOperationLease EnterOperation()
        {
            _order.Add(NativeOperationOwnerId);
            return _inner.EnterOperation();
        }
    }

    private sealed class ThrowingSource : INativeOperationSource
    {
        public ThrowingSource(long ownerId)
        {
            NativeOperationOwnerId = ownerId;
        }

        public long NativeOperationOwnerId { get; }

        public NativeOperationLease EnterOperation()
        {
            throw new StageException();
        }
    }

    private sealed class StageException : Exception
    {
    }
}

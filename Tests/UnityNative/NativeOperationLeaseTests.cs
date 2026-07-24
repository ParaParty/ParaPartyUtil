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

        Task dispose = Task.Run(value.Dispose);
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
        Task dispose = Task.Run(value.Dispose);
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
        Assert.AreEqual(typeof(IntPtr), typeof(NativeOperationLease).GetProperty("Pointer")?.PropertyType);
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

        public void Publish(IntPtr pointer)
        {
            PublishNativePointer(pointer);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative.Base;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class NativeQuiesceTests
{
    [TestMethod]
    public void CleanupRunsManagedQuiesceFenceNativeAndUnpublishInOrder()
    {
        var value = new QuiesceDisposable();

        value.Dispose();

        CollectionAssert.AreEqual(
            new[] { "managed", "quiesce", "fence", "native", "unpublish" },
            value.Calls);
        Assert.AreEqual(CleanupStage.All, value.CompletedCleanupStages);
        Assert.AreEqual(1, value.Tokens.Count);
        Assert.AreEqual(new IntPtr(0x1234), value.ResolvedPointers.Single());
        Assert.IsFalse(value.Tokens[0].IsActive);
        Assert.IsTrue(value.NativeObservedTokenInactive);
        Assert.IsTrue(value.UnpublishObservedTokenInactive);
    }

    [TestMethod]
    public void RetryUsesFreshTokenWithoutChangingPointerPublication()
    {
        var value = new QuiesceDisposable();
        value.QuiesceResults.Enqueue(
            CleanupStageResult.RetryableFailure(new StageException("deadline")));
        value.QuiesceResults.Enqueue(CleanupStageResult.Succeeded());

        NativeCleanupException first = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsTrue(first.IsRetryable);
        Assert.AreEqual(CleanupStage.Managed, first.CompletedStages);
        Assert.AreEqual(CleanupStage.NativeQuiesce, first.FailedStages);
        Assert.AreEqual(1, value.ManagedCalls);
        Assert.AreEqual(1, value.QuiesceCalls);
        Assert.AreEqual(0, value.FenceCalls);
        Assert.AreEqual(0, value.NativeCalls);
        NativeQuiesceToken firstToken = value.Tokens.Single();
        Assert.IsFalse(firstToken.IsActive);

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.ManagedCalls);
        Assert.AreEqual(2, value.QuiesceCalls);
        Assert.AreEqual(1, value.FenceCalls);
        Assert.AreEqual(1, value.NativeCalls);
        Assert.AreEqual(2, value.Tokens.Count);
        NativeQuiesceToken secondToken = value.Tokens[1];
        Assert.AreNotSame(firstToken, secondToken);
        Assert.AreEqual(firstToken.OwnerId, secondToken.OwnerId);
        Assert.IsTrue(secondToken.LifecycleEpoch > firstToken.LifecycleEpoch);
        Assert.AreEqual(
            firstToken.PointerPublicationGeneration,
            secondToken.PointerPublicationGeneration);
        Assert.IsTrue(
            secondToken.QuiesceAttemptGeneration > firstToken.QuiesceAttemptGeneration);
        Assert.IsFalse(secondToken.IsActive);
    }

    [TestMethod]
    public void QuiesceSuccessRejectsInHookPublicationInvalidationBeforeReceipt()
    {
        var value = new QuiesceDisposable();
        value.QuiesceAction = _ => value.InvalidatePublication();

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsFalse(failure.IsRetryable);
        Assert.AreEqual(CleanupStage.Managed, value.CompletedCleanupStages);
        Assert.AreEqual(CleanupStage.NativeQuiesce, failure.FailedStages);
        Assert.AreEqual(1, value.QuiesceCalls);
        Assert.AreEqual(0, value.FenceCalls);
        Assert.AreEqual(0, value.NativeCalls);
        Assert.AreEqual(1, value.UnpublishCalls);
        Assert.IsFalse(value.Tokens.Single().IsActive);
    }

    [TestMethod]
    public void DestroyRejectsUnpublishedReceiptBeforeNativeCall()
    {
        var value = new QuiesceDisposable();
        value.FenceAction = value.InvalidatePublication;

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsFalse(failure.IsRetryable);
        Assert.AreEqual(
            CleanupStage.Managed | CleanupStage.NativeQuiesce | CleanupStage.CallbackFence,
            value.CompletedCleanupStages);
        Assert.AreEqual(CleanupStage.Native, failure.FailedStages);
        Assert.AreEqual(NativeResourceLiveness.KnownLive, value.NativeLiveness);
        Assert.AreEqual(0, value.NativeCalls);
        Assert.AreEqual(0, value.DestroyedPointers.Count);
    }

    [TestMethod]
    public void DestroyRejectsSameAddressRepublishedGenerationBeforeNativeCall()
    {
        var value = new QuiesceDisposable();
        value.FenceAction = () => value.ReplacePublicationForTest(new IntPtr(0x1234));

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsFalse(failure.IsRetryable);
        Assert.AreEqual(CleanupStage.Native, failure.FailedStages);
        Assert.AreEqual(NativeResourceLiveness.KnownLive, value.NativeLiveness);
        Assert.AreEqual(0, value.NativeCalls);
        Assert.AreEqual(0, value.DestroyedPointers.Count);
    }

    [TestMethod]
    public void NativeRetryReusesReceiptOnlyForTheUnchangedPublication()
    {
        var value = new QuiesceDisposable();
        value.NativeResults.Enqueue(
            NativeCleanupResult.KnownLiveRetryableFailure(new StageException("native retry")));
        value.NativeResults.Enqueue(NativeCleanupResult.Freed());

        NativeCleanupException first = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsTrue(first.IsRetryable);
        Assert.AreEqual(
            CleanupStage.Managed | CleanupStage.NativeQuiesce | CleanupStage.CallbackFence,
            value.CompletedCleanupStages);
        Assert.AreEqual(1, value.QuiesceCalls);
        Assert.AreEqual(1, value.NativeCalls);

        value.Dispose();

        Assert.AreEqual(NativeLifecycleState.Disposed, value.LifecycleState);
        Assert.AreEqual(1, value.QuiesceCalls);
        Assert.AreEqual(2, value.NativeCalls);
        CollectionAssert.AreEqual(
            new[] { new IntPtr(0x1234), new IntPtr(0x1234) },
            value.DestroyedPointers);
    }

    [TestMethod]
    public void ActiveForeignQuiesceTokenFailsBeforeNativeCall()
    {
        var owner = new QuiesceDisposable();
        var foreign = new QuiesceDisposable();
        owner.QuiesceAction = token =>
        {
            Assert.IsTrue(token.IsActive);
            Assert.ThrowsException<InvalidOperationException>(
                () => foreign.ResolveForSimulatedNativeCall(token));
        };

        owner.Dispose();

        Assert.AreEqual(0, foreign.StaleResolutionNativeCalls);
        foreign.Dispose();
    }

    [TestMethod]
    public void RevokedTokenFailsBeforeAStaleNativeCallCanBegin()
    {
        var value = new QuiesceDisposable();
        value.QuiesceResults.Enqueue(
            CleanupStageResult.RetryableFailure(new StageException("deadline")));

        Assert.ThrowsException<NativeCleanupException>(value.Dispose);
        NativeQuiesceToken stale = value.Tokens.Single();

        Assert.ThrowsException<InvalidOperationException>(() => value.ResolveForSimulatedNativeCall(stale));
        Assert.AreEqual(0, value.StaleResolutionNativeCalls);
        Assert.IsFalse(stale.IsActive);

        value.Dispose();
    }

    [TestMethod]
    public void RetryableTimeoutIsObservableAndBlocksFenceAndDestroy()
    {
        var value = new QuiesceDisposable();
        value.QuiesceResults.Enqueue(
            CleanupStageResult.RetryableFailure(new TimeoutException("native stop deadline")));

        NativeCleanupException failure = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.IsTrue(failure.IsRetryable);
        Assert.AreEqual(CleanupStage.Managed, value.CompletedCleanupStages);
        Assert.AreEqual(CleanupStage.NativeQuiesce, failure.FailedStages);
        Assert.AreEqual(0, value.FenceCalls);
        Assert.AreEqual(0, value.NativeCalls);
        Assert.AreEqual(0, value.UnpublishCalls);

        value.Dispose();
    }

    [TestMethod]
    public void NonRetryableQuiesceFailureIsStableAndDoesNotRunAgain()
    {
        var value = new QuiesceDisposable();
        value.QuiesceResults.Enqueue(
            CleanupStageResult.NonRetryableFailure(new StageException("unsafe retry")));

        NativeCleanupException first = Assert.ThrowsException<NativeCleanupException>(value.Dispose);
        NativeCleanupException second = Assert.ThrowsException<NativeCleanupException>(value.Dispose);

        Assert.AreSame(first, second);
        Assert.IsFalse(first.IsRetryable);
        Assert.AreEqual(CleanupStage.Managed, value.CompletedCleanupStages);
        Assert.AreEqual(1, value.QuiesceCalls);
        Assert.AreEqual(0, value.FenceCalls);
        Assert.AreEqual(0, value.NativeCalls);
        Assert.IsFalse(value.Tokens.Single().IsActive);
    }

    [TestMethod]
    public void DefaultFinalizerDoesNotInvokeNativeQuiesce()
    {
        FinalizableQuiesceDisposable.Reset();

        WeakReference weak = CreateFinalizableQuiesceDisposable();
        ForceFinalization(weak);

        Assert.IsFalse(weak.IsAlive);
        Assert.AreEqual(0, FinalizableQuiesceDisposable.QuiesceCalls);
        Assert.AreEqual(0, FinalizableQuiesceDisposable.NativeCalls);
    }

    [TestMethod]
    public void TokenPublicApiIsOpaqueAndCleanupStageValuesRemainStable()
    {
        Type tokenType = typeof(NativeQuiesceToken);

        Assert.IsTrue(tokenType.IsSealed);
        Assert.AreEqual(0, tokenType.GetConstructors().Length);
        Assert.IsFalse(tokenType.GetProperties().Any(property => property.PropertyType == typeof(IntPtr)));
        Assert.IsFalse(tokenType.GetFields().Any(field => field.FieldType == typeof(IntPtr)));
        Assert.AreEqual(1, (int)CleanupStage.Managed);
        Assert.AreEqual(2, (int)CleanupStage.CallbackFence);
        Assert.AreEqual(4, (int)CleanupStage.Native);
        Assert.AreEqual(8, (int)CleanupStage.OwnerUnpublish);
        Assert.AreEqual(16, (int)CleanupStage.NativeQuiesce);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFinalizableQuiesceDisposable()
    {
        return new WeakReference(new FinalizableQuiesceDisposable());
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

    private sealed class QuiesceDisposable : DisposableNativeObject
    {
        public QuiesceDisposable()
            : base(new IntPtr(0x1234))
        {
        }

        public Queue<CleanupStageResult> QuiesceResults { get; } = new();

        public Queue<NativeCleanupResult> NativeResults { get; } = new();

        public List<string> Calls { get; } = new();

        public List<NativeQuiesceToken> Tokens { get; } = new();

        public List<IntPtr> ResolvedPointers { get; } = new();

        public List<IntPtr> DestroyedPointers { get; } = new();

        public Action<NativeQuiesceToken> QuiesceAction { get; set; }

        public Action FenceAction { get; set; }

        public int ManagedCalls { get; private set; }

        public int QuiesceCalls { get; private set; }

        public int FenceCalls { get; private set; }

        public int NativeCalls { get; private set; }

        public int UnpublishCalls { get; private set; }

        public int StaleResolutionNativeCalls { get; private set; }

        public bool NativeObservedTokenInactive { get; private set; }

        public bool UnpublishObservedTokenInactive { get; private set; }

        public IntPtr ResolveForSimulatedNativeCall(NativeQuiesceToken token)
        {
            IntPtr nativePointer = GetNativePointer(token);
            StaleResolutionNativeCalls++;
            return nativePointer;
        }

        public void InvalidatePublication()
        {
            UnpublishOwner();
        }

        public void ReplacePublicationForTest(IntPtr nativePointer)
        {
            Type type = typeof(DisposableNativeObject);
            FieldInfo pointer = type.GetField("_nativePointer", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo published = type.GetField("_pointerPublished", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo generation = type.GetField(
                "_pointerPublicationGeneration",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(pointer);
            Assert.IsNotNull(published);
            Assert.IsNotNull(generation);

            long nextGeneration = checked((long)generation.GetValue(this) + 1);
            pointer.SetValue(this, nativePointer);
            published.SetValue(this, true);
            generation.SetValue(this, nextGeneration);
        }

        protected override CleanupStageResult CleanupManagedResources()
        {
            Calls.Add("managed");
            ManagedCalls++;
            return CleanupStageResult.Succeeded();
        }

        protected override CleanupStageResult QuiesceNativeResource(NativeQuiesceToken token)
        {
            Calls.Add("quiesce");
            QuiesceCalls++;
            Tokens.Add(token);
            Assert.IsTrue(token.IsActive);
            Assert.AreEqual(NativeOperationOwnerId, token.OwnerId);
            Assert.AreEqual(LifecycleEpoch, token.LifecycleEpoch);
            ResolvedPointers.Add(GetNativePointer(token));
            QuiesceAction?.Invoke(token);
            return QuiesceResults.Count == 0
                ? CleanupStageResult.Succeeded()
                : QuiesceResults.Dequeue();
        }

        protected override CleanupStageResult FenceNativeCallbacks()
        {
            Calls.Add("fence");
            FenceCalls++;
            FenceAction?.Invoke();
            return CleanupStageResult.Succeeded();
        }

        protected override NativeCleanupResult CleanupNativeResource(IntPtr nativePointer)
        {
            Calls.Add("native");
            NativeCalls++;
            DestroyedPointers.Add(nativePointer);
            NativeObservedTokenInactive = Tokens.All(token => !token.IsActive);
            return NativeResults.Count == 0 ? NativeCleanupResult.Freed() : NativeResults.Dequeue();
        }

        protected override CleanupStageResult UnpublishOwner()
        {
            Calls.Add("unpublish");
            UnpublishCalls++;
            CleanupStageResult result = base.UnpublishOwner();
            UnpublishObservedTokenInactive = Tokens.All(token => !token.IsActive);
            return result;
        }
    }

    private sealed class FinalizableQuiesceDisposable : DisposableNativeObject
    {
        public FinalizableQuiesceDisposable()
            : base(new IntPtr(0x1234))
        {
        }

        public static int QuiesceCalls;

        public static int NativeCalls;

        protected override CleanupStage FinalizerSafeStages =>
            CleanupStage.CallbackFence | CleanupStage.Native | CleanupStage.OwnerUnpublish;

        public static void Reset()
        {
            QuiesceCalls = 0;
            NativeCalls = 0;
        }

        protected override CleanupStageResult QuiesceNativeResource(NativeQuiesceToken token)
        {
            Interlocked.Increment(ref QuiesceCalls);
            return CleanupStageResult.Succeeded();
        }

        protected override NativeCleanupResult CleanupNativeResource(IntPtr nativePointer)
        {
            Interlocked.Increment(ref NativeCalls);
            return NativeCleanupResult.Freed();
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

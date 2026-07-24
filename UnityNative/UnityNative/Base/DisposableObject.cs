using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Owns a staged, retry-aware cleanup lifecycle.
    /// </summary>
    public abstract class DisposableObject : IDisposable
    {
        private sealed class DisposeAttempt
        {
            public DisposeAttempt(long epoch, int ownerThreadId)
            {
                Epoch = epoch;
                OwnerThreadId = ownerThreadId;
            }

            public long Epoch { get; }

            public int OwnerThreadId { get; }

            public bool IsComplete { get; set; }

            public NativeCleanupException Failure { get; set; }
        }

        private static readonly CleanupStage[] OrderedStages =
        {
            CleanupStage.Managed,
            CleanupStage.CallbackFence,
            CleanupStage.Native,
            CleanupStage.OwnerUnpublish,
        };

        private readonly object _lifecycleLock = new object();
        private readonly Dictionary<CleanupStage, CleanupStageResult> _stageFailures =
            new Dictionary<CleanupStage, CleanupStageResult>();

        private DisposeAttempt _currentAttempt;
        private NativeCleanupException _stableFailure;
        private NativeLifecycleState _lifecycleState;
        private CleanupStage _completedStages;
        private NativeResourceLiveness _nativeLiveness;
        private long _attemptEpoch;
        private long _lifecycleEpoch;
        private bool _baseNativeResourcesReleased;

        protected GCHandle DataHandle { get; private set; }

        protected IntPtr AllocatedMemory { get; set; }

        protected long AllocatedMemorySize { get; set; }

        protected DisposableObject()
            : this(NativeOwnershipKind.Owned)
        {
        }

        protected DisposableObject(NativeOwnershipKind ownership)
        {
            if (ownership != NativeOwnershipKind.Owned && ownership != NativeOwnershipKind.Borrowed)
                throw new ArgumentOutOfRangeException(nameof(ownership));

            Ownership = ownership;
            _lifecycleState = NativeLifecycleState.Active;
            _nativeLiveness = NativeResourceLiveness.KnownLive;
            AllocatedMemory = IntPtr.Zero;
        }

        public NativeOwnershipKind Ownership { get; }

        public NativeLifecycleState LifecycleState
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _lifecycleState;
                }
            }
        }

        public bool IsDisposed
        {
            get
            {
                NativeLifecycleState state = LifecycleState;
                return state == NativeLifecycleState.Disposed || state == NativeLifecycleState.Transferred;
            }
        }

        public CleanupStage CompletedCleanupStages
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _completedStages;
                }
            }
        }

        public long AttemptEpoch
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _attemptEpoch;
                }
            }
        }

        public long LifecycleEpoch
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _lifecycleEpoch;
                }
            }
        }

        public NativeResourceLiveness NativeLiveness
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _nativeLiveness;
                }
            }
        }

        public void Dispose()
        {
            ValidateExplicitDisposeThread();

            DisposeAttempt attempt;
            bool ownsAttempt;

            lock (_lifecycleLock)
            {
                if (_lifecycleState == NativeLifecycleState.Disposed ||
                    _lifecycleState == NativeLifecycleState.Transferred)
                {
                    GC.SuppressFinalize(this);
                    return;
                }

                if (_lifecycleState == NativeLifecycleState.Disposing)
                {
                    attempt = _currentAttempt;
                    if (attempt.OwnerThreadId == Thread.CurrentThread.ManagedThreadId)
                        return;

                    ownsAttempt = false;
                }
                else
                {
                    if (_stableFailure != null)
                        throw _stableFailure;

                    attempt = StartAttemptLocked();
                    ownsAttempt = true;
                }
            }

            if (ownsAttempt)
                ExecuteAttempt(attempt, true);
            else
                WaitForAttempt(attempt);

            if (attempt.Failure != null)
                throw attempt.Failure;

            GC.SuppressFinalize(this);
        }

        ~DisposableObject()
        {
            try
            {
                ExecuteFinalizerAttempt();
            }
            catch
            {
                // A finalizer must never terminate the process. Detailed diagnostics are added later.
            }
        }

        protected virtual CleanupStageResult CleanupManagedResources()
        {
            return CleanupStageResult.Succeeded();
        }

        protected virtual CleanupStageResult FenceNativeCallbacks()
        {
            return CleanupStageResult.Succeeded();
        }

        protected virtual NativeCleanupResult CleanupNativeResource()
        {
            return NativeCleanupResult.Freed();
        }

        protected virtual CleanupStageResult UnpublishOwner()
        {
            return CleanupStageResult.Succeeded();
        }

        protected virtual void ValidateExplicitDisposeThread()
        {
        }

        protected virtual void CloseOperationAdmissionAndDrain()
        {
        }

        protected bool TryTransitionToTransferred()
        {
            lock (_lifecycleLock)
            {
                if (_lifecycleState != NativeLifecycleState.Active)
                    return false;

                checked
                {
                    _lifecycleEpoch++;
                }

                _lifecycleState = NativeLifecycleState.Transferred;
                return true;
            }
        }

        protected internal GCHandle AllocGCHandle(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            ThrowIfDisposed();
            if (DataHandle.IsAllocated)
                DataHandle.Free();
            DataHandle = GCHandle.Alloc(obj, GCHandleType.Pinned);
            return DataHandle;
        }

        protected IntPtr AllocMemory(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            ThrowIfDisposed();
            if (AllocatedMemory != IntPtr.Zero)
                Marshal.FreeHGlobal(AllocatedMemory);
            AllocatedMemory = Marshal.AllocHGlobal(size);
            NotifyMemoryPressure(size);
            return AllocatedMemory;
        }

        protected void NotifyMemoryPressure(long size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            if (AllocatedMemorySize > 0)
                GC.RemoveMemoryPressure(AllocatedMemorySize);

            AllocatedMemorySize = size;
            GC.AddMemoryPressure(size);
        }

        public void ThrowIfDisposed()
        {
            NativeLifecycleState state = LifecycleState;
            if (state != NativeLifecycleState.Active)
            {
                string message = "Accessing an unavailable object of type " + GetType().FullName +
                                 ". LifecycleState=" + state + ", Object Hash: " + GetHashCode().ToString("X") + ".";
                throw new ObjectDisposedException(GetType().FullName, message);
            }
        }

        private DisposeAttempt StartAttemptLocked()
        {
            checked
            {
                _attemptEpoch++;
                _lifecycleEpoch++;
            }

            _lifecycleState = NativeLifecycleState.Disposing;
            _currentAttempt = new DisposeAttempt(_attemptEpoch, Thread.CurrentThread.ManagedThreadId);
            return _currentAttempt;
        }

        private void WaitForAttempt(DisposeAttempt attempt)
        {
            lock (_lifecycleLock)
            {
                while (!attempt.IsComplete)
                    Monitor.Wait(_lifecycleLock);
            }
        }

        private void ExecuteAttempt(DisposeAttempt attempt, bool includeManagedStage)
        {
            CloseOperationAdmissionAndDrain();

            if (includeManagedStage)
                ExecuteStage(CleanupStage.Managed, InvokeManagedCleanup);

            ExecuteStage(CleanupStage.CallbackFence, InvokeCallbackFence);

            if (IsStageComplete(CleanupStage.CallbackFence))
                ExecuteNativeStage();

            if (IsStageComplete(CleanupStage.Native))
                ExecuteStage(CleanupStage.OwnerUnpublish, InvokeOwnerUnpublish);

            CompleteAttempt(attempt);
        }

        private void ExecuteFinalizerAttempt()
        {
            DisposeAttempt attempt;

            lock (_lifecycleLock)
            {
                if (_lifecycleState == NativeLifecycleState.Disposed ||
                    _lifecycleState == NativeLifecycleState.Transferred ||
                    _lifecycleState == NativeLifecycleState.Disposing ||
                    _stableFailure != null)
                {
                    return;
                }

                attempt = StartAttemptLocked();
            }

            ExecuteAttempt(attempt, false);
        }

        private void ExecuteStage(CleanupStage stage, Func<CleanupStageResult> cleanup)
        {
            if (IsStageComplete(stage))
                return;

            CleanupStageResult result;
            try
            {
                result = cleanup();
                if (result == null)
                    throw new InvalidOperationException("Cleanup stage " + stage + " returned null.");
            }
            catch (Exception exception)
            {
                result = CleanupStageResult.NonRetryableFailure(exception);
            }

            lock (_lifecycleLock)
            {
                if (result.IsSuccess)
                {
                    _completedStages |= stage;
                    _stageFailures.Remove(stage);
                }
                else
                {
                    _stageFailures[stage] = result;
                }
            }
        }

        private void ExecuteNativeStage()
        {
            if (IsStageComplete(CleanupStage.Native))
                return;

            NativeCleanupResult nativeResult = null;
            CleanupStageResult baseResourcesResult;

            if (Ownership == NativeOwnershipKind.Borrowed)
            {
                lock (_lifecycleLock)
                {
                    _nativeLiveness = NativeResourceLiveness.KnownLive;
                }
            }
            else
            {
                try
                {
                    nativeResult = CleanupNativeResource();
                    if (nativeResult == null)
                        throw new InvalidOperationException("Native cleanup returned null.");
                }
                catch (Exception exception)
                {
                    nativeResult = NativeCleanupResult.LivenessUnknownFailure(exception);
                }

                lock (_lifecycleLock)
                {
                    _nativeLiveness = nativeResult.Liveness;
                }
            }

            baseResourcesResult = ReleaseBaseNativeResources();

            lock (_lifecycleLock)
            {
                bool nativeSucceeded = Ownership == NativeOwnershipKind.Borrowed || nativeResult.IsSuccess;
                if (nativeSucceeded && baseResourcesResult.IsSuccess)
                {
                    _completedStages |= CleanupStage.Native;
                    _stageFailures.Remove(CleanupStage.Native);
                    return;
                }

                var exceptions = new List<Exception>();
                CleanupFailureDisposition disposition = CleanupFailureDisposition.Retryable;

                if (!nativeSucceeded)
                {
                    exceptions.Add(nativeResult.Exception);
                    disposition = nativeResult.Disposition;
                }

                if (!baseResourcesResult.IsSuccess)
                {
                    exceptions.Add(baseResourcesResult.Exception);
                    disposition = CleanupFailureDisposition.NonRetryable;
                }

                Exception exception = exceptions.Count == 1
                    ? exceptions[0]
                    : new AggregateException("Native-stage substeps failed.", exceptions);

                _stageFailures[CleanupStage.Native] = disposition == CleanupFailureDisposition.Retryable
                    ? CleanupStageResult.RetryableFailure(exception)
                    : CleanupStageResult.NonRetryableFailure(exception);
            }
        }

        private CleanupStageResult ReleaseBaseNativeResources()
        {
            lock (_lifecycleLock)
            {
                if (_baseNativeResourcesReleased)
                    return CleanupStageResult.Succeeded();
            }

            var failures = new List<Exception>();

            try
            {
                if (DataHandle.IsAllocated)
                    DataHandle.Free();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                if (AllocatedMemorySize > 0)
                {
                    GC.RemoveMemoryPressure(AllocatedMemorySize);
                    AllocatedMemorySize = 0;
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                if (AllocatedMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(AllocatedMemory);
                    AllocatedMemory = IntPtr.Zero;
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count != 0)
            {
                return CleanupStageResult.NonRetryableFailure(
                    failures.Count == 1
                        ? failures[0]
                        : new AggregateException("Base native resource cleanup failed.", failures));
            }

            lock (_lifecycleLock)
            {
                _baseNativeResourcesReleased = true;
            }

            return CleanupStageResult.Succeeded();
        }

        private void CompleteAttempt(DisposeAttempt attempt)
        {
            lock (_lifecycleLock)
            {
                if (_completedStages == CleanupStage.All)
                {
                    _lifecycleState = NativeLifecycleState.Disposed;
                    attempt.Failure = null;
                }
                else
                {
                    _lifecycleState = NativeLifecycleState.DisposeFaulted;
                    attempt.Failure = BuildFailureLocked(attempt.Epoch);
                    if (!attempt.Failure.IsRetryable)
                        _stableFailure = attempt.Failure;
                }

                attempt.IsComplete = true;
                Monitor.PulseAll(_lifecycleLock);
            }
        }

        private NativeCleanupException BuildFailureLocked(long attemptEpoch)
        {
            CleanupStage failedStages = CleanupStage.None;
            bool hasRetryableFailure = false;
            bool hasNonRetryableFailure = false;
            var exceptions = new List<Exception>();

            foreach (CleanupStage stage in OrderedStages)
            {
                CleanupStageResult result;
                if (!_stageFailures.TryGetValue(stage, out result))
                    continue;

                failedStages |= stage;
                exceptions.Add(result.Exception);
                hasRetryableFailure |= result.Disposition == CleanupFailureDisposition.Retryable;
                hasNonRetryableFailure |= result.Disposition == CleanupFailureDisposition.NonRetryable;
            }

            if (exceptions.Count == 0)
            {
                exceptions.Add(new InvalidOperationException(
                    "Cleanup ended without completing all required stages and without a stage diagnosis."));
                hasNonRetryableFailure = true;
            }

            return new NativeCleanupException(
                attemptEpoch,
                NativeLifecycleState.DisposeFaulted,
                _completedStages,
                failedStages,
                _nativeLiveness,
                hasRetryableFailure && !hasNonRetryableFailure,
                exceptions);
        }

        private bool IsStageComplete(CleanupStage stage)
        {
            lock (_lifecycleLock)
            {
                return (_completedStages & stage) == stage;
            }
        }

        private CleanupStageResult InvokeManagedCleanup()
        {
            return CleanupManagedResources();
        }

        private CleanupStageResult InvokeCallbackFence()
        {
            return FenceNativeCallbacks();
        }

        private CleanupStageResult InvokeOwnerUnpublish()
        {
            return UnpublishOwner();
        }
    }
}

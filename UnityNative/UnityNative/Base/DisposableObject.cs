using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Owns a staged, retry-aware cleanup lifecycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The public workflow is <c>Active -&gt; Disposing -&gt; Disposed</c> on success and
    /// <c>Active -&gt; Disposing -&gt; DisposeFaulted</c> when cleanup remains incomplete.
    /// A native wrapper may instead make the one-way transition <c>Active -&gt; Transferred</c>.
    /// Neither disposal nor transfer ever returns an instance to <c>Active</c>.
    /// </para>
    /// <para>
    /// Cleanup is a dependency graph, not an expanded lifecycle enum. Managed cleanup and callback
    /// fencing run before native cleanup; owner unpublication follows native completion. The four
    /// nodes are independent completion bits, so a later attempt skips completed work. A failed node
    /// is retryable only when its result explicitly proves another attempt is safe. An exception or
    /// unknown native liveness creates a stable fault rather than risking repeated irreversible work.
    /// Base-owned pinned handles and unmanaged buffers are part of the native node: an owned wrapper
    /// releases them only after the cleanup oracle proves the native resource freed. Known-live or
    /// unknown results retain that supporting memory; a borrowed wrapper releases it after its callback
    /// fence because no native destruction is attempted.
    /// </para>
    /// <para>
    /// One thread owns each monotonic cleanup-attempt epoch. Concurrent callers wait for that exact
    /// attempt and observe the same result; reentrant disposal by the owner returns without deadlock.
    /// The lifecycle epoch never moves backward, preventing a stale operation from being mistaken for
    /// a newer lifecycle. Native pointer values are never used as liveness or identity evidence.
    /// </para>
    /// <para>
    /// Explicit disposal closes operation admission and drains admitted work before executing the graph.
    /// Finalization is a restricted, non-blocking executor: it runs only <see cref="FinalizerSafeStages"/>,
    /// contains every exception, and emits truthful diagnostics when the object remains nonterminal.
    /// </para>
    /// </remarks>
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

        /// <summary>Gets the pinned managed handle owned by the base native-cleanup stage.</summary>
        protected GCHandle DataHandle { get; private set; }

        /// <summary>Gets or sets the unmanaged allocation owned by the base native-cleanup stage.</summary>
        protected IntPtr AllocatedMemory { get; set; }

        /// <summary>Gets or sets the memory-pressure size associated with <see cref="AllocatedMemory"/>.</summary>
        protected long AllocatedMemorySize { get; set; }

        /// <summary>Initializes an active object that owns its native resource.</summary>
        protected DisposableObject()
            : this(NativeOwnershipKind.Owned)
        {
        }

        /// <summary>Initializes an active object with immutable native ownership.</summary>
        /// <param name="ownership">Whether native cleanup destroys or only invalidates the native resource.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ownership"/> is not a defined ownership kind.</exception>
        protected DisposableObject(NativeOwnershipKind ownership)
        {
            if (ownership != NativeOwnershipKind.Owned && ownership != NativeOwnershipKind.Borrowed)
                throw new ArgumentOutOfRangeException(nameof(ownership));

            Ownership = ownership;
            _lifecycleState = NativeLifecycleState.Active;
            _nativeLiveness = NativeResourceLiveness.KnownLive;
            AllocatedMemory = IntPtr.Zero;
        }

        /// <summary>Gets the immutable ownership selected during construction.</summary>
        public NativeOwnershipKind Ownership { get; }

        /// <summary>Gets the current externally observable lifecycle state under the lifecycle lock.</summary>
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

        /// <summary>Gets whether the wrapper reached either terminal state, disposed or transferred.</summary>
        public bool IsDisposed
        {
            get
            {
                NativeLifecycleState state = LifecycleState;
                return state == NativeLifecycleState.Disposed || state == NativeLifecycleState.Transferred;
            }
        }

        /// <summary>Gets the cleanup stages proven complete across all attempts.</summary>
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

        /// <summary>Gets the monotonic identity of the most recently started cleanup attempt.</summary>
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

        /// <summary>Gets the monotonic lifecycle identity used to reject stale operation leases.</summary>
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

        /// <summary>Gets the last oracle-proven liveness of the native resource.</summary>
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

        /// <summary>
        /// Closes operation admission, waits for admitted work, and executes all incomplete cleanup stages.
        /// </summary>
        /// <exception cref="NativeCleanupException">
        /// Cleanup remains incomplete. A later call is allowed only when <see cref="NativeCleanupException.IsRetryable"/> is true.
        /// </exception>
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
                ExecuteAttempt(attempt, CleanupStage.All, true);
            else
                WaitForAttempt(attempt);

            if (attempt.Failure != null)
                throw attempt.Failure;

            GC.SuppressFinalize(this);
        }

        /// <summary>Runs the configured finalizer-safe subset and reports nonterminal cleanup without throwing.</summary>
        ~DisposableObject()
        {
            Exception failure = null;
            try
            {
                failure = ExecuteFinalizerAttempt();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                NativeLifecycleState state = LifecycleState;
                if (state != NativeLifecycleState.Disposed && state != NativeLifecycleState.Transferred)
                {
                    NativeCleanupDiagnostics.ReportNoThrow(new NativeCleanupDiagnostic(
                        GetType().FullName,
                        state,
                        CompletedCleanupStages,
                        Ownership,
                        NativeLiveness,
                        AttemptEpoch,
                        true,
                        failure));
                }
            }
            catch
            {
                // The finalizer must never allow either cleanup or telemetry to escape.
            }
        }

        /// <summary>
        /// Gets the cleanup stages that are explicitly safe on the finalizer thread. The default permits none.
        /// Dependencies must also be complete before a listed stage can run.
        /// </summary>
        protected virtual CleanupStage FinalizerSafeStages => CleanupStage.None;

        /// <summary>Releases managed registrations and state for the managed cleanup stage.</summary>
        /// <returns>A result that explicitly states success or retry safety.</returns>
        protected virtual CleanupStageResult CleanupManagedResources()
        {
            return CleanupStageResult.Succeeded();
        }

        /// <summary>Stops callbacks and registrations before native destruction can begin.</summary>
        /// <returns>A result that explicitly states success or retry safety.</returns>
        protected virtual CleanupStageResult FenceNativeCallbacks()
        {
            return CleanupStageResult.Succeeded();
        }

        /// <summary>Destroys an owned native resource and reports oracle-proven liveness.</summary>
        /// <returns>A result that never infers liveness from a pointer value.</returns>
        protected virtual NativeCleanupResult CleanupNativeResource()
        {
            return NativeCleanupResult.Freed();
        }

        /// <summary>Removes native ownership publication after the native stage completes.</summary>
        /// <returns>A result that explicitly states success or retry safety.</returns>
        protected virtual CleanupStageResult UnpublishOwner()
        {
            return CleanupStageResult.Succeeded();
        }

        /// <summary>Validates any thread policy required before explicit disposal starts.</summary>
        /// <exception cref="InvalidOperationException">The current thread violates the object's disposal policy.</exception>
        protected virtual void ValidateExplicitDisposeThread()
        {
        }

        /// <summary>Closes new operation admission and waits for every admitted operation to leave.</summary>
        protected virtual void CloseOperationAdmissionAndDrain()
        {
        }

        /// <summary>Atomically makes the one-way transition from active to transferred.</summary>
        /// <returns><see langword="true"/> when this call won the transition; otherwise, <see langword="false"/>.</returns>
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

        /// <summary>Replaces the base-owned pinned handle with a handle for <paramref name="obj"/>.</summary>
        /// <param name="obj">The managed object to pin while the wrapper is active.</param>
        /// <returns>The allocated pinned handle.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">The wrapper is not active.</exception>
        protected internal GCHandle AllocGCHandle(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            ThrowIfDisposed();
            if (DataHandle.IsAllocated)
            {
                GCHandle previousHandle = DataHandle;
                previousHandle.Free();
                DataHandle = default(GCHandle);
            }
            DataHandle = GCHandle.Alloc(obj, GCHandleType.Pinned);
            return DataHandle;
        }

        /// <summary>Replaces the base-owned unmanaged allocation and records matching memory pressure.</summary>
        /// <param name="size">The positive number of bytes to allocate.</param>
        /// <returns>The allocated unmanaged address.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
        /// <exception cref="ObjectDisposedException">The wrapper is not active.</exception>
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

        /// <summary>Replaces the memory-pressure accounting associated with the base allocation.</summary>
        /// <param name="size">The positive number of unmanaged bytes currently owned.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive.</exception>
        protected void NotifyMemoryPressure(long size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            if (AllocatedMemorySize > 0)
                GC.RemoveMemoryPressure(AllocatedMemorySize);

            AllocatedMemorySize = size;
            GC.AddMemoryPressure(size);
        }

        /// <summary>Throws unless the wrapper still accepts ordinary operations.</summary>
        /// <exception cref="ObjectDisposedException">The lifecycle state is not <see cref="NativeLifecycleState.Active"/>.</exception>
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

        private void ExecuteAttempt(
            DisposeAttempt attempt,
            CleanupStage allowedStages,
            bool closeOperationAdmission)
        {
            try
            {
                if (closeOperationAdmission)
                    CloseOperationAdmissionAndDrain();

                allowedStages &= CleanupStage.All;
                if ((allowedStages & CleanupStage.Managed) != 0)
                    ExecuteStage(CleanupStage.Managed, InvokeManagedCleanup);

                if ((allowedStages & CleanupStage.CallbackFence) != 0)
                    ExecuteStage(CleanupStage.CallbackFence, InvokeCallbackFence);

                if ((allowedStages & CleanupStage.Native) != 0 &&
                    IsStageComplete(CleanupStage.CallbackFence))
                {
                    ExecuteNativeStage();
                }

                if ((allowedStages & CleanupStage.OwnerUnpublish) != 0 &&
                    IsStageComplete(CleanupStage.Native))
                {
                    ExecuteStage(CleanupStage.OwnerUnpublish, InvokeOwnerUnpublish);
                }
            }
            catch (Exception exception)
            {
                RecordUnexpectedAttemptFailure(exception);
            }
            finally
            {
                CompleteAttempt(attempt);
            }
        }

        private Exception ExecuteFinalizerAttempt()
        {
            DisposeAttempt attempt;

            lock (_lifecycleLock)
            {
                if (_lifecycleState == NativeLifecycleState.Disposed ||
                    _lifecycleState == NativeLifecycleState.Transferred ||
                    _lifecycleState == NativeLifecycleState.Disposing)
                {
                    return null;
                }

                if (_stableFailure != null)
                    return _stableFailure;

                attempt = StartAttemptLocked();
            }

            // An unreachable lease can still leave a token in the owner. Finalizers must
            // never wait for operation-drain cooperation from unreachable managed state.
            ExecuteAttempt(attempt, FinalizerSafeStages, false);
            return attempt.Failure;
        }

        private void RecordUnexpectedAttemptFailure(Exception exception)
        {
            lock (_lifecycleLock)
            {
                foreach (CleanupStage stage in OrderedStages)
                {
                    if ((_completedStages & stage) != 0)
                        continue;

                    _stageFailures[stage] = CleanupStageResult.NonRetryableFailure(exception);
                    return;
                }
            }
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
            CleanupStageResult baseResourcesResult = CleanupStageResult.Succeeded();

            if (Ownership == NativeOwnershipKind.Borrowed)
            {
                lock (_lifecycleLock)
                {
                    _nativeLiveness = NativeResourceLiveness.KnownLive;
                }

                baseResourcesResult = ReleaseBaseNativeResources();
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

                // Supporting pins and unmanaged buffers can remain reachable from a live
                // native resource. Preserve them unless destruction is explicitly proven.
                if (nativeResult.IsSuccess)
                    baseResourcesResult = ReleaseBaseNativeResources();
            }

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
                {
                    GCHandle handle = DataHandle;
                    handle.Free();
                    DataHandle = default(GCHandle);
                }
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

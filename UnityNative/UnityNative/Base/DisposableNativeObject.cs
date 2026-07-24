using System;
using System.Collections.Generic;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Staged cleanup base for a wrapper around a private native pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Construction either publishes one pointer immediately or leaves it unpublished for a single later
    /// <see cref="PublishNativePointer"/> call. The pointer is never exposed as a reusable property. Each
    /// native operation must call <see cref="EnterOperation"/> and hold the returned lease across the complete
    /// P/Invoke interval. A native-to-managed adapter must enter <see cref="EnterNativeCallbackExecution"/>
    /// before invoking callback consumers, including callbacks on threads without managed execution-context
    /// flow. Disposal from the same logical execution context as an active lease or callback scope is rejected
    /// before lifecycle arbitration; external disposal closes admission, waits for every lease token, executes
    /// staged cleanup, and unpublishes the pointer only after the native stage succeeds.
    /// </para>
    /// <para>
    /// Lease identity combines a process-wide monotonic owner ID, lifecycle epoch, and per-owner monotonic
    /// token. These values are never reused, so an old release cannot affect a newer operation and pointer
    /// address reuse cannot create an ABA identity match. Creating-thread confinement adds a thread invariant
    /// without bypassing lease admission.
    /// </para>
    /// <para>
    /// Transfer is disabled unless a derived wrapper explicitly opts in and declares no cleanup stages that
    /// must remain with the wrapper. It must also prove that rollback is safe on the finalizer thread so an
    /// abandoned ticket cannot lose ownership. Transfer closes admission, requires zero active leases, runs
    /// preparation, allocates an unarmed ticket, and only then atomically moves the pointer and makes the wrapper
    /// terminal. Commit, rollback, completion, and abandoned-ticket recovery are arbitrated by that ticket.
    /// </para>
    /// </remarks>
    public abstract class DisposableNativeObject : DisposableObject, INativeOperationSource
    {
        private enum PointerPublicationState
        {
            Unpublished = 0,
            Published = 1,
        }

        private static long _lastOwnerId;

        private readonly object _operationLock = new object();
        private readonly Dictionary<long, long> _activeLeaseEpochs = new Dictionary<long, long>();
        private readonly int _creatingThreadId;
        private readonly NativeAccessPolicy _accessPolicy;
        private readonly long _operationOwnerId;

        private IntPtr _nativePointer;
        private bool _pointerPublished;
        private bool _operationAdmissionClosed;
        private bool _transferInProgress;
        private long _lastLeaseToken;

        /// <summary>Initializes an owned wrapper whose pointer will be published once after construction starts.</summary>
        protected DisposableNativeObject()
            : this(
                IntPtr.Zero,
                PointerPublicationState.Unpublished,
                NativeOwnershipKind.Owned,
                NativeAccessPolicy.Concurrent)
        {
        }

        /// <summary>Initializes an owned wrapper with an immediately published pointer.</summary>
        /// <param name="nativePointer">The pointer protected by operation leases.</param>
        protected DisposableNativeObject(IntPtr nativePointer)
            : this(
                nativePointer,
                PointerPublicationState.Published,
                NativeOwnershipKind.Owned,
                NativeAccessPolicy.Concurrent)
        {
        }

        /// <summary>Initializes a wrapper with immutable ownership and deferred one-time pointer publication.</summary>
        /// <param name="ownership">Whether cleanup destroys or only invalidates the native resource.</param>
        protected DisposableNativeObject(NativeOwnershipKind ownership)
            : this(
                IntPtr.Zero,
                PointerPublicationState.Unpublished,
                ownership,
                NativeAccessPolicy.Concurrent)
        {
        }

        /// <summary>Initializes a wrapper with immutable ownership and an immediately published pointer.</summary>
        /// <param name="nativePointer">The pointer protected by operation leases.</param>
        /// <param name="ownership">Whether cleanup destroys or only invalidates the native resource.</param>
        protected DisposableNativeObject(IntPtr nativePointer, NativeOwnershipKind ownership)
            : this(
                nativePointer,
                PointerPublicationState.Published,
                ownership,
                NativeAccessPolicy.Concurrent)
        {
        }

        /// <summary>Initializes a wrapper with an immediately published pointer, ownership, and access policy.</summary>
        /// <param name="nativePointer">The pointer protected by operation leases.</param>
        /// <param name="ownership">Whether cleanup destroys or only invalidates the native resource.</param>
        /// <param name="accessPolicy">The managed-thread policy for operations and explicit disposal.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="accessPolicy"/> is not defined.</exception>
        protected DisposableNativeObject(
            IntPtr nativePointer,
            NativeOwnershipKind ownership,
            NativeAccessPolicy accessPolicy)
            : this(nativePointer, PointerPublicationState.Published, ownership, accessPolicy)
        {
        }

        private DisposableNativeObject(
            IntPtr nativePointer,
            PointerPublicationState pointerPublicationState,
            NativeOwnershipKind ownership,
            NativeAccessPolicy accessPolicy)
            : base(ownership)
        {
            if (accessPolicy != NativeAccessPolicy.Concurrent &&
                accessPolicy != NativeAccessPolicy.CreatingThreadConfined)
            {
                throw new ArgumentOutOfRangeException(nameof(accessPolicy));
            }

            _operationOwnerId = NextMonotonicId(ref _lastOwnerId);
            _creatingThreadId = Thread.CurrentThread.ManagedThreadId;
            _accessPolicy = accessPolicy;
            _nativePointer = nativePointer;
            _pointerPublished = pointerPublicationState == PointerPublicationState.Published;
        }

        /// <inheritdoc/>
        public long NativeOperationOwnerId => _operationOwnerId;

        /// <summary>Gets the immutable managed-thread access policy selected during construction.</summary>
        public NativeAccessPolicy AccessPolicy => _accessPolicy;

        /// <summary>Gets whether this wrapper explicitly supports exclusive native ownership transfer.</summary>
        protected virtual bool SupportsNativeTransfer => false;

        /// <summary>
        /// Gets whether transferred native rollback is explicitly safe to invoke from the finalizer thread.
        /// Transfer is rejected unless a derived wrapper opts in.
        /// </summary>
        protected virtual bool IsTransferRollbackFinalizerSafe => false;

        /// <summary>
        /// Gets cleanup stages that cannot move to a transfer ticket. Transfer requires
        /// <see cref="CleanupStage.None"/>.
        /// </summary>
        protected virtual CleanupStage RequiredCleanupBeforeTransfer => CleanupStage.All;

        /// <inheritdoc/>
        public NativeOperationLease EnterOperation()
        {
            lock (_operationLock)
            {
                ValidateOperationThread();
                if (_operationAdmissionClosed || LifecycleState != NativeLifecycleState.Active)
                    throw CreateUnavailableException();
                if (!_pointerPublished)
                    throw new InvalidOperationException("The native pointer has not been published.");

                long leaseToken = NextMonotonicId(ref _lastLeaseToken);
                long lifecycleEpoch = LifecycleEpoch;
                var marker = new NativeOperationExecutionMarker(
                    _operationOwnerId,
                    lifecycleEpoch,
                    leaseToken);
                var lease = new NativeOperationLease(
                    this,
                    _nativePointer,
                    marker);

                _activeLeaseEpochs.Add(leaseToken, lifecycleEpoch);
                try
                {
                    NativeOperationExecutionContext.Enter(marker);
                    return lease;
                }
                catch
                {
                    marker.Deactivate();
                    _activeLeaseEpochs.Remove(leaseToken);
                    throw;
                }
            }
        }

        /// <summary>
        /// Marks the current native-to-managed callback execution for this wrapper before consumer code runs.
        /// </summary>
        /// <returns>An opaque scope that removes only this callback marker when disposed.</returns>
        /// <remarks>
        /// <para>
        /// Derived wrappers should expose this only to their callback adapters and hold the returned scope
        /// across the complete callback body. Entry is allowed on a native callback thread even when ordinary
        /// operations are creating-thread confined. The scope grants no pointer access and does not replace
        /// callback admission, native deregistration, exception containment, or an in-flight callback fence.
        /// </para>
        /// <para>
        /// The returned scope must be disposed in a <see langword="finally"/> block before the callback returns
        /// to native code.
        /// </para>
        /// </remarks>
        protected NativeCallbackExecutionScope EnterNativeCallbackExecution()
        {
            return new NativeCallbackExecutionScope(_operationOwnerId, LifecycleEpoch);
        }

        /// <summary>Moves the published pointer into a new exclusive transfer ticket.</summary>
        /// <returns>The ticket that becomes the sole native owner.</returns>
        /// <exception cref="NotSupportedException"><see cref="SupportsNativeTransfer"/> is false.</exception>
        /// <exception cref="InvalidOperationException">
        /// Finalizer-safe rollback is not enabled, cleanup requirements remain, transfer preparation fails,
        /// the pointer is unpublished, or operations are active.
        /// </exception>
        /// <exception cref="ObjectDisposedException">Admission is closed or the wrapper is not active.</exception>
        public NativeTransferTicket CreateTransferTicket()
        {
            lock (_operationLock)
            {
                ValidateOperationThread();
                if (!SupportsNativeTransfer)
                    throw new NotSupportedException("This native wrapper does not support ownership transfer.");
                if (!IsTransferRollbackFinalizerSafe)
                {
                    throw new InvalidOperationException(
                        "Native ownership transfer requires finalizer-safe rollback support.");
                }
                if (RequiredCleanupBeforeTransfer != CleanupStage.None)
                {
                    throw new InvalidOperationException(
                        "This native wrapper has cleanup requirements that cannot be deferred to a transfer ticket.");
                }
                if (_operationAdmissionClosed || _transferInProgress ||
                    LifecycleState != NativeLifecycleState.Active)
                {
                    throw CreateUnavailableException();
                }
                if (!_pointerPublished)
                    throw new InvalidOperationException("The native pointer has not been published.");
                if (_activeLeaseEpochs.Count != 0)
                    throw new InvalidOperationException("Native ownership cannot be transferred while operations are active.");

                _transferInProgress = true;
                _operationAdmissionClosed = true;
            }

            CleanupStageResult preparation = InvokeTransferPreparation();
            if (!preparation.IsSuccess)
            {
                lock (_operationLock)
                {
                    _transferInProgress = false;
                    if (LifecycleState == NativeLifecycleState.Active)
                        _operationAdmissionClosed = false;
                    Monitor.PulseAll(_operationLock);
                }

                throw new InvalidOperationException("Native transfer preparation failed.", preparation.Exception);
            }

            lock (_operationLock)
            {
                NativeTransferTicket ticket;
                try
                {
                    ticket = new NativeTransferTicket(_nativePointer, CleanupTransferredNativeResource);
                }
                catch
                {
                    _transferInProgress = false;
                    if (LifecycleState == NativeLifecycleState.Active)
                        _operationAdmissionClosed = false;
                    Monitor.PulseAll(_operationLock);
                    throw;
                }

                _transferInProgress = false;
                if (!TryTransitionToTransferred())
                {
                    Monitor.PulseAll(_operationLock);
                    throw CreateUnavailableException();
                }

                _nativePointer = IntPtr.Zero;
                _pointerPublished = false;
                ticket.PublishOwnership();
                Monitor.PulseAll(_operationLock);
                GC.SuppressFinalize(this);

                return ticket;
            }
        }

        /// <summary>Publishes the native pointer exactly once before any operation enters.</summary>
        /// <param name="nativePointer">The pointer protected by future operation leases.</param>
        /// <exception cref="InvalidOperationException">A pointer is already published or an operation is active.</exception>
        /// <exception cref="ObjectDisposedException">Admission is closed or the wrapper is not active.</exception>
        protected void PublishNativePointer(IntPtr nativePointer)
        {
            lock (_operationLock)
            {
                ValidateOperationThread();
                if (_operationAdmissionClosed || LifecycleState != NativeLifecycleState.Active)
                    throw CreateUnavailableException();
                if (_pointerPublished)
                    throw new InvalidOperationException("The native pointer has already been published.");
                if (_activeLeaseEpochs.Count != 0)
                    throw new InvalidOperationException("A native pointer cannot be published while operations are active.");

                _nativePointer = nativePointer;
                _pointerPublished = true;
            }
        }

        /// <summary>Runs native cleanup against the private published pointer.</summary>
        /// <returns>The derived cleanup oracle result.</returns>
        protected sealed override NativeCleanupResult CleanupNativeResource()
        {
            return CleanupNativeResource(_nativePointer);
        }

        /// <summary>Destroys an owned native resource and returns explicit liveness evidence.</summary>
        /// <param name="nativePointer">The pointer captured after operation admission has drained.</param>
        /// <returns>A result proving freed, known-live, or unknown liveness.</returns>
        protected abstract NativeCleanupResult CleanupNativeResource(IntPtr nativePointer);

        /// <summary>Performs the final wrapper-specific fence before ownership moves to a ticket.</summary>
        /// <returns>A successful result to permit transfer, or a failure that aborts it.</returns>
        protected virtual CleanupStageResult PrepareNativeTransfer()
        {
            return CleanupStageResult.Succeeded();
        }

        /// <inheritdoc/>
        protected override CleanupStageResult UnpublishOwner()
        {
            lock (_operationLock)
            {
                _nativePointer = IntPtr.Zero;
                _pointerPublished = false;
            }

            return CleanupStageResult.Succeeded();
        }

        /// <inheritdoc/>
        protected sealed override void ValidateExplicitDisposeThread()
        {
            if (NativeOperationExecutionContext.ContainsOwner(_operationOwnerId))
            {
                throw new InvalidOperationException(
                    "A native wrapper cannot be disposed from an execution context that holds one of its " +
                    "operation leases or native callback scopes.");
            }

            ValidateOperationThread();
        }

        /// <inheritdoc/>
        protected sealed override void CloseOperationAdmissionAndDrain()
        {
            lock (_operationLock)
            {
                _operationAdmissionClosed = true;
                while (_activeLeaseEpochs.Count != 0 || _transferInProgress)
                    Monitor.Wait(_operationLock);
            }
        }

        internal bool IsLeaseActive(long ownerId, long lifecycleEpoch, long leaseToken)
        {
            lock (_operationLock)
            {
                long admittedEpoch;
                return ownerId == _operationOwnerId &&
                       _activeLeaseEpochs.TryGetValue(leaseToken, out admittedEpoch) &&
                       admittedEpoch == lifecycleEpoch;
            }
        }

        internal void ValidateLeaseAccess(long ownerId, long lifecycleEpoch, long leaseToken)
        {
            ValidateOperationThread();
            if (!IsLeaseActive(ownerId, lifecycleEpoch, leaseToken))
                throw new ObjectDisposedException(typeof(NativeOperationLease).FullName);
        }

        internal void ReleaseOperation(long ownerId, long lifecycleEpoch, long leaseToken)
        {
            lock (_operationLock)
            {
                if (ownerId != _operationOwnerId)
                    return;

                long admittedEpoch;
                if (!_activeLeaseEpochs.TryGetValue(leaseToken, out admittedEpoch) ||
                    admittedEpoch != lifecycleEpoch)
                {
                    return;
                }

                _activeLeaseEpochs.Remove(leaseToken);

                if (_activeLeaseEpochs.Count == 0)
                    Monitor.PulseAll(_operationLock);
            }
        }

        private void ValidateOperationThread()
        {
            if (_accessPolicy == NativeAccessPolicy.CreatingThreadConfined &&
                Thread.CurrentThread.ManagedThreadId != _creatingThreadId)
            {
                throw new InvalidOperationException(
                    "This native wrapper is confined to its creating managed thread.");
            }
        }

        private CleanupStageResult InvokeTransferPreparation()
        {
            try
            {
                CleanupStageResult result = PrepareNativeTransfer();
                return result ?? CleanupStageResult.NonRetryableFailure(
                    new InvalidOperationException("Native transfer preparation returned null."));
            }
            catch (Exception exception)
            {
                return CleanupStageResult.NonRetryableFailure(exception);
            }
        }

        private NativeCleanupResult CleanupTransferredNativeResource(IntPtr nativePointer)
        {
            try
            {
                NativeCleanupResult result = CleanupNativeResource(nativePointer);
                return result ?? NativeCleanupResult.LivenessUnknownFailure(
                    new InvalidOperationException("Transferred native cleanup returned null."));
            }
            catch (Exception exception)
            {
                return NativeCleanupResult.LivenessUnknownFailure(exception);
            }
        }

        private ObjectDisposedException CreateUnavailableException()
        {
            return new ObjectDisposedException(
                GetType().FullName,
                "Native operation admission is closed. LifecycleState=" + LifecycleState + ".");
        }

        private static long NextMonotonicId(ref long counter)
        {
            long value = Interlocked.Increment(ref counter);
            if (value <= 0)
                throw new InvalidOperationException("The monotonic native-operation identity space was exhausted.");
            return value;
        }
    }
}

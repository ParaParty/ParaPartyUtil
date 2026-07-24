using System;
using System.Collections.Generic;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Staged cleanup base for a wrapper around a private native pointer.
    /// </summary>
    public abstract class DisposableNativeObject : DisposableObject, INativeOperationSource
    {
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

        protected DisposableNativeObject()
            : this(IntPtr.Zero, false, NativeOwnershipKind.Owned, NativeAccessPolicy.Concurrent)
        {
        }

        protected DisposableNativeObject(IntPtr nativePointer)
            : this(nativePointer, true, NativeOwnershipKind.Owned, NativeAccessPolicy.Concurrent)
        {
        }

        protected DisposableNativeObject(NativeOwnershipKind ownership)
            : this(IntPtr.Zero, false, ownership, NativeAccessPolicy.Concurrent)
        {
        }

        protected DisposableNativeObject(IntPtr nativePointer, NativeOwnershipKind ownership)
            : this(nativePointer, true, ownership, NativeAccessPolicy.Concurrent)
        {
        }

        protected DisposableNativeObject(
            IntPtr nativePointer,
            NativeOwnershipKind ownership,
            NativeAccessPolicy accessPolicy)
            : this(nativePointer, true, ownership, accessPolicy)
        {
        }

        private DisposableNativeObject(
            IntPtr nativePointer,
            bool pointerPublished,
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
            _pointerPublished = pointerPublished;
        }

        public long NativeOperationOwnerId => _operationOwnerId;

        public NativeAccessPolicy AccessPolicy => _accessPolicy;

        protected virtual bool SupportsNativeTransfer => false;

        protected virtual CleanupStage RequiredCleanupBeforeTransfer => CleanupStage.All;

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
                _activeLeaseEpochs.Add(leaseToken, lifecycleEpoch);
                return new NativeOperationLease(
                    this,
                    _nativePointer,
                    _operationOwnerId,
                    lifecycleEpoch,
                    leaseToken);
            }
        }

        public NativeTransferTicket CreateTransferTicket()
        {
            lock (_operationLock)
            {
                ValidateOperationThread();
                if (!SupportsNativeTransfer)
                    throw new NotSupportedException("This native wrapper does not support ownership transfer.");
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
                _transferInProgress = false;
                if (!TryTransitionToTransferred())
                {
                    Monitor.PulseAll(_operationLock);
                    throw CreateUnavailableException();
                }

                IntPtr transferredPointer = _nativePointer;
                _nativePointer = IntPtr.Zero;
                _pointerPublished = false;
                Monitor.PulseAll(_operationLock);
                GC.SuppressFinalize(this);

                return new NativeTransferTicket(transferredPointer, CleanupTransferredNativeResource);
            }
        }

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

        protected sealed override NativeCleanupResult CleanupNativeResource()
        {
            return CleanupNativeResource(_nativePointer);
        }

        protected abstract NativeCleanupResult CleanupNativeResource(IntPtr nativePointer);

        protected virtual CleanupStageResult PrepareNativeTransfer()
        {
            return CleanupStageResult.Succeeded();
        }

        protected override CleanupStageResult UnpublishOwner()
        {
            lock (_operationLock)
            {
                _nativePointer = IntPtr.Zero;
                _pointerPublished = false;
            }

            return CleanupStageResult.Succeeded();
        }

        protected override void ValidateExplicitDisposeThread()
        {
            ValidateOperationThread();
        }

        protected override void CloseOperationAdmissionAndDrain()
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

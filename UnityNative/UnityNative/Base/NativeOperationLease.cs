using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Holds operation admission and a stable pointer across one complete native call interval.
    /// </summary>
    public sealed class NativeOperationLease : IDisposable
    {
        private DisposableNativeObject _owner;
        private readonly IntPtr _pointer;

        internal NativeOperationLease(
            DisposableNativeObject owner,
            IntPtr pointer,
            long ownerId,
            long lifecycleEpoch,
            long leaseToken)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _pointer = pointer;
            OwnerId = ownerId;
            LifecycleEpoch = lifecycleEpoch;
            LeaseToken = leaseToken;
        }

        public long OwnerId { get; }

        public long LifecycleEpoch { get; }

        public long LeaseToken { get; }

        public IntPtr Pointer
        {
            get
            {
                DisposableNativeObject owner = Volatile.Read(ref _owner);
                if (owner == null)
                    throw new ObjectDisposedException(GetType().FullName);

                owner.ValidateLeaseAccess(OwnerId, LifecycleEpoch, LeaseToken);
                return _pointer;
            }
        }

        public void Dispose()
        {
            DisposableNativeObject owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null)
                return;

            owner.ReleaseOperation(OwnerId, LifecycleEpoch, LeaseToken);
            GC.SuppressFinalize(this);
        }

        ~NativeOperationLease()
        {
            try
            {
                Dispose();
            }
            catch
            {
                // Releasing an abandoned lease must not throw from the finalizer thread.
            }
        }
    }
}

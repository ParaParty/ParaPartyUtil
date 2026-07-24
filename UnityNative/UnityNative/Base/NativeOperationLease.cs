using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Holds operation admission and a stable pointer across one complete native call interval.
    /// </summary>
    /// <remarks>
    /// The owner cannot begin native cleanup until every admitted lease is released. The owner ID,
    /// lifecycle epoch, and monotonic lease token form the lease identity; a stale or repeated release
    /// cannot decrement a newer operation. Dispose the lease in a <see langword="using"/> scope that
    /// encloses the entire P/Invoke call, not only the pointer read.
    /// </remarks>
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

        /// <summary>Gets the stable identity of the wrapper that admitted this operation.</summary>
        public long OwnerId { get; }

        /// <summary>Gets the owner's lifecycle epoch at admission time.</summary>
        public long LifecycleEpoch { get; }

        /// <summary>Gets the monotonic token that uniquely identifies this lease within its owner.</summary>
        public long LeaseToken { get; }

        /// <summary>
        /// Gets the pointer captured at admission while the lease remains active and satisfies the
        /// owner's thread-access policy.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The lease has been released or is stale.</exception>
        /// <exception cref="InvalidOperationException">The current thread violates owner confinement.</exception>
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

        /// <summary>Releases operation admission exactly once and unblocks disposal when this is the last lease.</summary>
        public void Dispose()
        {
            DisposableNativeObject owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null)
                return;

            owner.ReleaseOperation(OwnerId, LifecycleEpoch, LeaseToken);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases an abandoned lease without allowing an exception to escape finalization.</summary>
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

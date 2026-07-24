using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    public enum NativeAccessPolicy
    {
        Concurrent = 0,
        CreatingThreadConfined = 1,
    }

    public interface INativeOperationSource
    {
        long NativeOperationOwnerId { get; }

        NativeOperationLease EnterOperation();
    }

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

    public sealed class NativeOperationGroup : IDisposable
    {
        private IReadOnlyList<NativeOperationLease> _leases;

        private NativeOperationGroup(IReadOnlyList<NativeOperationLease> leases)
        {
            _leases = leases;
        }

        public IReadOnlyList<NativeOperationLease> Leases
        {
            get
            {
                IReadOnlyList<NativeOperationLease> leases = Volatile.Read(ref _leases);
                if (leases == null)
                    throw new ObjectDisposedException(GetType().FullName);
                return leases;
            }
        }

        public static NativeOperationGroup Acquire(params INativeOperationSource[] sources)
        {
            if (sources == null)
                throw new ArgumentNullException(nameof(sources));

            var sourcesByOwner = new SortedDictionary<long, INativeOperationSource>();
            foreach (INativeOperationSource source in sources)
            {
                if (source == null)
                    throw new ArgumentException("Operation sources cannot contain null.", nameof(sources));
                if (source.NativeOperationOwnerId <= 0)
                    throw new ArgumentException("Operation source owner IDs must be positive.", nameof(sources));

                INativeOperationSource existing;
                if (sourcesByOwner.TryGetValue(source.NativeOperationOwnerId, out existing))
                {
                    if (!ReferenceEquals(existing, source))
                    {
                        throw new ArgumentException(
                            "Different operation sources published the same owner ID.",
                            nameof(sources));
                    }

                    continue;
                }

                sourcesByOwner.Add(source.NativeOperationOwnerId, source);
            }

            var acquired = new List<NativeOperationLease>(sourcesByOwner.Count);
            try
            {
                foreach (INativeOperationSource source in sourcesByOwner.Values)
                    acquired.Add(source.EnterOperation());

                return new NativeOperationGroup(
                    new ReadOnlyCollection<NativeOperationLease>(acquired));
            }
            catch
            {
                ReleaseReverse(acquired);
                throw;
            }
        }

        public void Dispose()
        {
            IReadOnlyList<NativeOperationLease> leases = Interlocked.Exchange(ref _leases, null);
            if (leases == null)
                return;

            ReleaseReverse(leases);
        }

        private static void ReleaseReverse(IReadOnlyList<NativeOperationLease> leases)
        {
            for (int index = leases.Count - 1; index >= 0; index--)
                leases[index].Dispose();
        }
    }
}

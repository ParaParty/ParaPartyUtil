using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Acquires several operation leases in deterministic owner order.
    /// </summary>
    /// <remarks>
    /// Sources are deduplicated by owner identity and acquired in ascending identity order to prevent
    /// ABBA lock ordering between multi-owner native calls. If any acquisition fails, leases already
    /// acquired by the group are released in reverse order before the failure is rethrown.
    /// </remarks>
    public sealed class NativeOperationGroup : IDisposable
    {
        private IReadOnlyList<NativeOperationLease> _leases;

        private NativeOperationGroup(IReadOnlyList<NativeOperationLease> leases)
        {
            _leases = leases;
        }

        /// <summary>Gets the acquired leases in deterministic owner order.</summary>
        /// <exception cref="ObjectDisposedException">The group has been disposed.</exception>
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

        /// <summary>Acquires one lease for each distinct source in stable owner order.</summary>
        /// <param name="sources">The operation sources participating in one native operation.</param>
        /// <returns>A group that owns every acquired lease.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sources"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// A source is null, has a non-positive owner ID, or conflicts with another source using the same ID.
        /// </exception>
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

        /// <summary>Releases every acquired lease in reverse owner order. Repeated calls are no-ops.</summary>
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

using System;
using System.Collections.Generic;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class LifecycleResourceSlot
    {
        private readonly List<long> _detached = new List<long>();
        private readonly HashSet<long> _released = new HashSet<long>();
        private long _nextGeneration;
        private long _liveGeneration;
        private long _reservedGeneration;
        private KernelReceipt _reservation;

        internal KernelResourcePhase Phase
        {
            get
            {
                var active = (_liveGeneration == 0 ? 0 : 1) +
                             (_reservedGeneration == 0 ? 0 : 1) +
                             (_detached.Count == 0 ? 0 : 1);
                if (active > 1) return KernelResourcePhase.Mixed;
                if (_reservedGeneration != 0) return KernelResourcePhase.Reserved;
                if (_liveGeneration != 0) return KernelResourcePhase.Live;
                if (_detached.Count != 0) return KernelResourcePhase.Detached;
                return _released.Count == 0 ? KernelResourcePhase.Empty : KernelResourcePhase.Released;
            }
        }

        internal bool EmptyForTransfer =>
            _liveGeneration == 0 && _reservedGeneration == 0 && _detached.Count == 0;

        internal bool HasReservation => _reservedGeneration != 0;
        internal bool HasLive => _liveGeneration != 0;
        internal long LiveGeneration => _liveGeneration;
        internal int DetachedCount => _detached.Count;
        internal int ReleasedCount => _released.Count;

        internal KernelReceipt Reserve(KernelIdentity owner, KernelIdentity lifecycle, KernelIdentity attempt)
        {
            if (_reservedGeneration != 0)
                return null;
            _reservedGeneration = ++_nextGeneration;
            _reservation = new KernelReceipt(
                "resource-reservation", owner, lifecycle, attempt, new KernelIdentity(_reservedGeneration));
            return _reservation;
        }

        internal bool AllocationFailed(KernelReceipt receipt, KernelIdentity owner, KernelIdentity lifecycle)
        {
            if (!MatchesReservation(receipt, owner, lifecycle))
                return false;
            _reservedGeneration = 0;
            _reservation = null;
            return true;
        }

        internal bool CommitAllocation(
            KernelReceipt receipt,
            KernelIdentity owner,
            KernelIdentity lifecycle,
            bool mayPublish)
        {
            if (!MatchesReservation(receipt, owner, lifecycle))
                return false;
            var generation = _reservedGeneration;
            _reservedGeneration = 0;
            _reservation = null;
            if (mayPublish)
            {
                if (_liveGeneration != 0)
                    _detached.Add(_liveGeneration);
                _liveGeneration = generation;
            }
            else
            {
                _detached.Add(generation);
            }
            return true;
        }

        internal long DetachLive()
        {
            if (_reservedGeneration != 0 || _liveGeneration == 0)
                return 0;
            var generation = _liveGeneration;
            _liveGeneration = 0;
            _detached.Add(generation);
            return generation;
        }

        internal bool Release(long generation)
        {
            var index = _detached.IndexOf(generation);
            if (index < 0 || _released.Contains(generation))
                return false;
            _detached.RemoveAt(index);
            return _released.Add(generation);
        }

        private bool MatchesReservation(
            KernelReceipt receipt,
            KernelIdentity owner,
            KernelIdentity lifecycle)
        {
            return receipt != null && _reservation != null && _reservedGeneration > 0 && receipt.Matches(
                "resource-reservation",
                _reservation.Owner,
                _reservation.Lifecycle,
                _reservation.Attempt,
                _reservation.Generation) && receipt.Owner.Equals(owner);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class LifecycleOrphanRecord
    {
        internal LifecycleOrphanRecord(KernelIdentity owner)
        {
            Owner = owner;
        }

        internal KernelIdentity Owner { get; }
        internal LifecycleKernel RecoveryOwner { get; private set; }
        internal LifecycleOrphanRecord EmergencyNext { get; set; }
        internal bool Published { get; private set; }
        internal bool Emergency { get; private set; }
        internal bool Terminal { get; set; }

        private WeakReference<LifecycleKernel> ReservedOwner { get; set; }

        internal void Bind(LifecycleKernel owner)
        {
            if (ReservedOwner != null)
                throw new InvalidOperationException("The orphan record already has an owner.");
            ReservedOwner = new WeakReference<LifecycleKernel>(
                owner ?? throw new ArgumentNullException(nameof(owner)));
        }

        internal LifecycleKernel DiscoverOwner()
        {
            if (RecoveryOwner != null)
                return RecoveryOwner;
            LifecycleKernel owner;
            return ReservedOwner != null && ReservedOwner.TryGetTarget(out owner) ? owner : null;
        }

        internal void Publish(LifecycleKernel recoveryOwner, bool emergency)
        {
            RecoveryOwner = recoveryOwner ?? throw new ArgumentNullException(nameof(recoveryOwner));
            Emergency = emergency;
            Published = true;
        }

        internal void Clear()
        {
            RecoveryOwner = null;
            ReservedOwner = null;
            EmergencyNext = null;
            Emergency = false;
            Published = false;
        }
    }

    internal sealed class LifecycleOrphanDrainResult
    {
        internal int Attempted { get; set; }
        internal int Recovered { get; set; }
        internal int RetryableRetained { get; set; }
        internal int StableRetained { get; set; }
        internal int TimedOut { get; set; }
        internal int Remaining { get; set; }
    }

    internal sealed class LifecycleOrphanRegistry
    {
        private static readonly LifecycleOrphanRegistry DomainRegistry =
            new LifecycleOrphanRegistry(int.MaxValue, false);

        private readonly object _sync = new object();
        private readonly List<LifecycleOrphanRecord> _records = new List<LifecycleOrphanRecord>();
        private readonly int _capacity;
        private readonly bool _forcePrimaryPublishFailure;
        private LifecycleOrphanRecord _emergencyHead;
        private bool _shutdown;

        internal LifecycleOrphanRegistry(int capacity)
            : this(capacity, false)
        {
        }

        internal LifecycleOrphanRegistry(int capacity, bool forcePrimaryPublishFailure)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _forcePrimaryPublishFailure = forcePrimaryPublishFailure;
        }

        internal static LifecycleOrphanRegistry Domain => DomainRegistry;

        internal LifecycleOrphanRecord Reserve(KernelIdentity owner)
        {
            lock (_sync)
            {
                if (_shutdown || _records.Count >= _capacity || ContainsOwner(owner))
                    return null;
                var record = new LifecycleOrphanRecord(owner);
                _records.Add(record);
                return record;
            }
        }

        internal bool Bind(LifecycleOrphanRecord record, LifecycleKernel owner)
        {
            if (record == null || owner == null)
                throw new ArgumentNullException(record == null ? nameof(record) : nameof(owner));
            lock (_sync)
            {
                if (!Contains(record) || record.Terminal)
                    throw new InvalidOperationException("The orphan reservation is no longer active.");
                record.Bind(owner);
                return _shutdown;
            }
        }

        internal bool Publish(LifecycleOrphanRecord record, LifecycleKernel recoveryOwner)
        {
            if (record == null || recoveryOwner == null || record.Terminal)
                return false;
            lock (_sync)
            {
                if (!Contains(record))
                    return false;
                if (record.Published)
                    return ReferenceEquals(record.RecoveryOwner, recoveryOwner);
                if (!_forcePrimaryPublishFailure)
                {
                    record.Publish(recoveryOwner, false);
                    return true;
                }

                // The record and link are construction-time allocations; finalizer fallback only assigns fields.
                record.EmergencyNext = _emergencyHead;
                _emergencyHead = record;
                record.Publish(recoveryOwner, true);
                return true;
            }
        }

        internal LifecycleOrphanRecord[] Snapshot()
        {
            lock (_sync)
            {
                var records = new List<LifecycleOrphanRecord>();
                for (var index = 0; index < _records.Count; index++)
                {
                    var record = _records[index];
                    if (record.Published && !record.Terminal && !record.Emergency)
                        records.Add(record);
                }
                var emergency = _emergencyHead;
                while (emergency != null)
                {
                    if (emergency.Published && !emergency.Terminal)
                        records.Add(emergency);
                    emergency = emergency.EmergencyNext;
                }
                return records.ToArray();
            }
        }

        internal Task<LifecycleOrphanDrainResult> DrainAsync(DateTime deadlineUtc)
            => Task.FromResult(DrainSnapshot(Snapshot(), deadlineUtc));

        internal Task<LifecycleOrphanDrainResult> RetryAsync(
            KernelIdentity owner,
            DateTime deadlineUtc)
        {
            var snapshot = Snapshot();
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (snapshot[index].Owner.Equals(owner))
                    return Task.FromResult(DrainSnapshot(new[] { snapshot[index] }, deadlineUtc));
            }
            return Task.FromResult(new LifecycleOrphanDrainResult { Remaining = Count });
        }

        internal Task<LifecycleOrphanDrainResult> ShutdownAsync(DateTime deadlineUtc)
        {
            LifecycleKernel[] owners;
            lock (_sync)
            {
                _shutdown = true;
                owners = DiscoverOwners();
            }
            for (var index = 0; index < owners.Length; index++)
                owners[index].PrepareDomainShutdown();
            return DrainAsync(deadlineUtc);
        }

        internal bool RemoveTerminal(LifecycleOrphanRecord record)
        {
            if (record == null || !record.Terminal)
                return false;
            lock (_sync)
            {
                var index = _records.IndexOf(record);
                if (index < 0)
                    return false;
                RemoveEmergency(record);
                _records.RemoveAt(index);
                record.Clear();
                return true;
            }
        }

        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    var count = 0;
                    for (var index = 0; index < _records.Count; index++)
                        if (_records[index].Published && !_records[index].Terminal &&
                            !_records[index].Emergency)
                            count++;
                    var emergency = _emergencyHead;
                    while (emergency != null)
                    {
                        if (emergency.Published && !emergency.Terminal)
                            count++;
                        emergency = emergency.EmergencyNext;
                    }
                    return count;
                }
            }
        }

        internal bool Shutdown
        {
            get
            {
                lock (_sync)
                {
                    return _shutdown;
                }
            }
        }

        private LifecycleOrphanDrainResult DrainSnapshot(
            LifecycleOrphanRecord[] records,
            DateTime deadlineUtc)
        {
            var result = new LifecycleOrphanDrainResult();
            for (var index = 0; index < records.Length; index++)
            {
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    result.TimedOut += records.Length - index;
                    break;
                }
                var record = records[index];
                var owner = record.RecoveryOwner;
                if (owner == null || record.Terminal)
                    continue;
                result.Attempted++;
                var outcome = owner.TryRecoverOrphan();
                if (outcome == KernelOrphanRecoveryOutcome.Recovered)
                    result.Recovered++;
                else if (outcome == KernelOrphanRecoveryOutcome.StableRetained)
                    result.StableRetained++;
                else
                    result.RetryableRetained++;
            }
            result.Remaining = Count;
            return result;
        }

        private bool Contains(LifecycleOrphanRecord record) => _records.Contains(record);

        private bool ContainsOwner(KernelIdentity owner)
        {
            for (var index = 0; index < _records.Count; index++)
                if (_records[index].Owner.Equals(owner))
                    return true;
            return false;
        }

        private LifecycleKernel[] DiscoverOwners()
        {
            var owners = new List<LifecycleKernel>();
            for (var index = 0; index < _records.Count; index++)
            {
                var owner = _records[index].DiscoverOwner();
                if (owner != null)
                    owners.Add(owner);
            }
            return owners.ToArray();
        }

        private void RemoveEmergency(LifecycleOrphanRecord record)
        {
            LifecycleOrphanRecord prior = null;
            var current = _emergencyHead;
            while (current != null)
            {
                if (ReferenceEquals(current, record))
                {
                    if (prior == null)
                        _emergencyHead = current.EmergencyNext;
                    else
                        prior.EmergencyNext = current.EmergencyNext;
                    return;
                }
                prior = current;
                current = current.EmergencyNext;
            }
        }
    }
}

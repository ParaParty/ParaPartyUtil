using System;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class LifecycleOrphanRecord
    {
        internal LifecycleOrphanRecord(KernelIdentity owner)
        {
            Owner = owner;
        }

        internal KernelIdentity Owner { get; }
        internal bool Published { get; set; }
        internal bool Terminal { get; set; }
    }

    internal sealed class LifecycleOrphanRegistry
    {
        private readonly object _sync = new object();
        private readonly LifecycleOrphanRecord[] _records;

        internal LifecycleOrphanRegistry(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _records = new LifecycleOrphanRecord[capacity];
        }

        internal LifecycleOrphanRecord Reserve(KernelIdentity owner)
        {
            lock (_sync)
            {
                for (var index = 0; index < _records.Length; index++)
                {
                    if (_records[index] != null) continue;
                    var record = new LifecycleOrphanRecord(owner);
                    _records[index] = record;
                    return record;
                }
                return null;
            }
        }

        internal bool Publish(LifecycleOrphanRecord record)
        {
            if (record == null || record.Terminal)
                return false;
            lock (_sync)
            {
                if (!Contains(record) || record.Published)
                    return false;
                record.Published = true;
                return true;
            }
        }

        internal bool RemoveTerminal(LifecycleOrphanRecord record)
        {
            if (record == null || !record.Terminal)
                return false;
            lock (_sync)
            {
                for (var index = 0; index < _records.Length; index++)
                {
                    if (!ReferenceEquals(_records[index], record)) continue;
                    _records[index] = null;
                    record.Published = false;
                    return true;
                }
                return false;
            }
        }

        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    var count = 0;
                    for (var index = 0; index < _records.Length; index++)
                        if (_records[index] != null && _records[index].Published)
                            count++;
                    return count;
                }
            }
        }

        private bool Contains(LifecycleOrphanRecord record)
        {
            for (var index = 0; index < _records.Length; index++)
                if (ReferenceEquals(_records[index], record))
                    return true;
            return false;
        }
    }
}

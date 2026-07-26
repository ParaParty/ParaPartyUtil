using System;
using System.Collections.Generic;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class KernelAttemptRecord
    {
        private readonly HashSet<string> _waiters = new HashSet<string>(StringComparer.Ordinal);

        internal KernelAttemptRecord(KernelIdentity id, KernelCausalToken causalToken)
        {
            Id = id;
            CausalToken = causalToken;
            Outcome = KernelAttemptOutcome.Running;
        }

        internal KernelIdentity Id { get; }
        internal KernelCausalToken CausalToken { get; }
        internal KernelAttemptOutcome Outcome { get; private set; }
        internal string Result { get; private set; }
        internal bool IsComplete => Outcome != KernelAttemptOutcome.Running;

        internal bool RegisterWaiter(string waiter)
        {
            return !string.IsNullOrWhiteSpace(waiter) && _waiters.Add(waiter);
        }

        internal string Observe(string waiter)
        {
            return IsComplete && _waiters.Contains(waiter) ? Result : null;
        }

        internal void Complete(KernelAttemptOutcome outcome, string result)
        {
            if (IsComplete)
                throw new InvalidOperationException("An immutable attempt record cannot be overwritten.");
            if (outcome == KernelAttemptOutcome.Running || string.IsNullOrEmpty(result))
                throw new ArgumentException("Completion requires an exact terminal result.");
            Outcome = outcome;
            Result = result;
        }
    }

    internal sealed class LifecycleAttemptLedger
    {
        private readonly List<KernelAttemptRecord> _records = new List<KernelAttemptRecord>();

        internal KernelAttemptRecord Current => _records.Count == 0 ? null : _records[_records.Count - 1];
        internal int Count => _records.Count;

        internal KernelAttemptRecord Append(KernelIdentity attempt, KernelCausalToken causalToken)
        {
            if (Current != null && !Current.IsComplete)
                throw new InvalidOperationException("The current attempt is still running.");
            if (Current != null && attempt.Value <= Current.Id.Value)
                throw new InvalidOperationException("Attempt identities must be monotonic.");
            var record = new KernelAttemptRecord(attempt, causalToken);
            _records.Add(record);
            return record;
        }

        internal KernelAttemptRecord Find(KernelIdentity attempt)
        {
            for (var index = 0; index < _records.Count; index++)
                if (_records[index].Id.Equals(attempt))
                    return _records[index];
            return null;
        }
    }
}

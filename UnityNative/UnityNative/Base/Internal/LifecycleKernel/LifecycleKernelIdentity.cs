using System;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal struct KernelIdentity
    {
        internal KernelIdentity(long value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        internal long Value { get; }

        internal bool Equals(KernelIdentity other) => Value == other.Value;
    }

    internal struct KernelCausalToken
    {
        internal KernelCausalToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A causal token must be nonempty.", nameof(value));
            Value = value;
        }

        internal string Value { get; }

        internal bool Equals(KernelCausalToken other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);
    }
}

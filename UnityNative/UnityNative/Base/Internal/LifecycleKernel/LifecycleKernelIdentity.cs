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

    internal sealed class KernelAttemptCapability
    {
        internal KernelAttemptCapability(
            KernelIdentity owner,
            KernelIdentity attempt,
            KernelAttemptKind kind,
            KernelCausalToken token,
            object issuerSeal)
        {
            Owner = owner;
            Attempt = attempt;
            Kind = kind;
            Token = token;
            IssuerSeal = issuerSeal ?? throw new ArgumentNullException(nameof(issuerSeal));
        }

        internal KernelIdentity Owner { get; }
        internal KernelIdentity Attempt { get; }
        internal KernelAttemptKind Kind { get; }
        internal KernelCausalToken Token { get; }
        internal object IssuerSeal { get; }

        internal bool Matches(
            KernelIdentity owner,
            KernelIdentity attempt,
            KernelAttemptKind kind,
            object issuerSeal)
        {
            return Owner.Equals(owner) && Attempt.Equals(attempt) && Kind == kind &&
                   ReferenceEquals(IssuerSeal, issuerSeal);
        }
    }

    internal sealed class KernelCalloutCapability
    {
        internal KernelCalloutCapability(
            KernelAttemptCapability attempt,
            KernelCalloutKind kind,
            object issuerSeal)
        {
            Attempt = attempt ?? throw new ArgumentNullException(nameof(attempt));
            Kind = kind;
            IssuerSeal = issuerSeal ?? throw new ArgumentNullException(nameof(issuerSeal));
            if (!ReferenceEquals(attempt.IssuerSeal, issuerSeal))
                throw new ArgumentException("The callout issuer must match its attempt capability.", nameof(issuerSeal));
        }

        internal KernelAttemptCapability Attempt { get; }
        internal KernelCalloutKind Kind { get; }
        internal object IssuerSeal { get; }
        internal bool IsWellFormed => ReferenceEquals(Attempt.IssuerSeal, IssuerSeal);
    }

    internal sealed class KernelAttemptReservation
    {
        internal KernelAttemptReservation(KernelReceipt receipt, KernelAttemptCapability capability)
        {
            Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
            Capability = capability ?? throw new ArgumentNullException(nameof(capability));
        }

        internal KernelReceipt Receipt { get; }
        internal KernelAttemptCapability Capability { get; }
    }

    internal sealed class KernelJoinReservation
    {
        internal KernelJoinReservation(
            KernelAttemptKind kind,
            KernelIdentity attempt,
            string waiterId)
        {
            if (string.IsNullOrWhiteSpace(waiterId))
                throw new ArgumentException("A join reservation requires a waiter identity.", nameof(waiterId));
            Kind = kind;
            Attempt = attempt;
            WaiterId = waiterId;
        }

        internal KernelAttemptKind Kind { get; }
        internal KernelIdentity Attempt { get; }
        internal string WaiterId { get; }
    }
}

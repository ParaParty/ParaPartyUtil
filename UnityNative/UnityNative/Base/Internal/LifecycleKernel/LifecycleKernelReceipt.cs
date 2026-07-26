namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class KernelReceipt
    {
        internal KernelReceipt(
            string purpose,
            KernelIdentity owner,
            KernelIdentity lifecycle,
            KernelIdentity attempt,
            KernelIdentity generation)
        {
            Purpose = purpose;
            Owner = owner;
            Lifecycle = lifecycle;
            Attempt = attempt;
            Generation = generation;
        }

        internal KernelReceipt(
            string purpose,
            KernelIdentity owner,
            KernelIdentity lifecycle,
            KernelIdentity attempt,
            KernelIdentity generation,
            KernelStage stage)
            : this(purpose, owner, lifecycle, attempt, generation)
        {
            Stage = stage;
        }

        internal string Purpose { get; }
        internal KernelIdentity Owner { get; }
        internal KernelIdentity Lifecycle { get; }
        internal KernelIdentity Attempt { get; }
        internal KernelIdentity Generation { get; }
        internal KernelStage? Stage { get; }

        internal bool Matches(
            string purpose,
            KernelIdentity owner,
            KernelIdentity lifecycle,
            KernelIdentity attempt,
            KernelIdentity generation)
        {
            return Purpose == purpose && Owner.Equals(owner) && Lifecycle.Equals(lifecycle) &&
                   Attempt.Equals(attempt) && Generation.Equals(generation);
        }

        internal bool MatchesStage(
            KernelIdentity owner,
            KernelIdentity lifecycle,
            KernelIdentity attempt,
            KernelIdentity generation,
            KernelStage stage)
        {
            return Matches("stage", owner, lifecycle, attempt, generation) && Stage == stage;
        }
    }

    internal sealed class KernelToken
    {
        internal KernelToken(string purpose, KernelIdentity owner, KernelIdentity generation)
        {
            Purpose = purpose;
            Owner = owner;
            Generation = generation;
        }

        internal string Purpose { get; }
        internal KernelIdentity Owner { get; }
        internal KernelIdentity Generation { get; }
    }
}

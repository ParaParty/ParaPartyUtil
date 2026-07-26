using System;
using System.Collections.Generic;
using System.Threading;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class KernelCausalSnapshot
    {
        internal KernelCausalSnapshot(
            KernelAttemptCapability attempt,
            KernelCalloutCapability[] callouts)
        {
            Attempt = attempt;
            Callouts = callouts;
        }

        internal KernelAttemptCapability Attempt { get; }
        internal KernelCalloutCapability[] Callouts { get; }
        internal KernelCalloutCapability CurrentCallout =>
            Callouts.Length == 0 ? null : Callouts[Callouts.Length - 1];
    }

    internal static class LifecycleCausalContext
    {
        private sealed class ContextState
        {
            internal KernelAttemptCapability Attempt;
            internal List<KernelCalloutCapability> Callouts = new List<KernelCalloutCapability>();

            internal ContextState Copy()
            {
                return new ContextState
                {
                    Attempt = Attempt,
                    Callouts = new List<KernelCalloutCapability>(Callouts)
                };
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly ContextState _prior;
            private bool _disposed;

            internal Scope(ContextState prior) => _prior = prior;

            void IDisposable.Dispose()
            {
                if (_disposed) return;
                Current.Value = _prior;
                _disposed = true;
            }
        }

        private static readonly AsyncLocal<ContextState> Current = new AsyncLocal<ContextState>();

        internal static KernelCausalSnapshot Capture()
        {
            var state = Current.Value;
            return state == null
                ? new KernelCausalSnapshot(null, new KernelCalloutCapability[0])
                : new KernelCausalSnapshot(state.Attempt, state.Callouts.ToArray());
        }

        internal static IDisposable EnterAttempt(KernelAttemptCapability attempt)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            var prior = Current.Value;
            var next = prior == null ? new ContextState() : prior.Copy();
            next.Attempt = attempt;
            Current.Value = next;
            return new Scope(prior);
        }

        internal static IDisposable EnterCallout(KernelCalloutCapability callout)
        {
            if (callout == null) throw new ArgumentNullException(nameof(callout));
            var prior = Current.Value;
            var next = prior == null ? new ContextState() : prior.Copy();
            next.Callouts.Add(callout);
            Current.Value = next;
            return new Scope(prior);
        }
    }
}

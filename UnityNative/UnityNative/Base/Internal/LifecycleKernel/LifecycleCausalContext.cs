using System;
using System.Collections.Generic;
using System.Threading;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class KernelCalloutFrame
    {
        internal KernelCalloutFrame(KernelIdentity owner, KernelIdentity attempt, KernelCalloutKind kind)
        {
            Owner = owner;
            Attempt = attempt;
            Kind = kind;
        }

        internal KernelIdentity Owner { get; }
        internal KernelIdentity Attempt { get; }
        internal KernelCalloutKind Kind { get; }
    }

    internal sealed class KernelCausalSnapshot
    {
        internal KernelCausalSnapshot(KernelCausalToken? token, KernelCalloutFrame[] frames)
        {
            Token = token;
            Frames = frames;
        }

        internal KernelCausalToken? Token { get; }
        internal KernelCalloutFrame[] Frames { get; }
        internal bool IsInsideCallout => Frames.Length != 0;
    }

    internal static class LifecycleCausalContext
    {
        private sealed class ContextState
        {
            internal KernelCausalToken? Token;
            internal List<KernelCalloutFrame> Frames = new List<KernelCalloutFrame>();

            internal ContextState Copy()
            {
                return new ContextState
                {
                    Token = Token,
                    Frames = new List<KernelCalloutFrame>(Frames)
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
                ? new KernelCausalSnapshot(null, new KernelCalloutFrame[0])
                : new KernelCausalSnapshot(state.Token, state.Frames.ToArray());
        }

        internal static IDisposable EnterAttempt(KernelCausalToken token)
        {
            var prior = Current.Value;
            var next = prior == null ? new ContextState() : prior.Copy();
            next.Token = token;
            Current.Value = next;
            return new Scope(prior);
        }

        internal static IDisposable EnterCallout(
            KernelIdentity owner,
            KernelIdentity attempt,
            KernelCalloutKind kind)
        {
            var prior = Current.Value;
            var next = prior == null ? new ContextState() : prior.Copy();
            next.Frames.Add(new KernelCalloutFrame(owner, attempt, kind));
            Current.Value = next;
            return new Scope(prior);
        }
    }
}

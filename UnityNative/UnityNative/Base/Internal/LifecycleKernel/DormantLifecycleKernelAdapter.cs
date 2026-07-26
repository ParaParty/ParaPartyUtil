using System;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class DormantLifecycleKernelAdapter
    {
        private readonly LifecycleKernel _kernel;

        internal DormantLifecycleKernelAdapter(LifecycleKernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        internal LifecycleKernelSnapshot Snapshot() => _kernel.Snapshot();

        internal KernelTransition AdmitCallback() => _kernel.EnterCallback();

        internal KernelTransition ReleaseCallback(KernelToken token)
            => _kernel.ReleaseCallback(token);

        internal KernelTransition RequestDispose(string causalToken)
        {
            var context = LifecycleCausalContext.Capture();
            var running = _kernel.Snapshot();
            if (running.DisposalAttempt == KernelAttemptState.Running)
            {
                return _kernel.JoinDispose(
                    "adapter:" + causalToken,
                    context.Token.HasValue ? context.Token.Value.Value : causalToken,
                    context.IsInsideCallout);
            }
            return _kernel.StartDispose(causalToken);
        }

        internal IDisposable EnterCallout(KernelCalloutKind kind)
        {
            var snapshot = _kernel.Snapshot();
            var attempt = snapshot.DisposalAttemptId > 0
                ? new KernelIdentity(snapshot.DisposalAttemptId)
                : new KernelIdentity(snapshot.LifecycleId);
            return LifecycleCausalContext.EnterCallout(
                new KernelIdentity(snapshot.OwnerId), attempt, kind);
        }
    }
}

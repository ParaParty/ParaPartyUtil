using System;
using System.Threading;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal enum KernelAdapterOperation
    {
        Disposal,
        Transfer,
        Rollback
    }

    internal sealed class KernelAdapterRequest
    {
        private readonly LifecycleKernel _kernel;
        private readonly KernelAdapterOperation _operation;
        private readonly long _attemptId;
        private readonly string _waiterId;
        private readonly KernelAttemptReservation _reservation;

        internal KernelAdapterRequest(
            LifecycleKernel kernel,
            KernelAdapterOperation operation,
            KernelTransition transition,
            long attemptId = 0,
            string waiterId = null)
        {
            _kernel = kernel;
            Transition = transition;
            _reservation = transition?.Value as KernelAttemptReservation;
            var join = transition?.Value as KernelJoinReservation;
            _operation = join == null ? operation : AdapterOperation(join.Kind);
            _attemptId = join == null ? attemptId : join.Attempt.Value;
            _waiterId = join == null ? waiterId : join.WaiterId;
        }

        internal KernelTransition Transition { get; }
        internal KernelReceipt Receipt => _reservation?.Receipt;
        internal KernelAttemptCapability Capability => _reservation?.Capability;
        internal bool IsJoin => !string.IsNullOrEmpty(_waiterId);

        internal IDisposable EnterScope()
        {
            if (Capability == null)
                throw new InvalidOperationException("Only a newly started attempt owns a causal scope.");
            return LifecycleCausalContext.EnterAttempt(Capability);
        }

        internal string Observe()
        {
            if (!IsJoin) return null;
            switch (_operation)
            {
                case KernelAdapterOperation.Disposal:
                    return _kernel.ObserveDisposeResult(_attemptId, _waiterId);
                case KernelAdapterOperation.Transfer:
                    return _kernel.ObserveTransferResult(_attemptId, _waiterId);
                default:
                    return _kernel.ObserveRollbackResult(_attemptId, _waiterId);
            }
        }

        private static KernelAdapterOperation AdapterOperation(KernelAttemptKind kind)
        {
            switch (kind)
            {
                case KernelAttemptKind.Disposal:
                    return KernelAdapterOperation.Disposal;
                case KernelAttemptKind.Transfer:
                    return KernelAdapterOperation.Transfer;
                default:
                    return KernelAdapterOperation.Rollback;
            }
        }
    }

    internal sealed class DormantLifecycleKernelAdapter
    {
        private static long _nextWaiter;
        private readonly LifecycleKernel _kernel;

        internal DormantLifecycleKernelAdapter(LifecycleKernel kernel)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        }

        internal LifecycleKernelSnapshot Snapshot() => _kernel.Snapshot();

        internal KernelTransition AdmitCallback() => _kernel.EnterCallback();

        internal KernelTransition ReleaseCallback(KernelToken token)
            => _kernel.ReleaseCallback(token);

        internal KernelAdapterRequest RequestDispose()
            => RequestDisposeCore(null, false);

        internal KernelAdapterRequest RequestDisposeFromFramework(
            KernelCalloutCapability explicitCallout)
            => RequestDisposeCore(explicitCallout, true);

        private KernelAdapterRequest RequestDisposeCore(
            KernelCalloutCapability explicitCallout,
            bool requireExplicitCallout)
        {
            var context = LifecycleCausalContext.Capture();
            var callout = ResolveCallout(context, explicitCallout, requireExplicitCallout);
            if (callout != null && !callout.IsWellFormed)
                return Rejected(
                    KernelAdapterOperation.Disposal,
                    KernelTransitionCode.InvalidToken,
                    "Disposal callout capability is not kernel-issued.");
            if (requireExplicitCallout && callout == null)
                return Rejected(
                    KernelAdapterOperation.Disposal,
                    KernelTransitionCode.InvalidToken,
                    "Suppressed-flow framework disposal requires an explicit callout capability.");
            var waiter = NextWaiter("dispose");
            return new KernelAdapterRequest(
                _kernel,
                KernelAdapterOperation.Disposal,
                _kernel.RequestDispose(waiter, context.Attempt, callout));
        }

        internal KernelAdapterRequest RequestTransfer()
            => RequestTransferCore(null, false);

        internal KernelAdapterRequest RequestTransferFromFramework(
            KernelCalloutCapability explicitCallout)
            => RequestTransferCore(explicitCallout, true);

        private KernelAdapterRequest RequestTransferCore(
            KernelCalloutCapability explicitCallout,
            bool requireExplicitCallout)
        {
            var context = LifecycleCausalContext.Capture();
            var callout = ResolveCallout(context, explicitCallout, requireExplicitCallout);
            if (callout != null && !callout.IsWellFormed)
                return Rejected(
                    KernelAdapterOperation.Transfer,
                    KernelTransitionCode.InvalidToken,
                    "Transfer callout capability is not kernel-issued.");
            if (requireExplicitCallout && callout == null)
                return Rejected(
                    KernelAdapterOperation.Transfer,
                    KernelTransitionCode.InvalidToken,
                    "Suppressed-flow framework transfer requires an explicit callout capability.");
            var waiter = NextWaiter("transfer");
            return new KernelAdapterRequest(
                _kernel,
                KernelAdapterOperation.Transfer,
                _kernel.RequestTransfer(waiter, context.Attempt, callout));
        }

        internal KernelAdapterRequest RequestRollback(KernelReceipt ticketReceipt)
            => RequestRollbackCore(ticketReceipt, null, false);

        internal KernelAdapterRequest RequestRollbackFromFramework(
            KernelReceipt ticketReceipt,
            KernelCalloutCapability explicitCallout)
            => RequestRollbackCore(ticketReceipt, explicitCallout, true);

        private KernelAdapterRequest RequestRollbackCore(
            KernelReceipt ticketReceipt,
            KernelCalloutCapability explicitCallout,
            bool requireExplicitCallout)
        {
            var context = LifecycleCausalContext.Capture();
            var callout = ResolveCallout(context, explicitCallout, requireExplicitCallout);
            if (callout != null && !callout.IsWellFormed)
                return Rejected(
                    KernelAdapterOperation.Rollback,
                    KernelTransitionCode.InvalidToken,
                    "Rollback callout capability is not kernel-issued.");
            if (requireExplicitCallout && callout == null)
                return Rejected(
                    KernelAdapterOperation.Rollback,
                    KernelTransitionCode.InvalidToken,
                    "Suppressed-flow framework rollback requires an explicit callout capability.");
            var waiter = NextWaiter("rollback");
            return new KernelAdapterRequest(
                _kernel,
                KernelAdapterOperation.Rollback,
                _kernel.RequestRollback(ticketReceipt, waiter, context.Attempt, callout));
        }

        internal KernelCalloutCapability CreateCalloutCapability(KernelCalloutKind kind)
        {
            var attempt = LifecycleCausalContext.Capture().Attempt;
            var transition = _kernel.IssueCalloutCapability(attempt, kind);
            if (!transition.Accepted)
                throw new InvalidOperationException(transition.Detail);
            return (KernelCalloutCapability)transition.Value;
        }

        internal IDisposable EnterCallout(KernelCalloutKind kind)
            => LifecycleCausalContext.EnterCallout(CreateCalloutCapability(kind));

        internal IDisposable EnterCallout(KernelCalloutCapability capability)
            => LifecycleCausalContext.EnterCallout(
                capability ?? throw new ArgumentNullException(nameof(capability)));

        private KernelAdapterRequest Rejected(
            KernelAdapterOperation operation,
            KernelTransitionCode code,
            string detail)
            => new KernelAdapterRequest(_kernel, operation, KernelTransition.Reject(code, detail));

        private static KernelCalloutCapability ResolveCallout(
            KernelCausalSnapshot context,
            KernelCalloutCapability explicitCallout,
            bool requireExplicitCallout)
        {
            if (explicitCallout != null) return explicitCallout;
            return requireExplicitCallout ? null : context.CurrentCallout;
        }

        private static string NextWaiter(string prefix)
            => prefix + "-waiter-" + Interlocked.Increment(ref _nextWaiter);
    }
}

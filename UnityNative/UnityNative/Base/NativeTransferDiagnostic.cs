using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Truthful telemetry for a transfer ticket that could not reach a terminal ownership state.
    /// </summary>
    public sealed class NativeTransferDiagnostic
    {
        internal NativeTransferDiagnostic(
            long ticketId,
            NativeTransferState state,
            NativeResourceLiveness nativeLiveness,
            long rollbackAttemptEpoch,
            bool isFinalizer,
            Exception exception)
        {
            TicketId = ticketId;
            State = state;
            NativeLiveness = nativeLiveness;
            RollbackAttemptEpoch = rollbackAttemptEpoch;
            IsFinalizer = isFinalizer;
            Exception = exception;
        }

        /// <summary>Gets the monotonic identity of the transfer ticket.</summary>
        public long TicketId { get; }

        /// <summary>Gets the transfer state observed when telemetry was emitted.</summary>
        public NativeTransferState State { get; }

        /// <summary>Gets the rollback oracle's last liveness evidence.</summary>
        public NativeResourceLiveness NativeLiveness { get; }

        /// <summary>Gets the number of rollback attempts started by the ticket.</summary>
        public long RollbackAttemptEpoch { get; }

        /// <summary>Gets whether the diagnostic was emitted during finalization.</summary>
        public bool IsFinalizer { get; }

        /// <summary>Gets the rollback or arbitration failure, if one was available.</summary>
        public Exception Exception { get; }
    }
}

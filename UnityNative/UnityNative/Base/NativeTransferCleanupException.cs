using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Describes a retryable or stable transfer rollback failure.
    /// </summary>
    public sealed class NativeTransferCleanupException : Exception
    {
        internal NativeTransferCleanupException(
            long ticketId,
            long rollbackAttemptEpoch,
            NativeResourceLiveness nativeLiveness,
            bool isRetryable,
            Exception innerException)
            : base(
                "Native transfer ticket " + ticketId +
                " rollback attempt " + rollbackAttemptEpoch +
                " failed. NativeLiveness=" + nativeLiveness +
                ", Retryable=" + isRetryable + ".",
                innerException)
        {
            TicketId = ticketId;
            RollbackAttemptEpoch = rollbackAttemptEpoch;
            NativeLiveness = nativeLiveness;
            IsRetryable = isRetryable;
        }

        /// <summary>Gets the monotonic identity of the transfer ticket.</summary>
        public long TicketId { get; }

        /// <summary>Gets the monotonic identity of the failed rollback attempt.</summary>
        public long RollbackAttemptEpoch { get; }

        /// <summary>Gets the cleanup oracle's liveness evidence after the failure.</summary>
        public NativeResourceLiveness NativeLiveness { get; }

        /// <summary>Gets whether another rollback attempt is proven safe.</summary>
        public bool IsRetryable { get; }
    }
}

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

        public long TicketId { get; }

        public long RollbackAttemptEpoch { get; }

        public NativeResourceLiveness NativeLiveness { get; }

        public bool IsRetryable { get; }
    }
}

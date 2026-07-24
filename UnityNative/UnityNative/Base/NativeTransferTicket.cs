using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Exclusively arbitrates commit, rollback, and native completion after pointer transfer.
    /// </summary>
    public sealed class NativeTransferTicket : IDisposable
    {
        private static long _lastTicketId;

        private readonly object _ticketLock = new object();
        private readonly IntPtr _pointer;
        private readonly Func<IntPtr, NativeCleanupResult> _rollbackCleanup;

        private NativeTransferState _state;
        private NativeResourceLiveness _nativeLiveness;
        private NativeTransferCleanupException _stableFailure;
        private long _rollbackAttemptEpoch;

        internal NativeTransferTicket(
            IntPtr pointer,
            Func<IntPtr, NativeCleanupResult> rollbackCleanup)
        {
            _rollbackCleanup = rollbackCleanup ?? throw new ArgumentNullException(nameof(rollbackCleanup));
            _pointer = pointer;
            _state = NativeTransferState.Pending;
            _nativeLiveness = NativeResourceLiveness.KnownLive;
            TicketId = NextTicketId();
        }

        public long TicketId { get; }

        public NativeTransferState State
        {
            get
            {
                lock (_ticketLock)
                {
                    return _state;
                }
            }
        }

        public NativeResourceLiveness NativeLiveness
        {
            get
            {
                lock (_ticketLock)
                {
                    return _nativeLiveness;
                }
            }
        }

        public long RollbackAttemptEpoch
        {
            get
            {
                lock (_ticketLock)
                {
                    return _rollbackAttemptEpoch;
                }
            }
        }

        public IntPtr Pointer
        {
            get
            {
                lock (_ticketLock)
                {
                    if (_state != NativeTransferState.Pending)
                        throw new InvalidOperationException("The transfer pointer is only available while the ticket is pending.");
                    return _pointer;
                }
            }
        }

        public bool TryCommit()
        {
            lock (_ticketLock)
            {
                if (_state != NativeTransferState.Pending)
                    return false;

                _state = NativeTransferState.Committed;
                return true;
            }
        }

        public bool TryComplete(long ticketId)
        {
            if (ticketId != TicketId)
                return false;

            lock (_ticketLock)
            {
                if (_state != NativeTransferState.Pending &&
                    _state != NativeTransferState.Committed)
                {
                    return false;
                }

                _nativeLiveness = NativeResourceLiveness.Freed;
                _state = NativeTransferState.Completed;
                return true;
            }
        }

        public bool TryRollback()
        {
            long attemptEpoch;

            lock (_ticketLock)
            {
                if (_stableFailure != null)
                    throw _stableFailure;
                if (_state != NativeTransferState.Pending &&
                    !(_state == NativeTransferState.RollbackFaulted &&
                      _nativeLiveness == NativeResourceLiveness.KnownLive))
                {
                    return false;
                }

                checked
                {
                    _rollbackAttemptEpoch++;
                }

                attemptEpoch = _rollbackAttemptEpoch;
                _state = NativeTransferState.RollingBack;
            }

            NativeCleanupResult result = _rollbackCleanup(_pointer);

            lock (_ticketLock)
            {
                _nativeLiveness = result.Liveness;
                if (result.IsSuccess)
                {
                    _state = NativeTransferState.RolledBack;
                    return true;
                }

                _state = NativeTransferState.RollbackFaulted;
                var failure = new NativeTransferCleanupException(
                    TicketId,
                    attemptEpoch,
                    result.Liveness,
                    result.Disposition == CleanupFailureDisposition.Retryable,
                    result.Exception);
                if (!failure.IsRetryable)
                    _stableFailure = failure;
                throw failure;
            }
        }

        public void Dispose()
        {
            TryRollback();
        }

        private static long NextTicketId()
        {
            long ticketId = Interlocked.Increment(ref _lastTicketId);
            if (ticketId <= 0)
                throw new InvalidOperationException("The monotonic native-transfer ticket identity space was exhausted.");
            return ticketId;
        }
    }
}

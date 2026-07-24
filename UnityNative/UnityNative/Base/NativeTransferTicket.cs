using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Exclusively arbitrates commit, rollback, and native completion after pointer transfer.
    /// </summary>
    /// <remarks>
    /// A ticket starts in <see cref="NativeTransferState.Pending"/> as the sole owner of the transferred
    /// pointer. Commit hands ownership to the receiving native system; rollback destroys the still-pending
    /// resource; completion records that the receiving system destroyed it. These outcomes arbitrate under
    /// one lock so only one ownership path wins. Completion must present the monotonic ticket ID, preventing
    /// a stale native callback from completing a newer transfer even when an allocator reuses the same pointer.
    /// Rollback may be retried only after an oracle proves the resource remains live. Unknown liveness is a
    /// stable fault because a second destroy could double-free the resource. A ticket is constructed unarmed
    /// before its wrapper publishes transfer, closing the constructor-failure ownership gap. Once armed, its
    /// finalizer performs at most one finalizer-safe rollback from Pending or retryable RollbackFaulted state;
    /// committed and terminal tickets never roll back, while stable or unexpected states emit no-throw telemetry.
    /// </remarks>
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
        private int _ownershipPublished;

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

        /// <summary>Gets the process-wide monotonic identity used to reject stale completions.</summary>
        public long TicketId { get; }

        /// <summary>Gets the current transfer arbitration state.</summary>
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

        /// <summary>Gets the last proven liveness of the transferred native resource.</summary>
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

        /// <summary>Gets the number of rollback attempts started by this ticket.</summary>
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

        /// <summary>Gets the transferred pointer while the ticket remains pending.</summary>
        /// <exception cref="InvalidOperationException">The ticket is no longer pending.</exception>
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

        /// <summary>Commits pending ownership to the receiving native system.</summary>
        /// <returns><see langword="true"/> when this call won commit arbitration; otherwise, <see langword="false"/>.</returns>
        public bool TryCommit()
        {
            lock (_ticketLock)
            {
                if (_state != NativeTransferState.Pending)
                    return false;

                _state = NativeTransferState.Committed;
                GC.SuppressFinalize(this);
                return true;
            }
        }

        /// <summary>Records native completion when the supplied identity matches this ticket.</summary>
        /// <param name="ticketId">The ticket identity returned to the receiving native system.</param>
        /// <returns><see langword="true"/> when this call won completion arbitration; otherwise, <see langword="false"/>.</returns>
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
                GC.SuppressFinalize(this);
                return true;
            }
        }

        /// <summary>Attempts to destroy a still-pending transferred resource.</summary>
        /// <returns><see langword="true"/> when rollback freed the resource; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="NativeTransferCleanupException">
        /// Cleanup failed. Inspect <see cref="NativeTransferCleanupException.IsRetryable"/> before another attempt.
        /// </exception>
        public bool TryRollback()
        {
            long attemptEpoch;

            if (!TryStartRollback(false, out attemptEpoch, out _))
                return false;

            NativeCleanupResult result = InvokeRollbackCleanup();
            NativeTransferCleanupException failure;
            if (CompleteRollback(attemptEpoch, result, out failure))
                return true;

            throw failure;
        }

        /// <summary>Rolls back pending ownership. Committed or terminal tickets are left unchanged.</summary>
        /// <exception cref="NativeTransferCleanupException">Pending rollback cleanup failed.</exception>
        public void Dispose()
        {
            TryRollback();
        }

        /// <summary>
        /// Attempts one finalizer-safe rollback for abandoned managed ownership and reports any remaining
        /// nonterminal state without throwing.
        /// </summary>
        ~NativeTransferTicket()
        {
            NativeTransferDiagnostic diagnostic = null;
            try
            {
                if (Volatile.Read(ref _ownershipPublished) == 0)
                    return;

                long attemptEpoch;
                if (TryStartRollback(true, out attemptEpoch, out diagnostic))
                {
                    NativeCleanupResult result = InvokeRollbackCleanup();
                    NativeTransferCleanupException failure;
                    if (!CompleteRollback(attemptEpoch, result, out failure))
                        diagnostic = CreateDiagnostic(failure);
                }
            }
            catch (Exception exception)
            {
                try
                {
                    diagnostic = CreateDiagnostic(exception);
                }
                catch
                {
                    // Finalizer telemetry construction must not escape either.
                }
            }

            if (diagnostic != null)
                NativeTransferDiagnostics.ReportNoThrow(diagnostic);
        }

        internal void PublishOwnership()
        {
            Volatile.Write(ref _ownershipPublished, 1);
        }

        private bool TryStartRollback(
            bool isFinalizer,
            out long attemptEpoch,
            out NativeTransferDiagnostic diagnostic)
        {
            lock (_ticketLock)
            {
                attemptEpoch = 0;
                diagnostic = null;

                if (_stableFailure != null)
                {
                    if (!isFinalizer)
                        throw _stableFailure;

                    diagnostic = CreateDiagnosticLocked(_stableFailure);
                    return false;
                }

                bool canRollback = _state == NativeTransferState.Pending ||
                                   (_state == NativeTransferState.RollbackFaulted &&
                                    _nativeLiveness == NativeResourceLiveness.KnownLive);
                if (!canRollback)
                {
                    if (isFinalizer &&
                        (_state == NativeTransferState.RollbackFaulted ||
                         _state == NativeTransferState.RollingBack))
                    {
                        diagnostic = CreateDiagnosticLocked(
                            new InvalidOperationException(
                                "An abandoned transfer ticket could not safely start rollback from state " +
                                _state + "."));
                    }

                    return false;
                }

                checked
                {
                    _rollbackAttemptEpoch++;
                }

                attemptEpoch = _rollbackAttemptEpoch;
                _state = NativeTransferState.RollingBack;
                return true;
            }
        }

        private NativeCleanupResult InvokeRollbackCleanup()
        {
            try
            {
                NativeCleanupResult result = _rollbackCleanup(_pointer);
                return result ?? NativeCleanupResult.LivenessUnknownFailure(
                    new InvalidOperationException("Transfer rollback cleanup returned null."));
            }
            catch (Exception exception)
            {
                return NativeCleanupResult.LivenessUnknownFailure(exception);
            }
        }

        private bool CompleteRollback(
            long attemptEpoch,
            NativeCleanupResult result,
            out NativeTransferCleanupException failure)
        {
            lock (_ticketLock)
            {
                _nativeLiveness = result.Liveness;
                if (result.IsSuccess)
                {
                    _state = NativeTransferState.RolledBack;
                    failure = null;
                    GC.SuppressFinalize(this);
                    return true;
                }

                _state = NativeTransferState.RollbackFaulted;
                failure = new NativeTransferCleanupException(
                    TicketId,
                    attemptEpoch,
                    result.Liveness,
                    result.Disposition == CleanupFailureDisposition.Retryable,
                    result.Exception);
                if (!failure.IsRetryable)
                    _stableFailure = failure;
                return false;
            }
        }

        private NativeTransferDiagnostic CreateDiagnostic(Exception exception)
        {
            lock (_ticketLock)
            {
                return CreateDiagnosticLocked(exception);
            }
        }

        private NativeTransferDiagnostic CreateDiagnosticLocked(Exception exception)
        {
            return new NativeTransferDiagnostic(
                TicketId,
                _state,
                _nativeLiveness,
                _rollbackAttemptEpoch,
                true,
                exception);
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

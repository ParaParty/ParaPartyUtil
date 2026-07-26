using System;
using System.Collections.Generic;
using System.Threading;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class LifecycleKernel
    {
        private static long _nextOwnerId;

        private readonly object _sync = new object();
        private readonly HashSet<long> _leases = new HashSet<long>();
        private readonly HashSet<long> _callbacks = new HashSet<long>();
        private readonly HashSet<long> _ticketUses = new HashSet<long>();
        private readonly HashSet<KernelStage> _completedStages = new HashSet<KernelStage>();
        private readonly LifecycleAttemptLedger _disposeAttempts = new LifecycleAttemptLedger();
        private readonly LifecycleAttemptLedger _rollbackAttempts = new LifecycleAttemptLedger();
        private readonly LifecycleResourceSlot _handleSlot = new LifecycleResourceSlot();
        private readonly LifecycleResourceSlot _memorySlot = new LifecycleResourceSlot();
        private readonly LifecycleOrphanRegistry _orphanRegistry;
        private readonly LifecycleOrphanRecord _orphanRecord;
        private readonly KernelIdentity _owner;
        private readonly KernelOwnership _ownership;

        private KernelIdentity _lifecycle;
        private KernelLife _life;
        private KernelPublication _publication;
        private KernelNativeAuthority _native;
        private KernelAdmission _admission;
        private KernelAttemptState _attempt;
        private KernelTransferState _transfer;
        private KernelStage? _runningStage;
        private IntPtr _pointer;
        private long _nextIdentity;
        private long _publicationGeneration;
        private long _disposeAttemptId;
        private long _rollbackAttemptId;
        private long _transferId;
        private long _quiesceGeneration;
        private string _disposeToken;
        private string _rollbackToken;
        private bool _wrapperRoot = true;
        private bool _trackerRoot;
        private bool _orphanRoot;
        private bool _ticketRoot;
        private bool _finalizerSeen;

        internal LifecycleKernel(KernelOwnership ownership, bool published)
            : this(ownership, published, new LifecycleOrphanRegistry(8))
        {
        }

        internal LifecycleKernel(
            KernelOwnership ownership,
            bool published,
            LifecycleOrphanRegistry orphanRegistry)
        {
            _owner = new KernelIdentity(Interlocked.Increment(ref _nextOwnerId));
            _lifecycle = new KernelIdentity(1);
            _nextIdentity = 1;
            _ownership = ownership;
            _orphanRegistry = orphanRegistry ?? throw new ArgumentNullException(nameof(orphanRegistry));
            _orphanRecord = _orphanRegistry.Reserve(_owner) ??
                            throw new InvalidOperationException("The orphan registry has no reservation capacity.");
            _life = KernelLife.Active;
            _admission = KernelAdmission.Open;
            _attempt = KernelAttemptState.Idle;
            _transfer = KernelTransferState.None;
            if (published)
            {
                _publication = KernelPublication.Published;
                _publicationGeneration = 1;
                _pointer = new IntPtr(1);
                _native = ownership == KernelOwnership.Owned
                    ? KernelNativeAuthority.OwnedLive
                    : KernelNativeAuthority.BorrowedExternal;
            }
            else
            {
                _publication = KernelPublication.Never;
                _native = KernelNativeAuthority.Absent;
            }
            AssertInvariants();
        }

        internal LifecycleKernelSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new LifecycleKernelSnapshot
                {
                    Ownership = _ownership,
                    Life = _life,
                    Publication = _publication,
                    NativeAuthority = _native,
                    Admission = _admission,
                    DisposalAttempt = _attempt,
                    Transfer = _transfer,
                    GCHandlePhase = _handleSlot.Phase,
                    UnmanagedMemoryPhase = _memorySlot.Phase,
                    OwnerId = _owner.Value,
                    LifecycleId = _lifecycle.Value,
                    PublicationGeneration = _publicationGeneration,
                    DisposalAttemptId = _disposeAttemptId,
                    RollbackAttemptId = _rollbackAttemptId,
                    LeaseCount = _leases.Count,
                    CallbackCount = _callbacks.Count,
                    TicketUseCount = _ticketUses.Count,
                    CompletedStageCount = _completedStages.Count,
                    DisposalRecordCount = _disposeAttempts.Count,
                    RollbackRecordCount = _rollbackAttempts.Count,
                    WrapperRoot = _wrapperRoot,
                    TrackerRoot = _trackerRoot,
                    OrphanRoot = _orphanRoot,
                    TicketRoot = _ticketRoot,
                    FinalizerSeen = _finalizerSeen
                };
            }
        }

        internal KernelTransition Publish(IntPtr pointer)
        {
            lock (_sync)
            {
                if (pointer == IntPtr.Zero || _life != KernelLife.Active ||
                    _admission != KernelAdmission.Open || _publication != KernelPublication.Never ||
                    _transfer != KernelTransferState.None || _leases.Count != 0 || _callbacks.Count != 0 ||
                    _handleSlot.HasReservation || _memorySlot.HasReservation)
                    return Illegal("Publication requires an open, never-published owner with no admitted work or reservation.");
                _publication = KernelPublication.Published;
                _publicationGeneration++;
                _pointer = pointer;
                _native = _ownership == KernelOwnership.Owned
                    ? KernelNativeAuthority.OwnedLive
                    : KernelNativeAuthority.BorrowedExternal;
                return Commit();
            }
        }

        internal KernelTransition ReserveResource(KernelResourceKind kind)
        {
            lock (_sync)
            {
                if (_life != KernelLife.Active || _admission != KernelAdmission.Open ||
                    _transfer != KernelTransferState.None)
                    return Illegal("Resource allocation is closed.");
                var receipt = Slot(kind).Reserve(_owner, _lifecycle, NextIdentity());
                return receipt == null
                    ? Illegal("The resource slot already has an allocation reservation.")
                    : Commit(receipt);
            }
        }

        internal KernelTransition AllocationFailed(KernelResourceKind kind, KernelReceipt receipt)
        {
            lock (_sync)
            {
                return Slot(kind).AllocationFailed(receipt, _owner, _lifecycle)
                    ? Commit()
                    : Stale("Allocation failure does not match the current reservation.");
            }
        }

        internal KernelTransition CommitAllocation(KernelResourceKind kind, KernelReceipt receipt)
        {
            lock (_sync)
            {
                var mayPublish = _life == KernelLife.Active && _admission == KernelAdmission.Open &&
                                 _transfer == KernelTransferState.None;
                return Slot(kind).CommitAllocation(receipt, _owner, _lifecycle, mayPublish)
                    ? Commit()
                    : Stale("Allocation completion does not match the current reservation.");
            }
        }

        internal KernelTransition DetachResource(KernelResourceKind kind)
        {
            lock (_sync)
            {
                if (_life != KernelLife.Disposing || _runningStage != KernelStage.BaseResources)
                    return Illegal("Only the running base-resource stage may detach a live slot.");
                var generation = Slot(kind).DetachLive();
                return generation == 0 ? Illegal("No detachable live generation exists.") : Commit(generation);
            }
        }

        internal KernelTransition ReleaseResource(KernelResourceKind kind, long generation)
        {
            lock (_sync)
            {
                return Slot(kind).Release(generation)
                    ? Commit()
                    : Stale("The detached generation is absent or was already released.");
            }
        }

        internal KernelTransition EnterOperation()
        {
            lock (_sync)
            {
                if (_life != KernelLife.Active || _admission != KernelAdmission.Open ||
                    _publication != KernelPublication.Published)
                    return Illegal("Operation admission is closed or native authority is unpublished.");
                var token = new KernelToken("operation", _owner, NextIdentity());
                _leases.Add(token.Generation.Value);
                return Commit(token);
            }
        }

        internal KernelTransition ReleaseOperation(KernelToken token)
        {
            lock (_sync)
            {
                return ReleaseToken(token, "operation", _leases);
            }
        }

        internal KernelTransition EnterCallback()
        {
            lock (_sync)
            {
                if (_life != KernelLife.Active || _admission != KernelAdmission.Open)
                    return Illegal("Callback admission is closed.");
                var token = new KernelToken("callback", _owner, NextIdentity());
                _callbacks.Add(token.Generation.Value);
                return Commit(token);
            }
        }

        internal KernelTransition ReleaseCallback(KernelToken token)
        {
            lock (_sync)
            {
                return ReleaseToken(token, "callback", _callbacks);
            }
        }

        internal KernelTransition StartDispose(string causalToken)
        {
            lock (_sync)
            {
                if (_life == KernelLife.FaultStable)
                    return KernelTransition.Reject(KernelTransitionCode.StableFailure, ExactDisposalResult());
                if (_attempt == KernelAttemptState.Running)
                    return _disposeToken == causalToken
                        ? KernelTransition.Reject(KernelTransitionCode.SameCausalAttempt, "Same-causal disposal reentry is forbidden.")
                        : KernelTransition.Reject(KernelTransitionCode.PendingWork, "A foreign caller must join the exact running attempt.");
                if (_life != KernelLife.Active && _life != KernelLife.FaultRetryable)
                    return Illegal("Disposal cannot start from this lifecycle state.");
                var token = new KernelCausalToken(causalToken);
                _disposeAttemptId++;
                _lifecycle = NextIdentity();
                _disposeAttempts.Append(new KernelIdentity(_disposeAttemptId), token);
                _disposeToken = token.Value;
                _life = KernelLife.Disposing;
                _admission = _admission == KernelAdmission.Open
                    ? KernelAdmission.Closing
                    : KernelAdmission.Closed;
                _attempt = KernelAttemptState.Running;
                _runningStage = null;
                _quiesceGeneration = 0;
                return Commit(new KernelIdentity(_disposeAttemptId));
            }
        }

        internal KernelTransition JoinDispose(string waiterId, string currentToken, bool insideCallout)
        {
            lock (_sync)
            {
                if (_attempt != KernelAttemptState.Running)
                    return Illegal("There is no running disposal attempt to join.");
                if (_disposeToken == currentToken)
                    return KernelTransition.Reject(KernelTransitionCode.SameCausalAttempt, "Same-causal disposal join is forbidden.");
                if (insideCallout)
                    return KernelTransition.Reject(KernelTransitionCode.CycleRisk, "A cleanup callout cannot block on a foreign running owner.");
                return _disposeAttempts.Current.RegisterWaiter(waiterId)
                    ? Commit(new KernelIdentity(_disposeAttemptId))
                    : Illegal("The waiter identity must be nonempty and unique.");
            }
        }

        internal string ObserveDisposeResult(long attemptId, string waiterId)
        {
            lock (_sync)
            {
                var record = attemptId <= 0 ? null : _disposeAttempts.Find(new KernelIdentity(attemptId));
                return record?.Observe(waiterId);
            }
        }

        internal KernelTransition CloseAdmission()
        {
            lock (_sync)
            {
                if (_life != KernelLife.Disposing || _admission != KernelAdmission.Closing)
                    return Illegal("Admission is not closing for disposal.");
                if (_leases.Count != 0 || _callbacks.Count != 0)
                    return KernelTransition.Reject(KernelTransitionCode.PendingWork, "Admitted leases and callbacks must drain first.");
                _admission = KernelAdmission.Closed;
                return Commit();
            }
        }

        internal KernelTransition BeginStage(KernelStage stage)
        {
            lock (_sync)
            {
                if (_life != KernelLife.Disposing || _attempt != KernelAttemptState.Running ||
                    _runningStage.HasValue || _completedStages.Contains(stage) || !StageDependenciesMet(stage))
                    return Illegal("The cleanup stage is not legal at this point.");
                _runningStage = stage;
                return Commit(new KernelReceipt(
                    "stage", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId), NextIdentity()));
            }
        }

        internal KernelTransition CompleteManaged()
            => CompleteSimpleStage(KernelStage.Managed);

        internal KernelTransition RecordQuiescence()
        {
            lock (_sync)
            {
                if (_runningStage != KernelStage.NativeQuiesce || _admission != KernelAdmission.Closed)
                    return Illegal("Quiescence requires the exact running stage and closed admission.");
                if (_publication == KernelPublication.Published)
                {
                    _quiesceGeneration = _publicationGeneration;
                    _runningStage = null;
                    _completedStages.Add(KernelStage.NativeQuiesce);
                    return Commit(new KernelReceipt(
                        "quiescence", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                        new KernelIdentity(_publicationGeneration)));
                }
                if (_publication == KernelPublication.Never && _native == KernelNativeAuthority.Absent)
                {
                    _runningStage = null;
                    _completedStages.Add(KernelStage.NativeQuiesce);
                    return Commit();
                }
                return Illegal("The current publication cannot produce a quiescence receipt.");
            }
        }

        internal KernelTransition CompleteCallbackFence()
        {
            lock (_sync)
            {
                if (_runningStage != KernelStage.CallbackFence || _leases.Count != 0 || _callbacks.Count != 0)
                    return Illegal("Callback fence completion requires zero admitted work.");
                _runningStage = null;
                _completedStages.Add(KernelStage.CallbackFence);
                return Commit();
            }
        }

        internal KernelTransition CommitNativeAuthority(KernelReceipt quiescenceReceipt)
        {
            lock (_sync)
            {
                if (_runningStage != KernelStage.NativeAuthority ||
                    !_completedStages.Contains(KernelStage.CallbackFence))
                    return Illegal("Native authority is not the exact running stage.");
                if (_publication == KernelPublication.Published && _ownership == KernelOwnership.Owned)
                {
                    if (_native != KernelNativeAuthority.OwnedLive || _quiesceGeneration <= 0 ||
                        quiescenceReceipt == null || !quiescenceReceipt.Matches(
                            "quiescence", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                            new KernelIdentity(_publicationGeneration)))
                        return Stale("Owned destruction requires the exact quiescence receipt.");
                    _pointer = IntPtr.Zero;
                    _native = KernelNativeAuthority.Freed;
                    _publication = KernelPublication.Unpublished;
                }
                else if (_publication == KernelPublication.Published && _ownership == KernelOwnership.Borrowed)
                {
                    _pointer = IntPtr.Zero;
                    _publication = KernelPublication.Unpublished;
                }
                else if (_publication != KernelPublication.Never || _native != KernelNativeAuthority.Absent)
                {
                    return Illegal("Native authority does not match owned, borrowed, or absent cleanup.");
                }
                _quiesceGeneration = 0;
                _runningStage = null;
                _completedStages.Add(KernelStage.NativeAuthority);
                return Commit();
            }
        }

        internal KernelTransition CompleteLegacyNotification()
            => CompleteSimpleStage(KernelStage.LegacyUnpublishNotification);

        internal KernelTransition CompleteBaseResources()
        {
            lock (_sync)
            {
                if (_runningStage != KernelStage.BaseResources ||
                    !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer)
                    return Illegal("Base-resource completion requires every exact generation to be released.");
                _runningStage = null;
                _completedStages.Add(KernelStage.BaseResources);
                return Commit();
            }
        }

        internal KernelTransition FailRunningStage(bool stable)
        {
            lock (_sync)
            {
                if (_life != KernelLife.Disposing || !_runningStage.HasValue)
                    return Illegal("No cleanup stage is running.");
                var outcome = stable
                    ? KernelAttemptOutcome.StableFailure
                    : KernelAttemptOutcome.RetryableFailure;
                var result = "dispose-" + (stable ? "stable" : "retryable") +
                             "-failure:" + _disposeAttemptId + ":" + _runningStage.Value;
                _disposeAttempts.Current.Complete(outcome, result);
                _life = stable ? KernelLife.FaultStable : KernelLife.FaultRetryable;
                _attempt = stable ? KernelAttemptState.StableFault : KernelAttemptState.RetryableFault;
                _admission = KernelAdmission.Closed;
                _disposeToken = null;
                _runningStage = null;
                _quiesceGeneration = 0;
                return Commit(result);
            }
        }

        internal KernelTransition FinishDispose()
        {
            lock (_sync)
            {
                if (_life != KernelLife.Disposing || _admission != KernelAdmission.Closed ||
                    _runningStage.HasValue || _completedStages.Count != 6 ||
                    _leases.Count != 0 || _callbacks.Count != 0 ||
                    !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer)
                    return Illegal("Disposal is not terminal-safe.");
                var result = "dispose-success:" + _disposeAttemptId;
                _disposeAttempts.Current.Complete(KernelAttemptOutcome.Success, result);
                _life = KernelLife.Disposed;
                _attempt = KernelAttemptState.Complete;
                _disposeToken = null;
                _wrapperRoot = false;
                _trackerRoot = false;
                _orphanRoot = false;
                _ticketRoot = false;
                _orphanRecord.Terminal = true;
                _orphanRegistry.RemoveTerminal(_orphanRecord);
                return Commit(result);
            }
        }

        internal KernelTransition BeginTransfer(string causalToken)
        {
            lock (_sync)
            {
                if (_life != KernelLife.Active || _ownership != KernelOwnership.Owned ||
                    _admission != KernelAdmission.Open || _publication != KernelPublication.Published ||
                    _native != KernelNativeAuthority.OwnedLive || _leases.Count != 0 || _callbacks.Count != 0 ||
                    !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer ||
                    _transfer != KernelTransferState.None || !_wrapperRoot || _trackerRoot || _orphanRoot)
                    return Illegal("Transfer requires an untracked OwnedKnownLive owner with empty slots and no admitted work.");
                new KernelCausalToken(causalToken);
                _transferId++;
                _admission = KernelAdmission.Closed;
                _transfer = KernelTransferState.Preparing;
                return Commit(new KernelReceipt(
                    "transfer-preparation", _owner, _lifecycle, new KernelIdentity(_transferId),
                    new KernelIdentity(_publicationGeneration)));
            }
        }

        internal KernelTransition CommitTransfer(KernelReceipt preparationReceipt)
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.Preparing || preparationReceipt == null ||
                    !preparationReceipt.Matches(
                        "transfer-preparation", _owner, _lifecycle, new KernelIdentity(_transferId),
                        new KernelIdentity(_publicationGeneration)))
                    return Stale("Transfer commit requires the exact preparation receipt.");
                _life = KernelLife.Transferred;
                _publication = KernelPublication.Unpublished;
                _native = KernelNativeAuthority.TicketOwned;
                _transfer = KernelTransferState.Pending;
                _wrapperRoot = false;
                _ticketRoot = true;
                return Commit();
            }
        }

        internal KernelTransition EnterTicketUse()
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.Pending && _transfer != KernelTransferState.HandoffInUse)
                    return Illegal("Ticket-use admission is closed.");
                var token = new KernelToken("ticket-use", _owner, NextIdentity());
                _ticketUses.Add(token.Generation.Value);
                _transfer = KernelTransferState.HandoffInUse;
                return Commit(token);
            }
        }

        internal KernelTransition ReleaseTicketUse(KernelToken token)
        {
            lock (_sync)
            {
                if (token == null || token.Purpose != "ticket-use" || !token.Owner.Equals(_owner) ||
                    !_ticketUses.Remove(token.Generation.Value))
                    return KernelTransition.Reject(
                        KernelTransitionCode.InvalidToken,
                        "The ticket-use token is foreign, stale, or already released.");
                if (_ticketUses.Count == 0 && _transfer == KernelTransferState.HandoffInUse)
                    _transfer = KernelTransferState.Pending;
                return Commit();
            }
        }

        internal KernelTransition CommitTicket()
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.Pending || _ticketUses.Count != 0)
                    return Illegal("Ticket commit requires Pending with zero exact uses.");
                _transfer = KernelTransferState.Committed;
                return Commit();
            }
        }

        internal KernelTransition CompleteTicket()
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.Committed || _native != KernelNativeAuthority.TicketOwned)
                    return Illegal("Only a committed ticket can complete.");
                _transfer = KernelTransferState.Completed;
                _native = KernelNativeAuthority.Freed;
                _pointer = IntPtr.Zero;
                _ticketRoot = false;
                _orphanRoot = false;
                _orphanRecord.Terminal = true;
                _orphanRegistry.RemoveTerminal(_orphanRecord);
                return Commit();
            }
        }

        internal KernelTransition StartRollback(string causalToken)
        {
            lock (_sync)
            {
                if (_transfer == KernelTransferState.RollbackStable)
                    return KernelTransition.Reject(KernelTransitionCode.StableFailure, ExactRollbackResult());
                if (_transfer == KernelTransferState.RollbackDraining || _transfer == KernelTransferState.RollingBack)
                    return _rollbackToken == causalToken
                        ? KernelTransition.Reject(KernelTransitionCode.SameCausalAttempt, "Same-causal rollback reentry is forbidden.")
                        : KernelTransition.Reject(KernelTransitionCode.PendingWork, "A foreign caller must join the exact rollback attempt.");
                if (_transfer != KernelTransferState.Pending &&
                    _transfer != KernelTransferState.HandoffInUse &&
                    _transfer != KernelTransferState.RollbackRetryable)
                    return Illegal("Rollback cannot start from this ticket state.");
                var token = new KernelCausalToken(causalToken);
                _rollbackAttemptId++;
                _rollbackAttempts.Append(new KernelIdentity(_rollbackAttemptId), token);
                _rollbackToken = token.Value;
                _transfer = _ticketUses.Count == 0
                    ? KernelTransferState.RollingBack
                    : KernelTransferState.RollbackDraining;
                return Commit(new KernelIdentity(_rollbackAttemptId));
            }
        }

        internal KernelTransition JoinRollback(string waiterId, string currentToken, bool insideCallout)
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.RollbackDraining &&
                    _transfer != KernelTransferState.RollingBack)
                    return Illegal("There is no running rollback attempt to join.");
                if (_rollbackToken == currentToken)
                    return KernelTransition.Reject(KernelTransitionCode.SameCausalAttempt, "Same-causal rollback join is forbidden.");
                if (insideCallout)
                    return KernelTransition.Reject(KernelTransitionCode.CycleRisk, "A cleanup callout cannot block on a foreign rollback.");
                return _rollbackAttempts.Current.RegisterWaiter(waiterId)
                    ? Commit(new KernelIdentity(_rollbackAttemptId))
                    : Illegal("The waiter identity must be nonempty and unique.");
            }
        }

        internal string ObserveRollbackResult(long attemptId, string waiterId)
        {
            lock (_sync)
            {
                var record = attemptId <= 0 ? null : _rollbackAttempts.Find(new KernelIdentity(attemptId));
                return record?.Observe(waiterId);
            }
        }

        internal KernelTransition FinishRollbackDrain()
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.RollbackDraining || _ticketUses.Count != 0)
                    return KernelTransition.Reject(KernelTransitionCode.PendingWork, "Ticket uses have not drained.");
                _transfer = KernelTransferState.RollingBack;
                return Commit();
            }
        }

        internal KernelTransition CompleteRollback(KernelAttemptOutcome outcome)
        {
            lock (_sync)
            {
                if (_transfer != KernelTransferState.RollingBack || outcome == KernelAttemptOutcome.Running)
                    return Illegal("Rollback completion requires the exact running attempt.");
                var result = "rollback-" + outcome + ":" + _rollbackAttemptId;
                _rollbackAttempts.Current.Complete(outcome, result);
                _rollbackToken = null;
                if (outcome == KernelAttemptOutcome.Success)
                {
                    _transfer = KernelTransferState.RolledBack;
                    _native = KernelNativeAuthority.Freed;
                    _pointer = IntPtr.Zero;
                    _ticketRoot = false;
                    _orphanRoot = false;
                    _orphanRecord.Terminal = true;
                    _orphanRegistry.RemoveTerminal(_orphanRecord);
                }
                else if (outcome == KernelAttemptOutcome.RetryableFailure)
                {
                    _transfer = KernelTransferState.RollbackRetryable;
                }
                else
                {
                    _transfer = KernelTransferState.RollbackStable;
                    _native = KernelNativeAuthority.Unknown;
                }
                return Commit(result);
            }
        }

        internal KernelTransition RegisterTracker()
        {
            lock (_sync)
            {
                if (!_wrapperRoot || _orphanRoot || _transfer != KernelTransferState.None ||
                    _life == KernelLife.Disposed || _life == KernelLife.Transferred)
                    return Illegal("Tracker registration requires ordinary wrapper reachability.");
                _trackerRoot = true;
                return Commit();
            }
        }

        internal KernelTransition FinalizeWrapper()
        {
            lock (_sync)
            {
                if (!_wrapperRoot || _trackerRoot)
                    return Illegal("Wrapper finalizer handoff is not legal.");
                if (!_orphanRegistry.Publish(_orphanRecord))
                    return Illegal("The reserved orphan record could not be published.");
                _wrapperRoot = false;
                _orphanRoot = true;
                _ticketRoot = false;
                _admission = KernelAdmission.Closed;
                _finalizerSeen = true;
                return Commit();
            }
        }

        internal KernelTransition FinalizeTicket()
        {
            lock (_sync)
            {
                if (!_ticketRoot || !IsLiveTicket(_transfer))
                    return Illegal("Ticket finalizer handoff is not legal.");
                if (!_orphanRegistry.Publish(_orphanRecord))
                    return Illegal("The reserved orphan record could not be published.");
                _ticketRoot = false;
                _orphanRoot = true;
                _finalizerSeen = true;
                return Commit();
            }
        }

        internal KernelTransition ValidateTicketRootVector(
            bool wrapper,
            bool tracker,
            bool orphan,
            bool ticket,
            bool terminal)
        {
            var count = (wrapper ? 1 : 0) + (tracker ? 1 : 0) + (orphan ? 1 : 0) + (ticket ? 1 : 0);
            var accepted = terminal
                ? count == 0
                : count == 1 && !wrapper && !tracker && (orphan || ticket);
            return accepted ? KernelTransition.Accept() : Illegal("The recovery-root vector is not legal.");
        }

        internal string[] InvariantErrors()
        {
            lock (_sync)
            {
                var errors = new List<string>();
                if (_publication == KernelPublication.Never &&
                    (_publicationGeneration != 0 || _native != KernelNativeAuthority.Absent))
                    errors.Add("NeverPublished requires generation zero and Absent authority.");
                if (_publication == KernelPublication.Published &&
                    (_publicationGeneration <= 0 || _pointer == IntPtr.Zero ||
                     (_native != KernelNativeAuthority.OwnedLive &&
                      _native != KernelNativeAuthority.BorrowedExternal &&
                      _native != KernelNativeAuthority.Unknown)))
                    errors.Add("Published requires a pointer, generation, and live/unknown authority.");
                if (_publication == KernelPublication.Unpublished &&
                    (_publicationGeneration <= 0 ||
                     (_native != KernelNativeAuthority.BorrowedExternal &&
                      _native != KernelNativeAuthority.Unknown &&
                      _native != KernelNativeAuthority.Freed &&
                      _native != KernelNativeAuthority.TicketOwned)))
                    errors.Add("Unpublished requires a prior generation and non-wrapper-owned authority.");
                if (_native == KernelNativeAuthority.Freed && _publication == KernelPublication.Published)
                    errors.Add("Freed and Published cannot coexist.");
                if (_ownership == KernelOwnership.Borrowed &&
                    (_native == KernelNativeAuthority.OwnedLive || _native == KernelNativeAuthority.Unknown ||
                     _native == KernelNativeAuthority.Freed || _native == KernelNativeAuthority.TicketOwned))
                    errors.Add("Borrowed authority cannot become destructive.");
                if (_quiesceGeneration != 0 &&
                    (_publication != KernelPublication.Published ||
                     _quiesceGeneration != _publicationGeneration ||
                     _attempt != KernelAttemptState.Running))
                    errors.Add("Quiescence receipt must name the exact running published generation.");
                if (_life == KernelLife.Active && _attempt != KernelAttemptState.Idle)
                    errors.Add("Active requires an idle disposal attempt.");
                if (_life == KernelLife.Disposing &&
                    (_attempt != KernelAttemptState.Running || _admission == KernelAdmission.Open ||
                     string.IsNullOrEmpty(_disposeToken)))
                    errors.Add("Disposing requires one causal running attempt and closing admission.");
                if (_life == KernelLife.FaultRetryable &&
                    (_attempt != KernelAttemptState.RetryableFault || _admission == KernelAdmission.Open))
                    errors.Add("Retryable fault requires its exact completed attempt.");
                if (_life == KernelLife.FaultStable &&
                    (_attempt != KernelAttemptState.StableFault || _admission == KernelAdmission.Open))
                    errors.Add("Stable fault requires its exact completed attempt.");
                if (_attempt != KernelAttemptState.Running && !string.IsNullOrEmpty(_disposeToken))
                    errors.Add("Only a running disposal attempt may retain a causal token.");
                if (_runningStage.HasValue &&
                    (_life != KernelLife.Disposing || _attempt != KernelAttemptState.Running ||
                     _completedStages.Contains(_runningStage.Value)))
                    errors.Add("A running stage must be incomplete and owned by the current disposal attempt.");
                if (_life == KernelLife.Disposed &&
                    (_attempt != KernelAttemptState.Complete || _completedStages.Count != 6 ||
                     _admission != KernelAdmission.Closed || _leases.Count != 0 || _callbacks.Count != 0 ||
                     !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer ||
                     _transfer != KernelTransferState.None ||
                     _native == KernelNativeAuthority.OwnedLive || _native == KernelNativeAuthority.Unknown ||
                     _native == KernelNativeAuthority.TicketOwned || _wrapperRoot || _trackerRoot ||
                     _orphanRoot || _ticketRoot))
                    errors.Add("Disposed retains safety-relevant authority.");
                if (_life == KernelLife.Transferred &&
                    (_ownership != KernelOwnership.Owned || _admission != KernelAdmission.Closed ||
                     _publication != KernelPublication.Unpublished ||
                     !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer ||
                     _leases.Count != 0 || _callbacks.Count != 0 ||
                     _transfer == KernelTransferState.None || _transfer == KernelTransferState.Preparing))
                    errors.Add("Transferred wrapper retains wrapper-side authority.");
                if (_transfer == KernelTransferState.None &&
                    (_ticketUses.Count != 0 || !string.IsNullOrEmpty(_rollbackToken) || _ticketRoot))
                    errors.Add("No-transfer state retains ticket state.");
                if (_transfer == KernelTransferState.Preparing &&
                    (_life != KernelLife.Active || _ticketRoot || _ticketUses.Count != 0))
                    errors.Add("Preparing transfer must remain an active wrapper reservation.");
                if (IsLiveTicket(_transfer) && !ValidLiveTicketRoots())
                    errors.Add("Live ticket requires exactly orphan-only or ticket-only recovery root.");
                if (IsTerminalTicket(_transfer) && (_wrapperRoot || _trackerRoot || _orphanRoot || _ticketRoot))
                    errors.Add("Terminal ticket requires zero recovery roots.");
                if (_transfer == KernelTransferState.HandoffInUse && _ticketUses.Count == 0)
                    errors.Add("HandoffInUse requires at least one exact ticket-use token.");
                if (_ticketUses.Count != 0 && _transfer != KernelTransferState.HandoffInUse &&
                    _transfer != KernelTransferState.RollbackDraining)
                    errors.Add("Ticket-use tokens exist outside handoff or rollback draining.");
                if (_transfer == KernelTransferState.RollingBack && _ticketUses.Count != 0)
                    errors.Add("Native rollback cannot run while ticket uses remain.");
                if ((_transfer == KernelTransferState.RollbackDraining ||
                     _transfer == KernelTransferState.RollingBack) != !string.IsNullOrEmpty(_rollbackToken))
                    errors.Add("Rollback causal token does not match the running rollback attempt.");
                if (_orphanRoot && (_wrapperRoot || _trackerRoot || _ticketRoot))
                    errors.Add("Orphan recovery must be the sole root.");
                if (_finalizerSeen && _wrapperRoot)
                    errors.Add("Finalizer-observed authority cannot retain ordinary wrapper reachability.");
                if (_finalizerSeen && _life != KernelLife.Disposed && !IsTerminalTicket(_transfer) && !_orphanRoot)
                    errors.Add("Finalizer-observed nonterminal authority requires an orphan root.");
                return errors.ToArray();
            }
        }

        private KernelTransition CompleteSimpleStage(KernelStage stage)
        {
            lock (_sync)
            {
                if (_runningStage != stage)
                    return Illegal("The requested cleanup stage is not running.");
                _runningStage = null;
                _completedStages.Add(stage);
                return Commit();
            }
        }

        private bool StageDependenciesMet(KernelStage stage)
        {
            switch (stage)
            {
                case KernelStage.Managed:
                    return true;
                case KernelStage.NativeQuiesce:
                    return _completedStages.Contains(KernelStage.Managed) && _admission == KernelAdmission.Closed;
                case KernelStage.CallbackFence:
                    return _completedStages.Contains(KernelStage.NativeQuiesce) &&
                           _leases.Count == 0 && _callbacks.Count == 0;
                case KernelStage.NativeAuthority:
                    return _completedStages.Contains(KernelStage.CallbackFence);
                case KernelStage.LegacyUnpublishNotification:
                case KernelStage.BaseResources:
                    return _completedStages.Contains(KernelStage.NativeAuthority);
                default:
                    return false;
            }
        }

        private LifecycleResourceSlot Slot(KernelResourceKind kind)
            => kind == KernelResourceKind.GCHandle ? _handleSlot : _memorySlot;

        private KernelIdentity NextIdentity() => new KernelIdentity(++_nextIdentity);

        private KernelTransition ReleaseToken(KernelToken token, string purpose, HashSet<long> tokens)
        {
            if (token == null || token.Purpose != purpose || !token.Owner.Equals(_owner))
                return KernelTransition.Reject(KernelTransitionCode.InvalidToken, "The token belongs to another authority.");
            if (!tokens.Remove(token.Generation.Value))
                return KernelTransition.Reject(KernelTransitionCode.InvalidToken, "The token is stale or already released.");
            return Commit();
        }

        private string ExactDisposalResult() => _disposeAttempts.Current?.Result ?? "stable disposal failure";
        private string ExactRollbackResult() => _rollbackAttempts.Current?.Result ?? "stable rollback failure";

        private bool ValidLiveTicketRoots()
        {
            var count = (_wrapperRoot ? 1 : 0) + (_trackerRoot ? 1 : 0) +
                        (_orphanRoot ? 1 : 0) + (_ticketRoot ? 1 : 0);
            return count == 1 && !_wrapperRoot && !_trackerRoot && (_orphanRoot || _ticketRoot);
        }

        private static bool IsLiveTicket(KernelTransferState state)
        {
            return state == KernelTransferState.Pending || state == KernelTransferState.HandoffInUse ||
                   state == KernelTransferState.Committed || state == KernelTransferState.RollbackDraining ||
                   state == KernelTransferState.RollingBack || state == KernelTransferState.RollbackRetryable ||
                   state == KernelTransferState.RollbackStable;
        }

        private static bool IsTerminalTicket(KernelTransferState state)
            => state == KernelTransferState.Completed || state == KernelTransferState.RolledBack;

        private KernelTransition Commit(object value = null)
        {
            AssertInvariants();
            return KernelTransition.Accept(value);
        }

        private static KernelTransition Illegal(string detail)
            => KernelTransition.Reject(KernelTransitionCode.IllegalState, detail);

        private static KernelTransition Stale(string detail)
            => KernelTransition.Reject(KernelTransitionCode.StaleReceipt, detail);

        private void AssertInvariants()
        {
            var errors = InvariantErrors();
            if (errors.Length != 0)
                throw new InvalidOperationException("Lifecycle kernel invariant failure: " + string.Join(" | ", errors));
        }
    }
}

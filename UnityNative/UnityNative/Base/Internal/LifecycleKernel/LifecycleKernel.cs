using System;
using System.Collections.Generic;
using System.Threading;

namespace Paraparty.UnityNative.Base.Internal.LifecycleKernel
{
    internal sealed class LifecycleKernel
    {
        private static long _nextOwnerId;

        private readonly object _sync = new object();
        private readonly object _capabilitySeal = new object();
        private readonly HashSet<long> _leases = new HashSet<long>();
        private readonly HashSet<long> _callbacks = new HashSet<long>();
        private readonly HashSet<long> _ticketUses = new HashSet<long>();
        private readonly HashSet<KernelStage> _completedStages = new HashSet<KernelStage>();
        private readonly LifecycleAttemptLedger _disposeAttempts = new LifecycleAttemptLedger();
        private readonly LifecycleAttemptLedger _transferAttempts = new LifecycleAttemptLedger();
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
        private KernelReceipt _runningStageReceipt;
        private KernelReceipt _disposeReceipt;
        private KernelReceipt _quiescenceReceipt;
        private KernelReceipt _transferReceipt;
        private KernelReceipt _ticketReceipt;
        private KernelReceipt _terminalTicketReceipt;
        private KernelReceipt _rollbackReceipt;
        private KernelAttemptCapability _disposeCapability;
        private KernelAttemptCapability _transferCapability;
        private KernelAttemptCapability _rollbackCapability;
        private IntPtr _pointer;
        private long _nextIdentity;
        private long _publicationGeneration;
        private long _disposeAttemptId;
        private long _rollbackAttemptId;
        private long _transferId;
        private long _quiesceGeneration;
        private bool _wrapperRoot = true;
        private bool _trackerRoot;
        private bool _orphanRoot;
        private bool _ticketRoot;
        private bool _finalizerSeen;
        private bool _domainShutdownPrepared;

        internal LifecycleKernel(KernelOwnership ownership, bool published)
            : this(ownership, published, LifecycleOrphanRegistry.Domain)
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
            var shutdownDuringConstruction = _orphanRegistry.Bind(_orphanRecord, this);
            if (shutdownDuringConstruction)
                PrepareDomainShutdown();
            else
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
                    TransferAttemptId = _transferId,
                    RollbackAttemptId = _rollbackAttemptId,
                    LeaseCount = _leases.Count,
                    CallbackCount = _callbacks.Count,
                    TicketUseCount = _ticketUses.Count,
                    CompletedStageCount = _completedStages.Count,
                    DisposalRecordCount = _disposeAttempts.Count,
                    TransferRecordCount = _transferAttempts.Count,
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
                    _orphanRegistry.Shutdown ||
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
                    _orphanRegistry.Shutdown ||
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
                                 !_orphanRegistry.Shutdown && _transfer == KernelTransferState.None;
                return Slot(kind).CommitAllocation(receipt, _owner, _lifecycle, mayPublish)
                    ? Commit()
                    : Stale("Allocation completion does not match the current reservation.");
            }
        }

        internal KernelTransition DetachResource(KernelResourceKind kind, KernelReceipt stageReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRunningStage(KernelStage.BaseResources, stageReceipt))
                    return Stale("Base-resource detachment requires the exact stage reservation.");
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
                    _orphanRegistry.Shutdown ||
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
                if (_orphanRegistry.Shutdown)
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

        internal KernelTransition IssueCalloutCapability(
            KernelAttemptCapability attempt,
            KernelCalloutKind kind)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(attempt, _disposeCapability) &&
                    !ReferenceEquals(attempt, _transferCapability) &&
                    !ReferenceEquals(attempt, _rollbackCapability))
                    return KernelTransition.Reject(
                        KernelTransitionCode.InvalidToken,
                        "Callout capability requires this owner's exact running attempt.");
                return Commit(new KernelCalloutCapability(attempt, kind, _capabilitySeal));
            }
        }

        internal KernelTransition RequestDispose(
            string waiterId,
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (_transfer == KernelTransferState.RollbackDraining ||
                    _transfer == KernelTransferState.RollingBack)
                    return JoinRunningAttempt(
                        _rollbackAttempts.Current,
                        _rollbackCapability,
                        currentAttempt,
                        callout,
                        waiterId,
                        KernelAttemptKind.Rollback,
                        "rollback");
                if (_attempt == KernelAttemptState.Running)
                    return JoinRunningAttempt(
                        _disposeAttempts.Current,
                        _disposeCapability,
                        currentAttempt,
                        callout,
                        waiterId,
                        KernelAttemptKind.Disposal,
                        "disposal");
                if (_life == KernelLife.Disposed || _life == KernelLife.Transferred)
                    return Commit("dispose-terminal");
                return StartDispose(currentAttempt, callout);
            }
        }

        internal KernelTransition StartDispose(
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (_life == KernelLife.FaultStable)
                    return KernelTransition.Reject(KernelTransitionCode.StableFailure, ExactDisposalResult());
                if (_attempt == KernelAttemptState.Running)
                    return RunningAttemptConflict(
                        _disposeCapability, currentAttempt, callout, "disposal");
                if (_life != KernelLife.Active && _life != KernelLife.FaultRetryable)
                    return Illegal("Disposal cannot start from this lifecycle state.");
                if (_transfer != KernelTransferState.None)
                    return Illegal("Disposal cannot start while transfer authority is active or reserved.");
                if (_life == KernelLife.FaultRetryable &&
                    !_completedStages.Contains(KernelStage.NativeAuthority))
                {
                    _completedStages.Remove(KernelStage.NativeQuiesce);
                    _completedStages.Remove(KernelStage.CallbackFence);
                }
                _disposeAttemptId++;
                _lifecycle = NextIdentity();
                _disposeCapability = IssueAttemptCapability(
                    KernelAttemptKind.Disposal, new KernelIdentity(_disposeAttemptId));
                _disposeAttempts.Append(
                    new KernelIdentity(_disposeAttemptId), _disposeCapability.Token);
                _life = KernelLife.Disposing;
                _admission = _admission == KernelAdmission.Open
                    ? KernelAdmission.Closing
                    : KernelAdmission.Closed;
                _attempt = KernelAttemptState.Running;
                _runningStage = null;
                _runningStageReceipt = null;
                _quiesceGeneration = 0;
                _quiescenceReceipt = null;
                _disposeReceipt = new KernelReceipt(
                    "dispose-attempt", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId), NextIdentity());
                return Commit(new KernelAttemptReservation(_disposeReceipt, _disposeCapability));
            }
        }

        internal KernelTransition JoinDispose(
            string waiterId,
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (_attempt != KernelAttemptState.Running)
                    return Illegal("There is no running disposal attempt to join.");
                return JoinRunningAttempt(
                    _disposeAttempts.Current,
                    _disposeCapability,
                    currentAttempt,
                    callout,
                    waiterId,
                    KernelAttemptKind.Disposal,
                    "disposal");
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

        internal KernelTransition CloseAdmission(KernelReceipt disposeReceipt)
        {
            lock (_sync)
            {
                if (!MatchesDisposeAttempt(disposeReceipt))
                    return Stale("Admission closure requires the exact disposal attempt.");
                if (_life != KernelLife.Disposing || _admission != KernelAdmission.Closing)
                    return Illegal("Admission is not closing for disposal.");
                if (_leases.Count != 0 || _callbacks.Count != 0)
                    return KernelTransition.Reject(KernelTransitionCode.PendingWork, "Admitted leases and callbacks must drain first.");
                _admission = KernelAdmission.Closed;
                return Commit();
            }
        }

        internal KernelTransition BeginStage(KernelStage stage, KernelReceipt disposeReceipt)
        {
            lock (_sync)
            {
                if (!MatchesDisposeAttempt(disposeReceipt))
                    return Stale("Stage reservation requires the exact disposal attempt.");
                if (_life != KernelLife.Disposing || _attempt != KernelAttemptState.Running ||
                    _runningStage.HasValue || _completedStages.Contains(stage) || !StageDependenciesMet(stage))
                    return Illegal("The cleanup stage is not legal at this point.");
                _runningStage = stage;
                _runningStageReceipt = new KernelReceipt(
                    "stage", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId), NextIdentity(), stage);
                return Commit(_runningStageReceipt);
            }
        }

        internal KernelTransition CompleteManaged(KernelReceipt stageReceipt)
            => CompleteSimpleStage(KernelStage.Managed, stageReceipt);

        internal KernelTransition RecordQuiescence(KernelReceipt stageReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRunningStage(KernelStage.NativeQuiesce, stageReceipt))
                    return Stale("Quiescence completion requires the exact stage reservation.");
                if (_admission != KernelAdmission.Closed)
                    return Illegal("Quiescence requires the exact running stage and closed admission.");
                if (_publication == KernelPublication.Published)
                {
                    _quiesceGeneration = _publicationGeneration;
                    CompleteRunningStage(KernelStage.NativeQuiesce);
                    _quiescenceReceipt = new KernelReceipt(
                        "quiescence", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                        new KernelIdentity(_publicationGeneration));
                    return Commit(_quiescenceReceipt);
                }
                if (_publication == KernelPublication.Never && _native == KernelNativeAuthority.Absent)
                {
                    CompleteRunningStage(KernelStage.NativeQuiesce);
                    return Commit();
                }
                return Illegal("The current publication cannot produce a quiescence receipt.");
            }
        }

        internal KernelTransition CompleteCallbackFence(KernelReceipt stageReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRunningStage(KernelStage.CallbackFence, stageReceipt))
                    return Stale("Callback-fence completion requires the exact stage reservation.");
                if (_leases.Count != 0 || _callbacks.Count != 0)
                    return Illegal("Callback fence completion requires zero admitted work.");
                CompleteRunningStage(KernelStage.CallbackFence);
                return Commit();
            }
        }

        internal KernelTransition CommitNativeAuthority(
            KernelReceipt stageReceipt,
            KernelReceipt quiescenceReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRunningStage(KernelStage.NativeAuthority, stageReceipt))
                    return Stale("Native-authority completion requires the exact stage reservation.");
                if (!_completedStages.Contains(KernelStage.CallbackFence))
                    return Illegal("Native authority dependencies are incomplete.");
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
                _quiescenceReceipt = null;
                CompleteRunningStage(KernelStage.NativeAuthority);
                return Commit();
            }
        }

        internal KernelTransition CompleteLegacyNotification(KernelReceipt stageReceipt)
            => CompleteSimpleStage(KernelStage.LegacyUnpublishNotification, stageReceipt);

        internal KernelTransition CompleteBaseResources(KernelReceipt stageReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRunningStage(KernelStage.BaseResources, stageReceipt))
                    return Stale("Base-resource completion requires the exact stage reservation.");
                if (!_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer)
                    return Illegal("Base-resource completion requires every exact generation to be released.");
                CompleteRunningStage(KernelStage.BaseResources);
                return Commit();
            }
        }

        internal KernelTransition FailRunningStage(KernelReceipt stageReceipt, bool stable)
        {
            lock (_sync)
            {
                if (!_runningStage.HasValue ||
                    !MatchesRunningStage(_runningStage.Value, stageReceipt))
                    return Stale("Stage failure requires the exact stage reservation.");
                var outcome = stable
                    ? KernelAttemptOutcome.StableFailure
                    : KernelAttemptOutcome.RetryableFailure;
                var result = "dispose-" + (stable ? "stable" : "retryable") +
                             "-failure:" + _disposeAttemptId + ":" + _runningStage.Value;
                _disposeAttempts.Current.Complete(outcome, result);
                _life = stable ? KernelLife.FaultStable : KernelLife.FaultRetryable;
                _attempt = stable ? KernelAttemptState.StableFault : KernelAttemptState.RetryableFault;
                _admission = KernelAdmission.Closed;
                _disposeCapability = null;
                _runningStage = null;
                _runningStageReceipt = null;
                _disposeReceipt = null;
                _quiesceGeneration = 0;
                _quiescenceReceipt = null;
                return Commit(result);
            }
        }

        internal KernelTransition FinishDispose(KernelReceipt disposeReceipt)
        {
            lock (_sync)
            {
                if (!MatchesDisposeAttempt(disposeReceipt))
                    return Stale("Final disposal completion requires the exact disposal attempt.");
                if (_life != KernelLife.Disposing || _admission != KernelAdmission.Closed ||
                    _runningStage.HasValue || _completedStages.Count != 6 ||
                    _leases.Count != 0 || _callbacks.Count != 0 ||
                    !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer)
                    return Illegal("Disposal is not terminal-safe.");
                var result = "dispose-success:" + _disposeAttemptId;
                _disposeAttempts.Current.Complete(KernelAttemptOutcome.Success, result);
                _life = KernelLife.Disposed;
                _attempt = KernelAttemptState.Complete;
                _disposeCapability = null;
                _disposeReceipt = null;
                _wrapperRoot = false;
                _trackerRoot = false;
                _orphanRoot = false;
                _ticketRoot = false;
                _orphanRecord.Terminal = true;
                _orphanRegistry.RemoveTerminal(_orphanRecord);
                return Commit(result);
            }
        }

        internal KernelTransition BeginTransfer(
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (_transfer == KernelTransferState.Preparing)
                    return RunningAttemptConflict(
                        _transferCapability, currentAttempt, callout, "transfer preparation");
                if (_life != KernelLife.Active || _ownership != KernelOwnership.Owned ||
                    _orphanRegistry.Shutdown ||
                    _admission != KernelAdmission.Open || _publication != KernelPublication.Published ||
                    _native != KernelNativeAuthority.OwnedLive || _leases.Count != 0 || _callbacks.Count != 0 ||
                    !_handleSlot.EmptyForTransfer || !_memorySlot.EmptyForTransfer ||
                    _transfer != KernelTransferState.None || !_wrapperRoot || _trackerRoot || _orphanRoot)
                    return Illegal("Transfer requires an untracked OwnedKnownLive owner with empty slots and no admitted work.");
                _transferId++;
                _admission = KernelAdmission.Closed;
                _transfer = KernelTransferState.Preparing;
                _transferCapability = IssueAttemptCapability(
                    KernelAttemptKind.Transfer, new KernelIdentity(_transferId));
                _transferAttempts.Append(
                    new KernelIdentity(_transferId), _transferCapability.Token);
                _transferReceipt = new KernelReceipt(
                    "transfer-preparation", _owner, _lifecycle, new KernelIdentity(_transferId),
                    new KernelIdentity(_publicationGeneration));
                return Commit(new KernelAttemptReservation(_transferReceipt, _transferCapability));
            }
        }

        internal KernelTransition RequestTransfer(
            string waiterId,
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (_transfer == KernelTransferState.Preparing)
                    return JoinRunningAttempt(
                        _transferAttempts.Current,
                        _transferCapability,
                        currentAttempt,
                        callout,
                        waiterId,
                        KernelAttemptKind.Transfer,
                        "transfer preparation");
                if (_life == KernelLife.Transferred)
                    return Commit("transfer-terminal");
                return BeginTransfer(currentAttempt, callout);
            }
        }

        internal string ObserveTransferResult(long attemptId, string waiterId)
        {
            lock (_sync)
            {
                var record = attemptId <= 0 ? null : _transferAttempts.Find(new KernelIdentity(attemptId));
                return record?.Observe(waiterId);
            }
        }

        internal KernelTransition CommitTransfer(KernelReceipt preparationReceipt)
        {
            lock (_sync)
            {
                if (!MatchesTransferPreparation(preparationReceipt))
                    return Stale("Transfer commit requires the exact preparation receipt.");
                if (!TransferPreparationStateIsValid())
                    return Illegal("Transfer preparation state changed before commit.");
                _transferAttempts.Current.Complete(
                    KernelAttemptOutcome.Success,
                    "transfer-commit:" + _transferId);
                _life = KernelLife.Transferred;
                _publication = KernelPublication.Unpublished;
                _native = KernelNativeAuthority.TicketOwned;
                _transfer = KernelTransferState.Pending;
                _transferCapability = null;
                _transferReceipt = null;
                _ticketReceipt = new KernelReceipt(
                    "ticket", _owner, _lifecycle, new KernelIdentity(_transferId),
                    new KernelIdentity(_publicationGeneration));
                _wrapperRoot = false;
                _ticketRoot = true;
                return Commit(_ticketReceipt);
            }
        }

        internal KernelTransition FailTransferPreparation(KernelReceipt preparationReceipt)
        {
            lock (_sync)
            {
                if (!MatchesTransferPreparation(preparationReceipt))
                    return Stale("Transfer failure requires the exact preparation receipt.");
                if (!TransferPreparationStateIsValid())
                    return Illegal("Transfer preparation state changed before failure handling.");
                _transferAttempts.Current.Complete(
                    KernelAttemptOutcome.RetryableFailure,
                    "transfer-failure:" + _transferId);
                RestoreAfterTransferPreparation();
                return Commit();
            }
        }

        internal KernelTransition AbortTransfer(KernelReceipt preparationReceipt)
        {
            lock (_sync)
            {
                if (!MatchesTransferPreparation(preparationReceipt))
                    return Stale("Transfer abort requires the exact preparation receipt.");
                if (!TransferPreparationStateIsValid())
                    return Illegal("Transfer preparation state changed before abort.");
                _transferAttempts.Current.Complete(
                    KernelAttemptOutcome.RetryableFailure,
                    "transfer-abort:" + _transferId);
                RestoreAfterTransferPreparation();
                return Commit();
            }
        }

        internal KernelTransition EnterTicketUse(KernelReceipt ticketReceipt)
        {
            lock (_sync)
            {
                if (!MatchesTicket(ticketReceipt))
                    return Stale("Ticket-use admission requires the exact ticket.");
                if (_orphanRegistry.Shutdown)
                    return Illegal("Ticket-use admission is closed.");
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

        internal KernelTransition CommitTicket(KernelReceipt ticketReceipt)
        {
            lock (_sync)
            {
                if (!MatchesTicket(ticketReceipt))
                    return Stale("Ticket commit requires the exact ticket.");
                if (_transfer != KernelTransferState.Pending || _ticketUses.Count != 0)
                    return Illegal("Ticket commit requires Pending with zero exact uses.");
                _transfer = KernelTransferState.Committed;
                return Commit();
            }
        }

        internal KernelTransition CompleteTicket(KernelReceipt ticketReceipt)
        {
            lock (_sync)
            {
                if (!MatchesTicket(ticketReceipt))
                    return Stale("Ticket completion requires the exact ticket.");
                if (_transfer != KernelTransferState.Committed || _native != KernelNativeAuthority.TicketOwned)
                    return Illegal("Only a committed ticket can complete.");
                _transfer = KernelTransferState.Completed;
                _native = KernelNativeAuthority.Freed;
                _pointer = IntPtr.Zero;
                _ticketRoot = false;
                _terminalTicketReceipt = _ticketReceipt;
                _ticketReceipt = null;
                _orphanRoot = false;
                _orphanRecord.Terminal = true;
                _orphanRegistry.RemoveTerminal(_orphanRecord);
                return Commit();
            }
        }

        internal KernelTransition StartRollback(
            KernelReceipt ticketReceipt,
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (!MatchesTicket(ticketReceipt))
                    return Stale("Rollback requires the exact ticket.");
                if (_transfer == KernelTransferState.RollbackStable)
                    return KernelTransition.Reject(KernelTransitionCode.StableFailure, ExactRollbackResult());
                if (_transfer == KernelTransferState.RollbackDraining || _transfer == KernelTransferState.RollingBack)
                    return RunningAttemptConflict(
                        _rollbackCapability, currentAttempt, callout, "rollback");
                if (_transfer != KernelTransferState.Pending &&
                    _transfer != KernelTransferState.HandoffInUse &&
                    _transfer != KernelTransferState.RollbackRetryable)
                    return Illegal("Rollback cannot start from this ticket state.");
                _rollbackAttemptId++;
                _rollbackCapability = IssueAttemptCapability(
                    KernelAttemptKind.Rollback, new KernelIdentity(_rollbackAttemptId));
                _rollbackAttempts.Append(
                    new KernelIdentity(_rollbackAttemptId), _rollbackCapability.Token);
                _transfer = _ticketUses.Count == 0
                    ? KernelTransferState.RollingBack
                    : KernelTransferState.RollbackDraining;
                _rollbackReceipt = new KernelReceipt(
                    "rollback-attempt", _owner, _lifecycle, new KernelIdentity(_rollbackAttemptId),
                    new KernelIdentity(_transferId));
                return Commit(new KernelAttemptReservation(_rollbackReceipt, _rollbackCapability));
            }
        }

        internal KernelTransition JoinRollback(
            KernelReceipt ticketReceipt,
            string waiterId,
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (!MatchesTicket(ticketReceipt))
                    return Stale("Rollback join requires the exact ticket.");
                if (_transfer != KernelTransferState.RollbackDraining &&
                    _transfer != KernelTransferState.RollingBack)
                    return Illegal("There is no running rollback attempt to join.");
                return JoinRunningAttempt(
                    _rollbackAttempts.Current,
                    _rollbackCapability,
                    currentAttempt,
                    callout,
                    waiterId,
                    KernelAttemptKind.Rollback,
                    "rollback");
            }
        }

        internal KernelTransition RequestRollback(
            KernelReceipt ticketReceipt,
            string waiterId,
            KernelAttemptCapability currentAttempt,
            KernelCalloutCapability callout)
        {
            lock (_sync)
            {
                if (IsTerminalTicket(_transfer))
                    return MatchesTerminalTicket(ticketReceipt)
                        ? Commit(_transfer == KernelTransferState.RolledBack
                            ? ExactRollbackResult()
                            : "rollback-terminal")
                        : Stale("Terminal rollback replay requires the exact ticket identity.");
                if (!MatchesTicket(ticketReceipt))
                    return Stale("Rollback requires the exact ticket.");
                if (_transfer == KernelTransferState.RollbackStable)
                    return KernelTransition.Reject(
                        KernelTransitionCode.StableFailure,
                        ExactRollbackResult());
                if (_transfer == KernelTransferState.RollbackDraining ||
                    _transfer == KernelTransferState.RollingBack)
                    return JoinRunningAttempt(
                        _rollbackAttempts.Current,
                        _rollbackCapability,
                        currentAttempt,
                        callout,
                        waiterId,
                        KernelAttemptKind.Rollback,
                        "rollback");
                return StartRollback(ticketReceipt, currentAttempt, callout);
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

        internal KernelTransition FinishRollbackDrain(KernelReceipt rollbackReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRollbackAttempt(rollbackReceipt))
                    return Stale("Rollback drain completion requires the exact rollback attempt.");
                if (_transfer != KernelTransferState.RollbackDraining || _ticketUses.Count != 0)
                    return KernelTransition.Reject(KernelTransitionCode.PendingWork, "Ticket uses have not drained.");
                _transfer = KernelTransferState.RollingBack;
                return Commit();
            }
        }

        internal KernelTransition CompleteRollback(
            KernelReceipt rollbackReceipt,
            KernelAttemptOutcome outcome)
        {
            lock (_sync)
            {
                if (!MatchesRollbackAttempt(rollbackReceipt))
                    return Stale("Rollback completion requires the exact rollback attempt.");
                if (_transfer != KernelTransferState.RollingBack || outcome == KernelAttemptOutcome.Running)
                    return Illegal("Rollback completion requires the exact running attempt.");
                var result = "rollback-" + outcome + ":" + _rollbackAttemptId;
                _rollbackAttempts.Current.Complete(outcome, result);
                _rollbackCapability = null;
                _rollbackReceipt = null;
                if (outcome == KernelAttemptOutcome.Success)
                {
                    _transfer = KernelTransferState.RolledBack;
                    _native = KernelNativeAuthority.Freed;
                    _pointer = IntPtr.Zero;
                    _ticketRoot = false;
                    _terminalTicketReceipt = _ticketReceipt;
                    _ticketReceipt = null;
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
                    _orphanRegistry.Shutdown ||
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
                if (!_orphanRegistry.Publish(_orphanRecord, this))
                    return Illegal("The reserved orphan record could not be published.");
                if (_transfer == KernelTransferState.Preparing)
                    AbortTransferPreparation();
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
                if (!_orphanRegistry.Publish(_orphanRecord, this))
                    return Illegal("The reserved orphan record could not be published.");
                _ticketRoot = false;
                _orphanRoot = true;
                _finalizerSeen = true;
                return Commit();
            }
        }

        internal KernelOrphanRecoveryOutcome TryRecoverOrphan()
        {
            try
            {
                var snapshot = Snapshot();
                if (snapshot.Life == KernelLife.Disposed ||
                    snapshot.Transfer == KernelTransferState.Completed ||
                    snapshot.Transfer == KernelTransferState.RolledBack)
                    return KernelOrphanRecoveryOutcome.Recovered;
                if (snapshot.Life == KernelLife.FaultStable ||
                    snapshot.Transfer == KernelTransferState.RollbackStable ||
                    snapshot.NativeAuthority == KernelNativeAuthority.Unknown)
                    return KernelOrphanRecoveryOutcome.StableRetained;

                if (snapshot.Transfer != KernelTransferState.None)
                    return RecoverOrphanTicket(snapshot);

                KernelReceipt disposeReceipt;
                if (snapshot.Life == KernelLife.Disposing &&
                    snapshot.DisposalAttempt == KernelAttemptState.Running)
                {
                    lock (_sync)
                    {
                        disposeReceipt = _disposeReceipt;
                    }
                }
                else
                {
                    var start = StartDispose(null, null);
                    if (!start.Accepted)
                        return start.Code == KernelTransitionCode.StableFailure
                            ? KernelOrphanRecoveryOutcome.StableRetained
                            : KernelOrphanRecoveryOutcome.RetryableRetained;
                    disposeReceipt = ((KernelAttemptReservation)start.Value).Receipt;
                }
                if (disposeReceipt == null)
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                if (Snapshot().Admission == KernelAdmission.Closing &&
                    !CloseAdmission(disposeReceipt).Accepted)
                    return KernelOrphanRecoveryOutcome.RetryableRetained;

                if (!RecoverSimpleStage(KernelStage.Managed, disposeReceipt))
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                if (!RecoverQuiescence(disposeReceipt))
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                if (!RecoverSimpleStage(KernelStage.CallbackFence, disposeReceipt))
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                if (!StageComplete(KernelStage.NativeAuthority))
                {
                    var nativeStage = GetOrBeginStage(KernelStage.NativeAuthority, disposeReceipt);
                    KernelReceipt quiescenceReceipt;
                    lock (_sync)
                    {
                        quiescenceReceipt = _quiescenceReceipt;
                    }
                    if (nativeStage == null ||
                        !CommitNativeAuthority(nativeStage, quiescenceReceipt).Accepted)
                        return KernelOrphanRecoveryOutcome.RetryableRetained;
                }
                if (!RecoverSimpleStage(KernelStage.LegacyUnpublishNotification, disposeReceipt))
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                if (!RecoverBaseResources(disposeReceipt))
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                return FinishDispose(disposeReceipt).Accepted
                    ? KernelOrphanRecoveryOutcome.Recovered
                    : KernelOrphanRecoveryOutcome.RetryableRetained;
            }
            catch
            {
                return KernelOrphanRecoveryOutcome.RetryableRetained;
            }
        }

        internal KernelTransition PrepareDomainShutdown()
        {
            lock (_sync)
            {
                if (_life == KernelLife.Disposed || IsTerminalTicket(_transfer))
                    return KernelTransition.Accept("shutdown-terminal");
                if (_domainShutdownPrepared)
                    return Commit("shutdown-prepared");
                if (!_orphanRegistry.Publish(_orphanRecord, this))
                    return Illegal("Domain shutdown could not publish recovery authority.");
                if (_transfer == KernelTransferState.Preparing)
                    AbortTransferPreparation();
                _admission = KernelAdmission.Closed;
                _wrapperRoot = false;
                _trackerRoot = false;
                _ticketRoot = false;
                _orphanRoot = true;
                _domainShutdownPrepared = true;
                return Commit("shutdown-prepared");
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
                if ((_quiesceGeneration != 0) != (_quiescenceReceipt != null))
                    errors.Add("Quiescence generation and receipt must be retained together.");
                if (_quiescenceReceipt != null && !_quiescenceReceipt.Matches(
                        "quiescence", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                        new KernelIdentity(_publicationGeneration)))
                    errors.Add("The retained quiescence receipt must name the exact publication generation.");
                if ((_attempt == KernelAttemptState.Running) != (_disposeReceipt != null))
                    errors.Add("A running disposal attempt requires exactly one attempt receipt.");
                if (_disposeReceipt != null && !MatchesDisposeAttempt(_disposeReceipt))
                    errors.Add("The disposal attempt receipt does not match the running attempt.");
                if (_life == KernelLife.Active && _attempt != KernelAttemptState.Idle)
                    errors.Add("Active requires an idle disposal attempt.");
                if (_life == KernelLife.Disposing &&
                    (_attempt != KernelAttemptState.Running || _admission == KernelAdmission.Open ||
                     !MatchesAttemptCapability(
                         _disposeCapability, KernelAttemptKind.Disposal, _disposeAttemptId)))
                    errors.Add("Disposing requires one causal running attempt and closing admission.");
                if (_life == KernelLife.FaultRetryable &&
                    (_attempt != KernelAttemptState.RetryableFault || _admission == KernelAdmission.Open))
                    errors.Add("Retryable fault requires its exact completed attempt.");
                if (_life == KernelLife.FaultStable &&
                    (_attempt != KernelAttemptState.StableFault || _admission == KernelAdmission.Open))
                    errors.Add("Stable fault requires its exact completed attempt.");
                if (_attempt != KernelAttemptState.Running && _disposeCapability != null)
                    errors.Add("Only a running disposal attempt may retain a causal token.");
                if ((_attempt == KernelAttemptState.Running) !=
                    (_disposeAttempts.Current != null && !_disposeAttempts.Current.IsComplete))
                    errors.Add("The disposal ledger must retain exactly the running attempt.");
                if (_disposeAttempts.Current != null &&
                    _disposeAttempts.Current.Id.Value != _disposeAttemptId)
                    errors.Add("The disposal ledger head must match the current attempt identity.");
                if (_runningStage.HasValue &&
                    (_life != KernelLife.Disposing || _attempt != KernelAttemptState.Running ||
                     _completedStages.Contains(_runningStage.Value) || _runningStageReceipt == null ||
                     !_runningStageReceipt.MatchesStage(
                         _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                         _runningStageReceipt.Generation, _runningStage.Value)))
                    errors.Add("A running stage must be incomplete and owned by the current disposal attempt.");
                if (!_runningStage.HasValue && _runningStageReceipt != null)
                    errors.Add("An idle stage cannot retain a stage reservation.");
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
                    (_ticketUses.Count != 0 || _rollbackCapability != null || _ticketRoot))
                    errors.Add("No-transfer state retains ticket state.");
                if (_transfer == KernelTransferState.Preparing &&
                    (_life != KernelLife.Active || _ticketRoot || _ticketUses.Count != 0 ||
                     !MatchesAttemptCapability(
                         _transferCapability, KernelAttemptKind.Transfer, _transferId) ||
                     _transferReceipt == null ||
                     !MatchesTransferPreparation(_transferReceipt)))
                    errors.Add("Preparing transfer must remain an active wrapper reservation.");
                if ((_transfer == KernelTransferState.Preparing) !=
                    (_transferAttempts.Current != null && !_transferAttempts.Current.IsComplete))
                    errors.Add("The transfer ledger must retain exactly the Preparing attempt.");
                if (_transferAttempts.Current != null &&
                    _transferAttempts.Current.Id.Value != _transferId)
                    errors.Add("The transfer ledger head must match the current attempt identity.");
                if (_transfer != KernelTransferState.Preparing &&
                    (_transferCapability != null || _transferReceipt != null))
                    errors.Add("Only transfer preparation may retain its causal reservation.");
                if (IsLiveTicket(_transfer) && _ticketReceipt == null)
                    errors.Add("A live ticket requires its exact ticket receipt.");
                if (!IsLiveTicket(_transfer) && _ticketReceipt != null)
                    errors.Add("A non-live ticket cannot retain a ticket receipt.");
                if (IsLiveTicket(_transfer) && !ValidLiveTicketRoots())
                    errors.Add("Live ticket requires exactly orphan-only or ticket-only recovery root.");
                if (IsTerminalTicket(_transfer) && (_wrapperRoot || _trackerRoot || _orphanRoot || _ticketRoot))
                    errors.Add("Terminal ticket requires zero recovery roots.");
                if (IsTerminalTicket(_transfer) != (_terminalTicketReceipt != null))
                    errors.Add("Terminal ticket state must retain only its immutable authentication receipt.");
                if (_transfer == KernelTransferState.HandoffInUse && _ticketUses.Count == 0)
                    errors.Add("HandoffInUse requires at least one exact ticket-use token.");
                if (_ticketUses.Count != 0 && _transfer != KernelTransferState.HandoffInUse &&
                    _transfer != KernelTransferState.RollbackDraining)
                    errors.Add("Ticket-use tokens exist outside handoff or rollback draining.");
                if (_transfer == KernelTransferState.RollingBack && _ticketUses.Count != 0)
                    errors.Add("Native rollback cannot run while ticket uses remain.");
                if ((_transfer == KernelTransferState.RollbackDraining ||
                     _transfer == KernelTransferState.RollingBack) !=
                    (MatchesAttemptCapability(
                         _rollbackCapability, KernelAttemptKind.Rollback, _rollbackAttemptId) &&
                     _rollbackReceipt != null))
                    errors.Add("Rollback causal token does not match the running rollback attempt.");
                if (_rollbackReceipt != null && !MatchesRollbackAttempt(_rollbackReceipt))
                    errors.Add("The rollback attempt receipt does not match the running attempt.");
                if (_transfer != KernelTransferState.RollbackDraining &&
                    _transfer != KernelTransferState.RollingBack &&
                    (_rollbackCapability != null || _rollbackReceipt != null))
                    errors.Add("Only a running rollback attempt may retain its causal reservation.");
                if ((_transfer == KernelTransferState.RollbackDraining ||
                     _transfer == KernelTransferState.RollingBack) !=
                    (_rollbackAttempts.Current != null && !_rollbackAttempts.Current.IsComplete))
                    errors.Add("The rollback ledger must retain exactly the running attempt.");
                if (_rollbackAttempts.Current != null &&
                    _rollbackAttempts.Current.Id.Value != _rollbackAttemptId)
                    errors.Add("The rollback ledger head must match the current attempt identity.");
                if (_orphanRoot && (_wrapperRoot || _trackerRoot || _ticketRoot))
                    errors.Add("Orphan recovery must be the sole root.");
                if (_finalizerSeen && _wrapperRoot)
                    errors.Add("Finalizer-observed authority cannot retain ordinary wrapper reachability.");
                if (_finalizerSeen && _life != KernelLife.Disposed && !IsTerminalTicket(_transfer) && !_orphanRoot)
                    errors.Add("Finalizer-observed nonterminal authority requires an orphan root.");
                if (_domainShutdownPrepared && _life != KernelLife.Disposed &&
                    !IsTerminalTicket(_transfer) &&
                    (_admission != KernelAdmission.Closed || !_orphanRecord.Published ||
                     !ReferenceEquals(_orphanRecord.RecoveryOwner, this) || !_orphanRoot ||
                     _wrapperRoot || _trackerRoot || _ticketRoot))
                    errors.Add("Domain shutdown requires closed admission and one strong orphan recovery root.");
                return errors.ToArray();
            }
        }

        private KernelTransition CompleteSimpleStage(KernelStage stage, KernelReceipt stageReceipt)
        {
            lock (_sync)
            {
                if (!MatchesRunningStage(stage, stageReceipt))
                    return Stale("Stage completion requires the exact stage reservation.");
                CompleteRunningStage(stage);
                return Commit();
            }
        }

        private bool MatchesRunningStage(KernelStage stage, KernelReceipt receipt)
        {
            return _life == KernelLife.Disposing && _attempt == KernelAttemptState.Running &&
                   _runningStage == stage && _runningStageReceipt != null && receipt != null &&
                   receipt.MatchesStage(
                       _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                       _runningStageReceipt.Generation, stage);
        }

        private void CompleteRunningStage(KernelStage stage)
        {
            _runningStage = null;
            _runningStageReceipt = null;
            _completedStages.Add(stage);
        }

        private bool MatchesDisposeAttempt(KernelReceipt receipt)
        {
            return _attempt == KernelAttemptState.Running && _disposeReceipt != null && receipt != null &&
                   receipt.Matches(
                       "dispose-attempt", _owner, _lifecycle, new KernelIdentity(_disposeAttemptId),
                       _disposeReceipt.Generation);
        }

        private bool MatchesAttemptCapability(
            KernelAttemptCapability capability,
            KernelAttemptKind kind,
            long attemptId)
        {
            return capability != null && attemptId > 0 && capability.Matches(
                _owner, new KernelIdentity(attemptId), kind, _capabilitySeal);
        }

        private bool MatchesTransferPreparation(KernelReceipt receipt)
        {
            return _transfer == KernelTransferState.Preparing && _transferReceipt != null && receipt != null &&
                   receipt.Matches(
                       "transfer-preparation", _owner, _lifecycle, new KernelIdentity(_transferId),
                       new KernelIdentity(_publicationGeneration));
        }

        private bool TransferPreparationStateIsValid()
        {
            return _life == KernelLife.Active && _ownership == KernelOwnership.Owned &&
                   _admission == KernelAdmission.Closed && _publication == KernelPublication.Published &&
                   _native == KernelNativeAuthority.OwnedLive && _leases.Count == 0 &&
                   _callbacks.Count == 0 && _handleSlot.EmptyForTransfer &&
                   _memorySlot.EmptyForTransfer && _transfer == KernelTransferState.Preparing &&
                   _wrapperRoot && !_trackerRoot && !_orphanRoot && !_ticketRoot;
        }

        private bool MatchesTicket(KernelReceipt receipt)
        {
            return _ticketReceipt != null && receipt != null && receipt.Matches(
                "ticket", _owner, _lifecycle, new KernelIdentity(_transferId),
                new KernelIdentity(_publicationGeneration));
        }

        private bool MatchesTerminalTicket(KernelReceipt receipt)
        {
            return _terminalTicketReceipt != null && receipt != null && receipt.Matches(
                "ticket", _owner, _lifecycle, new KernelIdentity(_transferId),
                new KernelIdentity(_publicationGeneration));
        }

        private bool MatchesRollbackAttempt(KernelReceipt receipt)
        {
            return (_transfer == KernelTransferState.RollbackDraining ||
                    _transfer == KernelTransferState.RollingBack) &&
                   _rollbackReceipt != null && receipt != null && receipt.Matches(
                       "rollback-attempt", _owner, _lifecycle, new KernelIdentity(_rollbackAttemptId),
                       new KernelIdentity(_transferId));
        }

        private void RestoreAfterTransferPreparation()
        {
            _transfer = KernelTransferState.None;
            _admission = KernelAdmission.Open;
            _transferCapability = null;
            _transferReceipt = null;
        }

        private void AbortTransferPreparation()
        {
            if (_transfer != KernelTransferState.Preparing)
                return;
            _transferAttempts.Current.Complete(
                KernelAttemptOutcome.RetryableFailure,
                "transfer-abort:" + _transferId);
            RestoreAfterTransferPreparation();
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

        private KernelOrphanRecoveryOutcome RecoverOrphanTicket(LifecycleKernelSnapshot snapshot)
        {
            KernelReceipt ticket;
            KernelReceipt rollback;
            lock (_sync)
            {
                ticket = _ticketReceipt;
                rollback = _rollbackReceipt;
            }
            if (ticket == null)
                return KernelOrphanRecoveryOutcome.StableRetained;
            if (snapshot.Transfer == KernelTransferState.Committed)
                return CompleteTicket(ticket).Accepted
                    ? KernelOrphanRecoveryOutcome.Recovered
                    : KernelOrphanRecoveryOutcome.RetryableRetained;
            if (snapshot.Transfer == KernelTransferState.RollbackDraining)
            {
                if (rollback == null || !FinishRollbackDrain(rollback).Accepted)
                    return KernelOrphanRecoveryOutcome.RetryableRetained;
                snapshot = Snapshot();
            }
            if (snapshot.Transfer == KernelTransferState.RollingBack)
                return rollback != null &&
                       CompleteRollback(rollback, KernelAttemptOutcome.Success).Accepted
                    ? KernelOrphanRecoveryOutcome.Recovered
                    : KernelOrphanRecoveryOutcome.RetryableRetained;
            if (snapshot.Transfer != KernelTransferState.Pending &&
                snapshot.Transfer != KernelTransferState.HandoffInUse &&
                snapshot.Transfer != KernelTransferState.RollbackRetryable)
                return KernelOrphanRecoveryOutcome.RetryableRetained;
            var start = StartRollback(ticket, null, null);
            if (!start.Accepted)
                return start.Code == KernelTransitionCode.StableFailure
                    ? KernelOrphanRecoveryOutcome.StableRetained
                    : KernelOrphanRecoveryOutcome.RetryableRetained;
            var startedRollback = ((KernelAttemptReservation)start.Value).Receipt;
            return CompleteRollback(startedRollback, KernelAttemptOutcome.Success).Accepted
                ? KernelOrphanRecoveryOutcome.Recovered
                : KernelOrphanRecoveryOutcome.RetryableRetained;
        }

        private bool RecoverSimpleStage(KernelStage stage, KernelReceipt disposeReceipt)
        {
            if (StageComplete(stage))
                return true;
            var stageReceipt = GetOrBeginStage(stage, disposeReceipt);
            if (stageReceipt == null)
                return false;
            switch (stage)
            {
                case KernelStage.Managed:
                    return CompleteManaged(stageReceipt).Accepted;
                case KernelStage.CallbackFence:
                    return CompleteCallbackFence(stageReceipt).Accepted;
                case KernelStage.LegacyUnpublishNotification:
                    return CompleteLegacyNotification(stageReceipt).Accepted;
                default:
                    return false;
            }
        }

        private bool RecoverBaseResources(KernelReceipt disposeReceipt)
        {
            if (StageComplete(KernelStage.BaseResources))
                return true;
            var stageReceipt = GetOrBeginStage(KernelStage.BaseResources, disposeReceipt);
            if (stageReceipt == null)
                return false;
            if (!RecoverResourceSlot(KernelResourceKind.GCHandle, stageReceipt) ||
                !RecoverResourceSlot(KernelResourceKind.UnmanagedMemory, stageReceipt))
                return false;
            return CompleteBaseResources(stageReceipt).Accepted;
        }

        private bool RecoverResourceSlot(KernelResourceKind kind, KernelReceipt stageReceipt)
        {
            var slot = Slot(kind);
            if (slot.HasReservation)
                return false;
            if (slot.HasLive)
            {
                var detached = DetachResource(kind, stageReceipt);
                if (!detached.Accepted)
                    return false;
            }
            var generations = slot.DetachedGenerations();
            for (var index = 0; index < generations.Length; index++)
                if (!ReleaseResource(kind, generations[index]).Accepted)
                    return false;
            return true;
        }

        private bool RecoverQuiescence(KernelReceipt disposeReceipt)
        {
            if (StageComplete(KernelStage.NativeQuiesce))
                return true;
            var stageReceipt = GetOrBeginStage(KernelStage.NativeQuiesce, disposeReceipt);
            return stageReceipt != null && RecordQuiescence(stageReceipt).Accepted;
        }

        private KernelReceipt GetOrBeginStage(KernelStage stage, KernelReceipt disposeReceipt)
        {
            lock (_sync)
            {
                if (_runningStage == stage && MatchesRunningStage(stage, _runningStageReceipt))
                    return _runningStageReceipt;
                if (_runningStage.HasValue)
                    return null;
                var reservation = BeginStage(stage, disposeReceipt);
                return reservation.Accepted ? (KernelReceipt)reservation.Value : null;
            }
        }

        private bool StageComplete(KernelStage stage)
        {
            lock (_sync)
            {
                return _completedStages.Contains(stage);
            }
        }

        private KernelAttemptCapability IssueAttemptCapability(
            KernelAttemptKind kind,
            KernelIdentity attempt)
        {
            var token = new KernelCausalToken(
                kind + ":" + _owner.Value + ":" + attempt.Value + ":" + NextIdentity().Value);
            return new KernelAttemptCapability(_owner, attempt, kind, token, _capabilitySeal);
        }

        private static KernelTransition RunningAttemptConflict(
            KernelAttemptCapability running,
            KernelAttemptCapability current,
            KernelCalloutCapability callout,
            string name)
        {
            if (ReferenceEquals(running, current))
                return KernelTransition.Reject(
                    KernelTransitionCode.SameCausalAttempt,
                    "Same-causal " + name + " reentry is forbidden.");
            if (callout != null)
                return KernelTransition.Reject(
                    KernelTransitionCode.CycleRisk,
                    "A callout cannot wait on a foreign " + name + ".");
            return KernelTransition.Reject(
                KernelTransitionCode.PendingWork,
                "A foreign " + name + " is already running.");
        }

        private KernelTransition JoinRunningAttempt(
            KernelAttemptRecord record,
            KernelAttemptCapability running,
            KernelAttemptCapability current,
            KernelCalloutCapability callout,
            string waiterId,
            KernelAttemptKind kind,
            string name)
        {
            if (record == null || record.IsComplete ||
                !MatchesAttemptCapability(running, kind, record.Id.Value))
                return Illegal("The exact " + name + " attempt is unavailable.");
            if (ReferenceEquals(running, current))
                return KernelTransition.Reject(
                    KernelTransitionCode.SameCausalAttempt,
                    "Same-causal " + name + " join is forbidden.");
            if (callout != null)
                return KernelTransition.Reject(
                    KernelTransitionCode.CycleRisk,
                    "A callout cannot wait on a foreign " + name + ".");
            if (!record.RegisterWaiter(waiterId))
                return Illegal("The waiter identity must be nonempty and unique.");
            return Commit(new KernelJoinReservation(kind, record.Id, waiterId));
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

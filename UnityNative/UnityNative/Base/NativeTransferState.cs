namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// The arbitration state of one transfer ticket.
    /// </summary>
    public enum NativeTransferState
    {
        /// <summary>The ticket exclusively owns a pointer and accepts commit, rollback, or completion.</summary>
        Pending = 0,

        /// <summary>Ownership was committed to the receiving native system and awaits completion.</summary>
        Committed = 1,

        /// <summary>One managed caller is executing rollback cleanup.</summary>
        RollingBack = 2,

        /// <summary>Rollback failed; another attempt is allowed only when liveness remains known-live.</summary>
        RollbackFaulted = 3,

        /// <summary>Rollback destroyed the native resource.</summary>
        RolledBack = 4,

        /// <summary>The receiving native system reported completion for this ticket.</summary>
        Completed = 5,
    }
}

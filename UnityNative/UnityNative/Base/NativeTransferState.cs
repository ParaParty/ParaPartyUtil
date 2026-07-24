namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// The arbitration state of one transfer ticket.
    /// </summary>
    public enum NativeTransferState
    {
        Pending = 0,
        Committed = 1,
        RollingBack = 2,
        RollbackFaulted = 3,
        RolledBack = 4,
        Completed = 5,
    }
}

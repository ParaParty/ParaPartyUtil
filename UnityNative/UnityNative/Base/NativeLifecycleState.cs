namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// The externally observable lifecycle of a native wrapper.
    /// </summary>
    public enum NativeLifecycleState
    {
        Active = 0,
        Disposing = 1,
        DisposeFaulted = 2,
        Disposed = 3,
        Transferred = 4,
    }
}

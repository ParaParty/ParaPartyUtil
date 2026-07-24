namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// The externally observable lifecycle of a native wrapper.
    /// </summary>
    public enum NativeLifecycleState
    {
        /// <summary>The wrapper accepts operations and may enter disposal or transfer.</summary>
        Active = 0,

        /// <summary>The wrapper has closed operation admission and is executing one cleanup attempt.</summary>
        Disposing = 1,

        /// <summary>Cleanup is incomplete; only a proven retryable stage may run in a later attempt.</summary>
        DisposeFaulted = 2,

        /// <summary>Every required cleanup stage completed successfully.</summary>
        Disposed = 3,

        /// <summary>Native ownership moved to an exclusive transfer ticket.</summary>
        Transferred = 4,
    }
}

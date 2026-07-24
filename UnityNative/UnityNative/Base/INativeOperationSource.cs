namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Provides lease-based admission to a native wrapper.
    /// </summary>
    public interface INativeOperationSource
    {
        /// <summary>Gets the stable, positive owner identity used for deterministic group ordering.</summary>
        long NativeOperationOwnerId { get; }

        /// <summary>Acquires operation admission and a stable pointer for one complete native call interval.</summary>
        /// <returns>A lease that must remain alive until the native operation returns.</returns>
        /// <exception cref="System.ObjectDisposedException">Operation admission is closed.</exception>
        NativeOperationLease EnterOperation();
    }
}

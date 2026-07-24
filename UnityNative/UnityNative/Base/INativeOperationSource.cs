namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Provides lease-based admission to a native wrapper.
    /// </summary>
    public interface INativeOperationSource
    {
        long NativeOperationOwnerId { get; }

        NativeOperationLease EnterOperation();
    }
}

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Describes whether a wrapper must destroy its native resource.
    /// </summary>
    public enum NativeOwnershipKind
    {
        /// <summary>The wrapper must destroy the native resource during cleanup.</summary>
        Owned = 0,

        /// <summary>The wrapper invalidates itself without destroying the externally owned native resource.</summary>
        Borrowed = 1,
    }
}

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Describes whether a wrapper must destroy its native resource.
    /// </summary>
    public enum NativeOwnershipKind
    {
        Owned = 0,
        Borrowed = 1,
    }
}

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Evidence about whether the native resource remains live after cleanup.
    /// </summary>
    public enum NativeResourceLiveness
    {
        KnownLive = 0,
        Freed = 1,
        Unknown = 2,
    }
}

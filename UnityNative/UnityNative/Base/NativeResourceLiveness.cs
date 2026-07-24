namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Evidence about whether the native resource remains live after cleanup.
    /// </summary>
    public enum NativeResourceLiveness
    {
        /// <summary>The cleanup oracle proves that the native resource still exists.</summary>
        KnownLive = 0,

        /// <summary>The cleanup oracle proves that the native resource was destroyed.</summary>
        Freed = 1,

        /// <summary>Cleanup may have crossed an irreversible boundary, so liveness cannot be proven.</summary>
        Unknown = 2,
    }
}

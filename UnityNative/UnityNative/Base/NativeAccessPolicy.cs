namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Defines whether native operations may enter from several managed threads.
    /// </summary>
    public enum NativeAccessPolicy
    {
        /// <summary>Operations and explicit disposal may enter from any managed thread.</summary>
        Concurrent = 0,

        /// <summary>Operations and explicit disposal must run on the wrapper's creating thread.</summary>
        CreatingThreadConfined = 1,
    }
}

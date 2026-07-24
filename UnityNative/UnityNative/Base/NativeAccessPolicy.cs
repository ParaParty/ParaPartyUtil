namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Defines whether native operations may enter from several managed threads.
    /// </summary>
    public enum NativeAccessPolicy
    {
        Concurrent = 0,
        CreatingThreadConfined = 1,
    }
}

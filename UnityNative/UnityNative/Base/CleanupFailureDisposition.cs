namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Whether a failed cleanup stage may safely be attempted again.
    /// </summary>
    public enum CleanupFailureDisposition
    {
        None = 0,
        Retryable = 1,
        NonRetryable = 2,
    }
}

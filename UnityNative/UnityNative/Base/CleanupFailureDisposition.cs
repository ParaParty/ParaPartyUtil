namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Whether a failed cleanup stage may safely be attempted again.
    /// </summary>
    public enum CleanupFailureDisposition
    {
        /// <summary>The operation completed successfully and has no failure disposition.</summary>
        None = 0,

        /// <summary>The operation failed before irreversible cleanup and may be attempted again.</summary>
        Retryable = 1,

        /// <summary>The operation failed without proof that another attempt is safe.</summary>
        NonRetryable = 2,
    }
}

using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Receives cleanup diagnostics without allowing a sink failure to escape cleanup.
    /// </summary>
    public static class NativeCleanupDiagnostics
    {
        private static Action<NativeCleanupDiagnostic> _sink;

        public static Action<NativeCleanupDiagnostic> Sink
        {
            get => Volatile.Read(ref _sink);
            set => Volatile.Write(ref _sink, value);
        }

        internal static void ReportNoThrow(NativeCleanupDiagnostic diagnostic)
        {
            try
            {
                Action<NativeCleanupDiagnostic> sink = Sink;
                sink?.Invoke(diagnostic);
            }
            catch
            {
                // Diagnostics must never change cleanup or finalizer control flow.
            }
        }
    }
}

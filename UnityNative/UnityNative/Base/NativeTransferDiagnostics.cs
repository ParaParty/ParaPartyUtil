using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Receives transfer-ticket diagnostics without allowing a sink failure to escape finalization.
    /// </summary>
    public static class NativeTransferDiagnostics
    {
        private static Action<NativeTransferDiagnostic> _sink;

        /// <summary>
        /// Gets or sets the process-wide diagnostic receiver. Receiver exceptions are ignored so
        /// telemetry cannot change transfer or finalizer control flow.
        /// </summary>
        public static Action<NativeTransferDiagnostic> Sink
        {
            get => Volatile.Read(ref _sink);
            set => Volatile.Write(ref _sink, value);
        }

        internal static void ReportNoThrow(NativeTransferDiagnostic diagnostic)
        {
            try
            {
                Action<NativeTransferDiagnostic> sink = Sink;
                sink?.Invoke(diagnostic);
            }
            catch
            {
                // Diagnostics must never change transfer or finalizer control flow.
            }
        }
    }
}

using System;
using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Marks one native-to-managed callback as causally executing for its wrapper owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A native callback can arrive on a thread without managed <see cref="ExecutionContext"/> flow and
    /// without the operation lease held by the native caller. A derived wrapper enters this scope before
    /// invoking any callback consumer so disposal from that callback fails before lifecycle arbitration
    /// instead of waiting for the caller's lease.
    /// </para>
    /// <para>
    /// This scope grants no native-pointer access and is not operation admission, callback registration, or
    /// a callback-drain fence. The wrapper remains responsible for rejecting late callbacks and proving that
    /// every admitted callback has left before native destruction.
    /// </para>
    /// </remarks>
    public sealed class NativeCallbackExecutionScope : IDisposable
    {
        private NativeOperationExecutionMarker _executionMarker;

        internal NativeCallbackExecutionScope(long ownerId, long lifecycleEpoch)
        {
            var marker = new NativeOperationExecutionMarker(ownerId, lifecycleEpoch, 0);
            _executionMarker = marker;

            try
            {
                NativeOperationExecutionContext.Enter(marker);
            }
            catch
            {
                marker.Deactivate();
                _executionMarker = null;
                throw;
            }
        }

        /// <summary>Deactivates this callback marker exactly once without releasing an operation lease.</summary>
        public void Dispose()
        {
            NativeOperationExecutionMarker marker = Interlocked.Exchange(ref _executionMarker, null);
            if (marker == null)
                return;

            NativeOperationExecutionContext.Exit(marker);
            GC.SuppressFinalize(this);
        }

        /// <summary>Deactivates an abandoned marker without allowing an exception to escape finalization.</summary>
        ~NativeCallbackExecutionScope()
        {
            try
            {
                Dispose();
            }
            catch
            {
                // An abandoned callback marker must not throw from the finalizer thread.
            }
        }
    }
}

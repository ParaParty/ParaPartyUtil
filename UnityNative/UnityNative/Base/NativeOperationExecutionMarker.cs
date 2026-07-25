using System.Threading;

namespace Paraparty.UnityNative.Base
{
    internal sealed class NativeOperationExecutionMarker
    {
        private int _isActive = 1;

        public NativeOperationExecutionMarker(
            long ownerId,
            long lifecycleEpoch,
            long leaseToken,
            NativeOperationExecutionKind kind)
        {
            OwnerId = ownerId;
            LifecycleEpoch = lifecycleEpoch;
            LeaseToken = leaseToken;
            Kind = kind;
        }

        public long OwnerId { get; }

        public long LifecycleEpoch { get; }

        public long LeaseToken { get; }

        public NativeOperationExecutionKind Kind { get; }

        public bool IsActive => Volatile.Read(ref _isActive) != 0;

        public void Deactivate()
        {
            Interlocked.Exchange(ref _isActive, 0);
        }
    }
}

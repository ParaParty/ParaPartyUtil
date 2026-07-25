using System.Threading;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Authorizes pointer resolution only during one native-quiesce hook invocation.
    /// </summary>
    /// <remarks>
    /// The token is created and revoked by <see cref="DisposableNativeObject"/>. It never exposes the
    /// native pointer directly and cannot be reused by a later cleanup attempt or pointer publication.
    /// </remarks>
    public sealed class NativeQuiesceToken
    {
        private int _isActive;

        internal NativeQuiesceToken(
            DisposableNativeObject owner,
            long ownerId,
            long lifecycleEpoch,
            long pointerPublicationGeneration,
            long quiesceAttemptGeneration)
        {
            Owner = owner;
            OwnerId = ownerId;
            LifecycleEpoch = lifecycleEpoch;
            PointerPublicationGeneration = pointerPublicationGeneration;
            QuiesceAttemptGeneration = quiesceAttemptGeneration;
            _isActive = 1;
        }

        /// <summary>Gets the monotonic identity of the wrapper that issued this token.</summary>
        public long OwnerId { get; }

        /// <summary>Gets the lifecycle epoch in which this quiesce attempt started.</summary>
        public long LifecycleEpoch { get; }

        /// <summary>Gets the identity of the pointer publication observed by this attempt.</summary>
        public long PointerPublicationGeneration { get; }

        /// <summary>Gets the wrapper-local monotonic identity of this quiesce hook invocation.</summary>
        public long QuiesceAttemptGeneration { get; }

        /// <summary>Gets whether the issuing wrapper still accepts this token.</summary>
        public bool IsActive => Volatile.Read(ref _isActive) != 0;

        internal DisposableNativeObject Owner { get; }

        internal void Revoke()
        {
            Interlocked.Exchange(ref _isActive, 0);
        }
    }
}

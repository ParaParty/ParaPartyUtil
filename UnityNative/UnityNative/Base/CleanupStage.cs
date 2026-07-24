using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Independently completed cleanup stages. These are not lifecycle states.
    /// </summary>
    [Flags]
    public enum CleanupStage
    {
        /// <summary>No cleanup stage.</summary>
        None = 0,

        /// <summary>Runs wrapper-specific cleanup for managed registrations and managed-owned state.</summary>
        Managed = 1 << 0,

        /// <summary>Stops callbacks and registrations from reaching the native owner.</summary>
        CallbackFence = 1 << 1,

        /// <summary>Destroys or invalidates the native resource and releases base-owned unmanaged state.</summary>
        Native = 1 << 2,

        /// <summary>Removes the native pointer from the managed owner after native cleanup.</summary>
        OwnerUnpublish = 1 << 3,

        /// <summary>All stages required for a normally disposed wrapper.</summary>
        All = Managed | CallbackFence | Native | OwnerUnpublish,
    }
}

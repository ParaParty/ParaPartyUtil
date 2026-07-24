using System;

namespace Paraparty.UnityNative.Base
{
    /// <summary>
    /// Independently completed cleanup stages. These are not lifecycle states.
    /// </summary>
    [Flags]
    public enum CleanupStage
    {
        None = 0,
        Managed = 1 << 0,
        CallbackFence = 1 << 1,
        Native = 1 << 2,
        OwnerUnpublish = 1 << 3,
        All = Managed | CallbackFence | Native | OwnerUnpublish,
    }
}

#if PARAPARTYUTIL_NOUNITY
using System.Drawing;
#else
using UnityEngine;
#endif

namespace Paraparty.Colors
{
    internal static class Constance
    {
        internal static Color White =
#if PARAPARTYUTIL_NOUNITY
            Color.White;
#else
            Color.white;
#endif
    }
}

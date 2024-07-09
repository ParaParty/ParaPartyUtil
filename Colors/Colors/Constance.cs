#if !UNITY_2022_1_OR_NEWER
using System.Drawing;
#else
using UnityEngine;
#endif

namespace Paraparty.Colors
{
    internal static class Constance
    {
        internal static Color White =
#if !UNITY_2022_1_OR_NEWER
            Color.White;
#else
            Color.white;
#endif
    }
}

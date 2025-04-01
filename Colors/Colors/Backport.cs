using System;
using System.Runtime.CompilerServices;

namespace Paraparty.Colors
{
    internal static class Backport
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double MathClamp(double value, double min, double max)
        {
#if UNITY_2020_1_OR_NEWER && !UNITY_2022_1_OR_NEWER
            if (value > max)
            {
                return max;
            }
            
            return value < min ? min : value;
#else
            return Math.Clamp(value, min, max);
#endif
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double MathCbrt(double d)
        {
#if UNITY_2020_1_OR_NEWER && !UNITY_2022_1_OR_NEWER
            return d < 0 ? -Math.Pow(-d, 1.0 / 3.0) : Math.Pow(d, 1.0 / 3.0);
#else
            return Math.Cbrt(d);
#endif
        }
    }
}

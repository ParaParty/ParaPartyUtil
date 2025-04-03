using System;
using System.Runtime.CompilerServices;

namespace Paraparty.UnityPolyfill
{
    public class MathPolyfill
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
        {
#if (UNITY_2020_1_OR_NEWER && !UNITY_2022_1_OR_NEWER) || NETSTANDARD20
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
        public static double Cbrt(double d)
        {
#if (UNITY_2020_1_OR_NEWER && !UNITY_2022_1_OR_NEWER) || NETSTANDARD20
            return d < 0 ? -Math.Pow(-d, 1.0 / 3.0) : Math.Pow(d, 1.0 / 3.0);
#else
            return Math.Cbrt(d);
#endif
        }
    }
}

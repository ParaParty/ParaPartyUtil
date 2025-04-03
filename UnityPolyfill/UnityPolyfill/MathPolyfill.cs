using System;
using System.Runtime.CompilerServices;

namespace Paraparty.UnityPolyfill
{
    public class MathPolyfill
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double min, double max)
        {
#if !NET_STANDARD_2_1
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
#if !NET_STANDARD_2_1
            return d < 0 ? -Math.Pow(-d, 1.0 / 3.0) : Math.Pow(d, 1.0 / 3.0);
#else
            return Math.Cbrt(d);
#endif
        }
    }
}

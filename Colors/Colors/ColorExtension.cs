using System.Text;
#if !UNITY_2020_1_OR_NEWER
using System.Drawing;
#else
using UnityEngine;
#endif

namespace Paraparty.Colors
{
    public static class ColorExtension
    {
        public static string SerializeColor(this Color color)
        {
#if !UNITY_2020_1_OR_NEWER
            int r = color.R;
            int g = color.G;
            int b = color.B;
            int a = color.A;
#else
            int r = (int)(color.r * 255);
            int g = (int)(color.g * 255);
            int b = (int)(color.b * 255);
            int a = (int)(color.a * 255);
#endif

            var sb = new StringBuilder();
            sb.Append("#");

            sb.Append(r.ToString("X2"));
            sb.Append(g.ToString("X2"));
            sb.Append(b.ToString("X2"));
            if (a < 255)
            {
                sb.Append(a.ToString("X2"));
            }

            return sb.ToString();
        }

        public static string SerializeColor(this Oklab color)
        {
            return color.ToString();
        }

        public static string SerializeColor(this Oklch color)
        {
            return color.ToString();
        }
    }
}

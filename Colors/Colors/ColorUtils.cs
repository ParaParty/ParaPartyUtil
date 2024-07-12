#if !UNITY_2022_1_OR_NEWER
using System.Drawing;
#else
using UnityEngine;
#endif

namespace Paraparty.Colors
{
    public static class ColorUtils
    {
        public static string SerializeColor(Color color)
        {
            var oklch = Oklch.FromColor(color);
            return oklch.ToString();
        }


        public static Color ParseColor(string src)
        {
            src = src.Trim();
            if (string.IsNullOrWhiteSpace(src))
            {
                return Constance.White;
            }

            if (uint.TryParse(src, out var uintColor))
            {
                return ParseUintColor(uintColor);
            }

            if (src.StartsWith("#"))
            {
                src = src[1..];
                if (src.Length == 3 &&
                    Tools.TryParseHexToUint($"{src[0]}{src[0]}{src[1]}{src[1]}{src[2]}{src[2]}FF", out uintColor))
                {
                    return ParseUintColor(uintColor);
                }

                if (src.Length == 6 && Tools.TryParseHexToUint($"{src}FF", out uintColor))
                {
                    return ParseUintColor(uintColor);
                }

                if (src.Length == 8 && Tools.TryParseHexToUint($"{src}", out uintColor))
                {
                    return ParseUintColor(uintColor);
                }
            }

            if (src.StartsWith("oklab(") && src.EndsWith(")"))
            {
                return Oklab.ParseOklch(src).ToColor();
            }

            if (src.StartsWith("oklch(") && src.EndsWith(")"))
            {
                return Oklch.ParseOklch(src).ToColor();
            }

            return Constance.White;
        }

        private static Color ParseUintColor(uint src)
        {
            var b = new byte[]
            {
                (byte)(src >> 24),
                (byte)((src >> 16) & 0x00FF),
                (byte)((src >> 8) & 0x0000FF),
                (byte)(src & 0x000000FF)
            };
            return MakeColorFromRgba(b[0], b[1], b[2], b[3]);
        }

        internal static Color MakeColorFromRgba(int red, int green, int blue, int alpha)
        {
#if !UNITY_2022_1_OR_NEWER
            return Color.FromArgb(alpha, red, green, blue);
#else
            return new Color(red * 1f / 255f, green * 1f / 255f, blue * 1f / 255f, alpha * 1f / 255f);
#endif
        }

        internal static Color MakeColorFromRgbaF(float red, float green, float blue, float alpha)
        {
#if !UNITY_2022_1_OR_NEWER
            return Color.FromArgb(
                (int)(alpha * 255),
                (int)(red * 255),
                (int)(green * 255),
                (int)(blue * 255)
            );
#else
            return new Color(red, green, blue, alpha);
#endif
        }
    }
}

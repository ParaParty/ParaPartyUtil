#if PARAPARTYUTIL_NOUNITY
using System.Drawing;
#else
using UnityEngine;
#endif
using System;
using System.Collections.Generic;

namespace Paraparty.Colors
{
    public static class ColorUtils
    {
        private static readonly IReadOnlyDictionary<string, uint> NamedColors =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["transparent"] = 0x00000000,
                ["black"] = 0x000000FF,
                ["silver"] = 0xC0C0C0FF,
                ["gray"] = 0x808080FF,
                ["grey"] = 0x808080FF,
                ["white"] = 0xFFFFFFFF,
                ["maroon"] = 0x800000FF,
                ["red"] = 0xFF0000FF,
                ["purple"] = 0x800080FF,
                ["fuchsia"] = 0xFF00FFFF,
                ["magenta"] = 0xFF00FFFF,
                ["green"] = 0x008000FF,
                ["lime"] = 0x00FF00FF,
                ["olive"] = 0x808000FF,
                ["yellow"] = 0xFFFF00FF,
                ["navy"] = 0x000080FF,
                ["blue"] = 0x0000FFFF,
                ["teal"] = 0x008080FF,
                ["aqua"] = 0x00FFFFFF,
                ["cyan"] = 0x00FFFFFF,
                ["orange"] = 0xFFA500FF,
            };

        public static string SerializeColor(Color color)
        {
            var oklch = Oklch.FromColor(color);
            return oklch.ToString();
        }


        public static Color ParseColor(string src)
        {
            return TryParseColor(src, out var color) ? color : Constance.White;
        }

        public static bool TryParseColor(string src, out Color color)
        {
            color = Constance.White;
            if (string.IsNullOrWhiteSpace(src))
            {
                return false;
            }

            src = src.Trim();
            if (NamedColors.TryGetValue(src, out var namedColor))
            {
                color = ParseUintColor(namedColor);
                return true;
            }

            if (uint.TryParse(src, out var uintColor))
            {
                color = ParseUintColor(uintColor);
                return true;
            }

            if (src.StartsWith("#"))
            {
                src = src.Substring(1);
                if (src.Length == 3 &&
                    Tools.TryParseHexToUint($"{src[0]}{src[0]}{src[1]}{src[1]}{src[2]}{src[2]}FF", out uintColor))
                {
                    color = ParseUintColor(uintColor);
                    return true;
                }

                if (src.Length == 4 &&
                    Tools.TryParseHexToUint(
                        $"{src[0]}{src[0]}{src[1]}{src[1]}{src[2]}{src[2]}{src[3]}{src[3]}",
                        out uintColor))
                {
                    color = ParseUintColor(uintColor);
                    return true;
                }

                if (src.Length == 6 && Tools.TryParseHexToUint($"{src}FF", out uintColor))
                {
                    color = ParseUintColor(uintColor);
                    return true;
                }

                if (src.Length == 8 && Tools.TryParseHexToUint($"{src}", out uintColor))
                {
                    color = ParseUintColor(uintColor);
                    return true;
                }

                return false;
            }

            if (src.StartsWith("oklab(", StringComparison.OrdinalIgnoreCase) &&
                src.EndsWith(")", StringComparison.Ordinal))
            {
                color = Oklab.ParseOklch(src).ToColor();
                return true;
            }

            if (src.StartsWith("oklch(", StringComparison.OrdinalIgnoreCase) &&
                src.EndsWith(")", StringComparison.Ordinal))
            {
                color = Oklch.ParseOklch(src).ToColor();
                return true;
            }

            return false;
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
#if PARAPARTYUTIL_NOUNITY
            return Color.FromArgb(alpha, red, green, blue);
#else
            return new Color(red * 1f / 255f, green * 1f / 255f, blue * 1f / 255f, alpha * 1f / 255f);
#endif
        }

        internal static Color MakeColorFromRgbaF(float red, float green, float blue, float alpha)
        {
#if PARAPARTYUTIL_NOUNITY
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

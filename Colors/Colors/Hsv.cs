using System;
using System.Linq;

#if PARAPARTYUTIL_NOUNITY
using System.Drawing;
#else
using UnityEngine;
#endif

#if NET_STANDARD_2_1
using PMath = System.Math;
#else
using PMath = Paraparty.UnityPolyfill.MathPolyfill;
#endif

namespace Paraparty.Colors
{
    public struct Hsv
    {
        public double H { get; set; }
        public double S { get; set; }
        public double V { get; set; }
        public double Opacity { get; set; }

        public override string ToString()
        {
            if (Opacity > 0.999)
            {
                return $"hsv({H} {S} {V})";
            }
            else
            {
                return $"hsv({H} {S} {V} / {Opacity})";
            }
        }

        public Hsv(double h, double s, double v, double opacity)
        {
            H = h;
            S = s;
            V = v;
            Opacity = opacity;
        }

        public Color ToColor()
        {
            Color color = new Color();
            float r, g, b;
            double hh, p, q, t, ff;
            long i;

            hh = H;
            if (hh >= 360.0) hh = 0.0;
            hh /= 60.0;
            i = (long)hh;
            ff = hh - i;
            p = V * (1.0 - S);
            q = V * (1.0 - (S * ff));
            t = V * (1.0 - (S * (1.0 - ff)));

            switch (i)
            {
                case 0:
                    r = (float)(V * 255);
                    g = (float)(t * 255);
                    b = (float)(p * 255);
                    break;
                case 1:
                    r = (float)(q * 255);
                    g = (float)(V * 255);
                    b = (float)(p * 255);
                    break;
                case 2:
                    r = (float)(p * 255);
                    g = (float)(V * 255);
                    b = (float)(t * 255);
                    break;

                case 3:
                    r = (float)(p * 255);
                    g = (float)(q * 255);
                    b = (float)(V * 255);
                    break;
                case 4:
                    r = (float)(t * 255);
                    g = (float)(p * 255);
                    b = (float)(V * 255);
                    break;
                case 5:
                default:
                    r = (float)(V * 255);
                    g = (float)(p * 255);
                    b = (float)(q * 255);
                    break;
            }

            return ColorUtils.MakeColorFromRgbaF(r, g, b, (float)Opacity);
        }


        public static Hsv FromColor(Color color)
        {
#if PARAPARTYUTIL_NOUNITY
            double r = (double)color.R / 255.0;
            double g = (double)color.G / 255.0;
            double b = (double)color.B / 255.0;
            double a = (double)color.A / 255.0;
#else
            double r = color.r;
            double g = color.g;
            double b = color.b;
            double a = color.a;
#endif
            return FromColor(r, g, b, a);
        }

        public static Hsv FromColor(double inputR, double inputG, double inputB, double inputA)
        {
            double h, s, v, opacity = inputA;

            double rf = inputR / 255.0;
            double gf = inputG / 255.0;
            double bf = inputB / 255.0;

            double max = Math.Max(rf, Math.Max(gf, bf));
            double min = Math.Min(rf, Math.Min(gf, bf));
            double delta = max - min;

            h = 0; // 默认值
            s = 0; // 默认值
            v = max;

            if (delta != 0)
            {
                s = delta / max;

                if (rf == max)
                {
                    h = (gf - bf) / delta;
                }
                else if (gf == max)
                {
                    h = 2 + (bf - rf) / delta;
                }
                else
                {
                    h = 4 + (rf - gf) / delta;
                }

                h *= 60;
                if (h < 0)
                {
                    h += 360;
                }
            }

            return new Hsv(h, s, v, opacity);
        }

        static readonly int HsvPrefixLength = "hsv(".Length;
        static readonly int HsvSuffixLength = ")".Length;

        public static Hsv ParseHsv(string ssrc)
        {
            var White = new Hsv(0, 0, 1, 1);

            var src = ssrc.Substring(HsvPrefixLength, ssrc.Length - HsvPrefixLength - HsvSuffixLength);
            if (string.IsNullOrWhiteSpace(src))
            {
                Tools.LogError($"empty Hsv: {ssrc}");
                return White;
            }

            var colorAlpha = src.Split('/');
            if (colorAlpha.Length > 2)
            {
                Tools.LogError($"multi slash: {ssrc}");
                return White;
            }

            double opacity = 1;
            if (colorAlpha.Length == 2)
            {
                var alpha = colorAlpha[1];
                if (Tools.TryParsePercentage(alpha, out opacity))
                {
                }
                else if (Tools.TryParseDouble(alpha, out opacity))
                {
                }
                else
                {
                    Tools.LogError($"parsing Alpha error: {ssrc}");
                    return White;
                }

                opacity = PMath.Clamp(opacity, 0.0, 1.0);
            }

            var lch = colorAlpha[0].Split(' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (lch.Length != 3)
            {
                Tools.LogError($"lch elements mismatched: {ssrc}");
                return White;
            }

            string lStr = lch[0];
            string aStr = lch[1];
            string bStr = lch[2];

            double l = 0;
            double a = 0;
            double b = 0;

            if (Tools.TryParsePercentage(lStr, out l))
            {
            }
            else if (Tools.TryParseDouble(lStr, out l))
            {
            }
            else
            {
                Tools.LogError($"parsing L error: {ssrc}");
                return White;
            }


            if (Tools.TryParsePercentage(aStr, out a))
            {
                a = a * 0.8 - 0.4;
            }
            else if (Tools.TryParseDouble(aStr, out a))
            {
            }
            else
            {
                Tools.LogError($"parsing A error: {ssrc}");
                return White;
            }

            a = PMath.Clamp(a, -0.4, 0.4);


            if (Tools.TryParsePercentage(bStr, out b))
            {
                b = b * 0.8 - 0.4;
            }
            else if (Tools.TryParseDouble(bStr, out b))
            {
            }
            else
            {
                Tools.LogError($"parsing B error: {ssrc}");
                return White;
            }

            b = PMath.Clamp(b, -0.4, 0.4);

            return new Hsv(l, a, b, opacity);
        }
    }
}

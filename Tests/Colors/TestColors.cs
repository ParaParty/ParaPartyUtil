using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.Colors;

namespace Paraparty.Tests.Colors;

[TestClass]
public class TestColors
{
    [TestMethod]
    public void TestRGB()
    {
        Assert.AreEqual("#00FFFF", Color.Aqua.SerializeColor());
        Assert.AreEqual(Color.FromArgb(0xff, 0x66, 0xCC, 0xFF), ColorUtils.ParseColor("#6cf"));
    }

    [TestMethod]
    public void TestOklch()
    {
        Assert.AreEqual(Color.FromArgb(0xff, 0x94, 0x96, 0xdb), ColorUtils.ParseColor("oklch(70% 0.1 282)"));
        Assert.AreEqual(Color.FromArgb(0x7f, 0xb7, 0x9b, 0x50), ColorUtils.ParseColor("oklch(70% 0.1 89 / 0.5)"));
    }

    [TestMethod]
    public void TestTryParseColor()
    {
        Assert.IsTrue(ColorUtils.TryParseColor("#1234", out var shortRgba));
        Assert.AreEqual(Color.FromArgb(0x44, 0x11, 0x22, 0x33), shortRgba);

        Assert.IsTrue(ColorUtils.TryParseColor("YeLlOw", out var yellow));
        Assert.AreEqual(Color.FromArgb(0xff, 0xff, 0xff, 0x00), yellow);

        Assert.IsTrue(ColorUtils.TryParseColor("transparent", out var transparent));
        Assert.AreEqual(Color.FromArgb(0, 0, 0, 0), transparent);

        Assert.IsFalse(ColorUtils.TryParseColor("not-a-color", out _));
        Assert.IsFalse(ColorUtils.TryParseColor("#12", out _));
        Assert.IsFalse(ColorUtils.TryParseColor("", out _));
    }

    [TestMethod]
    public void TestParseColorKeepsWhiteFallback()
    {
        Assert.AreEqual(Color.White, ColorUtils.ParseColor("not-a-color"));
        Assert.AreEqual(Color.White, ColorUtils.ParseColor(""));
    }
}

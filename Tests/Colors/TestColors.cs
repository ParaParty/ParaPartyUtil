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
}

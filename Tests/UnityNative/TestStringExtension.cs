using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.UnityNative;

namespace Paraparty.Tests.UnityNative;

[TestClass]
public class TestStringExtension
{
    [TestMethod]
    public void TestCStr()
    {
        var s = "test";
        var cstr = s.CStr();
        var expected = new byte[] { (byte)'t', (byte)'e', (byte)'s', (byte)'t', (byte)'\0' };
        for (int i = 0; i < cstr.Length; i++)
        {
            Assert.AreEqual(expected[i], cstr[i]);
        }
    }
}

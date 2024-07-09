using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.Kotlinize;

namespace Paraparty.Tests.Kotlinize;

[TestClass]
public class CollectionExtension
{
    [TestMethod]
    public void CreateList()
    {
        var t = Collections.ListOf(1, 2.2);
        Assert.AreEqual(1, t[0]);
        Assert.AreEqual(2.2, t[1]);
    }

    [TestMethod]
    public void CreatePair()
    {
        var t = "ΩΩPARTS".To("かめりあ");
        Assert.AreEqual("ΩΩPARTS", t.Key);
        Assert.AreEqual("かめりあ", t.Value);
    }
    
    [TestMethod]
    public void CreateMap()
    {
        var t = Collections.MapOf(
            "ΩΩPARTS".To("かめりあ"),
                        "ANiMA".To("Xi")
        );
        Assert.AreEqual("かめりあ", t["ΩΩPARTS"]);
        Assert.AreEqual("Xi", t["ANiMA"]);
    }
    
    [TestMethod]
    public void CreateSet()
    {
        var t = Collections.SetOf(
            1, 1, 4, 5, 1, 4
        );
        Assert.AreEqual(3,t.Count);
        Assert.IsTrue(t.Contains(1));
        Assert.IsFalse(t.Contains(2));
    }
}

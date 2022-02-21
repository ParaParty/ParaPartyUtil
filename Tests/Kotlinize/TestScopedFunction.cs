using NUnit.Framework;
using Paraparty.Kotlinize;

namespace Paraparty.Tests.Kotlinize;

public class CollectionExtension
{
    [Test]
    public void CreateList()
    {
        var t = Collections.ListOf(1, 2.2);
        Assert.Equals(1, t[0]);
        Assert.Equals(2.2, t[1]);
    }

    [Test]
    public void CreatePair()
    {
        var t = "ΩΩPARTS".To("かめりあ");
        Assert.Equals("ΩΩPARTS", t.Key);
        Assert.Equals("かめりあ", t.Value);
    }
    
    [Test]
    public void CreateMap()
    {
        var t = Collections.MapOf(
            "ΩΩPARTS".To("かめりあ"),
                        "ANiMA".To("Xi")
        );
        Assert.Equals("かめりあ", t["ΩΩPARTS"]);
        Assert.Equals("Xi", t["ANiMA"]);
    }
    
    [Test]
    public void CreateSet()
    {
        var t = Collections.SetOf(
            1, 1, 4, 5, 1, 4
        );
        Assert.Equals(3,t.Count);
        Assert.True(t.Contains(1));
        Assert.False(t.Contains(2));
    }
}

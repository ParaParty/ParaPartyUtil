using Paraparty.Kotlinize;
using Xunit;

namespace Paraparty.Tests.Kotlinize;

public class CollectionExtension
{
    [Fact]
    public void CreateList()
    {
        var t = Collections.ListOf(1, 2.2);
        Assert.Equal(1, t[0]);
        Assert.Equal(2.2, t[1]);
    }

    [Fact]
    public void CreatePair()
    {
        var t = "ΩΩPARTS".To("かめりあ");
        Assert.Equal("ΩΩPARTS", t.Key);
        Assert.Equal("かめりあ", t.Value);
    }
    
    [Fact]
    public void CreateMap()
    {
        var t = Collections.MapOf(
            "ΩΩPARTS".To("かめりあ"),
                        "ANiMA".To("Xi")
        );
        Assert.Equal("かめりあ", t["ΩΩPARTS"]);
        Assert.Equal("Xi", t["ANiMA"]);
    }
    
    [Fact]
    public void CreateSet()
    {
        var t = Collections.SetOf(
            1, 1, 4, 5, 1, 4
        );
        Assert.Equal(3,t.Count);
        Assert.True(t.Contains(1));
        Assert.False(t.Contains(2));
    }
}

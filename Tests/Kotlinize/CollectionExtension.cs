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
}

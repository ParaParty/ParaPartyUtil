using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paraparty.Kotlinize.Extension;

namespace Paraparty.Tests;

public class TestKotlinize
{
    [Test]
    public void TestAlso()
    {
        var numbers = new List<string> {"one", "two", "three"};
        numbers
            .Also(it => Console.WriteLine($"The list elements before adding new one: {string.Join(',', it)}"))
            .Add("four");
        Assert.Pass();
    }

    [Test]
    public void TestLet()
    {
        var numbers = new[] {"one", "two", "three", "four", "five"};
        numbers.Select(s => s.Length)
            .Where(l => l > 3)
            .Let(it => Console.WriteLine(string.Join(",", it)));
        Assert.Pass();
    }
}

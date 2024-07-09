using Microsoft.VisualStudio.TestTools.UnitTesting;
using Paraparty.Strings;

namespace Paraparty.Tests.Strings;

[TestClass]
public class NamingStrategyUtilTest
{
    public static string[] lockedWords = new[] { "QQ", "ID" };

    [TestMethod]
    public void ToSnakeCaseTest()
    {
        var lowerSnake = new PropertyNamingStrategy.LowerSnakeNamingStrategy(lockedWords);
        Assert.AreEqual("qq", lowerSnake.Convert("QQ"));
        Assert.AreEqual("id", lowerSnake.Convert("ID"));
        Assert.AreEqual("qq id", lowerSnake.Convert("QQ ID"));

        // ambiguous case
        // Assert.AreEqual("qq_qq_id qq_id_id_qq", NamingStrategyUtil.ToSnakeCase("QQQQID QQIDIDQQ", NamingStrategyUtil.CaseType.Lower, lockedWords));

        Assert.AreEqual("user_qq_token",
            NamingStrategyUtil.ToSnakeCase("UserQQToken", NamingStrategyUtil.CaseType.Lower, lockedWords));
        Assert.AreEqual("sweet_icelolly_id",
            NamingStrategyUtil.ToSnakeCase("SweetIcelollyID", NamingStrategyUtil.CaseType.Lower, lockedWords));
        Assert.AreEqual("wey_sun+ice_eric_vancheng_dresses",
            NamingStrategyUtil.ToSnakeCase("WeySun+IceEricVanChengDresses", NamingStrategyUtil.CaseType.Lower,
                new[] { "SUN+ICE", "VANCHENG" }));

        Assert.AreEqual("QQ", NamingStrategyUtil.ToSnakeCase("QQ", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("ID", NamingStrategyUtil.ToSnakeCase("ID", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("QQ ID",
            NamingStrategyUtil.ToSnakeCase("QQ ID", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("QQ_QQ_ID QQ_ID_ID_QQ",
            NamingStrategyUtil.ToSnakeCase("QQQQID QQIDIDQQ", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("USER_QQ_TOKEN",
            NamingStrategyUtil.ToSnakeCase("UserQQToken", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("SWEET_ICELOLLY_ID",
            NamingStrategyUtil.ToSnakeCase("SweetIcelollyID", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("WEY_SUN+ICE_ERIC_VANCHENG_DRESSES",
            NamingStrategyUtil.ToSnakeCase("WeySun+IceEricVanChengDresses", NamingStrategyUtil.CaseType.Upper,
                new[] { "SUN+ICE", "VANCHENG" }));

        Assert.AreEqual("one_another",
            NamingStrategyUtil.ToSnakeCase("oneAnother", NamingStrategyUtil.CaseType.Lower, lockedWords));
        Assert.AreEqual("ONE_ANOTHER",
            NamingStrategyUtil.ToSnakeCase("oneAnother", NamingStrategyUtil.CaseType.Upper, lockedWords));

        Assert.AreEqual("ONE_ANOTHER",
            NamingStrategyUtil.ToSnakeCase("one-Another", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("ONE_ANOTHER",
            NamingStrategyUtil.ToSnakeCase("one_Another", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("ONE_ANOTHER",
            NamingStrategyUtil.ToSnakeCase("one_-Another", NamingStrategyUtil.CaseType.Upper, lockedWords));
        Assert.AreEqual("ONE_ANOTHER",
            NamingStrategyUtil.ToSnakeCase("ONE_ANOTHER", NamingStrategyUtil.CaseType.Upper, lockedWords));
    }

    [TestMethod]
    public void ToLowerCamelTest()
    {
        Assert.AreEqual("userQQ",
            NamingStrategyUtil.ToCamelCase("USER_QQ", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));

        Assert.AreEqual("userQQ",
            NamingStrategyUtil.ToCamelCase("user_qq", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));
        Assert.AreEqual("userQQ",
            NamingStrategyUtil.ToCamelCase("userQQ", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));
        Assert.AreEqual("userQQ",
            NamingStrategyUtil.ToCamelCase("USER_QQ", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));
        Assert.AreEqual("userQQ",
            NamingStrategyUtil.ToCamelCase("userQq", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));
        Assert.AreEqual("userID",
            NamingStrategyUtil.ToCamelCase("user_id", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));
        Assert.AreEqual("qqID",
            NamingStrategyUtil.ToCamelCase("qq_id", NamingStrategyUtil.CamelCaseType.Lower, lockedWords));
        Assert.AreEqual("icelolly", NamingStrategyUtil.ToCamelCase("icelolly", NamingStrategyUtil.CamelCaseType.Lower));
        Assert.AreEqual("icelolly", NamingStrategyUtil.ToCamelCase("Icelolly", NamingStrategyUtil.CamelCaseType.Lower));

        // ambiguous case
        Assert.AreEqual("iceLOlly", NamingStrategyUtil.ToCamelCase("IceLOlly", NamingStrategyUtil.CamelCaseType.Lower));

        Assert.AreEqual("icelollyDress",
            NamingStrategyUtil.ToCamelCase("icelolly_dress", NamingStrategyUtil.CamelCaseType.Lower));
        Assert.AreEqual("icelollyDress2",
            NamingStrategyUtil.ToCamelCase("icelolly_dress2", NamingStrategyUtil.CamelCaseType.Lower));
        Assert.AreEqual("icelollyDress2",
            NamingStrategyUtil.ToCamelCase("icelolly_dress_2", NamingStrategyUtil.CamelCaseType.Lower));

        Assert.AreEqual("aDRess", NamingStrategyUtil.ToCamelCase("A_DRess", NamingStrategyUtil.CamelCaseType.Lower));

        // ambiguous case
        Assert.AreEqual("iCelollyDRess",
            NamingStrategyUtil.ToCamelCase("ICelolly_DRess", NamingStrategyUtil.CamelCaseType.Lower));

        Assert.AreEqual("iCelollyDRess daiSuki",
            NamingStrategyUtil.ToCamelCase("iCelolly_dRess dai_suki", NamingStrategyUtil.CamelCaseType.Lower));
        Assert.AreEqual("iCelollyDRess daiSuki404",
            NamingStrategyUtil.ToCamelCase("iCelolly_dRess dai_suki_404", NamingStrategyUtil.CamelCaseType.Lower));
        Assert.AreEqual("iCelollyDRess",
            NamingStrategyUtil.ToCamelCase("iCelolly_----dRess", NamingStrategyUtil.CamelCaseType.Lower));
    }

    [TestMethod]
    public void ToUpperCamelTest()
    {
        Assert.AreEqual("UserQQ",
            NamingStrategyUtil.ToCamelCase("user_qq", NamingStrategyUtil.CamelCaseType.Upper, lockedWords));
        Assert.AreEqual("UserID",
            NamingStrategyUtil.ToCamelCase("user_id", NamingStrategyUtil.CamelCaseType.Upper, lockedWords));
        Assert.AreEqual("QQID",
            NamingStrategyUtil.ToCamelCase("qq_id", NamingStrategyUtil.CamelCaseType.Upper, lockedWords));
        Assert.AreEqual("Icelolly", NamingStrategyUtil.ToCamelCase("icelolly", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("Icelolly", NamingStrategyUtil.ToCamelCase("Icelolly", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("IceLOlly", NamingStrategyUtil.ToCamelCase("IceLOlly", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("IcelollyDress",
            NamingStrategyUtil.ToCamelCase("icelolly_dress", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("IcelollyDress2",
            NamingStrategyUtil.ToCamelCase("icelolly_dress2", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("ICelollyDRess",
            NamingStrategyUtil.ToCamelCase("ICelolly_DRess", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("ICelollyDRess DaiSuki",
            NamingStrategyUtil.ToCamelCase("iCelolly_dRess dai_suki", NamingStrategyUtil.CamelCaseType.Upper));
        Assert.AreEqual("ICelollyDRess DaiSuki404",
            NamingStrategyUtil.ToCamelCase("iCelolly_dRess dai_suki_404", NamingStrategyUtil.CamelCaseType.Upper));
    }
}

using MiniTools.Contracts;

namespace MiniTools.Contracts.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("V10.0.15", 10, 0, 15)]
    [InlineData(" 0.0.1 ", 0, 0, 1)]
    public void Parse_Valid(string s, int major, int minor, int patch)
    {
        var v = AppVersion.Parse(s);
        Assert.Equal(new AppVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.x")]
    [InlineData("vv1.2.3")]
    [InlineData("1..3")]
    [InlineData("+1.2.3")]
    public void Parse_Invalid_Throws(string s)
    {
        Assert.Throws<FormatException>(() => AppVersion.Parse(s));
        Assert.False(AppVersion.TryParse(s, out _));
    }

    [Fact]
    public void TryParse_Null_IsFalse() => Assert.False(AppVersion.TryParse(null, out _));

    [Fact]
    public void ToString_NoPrefix() => Assert.Equal("1.2.0", new AppVersion(1, 2, 0).ToString());

    [Fact]
    public void Compare_IsNumeric_NotLexical()
    {
        Assert.True(AppVersion.Parse("1.10.0") > AppVersion.Parse("1.9.9"));
        Assert.True(AppVersion.Parse("2.0.0") > AppVersion.Parse("1.99.99"));
        Assert.True(AppVersion.Parse("1.2.3") < AppVersion.Parse("1.2.4"));
        Assert.Equal(0, AppVersion.Parse("v1.2.3").CompareTo(AppVersion.Parse("1.2.3")));
    }
}

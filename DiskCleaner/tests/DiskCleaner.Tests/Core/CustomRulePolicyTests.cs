using DiskCleaner.Core;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Core;

public class CustomRulePolicyTests
{
    private const string Profile = @"C:\Users\tester";
    private const string Local = @"C:\Users\tester\AppData\Local";
    private const string AppDataDir = @"C:\Users\tester\AppData\Local\DiskCleaner";
    private const string ExeDir = @"C:\Apps\DiskCleaner";

    private static CustomRuleCheck Eval(string path, IEnumerable<string>? existing = null, FakeEnvironment? env = null)
    {
        env ??= FakeEnvironment.Standard();
        return CustomRulePolicy.Evaluate(path, env, SafetyGuard.ForbiddenRoots(env), existing ?? [], AppDataDir, ExeDir);
    }

    [Fact]
    public void CapCutCache_IsOk()
    {
        var r = Eval(Local + @"\CapCut\User Data\Cache");
        Assert.Equal(CustomRuleVerdict.Ok, r.Verdict);
        Assert.Empty(r.ReasonKeys);
    }

    [Theory]
    [InlineData(Profile + @"\AppData")]
    [InlineData(Local)]
    [InlineData(Profile + @"\AppData\Roaming")]
    [InlineData(Profile + @"\AppData\LocalLow")]
    [InlineData(Local + @"\Programs")]
    [InlineData(Local + @"\Packages")]
    [InlineData(Local + @"\Microsoft")]
    [InlineData(Profile + @"\Downloads")]
    [InlineData(Profile + @"\.ssh")]
    [InlineData(Profile + @"\source")]
    [InlineData(@"C:\Users")]
    [InlineData(Profile)]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files\Foo")]
    [InlineData(@"C:\ProgramData\Foo")]
    [InlineData(Profile + @"\Documents")]
    [InlineData(Profile + @"\Documents\Stuff")]
    [InlineData(Profile + @"\Desktop")]
    [InlineData(AppDataDir)]
    [InlineData(AppDataDir + @"\logs")]
    [InlineData(ExeDir)]
    [InlineData(ExeDir + @"\Rules")]
    [InlineData(@"C:\Apps")]
    [InlineData(@"relative\path")]
    [InlineData(@"\\server\share\cache")]
    [InlineData("")]
    [InlineData(Local + @"\%TEMP%")]
    public void Blocked(string path)
    {
        var r = Eval(path);
        Assert.Equal(CustomRuleVerdict.Blocked, r.Verdict);
        Assert.NotEmpty(r.ReasonKeys);
    }

    [Fact]
    public void ExistingRulePath_IsBlocked_WithEnvAndWildcardPatterns()
    {
        var existing = new[]
        {
            @"%LOCALAPPDATA%\CapCut\User Data\Cache",
            @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache"
        };
        var a = Eval(Local + @"\CapCut\User Data\Cache", existing);
        Assert.Equal(CustomRuleVerdict.Blocked, a.Verdict);
        Assert.Contains(CustomRulePolicy.BlockAlreadyExists, a.ReasonKeys);

        var b = Eval(Local + @"\Google\Chrome\User Data\Default\Cache", existing);
        Assert.Contains(CustomRulePolicy.BlockAlreadyExists, b.ReasonKeys);

        var c = Eval(Local + @"\Google\Chrome\User Data\Default\Cache\Sub", existing);
        Assert.Equal(CustomRuleVerdict.Ok, c.Verdict);
    }

    [Fact]
    public void FolderInsideTemp_IsNotBlocked()
    {
        var r = Eval(Local + @"\Temp\MyAppTemp");
        Assert.NotEqual(CustomRuleVerdict.Blocked, r.Verdict);
    }

    [Fact]
    public void NotCacheLike_Warns()
    {
        var r = Eval(Local + @"\SomeApp\Data");
        Assert.Equal(CustomRuleVerdict.Warn, r.Verdict);
        Assert.Contains(CustomRulePolicy.WarnNotCacheLike, r.ReasonKeys);
        Assert.DoesNotContain(CustomRulePolicy.WarnOutsideAppData, r.ReasonKeys);
    }

    [Theory]
    [InlineData(@"\Foo\GPUCache")]
    [InlineData(@"\Foo\Code Cache")]
    [InlineData(@"\Foo\ShaderCache")]
    [InlineData(@"\Foo\logs\2024")]       // từ khóa nằm ở thư mục cha
    [InlineData(@"\Foo\Crash Reports")]
    [InlineData(@"\Foo\tmp")]
    public void CacheKeywords_NoNotCacheLikeWarning(string suffix)
    {
        var r = Eval(Local + suffix);
        Assert.DoesNotContain(CustomRulePolicy.WarnNotCacheLike, r.ReasonKeys);
    }

    [Fact]
    public void KeywordMustBeWholeWord_CatalogIsNotLog()
    {
        var r = Eval(Local + @"\Foo\Catalog");
        Assert.Contains(CustomRulePolicy.WarnNotCacheLike, r.ReasonKeys);
    }

    [Fact]
    public void OutsideAppData_Warns()
    {
        var r = Eval(@"D:\Foo\cache");
        Assert.Equal(CustomRuleVerdict.Warn, r.Verdict);
        Assert.Contains(CustomRulePolicy.WarnOutsideAppData, r.ReasonKeys);
        Assert.DoesNotContain(CustomRulePolicy.WarnNotCacheLike, r.ReasonKeys);
    }

    [Fact]
    public void GitFolder_WarnsLooksLikeProject()
    {
        using var t = new TempTree();
        t.CreateDir(@"proj\cache\.git");
        var r = Eval(t.P(@"proj\cache"));
        Assert.Equal(CustomRuleVerdict.Warn, r.Verdict);
        Assert.Contains(CustomRulePolicy.WarnLooksLikeProject, r.ReasonKeys);
    }

    [Fact]
    public void Junction_IsBlocked()
    {
        using var t = new TempTree();
        var target = t.CreateDir(@"real\cache");
        if (!t.CreateJunction(@"link\cache", target)) return; // môi trường không tạo được junction
        var r = Eval(t.P(@"link\cache"));
        Assert.Equal(CustomRuleVerdict.Blocked, r.Verdict);
        Assert.Contains(CustomRulePolicy.BlockReparse, r.ReasonKeys);
    }

    [Fact]
    public void RedirectedLocalAppData_StillBlocksItsRoot()
    {
        var env = FakeEnvironment.Standard().Set("LOCALAPPDATA", @"D:\Redirected\Local");
        Assert.Equal(CustomRuleVerdict.Blocked, Eval(@"D:\Redirected\Local", null, env).Verdict);
        Assert.Equal(CustomRuleVerdict.Blocked, Eval(@"D:\Redirected\Local\Microsoft", null, env).Verdict);
        Assert.Equal(CustomRuleVerdict.Ok, Eval(@"D:\Redirected\Local\CapCut\Cache", null, env).Verdict);
    }
}

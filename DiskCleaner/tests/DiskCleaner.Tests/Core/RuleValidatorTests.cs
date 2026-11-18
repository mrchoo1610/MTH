using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Core;

public class RuleValidatorTests
{
    private static readonly FakeEnvironment Env = FakeEnvironment.Standard();

    private static CleanRule Rule(string id, params string[] paths) => new()
    {
        Id = id, Name = id, Description = id,
        Category = RuleCategory.System, Action = RuleAction.DeleteFiles, Paths = paths
    };

    private static IReadOnlyList<CleanRule> Valid(params CleanRule[] rules) =>
        RuleValidator.ValidateAll(rules, Env).Valid;

    [Fact]
    public void NormalRule_IsAccepted() =>
        Assert.Single(Valid(Rule("user-temp", "%TEMP%")));

    [Fact]
    public void WildcardRule_IsAccepted() =>
        Assert.Single(Valid(Rule("chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache")));

    [Fact]
    public void WindowsTempAndWuCache_AreAccepted()
    {
        Assert.Single(Valid(Rule("win-temp", @"%WINDIR%\Temp")));
        Assert.Single(Valid(Rule("wu", @"%WINDIR%\SoftwareDistribution\Download")));
    }

    [Theory]
    [InlineData(@"%WINDIR%\System32")]
    [InlineData(@"%WINDIR%")]
    [InlineData(@"C:\Program Files\App")]
    [InlineData(@"%PROGRAMDATA%\Foo")]
    [InlineData(@"%USERPROFILE%\Documents")]
    [InlineData(@"%USERPROFILE%\Desktop\x")]
    [InlineData(@"%USERPROFILE%")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\")]
    [InlineData(@"%USERPROFILE%\OneDrive - Contoso")]
    public void ForbiddenPaths_AreRejected(string path)
    {
        var result = RuleValidator.ValidateAll([Rule("bad", path)], Env);
        Assert.Empty(result.Valid);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData(@"relative\path")]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"%LOCALAPPDATA%\..\..\Windows")]
    [InlineData(@"%UNDEFINED_VAR%\x")]
    [InlineData(@"%USERPROFILE%\PROGRA~1")]
    public void SuspiciousPaths_AreRejected(string path) =>
        Assert.Empty(Valid(Rule("bad", path)));

    [Fact]
    public void DuplicateId_SecondIsRejected()
    {
        var result = RuleValidator.ValidateAll([Rule("same", "%TEMP%"), Rule("same", @"%LOCALAPPDATA%\CrashDumps")], Env);
        Assert.Single(result.Valid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void RunCommand_WithoutCommand_IsRejected()
    {
        var r = Rule("cmd", @"%LOCALAPPDATA%\npm-cache") with { Action = RuleAction.RunCommand, Command = null };
        Assert.Empty(Valid(r));
    }

    [Fact]
    public void RunCommand_NotInWhitelist_IsRejected()
    {
        var r = Rule("cmd") with { Action = RuleAction.RunCommand, Command = "powershell", CommandArgs = "-c whoami" };
        Assert.Empty(Valid(r));
    }

    [Fact]
    public void RunCommand_Whitelisted_IsAccepted()
    {
        var r = Rule("npm", @"%LOCALAPPDATA%\npm-cache") with
        { Action = RuleAction.RunCommand, Command = "npm", CommandArgs = "cache clean --force" };
        Assert.Single(Valid(r));
    }

    [Fact]
    public void DeleteFiles_WithoutPaths_IsRejected() =>
        Assert.Empty(Valid(Rule("nopaths")));

    [Fact]
    public void EmptyRecycleBin_WithoutPaths_IsAccepted()
    {
        var r = Rule("rb") with { Action = RuleAction.EmptyRecycleBin };
        Assert.Single(Valid(r));
    }

    [Fact]
    public void InvalidId_IsRejected() =>
        Assert.Empty(Valid(Rule("Bad Id!", "%TEMP%")));
}

using DiskCleaner.Core;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Core;

public class PathExpanderTests
{
    private static IEnumerable<string> ListDirs(string p) => Directory.EnumerateDirectories(p);

    [Fact]
    public void ExpandsEnvironmentVariable()
    {
        using var t = new TempTree();
        var env = new FakeEnvironment().Set("TEMP", t.Root);
        var result = PathExpander.Expand("%TEMP%", env, ListDirs);
        Assert.Single(result);
        Assert.Equal(t.Root, result[0], ignoreCase: true);
    }

    [Fact]
    public void MissingVariable_ReturnsEmpty()
    {
        var result = PathExpander.Expand(@"%NOPE_NOT_DEFINED%\x", new FakeEnvironment(), ListDirs);
        Assert.Empty(result);
    }

    [Fact]
    public void NonExistingPath_ReturnsEmpty()
    {
        using var t = new TempTree();
        var env = new FakeEnvironment().Set("ROOT", t.Root);
        Assert.Empty(PathExpander.Expand(@"%ROOT%\does\not\exist", env, ListDirs));
    }

    [Fact]
    public void Wildcard_MatchesMultipleProfiles()
    {
        using var t = new TempTree();
        t.CreateDir(@"User Data\Default\Cache");
        t.CreateDir(@"User Data\Profile 1\Cache");
        t.CreateDir(@"User Data\Profile 2\Other");
        t.CreateDir(@"User Data\Profile 1\Code Cache");
        var env = new FakeEnvironment().Set("ROOT", t.Root);

        var cache = PathExpander.Expand(@"%ROOT%\User Data\*\Cache", env, ListDirs);
        Assert.Equal(2, cache.Count);
        Assert.Contains(cache, p => p.EndsWith(@"Default\Cache", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cache, p => p.EndsWith(@"Profile 1\Cache", StringComparison.OrdinalIgnoreCase));

        var code = PathExpander.Expand(@"%ROOT%\User Data\*\Code Cache", env, ListDirs);
        Assert.Single(code);
    }

    [Fact]
    public void Wildcard_WhenParentMissing_ReturnsEmpty()
    {
        using var t = new TempTree();
        var env = new FakeEnvironment().Set("ROOT", t.Root);
        Assert.Empty(PathExpander.Expand(@"%ROOT%\missing\*\Cache", env, ListDirs));
    }

    [Fact]
    public void RelativeOrUncPattern_ReturnsEmpty()
    {
        var env = new FakeEnvironment();
        Assert.Empty(PathExpander.Expand(@"relative\path", env, ListDirs));
        Assert.Empty(PathExpander.Expand(@"\\server\share\x", env, ListDirs));
    }
}

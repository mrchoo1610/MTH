using DiskCleaner.Services;

namespace DiskCleaner.Tests.Services;

public class SystemEnvironmentTests
{
    [Fact]
    public void ToLongPath_LeavesLongPathsUnchanged()
    {
        var dir = AppContext.BaseDirectory.TrimEnd('\\');
        Assert.Equal(dir, SystemEnvironment.ToLongPath(dir), ignoreCase: true);
    }

    [Fact]
    public void ToLongPath_NonExistingOrRelative_ReturnsInput()
    {
        Assert.Equal(@"C:\does-not-exist~1\x", SystemEnvironment.ToLongPath(@"C:\does-not-exist~1\x"));
        Assert.Equal(@"rel~1\x", SystemEnvironment.ToLongPath(@"rel~1\x"));
    }

    [Fact]
    public void Get_Temp_HasNoShortNameSegments()
    {
        // On machines/CI where TEMP is an 8.3 path (C:\Users\RUNNER~1\...), Get() must return the long form.
        var temp = new SystemEnvironment().Get("TEMP");
        Assert.NotNull(temp);
        Assert.DoesNotMatch(@"~\d", temp!);
    }
}

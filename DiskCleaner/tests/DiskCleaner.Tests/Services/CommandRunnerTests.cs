using DiskCleaner.Services;

namespace DiskCleaner.Tests.Services;

public class CommandRunnerTests
{
    [Fact]
    public void IsAvailable_FindsCmd_AndRejectsUnknown()
    {
        var runner = new CommandRunner();
        Assert.True(runner.IsAvailable("cmd"));
        Assert.False(runner.IsAvailable("definitely-not-a-real-command-xyz"));
    }

    [Fact]
    public void IsAvailable_RejectsPathsAndEmpty()
    {
        var runner = new CommandRunner();
        Assert.False(runner.IsAvailable(@"C:\Windows\System32\cmd"));
        Assert.False(runner.IsAvailable(""));
    }

    [Fact]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        var (code, output) = await new CommandRunner().RunAsync("cmd", "/c echo hello-test", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(0, code);
        Assert.Contains("hello-test", output);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ReturnsMinusOne()
    {
        var (code, _) = await new CommandRunner().RunAsync("definitely-not-a-real-command-xyz", "", TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(-1, code);
    }
}

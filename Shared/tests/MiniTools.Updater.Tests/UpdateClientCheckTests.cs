using System.Net;
using MiniTools.Contracts;

namespace MiniTools.Updater.Tests;

public class UpdateClientCheckTests
{
    private static readonly byte[] Pkg = Fx.Zip(("a.txt", "hello"));

    [Fact]
    public async Task Check_204_ReturnsNull_UpToDate()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var c = Fx.Client(h, pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
        Assert.Equal(UpdateCheckStatus.UpToDate, c.LastStatus);
        var q = h.Requests.Single();
        Assert.Equal("/api/apps/disk-cleaner/latest", q.AbsolutePath);
        Assert.Contains("current=1.2.0", q.Query);
        Assert.Contains("channel=stable", q.Query);
    }

    [Fact]
    public async Task Check_ValidSignedManifest_ReturnsIt()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var m = Fx.Manifest(Pkg, priv);
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(m)), pub, t.Path);
        var got = await c.CheckAsync(default);
        Assert.NotNull(got);
        Assert.Equal("1.3.0", got!.Version);
        Assert.Equal(UpdateCheckStatus.Available, c.LastStatus);
    }

    [Fact]
    public async Task Check_SignedWithOtherKey_ReturnsNull_AndLogs()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var (otherPriv, _) = Fx.Keys();
        var log = new List<string>();
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(Fx.Manifest(Pkg, otherPriv))), pub, t.Path, log);
        Assert.Null(await c.CheckAsync(default));
        Assert.Equal(UpdateCheckStatus.Failed, c.LastStatus);
        Assert.Contains(log, l => l.Contains("signature"));
    }

    [Fact]
    public async Task Check_TamperedAfterSigning_ReturnsNull()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var tampered = Fx.Manifest(Pkg, priv) with { Sha256 = new string('a', 64) };
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(tampered)), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
    }

    [Fact]
    public async Task Check_UnsignedManifest_ReturnsNull()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var unsigned = Fx.Manifest(Pkg, priv) with { Signature = null };
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(unsigned)), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a pem")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    public async Task Check_EmptyOrInvalidPublicKey_IsDisabled_NoHttpCall(string key)
    {
        using var t = new TempDir();
        var h = new FakeHandler(_ => throw new InvalidOperationException("must not be called"));
        var log = new List<string>();
        var c = Fx.Client(h, key, t.Path, log);
        Assert.False(c.IsEnabled);
        Assert.Null(await c.CheckAsync(default));
        Assert.Equal(UpdateCheckStatus.Disabled, c.LastStatus);
        Assert.Empty(h.Requests);
        Assert.Contains(log, l => l.Contains("disabled"));
    }

    [Fact]
    public async Task Check_NetworkError_ReturnsNull_DoesNotThrow()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var log = new List<string>();
        var c = Fx.Client(new FakeHandler(_ => throw new HttpRequestException("boom")), pub, t.Path, log);
        Assert.Null(await c.CheckAsync(default));
        Assert.Equal(UpdateCheckStatus.Failed, c.LastStatus);
        Assert.NotEmpty(log);
    }

    [Fact]
    public async Task Check_Http500_ReturnsNull()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var c = Fx.Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
        Assert.Equal(UpdateCheckStatus.Failed, c.LastStatus);
    }

    [Fact]
    public async Task Check_GarbageBody_ReturnsNull()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var c = Fx.Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") }), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
    }

    [Fact]
    public async Task Check_NotNewer_ReturnsNull()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(Fx.Manifest(Pkg, priv, version: "1.2.0"))), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
        Assert.Equal(UpdateCheckStatus.UpToDate, c.LastStatus);
    }

    [Fact]
    public async Task Check_OtherAppId_ReturnsNull()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(Fx.Manifest(Pkg, priv, appId: "taskbar-monitor"))), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
    }

    [Fact]
    public async Task Check_RequiresNewerUpdater_ReturnsNull()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var c = Fx.Client(new FakeHandler(_ => Fx.Json(Fx.Manifest(Pkg, priv, minUpdater: "9.0.0"))), pub, t.Path);
        Assert.Null(await c.CheckAsync(default));
    }

    [Fact]
    public async Task Check_RemovesStaleStagedUpdates()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var stale = t.Write(Path.Combine("updates", Fx.AppId, "1.2.0", "pkg.zip"), "x");
        var newer = t.Write(Path.Combine("updates", Fx.AppId, "1.3.0", "pkg.zip"), "x");
        var c = Fx.Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)), pub, t.Path);
        await c.CheckAsync(default);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(newer));
    }

    [Fact]
    public void HelperArguments_AreQuotedAndComplete()
    {
        var a = UpdateClient.BuildHelperArguments(42, @"C:\u p\files\", @"D:\apps\My App\", @"D:\apps\My App\x.exe", "1.3.0", true);
        Assert.Equal("--pid 42 --source \"C:\\u p\\files\" --target \"D:\\apps\\My App\" --restart \"D:\\apps\\My App\\x.exe\" --version \"1.3.0\" --deelevate", a);
        Assert.DoesNotContain("--deelevate", UpdateClient.BuildHelperArguments(1, "s", "t", "r", "1.0.0", false));
    }
}

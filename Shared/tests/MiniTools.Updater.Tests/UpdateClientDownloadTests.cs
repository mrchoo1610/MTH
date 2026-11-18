using System.Net;
using MiniTools.Contracts;

namespace MiniTools.Updater.Tests;

public class UpdateClientDownloadTests
{
    private sealed class Collect : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    [Fact]
    public async Task Download_Valid_VerifiesAndExtracts()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var zip = Fx.Zip(("App.exe", "exe-bytes"), ("Rules/rules.json", "{}"));
        var m = Fx.Manifest(zip, priv);
        var h = new FakeHandler(_ => Fx.Bytes(zip));
        var c = Fx.Client(h, pub, t.Path);
        var progress = new Collect();

        var p = await c.DownloadAsync(m, progress, default);

        Assert.Equal("/dl/disk-cleaner/1.3.0/App-v1.3.0-Portable.zip", h.Requests.Single().AbsolutePath);
        Assert.True(File.Exists(p.PackagePath));
        Assert.Equal(Path.Combine(t.Path, "updates", Fx.AppId, "1.3.0", "pkg.zip"), p.PackagePath);
        Assert.Equal("exe-bytes", File.ReadAllText(Path.Combine(p.FilesDir, "App.exe")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(p.FilesDir, "Rules", "rules.json")));
        Assert.Equal(new AppVersion(1, 3, 0), p.Version);
        Assert.NotEmpty(progress.Values);
        Assert.Equal(1.0, progress.Values[^1]);
    }

    [Fact]
    public async Task Download_WrongSha_Throws_AndDeletesFile()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var good = Fx.Zip(("a.txt", "good"));
        var evil = Fx.Zip(("a.txt", "evil")); // same size, different content
        Assert.Equal(good.Length, evil.Length);
        var m = Fx.Manifest(good, priv);
        var c = Fx.Client(new FakeHandler(_ => Fx.Bytes(evil)), pub, t.Path);

        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(m, null, default));

        var dir = Path.Combine(t.Path, "updates", Fx.AppId, "1.3.0");
        Assert.False(File.Exists(Path.Combine(dir, "pkg.zip")));
        Assert.False(File.Exists(Path.Combine(dir, "pkg.zip.part")));
        Assert.False(Directory.Exists(Path.Combine(dir, "files")));
    }

    [Fact]
    public async Task Download_WrongSize_Throws()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var good = Fx.Zip(("a.txt", "good"));
        var m = Fx.Manifest(good, priv);
        var c = Fx.Client(new FakeHandler(_ => Fx.Bytes(Fx.Zip(("a.txt", "good"), ("b.txt", "extra")))), pub, t.Path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(m, null, default));
        Assert.False(File.Exists(Path.Combine(t.Path, "updates", Fx.AppId, "1.3.0", "pkg.zip")));
    }

    [Fact]
    public async Task Download_TruncatedFile_Throws()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var good = Fx.Zip(("a.txt", "some longer content here"));
        var m = Fx.Manifest(good, priv);
        var c = Fx.Client(new FakeHandler(_ => Fx.Bytes(good[..^5])), pub, t.Path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(m, null, default));
    }

    [Fact]
    public async Task Download_BadSignature_ThrowsBeforeAnyRequest()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var zip = Fx.Zip(("a.txt", "x"));
        var m = Fx.Manifest(zip, priv) with { Version = "1.9.9" }; // signature no longer matches
        var h = new FakeHandler(_ => Fx.Bytes(zip));
        var c = Fx.Client(h, pub, t.Path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(m, null, default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Download_WhenDisabled_Throws()
    {
        using var t = new TempDir();
        var (priv, _) = Fx.Keys();
        var zip = Fx.Zip(("a.txt", "x"));
        var h = new FakeHandler(_ => Fx.Bytes(zip));
        var c = Fx.Client(h, "", t.Path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(Fx.Manifest(zip, priv), null, default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Download_ZipSlipEntry_Throws_NothingWrittenOutside()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var zip = Fx.Zip(("ok.txt", "fine"), ("../evil.txt", "pwned"));
        var m = Fx.Manifest(zip, priv);
        var c = Fx.Client(new FakeHandler(_ => Fx.Bytes(zip)), pub, t.Path);

        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(m, null, default));

        var verDir = Path.Combine(t.Path, "updates", Fx.AppId, "1.3.0");
        Assert.False(File.Exists(Path.Combine(verDir, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(verDir, "files", "ok.txt")), "validation must happen before extraction");
        Assert.False(File.Exists(Path.Combine(verDir, "pkg.zip")));
    }

    [Theory]
    [InlineData("..\\..\\evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("/evil.txt")]
    public async Task Download_OtherUnsafeEntryNames_Throw(string name)
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var zip = Fx.Zip((name, "pwned"));
        var m = Fx.Manifest(zip, priv);
        var c = Fx.Client(new FakeHandler(_ => Fx.Bytes(zip)), pub, t.Path);
        await Assert.ThrowsAsync<UpdateVerificationException>(() => c.DownloadAsync(m, null, default));
    }

    [Fact]
    public async Task Download_Http404_Throws()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var zip = Fx.Zip(("a.txt", "x"));
        var c = Fx.Client(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), pub, t.Path);
        await Assert.ThrowsAnyAsync<Exception>(() => c.DownloadAsync(Fx.Manifest(zip, priv), null, default));
        Assert.False(File.Exists(Path.Combine(t.Path, "updates", Fx.AppId, "1.3.0", "pkg.zip.part")));
    }

    [Fact]
    public async Task Download_Cancelled_LeavesNoPartialFile()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var zip = Fx.Zip(("a.txt", "x"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var c = Fx.Client(new FakeHandler(_ => Fx.Bytes(zip)), pub, t.Path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.DownloadAsync(Fx.Manifest(zip, priv), null, cts.Token));
        Assert.False(File.Exists(Path.Combine(t.Path, "updates", Fx.AppId, "1.3.0", "pkg.zip.part")));
    }
}

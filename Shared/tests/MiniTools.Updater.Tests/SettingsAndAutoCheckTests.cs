using System.Net;
using MiniTools.Contracts;
using MiniTools.Updater.UI;

namespace MiniTools.Updater.Tests;

public class SettingsAndAutoCheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Store_Defaults_RoundTrip_AndCorruptFile()
    {
        using var t = new TempDir();
        var s = new UpdateSettingsStore("disk-cleaner", t.Path);
        s.Load();
        Assert.True(s.AutoCheck);
        Assert.Null(s.LastCheckUtc);
        Assert.Null(s.SkippedVersion);
        Assert.EndsWith(Path.Combine("disk-cleaner", "update.json"), s.FilePath);

        s.AutoCheck = false;
        s.LastCheckUtc = T0;
        s.SkippedVersion = "1.4.0";
        s.Save();

        var s2 = new UpdateSettingsStore("disk-cleaner", t.Path);
        s2.Load();
        Assert.False(s2.AutoCheck);
        Assert.Equal(T0, s2.LastCheckUtc);
        Assert.Equal("1.4.0", s2.SkippedVersion);

        File.WriteAllText(s.FilePath, "{ not json");
        var s3 = new UpdateSettingsStore("disk-cleaner", t.Path);
        s3.Load();
        Assert.True(s3.AutoCheck);
        Assert.Null(s3.LastCheckUtc);
    }

    [Fact]
    public void IsAutoCheckDue_Respects24hAndAutoCheckFlag()
    {
        var s = new UpdateSettingsStore("x", Path.GetTempPath());
        Assert.True(s.IsAutoCheckDue(T0));
        s.LastCheckUtc = T0;
        Assert.False(s.IsAutoCheckDue(T0.AddHours(23)));
        Assert.True(s.IsAutoCheckDue(T0.AddHours(24)));
        Assert.True(s.IsAutoCheckDue(T0.AddHours(-1)), "clock went backwards: do not block forever");
        s.AutoCheck = false;
        Assert.False(s.IsAutoCheckDue(T0.AddDays(5)));
    }

    private static (AutoUpdateChecker Checker, List<ReleaseManifest> Found, List<TimeSpan> Delays, FakeHandler Handler, UpdateSettingsStore Store)
        Build(TempDir t, Func<bool> busy, ReleaseManifest? manifest, string pub, Action<UpdateSettingsStore>? prep = null)
    {
        var handler = new FakeHandler(_ => manifest is null ? new HttpResponseMessage(HttpStatusCode.NoContent) : Fx.Json(manifest));
        var client = Fx.Client(handler, pub, t.Path);
        var store = new UpdateSettingsStore(Fx.AppId, t.Path);
        prep?.Invoke(store);
        store.Save();
        var found = new List<ReleaseManifest>();
        var delays = new List<TimeSpan>();
        var checker = new AutoUpdateChecker(client, store, busy, found.Add, (d, _) => { delays.Add(d); return Task.CompletedTask; }, () => T0);
        return (checker, found, delays, handler, store);
    }

    [Fact]
    public async Task AutoCheck_WaitsTenSeconds_ThenReportsAndRecordsTime()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var m = Fx.Manifest(Fx.Zip(("a", "b")), priv);
        var (c, found, delays, h, store) = Build(t, () => false, m, pub);

        await c.RunAsync(default);

        Assert.Equal(TimeSpan.FromSeconds(10), delays[0]);
        Assert.Single(found);
        Assert.Single(h.Requests);
        Assert.Equal(T0, store.LastCheckUtc);
    }

    [Fact]
    public async Task AutoCheck_NotDue_DoesNotCallServer()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var m = Fx.Manifest(Fx.Zip(("a", "b")), priv);
        var (c, found, _, h, _) = Build(t, () => false, m, pub, s => s.LastCheckUtc = T0.AddHours(-3));
        await c.RunAsync(default);
        Assert.Empty(h.Requests);
        Assert.Empty(found);
    }

    [Fact]
    public async Task AutoCheck_Disabled_DoesNotCallServer()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var m = Fx.Manifest(Fx.Zip(("a", "b")), priv);
        var (c, _, _, h, _) = Build(t, () => false, m, pub, s => s.AutoCheck = false);
        await c.RunAsync(default);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task AutoCheck_WaitsWhileBusy()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var m = Fx.Manifest(Fx.Zip(("a", "b")), priv);
        var busyCalls = 0;
        var (c, found, delays, h, _) = Build(t, () => ++busyCalls <= 2, m, pub);
        await c.RunAsync(default);
        Assert.Equal(3, delays.Count);                 // initial + 2 busy waits
        Assert.Equal(TimeSpan.FromSeconds(30), delays[1]);
        Assert.Single(h.Requests);
        Assert.Single(found);
    }

    [Fact]
    public async Task AutoCheck_SkippedVersion_IsNotReported()
    {
        using var t = new TempDir();
        var (priv, pub) = Fx.Keys();
        var m = Fx.Manifest(Fx.Zip(("a", "b")), priv, version: "1.3.0");
        var (c, found, _, h, _) = Build(t, () => false, m, pub, s => s.SkippedVersion = "1.3.0");
        await c.RunAsync(default);
        Assert.Single(h.Requests);
        Assert.Empty(found);
    }

    [Fact]
    public async Task AutoCheck_FailedCheck_DoesNotConsumeTheDailySlot()
    {
        using var t = new TempDir();
        var (_, pub) = Fx.Keys();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var store = new UpdateSettingsStore(Fx.AppId, t.Path);
        var checker = new AutoUpdateChecker(Fx.Client(handler, pub, t.Path), store, () => false, _ => { },
            (_, _) => Task.CompletedTask, () => T0);
        await checker.RunAsync(default);
        Assert.Null(store.LastCheckUtc);
    }

    [Fact]
    public void Strings_PickNotes_FallsBackToEnglishThenAny()
    {
        var m = Fx.Manifest(Fx.Zip(("a", "b")), Fx.Keys().Private);
        Assert.Equal("ban moi", UpdateStrings.PickNotes(m, "vi"));
        Assert.Equal("new version", UpdateStrings.PickNotes(m, "fr"));
        Assert.Equal("only-de", UpdateStrings.PickNotes(m with { Notes = new() { ["de"] = "only-de" } }, "vi"));
        Assert.Equal("", UpdateStrings.PickNotes(m with { Notes = new() }, "vi"));
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(5_242_880L, "5 MB")]
    public void Strings_FormatSize(long bytes, string expected) =>
        Assert.Equal(expected, UpdateStrings.FormatSize(bytes));

    [Fact]
    public void Strings_ForLanguage_AllFieldsFilled()
    {
        foreach (var s in new[] { UpdateStrings.For("vi"), UpdateStrings.For("en") })
            foreach (var p in typeof(UpdateStrings).GetProperties().Where(p => p.PropertyType == typeof(string)))
                Assert.False(string.IsNullOrWhiteSpace((string?)p.GetValue(s)), p.Name);
        Assert.Same(UpdateStrings.En, UpdateStrings.For("EN"));
        Assert.Same(UpdateStrings.Vi, UpdateStrings.For("vi"));
    }
}

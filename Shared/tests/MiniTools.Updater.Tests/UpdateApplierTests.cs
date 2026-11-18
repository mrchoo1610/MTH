using MiniTools.UpdateHelper;

namespace MiniTools.Updater.Tests;

public class UpdateApplierTests
{
    private sealed class Env : IDisposable
    {
        public TempDir Root = new();
        public string Source => Root.P("src");
        public string Target => Root.P("app");
        public string Backup => Root.P("app.bak-1.3.0");

        public Env()
        {
            // installed app
            Root.Write("app/App.exe", "old-exe");
            Root.Write("app/a.txt", "old-a");
            Root.Write("app/b.txt", "old-b");
            Root.Write("app/Rules/rules.json", "old-rules");
            Root.Write("app/settings.json", "USER-SETTINGS");       // not in the package
            Root.Write("app/logs/user.log", "USER-LOG");            // not in the package
            // package
            Root.Write("src/App.exe", "new-exe");
            Root.Write("src/a.txt", "new-a");
            Root.Write("src/b.txt", "new-b");
            Root.Write("src/c.txt", "brand-new-c");
            Root.Write("src/Rules/rules.json", "new-rules");
            Root.Write("src/newdir/deep/d.txt", "new-d");
            Directory.CreateDirectory(Root.P("outside"));
            Root.Write("outside/keep.txt", "outside");
        }

        public string Read(string rel) => File.ReadAllText(Root.P("app", rel));
        public void Dispose() => Root.Dispose();
    }

    [Fact]
    public void Apply_OverwritesPackageFiles_AddsNew_KeepsUserFiles_RemovesBackup()
    {
        using var e = new Env();
        var r = new UpdateApplier().Apply(e.Source, e.Target, e.Backup);

        Assert.True(r.Success);
        Assert.Equal(6, r.FilesCopied);
        Assert.Equal("new-exe", e.Read("App.exe"));
        Assert.Equal("new-a", e.Read("a.txt"));
        Assert.Equal("new-rules", e.Read(Path.Combine("Rules", "rules.json")));
        Assert.Equal("brand-new-c", e.Read("c.txt"));
        Assert.Equal("new-d", e.Read(Path.Combine("newdir", "deep", "d.txt")));
        Assert.Equal("USER-SETTINGS", e.Read("settings.json"));
        Assert.Equal("USER-LOG", e.Read(Path.Combine("logs", "user.log")));
        Assert.False(Directory.Exists(e.Backup));
        Assert.Equal("outside", File.ReadAllText(e.Root.P("outside", "keep.txt")));
        Assert.Empty(Directory.GetFiles(e.Target, "*.mtupd.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Apply_FailureMidWay_RollsBackEverything()
    {
        using var e = new Env();
        // Sorted order: App.exe, a.txt, b.txt, c.txt, newdir/deep/d.txt, Rules/rules.json. Fail after c.txt.
        var applier = new UpdateApplier
        {
            AfterFileInstalled = rel => { if (rel == "c.txt") throw new IOException("disk full (simulated)"); }
        };

        var r = applier.Apply(e.Source, e.Target, e.Backup);

        Assert.False(r.Success);
        Assert.True(r.RolledBack);
        Assert.Equal("old-exe", e.Read("App.exe"));
        Assert.Equal("old-a", e.Read("a.txt"));
        Assert.Equal("old-b", e.Read("b.txt"));
        Assert.Equal("old-rules", e.Read(Path.Combine("Rules", "rules.json")));
        Assert.False(File.Exists(e.Root.P("app", "c.txt")), "newly created file must be removed");
        Assert.False(Directory.Exists(e.Root.P("app", "newdir")), "newly created directories must be removed");
        Assert.Equal("USER-SETTINGS", e.Read("settings.json"));
        Assert.Equal("USER-LOG", e.Read(Path.Combine("logs", "user.log")));
        Assert.Empty(Directory.GetFiles(e.Target, "*.mtupd.tmp", SearchOption.AllDirectories));
        Assert.Equal("outside", File.ReadAllText(e.Root.P("outside", "keep.txt")));
    }

    [Fact]
    public void Apply_LockedTargetFile_FailsAndRollsBack()
    {
        using var e = new Env();
        using var lockFile = new FileStream(e.Root.P("app", "b.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        var applier = new UpdateApplier { RetryCount = 2, RetryDelay = TimeSpan.FromMilliseconds(10) };

        var r = applier.Apply(e.Source, e.Target, e.Backup);

        Assert.False(r.Success);
        lockFile.Dispose();
        Assert.Equal("old-exe", e.Read("App.exe"));
        Assert.Equal("old-a", e.Read("a.txt"));
        Assert.Equal("old-b", e.Read("b.txt"));
        Assert.False(File.Exists(e.Root.P("app", "c.txt")));
        Assert.Equal("USER-SETTINGS", e.Read("settings.json"));
    }

    [Fact]
    public void Apply_NeverDeletesFilesMissingFromPackage()
    {
        using var e = new Env();
        e.Root.Write("app/extra-user-file.dat", "mine");
        new UpdateApplier().Apply(e.Source, e.Target, e.Backup);
        Assert.Equal("mine", e.Read("extra-user-file.dat"));
    }

    [Fact]
    public void Apply_MissingSourceOrTarget_FailsWithoutChanges()
    {
        using var e = new Env();
        Assert.False(new UpdateApplier().Apply(e.Root.P("nope"), e.Target, e.Backup).Success);
        Assert.False(new UpdateApplier().Apply(e.Source, e.Root.P("nope"), e.Backup).Success);
        Assert.Equal("old-a", e.Read("a.txt"));
        Assert.False(Directory.Exists(e.Backup));
    }

    [Fact]
    public void Apply_EmptyPackage_Fails()
    {
        using var e = new Env();
        Directory.CreateDirectory(e.Root.P("empty"));
        var r = new UpdateApplier().Apply(e.Root.P("empty"), e.Target, e.Backup);
        Assert.False(r.Success);
        Assert.Equal("old-a", e.Read("a.txt"));
    }

    [Fact]
    public void Apply_ExistingBackupFolder_IsNotReusedOrDeleted()
    {
        using var e = new Env();
        e.Root.Write("app.bak-1.3.0/precious.txt", "older backup");
        var r = new UpdateApplier().Apply(e.Source, e.Target, e.Backup);
        Assert.True(r.Success);
        Assert.Equal("older backup", File.ReadAllText(e.Root.P("app.bak-1.3.0", "precious.txt")));
    }
}

namespace MiniTools.UpdateHelper;

public sealed record ApplyResult(bool Success, int FilesCopied, string? Error, bool RolledBack);

/// <summary>
/// Copies the files of an extracted update package over a target folder.
/// Backs up every file it is about to overwrite, rolls everything back on any failure,
/// never writes outside the target folder and never deletes a file that is not part of the package.
/// </summary>
public sealed class UpdateApplier
{
    private readonly Action<string> _log;

    public UpdateApplier(Action<string>? log = null) => _log = log ?? (_ => { });

    /// <summary>Attempts per file (locked files are retried while the old process finishes exiting).</summary>
    public int RetryCount { get; init; } = 20;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Test hook: invoked with the relative path after each file was installed.</summary>
    public Action<string>? AfterFileInstalled { get; init; }

    private sealed class Entry
    {
        public required string Rel { get; init; }
        public required string Source { get; init; }
        public required string Dest { get; init; }
        public string? Backup { get; set; }   // null => the file did not exist before
        public bool Installed { get; set; }
    }

    public ApplyResult Apply(string sourceDir, string targetDir, string backupDir)
    {
        var applied = new List<Entry>();
        var createdDirs = new List<string>();
        string? backupRoot = null;
        try
        {
            var source = Normalize(sourceDir);
            var target = Normalize(targetDir);
            if (!Directory.Exists(source)) return Fail($"Source folder not found: {source}");
            if (!Directory.Exists(target)) return Fail($"Target folder not found: {target}");

            // 1. Plan + validate every path BEFORE touching anything.
            var plan = new List<Entry>();
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(source, file);
                if (Path.IsPathRooted(rel) || rel.StartsWith("..", StringComparison.Ordinal))
                    return Fail($"Unsafe path in package: {rel}");
                var dest = Path.GetFullPath(Path.Combine(target, rel));
                if (!dest.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                    return Fail($"Path escapes the target folder: {rel}");
                plan.Add(new Entry { Rel = rel, Source = file, Dest = dest });
            }
            if (plan.Count == 0) return Fail("Package is empty.");

            // 2. Back up the files that will be overwritten (a failure here aborts before any change).
            backupRoot = UniqueDir(Path.GetFullPath(backupDir));
            Directory.CreateDirectory(backupRoot);
            foreach (var e in plan.Where(p => File.Exists(p.Dest)))
            {
                var b = Path.Combine(backupRoot, e.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(b)!);
                Retry(() => File.Copy(e.Dest, b, true), "backup " + e.Rel);
                e.Backup = b;
            }
            _log($"Backed up {plan.Count(p => p.Backup != null)} file(s) to {backupRoot}.");

            // 3. Install.
            foreach (var e in plan)
            {
                var dir = Path.GetDirectoryName(e.Dest)!;
                MakeDirs(dir, target, createdDirs);
                var tmp = e.Dest + ".mtupd.tmp";
                try
                {
                    File.Copy(e.Source, tmp, true);
                    Retry(() => File.Move(tmp, e.Dest, true), "replace " + e.Rel);
                }
                finally
                {
                    if (File.Exists(tmp)) { try { File.Delete(tmp); } catch (Exception) { } }
                }
                e.Installed = true;
                applied.Add(e);
                AfterFileInstalled?.Invoke(e.Rel);
            }

            // 4. Success: drop the backup.
            try { Directory.Delete(backupRoot, true); } catch (Exception ex) { _log("Could not delete backup: " + ex.Message); }
            _log($"Installed {applied.Count} file(s).");
            return new ApplyResult(true, applied.Count, null, false);
        }
        catch (Exception ex)
        {
            _log("Update failed: " + ex.Message + " - rolling back.");
            var ok = Rollback(applied, createdDirs);
            if (ok && backupRoot != null) { try { Directory.Delete(backupRoot, true); } catch (Exception) { } }
            return new ApplyResult(false, 0, ex.Message, ok);
        }
    }

    private bool Rollback(List<Entry> applied, List<string> createdDirs)
    {
        var allOk = true;
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            var e = applied[i];
            try
            {
                if (e.Backup != null) Retry(() => File.Copy(e.Backup, e.Dest, true), "restore " + e.Rel);
                else if (File.Exists(e.Dest)) Retry(() => File.Delete(e.Dest), "remove " + e.Rel);
            }
            catch (Exception ex)
            {
                allOk = false;
                _log($"Rollback of {e.Rel} failed: {ex.Message}");
            }
        }
        // Only directories this run created, deepest first, only when empty.
        foreach (var d in createdDirs.AsEnumerable().Reverse())
        {
            try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); }
            catch (Exception) { }
        }
        _log(allOk ? "Rollback complete." : "Rollback INCOMPLETE; see backup folder.");
        return allOk;
    }

    private void Retry(Action action, string what)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { action(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < RetryCount)
            {
                _log($"{what}: {ex.Message} (attempt {attempt}/{RetryCount})");
                Thread.Sleep(RetryDelay);
            }
        }
    }

    private static void MakeDirs(string dir, string target, List<string> created)
    {
        if (Directory.Exists(dir)) return;
        var parent = Path.GetDirectoryName(dir);
        if (parent != null && parent.Length > target.Length) MakeDirs(parent, target, created);
        Directory.CreateDirectory(dir);
        created.Add(dir);
    }

    private static string UniqueDir(string dir)
    {
        if (!Directory.Exists(dir)) return dir;
        for (var i = 2; ; i++)
            if (!Directory.Exists($"{dir}-{i}")) return $"{dir}-{i}";
    }

    private static string Normalize(string p) =>
        Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private ApplyResult Fail(string message)
    {
        _log(message);
        return new ApplyResult(false, 0, message, false);
    }
}

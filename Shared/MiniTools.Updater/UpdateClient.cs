using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MiniTools.Contracts;

namespace MiniTools.Updater;

/// <summary>
/// Checks the hub for a newer signed release, downloads + verifies it (signature, size, sha256, zip-slip)
/// and hands it to UpdateHelper. With an empty/invalid public key the client is disabled and never
/// checks or installs anything.
/// </summary>
public sealed class UpdateClient
{
    public static readonly AppVersion UpdaterVersion = new(1, 0, 0);
    public const string HelperExeName = "MiniTools.UpdateHelper.exe";
    private const long MaxExtractedBytes = 1L << 30; // 1 GB zip-bomb guard

    private readonly UpdateOptions _o;
    private readonly HttpClient _http;
    private readonly string _publicKeyPem;
    private readonly Action<string> _log;
    private readonly string _dataRoot;
    private readonly string _disabledReason;

    public UpdateClient(UpdateOptions options, HttpClient http, string publicKeyPem,
        Action<string>? log = null, string? dataRoot = null)
    {
        _o = options;
        _http = http;
        _publicKeyPem = publicKeyPem ?? "";
        _log = log ?? (_ => { });
        _dataRoot = dataRoot ?? UpdatePaths.DataRoot();
        IsEnabled = IsUsablePublicKey(_publicKeyPem, out _disabledReason);
    }

    public UpdateOptions Options => _o;

    /// <summary>False when the public key is empty/invalid.</summary>
    public bool IsEnabled { get; }

    public UpdateCheckStatus LastStatus { get; private set; } = UpdateCheckStatus.NotChecked;
    public string UpdatesRoot => UpdatePaths.UpdatesRoot(_dataRoot);

    public static bool IsUsablePublicKey(string? pem, out string reason)
    {
        if (string.IsNullOrWhiteSpace(pem)) { reason = "public key is empty"; return false; }
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(ManifestSigner.NormalizePem(pem));
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = "public key is invalid (" + ex.GetType().Name + ")";
            return false;
        }
    }

    private string Base => _o.HubBaseUrl.ToString().TrimEnd('/') + "/";

    /// <summary>The newest verified release, or null when up to date / disabled / on any error (logged, never throws).</summary>
    public async Task<ReleaseManifest?> CheckAsync(CancellationToken ct)
    {
        if (!IsEnabled)
        {
            LastStatus = UpdateCheckStatus.Disabled;
            _log($"Updates disabled: {_disabledReason}. Run scripts/new-signing-key.ps1 and rebuild.");
            return null;
        }

        try
        {
            CleanupStaleUpdates();
            var url = Base + $"api/apps/{Uri.EscapeDataString(_o.AppId)}/latest?channel={Uri.EscapeDataString(_o.Channel)}&current={Uri.EscapeDataString(_o.CurrentVersion.ToString())}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NoContent)
            {
                LastStatus = UpdateCheckStatus.UpToDate;
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                LastStatus = UpdateCheckStatus.Failed;
                _log($"Update check failed: HTTP {(int)resp.StatusCode}.");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var m = ManifestJson.Deserialize(json);
            var problem = m is null ? "empty manifest" : ValidateManifest(m);
            if (problem is not null)
            {
                LastStatus = UpdateCheckStatus.Failed;
                _log($"Update check rejected the manifest: {problem}.");
                return null;
            }

            if (AppVersion.Parse(m!.Version) <= _o.CurrentVersion)
            {
                LastStatus = UpdateCheckStatus.UpToDate;
                return null;
            }
            LastStatus = UpdateCheckStatus.Available;
            return m;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LastStatus = UpdateCheckStatus.Failed;
            return null;
        }
        catch (Exception ex)
        {
            LastStatus = UpdateCheckStatus.Failed;
            _log($"Update check failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns null when the manifest is acceptable for this app, else the reason.</summary>
    private string? ValidateManifest(ReleaseManifest m)
    {
        var bad = m.Validate();
        if (bad is not null) return bad;
        if (!ManifestSigner.Verify(m, _publicKeyPem)) return "signature missing or invalid";
        if (!string.Equals(m.AppId, _o.AppId, StringComparison.Ordinal)) return "appId mismatch";
        if (!string.Equals(m.Channel, _o.Channel, StringComparison.Ordinal)) return "channel mismatch";
        if (!AppVersion.TryParse(m.MinUpdaterVersion, out var min) || min > UpdaterVersion)
            return "requires a newer updater (" + m.MinUpdaterVersion + ")";
        return null;
    }

    /// <summary>
    /// Downloads to %LOCALAPPDATA%\MiniTools\updates\{appId}\{version}\pkg.zip, verifies signature + size + sha256
    /// (on mismatch deletes the file and throws), then extracts to ...\files\ (zip-slip safe).
    /// </summary>
    public async Task<PreparedUpdate> DownloadAsync(ReleaseManifest m, IProgress<double>? progress, CancellationToken ct)
    {
        if (!IsEnabled) throw new UpdateVerificationException("Updates are disabled: " + _disabledReason + ".");
        var problem = ValidateManifest(m);
        if (problem is not null) throw new UpdateVerificationException("Manifest rejected: " + problem + ".");

        var dir = Path.Combine(UpdatesRoot, m.AppId, m.Version);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var pkg = Path.Combine(dir, "pkg.zip");
        var part = pkg + ".part";
        var filesDir = Path.Combine(dir, "files");

        var url = Base + $"dl/{Uri.EscapeDataString(m.AppId)}/{Uri.EscapeDataString(m.Version)}/{Uri.EscapeDataString(m.FileName)}";
        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            using var sha = SHA256.Create();
            long total = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > m.SizeBytes) throw new UpdateVerificationException("Downloaded file is larger than the signed size.");
                    sha.TransformBlock(buf, 0, n, null, 0);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Report(Math.Min(1.0, (double)total / m.SizeBytes));
                }
            }
            sha.TransformFinalBlock([], 0, 0);

            if (total != m.SizeBytes) throw new UpdateVerificationException($"Size mismatch: expected {m.SizeBytes}, got {total}.");
            var actual = Convert.ToHexStringLower(sha.Hash!);
            if (!string.Equals(actual, m.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateVerificationException("SHA-256 mismatch.");

            File.Move(part, pkg, true);
            Extract(pkg, filesDir);
            progress?.Report(1.0);
            return new PreparedUpdate { Manifest = m, PackagePath = pkg, FilesDir = filesDir };
        }
        catch
        {
            TryDelete(part);
            TryDelete(pkg);
            TryDeleteDir(filesDir);
            throw;
        }
    }

    /// <summary>Extracts every entry into <paramref name="destDir"/>; any entry escaping it throws before anything is written.</summary>
    internal static void Extract(string zipPath, string destDir)
    {
        var dest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);

        long total = 0;
        foreach (var e in zip.Entries) total += e.Length;
        if (total > MaxExtractedBytes) throw new UpdateVerificationException("Package is too large when extracted.");

        var plan = new List<(ZipArchiveEntry Entry, string Path)>();
        foreach (var e in zip.Entries)
        {
            if (e.FullName.Contains(':') || Path.IsPathRooted(e.FullName))
                throw new UpdateVerificationException($"Unsafe path in package: {e.FullName}");
            var full = Path.GetFullPath(Path.Combine(dest, e.FullName));
            if (!full.StartsWith(dest, StringComparison.OrdinalIgnoreCase))
                throw new UpdateVerificationException($"Unsafe path in package (zip-slip): {e.FullName}");
            plan.Add((e, full));
        }

        Directory.CreateDirectory(dest);
        foreach (var (e, full) in plan)
        {
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(full); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            e.ExtractToFile(full, true);
        }
    }

    /// <summary>Removes staged updates for versions not newer than the running one. Best effort.</summary>
    private void CleanupStaleUpdates()
    {
        try
        {
            var appDir = Path.Combine(UpdatesRoot, _o.AppId);
            if (!Directory.Exists(appDir)) return;
            foreach (var d in Directory.EnumerateDirectories(appDir))
                if (AppVersion.TryParse(Path.GetFileName(d), out var v) && v <= _o.CurrentVersion)
                    TryDeleteDir(d);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Copies UpdateHelper to a scratch folder (so it does not lock the app folder), starts it, then shuts the app down.
    /// </summary>
    public void ApplyAndRestart(PreparedUpdate u)
    {
        StartHelper(u);
        if (System.Windows.Application.Current is { } app) app.Dispatcher.Invoke(app.Shutdown);
        else Environment.Exit(0);
    }

    /// <summary>Starts UpdateHelper without shutting the app down. Target/restart/pid are overridable for tests.</summary>
    public Process StartHelper(PreparedUpdate u, string? targetDir = null, string? restartExe = null, int? waitPid = null)
    {
        var target = (targetDir ?? AppContext.BaseDirectory).TrimEnd('\\', '/');
        var exe = restartExe ?? Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the app executable.");
        var helper = CopyHelper(target);

        var elevate = !CanWrite(target);
        var args = BuildHelperArguments(waitPid ?? Environment.ProcessId, u.FilesDir, target, exe, u.Manifest.Version, elevate);
        _log($"Starting helper: {helper} {args}");
        var psi = new ProcessStartInfo(helper, args)
        {
            UseShellExecute = elevate,
            WorkingDirectory = Path.GetDirectoryName(helper)!,
            CreateNoWindow = true,
        };
        if (elevate) psi.Verb = "runas";
        return Process.Start(psi) ?? throw new InvalidOperationException("Could not start UpdateHelper.");
    }

    internal static string BuildHelperArguments(int pid, string source, string target, string restart, string version, bool deElevate)
    {
        var sb = new StringBuilder();
        sb.Append("--pid ").Append(pid);
        sb.Append(" --source ").Append(Quote(source.TrimEnd('\\', '/')));
        sb.Append(" --target ").Append(Quote(target.TrimEnd('\\', '/')));
        sb.Append(" --restart ").Append(Quote(restart));
        sb.Append(" --version ").Append(Quote(version));
        if (deElevate) sb.Append(" --deelevate");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";

    private string CopyHelper(string targetDir)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, HelperExeName);
        if (!File.Exists(exe)) exe = Path.Combine(targetDir, HelperExeName);
        if (!File.Exists(exe))
            throw new InvalidOperationException($"{HelperExeName} was not found next to the application; this build cannot self-update.");

        var helperRoot = Path.Combine(UpdatesRoot, "helper");
        var scratch = Path.Combine(helperRoot, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Environment.ProcessId);
        Directory.CreateDirectory(scratch);
        var from = Path.GetDirectoryName(exe)!;
        foreach (var f in Directory.EnumerateFiles(from, "MiniTools.UpdateHelper*"))
        {
            if (f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(scratch, Path.GetFileName(f)), true);
        }

        // Best-effort cleanup of scratch folders from earlier runs.
        try
        {
            foreach (var d in Directory.EnumerateDirectories(helperRoot))
                if (!string.Equals(d, scratch, StringComparison.OrdinalIgnoreCase)
                    && Directory.GetCreationTimeUtc(d) < DateTime.UtcNow.AddDays(-1)) TryDeleteDir(d);
        }
        catch (Exception) { }
        return Path.Combine(scratch, HelperExeName);
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".mt-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { } }
    private static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception) { } }
}

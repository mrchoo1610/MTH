using System.Security.Cryptography;
using System.Text;
using MiniTools.Contracts;

// ManifestTool: helper for scripts/release.ps1 and scripts/new-signing-key.ps1.
//   keygen --out <private.pem> [--force]        create ECDSA P-256 key; writes private PEM, prints PUBLIC PEM to stdout
//   sign   --app-id --version --channel --zip <file> --key <private.pem> --out <manifest.json>
//          [--notes-vi-file f] [--notes-en-file f] [--min-updater 1.0.0] [--file-name name.zip]
//   verify --manifest <json> --public-key <pem|file>
//   token                                       random admin token + its SHA-256 (hex)
return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0) return Usage();
    var cmd = args[0].ToLowerInvariant();
    var opts = ParseOptions(args.Skip(1).ToArray());
    try
    {
        return cmd switch
        {
            "keygen" => KeyGen(opts),
            "sign" => Sign(opts),
            "verify" => Verify(opts),
            "token" => Token(),
            _ => Usage(),
        };
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or FormatException or CryptographicException)
    {
        Console.Error.WriteLine("ERROR: " + ex.Message);
        return 1;
    }
}

static int Usage()
{
    Console.Error.WriteLine("Usage: ManifestTool keygen|sign|verify|token [options]  (see Program.cs header)");
    return 2;
}

static Dictionary<string, string> ParseOptions(string[] a)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < a.Length; i++)
    {
        if (!a[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{a[i]}'.");
        var key = a[i][2..];
        if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) d[key] = a[++i];
        else d[key] = "true";
    }
    return d;
}

static string Req(Dictionary<string, string> o, string key) =>
    o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"Missing --{key}.");

static int KeyGen(Dictionary<string, string> o)
{
    var path = Path.GetFullPath(Req(o, "out"));
    if (File.Exists(path) && !o.ContainsKey("force"))
        throw new IOException($"Key already exists: {path} (use --force to overwrite).");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var (priv, pub) = ManifestSigner.GenerateKeyPair();
    File.WriteAllText(path, priv, new UTF8Encoding(false));
    Console.Out.Write(pub);
    return 0;
}

static int Sign(Dictionary<string, string> o)
{
    var zip = Path.GetFullPath(Req(o, "zip"));
    if (!File.Exists(zip)) throw new IOException($"Zip not found: {zip}");
    var version = AppVersion.Parse(Req(o, "version")).ToString();
    var notes = new Dictionary<string, string>
    {
        ["vi"] = ReadNotes(o, "notes-vi-file") ?? $"Phiên bản {version}.",
        ["en"] = ReadNotes(o, "notes-en-file") ?? $"Version {version}.",
    };

    string sha;
    using (var fs = File.OpenRead(zip))
        sha = Convert.ToHexStringLower(SHA256.HashData(fs));

    var manifest = new ReleaseManifest
    {
        AppId = Req(o, "app-id"),
        Version = version,
        Channel = o.GetValueOrDefault("channel", "stable"),
        FileName = o.GetValueOrDefault("file-name") ?? Path.GetFileName(zip),
        SizeBytes = new FileInfo(zip).Length,
        Sha256 = sha,
        PublishedAt = DateTimeOffset.UtcNow,
        MinUpdaterVersion = o.GetValueOrDefault("min-updater", "1.0.0"),
        Notes = notes,
    };
    var err = manifest.Validate();
    if (err is not null) throw new ArgumentException(err);

    var signed = manifest with { Signature = ManifestSigner.Sign(manifest, File.ReadAllText(Path.GetFullPath(Req(o, "key")))) };
    File.WriteAllText(Path.GetFullPath(Req(o, "out")), ManifestJson.Serialize(signed, indented: true), new UTF8Encoding(false));
    Console.WriteLine($"Signed {signed.AppId} {signed.Version} sha256={signed.Sha256} size={signed.SizeBytes}");
    return 0;
}

static string? ReadNotes(Dictionary<string, string> o, string key)
{
    if (!o.TryGetValue(key, out var path)) return null;
    var text = File.ReadAllText(Path.GetFullPath(path), Encoding.UTF8).Trim();
    return text.Length == 0 ? null : text;
}

static int Verify(Dictionary<string, string> o)
{
    var manifest = ManifestJson.Deserialize(File.ReadAllText(Path.GetFullPath(Req(o, "manifest"))))
                   ?? throw new FormatException("Manifest JSON is empty.");
    var keyArg = Req(o, "public-key");
    var pem = File.Exists(keyArg) ? File.ReadAllText(keyArg) : keyArg;
    var ok = ManifestSigner.Verify(manifest, pem);
    Console.WriteLine(ok ? "OK: signature valid" : "FAIL: signature invalid");
    return ok ? 0 : 1;
}

static int Token()
{
    var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    Console.WriteLine($"TOKEN={token}");
    Console.WriteLine($"SHA256={hash}");
    return 0;
}

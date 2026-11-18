using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MiniTools.Contracts;

public sealed partial record ReleaseManifest
{
    public required string AppId { get; init; }          // "disk-cleaner" (kebab-case, regex ^[a-z0-9-]{2,40}$)
    public required string Version { get; init; }        // "1.2.0"
    public required string Channel { get; init; }        // "stable" | "beta"
    public required string FileName { get; init; }       // "DiskCleaner-v1.2.0-Portable.zip"
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }         // hex lowercase
    public required DateTimeOffset PublishedAt { get; init; }
    public required string MinUpdaterVersion { get; init; } = "1.0.0";
    public required Dictionary<string, string> Notes { get; init; }   // { "vi": "...", "en": "..." } (markdown)
    public string? Signature { get; init; }              // base64 ECDSA over CanonicalPayload()

    public const string PayloadMagic = "minitools-release-manifest-v1";

    /// <summary>
    /// The signed string: all fields except Signature and Notes joined by '\n' in a fixed order,
    /// ending with the sha256 (hex) of the canonical Notes JSON (keys sorted ordinally).
    /// </summary>
    public string CanonicalPayload()
    {
        var sb = new StringBuilder();
        sb.Append(PayloadMagic).Append('\n');
        sb.Append(AppId).Append('\n');
        sb.Append(Version).Append('\n');
        sb.Append(Channel).Append('\n');
        sb.Append(FileName).Append('\n');
        sb.Append(SizeBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(Sha256).Append('\n');
        sb.Append(PublishedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(MinUpdaterVersion).Append('\n');
        sb.Append(NotesHash());
        return sb.ToString();
    }

    private string NotesHash()
    {
        var sorted = new SortedDictionary<string, string>(Notes ?? new(), StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(sorted, CanonicalJson);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex AppIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+\.zip$")]
    private static partial Regex FileNameRegex();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Regex();

    public static bool IsValidAppId(string? s) => s is not null && AppIdRegex().IsMatch(s);
    public static bool IsValidFileName(string? s) => s is not null && s.Length <= 200 && !s.Contains("..") && FileNameRegex().IsMatch(s);
    public static bool IsValidSha256(string? s) => s is not null && Sha256Regex().IsMatch(s);
    public static bool IsValidChannel(string? s) => s is "stable" or "beta";

    /// <summary>Checks field formats. Returns an error message, or null when valid.</summary>
    public string? Validate()
    {
        if (!IsValidAppId(AppId)) return "Invalid appId.";
        if (!AppVersion.TryParse(Version, out var v) || v.ToString() != Version) return "Invalid version (expected Major.Minor.Patch without prefix).";
        if (!IsValidChannel(Channel)) return "Invalid channel (stable|beta).";
        if (!IsValidFileName(FileName)) return "Invalid fileName.";
        if (SizeBytes <= 0) return "Invalid sizeBytes.";
        if (!IsValidSha256(Sha256)) return "Invalid sha256 (64 lowercase hex chars).";
        if (!AppVersion.TryParse(MinUpdaterVersion, out _)) return "Invalid minUpdaterVersion.";
        if (Notes is null) return "Missing notes.";
        return null;
    }
}

/// <summary>Shared JSON options for manifests (camelCase) used by server, tools and updater.</summary>
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions IndentedOptions = new(Options) { WriteIndented = true };

    public static string Serialize(ReleaseManifest m, bool indented = false) =>
        JsonSerializer.Serialize(m, indented ? IndentedOptions : Options);

    public static ReleaseManifest? Deserialize(string json) =>
        JsonSerializer.Deserialize<ReleaseManifest>(json, Options);
}

using System.Reflection;
using System.Text.Json;

namespace MiniToolsHub.Server;

/// <summary>UI strings (VI/EN) loaded from embedded Strings.{lang}.json.</summary>
public sealed class Loc
{
    public static readonly string[] Languages = ["vi", "en"];
    private readonly Dictionary<string, Dictionary<string, string>> _strings = new();

    public Loc()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var lang in Languages)
        {
            var name = asm.GetManifestResourceNames().Single(n => n.EndsWith($"Strings.{lang}.json", StringComparison.Ordinal));
            using var s = asm.GetManifestResourceStream(name)!;
            _strings[lang] = JsonSerializer.Deserialize<Dictionary<string, string>>(s)!;
        }
    }

    public static bool IsLang(string? l) => l is "vi" or "en";
    public static string Other(string lang) => lang == "vi" ? "en" : "vi";

    public string T(string lang, string key, params object[] args)
    {
        var d = _strings[IsLang(lang) ? lang : "en"];
        var s = d.TryGetValue(key, out var v) ? v : key;
        return args.Length == 0 ? s : string.Format(System.Globalization.CultureInfo.InvariantCulture, s, args);
    }

    public IEnumerable<string> Keys(string lang) => _strings[lang].Keys;

    /// <summary>Picks "vi" when Accept-Language prefers Vietnamese, otherwise "en".</summary>
    public static string FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return "en";
        var best = (lang: "en", q: -1.0);
        foreach (var part in header.Split(','))
        {
            var seg = part.Trim().Split(';');
            var tag = seg[0].Trim().ToLowerInvariant();
            var q = 1.0;
            foreach (var p in seg.Skip(1))
            {
                var kv = p.Trim().Split('=');
                if (kv.Length == 2 && kv[0] == "q" && double.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var qq)) q = qq;
            }
            var lang = tag.StartsWith("vi") ? "vi" : tag.StartsWith("en") ? "en" : null;
            if (lang is not null && q > best.q) best = (lang, q);
        }
        return best.lang;
    }
}

public static class Fmt
{
    public static string Size(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (bytes >= GB) return (bytes / GB).ToString("0.##", ci) + " GB";
        if (bytes >= MB) return (bytes / MB).ToString("0.#", ci) + " MB";
        return Math.Max(1, Math.Round(bytes / KB)).ToString("0", ci) + " KB";
    }

    public static string Date(DateTimeOffset d, string lang) =>
        lang == "vi"
            ? d.UtcDateTime.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : d.UtcDateTime.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    public static string Number(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Options;
using MiniTools.Contracts;

namespace MiniToolsHub.Server.Content;

public sealed class ShotMeta
{
    public Dictionary<string, string> File { get; set; } = new();
    public Dictionary<string, string> Alt { get; set; } = new();
}

public sealed class ToolMeta
{
    public string AppId { get; set; } = "";
    public int Order { get; set; }
    public Dictionary<string, string> Name { get; set; } = new();
    public Dictionary<string, string> Tagline { get; set; } = new();
    public Dictionary<string, string[]> Features { get; set; } = new();
    public Dictionary<string, string> Requirements { get; set; } = new();
    public string Accent { get; set; } = "#6D5EF6";
    public List<ShotMeta> Shots { get; set; } = new();
}

public sealed class ToolContent
{
    public required ToolMeta Meta { get; init; }
    public required Dictionary<string, string> DescriptionHtml { get; init; }
    public string AppId => Meta.AppId;

    public string Name(string lang) => Pick(Meta.Name, lang);
    public string Tagline(string lang) => Pick(Meta.Tagline, lang);
    public string Requirements(string lang) => Pick(Meta.Requirements, lang);
    public string[] Features(string lang) => Meta.Features.TryGetValue(lang, out var f) ? f : Meta.Features.Values.FirstOrDefault() ?? [];
    public string Description(string lang) => Pick(DescriptionHtml, lang);

    private static string Pick(Dictionary<string, string> d, string lang) =>
        d.TryGetValue(lang, out var v) ? v : d.GetValueOrDefault("en") ?? d.Values.FirstOrDefault() ?? "";
}

/// <summary>Reads static tool content (content/tools/{appId}) once at startup.</summary>
public sealed partial class ToolContentStore
{
    private readonly Dictionary<string, ToolContent> _tools;
    public string ToolsDir { get; }

    public ToolContentStore(IOptions<HubOptions> options, ILogger<ToolContentStore> log)
    {
        ToolsDir = Path.Combine(Path.GetFullPath(options.Value.ContentDir), "tools");
        _tools = new(StringComparer.Ordinal);
        var md = new MarkdownPipelineBuilder().DisableHtml().UseAdvancedExtensions().Build();
        if (!Directory.Exists(ToolsDir))
        {
            log.LogWarning("Content directory not found: {Dir}", ToolsDir);
            return;
        }
        foreach (var dir in Directory.GetDirectories(ToolsDir))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var meta = JsonSerializer.Deserialize<ToolMeta>(File.ReadAllText(metaPath), ManifestJson.Options)!;
                if (!ReleaseManifest.IsValidAppId(meta.AppId) || meta.AppId != Path.GetFileName(dir))
                    throw new InvalidDataException($"appId '{meta.AppId}' does not match folder name.");
                var desc = new Dictionary<string, string>();
                foreach (var lang in Loc.Languages)
                {
                    var p = Path.Combine(dir, lang + ".md");
                    if (File.Exists(p)) desc[lang] = Markdown.ToHtml(File.ReadAllText(p), md);
                }
                _tools[meta.AppId] = new ToolContent { Meta = meta, DescriptionHtml = desc };
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                log.LogError(ex, "Invalid tool content in {Dir}", dir);
            }
        }
    }

    public IReadOnlyList<ToolContent> All => _tools.Values.OrderBy(t => t.Meta.Order).ThenBy(t => t.AppId, StringComparer.Ordinal).ToList();
    public bool Contains(string appId) => _tools.ContainsKey(appId);
    public ToolContent? Get(string appId) => _tools.GetValueOrDefault(appId);

    [GeneratedRegex(@"^(icon\.png|shots/[A-Za-z0-9._-]+\.(png|jpg|jpeg|webp|svg))$")]
    private static partial Regex MediaPathRegex();

    /// <summary>Resolves a media file (icon / screenshot) safely; null when invalid or missing.</summary>
    public string? ResolveMedia(string appId, string relPath)
    {
        if (!Contains(appId) || !MediaPathRegex().IsMatch(relPath)) return null;
        var root = Path.GetFullPath(Path.Combine(ToolsDir, appId));
        var full = Path.GetFullPath(Path.Combine(root, relPath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return File.Exists(full) ? full : null;
    }
}

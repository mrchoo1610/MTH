using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiniTools.Contracts;
using MiniToolsHub.Server.Content;
using MiniToolsHub.Server.Data;

namespace MiniToolsHub.Server.Pages;

public class ToolModel(Loc loc, IOptions<HubOptions> options, CatalogService catalog, ReleaseRepository releases) : HubPageModel(loc, options)
{
    private static readonly MarkdownPipeline Md = new MarkdownPipelineBuilder().DisableHtml().UseAdvancedExtensions().Build();

    public ToolCard Card { get; private set; } = null!;
    public List<(ReleaseManifest Release, string NotesHtml)> History { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string appId)
    {
        var card = await catalog.GetCardAsync(appId);
        if (card is null) return NotFound();
        Card = card;

        var list = await releases.ListAsync(appId, "beta", 50);
        History = list.Select(r => (r, Markdown.ToHtml(Notes(r), Md))).ToList();

        var lang = Lang;
        Seo(card.Tool.Name(lang), card.Tool.Tagline(lang), "/tools/" + appId, $"/media/{appId}/icon.png");
        return Page();
    }

    private string Notes(ReleaseManifest r) =>
        r.Notes.TryGetValue(Lang, out var n) && !string.IsNullOrWhiteSpace(n) ? n
        : r.Notes.GetValueOrDefault("en") ?? r.Notes.Values.FirstOrDefault() ?? L("tool.noNotes");
}

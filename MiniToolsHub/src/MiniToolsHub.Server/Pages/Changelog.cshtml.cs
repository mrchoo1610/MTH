using Markdig;
using Microsoft.Extensions.Options;
using MiniTools.Contracts;
using MiniToolsHub.Server.Content;
using MiniToolsHub.Server.Data;

namespace MiniToolsHub.Server.Pages;

public class ChangelogModel(Loc loc, IOptions<HubOptions> options, ToolContentStore content, ReleaseRepository releases) : HubPageModel(loc, options)
{
    private static readonly MarkdownPipeline Md = new MarkdownPipelineBuilder().DisableHtml().UseAdvancedExtensions().Build();

    public List<(ToolContent Tool, ReleaseManifest Release, string NotesHtml)> Entries { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var all = await releases.ListAllAsync();
        foreach (var r in all)
        {
            var tool = content.Get(r.AppId);
            if (tool is null) continue;
            var notes = r.Notes.TryGetValue(Lang, out var n) && !string.IsNullOrWhiteSpace(n) ? n
                : r.Notes.GetValueOrDefault("en") ?? L("tool.noNotes");
            Entries.Add((tool, r, Markdown.ToHtml(notes, Md)));
        }
        Seo(L("changelog.title"), L("changelog.sub"), "/changelog");
    }
}

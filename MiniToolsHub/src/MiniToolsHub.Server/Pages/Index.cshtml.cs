using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiniToolsHub.Server.Content;

namespace MiniToolsHub.Server.Pages;

public class IndexModel(Loc loc, IOptions<HubOptions> options, CatalogService catalog) : HubPageModel(loc, options)
{
    public List<ToolCard> Cards { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        Cards = await catalog.GetCardsAsync();
        Seo(L("site.name") + " - " + L("site.tagline"), L("site.description"), "");
        // On the home page the title is just the site name; keep it short.
        ViewData["Title"] = L("site.name");
        return Page();
    }
}

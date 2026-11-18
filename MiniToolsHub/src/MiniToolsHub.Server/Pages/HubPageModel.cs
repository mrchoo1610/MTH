using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace MiniToolsHub.Server.Pages;

/// <summary>Base for localized pages: resolves the language, translations and SEO metadata.</summary>
public abstract class HubPageModel(Loc loc, IOptions<HubOptions> options) : PageModel
{
    public virtual string Lang =>
        RouteData.Values["lang"] is string l && Loc.IsLang(l) ? l : Loc.FromAcceptLanguage(Request.Headers.AcceptLanguage);

    public string BaseUrl => options.Value.PublicBaseUrl.TrimEnd('/');

    public string L(string key, params object[] args) => loc.T(Lang, key, args);

    /// <param name="pathSuffix">Page path without the language prefix, e.g. "/tools/disk-cleaner" or "".</param>
    protected void Seo(string title, string description, string pathSuffix, string? ogImagePath = null)
    {
        ViewData["Title"] = title;
        ViewData["Description"] = description;
        ViewData["PathSuffix"] = pathSuffix;
        ViewData["OgImage"] = BaseUrl + (ogImagePath ?? "/img/og.png");
    }
}

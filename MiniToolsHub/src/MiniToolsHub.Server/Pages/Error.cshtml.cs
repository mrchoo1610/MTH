using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace MiniToolsHub.Server.Pages;

public class ErrorModel(Loc loc, IOptions<HubOptions> options) : HubPageModel(loc, options)
{
    public int Code { get; private set; } = 500;

    public override string Lang
    {
        get
        {
            var original = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath ?? "";
            if (original.StartsWith("/vi")) return "vi";
            if (original.StartsWith("/en")) return "en";
            return Loc.FromAcceptLanguage(Request.Headers.AcceptLanguage);
        }
    }

    public void OnGet(int? code)
    {
        Code = code ?? 500;
        // Keep the original status code (the status-code middleware restores it for re-executed pages).
        if (code is null) Response.StatusCode = 500;
        Seo(L("error.title"), L("error.title"), "");
        ViewData["NoIndex"] = true;
    }

    public string Message => Code switch { 404 => L("error.404"), >= 500 => L("error.500"), _ => L("error.other") };
}

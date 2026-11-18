using MiniTools.Contracts;
using MiniToolsHub.Server.Content;
using MiniToolsHub.Server.Data;

namespace MiniToolsHub.Server.Api;

public static class PublicApi
{
    public static void MapPublicApi(this WebApplication app)
    {
        var g = app.MapGroup("/api").RequireRateLimiting("public");

        g.MapGet("/apps", async (CatalogService catalog) =>
        {
            var cards = await catalog.GetCardsAsync();
            return Results.Json(cards.Select(c => new AppCatalogEntry(c.Tool.AppId, c.Tool.Name("en"), c.Latest, c.TotalDownloads)),
                ManifestJson.Options);
        });

        g.MapGet("/apps/{appId}/latest", async (HttpContext ctx, string appId, string? channel, string? current,
            ToolContentStore content, ReleaseRepository releases) =>
        {
            if (!content.Contains(appId)) return Results.Json(new { error = "App not found" }, statusCode: 404);
            channel = string.IsNullOrEmpty(channel) ? "stable" : channel;
            if (!ReleaseManifest.IsValidChannel(channel)) return Results.Json(new { error = "Invalid channel" }, statusCode: 400);

            ctx.Response.Headers.CacheControl = "public, max-age=300";
            var latest = await releases.GetLatestAsync(appId, channel);
            if (latest is null) return Results.NoContent();
            if (AppVersion.TryParse(current, out var cur) && cur >= AppVersion.Parse(latest.Version))
                return Results.NoContent();
            return Results.Json(latest, ManifestJson.Options);
        });

        g.MapGet("/apps/{appId}/releases", async (string appId, ToolContentStore content, ReleaseRepository releases) =>
        {
            if (!content.Contains(appId)) return Results.Json(new { error = "App not found" }, statusCode: 404);
            return Results.Json(await releases.ListAsync(appId, "beta", 50), ManifestJson.Options);
        });
    }
}

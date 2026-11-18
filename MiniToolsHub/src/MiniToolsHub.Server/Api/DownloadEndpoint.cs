using MiniTools.Contracts;
using Microsoft.Extensions.Options;
using MiniToolsHub.Server.Content;
using MiniToolsHub.Server.Data;

namespace MiniToolsHub.Server.Api;

public static class DownloadEndpoint
{
    public static void MapDownloads(this WebApplication app)
    {
        app.MapGet("/dl/{appId}/{version}/{fileName}", async (HttpContext ctx, string appId, string version, string fileName,
            IOptions<HubOptions> options, ReleaseRepository releases, DownloadCounter counter) =>
        {
            if (!ReleaseManifest.IsValidAppId(appId) || !ReleaseManifest.IsValidFileName(fileName)
                || !AppVersion.TryParse(version, out var v) || v.ToString() != version)
                return Results.Json(new { error = "Invalid path" }, statusCode: 400);

            var release = await releases.GetAsync(appId, version);
            if (release is null || release.FileName != fileName)
                return Results.Json(new { error = "Not found" }, statusCode: 404);

            var path = ReleaseFiles.Resolve(options.Value, appId, version, fileName);
            if (path is null || !File.Exists(path))
                return Results.Json(new { error = "Not found" }, statusCode: 404);

            // Count whole-file requests only (not range continuations / resumed downloads).
            var range = ctx.Request.Headers.Range.ToString();
            if (range.Length == 0 || range.StartsWith("bytes=0-", StringComparison.Ordinal))
                await counter.IncrementAsync(appId, version);

            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(path, "application/zip", fileDownloadName: fileName, enableRangeProcessing: true);
        }).RequireRateLimiting("public");
    }
}

public static class ReleaseFiles
{
    /// <summary>Builds DataDir/files/{appId}/{version}/{fileName}; null if it would escape the files root.</summary>
    public static string? Resolve(HubOptions o, string appId, string version, string fileName)
    {
        var root = o.FilesRoot;
        var full = Path.GetFullPath(Path.Combine(root, appId, version, fileName));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }
}

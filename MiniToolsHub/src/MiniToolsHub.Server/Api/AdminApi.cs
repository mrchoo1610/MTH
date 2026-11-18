using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiniTools.Contracts;
using MiniToolsHub.Server.Content;
using MiniToolsHub.Server.Data;

namespace MiniToolsHub.Server.Api;

public static class AdminApi
{
    private static IResult Err(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    public static void MapAdminApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin").RequireRateLimiting("admin").AddEndpointFilter<ApiKeyAuth>();

        g.MapPost("/releases", UploadAsync);

        g.MapPost("/releases/{appId}/{version}/yank", async (string appId, string version, ReleaseRepository releases) =>
        {
            if (!ReleaseManifest.IsValidAppId(appId) || !AppVersion.TryParse(version, out _))
                return Err(400, "Invalid appId or version.");
            return await releases.YankAsync(appId, version) ? Results.Json(new { yanked = true, appId, version }) : Err(404, "Release not found.");
        });
    }

    private static async Task<IResult> UploadAsync(HttpContext ctx, IOptions<HubOptions> options, ToolContentStore content,
        ReleaseRepository releases, ILoggerFactory lf)
    {
        var log = lf.CreateLogger("Upload");
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.PublicKeyPem)) return Err(503, "Server has no Hub:PublicKeyPem configured.");
        if (!ctx.Request.HasFormContentType) return Err(400, "Expected multipart/form-data.");

        var maxBytes = (long)o.MaxUploadMb * 1024 * 1024;
        IFormCollection form;
        try { form = await ctx.Request.ReadFormAsync(ctx.RequestAborted); }
        catch (InvalidDataException) { return Err(413, "Upload too large."); }
        catch (BadHttpRequestException ex) { return Err(ex.StatusCode, ex.Message); }

        // The manifest may be a text field or a file part.
        string? manifestJson = form["manifest"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(manifestJson) && form.Files.GetFile("manifest") is { } mf)
        {
            using var r = new StreamReader(mf.OpenReadStream());
            manifestJson = await r.ReadToEndAsync();
        }
        var file = form.Files.GetFile("file");
        if (string.IsNullOrWhiteSpace(manifestJson) || file is null) return Err(400, "Both 'manifest' and 'file' are required.");

        ReleaseManifest? m;
        try { m = ManifestJson.Deserialize(manifestJson); }
        catch (JsonException) { return Err(400, "Manifest is not valid JSON."); }
        if (m is null) return Err(400, "Manifest is empty.");

        var invalid = m.Validate();
        if (invalid is not null) return Err(400, invalid);
        if (!content.Contains(m.AppId)) return Err(400, "Unknown appId (no content/tools entry).");
        if (file.Length > maxBytes) return Err(413, $"File exceeds {o.MaxUploadMb} MB.");
        if (file.Length != m.SizeBytes) return Err(400, "sizeBytes does not match the uploaded file.");
        if (!ManifestSigner.Verify(m, o.PublicKeyPem)) return Err(400, "Invalid manifest signature.");
        if (await releases.ExistsAsync(m.AppId, m.Version)) return Err(409, "This version already exists.");

        var target = ReleaseFiles.Resolve(o, m.AppId, m.Version, m.FileName);
        if (target is null) return Err(400, "Invalid file path.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            string sha;
            using (var input = file.OpenReadStream())
            using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buf = new byte[81920];
                int n;
                while ((n = await input.ReadAsync(buf)) > 0)
                {
                    hash.AppendData(buf, 0, n);
                    await output.WriteAsync(buf.AsMemory(0, n));
                }
                sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            if (sha != m.Sha256) { File.Delete(tmp); return Err(400, "sha256 does not match the uploaded file."); }

            // A file without a DB row is an orphan from an earlier failed upload, safe to replace.
            File.Move(tmp, target, overwrite: true);
            if (!await releases.InsertAsync(m))
                return Err(409, "This version already exists.");
        }
        catch (IOException ex)
        {
            log.LogError(ex, "Failed to store release {App} {Version}", m.AppId, m.Version);
            if (File.Exists(tmp)) File.Delete(tmp);
            return Err(500, "Could not store the file.");
        }

        log.LogInformation("Published {App} {Version} ({Size} bytes)", m.AppId, m.Version, m.SizeBytes);
        return Results.Json(m, ManifestJson.Options, statusCode: 201);
    }
}

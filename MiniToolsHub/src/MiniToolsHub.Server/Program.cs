using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using MiniToolsHub.Server;
using MiniToolsHub.Server.Api;
using MiniToolsHub.Server.Content;
using MiniToolsHub.Server.Data;

// Container HEALTHCHECK (the aspnet image has no curl/wget): `dotnet MiniToolsHub.Server.dll --healthcheck`
if (args.Contains("--healthcheck"))
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var ok = (await http.GetAsync("http://localhost:18473/healthz")).IsSuccessStatusCode;
        return ok ? 0 : 1;
    }
    catch (HttpRequestException) { return 1; }
    catch (TaskCanceledException) { return 1; }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<HubOptions>().Bind(builder.Configuration.GetSection("Hub"))
    .PostConfigure(o =>
    {
        // Relative dirs resolve against the content root so `dotnet run` works from any cwd.
        if (!Path.IsPathRooted(o.DataDir)) o.DataDir = Path.GetFullPath(o.DataDir, builder.Environment.ContentRootPath);
        if (!Path.IsPathRooted(o.ContentDir)) o.ContentDir = Path.GetFullPath(o.ContentDir, builder.Environment.ContentRootPath);
    });

builder.Services.AddOptions<KestrelServerOptions>().Configure<IOptions<HubOptions>>((k, h) =>
    k.Limits.MaxRequestBodySize = ((long)h.Value.MaxUploadMb + 2) * 1024 * 1024);
builder.Services.AddOptions<Microsoft.AspNetCore.Http.Features.FormOptions>().Configure<IOptions<HubOptions>>((f, h) =>
{
    f.MultipartBodyLengthLimit = ((long)h.Value.MaxUploadMb + 2) * 1024 * 1024;
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // cloudflared reaches the app over the private docker network; trust its X-Forwarded-* headers.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests" }, ct);
    };
    o.AddPolicy("admin", ctx => PartitionFor(ctx, "admin", h => h.AdminRequestsPerMinute));
    o.AddPolicy("public", ctx => PartitionFor(ctx, "public", h => h.PublicRequestsPerMinute));
});

builder.Services.AddSingleton<Loc>();
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<ToolContentStore>();
builder.Services.AddSingleton<ReleaseRepository>();
builder.Services.AddSingleton<DownloadCounter>();
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddScoped<ApiKeyAuth>();
builder.Services.AddRazorPages();
// Emit Vietnamese characters as-is instead of &#x...; entities.
builder.Services.AddWebEncoders(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

var app = builder.Build();

app.Services.GetRequiredService<Db>().Migrate();
_ = app.Services.GetRequiredService<ToolContentStore>();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

// Friendly HTML error pages for the website only; API and downloads keep their JSON bodies.
app.UseWhen(c => !c.Request.Path.StartsWithSegments("/api") && !c.Request.Path.StartsWithSegments("/dl"),
    b => b.UseStatusCodePagesWithReExecute("/error/{0}"));

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["X-Frame-Options"] = "DENY";
    h["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' https://fonts.googleapis.com; " +
        "style-src-attr 'unsafe-inline'; font-src https://fonts.gstatic.com; script-src 'self'; " +
        "base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    await next();
});

app.UseStaticFiles();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet("/", (HttpContext ctx) =>
    Results.Redirect("/" + Loc.FromAcceptLanguage(ctx.Request.Headers.AcceptLanguage), permanent: false));

app.MapPublicApi();
app.MapAdminApi();
app.MapDownloads();

app.MapGet("/media/{appId}/{**path}", (string appId, string path, ToolContentStore content) =>
{
    var file = content.ResolveMedia(appId, path);
    if (file is null) return Results.NotFound();
    var type = Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
    return Results.File(file, type, lastModified: File.GetLastWriteTimeUtc(file));
});

app.MapGet("/robots.txt", (IOptions<HubOptions> o) =>
    Results.Text($"User-agent: *\nAllow: /\nDisallow: /api/\nSitemap: {o.Value.PublicBaseUrl.TrimEnd('/')}/sitemap.xml\n", "text/plain"));

app.MapGet("/sitemap.xml", (IOptions<HubOptions> o, ToolContentStore content) =>
{
    var b = o.Value.PublicBaseUrl.TrimEnd('/');
    var paths = new List<string> { "", "/changelog" };
    paths.AddRange(content.All.Select(t => "/tools/" + t.AppId));
    var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">\n");
    foreach (var p in paths)
        foreach (var lang in Loc.Languages)
        {
            sb.Append("  <url><loc>").Append(b).Append('/').Append(lang).Append(p).Append("</loc>");
            foreach (var alt in Loc.Languages)
                sb.Append("<xhtml:link rel=\"alternate\" hreflang=\"").Append(alt).Append("\" href=\"").Append(b).Append('/').Append(alt).Append(p).Append("\"/>");
            sb.Append("</url>\n");
        }
    sb.Append("</urlset>\n");
    return Results.Text(sb.ToString(), "application/xml", Encoding.UTF8);
});

app.MapRazorPages();

app.Run();
return 0;

static RateLimitPartition<string> PartitionFor(HttpContext ctx, string policy, Func<HubOptions, int> limit)
{
    var opts = ctx.RequestServices.GetRequiredService<IOptions<HubOptions>>().Value;
    var ip = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{ip}", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(1, limit(opts)),
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    });
}

public partial class Program;

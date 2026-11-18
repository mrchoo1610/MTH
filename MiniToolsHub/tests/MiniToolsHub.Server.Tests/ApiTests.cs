using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniTools.Contracts;

namespace MiniToolsHub.Server.Tests;

public class ApiTests : IClassFixture<HubFactory>
{
    private readonly HubFactory _f;
    public ApiTests(HubFactory f) => _f = f;

    private Task<HttpResponseMessage> Upload(HttpClient c, ReleaseManifest m, byte[] bytes) =>
        c.PostAsync("/api/admin/releases", HubFactory.Form(m, bytes));

    [Fact]
    public async Task Upload_WithoutToken_Is401()
    {
        var (m, b) = _f.MakeRelease(version: "9.0.1");
        var r = await Upload(_f.CreateClient(), m, b);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Upload_WithWrongToken_Is401()
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nope");
        var (m, b) = _f.MakeRelease(version: "9.0.2");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(c, m, b)).StatusCode);
    }

    [Fact]
    public async Task Upload_Valid_Is201_AndLatestReturnsIt()
    {
        var c = _f.CreateAdminClient();
        var (m, b) = _f.MakeRelease("taskbar-monitor", "1.0.0");
        var r = await Upload(c, m, b);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var latest = await _f.CreateClient().GetFromJsonAsync<ReleaseManifest>("/api/apps/taskbar-monitor/latest", ManifestJson.Options);
        Assert.Equal("1.0.0", latest!.Version);
        Assert.True(ManifestSigner.Verify(latest, _f.PublicPem));
    }

    [Fact]
    public async Task Upload_DuplicateVersion_Is409()
    {
        var c = _f.CreateAdminClient();
        var (m, b) = _f.MakeRelease("disk-cleaner", "2.0.0");
        Assert.Equal(HttpStatusCode.Created, (await Upload(c, m, b)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(c, m, b)).StatusCode);
    }

    [Fact]
    public async Task Upload_WrongSha256_Is400()
    {
        var (m, b) = _f.MakeRelease(version: "3.0.0");
        var bad = m with { Sha256 = new string('0', 64) };
        bad = bad with { Signature = ManifestSigner.Sign(bad, _f.PrivatePem) };   // validly signed, but sha does not match the file
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(_f.CreateAdminClient(), bad, b)).StatusCode);
    }

    [Fact]
    public async Task Upload_BadSignature_Is400()
    {
        var (m, b) = _f.MakeRelease(version: "3.1.0");
        var other = ManifestSigner.GenerateKeyPair();
        var bad = m with { Signature = ManifestSigner.Sign(m, other.PrivatePem) };
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(_f.CreateAdminClient(), bad, b)).StatusCode);
        var unsigned = m with { Signature = null };
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(_f.CreateAdminClient(), unsigned, b)).StatusCode);
    }

    [Fact]
    public async Task Upload_UnknownApp_Is400()
    {
        var (m, b) = _f.MakeRelease("not-an-app", "1.0.0");
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(_f.CreateAdminClient(), m, b)).StatusCode);
    }

    [Fact]
    public async Task Upload_PathTraversalFileName_Is400()
    {
        var (m, b) = _f.MakeRelease(version: "3.2.0", fileName: "../x.zip");
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(_f.CreateAdminClient(), m, b)).StatusCode);
    }

    [Fact]
    public async Task Upload_TooLarge_Is413()
    {
        using var small = new HubFactory { MaxUploadMb = 1 };
        var (m, b) = small.MakeRelease(version: "3.3.0", size: 2 * 1024 * 1024);
        var r = await Upload(small.CreateAdminClient(), m, b);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
    }

    [Fact]
    public async Task Latest_CurrentEqualsLatest_Is204_And_OlderGetsManifest()
    {
        using var f = new HubFactory();
        var c = f.CreateAdminClient();
        var (m, b) = f.MakeRelease("disk-cleaner", "4.0.0");
        await Upload(c, m, b);

        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await anon.GetAsync("/api/apps/disk-cleaner/latest?current=4.0.0")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.GetAsync("/api/apps/disk-cleaner/latest?current=99.0.0")).StatusCode);
        var older = await anon.GetAsync("/api/apps/disk-cleaner/latest?current=3.9.9");
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
        Assert.Contains("max-age=300", older.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Latest_UnknownApp_Is404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _f.CreateClient().GetAsync("/api/apps/nope-app/latest")).StatusCode);
    }

    [Fact]
    public async Task Latest_SkipsYanked_AndComparesNumerically()
    {
        using var f = new HubFactory();
        var c = f.CreateAdminClient();
        foreach (var v in new[] { "1.9.0", "1.10.0", "1.11.0" })
        {
            var (m, b) = f.MakeRelease("disk-cleaner", v);
            Assert.Equal(HttpStatusCode.Created, (await Upload(c, m, b)).StatusCode);
        }
        var anon = f.CreateClient();
        Assert.Equal("1.11.0", (await anon.GetFromJsonAsync<ReleaseManifest>("/api/apps/disk-cleaner/latest", ManifestJson.Options))!.Version);

        var yank = await c.PostAsync("/api/admin/releases/disk-cleaner/1.11.0/yank", null);
        Assert.Equal(HttpStatusCode.OK, yank.StatusCode);
        Assert.Equal("1.10.0", (await anon.GetFromJsonAsync<ReleaseManifest>("/api/apps/disk-cleaner/latest", ManifestJson.Options))!.Version);

        var list = await anon.GetFromJsonAsync<List<ReleaseManifest>>("/api/apps/disk-cleaner/releases", ManifestJson.Options);
        Assert.Equal(["1.10.0", "1.9.0"], list!.Select(x => x.Version).ToArray());

        // Yanked files are no longer downloadable.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/dl/disk-cleaner/1.11.0/disk-cleaner-v1.11.0-Portable.zip")).StatusCode);
    }

    [Fact]
    public async Task Yank_RequiresAuth_AndUnknownIs404()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PostAsync("/api/admin/releases/disk-cleaner/1.0.0/yank", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _f.CreateAdminClient().PostAsync("/api/admin/releases/disk-cleaner/7.7.7/yank", null)).StatusCode);
    }

    [Fact]
    public async Task Download_ReturnsBytes_Range_AndCounts()
    {
        var c = _f.CreateAdminClient();
        var (m, bytes) = _f.MakeRelease("disk-cleaner", "5.0.0", size: 4096);
        await Upload(c, m, bytes);
        var anon = _f.CreateClient();
        var url = $"/dl/disk-cleaner/5.0.0/{m.FileName}";

        var r = await anon.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/zip", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", r.Headers.CacheControl!.ToString());
        Assert.Equal(bytes, await r.Content.ReadAsByteArrayAsync());

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(10, 19);
        var part = await anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(bytes[10..20], await part.Content.ReadAsByteArrayAsync());

        var counter = _f.Services.GetRequiredService<MiniToolsHub.Server.Data.DownloadCounter>();
        Assert.Equal(1, await counter.TotalAsync("disk-cleaner", "5.0.0"));   // range continuation not counted

        await anon.GetAsync(url);
        Assert.Equal(2, await counter.TotalAsync("disk-cleaner", "5.0.0"));
    }

    [Theory]
    [InlineData("/dl/disk-cleaner/1.0.0/..%2F..%2Fhub.db")]
    [InlineData("/dl/disk-cleaner/..%2F..%2F/x.zip")]
    [InlineData("/dl/..%2Fdata/1.0.0/x.zip")]
    [InlineData("/dl/disk-cleaner/1.0.0/..\\..\\hub.db")]
    public async Task Download_PathTraversal_IsRejected(string url)
    {
        var r = await _f.CreateClient().GetAsync(url);
        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"got {(int)r.StatusCode}");
    }

    [Fact]
    public async Task Apps_ListsContentTools()
    {
        var json = await _f.CreateClient().GetStringAsync("/api/apps");
        using var doc = JsonDocument.Parse(json);
        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("appId").GetString()).ToList();
        Assert.Contains("disk-cleaner", ids);
        Assert.Contains("taskbar-monitor", ids);
    }

    [Fact]
    public async Task Healthz_Ok() => Assert.Equal("ok", await _f.CreateClient().GetStringAsync("/healthz"));

    [Fact]
    public async Task AdminRateLimit_Returns429()
    {
        using var f = new HubFactory { AdminPerMinute = 3 };
        var c = f.CreateAdminClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await c.PostAsync("/api/admin/releases/disk-cleaner/0.0.1/yank", null)).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.Equal(HttpStatusCode.NotFound, codes[0]);
    }
}

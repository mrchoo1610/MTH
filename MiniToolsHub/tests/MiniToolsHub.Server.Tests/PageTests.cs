using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniToolsHub.Server;

namespace MiniToolsHub.Server.Tests;

public class PageTests : IClassFixture<HubFactory>
{
    private readonly HubFactory _f;
    public PageTests(HubFactory f) => _f = f;

    [Theory]
    [InlineData("/vi", "Taskbar Hardware Monitor")]
    [InlineData("/en", "Disk Cleaner")]
    [InlineData("/vi/tools/disk-cleaner", "Disk Cleaner")]
    [InlineData("/en/tools/disk-cleaner", "Disk Cleaner")]
    [InlineData("/en/tools/taskbar-monitor", "Taskbar Hardware Monitor")]
    [InlineData("/vi/changelog", "Lịch sử phiên bản")]
    public async Task Pages_Return200_WithContent(string url, string expected)
    {
        var r = await _f.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains(expected, html);
    }

    [Fact]
    public async Task ToolPage_HasSeoTags()
    {
        var html = await _f.CreateClient().GetStringAsync("/en/tools/disk-cleaner");
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("hreflang=\"vi\" href=\"https://mth.mimehub.top/vi/tools/disk-cleaner\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("rel=\"canonical\" href=\"https://mth.mimehub.top/en/tools/disk-cleaner\"", html);
    }

    [Fact]
    public async Task UnknownTool_And_UnknownLang_Are404()
    {
        var c = _f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/en/tools/not-here")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/fr")).StatusCode);
    }

    [Theory]
    [InlineData("vi-VN,vi;q=0.9,en;q=0.8", "/vi")]
    [InlineData("en-US,en;q=0.9", "/en")]
    [InlineData("fr-FR", "/en")]
    [InlineData("en;q=0.5,vi;q=0.9", "/vi")]
    public async Task Root_RedirectsByAcceptLanguage(string header, string expected)
    {
        using var client = _f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Accept-Language", header);
        var r = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(expected, r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Root_WithoutHeader_RedirectsToEn()
    {
        using var client = _f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal("/en", (await client.GetAsync("/")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Media_ServesIcon_AndRejectsTraversal()
    {
        var c = _f.CreateClient();
        var ok = await c.GetAsync("/media/disk-cleaner/icon.png");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/png", ok.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/media/disk-cleaner/meta.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/media/disk-cleaner/shots/..%2F..%2Fmeta.json")).StatusCode);
    }

    [Fact]
    public async Task Seo_Robots_And_Sitemap()
    {
        var c = _f.CreateClient();
        var robots = await c.GetStringAsync("/robots.txt");
        Assert.Contains("Sitemap: https://mth.mimehub.top/sitemap.xml", robots);
        var sitemap = await c.GetStringAsync("/sitemap.xml");
        Assert.Contains("https://mth.mimehub.top/vi/tools/disk-cleaner", sitemap);
        Assert.Contains("hreflang=\"en\"", sitemap);
    }

    [Fact]
    public void Localization_BothLanguagesHaveSameKeys()
    {
        var loc = new Loc();
        Assert.Equal(loc.Keys("vi").OrderBy(x => x), loc.Keys("en").OrderBy(x => x));
    }

    [Fact]
    public async Task ReleaseNotes_AppearOnToolPage_InPageLanguage()
    {
        using var f = new HubFactory();
        var (m, b) = f.MakeRelease("disk-cleaner", "1.2.0");
        var admin = f.CreateAdminClient();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/admin/releases", HubFactory.Form(m, b))).StatusCode);

        var vi = await f.CreateClient().GetStringAsync("/vi/tools/disk-cleaner");
        Assert.Contains("Ghi chú <strong>VI</strong> 1.2.0", vi);
        Assert.Contains($"/dl/disk-cleaner/1.2.0/{m.FileName}", vi);
        Assert.Contains(m.Sha256, vi);
        var en = await f.CreateClient().GetStringAsync("/en/tools/disk-cleaner");
        Assert.Contains("Notes <strong>EN</strong> 1.2.0", en);
        var home = await f.CreateClient().GetStringAsync("/vi");
        Assert.Contains("Phiên bản 1.2.0", home);
    }
}

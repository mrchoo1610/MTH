using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniTools.Contracts;

namespace MiniToolsHub.Server.Tests;

/// <summary>Test host: temp DataDir, real content, a fresh test signing key and admin token per factory.</summary>
public sealed class HubFactory : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "minitoolshub-tests-" + Guid.NewGuid().ToString("N"));
    public string AdminToken { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    public string PrivatePem { get; }
    public string PublicPem { get; }
    public int MaxUploadMb { get; init; } = 95;
    public int AdminPerMinute { get; init; } = 1000;

    public HubFactory()
    {
        (PrivatePem, PublicPem) = ManifestSigner.GenerateKeyPair();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Hub:DataDir", DataDir);
        builder.UseSetting("Hub:ContentDir", Path.Combine(AppContext.BaseDirectory, "content"));
        builder.UseSetting("Hub:AdminTokenSha256", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AdminToken))));
        builder.UseSetting("Hub:PublicKeyPem", PublicPem);
        builder.UseSetting("Hub:PublicBaseUrl", "https://mth.mimehub.top");
        builder.UseSetting("Hub:MaxUploadMb", MaxUploadMb.ToString());
        builder.UseSetting("Hub:AdminRequestsPerMinute", AdminPerMinute.ToString());
        builder.UseSetting("Hub:PublicRequestsPerMinute", "100000");
    }

    public HttpClient CreateAdminClient()
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        return c;
    }

    /// <summary>Builds a signed manifest + zip payload.</summary>
    public (ReleaseManifest Manifest, byte[] Bytes) MakeRelease(string appId = "disk-cleaner", string version = "1.0.0",
        int size = 2048, string channel = "stable", string? fileName = null)
    {
        var bytes = new byte[size];
        RandomNumberGenerator.Fill(bytes);
        var m = new ReleaseManifest
        {
            AppId = appId,
            Version = version,
            Channel = channel,
            FileName = fileName ?? $"{appId}-v{version}-Portable.zip",
            SizeBytes = size,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            PublishedAt = DateTimeOffset.UtcNow,
            MinUpdaterVersion = "1.0.0",
            Notes = new() { ["vi"] = "Ghi chú **VI** " + version, ["en"] = "Notes **EN** " + version },
        };
        return (m with { Signature = ManifestSigner.Sign(m, PrivatePem) }, bytes);
    }

    public static MultipartFormDataContent Form(ReleaseManifest m, byte[] bytes)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(ManifestJson.Serialize(m), Encoding.UTF8), "manifest" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", m.FileName);
        return form;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch (IOException) { /* sqlite may hold the file briefly */ }
    }
}

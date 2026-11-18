using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MiniTools.Contracts;

namespace MiniTools.Updater.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mt-updater-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string P(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string Write(string rel, string content)
    {
        var f = P(rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
        File.WriteAllText(f, content);
        return f;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (Exception) { }
    }
}

public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<Uri> Requests { get; } = [];

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_respond(request));
    }
}

public static class Fx
{
    public const string AppId = "disk-cleaner";
    public static readonly Uri Hub = new("http://hub.test");

    public static (string Private, string Public) Keys() => ManifestSigner.GenerateKeyPair();

    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                var e = z.CreateEntry(name);
                using var s = e.Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return ms.ToArray();
    }

    public static ReleaseManifest Manifest(byte[] zip, string privateKey, string version = "1.3.0", string appId = AppId,
        string minUpdater = "1.0.0", string channel = "stable")
    {
        var m = new ReleaseManifest
        {
            AppId = appId,
            Version = version,
            Channel = channel,
            FileName = $"App-v{version}-Portable.zip",
            SizeBytes = zip.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(zip)),
            PublishedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
            MinUpdaterVersion = minUpdater,
            Notes = new() { ["vi"] = "ban moi", ["en"] = "new version" },
        };
        return m with { Signature = ManifestSigner.Sign(m, privateKey) };
    }

    public static HttpResponseMessage Json(ReleaseManifest m) =>
        new(HttpStatusCode.OK) { Content = new StringContent(ManifestJson.Serialize(m), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };

    public static UpdateClient Client(HttpMessageHandler h, string publicKey, string dataRoot, List<string>? log = null,
        string current = "1.2.0") =>
        new(new UpdateOptions(AppId, AppVersion.Parse(current), Hub), new HttpClient(h), publicKey,
            log is null ? null : log.Add, dataRoot);
}

using MiniTools.Contracts;

namespace MiniTools.Contracts.Tests;

public class ManifestSignerTests
{
    private static readonly (string Priv, string Pub) Keys = ManifestSigner.GenerateKeyPair();

    private static ReleaseManifest Sample() => new()
    {
        AppId = "disk-cleaner",
        Version = "1.2.0",
        Channel = "stable",
        FileName = "DiskCleaner-v1.2.0-Portable.zip",
        SizeBytes = 370263,
        Sha256 = new string('a', 64),
        PublishedAt = new DateTimeOffset(2026, 11, 18, 4, 5, 6, TimeSpan.Zero),
        MinUpdaterVersion = "1.0.0",
        Notes = new() { ["vi"] = "Bản mới", ["en"] = "New release" },
    };

    private static ReleaseManifest Signed(ReleaseManifest m) => m with { Signature = ManifestSigner.Sign(m, Keys.Priv) };

    [Fact]
    public void SignThenVerify_IsTrue() => Assert.True(ManifestSigner.Verify(Signed(Sample()), Keys.Pub));

    [Fact]
    public void Verify_SurvivesJsonRoundTrip()
    {
        var json = ManifestJson.Serialize(Signed(Sample()));
        var back = ManifestJson.Deserialize(json)!;
        Assert.True(ManifestSigner.Verify(back, Keys.Pub));
    }

    [Fact]
    public void Verify_IgnoresUtcOffsetRepresentation()
    {
        var s = Signed(Sample());
        var shifted = s with { PublishedAt = s.PublishedAt.ToOffset(TimeSpan.FromHours(7)) };
        Assert.True(ManifestSigner.Verify(shifted, Keys.Pub));
    }

    [Fact]
    public void Verify_NotesKeyOrderDoesNotMatter()
    {
        var s = Signed(Sample());
        var reordered = s with { Notes = new() { ["en"] = "New release", ["vi"] = "Bản mới" } };
        Assert.True(ManifestSigner.Verify(reordered, Keys.Pub));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("sha256")]
    [InlineData("fileName")]
    [InlineData("notes")]
    [InlineData("appId")]
    [InlineData("channel")]
    [InlineData("size")]
    [InlineData("publishedAt")]
    [InlineData("minUpdater")]
    public void Tampering_AnyField_FailsVerify(string field)
    {
        var s = Signed(Sample());
        var t = field switch
        {
            "version" => s with { Version = "1.2.1" },
            "sha256" => s with { Sha256 = new string('b', 64) },
            "fileName" => s with { FileName = "Evil.zip" },
            "notes" => s with { Notes = new() { ["vi"] = "Bản mới", ["en"] = "Hacked" } },
            "appId" => s with { AppId = "other-app" },
            "channel" => s with { Channel = "beta" },
            "size" => s with { SizeBytes = s.SizeBytes + 1 },
            "publishedAt" => s with { PublishedAt = s.PublishedAt.AddSeconds(1) },
            "minUpdater" => s with { MinUpdaterVersion = "9.0.0" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Assert.False(ManifestSigner.Verify(t, Keys.Pub));
    }

    [Fact]
    public void DifferentKey_FailsVerify()
    {
        var other = ManifestSigner.GenerateKeyPair();
        Assert.False(ManifestSigner.Verify(Signed(Sample()), other.PublicPem));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]
    public void GarbageSignature_FalseWithoutThrowing(string? sig)
    {
        var m = Sample() with { Signature = sig };
        Assert.False(ManifestSigner.Verify(m, Keys.Pub));
    }

    [Fact]
    public void GarbagePublicKey_FalseWithoutThrowing()
    {
        Assert.False(ManifestSigner.Verify(Signed(Sample()), "garbage"));
        Assert.False(ManifestSigner.Verify(Signed(Sample()), ""));
    }

    [Fact]
    public void SingleLinePemWithEscapedNewlines_Works()
    {
        var pub = Keys.Pub.Trim().Replace("\r", "").Replace("\n", "\\n");
        Assert.True(ManifestSigner.Verify(Signed(Sample()), pub));
    }

    [Fact]
    public void Validate_CatchesBadFields()
    {
        Assert.Null(Sample().Validate());
        Assert.NotNull((Sample() with { AppId = "../x" }).Validate());
        Assert.NotNull((Sample() with { Version = "v1.2.0" }).Validate());
        Assert.NotNull((Sample() with { FileName = "../x.zip" }).Validate());
        Assert.NotNull((Sample() with { Sha256 = "ABC" }).Validate());
        Assert.NotNull((Sample() with { Channel = "nightly" }).Validate());
    }
}

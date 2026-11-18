using DiskCleaner.Core;

namespace DiskCleaner.Tests.Core;

public class FileFilterTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewFile_IsSkipped_WhenMinAgeSet() =>
        Assert.False(FileFilter.ShouldDelete(FileAttributes.Normal, Now.AddHours(-1), Now, 24, null, "a.tmp"));

    [Fact]
    public void OldFile_IsDeleted_WhenMinAgeSet() =>
        Assert.True(FileFilter.ShouldDelete(FileAttributes.Normal, Now.AddHours(-25), Now, 24, null, "a.tmp"));

    [Fact]
    public void MinAgeZero_DeletesEvenBrandNewFiles() =>
        Assert.True(FileFilter.ShouldDelete(FileAttributes.Normal, Now, Now, 0, null, "a.tmp"));

    [Fact]
    public void ExactlyMinAge_IsDeleted() =>
        Assert.True(FileFilter.ShouldDelete(FileAttributes.Normal, Now.AddHours(-24), Now, 24, null, "a.tmp"));

    [Fact]
    public void ReparsePoint_IsSkipped() =>
        Assert.False(FileFilter.ShouldDelete(FileAttributes.ReparsePoint, Now.AddDays(-9), Now, 0, null, "link"));

    [Fact]
    public void RecallOnDataAccess_IsSkipped() =>
        Assert.False(FileFilter.ShouldDelete((FileAttributes)0x400000, Now.AddDays(-9), Now, 0, null, "cloud.docx"));

    [Fact]
    public void Offline_IsSkipped() =>
        Assert.False(FileFilter.ShouldDelete(FileAttributes.Offline, Now.AddDays(-9), Now, 0, null, "off.bin"));

    [Theory]
    [InlineData("thumbcache_256.db", true)]
    [InlineData("THUMBCACHE_idx.DB", true)]
    [InlineData("iconcache_32.db", false)]
    [InlineData("thumbcache_256.txt", false)]
    [InlineData("notes.txt", false)]
    public void NamePattern_Thumbcache(string name, bool expected) =>
        Assert.Equal(expected,
            FileFilter.ShouldDelete(FileAttributes.Normal, Now.AddDays(-1), Now, 0, "thumbcache_*.db", name));

    [Fact]
    public void ShouldDeleteAny_MatchesAnyPattern()
    {
        string[] patterns = ["thumbcache_*.db", "iconcache_*.db"];
        Assert.True(FileFilter.ShouldDeleteAny(FileAttributes.Normal, Now, Now, 0, patterns, "iconcache_32.db"));
        Assert.False(FileFilter.ShouldDeleteAny(FileAttributes.Normal, Now, Now, 0, patterns, "other.db"));
        Assert.True(FileFilter.ShouldDeleteAny(FileAttributes.Normal, Now, Now, 0, [], "anything.xyz"));
    }
}

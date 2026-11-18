using DiskCleaner.Core;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Core;

public class SafetyGuardTests
{
    private static readonly IReadOnlyList<string> Forbidden = SafetyGuard.ForbiddenRoots(FakeEnvironment.Standard());

    private static bool Safe(string target, params string[] allowed) =>
        SafetyGuard.IsSafeToDelete(target, allowed, Forbidden);

    [Fact]
    public void FileInsideAllowedRoot_IsSafe() =>
        Assert.True(Safe(@"C:\Temp\a\b.txt", @"C:\Temp"));

    [Fact]
    public void AllowedRootItself_IsNotSafe() =>
        Assert.False(Safe(@"C:\Temp", @"C:\Temp"));

    [Fact]
    public void AllowedRootWithTrailingSlash_ItselfIsNotSafe() =>
        Assert.False(Safe(@"C:\Temp\", @"C:\Temp"));

    [Fact]
    public void SiblingWithSamePrefix_IsNotSafe() =>
        Assert.False(Safe(@"C:\Temp2\x.txt", @"C:\Temp"));

    [Fact]
    public void DotDotEscapingRoot_IsNotSafe() =>
        Assert.False(Safe(@"C:\Temp\..\Other\x.txt", @"C:\Temp"));

    [Fact]
    public void DotDotThatStaysInside_IsSafe() =>
        Assert.True(Safe(@"C:\Temp\sub\..\b.txt", @"C:\Temp"));

    [Fact]
    public void WindowsSystem32_IsNotSafe_EvenIfWindowsIsAllowedRoot()
    {
        Assert.False(Safe(@"C:\Windows\System32\x.dll", @"C:\Windows\System32"));
        Assert.False(Safe(@"C:\Windows\System32\x.dll", @"C:\Windows"));
    }

    [Fact]
    public void WindowsTemp_IsAllowedException()
    {
        Assert.True(Safe(@"C:\Windows\Temp\x.tmp", @"C:\Windows\Temp"));
        Assert.False(Safe(@"C:\Windows\Temp\..\System32\x.dll", @"C:\Windows\Temp"));
    }

    [Fact]
    public void SoftwareDistributionDownload_IsAllowedExceptionButNotParent()
    {
        Assert.True(Safe(@"C:\Windows\SoftwareDistribution\Download\a.cab", @"C:\Windows\SoftwareDistribution\Download"));
        Assert.False(Safe(@"C:\Windows\SoftwareDistribution\DataStore\a.edb", @"C:\Windows\SoftwareDistribution\Download"));
    }

    [Fact]
    public void DriveRoot_IsNotSafe()
    {
        Assert.False(Safe(@"C:\", @"C:\Temp"));
        Assert.False(Safe(@"C:\Users", @"C:\"));   // allowed root là gốc ổ: bị bỏ qua
    }

    [Fact]
    public void UncPath_IsNotSafe()
    {
        Assert.False(Safe(@"\\server\share\x.txt", @"\\server\share"));
        Assert.False(Safe(@"\\?\C:\Temp\x.txt", @"C:\Temp"));
    }

    [Fact]
    public void RelativePath_IsNotSafe()
    {
        Assert.False(Safe(@"Temp\x.txt", @"C:\Temp"));
        Assert.False(Safe(@"\Temp\x.txt", @"C:\Temp"));
        Assert.False(Safe("", @"C:\Temp"));
    }

    [Fact]
    public void CaseInsensitive_IsRecognised()
    {
        Assert.True(Safe(@"c:\TEMP\X.TXT", @"C:\temp"));
        Assert.False(Safe(@"c:\windows\SYSTEM32\x.dll", @"C:\Windows"));
    }

    [Fact]
    public void UserFolders_AreForbidden()
    {
        Assert.False(Safe(@"C:\Users\tester\Documents\a.docx", @"C:\Users\tester"));
        Assert.False(Safe(@"C:\Users\tester\Desktop\a.txt", @"C:\Users\tester\Desktop"));
        Assert.False(Safe(@"C:\Users\tester\OneDrive - Contoso\a.txt", @"C:\Users\tester\OneDrive - Contoso"));
        Assert.False(Safe(@"C:\Users\tester\OneDrive\a.txt", @"C:\Users\tester\OneDrive"));
    }

    [Fact]
    public void UserProfileItself_IsForbiddenButCachesInsideAreNot()
    {
        Assert.True(SafetyGuard.IsForbidden(@"C:\Users\tester", Forbidden));
        Assert.True(Safe(@"C:\Users\tester\.gradle\caches\x.bin", @"C:\Users\tester\.gradle\caches"));
    }

    [Fact]
    public void ProgramFilesAndProgramData_AreForbidden()
    {
        Assert.False(Safe(@"C:\Program Files\App\a.dll", @"C:\Program Files\App"));
        Assert.False(Safe(@"C:\Program Files (x86)\App\a.dll", @"C:\Program Files (x86)\App"));
        Assert.False(Safe(@"C:\ProgramData\App\a.dat", @"C:\ProgramData\App"));
    }

    [Fact]
    public void AlternateDataStream_IsNotSafe() =>
        Assert.False(Safe(@"C:\Temp\a.txt:stream", @"C:\Temp"));

    [Fact]
    public void NoAllowedRoots_IsNotSafe() =>
        Assert.False(SafetyGuard.IsSafeToDelete(@"C:\Temp\a.txt", [], Forbidden));
}

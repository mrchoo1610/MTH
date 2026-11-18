using DiskCleaner.Core;

namespace DiskCleaner.Tests.Core;

public class SizeFormatterTests
{
    [Fact] public void Zero() => Assert.Equal("0 B", SizeFormatter.Format(0));
    [Fact] public void Below1KB() => Assert.Equal("1023 B", SizeFormatter.Format(1023));
    [Fact] public void Exactly1KB() => Assert.Equal("1 KB", SizeFormatter.Format(1024));
    [Fact] public void KB512() => Assert.Equal("512 KB", SizeFormatter.Format(512L * 1024));
    [Fact] public void OneAndHalfGB() => Assert.Equal("1.5 GB", SizeFormatter.Format((long)(1.5 * 1024 * 1024 * 1024)));
    [Fact] public void TwoDecimals() => Assert.Equal("1.23 GB", SizeFormatter.Format((long)(1.234 * 1024 * 1024 * 1024)));
    [Fact] public void MaxValue_DoesNotOverflow() => Assert.Equal("8 EB", SizeFormatter.Format(long.MaxValue));
    [Fact] public void Negative_IsZero() => Assert.Equal("0 B", SizeFormatter.Format(-5));
}

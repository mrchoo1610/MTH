namespace DiskCleaner.Core.Models;

public sealed record LargeFileEntry(string FullPath, long SizeBytes, DateTime LastWriteUtc);

namespace DiskCleaner.Core.Models;

public sealed class FolderNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsAccessDenied { get; set; }
    public List<FolderNode> Children { get; } = [];
}

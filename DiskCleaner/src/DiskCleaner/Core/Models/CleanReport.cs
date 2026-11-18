namespace DiskCleaner.Core.Models;

public sealed record CleanReport(
    string RuleId, long BytesFreed, int FilesDeleted, int FilesSkipped,
    IReadOnlyList<string> Errors, TimeSpan Duration);

public sealed record CleanProgress(string RuleId, string Message, int FilesProcessed, long BytesFreed);

public sealed record AnalyzeProgress(string CurrentPath, long FilesScanned, long BytesScanned);

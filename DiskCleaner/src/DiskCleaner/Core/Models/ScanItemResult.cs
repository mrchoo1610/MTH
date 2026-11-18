namespace DiskCleaner.Core.Models;

/// <summary>
/// Available=false khi path không tồn tại hoặc command không có trên PATH.
/// AccessDenied=true khi không đủ quyền đọc thư mục của rule (vd Windows Temp khi không chạy admin): số liệu 0 B không đáng tin.
/// </summary>
public sealed record ScanItemResult(string RuleId, long Bytes, int FileCount, bool Available, string? Error, bool AccessDenied = false);

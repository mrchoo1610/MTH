namespace DiskCleaner.Core.Models;

public enum RuleAction
{
    DeleteFiles,        // xóa file bên trong Paths (giữ lại thư mục gốc)
    EmptyRecycleBin,    // SHEmptyRecycleBin
    RunCommand          // chạy Command, đo dung lượng trước/sau bằng Paths
}

public sealed record CleanRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required RuleCategory Category { get; init; }
    public required RuleAction Action { get; init; }
    public string[] Paths { get; init; } = [];          // %VAR% và '*' (wildcard 1 cấp thư mục)
    public string[] NamePatterns { get; init; } = [];   // chỉ xóa file có tên khớp (vd thumbcache_*.db); rỗng = mọi file
    public int MinAgeHours { get; init; } = 0;
    public bool RequiresAdmin { get; init; }
    public bool DefaultSelected { get; init; }
    public string? Command { get; init; }
    public string? CommandArgs { get; init; }
    public string? Warning { get; init; }

    /// <summary>Rule do người dùng thêm từ tab Phân tích (lưu ở custom-rules.json), không có trong rules.json.</summary>
    public bool IsCustom { get; init; }

    // Bản tiếng Anh (tuỳ chọn). Thiếu thì fallback về tiếng Việt.
    public string? NameEn { get; init; }
    public string? DescriptionEn { get; init; }
    public string? WarningEn { get; init; }

    public string GetName(AppLanguage lang) => lang == AppLanguage.En && !string.IsNullOrWhiteSpace(NameEn) ? NameEn : Name;

    public string GetDescription(AppLanguage lang) =>
        lang == AppLanguage.En && !string.IsNullOrWhiteSpace(DescriptionEn) ? DescriptionEn : Description;

    public string? GetWarning(AppLanguage lang) =>
        lang == AppLanguage.En && !string.IsNullOrWhiteSpace(WarningEn) ? WarningEn : Warning;
}

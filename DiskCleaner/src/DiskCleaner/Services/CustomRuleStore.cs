using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>
/// Lưu rule tùy chỉnh (thư mục người dùng chọn ở tab Phân tích) vào %LOCALAPPDATA%\DiskCleaner\custom-rules.json.
/// Mọi rule đều đi qua RuleValidator + CustomRulePolicy cả khi thêm lẫn khi nạp (file có thể bị sửa tay).
/// Chỉ quản lý DANH SÁCH rule; không bao giờ đụng tới file trong thư mục đó.
/// </summary>
public sealed class CustomRuleStore
{
    private readonly string _filePath;
    private readonly IEnvironment _env;
    private readonly ILogger _log;
    private readonly IReadOnlyList<CleanRule> _builtInRules;
    private readonly string _appDataDir;
    private readonly string _appExeDir;
    private readonly IReadOnlyList<string> _forbidden;
    private readonly object _lock = new();
    private List<CleanRule>? _rules;

    public event EventHandler? Changed;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskCleaner", "custom-rules.json");

    /// <param name="builtInRules">Rule dựng sẵn (để phát hiện thư mục đã có trong Dọn dẹp).</param>
    /// <param name="appDataDir">Mặc định %LOCALAPPDATA%\DiskCleaner.</param>
    /// <param name="appExeDir">Mặc định thư mục chứa exe.</param>
    public CustomRuleStore(string filePath, IEnvironment env, ILogger log,
        IEnumerable<CleanRule>? builtInRules = null, string? appDataDir = null, string? appExeDir = null)
    {
        _filePath = filePath;
        _env = env;
        _log = log;
        _builtInRules = builtInRules?.ToList() ?? [];
        _appDataDir = appDataDir ?? DefaultAppDataDir(env);
        _appExeDir = appExeDir ?? AppContext.BaseDirectory;
        _forbidden = SafetyGuard.ForbiddenRoots(env);
    }

    public static string DefaultAppDataDir(IEnvironment env)
    {
        var local = env.Get("LOCALAPPDATA");
        return string.IsNullOrWhiteSpace(local) ? "" : Path.Combine(local, "DiskCleaner");
    }

    /// <summary>Id ổn định: "custom-" + 8 ký tự hex đầu của SHA256(path chuẩn hóa, chữ thường).</summary>
    public static string ComputeId(string folderPath)
    {
        var norm = (PathHelper.TryNormalize(folderPath) ?? folderPath).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return "custom-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Tên hiển thị mặc định: "&lt;thư mục cha&gt;\&lt;tên&gt;" (vd CapCut\Cache).</summary>
    public static string DefaultDisplayName(string folderPath)
    {
        var norm = PathHelper.TryNormalize(folderPath) ?? folderPath;
        var name = Path.GetFileName(norm);
        var parent = Path.GetFileName(Path.GetDirectoryName(norm) ?? "");
        if (string.IsNullOrEmpty(name)) return norm;
        return string.IsNullOrEmpty(parent) ? name : parent + "\\" + name;
    }

    /// <summary>Đọc lại file; rule lỗi hoặc bị Blocked sẽ bị loại và ghi log. Không bao giờ throw.</summary>
    public IReadOnlyList<CleanRule> LoadAll()
    {
        lock (_lock)
        {
            _rules = ReadFromDisk();
            return _rules.ToList();
        }
    }

    /// <summary>Danh sách hiện tại (đọc file lần đầu nếu chưa nạp).</summary>
    public IReadOnlyList<CleanRule> Current
    {
        get { lock (_lock) return (_rules ??= ReadFromDisk()).ToList(); }
    }

    private List<CleanRule> ReadFromDisk()
    {
        var result = new List<CleanRule>();
        if (!File.Exists(_filePath)) return result;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_filePath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("rules", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new JsonException("Thiếu mảng 'rules'");

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in arr.EnumerateArray())
            {
                try
                {
                    var raw = el.Deserialize<CleanRule>(JsonDefaults.Options);
                    if (raw == null) continue;
                    var err = ValidateLoaded(raw, result, seenIds, out var clean);
                    if (err != null)
                    {
                        _log.Error($"Loại rule tùy chỉnh '{raw.Id}': {err}");
                        continue;
                    }
                    seenIds.Add(clean!.Id);
                    result.Add(clean);
                }
                catch (JsonException ex)
                {
                    _log.Error("Loại rule tùy chỉnh không đọc được: " + ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Error($"Không đọc được {_filePath}", ex);
            return [];
        }
        return result;
    }

    /// <summary>Trả null nếu hợp lệ (và đặt clean = rule đã làm sạch), ngược lại là lý do loại.</summary>
    private string? ValidateLoaded(CleanRule raw, List<CleanRule> accepted, HashSet<string> seenIds, out CleanRule? clean)
    {
        clean = null;
        if (raw.Action != RuleAction.DeleteFiles) return "Action không phải DeleteFiles";
        if (raw.Paths.Length != 1) return "Cần đúng 1 path";
        var path = raw.Paths[0];
        if (string.IsNullOrWhiteSpace(raw.Id) || seenIds.Contains(raw.Id)) return "Id thiếu hoặc bị trùng";

        // Dựng lại rule từ các trường an toàn: bỏ NamePatterns/Command/RequiresAdmin do file tự khai.
        var candidate = BuildRule(raw.Id, path, string.IsNullOrWhiteSpace(raw.Name) ? DefaultDisplayName(path) : raw.Name,
            raw.MinAgeHours);

        var verr = RuleValidator.Validate(candidate, _env, _forbidden);
        if (verr != null) return verr;

        var check = Evaluate(path, accepted.SelectMany(r => r.Paths));
        if (check.IsBlocked) return "Bị chặn: " + string.Join(", ", check.ReasonKeys);

        clean = candidate;
        return null;
    }

    private static CleanRule BuildRule(string id, string path, string displayName, int minAgeHours) => new()
    {
        Id = id,
        Name = displayName,
        NameEn = displayName,
        Description = path,
        DescriptionEn = path,
        Category = RuleCategory.Custom,
        Action = RuleAction.DeleteFiles,
        Paths = [path],
        MinAgeHours = Math.Max(0, minAgeHours),
        RequiresAdmin = false,
        DefaultSelected = false,
        IsCustom = true
    };

    private CustomRuleCheck Evaluate(string folderPath, IEnumerable<string> extraExistingPaths)
    {
        var existing = _builtInRules.SelectMany(r => r.Paths).Concat(extraExistingPaths);
        return CustomRulePolicy.Evaluate(folderPath, _env, _forbidden, existing, _appDataDir, _appExeDir);
    }

    /// <summary>Kiểm tra một thư mục có thể thêm không (Ok / Warn / Blocked + lý do).</summary>
    public CustomRuleCheck Check(string folderPath)
    {
        lock (_lock)
        {
            var current = _rules ??= ReadFromDisk();
            return Evaluate(folderPath, current.SelectMany(r => r.Paths));
        }
    }

    /// <summary>Thêm rule; ném InvalidOperationException nếu Check trả Blocked. Lưu file rồi raise Changed.</summary>
    public CleanRule Add(string folderPath, string displayName, int minAgeHours)
    {
        CleanRule rule;
        lock (_lock)
        {
            var current = _rules ??= ReadFromDisk();
            var check = Evaluate(folderPath, current.SelectMany(r => r.Paths));
            if (check.IsBlocked)
                throw new InvalidOperationException("Thư mục bị chặn: " + string.Join(", ", check.ReasonKeys));

            var norm = PathHelper.TryNormalize(folderPath)!;
            var name = string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName(norm) : displayName.Trim();
            rule = BuildRule(ComputeId(norm), norm, name, minAgeHours);
            var verr = RuleValidator.Validate(rule, _env, _forbidden);
            if (verr != null) throw new InvalidOperationException("Rule không hợp lệ: " + verr);

            var updated = new List<CleanRule>(current) { rule };
            Save(updated);
            _rules = updated;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return rule;
    }

    /// <summary>Gỡ rule khỏi danh sách (KHÔNG xóa file nào).</summary>
    public void Remove(string ruleId)
    {
        lock (_lock)
        {
            var current = _rules ??= ReadFromDisk();
            var updated = current.Where(r => !string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (updated.Count == current.Count) return;
            Save(updated);
            _rules = updated;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record FileDto(List<CleanRule> Rules);

    private void Save(List<CleanRule> rules)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new FileDto(rules), CustomJson);
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static readonly JsonSerializerOptions CustomJson = new(JsonDefaults.Options)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

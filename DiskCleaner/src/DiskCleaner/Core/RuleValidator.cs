using System.Text.RegularExpressions;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Core;

public sealed record RuleValidationResult(IReadOnlyList<CleanRule> Valid, IReadOnlyList<string> Errors);

public static class RuleValidator
{
    /// <summary>Chỉ các lệnh này được phép chạy qua RunCommand.</summary>
    public static readonly IReadOnlySet<string> AllowedCommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "npm", "dotnet", "pip", "yarn", "docker" };

    private static readonly Regex IdRx = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);
    private static readonly Regex ShortNameRx = new("~\\d", RegexOptions.Compiled);

    public static RuleValidationResult ValidateAll(IEnumerable<CleanRule> rules, IEnvironment env)
    {
        var forbidden = SafetyGuard.ForbiddenRoots(env);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid = new List<CleanRule>();
        var errors = new List<string>();

        foreach (var rule in rules)
        {
            var err = Validate(rule, env, forbidden);
            if (err == null && !seen.Add(rule.Id))
                err = "Id bị trùng";
            if (err != null) errors.Add($"Rule '{rule.Id}': {err}");
            else valid.Add(rule);
        }
        return new RuleValidationResult(valid, errors);
    }

    /// <summary>Trả về null nếu rule hợp lệ, ngược lại là thông báo lỗi.</summary>
    public static string? Validate(CleanRule rule, IEnvironment env, IReadOnlyList<string> forbidden)
    {
        if (string.IsNullOrWhiteSpace(rule.Id) || !IdRx.IsMatch(rule.Id)) return "Id không hợp lệ";
        if (string.IsNullOrWhiteSpace(rule.Name)) return "Thiếu Name";
        if (rule.MinAgeHours < 0) return "MinAgeHours âm";

        switch (rule.Action)
        {
            case RuleAction.DeleteFiles:
                if (rule.Paths.Length == 0) return "DeleteFiles cần ít nhất 1 path";
                break;
            case RuleAction.RunCommand:
                if (string.IsNullOrWhiteSpace(rule.Command)) return "RunCommand thiếu Command";
                if (!AllowedCommands.Contains(rule.Command)) return $"Command '{rule.Command}' không nằm trong whitelist";
                break;
            case RuleAction.EmptyRecycleBin:
                break;
            default:
                return "Action không hợp lệ";
        }

        foreach (var np in rule.NamePatterns)
            if (string.IsNullOrWhiteSpace(np) || np.Contains('\\') || np.Contains('/'))
                return $"NamePattern không hợp lệ: '{np}'";

        foreach (var pattern in rule.Paths)
        {
            var err = ValidatePath(pattern, env, forbidden);
            if (err != null) return err;
        }
        return null;
    }

    private static string? ValidatePath(string pattern, IEnvironment env, IReadOnlyList<string> forbidden)
    {
        var expanded = PathExpander.ExpandVariables(pattern, env);
        if (expanded == null) return $"Path '{pattern}' dùng biến môi trường không tồn tại";
        if (PathHelper.IsUnc(expanded)) return $"Path '{pattern}' là UNC";
        if (!Path.IsPathFullyQualified(expanded)) return $"Path '{pattern}' không phải đường dẫn tuyệt đối";

        var segments = expanded.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == "..")) return $"Path '{pattern}' chứa '..'";
        if (segments.Any(s => ShortNameRx.IsMatch(s))) return $"Path '{pattern}' chứa tên 8.3";
        if (segments.Length > 0 && segments[0].Contains('*')) return $"Path '{pattern}' có wildcard ở ổ đĩa";

        // Thay wildcard bằng tên giả để kiểm tra phần còn lại.
        var concrete = string.Join("\\", segments.Select(s => s.Contains('*') || s.Contains('?') ? "_wild_" : s));
        // "C:\" splits to just "C:", which Path.GetFullPath treats as drive-RELATIVE (the current dir on C:).
        // Keep the root explicit so a bare drive root is always recognised (and rejected).
        if (concrete.Length == 2 && concrete[1] == ':') concrete += "\\";
        var norm = PathHelper.TryNormalize(concrete);
        if (norm == null) return $"Path '{pattern}' không chuẩn hóa được";

        if (SafetyGuard.IsForbidden(norm, forbidden)) return $"Path '{pattern}' nằm trong vùng cấm";

        // Path không được là tổ tiên của vùng cấm (xóa bên trong sẽ lan tới vùng cấm).
        foreach (var entry in forbidden)
        {
            if (entry.StartsWith(SafetyGuard.ExceptionPrefix, StringComparison.Ordinal)) continue;
            var f = entry.StartsWith(SafetyGuard.ExactPrefix, StringComparison.Ordinal) ? entry[1..] : entry;
            if (f.EndsWith('*')) f = Path.GetDirectoryName(f) ?? f;
            if (PathHelper.IsSameOrUnder(f, norm))
                return $"Path '{pattern}' chứa vùng cấm '{f}'";
        }
        return null;
    }
}

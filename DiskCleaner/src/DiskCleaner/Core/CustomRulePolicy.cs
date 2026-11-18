using System.Text;

namespace DiskCleaner.Core;

public enum CustomRuleVerdict { Ok, Warn, Blocked }

/// <summary>ReasonKeys là key i18n (Custom.Block.* / Custom.Warn.*).</summary>
public sealed record CustomRuleCheck(CustomRuleVerdict Verdict, IReadOnlyList<string> ReasonKeys)
{
    public bool IsBlocked => Verdict == CustomRuleVerdict.Blocked;
}

/// <summary>
/// Chính sách cho rule tùy chỉnh (thư mục do người dùng chọn). Gần như thuần: chỉ đọc thuộc tính reparse point
/// và sự tồn tại của ".git". Không bao giờ xóa/ghi gì.
/// </summary>
public static class CustomRulePolicy
{
    public const string BlockInvalid = "Custom.Block.Invalid";
    public const string BlockForbidden = "Custom.Block.Forbidden";
    public const string BlockContainsForbidden = "Custom.Block.ContainsForbidden";
    public const string BlockTooBroad = "Custom.Block.TooBroad";
    public const string BlockAppItself = "Custom.Block.AppItself";
    public const string BlockReparse = "Custom.Block.Reparse";
    public const string BlockAlreadyExists = "Custom.Block.AlreadyExists";
    public const string WarnNotCacheLike = "Custom.Warn.NotCacheLike";
    public const string WarnOutsideAppData = "Custom.Warn.OutsideAppData";
    public const string WarnLooksLikeProject = "Custom.Warn.LooksLikeProject";

    private static readonly HashSet<string> CacheWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache", "caches", "temp", "tmp", "log", "logs", "crash", "dump", "dumps",
        "thumbnails", "shadercache", "gpucache"
    };

    public static CustomRuleCheck Evaluate(string folderPath, IEnvironment env, IReadOnlyList<string> forbiddenRoots,
        IEnumerable<string> existingRulePaths, string appDataDir, string appExeDir)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || PathHelper.IsUnc(folderPath) ||
            !Path.IsPathFullyQualified(folderPath) || folderPath.IndexOf(':', 2) >= 0 ||
            folderPath.IndexOfAny(['%', '*', '?']) >= 0)
            return Blocked(BlockInvalid);

        var norm = PathHelper.TryNormalize(folderPath);
        if (norm == null || PathHelper.IsUnc(norm) || PathHelper.IsDriveRoot(norm))
            return Blocked(BlockInvalid);

        var blocked = new List<string>();

        // 1) Nằm trong / bằng vùng cấm của SafetyGuard.
        if (SafetyGuard.IsForbidden(norm, forbiddenRoots)) blocked.Add(BlockForbidden);

        // 2) Là tổ tiên của vùng cấm (xóa bên trong sẽ lan tới vùng cấm) hoặc của %USERPROFILE% (vd C:\Users).
        var profile = PathHelper.TryNormalize(env.Get("USERPROFILE"));
        bool ancestor = profile != null && PathHelper.IsSameOrUnder(profile, norm);
        foreach (var entry in forbiddenRoots)
        {
            if (ancestor) break;
            if (entry.StartsWith(SafetyGuard.ExceptionPrefix, StringComparison.Ordinal)) continue;
            var f = entry.StartsWith(SafetyGuard.ExactPrefix, StringComparison.Ordinal) ? entry[1..] : entry;
            if (f.EndsWith('*')) f = Path.GetDirectoryName(f) ?? f;
            if (PathHelper.IsSameOrUnder(f, norm)) ancestor = true;
        }
        if (ancestor && !blocked.Contains(BlockForbidden)) blocked.Add(BlockContainsForbidden);

        // 3) Thư mục quá rộng (hoặc tổ tiên của chúng).
        foreach (var broad in BroadDirectories(env, profile))
            if (PathHelper.IsSameOrUnder(broad, norm)) { blocked.Add(BlockTooBroad); break; }

        // 4) Dữ liệu của chính app / thư mục chứa exe (nằm trong hoặc là tổ tiên).
        foreach (var own in new[] { appDataDir, appExeDir })
        {
            var n = PathHelper.TryNormalize(own);
            if (n == null) continue;
            if (PathHelper.IsSameOrUnder(norm, n) || PathHelper.IsSameOrUnder(n, norm))
            {
                blocked.Add(BlockAppItself);
                break;
            }
        }

        // 5) Reparse point.
        if (IsReparsePoint(norm)) blocked.Add(BlockReparse);

        // 6) Đã có trong rule khác.
        if (MatchesAny(norm, existingRulePaths, env)) blocked.Add(BlockAlreadyExists);

        if (blocked.Count > 0) return new CustomRuleCheck(CustomRuleVerdict.Blocked, blocked);

        var warn = new List<string>();
        if (!LooksCacheLike(norm)) warn.Add(WarnNotCacheLike);
        if (!IsInsideAppDataOrTemp(norm, env, profile)) warn.Add(WarnOutsideAppData);
        if (HasGit(norm)) warn.Add(WarnLooksLikeProject);
        return warn.Count > 0
            ? new CustomRuleCheck(CustomRuleVerdict.Warn, warn)
            : new CustomRuleCheck(CustomRuleVerdict.Ok, []);
    }

    private static CustomRuleCheck Blocked(string key) => new(CustomRuleVerdict.Blocked, [key]);

    private static IEnumerable<string> BroadDirectories(IEnvironment env, string? profile)
    {
        var list = new List<string>();
        if (profile != null)
        {
            var appData = Path.Combine(profile, "AppData");
            var local = Path.Combine(appData, "Local");
            var roaming = Path.Combine(appData, "Roaming");
            list.AddRange([appData, local, roaming, Path.Combine(appData, "LocalLow"),
                Path.Combine(local, "Programs"), Path.Combine(local, "Packages"), Path.Combine(local, "Microsoft"),
                Path.Combine(roaming, "Microsoft"),
                Path.Combine(profile, "Downloads"), Path.Combine(profile, ".ssh"), Path.Combine(profile, ".gnupg"),
                Path.Combine(profile, "source"), Path.Combine(profile, ".config")]);
        }
        // LOCALAPPDATA / APPDATA có thể bị chuyển hướng khỏi %USERPROFILE%.
        var la = PathHelper.TryNormalize(env.Get("LOCALAPPDATA"));
        if (la != null)
            list.AddRange([la, Path.Combine(la, "Programs"), Path.Combine(la, "Packages"), Path.Combine(la, "Microsoft")]);
        var ra = PathHelper.TryNormalize(env.Get("APPDATA"));
        if (ra != null) list.AddRange([ra, Path.Combine(ra, "Microsoft")]);
        return list;
    }

    private static bool IsReparsePoint(string norm)
    {
        try
        {
            var di = new DirectoryInfo(norm);
            return di.Exists && (di.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasGit(string norm)
    {
        try
        {
            var git = Path.Combine(norm, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>So path đã chuẩn hóa với danh sách path rule (có thể chứa %VAR% và wildcard).</summary>
    private static bool MatchesAny(string norm, IEnumerable<string> patterns, IEnvironment env)
    {
        var normSegs = norm.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var expanded = PathExpander.ExpandVariables(p, env) ?? p;
            var segs = expanded.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length != normSegs.Length) continue;
            bool all = true;
            for (int i = 0; i < segs.Length && all; i++)
                all = segs[i].IndexOfAny(['*', '?']) >= 0
                    ? Glob.IsMatch(segs[i], normSegs[i])
                    : string.Equals(segs[i], normSegs[i], StringComparison.OrdinalIgnoreCase);
            if (all) return true;
        }
        return false;
    }

    /// <summary>Tên thư mục và tối đa 3 thư mục cha có từ khóa cache/tạm không.</summary>
    private static bool LooksCacheLike(string norm)
    {
        var segs = norm.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // segs[0] là ổ đĩa ("C:"), không xét.
        for (int i = segs.Length - 1, n = 0; i >= 1 && n < 4; i--, n++)
            if (SegmentHasKeyword(segs[i])) return true;
        return false;
    }

    private static bool SegmentHasKeyword(string segment)
    {
        foreach (var token in Tokenize(segment))
            if (CacheWords.Contains(token) || token.EndsWith("cache", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Tách theo ký tự không phải chữ/số và theo ranh giới camelCase ("GPUCache" thành "GPU", "Cache").</summary>
    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            bool boundary = sb.Length > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) ||
                            (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1])));
            if (boundary) { tokens.Add(sb.ToString()); sb.Clear(); }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static bool IsInsideAppDataOrTemp(string norm, IEnvironment env, string? profile)
    {
        var roots = new List<string?>
        {
            profile == null ? null : Path.Combine(profile, "AppData"),
            env.Get("LOCALAPPDATA"), env.Get("APPDATA"), env.Get("TEMP"), env.Get("TMP")
        };
        foreach (var r in roots)
        {
            var n = PathHelper.TryNormalize(r);
            if (n != null && PathHelper.IsSameOrUnder(norm, n)) return true;
        }
        return false;
    }
}

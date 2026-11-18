namespace DiskCleaner.Core;

/// <summary>
/// Lớp bảo vệ cuối cùng trước mọi thao tác xóa. Thuần logic (không I/O).
///
/// Danh sách ForbiddenRoots dùng 3 loại phần tử:
///   "C:\Windows"      - cấm cả thư mục và mọi thứ bên trong.
///   "=C:\Users\x"     - chỉ cấm CHÍNH path này (không cấm nội dung bên trong).
///   "!C:\Windows\Temp"- ngoại lệ: path nằm trong ngoại lệ thì không bị các root cấm khác chặn.
///   "C:\Users\x\OneDrive*" - cấm mọi thư mục con cấp 1 có tên bắt đầu bằng "OneDrive".
/// </summary>
public static class SafetyGuard
{
    public const string ExactPrefix = "=";
    public const string ExceptionPrefix = "!";

    public static IReadOnlyList<string> ForbiddenRoots(IEnvironment env)
    {
        var list = new List<string>();

        void AddRecursive(string? p)
        {
            var n = PathHelper.TryNormalize(p);
            if (n != null) list.Add(n);
        }

        var win = env.Get("WINDIR") ?? env.Get("SystemRoot");
        if (!string.IsNullOrWhiteSpace(win))
        {
            AddRecursive(win);
            var w = PathHelper.TryNormalize(win);
            if (w != null)
            {
                list.Add(ExceptionPrefix + Path.Combine(w, "Temp"));
                list.Add(ExceptionPrefix + Path.Combine(w, "SoftwareDistribution", "Download"));
            }
        }

        AddRecursive(env.Get("PROGRAMFILES"));
        AddRecursive(env.Get("PROGRAMFILES(X86)"));
        AddRecursive(env.Get("ProgramW6432"));
        AddRecursive(env.Get("PROGRAMDATA"));
        AddRecursive(env.Get("OneDrive"));
        AddRecursive(env.Get("OneDriveConsumer"));
        AddRecursive(env.Get("OneDriveCommercial"));

        var profile = PathHelper.TryNormalize(env.Get("USERPROFILE"));
        if (profile != null)
        {
            foreach (var d in new[] { "Documents", "Desktop", "Pictures", "Videos", "Music" })
                list.Add(Path.Combine(profile, d));
            list.Add(Path.Combine(profile, "OneDrive*"));
            list.Add(ExactPrefix + profile);
        }

        var sysDrive = env.Get("SystemDrive");
        if (!string.IsNullOrWhiteSpace(sysDrive))
        {
            var root = PathHelper.TryNormalize(sysDrive.TrimEnd('\\') + "\\");
            if (root != null) list.Add(ExactPrefix + root);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>path (sau chuẩn hóa) bằng hoặc nằm trong một forbidden root (có tính ngoại lệ).</summary>
    public static bool IsForbidden(string path, IReadOnlyList<string> forbiddenRoots)
    {
        var target = PathHelper.TryNormalize(path);
        if (target == null) return true;
        return IsForbiddenNormalized(target, forbiddenRoots);
    }

    private static bool IsForbiddenNormalized(string target, IReadOnlyList<string> forbidden)
    {
        if (PathHelper.IsDriveRoot(target)) return true;

        foreach (var entry in forbidden)
        {
            if (entry.StartsWith(ExceptionPrefix, StringComparison.Ordinal)) continue;

            if (entry.StartsWith(ExactPrefix, StringComparison.Ordinal))
            {
                if (PathHelper.Equal(target, entry[1..])) return true;
            }
            else if (entry.EndsWith('*'))
            {
                var dir = Path.GetDirectoryName(entry);
                var prefix = Path.GetFileName(entry).TrimEnd('*');
                if (dir != null && PathHelper.IsStrictlyUnder(target, dir))
                {
                    var rel = target[(dir.TrimEnd('\\').Length + 1)..];
                    var first = rel.Split('\\')[0];
                    if (first.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            else if (PathHelper.IsSameOrUnder(target, entry))
            {
                bool carvedOut = false;
                foreach (var ex in forbidden)
                {
                    if (ex.StartsWith(ExceptionPrefix, StringComparison.Ordinal) &&
                        PathHelper.IsSameOrUnder(target, ex[1..]))
                    {
                        carvedOut = true;
                        break;
                    }
                }
                if (!carvedOut) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// true nếu target nằm BÊN TRONG (không bằng) một allowed root và không thuộc forbidden.
    /// Từ chối: path rỗng/tương đối, UNC, drive root, alternate data stream.
    /// </summary>
    public static bool IsSafeToDelete(string targetPath, IEnumerable<string> allowedRoots,
                                      IReadOnlyList<string> forbiddenRoots)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return false;
        if (PathHelper.IsUnc(targetPath)) return false;
        if (!Path.IsPathFullyQualified(targetPath)) return false;
        if (targetPath.IndexOf(':', 2) >= 0) return false;

        var target = PathHelper.TryNormalize(targetPath);
        if (target == null) return false;
        if (IsForbiddenNormalized(target, forbiddenRoots)) return false;

        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || PathHelper.IsUnc(root) || !Path.IsPathFullyQualified(root))
                continue;
            var nr = PathHelper.TryNormalize(root);
            if (nr == null || PathHelper.IsDriveRoot(nr)) continue;
            if (IsForbiddenNormalized(nr, forbiddenRoots)) continue;
            if (PathHelper.IsStrictlyUnder(target, nr)) return true;
        }
        return false;
    }
}

/// <summary>Gói biến môi trường + forbidden roots để truyền cho CleanExecutor.</summary>
public sealed class SafetyGuardContext
{
    public IEnvironment Env { get; }
    public IReadOnlyList<string> ForbiddenRoots { get; }

    public SafetyGuardContext(IEnvironment env)
    {
        Env = env;
        ForbiddenRoots = SafetyGuard.ForbiddenRoots(env);
    }

    public bool IsForbidden(string path) => SafetyGuard.IsForbidden(path, ForbiddenRoots);

    public bool IsSafeToDelete(string target, IEnumerable<string> allowedRoots) =>
        SafetyGuard.IsSafeToDelete(target, allowedRoots, ForbiddenRoots);
}

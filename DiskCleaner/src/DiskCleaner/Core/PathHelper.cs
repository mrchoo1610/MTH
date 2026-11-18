namespace DiskCleaner.Core;

internal static class PathHelper
{
    private const StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    public static string? TryNormalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return TrimTrailing(Path.GetFullPath(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string TrimTrailing(string p)
    {
        var root = Path.GetPathRoot(p);
        if (root != null && p.Length <= root.Length) return p;
        return p.TrimEnd('\\', '/');
    }

    public static bool IsUnc(string p) => p.StartsWith(@"\\", Cmp) || p.StartsWith("//", Cmp);

    public static bool IsDriveRoot(string normalized)
    {
        var root = Path.GetPathRoot(normalized);
        return root != null && string.Equals(root.TrimEnd('\\', '/'), normalized.TrimEnd('\\', '/'), Cmp);
    }

    public static bool Equal(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), Cmp);

    /// <summary>target nằm BÊN TRONG root (không bằng), so sánh theo ranh giới thư mục.</summary>
    public static bool IsStrictlyUnder(string target, string root)
    {
        var prefix = root.TrimEnd('\\', '/') + "\\";
        return target.Length > prefix.Length && target.StartsWith(prefix, Cmp);
    }

    public static bool IsSameOrUnder(string target, string root) =>
        Equal(target, root) || IsStrictlyUnder(target, root);
}

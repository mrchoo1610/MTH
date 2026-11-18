using System.Text.RegularExpressions;

namespace DiskCleaner.Core;

public static class PathExpander
{
    private static readonly Regex VarRx = new("%([^%]+)%", RegexOptions.Compiled);

    /// <summary>Thay %VAR%; trả null nếu có biến không tồn tại/rỗng.</summary>
    public static string? ExpandVariables(string pattern, IEnvironment env)
    {
        bool failed = false;
        var result = VarRx.Replace(pattern, m =>
        {
            var v = env.Get(m.Groups[1].Value);
            if (string.IsNullOrEmpty(v)) { failed = true; return m.Value; }
            return v;
        });
        return failed ? null : result;
    }

    /// <summary>
    /// Mở rộng %VAR% và wildcard '*' ở từng cấp thư mục; trả về các path (thư mục) TỒN TẠI.
    /// exists mặc định là Directory.Exists.
    /// </summary>
    public static IReadOnlyList<string> Expand(string pattern, IEnvironment env,
        Func<string, IEnumerable<string>> listDirs, Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        var expanded = ExpandVariables(pattern, env);
        if (expanded == null || PathHelper.IsUnc(expanded)) return [];

        var segments = expanded.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments[0].Length != 2 || segments[0][1] != ':' || segments[0].Contains('*'))
            return [];

        var current = new List<string> { segments[0] + "\\" };
        for (int i = 1; i < segments.Length; i++)
        {
            var seg = segments[i];
            var next = new List<string>();
            if (seg.Contains('*') || seg.Contains('?'))
            {
                foreach (var dir in current)
                {
                    List<string> children;
                    try { children = listDirs(dir).ToList(); }
                    catch (Exception) { continue; }
                    foreach (var child in children)
                        if (Glob.IsMatch(seg, Path.GetFileName(child.TrimEnd('\\', '/'))))
                            next.Add(child);
                }
            }
            else
            {
                foreach (var dir in current) next.Add(Path.Combine(dir, seg));
            }
            current = next;
            if (current.Count == 0) return [];
        }

        return current.Where(exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

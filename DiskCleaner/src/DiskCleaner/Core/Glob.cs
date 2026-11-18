using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DiskCleaner.Core;

/// <summary>So khớp tên với wildcard '*' và '?', không phân biệt hoa thường.</summary>
public static class Glob
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    public static bool IsMatch(string pattern, string name)
    {
        var rx = Cache.GetOrAdd(pattern, p =>
            new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return rx.IsMatch(name);
    }
}

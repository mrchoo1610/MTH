using System.Runtime.InteropServices;
using System.Text;
using DiskCleaner.Core;

namespace DiskCleaner.Services;

/// <summary>
/// Real environment variables. Values that contain 8.3 short names (e.g. TEMP = C:\Users\NGUYEN~1\AppData\Local\Temp,
/// common for long or non-ASCII user names) are expanded to their long form, because the rule validator and
/// SafetyGuard compare long paths and deliberately reject 8.3 aliases in rule patterns.
/// </summary>
public sealed class SystemEnvironment : IEnvironment
{
    public string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is not null && value.Contains('~') ? ToLongPath(value) : value;
    }

    /// <summary>Long form of an existing path; returns the input unchanged if it cannot be resolved.</summary>
    public static string ToLongPath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return path;
        try
        {
            var sb = new StringBuilder(512);
            var len = GetLongPathName(path, sb, (uint)sb.Capacity);
            if (len > sb.Capacity)
            {
                sb = new StringBuilder((int)len);
                len = GetLongPathName(path, sb, (uint)sb.Capacity);
            }
            return len == 0 ? path : sb.ToString();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return path;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);
}

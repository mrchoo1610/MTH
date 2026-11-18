using System.Globalization;

namespace MiniTools.Contracts;

/// <summary>Version as Major.Minor.Patch (compared numerically, never as strings).</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    /// <summary>Accepts "1.2.0" or "v1.2.0". Throws <see cref="FormatException"/> on bad input.</summary>
    public static AppVersion Parse(string s)
    {
        if (!TryParse(s, out var v))
            throw new FormatException($"Invalid version: '{s}'. Expected Major.Minor.Patch (e.g. 1.2.0).");
        return v;
    }

    public static bool TryParse(string? s, out AppVersion v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var span = s.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];

        var parts = span.ToString().Split('.');
        if (parts.Length != 3) return false;

        var nums = new int[3];
        for (var i = 0; i < 3; i++)
        {
            // Decimal digits only: no sign, no whitespace.
            if (parts[i].Length == 0 || parts[i].Length > 9) return false;
            foreach (var c in parts[i])
                if (c < '0' || c > '9') return false;
            nums[i] = int.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture);
        }

        v = new AppVersion(nums[0], nums[1], nums[2]);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}

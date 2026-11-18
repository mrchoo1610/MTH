using System.Text.Json;

namespace MiniTools.Updater;

/// <summary>Per-app update preferences: %LOCALAPPDATA%\MiniTools\{appId}\update.json.</summary>
public sealed class UpdateSettingsStore
{
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();

    public UpdateSettingsStore(string appId, string? dataRoot = null)
    {
        FilePath = Path.Combine(dataRoot ?? UpdatePaths.DataRoot(), appId, "update.json");
    }

    public string FilePath { get; }
    public bool AutoCheck { get; set; } = true;
    public DateTimeOffset? LastCheckUtc { get; set; }
    public string? SkippedVersion { get; set; }

    private sealed class Dto
    {
        public bool AutoCheck { get; set; } = true;
        public DateTimeOffset? LastCheckUtc { get; set; }
        public string? SkippedVersion { get; set; }
    }

    /// <summary>Loads from disk; a missing or corrupt file leaves the defaults.</summary>
    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath), Json);
                if (dto is null) return;
                AutoCheck = dto.AutoCheck;
                LastCheckUtc = dto.LastCheckUtc;
                SkippedVersion = dto.SkippedVersion;
            }
            catch (Exception)
            {
                AutoCheck = true;
                LastCheckUtc = null;
                SkippedVersion = null;
            }
        }
    }

    /// <summary>Best effort: never throws.</summary>
    public void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var dto = new Dto { AutoCheck = AutoCheck, LastCheckUtc = LastCheckUtc, SkippedVersion = SkippedVersion };
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(dto, Json));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception) { }
        }
    }

    /// <summary>True when auto-check is enabled and the last check was at least 24h ago (or never).</summary>
    public bool IsAutoCheckDue(DateTimeOffset now) =>
        AutoCheck && (LastCheckUtc is null || now - LastCheckUtc.Value >= AutoCheckInterval || LastCheckUtc.Value > now);
}

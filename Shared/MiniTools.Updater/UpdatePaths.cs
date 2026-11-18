namespace MiniTools.Updater;

/// <summary>Where the updater keeps its files. Override the root with MINITOOLS_DATA_DIR (used by tests/e2e).</summary>
public static class UpdatePaths
{
    public const string DataDirEnvVar = "MINITOOLS_DATA_DIR";

    public static string DataRoot()
    {
        var env = Environment.GetEnvironmentVariable(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniTools");
    }

    public static string UpdatesRoot(string? dataRoot = null) => Path.Combine(dataRoot ?? DataRoot(), "updates");
}

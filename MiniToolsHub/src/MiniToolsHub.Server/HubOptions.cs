namespace MiniToolsHub.Server;

public sealed class HubOptions
{
    public string PublicBaseUrl { get; set; } = "https://mth.mimehub.top";
    public string DataDir { get; set; } = "/data";
    public string ContentDir { get; set; } = "/app/content";
    /// <summary>SHA-256 hex of the admin token (the raw token is never stored).</summary>
    public string AdminTokenSha256 { get; set; } = "";
    public int MaxUploadMb { get; set; } = 95;
    public string PublicKeyPem { get; set; } = "";
    public int AdminRequestsPerMinute { get; set; } = 10;
    public int PublicRequestsPerMinute { get; set; } = 120;

    public string FilesRoot => Path.Combine(Path.GetFullPath(DataDir), "files");
}

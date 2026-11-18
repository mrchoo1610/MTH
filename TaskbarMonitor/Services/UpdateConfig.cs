namespace TaskbarMonitor.Services;

/// <summary>Cau hinh cap nhat tu dong: sua dung mot cho nay khi doi domain hub.</summary>
public static class UpdateConfig
{
    public const string HubBaseUrl = "https://mth.mimehub.top";
    public const string AppId = "taskbar-monitor";

    /// <summary>Ban Debug cho phep ghi de URL hub bang bien moi truong (test voi hub local). Release luon dung HubBaseUrl.</summary>
    public static string ResolveHubBaseUrl()
    {
#if DEBUG
        var o = Environment.GetEnvironmentVariable("MINITOOLS_UPDATE_HUB_URL");
        if (!string.IsNullOrWhiteSpace(o)) return o;
#endif
        return HubBaseUrl;
    }
}

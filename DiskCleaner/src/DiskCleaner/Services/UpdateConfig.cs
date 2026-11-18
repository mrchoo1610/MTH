namespace DiskCleaner.Services;

/// <summary>Cấu hình cập nhật tự động: sửa đúng một chỗ này khi đổi domain hub.</summary>
public static class UpdateConfig
{
    public const string HubBaseUrl = "https://mth.mimehub.top";
    public const string AppId = "disk-cleaner";

    /// <summary>
    /// Bản Debug cho phép ghi đè URL hub bằng biến môi trường (dùng để test với hub chạy local).
    /// Bản Release luôn dùng <see cref="HubBaseUrl"/>.
    /// </summary>
    public static string ResolveHubBaseUrl()
    {
#if DEBUG
        var o = Environment.GetEnvironmentVariable("MINITOOLS_UPDATE_HUB_URL");
        if (!string.IsNullOrWhiteSpace(o)) return o;
#endif
        return HubBaseUrl;
    }
}

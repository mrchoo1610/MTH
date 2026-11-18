using System.Globalization;
using System.Text.Json;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>Lưu cài đặt người dùng vào %LOCALAPPDATA%\DiskCleaner\settings.json ({ "language": "vi" }). Không bao giờ throw.</summary>
public sealed class SettingsService
{
    private sealed class SettingsDto
    {
        public string? Language { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly CultureInfo _uiCulture;

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskCleaner", "settings.json");

    public SettingsService(string? path = null, CultureInfo? uiCulture = null)
    {
        _path = path ?? DefaultPath;
        _uiCulture = uiCulture ?? CultureInfo.CurrentUICulture;
    }

    /// <summary>Ngôn ngữ đã lưu; lần đầu (chưa có file/giá trị hợp lệ): UI culture là "vi" thì Vi, còn lại En.</summary>
    public AppLanguage LoadLanguage()
    {
        try
        {
            if (File.Exists(_path))
            {
                var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(_path), Options);
                var code = dto?.Language?.Trim().ToLowerInvariant();
                if (code == "vi") return AppLanguage.Vi;
                if (code == "en") return AppLanguage.En;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return _uiCulture.TwoLetterISOLanguageName.Equals("vi", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Vi
            : AppLanguage.En;
    }

    public void SaveLanguage(AppLanguage lang)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var dto = new SettingsDto { Language = lang == AppLanguage.Vi ? "vi" : "en" };
            File.WriteAllText(_path, JsonSerializer.Serialize(dto, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Instance.Error("Không lưu được settings", ex);
        }
    }
}

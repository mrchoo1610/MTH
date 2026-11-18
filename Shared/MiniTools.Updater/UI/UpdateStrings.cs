using System.Globalization;
using MiniTools.Contracts;

namespace MiniTools.Updater.UI;

/// <summary>Texts of the update dialog, supplied by the host app (so each app keeps its own localization).</summary>
public sealed class UpdateStrings
{
    public string Title { get; init; } = "";
    /// <summary>{0} = new version, {1} = current version.</summary>
    public string NewVersionFormat { get; init; } = "";
    /// <summary>{0} = size (e.g. 1.2 MB).</summary>
    public string SizeFormat { get; init; } = "";
    public string NotesHeader { get; init; } = "";
    public string UpdateNow { get; init; } = "";
    public string Later { get; init; } = "";
    public string SkipVersion { get; init; } = "";
    public string Downloading { get; init; } = "";
    public string Installing { get; init; } = "";
    /// <summary>{0} = error message.</summary>
    public string ErrorFormat { get; init; } = "";
    public string BusyMessage { get; init; } = "";

    public static UpdateStrings Vi { get; } = new()
    {
        Title = "Có bản cập nhật",
        NewVersionFormat = "Có bản mới {0} (bạn đang dùng {1})",
        SizeFormat = "Dung lượng: {0}",
        NotesHeader = "Có gì mới",
        UpdateNow = "Cập nhật ngay",
        Later = "Để sau",
        SkipVersion = "Bỏ qua bản này",
        Downloading = "Đang tải và kiểm tra chữ ký...",
        Installing = "Đang cài đặt, ứng dụng sẽ tự khởi động lại...",
        ErrorFormat = "Cập nhật thất bại: {0}",
        BusyMessage = "Ứng dụng đang bận. Hãy cập nhật sau khi tác vụ hiện tại hoàn tất.",
    };

    public static UpdateStrings En { get; } = new()
    {
        Title = "Update available",
        NewVersionFormat = "Version {0} is available (you have {1})",
        SizeFormat = "Download size: {0}",
        NotesHeader = "What's new",
        UpdateNow = "Update now",
        Later = "Later",
        SkipVersion = "Skip this version",
        Downloading = "Downloading and verifying signature...",
        Installing = "Installing; the app will restart by itself...",
        ErrorFormat = "Update failed: {0}",
        BusyMessage = "The app is busy. Update after the current task finishes.",
    };

    public static UpdateStrings For(string language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? En : Vi;

    /// <summary>Notes in the requested language, else English, else any, else empty.</summary>
    public static string PickNotes(ReleaseManifest m, string language)
    {
        if (m.Notes is null || m.Notes.Count == 0) return "";
        if (m.Notes.TryGetValue(language, out var s) && !string.IsNullOrWhiteSpace(s)) return s;
        if (m.Notes.TryGetValue("en", out s) && !string.IsNullOrWhiteSpace(s)) return s;
        return m.Notes.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }

    public static string FormatSize(long bytes)
    {
        double v = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return string.Format(CultureInfo.CurrentCulture, i == 0 ? "{0:0} {1}" : "{0:0.#} {1}", v, units[i]);
    }
}

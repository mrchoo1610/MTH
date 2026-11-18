using System.Net.Http;
using System.Reflection;
using System.Windows;
using DiskCleaner.Core.Models;
using MiniTools.Contracts;
using MiniTools.Updater;
using MiniTools.Updater.UI;

namespace DiskCleaner.Services;

/// <summary>Gắn MiniTools.Updater vào Disk Cleaner: client, cài đặt, auto-check nền, hộp thoại cập nhật.</summary>
public sealed class AppUpdateService : IDisposable
{
    private readonly LocalizationService _loc;
    private readonly Func<bool> _isBusy;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly CancellationTokenSource _cts = new();

    public UpdateClient Client { get; }
    public UpdateSettingsStore Store { get; }

    /// <summary>Bản mới đang chờ người dùng (null nếu không có).</summary>
    public ReleaseManifest? Available { get; private set; }

    public event EventHandler? AvailableChanged;

    public AppUpdateService(LocalizationService loc, Func<bool> isBusy)
    {
        _loc = loc;
        _isBusy = isBusy;
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        var options = new UpdateOptions(UpdateConfig.AppId, new AppVersion(v.Major, v.Minor, Math.Max(0, v.Build)),
            new Uri(UpdateConfig.ResolveHubBaseUrl()));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DiskCleaner/{options.CurrentVersion}");
        Client = new UpdateClient(options, _http, PublicKey.Pem, AppLog.Instance.Info);
        Store = new UpdateSettingsStore(UpdateConfig.AppId);
        Store.Load();
    }

    /// <summary>Check nền: 10s sau khi mở app, tối đa 1 lần/24h, không chạy khi đang quét/dọn.</summary>
    public void StartAutoCheck()
    {
        var checker = new AutoUpdateChecker(Client, Store, _isBusy, m => SetAvailable(m));
        _ = Task.Run(() => checker.RunAsync(_cts.Token));
    }

    private void SetAvailable(ReleaseManifest? m)
    {
        void Apply()
        {
            Available = m;
            AvailableChanged?.Invoke(this, EventArgs.Empty);
        }
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) Apply();
        else d.BeginInvoke(Apply);
    }

    public string Language => _loc.Current == AppLanguage.En ? "en" : "vi";

    public UpdateStrings BuildStrings() => new()
    {
        Title = _loc.Get("Update.Title"),
        NewVersionFormat = _loc.Get("Update.NewVersion"),
        SizeFormat = _loc.Get("Update.Size"),
        NotesHeader = _loc.Get("Update.Notes"),
        UpdateNow = _loc.Get("Update.Now"),
        Later = _loc.Get("Update.Later"),
        SkipVersion = _loc.Get("Update.Skip"),
        Downloading = _loc.Get("Update.Downloading"),
        Installing = _loc.Get("Update.Installing"),
        ErrorFormat = _loc.Get("Update.Error"),
        BusyMessage = _loc.Get("Update.Busy"),
    };

    /// <summary>Kiểm tra thủ công. Trả về trạng thái để UI báo cho người dùng.</summary>
    public async Task<UpdateCheckStatus> CheckNowAsync()
    {
        var m = await Client.CheckAsync(_cts.Token);
        if (Client.LastStatus is UpdateCheckStatus.UpToDate or UpdateCheckStatus.Available)
        {
            Store.LastCheckUtc = DateTimeOffset.UtcNow;
            Store.Save();
        }
        SetAvailable(m);
        return Client.LastStatus;
    }

    public void ShowDialog(ReleaseManifest m)
    {
        UpdateDialog.ShowFor(Application.Current?.MainWindow, Client, m, BuildStrings(), Language, Store, _isBusy);
        // "Bỏ qua bản này" -> tắt badge.
        if (Available != null && Store.SkippedVersion == Available.Version) SetAvailable(null);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}

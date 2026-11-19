using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MiniTools.Contracts;
using MiniTools.Updater;
using MiniTools.Updater.UI;

namespace TaskbarMonitor.Services;

/// <summary>Gan MiniTools.Updater vao Taskbar Monitor (ngon ngu: tieng Viet).</summary>
public sealed class AppUpdateService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly CancellationTokenSource _cts = new();

    public UpdateClient Client { get; }
    public UpdateSettingsStore Store { get; }
    public ReleaseManifest? Available { get; private set; }

    /// <summary>Raised on a background thread when the auto-check finds a new version.</summary>
    public event Action<ReleaseManifest>? UpdateFound;

    public static string CurrentVersionText { get; } = GetVersion().ToString();

    private static AppVersion GetVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        return new AppVersion(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public AppUpdateService()
    {
        var options = new UpdateOptions(UpdateConfig.AppId, GetVersion(), new Uri(UpdateConfig.ResolveHubBaseUrl()), "stable", "vi");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"TaskbarMonitor/{options.CurrentVersion}");
        Client = new UpdateClient(options, _http, PublicKey.Pem, Logger.Log);
        Store = new UpdateSettingsStore(UpdateConfig.AppId);
        Store.Load();
    }

    /// <summary>Check nen: 10s sau khi mo app, toi da 1 lan/24h.</summary>
    public void StartAutoCheck()
    {
        var checker = new AutoUpdateChecker(Client, Store, () => false, m =>
        {
            Available = m;
            UpdateFound?.Invoke(m);
        });
        _ = Task.Run(() => checker.RunAsync(_cts.Token));
    }

    /// <summary>Kiem tra thu cong (tray menu).</summary>
    public async Task<UpdateCheckStatus> CheckNowAsync()
    {
        var m = await Client.CheckAsync(_cts.Token);
        if (Client.LastStatus is UpdateCheckStatus.UpToDate or UpdateCheckStatus.Available)
        {
            Store.LastCheckUtc = DateTimeOffset.UtcNow;
            Store.Save();
        }
        Available = m;
        return Client.LastStatus;
    }

    public void ShowDialog(System.Windows.Window? owner, ReleaseManifest m) =>
        UpdateDialog.ShowFor(owner, Client, m, UpdateStrings.En, "en", Store);

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}

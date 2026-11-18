using MiniTools.Contracts;

namespace MiniTools.Updater;

/// <summary>
/// Background auto-check: waits <see cref="InitialDelay"/> (10s), runs at most once per 24h,
/// postpones while the app is busy, ignores the version the user chose to skip.
/// </summary>
public sealed class AutoUpdateChecker
{
    private readonly UpdateClient _client;
    private readonly UpdateSettingsStore _store;
    private readonly Func<bool> _isBusy;
    private readonly Action<ReleaseManifest> _onAvailable;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan BusyRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    public AutoUpdateChecker(UpdateClient client, UpdateSettingsStore store, Func<bool> isBusy,
        Action<ReleaseManifest> onAvailable,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? now = null)
    {
        _client = client;
        _store = store;
        _isBusy = isBusy;
        _onAvailable = onAvailable;
        _delay = delay ?? Task.Delay;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Returns the manifest that was reported via the callback (or null).</summary>
    public async Task<ReleaseManifest?> RunAsync(CancellationToken ct)
    {
        try
        {
            await _delay(InitialDelay, ct).ConfigureAwait(false);
            _store.Load();
            if (!_store.IsAutoCheckDue(_now())) return null;

            while (_isBusy())
                await _delay(BusyRetryDelay, ct).ConfigureAwait(false);

            var m = await _client.CheckAsync(ct).ConfigureAwait(false);
            if (_client.LastStatus is UpdateCheckStatus.UpToDate or UpdateCheckStatus.Available)
            {
                _store.LastCheckUtc = _now();
                _store.Save();
            }
            if (m is null || string.Equals(m.Version, _store.SkippedVersion, StringComparison.Ordinal)) return null;
            _onAvailable(m);
            return m;
        }
        catch (OperationCanceledException) { return null; }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MiniTools.Contracts;

namespace MiniTools.Updater.UI;

/// <summary>Dark update dialog: release notes, "Update now" (progress then restart), "Later", "Skip this version".</summary>
public partial class UpdateDialog : Window
{
    private readonly UpdateClient _client;
    private readonly ReleaseManifest _manifest;
    private readonly UpdateStrings _s;
    private readonly UpdateSettingsStore? _store;
    private readonly Func<bool>? _isBusy;
    private readonly CancellationTokenSource _cts = new();
    private bool _working;

    public UpdateDialog(UpdateClient client, ReleaseManifest manifest, UpdateStrings strings, string language,
        UpdateSettingsStore? store = null, Func<bool>? isBusy = null)
    {
        InitializeComponent();
        _client = client;
        _manifest = manifest;
        _s = strings;
        _store = store;
        _isBusy = isBusy;

        Title = strings.Title;
        TitleText.Text = strings.Title;
        VersionText.Text = string.Format(CultureInfo.CurrentCulture, strings.NewVersionFormat,
            manifest.Version, client.Options.CurrentVersion);
        SizeText.Text = string.Format(CultureInfo.CurrentCulture, strings.SizeFormat, UpdateStrings.FormatSize(manifest.SizeBytes));
        NotesHeader.Text = strings.NotesHeader;
        var notes = UpdateStrings.PickNotes(manifest, language);
        NotesText.Text = notes;
        if (string.IsNullOrWhiteSpace(notes)) NotesPanel.Visibility = Visibility.Collapsed;
        UpdateButton.Content = strings.UpdateNow;
        LaterButton.Content = strings.Later;
        SkipButton.Content = strings.SkipVersion;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        Closing += (_, e) =>
        {
            if (_working) e.Cancel = true; // do not close mid-install
        };
        Closed += (_, _) => _cts.Cancel();
    }

    /// <summary>Shows the dialog modally (centered on <paramref name="owner"/> when it is visible).</summary>
    public static void ShowFor(Window? owner, UpdateClient client, ReleaseManifest manifest, UpdateStrings strings,
        string language, UpdateSettingsStore? store = null, Func<bool>? isBusy = null)
    {
        var dlg = new UpdateDialog(client, manifest, strings, language, store, isBusy);
        if (owner is { IsVisible: true }) { dlg.Owner = owner; dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        dlg.ShowDialog();
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(error ? "DangerBrush" : "TextMutedBrush");
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        if (_isBusy?.Invoke() == true)
        {
            SetStatus(_s.BusyMessage);
            return;
        }

        _working = true;
        UpdateButton.IsEnabled = LaterButton.IsEnabled = SkipButton.IsEnabled = false;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        SetStatus(_s.Downloading);
        try
        {
            var progress = new Progress<double>(p => Progress.Value = p * 100);
            var prepared = await _client.DownloadAsync(_manifest, progress, _cts.Token);
            SetStatus(_s.Installing);
            _client.ApplyAndRestart(prepared); // shuts the app down
        }
        catch (OperationCanceledException)
        {
            _working = false;
        }
        catch (Exception ex)
        {
            _working = false;
            Progress.Visibility = Visibility.Collapsed;
            SetStatus(string.Format(CultureInfo.CurrentCulture, _s.ErrorFormat, ex.Message), error: true);
            UpdateButton.IsEnabled = LaterButton.IsEnabled = SkipButton.IsEnabled = true;
        }
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        if (!_working) Close();
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        if (_store is not null)
        {
            _store.SkippedVersion = _manifest.Version;
            _store.Save();
        }
        Close();
    }
}

using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.UI.Mvvm;

namespace DiskCleaner.UI.ViewModels;

public sealed class LargeFileItem
{
    public LargeFileItem(LargeFileEntry e, long largestBytes)
    {
        FullPath = e.FullPath;
        SizeBytes = e.SizeBytes;
        LastWriteLocal = e.LastWriteUtc.ToLocalTime();
        Ratio = largestBytes > 0 ? e.SizeBytes * 100.0 / largestBytes : 0;
    }

    public string FullPath { get; }
    public long SizeBytes { get; }
    public DateTime LastWriteLocal { get; }

    /// <summary>% so với file lớn nhất trong danh sách (cho thanh nhỏ trên mỗi dòng).</summary>
    public double Ratio { get; }
}

public sealed class LargeFilesViewModel : ObservableObject
{
    private enum LargeState { Idle, Scanning, Found, Cancelled, Error, Moved }

    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly LocalizationService _loc;
    private CancellationTokenSource? _cts;
    private string _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _minSizeMb = "500";
    private string _maxResults = "200";
    private bool _isBusy;
    private LargeState _state = LargeState.Idle;
    private int _stateCount;
    private long _stateBytes;

    public ObservableCollection<LargeFileItem> Items { get; } = [];

    public string RootPath { get => _rootPath; set => SetProperty(ref _rootPath, value); }
    public string MinSizeMb { get => _minSizeMb; set => SetProperty(ref _minSizeMb, value); }
    public string MaxResults { get => _maxResults; set => SetProperty(ref _maxResults, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }
    public bool IsIdle => !IsBusy;
    public bool IsEmpty => Items.Count == 0 && !IsBusy;

    public string Status => _state switch
    {
        LargeState.Scanning => _loc.Get("Large.Status.Scanning"),
        LargeState.Found => _loc.Format("Large.Status.Found", _stateCount, SizeFormatter.Format(_stateBytes)),
        LargeState.Cancelled => _loc.Get("Large.Status.Cancelled"),
        LargeState.Error => _loc.Get("Large.Status.Error"),
        LargeState.Moved => _loc.Format("Large.Status.Moved", _stateCount, SizeFormatter.Format(_stateBytes)),
        _ => _loc.Get("Large.Status.Idle")
    };

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand MoveToRecycleBinCommand { get; }

    public LargeFilesViewModel(AppServices services, IDialogService dialogs)
    {
        _s = services;
        _dialogs = dialogs;
        _loc = services.Loc;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy, OnError);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        BrowseCommand = new RelayCommand(() =>
        {
            var p = _dialogs.PickFolder(RootPath);
            if (p != null) RootPath = p;
        }, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        MoveToRecycleBinCommand = new RelayCommand(MoveToRecycleBin);
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        _loc.LanguageChanged += (_, _) => RaiseAll();
    }

    private void SetState(LargeState state, int count = 0, long bytes = 0)
    {
        _state = state;
        _stateCount = count;
        _stateBytes = bytes;
        OnPropertyChanged(nameof(Status));
    }

    private async Task ScanAsync()
    {
        var title = _loc.Get("App.Name");
        var path = RootPath.Trim();
        if (!Directory.Exists(path))
        {
            _dialogs.Error(title, _loc.Format("Large.FolderMissing", path));
            return;
        }
        if (!double.TryParse(MinSizeMb, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) &&
            !double.TryParse(MinSizeMb, NumberStyles.Float, CultureInfo.CurrentCulture, out mb) || mb < 0)
        {
            _dialogs.Error(title, _loc.Get("Large.InvalidMin"));
            return;
        }
        if (!int.TryParse(MaxResults, out var max) || max <= 0)
        {
            _dialogs.Error(title, _loc.Get("Large.InvalidMax"));
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Items.Clear();
        SetState(LargeState.Scanning);
        try
        {
            var found = await _s.LargeFiles.FindAsync(path, (long)(mb * 1024 * 1024), max, _cts.Token);
            var largest = found.Count > 0 ? found.Max(f => f.SizeBytes) : 0;
            foreach (var f in found) Items.Add(new LargeFileItem(f, largest));
            SetState(LargeState.Found, found.Count, found.Sum(f => f.SizeBytes));
        }
        catch (OperationCanceledException)
        {
            SetState(LargeState.Cancelled);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static List<LargeFileItem> GetSelected(object? parameter) =>
        (parameter as IList)?.OfType<LargeFileItem>().ToList() ?? [];

    private void OpenFolder(object? parameter)
    {
        var first = GetSelected(parameter).FirstOrDefault();
        if (first == null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("Mở thư mục lỗi", ex);
        }
    }

    /// <summary>Chỉ chuyển vào Thùng rác (có thể khôi phục), không có xóa vĩnh viễn.</summary>
    private void MoveToRecycleBin(object? parameter)
    {
        var selected = GetSelected(parameter);
        if (selected.Count == 0) return;

        var title = _loc.Get("App.Name");
        var allowed = new List<LargeFileItem>();
        foreach (var f in selected)
        {
            if (_s.Guard.IsForbidden(f.FullPath) || !File.Exists(f.FullPath))
            {
                AppLog.Instance.Error($"Từ chối chuyển vào Thùng rác (vùng cấm hoặc không tồn tại): {f.FullPath}");
                continue;
            }
            allowed.Add(f);
        }
        if (allowed.Count == 0)
        {
            _dialogs.Info(title, _loc.Get("Large.Protected"));
            return;
        }

        var total = allowed.Sum(f => f.SizeBytes);
        if (!_dialogs.Confirm(_loc.Get("Large.Confirm.Title"),
                _loc.Format("Large.Confirm.Message", allowed.Count, SizeFormatter.Format(total))))
            return;

        try
        {
            _s.RecycleBin.SendToRecycleBin(allowed.Select(f => f.FullPath));
            foreach (var f in allowed)
            {
                AppLog.Instance.Info($"RECYCLE {f.FullPath} ({f.SizeBytes} B)");
                Items.Remove(f);
            }
            AppLog.Instance.Flush();
            SetState(LargeState.Moved, allowed.Count, total);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("Chuyển vào Thùng rác lỗi", ex);
            _dialogs.Error(title, _loc.Format("Large.MoveFailed", ex.Message));
        }
    }

    private void OnError(Exception ex)
    {
        AppLog.Instance.Error("Large files lỗi", ex);
        SetState(LargeState.Error);
        _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Large.ScanFailed", ex.Message));
    }
}

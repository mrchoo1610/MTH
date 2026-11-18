using System.Collections.ObjectModel;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.UI.Mvvm;

namespace DiskCleaner.UI.ViewModels;

/// <summary>Một ổ đĩa trên Overview. Text phụ thuộc ngôn ngữ nên tính lại khi LanguageChanged.</summary>
public sealed class DriveItemViewModel : ObservableObject
{
    private readonly LocalizationService _loc;

    public DriveSnapshot Drive { get; }

    public DriveItemViewModel(DriveSnapshot drive, LocalizationService loc)
    {
        Drive = drive;
        _loc = loc;
        loc.LanguageChanged += (_, _) => RaiseAll();
    }

    public string Name => Drive.Name.TrimEnd('\\');
    public string Label => string.IsNullOrWhiteSpace(Drive.Label) ? "" : Drive.Label;
    public double UsedPercent => Drive.UsedPercent;
    public string UsedPercentText => $"{Drive.UsedPercent:0.#}%";
    public string UsedText => SizeFormatter.Format(Drive.UsedBytes);
    public string FreeText => SizeFormatter.Format(Drive.FreeBytes);
    public string TotalText => SizeFormatter.Format(Drive.TotalBytes);
    public DriveHealth Health => Drive.Health;

    public string HealthText => _loc.Get(Drive.Health switch
    {
        DriveHealth.Critical => "Overview.Health.Critical",
        DriveHealth.Warning => "Overview.Health.Warning",
        _ => "Overview.Health.Ok"
    });
}

public sealed class OverviewViewModel : ObservableObject
{
    private enum ScanState { Idle, Scanning, Done, Cancelled, Error }

    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly LocalizationService _loc;
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private ScanState _state = ScanState.Idle;
    private CleanRule? _scanningRule;

    public ObservableCollection<DriveItemViewModel> OtherDrives { get; } = [];
    public DriveItemViewModel? PrimaryDrive { get; private set; }
    public bool HasDrives => PrimaryDrive != null || OtherDrives.Count > 0;
    public bool HasOtherDrives => OtherDrives.Count > 0;
    public Action? GoToClean { get; set; }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(ShowDetails)); } }
    }
    public bool IsIdle => !IsBusy;

    public string Status => _state switch
    {
        ScanState.Scanning => _scanningRule == null
            ? _loc.Get("Overview.Status.Scanning")
            : _loc.Format("Overview.Status.ScanningRule", _scanningRule.GetName(_loc.Current)),
        ScanState.Done => _loc.Get("Overview.Status.Done"),
        ScanState.Cancelled => _loc.Get("Overview.Status.Cancelled"),
        ScanState.Error => _loc.Get("Overview.Status.Error"),
        _ => ""
    };

    // ---- Tiềm năng giải phóng: đọc từ ScanResultStore dùng chung với tab Dọn dẹp ----
    private IEnumerable<ScanItemResult> DefaultResults()
    {
        foreach (var r in _s.Rules.Where(r => r.DefaultSelected))
            if (_s.ScanResults.TryGet(r.Id, out var res)) yield return res;
    }

    public bool HasScan => DefaultResults().Any();
    public long ReclaimableBytes => DefaultResults().Where(r => r.Available && !r.AccessDenied).Sum(r => r.Bytes);
    public string ReclaimValueText => HasScan ? SizeFormatter.Format(ReclaimableBytes) : _loc.Get("Overview.Reclaim.NotScanned");
    public bool ShowAdminNote => DefaultResults().Any(r => r.AccessDenied);
    public bool ShowDetails => HasScan && !IsBusy;

    public AsyncRelayCommand QuickScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand GoToCleanCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public OverviewViewModel(AppServices services, IDialogService dialogs)
    {
        _s = services;
        _dialogs = dialogs;
        _loc = services.Loc;
        QuickScanCommand = new AsyncRelayCommand(QuickScanAsync, () => !IsBusy, OnError);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        GoToCleanCommand = new RelayCommand(() => GoToClean?.Invoke());
        RefreshCommand = new RelayCommand(RefreshDrives);
        _loc.LanguageChanged += (_, _) => RaiseAll();
        _s.ScanResults.Changed += (_, _) => RaiseReclaim();
        RefreshDrives();
    }

    private void RaiseReclaim()
    {
        if (!IsBusy && _state == ScanState.Done && !HasScan) SetState(ScanState.Idle);
        OnPropertyChanged(nameof(HasScan));
        OnPropertyChanged(nameof(ReclaimableBytes));
        OnPropertyChanged(nameof(ReclaimValueText));
        OnPropertyChanged(nameof(ShowAdminNote));
        OnPropertyChanged(nameof(ShowDetails));
    }

    public void RefreshDrives()
    {
        try
        {
            var list = _s.Drives.GetFixedDrives();
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var primary = list.FirstOrDefault(d => d.Name.Equals(systemRoot, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();

            PrimaryDrive = primary == null ? null : new DriveItemViewModel(primary, _loc);
            OtherDrives.Clear();
            foreach (var d in list.Where(d => d != primary)) OtherDrives.Add(new DriveItemViewModel(d, _loc));
            OnPropertyChanged(nameof(PrimaryDrive));
            OnPropertyChanged(nameof(HasDrives));
            OnPropertyChanged(nameof(HasOtherDrives));
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("Không đọc được thông tin ổ đĩa", ex);
        }
    }

    private void SetState(ScanState state, CleanRule? rule = null)
    {
        _state = state;
        _scanningRule = rule;
        OnPropertyChanged(nameof(Status));
    }

    private async Task QuickScanAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        SetState(ScanState.Scanning);
        try
        {
            var rules = _s.Rules.Where(r => r.DefaultSelected).ToList();
            var progress = new Progress<string>(name => SetState(ScanState.Scanning, rules.FirstOrDefault(r => r.Name == name)));
            var results = await _s.Scanner.ScanAllAsync(rules, progress, _cts.Token);
            _s.ScanResults.Update(results);
            SetState(ScanState.Done);
        }
        catch (OperationCanceledException)
        {
            SetState(ScanState.Cancelled);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnError(Exception ex)
    {
        AppLog.Instance.Error("Quét nhanh lỗi", ex);
        SetState(ScanState.Error);
        _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Overview.ScanFailed", ex.Message));
    }
}

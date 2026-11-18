using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.UI.Mvvm;

namespace DiskCleaner.UI.ViewModels;

/// <summary>Cách hiển thị ô dung lượng của một rule (dùng cho DataTrigger màu chữ).</summary>
public enum SizeKind { NotScanned, Value, Unavailable, AdminRequired }

public sealed class RuleItemViewModel : ObservableObject
{
    private readonly LocalizationService _loc;
    private bool _isSelected;
    private ScanItemResult? _result;

    public CleanRule Rule { get; }
    public event EventHandler? SelectionChanged;

    public RuleItemViewModel(CleanRule rule, LocalizationService loc)
    {
        Rule = rule;
        _loc = loc;
        _isSelected = rule.DefaultSelected;
        loc.LanguageChanged += (_, _) => RaiseAll();
    }

    public string Name => Rule.GetName(_loc.Current);
    public string Description => Rule.GetDescription(_loc.Current);
    public string? Warning => Rule.GetWarning(_loc.Current);
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
    public bool RequiresAdmin => Rule.RequiresAdmin;
    public bool IsCustom => Rule.IsCustom;

    /// <summary>Glyph Segoe MDL2 theo nhóm (không dùng emoji).</summary>
    public string IconGlyph => Rule.Category switch
    {
        RuleCategory.System => "",
        RuleCategory.Browser => "",
        RuleCategory.Custom => "\uE838",
        _ => ""
    };

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) SelectionChanged?.Invoke(this, EventArgs.Empty); }
    }

    public bool Scanned => _result != null;
    public bool Available => _result?.Available ?? true;
    public bool AccessDenied => _result is { Available: true, AccessDenied: true };
    public bool CanSelect => Available;
    public double RowOpacity => Available ? 1.0 : 0.5;
    public long Bytes => _result is { Available: true, AccessDenied: false } r ? r.Bytes : 0;
    public int FileCount => _result?.FileCount ?? 0;

    public SizeKind SizeKind => _result == null ? SizeKind.NotScanned
        : !_result.Available ? SizeKind.Unavailable
        : _result.AccessDenied ? SizeKind.AdminRequired
        : SizeKind.Value;

    public string SizeText => SizeKind switch
    {
        SizeKind.NotScanned => _loc.Get("Clean.Size.NotScanned"),
        SizeKind.Unavailable => _loc.Get("Clean.Size.Unavailable"),
        SizeKind.AdminRequired => _loc.Get("Clean.Size.AdminRequired"),
        _ => SizeFormatter.Format(Bytes)
    };

    /// <summary>Lý do (tooltip) khi rule không khả dụng / cần admin; null thì không hiện tooltip.</summary>
    public string? SizeTooltip => SizeKind switch
    {
        SizeKind.Unavailable => Rule.Action == RuleAction.RunCommand && !string.IsNullOrWhiteSpace(Rule.Command)
            ? _loc.Format("Clean.Unavailable.Command", Rule.Command!)
            : _loc.Get("Clean.Unavailable.Folder"),
        SizeKind.AdminRequired => _loc.Get("Clean.AdminRequired.Tooltip"),
        _ => null
    };

    public void ApplyResult(ScanItemResult? result)
    {
        _result = result;
        if (result is { Available: false }) IsSelected = false;
        RaiseAll();
    }
}

public sealed class RuleGroupViewModel : ObservableObject
{
    private readonly LocalizationService _loc;

    public RuleCategory Category { get; }
    public ObservableCollection<RuleItemViewModel> Items { get; } = [];

    public RuleGroupViewModel(RuleCategory category, LocalizationService loc)
    {
        Category = category;
        _loc = loc;
        loc.LanguageChanged += (_, _) => RaiseAll();
    }

    public string Title => _loc.Get("Clean.Category." + Category);
    public string IconGlyph => Category switch
    {
        RuleCategory.System => "",
        RuleCategory.Browser => "",
        RuleCategory.Custom => "\uE838",
        _ => ""
    };

    public bool HasTotal => Items.Any(i => i.Scanned && i.Available && !i.AccessDenied);
    public string TotalText => HasTotal ? SizeFormatter.Format(Items.Where(i => i.Available).Sum(i => i.Bytes)) : "";

    public void RefreshTotal()
    {
        OnPropertyChanged(nameof(HasTotal));
        OnPropertyChanged(nameof(TotalText));
    }
}

public sealed class CleanResultRow : ObservableObject
{
    private readonly LocalizationService _loc;
    private readonly CleanReport _report;
    private readonly CleanRule? _rule;

    public CleanResultRow(CleanRule? rule, CleanReport report, LocalizationService loc)
    {
        _rule = rule;
        _report = report;
        _loc = loc;
        loc.LanguageChanged += (_, _) => RaiseAll();
    }

    public string RuleName => _rule?.GetName(_loc.Current) ?? _report.RuleId;
    public string Freed => SizeFormatter.Format(_report.BytesFreed);
    public int FilesDeleted => _report.FilesDeleted;
    public int FilesSkipped => _report.FilesSkipped;
    public string Note => _report.Errors.Count > 0 ? _loc.Format("Clean.Result.ErrorsFormat", _report.Errors.Count) : "";
    public string? NoteTooltip => _report.Errors.Count > 0 ? string.Join(Environment.NewLine, _report.Errors) : null;
}

public sealed class CleanViewModel : ObservableObject
{
    private enum CleanState { Idle, Scanning, ScanCancelled, Cleaning, WaitingUac, Done, CleanCancelled, Error }

    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly LocalizationService _loc;
    private readonly List<RuleItemViewModel> _items = [];
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private CleanState _state = CleanState.Idle;
    private CleanRule? _currentRule;
    private int _filesProcessed;
    private long _bytesProgress;
    private string? _lastLogPath;
    private long _freedTotal;

    public ObservableCollection<RuleGroupViewModel> Groups { get; } = [];
    public ObservableCollection<CleanResultRow> Results { get; } = [];

    public event EventHandler? CleanCompleted;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(ShowEmptyHint));
            }
        }
    }
    public bool IsIdle => !IsBusy;
    public bool HasResults => Results.Count > 0;
    public bool HasLog => _lastLogPath != null;
    public bool ShowEmptyHint => !IsBusy && !_s.ScanResults.HasAny && !HasResults;

    // ---- Tổng hợp theo mục đã chọn ----
    public int SelectedCount => _items.Count(i => i.IsSelected && i.Available);
    public long SelectedBytes => _items.Where(i => i.IsSelected && i.Available).Sum(i => i.Bytes);
    public string SelectedChipText => _loc.Format("Clean.SelectedChip", SizeFormatter.Format(SelectedBytes));
    public string SelectionSummaryText => _loc.Format("Clean.SelectionSummary", SelectedCount, SizeFormatter.Format(SelectedBytes));
    public string CleanButtonText => _loc.Format("Clean.CleanButtonFormat", SizeFormatter.Format(SelectedBytes));

    private bool NeedsScan => _items.Any(i => i.IsSelected && i.Available && !i.Scanned);
    private bool CanClean => !IsBusy && SelectedCount > 0 && !NeedsScan;

    public string Status => _state switch
    {
        CleanState.Scanning => _loc.Get("Clean.Status.Scanning"),
        CleanState.ScanCancelled => _loc.Get("Clean.Status.ScanCancelled"),
        CleanState.Cleaning => _loc.Format("Clean.Status.Cleaning", _currentRule?.GetName(_loc.Current) ?? ""),
        CleanState.WaitingUac => _loc.Get("Clean.Status.WaitingUac"),
        CleanState.Done => _loc.Format("Clean.Status.Done", SizeFormatter.Format(_freedTotal)),
        CleanState.CleanCancelled => _loc.Format("Clean.Status.CleanCancelled", SizeFormatter.Format(_freedTotal)),
        CleanState.Error => _loc.Get("Clean.Status.Error"),
        _ => !_s.ScanResults.HasAny ? _loc.Get("Clean.Status.Idle")
            : NeedsScan ? _loc.Get("Clean.Status.NeedScan")
            : _loc.Format("Clean.Status.ScanDone", SizeFormatter.Format(SelectedBytes))
    };

    public string CurrentText => _state switch
    {
        CleanState.Scanning when _currentRule != null => _loc.Format("Clean.Progress.Scanning", _currentRule.GetName(_loc.Current)),
        CleanState.Cleaning when _currentRule != null => _loc.Format("Clean.Progress.Rule", _currentRule.GetName(_loc.Current),
            _filesProcessed, SizeFormatter.Format(_bytesProgress)),
        _ => ""
    };

    public string ResultSummaryText => _loc.Format("Clean.Result.Freed", SizeFormatter.Format(_freedTotal));

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenLogCommand { get; }
    public RelayCommand RemoveCustomRuleCommand { get; }

    /// <summary>View đọc cờ này khi mở để cuộn xuống nhóm "Tùy chỉnh" sau khi vừa thêm rule.</summary>
    public bool ScrollToCustomRequested { get; set; }

    public CleanViewModel(AppServices services, IDialogService dialogs)
    {
        _s = services;
        _dialogs = dialogs;
        _loc = services.Loc;

        var allRules = _s.AllRules;
        foreach (var cat in Enum.GetValues<RuleCategory>())
        {
            var rules = allRules.Where(r => r.Category == cat).ToList();
            if (rules.Count == 0) continue;
            var group = new RuleGroupViewModel(cat, _loc);
            foreach (var r in rules)
            {
                var vm = new RuleItemViewModel(r, _loc);
                vm.SelectionChanged += (_, _) => RaiseTotals();
                group.Items.Add(vm);
                _items.Add(vm);
            }
            Groups.Add(group);
        }

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy, OnError);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => CanClean, OnError);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        OpenLogCommand = new RelayCommand(OpenLog, () => HasLog);
        RemoveCustomRuleCommand = new RelayCommand(p => RemoveCustomRule(p as RuleItemViewModel), p => !IsBusy && p is RuleItemViewModel { IsCustom: true });

        _loc.LanguageChanged += (_, _) => RaiseAll();
        _s.ScanResults.Changed += (_, _) => ApplyStore();
        _s.CustomRules.Changed += (_, _) => SyncCustomRules();
        ApplyStore();
    }

    // ---------- Rule tùy chỉnh ----------

    /// <summary>Đồng bộ nhóm "Tùy chỉnh" với CustomRuleStore (thêm/bớt thẻ, tạo/xóa nhóm).</summary>
    private void SyncCustomRules()
    {
        var current = _s.CustomRules.Current;
        var ids = current.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var group = Groups.FirstOrDefault(g => g.Category == RuleCategory.Custom);

        foreach (var item in _items.Where(i => i.Rule.IsCustom && !ids.Contains(i.Rule.Id)).ToList())
        {
            _items.Remove(item);
            group?.Items.Remove(item);
        }

        foreach (var rule in current)
        {
            if (_items.Any(i => string.Equals(i.Rule.Id, rule.Id, StringComparison.OrdinalIgnoreCase))) continue;
            if (group == null)
            {
                group = new RuleGroupViewModel(RuleCategory.Custom, _loc);
                Groups.Add(group); // Custom là category cuối cùng
            }
            var vm = new RuleItemViewModel(rule, _loc);
            vm.SelectionChanged += (_, _) => RaiseTotals();
            group.Items.Add(vm);
            _items.Add(vm);
        }

        if (group != null && group.Items.Count == 0) Groups.Remove(group);
        ApplyStore();
    }

    /// <summary>
    /// Sau khi thêm rule tùy chỉnh: bật toggle và quét riêng rule đó (dry-run) để thẻ có dung lượng ngay.
    /// Không xóa gì.
    /// </summary>
    public async Task ActivateCustomRuleAsync(CleanRule rule)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.Rule.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;
        ScrollToCustomRequested = true;
        item.IsSelected = true;
        if (IsBusy) return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        SetState(CleanState.Scanning, rule);
        try
        {
            var result = await _s.Scanner.ScanAsync(rule, _cts.Token);
            SetState(CleanState.Idle);
            _s.ScanResults.Update([result]);
            item.IsSelected = result.Available;
        }
        catch (OperationCanceledException)
        {
            SetState(CleanState.ScanCancelled);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            RaiseTotals();
        }
    }

    private void RemoveCustomRule(RuleItemViewModel? item)
    {
        if (item is not { IsCustom: true } || IsBusy) return;
        if (!_dialogs.Confirm(_loc.Get("Clean.Custom.RemoveTitle"), _loc.Format("Clean.Custom.RemoveConfirm", item.Name))) return;
        try
        {
            _s.CustomRules.Remove(item.Rule.Id); // chỉ gỡ rule khỏi danh sách, không đụng file
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    /// <summary>Đồng bộ kết quả quét dùng chung (Quét nhanh ở Overview hoặc Quét ở tab này) vào từng rule.</summary>
    private void ApplyStore()
    {
        foreach (var item in _items)
            item.ApplyResult(_s.ScanResults.TryGet(item.Rule.Id, out var r) ? r : null);
        foreach (var g in Groups) g.RefreshTotal();
        if (_state is CleanState.ScanCancelled || (_state is CleanState.Done or CleanState.CleanCancelled && _s.ScanResults.HasAny))
            _state = CleanState.Idle;
        RaiseTotals();
        OnPropertyChanged(nameof(ShowEmptyHint));
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedChipText));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(CleanButtonText));
        OnPropertyChanged(nameof(Status));
        foreach (var g in Groups) g.RefreshTotal();
    }

    private void SetState(CleanState state, CleanRule? rule = null)
    {
        _state = state;
        _currentRule = rule;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CurrentText));
    }

    private async Task ScanAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        SetState(CleanState.Scanning);
        try
        {
            var rules = _items.Select(i => i.Rule).ToList();
            var progress = new Progress<string>(name => SetState(CleanState.Scanning, rules.FirstOrDefault(r => r.Name == name)));
            var results = await _s.Scanner.ScanAllAsync(rules, progress, _cts.Token);
            SetState(CleanState.Idle);
            _s.ScanResults.Update(results); // ApplyStore cập nhật từng rule
        }
        catch (OperationCanceledException)
        {
            SetState(CleanState.ScanCancelled);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            RaiseTotals();
        }
    }

    private async Task CleanAsync()
    {
        var selected = _items.Where(i => i.IsSelected && i.Available).Select(i => i.Rule).ToList();
        if (selected.Count == 0) return;

        var lang = _loc.Current;
        var needAdmin = selected.Where(r => r.RequiresAdmin).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(_loc.Get("Clean.Confirm.Header"));
        foreach (var r in selected)
        {
            var item = _items.First(i => i.Rule.Id == r.Id);
            sb.AppendLine($"  • {r.GetName(lang)}: {item.SizeText}");
        }
        var custom = selected.Where(r => r.IsCustom).ToList();
        if (custom.Count > 0)
        {
            sb.AppendLine().AppendLine(_loc.Get("Clean.Confirm.CustomHeader"));
            foreach (var r in custom)
            {
                sb.AppendLine($"  • {r.GetName(lang)}");
                sb.AppendLine($"     {r.Paths[0]}");
            }
            sb.AppendLine(_loc.Get("Clean.Confirm.CustomPermanent"));
        }
        sb.AppendLine().AppendLine(_loc.Format("Clean.Confirm.Total", SizeFormatter.Format(SelectedBytes)));
        if (needAdmin.Count > 0 && !_s.Elevation.IsElevated)
            sb.AppendLine().AppendLine(_loc.Format("Clean.Confirm.Uac", string.Join(", ", needAdmin.Select(r => r.GetName(lang)))));
        sb.AppendLine().Append(_loc.Get("Clean.Confirm.Question"));
        if (!_dialogs.Confirm(_loc.Get("Clean.Confirm.Title"), sb.ToString())) return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Results.Clear();
        _freedTotal = 0;
        OnPropertyChanged(nameof(HasResults));
        bool cancelled = false;
        using var logger = FileLogger.CreateTimestamped("clean");
        _lastLogPath = logger.FilePath;
        OnPropertyChanged(nameof(HasLog));
        logger.Info($"Bắt đầu dọn dẹp. Rule: {string.Join(", ", selected.Select(r => r.Id))}");

        try
        {
            var executor = new CleanExecutor(_s.Guard, _s.Runner, _s.RecycleBin, logger);
            var progress = new Progress<CleanProgress>(p =>
            {
                _filesProcessed = p.FilesProcessed;
                _bytesProgress = p.BytesFreed;
                OnPropertyChanged(nameof(CurrentText));
            });

            var inProcess = selected.Where(r => !r.RequiresAdmin || _s.Elevation.IsElevated).ToList();
            var elevated = selected.Where(r => r.RequiresAdmin && !_s.Elevation.IsElevated).ToList();

            foreach (var rule in inProcess)
            {
                _filesProcessed = 0;
                _bytesProgress = 0;
                SetState(CleanState.Cleaning, rule);
                var report = await executor.CleanAsync(rule, progress, _cts.Token);
                AddResult(rule, report);
            }

            if (elevated.Count > 0)
            {
                SetState(CleanState.WaitingUac);
                var reports = await _s.Elevation.RunElevatedCleanAsync(elevated.Select(r => r.Id), _cts.Token);
                if (reports == null)
                {
                    _dialogs.Info(_loc.Get("App.Name"), _loc.Get("Clean.UacDeclined"));
                    logger.Info("Người dùng từ chối UAC.");
                }
                else
                {
                    foreach (var rep in reports)
                        AddResult(_s.Rules.FirstOrDefault(r => r.Id == rep.RuleId), rep);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            logger.Info("Người dùng hủy.");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }

        SetState(cancelled ? CleanState.CleanCancelled : CleanState.Done);
        OnPropertyChanged(nameof(ResultSummaryText));
        // Số liệu cũ không còn đúng: xóa store (Overview + Clean cùng thấy "chưa quét") và yêu cầu quét lại.
        _s.ScanResults.Clear();
        RaiseTotals();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowEmptyHint));
        CleanCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void AddResult(CleanRule? rule, CleanReport report)
    {
        _freedTotal += report.BytesFreed;
        Results.Add(new CleanResultRow(rule, report, _loc));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultSummaryText));
    }

    private void OpenLog()
    {
        if (_lastLogPath == null) return;
        try
        {
            if (File.Exists(_lastLogPath))
                Process.Start(new ProcessStartInfo(_lastLogPath) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Clean.LogOpenError", ex.Message));
        }
    }

    private void OnError(Exception ex)
    {
        AppLog.Instance.Error("Thao tác tab Dọn dẹp lỗi", ex);
        SetState(CleanState.Error);
        _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Clean.Error", ex.Message));
    }
}

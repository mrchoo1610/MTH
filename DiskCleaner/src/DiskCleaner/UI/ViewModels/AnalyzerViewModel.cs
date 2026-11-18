using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.UI.Mvvm;

namespace DiskCleaner.UI.ViewModels;

/// <summary>Node của TreeView, con được tạo lười (lazy) khi mở rộng.</summary>
public sealed class TreeNodeViewModel : ObservableObject
{
    private static readonly FolderNode PlaceholderNode = new() { Name = "…", FullPath = "" };

    private readonly FolderNode _node;
    private readonly long _parentSize;
    private readonly bool _isPlaceholder;
    private readonly AnalyzerViewModel? _owner;
    private bool _loaded;
    private bool _isExpanded;
    private bool _isSelected;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel(FolderNode node, long parentSize, AnalyzerViewModel? owner = null)
    {
        _node = node;
        _parentSize = parentSize;
        _owner = owner;
        if (node.Children.Count > 0) Children.Add(new TreeNodeViewModel());
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer);
        CopyPathCommand = new RelayCommand(CopyPath);
        AddToCleanCommand = new RelayCommand(() => _owner?.AddToClean(this), () => CanAddToClean);
    }

    private TreeNodeViewModel()
    {
        _node = PlaceholderNode;
        _isPlaceholder = true;
        OpenInExplorerCommand = new RelayCommand(() => { });
        CopyPathCommand = new RelayCommand(() => { });
        AddToCleanCommand = new RelayCommand(() => { }, () => false);
    }

    public string Name => _node.Name;
    public string FullPath => _node.FullPath;
    public long SizeBytes => _node.SizeBytes;
    public int FileCount => _node.FileCount;
    public bool IsAccessDenied => _node.IsAccessDenied;
    public double Percent => _isPlaceholder ? 0 : _parentSize > 0 ? _node.SizeBytes * 100.0 / _parentSize : 100;
    public string PercentText => _isPlaceholder ? "" : $"{Percent:0.#}%";
    public string SizeText => _isPlaceholder ? "" : SizeFormatter.Format(_node.SizeBytes);
    public string FileCountText => _isPlaceholder ? "" : _node.FileCount.ToString("N0");

    public RelayCommand OpenInExplorerCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand AddToCleanCommand { get; }

    public bool IsPlaceholder => _isPlaceholder;

    /// <summary>Đồng bộ với TreeViewItem.IsSelected; node được chọn là nguồn cho nút "Thêm vào Dọn dẹp".</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            if (!_isPlaceholder) _owner?.OnNodeSelectionChanged(this, value);
        }
    }

    private AddToCleanState AddState => _owner != null && !_isPlaceholder ? _owner.GetAddState(this) : AddToCleanState.None;
    public bool CanAddToClean => AddState.CanAdd;
    public string AddMenuText => AddState.ButtonText;

    /// <summary>Tính lại trạng thái "Thêm vào Dọn dẹp" (gọi khi mở menu chuột phải).</summary>
    public void RefreshAddState()
    {
        OnPropertyChanged(nameof(CanAddToClean));
        OnPropertyChanged(nameof(AddMenuText));
        AddToCleanCommand.RaiseCanExecuteChanged();
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            if (value && !_loaded && !_isPlaceholder) LoadChildren();
        }
    }

    private void LoadChildren()
    {
        _loaded = true;
        Children.Clear();
        foreach (var c in _node.Children.OrderByDescending(c => c.SizeBytes))
            Children.Add(new TreeNodeViewModel(c, _node.SizeBytes, _owner));
    }

    private void OpenInExplorer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_node.FullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("Mở Explorer lỗi", ex);
        }
    }

    private void CopyPath()
    {
        try { Clipboard.SetText(_node.FullPath); }
        catch (Exception ex) { AppLog.Instance.Error("Copy path lỗi", ex); }
    }
}

/// <summary>Trạng thái nút "Thêm vào Dọn dẹp" cho một thư mục đang chọn.</summary>
public sealed record AddToCleanState(bool CanAdd, string ButtonText, string? Tooltip)
{
    public static readonly AddToCleanState None = new(false, "", null);
}

public sealed class AnalyzerViewModel : ObservableObject
{
    private enum AnalyzerState { Idle, Scanning, Progress, Done, Cancelled, Error }

    private readonly AppServices _s;
    private readonly IDialogService _dialogs;
    private readonly LocalizationService _loc;
    private CancellationTokenSource? _cts;
    private string _rootPath = @"C:\";
    private bool _isBusy;
    private AnalyzerState _state = AnalyzerState.Idle;
    private AnalyzeProgress? _progress;
    private FolderNode? _doneNode;
    private TreeNodeViewModel? _selectedNode;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = [];

    /// <summary>Raise sau khi rule tùy chỉnh được thêm; MainViewModel chuyển sang tab Dọn dẹp.</summary>
    public event Action<CleanRule>? RuleAdded;

    public string RootPath { get => _rootPath; set => SetProperty(ref _rootPath, value); }
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
    public bool HasData => Roots.Count > 0;
    public bool IsEmpty => !HasData && !IsBusy;

    public string Status => _state switch
    {
        AnalyzerState.Scanning => _loc.Get("Analyzer.Status.Scanning"),
        AnalyzerState.Progress when _progress != null => _loc.Format("Analyzer.Status.ScanningProgress",
            _progress.FilesScanned.ToString("N0"), SizeFormatter.Format(_progress.BytesScanned), _progress.CurrentPath),
        AnalyzerState.Progress => _loc.Get("Analyzer.Status.Scanning"),
        AnalyzerState.Done when _doneNode != null => _loc.Format("Analyzer.Status.Done",
            SizeFormatter.Format(_doneNode.SizeBytes), _doneNode.FileCount.ToString("N0")),
        AnalyzerState.Cancelled => _loc.Get("Analyzer.Status.Cancelled"),
        AnalyzerState.Error => _loc.Get("Analyzer.Status.Error"),
        _ => _loc.Get("Analyzer.Status.Idle")
    };

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand AddToCleanCommand { get; }

    // ---- "Thêm vào Dọn dẹp" cho node đang chọn ----
    private AddToCleanState SelectedAddState => _selectedNode == null ? AddToCleanState.None : GetAddState(_selectedNode);
    public bool CanAddSelected => SelectedAddState.CanAdd;
    public string AddButtonText => _selectedNode == null ? _loc.Get("Analyzer.AddToClean") : SelectedAddState.ButtonText;
    public string AddButtonTooltip => _selectedNode == null ? _loc.Get("Analyzer.Add.SelectHint") : SelectedAddState.Tooltip ?? "";
    public bool HasSelection => _selectedNode != null;
    public string SelectedPathText => _selectedNode?.FullPath ?? _loc.Get("Analyzer.Add.SelectHint");

    public AnalyzerViewModel(AppServices services, IDialogService dialogs)
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
        AddToCleanCommand = new RelayCommand(() => AddToClean(_selectedNode), () => CanAddSelected);
        Roots.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(HasData)); OnPropertyChanged(nameof(IsEmpty)); };
        _loc.LanguageChanged += (_, _) => RaiseAll();
        _s.CustomRules.Changed += (_, _) => RefreshAddState();
    }

    internal void OnNodeSelectionChanged(TreeNodeViewModel node, bool selected)
    {
        if (selected) _selectedNode = node;
        else if (ReferenceEquals(_selectedNode, node)) _selectedNode = null;
        RefreshAddState();
    }

    private void RefreshAddState()
    {
        OnPropertyChanged(nameof(CanAddSelected));
        OnPropertyChanged(nameof(AddButtonText));
        OnPropertyChanged(nameof(AddButtonTooltip));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedPathText));
        AddToCleanCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Kiểm tra chính sách cho thư mục và đổi thành chữ + tooltip phù hợp ngôn ngữ hiện tại.</summary>
    internal AddToCleanState GetAddState(TreeNodeViewModel node)
    {
        var check = _s.CustomRules.Check(node.FullPath);
        if (!check.IsBlocked) return new AddToCleanState(true, _loc.Get("Analyzer.AddToClean"), null);
        if (check.ReasonKeys.Contains(CustomRulePolicy.BlockAlreadyExists))
            return new AddToCleanState(false, _loc.Get("Analyzer.AlreadyInClean"), _loc.Get(CustomRulePolicy.BlockAlreadyExists));
        return new AddToCleanState(false, _loc.Get("Analyzer.AddToClean"),
            _loc.Format("Analyzer.Add.BlockedTooltip", LocalizeReasons(check)));
    }

    private string LocalizeReasons(CustomRuleCheck check) =>
        string.Join("\n", check.ReasonKeys.Select(k => _loc.Get(k)));

    /// <summary>Mở hộp thoại thêm rule tùy chỉnh cho node (mặc định: node đang chọn).</summary>
    internal void AddToClean(TreeNodeViewModel? node)
    {
        node ??= _selectedNode;
        if (node == null || node.IsPlaceholder) return;

        var check = _s.CustomRules.Check(node.FullPath);
        if (check.IsBlocked) return;

        var dialogVm = new AddCustomRuleDialogViewModel(node.FullPath, node.SizeBytes, node.FileCount,
            check.ReasonKeys.Select(k => _loc.Get(k)).ToList(), _loc);
        if (!_dialogs.ShowAddCustomRule(dialogVm)) return;

        try
        {
            var rule = _s.CustomRules.Add(node.FullPath, dialogVm.Name, dialogVm.SelectedAge.Hours);
            RuleAdded?.Invoke(rule);
        }
        catch (Exception ex)
        {
            AppLog.Instance.Error("Thêm rule tùy chỉnh lỗi", ex);
            var reason = ex is InvalidOperationException ? LocalizeReasons(_s.CustomRules.Check(node.FullPath)) : ex.Message;
            _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Clean.Custom.AddFailed", reason));
        }
    }

    private void SetState(AnalyzerState state)
    {
        _state = state;
        OnPropertyChanged(nameof(Status));
    }

    private async Task ScanAsync()
    {
        var path = RootPath.Trim();
        if (!Directory.Exists(path))
        {
            _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Analyzer.FolderMissing", path));
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Roots.Clear();
        _selectedNode = null;
        RefreshAddState();
        _progress = null;
        SetState(AnalyzerState.Scanning);
        try
        {
            var progress = new Progress<AnalyzeProgress>(p =>
            {
                _progress = p;
                SetState(AnalyzerState.Progress);
            });
            var root = await _s.Analyzer.AnalyzeAsync(path, progress, _cts.Token, maxDepth: 12);
            var node = new TreeNodeViewModel(root, root.SizeBytes, this) { IsExpanded = true };
            Roots.Add(node);
            _selectedNode = null;
            RefreshAddState();
            _doneNode = root;
            SetState(AnalyzerState.Done);
        }
        catch (OperationCanceledException)
        {
            SetState(AnalyzerState.Cancelled);
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
        AppLog.Instance.Error("Analyzer lỗi", ex);
        SetState(AnalyzerState.Error);
        _dialogs.Error(_loc.Get("App.Name"), _loc.Format("Analyzer.ScanFailed", ex.Message));
    }
}

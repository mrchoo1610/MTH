using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.UI.Mvvm;
using MiniTools.Updater;

namespace DiskCleaner.UI.ViewModels;

public sealed class NavItem : ObservableObject
{
    private readonly LocalizationService _loc;

    public NavItem(string titleKey, string icon, object viewModel, LocalizationService loc)
    {
        TitleKey = titleKey;
        Icon = icon;
        ViewModel = viewModel;
        _loc = loc;
        loc.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public string TitleKey { get; }
    public string Title => _loc.Get(TitleKey);
    public string Icon { get; }
    public object ViewModel { get; }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly LocalizationService _loc;
    private readonly IDialogService _dialogs;
    private readonly AppUpdateService _updates;
    private NavItem _selectedPage;

    public ObservableCollection<NavItem> Pages { get; } = [];
    public OverviewViewModel Overview { get; }
    public CleanViewModel Clean { get; }
    public AnalyzerViewModel Analyzer { get; }
    public LargeFilesViewModel LargeFiles { get; }

    public NavItem SelectedPage
    {
        get => _selectedPage;
        set { if (value != null) SetProperty(ref _selectedPage, value); }
    }

    public string VersionText { get; } = "v" + (Assembly.GetEntryAssembly()?.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.3.0");

    /// <summary>Có bản mới: hiện chấm tím ở link "Kiểm tra cập nhật".</summary>
    public bool UpdateAvailable => _updates.Available != null;

    public string UpdateTooltip => _updates.Available is { } m
        ? _loc.Format("Update.LinkAvailable", m.Version)
        : _loc.Get("Update.LinkTooltip");

    public ICommand CheckUpdateCommand { get; }

    private bool AnyBusy => Overview.IsBusy || Clean.IsBusy || Analyzer.IsBusy || LargeFiles.IsBusy;

    public bool IsVietnamese
    {
        get => _loc.Current == AppLanguage.Vi;
        set { if (value) _loc.SetLanguage(AppLanguage.Vi); }
    }

    public bool IsEnglish
    {
        get => _loc.Current == AppLanguage.En;
        set { if (value) _loc.SetLanguage(AppLanguage.En); }
    }

    public MainViewModel(AppServices services, IDialogService dialogs)
    {
        _loc = services.Loc;
        _dialogs = dialogs;
        Overview = new OverviewViewModel(services, dialogs);
        Clean = new CleanViewModel(services, dialogs);
        Analyzer = new AnalyzerViewModel(services, dialogs);
        LargeFiles = new LargeFilesViewModel(services, dialogs);

        var overview = new NavItem("Nav.Overview", "", Overview, _loc);
        Pages.Add(overview);
        Pages.Add(new NavItem("Nav.Clean", "", Clean, _loc));
        Pages.Add(new NavItem("Nav.Analyzer", "", Analyzer, _loc));
        Pages.Add(new NavItem("Nav.LargeFiles", "", LargeFiles, _loc));
        _selectedPage = overview;

        Overview.GoToClean = () => SelectedPage = Pages[1];
        Analyzer.RuleAdded += rule =>
        {
            SelectedPage = Pages[1];
            _ = Clean.ActivateCustomRuleAsync(rule);
        };
        Clean.CleanCompleted += (_, _) => Overview.RefreshDrives();
        _loc.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsVietnamese));
            OnPropertyChanged(nameof(IsEnglish));
        };

        _updates = new AppUpdateService(_loc, () => AnyBusy);
        _updates.AvailableChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(UpdateAvailable));
            OnPropertyChanged(nameof(UpdateTooltip));
        };
        _loc.LanguageChanged += (_, _) => OnPropertyChanged(nameof(UpdateTooltip));
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, null, ex => AppLog.Instance.Error("Check update", ex));
        _updates.StartAutoCheck();

        if (services.RejectedRules.Count > 0)
            dialogs.Info(_loc.Get("App.Name"), _loc.Format("App.RejectedRules", string.Join("\n", services.RejectedRules)));
    }

    private async Task CheckUpdateAsync()
    {
        if (_updates.Available is { } pending)
        {
            _updates.ShowDialog(pending);
            return;
        }

        var status = await _updates.CheckNowAsync();
        switch (status)
        {
            case UpdateCheckStatus.Available when _updates.Available is { } m:
                _updates.ShowDialog(m);
                break;
            case UpdateCheckStatus.Disabled:
                _dialogs.Info(_loc.Get("App.Name"), _loc.Get("Update.Disabled"));
                break;
            case UpdateCheckStatus.Failed:
                _dialogs.Error(_loc.Get("App.Name"), _loc.Get("Update.CheckFailed"));
                break;
            default:
                _dialogs.Info(_loc.Get("App.Name"), _loc.Format("Update.UpToDate", _updates.Client.Options.CurrentVersion));
                break;
        }
    }
}

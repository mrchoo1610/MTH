using System.Windows;
using DiskCleaner.Services;
using DiskCleaner.UI.ViewModels;
using DiskCleaner.UI.Views;
using Microsoft.Win32;

namespace DiskCleaner.UI;

public interface IDialogService
{
    bool Confirm(string title, string message);
    void Info(string title, string message);
    void Error(string title, string message);
    string? PickFolder(string? initial);

    /// <summary>Hộp thoại "Thêm vào Dọn dẹp". true nếu người dùng bấm Thêm (giá trị nhập nằm trong vm).</summary>
    bool ShowAddCustomRule(AddCustomRuleDialogViewModel vm);
}

public sealed class DialogService : IDialogService
{
    private readonly LocalizationService _loc;

    public DialogService(LocalizationService loc) => _loc = loc;

    private static Window? Owner => Application.Current?.MainWindow;

    private bool Show(string title, string message, DialogKind kind, string primary, string? secondary)
    {
        var dlg = new DialogWindow(title, message, kind, primary, secondary);
        var owner = Owner;
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true;
    }

    public bool Confirm(string title, string message) =>
        Show(title, message, DialogKind.Confirm, _loc.Get("Common.Yes"), _loc.Get("Common.No"));

    public void Info(string title, string message) =>
        Show(title, message, DialogKind.Info, _loc.Get("Common.OK"), null);

    public void Error(string title, string message) =>
        Show(title, message, DialogKind.Error, _loc.Get("Common.OK"), null);

    public bool ShowAddCustomRule(AddCustomRuleDialogViewModel vm)
    {
        var content = new AddCustomRuleContent { DataContext = vm };
        var dlg = new DialogWindow(_loc.Get("Custom.Dialog.Title"), "", DialogKind.Info,
            _loc.Get("Custom.Dialog.Add"), _loc.Get("Common.Cancel"), content);
        dlg.SetPrimaryEnabled(vm.CanAdd);
        vm.PropertyChanged += (_, _) => dlg.SetPrimaryEnabled(vm.CanAdd);
        var owner = Owner;
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true;
    }

    public string? PickFolder(string? initial)
    {
        var dlg = new OpenFolderDialog { Title = _loc.Get("Dialog.PickFolder") };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        var owner = Owner;
        var ok = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        return ok == true ? dlg.FolderName : null;
    }
}

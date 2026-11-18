using DiskCleaner.Core;
using DiskCleaner.Services;
using DiskCleaner.UI.Mvvm;

namespace DiskCleaner.UI.ViewModels;

public sealed record AgeOption(int Hours, string Text);

/// <summary>Dữ liệu của hộp thoại "Thêm vào Dọn dẹp" (tên, tuổi tối thiểu, thông tin thư mục, cảnh báo).</summary>
public sealed class AddCustomRuleDialogViewModel : ObservableObject
{
    private string _name;
    private AgeOption _selectedAge;

    public string FullPath { get; }
    public string SizeSummary { get; }
    public IReadOnlyList<AgeOption> AgeOptions { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool HasWarnings => Warnings.Count > 0;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(CanAdd));
        }
    }

    public AgeOption SelectedAge
    {
        get => _selectedAge;
        set => SetProperty(ref _selectedAge, value);
    }

    public bool CanAdd => !string.IsNullOrWhiteSpace(Name);

    public AddCustomRuleDialogViewModel(string fullPath, long sizeBytes, int fileCount, IReadOnlyList<string> warnings,
        LocalizationService loc)
    {
        FullPath = fullPath;
        SizeSummary = loc.Format("Custom.Dialog.SizeFormat", SizeFormatter.Format(sizeBytes), fileCount.ToString("N0"));
        Warnings = warnings;
        _name = CustomRuleStore.DefaultDisplayName(fullPath);
        AgeOptions =
        [
            new AgeOption(0, loc.Get("Custom.Dialog.Age.Any")),
            new AgeOption(24, loc.Get("Custom.Dialog.Age.24h")),
            new AgeOption(24 * 7, loc.Get("Custom.Dialog.Age.7d")),
            new AgeOption(24 * 30, loc.Get("Custom.Dialog.Age.30d"))
        ];
        _selectedAge = AgeOptions[0];
    }
}

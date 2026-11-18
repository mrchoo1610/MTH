using System.ComponentModel;
using System.Globalization;
using System.Windows;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>
/// Song ngữ VI/EN chạy lúc runtime. XAML dùng {DynamicResource Key}; ViewModel dùng Get/Format.
/// Bảng chuỗi lấy từ UI/Localization/Strings.{vi,en}.xaml.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string PackBase = "pack://application:,,,/DiskCleaner;component/UI/Localization/";

    private readonly SettingsService _settings;
    private readonly Func<AppLanguage, IReadOnlyDictionary<string, string>> _tableLoader;
    private readonly Action<AppLanguage>? _applyToApplication;
    private readonly Dictionary<AppLanguage, IReadOnlyDictionary<string, string>> _tables = [];
    private IReadOnlyDictionary<string, string> _table;

    public AppLanguage Current { get; private set; }

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Instance dùng chung cho code-behind/attached property (đặt bởi CreateForApplication).</summary>
    public static LocalizationService? Instance { get; private set; }

    public LocalizationService(SettingsService settings,
        Func<AppLanguage, IReadOnlyDictionary<string, string>> tableLoader,
        Action<AppLanguage>? applyToApplication = null)
    {
        _settings = settings;
        _tableLoader = tableLoader;
        _applyToApplication = applyToApplication;
        Current = settings.LoadLanguage();
        _table = LoadTable(Current);
    }

    /// <summary>Tạo bản chạy thật: đọc ResourceDictionary trong assembly và merge vào Application.Resources.</summary>
    public static LocalizationService CreateForApplication(SettingsService settings)
    {
        var dictionaries = new Dictionary<AppLanguage, ResourceDictionary>();
        ResourceDictionary Load(AppLanguage l)
        {
            if (!dictionaries.TryGetValue(l, out var d))
            {
                d = new ResourceDictionary { Source = new Uri(PackBase + FileName(l), UriKind.Absolute) };
                dictionaries[l] = d;
            }
            return d;
        }

        IReadOnlyDictionary<string, string> Table(AppLanguage l)
        {
            var map = new Dictionary<string, string>();
            var dict = Load(l);
            foreach (var key in dict.Keys)
                if (key is string k && dict[k] is string v) map[k] = v;
            return map;
        }

        ResourceDictionary? applied = null;
        void Apply(AppLanguage l)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            var next = Load(l);
            if (ReferenceEquals(applied, next)) return;
            if (applied != null) merged.Remove(applied);
            merged.Add(next);
            applied = next;
        }

        var svc = new LocalizationService(settings, Table, Apply);
        Apply(svc.Current);
        Instance = svc;
        return svc;
    }

    public static string FileName(AppLanguage l) => l == AppLanguage.Vi ? "Strings.vi.xaml" : "Strings.en.xaml";

    private IReadOnlyDictionary<string, string> LoadTable(AppLanguage l)
    {
        if (!_tables.TryGetValue(l, out var t))
        {
            t = _tableLoader(l);
            _tables[l] = t;
        }
        return t;
    }

    /// <summary>Đổi ngôn ngữ ngay (không cần khởi động lại), lưu settings, báo cho UI/ViewModel.</summary>
    public void SetLanguage(AppLanguage lang)
    {
        if (lang == Current) return;
        Current = lang;
        _table = LoadTable(lang);
        _applyToApplication?.Invoke(lang);
        _settings.SaveLanguage(lang);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Thiếu key thì trả "[key]" (nhìn thấy ngay khi test).</summary>
    public string Get(string key) => _table.TryGetValue(key, out var v) ? v : $"[{key}]";

    public string this[string key] => Get(key);

    public string Format(string key, params object[] args)
    {
        var pattern = Get(key);
        try { return string.Format(CultureInfo.CurrentCulture, pattern, args); }
        catch (FormatException) { return pattern; }
    }
}

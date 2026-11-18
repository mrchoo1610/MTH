using System.Windows;
using System.Windows.Controls;
using DiskCleaner.Services;

namespace DiskCleaner.UI.Localization;

/// <summary>
/// Attached property cho DataGridColumn (không nằm trong visual tree nên {DynamicResource} không dùng được):
/// loc:L.HeaderKey="Clean.Col.Item" sẽ đặt Header theo ngôn ngữ hiện tại và tự cập nhật khi đổi ngôn ngữ.
/// </summary>
public static class L
{
    public static readonly DependencyProperty HeaderKeyProperty =
        DependencyProperty.RegisterAttached("HeaderKey", typeof(string), typeof(L), new PropertyMetadata(null, OnHeaderKeyChanged));

    private static readonly List<WeakReference<DataGridColumn>> Columns = [];
    private static bool _subscribed;

    public static string? GetHeaderKey(DependencyObject d) => (string?)d.GetValue(HeaderKeyProperty);
    public static void SetHeaderKey(DependencyObject d, string? value) => d.SetValue(HeaderKeyProperty, value);

    private static void OnHeaderKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGridColumn col) return;
        Columns.Add(new WeakReference<DataGridColumn>(col));
        Apply(col);
        var loc = LocalizationService.Instance;
        if (loc != null && !_subscribed)
        {
            _subscribed = true;
            loc.LanguageChanged += (_, _) => Refresh();
        }
    }

    private static void Refresh()
    {
        Columns.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in Columns)
            if (w.TryGetTarget(out var c)) Apply(c);
    }

    private static void Apply(DataGridColumn col)
    {
        var key = GetHeaderKey(col);
        if (key != null) col.Header = LocalizationService.Instance?.Get(key) ?? key;
    }
}

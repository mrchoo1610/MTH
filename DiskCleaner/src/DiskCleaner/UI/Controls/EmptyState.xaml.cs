using System.Windows;
using System.Windows.Controls;

namespace DiskCleaner.UI.Controls;

/// <summary>Empty state: icon lớn mờ + tiêu đề + 1 dòng hướng dẫn. Title/Hint gán bằng {DynamicResource}.</summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public EmptyState() => InitializeComponent();
}

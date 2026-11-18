using System.Windows;
using System.Windows.Media;

namespace DiskCleaner.UI;

public enum DialogKind { Info, Error, Confirm }

/// <summary>Hộp thoại dark thay cho MessageBox (nút Có/Không/OK theo ngôn ngữ của app).</summary>
public partial class DialogWindow : Window
{
    public DialogWindow(string title, string message, DialogKind kind, string primaryText, string? secondaryText,
        object? extraContent = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        if (string.IsNullOrEmpty(message)) MessageScroll.Visibility = Visibility.Collapsed;
        if (extraContent != null) ExtraHost.Content = extraContent;
        else ExtraHost.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = primaryText;
        if (secondaryText == null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
        }

        switch (kind)
        {
            case DialogKind.Error:
                IconText.Text = ""; // ErrorBadge
                IconText.Foreground = (Brush)FindResource("DangerBrush");
                IconBox.Background = (Brush)FindResource("DangerSoftBrush");
                break;
            case DialogKind.Confirm:
                IconText.Text = ""; // Help
                break;
            default:
                IconText.Text = ""; // Info
                break;
        }

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        };
    }

    public void SetPrimaryEnabled(bool enabled) => PrimaryButton.IsEnabled = enabled;

    private void OnPrimary(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSecondary(object sender, RoutedEventArgs e) => DialogResult = false;
}

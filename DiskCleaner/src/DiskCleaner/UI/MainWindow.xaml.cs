using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using DiskCleaner.Services;
using DiskCleaner.UI.ViewModels;

namespace DiskCleaner.UI;

public partial class MainWindow : Window
{
    private readonly LocalizationService _loc;

    public MainWindow(AppServices services)
    {
        _loc = services.Loc;
        InitializeComponent();
        FitToWorkArea();

        var vm = new MainViewModel(services, new DialogService(services.Loc));
        DataContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        SourceInitialized += (_, _) => WindowInterop.EnableDarkTitleBar(this);
        StateChanged += (_, _) => UpdateMaximizeState();
        _loc.LanguageChanged += (_, _) => UpdateMaximizeState();
        UpdateMaximizeState();
    }

    /// <summary>Màn hình nhỏ / DPI cao (vd 1080p ở 150%): thu cửa sổ cho vừa vùng làm việc, nội dung đã cuộn được.</summary>
    private void FitToWorkArea()
    {
        var work = SystemParameters.WorkArea; // DIP
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, work.Width - 16));
        Height = Math.Max(MinHeight, Math.Min(Height, work.Height - 16));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedPage)) PlayPageTransition();
    }

    private void PlayPageTransition()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(150);
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        PageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }

    private void UpdateMaximizeState()
    {
        var maximized = WindowState == WindowState.Maximized;
        // Cửa sổ WindowChrome khi maximize bị tràn ra ngoài màn hình đúng bằng độ dày viền resize.
        RootBorder.Padding = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaxButton.Content = maximized ? "" : "";
        MaxButton.SetResourceReference(ToolTipProperty, maximized ? "Window.Restore" : "Window.Maximize");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

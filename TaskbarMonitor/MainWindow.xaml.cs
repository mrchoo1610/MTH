using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskbarMonitor.Models;
using TaskbarMonitor.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfApp = System.Windows.Application;
using WpfPoint = System.Windows.Point;
using WpfBrush = System.Windows.Media.Brush;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WinForms = System.Windows.Forms;

namespace TaskbarMonitor;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HardwareMonitorService _monitor;
    private WinForms.NotifyIcon? _notifyIcon;
    private AppUpdateService? _updates;
    private MiniTools.Contracts.ReleaseManifest? _pendingUpdate;

    private WpfPoint _dragStartPoint;
    private bool _isDragging;

    private static readonly WpfBrush NormalBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC));
    private static readonly WpfBrush WarningBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)); // Amber
    private static readonly WpfBrush DangerBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));  // Red

    public MainWindow()
    {
        Logger.Log("MainWindow initializing...");
        InitializeComponent();

        _settings = AppSettings.Load();
        _monitor = new HardwareMonitorService();
        _monitor.SnapshotUpdated += OnSnapshotUpdated;

        ApplySettings();
        InitTrayIcon();
        Logger.Log("MainWindow initialized.");
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Log("Window_Loaded called.");
        UpdateLayout();
        double w = ActualWidth > 50 ? ActualWidth : 420;
        double h = ActualHeight > 10 ? ActualHeight : 30;

        if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0 &&
            TaskbarHelper.IsPositionValid(_settings.WindowLeft, _settings.WindowTop, w, h))
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
        else
        {
            SnapToTaskbar();
        }

        _monitor.Start(_settings.RefreshIntervalMs);
        Logger.Log($"Monitor started. Placed at Left={Left}, Top={Top}, Width={ActualWidth}, Height={ActualHeight}");
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Logger.Log("Window_Closing called.");
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();

        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        _monitor.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }

    #region Settings & Visibility

    private void ApplySettings()
    {
        Opacity = _settings.WindowOpacity;
        UpdateVisibility();
    }

    private void SyncSettingsUi()
    {
        ChkCpu.IsChecked = _settings.ShowCpu;
        ChkRam.IsChecked = _settings.ShowRam;
        ChkGpu.IsChecked = _settings.ShowGpu;
        ChkVram.IsChecked = _settings.ShowVram;
        ChkDisk.IsChecked = _settings.ShowDisk;

        ChkLockPosition.IsChecked = _settings.IsPositionLocked;
        ChkAutoStart.IsChecked = _settings.AutoStartWithWindows;
    }

    private void UpdateVisibility()
    {
        CpuPanel.Visibility = _settings.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
        DivRam.Visibility = (_settings.ShowCpu && (_settings.ShowRam || _settings.ShowGpu || _settings.ShowVram || _settings.ShowDisk)) ? Visibility.Visible : Visibility.Collapsed;

        RamPanel.Visibility = _settings.ShowRam ? Visibility.Visible : Visibility.Collapsed;
        DivGpu.Visibility = (_settings.ShowRam && (_settings.ShowGpu || _settings.ShowVram || _settings.ShowDisk)) ? Visibility.Visible : Visibility.Collapsed;

        GpuPanel.Visibility = _settings.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
        DivVram.Visibility = (_settings.ShowGpu && (_settings.ShowVram || _settings.ShowDisk)) ? Visibility.Visible : Visibility.Collapsed;

        VramPanel.Visibility = _settings.ShowVram ? Visibility.Visible : Visibility.Collapsed;
        DivDisk.Visibility = (_settings.ShowVram && _settings.ShowDisk) ? Visibility.Visible : Visibility.Collapsed;

        DiskPanel.Visibility = _settings.ShowDisk ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SnapToTaskbar()
    {
        UpdateLayout();
        double w = ActualWidth > 50 ? ActualWidth : 420;
        double h = ActualHeight > 10 ? ActualHeight : 30;
        var (left, top) = TaskbarHelper.GetTaskbarDockPosition(w, h);
        Left = left;
        Top = top;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
        Logger.Log($"SnapToTaskbar: Placed at Left={Left}, Top={Top}");
    }

    public void CenterOnScreen()
    {
        UpdateLayout();
        double w = ActualWidth > 50 ? ActualWidth : 420;
        double h = ActualHeight > 10 ? ActualHeight : 30;
        Left = (SystemParameters.PrimaryScreenWidth - w) / 2.0;
        Top = (SystemParameters.PrimaryScreenHeight - h) / 2.0;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
        Activate();
        Logger.Log($"CenterOnScreen: Placed at Left={Left}, Top={Top}");
    }

    #endregion

    #region Hardware Updates

    private void OnSnapshotUpdated(HardwareSnapshot s)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // 1. CPU
            TxtCpuUsage.Text = $"{s.CpuUsage:0}%";
            TxtCpuUsage.Foreground = s.CpuUsage >= 90 ? DangerBrush : (s.CpuUsage >= 75 ? WarningBrush : NormalBrush);

            if (s.CpuTemp.HasValue && s.CpuTemp.Value > 0)
            {
                TxtCpuTemp.Text = $"{s.CpuTemp.Value:0}°C";
                TxtCpuTemp.Foreground = s.CpuTemp.Value >= 85 ? DangerBrush : (s.CpuTemp.Value >= 75 ? WarningBrush : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)));
            }
            else
            {
                TxtCpuTemp.Text = string.Empty;
            }

            // 2. RAM
            TxtRamUsage.Text = $"{s.RamUsedGb:0.0}G ({s.RamPercent:0}%)";
            TxtRamUsage.Foreground = s.RamPercent >= 90 ? DangerBrush : (s.RamPercent >= 80 ? WarningBrush : NormalBrush);

            // 3. GPU
            TxtGpuUsage.Text = $"{s.GpuUsage:0}%";
            TxtGpuUsage.Foreground = s.GpuUsage >= 90 ? DangerBrush : (s.GpuUsage >= 80 ? WarningBrush : NormalBrush);

            if (s.GpuTemp.HasValue && s.GpuTemp.Value > 0)
            {
                TxtGpuTemp.Text = $"{s.GpuTemp.Value:0}°C";
                TxtGpuTemp.Foreground = s.GpuTemp.Value >= 83 ? DangerBrush : (s.GpuTemp.Value >= 75 ? WarningBrush : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)));
            }
            else
            {
                TxtGpuTemp.Text = string.Empty;
            }

            // 4. VRAM
            if (s.VramTotalGb > 0 && s.VramTotalGb != 100f)
            {
                TxtVramUsage.Text = $"{s.VramUsedGb:0.0}/{s.VramTotalGb:0.0}G ({s.VramPercent:0}%)";
            }
            else
            {
                TxtVramUsage.Text = $"{s.VramUsedGb:0}%";
            }
            TxtVramUsage.Foreground = s.VramPercent >= 90 ? DangerBrush : (s.VramPercent >= 80 ? WarningBrush : NormalBrush);

            // 5. Disk Speed
            if (s.DiskTotalSpeedMb >= 1.0)
            {
                TxtDiskSpeed.Text = $"{s.DiskTotalSpeedMb:0.0} MB/s";
            }
            else
            {
                TxtDiskSpeed.Text = "0 MB/s";
            }

            // Update Flyout if open
            if (FlyoutPopup.IsOpen)
            {
                UpdateFlyoutData(s);
            }
        });
    }

    private void UpdateFlyoutData(HardwareSnapshot s)
    {
        TxtGpuModel.Text = string.IsNullOrEmpty(s.GpuName) ? "NVIDIA Graphics" : s.GpuName;
        TxtCpuModel.Text = string.IsNullOrEmpty(s.CpuName) ? "Processor" : s.CpuName;
        TxtAdminBadge.Text = s.IsAdmin ? "ADMIN" : "USER";
        TxtAdminBadge.Foreground = s.IsAdmin ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));

        TxtFlyoutCpu.Text = s.CpuTemp.HasValue ? $"{s.CpuUsage:0}% ({s.CpuTemp.Value:0}°C)" : $"{s.CpuUsage:0}%";
        PbCpu.Value = Math.Clamp(s.CpuUsage, 0, 100);

        TxtFlyoutRam.Text = $"{s.RamUsedGb:0.0} GB / {s.RamTotalGb:0.0} GB ({s.RamPercent:0}%)";
        PbRam.Value = Math.Clamp(s.RamPercent, 0, 100);

        TxtFlyoutGpu.Text = s.GpuTemp.HasValue ? $"{s.GpuUsage:0}% ({s.GpuTemp.Value:0}°C)" : $"{s.GpuUsage:0}%";
        PbGpu.Value = Math.Clamp(s.GpuUsage, 0, 100);

        TxtFlyoutVram.Text = $"{s.VramUsedGb:0.0} GB / {s.VramTotalGb:0.0} GB ({s.VramPercent:0}%)";
        PbVram.Value = Math.Clamp(s.VramPercent, 0, 100);

        TxtFlyoutDisk.Text = $"R: {s.DiskReadSpeedMb:0.0} MB/s | W: {s.DiskWriteSpeedMb:0.0} MB/s";
    }

    private async void RefreshProcesses()
    {
        var processes = await Task.Run(() => ProcessHelper.GetTopMemoryProcesses(5));
        ProcessList.ItemsSource = processes;
    }

    #endregion

    #region Mouse Interactions (Left Click = Flyout, Right Click = Settings)

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
        {
            var currentPoint = e.GetPosition(this);
            var diff = _dragStartPoint - currentPoint;

            if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
            {
                _isDragging = true;
                if (!_settings.IsPositionLocked)
                {
                    ReleaseMouseCapture();
                    try
                    {
                        DragMove();
                        _settings.WindowLeft = Left;
                        _settings.WindowTop = Top;
                    }
                    catch { }
                }
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        // Click: Toggle Flyout
        if (!_isDragging)
        {
            SettingsPopup.IsOpen = false; // close settings if open
            FlyoutPopup.IsOpen = !FlyoutPopup.IsOpen;
            if (FlyoutPopup.IsOpen)
            {
                UpdateFlyoutData(_monitor.CurrentSnapshot);
                RefreshProcesses();
            }
        }

        _isDragging = false;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        FlyoutPopup.IsOpen = false; // close flyout if open
        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
        if (SettingsPopup.IsOpen)
        {
            SyncSettingsUi();
        }
    }

    #endregion

    #region Settings Panel Events

    private void BtnCloseSettings_Click(object sender, MouseButtonEventArgs e)
    {
        SettingsPopup.IsOpen = false;
    }

    private void ChkMetric_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowCpu = ChkCpu.IsChecked == true;
        _settings.ShowRam = ChkRam.IsChecked == true;
        _settings.ShowGpu = ChkGpu.IsChecked == true;
        _settings.ShowVram = ChkVram.IsChecked == true;
        _settings.ShowDisk = ChkDisk.IsChecked == true;
        _settings.Save();
        UpdateVisibility();
    }

    private void BtnOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is string tagStr && double.TryParse(tagStr, System.Globalization.CultureInfo.InvariantCulture, out double opacity))
        {
            _settings.WindowOpacity = opacity;
            _settings.Save();
            Opacity = opacity;
        }
    }

    private void BtnRefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int interval))
        {
            _settings.RefreshIntervalMs = interval;
            _settings.Save();
            _monitor.Start(interval);
        }
    }

    private void ChkLockPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsPositionLocked = ChkLockPosition.IsChecked == true;
        _settings.Save();
    }

    private void ChkAutoStart_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkAutoStart.IsChecked == true;
        _settings.AutoStartWithWindows = enable;
        AppSettings.SetAutoStart(enable);
        _settings.Save();
    }

    private void BtnSnapTaskbar_Click(object sender, RoutedEventArgs e)
    {
        SnapToTaskbar();
    }

    private void BtnRunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                WpfApp.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show($"Failed to restart as Administrator: {ex.Message}", "Notice", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
        }
    }

    private void MenuOpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch { }
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        WpfApp.Current.Shutdown();
    }

    #endregion

    #region Tray Icon

    private void InitTrayIcon()
    {
        try
        {
            System.Drawing.Icon? appIcon = null;
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                try { appIcon = new System.Drawing.Icon(iconPath); } catch { }
            }

            if (appIcon == null)
            {
                try
                {
                    var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
                    if (streamInfo != null)
                    {
                        appIcon = new System.Drawing.Icon(streamInfo.Stream);
                    }
                }
                catch { }
            }

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = appIcon ?? System.Drawing.SystemIcons.Application,
                Text = "Taskbar Hardware Monitor v" + AppUpdateService.CurrentVersionText,
                Visible = true
            };

            var trayMenu = new WinForms.ContextMenuStrip();
            var titleItem = trayMenu.Items.Add("Taskbar Monitor v" + AppUpdateService.CurrentVersionText);
            titleItem.Enabled = false;
            trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            trayMenu.Items.Add("Show / Hide Monitor", null, (s, e) =>
            {
                if (Visibility == Visibility.Visible)
                    Visibility = Visibility.Hidden;
                else
                {
                    Visibility = Visibility.Visible;
                    Activate();
                }
            });
            trayMenu.Items.Add("Snap to Taskbar (Bottom-Right)", null, (s, e) => SnapToTaskbar());
            trayMenu.Items.Add("Center on Screen", null, (s, e) => CenterOnScreen());
            trayMenu.Items.Add("Settings", null, (s, e) =>
            {
                Visibility = Visibility.Visible;
                Activate();
                SettingsPopup.IsOpen = true;
                SyncSettingsUi();
            });
            trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            trayMenu.Items.Add("Kiểm tra cập nhật…", null, async (s, e) => await CheckForUpdatesAsync());
            trayMenu.Items.Add("Exit", null, (s, e) => WpfApp.Current.Shutdown());

            _notifyIcon.ContextMenuStrip = trayMenu;
            _notifyIcon.DoubleClick += (s, e) =>
            {
                Visibility = Visibility.Visible;
                Activate();
            };

            InitUpdates();
        }
        catch { }
    }

    #endregion

    #region Updates

    private void InitUpdates()
    {
        try
        {
            _updates = new AppUpdateService();
            if (_notifyIcon != null) _notifyIcon.BalloonTipClicked += (_, _) => ShowUpdateDialog();
            _updates.UpdateFound += m => Dispatcher.BeginInvoke(() =>
            {
                _pendingUpdate = m;
                ShowTrayMessage($"Có bản mới {m.Version}. Bấm vào đây để xem.", WinForms.ToolTipIcon.Info);
            });
            _updates.StartAutoCheck();
            Closed += (_, _) => _updates?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Log("Update init failed: " + ex.Message);
        }
    }

    private void ShowUpdateDialog()
    {
        if (_updates == null || _pendingUpdate == null) return;
        _updates.ShowDialog(IsVisible ? this : null, _pendingUpdate);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updates == null) return;
        try
        {
            var status = await _updates.CheckNowAsync();
            switch (status)
            {
                case MiniTools.Updater.UpdateCheckStatus.Available when _updates.Available is { } m:
                    _pendingUpdate = m;
                    ShowUpdateDialog();
                    break;
                case MiniTools.Updater.UpdateCheckStatus.Disabled:
                    _pendingUpdate = null;
                    ShowTrayMessage("Bản này chưa bật cập nhật tự động (chưa có khóa xác thực chữ ký). Hãy tải bản mới thủ công.", WinForms.ToolTipIcon.Warning);
                    break;
                case MiniTools.Updater.UpdateCheckStatus.Failed:
                    _pendingUpdate = null;
                    ShowTrayMessage("Không kiểm tra được cập nhật. Hãy kiểm tra kết nối mạng rồi thử lại.", WinForms.ToolTipIcon.Error);
                    break;
                default:
                    _pendingUpdate = null;
                    ShowTrayMessage($"Bạn đang dùng bản mới nhất ({AppUpdateService.CurrentVersionText}).", WinForms.ToolTipIcon.Info);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Manual update check failed: " + ex);
        }
    }

    private void ShowTrayMessage(string text, WinForms.ToolTipIcon icon)
    {
        if (_notifyIcon == null) return;
        _notifyIcon.BalloonTipTitle = "Taskbar Monitor";
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(8000);
    }

    #endregion
}

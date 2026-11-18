using System.Windows;
using System.Windows.Threading;
using DiskCleaner.Services;
using DiskCleaner.UI;

namespace DiskCleaner;

public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Chế độ elevated: không mở cửa sổ, chỉ dọn các rule cần admin rồi thoát.
        var idx = Array.IndexOf(e.Args, ElevationService.ElevatedArg);
        if (idx >= 0 && idx + 1 < e.Args.Length)
        {
            var jobFile = e.Args[idx + 1];
            var logger = FileLogger.CreateTimestamped("clean-elevated");
            int code;
            try
            {
                code = Task.Run(() => ElevatedCleanRunner.Run(jobFile, AppServices.RulesPath, new SystemEnvironment(),
                    new CommandRunner(), new RecycleBinService(), logger)).GetAwaiter().GetResult();
            }
            finally
            {
                logger.Dispose();
            }
            Shutdown(code);
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Instance.Error("Unhandled exception", args.ExceptionObject as Exception);

        _services = new AppServices();
        var window = new MainWindow(_services);
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Instance.Error("Lỗi chưa xử lý", e.Exception);
        AppLog.Instance.Flush();
        var message = _services?.Loc.Format("App.UnhandledError", e.Exception.Message)
                      ?? "Unexpected error:\n" + e.Exception.Message;
        MessageBox.Show(message, "Disk Cleaner", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

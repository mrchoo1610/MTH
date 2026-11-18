using System;
using System.IO;
using System.Windows;
using TaskbarMonitor.Services;

namespace TaskbarMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Log("App OnStartup started.");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Logger.Log($"AppDomain UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Logger.Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"App OnExit with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }
}

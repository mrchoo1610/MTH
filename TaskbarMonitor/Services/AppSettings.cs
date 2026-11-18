using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TaskbarMonitor.Services;

public class AppSettings
{
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunName = "TaskbarHardwareMonitor";

    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public bool IsPositionLocked { get; set; } = false;
    public double WindowOpacity { get; set; } = 0.90;

    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowDisk { get; set; } = true;

    public int RefreshIntervalMs { get; set; } = 1000;
    public bool AutoStartWithWindows { get; set; } = false;

    public static string GetSettingsFilePath()
    {
        // 1. Check for local portable mode (file exists or .portable flag present)
        var localDir = AppDomain.CurrentDomain.BaseDirectory;
        var localSettings = Path.Combine(localDir, "settings.json");
        var portableFlag = Path.Combine(localDir, ".portable");

        if (File.Exists(localSettings) || File.Exists(portableFlag))
        {
            return localSettings;
        }

        // 2. Standard AppData location
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarMonitor"
        );
        return Path.Combine(appDataFolder, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var filePath = GetSettingsFilePath();
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    settings.AutoStartWithWindows = IsAutoStartEnabled();
                    return settings;
                }
            }
        }
        catch { }

        var def = new AppSettings();
        def.AutoStartWithWindows = IsAutoStartEnabled();
        return def;
    }

    public void Save()
    {
        try
        {
            var filePath = GetSettingsFilePath();
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch { }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, false);
            return key?.GetValue(AppRunName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppRunName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRunName, false);
            }
        }
        catch { }
    }
}

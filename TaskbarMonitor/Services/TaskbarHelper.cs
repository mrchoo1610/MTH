using System;
using System.Windows;

namespace TaskbarMonitor.Services;

public static class TaskbarHelper
{
    /// <summary>
    /// Computes safe dock position in WPF DIPs right above the taskbar at bottom-right corner.
    /// </summary>
    public static (double Left, double Top) GetTaskbarDockPosition(double windowWidth, double windowHeight)
    {
        var workArea = SystemParameters.WorkArea;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var screenWidth = SystemParameters.PrimaryScreenWidth;

        double width = windowWidth > 50 ? windowWidth : 420;
        double height = windowHeight > 10 ? windowHeight : 30;

        // Dock right at bottom-right of work area (immediately above taskbar, next to tray)
        double left = workArea.Right - width - 16;
        double top = workArea.Bottom - height - 4;

        // Safety clamps
        left = Math.Clamp(left, 10, screenWidth - width - 10);
        top = Math.Clamp(top, 10, screenHeight - height - 10);

        return (left, top);
    }

    public static (double Left, double Top) GetDefaultPosition(double windowWidth, double windowHeight)
    {
        return GetTaskbarDockPosition(windowWidth, windowHeight);
    }

    /// <summary>
    /// Validates if a position is completely visible on screen.
    /// </summary>
    public static bool IsPositionValid(double left, double top, double windowWidth, double windowHeight)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        double width = windowWidth > 50 ? windowWidth : 420;
        double height = windowHeight > 10 ? windowHeight : 30;

        if (left < 0 || left > screenWidth - 60) return false;
        if (top < 0 || top > screenHeight - 40) return false;

        return true;
    }
}

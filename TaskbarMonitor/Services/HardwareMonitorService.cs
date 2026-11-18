using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using TaskbarMonitor.Models;

namespace TaskbarMonitor.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private CancellationTokenSource? _cts;

    public event Action<HardwareSnapshot>? SnapshotUpdated;

    public HardwareSnapshot CurrentSnapshot { get; private set; } = new();

    public HardwareMonitorService()
    {
        Logger.Log("HardwareMonitorService ctor: creating Computer...");
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true
        };

        try
        {
            _computer.Open();
            Logger.Log($"HardwareMonitorService: Computer opened. Hardware count = {_computer.Hardware.Count}");
            foreach (var h in _computer.Hardware)
            {
                Logger.Log($"Found hardware: {h.HardwareType} - {h.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error opening LibreHardwareMonitor: {ex}");
        }
    }

    public void Start(int intervalMs = 1000)
    {
        Logger.Log($"HardwareMonitorService.Start called with interval {intervalMs}ms");
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            Logger.Log("HardwareMonitorService: background loop started.");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var snapshot = CollectSnapshot();
                    CurrentSnapshot = snapshot;
                    SnapshotUpdated?.Invoke(snapshot);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error in loop iteration: {ex}");
                }

                try
                {
                    await Task.Delay(intervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
            Logger.Log("HardwareMonitorService: background loop stopped.");
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public HardwareSnapshot CollectSnapshot()
    {
        var snapshot = new HardwareSnapshot
        {
            IsAdmin = CheckIsAdmin()
        };

        // Update hardware sensors
        try
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();
                foreach (var sub in hw.SubHardware)
                {
                    sub.Update();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error updating hardware: {ex.Message}");
        }

        // 1. CPU
        try
        {
            var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu != null)
            {
                snapshot.CpuName = cpu.Name;
                
                var loadSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && (s.Name.Contains("Total") || s.Name.Contains("CPU")));
                if (loadSensor?.Value != null)
                {
                    snapshot.CpuUsage = (float)Math.Round(loadSensor.Value.Value, 1);
                }

                var tempSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && (s.Name.Contains("Package") || s.Name.Contains("Core Max")));
                if (tempSensor == null)
                {
                    tempSensor = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core")).OrderByDescending(s => s.Value).FirstOrDefault();
                }
                if (tempSensor?.Value != null)
                {
                    snapshot.CpuTemp = (float)Math.Round(tempSensor.Value.Value, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error reading CPU: {ex.Message}");
        }

        // 2. RAM
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                var totalGb = memStatus.ullTotalPhys / (1024.0f * 1024.0f * 1024.0f);
                var availGb = memStatus.ullAvailPhys / (1024.0f * 1024.0f * 1024.0f);
                var usedGb = totalGb - availGb;

                snapshot.RamTotalGb = (float)Math.Round(totalGb, 1);
                snapshot.RamUsedGb = (float)Math.Round(usedGb, 1);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error reading RAM: {ex.Message}");
        }

        // 3. GPU (Prioritize NVIDIA)
        try
        {
            var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
                   ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
                   ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);

            if (gpu != null)
            {
                snapshot.GpuName = gpu.Name;

                // GPU Core Load
                var gpuLoad = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && (s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("3D") || s.Name.Contains("Core")));
                if (gpuLoad?.Value != null)
                {
                    snapshot.GpuUsage = (float)Math.Round(gpuLoad.Value.Value, 1);
                }

                // GPU Temp
                var gpuTemp = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && (s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core")));
                if (gpuTemp?.Value != null)
                {
                    snapshot.GpuTemp = (float)Math.Round(gpuTemp.Value.Value, 0);
                }

                // VRAM
                var vramUsedSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && (s.Name.Contains("Memory Used") || s.Name.Contains("VRAM Used")));
                var vramTotalSensor = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && (s.Name.Contains("Memory Total") || s.Name.Contains("VRAM Total")));

                if (vramUsedSensor?.Value != null && vramTotalSensor?.Value != null)
                {
                    snapshot.VramUsedGb = (float)Math.Round(vramUsedSensor.Value.Value / 1024.0f, 1);
                    snapshot.VramTotalGb = (float)Math.Round(vramTotalSensor.Value.Value / 1024.0f, 1);
                }
                else
                {
                    var vramLoad = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && (s.Name.Contains("Memory") || s.Name.Contains("VRAM")));
                    if (vramLoad?.Value != null)
                    {
                        snapshot.VramUsedGb = (float)Math.Round(vramLoad.Value.Value, 0);
                        snapshot.VramTotalGb = 100f;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error reading GPU: {ex.Message}");
        }

        // 4. Disk Read / Write Speed via LibreHardwareMonitor Storage
        try
        {
            double totalRead = 0;
            double totalWrite = 0;
            foreach (var drive in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
            {
                var r = drive.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name.Contains("Read Rate"))?.Value ?? 0;
                var w = drive.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Throughput && s.Name.Contains("Write Rate"))?.Value ?? 0;
                totalRead += r;
                totalWrite += w;
            }

            snapshot.DiskReadSpeedMb = Math.Round(totalRead / (1024.0 * 1024.0), 1);
            snapshot.DiskWriteSpeedMb = Math.Round(totalWrite / (1024.0 * 1024.0), 1);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error reading Disk: {ex.Message}");
        }

        return snapshot;
    }

    public static bool CheckIsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        try
        {
            _computer.Close();
        }
        catch { }
    }

    #region Win32 API
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    #endregion
}

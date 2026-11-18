namespace TaskbarMonitor.Models;

public class HardwareSnapshot
{
    // CPU
    public string CpuName { get; set; } = "CPU";
    public float CpuUsage { get; set; }
    public float? CpuTemp { get; set; }

    // RAM
    public float RamUsedGb { get; set; }
    public float RamTotalGb { get; set; }
    public float RamPercent => RamTotalGb > 0 ? (RamUsedGb / RamTotalGb * 100f) : 0;

    // GPU (NVIDIA preferred)
    public string GpuName { get; set; } = "GPU";
    public float GpuUsage { get; set; }
    public float? GpuTemp { get; set; }

    // VRAM
    public float VramUsedGb { get; set; }
    public float VramTotalGb { get; set; }
    public float VramPercent => VramTotalGb > 0 ? (VramUsedGb / VramTotalGb * 100f) : 0;

    // Disk
    public double DiskReadSpeedMb { get; set; }
    public double DiskWriteSpeedMb { get; set; }
    public double DiskTotalSpeedMb => DiskReadSpeedMb + DiskWriteSpeedMb;

    public bool IsAdmin { get; set; }
}

public class ProcessItem
{
    public string Name { get; set; } = string.Empty;
    public double MemoryMb { get; set; }
    public double CpuPercent { get; set; }
}

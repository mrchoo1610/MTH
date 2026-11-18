using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaskbarMonitor.Models;

namespace TaskbarMonitor.Services;

public static class ProcessHelper
{
    public static List<ProcessItem> GetTopMemoryProcesses(int count = 5)
    {
        try
        {
            var processes = Process.GetProcesses();
            return processes
                .Select(p =>
                {
                    try
                    {
                        return new ProcessItem
                        {
                            Name = p.ProcessName,
                            MemoryMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 0)
                        };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(p => p != null && p.MemoryMb > 50)
                .OrderByDescending(p => p!.MemoryMb)
                .Take(count)
                .ToList()!;
        }
        catch
        {
            return new List<ProcessItem>();
        }
    }
}

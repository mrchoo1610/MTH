When you are in the middle of a heavy Blender render or a huge solution build, you usually find out the machine is struggling only **after** it has frozen. **Taskbar Hardware Monitor** puts the numbers that matter right on your taskbar so you can see them at all times, without opening Task Manager or a bulky monitoring suite.

### Who it is for

- 3D artists (Blender, Unreal Engine, Maya...) who need to keep an eye on **VRAM** to avoid out-of-memory crashes mid-render.
- Developers who build, run tests and keep many IDEs and browser tabs open, and want to know what is eating their RAM.
- Anyone who wants a small, good-looking hardware gauge that costs next to nothing.

### What it shows

- **CPU**: multi-core load and package temperature.
- **RAM**: used / total (GB) and percentage.
- **NVIDIA GPU**: 3D / CUDA engine load and temperature.
- **VRAM**: used / total (GB), the vital number when rendering.
- **Disk**: read/write throughput in MB/s.

Colors change with severity: white when normal, amber from 75%, red from 90% (or above 82°C), so a glance is enough.

### Controls

- **Drag** the widget anywhere on the taskbar or screen, or pick *Snap to Taskbar* to dock it next to the notification area.
- **Left-click** to open the detail card, including the processes using the most RAM.
- **Right-click** for the menu: lock position, choose which metrics to show, opacity (60-100%), refresh rate (0.5 / 1 / 2 s), start with Windows, run as Administrator.
- Minimize to the system tray at any time.

### Light and honest

Built with C# .NET (WPF), it uses roughly 30-50 MB of RAM and under 0.1% CPU. The download is **portable**: unzip and run, no installer. The app does not collect your personal data. Hardware readings come from the open-source LibreHardwareMonitor library.

### Quick start

1. Download the ZIP and extract it anywhere.
2. Run `TaskbarMonitor.exe` (or `Start-Monitor.bat`).
3. Right-click the widget and tick **Start with Windows** if you want it on every boot.

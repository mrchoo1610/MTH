# Taskbar Hardware Monitor (for 3D Artists & Developers)

A lightweight Windows desktop widget that displays real-time hardware telemetry directly on the **Windows Taskbar**, tailored specifically for **3D Rendering (Blender, Unreal Engine, Maya...)** and **Code Compilation**.

---

## 🚀 Key Features

* **Taskbar Docked Widget**: Sleek, dark frosted glass style, always-on-top, non-intrusive.
* **Optimized for 3D Render & Heavy Builds**:
  * **CPU**: Multi-core load percentage and CPU package temperature.
  * **RAM**: Physical memory usage (`Used / Total GB`) and percentage (`%`).
  * **GPU (NVIDIA)**: Real-time 3D / CUDA Engine load and GPU temperature.
  * **VRAM**: GPU Video RAM (`Used / Total GB`) — **crucial metric to prevent Out-of-Memory crashes during 3D rendering**.
  * **Disk**: Real-time read/write throughput (MB/s).
* **Smart Color Alerts**:
  * Normal (< 75%): Crisp White / Subtle Accent
  * Warning (75% - 89%): Amber Yellow
  * Critical (≥ 90% or Temp > 82°C): Bright Red alert
* **Mouse Interactions**:
  * **Drag & Drop**: Left-click and drag the strip to position it anywhere on the Taskbar or screen.
  * **Left-Click**: Toggles the detailed **Flyout card** showing hardware breakdown and **Top memory-consuming processes** (Blender, Unreal, VS Code, Chrome...).
  * **Right-Click**: Full Context Menu:
    * 📌 Snap to Taskbar (Bottom-Right next to notification area)
    * 📌 Reset Position
    * 🔒 Lock Position (prevents accidental dragging)
    * 👁 Show Metrics (Toggle CPU, RAM, GPU, VRAM, Disk)
    * 🌓 Opacity (60%, 75%, 90%, 100%)
    * ⏱ Refresh Rate (500ms, 1000ms, 2000ms)
    * 🚀 Start with Windows
    * 🛡 Run as Administrator (unlocks deep hardware & package sensors)
    * 📊 Open Task Manager
    * ❌ Exit
* **System Tray Integration**: Minimize to notification area anytime.
* **Ultra-Lightweight**: Built with native C# .NET WPF, consumes only ~30-50MB RAM and < 0.1% CPU.

---

## 📦 How to Run

1. **Quick Start**: Run [`Start-Monitor.bat`](./Start-Monitor.bat).
2. Or run directly: [`dist/TaskbarMonitor.exe`](./dist/TaskbarMonitor.exe).
3. **Auto-Start**: Right-click the widget and check **"🚀 Start with Windows"**.

---

## Auto-update (v1.1)

* Tray menu has **"Kiểm tra cập nhật…"**; the app also checks in the background 10 seconds after start (at most once every 24 hours) and shows a tray balloon when a new version exists.
* Updates are installed only if the release **signature (ECDSA P-256) and SHA-256 are valid**. `MiniTools.UpdateHelper.exe` then replaces the files (with backup + rollback; your `settings.json` is never deleted) and restarts the app.
* Hub URL: `UpdateConfig.HubBaseUrl` in `Services/UpdateConfig.cs` (default `https://mth.mimehub.top`, app id `taskbar-monitor`).
* The public key lives in `Shared/MiniTools.Updater/PublicKey.cs`. While it is the empty placeholder, **updates are disabled** until `scripts/new-signing-key.ps1` is run and the app is rebuilt.

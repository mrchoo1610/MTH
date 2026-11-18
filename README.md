# Mini Tools Workspace

Workspace chứa bộ sưu tập các ứng dụng và tiện ích nhỏ (Indie Desktop Mini Tools) phục vụ công việc lập trình, đồ họa 3D, tối ưu hóa hệ thống.

---

## 📂 Danh sách các Mini App

### 1. [Taskbar Hardware Monitor (v1.0.0)](./TaskbarMonitor)
* **Thư mục dự án**: [`TaskbarMonitor/`](./TaskbarMonitor)
* **Bản Portable sẵn sàng sử dụng**:
  * Thư mục Portable: [`releases/TaskbarMonitor-v1.0.0-Portable/`](./releases/TaskbarMonitor-v1.0.0-Portable/)
  * File nén ZIP: [`releases/TaskbarMonitor-v1.0.0-Portable.zip`](./releases/TaskbarMonitor-v1.0.0-Portable.zip) *(chỉ ~2.4 MB, copy vào USB chạy ngay mọi nơi)*
* **Mục đích**: Hiển thị thông số phần cứng thời gian thực trực tiếp trên thanh Taskbar (CPU, RAM, GPU NVIDIA, VRAM, Disk) phục vụ Render 3D & Build code.
* **Chạy nhanh**: Mở [`Start-Monitor.bat`](./TaskbarMonitor/Start-Monitor.bat) hoặc [`dist/TaskbarMonitor.exe`](./TaskbarMonitor/dist/TaskbarMonitor.exe).
* **Công nghệ**: C# .NET 10 WPF, LibreHardwareMonitorLib.

### 2. [Disk Cleaner (v1.1.0)](./DiskCleaner)
* **Thư mục dự án**: [`DiskCleaner/`](./DiskCleaner)
* **Bản Portable**:
  * Thư mục Portable: [`releases/DiskCleaner-v1.1.0-Portable/`](./releases/DiskCleaner-v1.1.0-Portable/)
  * File nén ZIP: [`releases/DiskCleaner-v1.1.0-Portable.zip`](./releases/DiskCleaner-v1.1.0-Portable.zip)
* **Mục đích**: Phân tích và dọn dẹp ổ C an toàn (temp, cache trình duyệt, cache dev tools), xem thư mục chiếm nhiều dung lượng, tìm file lớn và chuyển vào Thùng rác. Chỉ xóa theo whitelist, luôn quét thử (dry-run) trước.
* **Chạy nhanh**: `DiskCleaner.exe` trong bản Portable (cần .NET 10 Desktop Runtime), hoặc `dotnet run --project DiskCleaner/src/DiskCleaner`.
* **Công nghệ**: C# .NET 10 WPF (MVVM, không thư viện ngoài), xUnit.

### 3. [MiniToolsHub](./MiniToolsHub)
* **Thư mục dự án**: [`MiniToolsHub/`](./MiniToolsHub), kèm [`Shared/MiniTools.Contracts`](./Shared/MiniTools.Contracts) (model manifest + ký/kiểm chữ ký ECDSA).
* **Mục đích**: Website song ngữ VI/EN để giới thiệu và tải các mini tool, đồng thời là máy chủ cập nhật (manifest ký số, tải file, đếm lượt tải). Chạy Docker trên VPS, public qua Cloudflare Tunnel tại `https://mth.mimehub.top`.
* **Chạy local**: `dotnet run --project MiniToolsHub/src/MiniToolsHub.Server` rồi mở `http://localhost:18473`.
* **Phát hành bản mới**: `scripts/new-signing-key.ps1` (1 lần) rồi `scripts/release.ps1 -App DiskCleaner`. Chi tiết trong [`MiniToolsHub/README.md`](./MiniToolsHub/README.md).
* **Công nghệ**: ASP.NET Core 10 (Razor Pages + Minimal API), SQLite, Docker, Cloudflare Tunnel, xUnit.

---

## 🚀 CI/CD (GitHub Actions)

* **CI** ([`ci.yml`](./.github/workflows/ci.yml)): build + test toàn bộ solution trên mọi push/PR.
* **Deploy hub** ([`deploy-hub.yml`](./.github/workflows/deploy-hub.yml)): push `main` → test → image GHCR → deploy trên VPS qua self-hosted runner.
* **Release app** ([`release-app.yml`](./.github/workflows/release-app.yml)): push tag `disk-cleaner-vX.Y.Z` / `taskbar-monitor-vX.Y.Z` → build, ký, GitHub Release, upload lên hub.
* Hướng dẫn thiết lập (secrets, environments, runner, rollback): [`docs/ci-cd.md`](./docs/ci-cd.md).

---

## 🛠️ Cấu trúc thư mục

```text
mini-tool-indie/
├── MiniTools.slnx               # Solution quản lý toàn bộ các dự án
├── README.md                    # Mục lục các công cụ
├── .gitignore
├── scripts/                     # new-signing-key.ps1, release.ps1, tools/ManifestTool
├── Shared/                      # MiniTools.Contracts (+ tests); phase B: Updater, UpdateHelper
├── MiniToolsHub/                # [App 3] Website + update server (Docker)
├── releases/                    # Thư mục chứa các bản build đóng gói phân phối
│   ├── TaskbarMonitor-v1.0.0-Portable/    # Bản portable giải nén sẵn
│   ├── TaskbarMonitor-v1.0.0-Portable.zip # File nén zip mang đi
│   ├── DiskCleaner-v1.1.0-Portable/       # [App 2] Bản portable Disk Cleaner
│   └── DiskCleaner-v1.1.0-Portable.zip
│
├── TaskbarMonitor/              # [App 1] Taskbar Hardware Monitor
│   ├── dist/                    # Bản build chạy trực tiếp (.exe)
│   ├── Models/
│   ├── Services/
│   ├── MainWindow.xaml
│   ├── Start-Monitor.bat
│   └── README.md
├── DiskCleaner/                 # [App 2] Disk Cleaner
│   ├── src/DiskCleaner/         # Core/, Services/, Rules/, UI/
│   ├── tests/DiskCleaner.Tests/ # xUnit
│   ├── scripts/publish.ps1      # Build + đóng gói portable
│   ├── PLAN.md
│   └── README.md
└── [App-Tiep-Theo]/             # Các app mini tiếp theo sẽ được tạo ở đây
```

# DiskCleaner — PLAN (v1.0 MVP)

App desktop nhỏ để **phân tích và dọn dẹp ổ C** an toàn. Đây là app thứ 2 trong workspace `mini-tool-indie`, cùng stack với `TaskbarMonitor`.

> Nguyên tắc số 1: **không bao giờ xóa nhầm**. Mọi thao tác xóa phải đi qua `SafetyGuard`, chỉ xóa trong whitelist, mặc định là dry-run (quét thử).

---

## 1. Tech stack

| Thành phần | Lựa chọn |
|---|---|
| Runtime | .NET 10 (`net10.0-windows`), SDK 10.0.302 đã cài |
| UI | WPF, mô hình MVVM, **không dùng thư viện MVVM ngoài** (tự viết `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`) |
| Test | xUnit (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) |
| JSON | `System.Text.Json` |
| Recycle Bin | P/Invoke `SHFileOperation` (gửi file vào thùng rác) và `SHEmptyRecycleBin` / `SHQueryRecycleBin` |
| Không dùng | NuGet bên thứ ba cho app chính (giữ app nhẹ, dễ đóng gói portable) |

---

## 2. Cấu trúc thư mục

```text
DiskCleaner/
├── PLAN.md
├── README.md
├── src/DiskCleaner/
│   ├── DiskCleaner.csproj
│   ├── app.manifest                 # asInvoker (KHÔNG requireAdministrator)
│   ├── App.xaml / App.xaml.cs       # parse args: --elevated-clean <jobFile>
│   ├── Core/                        # logic thuần, KHÔNG I/O, test 100%
│   │   ├── Models/
│   │   │   ├── CleanRule.cs
│   │   │   ├── RuleCategory.cs
│   │   │   ├── ScanItemResult.cs
│   │   │   ├── CleanReport.cs
│   │   │   ├── DriveSnapshot.cs
│   │   │   ├── FolderNode.cs
│   │   │   └── LargeFileEntry.cs
│   │   ├── SafetyGuard.cs
│   │   ├── PathExpander.cs
│   │   ├── FileFilter.cs
│   │   ├── SizeFormatter.cs
│   │   └── RuleValidator.cs
│   ├── Services/                    # I/O
│   │   ├── RuleRepository.cs        # đọc Rules/rules.json
│   │   ├── DriveInfoService.cs
│   │   ├── FileSystemScanner.cs     # quét kích thước theo rule
│   │   ├── FolderAnalyzer.cs        # cây thư mục
│   │   ├── LargeFileFinder.cs
│   │   ├── CleanExecutor.cs         # xóa thật, gọi SafetyGuard
│   │   ├── CommandRunner.cs         # chạy npm/dotnet/docker...
│   │   ├── RecycleBinService.cs     # P/Invoke
│   │   ├── ElevationService.cs      # relaunch runas
│   │   └── Logger.cs                # %LOCALAPPDATA%\DiskCleaner\logs
│   ├── Rules/
│   │   └── rules.json               # CopyToOutputDirectory=PreserveNewest
│   └── UI/
│       ├── Mvvm/ (ObservableObject.cs, RelayCommand.cs, AsyncRelayCommand.cs)
│       ├── MainWindow.xaml(.cs)     # sidebar + 4 tab
│       ├── Views/ (OverviewView, CleanView, AnalyzerView, LargeFilesView).xaml
│       ├── ViewModels/ (MainViewModel, OverviewViewModel, CleanViewModel, AnalyzerViewModel, LargeFilesViewModel).cs
│       └── Converters/ (BytesToStringConverter, PercentToBrushConverter).cs
└── tests/DiskCleaner.Tests/
    ├── DiskCleaner.Tests.csproj
    ├── Core/ (SafetyGuardTests, PathExpanderTests, FileFilterTests, SizeFormatterTests, RuleValidatorTests)
    ├── Services/ (FileSystemScannerTests, CleanExecutorTests, FolderAnalyzerTests, LargeFileFinderTests, RuleRepositoryTests)
    └── TestHelpers/TempTree.cs      # tạo cây thư mục tạm, tự xóa khi Dispose
```

Cập nhật thêm:
- `MiniTools.slnx`: thêm 2 project `DiskCleaner/src/DiskCleaner/DiskCleaner.csproj` và `DiskCleaner/tests/DiskCleaner.Tests/DiskCleaner.Tests.csproj`.
- `README.md` gốc: thêm mục "2. DiskCleaner".
- Thêm `.gitignore` trong `DiskCleaner/` cho `bin/ obj/ dist/`.

---

## 3. Data model (Core/Models)

```csharp
public enum RuleCategory { System, Browser, Developer }

public enum RuleAction
{
    DeleteFiles,        // xóa file bên trong Paths (giữ lại thư mục gốc)
    EmptyRecycleBin,    // SHEmptyRecycleBin
    RunCommand          // chạy Command, đo dung lượng trước/sau bằng Paths
}

public sealed record CleanRule
{
    public required string Id { get; init; }            // "user-temp"
    public required string Name { get; init; }          // "Temp của người dùng"
    public required string Description { get; init; }
    public required RuleCategory Category { get; init; }
    public required RuleAction Action { get; init; }
    public string[] Paths { get; init; } = [];          // có thể dùng %TEMP%, %LOCALAPPDATA%, * (wildcard 1 cấp)
    public int MinAgeHours { get; init; } = 0;          // chỉ xóa file cũ hơn N giờ
    public bool RequiresAdmin { get; init; }
    public bool DefaultSelected { get; init; }
    public string? Command { get; init; }               // vd "npm"
    public string? CommandArgs { get; init; }           // vd "cache clean --force"
    public string? Warning { get; init; }               // hiện màu vàng trên UI
}

public sealed record ScanItemResult(
    string RuleId, long Bytes, int FileCount, bool Available, string? Error);
    // Available=false khi path không tồn tại hoặc command không có trên PATH

public sealed record CleanReport(
    string RuleId, long BytesFreed, int FilesDeleted, int FilesSkipped,
    IReadOnlyList<string> Errors, TimeSpan Duration);

public sealed record DriveSnapshot(
    string Name, string Label, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
    public DriveHealth Health => (FreeBytes * 100.0 / Math.Max(TotalBytes, 1)) switch
        { < 5 => DriveHealth.Critical, < 15 => DriveHealth.Warning, _ => DriveHealth.Ok };
}
public enum DriveHealth { Ok, Warning, Critical }

public sealed class FolderNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsAccessDenied { get; set; }
    public List<FolderNode> Children { get; } = [];
}

public sealed record LargeFileEntry(string FullPath, long SizeBytes, DateTime LastWriteUtc);
```

---

## 4. Rules (Rules/rules.json) — whitelist MVP

| Id | Category | Action | Paths / Command | MinAge | Admin | Default |
|---|---|---|---|---|---|---|
| `user-temp` | System | DeleteFiles | `%TEMP%` | 24h | no | ✔ |
| `windows-temp` | System | DeleteFiles | `%WINDIR%\Temp` | 24h | **yes** | ✔ |
| `wu-cache` | System | DeleteFiles | `%WINDIR%\SoftwareDistribution\Download` | 0 | **yes** | ☐ |
| `recycle-bin` | System | EmptyRecycleBin | — | — | no | ☐ |
| `crash-dumps` | System | DeleteFiles | `%LOCALAPPDATA%\CrashDumps` | 0 | no | ✔ |
| `thumb-cache` | System | DeleteFiles | `%LOCALAPPDATA%\Microsoft\Windows\Explorer` (chỉ `thumbcache_*.db`, `iconcache_*.db`) | 0 | no | ☐ |
| `chrome-cache` | Browser | DeleteFiles | `%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache`, `...\*\Code Cache`, `...\*\GPUCache` | 0 | no | ✔ |
| `edge-cache` | Browser | DeleteFiles | tương tự với `Microsoft\Edge\User Data\*\...` | 0 | no | ✔ |
| `npm-cache` | Developer | RunCommand | `npm cache clean --force`; đo `%LOCALAPPDATA%\npm-cache` | — | no | ☐ |
| `nuget-cache` | Developer | RunCommand | `dotnet nuget locals all --clear`; đo `%USERPROFILE%\.nuget\packages` | — | no | ☐ |
| `pip-cache` | Developer | RunCommand | `pip cache purge`; đo `%LOCALAPPDATA%\pip\Cache` | — | no | ☐ |
| `yarn-cache` | Developer | RunCommand | `yarn cache clean`; đo `%LOCALAPPDATA%\Yarn\Cache` | — | no | ☐ |
| `gradle-cache` | Developer | DeleteFiles | `%USERPROFILE%\.gradle\caches` | 720h | no | ☐ |
| `docker-prune` | Developer | RunCommand | `docker system prune -f`; đo `%LOCALAPPDATA%\Docker` | — | no | ☐ |

`Warning` bắt buộc cho: `nuget-cache` ("Lần build sau sẽ phải tải lại package"), `docker-prune` ("Xóa container đã dừng, image và network không dùng. File vhdx KHÔNG tự nhỏ lại, xem README"), `wu-cache` ("Nên dừng dịch vụ Windows Update trước").

Một rule chỉ là dữ liệu. Thêm rule mới **không cần sửa code**. `RuleValidator` từ chối mọi rule có path rơi vào danh sách cấm (mục 5).

---

## 5. An toàn — SafetyGuard (Core, thuần)

```csharp
public static class SafetyGuard
{
    // Không bao giờ được xóa trong các thư mục này, kể cả khi rule trỏ tới.
    public static IReadOnlyList<string> ForbiddenRoots(IEnvironment env);
    //  %WINDIR% (trừ %WINDIR%\Temp và %WINDIR%\SoftwareDistribution\Download),
    //  %PROGRAMFILES%, %PROGRAMFILES(X86)%, %PROGRAMDATA% (trừ khi rule cho phép rõ ràng — MVP: cấm hết),
    //  %USERPROFILE%\Documents, Desktop, Pictures, Videos, Music, OneDrive*,
    //  gốc ổ đĩa (vd "C:\"), %USERPROFILE% (chính nó)

    /// true nếu target nằm BÊN TRONG (không bằng) một allowed root và không thuộc forbidden.
    public static bool IsSafeToDelete(string targetPath, IEnumerable<string> allowedRoots,
                                      IReadOnlyList<string> forbiddenRoots);
}
```

Quy tắc:
1. Chuẩn hóa bằng `Path.GetFullPath`, so sánh `OrdinalIgnoreCase`, so sánh theo ranh giới thư mục (thêm `\` cuối) để `C:\Temp2` không bị coi là nằm trong `C:\Temp`.
2. Target phải nằm **bên trong** allowed root. Không xóa chính allowed root.
3. Từ chối path có `..` sau khi chuẩn hóa vẫn thoát ra ngoài, UNC path (`\\`), và path tương đối.
4. `IEnvironment` là interface để test có thể giả lập biến môi trường.

Quy tắc ở CleanExecutor:
- Duyệt bằng `Directory.EnumerateFileSystemEntries` với `EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0 }`. Tự đệ quy bằng tay để kiểm soát reparse point.
- **Gặp reparse point (junction/symlink) thì không đi vào bên trong và không xóa nội dung đích.** Chỉ xóa chính link nếu nó nằm trong allowed root.
- Bỏ qua file có attribute `RecallOnDataAccess (0x400000)` hoặc `Offline`, để không kích hoạt OneDrive tải file về.
- `MinAgeHours` so với `LastWriteTimeUtc`.
- File đang bị khóa (`IOException`) hoặc không có quyền (`UnauthorizedAccessException`) thì tính vào `FilesSkipped`, ghi log, không throw.
- Xóa file trước, sau đó xóa thư mục con rỗng (từ dưới lên). Không xóa thư mục gốc của rule.
- Hỗ trợ `CancellationToken` và `IProgress<CleanProgress>`.
- Mọi file đã xóa được ghi vào log: `%LOCALAPPDATA%\DiskCleaner\logs\clean-yyyyMMdd-HHmmss.log`.

---

## 6. Module API (Services)

```csharp
public interface IEnvironment { string? Get(string name); }      // impl: Environment.GetEnvironmentVariable

public static class PathExpander                                  // Core
{
    // Mở rộng %VAR% và wildcard '*' ở 1 cấp thư mục; trả về các path TỒN TẠI.
    public static IReadOnlyList<string> Expand(string pattern, IEnvironment env, Func<string, IEnumerable<string>> listDirs);
}

public static class FileFilter                                    // Core
{
    public static bool ShouldDelete(FileAttributes attrs, DateTime lastWriteUtc, DateTime nowUtc, int minAgeHours, string? namePattern, string fileName);
}

public static class SizeFormatter { public static string Format(long bytes); } // "1.23 GB", "512 KB", "0 B"

public sealed class RuleRepository
{
    public RuleRepository(string jsonPath);
    public IReadOnlyList<CleanRule> LoadAll();        // validate qua RuleValidator; rule lỗi thì bỏ + log
}

public sealed class DriveInfoService
{
    public IReadOnlyList<DriveSnapshot> GetFixedDrives();
}

public sealed class FileSystemScanner
{
    public Task<ScanItemResult> ScanAsync(CleanRule rule, CancellationToken ct);
    public Task<IReadOnlyList<ScanItemResult>> ScanAllAsync(IEnumerable<CleanRule> rules, IProgress<string>? progress, CancellationToken ct);
    // Chỉ đếm file thỏa FileFilter (cùng điều kiện với Clean) → số liệu dry-run khớp với số liệu xóa thật.
}

public sealed class CleanExecutor
{
    public CleanExecutor(SafetyGuardContext guard, ICommandRunner runner, IRecycleBinService recycleBin, ILogger log);
    public Task<CleanReport> CleanAsync(CleanRule rule, IProgress<CleanProgress>? progress, CancellationToken ct);
}

public interface ICommandRunner
{
    bool IsAvailable(string command);                                   // tìm trên PATH (.exe, .cmd, .ps1)
    Task<(int ExitCode, string Output)> RunAsync(string command, string args, TimeSpan timeout, CancellationToken ct);
    // Chạy ẩn cửa sổ, timeout mặc định 5 phút, npm/yarn chạy qua "cmd /c".
}

public interface IRecycleBinService
{
    (long Bytes, long Items) Query();                                   // SHQueryRecycleBin
    void Empty();                                                       // SHEmptyRecycleBin, flags NOCONFIRMATION|NOPROGRESSUI|NOSOUND
    void SendToRecycleBin(IEnumerable<string> paths);                   // SHFileOperation FO_DELETE + FOF_ALLOWUNDO
}

public sealed class FolderAnalyzer
{
    // Quét cây thư mục, tính size gộp từ dưới lên. Không đi vào reparse point.
    // Song song ở cấp con thứ nhất (Parallel.ForEachAsync, MaxDegreeOfParallelism = Environment.ProcessorCount).
    public Task<FolderNode> AnalyzeAsync(string rootPath, IProgress<AnalyzeProgress>? progress, CancellationToken ct);
}

public sealed class LargeFileFinder
{
    public Task<IReadOnlyList<LargeFileEntry>> FindAsync(string root, long minBytes, int maxResults, CancellationToken ct);
    // Dùng min-heap (PriorityQueue) giữ top N, không giữ hết file trong bộ nhớ.
}

public sealed class ElevationService
{
    public bool IsElevated { get; }
    // Ghi danh sách ruleId vào file job JSON trong %TEMP%\DiskCleaner, chạy lại chính exe với
    // Verb="runas", args: --elevated-clean "<jobFile>". Chờ process kết thúc, đọc file report JSON trả về.
    public Task<IReadOnlyList<CleanReport>?> RunElevatedCleanAsync(IEnumerable<string> ruleIds, CancellationToken ct);
    // Trả null nếu user bấm "No" ở UAC (Win32Exception 1223).
}
```

Chế độ elevated (`App.xaml.cs`): khi có `--elevated-clean <jobFile>`, app **không mở cửa sổ**. Nó đọc job, chỉ chạy các rule có `RequiresAdmin=true` và có Id nằm trong `rules.json` (không tin bất kỳ path nào trong job file), ghi report ra `<jobFile>.result.json`, rồi thoát.

---

## 7. UI

Cửa sổ 1100×720 (min 900×600), dark theme giống TaskbarMonitor. Sidebar trái gồm 4 mục:

**1. Overview**
- Card cho mỗi ổ cố định: tên, thanh % đã dùng (xanh dưới 85%, vàng 85–95%, đỏ trên 95%), Used/Free/Total.
- Nút "Quét nhanh" chạy `ScanAllAsync` với các rule mặc định, sau đó hiện "Có thể giải phóng ~X GB" và nút đi tới tab Clean.

**2. Clean** (màn hình chính)
- Nhóm theo Category (System / Browser / Developer). Mỗi rule là 1 dòng: checkbox, tên, mô tả, dung lượng quét được, icon 🛡 nếu cần admin, dòng cảnh báo màu vàng nếu có `Warning`. Rule `Available=false` thì mờ đi, không chọn được.
- Nút **Quét** (dry-run) → nút **Dọn dẹp (X GB)** chỉ bật sau khi đã quét.
- Bấm Dọn dẹp thì hiện dialog xác nhận, liệt kê rule và tổng dung lượng. Nếu có rule admin, ghi rõ "Sẽ hiện hộp thoại UAC".
- Khi chạy: progress bar, tên rule đang chạy, nút Hủy.
- Xong: bảng kết quả theo từng rule (đã giải phóng, số file xóa, số file bỏ qua), link "Mở log". Overview tự cập nhật free space.

**3. Analyzer**
- Chọn ổ hoặc thư mục (mặc định `C:\`), bấm Quét.
- `TreeView` dạng lazy (chỉ tạo node con khi mở rộng), các cột: Tên | Kích thước | % so với cha (thanh nhỏ) | Số file. Con sắp xếp giảm dần theo size.
- Chuột phải: "Mở trong Explorer", "Copy đường dẫn". **Không có chức năng xóa trong Analyzer ở MVP.**
- Node bị từ chối quyền thì hiện icon 🔒.

**4. Large Files**
- Ô nhập: thư mục gốc (mặc định `%USERPROFILE%`), ngưỡng tối thiểu (mặc định 500 MB), số kết quả tối đa (mặc định 200).
- `DataGrid`: chọn nhiều | Đường dẫn | Kích thước | Ngày sửa đổi. Sắp xếp được theo từng cột.
- Hành động: "Mở thư mục", "**Chuyển vào Thùng rác**" (dùng `SendToRecycleBin`, có xác nhận). Không có tùy chọn xóa vĩnh viễn.

Yêu cầu chung: mọi thao tác dài đều chạy async, UI không bị đơ, đều có nút Hủy. Exception chưa xử lý thì hiện dialog lỗi thân thiện và ghi log.

---

## 8. Tests (xUnit)

Mọi test dùng `TempTree` (tạo trong `Path.GetTempPath()/DiskCleanerTests/<guid>`) và **không bao giờ đụng vào thư mục thật của user**.

| Test class | Các case bắt buộc |
|---|---|
| `SafetyGuardTests` | file trong allowed root → true; chính allowed root → false; `C:\Temp2` vs root `C:\Temp` → false; path có `..` thoát ra → false; `C:\Windows\System32\x` → false; gốc ổ → false; UNC → false; khác hoa thường vẫn nhận đúng |
| `PathExpanderTests` | mở rộng `%TEMP%`; biến không tồn tại → rỗng; wildcard `User Data\*\Cache` khớp nhiều profile |
| `FileFilterTests` | MinAge: file mới bị bỏ qua, file cũ được xóa; attribute ReparsePoint / RecallOnDataAccess / Offline → bỏ qua; namePattern `thumbcache_*.db` |
| `SizeFormatterTests` | 0, 1023, 1024, 1.5 GB, long.MaxValue |
| `RuleValidatorTests` | rule có path cấm → bị loại; Id trùng → bị loại; RunCommand thiếu Command → bị loại |
| `RuleRepositoryTests` | load `rules.json` thật trong output → 14 rule hợp lệ, không rule nào bị loại |
| `FileSystemScannerTests` | tổng bytes và số file đúng; tôn trọng MinAge; path không tồn tại → Available=false |
| `CleanExecutorTests` | xóa đúng file, giữ thư mục gốc; xóa thư mục con rỗng; **junction trỏ ra ngoài root: file đích còn nguyên** (tạo junction bằng `cmd /c mklink /J`, skip test nếu tạo thất bại); file bị khóa (mở `FileStream` với `FileShare.None`) → Skipped, không throw; số bytes giải phóng bằng số bytes đã quét; Cancel giữa chừng → dừng |
| `FolderAnalyzerTests` | size gộp đúng; con được sắp xếp giảm dần; không đi theo junction |
| `LargeFileFinderTests` | trả top N đúng thứ tự; tôn trọng minBytes |

Chạy: `dotnet test DiskCleaner/tests/DiskCleaner.Tests`.

---

## 9. Build & đóng gói

- `scripts/publish.ps1` (trong `DiskCleaner/scripts/`): `dotnet publish src/DiskCleaner -c Release -r win-x64 --self-contained false -o dist`, sau đó copy sang `../releases/DiskCleaner-v1.0.0-Portable/` và nén zip.
- csproj: `Version 1.0.0`, `Product "Disk Cleaner"`, `Company IndieMiniTools`, `ApplicationManifest app.manifest` (asInvoker).

---

## 10. Ngoài phạm vi MVP (để v1.1+)

- Move & Link: chuyển thư mục sang ổ khác và tạo junction; đặt biến `NUGET_PACKAGES`.
- Compact file vhdx của Docker/WSL.
- Tìm `node_modules` / `bin` / `obj` cũ.
- DISM `/StartComponentCleanup`, `powercfg -h off`, xóa `Windows.old`.
- Tìm file trùng lặp, treemap, quét qua MFT, lên lịch dọn tự động.

---

## 11. Definition of Done

- [ ] `dotnet build MiniTools.slnx` không có lỗi, không có warning mới về nullable.
- [ ] `dotnet test` pass 100%, có đủ các case ở mục 8.
- [ ] Mọi đường xóa đều đi qua `SafetyGuard.IsSafeToDelete` (kiểm tra bằng cách grep `File.Delete` / `Directory.Delete` chỉ xuất hiện trong `CleanExecutor`).
- [ ] App mở được không cần quyền admin; tab Overview hiện đúng free space của ổ C (khớp Explorer).
- [ ] Bấm Quét trên tab Clean ra số liệu, không crash khi gặp thư mục bị từ chối quyền.
- [ ] Rule admin bật UAC; bấm "No" ở UAC thì app báo hủy, không crash.
- [ ] Analyzer quét xong `C:\` mà không treo UI và có thể hủy.
- [ ] Large Files chỉ chuyển vào Thùng rác, không xóa vĩnh viễn.
- [ ] Có log cho mỗi lần dọn dẹp.
- [ ] `README.md` của app: cách chạy, giải thích từng rule, cảnh báo Docker vhdx; README gốc được cập nhật.
- [ ] Bản portable trong `releases/`.

---

## 12. v1.1 — Giao diện hiện đại + Song ngữ EN/VI

### 12.1 Song ngữ (i18n)
- `src/DiskCleaner/UI/Localization/Strings.vi.xaml` và `Strings.en.xaml`: ResourceDictionary chứa `sys:String` theo key (vd `Nav.Overview`, `Clean.ScanButton`, `Clean.CleanButtonFormat` = "Dọn dẹp ({0})"). **Mọi text hiển thị trong XAML dùng `{DynamicResource Key}`**, không hardcode.
- `Services/LocalizationService.cs`:
  ```csharp
  public enum AppLanguage { Vi, En }
  public sealed class LocalizationService
  {
      public AppLanguage Current { get; }
      public event EventHandler? LanguageChanged;
      public void SetLanguage(AppLanguage lang);   // thay merged dictionary trong Application.Resources, lưu settings, raise event
      public string Get(string key);               // cho ViewModel; thiếu key → trả "[key]"
      public string Format(string key, params object[] args);
  }
  ```
- `Services/SettingsService.cs`: `%LOCALAPPDATA%\DiskCleaner\settings.json` lưu `{ "language": "vi" }`. Lần đầu: `CultureInfo.CurrentUICulture` là `vi` thì dùng `Vi`, còn lại `En`.
- ViewModel không lưu chuỗi đã dịch dạng cứng: lưu trạng thái (enum/key + tham số) và expose property text tính lại; khi `LanguageChanged` thì raise `PropertyChanged` cho các property đó → đổi ngôn ngữ **ngay lập tức, không cần khởi động lại**, kể cả kết quả quét đang hiện.
- `rules.json`: thêm `nameEn`, `descriptionEn`, `warningEn` cho cả 14 rule (giữ `name`, `description`, `warning` là tiếng Việt). `CleanRule` thêm 3 field optional và helper `GetName(AppLanguage)`, `GetDescription(...)`, `GetWarning(...)` (fallback về tiếng Việt nếu thiếu).
- Dialog xác nhận / lỗi (`DialogService`) và log message hiển thị cho user cũng dịch. File log nội bộ giữ nguyên.
- Nút chuyển ngôn ngữ: segmented control `VI | EN` ở cuối sidebar, cạnh số phiên bản.
- Test: `LocalizationTests` — hai file `.xaml` có **cùng tập key** (parse XAML bằng `XDocument`, so sánh key), không key nào rỗng; mọi rule trong `rules.json` có `nameEn`, `descriptionEn`; rule có `warning` thì có `warningEn`.

### 12.2 Giao diện hiện đại (dark, Fluent-style, chạy tốt trên Windows 10)
**Design tokens** — `UI/Themes/Tokens.xaml` (Color + SolidColorBrush), mọi view chỉ dùng token:

| Token | Giá trị |
|---|---|
| Bg | `#0E1014` |
| Surface | `#161920` |
| Surface2 | `#1D212A` |
| SurfaceHover | `#242934` |
| Border | `#2A303C` |
| Text | `#E8EAF0` |
| TextMuted | `#8B93A7` |
| Accent / AccentHover | `#6D5EF6` / `#8174FF` |
| AccentGradient | LinearGradient `#6D5EF6 → #A35BF0` (trái → phải) |
| Success / Warning / Danger | `#22C55E` / `#F5A524` / `#EF4444` |
| Radius | card 14, button 10, pill 999 |

- Font `Segoe UI Variable Display`, fallback `Segoe UI`. Heading 26 SemiBold, subtitle 13 TextMuted, body 14.
- Icon: font `Segoe MDL2 Assets` (có sẵn trên Win10; dùng `Segoe Fluent Icons` nếu có). **Không dùng emoji** làm icon.
- `UI/Themes/Controls.xaml`: style cho Button (Primary gradient / Secondary / Ghost / Danger), ToggleSwitch (style trên `CheckBox`, pill 40×22, knob animate), ScrollBar mảnh 8px bo tròn tự hiện khi hover, TextBox, ComboBox, DataGrid, TreeView, ProgressBar bo tròn, ToolTip, ContextMenu — tất cả dark. Hover/press dùng `ColorAnimation` 120ms.
- **Cửa sổ**: `WindowChrome` (CaptionHeight 40, ResizeBorder 6) với title bar tự vẽ cùng màu Bg, nút Min/Max/Close kiểu Fluent (Close hover đỏ `#E81123`). Kéo được, double-click để maximize, giữ snap của Windows. Bật dark title bar qua `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE=20)` cho viền/taskbar preview, bỏ qua lỗi nếu OS không hỗ trợ.
- Card: Surface, viền 1px Border, radius 14, `DropShadowEffect` (BlurRadius 24, Opacity 0.25, ShadowDepth 4).
- **Sidebar**: rộng 240, logo icon gradient + tên app, nav item có icon MDL2, item đang chọn có nền SurfaceHover + thanh accent 3px bên trái. Cuối sidebar: toggle `VI | EN` + version.
- **Overview**:
  - Hero card ổ C: **biểu đồ donut** (vẽ bằng `Path` + `ArcSegment`, dày 14, màu theo Health) bên trái với % ở giữa; bên phải 3 stat (Đã dùng / Còn trống / Tổng).
  - Ổ khác: card nhỏ hơn với thanh progress bo tròn.
  - Card "Tiềm năng giải phóng": nền AccentGradient, số GB lớn (32 SemiBold), nút "Quét nhanh" / "Xem chi tiết" → tab Dọn dẹp.
- **Clean**:
  - Header có tổng dung lượng **đã chọn** ở dạng chip.
  - Mỗi category là một section có tiêu đề + tổng dung lượng của category.
  - Mỗi rule là card hàng ngang: icon theo category trong ô tròn Surface2, tên + mô tả, warning hiện dạng badge vàng, **pill dung lượng** bên phải, icon khiên MDL2 nếu cần admin, **ToggleSwitch** thay checkbox. Card hover sáng nhẹ.
  - **Thanh hành động dính đáy** (sticky): trái "Đã chọn X mục · Y GB", phải nút `Quét` (Secondary) và `Dọn dẹp Y GB` (Primary gradient, disable nếu chưa quét). Khi chạy: progress bar bo tròn + text + nút Hủy thay vào chỗ đó.
  - Sau khi dọn: card kết quả với tổng đã giải phóng (Success) và bảng chi tiết.
- **Analyzer / Large Files**: toolbar trên cùng dạng card (input + nút), bảng/tree dùng style chung; mỗi dòng có thanh % nhỏ màu accent; dòng hover SurfaceHover; header cột TextMuted chữ nhỏ in hoa.
- Empty state cho mỗi tab (icon lớn mờ + 1 dòng hướng dẫn) thay vì để trống.
- Chuyển tab: fade + trượt nhẹ 8px trong 150ms.
- Kích thước: mặc định 1180×760, min 960×640. Layout không bị cắt ở min size và ở DPI 125%/150%.

### 12.3 Sửa lỗi phát hiện khi chạy thử v1.0
1. **Kết quả Quét nhanh ở Overview không được dùng ở tab Dọn dẹp** (tab Dọn dẹp vẫn hiện "—"). → Tạo `ScanResultStore` dùng chung (singleton trong `AppServices`), Overview và Clean cùng đọc/ghi; bấm "Xem chi tiết" sang tab Dọn dẹp thấy ngay số liệu.
2. **Rule cần admin hiện "0 B"** khi app không chạy admin (ví dụ Windows Temp không đọc được). → `ScanItemResult` thêm `bool AccessDenied`; UI hiện "Cần quyền admin để quét" / "Admin required to scan" thay vì 0 B.
3. Rule `Available=false` hiện chữ "Không khả dụng" kèm lý do (thư mục không tồn tại / lệnh không có trên PATH) trong tooltip.

### 12.4 Definition of Done v1.1
- [ ] Toàn bộ test cũ + `LocalizationTests` pass; build 0 warning.
- [ ] Không còn chuỗi hiển thị hardcode trong XAML/ViewModel (grep chữ có dấu tiếng Việt trong `UI/**/*.xaml` chỉ còn trong `Strings.vi.xaml`).
- [ ] Đổi VI ↔ EN cập nhật ngay mọi tab, lưu lại sau khi tắt mở app.
- [ ] Các mục sửa lỗi 12.3 hoạt động.
- [ ] Publish lại bản portable `releases/DiskCleaner-v1.1.0-Portable(.zip)`, version 1.1.0; README cập nhật (có mục ngôn ngữ).

---

## 13. v1.2 — Chọn folder ở Analyzer để đưa sang Dọn dẹp

Mục tiêu: trong tab Phân tích, user chọn một thư mục (vd `AppData\Local\CapCut\User Data\Cache` ~21 GB) rồi đưa sang tab Dọn dẹp. Thư mục trở thành **rule tùy chỉnh** được lưu lại, lần sau mở app vẫn còn.

### 13.1 Luồng
1. Analyzer: chọn node → nút **"Thêm vào Dọn dẹp" / "Add to Clean"** trên toolbar (bật khi có node hợp lệ được chọn) + mục tương ứng trong menu chuột phải.
2. Mở dialog tối màu (dùng `DialogWindow`) hiển thị: đường dẫn, dung lượng, số file, tên hiển thị (TextBox, mặc định `"<Thư mục cha>\<Tên>"`, vd `CapCut\Cache`), chọn "Chỉ xóa file cũ hơn" (ComboBox: 0 / 24h / 7 ngày / 30 ngày; mặc định 0). Nếu có cảnh báo (13.3) thì hiện badge vàng. Nút **Thêm** / **Hủy**.
3. Thêm xong → chuyển sang tab Dọn dẹp, rule mới nằm trong category mới **"Tùy chỉnh" / "Custom"** (đặt cuối danh sách), đã bật toggle, đã được quét luôn (gọi scan riêng rule đó, lưu vào `ScanResultStore`).
4. Card rule tùy chỉnh có thêm nút icon **xóa rule** (thùng rác MDL2, tooltip "Bỏ khỏi danh sách"/"Remove from list") — chỉ xóa rule, KHÔNG xóa file; có xác nhận.
5. Thư mục đã có trong rule (built-in hoặc custom, so sánh path chuẩn hóa) → nút chuyển thành "Đã có trong Dọn dẹp" (disabled).

### 13.2 Dữ liệu
- `CleanRule` thêm `bool IsCustom` (không có trong rules.json built-in). `RuleCategory` thêm `Custom`.
- `Services/CustomRuleStore.cs`: đọc/ghi `%LOCALAPPDATA%\DiskCleaner\custom-rules.json` (`{ "rules": [...] }`), API:
  ```csharp
  public sealed class CustomRuleStore
  {
      public CustomRuleStore(string filePath, IEnvironment env, ILogger log);
      public IReadOnlyList<CleanRule> LoadAll();                  // validate từng rule; rule lỗi → bỏ + log
      public CustomRuleCheck Check(string folderPath);            // xem 13.3
      public CleanRule Add(string folderPath, string displayName, int minAgeHours);  // throw nếu Check bị Blocked
      public void Remove(string ruleId);
      public event EventHandler? Changed;
  }
  ```
  Id: `custom-` + 8 ký tự hex đầu của SHA256(path chuẩn hóa lowercase). Action luôn = `DeleteFiles`, `RequiresAdmin=false`, Paths = **path tuyệt đối** (không dùng biến môi trường), Name = NameEn = displayName, Description = path.
- `RuleRepository` / `AppServices`: danh sách rule hiển thị = built-in + custom. Clean tab nghe `CustomRuleStore.Changed` để cập nhật.
- Chế độ elevated KHÔNG load custom rule (custom luôn chạy quyền thường).

### 13.3 An toàn — `CustomRuleCheck` (Core, thuần, test đầy đủ)
```csharp
public enum CustomRuleVerdict { Ok, Warn, Blocked }
public sealed record CustomRuleCheck(CustomRuleVerdict Verdict, IReadOnlyList<string> ReasonKeys); // key i18n
public static class CustomRulePolicy
{
    public static CustomRuleCheck Evaluate(string folderPath, IEnvironment env, IReadOnlyList<string> forbiddenRoots,
                                           IEnumerable<string> existingRulePaths, string appDataDir, string appExeDir);
}
```
**Blocked** (không cho thêm) khi:
- Path không tuyệt đối / UNC / là gốc ổ / thuộc `SafetyGuard` forbidden (`SafetyGuard.IsForbidden`).
- Path là **tổ tiên của** (hoặc bằng) bất kỳ forbidden root nào, hoặc tổ tiên của `%USERPROFILE%` (vd chọn `C:\Users`).
- Path là một trong các thư mục quá rộng hoặc tổ tiên của chúng: `%USERPROFILE%\AppData`, `AppData\Local`, `AppData\Roaming`, `AppData\LocalLow`, `AppData\Local\Programs`, `AppData\Local\Packages`, `AppData\Local\Microsoft`, `AppData\Roaming\Microsoft`, `%USERPROFILE%\Downloads`, `%USERPROFILE%\.ssh`, `%USERPROFILE%\.gnupg`, `%USERPROFILE%\source`, `%USERPROFILE%\.config`.
- Path là tổ tiên hoặc nằm trong thư mục dữ liệu của chính app (`%LOCALAPPDATA%\DiskCleaner`) hoặc thư mục chứa exe.
- Path là reparse point (không thêm junction).
- Path đã có trong rule khác (`AlreadyExists`).

**Warn** (cho thêm, hiện cảnh báo vàng) khi:
- Tên thư mục và các thư mục cha (tối đa 3 cấp) không chứa từ khóa cache/tạm: `cache`, `caches`, `temp`, `tmp`, `log`, `logs`, `crash`, `dump`, `dumps`, `thumbnails`, `shadercache`, `gpucache`, `code cache` (không phân biệt hoa thường) → key `Custom.Warn.NotCacheLike` ("Thư mục này không giống thư mục cache/tạm. Hãy chắc chắn dữ liệu bên trong có thể xóa.").
- Path nằm ngoài `%USERPROFILE%\AppData` và ngoài `%TEMP%` → key `Custom.Warn.OutsideAppData`.
- Chứa thư mục `.git` ở cấp 1 → key `Custom.Warn.LooksLikeProject`.

Xóa thật vẫn đi qua `CleanExecutor` + `SafetyGuard` như cũ; `CleanExecutor` gọi lại `CustomRulePolicy.Evaluate` cho rule `IsCustom` trước khi chạy và **dừng nếu Blocked** (phòng trường hợp file JSON bị sửa tay).

Dialog xác nhận khi bấm Dọn dẹp: nếu có rule Custom được chọn, liệt kê riêng các rule đó với đường dẫn đầy đủ và dòng "Xóa vĩnh viễn, không vào Thùng rác".

### 13.4 i18n
Mọi chuỗi mới có trong cả `Strings.vi.xaml` và `Strings.en.xaml` (test cùng key vẫn phải pass).

### 13.5 Tests
- `CustomRulePolicyTests`: Ok cho `%LOCALAPPDATA%\CapCut\User Data\Cache`; Blocked cho `AppData`, `AppData\Local`, `C:\Users`, gốc ổ, `%WINDIR%\System32`, Documents, thư mục app, path đã tồn tại, path tương đối, UNC; Warn NotCacheLike cho `%LOCALAPPDATA%\SomeApp\Data`; Warn OutsideAppData cho `D:\Foo\cache`; Warn LooksLikeProject khi có `.git` (temp tree).
- `CustomRuleStoreTests` (file JSON trong temp): Add → LoadAll trả lại; Id ổn định; Remove; file JSON hỏng → trả rỗng + log, không crash; rule bị sửa tay trỏ vào vùng cấm → bị loại khi load.
- `CleanExecutorTests`: rule custom Blocked → không xóa gì, có lỗi trong report; rule custom hợp lệ trên temp tree → xóa nội dung, giữ thư mục gốc.

### 13.6 Definition of Done v1.2
- [ ] Build 0 warning, toàn bộ test pass.
- [ ] Trên app thật: chọn `CapCut\User Data\Cache` ở Analyzer → Thêm → sang tab Dọn dẹp thấy card "Tùy chỉnh" có dung lượng; tắt mở app vẫn còn; xóa rule khỏi danh sách được. (**Không bấm Dọn dẹp thật** khi test.)
- [ ] Chọn `AppData\Local` → nút bị chặn, tooltip ghi lý do.
- [ ] Version 1.2.0, README có mục "Rule tùy chỉnh", publish `releases/DiskCleaner-v1.2.0-Portable(.zip)`.

# Disk Cleaner (v1.3.0)

App WPF nhỏ để **phân tích và dọn dẹp ổ C** an toàn. Nguyên tắc số 1: không bao giờ xóa nhầm.
Mọi lần xóa đều đi qua `SafetyGuard`, chỉ xóa trong whitelist (`rules.json`), mặc định quét thử (dry-run) trước.

## Chạy

- Yêu cầu: Windows 10/11 x64 và [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- Bản portable: giải nén `releases/DiskCleaner-v1.3.0-Portable.zip`, chạy `DiskCleaner.exe`. Không cần quyền admin.
- Từ source:

```powershell
dotnet run --project DiskCleaner/src/DiskCleaner
dotnet test DiskCleaner/tests/DiskCleaner.Tests
```

## Ngôn ngữ (VI / EN) và giao diện v1.1

- Nút `VI | EN` ở cuối thanh bên trái đổi ngôn ngữ **ngay lập tức** (kể cả kết quả quét đang hiện) và được lưu vào
  `%LOCALAPPDATA%\DiskCleaner\settings.json` (`{ "language": "vi" }`). Lần chạy đầu: nếu ngôn ngữ giao diện Windows là tiếng Việt thì dùng VI, còn lại EN.
- Chuỗi giao diện nằm ở `src/DiskCleaner/UI/Localization/Strings.vi.xaml` và `Strings.en.xaml` (hai file phải có cùng tập key, có test kiểm tra).
  Tên/mô tả/cảnh báo của rule có thêm `nameEn`, `descriptionEn`, `warningEn` trong `rules.json`.
- Giao diện dark kiểu Fluent (design token trong `UI/Themes/Tokens.xaml`, style control trong `UI/Themes/Controls.xaml`), title bar tự vẽ,
  biểu đồ donut ở Tổng quan, danh sách Dọn dẹp dạng card với công tắc bật/tắt và thanh hành động cố định ở đáy.
- Kết quả Quét nhanh ở Tổng quan được dùng chung với tab Dọn dẹp. Rule cần admin mà app không chạy admin hiện "Cần quyền admin để quét"
  thay vì 0 B; rule không khả dụng có tooltip giải thích lý do.

## 4 tab

| Tab | Chức năng |
|---|---|
| Tổng quan | Dung lượng các ổ cố định (thanh xanh/vàng/đỏ), nút "Quét nhanh" cho các rule mặc định. |
| Dọn dẹp | Chọn rule, **Quét** (dry-run) rồi **Dọn dẹp**. Có hộp thoại xác nhận, tiến trình, hủy, bảng kết quả và link mở log. |
| Phân tích | Cây thư mục theo dung lượng (lazy). Chuột phải: thêm vào Dọn dẹp, mở Explorer, copy đường dẫn. Tab này không xóa gì; chọn thư mục rồi **Thêm vào Dọn dẹp** để tạo rule tùy chỉnh. |
| File lớn | Tìm top N file lớn nhất. Chỉ **chuyển vào Thùng rác** (khôi phục được), không xóa vĩnh viễn. |

## Rule tùy chỉnh (v1.2)

Muốn dọn một thư mục cụ thể không có trong `rules.json` (ví dụ `AppData\Local\CapCut\User Data\Cache`, vài chục GB):

1. Tab **Phân tích**: quét ổ/thư mục, chọn thư mục cần dọn rồi bấm **Thêm vào Dọn dẹp** (hoặc chuột phải).
2. Hộp thoại hiện đường dẫn, dung lượng, số file, ô tên hiển thị và "Chỉ xóa file cũ hơn" (không giới hạn / 24 giờ / 7 ngày / 30 ngày),
   kèm cảnh báo vàng nếu có. Bấm **Thêm**.
3. App chuyển sang tab **Dọn dẹp**: rule nằm ở nhóm **Tùy chỉnh** (cuối danh sách), đã bật và đã quét dung lượng. Rule được lưu vào
   `%LOCALAPPDATA%\DiskCleaner\custom-rules.json` nên lần sau mở app vẫn còn (mặc định tắt khi mở lại).
4. Nút thùng rác trên thẻ chỉ **bỏ rule khỏi danh sách** (có hỏi xác nhận), không xóa file nào.

Thư mục đã có trong Dọn dẹp (rule dựng sẵn hoặc tùy chỉnh) hiện nút "Đã có trong Dọn dẹp" (không bấm được).

**Chính sách an toàn** (`Core/CustomRulePolicy.cs`, kiểm tra cả lúc thêm, lúc nạp file và ngay trước khi dọn):

- **Chặn** (không cho thêm, tooltip nêu lý do): đường dẫn không tuyệt đối/UNC/gốc ổ đĩa; nằm trong vùng cấm của `SafetyGuard`;
  là tổ tiên của vùng cấm hoặc của `%USERPROFILE%` (vd `C:\Users`); thư mục quá rộng hoặc tổ tiên của chúng
  (`AppData`, `AppData\Local`, `Roaming`, `LocalLow`, `Local\Programs`, `Local\Packages`, `Local\Microsoft`, `Roaming\Microsoft`,
  `Downloads`, `.ssh`, `.gnupg`, `source`, `.config`); thư mục dữ liệu của chính app (`%LOCALAPPDATA%\DiskCleaner`) hoặc thư mục chứa exe;
  junction/symlink; thư mục đã có rule.
- **Cảnh báo** (vẫn cho thêm): tên thư mục và tối đa 3 thư mục cha không có từ khóa cache/tạm (`cache`, `temp`, `tmp`, `log`, `crash`, `dump`,
  `thumbnails`, `gpucache`...); nằm ngoài `AppData` và `%TEMP%`; có thư mục `.git` ở cấp 1.
- `CleanExecutor` kiểm tra lại chính sách cho mọi rule tùy chỉnh và dừng nếu bị chặn (phòng khi file JSON bị sửa tay); khi nạp, rule sai/bị chặn bị loại và ghi log.
- Rule tùy chỉnh luôn chạy quyền thường: chế độ elevated (UAC) không bao giờ nạp `custom-rules.json`.
- Hộp thoại xác nhận Dọn dẹp liệt kê riêng các thư mục tùy chỉnh đã chọn với đường dẫn đầy đủ và nhắc **xóa vĩnh viễn, không vào Thùng rác**.

## Các rule dọn dẹp (`src/DiskCleaner/Rules/rules.json`)

| Id | Làm gì | Mặc định | Admin |
|---|---|---|---|
| `user-temp` | Xóa file trong `%TEMP%` cũ hơn 24 giờ | bật | không |
| `windows-temp` | Xóa file trong `%WINDIR%\Temp` cũ hơn 24 giờ | bật | **có** |
| `wu-cache` | Xóa `%WINDIR%\SoftwareDistribution\Download` (nên dừng dịch vụ Windows Update trước) | tắt | **có** |
| `recycle-bin` | Dọn Thùng rác | tắt | không |
| `crash-dumps` | Xóa `%LOCALAPPDATA%\CrashDumps` | bật | không |
| `thumb-cache` | Xóa `thumbcache_*.db`, `iconcache_*.db` của Explorer (Windows tự tạo lại) | tắt | không |
| `chrome-cache` | `Cache`, `Code Cache`, `GPUCache` của mọi profile Chrome | bật | không |
| `edge-cache` | Tương tự cho Microsoft Edge | bật | không |
| `npm-cache` | Chạy `npm cache clean --force` | tắt | không |
| `nuget-cache` | Chạy `dotnet nuget locals all --clear` (lần build sau phải tải lại package) | tắt | không |
| `pip-cache` | Chạy `pip cache purge` | tắt | không |
| `yarn-cache` | Chạy `yarn cache clean` | tắt | không |
| `gradle-cache` | Xóa file trong `%USERPROFILE%\.gradle\caches` cũ hơn 30 ngày | tắt | không |
| `docker-prune` | Chạy `docker system prune -f` | tắt | không |

Rule chỉ là dữ liệu: thêm rule mới vào `rules.json` không cần sửa code. Rule có path rơi vào vùng cấm, Id trùng, hoặc
`RunCommand` với lệnh ngoài whitelist (`npm`, `dotnet`, `pip`, `yarn`, `docker`) sẽ bị loại khi nạp.
Trường `namePatterns` (tuỳ chọn) giới hạn chỉ xóa file có tên khớp, ví dụ `thumbcache_*.db`.

## An toàn

- **Vùng cấm** (không bao giờ xóa, kể cả khi rule trỏ tới): `%WINDIR%` (trừ `Temp` và `SoftwareDistribution\Download`),
  Program Files, Program Files (x86), ProgramData, Documents/Desktop/Pictures/Videos/Music, `OneDrive*`, gốc ổ đĩa,
  và chính thư mục `%USERPROFILE%`.
- Chỉ xóa thứ **nằm bên trong** thư mục rule, không xóa chính thư mục gốc của rule. Từ chối path tương đối, UNC, `..` thoát ra ngoài.
- **Không đi theo junction/symlink**: gặp link thì chỉ gỡ chính link (nếu nằm trong root), không đụng nội dung đích.
- Bỏ qua file OneDrive "chỉ trên đám mây" (Recall/Offline) để không kích hoạt tải về.
- File đang bị khóa hoặc không đủ quyền thì bỏ qua và ghi log, không làm app sập.
- Chỉ `CleanExecutor` được gọi `File.Delete` / `Directory.Delete`, và mọi lần gọi đều qua `SafetyGuard.IsSafeToDelete`.
- Số liệu dry-run dùng cùng bộ lọc với lúc xóa thật nên khớp với số byte giải phóng.

### Log

Mỗi lần dọn dẹp ghi file log tại `%LOCALAPPDATA%\DiskCleaner\logs\clean-yyyyMMdd-HHmmss.log` (liệt kê từng file đã xóa).
Nút "Mở log" ở tab Dọn dẹp mở file log gần nhất. Lỗi chung của app nằm trong `app.log` cùng thư mục.

### Quyền admin (UAC)

App chạy ở chế độ thường (`asInvoker`). Khi bạn chọn rule cần admin (`windows-temp`, `wu-cache`), app hỏi xác nhận rồi
chạy lại chính nó với UAC (`--elevated-clean <jobFile>`). Tiến trình admin không mở cửa sổ, **chỉ nhận danh sách Id**,
chỉ chạy rule có `requiresAdmin` đã nạp từ `rules.json` (không tin path nào trong job file), rồi ghi kết quả ra
`<jobFile>.result.json`. Nếu bấm "No" ở UAC, app báo đã hủy và bỏ qua các mục đó.

Lưu ý bảo mật: tiến trình admin đọc `Rules\rules.json` cạnh file exe. Nếu để bản portable ở thư mục mà user thường ghi được
thì cần hiểu rằng file này quyết định rule admin; `SafetyGuard` vẫn chặn vùng cấm ở mọi trường hợp.

## Docker và file `vhdx`

`docker system prune -f` xóa container đã dừng, image và network không dùng, **nhưng file đĩa ảo
(`docker_data.vhdx` / `ext4.vhdx` của WSL2) KHÔNG tự nhỏ lại**, nên Explorer vẫn thấy ổ C đầy như cũ.
Để thu nhỏ thật sự (làm thủ công, ngoài phạm vi app này):

1. Thoát Docker Desktop, chạy `wsl --shutdown`.
2. Mở PowerShell quyền admin: `Optimize-VHD -Path <đường dẫn file .vhdx> -Mode Full` (cần Hyper-V),
   hoặc dùng `diskpart` -> `select vdisk file="..."` -> `compact vdisk`.

Vì vậy rule `docker-prune` thường báo giải phóng gần 0 byte. Đó là bình thường.

## Cập nhật tự động (v1.3)

- Sidebar có link **Kiểm tra cập nhật**; chấm tím hiện khi có bản mới. App tự kiểm tra nền 10 giây sau khi mở, tối đa 1 lần/24 giờ, và không kiểm tra/cài khi đang quét hoặc dọn.
- Bản cập nhật chỉ được cài khi **chữ ký số hợp lệ** (ECDSA P-256) và sha256 khớp; sau đó `MiniTools.UpdateHelper.exe` chép đè file (có backup + rollback, không xóa file người dùng như `settings.json`) rồi mở lại app.
- Hub: hằng số `UpdateConfig.HubBaseUrl` trong `src/DiskCleaner/Services/UpdateConfig.cs` (mặc định `https://mth.mimehub.top`).
- Khóa công khai nằm ở `Shared/MiniTools.Updater/PublicKey.cs`. Khi còn là chuỗi rỗng (placeholder), **cập nhật bị tắt**: app không kiểm tra và không cài gì cho tới khi chạy `scripts/new-signing-key.ps1` rồi build lại.
- Tuỳ chọn lưu ở `%LOCALAPPDATA%\MiniTools\disk-cleaner\update.json`. Xem `MiniToolsHub/README.md` để phát hành bản mới.

## Cấu trúc thư mục

```text
DiskCleaner/
├── PLAN.md, README.md, .gitignore
├── scripts/publish.ps1          # publish + đóng gói portable vào ../releases
├── src/DiskCleaner/
│   ├── Core/                    # logic thuần (SafetyGuard, CustomRulePolicy, PathExpander, FileFilter, RuleValidator, models)
│   ├── Services/                # I/O: scanner, CleanExecutor, CustomRuleStore, FolderAnalyzer, LargeFileFinder, Recycle Bin, UAC...
│   ├── Rules/rules.json         # whitelist rule
│   └── UI/                      # WPF MVVM (Mvvm, Views, ViewModels, Converters, Themes, Localization, Controls)
└── tests/DiskCleaner.Tests/     # xUnit, chỉ dùng cây thư mục tạm trong %TEMP%\DiskCleanerTests
```

## Đóng gói

```powershell
powershell -ExecutionPolicy Bypass -File DiskCleaner/scripts/publish.ps1
```

Kết quả: `releases/DiskCleaner-v1.3.0-Portable/` và `releases/DiskCleaner-v1.3.0-Portable.zip` (các bản cũ giữ nguyên).

## Ngoài phạm vi v1.3

Move & Link (chuyển thư mục sang ổ khác + junction), compact vhdx, tìm `node_modules`/`bin`/`obj` cũ,
DISM/`powercfg -h off`/`Windows.old`, tìm file trùng, treemap, quét MFT, lên lịch tự động.

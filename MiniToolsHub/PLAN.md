# MiniToolsHub — PLAN (v1.0)

Trang web quảng bá + tải các mini tool (TaskbarMonitor, DiskCleaner, ...) **và** máy chủ cập nhật để app tự update.
Chạy trên VPS Linux bằng **Docker**, public ra domain qua **Cloudflare Tunnel** (không mở port nào trên VPS).

```text
 Máy dev (Windows)                          VPS (Docker)                                   Người dùng
 ─────────────────                          ────────────                                   ──────────
 scripts/release.ps1                        ┌────────────── docker compose ─────────────┐
  build → zip → sha256                      │ cloudflared  ──►  hub (ASP.NET Core :18473) │◄── https://<domain>  (web, tải về)
  → KÝ manifest (ECDSA, key chỉ ở máy dev)  │                   volume ./data             │◄── App: GET /api/apps/{id}/latest
  → POST /api/admin/releases (token) ──────►│                   (sqlite + file zip)       │    → tải zip → kiểm tra sha256 + chữ ký
                                            └─────────────────────────────────────────────┘    → UpdateHelper thay file → restart
```

**Nguyên tắc bảo mật số 1:** app chỉ cài bản cập nhật có **chữ ký hợp lệ** từ private key nằm trên máy dev. Server (và Cloudflare) **không giữ private key**, nên kể cả server bị chiếm cũng không đẩy được bản độc hại.

---

## 1. Tech stack

| Phần | Lựa chọn |
|---|---|
| Server | ASP.NET Core 10 (`net10.0`): Minimal API + Razor Pages (render server-side, tốt cho SEO) |
| DB | SQLite qua `Microsoft.Data.Sqlite` + Dapper (nhẹ, 1 file trong volume) |
| Contracts | Class library `net10.0` dùng chung server ↔ updater (model manifest, version, ký/kiểm chữ ký) |
| Chữ ký | ECDSA P-256 + SHA-256 (`System.Security.Cryptography.ECDsa`, có sẵn trong .NET, không cần NuGet ngoài) |
| Updater | Class library `net10.0-windows` + console app nhỏ `MiniTools.UpdateHelper.exe` |
| Container | `mcr.microsoft.com/dotnet/aspnet:10.0` (image chạy), `mcr.microsoft.com/dotnet/sdk:10.0` (stage build) |
| Public | `cloudflare/cloudflared` trong cùng compose, chạy bằng `TUNNEL_TOKEN` |
| Test | xUnit + `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory) |

---

## 2. Cấu trúc thư mục (workspace)

```text
mini-tool-indie/
├── MiniTools.slnx                      # thêm các project mới
├── scripts/                            # script dùng chung cho mọi app
│   ├── new-signing-key.ps1             # tạo cặp khóa ECDSA (1 lần)
│   └── release.ps1                     # build → zip → ký → upload 1 app
├── Shared/
│   ├── MiniTools.Contracts/            # net10.0
│   │   ├── MiniTools.Contracts.csproj
│   │   ├── AppVersion.cs
│   │   ├── ReleaseManifest.cs
│   │   ├── ManifestSigner.cs
│   │   └── AppCatalogEntry.cs
│   ├── MiniTools.Updater/              # net10.0-windows (WPF-free, dùng được cả WPF lẫn WinForms)
│   │   ├── MiniTools.Updater.csproj
│   │   ├── UpdateClient.cs
│   │   ├── UpdateOptions.cs
│   │   ├── UpdateSettingsStore.cs
│   │   ├── PublicKey.cs                # public key nhúng sẵn (sinh từ new-signing-key.ps1)
│   │   └── UI/UpdateDialog.xaml(.cs)   # dialog WPF dùng chung (dark, VI/EN)
│   ├── MiniTools.UpdateHelper/         # net10.0-windows console, WinExe, self-contained=false
│   │   └── Program.cs
│   └── tests/
│       ├── MiniTools.Contracts.Tests/
│       └── MiniTools.Updater.Tests/
└── MiniToolsHub/
    ├── PLAN.md
    ├── README.md                       # chạy local, deploy, Cloudflare Tunnel, phát hành bản mới
    ├── src/MiniToolsHub.Server/
    │   ├── MiniToolsHub.Server.csproj
    │   ├── Program.cs
    │   ├── appsettings.json
    │   ├── Data/ (Db.cs, Migrations.sql, ReleaseRepository.cs, DownloadCounter.cs)
    │   ├── Api/ (PublicApi.cs, AdminApi.cs, DownloadEndpoint.cs, ApiKeyAuth.cs)
    │   ├── Content/ (ToolContentStore.cs)             # đọc nội dung giới thiệu tool
    │   ├── Pages/ (_Layout, Index, Tool, Changelog, Error).cshtml + PageModel
    │   ├── Localization/ (Strings.vi.json, Strings.en.json, Loc.cs)
    │   └── wwwroot/ (css/site.css, js/site.js, img/)
    ├── content/tools/                  # nội dung tĩnh từng tool, deploy cùng image
    │   ├── taskbar-monitor/ (meta.json, vi.md, en.md, icon.png, shots/*.png)
    │   └── disk-cleaner/   (meta.json, vi.md, en.md, icon.png, shots/*.png)
    ├── tests/MiniToolsHub.Server.Tests/
    ├── deploy/
    │   ├── Dockerfile
    │   ├── docker-compose.yml
    │   ├── .env.example
    │   └── .dockerignore
    └── scripts/
        └── deploy-server.ps1           # đóng gói & đẩy lên VPS qua SSH
```

---

## 3. Contracts (Shared/MiniTools.Contracts)

```csharp
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    public static AppVersion Parse(string s);            // "1.2.0" | "v1.2.0"; throw FormatException
    public static bool TryParse(string? s, out AppVersion v);
    public override string ToString();                   // "1.2.0"
}

public sealed record ReleaseManifest
{
    public required string AppId { get; init; }          // "disk-cleaner" (kebab-case, regex ^[a-z0-9-]{2,40}$)
    public required string Version { get; init; }        // "1.2.0"
    public required string Channel { get; init; }        // "stable" | "beta"
    public required string FileName { get; init; }       // "DiskCleaner-v1.2.0-Portable.zip"
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }         // hex lowercase
    public required DateTimeOffset PublishedAt { get; init; }
    public required string MinUpdaterVersion { get; init; } = "1.0.0";
    public required Dictionary<string, string> Notes { get; init; }   // { "vi": "...", "en": "..." } (markdown)
    public string? Signature { get; init; }              // base64 ECDSA trên CanonicalPayload()

    /// Chuỗi được ký: các field (trừ Signature, Notes) nối bằng '\n' theo thứ tự cố định + sha256 của Notes JSON.
    public string CanonicalPayload();
}

public static class ManifestSigner
{
    public static string Sign(ReleaseManifest m, string privateKeyPem);      // trả base64
    public static bool Verify(ReleaseManifest m, string publicKeyPem);       // false nếu thiếu/sai chữ ký, không throw
}

public sealed record AppCatalogEntry(string AppId, string Name, ReleaseManifest? Latest, long TotalDownloads);
```
URL tải **không nằm trong manifest được ký**; client tự ghép `{baseUrl}/dl/{appId}/{version}/{fileName}` và luôn kiểm tra sha256 + chữ ký.

---

## 4. Server (MiniToolsHub.Server)

### 4.1 Cấu hình (`appsettings.json` + biến môi trường)
| Key | Ý nghĩa |
|---|---|
| `Hub:PublicBaseUrl` | `https://<domain>` (dùng cho link tuyệt đối, sitemap) |
| `Hub:DataDir` | mặc định `/data` (volume): `hub.db`, `files/{appId}/{version}/{fileName}` |
| `Hub:ContentDir` | mặc định `/app/content` |
| `Hub:AdminTokenSha256` | **SHA-256 hex của** admin token (không lưu token gốc) |
| `Hub:MaxUploadMb` | 95 (Cloudflare Free giới hạn body request 100 MB) |
| `Hub:PublicKeyPem` | public key để server kiểm chữ ký khi nhận upload (từ chối bản không ký đúng) |

### 4.2 Data (SQLite, tạo bảng khi khởi động nếu chưa có)
```sql
CREATE TABLE IF NOT EXISTS releases (
  id INTEGER PRIMARY KEY, app_id TEXT NOT NULL, version TEXT NOT NULL, channel TEXT NOT NULL,
  file_name TEXT NOT NULL, size_bytes INTEGER NOT NULL, sha256 TEXT NOT NULL,
  manifest_json TEXT NOT NULL, published_at TEXT NOT NULL, is_yanked INTEGER NOT NULL DEFAULT 0,
  UNIQUE(app_id, version));
CREATE TABLE IF NOT EXISTS download_counts (
  app_id TEXT NOT NULL, version TEXT NOT NULL, day TEXT NOT NULL, count INTEGER NOT NULL,
  PRIMARY KEY(app_id, version, day));
```
Không lưu IP / User-Agent (chỉ đếm số lượt theo ngày).

### 4.3 API
| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/apps` | `AppCatalogEntry[]` (app có trong `content/tools` + bản mới nhất stable) |
| GET | `/api/apps/{appId}/latest?channel=stable&current=1.1.0` | `ReleaseManifest` mới nhất (bỏ bản yanked); `204` nếu `current` đã là mới nhất; `404` app không tồn tại. Header `Cache-Control: public, max-age=300` |
| GET | `/api/apps/{appId}/releases` | lịch sử (mới → cũ), tối đa 50 |
| GET | `/dl/{appId}/{version}/{fileName}` | trả file (`Results.File`, `enableRangeProcessing: true`, `Content-Type: application/zip`), tăng đếm; `Cache-Control: public, max-age=31536000, immutable` (Cloudflare cache được) |
| POST | `/api/admin/releases` | multipart: `manifest` (JSON) + `file`. Auth `Authorization: Bearer <token>` (so sánh SHA-256, `CryptographicOperations.FixedTimeEquals`). Kiểm tra: appId hợp lệ & có trong content, version hợp lệ & chưa tồn tại (409), fileName khớp `^[A-Za-z0-9._-]+\.zip$`, size ≤ MaxUploadMb, **sha256 file = manifest.Sha256**, **chữ ký hợp lệ với `Hub:PublicKeyPem`**. Ghi file ra `.tmp` rồi move (atomic). `201` + manifest |
| POST | `/api/admin/releases/{appId}/{version}/yank` | đánh dấu yanked (không xóa file) |
| GET | `/healthz` | `ok` |

An toàn path: mọi path file dựng từ appId/version/fileName **đã validate bằng regex**, sau đó `Path.GetFullPath` phải nằm trong `DataDir/files` (chống path traversal). Rate limit (`Microsoft.AspNetCore.RateLimiting`): admin 10 req/phút, public 120 req/phút theo `CF-Connecting-IP` (fallback RemoteIp).
`ForwardedHeaders`: tin `X-Forwarded-Proto` từ cloudflared để sinh URL https.

### 4.4 Nội dung tool (`content/tools/{appId}/meta.json`)
```json
{
  "appId": "disk-cleaner", "order": 2,
  "name": { "vi": "Disk Cleaner", "en": "Disk Cleaner" },
  "tagline": { "vi": "Dọn ổ C an toàn, quét trước xóa sau", "en": "Safe C: drive cleanup, scan before delete" },
  "features": { "vi": ["..."], "en": ["..."] },
  "requirements": { "vi": "Windows 10/11 x64, .NET 10 Desktop Runtime", "en": "..." },
  "accent": "#6D5EF6"
}
```
`vi.md` / `en.md`: mô tả dài (render markdown bằng `Markdig`). Ảnh chụp: lấy từ `DiskCleaner/tmp/screenshots` (v12 Overview, Clean, Analyzer) và chụp TaskbarMonitor nếu được; nếu không có thì dùng `icon.png` + placeholder.

### 4.5 Website (Razor Pages, song ngữ)
- Route: `/` → redirect `/vi` hoặc `/en` theo `Accept-Language`; `/{lang}`, `/{lang}/tools/{appId}`, `/{lang}/changelog`. Nút chuyển VI | EN giữ nguyên trang.
- Giao diện: cùng design tokens với DiskCleaner (Bg `#0E1014`, Surface `#161920`, Accent gradient `#6D5EF6 → #A35BF0`, Text `#E8EAF0`, Muted `#8B93A7`), font `Inter` (Google Fonts) fallback `Segoe UI`, card radius 14. CSS thuần (không framework), responsive: ≥1024 grid 3 cột, 640–1023 2 cột, <640 1 cột, gutter 16px, không scroll ngang.
- **Trang chủ**: hero (tên bộ tool, tagline, nút "Xem các tool"), grid card tool (icon, tên, tagline, version mới nhất, dung lượng, nút **Tải về**), section "Tự động cập nhật & an toàn" (3 ý: ký số, không thu thập dữ liệu, portable), footer.
- **Trang tool**: header (icon, tên, version, ngày phát hành, tổng lượt tải), nút Tải lớn + dòng `SHA-256: …` (copy được), carousel ảnh chụp (CSS scroll-snap), features, yêu cầu hệ thống, markdown mô tả, lịch sử phiên bản (accordion, notes theo ngôn ngữ).
- **Changelog**: tất cả bản của mọi tool theo thời gian.
- SEO: `<title>`, meta description, OpenGraph, `hreflang`, `sitemap.xml`, `robots.txt`.
- Không cookie, không analytics bên thứ ba.

---

## 5. Docker + Cloudflare Tunnel (`MiniToolsHub/deploy/`)

`Dockerfile` (build context = workspace root để copy `Shared/`):
- stage `build`: sdk:10.0, restore + `dotnet publish src/MiniToolsHub.Server -c Release -o /out`
- stage `final`: aspnet:10.0, user không phải root (`USER app`), copy `/out` và `MiniToolsHub/content` → `/app/content`, `ENV ASPNETCORE_URLS=http://+:18473`, `EXPOSE 18473`, `HEALTHCHECK` gọi `http://localhost:18473/healthz`.

**Port & tên riêng, tránh trùng với service khác trên VPS:**

| Thứ | Giá trị |
|---|---|
| Port app trong container | **18473** (`ASPNETCORE_URLS=http://+:18473`, không dùng 8080/5000/3000 phổ biến) |
| Port trên host (chỉ để debug, bind localhost) | `127.0.0.1:${HUB_HOST_PORT:-18473}` — đổi được qua `.env` nếu bị trùng |
| Port dev local (`launchSettings.json`) | `http://localhost:18473` |
| Compose project name | `minitools-hub` (`name:` trong compose) |
| Container names | `minitools-hub-app`, `minitools-hub-tunnel` |
| Network | `minitools-hub-net` (bridge riêng) |
| Image | `minitools-hub:latest` |
| Volume dữ liệu | `./data` (bind mount trong thư mục deploy) |

`docker-compose.yml`:
```yaml
name: minitools-hub
services:
  hub:
    build: { context: ../.., dockerfile: MiniToolsHub/deploy/Dockerfile }
    image: minitools-hub:latest
    container_name: minitools-hub-app
    restart: unless-stopped
    env_file: .env
    environment: [ "ASPNETCORE_URLS=http://+:18473" ]
    volumes: [ "./data:/data" ]
    ports: [ "127.0.0.1:${HUB_HOST_PORT:-18473}:18473" ]   # chỉ localhost, không lộ ra internet
    networks: [ minitools-hub-net ]
  cloudflared:
    image: cloudflare/cloudflared:${CLOUDFLARED_TAG:-latest}   # nên pin tag cụ thể trong .env khi deploy thật
    container_name: minitools-hub-tunnel
    command: tunnel --no-autoupdate run
    environment: [ "TUNNEL_TOKEN=${TUNNEL_TOKEN}" ]
    restart: unless-stopped
    depends_on: [ hub ]
    networks: [ minitools-hub-net ]
networks:
  minitools-hub-net: { name: minitools-hub-net }
```
README có lệnh kiểm tra port trước khi chạy: `ss -tlnp | grep 18473` và `docker ps --format '{{.Names}}' | grep minitools-hub`; nếu trùng thì đổi `HUB_HOST_PORT` trong `.env`.
Trên Cloudflare dashboard (user tự làm, ghi trong README): tạo Tunnel → Public Hostname `<domain>` → Service `http://minitools-hub-app:18473`. Khuyến nghị thêm **Cloudflare Access** (hoặc WAF rule) chặn `/api/admin/*` ngoài IP nhà/công ty — lớp bảo vệ thứ hai ngoài token.

`.env.example`: `TUNNEL_TOKEN=`, `HUB_HOST_PORT=18473`, `CLOUDFLARED_TAG=latest`, `Hub__PublicBaseUrl=https://mth.mimehub.top`, `Hub__AdminTokenSha256=`, `Hub__PublicKeyPem=`.
Lưu ý pin `cloudflared` theo version cụ thể khi deploy thật (README).

`scripts/deploy-server.ps1 -Host user@vps -Path /opt/minitools-hub`: tạo tarball (Shared, MiniToolsHub, MiniTools.slnx — bỏ bin/obj/data/.env) → `scp` → `ssh` chạy `docker compose -f MiniToolsHub/deploy/docker-compose.yml up -d --build`. Không bao giờ copy `.env` hay private key. (Agent **không chạy** script này.)

---

## 6. Ký & phát hành (`scripts/` ở workspace root)

- `new-signing-key.ps1`: tạo ECDSA P-256, lưu private key PEM vào `%USERPROFILE%\.minitools\signing-key.pem` (cảnh báo nếu đã tồn tại, không ghi đè trừ `-Force`), in public key PEM và ghi vào `Shared/MiniTools.Updater/PublicKey.cs` + hướng dẫn đặt `Hub__PublicKeyPem`. **Private key không bao giờ nằm trong repo** (thêm `*.pem` vào `.gitignore`).
- `release.ps1 -App DiskCleaner|TaskbarMonitor [-Channel stable] [-NotesVi "..."] [-NotesEn "..."] [-DryRun]`:
  1. Đọc version từ csproj; chạy publish của app (DiskCleaner: `DiskCleaner/scripts/publish.ps1`; TaskbarMonitor: thêm publish tương đương) để có zip trong `releases/`.
  2. Copy `MiniTools.UpdateHelper.exe` vào zip (cạnh exe app).
  3. Tính sha256, dựng manifest, ký bằng private key (dùng tool nhỏ `scripts/tools/ManifestTool` — console `net10.0` tham chiếu Contracts: `sign`, `verify`).
  4. Upload `POST /api/admin/releases` với token từ biến môi trường `MINITOOLS_ADMIN_TOKEN` và URL từ `MINITOOLS_HUB_URL`. `-DryRun`: làm hết trừ upload, in manifest.
- Map appId: `TaskbarMonitor → taskbar-monitor`, `DiskCleaner → disk-cleaner`.

---

## 7. Updater phía app (Shared/MiniTools.Updater + UpdateHelper)

```csharp
public sealed record UpdateOptions(string AppId, AppVersion CurrentVersion, Uri HubBaseUrl,
                                   string Channel = "stable", string Language = "vi");

public sealed class UpdateClient
{
    public UpdateClient(UpdateOptions o, HttpClient http, string publicKeyPem);
    public Task<ReleaseManifest?> CheckAsync(CancellationToken ct);             // null nếu đã mới nhất/lỗi mạng (log, không throw)
    public Task<PreparedUpdate> DownloadAsync(ReleaseManifest m, IProgress<double>? p, CancellationToken ct);
        // tải về %LOCALAPPDATA%\MiniTools\updates\{appId}\{version}\pkg.zip
        // kiểm tra: chữ ký (Verify) + size + sha256; sai → xóa file, throw UpdateVerificationException
        // giải nén vào ...\{version}\files\ (chống zip-slip: mọi entry phải nằm trong thư mục đích)
    public void ApplyAndRestart(PreparedUpdate u);
        // chạy UpdateHelper.exe (copy ra %LOCALAPPDATA%\MiniTools\updates\helper\ trước để tránh tự khóa)
        // args: --pid <pid> --source <files> --target <AppContext.BaseDirectory> --restart <exe path>
        // thư mục đích không ghi được → chạy helper với Verb=runas
        // rồi Application.Current.Shutdown()
}
```
- `UpdateHelper`: chờ pid thoát (timeout 60s) → backup thư mục đích vào `{target}\..\{name}.bak-{version}` → copy đè file mới → nếu lỗi bất kỳ: khôi phục từ backup, ghi log, vẫn restart bản cũ → thành công: xóa backup, restart app. Log: `%LOCALAPPDATA%\MiniTools\updates\helper.log`. Không xóa file nằm ngoài thư mục đích; chỉ copy file có trong gói (không xóa file thừa của user như `settings.json`).
- `UpdateSettingsStore`: `%LOCALAPPDATA%\MiniTools\{appId}\update.json` — `AutoCheck` (mặc định true), `LastCheckUtc`, `SkippedVersion`. Auto-check tối đa 1 lần/24h, chạy nền 10s sau khi app mở.
- `UpdateDialog` (WPF, dark, theo ngôn ngữ truyền vào): "Có bản mới X (bạn đang dùng Y)", notes, dung lượng; nút **Cập nhật ngay** (progress → ApplyAndRestart), **Để sau**, **Bỏ qua bản này**.
- Tích hợp:
  - **DiskCleaner**: sidebar dưới cùng thêm link "Kiểm tra cập nhật"; badge chấm tím khi có bản mới; tự check nền. Chuỗi mới vào `Strings.vi/en.xaml`. Hub URL lấy từ `appsettings`-style hằng số `UpdateConfig.HubBaseUrl` (một chỗ duy nhất, mặc định `https://mth.mimehub.top`, sửa khi có domain thật).
  - **TaskbarMonitor**: menu tray thêm "Kiểm tra cập nhật…" + auto-check nền; ngôn ngữ vi.
  - Không tự cập nhật khi DiskCleaner đang quét/dọn (hỏi lại sau).

---

## 8. Tests

| Project | Case bắt buộc |
|---|---|
| Contracts.Tests | AppVersion parse/compare/"v" prefix/sai định dạng; Sign→Verify true; sửa 1 field (version, sha256, fileName, notes) → false; key khác → false; signature rác → false không throw |
| Hub.Server.Tests (WebApplicationFactory, DataDir trong temp) | upload thiếu/sai token 401; đúng → 201 và `latest` trả về; trùng version 409; sha256 sai 400; chữ ký sai 400; appId không có content 400; fileName `../x.zip` 400; quá dung lượng 413; `latest?current=` bằng bản mới nhất → 204; yanked bị bỏ qua; `/dl` trả đúng bytes + Range + đếm tăng; path traversal `/dl/disk-cleaner/1.0.0/..%2F..%2Fhub.db` 400/404; trang `/vi`, `/en/tools/disk-cleaner` trả 200 có tên tool; `/` redirect theo Accept-Language |
| Updater.Tests | CheckAsync với HttpMessageHandler giả (204 → null, manifest ký đúng → trả về, ký sai → null + log); DownloadAsync sha sai → throw và xóa file; zip-slip entry → throw; UpdateHelper logic (tách thành class `UpdateApplier` testable): copy đè đúng, lỗi giữa chừng → rollback đầy đủ, không đụng file không có trong gói |

---

## 9. Definition of Done

- [ ] `dotnet build MiniTools.slnx` 0 lỗi, 0 warning mới; toàn bộ test (cũ + mới) pass.
- [ ] Chạy server local (`dotnet run`, DataDir temp) → trang `/vi`, `/en`, trang tool hiển thị đẹp ở desktop và mobile 375px.
- [ ] `release.ps1 -DryRun` với key test tạo manifest ký đúng; upload lên server local thành công; DiskCleaner build với `HubBaseUrl=http://localhost:xxxx` phát hiện bản mới (test bằng version giả cao hơn) — **không cần thực sự chạy ApplyAndRestart trên app thật**.
- [ ] `docker build` thành công (nếu Docker có trên máy dev; nếu không, ghi rõ chưa kiểm tra).
- [ ] README: chạy local, tạo key, cấu hình `.env`, tạo Cloudflare Tunnel + Public Hostname, Cloudflare Access cho `/api/admin`, phát hành bản mới, rollback (yank).
- [ ] Không có secret/private key nào trong repo; `.env.example` đầy đủ.

## 10. Ngoài phạm vi v1.0
Delta update, trang admin web, thống kê dạng biểu đồ, kênh beta trên UI, installer MSI, code-signing certificate cho exe (Authenticode).

# MiniTools Hub

Trang web quảng bá + tải các mini tool (Taskbar Monitor, Disk Cleaner, ...) **và** máy chủ cập nhật để app tự update.
Chạy bằng Docker trên VPS Linux, public qua **Cloudflare Tunnel** tại `https://mth.mimehub.top` (không mở port nào trên VPS).
Spec đầy đủ: [`PLAN.md`](./PLAN.md).

**Bảo mật số 1:** app chỉ cài bản cập nhật có **chữ ký ECDSA hợp lệ**. Private key chỉ nằm ở máy dev (`%USERPROFILE%\.minitools\signing-key.pem`); server chỉ giữ public key nên bị chiếm cũng không đẩy được bản độc hại.

## Cấu trúc

```text
MiniToolsHub/
├── src/MiniToolsHub.Server/   # ASP.NET Core 10: Minimal API + Razor Pages (VI/EN), SQLite + Dapper
├── tests/MiniToolsHub.Server.Tests/
├── content/tools/<appId>/     # meta.json, vi.md, en.md, icon.png, shots/ (deploy cùng image)
├── deploy/                    # Dockerfile, docker-compose.yml, .env.example, Dockerfile.dockerignore
├── scripts/deploy-server.ps1  # đóng gói + đẩy lên VPS qua SSH
└── tmp/                       # (git-ignored) screenshot, dữ liệu dev
```
Script dùng chung ở workspace root: `scripts/new-signing-key.ps1`, `scripts/release.ps1`, `scripts/tools/ManifestTool`.

## 1. Chạy local

```powershell
dotnet run --project MiniToolsHub/src/MiniToolsHub.Server      # http://localhost:18473
dotnet test MiniToolsHub/tests/MiniToolsHub.Server.Tests
```
Môi trường Development dùng `MiniToolsHub/tmp/data` làm DataDir (git-ignored). Mở `http://localhost:18473/vi` hoặc `/en`.

Để thử upload local, cần token admin và public key (xem mục 2, 3) rồi đặt biến môi trường:
```powershell
$env:Hub__AdminTokenSha256 = '<sha256 của token>'
$env:Hub__PublicKeyPem = Get-Content C:\path\public.pem -Raw
dotnet run --project MiniToolsHub/src/MiniToolsHub.Server
```

## 2. Tạo khóa ký (1 lần)

```powershell
./scripts/new-signing-key.ps1
```
- Private key lưu ở `%USERPROFILE%\.minitools\signing-key.pem`. **Sao lưu nó** (password manager / offline). Mất key = các app đã phát hành không nhận được update nữa.
- Script in public key, ghi vào `Shared/MiniTools.Updater/PublicKey.cs` nếu thư mục đó tồn tại, và in sẵn dòng `Hub__PublicKeyPem=...` (một dòng, dùng `\n` chữ) để dán vào `.env`.
- Không ghi đè key cũ trừ khi có `-Force`.

Tạo admin token + hash:
```powershell
dotnet run --project scripts/tools/ManifestTool -- token
# TOKEN=...   -> giữ trên máy dev:  $env:MINITOOLS_ADMIN_TOKEN
# SHA256=...  -> đặt vào .env:      Hub__AdminTokenSha256
```

## 3. Cấu hình `.env` và chạy Docker

Port/tên đã chọn riêng để tránh trùng với service khác trên VPS:

| Thứ | Giá trị |
|---|---|
| Port app trong container | `18473` |
| Port trên host (chỉ localhost) | `127.0.0.1:${HUB_HOST_PORT:-18473}` |
| Compose project | `minitools-hub` |
| Container | `minitools-hub-app`, `minitools-hub-tunnel` |
| Network | `minitools-hub-net` |
| Image | `minitools-hub:latest` |
| Dữ liệu | `MiniToolsHub/deploy/data` (SQLite `hub.db` + `files/{appId}/{version}/*.zip`) |

Trên VPS, kiểm tra trùng port/tên trước khi chạy:
```bash
ss -tlnp | grep 18473
docker ps --format '{{.Names}}' | grep minitools-hub
```
Nếu trùng, đổi `HUB_HOST_PORT` trong `.env`.

```bash
cd MiniToolsHub/deploy
cp .env.example .env     # điền TUNNEL_TOKEN, Hub__AdminTokenSha256, Hub__PublicKeyPem; pin CLOUDFLARED_TAG
mkdir -p data && sudo chown -R 1654:1654 data   # container chạy user "app" (uid 1654)
docker compose up -d --build
```
`Hub__PublicBaseUrl` mặc định là `https://mth.mimehub.top`. Có thể thay `MiniToolsHub/deploy/.env` bằng giá trị khác khi đổi domain.

Từ máy dev có thể dùng script (cần `.env` đã có sẵn trên VPS; script không bao giờ gửi `.env` hay private key):
```powershell
./MiniToolsHub/scripts/deploy-server.ps1 -Host user@vps -Path /opt/minitools-hub
```
Thêm `-DryRun` để chỉ in các bước.

## 4. Cloudflare Tunnel (làm trên dashboard)

1. Zero Trust -> Networks -> **Tunnels** -> Create tunnel (loại Cloudflared). Copy token vào `TUNNEL_TOKEN` trong `.env`.
2. Tab **Public Hostname** -> Add: Hostname `mth.mimehub.top` -> Service **HTTP** `http://minitools-hub-app:18473`.
3. Khuyến nghị: thêm **Cloudflare Access** (hoặc WAF rule) chặn `/api/admin/*` ngoài IP nhà/công ty, lớp bảo vệ thứ hai ngoài token.
4. Cloudflare Free giới hạn body request 100 MB, nên `Hub__MaxUploadMb` mặc định 95. Cloudflare có thể cache file `.zip` (header `immutable`), nên số lượt tải có thể thấp hơn thực tế.
5. Luôn pin `CLOUDFLARED_TAG` theo phiên bản cụ thể khi deploy thật.

Server tin `X-Forwarded-*` từ cloudflared (mạng docker riêng) và dùng `CF-Connecting-IP` để rate limit (admin 10 req/phút, public 120 req/phút). Vì cổng host bind `127.0.0.1`, không ai ngoài tunnel gửi được header giả.

## 5. Phát hành bản mới

```powershell
$env:MINITOOLS_HUB_URL     = 'https://mth.mimehub.top'   # mặc định, có thể bỏ qua
$env:MINITOOLS_ADMIN_TOKEN = '<token>'
./scripts/release.ps1 -App DiskCleaner -NotesVi 'Sửa lỗi ...' -NotesEn 'Fixed ...'
```
Các bước: đọc version từ csproj -> chạy publish của app -> chép `MiniTools.UpdateHelper.exe` vào zip -> tính sha256 -> dựng manifest -> **ký** -> `POST /api/admin/releases`.
- `-DryRun`: làm hết trừ upload, in manifest.
- `-SkipPublish`: dùng lại zip có sẵn trong `releases/`.
- `-KeyPath` / `$env:MINITOOLS_SIGNING_KEY`: dùng khóa khác (vd khóa test).
- Version phải tăng; trùng version trả 409.
- Với TaskbarMonitor, script gọi `TaskbarMonitor/scripts/publish.ps1`.

Kiểm tra: `https://mth.mimehub.top/api/apps/disk-cleaner/latest`.

### Cách app tự cập nhật (giai đoạn B)

1. `MiniTools.Updater` (thư viện dùng chung) gọi `GET /api/apps/{appId}/latest?current=...`; chấp nhận manifest chỉ khi **chữ ký hợp lệ với public key nhúng trong app** (`Shared/MiniTools.Updater/PublicKey.cs`), đúng appId/channel, version cao hơn.
2. Người dùng bấm **Cập nhật ngay** -> tải `/dl/{appId}/{version}/{file}` về `%LOCALAPPDATA%\MiniTools\updates\...`, kiểm tra size + sha256 (sai thì xóa file), giải nén chống zip-slip.
3. `MiniTools.UpdateHelper.exe` (chép ra thư mục tạm) chờ app thoát, backup file sắp bị ghi đè, chép file mới, **rollback nếu lỗi**, rồi mở lại app. Không xóa file ngoài gói (vd `settings.json`). Log: `%LOCALAPPDATA%\MiniTools\updates\helper.log`.
4. Auto-check nền: 10 giây sau khi mở app, tối đa 1 lần/24h (DiskCleaner hoãn khi đang quét/dọn). appId: `disk-cleaner`, `taskbar-monitor`. URL hub nằm ở `UpdateConfig.HubBaseUrl` của từng app.

**Placeholder = tắt cập nhật.** `PublicKey.cs` mặc định là chuỗi rỗng; khi đó app không kiểm tra và không cài bất cứ gì. Chạy `scripts/new-signing-key.ps1` (ghi public key vào `PublicKey.cs`), đặt `Hub__PublicKeyPem`, build lại và phát hành app (DiskCleaner 1.3.0, TaskbarMonitor 1.1.0 là các bản đầu có updater) để người dùng cập nhật được từ bản đó trở đi.

Test cục bộ: build app ở cấu hình Debug với public key test và đặt `MINITOOLS_UPDATE_HUB_URL=http://localhost:18473` (chỉ Debug mới đọc biến này); `MINITOOLS_DATA_DIR` chuyển thư mục dữ liệu updater sang thư mục tạm. Không commit key test.

## 6. Rollback (yank)

Đánh dấu một bản là lỗi để app không còn nhận nó và link tải bị khóa (file vẫn còn trên đĩa):
```powershell
curl.exe -X POST -H "Authorization: Bearer $env:MINITOOLS_ADMIN_TOKEN" https://mth.mimehub.top/api/admin/releases/disk-cleaner/1.3.0/yank
```
`latest` tự trả về bản cũ hơn gần nhất. Cache Cloudflare cho `latest` tối đa 5 phút.

## API

| Method | Path | Ghi chú |
|---|---|---|
| GET | `/api/apps` | Catalog + bản mới nhất |
| GET | `/api/apps/{appId}/latest?channel=stable&current=1.1.0` | manifest; `204` nếu đã mới nhất; `404` app lạ |
| GET | `/api/apps/{appId}/releases` | lịch sử (tối đa 50) |
| GET | `/dl/{appId}/{version}/{fileName}` | tải zip (Range, đếm lượt/ngày) |
| POST | `/api/admin/releases` | multipart `manifest` + `file`; Bearer token; kiểm tra sha256 + chữ ký |
| POST | `/api/admin/releases/{appId}/{version}/yank` | rollback |
| GET | `/healthz`, `/sitemap.xml`, `/robots.txt` | |

Server không lưu IP/User-Agent, chỉ đếm lượt tải theo ngày. Website không dùng cookie hay analytics.

## Thêm tool mới

Tạo `content/tools/<appId>/` gồm `meta.json`, `vi.md`, `en.md`, `icon.png`, `shots/`, rồi thêm app vào map trong `scripts/release.ps1` và build lại image.

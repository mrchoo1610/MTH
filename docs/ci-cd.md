# CI/CD — MiniTools (repo `mrchoo1610/MTH`)

## Tổng quan

| Workflow | Khi nào chạy | Chạy ở đâu | Làm gì |
|---|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | Mọi push (trừ tag) và pull request | GitHub (Windows + Ubuntu) | Build + test toàn bộ `MiniTools.slnx`, build thử Docker image |
| [`deploy-hub.yml`](../.github/workflows/deploy-hub.yml) | Push lên `main` có thay đổi `MiniToolsHub/**` hoặc `Shared/MiniTools.Contracts/**`; chạy tay để rollback | Test + build image trên GitHub, **deploy trên VPS** (self-hosted runner) | Test hub → đẩy image `ghcr.io/mrchoo1610/mth-hub:<sha>` → `docker compose pull && up -d` → health check |
| [`release-app.yml`](../.github/workflows/release-app.yml) | Push tag `disk-cleaner-vX.Y.Z` / `taskbar-monitor-vX.Y.Z` | Build + ký trên GitHub Windows, **upload trên VPS** | Test → build zip (kèm UpdateHelper) → **ký manifest** → tạo GitHub Release → upload lên hub qua `127.0.0.1` |

```text
push main ──► CI ──► deploy-hub: test → image (GHCR) → [VPS runner] compose pull/up → /healthz
push tag  ──► release-app: [GitHub Windows] test → zip → ký (secret) → GitHub Release
                                └──► [VPS runner] curl POST http://127.0.0.1:18473/api/admin/releases
                                                    → app của người dùng tự thấy bản mới
```

Upload bản phát hành đi thẳng vào `127.0.0.1` trên VPS, **không qua Cloudflare** (không bị giới hạn 100 MB, và vẫn chạy được khi `/api/admin` bị chặn bằng Cloudflare Access).

---

## 1. Thiết lập một lần

### 1.1 Repo GitHub
- **Settings → Actions → General → Fork pull request workflows from outside collaborators:** chọn **"Require approval for all outside collaborators"**.
  Lý do: repo public + self-hosted runner. Một PR từ fork có thể sửa workflow để chạy code lạ trên VPS của bạn; bắt buộc duyệt tay chặn được điều này.
- **Settings → Actions → General → Workflow permissions:** để "Read repository contents" (các workflow tự xin quyền cần thiết).
- (Khuyến nghị) **Settings → Branches:** bảo vệ `main` (bắt buộc CI pass trước khi merge).

### 1.2 Khóa ký và token — tạo bằng GitHub Actions (không cần chạy script trên máy)
1. Tạo **fine-grained PAT** (GitHub → Settings → Developer settings → Fine-grained tokens): chỉ repo `mrchoo1610/MTH`, quyền **Secrets**, **Variables**, **Environments**, **Administration**: *Read and write*. Hạn ngắn (vd 7 ngày) là đủ.
2. Lưu PAT vào repo secret **`ADMIN_PAT`** (Settings → Secrets and variables → Actions → New repository secret).
3. **Actions → "Bootstrap signing key & admin token" → Run workflow** (để các ô rotate trống). Workflow [`bootstrap-signing.yml`](../.github/workflows/bootstrap-signing.yml) sẽ:
   - tạo environment `release` và `production` nếu chưa có;
   - tạo khóa ECDSA → secret `SIGNING_KEY_PEM` (env `release`), public key → variable `HUB_PUBLIC_KEY_PEM`, và **commit `Shared/MiniTools.Updater/PublicKey.cs`** lên `main`;
   - tạo admin token → secret `HUB_ADMIN_TOKEN` (env `production`), SHA-256 → variable `HUB_ADMIN_TOKEN_SHA256`;
   - in ra **Job summary** 2 dòng `Hub__PublicKeyPem=…` và `Hub__AdminTokenSha256=…` để dán vào `.env` trên VPS (cả hai đều là giá trị công khai).
4. Chạy lại lần nữa là an toàn: cái gì đã có thì giữ nguyên. Chỉ tick **rotate_signing_key** khi thật sự cần — app đã cài sẽ **không nhận** bản ký bằng khóa mới (phải tải bản mới bằng tay).
5. Xong thì có thể xóa secret `ADMIN_PAT` / thu hồi PAT.

> ⚠️ Private key **chỉ** nằm trong GitHub Secrets (không đọc lại được, không có bản sao). Nếu xóa nhầm secret, các app đã phát hành sẽ không cập nhật được nữa. Muốn có bản sao lưu offline thì dùng cách thủ công bên dưới thay vì workflow.

<details><summary>Cách thủ công (trên máy dev Windows, có bản sao lưu private key)</summary>

```powershell
./scripts/new-signing-key.ps1
dotnet run --project scripts/tools/ManifestTool -- token
```
Rồi tự tạo các secret/variable như bảng dưới và commit `PublicKey.cs`.
</details>

### 1.3 Environments + secrets (workflow ở 1.2 tự tạo; bảng để tham khảo)

| Environment | Deployment rule | Secret | Giá trị |
|---|---|---|---|
| `release` | Tags: `disk-cleaner-v*`, `taskbar-monitor-v*` | `SIGNING_KEY_PEM` | Private key PEM |
| `production` | Branch `main` + các tag trên | `HUB_ADMIN_TOKEN` | Admin token (bản gốc, không phải SHA-256) |

Deployment rule và Required reviewers bạn đặt tay trong Settings → Environments (workflow không đặt).

- Nên bật **Required reviewers = chính bạn** cho `release`: mỗi lần phát hành phải bấm Approve, nên dù ai đó lấy được quyền push tag cũng không tự ký được bản mới.
- Biến (tùy chọn) **Settings → Variables:** `HUB_DEPLOY_DIR` nếu muốn thư mục deploy khác `~/minitools-hub`; `HUB_HOST_PORT` nếu 18473 bị trùng.

> ⚠️ Private key nằm trong GitHub Secrets theo lựa chọn "CI làm hết". Ai chiếm được tài khoản GitHub của bạn có thể phát hành bản độc hại. Hãy bật 2FA cho tài khoản GitHub và Required reviewers cho environment `release`.

### 1.4 VPS: không cần chuẩn bị file nào
Job deploy tự làm hết mỗi lần chạy:
- thư mục deploy: `~/minitools-hub` của user chạy runner (đổi bằng repo variable `HUB_DEPLOY_DIR` nếu muốn);
- **tự sinh `.env`** từ repo variables `HUB_PUBLIC_KEY_PEM`, `HUB_ADMIN_TOKEN_SHA256` (do bước 1.2 tạo) và `HUB_HOST_PORT` (mặc định 18473) — không sửa tay trên VPS;
- kéo image từ GHCR, `docker compose up -d`, kiểm tra `http://127.0.0.1:18473/healthz`.

Chỉ cần user chạy runner có quyền dùng Docker: `sudo usermod -aG docker <user>` rồi restart service runner.

### 1.5 VPS: self-hosted runner
Runner cần label **`mth-hub`** (Settings → Actions → Runners → runner → Labels) và có `docker`, `docker compose`, `curl`, `git`.
Nên chạy runner bằng user riêng (nhóm `docker` gần như tương đương root — đó là lý do bước 1.1 chặn PR từ fork).

### 1.6 Domain (Cloudflare Tunnel)
**Dùng tunnel đang có trên VPS (đơn giản nhất, không cần `TUNNEL_TOKEN`):** Zero Trust → Networks → Tunnels → tunnel hiện tại → **Public Hostname → Add**: `mth.mimehub.top` → Service **HTTP** `localhost:18473`.
- Nếu cloudflared của bạn chạy trong Docker (không phải trên host), `localhost` là chính container đó: hãy nối container cloudflared vào network `minitools-hub-net` (`docker network connect minitools-hub-net <tên-container-cloudflared>`) rồi dùng Service `minitools-hub-app:18473`.
- Nếu tunnel dùng `config.yml`: thêm `- hostname: mth.mimehub.top` / `service: http://localhost:18473` vào `ingress` (trước dòng 404) rồi restart cloudflared.

**Hoặc tunnel riêng cho web này:** tạo tunnel mới, lấy chuỗi sau `--token` trong lệnh Docker mà Cloudflare hiển thị, lưu vào secret **`TUNNEL_TOKEN`** của environment `production`. Job deploy sẽ tự bật container `minitools-hub-tunnel` (Public Hostname trỏ `minitools-hub-app:18473`).

### 1.7 Lần deploy đầu
Actions → **Deploy hub** → Run workflow (hoặc push lên `main`). Xong: `https://mth.mimehub.top/healthz` trả `ok`.

---

## 2. Phát hành bản mới của một app
1. Tăng `<Version>` trong csproj (DiskCleaner: `DiskCleaner/src/DiskCleaner/DiskCleaner.csproj`; TaskbarMonitor: `TaskbarMonitor/TaskbarMonitor.csproj`) và biến `$version` trong `scripts/publish.ps1` của app.
2. Viết ghi chú: `release-notes/<app-id>/<version>.vi.md` và `.en.md`.
3. Commit, push lên `main`, đợi CI xanh.
4. Tag và push:
   ```powershell
   git tag disk-cleaner-v1.3.0
   git push origin disk-cleaner-v1.3.0
   ```
5. Duyệt environment `release` (nếu bật reviewer). Workflow kiểm tra tag khớp version trong csproj, có đủ release notes, rồi build → ký → GitHub Release → upload hub.

## 3. Rollback
- **Hub (web/API):** Actions → **Deploy hub** → Run workflow → `image_tag` = sha của commit cũ.
- **Bản app lỗi:** gọi API yank (app ngừng thấy bản đó, người đã cài vẫn giữ):
  ```bash
  # trên VPS
  curl -X POST -H "Authorization: Bearer $TOKEN" http://127.0.0.1:18473/api/admin/releases/disk-cleaner/1.3.0/yank
  ```
  rồi phát hành bản sửa với version cao hơn.

## 4. Sự cố thường gặp
| Lỗi | Cách xử lý |
|---|---|
| Job deploy treo "Waiting for a runner" | Runner trên VPS tắt hoặc thiếu label `mth-hub`: `sudo ./svc.sh status` |
| `.env is missing` | Tạo `/opt/minitools-hub/deploy/.env` (mục 1.4) |
| `Invalid manifest signature` khi upload | `Hub__PublicKeyPem` trên VPS không khớp private key trong `SIGNING_KEY_PEM` |
| `Tag version != csproj version` | Sửa version trong csproj hoặc tag lại |
| Hub không healthy | Xem log ở bước Health check, hoặc `docker compose logs hub` trên VPS |

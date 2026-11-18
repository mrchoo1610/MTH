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
- Biến (tùy chọn) **Settings → Variables:** `HUB_DEPLOY_DIR` nếu thư mục deploy khác `/opt/minitools-hub/deploy`.

> ⚠️ Private key nằm trong GitHub Secrets theo lựa chọn "CI làm hết". Ai chiếm được tài khoản GitHub của bạn có thể phát hành bản độc hại. Hãy bật 2FA cho tài khoản GitHub và Required reviewers cho environment `release`.

### 1.4 VPS: Docker + thư mục deploy
```bash
sudo mkdir -p /opt/minitools-hub/deploy/data
cd /opt/minitools-hub/deploy
# lấy .env.example từ repo rồi điền: TUNNEL_TOKEN, Hub__AdminTokenSha256, Hub__PublicKeyPem (1 dòng, \n), HUB_HOST_PORT...
curl -fsSL https://raw.githubusercontent.com/mrchoo1610/MTH/main/MiniToolsHub/deploy/.env.example -o .env
nano .env && chmod 600 .env
# kiểm tra port/tên không trùng
ss -tlnp | grep 18473 ; docker ps --format '{{.Names}}' | grep minitools-hub
```

### 1.5 VPS: self-hosted runner
1. Tạo user riêng cho runner, cho vào nhóm `docker`, cấp quyền thư mục deploy:
   ```bash
   sudo useradd -m -s /bin/bash ghrunner
   sudo usermod -aG docker ghrunner
   sudo chown -R ghrunner:ghrunner /opt/minitools-hub
   ```
   (Nhóm `docker` gần như tương đương root trên VPS — đó là lý do phải chặn PR từ fork ở bước 1.1.)
2. GitHub repo → **Settings → Actions → Runners → New self-hosted runner → Linux x64**, làm theo lệnh hiển thị (chạy bằng user `ghrunner`). Khi `config.sh` hỏi labels, nhập: **`mth-hub`**.
3. Cài thành service để tự chạy lại sau reboot:
   ```bash
   sudo ./svc.sh install ghrunner && sudo ./svc.sh start
   ```
4. Runner cần: `docker`, `docker compose`, `curl`, `git` (để checkout).

### 1.6 Cloudflare Tunnel
Zero Trust → Networks → Tunnels → tunnel của bạn → **Public Hostname**: `mth.mimehub.top` → Service `http://minitools-hub-app:18473`.
(Tùy chọn) Cloudflare Access chặn `https://mth.mimehub.top/api/admin/*` — CI không bị ảnh hưởng vì upload đi qua `127.0.0.1`.

### 1.7 Lần deploy đầu
Push lên `main` (hoặc **Actions → Deploy hub → Run workflow**). Sau khi xong: `https://mth.mimehub.top/healthz` trả `ok`.

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

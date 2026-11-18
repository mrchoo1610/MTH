<#
.SYNOPSIS
  Build -> zip -> sha256 -> sign manifest -> upload one app to MiniToolsHub.
.EXAMPLE
  $env:MINITOOLS_HUB_URL = 'https://mth.mimehub.top'
  $env:MINITOOLS_ADMIN_TOKEN = '<token>'
  ./scripts/release.ps1 -App DiskCleaner -NotesVi 'Sua loi ...' -NotesEn 'Fixed ...'
.PARAMETER App
  DiskCleaner | TaskbarMonitor
.PARAMETER Channel
  stable (default) | beta
.PARAMETER DryRun
  Does everything except the upload; prints the signed manifest.
.PARAMETER SkipPublish
  Reuse the existing zip in releases/ instead of running the app's publish script.
.PARAMETER KeyPath
  Private signing key PEM. Default: $env:MINITOOLS_SIGNING_KEY or %USERPROFILE%\.minitools\signing-key.pem.
.PARAMETER HubUrl
  Hub base URL. Default: $env:MINITOOLS_HUB_URL, else https://mth.mimehub.top.
.PARAMETER AdminToken
  Admin token. Default: $env:MINITOOLS_ADMIN_TOKEN (prefer the env var so it stays out of shell history).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('DiskCleaner', 'TaskbarMonitor')][string]$App,
    [ValidateSet('stable', 'beta')][string]$Channel = 'stable',
    [string]$NotesVi,
    [string]$NotesEn,
    [switch]$DryRun,
    [switch]$SkipPublish,
    [string]$KeyPath,
    [string]$HubUrl,
    [string]$AdminToken
)
$ErrorActionPreference = 'Stop'

$workRoot = Split-Path -Parent $PSScriptRoot
$appIds = @{ TaskbarMonitor = 'taskbar-monitor'; DiskCleaner = 'disk-cleaner' }
$appId = $appIds[$App]

if (-not $KeyPath) {
    if ($env:MINITOOLS_SIGNING_KEY) { $KeyPath = $env:MINITOOLS_SIGNING_KEY }
    else { $KeyPath = Join-Path $env:USERPROFILE '.minitools\signing-key.pem' }
}
if (-not $HubUrl) { $HubUrl = $env:MINITOOLS_HUB_URL }
if (-not $HubUrl) { $HubUrl = 'https://mth.mimehub.top' }   # default production hub
if (-not $AdminToken) { $AdminToken = $env:MINITOOLS_ADMIN_TOKEN }

if (-not (Test-Path $KeyPath)) { throw "Signing key not found: $KeyPath (create it with scripts/new-signing-key.ps1)" }
if (-not $DryRun) {
    if (-not $AdminToken) { throw 'Admin token missing: set MINITOOLS_ADMIN_TOKEN or pass -AdminToken.' }
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

# 1. Version + publish ---------------------------------------------------------------------
$appDir = Join-Path $workRoot $App
$csproj = if ($App -eq 'DiskCleaner') { Join-Path $appDir 'src\DiskCleaner\DiskCleaner.csproj' } else { Join-Path $appDir 'TaskbarMonitor.csproj' }
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Version not found in $csproj" }
Write-Host "== $App v$version ($appId, channel $Channel)" -ForegroundColor Cyan

$zip = Join-Path $workRoot "releases\$App-v$version-Portable.zip"
if (-not $SkipPublish) {
    $publish = Join-Path $appDir 'scripts\publish.ps1'
    if (-not (Test-Path $publish)) { throw "Missing $publish" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
if (-not (Test-Path $zip)) { throw "Zip not found: $zip" }

# 2. Copy UpdateHelper into the zip (phase B builds it; skip with a warning until then) ---
$helperProj = Join-Path $workRoot 'Shared\MiniTools.UpdateHelper\MiniTools.UpdateHelper.csproj'
if (Test-Path $helperProj) {
    $helperOut = Join-Path ([System.IO.Path]::GetTempPath()) ("minitools-helper-" + [guid]::NewGuid().ToString('N'))
    dotnet publish $helperProj -c Release -r win-x64 --self-contained false -o $helperOut --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Publishing UpdateHelper failed.' }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Update')
    try {
        foreach ($f in Get-ChildItem $helperOut -File) {
            $existing = $archive.GetEntry($f.Name)
            if ($existing) { $existing.Delete() }
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $f.Name)
        }
    } finally { $archive.Dispose() }
    Remove-Item $helperOut -Recurse -Force
    Write-Host 'UpdateHelper added to the zip.'
} else {
    Write-Warning 'Shared/MiniTools.UpdateHelper not found (phase B): skipping "copy UpdateHelper into zip". This build cannot self-update yet.'
}

# 3. Manifest + signature -------------------------------------------------------------------
$toolProj = Join-Path $workRoot 'scripts\tools\ManifestTool\ManifestTool.csproj'
dotnet build $toolProj -c Release --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Building ManifestTool failed.' }
$toolDll = Join-Path $workRoot 'scripts\tools\ManifestTool\bin\Release\net10.0\ManifestTool.dll'

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("minitools-release-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $manifestPath = Join-Path $tmp 'manifest.json'
    $signArgs = @('sign', '--app-id', $appId, '--version', $version, '--channel', $Channel, '--zip', $zip, '--key', $KeyPath, '--out', $manifestPath)
    if ($NotesVi) { Write-Utf8NoBom (Join-Path $tmp 'vi.md') $NotesVi; $signArgs += @('--notes-vi-file', (Join-Path $tmp 'vi.md')) }
    if ($NotesEn) { Write-Utf8NoBom (Join-Path $tmp 'en.md') $NotesEn; $signArgs += @('--notes-en-file', (Join-Path $tmp 'en.md')) }
    & dotnet $toolDll @signArgs
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }

    Write-Host ''
    Write-Host (Get-Content $manifestPath -Raw -Encoding UTF8)

    # Local sanity check with the matching public key derived from the same private key is not possible
    # (the tool only holds the private key); the server verifies the signature on upload.

    if ($DryRun) {
        Write-Host '-DryRun: upload skipped.' -ForegroundColor Yellow
        $keep = Join-Path $workRoot "releases\$App-v$version-Portable.manifest.json"
        Copy-Item $manifestPath $keep -Force
        Write-Host "Manifest copy: $keep"
        return
    }

    # 4. Upload (token goes through a temp curl config file so it never appears in a process list) ----
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if (-not $curl) { throw 'curl.exe not found (included in Windows 10 1803+).' }
    $cfg = Join-Path $tmp 'curl.cfg'
    Write-Utf8NoBom $cfg ("header = `"Authorization: Bearer $AdminToken`"`n")
    $url = $HubUrl.TrimEnd('/') + '/api/admin/releases'
    $body = Join-Path $tmp 'response.txt'
    $code = & curl.exe -sS -K $cfg -o $body -w '%{http_code}' -F "manifest=<$manifestPath;type=application/json" -F "file=@$zip;type=application/zip" $url
    if ($LASTEXITCODE -ne 0) { throw "curl failed (exit $LASTEXITCODE)." }
    $text = if (Test-Path $body) { Get-Content $body -Raw -Encoding UTF8 } else { '' }
    if ($code -eq '201') {
        Write-Host "Published $appId $version -> $($HubUrl.TrimEnd('/'))/api/apps/$appId/latest" -ForegroundColor Green
    } else {
        throw "Upload failed: HTTP $code $text"
    }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

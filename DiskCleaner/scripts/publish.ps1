# Publish DiskCleaner (framework-dependent, win-x64) và đóng gói bản Portable.
# Chạy: powershell -ExecutionPolicy Bypass -File DiskCleaner/scripts/publish.ps1
$ErrorActionPreference = 'Stop'

$version  = '1.3.0'
$appRoot  = Split-Path -Parent $PSScriptRoot          # .../DiskCleaner
$workRoot = Split-Path -Parent $appRoot               # .../mini-tool-indie
$dist     = Join-Path $appRoot 'dist'
$name     = "DiskCleaner-v$version-Portable"
$release  = Join-Path $workRoot "releases\$name"
$zip      = Join-Path $workRoot "releases\$name.zip"

Push-Location $appRoot
try {
    # dist và thư mục portable chỉ chứa output build, an toàn để tạo lại.
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    dotnet publish src/DiskCleaner/DiskCleaner.csproj -c Release -r win-x64 --self-contained false -o $dist
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish thất bại (exit $LASTEXITCODE)" }

    if (Test-Path $release) { Remove-Item $release -Recurse -Force }
    New-Item -ItemType Directory -Force $release | Out-Null
    Copy-Item (Join-Path $dist '*') $release -Recurse -Force
    Copy-Item (Join-Path $appRoot 'README.md') $release -Force

    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip -CompressionLevel Optimal

    Write-Host "Portable folder: $release"
    Write-Host "Portable zip:    $zip"
}
finally {
    Pop-Location
}

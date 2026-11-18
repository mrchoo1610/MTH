# Publish TaskbarMonitor (framework-dependent, win-x64) and package a Portable zip into ../releases.
# Run: powershell -ExecutionPolicy Bypass -File TaskbarMonitor/scripts/publish.ps1
# Output goes to TaskbarMonitor/tmp/publish (git-ignored) so the existing dist/ folder is left untouched.
$ErrorActionPreference = 'Stop'

$appRoot  = Split-Path -Parent $PSScriptRoot          # .../TaskbarMonitor
$workRoot = Split-Path -Parent $appRoot               # .../mini-tool-indie
$csproj   = Join-Path $appRoot 'TaskbarMonitor.csproj'
$version  = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in TaskbarMonitor.csproj' }

$stage   = Join-Path $appRoot 'tmp\publish'
$name    = "TaskbarMonitor-v$version-Portable"
$release = Join-Path $workRoot "releases\$name"
$zip     = Join-Path $workRoot "releases\$name.zip"

Push-Location $appRoot
try {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    dotnet publish $csproj -c Release -r win-x64 --self-contained false -o $stage
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    if (Test-Path $release) { Remove-Item $release -Recurse -Force }
    New-Item -ItemType Directory -Force $release | Out-Null
    Copy-Item (Join-Path $stage '*') $release -Recurse -Force
    Copy-Item (Join-Path $appRoot 'README.md') $release -Force

    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip -CompressionLevel Optimal

    Write-Host "Portable folder: $release"
    Write-Host "Portable zip:    $zip"
}
finally {
    Pop-Location
}

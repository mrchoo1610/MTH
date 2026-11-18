<#
.SYNOPSIS
  Packs the sources and (re)deploys MiniToolsHub on a VPS over SSH with docker compose.
.EXAMPLE
  ./MiniToolsHub/scripts/deploy-server.ps1 -Host user@my-vps -Path /opt/minitools-hub
.NOTES
  - Never copies .env, private keys, bin/obj, data or tmp. Create MiniToolsHub/deploy/.env on the VPS once by hand.
  - Needs ssh, scp and tar on this machine (all ship with Windows 10+), docker + docker compose on the VPS.
  - -DryRun prints what would happen without touching the network.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('Host')][string]$SshTarget,
    [string]$Path = '/opt/minitools-hub',
    [int]$SshPort = 22,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

if ($SshTarget -notmatch '^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+$') { throw "Invalid -Host '$SshTarget' (expected user@host)." }
if ($Path -notmatch '^/[A-Za-z0-9_./-]+$' -or $Path -match '\.\.') { throw "Invalid -Path '$Path'." }

$workRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tgz = Join-Path ([System.IO.Path]::GetTempPath()) ("minitools-hub-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.tgz')

$tarArgs = @('-czf', $tgz,
    '--exclude=bin', '--exclude=obj', '--exclude=data', '--exclude=tmp', '--exclude=.env', '--exclude=*.pem',
    '-C', $workRoot, 'Shared', 'MiniToolsHub', 'MiniTools.slnx')

$remote = @(
    "set -e",
    "mkdir -p '$Path'",
    "tar -xzf '$Path/upload.tgz' -C '$Path'",
    "rm -f '$Path/upload.tgz'",
    "cd '$Path'",
    "test -f MiniToolsHub/deploy/.env || { echo 'Missing MiniToolsHub/deploy/.env on the VPS (copy .env.example and fill it).'; exit 1; }",
    "mkdir -p MiniToolsHub/deploy/data",
    # the container runs as uid 1654 (user 'app'); make the data volume writable for it
    "docker run --rm -v `"`$PWD/MiniToolsHub/deploy/data:/data`" busybox chown -R 1654:1654 /data",
    "docker compose -f MiniToolsHub/deploy/docker-compose.yml up -d --build",
    "docker compose -f MiniToolsHub/deploy/docker-compose.yml ps"
) -join '; '

if ($DryRun) {
    Write-Host "[DryRun] tar $($tarArgs -join ' ')"
    Write-Host "[DryRun] scp -P $SshPort <tgz> ${SshTarget}:$Path/upload.tgz   (after mkdir -p)"
    Write-Host "[DryRun] ssh -p $SshPort $SshTarget <remote>:"
    Write-Host $remote
    return
}

& tar @tarArgs
if ($LASTEXITCODE -ne 0) { throw 'tar failed.' }
try {
    & ssh -p $SshPort $SshTarget "mkdir -p '$Path'"
    if ($LASTEXITCODE -ne 0) { throw 'ssh mkdir failed.' }
    & scp -P $SshPort $tgz "${SshTarget}:$Path/upload.tgz"
    if ($LASTEXITCODE -ne 0) { throw 'scp failed.' }
    & ssh -p $SshPort $SshTarget $remote
    if ($LASTEXITCODE -ne 0) { throw 'Remote deploy failed.' }
    Write-Host 'Deployed. Check: https://mth.mimehub.top/healthz' -ForegroundColor Green
} finally {
    Remove-Item $tgz -Force -ErrorAction SilentlyContinue
}

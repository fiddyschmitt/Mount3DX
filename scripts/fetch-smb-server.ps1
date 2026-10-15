<#
.SYNOPSIS
  Fetches the portable-smb-server executable that Mount3DX embeds.

.DESCRIPTION
  Reads scripts/smb-server.json (release tag, asset name, SHA-256) and puts the binary at
  tools/portable-smb-server/portable-smb-server.exe, which Mount3DX.csproj embeds as a resource when it
  is present. The folder is gitignored, so run this once after cloning and again whenever the pinned
  version changes; the build says loudly when the binary is missing.

  -FromLocal copies the binary out of a local build of the Go repo (its build.ps1 output folder)
  instead of downloading, for work on both sides at once.

.EXAMPLE
  .\scripts\fetch-smb-server.ps1
  .\scripts\fetch-smb-server.ps1 -FromLocal ..\..\go\portable-smb-server\bin
#>
[CmdletBinding()]
param(
    [string]$FromLocal,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
# resolved here rather than in a parameter default: Windows PowerShell 5.1 has no $PSScriptRoot yet at that point
if (-not $OutDir) { $OutDir = Join-Path (Join-Path $PSScriptRoot '..') 'tools\portable-smb-server' }
$manifest = Get-Content (Join-Path $PSScriptRoot 'smb-server.json') -Raw | ConvertFrom-Json

New-Item -ItemType Directory -Force $OutDir | Out-Null
$dest = Join-Path $OutDir 'portable-smb-server.exe'

if ($FromLocal) {
    $source = Join-Path $FromLocal $manifest.asset
    if (-not (Test-Path $source)) { throw "$source not found (build the Go repo first: .\build.ps1)" }
    Copy-Item $source $dest -Force
    Write-Host "copied $source -> $dest"
    return
}

$url = "https://github.com/$($manifest.repo)/releases/download/$($manifest.version)/$($manifest.asset)"
Write-Host "downloading $url"
$temp = "$dest.download"
Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
$hash = (Get-FileHash $temp -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $manifest.sha256.ToLowerInvariant()) {
    Remove-Item $temp -Force
    throw "$($manifest.asset): SHA-256 $hash does not match smb-server.json ($($manifest.sha256))"
}
Move-Item $temp $dest -Force
Write-Host "sha256 ok; binary at $dest"

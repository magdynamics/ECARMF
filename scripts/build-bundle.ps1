<#
.SYNOPSIS
  TCEL P4.3 — assemble a delivery bundle FROM standalone files.

.DESCRIPTION
  A bundle is never authored independently; it is always derived from the
  exact standalone files, so it can never silently diverge from them (the
  T9-035 failure, where a bundle held a different draft than its standalone
  upload). This script zips the given files verbatim and writes a SHA-256
  checksum manifest alongside the archive.

.PARAMETER Path
  One or more standalone files to include (the exact bytes shipped).

.PARAMETER Output
  Path of the .zip bundle to produce.

.EXAMPLE
  ./scripts/build-bundle.ps1 -Path packages/ai-magdynamics-v1.json,packages/README.md -Output out/magdynamics-bundle.zip
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = 'Stop'

$resolved = @()
foreach ($p in $Path) {
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) {
        throw "Not a file: $p"
    }
    $resolved += (Resolve-Path -LiteralPath $p).Path
}

$outDir = Split-Path -Parent $Output
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Force }

# Compress the exact standalone files — no repackaging, no re-authoring.
Compress-Archive -LiteralPath $resolved -DestinationPath $Output -Force

# Emit a checksum manifest so the bundle's contents are verifiable against
# the standalone files at any later point.
$checksumPath = "$Output.sha256.txt"
$lines = foreach ($f in $resolved) {
    $hash = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash
    "{0}  {1}" -f $hash, (Split-Path -Leaf $f)
}
$lines | Set-Content -LiteralPath $checksumPath -Encoding utf8

Write-Host "Bundle written: $Output ($($resolved.Count) file(s))"
Write-Host "Checksums:      $checksumPath"

<#
.SYNOPSIS
  Promote ECARMF from the :8080 test process to the durable :5099 Windows
  service. Run in an ELEVATED PowerShell (Start-Service requires admin).

.DESCRIPTION
  Does only the mechanical infrastructure steps. It does NOT issue access keys
  or configure AI — those are deliberate, credential-bearing actions you do
  yourself (see deploy\RUNBOOK-golive-and-ai.md). With -LockDown, make sure you
  have issued an operator key first, or you will be locked out.

.PARAMETER LockDown
  Set Security:AllowHeaderIdentity=false (production key-only mode).

.PARAMETER RepointShortcut
  Update the desktop shortcut to point at :5099.

.PARAMETER Unlock
  Reverse of -LockDown: set AllowHeaderIdentity=true and restart the service
  (use to return to open test mode).
#>
[CmdletBinding()]
param(
  [switch]$LockDown,
  [switch]$RepointShortcut,
  [switch]$Unlock
)

$ErrorActionPreference = 'Stop'
$AppDir     = 'C:\ECARMF\app'
$AppSettings= Join-Path $AppDir 'appsettings.json'
$ServiceName= 'ECARMF'
$Port       = 5099
$Shortcut   = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ECARMF Platform.url'

function Assert-Admin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  $pr = New-Object Security.Principal.WindowsPrincipal($id)
  if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must run in an ELEVATED PowerShell (Run as Administrator)."
  }
}

function Set-HeaderIdentity([bool]$allow) {
  $json = Get-Content $AppSettings -Raw
  $repl = '"AllowHeaderIdentity": ' + $allow.ToString().ToLower()
  $json = [regex]::Replace($json, '"AllowHeaderIdentity":\s*(true|false)', $repl)
  Set-Content -Path $AppSettings -Value $json -Encoding utf8
  Write-Host "  set AllowHeaderIdentity=$($allow.ToString().ToLower())"
}

function Stop-TestProcess {
  $p = (Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue).OwningProcess
  if ($p) { Stop-Process -Id ($p | Select-Object -First 1) -Force; Write-Host "  stopped :8080 test process ($p)" }
  else { Write-Host "  no :8080 test process running" }
}

function Wait-Healthy {
  for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep 3
    try {
      $r = Invoke-WebRequest "http://localhost:$Port/auth-mode" -UseBasicParsing -TimeoutSec 3
      if ($r.StatusCode -eq 200) { return $r.Content }
    } catch {}
  }
  return $null
}

Assert-Admin
Write-Host "ECARMF go-live" -ForegroundColor Cyan

if ($Unlock) {
  Set-HeaderIdentity $true
  Restart-Service $ServiceName -Force
  $health = Wait-Healthy
  Write-Host "  service restarted in OPEN test mode. auth-mode: $health"
  return
}

if ($LockDown) { Set-HeaderIdentity $false }

Stop-TestProcess

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { throw "Service '$ServiceName' is not installed. Run deploy\install-windows-service.ps1 first." }
if ($svc.Status -ne 'Running') { Start-Service $ServiceName } else { Restart-Service $ServiceName -Force }

$health = Wait-Healthy
if (-not $health) { throw "Service did not answer on :$Port. Check: Get-EventLog -LogName Application -Source ECARMF*." }
Write-Host "  service is UP on :$Port. auth-mode: $health" -ForegroundColor Green

if ($RepointShortcut) {
  Set-Content -Path $Shortcut -Value "[InternetShortcut]`r`nURL=http://localhost:$Port/?tenant=platform`r`nIconIndex=0" -Encoding ascii
  Write-Host "  desktop shortcut repointed to :$Port"
}

Write-Host ""
Write-Host "Done. Next:" -ForegroundColor Cyan
Write-Host "  - Open the desktop shortcut and sign in with an operator access key."
if (-not $LockDown) { Write-Host "  - Still in OPEN mode. Re-run with -LockDown once keys are issued." }

# Registers the nightly database backup as a scheduled task (daily 02:00,
# SYSTEM). Run ELEVATED. Re-running replaces the existing registration.
$ErrorActionPreference = 'Stop'
# Fail fast when not elevated (task/event-source registration needs admin).
$pr = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an ELEVATED PowerShell (Run as Administrator).' }


$taskName = 'ECARMF-nightly-backup'
$script   = Join-Path $PSScriptRoot 'backup-nightly.ps1'
if (-not (Test-Path $script)) { throw "backup-nightly.ps1 not found next to this script." }

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
$trigger   = New-ScheduledTaskTrigger -Daily -At 02:00
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null

$task = Get-ScheduledTask -TaskName $taskName
Write-Host "Registered '$taskName' - state: $($task.State), next run: $((Get-ScheduledTaskInfo $taskName).NextRunTime)"
Write-Host "Manual run:   Start-ScheduledTask '$taskName'"
Write-Host "Restore drill: powershell -File `"$script`" -VerifyRestore   (quarterly)"

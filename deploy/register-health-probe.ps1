# Registers a 5-minute health probe of the platform as a scheduled task.
# On failure it writes a Windows Application-event-log Error (source
# ECARMF-Monitor, EventId 1001) and appends C:\ECARMF\logs\health.log; on
# recovery it writes an Information event (EventId 1000). Run ELEVATED
# (task + event-source registration need admin). Re-running replaces the task.
$ErrorActionPreference = 'Stop'
# Fail fast when not elevated (task/event-source registration needs admin).
$pr = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an ELEVATED PowerShell (Run as Administrator).' }


$taskName  = 'ECARMF-health-probe'
$probePath = 'C:\ECARMF\probe-health.ps1'

# The probe script itself (written to a stable path so the task survives
# repo moves). Tries the durable service port first, then the test process.
@'
$log = 'C:\ECARMF\logs\health.log'
$state = 'C:\ECARMF\logs\health.state'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$up = $false
foreach ($port in 5099, 8080) {
    try {
        $r = Invoke-WebRequest "http://localhost:$port/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $up = $true; $hit = $port; break }
    } catch {}
}
$wasUp = (Test-Path $state) -and ((Get-Content $state -ErrorAction SilentlyContinue) -eq 'up')
if ($up) {
    'up' | Set-Content $state
    if (-not $wasUp) {
        Write-EventLog -LogName Application -Source 'ECARMF-Monitor' -EventId 1000 -EntryType Information -Message "ECARMF is healthy again on :$hit."
        "$(Get-Date -Format s) RECOVERED :$hit" | Add-Content $log
    }
} else {
    'down' | Set-Content $state
    Write-EventLog -LogName Application -Source 'ECARMF-Monitor' -EventId 1001 -EntryType Error -Message 'ECARMF health probe FAILED on :5099 and :8080 - the platform is down.'
    "$(Get-Date -Format s) DOWN (5099+8080 unreachable)" | Add-Content $log
}
'@ | Set-Content $probePath -Encoding utf8

# Event source (one-time; needs admin).
if (-not [System.Diagnostics.EventLog]::SourceExists('ECARMF-Monitor')) {
    New-EventLog -LogName Application -Source 'ECARMF-Monitor'
}

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$probePath`""
$trigger   = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes 5)
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount
$settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 2)

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null

Write-Host "Registered '$taskName' (every 5 min). Probe: $probePath"
Write-Host "Alerts land in the Application event log, source ECARMF-Monitor (Error 1001 = down)."

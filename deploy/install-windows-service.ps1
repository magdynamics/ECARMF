# Converts the ECARMF platform from a scheduled task to a true Windows
# service backed by SQL Server Express. Run elevated. Writes the outcome
# to C:\ECARMF\install-result.txt.
$ErrorActionPreference = 'Stop'
$result = 'C:\ECARMF\install-result.txt'

try {
    Add-Type -AssemblyName System.Data
    $master = New-Object System.Data.SqlClient.SqlConnection('Server=localhost\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true')
    $master.Open()
    $cmd = $master.CreateCommand()

    # The service runs as NETWORK SERVICE; it needs to own the app database.
    $cmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'NT AUTHORITY\NETWORK SERVICE')
    CREATE LOGIN [NT AUTHORITY\NETWORK SERVICE] FROM WINDOWS;
"@
    $cmd.ExecuteNonQuery() | Out-Null
    $cmd.CommandText = @"
USE [ECARMF_Kernel];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'NT AUTHORITY\NETWORK SERVICE')
    CREATE USER [NT AUTHORITY\NETWORK SERVICE] FOR LOGIN [NT AUTHORITY\NETWORK SERVICE];
ALTER ROLE db_owner ADD MEMBER [NT AUTHORITY\NETWORK SERVICE];
"@
    $cmd.ExecuteNonQuery() | Out-Null
    $master.Close()

    # BACKUP DATABASE writes from the SQL engine process; the backup script
    # log is written by the task account.
    New-Item -ItemType Directory -Force 'C:\ECARMF\backups', 'C:\ECARMF\logs' | Out-Null
    icacls 'C:\ECARMF\backups' /grant 'NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)M' | Out-Null
    icacls 'C:\ECARMF\logs' /grant 'NETWORK SERVICE:(OI)(CI)M' | Out-Null

    # Retire the scheduled-task deployment.
    Get-Process -Name 'ECARMF.Kernel.Api' -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
    Unregister-ScheduledTask -TaskName 'ECARMF Platform' -Confirm:$false -ErrorAction SilentlyContinue

    # Real Windows service: starts at boot, restarts on failure.
    $svc = Get-Service -Name 'ECARMF' -ErrorAction SilentlyContinue
    if ($svc) { sc.exe delete ECARMF | Out-Null; Start-Sleep -Seconds 2 }
    sc.exe create ECARMF binPath= '"C:\ECARMF\app\ECARMF.Kernel.Api.exe" --urls http://0.0.0.0:5099' start= auto obj= "NT AUTHORITY\NetworkService" DisplayName= "ECARMF Platform" | Out-Null
    sc.exe description ECARMF "ECARMF Platform Kernel - serves the admin UI and API on port 5099." | Out-Null
    sc.exe failure ECARMF reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    Start-Service -Name ECARMF

    # Nightly 02:00 backup, also as NETWORK SERVICE (db_owner covers BACKUP).
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File C:\ECARMF\backup-nightly.ps1'
    $trigger = New-ScheduledTaskTrigger -Daily -At 02:00
    $principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\NETWORK SERVICE' -LogonType ServiceAccount
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)
    Register-ScheduledTask -TaskName 'ECARMF Nightly Backup' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

    Start-Sleep -Seconds 8
    $state = (Get-Service -Name ECARMF).Status
    "OK - service ECARMF is $state; nightly backup task registered" | Set-Content $result -Encoding utf8
} catch {
    "FAILED - $($_.Exception.Message)" | Set-Content $result -Encoding utf8
}

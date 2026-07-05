# Nightly backup of the ECARMF database (SQL Server Express).
# Registered as a scheduled task; keeps the newest 14 backups.
$ErrorActionPreference = 'Stop'
$backupDir = 'C:\ECARMF\backups'
$log = 'C:\ECARMF\logs\backup.log'

try {
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $file = Join-Path $backupDir "ECARMF_Kernel-$stamp.bak"

    Add-Type -AssemblyName System.Data
    $conn = New-Object System.Data.SqlClient.SqlConnection('Server=localhost\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true')
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = 600
    # Express edition does not support WITH COMPRESSION.
    $cmd.CommandText = "BACKUP DATABASE [ECARMF_Kernel] TO DISK='$file' WITH INIT"
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()

    Get-ChildItem $backupDir -Filter 'ECARMF_Kernel-*.bak' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip 14 |
        Remove-Item -Force -Confirm:$false

    "$(Get-Date -Format s) OK $file" | Add-Content $log
} catch {
    "$(Get-Date -Format s) FAILED $($_.Exception.Message)" | Add-Content $log
    exit 1
}

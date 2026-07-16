# Nightly backup of the ECARMF database (SQL Server Express).
# Registered as a scheduled task (deploy\register-backup-task.ps1); keeps the
# newest 14 backups. -VerifyRestore additionally proves the newest backup is
# actually restorable (restores it as ECARMF_Verify, checks a row count, drops
# it) - run that drill manually/quarterly, not nightly.
param([switch]$VerifyRestore)

$ErrorActionPreference = 'Stop'
$backupDir = 'C:\ECARMF\backups'
$log = 'C:\ECARMF\logs\backup.log'

function Invoke-Sql([System.Data.SqlClient.SqlConnection]$conn, [string]$sql, [int]$timeout = 600) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandTimeout = $timeout
    $cmd.CommandText = $sql
    return $cmd
}

try {
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

    Add-Type -AssemblyName System.Data
    $conn = New-Object System.Data.SqlClient.SqlConnection('Server=localhost\SQLEXPRESS;Database=master;Integrated Security=true;TrustServerCertificate=true')
    $conn.Open()

    if ($VerifyRestore) {
        # --- Restore drill: newest backup -> ECARMF_Verify -> sanity check -> drop ---
        $newest = Get-ChildItem $backupDir -Filter 'ECARMF_Kernel-*.bak' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $newest) { throw 'No backup found to verify.' }

        # Logical file names come from the backup itself, target paths must be
        # distinct from the live database's files.
        $files = Invoke-Sql $conn "RESTORE FILELISTONLY FROM DISK='$($newest.FullName)'"
        $reader = $files.ExecuteReader()
        $logical = @{}
        while ($reader.Read()) { $logical[$reader['Type']] = $reader['LogicalName'] }
        $reader.Close()

        $dataPath = Join-Path $backupDir 'ECARMF_Verify.mdf'
        $logPath  = Join-Path $backupDir 'ECARMF_Verify.ldf'
        (Invoke-Sql $conn "IF DB_ID('ECARMF_Verify') IS NOT NULL BEGIN ALTER DATABASE [ECARMF_Verify] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [ECARMF_Verify]; END").ExecuteNonQuery() | Out-Null
        (Invoke-Sql $conn ("RESTORE DATABASE [ECARMF_Verify] FROM DISK='$($newest.FullName)' WITH REPLACE, " +
            "MOVE '$($logical['D'])' TO '$dataPath', MOVE '$($logical['L'])' TO '$logPath'")).ExecuteNonQuery() | Out-Null

        $count = (Invoke-Sql $conn 'SELECT COUNT(*) FROM [ECARMF_Verify].dbo.Tenants').ExecuteScalar()
        (Invoke-Sql $conn 'ALTER DATABASE [ECARMF_Verify] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [ECARMF_Verify]').ExecuteNonQuery() | Out-Null
        $conn.Close()

        if ([int]$count -lt 1) { throw "Restore verification found $count tenants - backup may be unusable." }
        "$(Get-Date -Format s) VERIFY-OK $($newest.Name) ($count tenants restored)" | Add-Content $log
        Write-Host "VERIFY-OK: $($newest.Name) restored with $count tenants, verify DB dropped."
        return
    }

    # --- Nightly backup ---
    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $file = Join-Path $backupDir "ECARMF_Kernel-$stamp.bak"
    # Express edition does not support WITH COMPRESSION.
    (Invoke-Sql $conn "BACKUP DATABASE [ECARMF_Kernel] TO DISK='$file' WITH INIT").ExecuteNonQuery() | Out-Null
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

# Ships a new build to the running platform: stops the ECARMF service,
# mirrors C:\ECARMF\staging into C:\ECARMF\app, starts the service again.
# Run elevated. Writes the outcome to C:\ECARMF\install-result.txt.
$result = 'C:\ECARMF\install-result.txt'
try {
    Stop-Service -Name ECARMF -Force -ErrorAction Stop
    robocopy 'C:\ECARMF\staging' 'C:\ECARMF\app' /MIR /R:3 /W:2 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with code $LASTEXITCODE" }
    Start-Service -Name ECARMF
    Start-Sleep -Seconds 6
    "OK - app updated; service $((Get-Service ECARMF).Status)" | Set-Content $result -Encoding utf8
} catch {
    try { Start-Service -Name ECARMF -ErrorAction SilentlyContinue } catch {}
    "FAILED - $($_.Exception.Message)" | Set-Content $result -Encoding utf8
}

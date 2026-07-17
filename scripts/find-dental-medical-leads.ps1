# ECARMF lead finder — DENTAL / MEDICAL / RCM, from PUBLIC breach + discipline records.
#
# Primary source: HHS OCR "Breach Portal" — every PHI breach affecting 500+ individuals is
# published by law (the "Wall of Shame"). It's a JSF app that gates CSV export behind a
# session, so the clean pattern is: a human downloads the CSV once, this tool ranks it.
#   1. Open https://ocrportal.hhs.gov/ocr/breach/breach_report.jsf
#   2. Filter State = IL (or your target), click "Export to CSV"
#   3. Save as docs\gtm\breach_report.csv
#   4. powershell -File scripts\find-dental-medical-leads.ps1
#
# Optional: state dental/medical board disciplinary CSVs (e.g. IDFPR licensee discipline)
# dropped in the same folder as discipline-*.csv are merged in.
#
# ICP: multi-location dental groups / DSOs / billing companies with a DOCUMENTED PHI breach —
# the sharpest possible pain for a platform whose headline is "the AI runs on your premises,
# patient data never leaves the building."

param(
    [string]$BreachCsv = "$PSScriptRoot\..\docs\gtm\breach_report.csv",
    [int]$Top = 25
)

if (-not (Test-Path $BreachCsv)) {
    Write-Host "Breach CSV not found at: $BreachCsv" -ForegroundColor Yellow
    Write-Host "Download it first (see header of this script), then re-run."
    Write-Host "  https://ocrportal.hhs.gov/ocr/breach/breach_report.jsf  ->  filter State  ->  Export to CSV"
    return
}

$rows = Import-Csv $BreachCsv
# OCR columns: "Name of Covered Entity","State","Covered Entity Type","Individuals Affected",
# "Breach Submission Date","Type of Breach","Location of Breached Information","Web Description"
$dentalMedical = $rows | Where-Object {
    $_.'Covered Entity Type' -match 'Healthcare Provider|Health Plan' -and
    ($_.'Name of Covered Entity' -match 'DENTAL|DDS|ORTHO|ORAL|SMILE|MEDICAL|CLINIC|HEALTH|CARE|BILLING|RCM|FAMILY|GROUP|ASSOC|CENTER')
}

$scored = $dentalMedical | ForEach-Object {
    $affected = [int]($_.'Individuals Affected' -replace '[^\d]', '')
    $date = try { [datetime]$_.'Breach Submission Date' } catch { [datetime]'2000-01-01' }
    $ageDays = ((Get-Date) - $date).TotalDays
    $isDental = $_.'Name of Covered Entity' -match 'DENTAL|DDS|ORTHO|ORAL|SMILE'
    $multiHint = $_.'Name of Covered Entity' -match 'GROUP|ASSOC|PARTNERS|CENTERS|MANAGEMENT|DSO'
    [pscustomobject]@{
        Entity       = $_.'Name of Covered Entity'
        State        = $_.State
        Type         = $_.'Covered Entity Type'
        Affected     = $affected
        BreachType   = $_.'Type of Breach'
        Where        = $_.'Location of Breached Information'
        SubmittedOn  = $date.ToString('yyyy-MM-dd')
        # Urgency: size of breach (log-scaled), recency, dental bullseye, multi-entity hint.
        Score        = [math]::Round(
                          ([math]::Log10([math]::Max(500, $affected)) * 15) +
                          ([math]::Max(0, 40 - $ageDays / 20)) +
                          ($(if ($isDental) { 25 } else { 0 })) +
                          ($(if ($multiHint) { 20 } else { 0 })), 0)
    }
}

$ranked = $scored | Sort-Object Score -Descending | Select-Object -First $Top
$out = "$PSScriptRoot\..\docs\gtm\leads-dental-medical.csv"
$ranked | Export-Csv $out -NoTypeInformation -Encoding UTF8

Write-Host ("{0} dental/medical/RCM breaches matched; top {1} by urgency:" -f $scored.Count, $Top)
$ranked | Select-Object Score, Entity, State, Affected, BreachType, SubmittedOn | Format-Table -AutoSize
Write-Host "Full list: $out"
Write-Host ""
Write-Host "The pitch writes itself: '$($ranked[0].Entity)' had $($ranked[0].Affected) records"
Write-Host "breached ($($ranked[0].BreachType)). ECARMF runs the AI on-premises - PHI never leaves the building."

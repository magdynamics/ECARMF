# ECARMF lead finder — evidence-based prospecting from PUBLIC enforcement data.
# Source 1: City of Chicago Food Inspections (SODA API, no key needed).
# Finds businesses with FAILED inspections in the lookback window, ranks them by
# urgency, and flags MULTI-LOCATION operators (same name, multiple addresses) —
# our exact ICP: multi-entity businesses with documented, public risk exposure.
#
# Usage:  powershell -File scripts\find-risk-exposed-leads.ps1 [-Days 180] [-Top 25]
# Output: console ranking + docs\gtm\leads-chicago-food.csv

param(
    [int]$Days = 180,
    [int]$Top = 25
)

$since = (Get-Date).AddDays(-$Days).ToString('yyyy-MM-dd')
$url = "https://data.cityofchicago.org/resource/4ijn-s7e5.json" +
       "?`$where=results='Fail' AND inspection_date >= '$since'" +
       "&`$select=dba_name,aka_name,address,zip,inspection_date,violations,facility_type,risk" +
       "&`$limit=5000&`$order=inspection_date DESC"

Write-Host "Querying Chicago food inspections (fails since $since)..."
$rows = Invoke-RestMethod -Uri $url -TimeoutSec 60

# Group by business name: multiple failing addresses = multi-location operator.
$groups = $rows | Group-Object { $_.dba_name.Trim().ToUpper() } | ForEach-Object {
    $addresses = ($_.Group | Select-Object -ExpandProperty address -Unique)
    $latest = ($_.Group | Sort-Object inspection_date -Descending | Select-Object -First 1)
    $critical = ($_.Group | Where-Object { $_.violations -match 'CRITICAL|PRIORITY' }).Count
    [pscustomobject]@{
        Business      = $_.Name
        FailedChecks  = $_.Count
        Locations     = $addresses.Count
        LatestFail    = $latest.inspection_date.Substring(0, 10)
        FacilityType  = $latest.facility_type
        CityRisk      = $latest.risk
        CriticalHits  = $critical
        Addresses     = ($addresses -join ' | ')
        # Urgency score: multi-location is the ICP multiplier, repeat fails show
        # a control gap (not bad luck), recency shows an open wound.
        Score         = ($addresses.Count * 40) + ($_.Count * 15) + $critical * 5 +
                        [math]::Max(0, 30 - ([int]((Get-Date) - [datetime]$latest.inspection_date).TotalDays / 6))
    }
}

$ranked = $groups | Sort-Object Score -Descending | Select-Object -First $Top

$outDir = Join-Path $PSScriptRoot '..\docs\gtm'
$csv = Join-Path $outDir 'leads-chicago-food.csv'
$ranked | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8

Write-Host ""
Write-Host ("{0} failing businesses found; top {1} by urgency:" -f $groups.Count, $Top)
$ranked | Select-Object Score, Business, Locations, FailedChecks, CriticalHits, LatestFail |
    Format-Table -AutoSize
Write-Host "Full list with addresses: $csv"
Write-Host ""
Write-Host "Multi-location operators (the prime targets):"
$ranked | Where-Object { $_.Locations -gt 1 } |
    Select-Object Business, Locations, FailedChecks, LatestFail | Format-Table -AutoSize

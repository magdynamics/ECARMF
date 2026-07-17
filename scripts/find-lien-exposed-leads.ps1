# ECARMF lead finder — LIEN-EXPOSED BUSINESSES (federal tax, state tax, UCC, county).
#
# A lien is documented financial distress AND a public record — the sharpest qualifier for a
# business that needs cash-flow visibility, vendor-risk monitoring, and a governed board. A
# business with multiple liens across entities has exactly the control gap ECARMF closes.
#
# Sources (each publishes; formats differ, so this tool consumes downloaded CSVs and ranks +
# cross-references them into one prioritized sheet):
#   - Federal tax liens: IRS liens are filed at the COUNTY recorder; many counties publish.
#       Cook County: https://datacatalog.cookcountyil.gov  (search "lien" -> UCC / tax lien sets)
#   - State tax liens: most state SOS / dept-of-revenue lien lookups export CSV.
#   - UCC filings: Secretary of State UCC search (a UCC lien = a secured creditor; stacking = distress).
#   - Judgment liens / mechanics liens: county recorder.
# Drop any of these as CSVs in docs\gtm\liens\*.csv (any columns; the tool auto-detects a
# business-name column and an amount/date column heuristically), then:
#   powershell -File scripts\find-lien-exposed-leads.ps1
#
# The engine: normalize business names across files -> count liens per business -> a business
# appearing in MULTIPLE lien files or with MULTIPLE filings is the hottest lead (stacked
# distress across creditors = a company that has lost financial control = our buyer).

param(
    [string]$LienDir = "$PSScriptRoot\..\docs\gtm\liens",
    [int]$Top = 30
)

if (-not (Test-Path $LienDir)) { New-Item -ItemType Directory -Force $LienDir | Out-Null }
$files = Get-ChildItem $LienDir -Filter *.csv -ErrorAction SilentlyContinue
if (-not $files) {
    Write-Host "No lien CSVs found in: $LienDir" -ForegroundColor Yellow
    Write-Host "Drop one or more downloaded lien exports there (federal/state tax, UCC, judgments), re-run."
    Write-Host "Cook County catalog (has UCC + tax-lien + sheriff-sale sets): https://datacatalog.cookcountyil.gov"
    return
}

function Guess-Col($row, [string[]]$patterns) {
    foreach ($p in $patterns) {
        $m = $row.PSObject.Properties.Name | Where-Object { $_ -match $p } | Select-Object -First 1
        if ($m) { return $m }
    }
    return $null
}

$all = @()
foreach ($f in $files) {
    $rows = Import-Csv $f.FullName
    if (-not $rows) { continue }
    $nameCol = Guess-Col $rows[0] @('debtor','business','name','taxpayer','company','entity','organization')
    $amtCol  = Guess-Col $rows[0] @('amount','balance','value','total','judgment')
    $dateCol = Guess-Col $rows[0] @('date','filed','recorded')
    if (-not $nameCol) { Write-Host "  (skip $($f.Name): no name-like column)"; continue }
    foreach ($r in $rows) {
        $nm = ($r.$nameCol).ToString().Trim().ToUpper()
        if (-not $nm) { continue }
        $all += [pscustomobject]@{
            Business = ($nm -replace '\s+', ' ')
            Source   = $f.BaseName
            Amount   = if ($amtCol) { [double](($r.$amtCol) -replace '[^\d.]', '') } else { 0 }
            Date     = if ($dateCol) { $r.$dateCol } else { '' }
        }
    }
}

$grouped = $all | Group-Object Business | ForEach-Object {
    $sources = ($_.Group | Select-Object -ExpandProperty Source -Unique)
    $total = ($_.Group | Measure-Object Amount -Sum).Sum
    [pscustomobject]@{
        Business    = $_.Name
        LienCount   = $_.Count
        Sources     = $sources.Count
        SourceList  = ($sources -join ', ')
        TotalAmount = [math]::Round($total, 0)
        # Urgency: stacked liens across MULTIPLE creditor types = lost financial control.
        Score       = ($_.Count * 20) + ($sources.Count * 30) +
                      [math]::Min(30, [math]::Log10([math]::Max(1, $total)) * 4)
    }
}

$ranked = $grouped | Sort-Object Score -Descending | Select-Object -First $Top
$out = "$PSScriptRoot\..\docs\gtm\leads-liens.csv"
$ranked | Export-Csv $out -NoTypeInformation -Encoding UTF8

Write-Host ("{0} businesses across {1} lien file(s); top {2} by distress:" -f $grouped.Count, $files.Count, $Top)
$ranked | Select-Object Score, Business, LienCount, Sources, TotalAmount | Format-Table -AutoSize
Write-Host "Full list: $out"
Write-Host ""
Write-Host "Prime targets appear in MULTIPLE lien sources (stacked creditors = lost control = our buyer)."

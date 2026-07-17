# ECARMF lead finder — ASSET MANAGERS / RIAs / FAMILY OFFICES, from PUBLIC SEC records.
#
# Source: SEC Investment Adviser Public Disclosure (IAPD) / Form ADV. Every registered
# adviser files Form ADV; disciplinary and regulatory "disclosure events" are public. The
# richest bulk feed is the monthly Form ADV dataset (SEC FOIA) — a human downloads it once,
# this tool ranks it. Firms WITH disclosure events, multiple offices, or private-fund/family-
# office structure are our ICP: multi-entity, regulated, and already living under audit.
#
#   1. Download the latest "Investment Adviser Report" / Form ADV data set:
#        https://www.sec.gov/about/opendatasetsshtmlinvestment_adviser  (or IAPD compilation)
#      Save the firm CSV as docs\gtm\form-adv.csv
#   2. powershell -File scripts\find-advisor-family-office-leads.ps1
#
# Also usable standalone: the SEC data API (data.sec.gov) confirmed reachable for targeted
# lookups when you already have a firm/CRD in mind.

param(
    [string]$AdvCsv = "$PSScriptRoot\..\docs\gtm\form-adv.csv",
    [int]$Top = 25,
    [string]$State = ""   # optional filter, e.g. "IL"
)

if (-not (Test-Path $AdvCsv)) {
    Write-Host "Form ADV CSV not found at: $AdvCsv" -ForegroundColor Yellow
    Write-Host "Download the Form ADV firm dataset (see header), save as docs\gtm\form-adv.csv, re-run."
    return
}

$rows = Import-Csv $AdvCsv
# Form ADV columns vary by release; we probe common names defensively.
function Col($row, [string[]]$names) { foreach ($n in $names) { if ($row.PSObject.Properties[$n]) { return $row.$n } } return $null }

$scored = $rows | ForEach-Object {
    $name  = Col $_ @('Primary Business Name','1A-Legal Name','Legal Name','Business Name')
    $st    = Col $_ @('Main Office State','1F1-State','State')
    if (-not $name) { return }
    if ($State -and $st -ne $State) { return }

    $aum   = [double]((Col $_ @('5F2c-Total AUM','Total AUM','Regulatory Assets Under Management')) -replace '[^\d.]', '')
    $offices = [int]((Col $_ @('Number of Offices','1F-Offices')) -replace '[^\d]', '')
    # Disclosure events: any "Yes" in the DRP/disclosure flags is public risk.
    $disc = 0
    foreach ($p in $_.PSObject.Properties) {
        if ($p.Name -match '11[A-K]|Disclosure|DRP|Disciplinary' -and $p.Value -match '^Y') { $disc++ }
    }
    $family = $name -match 'FAMILY|MULTI-FAMILY|PRIVATE WEALTH|LEGACY|CAPITAL PARTNERS|HOLDINGS'
    $privateFund = ($_.PSObject.Properties.Name -match '7B|Private Fund') -and ($_ | Select-Object -ExpandProperty * -ErrorAction SilentlyContinue)

    [pscustomobject]@{
        Firm       = $name
        State      = $st
        AUM        = $aum
        Offices    = $offices
        Disclosures= $disc
        FamilyOffice = [bool]$family
        # Urgency: disclosure events are the wound; multi-office + family-office structure is
        # the ICP fit; AUM sizes the deal. We WANT firms already living under compliance load.
        Score      = [math]::Round(
                        ($disc * 25) +
                        ([math]::Min(30, $offices * 8)) +
                        ($(if ($family) { 20 } else { 0 })) +
                        ([math]::Min(20, [math]::Log10([math]::Max(1e6, $aum)) * 2)), 0)
    }
} | Where-Object { $_ }

$ranked = $scored | Sort-Object Score -Descending | Select-Object -First $Top
$out = "$PSScriptRoot\..\docs\gtm\leads-advisors-family-offices.csv"
$ranked | Export-Csv $out -NoTypeInformation -Encoding UTF8

Write-Host ("{0} advisers scored; top {1} by urgency (disclosure + multi-office + family-office):" -f $scored.Count, $Top)
$ranked | Select-Object Score, Firm, State, Disclosures, Offices, AUM | Format-Table -AutoSize
Write-Host "Full list: $out"

# ECARMF lead finder — PROPERTY MANAGERS / LANDLORDS, from PUBLIC building-violation data.
#
# High-criticality segment: unlike a food cert (~$200, days), a building violation drags liens,
# tenant lawsuits, habitability judgments, and vacancy — expensive and SLOW to undo. That's the
# urgency profile that makes a real buyer. And the data is OPENLY queryable (unlike HIPAA
# breaches), so this front is fully self-serve like restaurants.
#
# Source: City of Chicago Building Violations (SODA API, no key). Confirmed live.
# Signal: a `property_group` (one owner/manager) with OPEN violations across MULTIPLE buildings
# = a multi-entity operator losing control across its portfolio = our exact ICP.
#
# Usage:  powershell -File scripts\find-property-violation-leads.ps1 [-Days 120] [-Top 25]

param([int]$Days = 120, [int]$Top = 25)

$since = (Get-Date).AddDays(-$Days).ToString('yyyy-MM-dd')
$base = "https://data.cityofchicago.org/resource/22u3-xenr.json"

# Group by owner/manager cluster; keep those failing across 2+ buildings.
$q = "?`$where=violation_status='OPEN' AND violation_date >= '$since' AND property_group IS NOT NULL" +
     "&`$select=property_group,count(id) as violations,count(distinct address) as buildings" +
     "&`$group=property_group&`$having=count(distinct address) > 1" +
     "&`$order=violations DESC&`$limit=200"

Write-Host "Querying Chicago building violations (open, since $since)..."
$groups = Invoke-RestMethod -Uri ($base + $q) -TimeoutSec 60

$ranked = $groups | ForEach-Object {
    $v = [int]$_.violations; $b = [int]$_.buildings
    # For each hot group, pull a couple of sample addresses to make the outreach concrete.
    [pscustomobject]@{
        OwnerGroupId = $_.property_group
        OpenViolations = $v
        Buildings = $b
        # Urgency: portfolio spread (multi-building is the ICP) weighted with raw volume.
        Score = ($b * 50) + ($v * 4)
    }
} | Sort-Object Score -Descending | Select-Object -First $Top

# Enrich the top groups with real addresses + violation types for the Risk Briefs.
foreach ($g in $ranked) {
    $detail = Invoke-RestMethod -TimeoutSec 40 -Uri ($base +
        "?`$where=property_group='$($g.OwnerGroupId)' AND violation_status='OPEN'" +
        "&`$select=address,violation_description,violation_date&`$order=violation_date DESC&`$limit=3")
    $g | Add-Member -NotePropertyName SampleAddresses -NotePropertyValue (($detail.address | Select-Object -Unique) -join ' | ')
    $g | Add-Member -NotePropertyName SampleIssues -NotePropertyValue (($detail.violation_description | Select-Object -Unique | Select-Object -First 2) -join ' ; ')
    Start-Sleep -Milliseconds 200
}

$out = "$PSScriptRoot\..\docs\gtm\leads-property.csv"
$ranked | Export-Csv $out -NoTypeInformation -Encoding UTF8

Write-Host ""
Write-Host ("{0} multi-building owners with open violations; top {1}:" -f $groups.Count, $Top)
$ranked | Select-Object Score, OwnerGroupId, Buildings, OpenViolations, SampleAddresses | Format-Table -AutoSize -Wrap
Write-Host "Full list: $out"
Write-Host ""
Write-Host "Note: property_group is the city's owner/manager cluster id. To name the human owner,"
Write-Host "cross-ref the sample address in the Cook County Assessor property-owner lookup (public)."

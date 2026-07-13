<#
.SYNOPSIS
  TCEL P1.2 — offline ID-ledger projection over packages/*.json.

.DESCRIPTION
  The same projection the GET /api/packages/id-ledger endpoint produces, but
  computed from the repo's package files for OFFLINE authoring. Lists every
  declared id per kind with packageId@version provenance, so a follow-on or
  multi-wave package can treat existing ids as reserved. Consuming this (or
  the endpoint) before generating the next wave is a hard authoring rule —
  documentation banners were tried and failed six times.

.PARAMETER PackagesDir
  Directory of package manifests. Defaults to the repo's packages/ folder.

.EXAMPLE
  ./scripts/id-ledger.ps1 | Out-File id-ledger.json
#>
param(
    [string]$PackagesDir = (Join-Path (Join-Path $PSScriptRoot '..') 'packages')
)

$ErrorActionPreference = 'Stop'

$kinds = [ordered]@{
    entities              = { param($m) $m.entities.entityTypeName }
    events                = { param($m) $m.events.eventName }
    rules                 = { param($m) $m.rules.ruleId }
    capabilities          = { param($m) $m.capabilities.capabilityId }
    schemaTemplates       = { param($m) $m.schemaTemplates.templateId }
    performanceFrameworks = { param($m) $m.performanceFrameworks.frameworkId }
    workflows             = { param($m) $m.workflows.workflowId }
    agents                = { param($m) $m.agents.agentId }
    knowledgeAssets       = { param($m) $m.knowledgeAssets.assetId }
    aiExtractionTemplates = { param($m) $m.aiExtractionTemplates.templateId }
}

# kind -> id -> set of provenance strings
$ledger = [ordered]@{}
foreach ($k in $kinds.Keys) { $ledger[$k] = @{} }

foreach ($file in Get-ChildItem -LiteralPath $PackagesDir -Filter '*.json') {
    $m = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (-not $m.packageId) { continue }
    $provenance = "{0}@{1}" -f $m.packageId, $m.packageVersion

    foreach ($k in $kinds.Keys) {
        $ids = & $kinds[$k] $m
        foreach ($id in $ids) {
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            if (-not $ledger[$k].ContainsKey($id)) { $ledger[$k][$id] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$ledger[$k][$id].Add($provenance)
        }
    }
}

# Shape the output like the endpoint: { ids: { kind: [ {id, declaredBy} ] } }
$idsOut = [ordered]@{}
foreach ($k in $kinds.Keys) {
    $entries = foreach ($id in ($ledger[$k].Keys | Sort-Object)) {
        [pscustomobject]@{ id = $id; declaredBy = @($ledger[$k][$id] | Sort-Object) }
    }
    $idsOut[$k] = @($entries)
}

[pscustomobject]@{ ids = [pscustomobject]$idsOut } | ConvertTo-Json -Depth 6

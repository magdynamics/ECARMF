<#
.SYNOPSIS
  Generate an ECARMF knowledge-package manifest from a TCEL spec folder.

.DESCRIPTION
  Bulk onboarding tool for the TCEL (Tenant 9) 67-package spec. The first ten
  packages (T9-001..010) were hand-authored with fully-tuned control rules;
  this generator produces the remaining packages at FAITHFUL STRUCTURE:
    - entities  from each *.schema.json (title -> entityTypeName, properties -> attributes)
    - events    from EVENT-CATALOG.csv (column 1), MINUS any name already claimed
    - controls  -> a full-catalog traceability KnowledgeAsset, PLUS executable rules
                   for the two controls every package shares and that map cleanly to
                   the field-vs-literal condition model: the AI-execution boundary and
                   cross-tenant denial. Detailed per-control rule tuning is a documented
                   follow-up (the hand-authored packages show the target).
    - agent     from AGENT-SPECIFICATION.md (Identity + Mission -> AgentDeclaration)
  A global claimed-names ledger (seeded from existing packages/ai-tcel-*.json and
  updated per generation) guarantees no cross-package event/entity/rule/agent/
  capability collision -- first declarer wins, exactly what the ID-ledger surfaces.

.PARAMETER SpecRoot   Root folder holding the extracted T9 package folders.
.PARAMETER PackagesDir Output packages/ directory.
.PARAMETER Map        Ordered list of "FolderName=slug" to generate (dependency order).
#>
param(
  [Parameter(Mandatory=$true)][string]$SpecRoot,
  [Parameter(Mandatory=$true)][string]$PackagesDir,
  [Parameter(Mandatory=$true)][string[]]$Map
)
$ErrorActionPreference = 'Stop'

# ---- seed claimed-names ledger from existing packages ----
$claimed = @{ events=@{}; entities=@{}; rules=@{}; caps=@{}; agents=@{} }
function Claim($kind,$name){ if($name){ $claimed[$kind][$name.ToLower()] = $true } }
function IsClaimed($kind,$name){ return ($name -and $claimed[$kind].ContainsKey($name.ToLower())) }

# files this run will (re)generate -- don't seed the ledger from them (idempotent reruns)
$regenerating = @{}
foreach($pair in $Map){ $s = ($pair -split '=',2)[1]; $regenerating[("ai-tcel-$s-v1.json").ToLower()] = $true }

foreach($f in Get-ChildItem $PackagesDir -Filter 'ai-tcel-*.json'){
  if($regenerating.ContainsKey($f.Name.ToLower())){ continue }
  try { $m = Get-Content $f.FullName -Raw | ConvertFrom-Json } catch { continue }
  foreach($e in $m.events){ Claim events $e.eventName }
  foreach($e in $m.entities){ Claim entities $e.entityTypeName }
  foreach($r in $m.rules){ Claim rules $r.ruleId }
  foreach($c in $m.capabilities){ Claim caps $c.capabilityId }
  foreach($a in $m.agents){ Claim agents $a.agentId }
}
Write-Host ("seeded ledger: {0} events, {1} entities, {2} agents from existing packages" -f $claimed.events.Count,$claimed.entities.Count,$claimed.agents.Count)

function Find-One($dir,$leaf){ Get-ChildItem $dir -Recurse -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1 }

function Schema-Entities($dir){
  $out = @()
  foreach($sf in Get-ChildItem $dir -Recurse -File -Filter '*.schema.json' -ErrorAction SilentlyContinue){
    if($sf.Name -like 'package-manifest*'){ continue }
    try { $s = Get-Content $sf.FullName -Raw | ConvertFrom-Json } catch { continue }
    # Name from title when present, else PascalCase of the file base name
    # (expansion schemas are minimal stubs with no title/properties).
    if($s.title){ $name = ($s.title -replace '[^A-Za-z0-9]','') }
    else { $name = ((Get-Culture).TextInfo.ToTitleCase((($sf.BaseName -replace '\.schema$','') -replace '[-_]',' ')) -replace '[^A-Za-z0-9]','') }
    if(-not $name -or (IsClaimed entities $name)){ continue }
    $attrs = @()
    if($s.properties){
      foreach($p in $s.properties.PSObject.Properties){
        if($p.Name -eq 'tenantId'){ continue }
        $t = $p.Value.type; if($t -is [array]){ $t = ($t | Where-Object { $_ -ne 'null' } | Select-Object -First 1) }
        $dt = switch($t){ 'number'{'number'} 'integer'{'number'} 'boolean'{'boolean'} default {'string'} }
        $req = ($s.required -contains $p.Name)
        $attrs += [ordered]@{ name=$p.Name; dataType=$dt; required=[bool]$req }
      }
    } elseif($s.required){
      # stub schema: use the required[] fields as string attributes
      foreach($rq in $s.required){ if($rq -eq 'tenantId'){ continue }; $attrs += [ordered]@{ name=$rq; dataType='string'; required=$true } }
    }
    if($attrs.Count -eq 0){ continue }
    Claim entities $name
    $out += [ordered]@{ entityTypeName=$name; description=("$($s.title) (generated from schema)"); attributes=$attrs }
  }
  return ,$out
}

function Catalog-Events($dir){
  $ev = @(); $cf = Find-One $dir 'EVENT-CATALOG.csv'
  if($cf){
    $lines = Get-Content $cf.FullName | Select-Object -Skip 1
    foreach($ln in $lines){
      $name = ($ln -split ',')[0].Trim()
      if(-not $name){ continue }
      if(IsClaimed events $name){ continue }
      Claim events $name
      $ev += [ordered]@{ eventName=$name; description='Domain event.' }
    }
  }
  return ,$ev
}

function Catalog-Rows($dir){
  $cf = Get-ChildItem $dir -Recurse -File -Filter 'CONTROL-CATALOG.csv' -ErrorAction SilentlyContinue | Select-Object -First 1
  if(-not $cf){ return @() }
  $rows = @()
  foreach($ln in (Get-Content $cf.FullName | Select-Object -Skip 1)){
    $c = $ln -split ','
    if($c.Count -lt 5){ continue }
    $rows += [pscustomobject]@{ id=$c[0].Trim(); ctrl=$c[1].Trim(); type=$c[2].Trim(); beh=$c[3].Trim(); sev=$c[4].Trim(); owner=($c[5..($c.Count-1)] -join ',').Trim() }
  }
  return $rows
}

function Universal-Rules($rows){
  $rules = @()
  foreach($r in $rows){
    $isAi = ($r.ctrl -match '(?i)\bAI\b' -and $r.ctrl -match '(?i)(boundary|execution|cannot|execute|approval boundary)')
    $isXt = ($r.ctrl -match '(?i)cross-?tenant')
    if($isAi -and -not (IsClaimed rules $r.id)){
      Claim rules $r.id
      $rules += [ordered]@{ ruleId=$r.id; name=$r.ctrl; description=("Preventive/Critical ($($r.owner)). AI execution boundary."); triggerEvent='RecordReceived'; priority=5;
        conditions=@([ordered]@{field='recordType';operator='Equals';value='AgentAction'},[ordered]@{field='materialAction';operator='Equals';value='true'});
        outcomeOnMatch='Rejected'; reasonTemplate=("$($r.id): denied - an AI agent may not take material actions; agents are advisory-only ($($r.owner)).") }
    } elseif($isXt -and -not (IsClaimed rules $r.id)){
      Claim rules $r.id
      $rules += [ordered]@{ ruleId=$r.id; name=$r.ctrl; description=("Preventive/Critical ($($r.owner)). Cross-tenant denial."); triggerEvent='RecordReceived'; priority=5;
        conditions=@([ordered]@{field='recordType';operator='Equals';value='DataAccess'},[ordered]@{field='crossTenant';operator='Equals';value='true'});
        outcomeOnMatch='Rejected'; reasonTemplate=("$($r.id): access denied - cross-tenant access is prohibited ($($r.owner)).") }
    }
  }
  return ,$rules
}

function Parse-Agent($dir){
  $af = Get-ChildItem $dir -Recurse -File -Filter 'AGENT-SPECIFICATION.md' -ErrorAction SilentlyContinue | Select-Object -First 1
  if(-not $af){ return $null }
  $txt = Get-Content $af.FullName -Raw
  $id = if($txt -match '(?im)Agent ID:\s*`?([a-z0-9._-]+)`?'){ $Matches[1] } else { $null }
  if(-not $id){ return $null }
  if(IsClaimed agents $id){ return $null }
  $owner = if($txt -match '(?im)^\s*[-*]?\s*Owner:\s*(.+)$'){ $Matches[1].Trim() } else { 'TCEL' }
  $val = if($txt -match '(?im)Independent validator:\s*(.+)$'){ $Matches[1].Trim() } else { 'Model Risk Committee' }
  $tier = if($txt -match '(?im)Risk tier:\s*(.+)$'){ $Matches[1].Trim() } else { 'High' }
  $mission = if($txt -match '(?ims)##\s*Mission\s*(.+?)(\r?\n##|\z)'){ ($Matches[1].Trim() -replace '\s+',' ') } else { 'Governed advisory agent.' }
  $name = ($id -replace '^tcel\.','' -replace '\.',' ' -replace '\bai\b','AI')
  Claim agents $id
  return [ordered]@{
    agentId=$id; name=($id.Split('.') | ForEach-Object { $_ } | Select-Object -Last 2) -join '.' | ForEach-Object { $_.ToUpper() } # placeholder, overwritten below
  }
}

$manifests = 0
foreach($pair in $Map){
  $folder,$slug = $pair -split '=',2
  $dir = Get-ChildItem $SpecRoot -Recurse -Directory | Where-Object { $_.Name -eq $folder } | Select-Object -First 1
  if(-not $dir){ Write-Host "  SKIP (folder not found): $folder"; continue }
  $dir = $dir.FullName
  $pkgId = "ecarmf.ai-tcel-$slug"

  $entities = Schema-Entities $dir
  $events   = Catalog-Events $dir
  $rows     = Catalog-Rows $dir
  $rules    = Universal-Rules $rows

  # agent
  $agents = @()
  $af = Get-ChildItem $dir -Recurse -File -Filter 'AGENT-SPECIFICATION.md' -ErrorAction SilentlyContinue | Select-Object -First 1
  if($af){
    $txt = Get-Content $af.FullName -Raw
    $aid = if($txt -match '(?im)Agent ID:\s*`?([a-z0-9._-]+)`?'){ $Matches[1] } else { $null }
    if($aid -and -not (IsClaimed agents $aid)){
      $owner = if($txt -match '(?im)Owner:\s*(.+)$'){ ($Matches[1].Trim()) } else { 'TCEL' }
      $val = if($txt -match '(?im)Independent validator:\s*(.+)$'){ $Matches[1].Trim() } else { 'Model Risk Committee' }
      $tier = if($txt -match '(?im)Risk tier:\s*(.+)$'){ $Matches[1].Trim() } else { 'High' }
      $mission = if($txt -match '(?ims)##\s*Mission\s*(.+?)(\r?\n##|$)'){ ($Matches[1].Trim() -replace '\s+',' ') } else { 'Governed advisory agent.' }
      $disp = (Get-Culture).TextInfo.ToTitleCase((($aid -replace '^tcel\.','') -replace '\.',' ')) -replace '\bAi\b','AI'
      Claim agents $aid
      $agents += [ordered]@{
        agentId=$aid; name=$disp;
        description=("Governed advisory agent (generated from T9 spec). Advisory-only - no autonomous execution.");
        persona=("You are $disp for TCEL. $mission You ground every finding in the approved data and sources you can see, cite what you relied on, and state confidence, assumptions, and required approvers. You recommend, explain, and escalate; you never execute, approve, or alter any record - authorized humans decide.");
        contextSources=@('scores','deviations','references');
        sampleQuestions=@('What needs attention in this domain right now?','Which items breached a control or threshold this period?');
        owner=$owner; independentValidator=$val; riskTier=$tier;
        prohibited=@('execute, approve, or decide any material action','alter records or evidence; change its own permissions');
        outputDisclaimer=("Advisory only - $disp recommends and explains; authorized humans make every decision, not this agent.")
      }
    }
  }

  # knowledge asset: full control catalog
  $ka = @()
  if($rows.Count -gt 0){
    $content = ($rows | ForEach-Object { "$($_.id) $($_.ctrl) | $($_.type) | $($_.beh) | $($_.sev) | $($_.owner)." }) -join ' '
    $enforced = ($rules | ForEach-Object { $_.ruleId }) -join '/'
    $ka += [ordered]@{
      assetId=("tcel-$slug-control-catalog-2026"); docKey=("tcel-$slug-control-catalog");
      title=("TCEL $folder Control Catalog"); assetType='ReferenceManual'; docType='ControlCatalog';
      issuer='TCEL'; jurisdiction='US'; effectiveFrom='2026-01-01T00:00:00Z';
      summary=("The deterministic controls for this package. Enforced as executable rules: $enforced (the universal AI-execution-boundary / cross-tenant controls). The remaining controls are carried here for traceability; detailed field-vs-literal rule tuning is a follow-up (see the hand-authored T9-001..010 packages for the target).");
      contentText=$content
    }
  }

  $caps = @([ordered]@{ capabilityId=("$pkgId.Capability"); name=("TCEL $folder"); description=("Domain capability generated from the T9 spec.") })

  $manifest = [ordered]@{
    entityType='KnowledgePackageManifest'; entityName=("TCEL $folder"); version='1'; status='Published'; owner='TCEL';
    description=("TCEL Tenant-9 $folder package, generated from the spec folder as ECARMF knowledge-package metadata (structure faithful; universal AI-boundary/cross-tenant controls enforced, full control catalog carried as a knowledge asset for traceability). Depends on Foundation; does NOT re-declare RecordReceived; cross-package name collisions auto-omitted by the generator's claimed-names ledger.");
    packageId=$pkgId; name=("TCEL $folder"); packageVersion='1.0.0'; publisher='MAG Dynamics';
    dependencies=@([ordered]@{ packageId='ecarmf.ai-tcel-foundation'; minimumVersion='1.0.0' });
    entities=$entities; events=$events; rules=$rules; capabilities=$caps
  }
  if($agents.Count -gt 0){ $manifest.agents = $agents }
  if($ka.Count -gt 0){ $manifest.knowledgeAssets = $ka }

  $outPath = Join-Path $PackagesDir ("ai-tcel-$slug-v1.json")
  ($manifest | ConvertTo-Json -Depth 12) | Set-Content -LiteralPath $outPath -Encoding utf8
  $manifests++
  Write-Host ("  OK  {0}  (entities {1}, events {2}, rules {3}, agent {4})" -f $slug,$entities.Count,$events.Count,$rules.Count,$agents.Count)
}
Write-Host "generated $manifests manifests."

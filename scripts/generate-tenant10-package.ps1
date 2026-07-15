<#
.SYNOPSIS  Transform the official Tenant-10 (RCM) spec package into one ECARMF
           knowledge-package manifest. Supersedes the earlier representative
           ai-tenant10-rcm-v1.json.
.DESCRIPTION
  The T10 spec CSVs are the source of truth. This maps them onto ECARMF:
   - input-data-dictionary  -> entity declarations (grouped by EntityName, PHI-flagged)
   - risk-register (74)      -> full-register KnowledgeAsset + an executable risk-index
                                KPI over a T10RiskAssessment record, riskType from the
                                record's {domain} (dynamic-riskType refinement)
   - control-catalog (155)   -> full-catalog KnowledgeAsset (traceability) + a few
                                cleanly field-expressible executable control rules
   - kpi-catalog (53)        -> KnowledgeAsset (formulas are SQL-grain prose, not payload
                                expressions, so carried, not fake-computed)
   - alert-rules (145)       -> KnowledgeAsset (ECARMF benchmarks are tenant runtime config)
   - agent-inventory (25)    -> AgentDeclarations (advisory-only; PHI/phase noted)
  Uses Import-Csv (correct quoted-comma handling).
#>
param(
  [Parameter(Mandatory=$true)][string]$SpecRoot,
  [Parameter(Mandatory=$true)][string]$OutFile
)
$ErrorActionPreference='Stop'
$specs = Join-Path $SpecRoot '01-specs'
$dict     = Import-Csv (Join-Path $specs 'input-data-dictionary.csv')
$risks    = Import-Csv (Join-Path $specs 'risk-register.csv')
$controls = Import-Csv (Join-Path $specs 'control-catalog.csv')
$kpis     = Import-Csv (Join-Path $specs 'kpi-catalog.csv')
$alerts   = Import-Csv (Join-Path $specs 'alert-rules.csv')
$agents   = Import-Csv (Join-Path $SpecRoot '04-agents/agent-inventory.csv')

function DType($t){ switch -Wildcard ($t.ToLower()){ 'int*'{'number'} 'decimal*'{'number'} 'number*'{'number'} 'float*'{'number'} 'money*'{'number'} 'bool*'{'boolean'} default {'string'} } }
function IsYes($v){ return ($v -and ($v -match '^(yes|true|1)$')) }

# ---- entities from the data dictionary ----
$entities = @()
foreach($grp in ($dict | Group-Object EntityName | Sort-Object Name)){
  if(-not $grp.Name){ continue }
  $phiCount = (@($grp.Group | Where-Object { IsYes $_.IsPHI })).Count
  $attrs = @()
  foreach($f in $grp.Group){
    if(-not $f.FieldName){ continue }
    $attrs += [ordered]@{ name=$f.FieldName; dataType=(DType $f.DataType); required=[bool](IsYes $f.IsRequired) }
  }
  if($attrs.Count -eq 0){ continue }
  $entities += [ordered]@{ entityTypeName=$grp.Name; description=("T10 RCM entity ($phiCount PHI field(s))."); attributes=$attrs }
}
# synthetic record that drives the executable risk-index register
$entities += [ordered]@{ entityTypeName='T10RiskAssessment'; description='A risk-register assessment; domain drives the riskType tag via the {domain} token.'; attributes=@(
  [ordered]@{name='riskId';dataType='string';required=$true},
  [ordered]@{name='domain';dataType='string';required=$true},
  [ordered]@{name='severityValue';dataType='number';required=$true},
  [ordered]@{name='likelihood';dataType='number';required=$true}) }

# ---- executable control rules: field-expressible + universal ----
$rules = @(
  [ordered]@{ ruleId='RCM-CMP-011'; name='Minimum-necessary cross-client access'; description='Preventive/Critical (PrivacyOfficer). A biller may not read another client practice''s PHI.'; triggerEvent='RecordReceived'; priority=5;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='PhiAccess'},[ordered]@{field='crossClient';operator='Equals';value='true'}); outcomeOnMatch='Rejected';
    reasonTemplate='RCM-CMP-011: PHI access denied - minimum-necessary rule; staff may only access their assigned client''s patients (PrivacyOfficer).' },
  [ordered]@{ ruleId='T10-ALR-A1-02'; name='Access after termination'; description='Preventive/Critical (SecurityOfficer). PHI access after an employee''s termination date is denied.'; triggerEvent='RecordReceived'; priority=5;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='PhiAccess'},[ordered]@{field='userTerminated';operator='Equals';value='true'}); outcomeOnMatch='Rejected';
    reasonTemplate='T10-ALR-A1-02: PHI access denied - the user is terminated (SecurityOfficer).' },
  [ordered]@{ ruleId='RCM-CMP-006'; name='Privileged access without MFA'; description='Preventive/Critical (SecurityOfficer). Deny privileged/PHI access without MFA.'; triggerEvent='RecordReceived'; priority=5;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='PhiAccess'},[ordered]@{field='mfaEnabled';operator='Equals';value='false'}); outcomeOnMatch='Rejected';
    reasonTemplate='RCM-CMP-006: PHI access denied - MFA is not enabled on the account (SecurityOfficer).' },
  [ordered]@{ ruleId='RCM-CMP-020'; name='OIG exclusion screening'; description='Preventive/Critical (ComplianceManager). Block work assignment for an OIG-excluded person/vendor.'; triggerEvent='RecordReceived'; priority=10;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='WorkAssignment'},[ordered]@{field='oigExcluded';operator='Equals';value='true'}); outcomeOnMatch='Rejected';
    reasonTemplate='RCM-CMP-020: assignment blocked - the individual/vendor is on a federal exclusion list (ComplianceManager).' },
  [ordered]@{ ruleId='RCM-EHR-006'; name='Block on stale critical data'; description='Preventive/Critical (Technology). Block billing/posting on stale source data.'; triggerEvent='RecordReceived'; priority=10;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='BillingAction'},[ordered]@{field='sourceDataStale';operator='Equals';value='true'}); outcomeOnMatch='Flagged';
    reasonTemplate='RCM-EHR-006: billing/posting held - source data is stale (Technology).' },
  [ordered]@{ ruleId='RCM-CLM-003'; name='Payer filing limit'; description='Preventive/Critical (ContractManager). Reject a claim past the payer filing limit.'; triggerEvent='RecordReceived'; priority=10;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='Claim'},[ordered]@{field='pastFilingLimit';operator='Equals';value='true'}); outcomeOnMatch='Rejected';
    reasonTemplate='RCM-CLM-003: claim rejected - past the payer filing limit (ContractManager).' },
  [ordered]@{ ruleId='T10-AI-BOUNDARY'; name='AI execution boundary'; description='Preventive/Critical (Governance). Agents are advisory-only; deny material actions.'; triggerEvent='RecordReceived'; priority=5;
    conditions=@([ordered]@{field='recordType';operator='Equals';value='AgentAction'},[ordered]@{field='materialAction';operator='Equals';value='true'}); outcomeOnMatch='Rejected';
    reasonTemplate='T10-AI-BOUNDARY: denied - RCM agents are advisory-only and may not take material actions (Governance).' }
)

# ---- risk register performance framework (dynamic {domain} riskType) ----
$frameworks = @(
  [ordered]@{ frameworkId='t10-risk-register-v1'; name='Tenant-10 RCM Risk Register v1'; industry='Healthcare-RCM';
    description='The 74-risk register as ONE record type + ONE KPI: risk index = severityValue x likelihood, riskType from the record''s own {domain} (RCM-CMP, RCM-CLM, RCM-PAY, ...). Higher = worse.';
    kpis=@([ordered]@{ kpiId='t10-risk-index'; name='RCM risk index'; formula='severityValue * likelihood'; triggerRecordType='T10RiskAssessment'; subjectField='riskId'; subjectType='Risk'; riskType='{domain}'; unit='index'; targetValue=6; direction='lower'; metadataFields=@('severityValue','likelihood') }); okrs=@() }
)

# ---- agents ----
$agentDecls = @()
foreach($a in $agents){
  if(-not $a.AgentID){ continue }
  $id = 'tcel.t10.' + ($a.AgentName -replace 'Agent$','' -replace '[^A-Za-z0-9]','').ToLower(); $id = ('t10.' + ($a.AgentName -replace '[^A-Za-z0-9]','')).ToLower()
  $phi = if(IsYes $a.PhiAccess){ 'Regulated' } else { 'Elevated' }
  $agentDecls += [ordered]@{
    agentId=$id; name=$a.AgentName;
    description=("$($a.Purpose) Domain: $($a.DomainScope). Advisory-only ($($a.HumanInLoop)). Phase: $($a.DeferredUntilPhase). PHI access: $($a.PhiAccess).");
    persona=("You are $($a.AgentName) for TCEL Tenant-10 (medical billing / RCM). $($a.Purpose) You ground every finding in the approved data you can see ($($a.InputSources)), cite what you relied on, and state confidence and required approvers. You recommend and explain; you never execute, approve, post, or bill - a human decides. You never expose PHI beyond a user's minimum-necessary scope.");
    contextSources=@('scores','deviations','references');
    sampleQuestions=@("What does the $($a.DomainScope) domain need attention on right now?");
    owner='Tenant-10 Operations'; independentValidator='Compliance / Model Risk'; riskTier=$phi;
    prohibited=@('execute, approve, post, or bill any transaction','expose PHI beyond minimum-necessary scope; alter records or evidence; change its own permissions');
    outputDisclaimer=("Advisory only - $($a.AgentName) recommends; authorized humans make every billing, coding, and compliance decision, not this agent.")
  }
}

# ---- catalog knowledge assets ----
$riskText = (($risks | ForEach-Object { "$($_.RiskID) [$($_.DomainCode)] $($_.RiskTitle) - $($_.Severity) (score $($_.RiskScore))." }) -join ' ')
$ctrlText = (($controls | ForEach-Object { "$($_.ControlID) [$($_.DomainCode)] $($_.ControlName) - $($_.ControlType), owner $($_.OwnerRole)." }) -join ' ')
$kpiText  = (($kpis | ForEach-Object { "$($_.KPIID) [$($_.KPIGroup)] $($_.KPIName): $($_.Formula) (target $($_.DefaultTarget) $($_.Unit), $($_.Direction))." }) -join ' ')
$alertText= (($alerts | ForEach-Object { "$($_.AlertRuleID) [$($_.RiskID)] $($_.RuleName): $($_.TriggerCondition) (default $($_.DefaultValue) $($_.Unit), $($_.AlertSeverity))." }) -join ' ')
$phiFields = (@($dict | Where-Object { IsYes $_.IsPHI } | ForEach-Object { "$($_.EntityName).$($_.FieldName)" }) -join ', ')

$ka = @(
  [ordered]@{ assetId='t10-risk-register-catalog-2026'; docKey='t10-risk-register-catalog'; title=("Tenant-10 RCM Risk Register ($($risks.Count) risks, 12 domains)"); assetType='ReferenceManual'; docType='RiskRegister'; issuer='Tenant-10 Compliance'; jurisdiction='US-Federal-HIPAA'; effectiveFrom='2026-01-01T00:00:00Z'; summary=("The full RCM risk register. RiskScore = SeverityValue x Likelihood. Enforced live as the t10-risk-index KPI over T10RiskAssessment records, riskType from {domain}."); contentText=$riskText },
  [ordered]@{ assetId='t10-control-catalog-2026'; docKey='t10-control-catalog'; title=("Tenant-10 RCM Control Catalog ($($controls.Count) controls)"); assetType='ReferenceManual'; docType='ControlCatalog'; issuer='Tenant-10 Compliance'; jurisdiction='US-Federal-HIPAA'; effectiveFrom='2026-01-01T00:00:00Z'; summary=("The 155 deterministic RCM controls (41 Preventive, 41 Detective, 18 Corrective, +governance). A field-expressible subset is enforced as executable rules; the rest are carried for traceability with their test cases in the source spec."); contentText=$ctrlText },
  [ordered]@{ assetId='t10-kpi-catalog-2026'; docKey='t10-kpi-catalog'; title=("Tenant-10 RCM KPI Catalog ($($kpis.Count) KPIs, 8 groups)"); assetType='ReferenceManual'; docType='KpiCatalog'; issuer='Tenant-10 Operations'; jurisdiction='US'; effectiveFrom='2026-01-01T00:00:00Z'; summary=("The 53 RCM KPIs with formulas, targets, and grains. Formulas are SQL-grain aggregations (not payload expressions), so carried here as the catalog rather than fake-computed; each maps to configurable targets."); contentText=$kpiText },
  [ordered]@{ assetId='t10-alert-catalog-2026'; docKey='t10-alert-catalog'; title=("Tenant-10 RCM Alert Rule Catalog ($($alerts.Count) rules)"); assetType='ReferenceManual'; docType='AlertCatalog'; issuer='Tenant-10 Compliance'; jurisdiction='US'; effectiveFrom='2026-01-01T00:00:00Z'; summary=("The 145 alert rules, every threshold configurable. In ECARMF these become tenant-runtime Benchmarks; carried here as the source catalog."); contentText=$alertText },
  [ordered]@{ assetId='t10-phi-field-inventory-2026'; docKey='t10-phi-field-inventory'; title='Tenant-10 PHI Field Inventory (HIPAA)'; assetType='ReferenceManual'; docType='PhiInventory'; issuer='Tenant-10 Privacy'; jurisdiction='US-Federal-HIPAA'; effectiveFrom='2026-01-01T00:00:00Z'; summary='The PHI-flagged fields in the T10 data model; these drive the masked-field / audit-logged reveal pattern (UI Phase 1 s2.4) and the RegulatedDataProfile.'; contentText=$phiFields }
)

$manifest = [ordered]@{
  entityType='KnowledgePackageManifest'; entityName='AI Tenant-10 RCM (official spec)'; version='2'; status='Published'; owner='Tenant-10 (Medical Billing / RCM)';
  description=("Tenant-10 (RCM) onboarded from the OFFICIAL spec package (74 risks / 155 controls / 53 KPIs / 145 alert rules / 178 fields / 25 agents). First HIPAA-regulated tenant. Domain model from the data dictionary (PHI-flagged); risk register enforced as a dynamic-{domain}-riskType KPI; a field-expressible control subset as executable rules; full control/KPI/alert/risk catalogs + PHI inventory carried as traceability knowledge assets; 25 advisory-only agents. Supersedes the earlier representative ai-tenant10-rcm 1.0.0. NOTE: OI-01 (HIPAA field-encryption / audit substrate / per-tenant RegulatedDataProfile) is a platform GATE for Phase-1 PHI DATA handling - this package is domain METADATA and does not itself store PHI. Does NOT re-declare RecordReceived; declares its own for standalone activation.");
  packageId='ecarmf.ai-tenant10-rcm'; name='AI Tenant-10 RCM (official spec)'; packageVersion='2.0.0'; publisher='MAG Dynamics';
  dependencies=@();
  events=@([ordered]@{ eventName='RecordReceived'; description='A record has been durably persisted and is ready for rule/KPI evaluation.' });
  entities=$entities; rules=$rules; capabilities=@([ordered]@{ capabilityId='ecarmf.ai-tenant10-rcm.RcmIntelligence'; name='Tenant-10 RCM Intelligence'; description='Revenue-cycle risk register, controls, KPIs, alerts, PHI governance, and advisory agents for the medical-billing tenant.' });
  performanceFrameworks=$frameworks; agents=$agentDecls; knowledgeAssets=$ka
}

($manifest | ConvertTo-Json -Depth 14) | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host ("wrote {0}: entities {1}, rules {2}, agents {3}, frameworks {4}, knowledgeAssets {5}" -f (Split-Path $OutFile -Leaf), $entities.Count, $rules.Count, $agentDecls.Count, $frameworks.Count, $ka.Count)

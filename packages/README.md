# Knowledge Packages

Sample Knowledge Package manifests for the ECARMF Platform Kernel. A package
contributes entities, events, rules, capabilities, and score emissions as
pure metadata — the kernel executes declarations; it never contains package
code. Package versions are never overwritten: a changed manifest is a new
version (this is also how AI-learning threshold/weight updates ship).

## Loading a package

```bash
TENANT='X-Tenant-Id: tenant-alpha'

curl -X POST http://localhost:5099/api/packages -H "$TENANT" \
  -H "Content-Type: application/json" -d @packages/treasury-controls-v1.json
curl -X POST -H "$TENANT" \
  http://localhost:5099/api/packages/ecarmf.treasury-controls/1.1.0/activate
```

## Packages

| File | Package | What it proves |
|---|---|---|
| `treasury-controls-v1.json` | ecarmf.treasury-controls 1.2.0 | A simple control: withdrawals over $50,000 are Flagged for dual approval (TREASURY-R-001); everything else approved by default policy. 1.1.0 migrated the events to the generic record model (RecordReceived / Approved / Rejected / Flagged); 1.2.0 added the TREASURY-WF-001 workflow (notify + review task on Flagged). Earlier versions remain in history, never overwritten. |
| `flywheel-opportunity-evaluation-v1.json` | ecarmf.flywheel-opportunity-evaluation 1.0.0 | The full flywheel on the same kernel mechanism: scoring-only rules emit DataConfidence / RiskScore / Valuation / Compliance / AssetReadiness ScoreRecords, decision rules produce Accept / Hold / Escalate / AuditFurther with reasoning, and follow-up rules learn Trust and ControlEffectiveness from outcomes. Depends on treasury-controls (RecordReceived). |
| `connector-reference-templates-v1.json` | ecarmf.connector-reference-templates 1.0.0 | Six SchemaTemplates covering the source categories (manual form, MT940 bank text, broker CSV with consistency check, journal entry, SiteView event, market-risk provider). Connectors reference these; adding a second bank is a new connector instance, not new code. |
| `performance-frameworks-v1.json` | ecarmf.performance-frameworks 1.0.0 | Industry KPI/OKR frameworks (Build Chain live; Restaurant / Real Estate / Renewable Energy as definitions). KPIActual / KPIVariance / OKRAttainment compute in the same processing pass as rules. |

### Knowledge-content program (regulatory frameworks as packages)

Real governance content authored purely as metadata — zero kernel changes.
All four depend on treasury-controls ≥ 1.0.0 and activate side by side.

| File | Package | What it encodes |
|---|---|---|
| `compliance-aml-kyc-v1.json` | ecarmf.compliance-aml-kyc 1.0.0 | AML/KYC screening ordered *before* the generic treasury flag: sanctions hits are Rejected outright, unverified counterparties above the $10k CDD threshold and PEPs above $50k produce AMLEscalated with a Critical compliance workflow, and AMLRisk scores land even on rejected transactions. |
| `finance-gaap-controls-v1.json` | ecarmf.finance-gaap-controls 1.0.0 | GAAP journal-entry posting controls: unbalanced entries and closed-period postings are JournalHeld, material manual adjustments (> $100k) are Flagged for dual approval, clean entries auto-approve with a ControlCompliance score. Ships a `gaap-journal-json` SchemaTemplate for connector/document intake. |
| `coso-internal-controls-v1.json` | ecarmf.coso-internal-controls 1.0.0 | COSO 2013 as executable metadata: control assessments score ControlEffectiveness per control and COSOComponentHealth per component, material weaknesses / significant deficiencies / sub-0.6 ratings log ControlDeficiencyLogged and route to the Auditor + a remediation task, and the coso-maturity-v1 KPI framework tracks components against the 0.8 target. |
| `regd-offering-compliance-v1.json` | ecarmf.regd-offering-compliance 1.0.0 | SEC Regulation D: 506(c) subscriptions without verified accreditation are Rejected, 506(b) offerings reject generally-solicited investors, pending accreditations queue as compliance tasks, verified subscriptions approve with an InvestorAccreditation score per offering. |

## Authoring checklist (TCEL refinements)

Rules that recurred across the Tenant 9 (TCEL) build and are now enforced or
required. Follow them before a package is considered ready.

1. **Consume the ID ledger before generating a follow-on or multi-wave
   package.** Fetch `GET /api/packages/id-ledger` (tenant-scoped, requires
   `Registry:Read`) and treat every id it lists — for rules, capabilities,
   schema templates, frameworks, workflows, agents, knowledge assets,
   extraction templates, events, entities — as **reserved**. The ledger is a
   live projection over every stored package version (staged and inactive
   included), so it cannot go stale. A wave produced without consulting it is
   a delivery error, not a style issue. (Documentation banners were tried and
   failed six times; read the machine state.)
2. **Declare dependencies strictly cumulatively** — a package depends only on
   lower-numbered / already-existing packages. This is cycle-free by
   construction. The loader now **rejects** any manifest that would close a
   dependency cycle (self-dependency, or `a → b → a`), naming the full path.
3. **Never express a forward or loose coupling as a `PackageDependency`.** If
   a package consumes data a later package will publish, state that in the
   manifest `description` as published-data-only — a live dependency edge
   would create a cycle and be rejected.
4. **Start from the templates in `packages/templates/`** — `package-template.json`
   (every section, one filled-in placeholder each, `_comment` keys) and
   `agent-template.json` (the full Identity block: `owner`,
   `independentValidator`, `riskTier`, `prohibited`). A missing agent `owner`
   loads with a warning. Deviating from a template is allowed — but call the
   deviation out in the package, since drift can be an improvement and only
   *silent* drift is the bug. CSV header templates for control catalogs
   (`Control ID,Control,Type,System Behavior,Severity,Owner,Evidence`) and
   backlogs (`Epic,Name,Priority,Scope,Owner,Dependency`) live there too.
5. **State a boundary when you enter an existing domain.** If a package for a
   domain already exists for the tenant, the new package must include, in its
   `description`, an explicit `Boundary vs <packageId>:` sentence saying what
   it deliberately does *not* cover. Same for agents whose scope neighbours an
   existing one — the loader warns on overlapping scope terms; resolve or
   record the decomposition.
6. **Derive bundles, never author them.** A delivery bundle is assembled from
   the exact standalone files with `scripts/build-bundle.ps1` (which also
   writes a SHA-256 manifest) — never hand-built as a separate artifact that
   can silently diverge.

Two repo scripts back these rules: `scripts/id-ledger.ps1` (offline id
projection, same shape as `GET /api/packages/id-ledger`) and
`scripts/build-bundle.ps1` (bundle-from-standalone).

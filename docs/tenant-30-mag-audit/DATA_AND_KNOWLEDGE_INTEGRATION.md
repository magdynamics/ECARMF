# Tenant 30 Data and Knowledge Integration Map

## Purpose

This document tells the coding agent exactly where the existing MAG Audit data and knowledge assets reside, how they are connected, which system owns each category, and what may or may not be written.

All repository paths below are relative to the ECARMF repository root. Runtime configuration must resolve them to absolute paths; business code must not depend on a developer-specific absolute path.

## 1. Data ownership boundary

MAG Audit has three different data classes. They must not be merged into one database without preserving their ownership and lifecycle.

### A. ECARMF operational data — read/write

System of record: the configured ECARMF relational database through existing ECARMF repositories and tenant-scoped services.

Stores:

- Tenant 30 profile, users, roles, access keys and configuration;
- taxpayers, parties, sponsors, representatives and consent grants;
- engagements, cases, tasks, deadlines, contacts and chronology;
- evidence metadata and storage pointers;
- extracted facts accepted into a case;
- reconciliation runs and dispositions;
- assessments, scores, risks, programs, findings and outcomes;
- AI interactions, professional approvals and audit entries;
- client-visible updates, deliverables, value and learning proposals.

Every operational row must carry `TenantId = tenant-30-mag-audit`. Case-owned rows also carry `CaseId`; taxpayer-owned rows carry `TaxpayerId`. Access occurs only through ECARMF application interfaces—never by opening the production database directly from MAG Audit UI code.

### B. MAG Audit official-source corpus — immutable/read-only

System of record in the current development repository:

```text
IRS-AIKB/sources/
```

Current locations:

| Knowledge family | Repository location | Format | Current use |
|---|---|---|---|
| Mainstream Audit Technique Guides | `IRS-AIKB/sources/atg/` | PDF and preserved HTML | Industry, issue and examination techniques |
| Audit selection and process | `IRS-AIKB/sources/audit-selection-process-statistics/` | HTML, PDF and XLSX | Public selection context, IRM procedures, statistics and taxpayer guidance |
| Annual return forms and instructions | `IRS-AIKB/sources/annual-income-tax-forms/` | PDF | Form/version recognition and line definitions, 2018–2025 |
| Supporting schedules and instructions | `IRS-AIKB/sources/supporting-schedules/` | PDF | Schedule requirements, line mapping and extraction |
| Corporation Statistics of Income | `IRS-AIKB/sources/soi/corporation/` | PDF | Public corporate return statistics and ratios |

Source files are immutable evidence. Ingestion must verify SHA-256 against the manifest. A new IRS version creates a new `source_version`; it never overwrites the prior artifact.

In production, the physical binaries may move to encrypted object storage. The logical source/version identifiers, hashes, effective dates and citations must remain unchanged. Configure the binary root with `MAG_AUDIT_SOURCE_ROOT`; never hard-code a workstation path.

### C. Derived knowledge database — read-only to case workflows

Current development snapshot:

```text
IRS-AIKB/data/mainstream_atg.db
```

Engine: SQLite with FTS5.

Principal tables:

- `source` — stable source identity and authority class;
- `source_version` — publication/retrieval dates, hash, path and page count;
- `section` and `section_fts` — searchable page/heading text;
- `authority` and `section_authority` — extracted citation candidates;
- `technique` and `technique_fts` — technique candidates and source section;
- `risk_indicator` — curated or reviewed rule definitions;
- `assessment` — prototype assessment output only; production case assessments belong in ECARMF operational storage.

Current snapshot statistics are recorded in:

```text
IRS-AIKB/docs/BUILD_REPORT.json
```

Case workflows receive a read-only knowledge connection. Configure it with `MAG_AUDIT_KNOWLEDGE_DB`. The application must refuse startup or mark the IRS knowledge capability unavailable if the configured file hash/version is not registered.

## 2. Source registries and manifests

These files control corpus discovery and repeatable rebuilding:

```text
IRS-AIKB/source-manifest/sources.csv
IRS-AIKB/source-manifest/mainstream_atg_registry.csv
IRS-AIKB/source-manifest/annual_return_forms_2018_2025.csv
IRS-AIKB/source-manifest/supporting_schedules_2018_2025.csv
```

Required behavior:

1. Resolve each `local_path` against `MAG_AUDIT_SOURCE_ROOT` or the `IRS-AIKB` module root as configured.
2. Verify the file exists and its SHA-256 matches the registered version where a hash is present.
3. Treat missing, changed or unregistered files as unavailable—not as a reason to use a different source silently.
4. Record the exact source/version/section/page used by every case knowledge result.
5. Keep manifest completeness separate from professional review status.

Download/rebuild utilities currently reside at:

```text
IRS-AIKB/scripts/download_annual_return_corpus.py
IRS-AIKB/scripts/download_supporting_schedules.py
IRS-AIKB/irs_aikb/extraction.py
IRS-AIKB/irs_aikb/database.py
```

These are administrative ingestion tools. They do not run in an end-user request and must not mutate an active knowledge snapshot in place.

## 3. Existing MAG Audit domain code

The standalone prototype modules are located at:

```text
IRS-AIKB/irs_aikb/
```

Connection map:

| Capability | Existing module(s) |
|---|---|
| Source database and migrations | `database.py`, `extraction.py`, `IRS-AIKB/migrations/` |
| Return intake and e-file | `intake.py`, `efile_xml.py`, `canonical.py` |
| Form/year mapping | `line_mapping.py`, `supporting_schedules.py`, `schedule_requirements.py` |
| Reconciliation | `reconciliation.py` |
| Risk and portfolio analysis | `engine.py`, `portfolio.py`, `benchmark.py` |
| Chief Audit Officer logic | `chief_audit.py`, `penalty_defense.py` |
| Workflow and operations | `case_workflow.py`, `case_operations.py`, `production_pipeline.py` |
| Client and sponsor controls | `client_upload.py`, `client_engagement.py`, `sponsor_access.py` |
| Jurisdiction gating | `jurisdiction.py` |
| Outcomes and learning | `outcome.py`, `control_plane.py` |

The coding agent must extract stable domain contracts from these modules and place them behind MAG Audit Core interfaces. Do not call Python scripts from browser code. During transition, an application service may invoke the tested Python implementation through a controlled adapter; the long-term core contract must remain language-neutral.

## 4. Existing schema history

MAG Audit prototype migrations are ordered here:

```text
IRS-AIKB/migrations/001_initial.sql
...
IRS-AIKB/migrations/015_outcome_resolution_value.sql
```

They document the current domain model and remain the standalone SQLite migration history. They are not to be executed directly against the ECARMF SQL database.

For ECARMF integration:

1. map reusable concepts to existing ECARMF records, cases, tasks, scores, risks, knowledge assets and audit entries;
2. add ECARMF persistence only for genuinely tax-specific aggregates;
3. retain stable MAG Audit IDs across adapters;
4. create EF migrations through the ECARMF infrastructure project where new relational tables are necessary;
5. document the mapping from every standalone table to its ECARMF store in a migration crosswalk.

## 5. Knowledge query contract

Create `IMagAuditKnowledgeRepository` with at least:

```text
Search(query, jurisdiction, asOfDate, taxpayerType, returnType, industry, issue, reviewFloor)
GetSection(sectionId, versionId)
GetAuthorities(sectionId)
GetTechniques(issueOrSection, reviewFloor)
GetSourceVersion(versionId)
VerifySnapshot()
```

Every result must include:

```text
sourceId, versionId, title, officialUrl, publicationDate, retrievalDate,
sha256, sectionId, heading, pageStart, pageEnd, excerpt,
authorityClass, reviewStatus, effectivePeriod, jurisdiction
```

Default case use must exclude superseded sources and must not promote `machine_extracted` candidates to professional conclusions. Search snippets are research leads until reviewed.

The SQLite adapter uses parameterized queries and opens the knowledge file in read-only mode. A future ECARMF search/vector adapter must implement the same contract and return the same provenance envelope.

## 6. Evidence storage contract

Create `IMagAuditEvidenceStore` with separate binary and metadata responsibilities:

```text
PutQuarantined(stream, declaredMetadata)
VerifyAndAccept(artifactId, scanResult, sha256)
OpenOriginal(artifactId, versionId, authorizationContext)
CreateDerivedArtifact(parentVersionId, extractionMetadata)
ApplyClassification(artifactId, classification, reviewer)
PlaceLegalHold(artifactId, authority)
ListByCase(caseId, authorizationContext)
```

ECARMF stores the tenant/case metadata, permissions and audit trail. The configured blob provider stores encrypted binaries. Local development may use a case-scoped filesystem root configured by `MAG_AUDIT_EVIDENCE_ROOT`; production must use an approved encrypted provider. Never place client evidence under `IRS-AIKB/sources/` or commit it to Git.

## 7. Configuration contract

Required configuration keys:

```text
MAG_AUDIT_TENANT_ID=tenant-30-mag-audit
MAG_AUDIT_SOURCE_ROOT=<absolute runtime location of IRS-AIKB source corpus>
MAG_AUDIT_KNOWLEDGE_DB=<absolute runtime location of mainstream_atg.db>
MAG_AUDIT_EVIDENCE_PROVIDER=<local-dev|approved-object-store>
MAG_AUDIT_EVIDENCE_ROOT=<local development only>
MAG_AUDIT_JURISDICTIONS=US-IRS,US-IL-IDOR-PLACEHOLDER
MAG_AUDIT_MIN_REVIEW_STATUS=<configured professional review floor>
```

Use normal ECARMF secret/configuration providers. Do not commit absolute paths, credentials, client identifiers or storage secrets. Startup validation must report each dependency as available, degraded or blocked.

## 8. Runtime connection sequence

```text
1. Resolve authenticated ECARMF identity and tenant.
2. Require tenant-30-mag-audit and Regulated controls.
3. Resolve case and authorization scope.
4. Load operational data through tenant-scoped ECARMF repositories.
5. Load evidence metadata; request authorized binary access from evidence provider.
6. Query the read-only knowledge repository with jurisdiction and as-of date.
7. Return knowledge with full provenance and review state.
8. Run the applicable MAG Audit domain service.
9. Persist facts, explanations, decisions and reviewer states in ECARMF operational storage.
10. Write the immutable ECARMF audit entry.
```

## 9. Development and production topology

### Local integrated development

```text
ECARMF SQL database          -> operational Tenant 30 records
IRS-AIKB/data/mainstream_atg.db -> read-only IRS search
IRS-AIKB/sources/            -> read-only official source binaries
configured local evidence root -> synthetic case evidence only
```

### Production

```text
ECARMF production database   -> operational tenant/case records
versioned knowledge service  -> reviewed derived knowledge snapshot
encrypted source object store -> immutable official-source binaries
encrypted evidence object store -> isolated client evidence
search index/vector service  -> derived index with provenance back to source version
```

Promoting a knowledge snapshot to production requires source/hash verification, database integrity checks, review-state validation, search regression tests, and an immutable release identifier.

## 10. Current gaps the connector must expose honestly

- `mainstream_atg.db` contains machine-extracted authority and technique candidates awaiting professional review.
- The complete IRM and specialized federal corpora are not yet ingested.
- Supporting-schedule manifest coverage exceeds the files currently present.
- Corporation SOI currently preserves 2020–2022 files; later requested years remain to be completed.
- IDOR contains routing and governance only, not substantive validated knowledge.

The application must show these as coverage and review limitations. It must not hide them or compensate by silently consulting an unrelated source.


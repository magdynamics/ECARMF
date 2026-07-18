# Coding Agent Work Order — Tenant 30 MAG Audit

## Objective

Implement MAG Audit as a complete standalone-capable domain product and as the native ECARMF Tenant 30 package family. Use the existing `IRS-AIKB` domain modules and source corpus; do not replace them with mock screens or duplicate their logic in ECARMF-specific code.

Before writing integration code, read `DATA_AND_KNOWLEDGE_INTEGRATION.md`. It is the binding location and connection map for the existing source corpus, SQLite knowledge snapshot, manifests, domain modules, migration history, ECARMF operational storage and client evidence. No coding agent may invent an alternate data location or copy these assets into an unversioned store.

## Architecture boundary

Create four explicit assemblies or equivalent bounded modules:

1. `MAG.Audit.Core` — entities, value objects, policies, domain services, ports and events; no ECARMF or infrastructure dependency.
2. `MAG.Audit.Application` — use cases, commands, queries, orchestration and professional approval gates.
3. `MAG.Audit.Standalone` — standalone identity, organization, storage, workflow, notification, audit and administration adapters.
4. `MAG.Audit.ECARMF` — adapter mapping MAG Audit ports, records, events, tasks, scores, agents and audit entries to ECARMF tenant-scoped services.

Jurisdiction packages depend inward on the MAG Audit domain contracts. The shared core must never depend on IRS- or IDOR-specific law.

## Mandatory identifiers and scopes

- ECARMF tenant: `tenant-30-mag-audit`.
- Sensitivity: `Regulated`.
- Client/taxpayer records always carry `TenantId` and stable `TaxpayerId`.
- Case key: `TenantId + CaseId` with required taxpayer, jurisdiction, tax type, return/form, period and engagement scope.
- Evidence key: `TenantId + CaseId + ArtifactId + VersionId`.
- Knowledge key: jurisdiction + source + immutable version + section/page + effective period.
- Authorization key: client + case + grantee + action + artifact scope + effective period.

Never infer a tenant, case, tax period, jurisdiction, authorization or knowledge version.

## Required aggregate roots

- Organization configuration
- Party and relationship
- Client engagement
- Taxpayer profile
- Audit case
- Artifact/evidence record
- Return package and canonical fact set
- Reconciliation run
- Assessment and score explanation
- Audit program
- Authorization and consent grant
- External action request
- Finding chain
- Deliverable
- Learning proposal

## Professional state machines

Implement explicit, guarded transitions for:

- prospect → acceptance review → accepted/declined → engaged → closed;
- uploaded → quarantined → validated → extracted → professionally accepted → retained/superseded;
- case opened → intake → analysis → program → response/remediation → resolution → verified outcome → closed;
- machine extracted → curated seed → technically reviewed → legal current → superseded;
- draft external action → scope verified → privilege reviewed → client/professional approved → executed → receipt verified;
- potential issue → reviewed finding → recommendation → approved action → resolution → independently verified outcome;
- proposed learning → reviewed → approved/rejected → published → superseded/rolled back.

Invalid transitions must fail closed and create an audit entry.

## Integration rules

1. Use ECARMF `TenantProfile`, identity, package registry, records, events, rules, scores, risks, tasks, agents, knowledge assets and audit services through adapters.
2. Do not add tax-specific behavior to the ECARMF kernel when it can be expressed in MAG Audit code or packages.
3. Generalize a kernel primitive only when it is reusable across tenants and covered by cross-tenant tests.
4. Reuse existing `ecarmf.ai-cpa-firm`, CPA reference, document/evidence, risk, financial-analysis and notification capabilities where compatible; document boundary and version.
5. Keep large official source binaries outside package JSON. Packages register immutable source/version metadata and retrieval locations.
6. Every AI interaction records model/provider, prompt/template version, allowed context, cited evidence, confidence, output, human disposition and audit entry.
7. Implement `IMagAuditKnowledgeRepository` and `IMagAuditEvidenceStore` as defined in `DATA_AND_KNOWLEDGE_INTEGRATION.md`; all knowledge results return the provenance envelope.
8. Configure source, knowledge database and evidence locations at runtime. Repository-relative locations are development defaults only; no workstation-specific path or secret may enter code or manifests.
9. Treat `IRS-AIKB/data/mainstream_atg.db` and `IRS-AIKB/sources/` as read-only case-workflow inputs. Corpus ingestion occurs only through a separate governed administrative release process.

## Security controls

- Require production access-key identity for Tenant 30; never accept caller-asserted header identity.
- Deny sponsor access by default. Consent is purpose-, case-, action-, artifact-, and time-scoped.
- Separate tax-return information, government correspondence, internal work product, potentially privileged material and client-facing deliverables.
- Encrypt data in transit and at rest; secrets never enter package manifests or source control.
- Scan uploads, preserve originals, hash every version and maintain chain of custody.
- Apply least privilege, separation of preparation/review/approval, audit logging, retention, legal hold and verified deletion policies.
- Log and block cross-tenant access attempts.

## Scoring controls

Produce independent scores for public selection indicators, adjustment exposure, documentation readiness, controversy readiness, confidence/data quality and remediation urgency. Persist factor values, weights, source versions, exclusions, uncertainty, overrides and reviewers. Never label any output as DIF or a selection probability unless a validated public statistical model supports that exact interpretation.

## UI deliverables

Provide role-aware experiences for client, sponsor, staff, reviewer, engagement leader, administrator and ECARMF platform operator. Required screens include onboarding, taxpayer profile, case portfolio, case command center, evidence vault, return/reconciliation, risk explanation, knowledge research, audit program, rights/authorization, communications/deadlines, findings/outcomes, client status and leadership reporting.

Every screen must operate on persistent services. A mock interaction cannot satisfy an acceptance criterion.

## Implementation phases

### Phase A — Contract and tenant foundation

Register Tenant 30, add package manifests, define domain ports, create mapping conventions, validate tenant isolation and install dependencies.

### Phase B — Persistent vertical case

Implement client, taxpayer, consent, case, evidence metadata, task/deadline and chronology persistence. Connect these to the interface.

### Phase C — Tax intelligence

Connect return intake, canonical facts, line mapping, reconciliation, IRS-AIKB retrieval, score explanations and customized audit programs.

### Phase D — Representation and outcome

Connect authorization, correspondence, IDR production, findings, recommendations, external approvals, resolutions, deliverables and value.

### Phase E — Agents, reporting and standalone adapters

Enable governed agents, leadership reporting, controlled learning, operational controls and standalone infrastructure adapters.

### Phase F — Professional validation and production hardening

Complete source corpus, technical/legal review, threat model, accessibility, performance, backup/restore, retention, incident response and deployment certification.

## Test obligations

- package deserialization, dependency order, unique identifiers and cycle rejection;
- Tenant 30 Regulated identity enforcement;
- cross-tenant data denial;
- sponsor consent boundaries and revocation;
- case/jurisdiction/period isolation;
- evidence immutability, provenance and privilege restrictions;
- strict form/year line mapping;
- explainable scoring and no-DIF language;
- human gates for external actions and conclusions;
- IDOR fail-closed behavior while unvalidated;
- complete synthetic vertical-case acceptance journey;
- standalone/ECARMF behavioral contract parity;
- backup, restore and audit-history recovery.

## Prohibited shortcuts

- Do not create one taxpayer per ECARMF tenant.
- Do not copy IRS rules into IDOR.
- Do not store source PDFs inside JSON manifests.
- Do not silently replace an effective-dated source.
- Do not call a schema, mock screen or candidate extraction production-ready.
- Do not permit an AI agent to transmit, sign, waive, approve or make final professional determinations.
- Do not train or update rules directly from a closed case without reviewed learning approval.

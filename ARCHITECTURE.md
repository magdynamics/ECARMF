# ECARMF Architecture

ECARMF is organized as a layered framework. Sections 1–5 describe the
framework layers defined by the standards family; section 6 describes the
Platform Kernel, the reference runtime implementation of those layers.

## 1. Data Layer

Captures asset, capital, economic, risk, compliance, and operational data.

## 2. Model Layer

Transforms raw data into structured model objects, including asset profiles, capital profiles, risk records, economic assumptions, and thresholds.

## 3. Engine Layer

Includes the threshold engine, scoring engine, decision engine, forecast engine, simulation engine, and monitoring engine.

## 4. Governance Layer

Defines version control, change control, approval policies, validation procedures, and audit trails.

## 5. Reporting Layer

Produces investor reports, risk reports, compliance summaries, management dashboards, and exception alerts.

---

## 6. Platform Kernel

The Platform Kernel (`/src`) is the knowledge-driven runtime that executes
the ECARMF meta-model. It is a multi-tenant platform: it serves multiple
clients, and every architectural decision enforces tenant isolation.

### 6.1 The Kernel / Knowledge Boundary

This is the load-bearing rule for every contributor — human or AI agent:

> **The kernel provides mechanism. Knowledge Packages provide meaning.**

| Concern | Lives in | Never in |
|---|---|---|
| Registries, package lifecycle, event bus, rule evaluation, audit writing | Kernel code | — |
| Entities (e.g. `Venture`), events, rules, thresholds, capabilities | Knowledge Package manifests (JSON metadata) | Kernel code |
| Business outcomes ("withdrawals over $50,000 require dual approval") | A rule declaration in a manifest | A C# `if` statement |

This implements the ECARMF-002 Engineering Rule: *no implementation may
introduce a managed concept that is not defined by the standard or an
approved extension*. The kernel has no `Hotel`, `Asset`, `Risk`, or
`Capital` classes — those concepts arrive as package declarations and are
executed as metadata. If you find yourself adding a domain noun to
`ECARMF.Kernel.*`, you are on the wrong side of the boundary: write a
Knowledge Package instead.

### 6.2 Solution Layout

| Project | Role | May depend on |
|---|---|---|
| `ECARMF.Kernel.Domain` | POCOs: Universal Base Entity (ECARMF-002), manifest + declaration types, transaction, outcome, audit entry | nothing |
| `ECARMF.Kernel.Application` | Kernel mechanisms: registries, package loader, event bus, event processor, intake, ports (`IPackageStore`, `ITransactionStore`, `IOutcomeStore`, `IAuditLog`) | Domain |
| `ECARMF.Kernel.Infrastructure` | EF Core + SQL Server implementations of the ports | Application, Domain |
| `ECARMF.Kernel.Api` | ASP.NET Core composition root: REST endpoints, hosted event consumer, Swagger | Application, Infrastructure |
| `frontend/admin-ui` | React + TypeScript admin console (Package Inspector, Transaction Activity) | REST API only |

### 6.3 Multi-Tenancy

The platform serves multiple clients. Tenancy is enforced at every layer:

- Every API request carries an `X-Tenant-Id` header; requests without one
  are rejected. No default tenant exists.
- `TenantRegistryProvider` gives each tenant its own registry set —
  packages, rules, and events of one client can never observe or conflict
  with another client's.
- Every persisted row (packages, transactions, outcomes, audit entries)
  carries `TenantId`; all queries filter by it. The package uniqueness
  constraint is `(TenantId, PackageId, PackageVersion)`.
- Events on the kernel bus carry the tenant, and the processor resolves
  rules from that tenant's registries only.

### 6.4 Record Pipeline (audit integrity first)

Transaction and Opportunity are both just entity types flowing through one
generic record pipeline — there are no type-specific pipelines.

```
POST /api/records  (X-Tenant-Id, recordType + payload)
  1. Record persisted immutably                <- before anything else
  2. AuditEntry: RecordReceived                <- before publishing
  3. KernelEvent: RecordReceived -> bus        (only if an active package
                                                of this tenant declares it)
  4. EventProcessor (hosted service, scope per event)
       - evaluates the tenant's subscribed rules in priority order
       - audits every rule evaluation, matched or not
       - matching rules emit their declared ScoreRecords (audited as
         ScoreComputed with rule + package provenance)
       - scoring-only rules (no outcome) fire and processing continues;
         the first matching rule WITH an outcome decides and stops
       - outcome types are package-defined strings (Approved, Rejected,
         Flagged, Hold, Escalate, Accept, ...), never a kernel enum
       - no outcome rule matched on intake: Approved by default policy
         (recorded and explained as such)
       - outcome persisted with RuleId + PackageId + PackageVersion +
         rendered ReasonTemplate  (ECARMF-001 FND-0005: explainability)
       - the follow-up event name IS the outcome string, published only
         from intake processing (cycles are impossible) and only if an
         active package declares that event
```

Dual approval: `POST /api/records/{id}/approvals` lets a second approver —
who must differ from the submitter — release or reject a Flagged record.
The decision is append-only (one per record, enforced by a unique index),
audited before its consequences publish, and the releasing outcome cites
the rule that placed the hold.

Ordering rules that must never be violated:

1. **Persist before process.** The transaction record is written and
   audited before any downstream processing can observe it.
2. **Audit before publish.** Audit entries are appended before the events
   they describe are made visible to consumers.
3. **Append-only.** `ITransactionStore`, `IOutcomeStore`, and `IAuditLog`
   expose no update or delete members. Immutability is structural, not a
   convention.

### 6.5 Knowledge Package Lifecycle

```
upload (POST /api/packages)          validate manifest -> Staged | Failed
activate (POST .../activate)         resolve dependencies (same tenant,
                                     Active, minimum version) -> register
                                     declarations atomically -> Active
                                     (conflict -> rollback -> Failed)
deactivate (POST .../deactivate)     refused while an active dependent
                                     exists -> unregister -> Deactivated
startup                              rehydrate every tenant's Active
                                     packages into registries, dependency
                                     order, multi-pass
```

Failures are explainable: validation errors and registry conflicts are
persisted in `StatusDetail` and audited as `PackageFailed`.

### 6.6 Conformance Position

The kernel claims **partial conformance** with ECARMF-001/002 (permitted by
ECARMF-001 §12.1). Implemented: Universal Base Entity, requirements-driven
explainability (FND-0005), modular implementation (FND-0008), constraint
enforcement at manifest validation, event architecture, API contracts, and
audit trails (GOV requirements). Not yet implemented: temporal knowledge
graph, digital twin synchronization, full canonical entity catalog
enforcement, and certification artifacts — these arrive as later releases
and as Knowledge Packages, not as kernel changes.

### 6.7 Flywheel Intelligence — mapped onto kernel primitives

The flywheel cycle (collect → validate → process → score → decide →
execute → audit → learn → improve → repeat) is not a separate system. Every
flywheel concept maps onto a generic kernel primitive:

| Flywheel concept | Kernel primitive |
|---|---|
| Opportunity, Transaction | Entity types flowing through the one record pipeline (`POST /api/records`) |
| Trust / AssetReadiness / DataConfidence / ControlEffectiveness / TreasuryEfficiency scores | One `ScoreRecord` type with different `ScoreType` tags, emitted by rules as metadata (`EmitScores`), queryable at `GET /api/scores/{entityType}/{entityId}` |
| A flywheel "cycle" | The audit trail of one correlation id (`GET /api/audit/cycle/{correlationId}`) — one audit log, no divergence risk |
| Model / threshold / weight versioning for AI learning | Package versioning in `PackageLoader`: a learning update is a **new package version**; prior versions are never overwritten (see Treasury Controls 1.0.0 → 1.1.0 in the samples) |
| Manual override with reason, user, timestamp, approval trail | `ApprovalRecorded` audit entries + the append-only `Approvals` decision store |
| Decision recommendations (accept / reject / hold / improve / escalate / audit further) | Package-defined outcome strings with reasoning, confidence, and assumptions rendered into the audited reason |
| Learning feedback (execution results improving future evaluations) | Rules triggered by decision follow-up events (e.g. `Accept`, `Hold`) emitting Trust / ControlEffectiveness ScoreRecords — see `packages/flywheel-opportunity-evaluation-v1.json` |
| Flywheel KPIs (confidence averages, trust movement, override rate, cycle completion) | Dashboard queries over ScoreRecords and the Audit Log — no dedicated KPI backend |

### 6.8 Ingestion: Connectors and SchemaTemplates

Records arrive through configured **DataSourceConnector** instances (source
category + ingestion mode + a **SchemaTemplate** reference + reliability
tier + provenance class). Templates are declarative field mappings shipped
in packages and registered in the fifth registry (`SchemaTemplateRegistry`)
— adding a second bank is a new connector row reusing the bank template,
not new code. Everything lands through one door
(`POST /api/connectors/{id}/ingest`), stamped with `sourceId`,
`sourceCategory`, `provenance` (HumanEntered / ExternalSystemVerified /
AIGenerated), `reliabilityRating`, `ingestedAt`, and the template version,
then enters the same immutable intake pipeline as every record. Reference
templates for all six source categories ship in
`packages/connector-reference-templates-v1.json`; live third-party
integrations (OAuth, SFTP, MQTT) are later connector instances — the
mechanism already supports them without kernel changes.

**AI analytical sources earn trust, never assume it:** AI-generated scores
carry `provenance: AIGenerated`, and the `AILearningFeedbackService`
publishes predicted-vs-actual `ModelAccuracy` ScoreRecords (e.g. every
dual-approval verdict grades the rule that placed the hold). This is the
loop that stops the flywheel reinforcing its own errors.

### 6.9 Identity, Roles & Access

Every API call authenticates a seeded identity via `X-User-Id` (OAuth/SSO
integration is future work; the permission mechanism is real). The full
eight-role catalog is enforced through generic composable permissions
(`Capability:RequireDualApproval:Approve`, `Package:Manage`, …); the Owner
is the only unrestricted role; the AI system actor (`system:flywheel`) is a
first-class User whose actions are logged under its own identity and who
can never hold approval permissions. Submitter and approver are the
authenticated identity — segregation of duties (approver ≠ submitter)
enforces against real identities, and every audit entry's `Actor` is a real
User reference, never a placeholder.

### 6.10 Capital Intelligence and Performance Intelligence

Both compose on existing primitives rather than adding parallel mechanisms:

- **AllocationRecommendation** (Capital Intelligence, ECARMF-011): where /
  how much / which institution / which jurisdiction, with mandatory
  reasoning, confidence, assumptions, risk factors, ranked
  `alternativesConsidered`, and `supportingScoreRecordIds` grounding every
  claim in ScoreRecords. Three autonomy tiers — Autonomous (AI executes),
  RecommendOnly (human approves/modifies/rejects), Escalated (AI stops) —
  decided by `AutonomyPolicy`; an AI actor can generate but never decide.
- **Performance frameworks** (KPI/OKR) are package metadata in the sixth
  registry (`PerformanceFrameworkRegistry`). KPI calculation is a
  declarative formula evaluated in the same event-processing pass;
  `KPIActual` / `KPIVariance` / `OKRAttainment` are ScoreRecord types, so
  allocation reasoning and dashboards read them like every other score.

### 6.11 Guidance for Future Contributors and AI Agents

1. Read `ECARMF-001-Foundation-Standard.md` and `ECARMF 002/` before coding.
2. New domain behavior = new Knowledge Package manifest (see
   `packages/treasury-controls-v1.json`), not kernel code.
3. Never add update/delete members to the append-only ports.
4. Never resolve a tenant implicitly; tenancy always flows from the caller.
5. Every outcome-affecting change must keep the outcome traceable to a rule
   and package version.

## 7. The Enterprise AI Operating System layer (Phases 1–6)

Everything below is built **on** the kernel primitives above — new
registries, stores, and configuration; never a parallel engine.

### 7.1 Workflows (7th registry)

Packages declare `WorkflowDeclaration`s (trigger event + conditions +
steps: `notify` | `createTask` | `publishEvent`). The `WorkflowEngine`
executes them after the outcome is durable — including on follow-up
events with no matching rules. Tasks are human work items: an AI/system
actor cannot complete them. Every execution is audited
(`WorkflowExecuted`) with package provenance.

### 7.2 AI agents (8th registry) and the Executive Advisor

The `ExecutiveAdvisorService` is the built-in agent: it snapshots the
tenant's scores, deviations, allocations, and tasks and writes an audited
brief under `system:advisor`. **Declared agents** generalize it: packages
ship `AgentDeclaration`s (persona + declared context sources + sample
questions) into the `AgentRegistry`; the `AgentConsultService` wraps every
persona in a non-negotiable kernel guardrail (advisory-only, grounded,
never invent, recommend professionals), shows the agent only its declared
sources, provisions a per-agent identity (`system:agent:{id}`), audits
every consultation (`AgentConsulted`), and feeds human ratings into that
agent's own `ModelAccuracy` history. All agents run through the
`ILanguageModelProvider`, which resolves **the tenant's own Anthropic
credential** (encrypted at rest, write-only) — one client's AI usage never
runs on another's key; without a key, deterministic composers take over.

### 7.3 Document extraction and the source library

`DocumentExtractionService` turns uploads (PDF via PdfPig, text, email)
into the raw payload the target connector's SchemaTemplate expects — text
templates extract deterministically via their own regex patterns; json/csv
templates use the tenant's AI backend — then normal ingestion runs.
`IDocumentLibrary` archives **every** payload through any connector,
accepted or rejected, verbatim with SHA-256, size, source, uploader,
template, extraction backend, and produced record ids; document extraction
archives the original file as well. Reference case: a real Form 1120-S
binder PDF extracts to `{formType, entityName, taxYear, taxableIncome}`
and rule `IRS-R-021` flags it as a pass-through instead of mis-comparing
it to the 21 % corporate rate.

### 7.4 Integrations, benchmarks, search

`IntegrationDefinition`s manage feeds from external applications
(Accounting / POS / Billing / RealEstateManagement / …): push (the app
delivers) or pull (the platform fetches, with a protected bearer secret
and optional schedule run by `FeedSchedulerHostedService`); every run —
success or failure — is a `FeedRun` in the integration's health history.
`Benchmark`s are tenant-set expectations (score-kind watches every
computed score/KPI; recordField-kind watches incoming payloads); a breach
raises a `DeviationAlert`, a notification to the configured role, an
optional task, and a `BenchmarkBreached` audit entry — checked inside the
same processing pass. Record activity is queryable server-side
(`/api/records/search`: type/outcome/submitter/text/date + paging) for
tenants with thousands of records.

### 7.5 Platform operations: clients, credentials, billing

The reserved `platform` tenant is the operator console (guarded by
`Tenant:Manage` + the platform-tenant check). Client tenants are
`TenantProfile`s; their people are `User`s with contact fields and
**access-key credentials** (SHA-256 hashed, shown once, rotatable). The
`ApiKeyAuthenticationMiddleware` makes the credential authoritative: it
derives tenant + identity and overwrites asserted headers, and enforces
tenant suspension platform-wide. Billing meters utilization straight from
operational tables (records, documents/storage, AI calls, feed runs,
active users) — the meter can never disagree with what happened — and
prices it against assignable plans into itemized `BillingStatement`s.

### 7.6 Regulatory content as packages

COSO 2013, GAAP journal controls, SEC Reg D, AML/KYC screening, and the
IRS corporate-rate reference all ship as ordinary Knowledge Packages
(`packages/README.md`) — rules, events, templates, KPI frameworks,
workflows, and agents, activated side by side over the treasury base with
zero kernel changes. Published reference data (e.g. IRS rates) becomes a
KPI target; when the source updates, that is a **new package version**.

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

### 6.4 Transaction Pipeline (audit integrity first)

```
POST /api/transactions  (X-Tenant-Id)
  1. Transaction persisted immutably           <- before anything else
  2. AuditEntry: TransactionReceived           <- before publishing
  3. KernelEvent: TransactionReceived -> bus   (only if an active package
                                                of this tenant declares it)
  4. EventProcessor (hosted service, scope per event)
       - evaluates the tenant's subscribed rules in priority order
       - audits every rule evaluation, matched or not
       - first matching rule fires: outcome Approved/Rejected/Flagged
       - no match on intake: Approved by default policy (recorded as such)
       - outcome persisted with RuleId + PackageId + PackageVersion +
         rendered ReasonTemplate  (ECARMF-001 FND-0005: explainability)
       - follow-up event TransactionApproved/Rejected/Flagged published
         only from intake processing (cycles are impossible) and only if
         declared by an active package
```

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

### 6.7 Guidance for Future Contributors and AI Agents

1. Read `ECARMF-001-Foundation-Standard.md` and `ECARMF 002/` before coding.
2. New domain behavior = new Knowledge Package manifest (see
   `packages/treasury-controls-v1.json`), not kernel code.
3. Never add update/delete members to the append-only ports.
4. Never resolve a tenant implicitly; tenancy always flows from the caller.
5. Every outcome-affecting change must keep the outcome traceable to a rule
   and package version.

# ECARMF — Economic Capital Asset Risk Management Framework

ECARMF is an open framework for defining, measuring, scoring, monitoring, and governing asset risk using economic, capital, compliance, and threshold-based decision models.

## Purpose

The framework is designed to support real estate, infrastructure, energy, manufacturing, financial, and tokenized-asset projects. It provides a structured way to connect asset facts, capital structure, economic assumptions, risk indicators, compliance obligations, and decision thresholds.

## Core Modules

1. Asset Definition Model
2. Economic Model
3. Capital Model
4. Risk Model
5. Threshold Engine
6. Risk Scoring Engine
7. Decision Engine
8. Compliance Layer
9. Forecast Engine
10. Monitoring Engine
11. Simulation Engine
12. Reporting Engine
13. API Layer
14. Data Layer
15. Governance Framework

## Platform Kernel (reference runtime)

The Platform Kernel is the executable heart of the platform: a
**multi-tenant, knowledge-driven runtime** that loads Knowledge Packages
(pure metadata manifests declaring entities, events, rules, and
capabilities), processes transactions through those rules in real time, and
produces explainable, auditable outcomes. The kernel encodes no business
logic — behavior arrives as packages. See [ARCHITECTURE.md](ARCHITECTURE.md)
for the kernel/knowledge boundary.

### Stack

- .NET 8 / ASP.NET Core Web API (`/src`)
- React + TypeScript (Vite) admin UI (`/frontend/admin-ui`)
- SQL Server via EF Core (LocalDB for local dev; container via compose)
- xUnit test suite (`/tests`)

### Run locally (Windows, no Docker required)

```bash
# API (uses SQL Server LocalDB; applies migrations on start)
dotnet run --project src/ECARMF.Kernel.Api --urls http://localhost:5099

# Admin UI (proxies /api to the API)
npm install --prefix frontend/admin-ui
npm run dev --prefix frontend/admin-ui     # http://localhost:5173

# Tests
dotnet test
```

### Run with Docker Compose

```bash
cd docker
docker compose up --build
# API:      http://localhost:8080  (Swagger at /swagger in Development)
# Admin UI: http://localhost:3000
```

### Multi-tenancy & credentials

The platform serves multiple clients. We operate it as the platform admin
from the reserved `platform` tenant: client tenants are onboarded there
(profile, contacts, billing plan, suspend/reactivate), and every client user
is provisioned with an **access key** (shown once, stored hashed). A request
carrying `X-Api-Key` derives both the user and the tenant from the
credential — asserted headers are overridden, suspended tenants are locked
out platform-wide, and rotation revokes the old key instantly. For local
development, `X-Tenant-Id` + `X-User-Id` headers still work as an asserted
identity. Packages, records, outcomes, scores, documents, AI credentials,
and audit trails are isolated per tenant.

### Enterprise AI Operating System layer

Built on the kernel primitives, all metadata-driven:

- **Workflows** — packages declare trigger→steps automation (notify /
  createTask / publishEvent); tasks are human work items an AI actor can
  never complete.
- **Executive Advisor + declarable AI agents** — packages ship domain
  specialists (e.g. the IRS Corporate Tax Guide) via the 8th registry: a
  persona, declared context sources, kernel guardrails (advisory-only,
  grounded), each under its own identity, trust-tracked through human
  feedback (ModelAccuracy). Runs on each tenant's own Anthropic key
  (Setup → AI Backend); deterministic fallbacks otherwise.
- **Document extraction** — upload PDFs/emails/statements; the extraction
  agent produces the connector's expected payload and normal ingestion runs
  (a real 1120-S tax-return binder is the reference case: correctly flagged
  as a pass-through instead of mis-compared to the 21% corporate rate).
- **Source library** — every upload archived verbatim (SHA-256) and indexed:
  source, uploader, template, and the records it produced.
- **Integrations** — managed push/pull feeds from accounting, POS, billing,
  and real-estate systems, with scheduling, protected secrets, and run
  history.
- **Benchmarks** — tenant expectations ("GP% ≥ 25%", "no movement above
  10k") with configurable triggers, severities, and alert routing.
- **Billing** — per-tenant utilization metered from operational tables,
  plans with unit rates, itemized statements.
- **Regulatory content** — COSO, GAAP, Reg D, AML/KYC, and IRS reference
  rates authored purely as Knowledge Packages (see `packages/README.md`).

### Try the pipeline (control + flywheel)

```bash
TENANT='X-Tenant-Id: tenant-alpha'

# 1. Upload and activate the sample packages (flywheel depends on treasury)
curl -X POST http://localhost:5099/api/packages -H "$TENANT" \
  -H "Content-Type: application/json" -d @packages/treasury-controls-v1.json
curl -X POST -H "$TENANT" \
  http://localhost:5099/api/packages/ecarmf.treasury-controls/1.1.0/activate
curl -X POST http://localhost:5099/api/packages -H "$TENANT" \
  -H "Content-Type: application/json" -d @packages/flywheel-opportunity-evaluation-v1.json
curl -X POST -H "$TENANT" \
  http://localhost:5099/api/packages/ecarmf.flywheel-opportunity-evaluation/1.0.0/activate

# 2. A $60,000 withdrawal gets Flagged by rule TREASURY-R-001...
curl -X POST http://localhost:5099/api/records -H "$TENANT" \
  -H "Content-Type: application/json" \
  -d '{"recordType":"withdrawal","submittedBy":"treasurer@example.com","payload":{"transactionType":"withdrawal","ventureId":"V-001","amount":60000}}'

# ...and a second approver releases it (dual approval)
curl -X POST http://localhost:5099/api/records/{recordId}/approvals -H "$TENANT" \
  -H "Content-Type: application/json" \
  -d '{"approver":"cfo@example.com","verdict":"Approve","comment":"verified"}'

# 3. An opportunity flows through the full flywheel:
#    validated -> scored -> decided -> trust learned from the outcome
curl -X POST http://localhost:5099/api/records -H "$TENANT" \
  -H "Content-Type: application/json" \
  -d '{"recordType":"Opportunity","submittedBy":"scout@example.com","payload":{"opportunityId":"OPP-1","sourceType":"broker-network","reliabilityRating":0.9,"estimatedValue":1200000,"riskRating":0.3,"complianceRating":0.95,"readinessRating":0.8}}'

# 4. Score history and the reconstructed cycle
curl -H "$TENANT" http://localhost:5099/api/scores/Opportunity/OPP-1
curl -H "$TENANT" http://localhost:5099/api/audit/cycle/{recordId}
```

## Repository Structure

- `docs/`, `chapters/`, `diagrams/` — framework documentation
- `ECARMF 002/` … `ECARMF-020/` — approved standards working folders
- `schemas/` — JSON schemas (meta-model base entity, models)
- `src/` — Platform Kernel (.NET 8 solution `ECARMF.sln`)
- `tests/` — kernel test suite
- `frontend/admin-ui/` — React admin console
- `packages/` — sample Knowledge Package manifests
- `docker/` — Dockerfiles, nginx config, `docker-compose.yml`
- `datasets/`, `examples/` — sample data and use-case examples

## Current Version

Framework standards: `0.1.0-foundation` (ECARMF-001 and ECARMF-002 approved).
Platform Kernel: MVP — operational transaction pipeline with sample Treasury
Controls package; partial conformance per ECARMF-001 §12.1.

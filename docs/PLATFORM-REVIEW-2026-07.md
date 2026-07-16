# ECARMF Platform — Complete System Review & Go-Live Plan
**Date:** July 2026 · **Branch:** enterprise-ai-os-phase1 (152 commits) · **All figures measured, not estimated.**

---

## 1. Executive summary

ECARMF is a functionally complete multi-tenant "AI operating system for business governance":
a generic kernel where every domain capability arrives as a JSON knowledge package (a **Skill**),
executed as metadata — controls, KPIs, agents, risk, billing — with zero kernel changes per tenant.

| Dimension | Grade | One-line verdict |
|---|---|---|
| **Functional completeness** | A− | Full arc works end-to-end; only AI backend + durable service are off |
| **Architecture** | A− | Clean kernel/package split, proven 10× (28 tenants, 77 packages, no forks) |
| **Feature verification** | B+ | Every feature verified live; strong audit trail |
| **Automated test coverage** | **C−** | 228 tests cover the kernel; **9 of 10 newest services have zero tests** |
| **Security posture** | **C** | Keys hashed + tier enforcement good; **no HTTPS, no rate limiting, test mode on** |
| **Operations readiness** | C+ | Service installed + runbook ready; **backups not scheduled**, no monitoring |
| **Performance/efficiency** | B− | Fine at current scale (400 MB DB); known N+1 and in-memory aggregation hot-spots |
| **UI/UX** | B− | Consistent, functional, discoverable (⌘K, System Map); **40-item nav is sprawling** |

**Bottom line:** the product is built. The path to production is not more features — it is
**hardening (security, tests, ops) + UX consolidation + the two owner switches (AI backend, elevated go-live).**

---

## 2. System inventory (measured)

### 2.1 Code
| Layer | Files | Lines |
|---|---|---|
| Backend C# (Domain / Application / Infrastructure / Api) | 271 (+67 migrations) | 31,809 |
| Frontend React 19 + TypeScript | 50 | 10,149 |
| Knowledge packages (JSON manifests) | 70 in repo / 77 live | — |
| Tests | 35 files | 228 test methods (233 assertions pass) |

### 2.2 API surface — 174 HTTP routes across 44 endpoint files
Records & intake · packages & catalog · skills & packaging · registries · scores (+riskOnly) ·
audit · users/identity/keys · platform tenants & config · billing (plans/usage/statements) ·
templates & onboarding advisor · demo seeding · period analysis · cases · risk treatments (+remediate/resolve) ·
platform risk & actions · agents & consult · advisor · AI settings · connectors/integrations ·
benchmarks (+peer) · renewals · library/documents · financial statements · PHI reveal ·
org units · treasury · health/hardening · reports · mail. **86 registered services.**

### 2.3 Frontend — 44 screens, 40 nav items
- **Guidance:** Start Here, System Map, Dictionary, Capability Explorer, ⌘K global search
- **Setup:** Organization, Packages, Controls, Integrations, Benchmarks, Renewals, Treasury, AI Backend
- **Input:** Data Entry, Statement Review
- **Output:** Activity, Dashboard, Risk Register, Risk Treatment, Period Analysis, Cases,
  Reports, Board Pack, Peer Benchmark, Audit Trail, Library, Capital Flows, AI Advisor, AI Agents
- **Platform (operator):** Action Center, Platform Risk, Health Board, Enroll Tenant (+AI recommender),
  Package Library, Skills, Skills Library, Demo Twins, Clients, Billing, Email

### 2.4 Live data (shared DB, 400 MB)
28 tenants (14 real + 14 demo twins) · 77 catalog packages · 69 skills (152 executable controls
mapped to 9 assertions) · 4,024 records · 3,955 control outcomes · 8,488 scores ·
**78,136 audit entries** · 733 risks (155 critical) across 23/28 tenants · 42 cases · 3 treatments.

### 2.5 The capability arc (all shipped & verified live)
Skills & per-skill billing → Skills Library (value = skill→control→assertion) → Package Library
(install any skill on any tenant) → cross-platform T9-041/042 (foundation-agnostic orchestration +
financial continuity) → universal risk register → per-tenant heatmap → platform-wide heatmap →
risk treatment (owner/strategy/residual) → governed remediation loop (spawn→approve→execute→auto-mitigate) →
period-vs-period analysis → cases/projects comparison → AI onboarding recommender → demo engine
(14 heavy twins) → dictionary, ⌘K search, action center, board pack, audit explorer, peer benchmark.

---

## 3. Completeness — what's genuinely missing

### 3.1 Blocked on owner actions (ready, waiting)
| Item | State | Unblock |
|---|---|---|
| AI backend | Plumbing proven live with stand-in endpoint; 0 tenants configured | Paste Anthropic key **or** run Ollama (runbook Part A) |
| Durable :5099 service | Installed, auto-start, stopped | Elevated `deploy\go-live.ps1 -LockDown -RepointShortcut` |
| Access keys | Zero issued (seeded users have no key) | Rotate operator key **before** lockdown (runbook B1) |
| 4 regulated real tenants (magcpa, altera, asset-fractional, tenant-10) | Skills present, no risk data | Seed after keys exist |

### 3.2 Functional gaps (build items)
- **Notifications/digest** — tasks & alerts exist in data but nothing pushes (email/SMTP unconfigured).
- **LLM refinement of onboarding advisor** — deterministic core only (by design; seam ready).
- **Document pipeline edges** — handwritten-doc phase, review UI for extraction confidence.
- **Skill pricing catalog** — code-defined defaults + per-skill override exist; no bulk price management.
- **Bare tenants** (acme-capital, nour-foods) have no foundation package → records don't process.

---

## 4. Quality & testing — the biggest debt ⚠

**Measured:** the kernel (rules, packages, registries, billing, identity, approvals…) is well covered
(228 tests). But of the 10 newest major services, **9 have zero dedicated tests**:

`PackageCatalogService`, `DemoSeedingService`, `PeriodAnalysisService`, `CaseAnalysisService`,
`PlatformRiskService`, `RiskTreatment` endpoints, `OnboardingAdvisorService`, `PlatformActionService`,
`ControlAssertions` — all verified live only. (`SkillCatalogService` has partial coverage via a billing test.)

**Risk:** any refactor can silently break these; live verification doesn't regress.

**Plan (Test Debt Paydown, ~2–3 sessions):**
1. Pure-logic first (cheap, high yield): `ControlAssertions.Classify`, `OnboardingAdvisorService`
   industry mapping, `PeriodAnalysisService` bucketing/deltas, skill `Classify/Resolve`.
2. Store-backed with the existing in-memory fakes: catalog install ordering, case metrics,
   platform risk roll-up, treatment lifecycle (open→remediate→resolve residual math).
3. One smoke test per new endpoint group (auth + happy path).
Target: ~330–360 tests; then wire a CI gate (GitHub Actions: build + test on push).

---

## 5. Security & operations — must-fix before real client data

| Finding | Severity | Fix |
|---|---|---|
| **No HTTPS** anywhere | High | Put the service behind a reverse proxy w/ cert (IIS ARR, Caddy, or nginx), or Kestrel cert binding |
| Deployed config in **test mode** (`AllowHeaderIdentity=true`) | High | go-live.ps1 `-LockDown` (already scripted) — after keys |
| **No rate limiting / lockout** on key auth | Medium | ASP.NET `AddRateLimiter` on auth + write paths |
| **Backups not scheduled** (script exists, 0 scheduled tasks) | High | `Register-ScheduledTask` for `backup-nightly.ps1`; test a restore once |
| No monitoring/alerting | Medium | Health endpoint exists → add a scheduled probe + event-log alerts; later App Insights/OTel |
| `robocopy /MIR` clobbers prod config on deploy | Medium | Move secrets/overrides to `appsettings.Production.json` excluded from mirror, or env vars |
| Secrets handling | Medium | Move connection string + platform AI key to environment/user-secrets, out of appsettings |
| Single SQL Express instance | Accepted for now | Fine for pilot; plan SQL Server upgrade path with growth |

**What's already good:** access keys stored hashed (shown once), sensitivity tiers enforced upstream
(Regulated refuses header identity), PHI masking with audited reveal, append-only audit (78k entries),
EF-parameterized SQL throughout, per-tenant isolation verified repeatedly.

---

## 6. Performance & efficiency

Fine at today's scale; these are the known hot-spots to fix **before** data grows 10×:

1. **Client-side N+1 manifest fetches** — CapabilityExplorer & ⌘K palette fetch every active package's
   full manifest individually (42 calls on tcel). *Fix:* one `GET /api/capabilities` server-side index (cacheable).
2. **In-app aggregation** — Period/Case analysis page records 200-at-a-time and aggregate in C#;
   PlatformRisk pulls up to 20k scores into memory. *Fix:* SQL GROUP BY aggregates (+ index on `Scores(TenantId, RiskType, ComputedAt)`).
3. **No caching** — skill library/catalog recomputed per request. *Fix:* short `IMemoryCache` (30–60 s) on operator roll-ups.
4. **Audit growth** — 78k rows already; add retention/archival policy (e.g., archive > 24 months) — fits the archiving theme.
5. Registries are in-memory per tenant (rehydrated at startup) — good; document memory ceiling per 100 tenants.

---

## 7. UI/UX review & improvement plan

**Strengths:** consistent dark design language; guided Home + System Map + Dictionary; ⌘K search;
tenant-aware branding/posture; empty states with next actions; error boundaries per screen; PHI masking UX.

**Issues, in priority order:**

| # | Issue | Recommendation |
|---|---|---|
| 1 | **Nav sprawl — 40 items** in one sidebar | Collapsible groups (remember state); role-aware nav (hide Platform group for clients, hide unused Setup items per tenant's skills); "favorites" pinning |
| 2 | Insight is fragmented (Dashboard / Risk / Periods / Cases / Board Pack are separate) | A composable tenant **Overview** landing that embeds the top widget of each, linking deeper |
| 3 | Feedback is inline-text only | Toast notifications for actions; global busy indicator; optimistic updates where safe |
| 4 | Accessibility unaudited | Pass: focus rings, aria labels on icon buttons, contrast check on state colors, keyboard nav beyond ⌘K |
| 5 | Mobile untested | Audit at 375px: sidebar becomes drawer (exists), tables need horizontal scroll wrappers |
| 6 | Dark-only | Optional light theme via CSS variables (structure already supports it) |
| 7 | No in-app help beyond Dictionary | Contextual "?" per screen linking to Dictionary terms; first-run tour on Home |
| 8 | Charts are minimal (bars/heatmap) | Add trend sparklines to Dashboard/Periods; keep the no-dependency approach or adopt a tiny chart lib |

**Suggested UX sprint (1–2 sessions):** items 1–3 give 80% of the perceived polish.

---

## 8. Master go-live plan

**Phase 0 — Switch on (owner, ~30 min)** *(runbook: `deploy/RUNBOOK-golive-and-ai.md`)*
① AI backend (Ollama or Anthropic key) → ② rotate operator key → ③ elevated
`go-live.ps1 -LockDown -RepointShortcut` → ④ issue client keys → ⑤ seed 4 regulated tenants.

**Phase 1 — Production hardening (1–2 sessions)** — HTTPS reverse proxy · schedule backups + test restore ·
rate limiting · secrets out of appsettings · deploy script stops clobbering config · health probe alerting.

**Phase 2 — Test debt paydown + CI (2–3 sessions)** — per §4; GitHub Actions build+test gate.

**Phase 3 — UX consolidation (1–2 sessions)** — nav collapse/role-aware · tenant Overview · toasts · a11y pass.

**Phase 4 — Efficiency (1 session)** — server-side capabilities index · SQL aggregates + Scores index · caching · audit retention.

**Phase 5 — Feature round-out (as needed)** — notifications/SMTP digest · LLM onboarding refinement ·
document-pipeline edges · bulk skill pricing.

> Ordering rationale: Phase 1 protects real client data the day keys exist; Phase 2 protects the
> codebase before more change; Phases 3–4 are polish that compounds; Phase 5 only after the platform is live.

---

*Prepared as a grounded audit: every number above was measured against the repo, the live :8080 instance,
and the database at review time.*

---

## 9. Post-program re-measurement (production program executed)

Phases 1–4 of `docs/WORK-ORDER-production-program.md` were executed and verified live
(commits `6833339`, `f7c0f04`, `8559bb0`, `b3fd4f9`). Updated scorecard:

| Dimension | Was | Now | Evidence |
|---|---|---|---|
| Automated test coverage | **C−** | **B+** | 233 → **331 tests**; all 10 previously-untested services covered; CI fixed (was dormant/Linux-broken) and gating every branch. Found+fixed a live advisor misclassification bug. |
| Security posture | **C** | **B** | HTTPS config-ready (verified http+https side by side), rate limiting live (300/30s per IP; 10/min on credential routes — verified 300×200 then 50×429), config/secrets in a deploy-proof Production overlay. Remaining: run with a real cert + the owner lockdown. |
| Operations readiness | **C+** | **B+** | Backup **and restore drill proven** (28 tenants recovered from a real .bak); registration scripts ready (one elevated run); health probe + event-log alerting scripted; deploys can no longer clobber config; audit archival in place. |
| Performance/efficiency | B− | **B+** | Capability fan-out 42+ requests → **1**; operator roll-ups cached with stamp invalidation (catalog 833→278 ms); risk-score index; audit archival keeps the live table bounded. |
| UI/UX | B− | **B** | Collapsible role-aware nav (Platform hidden for client keys), Home overview strip, toast feedback, focus-visible + keyboard operability pass. Remaining polish: light theme, tour, chart depth. |

Unchanged (already strong): functional completeness A−, architecture A−, verification B+.
**Owner-gated items remain the only blockers to full production:** AI backend, elevated
service start + key issuance, scheduled-task registration (all in `deploy/RUNBOOK-golive-and-ai.md`).

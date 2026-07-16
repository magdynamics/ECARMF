# ECARMF — Work Order: Production Hardening & Quality Program

**Audience:** the coding agent executing this plan. It is self-contained: file paths, code-level
guidance, acceptance criteria, and verification steps per task. Basis: `docs/PLATFORM-REVIEW-2026-07.md`.

**Scope:** Phases 1–4 of the review's master plan. Phase 0 (AI key, elevated `Start-Service`,
issuing access keys) is **owner-only — never attempt it**; Phase 5 (new features) is out of scope.

---

## 0. Ground rules (read first)

### 0.1 Conventions
- Backend: .NET 8 minimal-API style; endpoints in `src/ECARMF.Kernel.Api/Endpoints/*.cs`, registered
  in `Program.cs` via `app.MapXxxEndpoints()`. Services: interface + implementation in
  `src/ECARMF.Kernel.Application/<Area>/`, registered in that project's `DependencyInjection.cs`
  (infrastructure stores in `src/ECARMF.Kernel.Infrastructure/DependencyInjection.cs`).
- Persistence: EF Core, `ECARMFDbContext` (`src/ECARMF.Kernel.Infrastructure/Persistence/`),
  record classes `XxxRecord.cs` + `EfXxxStore`. Migrations:
  `dotnet ef migrations add <Name> --project src/ECARMF.Kernel.Infrastructure/ECARMF.Kernel.Infrastructure.csproj --startup-project src/ECARMF.Kernel.Api/ECARMF.Kernel.Api.csproj`
  then `dotnet ef database update` (same args). Auto-migrate runs only in Development.
- Frontend: React 19 + TS, no UI framework, dark theme. Screens in
  `frontend/admin-ui/src/components/*.tsx`; nav + routes in `App.tsx` (NAV array + ternary chain);
  styles appended to `App.css`; API via `frontend/admin-ui/src/api.ts` (`api.get/post/put/delete`,
  tenant-aware headers). Components take `{ tenant, user }` and re-fetch on change.
- Comments: explain *why*, match existing density. Commit style: imperative title + body explaining
  rationale + `Co-Authored-By` trailer (see `git log`).

### 0.2 Verification protocol (every task)
1. `dotnet build src/ECARMF.Kernel.Api/ECARMF.Kernel.Api.csproj -c Debug` → 0 errors.
2. `dotnet test` → **all pass** (233 baseline; Phase 2 raises it — never merge with fewer passing than before).
3. Frontend: `cd frontend/admin-ui && npx tsc --noEmit && npm run build`.
4. Live check when observable: run a dev instance
   `ASPNETCORE_ENVIRONMENT=Development Security__AllowHeaderIdentity=true ASPNETCORE_URLS=http://localhost:5000 dotnet run --project src/ECARMF.Kernel.Api ...`
   (background), curl the new endpoint with headers `X-Tenant-Id` / `X-User-Id: owner@platform`,
   and browser-verify UI changes (Vite proxy target via `.claude/launch.json` env `VITE_API_TARGET`; **revert after**).
5. Deploy to the running :8080 instance only when asked; follow §0.4.
6. Commit + push per task (small, reviewable commits).

### 0.3 Known gotchas (hard-won; do not rediscover)
- **`EfTransactionStore.QueryAsync` caps `Take` at 200** — page (`skip += 200`) to count everything.
- **Scores compute asynchronously** after `ReceiveAsync` returns — re-query after a beat, don't assert immediately.
- **Score↔record join:** `ScoreRecord.CorrelationId == Transaction.TransactionId` (both `Guid`).
- **`RecordReceived` is owned by each tenant's foundation package.** Cross-tenant packages must NOT
  declare events; they attach rules/KPIs that reference the tenant's existing event.
- **Regulated / HighSensitivity+ tenants refuse header identity** on every route — test against
  Standard/Elevated tenants (`universal-dental`, `magdynamics`, demo twins) in header mode.
- **Risk heatmap plots by subject** (`subjectField`) — distinct risks need distinct subject values.
- **`robocopy /MIR` clobbers deployed config** (fixed by P1-5; until then re-set
  `AllowHeaderIdentity=true` in `C:\ECARMF\app\appsettings.json` after any mirror).
- Run the deployed exe **from `C:\ECARMF\app`** (content root) or connection-string load fails.
- Dev process file-locks build output — stop the :5000 process before rebuilding.
- After deleting a tenant's rows in SQL, **restart the API process** — in-memory registries go stale
  and reloading that tenant's packages silently no-ops.
- The untracked `ECARMF 001/ECARMF ACCESS.xlsx` is the user's file — never touch or commit it.

### 0.4 Deploy procedure (when a task ships to :8080)
```powershell
# build & publish
cd frontend/admin-ui; npm run build          # frontend → wwwroot
dotnet publish src/ECARMF.Kernel.Api/... -c Release -o C:\ECARMF\staging
# stop :8080 pid, then (after P1-5 lands, /XF protects config):
robocopy C:\ECARMF\staging C:\ECARMF\app /MIR /XF appsettings.Production.json
# restart from C:\ECARMF\app with --urls http://localhost:8080; wait for /auth-mode 200
```
Frontend-only changes: copy `wwwroot` contents only; no restart needed.

---

## PHASE 1 — Production hardening *(do first; protects real client data)*

### P1-1 · HTTPS support
**Objective:** the service can serve TLS without breaking existing HTTP operation.
**Implementation:**
1. In `Program.cs` (before `builder.Build()`): configure Kestrel from config —
   if `Kestrel:Certificates:Default:Path` is set, add an HTTPS endpoint on `Https:Port`
   (default 5443) alongside the HTTP URL; otherwise change nothing. Cert password read from
   config key `Kestrel:Certificates:Default:Password` (supplied via env var
   `Kestrel__Certificates__Default__Password`, never committed).
2. Add `app.UseHsts()` only when HTTPS is active and environment is not Development.
   Do **not** add `UseHttpsRedirection` unconditionally (would break the http-only :8080 test instance).
3. Update `deploy/RUNBOOK-golive-and-ai.md` with a "Enable HTTPS" section: generating/importing a
   PFX (`New-SelfSignedCertificate` + `Export-PfxCertificate` for internal use; real cert for public),
   the two env vars, and the reverse-proxy alternative (IIS ARR / Caddy) for public exposure.
**Acceptance:** no cert configured → behavior identical to today (verify /auth-mode on :5000).
With a self-signed test PFX configured → `https://localhost:5443/auth-mode` answers 200
(`curl -k`). Both verified. Tests pass.

### P1-2 · Schedule backups + restore verification
**Objective:** `deploy/backup-nightly.ps1` (already complete: full backup, 14-copy retention,
logging) actually runs nightly, and restore is proven.
**Implementation:**
1. New `deploy/register-backup-task.ps1` (elevated): `Register-ScheduledTask` named
   `ECARMF-nightly-backup`, daily 02:00, runs the script with `-ExecutionPolicy Bypass`, as SYSTEM,
   `-Force` to allow re-registration. Print status after.
2. Add a `-VerifyRestore` switch to `backup-nightly.ps1`: restores the newest `.bak` as database
   `ECARMF_Verify` (`WITH MOVE` to distinct file names, `REPLACE`), checks
   `SELECT COUNT(*) FROM Tenants` > 0, then drops `ECARMF_Verify`. Log outcome.
3. Runbook: add "Backups" section (register task, manual run, restore drill quarterly).
**Acceptance:** run the backup script manually → `.bak` produced in `C:\ECARMF\backups`, log line OK;
run with `-VerifyRestore` → restore check passes and verify DB is dropped. Task registration script
parses cleanly (registration itself needs elevation — document, attempt, and if denied report
"needs elevated run" rather than failing the task).

### P1-3 · Rate limiting
**Objective:** brute-force protection on the key-auth surface; abuse ceiling everywhere.
**Implementation (`Program.cs`):** .NET 8 `builder.Services.AddRateLimiter`:
- Global partitioned-by-IP fixed window: 300 requests / 30 s (generous; UI screens burst ~40 calls).
- Named policy `"auth-sensitive"`: 10 / min per IP; apply via `.RequireRateLimiting("auth-sensitive")`
  to `POST .../rotate-key`, `POST .../users` (key issuance) in `PlatformEndpoints.cs`, and
  `PUT /api/settings/ai` in `AiSettingsEndpoints.cs`.
- `app.UseRateLimiter()` **after** `MapHealthEndpoints` (probes must never be limited) and before
  the auth middleware. Rejection: 429 with `Retry-After`.
**Acceptance:** hammer `/api/records` (>300 in 30 s via a curl loop) → 429s appear then recover;
normal UI browsing unaffected (open 3–4 screens in the browser, no 429 in console/network).
Add one integration-style test if feasible; otherwise live verification documented in the commit.

### P1-4 + P1-5 · Config/secrets separation + deploy no longer clobbers
**Objective:** deployed overrides (test-mode flag, connection string, future keys) survive every
deploy; secrets leave the repo-tracked file.
**Key fact:** ASP.NET defaults to the **Production** environment when `ASPNETCORE_ENVIRONMENT`
is unset — both the service and the :8080 process therefore load `appsettings.Production.json`
automatically if present in `C:\ECARMF\app`.
**Implementation:**
1. Create `C:\ECARMF\app\appsettings.Production.json` (deployed machine only, **never** in the repo)
   containing the current live overrides: `Security:AllowHeaderIdentity: true` (until go-live) and
   the `ConnectionStrings:ECARMF` value. Remove the need to patch `appsettings.json` post-deploy.
2. `deploy/update-app.ps1` line 7 and `deploy/go-live.ps1`: add `/XF appsettings.Production.json`
   to every robocopy mirror. `go-live.ps1 -LockDown/-Unlock` must now edit
   `appsettings.Production.json` (fall back to creating it) instead of `appsettings.json`.
3. `.gitignore`: add `appsettings.Production.json` defensively.
4. Runbook: document the precedence (base file ships with the app; Production file is the machine's).
**Acceptance:** full publish + mirror cycle → `/auth-mode` still reports `headerIdentityAllowed: true`
**without any manual re-edit**; Production file untouched by robocopy (check timestamp).

### P1-6 · Health probe + failure alert
**Objective:** know within 5 minutes if the service dies.
**Implementation:** new `deploy/register-health-probe.ps1` (elevated): scheduled task every 5 min
runs an inline probe of `http://localhost:5099/health/live` (fallback :8080) — on failure writes
Windows Event Log (source `ECARMF-Monitor`, EventId 1001, Error) and appends
`C:\ECARMF\logs\health.log`; on recovery writes an Information event. Runbook section with a note
that Event Viewer can email/toast via task on the event.
**Acceptance:** probe script run manually against the live instance logs OK; against a dead port
logs failure + event (event-source creation needs elevation — same reporting rule as P1-2).

---

## PHASE 2 — Test debt paydown + CI *(protects the codebase before further change)*

General: use the existing fakes (`tests/ECARMF.Kernel.Tests/Fakes/InMemoryStores.cs` — package,
score, transaction, audit fakes already implement the current interfaces). Add fakes only when
missing (e.g. `ICaseStore`, `IRiskTreatmentStore`, `IRenewalStore`, `ITenantDirectory` stubs exist
in various test files — prefer promoting shared ones into `Fakes/`). For private static helpers in
`DemoSeedingService`, add `InternalsVisibleTo("ECARMF.Kernel.Tests")` to the Application csproj
and make the helpers `internal static` rather than testing through the full seed.

Write one test class per item; names below are the minimum meaningful cases.

**P2-1 `ControlAssertionsTests`** (`Application/Packages/ControlAssertions.cs`)
- Each keyword bucket maps correctly (phi→Privacy, cross-tenant→Unauthorized, autonomous→AiGovernance,
  liquidity→Liquidity, dual/approval→Segregation, oig/aml→Compliance, drift/incident→RiskRemediation,
  reconcil/stale→Integrity); unmatched → Policy; precedence: text containing both "phi" and
  "cross-tenant" → Privacy (order of checks).

**P2-2 `OnboardingAdvisorTests`** (`Application/Onboarding/OnboardingAdvisorService.cs`, fake `IPackageCatalog`)
- Dental profile → `Regulated`, `HandlesPhi=true`, dental skills + 3 universal baseline skills.
- Unknown industry → baseline only, note about unrecognized industry, confidence Medium.
- `RegulatoryContext:"hipaa"` on a Standard industry → forces Regulated+PHI.
- Industry-skill cap: catalog with 10 matching packages → ≤6 industry skills returned.

**P2-3 `PeriodAnalysisTests`** (`Application/Analytics/PeriodAnalysisService.cs`)
- Monthly bucketing: records placed in correct windows across a month boundary.
- **Paging:** fake `ITransactionStore` that enforces the 200-row cap → all 450 records counted.
- Delta math: current vs previous (rejected ↓ = improved; records ↓ = not improved).
- Recommendations fire at thresholds (rejections +15%, activity −25%); stable data → "stable" message.
- Quarter labels (`Q3 2026`) correct.

**P2-4 `SkillCatalogTests`** (extend the partial coverage)
- `Classify`: foundation/integration→Core·0; autonomous/continuity→AddOn·1500; else Industry·500.
- `Resolve` precedence: stored override beats code default; Essential forces price 0.
- `ActivePricedSkillsAsync`: only active + AlaCarte + price>0; Essential excluded.
- `ActivateAsync` on absent skill installs via catalog with deps; on inactive stored → re-activates.

**P2-5 `PackageCatalogTests`** (`Application/Packages/PackageCatalogService.cs`)
- `ListAsync` dedupes (id,version) across tenants; `installedInTenants` = Active only.
- `InstallAsync` clones (new `EntityId`, target `TenantId` — source manifest unmutated),
  installs dependencies in topological order, skips already-present (idempotent), surfaces load errors.

**P2-6 `CaseAnalysisTests`** (`Application/Cases/CaseAnalysisService.cs`)
- Metrics per case: records/rejected/flagged/distinct-controls; scores joined via `CorrelationId`;
  avg only over scored records; empty case → zeros; ordering by records desc.

**P2-7 `PlatformRiskTests`** (`Application/Analytics/PlatformRiskService.cs`)
- Latest-per-(tenant,subject) dedup (newest-first input).
- Severity/likelihood from metadata when present; derivation fallback from value; clamp 1..5.
- Critical = sev≥4 ∧ like≥4; per-tenant summary ordering by critical then risks.

**P2-8 `RiskTreatmentFlowTests`** (endpoint-level or service extraction — prefer extracting the
resolve/remediate logic into a small `RiskTreatmentService` if endpoint testing is awkward)
- Open is idempotent per riskKey (second POST returns existing).
- Update validates strategy/status enums; clamps residual 1..5.
- Remediate: submits `AutonomousActionRequest` with `approved=false`, links id, status→InTreatment.
- Resolve: submits `approved=true/verified=true`, status→Mitigated,
  residual = (max(1, sev−2), max(1, like−1)) — verify 5×4→3×3 and 1×1→1×1.

**P2-9 `PlatformActionTests`** — urgency: overdue renewal=100, ≤7d=92, ≤21d=75, ≤45d=58; critical-risk
urgency 55+4n capped 100; ranking desc; `urgent` = count ≥90.

**P2-10 `DemoSeedingTests`** (internals-visible helpers)
- `SatisfyingValue` per operator (GreaterThan numeric → value+1, NotEquals → altered, etc.).
- `BuildRecord` satisfies all of a rule's conditions (run `ConditionEvaluator.Matches` over the result).
- `BuildKpiRecord`: formula identifiers numeric; riskType token field populated; sev/like within 1..5.
- `Spread` within last 118 days; `CaseIdFor` returns null every 4th, else rotates.

**P2-11 CI gate** — `.github/workflows/ci.yml`: on push/PR → checkout, setup .NET 8 + Node 20,
`dotnet build -c Release`, `dotnet test`, `npm ci && npx tsc --noEmit && npm run build`
(working-directory `frontend/admin-ui`). Badge in README if one exists.

**Phase acceptance:** `dotnet test` ≥ 330 passing, zero skipped/failing; CI file valid YAML
(runs green on push).

---

## PHASE 3 — UX consolidation

### P3-1 · Nav: collapsible + role-aware *(highest-impact UX fix — 40 items today)*
**Files:** `App.tsx` (sidebar render, NAV array), `App.css`.
**Implementation:**
1. Group headers (`Setup`, `Input`, `Output`, `Platform`) become toggle buttons with a chevron;
   collapsed state per group in `localStorage` (`ecarmf.nav.collapsed`), default: all expanded
   except `Platform` when not on the operator tenant.
2. Role-aware: hide the `Platform` group entirely when signed in with a **client** key
   (`me.isPlatformOperator === false`); in header mode keep current behavior (gate screens already exist).
3. Active-tab's group auto-expands; ⌘K unaffected (it's the power path).
4. Keep top-level ungrouped items (Start Here, System Map, Dictionary, Capability Explorer) always visible.
**Acceptance:** browser-verify: collapse persists across reload; client-key session shows no
Platform group; all 40 tabs still reachable; console clean.

### P3-2 · Tenant Overview strip on Home
**Files:** `components/Home.tsx` (extend — do **not** add a 41st nav item).
**Implementation:** above the existing guided steps, a compact 4-card strip fetched in parallel
(tolerate individual failures): **Risk** (`/api/scores?riskOnly=true&limit=3000` → count + critical,
→ `risk` tab) · **This period** (`/api/analysis/periods?count=2` → records + top delta, → `periods`) ·
**Renewals due 30d** (`/api/renewals`, → `renewals`) · **Open cases** (`/api/cases`, → `cases`).
Reuse `pd-delta`-style cards; each card is a button calling `go(tab)`. Skip cards whose endpoint
403s (regulated header mode) — render nothing rather than an error.
**Acceptance:** Home on `tcel-demo` shows 4 populated cards, each navigates; bare tenant shows
none/zeros without errors.

### P3-3 · Toast notifications
**Files:** new `components/Toasts.tsx` (provider + hook `useToast`), mount in `App.tsx`; `App.css`.
**Implementation:** minimal context: `toast.success(msg)/error(msg)`, stacked top-right, auto-dismiss
4 s, manual ×, `aria-live="polite"`. Adopt in the highest-traffic mutation screens first:
`Skills.tsx`, `SkillsLibrary.tsx`, `PackageCatalog.tsx` (install result), `RiskTreatments.tsx`,
`Cases.tsx`, `EnrollTenant.tsx` (step failures keep the inline step list; add a final toast).
Leave inline `error small` messages in place where they carry field-level context.
**Acceptance:** trigger a success (toggle a skill) and a failure (invalid case id) → toasts appear/
dismiss; screen-reader announcement present; console clean.

### P3-4 · Accessibility pass
**Scope:** `App.css` + touched components. (1) visible `:focus-visible` outline (accent, 2px) on all
interactive elements; (2) `aria-label` on icon-only buttons (menu toggle, search, close, chevrons);
(3) contrast check the state colors (`state-*`, `act-badge`, heatmap zone text) — adjust to ≥4.5:1
where the checker fails; (4) `aria-expanded` on the new nav group toggles and Skills Library rows;
(5) heatmap cells already carry aria-labels — verify keyboard focus reaches them (`tabIndex` if not).
**Acceptance:** keyboard-only walkthrough (Tab/Enter) can open a nav group, run ⌘K, activate a skill;
axe or manual contrast spot-check documented in the commit message.

---

## PHASE 4 — Efficiency

### P4-1 · Server-side capabilities index (kills the client N+1)
**Backend:** new `Application/Registries/CapabilityIndexService.cs` (`ICapabilityIndex`) — for a
tenant, read Active packages via `IPackageStore.GetByStateAsync` and flatten to
`CapabilityItem(kind, id, name, description, packageId)` for rules/KPIs/agents/entities/events/
knowledgeAssets (mirror the client logic in `CapabilityExplorer.tsx`). Register in DI. New endpoint
`GET /api/capabilities` (tenant-scoped, `Permissions.RegistryRead`) in a small
`CapabilityEndpoints.cs` + `Program.cs` mapping.
**Frontend:** `CapabilityExplorer.tsx` and `CommandPalette.tsx` replace the per-package manifest
loop with one `api.get('/api/capabilities')`; keep client-side filtering. Delete the now-unused
`FullManifest` local types.
**Acceptance:** tcel Capability Explorer shows the same 727-item counts as before from **one**
network request (verify in browser network tab); ⌘K tenant search still finds controls/agents;
unit test for the flattener (counts per kind from a two-package fake store).

### P4-2 · SQL-side aggregation + Scores index
1. Migration `ScoresRiskTypeIndex`: index on `Scores (TenantId, RiskType, ComputedAt DESC)` —
   check the existing entity config in `ECARMFDbContext` and add via `HasIndex`. Apply to the DB.
2. `PeriodAnalysisService`: replace record paging + in-memory bucketing with grouped SQL — add
   `ITransactionStore.CountByPeriodAsync(tenantId, from)` returning per-month
   `(Year, Month, Records)` via EF GroupBy, and keep outcome/score joins but fetch only ids in
   window. **Pragmatic bound:** if the full rewrite balloons, at minimum push the record counting
   to SQL and keep outcome enrichment as-is; document the follow-up.
3. `PlatformRiskService`: cap stays, but query becomes index-served (verify plan or timing before/after).
**Acceptance:** period analysis on `tcel-demo` returns identical numbers to pre-change (capture
before/after JSON and diff); response time for `/api/platform/risk` and `/api/analysis/periods`
measured before/after in the commit message; tests from P2-3 still green.

### P4-3 · Short-lived caching on operator roll-ups
`builder.Services.AddMemoryCache()`. In `PackageCatalogService.ListAsync`,
`SkillCatalogService.LibraryAsync`, `PlatformRiskService.OverviewAsync`: wrap in
`IMemoryCache.GetOrCreateAsync` (key includes method; TTL 45 s). **Invalidate** (remove keys) on
catalog install / skill packaging change / package activate-deactivate — inject `IMemoryCache`
into those mutation paths. Never cache tenant-scoped user data.
**Acceptance:** two consecutive `/api/catalog` calls: second is served from cache (log or timing);
after a skill packaging PUT the library reflects the change immediately (invalidation works).

### P4-4 · Audit retention policy
`Application/Operations` (alongside `IPlatformJanitor`): `IAuditRetentionService.ArchiveAsync(cutoff)`
— move `AuditEntries` older than cutoff (default 24 months) into new table `AuditArchive`
(same schema + `ArchivedAt`; one migration) in 5k batches, then delete from source. Operator
endpoint `POST /api/platform/audit/retention {monthsToKeep}` (PlatformOperator guard, audited).
**No scheduled deletion** — manual/endpoint-triggered only; append-only guarantees preserved
(rows are moved, never dropped).
**Acceptance:** seed a fake old entry (SQL, backdated), run the endpoint → row in `AuditArchive`,
gone from `AuditEntries`, everything newer untouched; audit entry written about the run itself.

---

## Execution order & definition of done

Work strictly in order **P1 → P2 → P3 → P4**; within a phase, tasks are independent unless noted
(P1-5 before any deploy-heavy task; P4-1 before touching the two frontend consumers).

**Program complete when:**
- [ ] P1: HTTPS-capable, backups scheduled + restore-verified, rate-limited, config survives deploys, health-probed
- [ ] P2: ≥330 tests passing, CI workflow green
- [ ] P3: collapsible role-aware nav, Home overview strip, toasts, a11y pass
- [ ] P4: single-call capabilities index, indexed/SQL-side analytics, cached roll-ups, audit archival
- [ ] Every task committed + pushed; `docs/PLATFORM-REVIEW-2026-07.md` scorecard updated at the end
      (re-measure; Security/Ops/Tests should each move ≥1 grade)
- [ ] :8080 instance redeployed once at the end of each phase and smoke-checked (auth-mode,
      one tenant screen, one operator screen)

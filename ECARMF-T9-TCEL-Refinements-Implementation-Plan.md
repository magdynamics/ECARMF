# ECARMF Framework Refinements — Implementation Plan & Coding Guidelines
## Derived from Tenant 9 (TCEL) — 67 packages, 6 waves, 13 ADR addenda

**Status:** PLAN — no code written yet. Each phase requires owner approval before implementation (same working agreement as Batches 1–3).
**Audience:** any engineer or AI agent implementing these refinements in this repository.
**Source:** `ECARMF Framework Refinements — Derived from Tenant 9 (TCEL)` (§ references below point at that document).

---

## 0. How to read this plan

The TCEL findings are about the **authoring process and framework tooling**, not tenant content. That matters here because TCEL's artifacts (CONTROL-CATALOG.csv, IMPLEMENTATION-BACKLOG.csv, prose agent specs, wave bundles) are *authoring-side* deliverables, while this kernel consumes **JSON `KnowledgePackageManifest`s**. So every TCEL item lands in exactly one of three places, and implementers must not blur them:

| Lane | What it means | Where it lives |
|---|---|---|
| **A. Kernel intake** | Validation/behavior the kernel enforces when a package is loaded or activated | `ManifestValidator`, `PackageLoader`, manifest schema in `src/ECARMF.Kernel.Domain/Packages/` |
| **B. Authoring tooling & templates** | Artifacts and scripts used *before* a package reaches the kernel | new `packages/templates/` folder, repo scripts, `packages/README.md` |
| **C. Process/docs** | Conventions humans and generators must follow; the kernel can help surface but not fully enforce | authoring checklist section in `packages/README.md`, ARCHITECTURE.md notes |

**Iron rules for all lanes (non-negotiable, from the existing working agreement):**

1. **Additive only.** No committed entity/table schema changes. Manifest-level fields are safe: manifests persist as a JSON blob (`KnowledgePackageRecord.ManifestJson`), so new optional manifest fields require **no EF migration**. If you believe you need a migration, stop and get approval first.
2. **Errors vs warnings are different channels.** `ManifestValidator.Validate` returns the *complete* error list (never fail-fast) and an error blocks load. TCEL items that are heuristic (semantic overlap, consolidation checks) must surface as **warnings** that never block load. This requires a small additive extension (see §P1.3).
3. **Everything audited.** Any new intake behavior (cycle rejection, supersede resolution) must append `AuditEntry` rows with a new `AuditCategories` constant, following the existing pattern in `src/ECARMF.Kernel.Domain/Audit/AuditEntry.cs`.
4. **Tests ride the existing suite.** Add a `TcelRefinementTests.cs` (or extend `PackageLoaderTests.cs`) in `tests/ECARMF.Kernel.Tests/`, using the in-memory fakes in `tests/ECARMF.Kernel.Tests/Fakes/InMemoryStores.cs`. Current baseline: 209 green tests — the suite must stay green.
5. **Backwards compatibility with the 23 shipped packages in `packages/` is mandatory.** Every existing manifest must still load and activate unchanged. Add a regression test that loads all of them through the new validator.

---

## Phase 1 — Critical tooling (TCEL items #1 and #2)

### P1.1 Dependency cycle detection (§3 — Critical)

**Problem recap:** two confirmed circular-dependency clusters (T9-005↔T9-016; T9-022↔T9-025↔T9-026) went undetected because nothing checks the declared dependency graph for cycles. In this kernel, `PackageLoader.ResolveDependenciesAsync` only checks that each dependency is *already active* — which means a true cycle doesn't error meaningfully, it **deadlocks silently**: neither package can ever activate, and each failure message says only "Dependency X is not active," which points the operator at the wrong fix (activating X — impossible).

**Where:** `src/ECARMF.Kernel.Application/Packages/ManifestValidator.cs` (self-dependency) + `src/ECARMF.Kernel.Application/Packages/PackageLoader.cs` (graph check at load time).

**Design:**
1. **Self-dependency** — pure manifest check, belongs in `ManifestValidator`: a package that lists its own `PackageId` in `Dependencies` is an error.
2. **Cross-package cycles** — needs the tenant's staged+active package set, so it belongs in `PackageLoader.LoadAsync` (which already has `IPackageStore` access), *not* in the static validator. Algorithm: build the directed graph of `PackageId → Dependencies[].PackageId` over (all stored packages for the tenant) ∪ (the incoming manifest); run a DFS/Kahn cycle check **only over cycles that include the incoming package** (do not retroactively fail already-stored packages — surface those as warnings).
3. **Error message must name the full cycle path** (`"ecarmf.a → ecarmf.b → ecarmf.a"`), because the TCEL clusters were only resolvable once someone could see the loop. A bare "cycle detected" is not acceptable.
4. Audit as `PackageFailed` (existing category) with the cycle path in `Detail`.
5. **Asynchronous/published-data-only relationships** (the sanctioned escape hatch in §3): these are *not* declared in `Dependencies` at all — document in the authoring guide (Lane C) that a forward/loose coupling must be expressed in the manifest `description`/boundary note, never as a `PackageDependency`. The cycle check therefore needs no special-case bypass. Do **not** add a "soft dependency" field in this phase.

**Acceptance criteria:**
- Loading a manifest that closes a 2-cycle or 3-cycle is rejected with the named path.
- Self-dependency rejected by `ManifestValidator` (unit-testable without a store).
- All 23 existing `packages/*.json` still load and activate (regression test).
- A cycle among *already-stored* packages (pre-existing bad state) does not brick unrelated loads — it surfaces as a warning on the report endpoint (P1.2), not an error on every load.

### P1.2 Machine-readable ID ledger (§2.1 — Critical, highest priority in the source doc)

**Problem recap:** six consecutive waves of T9-015 restarted ID numbering because the generating process never read the integrated state. Documentation failed six times; the fix is that the **generator must read machine state, not prose**.

**Two halves — do both:**

**(a) Kernel half — an ID-ledger *report endpoint* (Lane A).** The kernel already knows every registered ID per tenant (registries + stored manifests). Expose it:

- New endpoint: `GET /api/packages/id-ledger` (tenant-scoped, `Registry:Read` permission, mapped in `src/ECARMF.Kernel.Api/Endpoints/PackageEndpoints.cs`).
- Response: for each ID kind (`rules`, `capabilities`, `schemaTemplates`, `performanceFrameworks`, `workflows`, `agents`, `knowledgeAssets`, `aiExtractionTemplates`, `events`, `entities`), return the **sorted list of every ID in use** across all stored (not just active) package versions for the tenant, plus `packageId@version` provenance per ID. Do **not** try to compute "nextControlId" — numbering schemes are authoring conventions the kernel can't know; the ledger's job is to make collisions impossible to miss, not to allocate.
- Implementation home: a small read-only service (suggested `Application/Packages/PackageIdLedgerService.cs`) that walks `IPackageStore.GetAllAsync(tenantId)` manifests. No new persistence — this is a projection, always consistent with reality by construction (the TCEL failure mode was a manually-maintained number going stale; a projection cannot go stale).
- Include the pre-existing-cycle warnings from P1.1 in this report (one endpoint an authoring pipeline polls before generating a wave).

**(b) Authoring half — the required input rule (Lane C).** Add to `packages/README.md` authoring checklist, stated as a hard rule: *any multi-wave or follow-on package generation MUST fetch `GET /api/packages/id-ledger` (or, offline, run the repo script below) and treat the returned IDs as reserved. A wave produced without consuming the ledger is a delivery error, not a style issue.* Optionally add a repo script (`scripts/id-ledger.ps1` or `.sh`) that produces the same projection from `packages/*.json` for offline authoring — same output shape as the endpoint so generators can use either.

**Acceptance criteria:**
- Endpoint returns every ID from every stored manifest with provenance; verified by test loading ≥2 packages and asserting the merged ledger.
- Duplicate-ID-across-packages attempts are already rejected at activation by `RegistryConflictException` — add a test that proves the ledger would have shown the ID *before* the collision (i.e., ledger lists IDs from *inactive/staged* versions too).
- README checklist updated.

### P1.3 Warnings channel (enabler for P1, P3)

`PackageOperationResult` (`src/ECARMF.Kernel.Application/Packages/PackageOperationResult.cs`) currently carries errors only. Add an optional `Warnings` list (additive; default empty; serialized in API responses from `PackageEndpoints`). Warnings **never** change the success/failure outcome. Every Phase-3 heuristic emits here. Keep the API response shape backwards-compatible (new field, nothing removed).

---

## Phase 2 — Manifest schema additions (TCEL items #6, and the schema part of #4)

All fields below are **optional with safe defaults**, live on the manifest (JSON blob → no migration), and must be ignored gracefully when absent (all 23 existing manifests omit them).

### P2.1 `supersedes` / `supersededBy` on the manifest (§5.1 — Medium)

**Where:** `src/ECARMF.Kernel.Domain/Packages/KnowledgePackageManifest.cs`.

**Design:**
- Add `Supersedes` (list of `{ packageId, packageVersion? }`) — declared by the *replacing* package. Do **not** add a writable `supersededBy` on the superseded side: the older manifest is immutable history (package versions are never overwritten — core invariant). "SupersededBy" is a *derived* view: compute it in package listing responses by scanning other manifests' `Supersedes`, mirroring how `RecurrencePattern` is derived from `RecurrenceMonths` (Batch 3 R17 precedent: one source of truth, no drift).
- `ManifestValidator`: a package must not supersede itself; entries need a non-empty `packageId`.
- `PackageLoader.ActivateAsync`: when the activating package supersedes an *active* package, do **not** auto-deactivate in this phase (that's a behavior change with blast radius — needs separate approval). Instead: append an audit entry (new category `PackageSuperseded`) and return a warning naming the still-active superseded package. The operator deactivates deliberately.
- **Duplicate-draft detection (the actual TCEL §5.1 failure):** loading a manifest with the same `PackageId@PackageVersion` as a stored one is already rejected (unique index `TenantId+PackageId+PackageVersion`). Add to the load path: if the same `PackageId` arrives with a *different* version and no `Supersedes`/higher-semver relationship to the stored drafts, that's fine (normal versioning) — but if two manifests share `PackageId` and `PackageVersion` **content differs**, that's the T9-035 case and is already impossible to store; make sure the rejection message says "a different draft of this exact package version already exists" rather than a generic conflict.

### P2.2 Agent Identity block (§1.3 — High; schema half)

**Where:** `src/ECARMF.Kernel.Domain/Packages/AgentDeclaration.cs`.

**Design:** add optional fields matching the T9-002–006 structure the source doc says should be the literal template: `Owner` (string), `IndependentValidator` (string), `RiskTier` (open string — do not enum it; same open-tag philosophy as `riskType`), and `Prohibited` (list of strings — the Allowed side is effectively `ContextSources` + `Persona`; don't duplicate it). `OutputDisclaimer` already exists — do not add a second disclaimer field.
- `ManifestValidator`: no new *errors* (existing packages omit these). Add a **warning** when an agent has no `Owner` — nudges without breaking.
- `AgentConsultService`/`AgentEndpoints`: surface the new fields read-only in agent listings; no behavior change.

### P2.3 `consolidates` field (§6 — Medium; schema half)

Add optional `Consolidates` (list of packageIds) to the manifest. Validation behavior is Phase 3 (P3.2); this phase just carries the declaration. Rationale for a distinct field vs `Supersedes`: consolidation *summarizes* live packages (they stay active); supersession *replaces* them.

---

## Phase 3 — Heuristic validations (TCEL items #5 and #7) — warnings only

### P3.1 Agent semantic-overlap check (§4.1 — High)

**Where:** inside `PackageLoader.LoadAsync`, after `ManifestValidator` passes, before staging. Compare each incoming `AgentDeclaration` against the tenant's `AgentRegistry` (all active agents).

**Design — keep it deliberately dumb and cheap:**
- Signal 1: normalized-name similarity (tokenize `AgentId`/`Name`, flag on shared dominant tokens — "executive", "advisor", "insights", "decision").
- Signal 2: bag-of-words overlap between the incoming agent's `Description`+`Persona` and each registered agent's, using a stopword-filtered Jaccard/containment score with a conservative threshold.
- Output: **warning only** — `"Agent 'executive-risk' overlaps registered agent 'executive-advisor' (ecarmf.x@1.0.0): shared scope terms [executive, synthesis, board]. Confirm the boundary or record the decomposition decision."` Never an error: the kernel cannot judge semantics; four TCEL agents overlapped *legitimately differently* and one decomposition (§4.3) was correct.
- Do **not** call an LLM for this in the load path (load must stay deterministic and offline). If smarter matching is wanted later, it's an authoring-time tool, not intake.
- **Agent status tracking (§4.2):** Planned/Stub/Built/Retired is an *authoring registry* concern (TCEL's T9-007 lives outside this kernel). Lane C only: add the status-table convention to the authoring guide. The kernel's contribution is already correct — an agent either exists in an active package or it doesn't.

### P3.2 Consolidation-is-real check (§6 — Medium)

**Where:** `PackageLoader.LoadAsync`, using P2.3's `Consolidates` field.

**Design:**
- Error (blocks load): a `Consolidates` entry naming a package that has never been stored for this tenant — consolidating nothing is the T9-015-024 failure verbatim.
- Warning (doesn't block): for each consolidated packageId, scan the incoming manifest's own content (rule descriptions/metadata, knowledge-asset relationships, capability descriptions) for at least one literal reference to that packageId or to any ID that package declared (the ID ledger projection from P1.2a gives you the ID set for free — reuse `PackageIdLedgerService`). Zero references → warning: *"declares consolidation of ecarmf.x but references none of its 41 declared IDs."*
- Accept the known limitation openly in code comments: this is a tripwire, not proof of semantic consolidation. File-presence passed T9-015-024; this check would have failed it — that's the bar.

---

## Phase 4 — Templates, boundary docs, and delivery tooling (TCEL items #3, #4, #8, #9, #10)

All Lane B/C. No kernel code. One PR, mostly new files.

### P4.1 Literal authoring templates (§1.1–1.4 — High)

Create `packages/templates/` containing:
- **`package-template.json`** — a complete skeleton manifest with every section present (entities, events, rules, capabilities, schemaTemplates, performanceFrameworks, workflows, agents, knowledgeAssets, aiExtractionTemplates, dependencies, and the new supersedes/consolidates), each with one fully-populated placeholder element and `_comment` keys explaining required vs optional. This is the kernel-world equivalent of TCEL's "literal CSV header, not prose description."
- **`agent-template.json`** — one `AgentDeclaration` with the full Identity block (agentId, name, owner, independentValidator, riskTier, persona, contextSources, sampleQuestions, prohibited, outputDisclaimer), copied from the best existing example (`ai-magdynamics-v1.json`'s agents are the house style).
- **CSV header templates** (for tenants like TCEL whose deliverables include control catalogs/backlogs even though this kernel doesn't ingest them): `CONTROL-CATALOG.header.csv` with the 6-column row `Control ID,Control,Type,System Behavior,Severity,Owner` **plus** `Evidence` per §1.4's improvement (decide 6 vs 7 columns with the owner — §1.4 explicitly says consider adopting `Evidence`; recommend adopting: 7 columns), and `IMPLEMENTATION-BACKLOG.header.csv` with `Epic,Name,Priority,Scope,Owner,Dependency` (§1.2 + §1.4 additions).
- Update `packages/README.md`: templates are the **starting point** for any new package; deviating from a template is allowed but must be called out in the package description (per §1.4 — drift can be improvement; silent drift is the bug).

### P4.2 Boundary-vs-neighbor convention (§7 — Medium)

Authoring rule in `packages/README.md`: any package entering a domain where a package already exists for the tenant must include, in its manifest `description`, an explicit `Boundary vs <packageId>:` sentence stating what it deliberately does *not* cover. Optional kernel assist (defer unless cheap during P3): warn when an incoming package declares entities/KPIs whose `TriggerRecordType`s are already claimed by another active package and the description contains no `Boundary` marker.

### P4.3 Bundle-from-standalone build script (§2.2 — Medium)

Repo script (`scripts/build-bundle.ps1`): assembles any delivery bundle *from* the standalone files (zip of the exact bytes) and emits a SHA-256 manifest. The rule to document: bundles are never authored independently — they are always derived. This is pure Lane B; the kernel never sees bundles.

### P4.4 Not in this repo — flag, don't build

- **§8 T9-009 certification pass:** Tenant 9's content does not live in this repository. This is a next step for the TCEL delivery pipeline, not an ECARMF-kernel work item. Record it in the roadmap; do not attempt here.
- **§4.2 central agent status registry:** same — the T9-007 registry format is the standard for *authoring-side* tracking; the kernel-side reality check is P1.2's ledger.

---

## Implementation order, sizing, and gating

| PR | Contents | Size | Risk | Gate |
|---|---|---|---|---|
| 1 | P1.3 warnings channel + P1.1 cycle detection + tests | S–M | Low (additive; one hot path touched) | Owner approval on this plan |
| 2 | P1.2 ID-ledger service + endpoint + README rule + tests | M | Low (read-only projection) | — |
| 3 | P2.1–P2.3 manifest fields + validator rules + audit categories + tests | M | Low (JSON-blob fields; no migration) | Confirm P2.1 "warn, don't auto-deactivate" stance |
| 4 | P3.1 overlap warnings + P3.2 consolidation checks + tests | M | Medium (heuristics — tune thresholds against the 23 real manifests; zero false errors allowed, false-positive warnings acceptable) | — |
| 5 | P4.1–P4.3 templates, conventions, script | S | None (docs/files) | Decide Evidence column (recommend yes) |

**Definition of done, whole effort:** all 209+ tests green; all 23 existing packages load/activate byte-identical behavior except new warnings; every new intake behavior audited; `packages/README.md` authoring checklist updated; nothing in the plan required an EF migration.

**Cross-tenant caveat (from the source doc itself):** these patterns come from one tenant. Items P3.1/P3.2 thresholds and the CSV column decisions should be revisited once Tenant 10 (RCM — already spec'd in this repo) and Tenant 11 materials land; the two Critical items (cycle detection, ID ledger) are safe regardless — they encode invariants, not conventions.

# Segment Strategy — where the urgent buyers actually are

*Director ARM analysis. MAG's insight (2026-07-17): urgency to buy is NOT proportional to how
OFTEN a business gets cited — it's proportional to how MUCH a failure COSTS and how HARD it is
to undo. A food-handler cert is ~$200 and a week to fix → weak buyer. A HIPAA breach, an SEC
action, a habitability lien → six figures, months, and reputational scarring → real buyer.
We don't yet know which segment answers fastest, so we run several in parallel and measure.*

## The scoring model

**Buyer urgency = Consequence × Irreversibility.** Then sequence the urgent ones by how fast we
can reach and close them, and by whether we can even FIND them (open public data vs. gated).

| Segment | Consequence of a failure | Fix cost / time | ACV (annual) | Sales velocity | Findable (open data?) | Our fit |
|---|---|---|---|---|---|---|
| **Independent restaurants** (multi-loc) | Low — $ hundreds, days, reversible | Low | Low ($3–9k) | **Fast** | **Yes — open API (live)** | Yes |
| **Property mgmt / landlords** (multi-bldg) | **High** — liens, tenant lawsuits, habitability, vacancy | **High** | Med–High | Medium | **Yes — open API (live)** | Yes |
| **Dental groups / DSO** | **Very high** — HIPAA $100k–$1.9M, license, patient harm | **Very high** | High | Medium | Partial — gated (HHS/board CSV) | Yes (deep) |
| **Medical billing / RCM** | **Severe** — PHI + False Claims Act + payer clawback | **Severe** | High | Med–slow | Partial — gated | Yes |
| **RIA / family office** | **Very high** — SEC enforcement, fiduciary liability | **Very high** | **Very high** | **Slow** | Partial — ADV download | Yes |
| **Construction / GC** | High — mechanics liens, OSHA, project failure | High | High | Medium | Yes — OSHA/DOL (download) | Yes |

## The read

- **Restaurants are the wrong ANCHOR but the right ENGINE.** Low consequence = low urgency = low
  price. But open data + fast decisions make them a cheap **learning-and-cash engine**: they
  teach us messaging and float the lights while the high-ACV cycles mature. Keep them running;
  don't expect them to be the business.
- **Property management is the hidden gem.** High consequence (a lien or habitability judgment
  is expensive and slow — MAG's exact criterion) AND the data is **openly queryable, live**
  (Chicago building-violations API — I already pulled multi-building owners with 8–14 stacked
  open violations across 3–4 buildings). Same self-serve lead engine as restaurants, but a
  buyer who actually feels pain. **This is the sharpest parallel bet — run it now.**
- **Dental / RCM / RIA are the ACV prize but slower.** Highest consequence, highest willingness
  to pay, but gated data and longer cycles. Worth pursuing deliberately, not for first cash.

## The decision: run THREE segments in parallel, measure response, reallocate

| Track | Segment | Role | Data | Status |
|---|---|---|---|---|
| **A — Velocity** | Restaurants | Learn messaging, fast small wins, cash | Open API | **Live — 3 briefs ready** |
| **B — Pain+Open** | Property / landlords | Real urgency + self-serve leads | Open API | **Proven live — build the tool next** |
| **C — ACV** | Dental groups / DSO | The thesis, the big tickets | Gated (1 download) | Tool built — needs the HHS/board CSV |

Run all three cheaply for 3–4 weeks. **The market tells us who answers.** Whichever track
books the first paid pilot fastest gets doubled down; the laggard gets paused. We are not
guessing our ICP — we are letting response rate reveal it, which is the only honest way when
we genuinely don't know yet.

## Why this is the disciplined move, not scatter

Three tracks, but they share ONE engine (public-record → rank → Risk Brief → give-first
outreach → demo on a live tenant) and ONE product. The marginal cost of adding a segment is a
data-source adapter and a package tweak — hours, not weeks — because the platform onboards
industries as knowledge, not code. Parallelism is cheap for us specifically. That asymmetry is
why we can afford to let the market pick.

## Next concrete steps
1. **Build `find-property-violation-leads.ps1`** (Chicago building-violations API — proven live).
   Ranks multi-building owners by stacked open violations. *(Director ARM — I can do this now.)*
2. **Restaurants** — keep as the velocity track; JJ Fish call stands.
3. **Dental** — one HHS/board CSV download unlocks Track C.

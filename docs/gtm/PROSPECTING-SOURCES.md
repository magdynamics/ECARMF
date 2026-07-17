# Prospecting Sources — the evidence engine, per vertical

*Every ECARMF approach is qualified by the prospect's own public record. This is the map of
where that record lives, which tool reads it, and how automatable it is. Doctrine: the tool
produces the ranked list; a human verifies the facts are really the prospect's before contact.*

## The four fronts and their tools

| Front | Tool | Public evidence | Fetch |
|---|---|---|---|
| **Restaurants / food** | `find-risk-exposed-leads.ps1` | City food-inspection failures | **Auto** (SODA API, live — 1,152 Chicago fails pulled) |
| **Dental / medical / RCM** | `find-dental-medical-leads.ps1` | HHS OCR PHI breaches (500+), board discipline | Semi-auto (download CSV → tool ranks) |
| **Asset mgmt / RIAs / family offices** | `find-advisor-family-office-leads.ps1` | SEC Form ADV disclosure events, multi-office, AUM | Semi-auto (ADV dataset → tool ranks; SEC data API live for targeted) |
| **Lien-exposed (any vertical)** | `find-lien-exposed-leads.ps1` | Federal/state tax liens, UCC, judgments | Hybrid (some county/state SODA live; others download) |

## Source detail

### Restaurants — City inspection data (fully automatable)
- Chicago: `data.cityofchicago.org/resource/4ijn-s7e5` — **confirmed live.**
- Same schema in most metros: NYC (`data.cityofnewyork.us`), LA County, Austin, Dallas, etc.
  Swap the endpoint in the script's `$url`.
- **Signal:** same business name, multiple failing addresses = multi-location control gap.

### Dental / Medical / RCM — HHS OCR "Wall of Shame"
- `ocrportal.hhs.gov/ocr/breach/breach_report.jsf` — **confirmed reachable (200).** Every PHI
  breach affecting 500+ people is published by federal law. Export is session-gated (JSF), so:
  filter State → "Export to CSV" → save as `docs/gtm/breach_report.csv` → run the tool.
- **Add:** state licensing-board disciplinary actions (e.g. IL IDFPR) — most publish
  disciplinary CSVs; drop as `docs/gtm/discipline-*.csv`.
- **Signal:** a dental *group* or billing company with a breach is the bullseye — the pitch is
  literally our headline: *the AI runs on your premises, PHI never leaves the building.*

### Asset management / RIAs / Family offices — SEC
- Form ADV bulk dataset (SEC open data / IAPD compilation) → `docs/gtm/form-adv.csv` → tool.
- `data.sec.gov` submissions API — **confirmed live** for targeted firm/CRD lookups.
- **Signal:** disclosure/disciplinary events + multiple offices + "family/private wealth" naming
  = a multi-entity, already-audited firm that feels compliance load daily.

### Liens — federal, state, UCC, judgment
- **Confirmed live SODA example:** Colorado UCC — `data.colorado.gov/resource/wffy-3uut.json`
  (returns real lien filings). Cook County catalog lists UCC, tax-lien, sheriff-sale, and
  child-support-lien datasets (`datacatalog.cookcountyil.gov`, search "lien").
- Federal tax liens are recorded at the **county** level — pull the county recorder's export.
- State tax liens: most state SOS / revenue depts publish a lien lookup with CSV export.
- Drop every export in `docs/gtm/liens/*.csv`; the tool auto-detects the name/amount/date
  columns and cross-references. **Signal:** a business appearing in MULTIPLE lien sources =
  stacked creditors = lost financial control = our buyer.

## The scoring philosophy (shared across all four tools)

`urgency = fit × exposure × recency`, where **fit** = multi-entity + we-have-the-package,
**exposure** = documented violation/breach/lien (the more, the sharper), **recency** = an open
wound the prospect is thinking about *right now*. Multi-entity is always the multiplier — it's
our differentiator and their unsolved problem in one signal.

## The production rule (non-negotiable)

The tool ranks; **MAG verifies each fact is really the prospect's** (name collisions are real —
"SUBWAY" is 14 franchisees, not one lead; skip franchisor-controlled national chains) **and MAG
makes contact.** AI produces the list and the brief; humans publish. One wrong fact in a Risk
Brief destroys the credibility the whole approach depends on.

## Legal / ethical guardrails

- Only **public** records, accessed through their **public** interfaces. No scraping behind
  logins, no purchased PII, no credit data (FCRA), no compiling sensitive personal profiles.
- We present a business's own public compliance facts back to it as a service — the same facts
  its customers, landlord, and insurer can already see. We are the ally against the next
  event, never a threat about the last one. Tone guardrail lives in `RISK-BRIEF-TEMPLATE.md`.

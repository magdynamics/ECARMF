# Lead Qualification — evidence before approach

*No cold calls. Every approach carries proof of the prospect's own documented exposure.*

## The funnel

| Stage | Definition | Gate to next stage |
|---|---|---|
| **Lead** | A multi-entity business in a vertical we have packages for | Appears in a public risk record OR has a warm path |
| **Qualified (MQL)** | Documented exposure: failed inspection, HIPAA breach report, OSHA citation, board action — public, recent (≤12 months) | Risk Brief prepared from their public record |
| **Opportunity** | They accepted the Risk Brief conversation | Demo on a tenant shaped like their business |
| **Founding client** | Signed pilot terms, key issued | First invoice |

## Scoring (per lead, 0–100)

- **Fit (max 40):** multi-entity +20 · regulated/inspected vertical +10 · we have their industry package +10
- **Urgency (max 40):** documented violation/breach +20 · repeat offenses (control gap, not bad luck) +10 · within 90 days +10
- **Access (max 20):** warm path +20 · local/reachable owner +10 · national franchise −20 (franchisor-controlled: skip)

≥60 = approach this week. 40–59 = nurture list. <40 = skip.

## Public evidence sources (the qualification engine)

| Vertical | Source | Automation |
|---|---|---|
| Restaurants / food (Chicago) | City of Chicago Food Inspections (SODA API) | **`scripts/find-risk-exposed-leads.ps1`** — live, ranked, multi-location flagged |
| Restaurants (other metros) | Most large cities publish the same dataset (NYC, LA County, Austin…) | Same script, swap endpoint |
| Dental / medical / RCM | HHS OCR breach portal ("wall of shame") — every PHI breach ≥500 is public; state dental board disciplinary actions | Manual pull, monthly |
| Any employer | OSHA establishment search + DOL enforcement data (enforcedata.dol.gov) | Manual pull, monthly |
| CPA firms (channel) | Not violation-driven — qualified by client count & advisory ambitions | Referral + LinkedIn |

## First live run (2026-07-17, Chicago, 180 days): 1,152 failing businesses

Top qualified targets (multi-location independents; national franchises excluded):

| Prospect | Locations | Fails | Latest | Score notes |
|---|---|---|---|---|
| **JJ FISH** ⭐ | 2 | 4 | 2026-04-10 | WARM + documented + already a live tenant → **Opportunity, call first** |
| Sharks Fish & Chicken | 6 | 7 | 2026-06-26 | Same cuisine as JJ Fish — packages already fit |
| Baba's Famous Steak & Lemonade | 3 | 3 | 2026-07-13 | Failed FOUR DAYS ago — maximum urgency |
| Taco Burrito King | 3 | 3 | 2026-07-06 | Local chain, fresh wound |
| Bittersweet | 2 | 3 | 2026-07-10 | |
| Cocula Restaurant | 2 | 2 | 2026-06-15 | |
| Lou Malnati's | 2 | 2 | 2026-06-26 | Larger org — longer cycle, bigger prize |

Full ranked list: `docs/gtm/leads-chicago-food.csv` (regenerate anytime with the script).

## The rule of respect

We never mock, never threaten, never ambulance-chase. The tone is a peer who reads public
records professionally: *"this is visible to everyone — including your customers and your
insurer; here's how operators like you got ahead of it."* We are the ally against the next
violation, not a vulture circling the last one.

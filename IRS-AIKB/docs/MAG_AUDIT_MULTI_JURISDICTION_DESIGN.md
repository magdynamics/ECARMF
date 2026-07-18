# MAG Audit Multi-Jurisdiction Design

## Product boundary

MAG Audit has two primary components:

1. Multi-Jurisdiction Audit Intelligence Engine.
2. Client Input and Case Management System.

Security, evidence lineage, professional review, workflow, and governance form a
shared control foundation. The client portal and evidence vault are reused across
all jurisdictions.

## Jurisdiction modules

```text
MAG Audit Intelligence Engine
|- US-IRS: Federal IRS Audit Intelligence Engine
|- US-IL-IDOR: Illinois IDOR Audit Intelligence Engine (reserved)
|- Future state and local authority modules
`- Cross-Jurisdiction Reconciliation Engine
```

The existing IRS-AIKB assets remain assigned to `US-IRS`. They must not be treated
as Illinois rules merely because federal return information may feed an Illinois
return.

## Illinois reserved module

`US-IL-IDOR` is registered now with four planned tax-type packages:

- Illinois income tax
- Illinois sales and use tax
- Illinois withholding tax
- Illinois specialty taxes

The module currently permits client, taxpayer, return, evidence, and case intake.
It does **not** permit Illinois risk scoring, audit analysis, deadline conclusions,
or recommendation release. Those capabilities remain blocked until the applicable
Illinois sources, forms, mappings, techniques, rights, notices, tests, and professional
approvals are populated.

## Illinois knowledge work reserved for a later phase

The later Illinois build will populate:

- IDOR source and immutable-version registry
- Illinois statutes and administrative rules
- Income and sales tax audit manuals
- Forms, instructions, schedules, and historical versions
- Return-line and federal-to-Illinois mappings
- Audit technique and evidence rules
- Notice and correspondence dictionary
- Taxpayer rights and representation rules
- Protest, conference, hearing, tribunal, and court pathways
- Deadline controls with notice-specific professional verification
- Penalty, interest, and reasonable-cause rules
- Illinois-specific risk and readiness scoring
- Cross-jurisdiction reconciliation tests

## Non-substitution control

Every case, source, rule, notice type, and assessment carries a `module_id`. A rule
from one jurisdiction cannot execute for another jurisdiction unless a reviewed,
explicit cross-jurisdiction mapping authorizes the relationship. Shared accounting
facts may be reused; legal conclusions may not.


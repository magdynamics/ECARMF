# IRS Audit Intelligence Knowledge Base (IRS-AIKB)

IRS-AIKB is a version-controlled foundation for organizing public IRS examination guidance into traceable sources, audit techniques, risk indicators, and readiness assessments.

Within the broader MAG Audit design, this repository module is the federal `US-IRS`
jurisdiction package. A governed, intake-only space for the future Illinois IDOR
engine is registered as `US-IL-IDOR`; its knowledge base is intentionally not yet populated.

## Status

This repository module contains the complete mainstream corpus linked by the IRS Audit Technique Guides index as checked on July 17, 2026. It includes 26 PDF artifacts totaling 2,482 pages and 17 web-native source pages. PDF covers were rendered and visually verified, every source is hashed and versioned, and page/heading text is extracted into a populated SQLite database. IRM Part 4, Exempt Organization ATGs/TGs, LB&I materials, and other specialized examination programs remain separate expansion phases.

The system distinguishes procedural IRS guidance from binding legal authority. Audit Technique Guides and the Internal Revenue Manual help identify examination procedures and risks; they do not replace the Internal Revenue Code, Treasury Regulations, controlling cases, or professional judgment.

## Included

- SQLite schema with source/version lineage
- Full-text search tables
- Rules-based audit-readiness engine
- Seed risk rules
- JSON profile assessment CLI
- Architecture and governance notes
- Source manifest template
- Automated tests
- Client onboarding, secure upload, and completeness controls
- Sponsor/referral relationship records with deny-by-default, client-consented access
- Case-, taxpayer-, year-, action-, and artifact-scoped sponsor authorization decisions
- Client transparency, service-agreement and authorization signature workflows
- Governed AI-agent contributions to communication, scheduling, evidence, rights, and value reporting
- 26 official IRS-linked PDFs totaling 2,482 pages
- 17 preserved web-native IRS source pages
- Populated SQLite snapshot with 2,604 searchable sections
- 2,396 authority candidates and 1,979 technique candidates
- SHA-256 source manifest and build report

## Quick start

Requires Python 3.11 or newer. Install the package to obtain its PDF dependency.

```powershell
python -m irs_aikb.cli init-db --database data/irs_aikb.db
python -m irs_aikb.cli load-manifest --database data/irs_aikb.db --manifest source-manifest/sources.csv
python -m irs_aikb.cli ingest-corpus --database data/mainstream_atg.db --registry source-manifest/mainstream_atg_registry.csv --root . --retrieval-date 2026-07-17
python -m irs_aikb.cli stats --database data/mainstream_atg.db
python -m irs_aikb.cli assess examples/demo_profile.json
python -m irs_aikb.cli assess-portfolio examples/demo_portfolio.json
python -m irs_aikb.cli evaluate-sponsor-access examples/demo_sponsor_access.json --output data/demo_sponsor_decision.json
python -m irs_aikb.cli list-jurisdiction-modules
python -m irs_aikb.cli evaluate-jurisdiction examples/demo_idor_placeholder.json --output data/demo_idor_gate.json
python -m irs_aikb.cli evaluate-client-engagement examples/demo_client_engagement.json --output data/demo_client_engagement.json
python -m unittest discover -s tests -v
```

Run commands from this `IRS-AIKB` directory.

The portfolio command produces independent public-selection-indicator,
adjustment-exposure, documentation-readiness, controversy-readiness, and
confidence scores. Its priority ranking is CPA workflow triage—not a prediction
that the IRS will audit a return.

## Review states

- `machine_extracted`: generated from a source and awaiting review
- `curated_seed`: intentionally authored but not tax-law validated
- `technical_reviewed`: reviewed by a qualified tax professional
- `legal_current`: checked against current controlling authority
- `superseded`: retained for historical traceability

## Repository layout

```text
IRS-AIKB/
├── docs/
├── examples/
├── irs_aikb/
├── migrations/
├── source-manifest/
└── tests/
```

## Non-reliance notice

This software supports research and audit-readiness workflows. It does not provide legal or tax advice, predict IRS selection, or establish that an asserted tax position is correct.

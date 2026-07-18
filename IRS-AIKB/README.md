# IRS Audit Intelligence Knowledge Base (IRS-AIKB)

IRS-AIKB is a version-controlled foundation for organizing public IRS examination guidance into traceable sources, audit techniques, risk indicators, and readiness assessments.

## Status

This repository module contains the verified seven-document initial IRS ATG corpus: Publications 5495, 5522, 5558, 5602, 5603, 5653, and 5712. Together they contain 1,125 pages. Each PDF was downloaded from `irs.gov`, parsed for page count, cover-rendered for visual verification, and registered with a SHA-256 hash. The broader IRS examination corpus and section-level extraction remain future work.

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
- Seven official IRS ATG PDFs totaling 1,125 pages
- SHA-256 source manifest and build report

## Quick start

Requires Python 3.11 or newer and no third-party packages.

```powershell
python -m irs_aikb.cli init-db --database data/irs_aikb.db
python -m irs_aikb.cli load-manifest --database data/irs_aikb.db --manifest source-manifest/sources.csv
python -m irs_aikb.cli assess examples/demo_profile.json
python -m unittest discover -s tests -v
```

Run commands from this `IRS-AIKB` directory.

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

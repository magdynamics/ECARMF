# Production Portfolio Pipeline v0.4

Release v0.4 connects the existing evidence, return, reconciliation, intelligence,
rights, and controversy layers into one controlled portfolio workflow.

## E-file XML

The XML adapter rejects DTD/entity declarations, enforces a size limit, hashes the
original, recognizes core return families, inventories unmapped numeric tags, and is
metadata-only by default. Its starter tag registry covers high-level income, expense,
tax, and balance-sheet values. Production mappings require the exact IRS Modernized
e-File schema version and authorized tax-software exports; tag-name similarity alone
is not enough for final validation.

## Benchmarking

Peer comparisons use versioned cells and require at least 20 observations. The engine
reports median, median absolute deviation, percentile, robust z-score, sample size,
and method. A peer anomaly is a review signal, never proof of an error. Production
cells must be segmented by return family, tax year, NAICS group, receipts/assets, and
source version. Client records cannot enter cross-client cells without authorization
and approved de-identification.

## Penalties and defenses

The screen separates the underlying adjustment from penalty consideration. It opens
fact development for accuracy-related, filing, information-return, or restricted
fraud-specialist matters and separately develops reasonable cause, professional
reliance, substantial authority, and adequate disclosure. It never asserts a penalty,
fraud, or a successful defense automatically.

## Pipeline

```text
authorized input → secure intake → canonical facts → reconciliation
→ ratios/issues → peer context → penalty/defense development
→ IDR/workpapers → rights/deadline controls → human review
```

Each case returns its blockers. The portfolio is ordered by review priority, which is
not an IRS audit probability. Sanitized tests cover XML security, missing benchmark
samples, robust outliers, penalty/defense separation, and pipeline gates.

## Remaining production work

This release is an integrated operational foundation, not certification that all IRS
MeF schemas, industries, issues, penalties, forms, and litigation paths are complete.
Completion requires licensed MeF schema packages or approved exports, many more
reviewed issue rules, authoritative peer datasets, independent tax/legal validation,
security testing, and controlled pilot cases.

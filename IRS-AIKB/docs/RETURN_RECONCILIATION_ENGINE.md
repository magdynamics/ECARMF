# Return Reconciliation and Completeness Engine

## Purpose

This layer determines whether a return package is sufficiently complete and internally
consistent to enter final audit-risk scoring. It does not predict IRS selection. It
tests the quality of the evidence supplied to the firm's readiness analysis.

## Controls implemented

- Gross receipts less returns and allowances equals net receipts.
- Net receipts less cost of goods sold equals gross profit.
- Total assets equals liabilities plus equity.
- Prior-year ending balances agree with current-year beginning balances.
- Required schedules are present.
- Book income plus permanent and temporary differences reconciles to tax income.
- Schedule K entity totals reconcile to all K-1 allocations.
- K-1 allocations reconcile to recipient-return reporting when both are supplied.
- Missing values remain `not_assessable`; they are never assumed to be zero.

Each result records rule, category, severity, status, expected value, observed value,
difference, and related return identifiers. Failed or incomplete controls restrict the
downstream score to `preliminary_only`. A package becomes `eligible` only when the
implemented controls are assessable and pass.

The engine also creates conservative schedule expectations from the form family and
explicit facts: business, rental, farm, capital, beneficiary, pass-through, M-3, and
unrelated-business-income indicators. These are review triggers, not automatic legal
conclusions; conditional filing exceptions must be verified by a qualified reviewer.

## Activities and schedules

The canonical model now supports separately identified activities and owner-level
allocations. Curated high-level Schedule C and Schedule F mappings cover 2018–2025.
They are marked as requiring official schedule-template validation before production
use. Schedule E is intentionally not reduced to a few flat lines because its columns,
properties, partnerships, S corporations, estates, and trusts must remain separate
activities.

Schedules K, K-1, L, M-1, M-2, and M-3 are represented through extensible allocation,
balance, capital, and reconciliation facts. Their detailed form/year mapping will be
released in controlled batches rather than guessed from generic PDF field names.

## Command

```text
irs-aikb validate-return-package examples/demo_return_package.json \
  --output return-validation.json
```

## Professional boundary

Passing these controls means only that the supplied normalized package passed the
implemented checks. It does not establish that the return is correct, complete for
all legal purposes, immune from examination, or consistent with undisclosed records.
CPA review remains required, and legal/privilege determinations remain with counsel.

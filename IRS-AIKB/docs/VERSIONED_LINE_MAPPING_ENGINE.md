# Versioned Form-Line Mapping Engine

## Result

The initial reviewed registry maps high-value lines from Forms 1040, 1041, 1065,
1120, and 1120-S for every year from 2018 through 2025. The automated validator
found every registered label in all 40 downloaded official IRS blank forms.

The registry is deliberately narrow and dependable. It currently covers principal
income, receipts, returns and allowances, cost of goods sold, gross profit, total
deductions, taxable or ordinary income, total tax, refund, and amount owed concepts
where applicable. It does not infer unmapped schedules or silently force a value into
the closest category.

## Versioning rule

A mapping key is:

```text
form family + printed revision year + schedule + source line + mapping version
```

This prevents line-number drift from corrupting analytics. For example, Form 1120-S
ordinary business income is line 21 through 2022 and line 22 beginning in 2023;
Form 1065 has a corresponding change. Each source fact retains both its IRS line and
its stable canonical concept.

## Strict mapping behavior

The mapper accepts already-extracted observations. An exact reviewed line must exist,
and any supplied label must agree with the official label. Unknown lines become
`no_reviewed_mapping` exceptions. Label disagreements become `label_conflict`
exceptions. Neither is guessed or discarded.

Mapped values remain `mapped_unvalidated` until arithmetic, schedule completeness,
and cross-return reconciliation checks pass. Blank remains different from zero.

## Commands

Validate the registry against the downloaded official forms:

```text
irs-aikb validate-line-mappings --forms-root sources/annual-income-tax-forms \
  --output mapping-validation.json
```

Map controlled line observations from JSON:

```text
irs-aikb map-lines observations.json --output canonical-facts.json
```

## Next expansion

The next mapping batches are supporting schedules and ownership allocations:

- Schedule C, E, and F activities for Form 1040
- Schedules K, K-1, L, M-1, M-2, and M-3
- Form 1041 distributable net income and beneficiary allocations
- Return payments, credits, and amendments
- Form 990-family functional expense and governance concepts
- International return families and linked information returns

PDF mapping remains one adapter. Authorized e-file XML should be preferred because
official PDF field identifiers are generic and unstable between revisions. Flattened
or scanned files require controlled OCR and human verification.

# Chief Audit Officer Engine v0.3

## Operating standard

The engine converts validated return facts into review priorities; it does not claim
to reproduce the IRS DIF system or calculate the probability of selection. Every
finding is explainable and includes the observed fact, audit technique, evidence,
possible non-adverse explanations, remediation, authority references, and the type of
professional review required.

## Analytical features

The first production feature set calculates:

- Gross margin and year-over-year gross-margin change
- Receipts growth or contraction
- Total and other-deduction concentration
- Taxable margin
- Officer-compensation-to-receipts ratio
- Related-party-receivable-to-assets ratio
- Bank-deposit-to-receipts ratio
- Information-return-to-receipts ratio
- Payroll-form-to-ledger-wages ratio
- Loss frequency across supplied years

Zero denominators produce `not computable`, not an artificial ratio. Missing data is
not converted to zero. Features require source lineage in persistent storage.

## Initial issue rules

The release detects and explains:

- Deposits exceeding reported receipts
- Third-party information exceeding receipts
- Material gross-margin movement
- Large revenue movement
- High other-deduction concentration
- Material owner or related-party receivables
- Payroll-to-ledger inconsistency
- Repeated reported losses

Thresholds are review triggers, not legal conclusions. They must eventually be
calibrated by return family, NAICS group, receipt/asset band, tax year, sample size,
and authoritative benchmark version. A threshold cannot substitute for fact
development or evidence.

## Independent scores

The output keeps separate:

- Public selection indicators
- Potential adjustment exposure
- Related-return exposure
- Documentation readiness
- Internal-control readiness
- Controversy readiness
- Assessment confidence
- Portfolio review priority

The portfolio priority is a workflow measure. It is not an audit probability. A
failed or incomplete reconciliation gate forces `preliminary_only`, even if a numeric
priority can be calculated for triage.

## Command

```text
irs-aikb chief-audit-assess examples/demo_chief_audit_package.json \
  --output chief-audit-assessment.json
```

## Accountability controls

No rule is production-complete until it has an approved source version, applicable
forms and years, calculation tests, evidence and defense paths, materiality logic,
known limitations, CPA review, and—where legal rights, privilege, fraud, or litigation
are involved—attorney or specialist review. Model output never transmits documents,
waives rights, signs a filing, agrees to an adjustment, or asserts fraud.

# Chief Audit Officer Engine Blueprint

## Mission and boundary

This is a CPA decision-support system for reviewing returns, identifying technical and evidentiary weaknesses, improving filing accuracy, preparing for examination, protecting taxpayer rights, and building a defensible controversy record.

It must not claim to prevent an audit, predict an IRS decision with certainty, reproduce confidential DIF models, conceal facts, fabricate support, obstruct an examination, or replace licensed legal advice.

## Independent scores

Never collapse unlike questions into one asserted probability. Preserve:

1. **Public selection indicators** — similarity to publicly disclosed IRS selection considerations.
2. **Adjustment exposure** — plausible substantive tax adjustment risk.
3. **Documentation readiness** — completeness, reliability, reconciliation, and accessibility of evidence; higher is better.
4. **Controversy readiness** — statutes, notices, representation, preservation, and privilege-review controls; higher is better.
5. **Assessment confidence** — completeness of return, books, prior-year, third-party, and benchmark inputs.
6. **Portfolio priority** — internal workflow ranking only, never an audit probability.

Income completeness, tax materiality, and procedural urgency will remain separately displayed as the system matures. Public historical coverage may contextualize a comparison group, but is not a taxpayer-specific probability.

## Architecture

```text
Engagement and access controls
→ Immutable file intake and hashing
→ Entity, owner, tax-year, and form classification
→ Tax-year-specific form parser
→ Canonical line and schedule model
→ Cross-return and multi-year reconciliation
→ Books, bank, payroll, and information-return matching
→ Financial ratios and SOI peer benchmarks
→ Public selection-indicator rules
→ Technical issue and potential-adjustment rules
→ Audit-technique, evidence, and authority mapping
→ Taxpayer-rights and examination state machine
→ Deadline, Appeals, and controversy workflow
→ Multidimensional scoring, uncertainty, and human review
→ Return report, portfolio dashboard, remediation, and defense file
```

Every conclusion must preserve this chain:

```text
Source fact → evidence and provenance → return line/year
→ ratio, mismatch, or legal element → versioned rule
→ selection principle or substantive authority → audit technique
→ required and counter evidence → score and uncertainty
→ remediation → reviewer and disposition
```

## Core engines

### Intake, security, and provenance

- Hash and preserve immutable originals and extraction lineage.
- Apply role-based access, least privilege, encryption, redaction, retention, legal holds, and immutable logs.
- Isolate potential privilege candidates pending counsel review.
- Never interpret an unrequested or unsupplied record as proof it does not exist.

### Entity and canonical return model

- Store legal entity, federal classification, owners, related entities, tax year, return family, amendments, and filing status.
- Preserve exact tax-year form revision and line reference.
- Map changing lines to stable concepts such as receipts, COGS, compensation, assets, debt, taxable income, distributions, and related-party balances.
- Validate arithmetic, schedules, balance sheets, K/K-1 allocations, capital roll-forwards, and M-1/M-3 reconciliations.

### Analytics and public selection intelligence

- Calculate profitability, expense, liquidity, leverage, turnover, book-tax, and tax-specific ratios.
- Compare only within defensible tax-year, form, entity, NAICS, receipt, asset, and accounting-method cells.
- Use robust percentiles; an outlier alone is not a tax error.
- Consider public indicators including information mismatches, LUQ items, related examinations, multi-year inconsistencies, financial-status issues, public campaigns, referrals, and supported preparer patterns.
- Random NRP selection is not predictable and is not taxpayer fault. Confidential DIF is not reproduced.

### Technical issue, technique, and evidence engine

- Detect income, deductions, capitalization, inventory, payroll, basis, distributions, related parties, credits, international, exempt-organization, trust, and industry issues.
- Connect each issue to ATGs, IRM procedures, binding authorities, evidence, interviews, IDRs, computations, counterevidence, and remediation.
- Label IRM, publications, and ATGs as administrative or educational rather than binding law.
- Build element-by-element proof matrices including burden, authenticity, custodian, completeness, contradictions, alternatives, and reviewer conclusions.

### Taxpayer rights and examination procedure

Maintain event checklists for the rights to be informed, quality service, correct tax, challenge and be heard, independent appeal, finality, privacy, confidentiality, representation, and a fair and just system.

The case state machine is:

```text
selected → initial contact → representation validation
→ pre-contact planning → opening conference/interview → IDR cycles
→ books, records, and income testing → proposed findings/penalties
→ closing conference → agreed or unagreed → Appeals/statutory notice
→ assessment, collection, reconsideration, refund route, or litigation
```

Procedures must distinguish correspondence, office, field, employment/excise, partnership/BBA, LB&I, and exempt-organization cases.

### Statutes, privilege, IDRs, Appeals, and litigation

- Store actual issue, mail, receipt, and service dates; filed/due dates; forum; statute; weekends/holidays; tolling; consents; relief; and counsel verification.
- Never infer a deadline from a document date or recommend/execute a statute extension automatically.
- Classify potential attorney-client, work-product, and IRC 7525 candidates without allowing AI to declare privilege conclusively.
- Track every IDR's scope, relevance, ambiguity, burden, custodian, format, privilege, production, and unresolved items.
- Route summonses, criminal exposure, self-incrimination, privilege, forum selection, waivers, pleadings, and legal representations to qualified counsel.
- Build the controversy file continuously: chronology, issues, proof, authorities and contrary authority, computations, witnesses, experts, privilege log, preservation, hazards, remedies, and deadlines.

### Penalty and criminal escalation

- Analyze penalties separately from tax adjustments, including statutory elements, burden, approvals, reasonable cause, reliance, disclosure, and computation.
- Fraud/criminal indicators never form an accusation score. Potential concealment, falsification, destruction, nominees, unexplained offshore/cash patterns, or self-incrimination concerns trigger restricted access and immediate tax-counsel escalation.

## Portfolio workflow

For a group of returns:

1. Inventory and hash every file.
2. Identify taxpayers, forms, years, amendments, owners, and related entities.
3. Validate arithmetic and required schedules.
4. Normalize lines into the canonical model.
5. Reconcile balances and multi-year continuity.
6. Calculate ratios, trends, materiality, and peer comparisons.
7. Match related returns, K-1s, information returns, payroll, books, and banks.
8. Distinguish confirmed inconsistencies from anomalies and missing data.
9. Map findings to sources, techniques, evidence, counterevidence, and remediation.
10. Produce independent scores with confidence and uncertainty warnings.
11. Rank review work by risk, materiality, urgency, confidence, and remediation feasibility.
12. Require CPA/legal sign-off before client-facing conclusions or external action.

## Delivery phases

- **A — Portfolio Triage MVP:** structured JSON/CSV, independent scores, cited rules, remediation, confidence, and ranking. Initial implementation: `irs_aikb.portfolio`.
- **B — Return parser and data model:** taxpayer, ownership, return, canonical line, schedule, amendment, and related-return schema; PDF/XML extraction with validation.
- **C — Analytics:** ratios, trends, SOI percentiles, Data Book context, and books/third-party reconciliation.
- **D — Reviewed rule library:** versioned entity/year/industry rules, materiality, counterevidence, correlation caps, uncertainty, and review states.
- **E — Rights and controversy:** rights events, exam state, deadlines, evidence, privilege isolation, IDR/summons, Appeals, reconsideration, and litigation-ready files.
- **F — Validation:** blind CPA/legal review, out-of-time testing, drift and false-positive analysis, calibration, security/fairness tests, override logs, and release approval.

## Mandatory quality gates

- No hallucinated citations or uncited score drivers.
- Tax-year-specific law and form validation.
- Identical inputs produce reproducible results.
- Correlated findings cannot double-count the same fact without disclosure.
- Missing data increases uncertainty rather than silently becoming zero risk.
- No protected characteristics or unjustified proxies.
- No automatic filing changes, submissions, disclosures, waivers, agreements, accusations, or legal actions.
- Material conclusions require human technical review and recorded override reasons.


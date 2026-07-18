# Organizational Intelligence Control Plane v0.5

Release v0.5 changes the platform's governing model from a collection of audit rules
to a controlled decision-and-learning system. IRS-AIKB remains the first authoritative
domain module; the control plane governs how capabilities, rules, agents, decisions,
events, outcomes, and learning proposals mature and create value.

## Maturity and completeness

Every capability uses ten stages from `concept_defined` through
`continuously_monitored`. Production claims require a professional reviewer, approved
tests, rollback, and monitoring. Missing controls create critical findings rather than
allowing an optimistic completeness percentage.

## Organizational memory

Decision records preserve the contemporaneous fact-snapshot hash, exact source
versions, recommendation, alternatives, professional decision, decision maker, date,
outcome link, and reuse restrictions. Events require unique identifiers and an
append-only, tamper-evident production implementation.

## Controlled learning

A learning candidate must pass evidence review, minimum sample, validation,
professional approval, pilot, and rollback gates. Passing makes it eligible for a
controlled release; it never promotes automatically. Outcomes do not prove that a tax
position was legally correct, and client data cannot become shared learning without
specific authorization and approved de-identification.

## Source-change impact

Changes trace from source to concept, rule, assessment, and client matter. Affected
rules must be suspended or revalidated when material. Prior assessments and active
matters receive controlled impact review rather than silent replacement.

## Value

The value engine measures exposure remediated, penalties mitigated, hours saved,
response time reduced, repeat findings reduced, and client value. Every measure needs
a baseline, attribution method, approval, and protection against double counting.

## Agent governance

Every agent contract defines allowed inputs/actions, prohibited actions, required
sources, confidence policy, escalation policy, and human approver. An agent without a
complete contract cannot be activated.

## Command

```text
irs-aikb evaluate-control-plane examples/demo_control_plane.json \
  --output control-plane-report.json
```

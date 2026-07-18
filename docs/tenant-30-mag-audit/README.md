# Tenant 30 — MAG Audit Integration Package

## Purpose

This directory is the authoritative coding-agent handoff for integrating MAG Audit into ECARMF as `tenant-30-mag-audit` while preserving a standalone deployment boundary.

MAG Audit is the tax-audit domain product. ECARMF is the enterprise operating platform. Taxpayer clients are records inside Tenant 30; they are not ECARMF tenants. A case is scoped to taxpayer, jurisdiction, tax type, return/form, period, and engagement.

## Required outcome

The implementation is accepted only when a persistent synthetic case completes this journey through the ECARMF application:

1. onboard sponsor and client;
2. capture consent and engagement;
3. create taxpayer and 2024 Form 1120-S IRS case;
4. upload, preserve, classify, and extract the return and evidence;
5. reconcile reported facts to supporting records;
6. retrieve IRS knowledge with source citations;
7. calculate separate explainable selection-indicator, exposure, readiness, controversy, and confidence scores;
8. generate and approve a customized audit program;
9. assign staff and governed AI work;
10. prepare authorization, correspondence, document request, and deadlines;
11. move findings through recommendations and approved actions;
12. communicate status to the client and an authorized sponsor;
13. verify resolution and outcome, issue deliverables, report value, and propose controlled learning.

## Files

- `TENANT_PROFILE.json` — canonical Tenant 30 registration values.
- `PACKAGE_CATALOG.json` — ordered package family and dependencies.
- `CODING_AGENT_WORK_ORDER.md` — implementation boundaries, phases, and acceptance controls.
- `DATA_AND_KNOWLEDGE_INTEGRATION.md` — exact source, database, manifest, evidence and runtime connection map.
- `REQUIREMENTS_TRACEABILITY.csv` — accountable requirement baseline and maturity state.

## Governing sources

- `ARCHITECTURE.md` — ECARMF kernel and tenant architecture.
- `IRS-AIKB/docs/MAG_AUDIT_SYSTEM_MASTER_NARRATIVE.md` — complete MAG Audit product narrative.
- `IRS-AIKB/docs/` — domain designs already built.
- `IRS-AIKB/migrations/` and `IRS-AIKB/irs_aikb/` — current standalone-domain prototypes.

## Non-negotiable integration rules

1. One core domain model; no separate ECARMF rewrite of MAG Audit logic.
2. Every ECARMF record and operation is explicitly tenant-scoped.
3. Tenant 30 uses `Regulated` sensitivity and production access-key identity.
4. Client consent never grants sponsor access by implication.
5. Original evidence and provenance are immutable.
6. Machine extraction and AI output require confidence and review state.
7. No score is represented as the IRS DIF score or a guaranteed audit prediction.
8. External submissions, signatures, disclosures, calls, waivers, and professional conclusions require human authorization.
9. Jurisdiction knowledge is isolated; IRS logic cannot substitute for IDOR law.
10. Package versions are immutable and dependency installation is ordered.

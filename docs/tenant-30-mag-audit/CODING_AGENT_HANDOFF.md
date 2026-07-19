# Tenant 30 MAG Audit — Coding Agent Handoff

## Start here

This directory and the referenced repository assets are one delivery package. Do not request the files separately and do not rebuild MAG Audit from an empty project.

Read in this order:

1. `README.md`
2. `TENANT_PROFILE.json`
3. `CODING_AGENT_WORK_ORDER.md`
4. `DATA_AND_KNOWLEDGE_INTEGRATION.md`
5. `COMPONENT_REGISTRY.json`
6. `PACKAGE_CATALOG.json`
7. `IMPLEMENTATION_BACKLOG.csv`
8. `REQUIREMENTS_TRACEABILITY.csv`
9. `BULK_DOCUMENT_INGESTION_BUILD.md`
10. `IRS-AIKB/docs/MAG_AUDIT_SYSTEM_MASTER_NARRATIVE.md`

## Delivery boundary

The coding agent receives the entire ECARMF repository branch, not only this directory. Required assets are distributed intentionally:

```text
docs/tenant-30-mag-audit/       governing handoff, registry and backlog
packages/mag-audit-*.json       17 ECARMF knowledge-package manifests
src/ECARMF.Kernel.Application/  ECARMF application services and MAG Audit ingestion core
src/ECARMF.Kernel.Infrastructure/ ECARMF persistence and provider adapters
src/ECARMF.Kernel.Api/          authenticated tenant-scoped endpoints
tests/ECARMF.Kernel.Tests/      ECARMF and Tenant 30 validation
IRS-AIKB/                       MAG Audit federal knowledge, domain prototypes, corpus and tests
```

## Coding command

Implement the backlog in ascending `sequence` order. Before starting an item:

1. locate its `requirement_ids` in `REQUIREMENTS_TRACEABILITY.csv`;
2. locate its package in `COMPONENT_REGISTRY.json`;
3. inspect all `existing_assets` before adding code;
4. preserve the standalone/ECARMF adapter boundary;
5. add tests for tenant, case, authorization, provenance and professional-review gates;
6. update maturity and evidence paths in both registries;
7. demonstrate the acceptance criterion with persistent data.

## Completion language

Use these meanings consistently:

- `specified` — requirement and boundary documented;
- `manifested` — ECARMF package metadata exists;
- `core-coded` — domain/application logic exists and is tested in isolation;
- `adapter-coded` — infrastructure/provider implementation exists;
- `api-connected` — authenticated persistent endpoint exists;
- `ui-connected` — interface reads/writes the real service;
- `integrated` — cross-component workflow passes;
- `professionally-validated` — qualified reviewer approved tax/legal behavior and sources;
- `production-ready` — security, operations, performance, recovery and deployment gates pass.

Never describe `specified`, `manifested`, a visual mockup, or machine-extracted knowledge as a completed production capability.

## Immediate implementation target

Complete the bulk-document vertical slice before expanding isolated screens:

```text
Desktop folder inventory
  -> resumable upload
  -> persistent batch/item records
  -> encrypted quarantine
  -> malware scan
  -> OCR/text extraction
  -> tax document classification
  -> taxpayer/case/year assignment
  -> human exception review
  -> accepted evidence vault
  -> import reconciliation report
  -> case evidence/search visibility
```

The current core is in `src/ECARMF.Kernel.Application/MagAudit/Documents/BulkDocumentIngestion.cs`. Its provider contracts are the starting point, not a reason to create competing interfaces.

## Acceptance evidence

The coding agent must return:

- changed files and package versions;
- migrations and rollback instructions;
- tests added and full results;
- screenshots or recorded steps of the connected workflow;
- a completed synthetic import manifest and reconciliation report;
- security and authorization tests;
- source/provenance examples;
- updated traceability and component maturity;
- known limitations and the next backlog item.


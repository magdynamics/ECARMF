# ECT-001 — Evidence Model Foundation

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the mandatory Evidence Model for ECARMF.

Evidence is the governed information object that supports an assertion, calculation, asset state, risk score, threshold event, decision, forecast, AI recommendation, or report.

No analytical conclusion shall exist without evidence.

## 2. Canonical Evidence Structure

Every Evidence object shall include:

```text
Evidence
    ├── Evidence ID
    ├── Evidence Type
    ├── Source
    ├── Provenance
    ├── Owner
    ├── Custodian
    ├── Validity Period
    ├── Confidence Score
    ├── Verification Status
    ├── Related Entity
    ├── Related Requirement
    ├── Related Decision
    ├── Hash / Integrity Signature
    ├── Access Classification
    └── Audit History
```

## Requirements

### ECT-001-0001 — Evidence Required

Every analytical conclusion shall reference one or more Evidence objects.

**Priority:** Mandatory  
**Developer Guidance:** Create Evidence as a canonical entity within the Governance or Foundation domain and expose it to every business domain by contract.  
**AI Agent Guidance:** Never generate a risk score, decision, recommendation, or report without evidence references.  
**Verification:** Attempting to create an unsupported analytical conclusion shall fail validation.

### ECT-001-0002 — Evidence Identity

Every Evidence object shall possess a globally unique immutable identifier.

### ECT-001-0003 — Evidence Provenance

Every Evidence object shall record provenance metadata, including source system, collection method, collection date, collector, and transformation history.

### ECT-001-0004 — Evidence Integrity

Every Evidence object shall support integrity verification through hash, checksum, digital signature, or equivalent control.

### ECT-001-0005 — Evidence Confidence

Every Evidence object shall include a confidence score or confidence classification.

### ECT-001-0006 — Evidence Validity

Every Evidence object shall define a validity period where applicable.

### ECT-001-0007 — Evidence Classification

Every Evidence object shall define confidentiality, retention, and access classification.

### ECT-001-0008 — Evidence API Contract

Every Evidence implementation shall expose:

```text
POST   /api/v1/evidence
GET    /api/v1/evidence/{id}
PATCH  /api/v1/evidence/{id}
GET    /api/v1/evidence/{id}/history
GET    /api/v1/evidence/{id}/entities
GET    /api/v1/evidence/{id}/decisions
GET    /api/v1/evidence/{id}/integrity
POST   /api/v1/evidence/{id}/verify
```

### ECT-001-0009 — Evidence Events

Every Evidence implementation shall publish:

- EvidenceCreated
- EvidenceVerified
- EvidenceRejected
- EvidenceExpired
- EvidenceLinked
- EvidenceIntegrityFailed

## Acceptance Criteria

Evidence implementation is complete when:

- Evidence entity exists.
- Evidence persistence exists.
- Evidence API exists.
- Evidence verification workflow exists.
- Evidence relationship mappings exist.
- Evidence validation tests pass.
- Evidence audit trail is immutable.

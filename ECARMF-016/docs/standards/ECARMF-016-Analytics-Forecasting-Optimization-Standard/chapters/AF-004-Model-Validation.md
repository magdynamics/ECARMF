# AF-004 — Model Validation

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the construction requirements for **Model Validation** within **ECARMF-016 — Analytics Forecasting and Optimization Standard**.

The objective is to give developers and AI coding agents a direct engineering contract describing what must be built, how it must behave, what interfaces it must expose, how it must be validated, and how it must remain traceable to ECARMF requirements.

## 2. Mandatory Construction Model

Every implementation of this work package shall include:

```text
ModelValidation
    ├── Domain Model
    ├── Application Service
    ├── API Contract
    ├── Persistence Contract
    ├── Event Contract
    ├── Knowledge Graph Mapping
    ├── Digital Twin Mapping where applicable
    ├── AI Context Contract
    ├── Validation Rules
    ├── Tests
    └── Documentation
```

## Requirements

### AF-004-0001 — Model Validation Mandatory

Every ECARMF-compliant implementation requiring Analytics Forecasting and Optimization Standard capabilities shall implement Model Validation.

**Priority:** Mandatory  
**Developer Guidance:** Create a bounded service or module aligned to ECARMF-003 architecture.  
**AI Agent Guidance:** Generate domain entities first, followed by services, APIs, persistence, graph mapping, tests, and documentation.  
**Verification:** Component shall fail conformance testing if this capability is missing.

### AF-004-0002 — Canonical Domain Model

The implementation shall define a canonical domain model for Model Validation.

**Developer Guidance:** Use ECARMF-002 Universal Base Entity inheritance and ECARMF-005 Evidence, Context, and Traceability requirements.  
**Verification:** Domain model shall include identity, lifecycle, ownership, evidence, context, audit, and traceability metadata.

### AF-004-0003 — API Contract

The implementation shall expose a versioned REST API for Model Validation.

Minimum endpoints:

```text
POST   /api/v1/model-validation
GET    /api/v1/model-validation/{id}
PATCH  /api/v1/model-validation/{id}
GET    /api/v1/model-validation/{id}/history
GET    /api/v1/model-validation/{id}/traceability
```

### AF-004-0004 — Event Contract

The implementation shall publish immutable events for create, update, validation, approval, execution, failure, and archival activities.

Required events:

- ModelValidationCreated
- ModelValidationUpdated
- ModelValidationValidated
- ModelValidationFailed
- ModelValidationArchived

### AF-004-0005 — Validation Contract

The implementation shall validate:

- Required attributes
- Lifecycle state
- Evidence references
- Context package
- Knowledge Graph mapping
- Security policy
- Requirement traceability

### AF-004-0006 — AI Construction Contract

AI coding agents shall preserve ECARMF requirement identifiers and shall not generate implementation artifacts that violate ECARMF-001 through ECARMF-005.

## Acceptance Criteria

This work package is complete when:

- Domain model exists.
- Service exists.
- API exists.
- Persistence exists.
- Events exist.
- Validation rules exist.
- Tests exist.
- Traceability exists.
- AI construction guide is followed.

# TH-002 — Threshold Types and Rules

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the construction requirements for **Threshold Types and Rules** within **ECARMF-009 — Threshold Intelligence Standard**.

The objective is to give developers and AI coding agents a direct engineering contract describing what must be built, how it must behave, what interfaces it must expose, how it must be validated, and how it must remain traceable to ECARMF requirements.

## 2. Mandatory Construction Model

Every implementation of this work package shall include:

```text
ThresholdTypesandRules
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

### TH-002-0001 — Threshold Types and Rules Mandatory

Every ECARMF-compliant implementation requiring Threshold Intelligence Standard capabilities shall implement Threshold Types and Rules.

**Priority:** Mandatory  
**Developer Guidance:** Create a bounded service or module aligned to ECARMF-003 architecture.  
**AI Agent Guidance:** Generate domain entities first, followed by services, APIs, persistence, graph mapping, tests, and documentation.  
**Verification:** Component shall fail conformance testing if this capability is missing.

### TH-002-0002 — Canonical Domain Model

The implementation shall define a canonical domain model for Threshold Types and Rules.

**Developer Guidance:** Use ECARMF-002 Universal Base Entity inheritance and ECARMF-005 Evidence, Context, and Traceability requirements.  
**Verification:** Domain model shall include identity, lifecycle, ownership, evidence, context, audit, and traceability metadata.

### TH-002-0003 — API Contract

The implementation shall expose a versioned REST API for Threshold Types and Rules.

Minimum endpoints:

```text
POST   /api/v1/threshold-types-and-rules
GET    /api/v1/threshold-types-and-rules/{id}
PATCH  /api/v1/threshold-types-and-rules/{id}
GET    /api/v1/threshold-types-and-rules/{id}/history
GET    /api/v1/threshold-types-and-rules/{id}/traceability
```

### TH-002-0004 — Event Contract

The implementation shall publish immutable events for create, update, validation, approval, execution, failure, and archival activities.

Required events:

- ThresholdTypesandRulesCreated
- ThresholdTypesandRulesUpdated
- ThresholdTypesandRulesValidated
- ThresholdTypesandRulesFailed
- ThresholdTypesandRulesArchived

### TH-002-0005 — Validation Contract

The implementation shall validate:

- Required attributes
- Lifecycle state
- Evidence references
- Context package
- Knowledge Graph mapping
- Security policy
- Requirement traceability

### TH-002-0006 — AI Construction Contract

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

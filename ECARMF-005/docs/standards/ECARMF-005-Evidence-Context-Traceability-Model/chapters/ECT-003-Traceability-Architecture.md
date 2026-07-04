# ECT-003 — Traceability Architecture

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the mandatory Traceability Architecture for ECARMF.

Traceability is the ability to connect any output back to its origin, including requirements, entities, evidence, context, services, APIs, data, events, calculations, AI prompts, AI outputs, tests, and approvals.

## 2. Traceability Graph

Traceability shall be implemented as a graph of relationships.

```text
Requirement
    ↓
Entity
    ↓
Service
    ↓
API
    ↓
Database Object
    ↓
Event
    ↓
Evidence
    ↓
Context
    ↓
Calculation
    ↓
Decision
    ↓
Test
    ↓
Release
```

## Requirements

### ECT-003-0001 — Traceability Mandatory

Every ECARMF output shall be traceable to originating requirements, source entities, supporting evidence, and execution context.

**Priority:** Mandatory  
**Developer Guidance:** Implement TraceabilityService as a platform service.  
**AI Agent Guidance:** Add requirement IDs and source references to generated artifacts.  
**Verification:** Outputs without traceability metadata shall fail certification.

### ECT-003-0002 — Requirement Traceability

Every implementation artifact shall reference applicable ECARMF requirement IDs.

### ECT-003-0003 — Source Data Traceability

Every calculated value shall reference its source data and transformation path.

### ECT-003-0004 — API Traceability

Every API response shall include request ID, correlation ID, and requirement references where applicable.

### ECT-003-0005 — AI Traceability

Every AI output shall reference prompt, context, model, evidence, confidence, and knowledge graph path.

### ECT-003-0006 — Decision Traceability

Every decision shall reference alternatives, evidence, thresholds, context, models, approvers, and outcomes.

### ECT-003-0007 — Traceability API Contract

Traceability implementation shall expose:

```text
GET /api/v1/traceability/{entityId}
GET /api/v1/traceability/requirements/{requirementId}
GET /api/v1/traceability/decisions/{decisionId}
GET /api/v1/traceability/evidence/{evidenceId}
GET /api/v1/traceability/correlation/{correlationId}
```

### ECT-003-0008 — Traceability Events

Traceability implementation shall publish:

- TraceabilityLinkCreated
- TraceabilityLinkValidated
- TraceabilityGapDetected
- TraceabilityGapResolved

## Acceptance Criteria

Traceability is complete when:

- Traceability graph exists.
- Requirement links exist.
- Evidence links exist.
- Context links exist.
- AI traceability exists.
- API traceability exists.
- Traceability tests pass.

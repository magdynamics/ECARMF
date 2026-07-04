# ECT-002 — Context Model Foundation

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the mandatory Context Model for ECARMF.

Context is the reproducible set of facts, assumptions, state, relationships, evidence, economic conditions, risk conditions, capital conditions, and time references under which analysis, decisioning, forecasting, or AI reasoning occurs.

A conclusion without context is invalid.

## 2. Canonical Context Package

Every Context Package shall include:

```text
ContextPackage
    ├── Context ID
    ├── Context Type
    ├── Time Reference
    ├── Asset References
    ├── EDC References
    ├── Portfolio References
    ├── Economic Conditions
    ├── Capital Conditions
    ├── Risk Conditions
    ├── Threshold State
    ├── Evidence References
    ├── Assumptions
    ├── Model Version
    ├── User / Actor
    ├── AI Agent Reference
    └── Reproducibility Hash
```

## Requirements

### ECT-002-0001 — Context Required

Every analytical process shall execute inside a defined Context Package.

**Priority:** Mandatory  
**Developer Guidance:** Implement ContextPackage as a governed entity with immutable snapshots.  
**AI Agent Guidance:** Build the Context Package before performing reasoning, forecasting, scoring, or recommendation.  
**Verification:** Analytical calls without Context Package shall fail.

### ECT-002-0002 — Context Reproducibility

Every Context Package shall be reproducible for audit and validation.

### ECT-002-0003 — Context Immutability

A Context Package used for approved decisions shall become immutable.

### ECT-002-0004 — Context Time Reference

Every Context Package shall define the time reference used for evaluation.

### ECT-002-0005 — Context Evidence Linkage

Every Context Package shall reference supporting Evidence objects.

### ECT-002-0006 — AI Context

Every AI reasoning request shall receive an AI Context Package derived from the canonical Context Package.

### ECT-002-0007 — Context API Contract

Every Context implementation shall expose:

```text
POST   /api/v1/contexts
GET    /api/v1/contexts/{id}
GET    /api/v1/contexts/{id}/evidence
GET    /api/v1/contexts/{id}/knowledge-paths
POST   /api/v1/contexts/{id}/reproduce
POST   /api/v1/contexts/{id}/lock
```

### ECT-002-0008 — Context Events

Every Context implementation shall publish:

- ContextCreated
- ContextLocked
- ContextReproduced
- ContextValidationFailed
- AIContextGenerated

## Acceptance Criteria

Context implementation is complete when:

- Context Package entity exists.
- Context builder service exists.
- Context lock mechanism exists.
- Context reproducibility tests pass.
- AI Context Package generation works.
- Context evidence linkage is validated.

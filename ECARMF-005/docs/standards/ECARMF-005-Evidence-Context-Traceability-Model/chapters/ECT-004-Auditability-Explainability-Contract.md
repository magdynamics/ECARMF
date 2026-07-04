# ECT-004 — Auditability & Explainability Contract

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the mandatory auditability and explainability requirements for ECARMF.

Auditability ensures that platform behavior can be inspected, verified, and reconstructed.

Explainability ensures that recommendations, decisions, scores, warnings, forecasts, and AI outputs can be understood by authorized humans and systems.

## Requirements

### ECT-004-0001 — Audit Trail Mandatory

Every state-changing operation shall create an immutable audit record.

**Priority:** Mandatory  
**Developer Guidance:** Implement AuditService and enforce audit creation through middleware, domain events, and persistence interceptors.  
**AI Agent Guidance:** Generate audit logging for every command handler and lifecycle transition.  
**Verification:** State changes without audit record shall fail validation.

### ECT-004-0002 — Audit Record Structure

Every Audit Record shall include:

- Audit ID
- Actor
- Action
- Entity
- Previous state
- New state
- Timestamp
- Correlation ID
- Requirement ID
- Evidence reference
- Context reference
- Result

### ECT-004-0003 — Explainability Required

Every recommendation, decision, risk score, threshold warning, or AI output shall provide an explanation.

### ECT-004-0004 — Explanation Structure

Every explanation shall include:

- Conclusion
- Inputs used
- Evidence used
- Context used
- Rules applied
- Models used
- Confidence
- Alternatives considered
- Knowledge Graph path
- Requirement references

### ECT-004-0005 — AI Explainability

AI-generated explanations shall be distinguished from deterministic rule-based explanations.

### ECT-004-0006 — Audit API Contract

Audit implementation shall expose:

```text
GET /api/v1/audit/entities/{entityId}
GET /api/v1/audit/correlation/{correlationId}
GET /api/v1/audit/users/{userId}
GET /api/v1/audit/requirements/{requirementId}
```

### ECT-004-0007 — Explainability API Contract

Explainability implementation shall expose:

```text
GET /api/v1/explanations/{id}
GET /api/v1/decisions/{id}/explanation
GET /api/v1/risks/{id}/explanation
GET /api/v1/threshold-events/{id}/explanation
GET /api/v1/ai-outputs/{id}/explanation
```

### ECT-004-0008 — Immutable Audit History

Audit records shall never be modified or deleted.

## Acceptance Criteria

Auditability and explainability are complete when:

- Audit service exists.
- Explanation service exists.
- Audit records are immutable.
- Decision explanations are generated.
- AI explanations are generated.
- Knowledge Graph paths are included.
- Audit and explanation APIs are tested.

# ECARMF-005 AI Construction Guide

## AI Agent Objective

Generate ECARMF-compliant evidence, context, traceability, auditability, and explainability implementation artifacts.

## Mandatory Generation Sequence

```text
Read ECARMF-001
Read ECARMF-002
Read ECARMF-003
Read ECARMF-005
Generate Evidence Model
Generate Context Model
Generate Traceability Model
Generate Audit Model
Generate Explanation Model
Generate Persistence
Generate Services
Generate APIs
Generate Knowledge Graph Mappings
Generate Validation Rules
Generate Tests
Generate Documentation
```

## AI Constraints

AI shall not:

- Produce conclusions without evidence.
- Produce recommendations without context.
- Generate untraceable code.
- Remove audit records.
- Override requirement traceability.
- Generate explanations without citing inputs.
- Hide AI-generated confidence levels.

## Required Code Comment Format

```csharp
// ECARMF Standard: ECARMF-005
// Implements: ECT-001-0001, ECT-003-0001
```

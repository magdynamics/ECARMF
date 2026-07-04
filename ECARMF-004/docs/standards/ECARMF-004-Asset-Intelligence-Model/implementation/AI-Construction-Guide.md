# ECARMF-004 AI Construction Guide

## AI Agent Objective

Generate ECARMF-compliant Asset Intelligence implementation artifacts while preserving ECARMF requirement traceability.

## Mandatory Generation Sequence

```text
Read ECARMF-001
Read ECARMF-002
Read ECARMF-003
Read ECARMF-004
Generate EDA Domain Model
Generate EDC Domain Model
Generate Digital Twin Model
Generate Events
Generate Persistence
Generate Services
Generate APIs
Generate Knowledge Graph Mapping
Generate Validation Rules
Generate Tests
Generate Documentation
```

## AI Constraints

AI shall not:

- Create asset entities outside the ECARMF ontology.
- Bypass EDA/EDC structure.
- Generate APIs before domain services.
- Generate persistence before domain models.
- Remove requirement IDs.
- Modify approved lifecycle states.
- Remove evidence or audit requirements.

## Required Code Comments

Every generated class shall include:

```csharp
// ECARMF Standard: ECARMF-004
// Implements: AI-002-0001, AI-003-0001
```

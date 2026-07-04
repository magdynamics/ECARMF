# ECARMF-006 AI Construction Guide

## Objective

Generate ECARMF-compliant implementation artifacts for Knowledge Graph Standard.

## Mandatory AI Generation Sequence

```text
Read Standards
    ↓
Load Requirements
    ↓
Generate Domain Models
    ↓
Generate Services
    ↓
Generate APIs
    ↓
Generate Persistence
    ↓
Generate Events
    ↓
Generate Graph Mappings
    ↓
Generate Validation
    ↓
Generate Tests
    ↓
Generate Documentation
```

## AI Constraints

AI shall not:

- Remove requirement traceability.
- Invent entities outside ECARMF-002.
- Bypass ECARMF-003 architecture.
- Produce unsupported conclusions without ECARMF-005 evidence and context.
- Generate production code without tests.

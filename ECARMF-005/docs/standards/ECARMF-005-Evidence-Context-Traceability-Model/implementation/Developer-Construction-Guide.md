# ECARMF-005 Developer Construction Guide

## Required Domains

```text
ECARMF.Governance.Domain
ECARMF.Governance.Application
ECARMF.Foundation.Audit
ECARMF.Foundation.Traceability
ECARMF.Foundation.Context
ECARMF.Foundation.Evidence
```

## Required Core Classes

```text
Evidence
EvidenceSource
EvidenceVerification
ContextPackage
AIContextPackage
TraceabilityLink
AuditRecord
Explanation
ExplanationPath
RequirementReference
CorrelationRecord
```

## Required Services

```text
EvidenceService
EvidenceVerificationService
ContextBuilderService
AIContextBuilderService
TraceabilityService
AuditService
ExplanationService
IntegrityVerificationService
```

## Development Order

1. Implement Evidence entity.
2. Implement ContextPackage entity.
3. Implement TraceabilityLink entity.
4. Implement AuditRecord entity.
5. Implement Explanation entity.
6. Implement persistence.
7. Implement services.
8. Implement APIs.
9. Implement Knowledge Graph mappings.
10. Implement audit middleware.
11. Implement validation rules.
12. Implement tests.

# ECARMF-004 Developer Construction Guide

## Required Domain Projects

```text
ECARMF.Asset.Domain
ECARMF.Asset.Application
ECARMF.Asset.Infrastructure
ECARMF.Asset.Persistence
ECARMF.Asset.API
ECARMF.Asset.Contracts
ECARMF.Asset.Tests
ECARMF.DigitalTwin.Domain
ECARMF.DigitalTwin.Application
ECARMF.DigitalTwin.API
```

## Required Core Classes

```text
EconomicDigitalAsset
EconomicDigitalCell
AssetDigitalTwin
AssetState
AssetEvidence
AssetRelationship
AssetEvent
AssetMetric
AssetPerformanceProfile
AssetRiskProfile
AssetCapitalProfile
```

## Required Services

```text
AssetService
AssetIntelligenceService
EconomicDigitalCellService
AssetDecompositionService
AssetDigitalTwinService
AssetEvidenceService
AssetKnowledgeGraphMapper
AssetAIContextBuilder
```

## Development Order

1. Implement EDA aggregate.
2. Implement EDC entity.
3. Implement lifecycle state model.
4. Implement evidence model.
5. Implement events.
6. Implement persistence.
7. Implement services.
8. Implement APIs.
9. Implement Knowledge Graph mapping.
10. Implement Digital Twin synchronization.
11. Implement tests.
12. Generate OpenAPI documentation.

# AI-002 — Economic Digital Asset Construction

## Classification

Normative engineering construction specification.

## 1. Purpose

This work package defines the construction requirements for the Economic Digital Asset (EDA).

An EDA is a managed economic asset composed of one or more Economic Digital Cells and governed by ECARMF lifecycle, ontology, knowledge graph, digital twin, and intelligence rules.

## 2. Mandatory EDA Structure

Every EDA shall include the following engineering components:

```text
EconomicDigitalAsset
    ├── Identity
    ├── Ownership
    ├── Lifecycle
    ├── Economic Profile
    ├── Capital Profile
    ├── Risk Profile
    ├── Performance Profile
    ├── EDC Collection
    ├── Evidence Collection
    ├── Relationship Collection
    ├── Digital Twin Reference
    ├── Knowledge Graph Node
    └── Event History
```

## Requirements

### AI-002-0001 — EDA Aggregate Root

Every Economic Digital Asset shall be implemented as an Aggregate Root.

**Developer Guidance:** Create `EconomicDigitalAsset` inside the Asset Domain.  
**.NET Guidance:** Implement as `ECARMF.Asset.Domain.Entities.EconomicDigitalAsset`.  
**Database Guidance:** Create `Asset_Current`, `Asset_History`, `Asset_Event`, `Asset_Evidence`, and `Asset_Relationship`.

### AI-002-0002 — EDA Identity

Every EDA shall inherit Universal Base Entity identity attributes from ECARMF-002.

### AI-002-0003 — EDA Ownership

Every EDA shall identify:

- Legal owner
- Operating entity
- Responsible party
- Governance owner
- Data custodian

### AI-002-0004 — EDA Lifecycle

Every EDA shall implement the Universal Lifecycle:

```text
Draft → Proposed → Validated → Approved → Active → Operational → Modified → Suspended → Retired → Archived
```

### AI-002-0005 — EDA EDC Composition

Every Operational EDA shall contain one or more EDCs.

### AI-002-0006 — EDA Evidence

Every EDA shall maintain evidence references supporting identity, ownership, valuation, condition, and performance.

### AI-002-0007 — EDA Knowledge Graph Node

Every EDA shall have exactly one canonical Knowledge Graph node.

### AI-002-0008 — EDA Digital Twin

Every Active EDA shall instantiate an Asset Digital Twin.

### AI-002-0009 — EDA API Contract

Every EDA implementation shall expose:

```text
POST   /api/v1/assets
GET    /api/v1/assets/{id}
PATCH  /api/v1/assets/{id}
GET    /api/v1/assets/{id}/history
GET    /api/v1/assets/{id}/edcs
GET    /api/v1/assets/{id}/digital-twin
GET    /api/v1/assets/{id}/knowledge
GET    /api/v1/assets/{id}/performance
GET    /api/v1/assets/{id}/risk
GET    /api/v1/assets/{id}/capital
```

### AI-002-0010 — EDA Events

Every EDA implementation shall publish:

- AssetCreated
- AssetValidated
- AssetApproved
- AssetActivated
- AssetModified
- AssetSuspended
- AssetRetired
- AssetArchived
- AssetDigitalTwinSynchronized
- AssetKnowledgeGraphUpdated

## AI Coding Agent Contract

An AI coding agent implementing EDA shall generate in this order:

1. Domain entity
2. Value objects
3. Lifecycle state model
4. Validation rules
5. Events
6. Repository interface
7. Persistence model
8. Application service
9. API controller
10. Knowledge graph mapping
11. Digital twin mapping
12. Unit tests
13. Integration tests
14. OpenAPI documentation

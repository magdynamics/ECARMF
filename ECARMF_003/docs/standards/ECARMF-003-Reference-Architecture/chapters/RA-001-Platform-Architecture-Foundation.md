# RA-001 — Platform Architecture Foundation

## 1. Purpose

This chapter defines the mandatory architectural foundation of every ECARMF Platform implementation. The architecture described herein is the canonical implementation model for all ECARMF-compliant systems.

## Requirements

**RA-001-0001** — Every ECARMF Platform shall implement the canonical platform architecture defined in this standard.

**RA-001-0002** — Business behavior shall be independent of technology selection.

**RA-001-0003** — The architecture shall support horizontal scalability without modification of domain behavior.

## 2. Platform Decomposition

The ECARMF Platform shall be composed of independent bounded contexts. Each bounded context represents one business capability. Bounded contexts shall communicate only through published contracts. Direct database access between bounded contexts is prohibited.

Mandatory domains:

```text
Foundation
Identity
Ontology
Knowledge Graph
Digital Twin
Asset
Capital
Economics
Risk
Threshold
Decision
Monitoring
Governance
Workflow
Notification
Integration
AI
Reporting
Administration
```

**RA-001-0100** — Every Platform Domain shall own its business rules.

**RA-001-0101** — Every Platform Domain shall own its persistence model.

**RA-001-0102** — Every Platform Domain shall publish versioned APIs.

**RA-001-0103** — Platform Domains shall never expose internal implementation objects.

## 3. Canonical Domain Structure

Every Platform Domain shall follow this directory structure:

```text
DomainName/
  README.md
  Domain/
  Application/
  Infrastructure/
  Persistence/
  API/
  Contracts/
  Events/
  Queries/
  Commands/
  Validators/
  Knowledge/
  DigitalTwin/
  Tests/
  Documentation/
```

**RA-001-0200** — Every Domain shall implement the canonical folder structure.

**RA-001-0201** — Generated source code shall preserve directory structure.

**RA-001-0202** — AI-generated code shall preserve directory structure.

## 4. Internal Architecture

Each Platform Domain shall implement the following internal layers:

```text
API
  ↓
Application
  ↓
Domain
  ↓
Knowledge
  ↓
Infrastructure
  ↓
Persistence
```

**RA-001-0300** — Business Rules shall exist only within the Domain Layer.

**RA-001-0301** — Application Services shall never contain business rules.

**RA-001-0302** — Infrastructure Services shall never contain business rules.

**RA-001-0303** — Repositories shall never perform business validation.

## 5. Dependency Rules

Dependencies shall always flow inward. Reverse dependencies and circular dependencies are prohibited.

**RA-001-0400** — The Domain Layer shall not reference Infrastructure assemblies.

**RA-001-0401** — Infrastructure shall implement Domain Interfaces.

**RA-001-0402** — Dependency Injection is mandatory.

## 6. Canonical Project Layout

Example for Asset Domain:

```text
ECARMF.Asset.Domain
ECARMF.Asset.Application
ECARMF.Asset.Infrastructure
ECARMF.Asset.Persistence
ECARMF.Asset.API
ECARMF.Asset.Contracts
ECARMF.Asset.Tests
```

**RA-001-0500** — One assembly per architectural responsibility.

**RA-001-0501** — Assemblies shall remain independently deployable.

**RA-001-0502** — Shared code shall reside only inside Foundation.

## 7. Foundation Domain

Foundation shall contain base classes, universal attributes, requirement registry, domain events, result patterns, exception framework, validation framework, authorization framework, audit framework, versioning framework, configuration framework, time provider, correlation IDs, logging contracts, telemetry contracts, caching contracts, repository interfaces, unit of work, event publisher, and dependency injection extensions.

**RA-001-0600** — Foundation shall remain business neutral.

**RA-001-0601** — Foundation shall not reference any business domain.

**RA-001-0602** — All Platform Domains shall reference Foundation.

## 8. Developer Construction Rules

Before writing code, developers shall identify applicable ECARMF standards, requirement IDs, entities, relationships, events, services, APIs, database objects, validation rules, test requirements, and acceptance criteria.

**RA-001-0700** — Every source file shall reference implemented Requirement IDs.

**RA-001-0701** — Every Pull Request shall list implemented Requirement IDs.

**RA-001-0702** — Every generated class shall preserve traceability.

## 9. AI Construction Contract

AI coding agents shall follow this sequence: read standards, build ontology, generate entities, generate database, generate repositories, generate domain services, generate APIs, generate knowledge graph, generate digital twins, generate tests, generate documentation.

**RA-001-0800** — AI shall never generate APIs before Domain Services.

**RA-001-0801** — AI shall never generate Persistence before Entities.

**RA-001-0802** — AI shall preserve Requirement Traceability.

**RA-001-0803** — AI shall generate automated tests simultaneously with production code.

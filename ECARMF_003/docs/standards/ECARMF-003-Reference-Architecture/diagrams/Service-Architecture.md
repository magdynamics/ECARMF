# Service Architecture Diagram

```mermaid
flowchart TD
  Controller --> ApplicationService
  ApplicationService --> DomainService
  DomainService --> RepositoryInterface
  RepositoryInterface --> InfrastructureImplementation
  DomainService --> DomainEvents
  DomainService --> Validators
  ApplicationService --> Telemetry
```

# API Architecture Diagram

```mermaid
flowchart TD
  Client --> APIController
  APIController --> DTOValidation
  DTOValidation --> ApplicationService
  ApplicationService --> DomainService
  DomainService --> Repository
  DomainService --> EventPublisher
  APIController --> ResponseContract
  APIController --> OpenAPI
  APIController --> Telemetry
```

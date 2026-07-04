# RA-004 — Canonical API Architecture Specification

## 1. Purpose

This specification defines the mandatory API architecture governing all communication between internal platform components, external systems, AI agents, Digital Twins, Knowledge Graph services, applications, enterprise integrations, and third-party systems.

**RA-004-0001** — Every externally accessible capability shall be exposed through a Canonical API.

**RA-004-0002** — All APIs shall conform to the ECARMF API Standard.

**RA-004-0003** — Every API shall be independently versioned.

**RA-004-0004** — APIs shall remain technology independent.

## 2. API Classification

The ECARMF Platform defines six API categories: Command API, Query API, Event API, Administration API, Integration API, and AI API.

**RA-004-0100** — Every endpoint shall belong to one API classification.

**RA-004-0101** — Endpoints shall never mix Command and Query responsibilities.

## 3. API Construction Rules

Every API shall implement Controller → DTO → Application Service → Domain Service → Repository Interface → Persistence. Controllers shall contain no business logic.

**RA-004-0200** — Business rules inside Controllers are prohibited.

**RA-004-0201** — Controllers shall never access repositories.

**RA-004-0202** — Controllers shall never access databases.

## 4. URI Standards

Canonical URI format:

```text
/api/v{version}/{domain}/{resource}
```

Examples:

```text
/api/v1/assets
/api/v1/assets/{id}
/api/v1/assets/search
/api/v1/risks
/api/v1/decisions
/api/v1/capital
/api/v1/thresholds
```

**RA-004-0300** — URIs shall use plural nouns.

**RA-004-0301** — Verbs are prohibited within URI paths.

## 5. HTTP Methods

Supported methods are GET, POST, PUT, PATCH, logical DELETE, OPTIONS, and HEAD. Physical DELETE is prohibited.

**RA-004-0400** — DELETE shall perform logical deletion.

**RA-004-0401** — GET requests shall remain side-effect free.

**RA-004-0402** — PATCH shall support partial updates only.

## 6. Request Contract

Every request shall include authentication token, correlation ID, request ID, API version, content type, accept header, localization, and optional tenant identifier.

**RA-004-0500** — Correlation ID is mandatory.

**RA-004-0501** — Every request shall be traceable.

**RA-004-0502** — Every request shall be logged.

## 7. Response Contract

Every response returns status, data, metadata, warnings, errors, and links.

```json
{
  "status": "Success",
  "data": {},
  "metadata": {},
  "warnings": [],
  "errors": [],
  "links": []
}
```

**RA-004-0600** — Response contract is standardized.

**RA-004-0601** — Internal exception messages are prohibited.

**RA-004-0602** — Every response shall include Request ID.

## 8. Error Contract

Standard error object:

```json
{
  "code": "",
  "message": "",
  "severity": "",
  "requirement": "",
  "correlationId": "",
  "details": []
}
```

**RA-004-0700** — Errors shall reference originating Requirement IDs.

**RA-004-0701** — Stack traces are prohibited.

**RA-004-0702** — Business errors shall be distinguished from technical errors.

## 9. API Versioning

Supported versions use v1, v2, v3 format. Version retirement is governed by Governance Domain.

**RA-004-0800** — Breaking changes require a new version.

**RA-004-0801** — Minor enhancements shall remain backward compatible.

**RA-004-0802** — Deprecated endpoints shall remain supported according to lifecycle policy.

## 10. Pagination

Large collections require pagination using page and pageSize.

**RA-004-0900** — Collection endpoints shall support pagination.

**RA-004-0901** — Maximum page size is configurable.

## 11. Filtering

Supported filters include equals, not equals, contains, starts with, ends with, between, in, greater than, and less than.

**RA-004-1000** — Filtering is standardized.

**RA-004-1001** — Filter validation is mandatory.

## 12. Sorting

Sorting supports ascending, descending, multiple columns, and stable sorting.

**RA-004-1100** — Sorting shall be deterministic.

## 13. Searching

Every Aggregate Root shall support searching. Searchable fields are declared by Domain. Semantic search is supported through Knowledge Graph.

**RA-004-1200** — Search contracts shall be documented.

**RA-004-1201** — Search indexing is configurable.

## 14. Command APIs

Commands modify business state and respond with 202 Accepted, 200 OK, or 201 Created. Commands do not return collections.

**RA-004-1300** — Commands publish Domain Events.

**RA-004-1301** — Commands execute inside transactions.

## 15. Query APIs

Queries never modify state and support projection, read models, caching, graph queries, and digital twin views.

**RA-004-1400** — Queries shall never publish events.

**RA-004-1401** — Queries are optimized independently.

## 16. Bulk Operations

Bulk create, update, archive, validation, import, and export operations are asynchronous.

**RA-004-1500** — Bulk operations shall publish progress events.

## 17. Event APIs

Event APIs support subscribe, unsubscribe, replay, checkpoint, and acknowledge operations.

**RA-004-1600** — Event replay is mandatory.

**RA-004-1601** — Subscriptions are durable.

## 18. API Security

Every endpoint implements OAuth2, JWT, role authorization, policy authorization, rate limiting, audit, input validation, and output validation.

**RA-004-1700** — Anonymous access is prohibited except explicitly approved public endpoints.

**RA-004-1701** — Authorization policies are centralized.

## 19. AI APIs

AI APIs operate through a dedicated AI Gateway and support reason, explain, forecast, optimize, classify, summarize, and recommend. Every AI request includes context, Knowledge Graph reference, Digital Twin reference, evidence, and requirement references.

**RA-004-1800** — AI shall never bypass business services.

**RA-004-1801** — AI responses shall be explainable.

**RA-004-1802** — AI confidence is mandatory.

## 20. OpenAPI Specification

Every API shall automatically generate OpenAPI, Swagger, JSON Schema, client SDK, examples, and test collections.

**RA-004-1900** — OpenAPI documentation is mandatory.

**RA-004-1901** — API documentation is generated automatically.

## 21. API Telemetry

Every endpoint publishes execution time, latency, response size, status code, user, correlation ID, requirement IDs, exceptions, and retries.

**RA-004-2000** — API telemetry is mandatory.

**RA-004-2001** — Telemetry shall integrate with Monitoring Domain.

## 22. API Testing

Every endpoint shall include unit tests, integration tests, contract tests, load tests, security tests, AI tests, and documentation tests.

**RA-004-2100** — API coverage is 100%.

**RA-004-2101** — Contract validation is mandatory before release.

## 23. AI Coding Agent Construction Contract

AI coding agents generating APIs shall generate controllers, DTOs, request objects, response objects, validators, OpenAPI specification, unit tests, integration tests, telemetry, documentation, and requirement references.

**RA-004-2200** — AI-generated APIs shall conform to Canonical API Architecture.

**RA-004-2201** — AI shall preserve REST semantics.

**RA-004-2202** — Generated endpoints shall include Requirement IDs within source documentation.

## 24. Acceptance Criteria

A Canonical API is complete when URI, OpenAPI, authentication, authorization, validation, telemetry, audit, tests, documentation, and requirement traceability are complete.

**RA-004-2300** — No API shall enter production until all acceptance criteria are satisfied.

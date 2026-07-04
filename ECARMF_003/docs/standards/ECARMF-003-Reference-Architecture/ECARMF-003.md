# ECARMF-003 — Reference Architecture Standard

## Engineering Construction Specification

This master document indexes the approved authoring baseline for ECARMF-003.

## Table of Contents

1. [RA-001 Platform Architecture Foundation](chapters/RA-001-Platform-Architecture-Foundation.md)
2. [RA-002 Enterprise Domain Architecture](chapters/RA-002-Enterprise-Domain-Architecture.md)
3. [RA-003 Canonical Service Architecture](chapters/RA-003-Canonical-Service-Architecture.md)
4. [RA-004 Canonical API Architecture](chapters/RA-004-Canonical-API-Architecture.md)

## Architecture Position

ECARMF-003 defines how the ECARMF platform is engineered. ECARMF-001 defines the constitutional foundation. ECARMF-002 defines the ontology, semantic model, and engineering language. ECARMF-003 defines the platform architecture, domain structure, service construction rules, and API contracts used to build the system.

## Reference Implementation Stack

- Backend: .NET 8, ASP.NET Core, C#
- Persistence: SQL Server with provider abstraction where applicable
- APIs: REST, OpenAPI 3.x, JSON
- Frontend: React, TypeScript
- Authentication: OAuth 2.1, OpenID Connect, JWT
- Deployment: Docker, Kubernetes, cloud-neutral
- AI: Provider abstraction layer for OpenAI, Azure OpenAI, Anthropic, Google Gemini, local models, and future providers

## Normative Rule

Every implementation claiming ECARMF architectural conformance shall satisfy all mandatory requirements contained in this standard and shall preserve requirement traceability in source code, tests, APIs, database artifacts, and AI-generated components.

# Chapter 1 — Universal Entity Model

## Requirement UEM-0001
Every ECARMF managed object shall inherit from the Universal Base Entity.

## Requirement UEM-0002
Every Universal Base Entity shall possess globally unique immutable identity.

## Requirement UEM-0003
Every Entity shall maintain lifecycle traceability, audit history, relationship references, governance metadata, evidence references, and security classification.

## Base Entity Mandatory Attribute Groups

- Identity
- Classification
- Ownership
- Lifecycle
- Governance
- Traceability
- Audit
- Security
- Metadata

## Relationship Entity Rule

Relationships are first-class entities. A relationship is not merely a database foreign key. It has identity, source, target, direction, cardinality, evidence, confidence, lifecycle, and audit history.

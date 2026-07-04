# Chapter 11 — Meta Model Constraints

## Requirement MMC-0001
Every implementation shall enforce all constraints defined by ECARMF.

## Constraint Hierarchy
Universal Laws → Ontology Constraints → Entity Constraints → Relationship Constraints → Lifecycle Constraints → Business Constraints → Security Constraints → Implementation Constraints

## Mandatory Constraints

- Duplicate identifiers are prohibited.
- Orphan relationships are prohibited.
- Invalid graph topology shall be rejected.
- Canonical definitions shall remain unique.
- Recursive ownership is prohibited.
- Every Risk shall reference measurable exposure.
- Every Decision shall reference context and evidence.
- AI shall not alter ontology.

## Requirement CEE-0001
Every implementation shall include a Constraint Enforcement Engine.

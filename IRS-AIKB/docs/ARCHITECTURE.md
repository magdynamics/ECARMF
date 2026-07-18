# Architecture

## Principles

1. **Traceability:** every extracted statement points to an immutable source version and location.
2. **Authority separation:** controlling law, administrative instructions, procedural guidance, and educational material remain distinguishable.
3. **Review gates:** machine extraction is never presented as reviewed tax guidance.
4. **Reproducibility:** hashes, retrieval dates, parsers, and schema migrations make every build auditable.
5. **Explainability:** each readiness finding exposes its rule, rationale, score, and recommended technique.

## Product structure

MAG Audit separates the shared client/case/evidence system from jurisdiction-specific
analysis. The present IRS knowledge base is the `US-IRS` module. The `US-IL-IDOR`
module is reserved for later Illinois knowledge work and is intake-only until approved.
See `MAG_AUDIT_MULTI_JURISDICTION_DESIGN.md`.

## Processing flow

```text
Official jurisdiction source
  -> immutable download + SHA-256
  -> source/version registry
  -> page and section extraction
  -> authority and technique candidates
  -> technical review
  -> searchable knowledge objects
  -> risk/readiness assessment
  -> CPA-reviewed deliverable
```

## Core entities

- `source` identifies the official publication or web resource.
- `source_version` preserves a retrieved artifact and hash.
- `section` stores addressable extracted text.
- `authority` normalizes legal and administrative citations.
- `technique` represents an examination procedure.
- `risk_indicator` maps observed facts to an explainable score and technique.
- `assessment` preserves the engine version, inputs, and findings.

## Planned ingestion controls

- Jurisdiction-specific official-domain allowlists
- content-type and file-size validation
- SHA-256 duplicate detection
- PDF page-count verification
- extraction error ledger
- review assignment and approval timestamps
- supersession links between source versions
- affected-object report when a source changes

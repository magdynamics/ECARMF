# MAG Audit Bulk Document Ingestion — Build Record

## Implemented core

The first native ingestion increment is implemented in:

```text
src/ECARMF.Kernel.Application/MagAudit/Documents/BulkDocumentIngestion.cs
```

It provides:

- tenant- and case-scoped import batches;
- pre-upload inventory registration;
- safe relative-path validation;
- supported document-type allowlist;
- 500-file accountable batch limit;
- 250 MB individual file limit;
- declared file size and SHA-256 verification;
- case-scoped duplicate candidate detection;
- quarantine-before-processing requirement;
- replaceable malware scanner, text extractor and classifier contracts;
- deletion of rejected quarantine objects;
- confidence-gated acceptance or human review;
- evidence acceptance contract; and
- append-only audit events for batch creation, processing and rejection.

The core does not link to ClamAV, Tesseract, Azure or any other vendor. Providers implement:

```text
IMagAuditMalwareScanner
IMagAuditTextExtractor
IMagAuditDocumentClassifier
IMagAuditEvidenceStore
IMagAuditDocumentImportStore
```

This preserves the standalone/ECARMF and local/cloud deployment choices.

## Security decisions

1. The client inventory declares size and hash before content processing.
2. The server recomputes SHA-256 and compares it before quarantine processing.
3. An unsafe relative path or unsupported extension is rejected.
4. A failed malware scan removes the quarantine object and records rejection.
5. Low-confidence classification remains in review; it is not accepted automatically.
6. Duplicate lookup is tenant- and case-scoped so hash matching cannot reveal another client's file.
7. No provider is silently treated as optional. Deployment is incomplete until real providers are registered.

## Provider implementation order

### Local/private pilot

1. Resumable tus upload transport.
2. Encrypted local evidence provider for synthetic pilot files.
3. ClamAV daemon adapter operating out of process.
4. Tesseract OCR adapter.
5. MAG Audit rule-based tax-document classifier.
6. Persistent ECARMF import store and API endpoints.
7. Staff exception-review UI and import reconciliation report.

### Production

1. Azure Blob evidence provider with encryption, versions, retention and legal hold.
2. Azure Document Intelligence fallback for approved low-confidence pages.
3. Production monitoring, retry queue and incident controls.
4. Optional advanced malware provider.
5. Optional semantic search adapter.

## Validation

`MagAuditBulkDocumentIngestionTests` currently verifies:

- clean content with matching hash can be accepted;
- a mismatched size/hash is rejected before extraction;
- malware rejection removes quarantine content;
- low confidence requires human review; and
- unsafe paths, unsupported types and cross-tenant batch access fail closed.

This increment is the application core, not the completed desktop importer or production storage deployment. The next increment connects persistent providers, resumable transport, API endpoints and the review interface.


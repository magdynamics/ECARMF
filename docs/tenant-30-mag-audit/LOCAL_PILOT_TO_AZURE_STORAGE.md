# MAG Audit storage plan: local pilot to Azure production

## Current pilot boundary

All pilot client, case, and document data remains local. Client/case metadata is
stored in `IRS-AIKB/data/pilot_workspace.db`. Uploaded originals are written to
`IRS-AIKB/tmp/pilot_vault/<case-id>/quarantine/`. These paths are runtime data
and must never be committed to GitHub.

Pilot uploads are hashed and quarantined. They are not approved for analysis or
external release. Until authentication, encryption, malware scanning, backup,
access logging, and recovery controls are complete, use synthetic or properly
de-identified pilot data only.

## Production Azure pattern

Use Azure Blob Storage for document bodies and an application database for
metadata. The database stores the document ID, tenant/client/case scope, blob
object ID, SHA-256, size, media type, classification, privilege state, retention
class, lineage, version, review status, and access policy. It does not store
large document bytes.

Recommended private containers:

- `evidence-originals`: immutable uploaded originals; versioning and legal hold.
- `evidence-quarantine`: new uploads pending malware and intake review.
- `evidence-working`: OCR derivatives, normalized files, and redacted copies.
- `workpapers`: controlled staff and agent work products.
- `deliverables`: approved reports and released response packages.
- `knowledge-sources`: versioned public regulatory source corpus.

All object names should use opaque tenant, case, and document identifiers—not
taxpayer names, TINs, or original filenames. Keep original filenames encrypted
in application metadata.

## Large-document flow

1. The application creates a scoped upload session.
2. The browser uploads in resumable blocks directly to the quarantine container
   using a short-lived, write-only token.
3. The system finalizes the upload, calculates/verifies SHA-256, records size and
   block manifest, and checks for duplicates.
4. Microsoft Defender for Storage or the approved scanning service reports the
   malware result through an event-driven workflow.
5. OCR and classification run on an isolated derivative; the original is never
   modified.
6. A human resolves identity, tax year, privilege, and classification exceptions.
7. The approved original is promoted by server-side copy to the immutable
   evidence container; lineage links every derivative to it.

## Security and lifecycle

- Private endpoints; public blob access disabled.
- Microsoft Entra workload identity; no storage account keys in code.
- Encryption at rest, TLS in transit, and customer-managed keys if required.
- Tenant/case authorization checked before issuing any temporary object token.
- Blob versioning, soft delete, immutable retention, and legal holds.
- Complete read, download, change, release, and deletion-attempt audit events.
- Hot tier for active matters; Cool/Cold after case inactivity; Archive only when
  retrieval delay is acceptable and no active matter or hold requires immediate
  access.
- Retention and destruction follow engagement, legal, professional, and client
  requirements; lifecycle rules never override a legal hold.

## Migration without redesign

Application services must depend on a `DocumentStorageProvider` contract. The
pilot implementation uses the local filesystem. Production uses Azure Blob
Storage. Document IDs, hashes, case links, review states, and lineage remain the
same. A controlled migration utility uploads each local original, verifies the
remote hash, records the Azure object version, and only then marks the local
copy eligible for approved disposition.

# Client Input and Document Upload Design

## Client journey

The portal should use a short guided flow:

```text
Invitation → Identity verification → Engagement and consent
→ Taxpayer/return scope → Guided checklist → Upload and classify
→ Review missing items → Client certification → Secure submission
→ Firm intake review
```

### Screen 1 — Welcome and security

- Firm and engagement name
- Client-safe matter reference
- Identity verification and MFA
- Privacy and permitted-use summary
- Session expiration and support contact

### Screen 2 — Scope confirmation

- Taxpayer/entity name displayed securely
- Entity and return types
- Tax years and periods requested
- Engagement purpose
- Authorized uploader capacity
- Consent version and acknowledgment
- A clear warning not to upload unrelated people or tax years

### Screen 3 — Guided checklist

The checklist adapts to the facts:

- Filed returns, schedules, elections, and statements
- Tax-software/e-file export where authorized
- Books, trial balance, and financial statements
- Bank and merchant records
- Information returns
- Payroll
- Inventory and COGS
- Fixed assets and depreciation
- K-1, basis, distributions, debt, and ownership
- IRS notices, envelopes, reports, and IDRs
- Issue-specific supporting documents

Every category shows `Not started`, `Files uploaded—unreviewed`, `Incomplete`,
`Accepted`, or `Not applicable—explanation required`.

### Screen 4 — Upload

The client can drag files or select them. Before upload, the portal asks for category,
tax year, return/entity relationship, and whether attorney or legal communications may
be included. Progress displays hashing, malware scan, encrypted upload, and receipt.

Do not use raw SSNs, EINs, bank numbers, or client names in storage keys or ordinary
logs. Original filenames are encrypted; a sanitized display name is used throughout
the application.

### Screen 5 — Review and submit

- Files grouped by year and category
- Duplicates and rejected files explained plainly
- Missing required categories highlighted
- IRS deadlines displayed only when verified from the supplied notice
- Client certification that the upload is authorized and responsive
- Submit button remains disabled while mandatory blockers exist

Submission routes the package to a firm intake specialist. It does not launch final
analysis automatically.

## Staff workspace

Firm personnel see:

- New submissions and urgency
- Consent, authority, and scope
- Malware/quarantine status
- Document categories and years
- Completeness matrix
- Duplicate and superseded files
- Potential privilege queue
- OCR/encryption exceptions
- Evidence hashes and custody events
- Accept, reject, reclassify, request more, or release for extraction actions

Every action records actor, reason, timestamp, and prior/resulting status.

## API contract

Recommended endpoints:

```text
POST   /matters/{matter}/upload-sessions
GET    /upload-sessions/{session}
POST   /upload-sessions/{session}/files/initiate
POST   /upload-sessions/{session}/files/{upload}/complete
PATCH  /upload-sessions/{session}/files/{upload}/classification
DELETE /upload-sessions/{session}/files/{upload}     (approval workflow, not hard delete)
GET    /upload-sessions/{session}/completeness
POST   /upload-sessions/{session}/submit
POST   /intake/{session}/review
POST   /intake/{session}/release-for-extraction
```

Use short-lived, single-object upload authorization. The application verifies the
server-calculated hash, byte count, detected type, scan result, and session scope; it
does not trust browser-provided metadata.

## Upload lifecycle

```text
initiated → encrypted_upload → hash_verified → malware_scanned
→ quarantined → classified → privilege_screened → completeness_reviewed
→ intake_approved → released_for_extraction
```

Exception statuses include rejected type, corrupt/encrypted, malware suspected,
outside scope, duplicate, consent missing, privilege hold, and legal-hold conflict.

## Accessibility and usability

- Plain language and progress indicators
- Keyboard and screen-reader support
- Mobile-responsive upload status
- Save and resume
- Large-file retry without restarting completed parts
- Clear errors with corrective instructions
- Multilingual instructions where supported
- No tax-risk score displayed to the client before professional review

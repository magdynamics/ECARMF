# Tax Return Intake and Canonical Model

## Purpose

Phase B turns a collection of taxpayer return files into an immutable, reviewable
inventory before any risk score is produced. It recognizes the return family and
printed year, hashes the source, records page and form-field counts, and flags files
that require OCR or human review.

The intake report is **metadata-only by default**. Taxpayer-entered PDF field values
are excluded unless the operator deliberately enables the sensitive-data option.
Client evidence must be stored separately from the public IRS source corpus.

## Recognition and extraction hierarchy

1. Exact hash against a versioned official-form registry.
2. PDF title/subject plus first-page IRS anchors.
3. Form description, printed year, IRS URL, and OMB number as corroboration.
4. AcroForm/XFA values, then positioned text, then controlled OCR.
5. Human verification when signals conflict or completeness is uncertain.

Official IRS form fields use generic, revision-dependent names. They are layout
templates, not stable accounting semantics. Authorized e-file XML or tax-software
exports should be preferred when available. A versioned mapping must connect each
form/year/schedule/line to a stable canonical concept.

## Provenance-preserving facts

Every extracted value must preserve the source hash, form revision, schedule, part,
line, label, page/location, extraction method and version, mapping rule and version,
confidence, and human validation status. The original value is never overwritten.
Amended and superseding returns are immutable linked filings.

Blank fields are not silently converted to zero. Missing states are explicitly:
`true_zero`, `blank_as_filed`, `not_applicable`, `schedule_missing`,
`extraction_failed`, or `unknown`.

## Security and professional gates

Production intake requires verified engagement authority and purpose, matter-level
access, PII classification, malware quarantine, privilege screening, retention and
legal-hold review, immutable hashes, chain of custody, and an assigned reviewer.
Names, TINs, bank data, signatures, and raw values must not enter filenames, logs,
prompts, embeddings, test fixtures, or the analytics identifier layer. The schema
uses tokenized external references and reserves encrypted storage for return values.

Potentially privileged material is isolated pending counsel review. Automated
analysis cannot waive privilege, transmit a document, sign a response, extend a
statute, agree to an adjustment, or make a final legal determination.

## Current command

```text
irs-aikb intake-returns <file-or-directory> --output intake-report.json
```

`--include-values` is intentionally non-default and creates a sensitive report that
must be protected as client tax-return information.

## Scope boundary

This release performs inventory and recognition. It does not yet claim complete
line-item extraction from flattened, scanned, password-protected, or tax-software
generated returns. The next increment is the versioned form-line mapping registry,
e-file XML adapter, OCR quarantine, arithmetic/cross-schedule validations, and
human-review workflow. Numeric portfolio scoring should be withheld or labeled
preliminary when completeness thresholds are not met.

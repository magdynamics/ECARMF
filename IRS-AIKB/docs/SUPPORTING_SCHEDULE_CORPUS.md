# Supporting Schedule Corpus — 2018–2025

## Download result

The official IRS supporting-schedule collection contains 111 verified PDFs totaling
1,070 pages and approximately 24.3 MB. Every downloaded object has its official URL,
retrieval date, byte count, page count, SHA-256 hash, local path, year, product, and
document type in `source-manifest/supporting_schedules_2018_2025.csv`.

Downloaded annual forms and instructions:

- Form 1040 Schedules C, E, and F
- Schedule K-1 for Forms 1065, 1120-S, and 1041
- Available Schedule M-3 forms and instructions for Forms 1065 and 1120

The manifest contains 144 attempted product/year/document records. Thirty-three are
marked `not_available`, predominantly because Schedule M-3 is revised periodically
rather than issued as a distinct annual PDF. Absence is recorded; it is not replaced
with an adjacent year's document. Schedule M-3 therefore needs an effective-revision
model before its mappings can be applied to a particular filing period.

## Mapping validation

The reviewed registry now contains control-total mappings for Schedules C, E, and F
and core allocation boxes for all three K-1 families. Forty-eight schedule/year forms
were checked across 2018–2025. Labels are validated directly against the official IRS
blank PDFs; generic AcroForm field names are not treated as accounting semantics.

Schedule E preserves separate properties and entity activities. K-1 mappings preserve
the issuing entity, recipient, box, code where applicable, source file, tax year, and
mapping version. Coded catch-all boxes require their code and attached statement and
must not be collapsed into a single undifferentiated value.

## IRS source behavior

The IRS prior-year library confirms annual K-1 versions, while the IRS form pages show
that K/K-1 allocations report pass-through income, deductions, and credits. Schedule
M-3 instructions use asset thresholds and periodic revisions; applicability must be
evaluated under the revision effective for the filing, not inferred from a missing
annual filename.

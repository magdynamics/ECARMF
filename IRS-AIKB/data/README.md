# Database snapshot

`mainstream_atg.db` is the reproducible SQLite snapshot for the mainstream IRS ATG-index corpus retrieved on July 17, 2026.

The snapshot contains source/version provenance, 2,604 searchable sections, normalized authority candidates, machine-extracted technique candidates, and FTS5 indexes. Rebuild it with the `ingest-corpus` command documented in the module README.

Machine-extracted records are not approved tax guidance. They require technical and current-law review before client use.

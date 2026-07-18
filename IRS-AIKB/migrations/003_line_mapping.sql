PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS line_mapping_definition (
    mapping_id TEXT PRIMARY KEY,
    form_family TEXT NOT NULL,
    tax_year INTEGER NOT NULL,
    schedule TEXT NOT NULL DEFAULT 'main',
    source_line TEXT NOT NULL,
    source_label TEXT NOT NULL,
    concept_id TEXT NOT NULL REFERENCES canonical_concept(concept_id),
    mapping_version TEXT NOT NULL,
    review_status TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (form_family, tax_year, schedule, source_line, mapping_version)
);

CREATE TABLE IF NOT EXISTS mapping_validation_run (
    validation_id TEXT PRIMARY KEY,
    mapping_version TEXT NOT NULL,
    official_file_sha256 TEXT NOT NULL,
    parser_version TEXT NOT NULL,
    result_json TEXT NOT NULL,
    validated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

PRAGMA foreign_keys = ON;

-- Client evidence is logically and operationally separate from the public-source corpus.
-- external_ref values are tokens; names, TINs, and raw return values do not belong here.
CREATE TABLE IF NOT EXISTS taxpayer (
    taxpayer_id TEXT PRIMARY KEY,
    external_ref TEXT NOT NULL UNIQUE,
    entity_classification TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS tax_return (
    return_id TEXT PRIMARY KEY,
    taxpayer_id TEXT REFERENCES taxpayer(taxpayer_id),
    form_family TEXT NOT NULL,
    form_variant TEXT,
    tax_year INTEGER NOT NULL,
    period_begin TEXT,
    period_end TEXT,
    filing_type TEXT NOT NULL DEFAULT 'original',
    supersedes_return_id TEXT REFERENCES tax_return(return_id),
    review_status TEXT NOT NULL DEFAULT 'intake',
    CHECK (filing_type IN ('original','superseding','amended','irs_adjusted'))
);

CREATE TABLE IF NOT EXISTS return_file (
    file_id TEXT PRIMARY KEY,
    return_id TEXT REFERENCES tax_return(return_id),
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    byte_count INTEGER NOT NULL,
    page_count INTEGER,
    media_type TEXT NOT NULL DEFAULT 'application/pdf',
    original_name_token TEXT,
    evidence_status TEXT NOT NULL DEFAULT 'received',
    recognition_confidence TEXT NOT NULL DEFAULT 'none',
    extraction_method TEXT,
    received_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (sha256, return_id)
);

CREATE TABLE IF NOT EXISTS canonical_concept (
    concept_id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    data_type TEXT NOT NULL DEFAULT 'money',
    concept_version TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS return_value (
    value_id TEXT PRIMARY KEY,
    return_id TEXT NOT NULL REFERENCES tax_return(return_id),
    concept_id TEXT NOT NULL REFERENCES canonical_concept(concept_id),
    source_form TEXT NOT NULL,
    source_schedule TEXT,
    source_line TEXT,
    source_label TEXT,
    source_page INTEGER,
    period_position TEXT NOT NULL DEFAULT 'flow',
    value_ciphertext BLOB,
    missing_reason TEXT,
    extraction_method TEXT NOT NULL,
    mapping_method TEXT NOT NULL,
    value_confidence REAL,
    mapping_confidence REAL,
    validation_status TEXT NOT NULL DEFAULT 'unreviewed',
    CHECK (period_position IN ('flow','beginning','ending','point_in_time')),
    CHECK (missing_reason IS NULL OR missing_reason IN
      ('true_zero','blank_as_filed','not_applicable','schedule_missing','extraction_failed','unknown'))
);

CREATE TABLE IF NOT EXISTS related_return (
    left_return_id TEXT NOT NULL REFERENCES tax_return(return_id),
    right_return_id TEXT NOT NULL REFERENCES tax_return(return_id),
    relationship_type TEXT NOT NULL,
    confidence REAL,
    confirmation_status TEXT NOT NULL DEFAULT 'inferred',
    PRIMARY KEY (left_return_id, right_return_id, relationship_type)
);

CREATE TABLE IF NOT EXISTS ingestion_event (
    event_id TEXT PRIMARY KEY,
    file_id TEXT REFERENCES return_file(file_id),
    event_type TEXT NOT NULL,
    actor_token TEXT,
    occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    details_json TEXT NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS idx_return_taxpayer_year ON tax_return(taxpayer_id, tax_year);
CREATE INDEX IF NOT EXISTS idx_return_file_hash ON return_file(sha256);
CREATE INDEX IF NOT EXISTS idx_return_value_concept ON return_value(return_id, concept_id);

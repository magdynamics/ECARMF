PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS return_activity (
    activity_id TEXT PRIMARY KEY,
    return_id TEXT NOT NULL REFERENCES tax_return(return_id),
    activity_type TEXT NOT NULL,
    activity_token TEXT NOT NULL,
    schedule TEXT NOT NULL,
    review_status TEXT NOT NULL DEFAULT 'unreviewed'
);

CREATE TABLE IF NOT EXISTS owner_allocation (
    allocation_id TEXT PRIMARY KEY,
    entity_return_id TEXT NOT NULL REFERENCES tax_return(return_id),
    recipient_taxpayer_id TEXT REFERENCES taxpayer(taxpayer_id),
    concept_id TEXT NOT NULL REFERENCES canonical_concept(concept_id),
    value_ciphertext BLOB,
    source_schedule TEXT NOT NULL,
    source_line TEXT,
    validation_status TEXT NOT NULL DEFAULT 'unreviewed'
);

CREATE TABLE IF NOT EXISTS validation_run (
    validation_id TEXT PRIMARY KEY,
    package_hash TEXT NOT NULL,
    engine_version TEXT NOT NULL,
    analysis_status TEXT NOT NULL,
    scoring_gate TEXT NOT NULL,
    result_json TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS validation_finding (
    finding_id TEXT PRIMARY KEY,
    validation_id TEXT NOT NULL REFERENCES validation_run(validation_id),
    rule_id TEXT NOT NULL,
    category TEXT NOT NULL,
    severity TEXT NOT NULL,
    status TEXT NOT NULL,
    expected_ciphertext BLOB,
    observed_ciphertext BLOB,
    message TEXT NOT NULL
);

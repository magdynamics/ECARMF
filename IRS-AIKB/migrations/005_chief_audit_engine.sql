PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS issue_rule_definition (
    issue_id TEXT PRIMARY KEY,
    dimension TEXT NOT NULL,
    title TEXT NOT NULL,
    rule_version TEXT NOT NULL,
    authority_json TEXT NOT NULL,
    evidence_json TEXT NOT NULL,
    review_status TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS analytical_feature (
    feature_id TEXT PRIMARY KEY,
    return_id TEXT NOT NULL REFERENCES tax_return(return_id),
    feature_name TEXT NOT NULL,
    value_ciphertext BLOB,
    calculation_version TEXT NOT NULL,
    denominator_status TEXT NOT NULL,
    source_lineage_json TEXT NOT NULL,
    UNIQUE(return_id, feature_name, calculation_version)
);

CREATE TABLE IF NOT EXISTS chief_audit_assessment (
    assessment_id TEXT PRIMARY KEY,
    return_id TEXT REFERENCES tax_return(return_id),
    engine_version TEXT NOT NULL,
    assessment_status TEXT NOT NULL,
    confidence_score INTEGER NOT NULL,
    priority_score INTEGER NOT NULL,
    input_hash TEXT NOT NULL,
    result_json TEXT NOT NULL,
    reviewer_token TEXT,
    reviewed_at TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

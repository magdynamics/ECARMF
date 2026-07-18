PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS source (
    source_id TEXT PRIMARY KEY,
    source_type TEXT NOT NULL,
    title TEXT NOT NULL,
    official_url TEXT NOT NULL,
    authority_class TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('current','historical','superseded','pending_verification')),
    last_checked_date TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS source_version (
    version_id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL REFERENCES source(source_id),
    publication_date TEXT,
    retrieval_date TEXT NOT NULL,
    sha256 TEXT NOT NULL CHECK (length(sha256) = 64),
    local_path TEXT NOT NULL,
    page_count INTEGER CHECK (page_count IS NULL OR page_count > 0),
    UNIQUE (source_id, sha256)
);

CREATE TABLE IF NOT EXISTS section (
    section_id TEXT PRIMARY KEY,
    version_id TEXT NOT NULL REFERENCES source_version(version_id),
    heading TEXT,
    page_start INTEGER,
    page_end INTEGER,
    body TEXT NOT NULL,
    review_status TEXT NOT NULL DEFAULT 'machine_extracted'
);

CREATE TABLE IF NOT EXISTS authority (
    authority_id TEXT PRIMARY KEY,
    authority_type TEXT NOT NULL,
    citation TEXT NOT NULL,
    title TEXT,
    UNIQUE (authority_type, citation)
);

CREATE TABLE IF NOT EXISTS section_authority (
    section_id TEXT NOT NULL REFERENCES section(section_id),
    authority_id TEXT NOT NULL REFERENCES authority(authority_id),
    PRIMARY KEY (section_id, authority_id)
);

CREATE TABLE IF NOT EXISTS technique (
    technique_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    objective TEXT NOT NULL,
    procedure_json TEXT NOT NULL DEFAULT '[]',
    source_section_id TEXT REFERENCES section(section_id),
    review_status TEXT NOT NULL DEFAULT 'machine_extracted'
);

CREATE TABLE IF NOT EXISTS risk_indicator (
    rule_id TEXT PRIMARY KEY,
    category TEXT NOT NULL,
    score INTEGER NOT NULL CHECK (score BETWEEN 0 AND 100),
    rationale TEXT NOT NULL,
    technique_id TEXT REFERENCES technique(technique_id),
    review_status TEXT NOT NULL DEFAULT 'curated_seed'
);

CREATE TABLE IF NOT EXISTS assessment (
    assessment_id TEXT PRIMARY KEY,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    engine_version TEXT NOT NULL,
    profile_json TEXT NOT NULL,
    score INTEGER NOT NULL CHECK (score BETWEEN 0 AND 100),
    classification TEXT NOT NULL,
    findings_json TEXT NOT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS section_fts USING fts5(
    section_id UNINDEXED,
    heading,
    body
);

CREATE VIRTUAL TABLE IF NOT EXISTS technique_fts USING fts5(
    technique_id UNINDEXED,
    name,
    objective
);

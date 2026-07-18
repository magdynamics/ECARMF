PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS upload_session (
 session_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL, client_token TEXT NOT NULL,
 uploader_token TEXT NOT NULL, consent_version TEXT NOT NULL, purpose_code TEXT NOT NULL,
 tax_years_json TEXT NOT NULL, return_types_json TEXT NOT NULL,
 status TEXT NOT NULL, expires_at TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS uploaded_document (
 upload_id TEXT PRIMARY KEY, session_id TEXT NOT NULL REFERENCES upload_session(session_id),
 evidence_id TEXT, original_name_ciphertext BLOB NOT NULL, safe_display_name TEXT NOT NULL,
 category TEXT NOT NULL, tax_year INTEGER, return_type TEXT, sha256 TEXT NOT NULL,
 byte_count INTEGER NOT NULL, detected_media_type TEXT, malware_status TEXT NOT NULL,
 privilege_status TEXT NOT NULL, quarantine_status TEXT NOT NULL,
 storage_object_token TEXT NOT NULL, uploaded_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
 UNIQUE(session_id,sha256)
);
CREATE TABLE IF NOT EXISTS upload_completeness (
 session_id TEXT NOT NULL REFERENCES upload_session(session_id), category TEXT NOT NULL,
 required_status TEXT NOT NULL, received_count INTEGER NOT NULL DEFAULT 0,
 reviewer_status TEXT NOT NULL DEFAULT 'unreviewed', exception_reason TEXT,
 PRIMARY KEY(session_id,category)
);
CREATE TABLE IF NOT EXISTS upload_consent_event (
 consent_event_id TEXT PRIMARY KEY, session_id TEXT NOT NULL REFERENCES upload_session(session_id),
 consent_version TEXT NOT NULL, action TEXT NOT NULL, actor_token TEXT NOT NULL,
 occurred_at TEXT NOT NULL, evidence_hash TEXT NOT NULL
);

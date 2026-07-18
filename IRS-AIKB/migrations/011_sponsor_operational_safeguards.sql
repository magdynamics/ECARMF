PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS sponsor_security_event (
 security_event_id TEXT PRIMARY KEY, sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id),
 event_type TEXT NOT NULL, actor_token TEXT NOT NULL, reason_code TEXT NOT NULL,
 prior_status TEXT, resulting_status TEXT NOT NULL,
 occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor_access_recertification (
 recertification_id TEXT PRIMARY KEY,
 access_grant_id TEXT NOT NULL REFERENCES sponsor_access_grant(access_grant_id),
 reviewed_by_token TEXT NOT NULL, scope_changed INTEGER NOT NULL DEFAULT 0,
 decision TEXT NOT NULL, evidence_hash TEXT NOT NULL,
 occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor_client_notification (
 notification_id TEXT PRIMARY KEY, client_token TEXT NOT NULL,
 sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id),
 access_grant_id TEXT REFERENCES sponsor_access_grant(access_grant_id),
 event_type TEXT NOT NULL, delivery_channel TEXT NOT NULL,
 delivery_status TEXT NOT NULL DEFAULT 'pending', resource_token TEXT,
 created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, delivered_at TEXT
);
CREATE TABLE IF NOT EXISTS sponsor_download_control (
 download_id TEXT PRIMARY KEY, access_grant_id TEXT NOT NULL REFERENCES sponsor_access_grant(access_grant_id),
 sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id), resource_type TEXT NOT NULL,
 resource_token TEXT NOT NULL, step_up_event_token TEXT NOT NULL,
 watermark_text TEXT NOT NULL, export_type TEXT NOT NULL,
 byte_count INTEGER, sha256 TEXT, occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS referral_compensation_control (
 compensation_control_id TEXT PRIMARY KEY,
 referral_id TEXT NOT NULL REFERENCES sponsor_referral(referral_id),
 arrangement_type TEXT NOT NULL, agreement_token TEXT,
 permissibility_status TEXT NOT NULL DEFAULT 'pending', reviewed_by_token TEXT,
 reviewed_at TEXT, payment_data_vault_token TEXT,
 CHECK (permissibility_status IN ('pending','approved','prohibited','not_applicable'))
);
CREATE INDEX IF NOT EXISTS idx_sponsor_notification_client ON sponsor_client_notification(client_token, created_at);
CREATE INDEX IF NOT EXISTS idx_sponsor_security_event ON sponsor_security_event(sponsor_id, occurred_at);

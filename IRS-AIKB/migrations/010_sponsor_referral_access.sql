PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS sponsor_organization (
 sponsor_organization_id TEXT PRIMARY KEY, organization_token TEXT NOT NULL UNIQUE,
 organization_type TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'active',
 created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor (
 sponsor_id TEXT PRIMARY KEY,
 sponsor_organization_id TEXT REFERENCES sponsor_organization(sponsor_organization_id),
 person_token TEXT NOT NULL UNIQUE, sponsor_type TEXT NOT NULL,
 professional_designation TEXT, credential_status TEXT NOT NULL DEFAULT 'not_verified',
 relationship_owner_token TEXT, status TEXT NOT NULL DEFAULT 'active',
 security_status TEXT NOT NULL DEFAULT 'active', mfa_enrolled_at TEXT, mfa_verified_at TEXT,
 created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor_credential (
 credential_id TEXT PRIMARY KEY, sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id),
 credential_type TEXT NOT NULL, jurisdiction TEXT, license_number_ciphertext BLOB,
 expires_at TEXT, verification_status TEXT NOT NULL, verified_at TEXT
);
CREATE TABLE IF NOT EXISTS sponsor_referral (
 referral_id TEXT PRIMARY KEY, sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id),
 client_token TEXT NOT NULL, taxpayer_token TEXT, matter_id TEXT REFERENCES controversy_matter(matter_id),
 referred_service TEXT NOT NULL, referral_date TEXT NOT NULL, reason_ciphertext BLOB,
 intake_status TEXT NOT NULL DEFAULT 'pending', conflict_status TEXT NOT NULL DEFAULT 'pending',
 independence_status TEXT NOT NULL DEFAULT 'pending', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor_role (
 sponsor_role_id TEXT PRIMARY KEY, referral_id TEXT NOT NULL REFERENCES sponsor_referral(referral_id),
 matter_id TEXT REFERENCES controversy_matter(matter_id), role_code TEXT NOT NULL,
 status TEXT NOT NULL DEFAULT 'active', effective_at TEXT, expires_at TEXT
);
CREATE TABLE IF NOT EXISTS sponsor_client_consent (
 consent_id TEXT PRIMARY KEY, referral_id TEXT NOT NULL REFERENCES sponsor_referral(referral_id),
 sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id), client_token TEXT NOT NULL,
 decision TEXT NOT NULL CHECK(decision IN ('declined','authorized')),
 consent_version TEXT NOT NULL, scope_json TEXT NOT NULL DEFAULT '{}',
 signed_evidence_hash TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'active',
 effective_at TEXT NOT NULL, expires_at TEXT, revoked_at TEXT, revoked_by_token TEXT,
 created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor_access_grant (
 access_grant_id TEXT PRIMARY KEY, consent_id TEXT NOT NULL REFERENCES sponsor_client_consent(consent_id),
 sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id), client_token TEXT NOT NULL,
 matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 taxpayer_scope_json TEXT NOT NULL DEFAULT '[]', tax_year_scope_json TEXT NOT NULL DEFAULT '[]',
 permission_scope_json TEXT NOT NULL DEFAULT '[]', document_category_scope_json TEXT NOT NULL DEFAULT '[]',
 effective_at TEXT NOT NULL, expires_at TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending',
 firm_approved_by_token TEXT, firm_approved_at TEXT, revoked_at TEXT, revoked_by_token TEXT,
 recertified_at TEXT, recertified_by_token TEXT,
 bulk_export_approved INTEGER NOT NULL DEFAULT 0,
 auto_terminate_on_matter_close INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS sponsor_artifact_release (
 release_id TEXT PRIMARY KEY, access_grant_id TEXT NOT NULL REFERENCES sponsor_access_grant(access_grant_id),
 artifact_type TEXT NOT NULL, artifact_id TEXT NOT NULL, sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id),
 release_status TEXT NOT NULL, released_by_token TEXT NOT NULL, released_at TEXT NOT NULL,
 UNIQUE(access_grant_id, artifact_type, artifact_id)
);
CREATE TABLE IF NOT EXISTS sponsor_access_event (
 access_event_id TEXT PRIMARY KEY, access_grant_id TEXT REFERENCES sponsor_access_grant(access_grant_id),
 sponsor_id TEXT NOT NULL REFERENCES sponsor(sponsor_id), matter_id TEXT,
 permission_code TEXT NOT NULL, resource_type TEXT, resource_token TEXT,
 decision TEXT NOT NULL, reason_codes_json TEXT NOT NULL, occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS sponsor_consent_event (
 consent_event_id TEXT PRIMARY KEY, consent_id TEXT NOT NULL REFERENCES sponsor_client_consent(consent_id),
 action TEXT NOT NULL, actor_token TEXT NOT NULL, evidence_hash TEXT NOT NULL,
 occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_referral_client ON sponsor_referral(client_token, referral_date);
CREATE INDEX IF NOT EXISTS idx_referral_sponsor ON sponsor_referral(sponsor_id, referral_date);
CREATE INDEX IF NOT EXISTS idx_sponsor_grant_matter ON sponsor_access_grant(sponsor_id, matter_id, status);
CREATE INDEX IF NOT EXISTS idx_sponsor_access_event ON sponsor_access_event(sponsor_id, occurred_at);

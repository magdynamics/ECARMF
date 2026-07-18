PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS client_communication_preference (
 preference_id TEXT PRIMARY KEY, client_token TEXT NOT NULL,
 preferred_channel TEXT NOT NULL, language_code TEXT NOT NULL,
 update_cadence_days INTEGER NOT NULL, urgent_channel TEXT NOT NULL,
 accessibility_json TEXT NOT NULL DEFAULT '{}', quiet_hours_json TEXT NOT NULL DEFAULT '{}',
 consent_status TEXT NOT NULL, effective_at TEXT NOT NULL, superseded_at TEXT
);
CREATE TABLE IF NOT EXISTS client_service_agreement (
 agreement_id TEXT PRIMARY KEY, client_token TEXT NOT NULL,
 matter_id TEXT REFERENCES controversy_matter(matter_id), template_version TEXT NOT NULL,
 scope_json TEXT NOT NULL, fee_terms_token TEXT, delivery_status TEXT NOT NULL,
 signature_status TEXT NOT NULL, delivered_at TEXT, signed_at TEXT,
 signed_evidence_hash TEXT, effective_at TEXT, supersedes_agreement_id TEXT REFERENCES client_service_agreement(agreement_id)
);
CREATE TABLE IF NOT EXISTS client_signature_request (
 signature_request_id TEXT PRIMARY KEY, client_token TEXT NOT NULL,
 matter_id TEXT REFERENCES controversy_matter(matter_id), document_type TEXT NOT NULL,
 document_token TEXT NOT NULL, signer_role TEXT NOT NULL, signer_token TEXT NOT NULL,
 signature_method TEXT NOT NULL, status TEXT NOT NULL, expires_at TEXT,
 sent_by_token TEXT, sent_at TEXT, completed_at TEXT, evidence_hash TEXT
);
CREATE TABLE IF NOT EXISTS case_client_update (
 update_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 client_token TEXT NOT NULL, event_type TEXT NOT NULL, jurisdiction_module_id TEXT,
 subject_token TEXT NOT NULL, body_ciphertext BLOB NOT NULL, plain_language_status TEXT NOT NULL,
 sensitivity_class TEXT NOT NULL, draft_agent_id TEXT, professional_approved_by_token TEXT,
 approval_status TEXT NOT NULL DEFAULT 'draft', delivery_channel TEXT,
 delivery_status TEXT NOT NULL DEFAULT 'not_sent', delivered_at TEXT
);
CREATE TABLE IF NOT EXISTS client_question (
 question_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 client_token TEXT NOT NULL, question_ciphertext BLOB NOT NULL, received_channel TEXT NOT NULL,
 urgency_status TEXT NOT NULL, assigned_owner_token TEXT NOT NULL,
 response_due_at TEXT, response_status TEXT NOT NULL DEFAULT 'open',
 responded_at TEXT, response_update_id TEXT REFERENCES case_client_update(update_id)
);
CREATE TABLE IF NOT EXISTS client_value_event (
 value_event_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 value_type TEXT NOT NULL, baseline_json TEXT NOT NULL, method_json TEXT NOT NULL,
 evidence_json TEXT NOT NULL, amount_ciphertext BLOB, attribution_status TEXT NOT NULL,
 professional_approved_by_token TEXT, approved_at TEXT, client_release_status TEXT NOT NULL DEFAULT 'hold'
);
CREATE TABLE IF NOT EXISTS case_agent_assignment (
 assignment_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 agent_id TEXT NOT NULL, agent_contract_version TEXT NOT NULL, role_code TEXT NOT NULL,
 allowed_actions_json TEXT NOT NULL, prohibited_actions_json TEXT NOT NULL,
 human_approver_token TEXT NOT NULL, status TEXT NOT NULL, assigned_at TEXT NOT NULL,
 ended_at TEXT, UNIQUE(matter_id, agent_id, role_code)
);
CREATE TABLE IF NOT EXISTS client_communication_event (
 communication_event_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 client_token TEXT NOT NULL, related_update_id TEXT REFERENCES case_client_update(update_id),
 event_type TEXT NOT NULL, actor_token TEXT NOT NULL, details_json TEXT NOT NULL,
 occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_client_update_matter ON case_client_update(matter_id, approval_status, delivery_status);
CREATE INDEX IF NOT EXISTS idx_client_question_owner ON client_question(assigned_owner_token, response_status, response_due_at);
CREATE INDEX IF NOT EXISTS idx_signature_request_case ON client_signature_request(matter_id, status, expires_at);


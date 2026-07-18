PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS staff_professional_profile (
 staff_token TEXT PRIMARY KEY, role_code TEXT NOT NULL, supervisor_token TEXT,
 credential_type TEXT, credential_jurisdiction TEXT, credential_status TEXT NOT NULL,
 representation_eligibility_json TEXT NOT NULL DEFAULT '{}', caf_number_vault_token TEXT,
 approval_authority_json TEXT NOT NULL DEFAULT '[]', status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS case_team_assignment (
 assignment_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 staff_token TEXT NOT NULL REFERENCES staff_professional_profile(staff_token),
 case_role TEXT NOT NULL, responsibility_json TEXT NOT NULL,
 effective_at TEXT NOT NULL, ended_at TEXT, status TEXT NOT NULL,
 UNIQUE(matter_id,staff_token,case_role)
);
CREATE TABLE IF NOT EXISTS taxpayer_authorization (
 authorization_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 taxpayer_token TEXT NOT NULL, authorization_type TEXT NOT NULL,
 representative_staff_token TEXT REFERENCES staff_professional_profile(staff_token),
 tax_forms_json TEXT NOT NULL, tax_periods_json TEXT NOT NULL, authorized_acts_json TEXT NOT NULL,
 signature_status TEXT NOT NULL, submission_channel TEXT, submitted_at TEXT,
 agency_status TEXT NOT NULL, agency_reference_token TEXT, revoked_at TEXT, superseded_by_id TEXT
);
CREATE TABLE IF NOT EXISTS authority_contact_event (
 contact_event_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 jurisdiction_module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 contact_type TEXT NOT NULL, authority_function TEXT, authority_employee_token TEXT,
 human_actor_token TEXT NOT NULL REFERENCES staff_professional_profile(staff_token),
 authorization_id TEXT REFERENCES taxpayer_authorization(authorization_id), objective TEXT NOT NULL,
 contemporaneous_memo_ciphertext BLOB, commitments_json TEXT NOT NULL DEFAULT '[]',
 promised_response_at TEXT, professional_review_status TEXT NOT NULL,
 occurred_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS authority_record_request (
 record_request_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 authorization_id TEXT REFERENCES taxpayer_authorization(authorization_id),
 product_type TEXT NOT NULL, tax_forms_json TEXT NOT NULL, tax_periods_json TEXT NOT NULL,
 request_channel TEXT NOT NULL, requested_by_token TEXT NOT NULL,
 request_status TEXT NOT NULL, requested_at TEXT, received_at TEXT,
 evidence_id TEXT, validation_status TEXT NOT NULL DEFAULT 'pending'
);
CREATE TABLE IF NOT EXISTS case_deadline (
 deadline_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 deadline_type TEXT NOT NULL CHECK(deadline_type IN ('verified_external','irs_promised','internal')),
 event_code TEXT NOT NULL, due_at TEXT NOT NULL, source_document_id TEXT,
 calculation_json TEXT NOT NULL DEFAULT '{}', verified_by_token TEXT,
 responsible_staff_token TEXT NOT NULL, status TEXT NOT NULL, completed_at TEXT,
 delivery_evidence_id TEXT
);
CREATE TABLE IF NOT EXISTS audit_program (
 program_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 jurisdiction_module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 program_version TEXT NOT NULL, taxpayer_profile_hash TEXT NOT NULL,
 source_versions_json TEXT NOT NULL, generation_method TEXT NOT NULL,
 status TEXT NOT NULL, approved_by_token TEXT, approved_at TEXT,
 UNIQUE(matter_id,program_version)
);
CREATE TABLE IF NOT EXISTS audit_program_procedure (
 procedure_id TEXT PRIMARY KEY, program_id TEXT NOT NULL REFERENCES audit_program(program_id),
 issue_id TEXT, objective TEXT NOT NULL, procedure_text TEXT NOT NULL,
 evidence_requirements_json TEXT NOT NULL, assigned_actor_type TEXT NOT NULL,
 assigned_to_token TEXT NOT NULL, reviewer_token TEXT NOT NULL,
 workpaper_token TEXT, status TEXT NOT NULL, conclusion_status TEXT NOT NULL DEFAULT 'not_started'
);
CREATE TABLE IF NOT EXISTS escalation_option (
 escalation_option_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 option_code TEXT NOT NULL, eligibility_json TEXT NOT NULL, authority_json TEXT NOT NULL,
 advantages_json TEXT NOT NULL, risks_json TEXT NOT NULL, effect_on_other_rights_json TEXT NOT NULL,
 deadline_id TEXT REFERENCES case_deadline(deadline_id), professional_recommendation_status TEXT NOT NULL,
 client_decision_id TEXT, status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS external_action_approval (
 approval_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 action_code TEXT NOT NULL, action_scope_json TEXT NOT NULL,
 prepared_by_token TEXT NOT NULL, approved_by_token TEXT NOT NULL,
 client_decision_id TEXT, approval_status TEXT NOT NULL, approved_at TEXT,
 executed_at TEXT, execution_evidence_id TEXT
);
CREATE INDEX IF NOT EXISTS idx_case_team_matter ON case_team_assignment(matter_id,status);
CREATE INDEX IF NOT EXISTS idx_case_deadline_open ON case_deadline(status,due_at);
CREATE INDEX IF NOT EXISTS idx_authority_contact_case ON authority_contact_event(matter_id,occurred_at);
CREATE INDEX IF NOT EXISTS idx_audit_program_case ON audit_program(matter_id,status);


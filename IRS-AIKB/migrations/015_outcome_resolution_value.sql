PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS case_finding (
 finding_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 jurisdiction_module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 issue_id TEXT NOT NULL, finding_version TEXT NOT NULL, status TEXT NOT NULL,
 facts_snapshot_hash TEXT NOT NULL, evidence_json TEXT NOT NULL, source_versions_json TEXT NOT NULL,
 rule_version TEXT NOT NULL, severity TEXT NOT NULL, confidence REAL NOT NULL,
 potential_exposure_ciphertext BLOB, professional_conclusion_ciphertext BLOB,
 reviewed_by_token TEXT, reviewed_at TEXT, supersedes_finding_id TEXT REFERENCES case_finding(finding_id)
);
CREATE TABLE IF NOT EXISTS case_recommendation (
 recommendation_id TEXT PRIMARY KEY, finding_id TEXT NOT NULL REFERENCES case_finding(finding_id),
 recommendation_type TEXT NOT NULL, recommendation_version TEXT NOT NULL,
 recommendation_ciphertext BLOB NOT NULL, alternatives_json TEXT NOT NULL,
 expected_benefit_json TEXT NOT NULL, risks_json TEXT NOT NULL, owner_token TEXT NOT NULL,
 priority TEXT NOT NULL, status TEXT NOT NULL, professional_approved_by_token TEXT,
 approved_at TEXT
);
CREATE TABLE IF NOT EXISTS remediation_action (
 action_id TEXT PRIMARY KEY, recommendation_id TEXT NOT NULL REFERENCES case_recommendation(recommendation_id),
 action_text_ciphertext BLOB NOT NULL, assigned_actor_type TEXT NOT NULL,
 assigned_to_token TEXT NOT NULL, reviewer_token TEXT NOT NULL, due_at TEXT,
 client_approval_required INTEGER NOT NULL DEFAULT 0, client_decision_id TEXT,
 status TEXT NOT NULL, completion_evidence_json TEXT NOT NULL DEFAULT '[]', completed_at TEXT
);
CREATE TABLE IF NOT EXISTS action_dependency (
 action_id TEXT NOT NULL REFERENCES remediation_action(action_id),
 depends_on_action_id TEXT NOT NULL REFERENCES remediation_action(action_id),
 dependency_type TEXT NOT NULL, PRIMARY KEY(action_id,depends_on_action_id)
);
CREATE TABLE IF NOT EXISTS case_resolution (
 resolution_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 resolution_type TEXT NOT NULL, action_ids_json TEXT NOT NULL,
 taxpayer_position_json TEXT NOT NULL DEFAULT '{}', authority_position_json TEXT NOT NULL DEFAULT '{}',
 final_position_json TEXT NOT NULL, evidence_json TEXT NOT NULL,
 verified_by_token TEXT NOT NULL, verified_at TEXT NOT NULL, status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS verified_outcome (
 outcome_id TEXT PRIMARY KEY, resolution_id TEXT NOT NULL REFERENCES case_resolution(resolution_id),
 outcome_type TEXT NOT NULL, baseline_json TEXT NOT NULL, method_json TEXT NOT NULL,
 evidence_json TEXT NOT NULL, attribution_json TEXT NOT NULL,
 financial_value_ciphertext BLOB, operational_value_json TEXT NOT NULL DEFAULT '{}',
 double_counting_reviewed INTEGER NOT NULL DEFAULT 0, remaining_risk_json TEXT NOT NULL DEFAULT '{}',
 verified_by_token TEXT NOT NULL, verified_at TEXT NOT NULL,
 client_release_status TEXT NOT NULL DEFAULT 'hold'
);
CREATE TABLE IF NOT EXISTS case_deliverable (
 deliverable_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 deliverable_type TEXT NOT NULL, audience TEXT NOT NULL,
 deliverable_version TEXT NOT NULL, storage_object_token TEXT NOT NULL,
 content_hash TEXT NOT NULL, contains_privileged_material INTEGER NOT NULL DEFAULT 0,
 professional_approved_by_token TEXT, external_action_approval_id TEXT REFERENCES external_action_approval(approval_id),
 release_status TEXT NOT NULL, released_at TEXT
);
CREATE TABLE IF NOT EXISTS outcome_follow_up (
 follow_up_id TEXT PRIMARY KEY, outcome_id TEXT NOT NULL REFERENCES verified_outcome(outcome_id),
 follow_up_type TEXT NOT NULL, responsible_staff_token TEXT NOT NULL,
 due_at TEXT NOT NULL, status TEXT NOT NULL, result_json TEXT NOT NULL DEFAULT '{}'
);
CREATE TABLE IF NOT EXISTS outcome_learning_candidate (
 learning_candidate_id TEXT PRIMARY KEY, outcome_id TEXT NOT NULL REFERENCES verified_outcome(outcome_id),
 candidate_type TEXT NOT NULL, observation_json TEXT NOT NULL,
 cross_client_use_authorized INTEGER NOT NULL DEFAULT 0,
 technical_review_status TEXT NOT NULL DEFAULT 'pending', legal_review_status TEXT NOT NULL DEFAULT 'pending',
 validation_status TEXT NOT NULL DEFAULT 'pending', production_status TEXT NOT NULL DEFAULT 'not_promotable'
);
CREATE INDEX IF NOT EXISTS idx_case_finding_matter ON case_finding(matter_id,status);
CREATE INDEX IF NOT EXISTS idx_remediation_action_status ON remediation_action(status,due_at);
CREATE INDEX IF NOT EXISTS idx_verified_outcome_resolution ON verified_outcome(resolution_id,client_release_status);


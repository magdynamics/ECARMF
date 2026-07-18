PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS capability_registry (
 capability_id TEXT PRIMARY KEY, name TEXT NOT NULL, scope_json TEXT NOT NULL,
 maturity_stage TEXT NOT NULL, owner_token TEXT, reviewer_token TEXT,
 test_suite_id TEXT, rollback_plan_id TEXT, monitoring_plan_id TEXT,
 last_reviewed_at TEXT, next_review_at TEXT
);
CREATE TABLE IF NOT EXISTS organizational_event (
 event_id TEXT PRIMARY KEY, event_type TEXT NOT NULL, object_type TEXT NOT NULL,
 object_id TEXT NOT NULL, actor_token TEXT NOT NULL, occurred_at TEXT NOT NULL,
 prior_hash TEXT, resulting_hash TEXT, payload_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS decision_memory (
 decision_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL, facts_snapshot_hash TEXT NOT NULL,
 source_versions_json TEXT NOT NULL, recommendation_json TEXT NOT NULL,
 alternatives_json TEXT NOT NULL, professional_decision_json TEXT NOT NULL,
 decision_maker_token TEXT NOT NULL, decision_date TEXT NOT NULL,
 outcome_id TEXT, reuse_restriction TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS learning_candidate (
 candidate_id TEXT PRIMARY KEY, candidate_type TEXT NOT NULL, evidence_json TEXT NOT NULL,
 sample_size INTEGER NOT NULL, validation_status TEXT NOT NULL, approval_id TEXT,
 pilot_status TEXT NOT NULL, rollback_plan_id TEXT, promotion_status TEXT NOT NULL,
 created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS value_outcome (
 outcome_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL, baseline_json TEXT NOT NULL,
 action_json TEXT NOT NULL, result_json TEXT NOT NULL, attribution_method TEXT NOT NULL,
 reviewer_token TEXT, approved_at TEXT
);
CREATE TABLE IF NOT EXISTS source_change_impact (
 impact_id TEXT PRIMARY KEY, source_id TEXT NOT NULL, source_version_from TEXT,
 source_version_to TEXT NOT NULL, affected_objects_json TEXT NOT NULL,
 review_status TEXT NOT NULL, technical_owner_token TEXT
);
CREATE TABLE IF NOT EXISTS agent_contract (
 agent_id TEXT PRIMARY KEY, allowed_inputs_json TEXT NOT NULL, allowed_actions_json TEXT NOT NULL,
 prohibited_actions_json TEXT NOT NULL, required_sources_json TEXT NOT NULL,
 confidence_policy_json TEXT NOT NULL, escalation_policy_json TEXT NOT NULL,
 human_approver_token TEXT NOT NULL, status TEXT NOT NULL
);

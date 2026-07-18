PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS controversy_matter (
  matter_id TEXT PRIMARY KEY, client_token TEXT NOT NULL, engagement_type TEXT NOT NULL,
  authorization_status TEXT NOT NULL, privilege_status TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS notice_control (
  notice_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
  notice_type TEXT NOT NULL, notice_date TEXT, response_due_date TEXT,
  deadline_source TEXT NOT NULL, verified_by_token TEXT, verification_status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS idr_workpaper (
  request_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
  issue_id TEXT NOT NULL, objective TEXT NOT NULL, request_json TEXT NOT NULL,
  privilege_status TEXT NOT NULL, production_status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS issue_workpaper (
  workpaper_id TEXT PRIMARY KEY, matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
  issue_id TEXT NOT NULL, facts_json TEXT NOT NULL, authority_json TEXT NOT NULL,
  evidence_json TEXT NOT NULL, conclusion_status TEXT NOT NULL,
  prepared_by_token TEXT, reviewed_by_token TEXT
);

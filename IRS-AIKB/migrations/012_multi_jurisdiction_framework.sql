PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS jurisdiction_module (
 module_id TEXT PRIMARY KEY, jurisdiction_code TEXT NOT NULL,
 module_name TEXT NOT NULL, authority_name TEXT NOT NULL,
 module_status TEXT NOT NULL, knowledge_status TEXT NOT NULL,
 accountable_owner_token TEXT, activated_at TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS jurisdiction_tax_type (
 tax_type_id TEXT PRIMARY KEY, module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 tax_type_code TEXT NOT NULL, label TEXT NOT NULL, module_status TEXT NOT NULL,
 UNIQUE(module_id, tax_type_code)
);
CREATE TABLE IF NOT EXISTS jurisdiction_source_link (
 module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 source_id TEXT NOT NULL REFERENCES source(source_id), applicability_status TEXT NOT NULL,
 mapping_rationale TEXT, reviewed_by_token TEXT, reviewed_at TEXT,
 PRIMARY KEY(module_id, source_id)
);
CREATE TABLE IF NOT EXISTS jurisdiction_rule_link (
 module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 issue_id TEXT NOT NULL REFERENCES issue_rule_definition(issue_id),
 tax_type_code TEXT NOT NULL, applicability_status TEXT NOT NULL,
 effective_from TEXT, effective_to TEXT, approval_id TEXT,
 PRIMARY KEY(module_id, issue_id, tax_type_code)
);
CREATE TABLE IF NOT EXISTS matter_jurisdiction (
 matter_id TEXT NOT NULL REFERENCES controversy_matter(matter_id),
 module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 tax_type_code TEXT NOT NULL, period_start TEXT, period_end TEXT,
 matter_stage TEXT NOT NULL, analysis_status TEXT NOT NULL DEFAULT 'intake_only',
 PRIMARY KEY(matter_id, module_id, tax_type_code)
);
CREATE TABLE IF NOT EXISTS cross_jurisdiction_mapping (
 mapping_id TEXT PRIMARY KEY, left_module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 right_module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 left_concept_id TEXT NOT NULL, right_concept_id TEXT NOT NULL,
 relationship_type TEXT NOT NULL, mapping_status TEXT NOT NULL,
 authority_json TEXT NOT NULL DEFAULT '[]', reviewed_by_token TEXT, reviewed_at TEXT
);
CREATE TABLE IF NOT EXISTS jurisdiction_notice_type (
 notice_type_id TEXT PRIMARY KEY, module_id TEXT NOT NULL REFERENCES jurisdiction_module(module_id),
 tax_type_code TEXT NOT NULL, notice_code TEXT NOT NULL, label TEXT NOT NULL,
 rights_status TEXT NOT NULL DEFAULT 'not_mapped', deadline_rule_status TEXT NOT NULL DEFAULT 'not_mapped',
 UNIQUE(module_id, notice_code)
);

INSERT OR IGNORE INTO jurisdiction_module
(module_id,jurisdiction_code,module_name,authority_name,module_status,knowledge_status)
VALUES
('US-IRS','US','Federal IRS Audit Intelligence Engine','Internal Revenue Service','foundation_active','partially_populated'),
('US-IL-IDOR','US-IL','Illinois IDOR Audit Intelligence Engine','Illinois Department of Revenue','reserved_placeholder','not_populated');

INSERT OR IGNORE INTO jurisdiction_tax_type
(tax_type_id,module_id,tax_type_code,label,module_status)
VALUES
('IL-INCOME','US-IL-IDOR','income','Illinois income tax','reserved_placeholder'),
('IL-SALES-USE','US-IL-IDOR','sales_use','Illinois sales and use tax','reserved_placeholder'),
('IL-WITHHOLDING','US-IL-IDOR','withholding','Illinois withholding tax','reserved_placeholder'),
('IL-SPECIALTY','US-IL-IDOR','specialty','Illinois specialty taxes','reserved_placeholder'),
('IRS-INCOME','US-IRS','federal_income','Federal income tax','foundation_active'),
('IRS-EMPLOYMENT','US-IRS','employment','Federal employment tax','planned'),
('IRS-EO','US-IRS','exempt_organization','Federal exempt organization tax','planned');


PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS xml_ingestion (
  ingestion_id TEXT PRIMARY KEY, file_sha256 TEXT NOT NULL, schema_version TEXT,
  form_family TEXT, tax_year INTEGER, security_mode TEXT NOT NULL,
  recognition_status TEXT NOT NULL, result_json TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS benchmark_cell (
  benchmark_id TEXT PRIMARY KEY, feature_name TEXT NOT NULL, return_family TEXT NOT NULL,
  tax_year INTEGER NOT NULL, naics_group TEXT, receipt_band TEXT, asset_band TEXT,
  sample_size INTEGER NOT NULL, source_version TEXT NOT NULL, statistics_json TEXT NOT NULL,
  review_status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS production_pipeline_run (
  run_id TEXT PRIMARY KEY, pipeline_version TEXT NOT NULL, input_hash TEXT NOT NULL,
  case_count INTEGER NOT NULL, result_json TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

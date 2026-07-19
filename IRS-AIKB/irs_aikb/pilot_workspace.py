"""Local-only pilot client and evidence workspace.

This is deliberately a pilot boundary: uploads are quarantined and cannot be
analyzed or released until production security and malware scanning exist.
"""
from __future__ import annotations

import base64
import hashlib
from pathlib import Path
import re
import sqlite3
import uuid

MAX_PILOT_FILE_BYTES = 25_000_000
ALLOWED_SUFFIXES = {".pdf", ".xml", ".csv", ".xlsx", ".xls", ".txt", ".zip"}


def initialize_pilot(database: Path) -> None:
    database.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(database)
    try:
        connection.executescript("""
        CREATE TABLE IF NOT EXISTS pilot_client(
          client_id TEXT PRIMARY KEY, client_name TEXT NOT NULL,
          client_type TEXT NOT NULL, primary_contact TEXT,
          email TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS pilot_case(
          case_id TEXT PRIMARY KEY, client_id TEXT NOT NULL REFERENCES pilot_client(client_id),
          jurisdiction TEXT NOT NULL, engagement TEXT NOT NULL,
          return_type TEXT, tax_year INTEGER,
          status TEXT NOT NULL DEFAULT 'pilot_intake',
          created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS pilot_document(
          document_id TEXT PRIMARY KEY, case_id TEXT NOT NULL REFERENCES pilot_case(case_id),
          original_name TEXT NOT NULL, sha256 TEXT NOT NULL, byte_count INTEGER NOT NULL,
          local_path TEXT NOT NULL, status TEXT NOT NULL,
          duplicate_of TEXT, uploaded_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        """)
        connection.commit()
    finally:
        connection.close()


def create_client_case(database: Path, payload: dict) -> dict:
    name = str(payload.get("client_name", "")).strip()
    if not name:
        raise ValueError("client_name is required")
    year = int(payload.get("tax_year") or 0)
    if year and not 1990 <= year <= 2100:
        raise ValueError("tax_year is invalid")
    initialize_pilot(database)
    client_id = f"PCL-{uuid.uuid4().hex[:10].upper()}"
    case_id = f"PCASE-{uuid.uuid4().hex[:10].upper()}"
    connection = sqlite3.connect(database)
    try:
        connection.execute("INSERT INTO pilot_client VALUES(?,?,?,?,?,CURRENT_TIMESTAMP)",
                           (client_id, name, payload.get("client_type", "business"),
                            payload.get("primary_contact"), payload.get("email")))
        connection.execute("""INSERT INTO pilot_case
            (case_id,client_id,jurisdiction,engagement,return_type,tax_year)
            VALUES(?,?,?,?,?,?)""", (case_id, client_id,
            payload.get("jurisdiction", "Federal IRS"),
            payload.get("engagement", "Examination readiness"),
            payload.get("return_type"), year or None))
        connection.commit()
    finally:
        connection.close()
    return {"client_id": client_id, "case_id": case_id, "status": "pilot_intake",
            "client_name": name}


def list_cases(database: Path) -> list[dict]:
    initialize_pilot(database)
    connection = sqlite3.connect(database)
    try:
        connection.row_factory = sqlite3.Row
        rows = connection.execute("""SELECT c.client_name,c.client_id,m.case_id,
            m.jurisdiction,m.engagement,m.return_type,m.tax_year,m.status,m.created_at
            FROM pilot_case m JOIN pilot_client c ON c.client_id=m.client_id
            ORDER BY m.created_at DESC""").fetchall()
    finally:
        connection.close()
    return [dict(row) for row in rows]


def quarantine_upload(database: Path, vault: Path, payload: dict) -> dict:
    initialize_pilot(database)
    case_id = str(payload.get("case_id", "")).strip()
    original_name = Path(str(payload.get("original_name", ""))).name
    if not case_id or not original_name:
        raise ValueError("case_id and original_name are required")
    suffix = Path(original_name).suffix.lower()
    if suffix not in ALLOWED_SUFFIXES:
        raise ValueError("file type is not allowed")
    try:
        raw = base64.b64decode(payload.get("content_base64", ""), validate=True)
    except Exception as error:
        raise ValueError("file content is not valid base64") from error
    if not raw or len(raw) > MAX_PILOT_FILE_BYTES:
        raise ValueError("file is empty or exceeds the 25 MB pilot limit")
    digest = hashlib.sha256(raw).hexdigest()
    connection = sqlite3.connect(database)
    try:
        if not connection.execute("SELECT 1 FROM pilot_case WHERE case_id=?", (case_id,)).fetchone():
            raise ValueError("pilot case does not exist")
        duplicate = connection.execute(
            "SELECT document_id FROM pilot_document WHERE case_id=? AND sha256=?",
            (case_id, digest)).fetchone()
        document_id = f"PDOC-{uuid.uuid4().hex[:12].upper()}"
        safe_stem = re.sub(r"[^A-Za-z0-9._-]+", "_", Path(original_name).stem)[:80] or "file"
        case_vault = vault / case_id / "quarantine"
        case_vault.mkdir(parents=True, exist_ok=True)
        target = case_vault / f"{document_id}_{safe_stem}{suffix}"
        target.write_bytes(raw)
        status = "duplicate_quarantined" if duplicate else "awaiting_malware_scan"
        connection.execute("""INSERT INTO pilot_document
            (document_id,case_id,original_name,sha256,byte_count,local_path,status,duplicate_of)
            VALUES(?,?,?,?,?,?,?,?)""", (document_id, case_id, original_name, digest,
            len(raw), str(target), status, duplicate[0] if duplicate else None))
        connection.commit()
    finally:
        connection.close()
    return {"document_id": document_id, "case_id": case_id, "original_name": original_name,
            "sha256": digest, "byte_count": len(raw), "status": status,
            "duplicate_of": duplicate[0] if duplicate else None,
            "analysis_allowed": False, "human_review_required": True}

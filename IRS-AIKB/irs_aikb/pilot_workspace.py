"""Local-only pilot client and evidence workspace.

This is deliberately a pilot boundary: uploads are quarantined and cannot be
analyzed or released until production security and malware scanning exist.
"""
from __future__ import annotations

import base64
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import uuid

from .intake import inspect_pdf

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
        CREATE TABLE IF NOT EXISTS pilot_document_derivative(
          derivative_id TEXT PRIMARY KEY,
          document_id TEXT NOT NULL REFERENCES pilot_document(document_id),
          derivative_kind TEXT NOT NULL, local_path TEXT NOT NULL,
          sha256 TEXT NOT NULL, byte_count INTEGER NOT NULL,
          page_count INTEGER, tool_name TEXT NOT NULL, tool_version TEXT,
          configuration TEXT NOT NULL, quality_status TEXT NOT NULL,
          created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
        CREATE TABLE IF NOT EXISTS pilot_analysis_job(
          job_id TEXT PRIMARY KEY, case_id TEXT NOT NULL REFERENCES pilot_case(case_id),
          requested_by TEXT NOT NULL, status TEXT NOT NULL, current_stage TEXT NOT NULL,
          result_json TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
          updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
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


def case_documents(database: Path, case_id: str) -> list[dict]:
    initialize_pilot(database)
    connection=sqlite3.connect(database)
    try:
        connection.row_factory=sqlite3.Row
        rows=connection.execute("""SELECT document_id,case_id,original_name,sha256,
            byte_count,status,duplicate_of,uploaded_at FROM pilot_document
            WHERE case_id=? ORDER BY uploaded_at""",(case_id,)).fetchall()
    finally:
        connection.close()
    return [dict(row) for row in rows]


def record_document_derivative(database: Path, payload: dict) -> dict:
    """Register a non-authoritative working copy without replacing evidence."""
    required=("document_id","derivative_kind","local_path","sha256","byte_count",
              "tool_name","configuration","quality_status")
    missing=[field for field in required if payload.get(field) in (None,"")]
    if missing:
        raise ValueError(f"missing derivative fields: {', '.join(missing)}")
    initialize_pilot(database)
    derivative_id=f"PDER-{uuid.uuid4().hex[:12].upper()}"
    connection=sqlite3.connect(database)
    try:
        if not connection.execute("SELECT 1 FROM pilot_document WHERE document_id=?",
                                  (payload["document_id"],)).fetchone():
            raise ValueError("pilot document does not exist")
        connection.execute("""INSERT INTO pilot_document_derivative
            (derivative_id,document_id,derivative_kind,local_path,sha256,byte_count,
             page_count,tool_name,tool_version,configuration,quality_status)
            VALUES(?,?,?,?,?,?,?,?,?,?,?)""",(derivative_id,payload["document_id"],
            payload["derivative_kind"],str(payload["local_path"]),payload["sha256"],
            int(payload["byte_count"]),payload.get("page_count"),payload["tool_name"],
            payload.get("tool_version"),payload["configuration"],payload["quality_status"]))
        connection.commit()
    finally:
        connection.close()
    return {"derivative_id":derivative_id,**payload}


def case_review_status(database: Path, case_id: str) -> dict:
    """Return user-facing document and AI-review progress for one pilot case."""
    initialize_pilot(database)
    connection=sqlite3.connect(database)
    connection.row_factory=sqlite3.Row
    try:
        documents=connection.execute("""SELECT d.document_id,d.original_name,d.status,d.byte_count,
            COUNT(x.derivative_id) derivative_count,
            SUM(CASE WHEN x.quality_status='review_ready' THEN 1 ELSE 0 END) review_ready_count
            FROM pilot_document d LEFT JOIN pilot_document_derivative x
              ON x.document_id=d.document_id
            WHERE d.case_id=? GROUP BY d.document_id ORDER BY d.uploaded_at""",(case_id,)).fetchall()
        job=connection.execute("""SELECT job_id,status,current_stage,result_json,created_at,updated_at
            FROM pilot_analysis_job WHERE case_id=? ORDER BY created_at DESC LIMIT 1""",
            (case_id,)).fetchone()
    finally:
        connection.close()
    document_rows=[dict(row) for row in documents]
    latest=None
    if job:
        latest=dict(job); latest["result"]=json.loads(latest.pop("result_json"))
    active_job=bool(latest and latest["status"] in {"queued","running","awaiting_staff_verification"})
    return {"case_id":case_id,"documents":document_rows,"latest_job":latest,
            "can_start_ai_review":bool(document_rows) and
                all(row["review_ready_count"] for row in document_rows) and not active_job,
            "stages":["Original preserved","Security scan","OCR and searchability",
                "Page classification","Value extraction","Staff verification",
                "Reconciliation","Ratio and audit-technique review","Risk report"]}


def start_case_ai_review(database: Path, case_id: str, requested_by: str="pilot_user") -> dict:
    """Start bounded review from approved OCR derivatives; no final conclusion is issued."""
    status=case_review_status(database,case_id)
    if not status["documents"]:
        raise ValueError("case has no documents")
    if not status["can_start_ai_review"]:
        raise ValueError("review-ready OCR derivatives are required before AI review")
    initialize_pilot(database)
    connection=sqlite3.connect(database)
    try:
        rows=connection.execute("""SELECT d.document_id,d.original_name,x.local_path,x.page_count
            FROM pilot_document d JOIN pilot_document_derivative x ON x.document_id=d.document_id
            WHERE d.case_id=? AND x.quality_status='review_ready'
            ORDER BY x.created_at DESC""",(case_id,)).fetchall()
        seen=set(); classified=[]
        for document_id,name,local_path,pages in rows:
            if document_id in seen: continue
            seen.add(document_id)
            pdf=Path(local_path)
            sidecar=pdf.with_name(pdf.name.replace("_searchable.pdf",".txt"))
            text=sidecar.read_text(encoding="utf-8",errors="ignore") if sidecar.exists() else ""
            jurisdictions=[]
            if "1120-S" in text or "8879-CORP" in text: jurisdictions.append("Federal IRS")
            if "IL-1120-ST" in text or "Illinois Department of Revenue" in text: jurisdictions.append("Illinois IDOR")
            classified.append({"document_id":document_id,"original_name":name,"pages":pages,
                "jurisdictions":jurisdictions or ["Requires staff classification"],
                "classification_status":"staff_confirmation_required"})
        result={"classified_documents":classified,"completed_stages":[
            "Original preserved","Security scan","OCR and searchability","Page classification"],
            "current_stage":"Value extraction and staff verification",
            "next_actions":["Confirm page-level federal and Illinois boundaries",
                "Review extracted form and tax-year labels","Verify material return-line values",
                "Approve reconciliation inputs before ratio or risk analysis"],
            "limitations":["No audit probability or final tax conclusion has been issued",
                "Illinois scoring is blocked until the IDOR knowledge base is approved"]}
        job_id=f"PJOB-{uuid.uuid4().hex[:12].upper()}"
        connection.execute("""INSERT INTO pilot_analysis_job
            (job_id,case_id,requested_by,status,current_stage,result_json)
            VALUES(?,?,?,?,?,?)""",(job_id,case_id,requested_by,"awaiting_staff_verification",
            result["current_stage"],json.dumps(result)))
        connection.commit()
    finally:
        connection.close()
    return {"job_id":job_id,"case_id":case_id,"status":"awaiting_staff_verification",**result}


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


def inspect_case_documents(database: Path, case_id: str, *, scan_passed: bool = False) -> dict:
    """Identify locally quarantined PDFs after an explicit external scan gate."""
    if not scan_passed:
        return {"case_id": case_id, "status": "blocked",
                "blockers": ["malware_scan_not_confirmed"], "documents": []}
    initialize_pilot(database)
    connection = sqlite3.connect(database)
    try:
        rows = connection.execute("""SELECT document_id,original_name,local_path,status
            FROM pilot_document WHERE case_id=? ORDER BY uploaded_at""", (case_id,)).fetchall()
        documents=[]
        for document_id, original_name, local_path, prior_status in rows:
            path=Path(local_path)
            if path.suffix.lower() != ".pdf":
                documents.append({"document_id":document_id,"original_name":original_name,
                    "status":"unsupported_for_return_identification"})
                continue
            record=inspect_pdf(path,include_values=False)
            status="identified_pending_value_extraction" if record.status=="recognized" else "identification_review_required"
            connection.execute("UPDATE pilot_document SET status=? WHERE document_id=?",(status,document_id))
            documents.append({"document_id":document_id,"original_name":original_name,
                "page_count":record.page_count,"form_family":record.form_family,
                "tax_year":record.tax_year,"recognition_confidence":record.recognition_confidence,
                "status":status,"warnings":list(record.warnings),"prior_status":prior_status})
        connection.commit()
    finally:
        connection.close()
    return {"case_id":case_id,"status":"document_review_ready","document_count":len(documents),
            "documents":documents,"next_steps":["confirm taxpayer identity and return relationship",
                "extract and review canonical return values","reconcile schedules and related returns",
                "select SOI cohort by form, year, NAICS and size","calculate ratios and run governed risk procedures"]}

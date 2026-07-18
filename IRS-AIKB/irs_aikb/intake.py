"""Secure-by-default inventory and recognition of tax-return PDF files."""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

from pypdf import PdfReader


SUPPORTED_FORMS = (
    "1040-NR", "1040-SR", "1040-X", "1040", "1041", "1065-X", "1065",
    "1120-F", "1120-S", "1120-X", "1120", "990-EZ", "990-PF", "990-T", "990",
)

FORM_PATTERNS = (
    ("1040-NR", re.compile(r"Form\s*1040-NR\b", re.I)),
    ("1040-SR", re.compile(r"Form\s*1040-SR\b", re.I)),
    ("1040-X", re.compile(r"Form\s*1040-X\b", re.I)),
    ("1065-X", re.compile(r"Form\s*1065-X\b", re.I)),
    ("1120-F", re.compile(r"Form\s*1120-F\b", re.I)),
    ("1120-S", re.compile(r"Form\s*1120-S\b", re.I)),
    ("1120-X", re.compile(r"Form\s*1120-X\b", re.I)),
    ("990-EZ", re.compile(r"Form\s*990-EZ\b", re.I)),
    ("990-PF", re.compile(r"Form\s*990-PF\b", re.I)),
    ("990-T", re.compile(r"Form\s*990-T\b", re.I)),
    ("1040", re.compile(r"Form\s*1040\b", re.I)),
    ("1041", re.compile(r"Form\s*1041\b", re.I)),
    ("1065", re.compile(r"Form\s*1065\b", re.I)),
    ("1120", re.compile(r"Form\s*1120\b", re.I)),
    ("990", re.compile(r"Form\s*990\b", re.I)),
)

YEAR_PATTERNS = (
    re.compile(r"For\s+(?:calendar\s+)?year[^\n]{0,40}?\b(20\d{2})\b", re.I),
    re.compile(r"\b(20\d{2})\b\s+U\.S\.", re.I),
    re.compile(r"(?:Form\s*[\w-]+\s+)(20\d{2})\b", re.I),
    re.compile(r"\b(20\d{2})\b"),
)


@dataclass(frozen=True)
class IntakeRecord:
    file_id: str
    path: str
    sha256: str
    bytes: int
    page_count: int | None
    form_family: str | None
    tax_year: int | None
    recognition_confidence: str
    extraction_method: str
    acroform_field_count: int
    populated_field_count: int
    duplicate_of: str | None
    status: str
    warnings: tuple[str, ...]
    field_values: dict[str, Any] | None = None


def identify_form(text: str) -> tuple[str | None, str]:
    """Identify a principal return family from extracted first-page text."""
    normalized = " ".join(text.replace("\x00", " ").split())
    matches = [form for form, pattern in FORM_PATTERNS if pattern.search(normalized)]
    if not matches:
        return None, "none"
    return matches[0], "high" if len(matches) == 1 else "medium"


def identify_tax_year(text: str) -> tuple[int | None, str]:
    """Identify a plausible return year; reject unrelated old/new years."""
    for index, pattern in enumerate(YEAR_PATTERNS):
        match = pattern.search(text)
        if match:
            year = int(match.group(1))
            if 1990 <= year <= 2100:
                return year, "high" if index < 3 else "low"
    return None, "none"


def _safe_field_value(value: Any) -> Any:
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    return str(value)


def inspect_pdf(path: Path, *, include_values: bool = False) -> IntakeRecord:
    """Hash, open, recognize, and inventory one PDF without altering it."""
    raw = path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    warnings: list[str] = []
    try:
        reader = PdfReader(str(path))
        page_count = len(reader.pages)
        text = "\n".join((page.extract_text() or "") for page in reader.pages[:2])
        form_family, form_confidence = identify_form(text)
        tax_year, year_confidence = identify_tax_year(text)
        fields = reader.get_fields() or {}
        values = {
            name: _safe_field_value(field.get("/V"))
            for name, field in fields.items()
            if isinstance(field, dict) and field.get("/V") not in (None, "", "/Off")
        }
        if not text.strip():
            warnings.append("no_extractable_text_ocr_required")
        if not form_family:
            warnings.append("unsupported_or_unrecognized_form")
        if tax_year is None:
            warnings.append("tax_year_not_recognized")
        if not fields:
            warnings.append("no_acroform_fields_flattened_or_scanned_pdf")
        confidence = (
            "high" if form_confidence == "high" and year_confidence == "high"
            else "medium" if form_family and tax_year else "low"
        )
        status = "recognized" if form_family and tax_year else "review_required"
        return IntakeRecord(
            file_id=f"FILE-{digest[:16]}", path=str(path), sha256=digest,
            bytes=len(raw), page_count=page_count, form_family=form_family,
            tax_year=tax_year, recognition_confidence=confidence,
            extraction_method="pdf_text_and_acroform", acroform_field_count=len(fields),
            populated_field_count=len(values), duplicate_of=None, status=status,
            warnings=tuple(warnings), field_values=values if include_values else None,
        )
    except Exception as exc:  # malformed/encrypted PDFs become review records
        return IntakeRecord(
            file_id=f"FILE-{digest[:16]}", path=str(path), sha256=digest,
            bytes=len(raw), page_count=None, form_family=None, tax_year=None,
            recognition_confidence="none", extraction_method="failed",
            acroform_field_count=0, populated_field_count=0, duplicate_of=None,
            status="quarantined", warnings=(f"pdf_read_error:{type(exc).__name__}",),
            field_values=None,
        )


def intake_paths(
    paths: Iterable[Path], *, include_values: bool = False
) -> dict[str, Any]:
    """Inventory a collection, identify duplicates, and return a safe report."""
    files: list[Path] = []
    for supplied in paths:
        path = supplied.resolve()
        if path.is_dir():
            files.extend(sorted(p for p in path.rglob("*.pdf") if p.is_file()))
        elif path.is_file() and path.suffix.lower() == ".pdf":
            files.append(path)
    unique_paths = sorted(set(files), key=lambda item: str(item).lower())
    records: list[IntakeRecord] = []
    first_by_hash: dict[str, str] = {}
    for path in unique_paths:
        record = inspect_pdf(path, include_values=include_values)
        duplicate_of = first_by_hash.get(record.sha256)
        if duplicate_of:
            record = IntakeRecord(**{**asdict(record), "duplicate_of": duplicate_of})
        else:
            first_by_hash[record.sha256] = record.file_id
        records.append(record)

    return {
        "intake_version": "0.1.0",
        "security_mode": "values_included" if include_values else "metadata_only",
        "file_count": len(records),
        "recognized_count": sum(record.status == "recognized" for record in records),
        "review_required_count": sum(record.status == "review_required" for record in records),
        "quarantined_count": sum(record.status == "quarantined" for record in records),
        "duplicate_count": sum(record.duplicate_of is not None for record in records),
        "records": [asdict(record) for record in records],
        "limitations": [
            "Metadata mode does not emit taxpayer-entered field values.",
            "Flattened or scanned PDFs require a separately controlled OCR workflow.",
            "Recognition is not line-item validation and requires human review.",
            "Potentially privileged files require access isolation and counsel review.",
        ],
    }


def write_intake_report(report: dict[str, Any], destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")

"""Secure, lineage-preserving adapter for authorized e-file XML exports."""

from __future__ import annotations
import hashlib
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

MAX_XML_BYTES = 50_000_000
FORM_MARKERS = {"IRS1040": "1040", "IRS1041": "1041", "IRS1065": "1065",
                "IRS1120": "1120", "IRS1120S": "1120-S", "IRS990": "990"}
TAG_MAPPINGS = {
    "GrossReceiptsOrSalesAmt": "income.gross_receipts",
    "ReturnsAndAllowancesAmt": "income.returns_allowances",
    "CostOfGoodsSoldAmt": "cogs.total", "GrossProfitAmt": "income.gross_profit",
    "TotalIncomeAmt": "income.total", "TotalDeductionsAmt": "expense.total",
    "TaxableIncomeAmt": "taxable_income.total", "TotalTaxAmt": "tax.total",
    "CashAmt": "balance.cash", "TotalAssetsAmt": "balance.total_assets",
    "TotalLiabilitiesAmt": "balance.total_liabilities",
}

def _local(tag: str) -> str: return tag.rsplit("}", 1)[-1]

def ingest_efile_xml(path: Path, *, include_values: bool = False) -> dict[str, Any]:
    raw = path.read_bytes()
    if len(raw) > MAX_XML_BYTES: raise ValueError("XML exceeds controlled intake size")
    head = raw[:4096].upper()
    if b"<!DOCTYPE" in head or b"<!ENTITY" in head: raise ValueError("DTD and entities are prohibited")
    root = ET.fromstring(raw)
    tags = [_local(node.tag) for node in root.iter()]
    families = [family for marker, family in FORM_MARKERS.items() if marker in tags]
    year = None
    for node in root.iter():
        if _local(node.tag) in {"TaxYr", "TaxYear", "TaxPeriodEndDt"} and node.text:
            match = re.search(r"(20\d{2})", node.text)
            if match: year = int(match.group(1)); break
    facts, unmapped = [], set()
    for node in root.iter():
        tag, text = _local(node.tag), (node.text or "").strip()
        if not text: continue
        concept = TAG_MAPPINGS.get(tag)
        if concept:
            fact = {"concept_id": concept, "source_tag": tag,
                    "source_path_status": "tag_only_requires_schema_version",
                    "extraction_method": "authorized_efile_xml", "validation_status": "extracted_unvalidated"}
            if include_values: fact["value"] = text
            facts.append(fact)
        elif tag.endswith(("Amt", "Pct", "Cnt")): unmapped.add(tag)
    return {"file_id": f"XML-{hashlib.sha256(raw).hexdigest()[:16]}",
            "sha256": hashlib.sha256(raw).hexdigest(), "byte_count": len(raw),
            "form_family": families[0] if len(families) == 1 else None, "tax_year": year,
            "recognition_status": "recognized" if len(families) == 1 and year else "review_required",
            "security_mode": "values_included" if include_values else "metadata_only",
            "mapped_fact_count": len(facts), "facts": facts,
            "unmapped_numeric_tags": sorted(unmapped),
            "limitations": ["Tag mappings require validation against the exact IRS MeF schema version.",
                            "Attachments and binary objects require separate controlled processing."]}

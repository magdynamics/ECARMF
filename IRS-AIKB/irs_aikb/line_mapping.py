"""Versioned, exact-match mappings from IRS return lines to canonical concepts."""

from __future__ import annotations

from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

from pypdf import PdfReader

from .canonical import CANONICAL_CONCEPTS

MAPPING_VERSION = "2026.07.1"


@dataclass(frozen=True)
class LineMapping:
    form_family: str
    tax_year: int
    schedule: str
    source_line: str
    source_label: str
    concept_id: str
    mapping_version: str = MAPPING_VERSION
    review_status: str = "verified_against_official_blank"


def _rows(form: str, years: Iterable[int], definitions: Iterable[tuple[str, str, str]]):
    return [LineMapping(form, year, "main", line, label, concept)
            for year in years for line, label, concept in definitions]


BUSINESS_COMMON = (
    ("1a", "Gross receipts or sales", "income.gross_receipts"),
    ("1b", "Returns and allowances", "income.returns_allowances"),
    ("1c", "Balance", "income.net_receipts"),
    ("2", "Cost of goods sold", "cogs.total"),
    ("3", "Gross profit", "income.gross_profit"),
)

MAPPINGS = tuple(
    _rows("1120", range(2018, 2026), BUSINESS_COMMON + (
        ("11", "Total income", "income.total"),
        ("27", "Total deductions", "expense.total"),
        ("30", "Taxable income", "taxable_income.total"),
        ("31", "Total tax", "tax.total"),
    ))
    + _rows("1065", range(2018, 2023), BUSINESS_COMMON + (
        ("8", "Total income", "income.total"), ("21", "Total deductions", "expense.total"),
        ("22", "Ordinary business income", "taxable_income.total"),
    ))
    + _rows("1065", range(2023, 2026), BUSINESS_COMMON + (
        ("8", "Total income", "income.total"), ("22", "Total deductions", "expense.total"),
        ("23", "Ordinary business income", "taxable_income.total"),
    ))
    + _rows("1120-S", range(2018, 2023), BUSINESS_COMMON + (
        ("6", "Total income", "income.total"), ("20", "Total deductions", "expense.total"),
        ("21", "Ordinary business income", "taxable_income.total"),
    ))
    + _rows("1120-S", range(2023, 2026), BUSINESS_COMMON + (
        ("6", "Total income", "income.total"), ("21", "Total deductions", "expense.total"),
        ("22", "Ordinary business income", "taxable_income.total"),
    ))
    + _rows("1041", [2018], (
        ("9", "Total income", "income.total"), ("22", "Taxable income", "taxable_income.total"),
        ("23", "Total tax", "tax.total"),
    ))
    + _rows("1041", range(2019, 2026), (
        ("9", "Total income", "income.total"), ("23", "Taxable income", "taxable_income.total"),
        ("24", "Total tax", "tax.total"),
    ))
    + _rows("1040", [2018], (
        ("1", "Wages", "income.wages"), ("6", "Total income", "income.total"),
        ("7", "Adjusted gross income", "income.adjusted_gross"),
        ("10", "Taxable income", "taxable_income.total"), ("15", "Total tax", "tax.total"),
        ("22", "Refund", "tax.refund"), ("25", "Amount you owe", "tax.amount_owed"),
    ))
    + _rows("1040", [2019], (
        ("1", "Wages", "income.wages"), ("7b", "Total income", "income.total"),
        ("8b", "Adjusted gross income", "income.adjusted_gross"),
        ("11b", "Taxable income", "taxable_income.total"), ("16", "Total tax", "tax.total"),
        ("21a", "Refund", "tax.refund"), ("23", "Amount you owe", "tax.amount_owed"),
    ))
    + _rows("1040", range(2020, 2026), (
        ("1", "Wages", "income.wages"), ("9", "Total income", "income.total"),
        ("11", "Adjusted gross income", "income.adjusted_gross"),
        ("15", "Taxable income", "taxable_income.total"), ("24", "Total tax", "tax.total"),
        ("34", "Refund", "tax.refund"), ("37", "Amount you owe", "tax.amount_owed"),
    ))
)


def mappings_for(form_family: str, tax_year: int, schedule: str = "main") -> list[LineMapping]:
    return [m for m in MAPPINGS if (m.form_family, m.tax_year, m.schedule) ==
            (form_family, tax_year, schedule)]


def map_observations(form_family: str, tax_year: int, observations: Iterable[dict[str, Any]]) -> dict:
    """Map already-extracted lines; labels must agree and ambiguous values are rejected."""
    registry = {m.source_line: m for m in mappings_for(form_family, tax_year)}
    facts, exceptions = [], []
    for item in observations:
        line = str(item.get("source_line", "")).strip()
        mapping = registry.get(line)
        if not mapping:
            exceptions.append({"source_line": line, "reason": "no_reviewed_mapping"})
            continue
        supplied_label = str(item.get("source_label", "")).lower()
        if supplied_label and mapping.source_label.lower() not in supplied_label:
            exceptions.append({"source_line": line, "reason": "label_conflict"})
            continue
        facts.append({**asdict(mapping), "value": item.get("value"),
                      "mapping_confidence": 1.0, "validation_status": "mapped_unvalidated"})
    return {"form_family": form_family, "tax_year": tax_year,
            "mapping_version": MAPPING_VERSION, "facts": facts, "exceptions": exceptions}


def validate_official_forms(root: Path) -> dict:
    """Check every reviewed mapping against downloaded official blank-form text."""
    results = []
    for form in ("1040", "1041", "1065", "1120", "1120-S"):
        for year in range(2018, 2026):
            directory = root / form / str(year)
            candidates = sorted(directory.glob("*form.pdf"))
            if not candidates:
                results.append({"form_family": form, "tax_year": year, "status": "missing_pdf"})
                continue
            text = "\n".join((p.extract_text() or "") for p in PdfReader(str(candidates[0])).pages)
            normalized = " ".join(text.split()).lower()
            missing = [m.source_line for m in mappings_for(form, year)
                       if m.source_label.lower() not in normalized]
            results.append({"form_family": form, "tax_year": year,
                            "mapping_count": len(mappings_for(form, year)),
                            "missing_label_lines": missing,
                            "status": "validated" if not missing else "review_required"})
    return {"mapping_version": MAPPING_VERSION, "forms_checked": len(results),
            "validated": sum(r["status"] == "validated" for r in results), "results": results}


def validate_registry() -> list[str]:
    errors, keys = [], set()
    for mapping in MAPPINGS:
        key = (mapping.form_family, mapping.tax_year, mapping.schedule, mapping.source_line)
        if key in keys:
            errors.append(f"duplicate:{key}")
        keys.add(key)
        if mapping.concept_id not in CANONICAL_CONCEPTS:
            errors.append(f"unknown_concept:{mapping.concept_id}")
    return errors

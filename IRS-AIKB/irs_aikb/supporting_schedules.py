"""Reviewed seed mappings for high-value supporting-schedule lines."""

from __future__ import annotations

from .line_mapping import LineMapping, MAPPING_VERSION
from pathlib import Path
from pypdf import PdfReader


def _schedule_rows(schedule: str, years: range, definitions):
    return tuple(LineMapping("1040", year, schedule, line, label, concept,
                             MAPPING_VERSION, "curated_requires_template_validation")
                 for year in years for line, label, concept in definitions)


# These stable seeds are intentionally limited to high-level control totals. Detailed
# expense lines and Schedule E columns require version/layout-specific extraction.
SUPPORTING_MAPPINGS = (
    _schedule_rows("C", range(2018, 2026), (
        ("1", "Gross receipts or sales", "income.gross_receipts"),
        ("2", "Returns and allowances", "income.returns_allowances"),
        ("3", "Subtract line 2 from line 1", "income.net_receipts"),
        ("4", "Cost of goods sold", "cogs.total"),
        ("5", "Gross profit", "income.gross_profit"),
        ("28", "Total expenses", "activity.total_expenses"),
        ("31", "Net profit or (loss)", "activity.net_profit"),
    ))
    + _schedule_rows("F", range(2018, 2026), (
        ("9", "Gross income", "income.total"),
        ("33", "Total expenses", "activity.total_expenses"),
        ("34", "Net farm profit or (loss)", "activity.net_profit"),
    ))
    + _schedule_rows("E", range(2018, 2026), (
        ("3", "Rents received", "income.rents"),
        ("4", "Royalties received", "income.royalties"),
        ("20", "Total expenses", "activity.total_expenses"),
        ("26", "Total rental real estate and royalty income", "income.rental_real_estate"),
        ("32", "Total partnership and S corporation income", "passthrough.k1_income"),
        ("37", "Total estate and trust income", "passthrough.k1_income"),
        ("41", "Total income or (loss)", "income.total"),
    ))
    + tuple(LineMapping("1065-K1", year, "K-1", line, label, concept,
                        MAPPING_VERSION, "verified_against_official_blank")
            for year in range(2018, 2026) for line, label, concept in (
        ("1", "Ordinary business income", "passthrough.ordinary_income"),
        ("2", "Net rental real estate income", "income.rental_real_estate"),
        ("3", "Other net rental income", "income.other_rental"),
        ("5", "Interest income", "passthrough.interest"),
        ("6a", "Ordinary dividends", "passthrough.dividends"),
        ("8", "Net short-term capital gain", "passthrough.capital_gain"),
        ("9a", "Net long-term capital gain", "passthrough.capital_gain"),
        ("10", "Net section 1231 gain", "passthrough.section1231"),
        ("19", "Distributions", "passthrough.distributions"),
    ))
    + tuple(LineMapping("1120-S-K1", year, "K-1", line, label, concept,
                        MAPPING_VERSION, "verified_against_official_blank")
            for year in range(2018, 2026) for line, label, concept in (
        ("1", "Ordinary business income", "passthrough.ordinary_income"),
        ("2", "Net rental real estate income", "income.rental_real_estate"),
        ("3", "Other net rental income", "income.other_rental"),
        ("4", "Interest income", "passthrough.interest"),
        ("5a", "Ordinary dividends", "passthrough.dividends"),
        ("7", "Net short-term capital gain", "passthrough.capital_gain"),
        ("8a", "Net long-term capital gain", "passthrough.capital_gain"),
        ("9", "Net section 1231 gain", "passthrough.section1231"),
    ))
    + tuple(LineMapping("1041-K1", year, "K-1", line, label, concept,
                        MAPPING_VERSION, "verified_against_official_blank")
            for year in range(2018, 2026) for line, label, concept in (
        ("1", "Interest income", "passthrough.interest"),
        ("2a", "Ordinary dividends", "passthrough.dividends"),
        ("3", "Net short-term capital gain", "passthrough.capital_gain"),
        ("4a", "Net long-term capital gain", "passthrough.capital_gain"),
        ("6", "Ordinary business income", "passthrough.ordinary_income"),
        ("7", "Net rental real estate income", "income.rental_real_estate"),
        ("8", "Other rental income", "income.other_rental"),
    ))
)


def supporting_mappings_for(schedule: str, tax_year: int) -> list[LineMapping]:
    return [m for m in SUPPORTING_MAPPINGS if m.schedule == schedule and m.tax_year == tax_year]


def validate_supporting_forms(root: Path) -> dict:
    products = {
        "C": ("1040-Schedule-C", "f1040sc"), "E": ("1040-Schedule-E", "f1040se"),
        "F": ("1040-Schedule-F", "f1040sf"), "1065-K1": ("1065-Schedule-K1", "f1065sk1"),
        "1120-S-K1": ("1120S-Schedule-K1", "f1120ssk"),
        "1041-K1": ("1041-Schedule-K1", "f1041sk1"),
    }
    results = []
    for key, (folder, stem) in products.items():
        for year in range(2018, 2026):
            path = root / folder / str(year) / f"{stem}--{year}.pdf"
            mappings = ([m for m in SUPPORTING_MAPPINGS if m.form_family == "1040" and
                         m.schedule == key and m.tax_year == year] if key in {"C", "E", "F"}
                        else [m for m in SUPPORTING_MAPPINGS if m.form_family == key and m.tax_year == year])
            text = " ".join(" ".join((p.extract_text() or "").split())
                            for p in PdfReader(str(path)).pages).lower()
            missing = [m.source_line for m in mappings if m.source_label.lower() not in text]
            results.append({"product": key, "tax_year": year, "mapping_count": len(mappings),
                            "missing_label_lines": missing,
                            "status": "validated" if not missing else "review_required"})
    return {"forms_checked": len(results),
            "validated": sum(x["status"] == "validated" for x in results), "results": results}

"""Reviewed seed mappings for high-value supporting-schedule lines."""

from __future__ import annotations

from .line_mapping import LineMapping, MAPPING_VERSION


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
        ("31", "Net profit or loss", "activity.net_profit"),
    ))
    + _schedule_rows("F", range(2018, 2026), (
        ("9", "Gross income", "income.total"),
        ("33", "Total expenses", "activity.total_expenses"),
        ("34", "Net farm profit or loss", "activity.net_profit"),
    ))
)


def supporting_mappings_for(schedule: str, tax_year: int) -> list[LineMapping]:
    return [m for m in SUPPORTING_MAPPINGS if m.schedule == schedule and m.tax_year == tax_year]

"""Return-package validation and cross-return reconciliation controls."""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any

from .schedule_requirements import expected_schedules

ENGINE_VERSION = "0.1.0"


@dataclass(frozen=True)
class ValidationFinding:
    rule_id: str
    category: str
    severity: str
    status: str
    message: str
    expected: float | None = None
    observed: float | None = None
    difference: float | None = None
    related_ids: tuple[str, ...] = ()


def _number(values: dict[str, Any], key: str) -> float | None:
    value = values.get(key)
    if value is None or isinstance(value, bool):
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _close(a: float, b: float, tolerance: float) -> bool:
    # `tolerance` is an absolute-dollar allowance; the tiny relative allowance
    # accommodates floating-point representation without permitting material drift.
    return abs(a - b) <= max(tolerance, 0.000001 * max(abs(a), abs(b)))


def _equation(findings, rule_id, category, values, result_key, component_keys,
              signs=None, tolerance=1.0):
    result = _number(values, result_key)
    components = [_number(values, key) for key in component_keys]
    if result is None or any(value is None for value in components):
        findings.append(ValidationFinding(rule_id, category, "info", "not_assessable",
                                           f"Cannot test {result_key}; required values are missing."))
        return
    signs = signs or [1] * len(components)
    expected = sum(value * sign for value, sign in zip(components, signs))
    difference = result - expected
    passed = _close(result, expected, tolerance)
    findings.append(ValidationFinding(rule_id, category, "none" if passed else "high",
                                       "passed" if passed else "failed",
                                       f"{result_key} {'reconciles' if passed else 'does not reconcile'}.",
                                       expected, result, difference))


def validate_return_package(package: dict[str, Any]) -> dict[str, Any]:
    """Validate normalized returns without estimating missing data."""
    findings: list[ValidationFinding] = []
    returns = package.get("returns", [])
    required = package.get("required_schedules", {})
    supplied = package.get("supplied_schedules", {})

    for item in returns:
        return_id = str(item.get("return_id", "unknown"))
        values = item.get("values", {})
        _equation(findings, f"{return_id}:receipts", "arithmetic", values,
                  "income.net_receipts", ["income.gross_receipts", "income.returns_allowances"],
                  [1, -1])
        _equation(findings, f"{return_id}:gross-profit", "arithmetic", values,
                  "income.gross_profit", ["income.net_receipts", "cogs.total"], [1, -1])
        _equation(findings, f"{return_id}:balance-sheet", "balance_sheet", values,
                  "balance.total_assets", ["balance.total_liabilities", "equity.total"])
        prior = item.get("prior_year_ending", {})
        current = item.get("current_year_beginning", {})
        for concept in sorted(set(prior) | set(current)):
            a, b = _number(prior, concept), _number(current, concept)
            if a is not None and b is not None and not _close(a, b, 1.0):
                findings.append(ValidationFinding(
                    f"{return_id}:rollforward:{concept}", "rollforward", "moderate", "failed",
                    f"Prior ending and current beginning {concept} differ.", a, b, b - a,
                    (return_id,)))

        inferred = {entry["schedule"] for entry in expected_schedules(item)}
        expected = set(required.get(return_id, [])) | inferred
        present = set(supplied.get(return_id, []))
        for schedule in sorted(expected - present):
            findings.append(ValidationFinding(
                f"{return_id}:missing:{schedule}", "completeness", "high", "failed",
                f"Required schedule {schedule} is missing.", related_ids=(return_id,)))

        book = _number(values, "reconciliation.book_income")
        permanent = _number(values, "reconciliation.permanent_differences")
        temporary = _number(values, "reconciliation.temporary_differences")
        tax = _number(values, "reconciliation.tax_income")
        if all(value is not None for value in (book, permanent, temporary, tax)):
            expected_tax = book + permanent + temporary
            if not _close(expected_tax, tax, 1.0):
                findings.append(ValidationFinding(
                    f"{return_id}:book-tax", "book_tax", "high", "failed",
                    "Book-to-tax reconciliation does not foot.", expected_tax, tax,
                    tax - expected_tax, (return_id,)))

    allocations = package.get("allocations", [])
    by_entity: dict[tuple[str, str], list[dict]] = {}
    for allocation in allocations:
        by_entity.setdefault((allocation["entity_return_id"], allocation["concept_id"]), []).append(allocation)
    entity_totals = {(x["entity_return_id"], x["concept_id"]): float(x["value"])
                     for x in package.get("entity_allocation_totals", [])}
    for key, total in entity_totals.items():
        allocated = sum(float(x["value"]) for x in by_entity.get(key, []))
        passed = _close(total, allocated, 1.0)
        findings.append(ValidationFinding(
            f"allocation:{key[0]}:{key[1]}", "k1_allocation", "none" if passed else "high",
            "passed" if passed else "failed",
            "Schedule K total reconciles to K-1 allocations." if passed else
            "Schedule K total does not reconcile to K-1 allocations.", total, allocated,
            allocated - total, (key[0],)))

    recipient_values = {(x["recipient_return_id"], x["concept_id"]): float(x["value"])
                        for x in package.get("recipient_reported_values", [])}
    for allocation in allocations:
        key = (allocation.get("recipient_return_id"), allocation["concept_id"])
        reported = recipient_values.get(key)
        if key[0] and reported is not None:
            allocated = float(allocation["value"])
            if not _close(allocated, reported, 1.0):
                findings.append(ValidationFinding(
                    f"recipient:{key[0]}:{key[1]}", "related_return", "high", "failed",
                    "K-1 allocation differs from recipient return reporting.", allocated,
                    reported, reported - allocated,
                    (allocation["entity_return_id"], key[0])))

    statuses = [f.status for f in findings]
    assessable = bool(returns) and "not_assessable" not in statuses
    failed = sum(status == "failed" for status in statuses)
    passed = sum(status == "passed" for status in statuses)
    completeness = round(100 * passed / max(1, passed + failed), 1)
    return {"engine_version": ENGINE_VERSION, "return_count": len(returns),
            "analysis_status": "validated" if assessable and failed == 0 else
            "exceptions_present" if returns else "not_assessable",
            "passed_count": passed, "failed_count": failed,
            "control_pass_rate": completeness,
            "findings": [asdict(finding) for finding in findings],
            "scoring_gate": "eligible" if assessable and failed == 0 else "preliminary_only"}

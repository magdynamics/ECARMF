"""Deterministic, explainable audit-readiness rules engine."""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any, Callable


@dataclass(frozen=True)
class Finding:
    rule_id: str
    category: str
    score: int
    rationale: str
    technique: str


@dataclass(frozen=True)
class Rule:
    rule_id: str
    category: str
    score: int
    rationale: str
    technique: str
    predicate: Callable[[dict[str, Any]], bool]


RULES = (
    Rule("RSK-INCOME-001", "income_completeness", 25,
         "Bank deposits exceed reported gross receipts by more than 5%.",
         "Bank-deposit reconciliation",
         lambda p: _number(p, "bank_deposits_to_reported_receipts", 1.0) > 1.05),
    Rule("RSK-INFO-001", "information_reporting", 20,
         "Information returns exceed recorded receipts by more than 2%.",
         "Information-return reconciliation",
         lambda p: _number(p, "information_returns_to_books", 1.0) > 1.02),
    Rule("RSK-RECORDS-001", "documentation", 15,
         "Books and records are missing or unreconciled.",
         "Books-to-return reconciliation",
         lambda p: p.get("records_quality") in {"missing", "unreconciled"}),
    Rule("RSK-CASH-001", "income_completeness", 10,
         "Cash receipts are at least 20% of reported receipts.",
         "Cash-receipts testing",
         lambda p: _number(p, "cash_receipts_pct", 0.0) >= 0.20),
    Rule("RSK-RELATED-001", "related_party", 15,
         "A related-party transaction lacks documentation.",
         "Related-party transaction analysis",
         lambda p: p.get("undocumented_related_party") is True),
    Rule("RSK-WORKER-001", "employment_tax", 15,
         "The profile identifies worker-classification uncertainty.",
         "Worker-classification analysis",
         lambda p: p.get("worker_classification_risk") is True),
    Rule("RSK-LOSS-001", "profit_motive", 10,
         "The activity reports losses in at least three of five years.",
         "Profit-motive fact development",
         lambda p: _number(p, "loss_years_last_five", 0) >= 3),
)


def _number(profile: dict[str, Any], key: str, default: float) -> float:
    value = profile.get(key, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{key} must be numeric")
    return float(value)


def classify(score: int) -> str:
    if score >= 70:
        return "critical"
    if score >= 45:
        return "high"
    if score >= 25:
        return "elevated"
    if score >= 10:
        return "moderate"
    return "low"


def assess(profile: dict[str, Any]) -> dict[str, Any]:
    """Evaluate a profile and return an explainable assessment."""
    if not isinstance(profile, dict):
        raise ValueError("profile must be a JSON object")

    findings = [
        Finding(r.rule_id, r.category, r.score, r.rationale, r.technique)
        for r in RULES
        if r.predicate(profile)
    ]
    score = min(100, sum(f.score for f in findings))
    return {
        "engine_version": "0.1.0",
        "score": score,
        "classification": classify(score),
        "findings": [asdict(f) for f in findings],
        "disclaimer": "Readiness screening only; professional review is required.",
    }

"""Explainable, multidimensional audit-readiness assessment for return portfolios.

This module evaluates public IRS selection indicators and readiness facts. It does
not estimate or reproduce DIF, predict an IRS decision, or provide legal advice.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any, Callable, Iterable


@dataclass(frozen=True)
class Signal:
    signal_id: str
    dimension: str
    points: int
    title: str
    rationale: str
    technique: str
    remediation: str
    source_reference: str
    predicate: Callable[[dict[str, Any]], bool]


@dataclass(frozen=True)
class PortfolioFinding:
    signal_id: str
    dimension: str
    points: int
    title: str
    rationale: str
    technique: str
    remediation: str
    source_reference: str


def _number(profile: dict[str, Any], key: str, default: float) -> float:
    value = profile.get(key, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{key} must be numeric")
    return float(value)


def _truth(profile: dict[str, Any], key: str) -> bool:
    value = profile.get(key, False)
    if not isinstance(value, bool):
        raise ValueError(f"{key} must be boolean")
    return value


SIGNALS = (
    Signal(
        "SEL-INFO-MISMATCH", "selection_indicators", 20,
        "Third-party information mismatch",
        "Information returns exceed recorded receipts by more than 2%.",
        "Reconcile Forms W-2/1099 and other third-party records to the return.",
        "Correct mapping errors and document every reconciling item.",
        "IRS Publication 556; IRM 4.1.27",
        lambda p: _number(p, "information_returns_to_books", 1.0) > 1.02,
    ),
    Signal(
        "SEL-BANK-DEPOSITS", "selection_indicators", 20,
        "Deposits exceed reported receipts",
        "Bank deposits exceed reported gross receipts by more than 5%.",
        "Perform a bank-deposit and nontaxable-source reconciliation.",
        "Prepare a deposit schedule tied to books, returns, loans, and capital.",
        "IRM 4.10.4",
        lambda p: _number(p, "bank_deposits_to_reported_receipts", 1.0) > 1.05,
    ),
    Signal(
        "SEL-LUQ", "selection_indicators", 12,
        "Large, unusual, or questionable item",
        "The return contains an identified material LUQ item.",
        "Develop the issue using the applicable return line and ATG procedure.",
        "Document business purpose, computation, authority, and support.",
        "IRM 4.1.5; IRM 4.10.2",
        lambda p: _truth(p, "large_unusual_questionable_item"),
    ),
    Signal(
        "SEL-RELATED", "selection_indicators", 12,
        "Related examination exposure",
        "A related taxpayer, owner, partner, investor, or transaction is under review.",
        "Map related returns and trace intercompany or owner transactions.",
        "Build a consistent related-party file across every affected return.",
        "IRS Audits; Publication 556; IRM 4.10.5",
        lambda p: _truth(p, "related_examination"),
    ),
    Signal(
        "SEL-TREND", "selection_indicators", 8,
        "Material multi-year inconsistency",
        "A three-year comparison identifies a material unexplained change.",
        "Perform horizontal analysis and return-to-return reconciliation.",
        "Document business changes and quantify every material variance.",
        "IRM 4.1.5",
        lambda p: _truth(p, "material_multiyear_inconsistency"),
    ),
    Signal(
        "SEL-CAMPAIGN", "selection_indicators", 10,
        "Published compliance campaign alignment",
        "The facts align with a currently published IRS compliance campaign.",
        "Apply the campaign issue guide and relevant ATG techniques.",
        "Complete a campaign-specific technical and evidence review.",
        "IRS LB&I Active Campaigns; IRM 4.50",
        lambda p: _truth(p, "published_campaign_alignment"),
    ),
    Signal(
        "EXP-UNSUPPORTED-INCOME", "adjustment_exposure", 25,
        "Unreconciled income difference",
        "An income mismatch remains unexplained after preliminary reconciliation.",
        "Use direct reconciliation and, if necessary, appropriate indirect methods.",
        "Resolve and evidence all taxable and nontaxable differences.",
        "IRM 4.10.4",
        lambda p: _truth(p, "unresolved_income_difference"),
    ),
    Signal(
        "EXP-DEDUCTIONS", "adjustment_exposure", 18,
        "Material unsupported deductions",
        "One or more material deductions lack contemporaneous support.",
        "Test business purpose, substantiation, timing, and capitalization.",
        "Obtain primary evidence or identify defensible alternative evidence.",
        "IRM 4.10.3; applicable ATG",
        lambda p: _truth(p, "material_unsupported_deductions"),
    ),
    Signal(
        "EXP-BASIS", "adjustment_exposure", 15,
        "Basis or loss-limitation uncertainty",
        "Owner basis, at-risk, passive-loss, or distribution support is incomplete.",
        "Reconstruct basis from formation through the current tax year.",
        "Prepare annual basis roll-forwards tied to K-1s and transactions.",
        "Forms 7203, 6198, 8582; applicable return instructions",
        lambda p: _truth(p, "basis_or_loss_limitation_risk"),
    ),
    Signal(
        "EXP-PAYROLL", "adjustment_exposure", 15,
        "Employment-tax exposure",
        "Worker classification, officer compensation, or payroll reporting is uncertain.",
        "Reconcile payroll and analyze worker status and reasonable compensation.",
        "Correct classifications and retain contracts and compensation analysis.",
        "IRM 4.23; applicable ATG",
        lambda p: _truth(p, "worker_classification_risk"),
    ),
    Signal(
        "EXP-RELATED-PARTY", "adjustment_exposure", 15,
        "Undocumented related-party transaction",
        "A material related-party transaction lacks governing documentation.",
        "Trace authorization, economics, pricing, recording, and reporting.",
        "Execute and retain agreements and reconcile all affected returns.",
        "IRM 4.10.3; applicable return instructions",
        lambda p: _truth(p, "undocumented_related_party"),
    ),
)


def _band(score: int, *, positive: bool = False) -> str:
    if positive:
        if score >= 85:
            return "strong"
        if score >= 65:
            return "adequate"
        if score >= 40:
            return "weak"
        return "critical_gap"
    if score >= 70:
        return "critical"
    if score >= 45:
        return "high"
    if score >= 25:
        return "elevated"
    if score >= 10:
        return "moderate"
    return "low"


def _readiness(profile: dict[str, Any]) -> tuple[int, list[str]]:
    score = 100
    gaps: list[str] = []
    penalties = {
        "records_quality": {"complete": 0, "reconciled": 5, "unreconciled": 30, "missing": 55},
        "return_to_books_reconciled": {True: 0, False: 25},
        "third_party_reconciled": {True: 0, False: 20},
        "contemporaneous_support": {True: 0, False: 20},
    }
    defaults: dict[str, Any] = {
        "records_quality": "unreconciled",
        "return_to_books_reconciled": False,
        "third_party_reconciled": False,
        "contemporaneous_support": False,
    }
    for key, mapping in penalties.items():
        value = profile.get(key, defaults[key])
        if value not in mapping:
            raise ValueError(f"invalid {key}: {value!r}")
        penalty = mapping[value]
        score -= penalty
        if penalty:
            gaps.append(key)
    return max(0, score), gaps


def _controversy_readiness(profile: dict[str, Any]) -> tuple[int, list[str]]:
    controls = (
        "statute_dates_verified",
        "rights_notices_retained",
        "authorized_representative_identified",
        "privilege_protocol_established",
        "document_preservation_active",
    )
    missing = [key for key in controls if not _truth(profile, key)]
    return round(100 * (len(controls) - len(missing)) / len(controls)), missing


def _confidence(profile: dict[str, Any]) -> tuple[int, str]:
    inputs = (
        "return_data_complete",
        "prior_years_available",
        "books_available",
        "third_party_data_available",
        "industry_benchmark_available",
    )
    present = sum(1 for key in inputs if _truth(profile, key))
    score = round(100 * present / len(inputs))
    grade = "A" if score >= 80 else "B" if score >= 60 else "C" if score >= 40 else "D"
    return score, grade


def assess_return(profile: dict[str, Any]) -> dict[str, Any]:
    """Assess one normalized return profile across independent risk dimensions."""
    if not isinstance(profile, dict):
        raise ValueError("return profile must be an object")
    required = ("return_id", "entity_type", "tax_year")
    missing = [key for key in required if key not in profile]
    if missing:
        raise ValueError(f"missing required fields: {', '.join(missing)}")

    findings = [
        PortfolioFinding(
            signal.signal_id, signal.dimension, signal.points, signal.title,
            signal.rationale, signal.technique, signal.remediation,
            signal.source_reference,
        )
        for signal in SIGNALS
        if signal.predicate(profile)
    ]
    selection = min(100, sum(f.points for f in findings if f.dimension == "selection_indicators"))
    exposure = min(100, sum(f.points for f in findings if f.dimension == "adjustment_exposure"))
    documentation, documentation_gaps = _readiness(profile)
    controversy, controversy_gaps = _controversy_readiness(profile)
    confidence, confidence_grade = _confidence(profile)

    # Portfolio priority is a workflow triage measure, not an audit probability.
    priority = round(
        selection * 0.35
        + exposure * 0.35
        + (100 - documentation) * 0.20
        + (100 - controversy) * 0.10
    )
    return {
        "engine_version": "0.2.0",
        "return_id": str(profile["return_id"]),
        "taxpayer_id": str(profile.get("taxpayer_id", "")),
        "entity_type": str(profile["entity_type"]),
        "tax_year": int(profile["tax_year"]),
        "scores": {
            "public_selection_indicators": selection,
            "adjustment_exposure": exposure,
            "documentation_readiness": documentation,
            "controversy_readiness": controversy,
            "assessment_confidence": confidence,
            "portfolio_priority": priority,
        },
        "bands": {
            "public_selection_indicators": _band(selection),
            "adjustment_exposure": _band(exposure),
            "documentation_readiness": _band(documentation, positive=True),
            "controversy_readiness": _band(controversy, positive=True),
            "assessment_confidence": confidence_grade,
            "portfolio_priority": _band(priority),
        },
        "documentation_gaps": documentation_gaps,
        "controversy_gaps": controversy_gaps,
        "findings": [asdict(finding) for finding in findings],
        "limitations": [
            "This is not a probability that the IRS will select the return.",
            "The engine uses public indicators and does not reproduce confidential DIF models.",
            "Scores require professional validation against the actual return and evidence.",
            "Legal positions, privilege, deadlines, and litigation strategy require qualified counsel.",
        ],
    }


def assess_portfolio(profiles: Iterable[dict[str, Any]]) -> dict[str, Any]:
    """Assess and rank multiple return profiles for CPA review workflow."""
    if isinstance(profiles, (str, bytes, dict)):
        raise ValueError("portfolio must be an array of return profiles")
    assessments = [assess_return(profile) for profile in profiles]
    assessments.sort(
        key=lambda item: (
            item["scores"]["portfolio_priority"],
            item["scores"]["adjustment_exposure"],
            item["scores"]["public_selection_indicators"],
        ),
        reverse=True,
    )
    return {
        "engine_version": "0.2.0",
        "return_count": len(assessments),
        "portfolio": assessments,
        "summary": {
            "critical_or_high_priority": sum(
                item["bands"]["portfolio_priority"] in {"critical", "high"}
                for item in assessments
            ),
            "low_confidence_assessments": sum(
                item["bands"]["assessment_confidence"] in {"C", "D"}
                for item in assessments
            ),
        },
        "ranking_note": "Portfolio priority ranks review work; it is not an IRS audit probability.",
    }

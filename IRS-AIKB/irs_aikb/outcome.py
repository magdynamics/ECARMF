"""Finding-to-outcome validation and professional release controls."""
from __future__ import annotations

from typing import Any

OUTCOME_VERSION = "0.1.0"


def evaluate_outcome_chain(payload: dict[str, Any]) -> dict[str, Any]:
    case = payload.get("case", {})
    findings = payload.get("findings", [])
    recommendations = payload.get("recommendations", [])
    actions = payload.get("actions", [])
    resolutions = payload.get("resolutions", [])
    outcomes = payload.get("outcomes", [])
    deliverables = payload.get("deliverables", [])
    blockers: list[str] = []

    for field in ("matter_id", "client_token", "taxpayer_token", "jurisdiction_module_id",
                  "professional_reviewer_token"):
        if not case.get(field):
            blockers.append(f"missing_case_{field}")

    finding_ids = {x.get("finding_id") for x in findings if x.get("finding_id")}
    recommendation_ids = {x.get("recommendation_id") for x in recommendations if x.get("recommendation_id")}
    action_ids = {x.get("action_id") for x in actions if x.get("action_id")}
    resolution_ids = {x.get("resolution_id") for x in resolutions if x.get("resolution_id")}

    for finding in findings:
        fid = finding.get("finding_id", "unknown")
        for field in ("facts_snapshot_hash", "evidence_ids", "source_versions", "rule_version",
                      "confidence", "professional_conclusion", "reviewed_by_token"):
            if not finding.get(field):
                blockers.append(f"finding_{fid}_missing_{field}")
        if finding.get("jurisdiction_module_id") != case.get("jurisdiction_module_id"):
            blockers.append(f"finding_{fid}_jurisdiction_mismatch")

    for recommendation in recommendations:
        rid = recommendation.get("recommendation_id", "unknown")
        if recommendation.get("finding_id") not in finding_ids:
            blockers.append(f"recommendation_{rid}_finding_link_required")
        for field in ("alternatives", "expected_benefit", "risks", "owner_token",
                      "professional_approved_by_token"):
            if not recommendation.get(field):
                blockers.append(f"recommendation_{rid}_missing_{field}")

    for action in actions:
        aid = action.get("action_id", "unknown")
        if action.get("recommendation_id") not in recommendation_ids:
            blockers.append(f"action_{aid}_recommendation_link_required")
        for field in ("assigned_to_token", "status", "due_at"):
            if not action.get(field):
                blockers.append(f"action_{aid}_missing_{field}")
        if action.get("client_approval_required") and not action.get("client_decision_id"):
            blockers.append(f"action_{aid}_client_decision_required")
        if action.get("status") == "completed" and not action.get("completion_evidence_ids"):
            blockers.append(f"action_{aid}_completion_evidence_required")

    for resolution in resolutions:
        resid = resolution.get("resolution_id", "unknown")
        linked = set(resolution.get("action_ids", []))
        if not linked or not linked.issubset(action_ids):
            blockers.append(f"resolution_{resid}_valid_action_links_required")
        for field in ("resolution_type", "final_position", "evidence_ids", "verified_by_token"):
            if not resolution.get(field):
                blockers.append(f"resolution_{resid}_missing_{field}")

    for outcome in outcomes:
        oid = outcome.get("outcome_id", "unknown")
        if outcome.get("resolution_id") not in resolution_ids:
            blockers.append(f"outcome_{oid}_resolution_link_required")
        for field in ("outcome_type", "baseline", "method", "evidence_ids", "attribution",
                      "verified_by_token"):
            if not outcome.get(field):
                blockers.append(f"outcome_{oid}_missing_{field}")
        if outcome.get("financial_value") is not None and not outcome.get("double_counting_reviewed"):
            blockers.append(f"outcome_{oid}_double_counting_review_required")

    for deliverable in deliverables:
        did = deliverable.get("deliverable_id", "unknown")
        if deliverable.get("audience") not in {"client", "internal", "tax_authority", "management"}:
            blockers.append(f"deliverable_{did}_invalid_audience")
        if not deliverable.get("professional_approved_by_token"):
            blockers.append(f"deliverable_{did}_professional_approval_required")
        if deliverable.get("audience") == "tax_authority" and not deliverable.get("external_action_approval_id"):
            blockers.append(f"deliverable_{did}_external_action_approval_required")
        if deliverable.get("contains_privileged_material"):
            blockers.append(f"deliverable_{did}_privileged_material_prohibited")

    blockers = sorted(set(blockers))
    chain_complete = bool(findings and recommendations and actions and resolutions and outcomes) and not blockers
    return {
        "outcome_version": OUTCOME_VERSION,
        "matter_id": case.get("matter_id"),
        "status": "verified_outcome" if chain_complete else ("action_required" if blockers else "in_progress"),
        "release_status": "eligible_for_professional_release" if chain_complete else "hold",
        "blockers": blockers,
        "counts": {"findings": len(findings), "recommendations": len(recommendations),
                   "actions": len(actions), "resolutions": len(resolutions),
                   "outcomes": len(outcomes), "deliverables": len(deliverables)},
        "controls": {"ai_may_draft": True, "ai_may_verify_outcome": False,
                     "professional_release_required": True, "client_decisions_preserved": True,
                     "evidence_lineage_required": True, "verified_value_only": True,
                     "automatic_rule_learning": False},
    }


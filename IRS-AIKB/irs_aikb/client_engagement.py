"""Client transparency, signature, communication, and value controls."""
from __future__ import annotations

from typing import Any

ENGAGEMENT_VERSION = "0.1.0"

AGENT_CONTRIBUTIONS = {
    "client_engagement_agent": "Draft plain-language status updates and route client questions.",
    "agreement_agent": "Prepare approved service-agreement templates and monitor signature status.",
    "authorization_agent": "Prepare Form 2848/8821 data and monitor taxpayer/representative signatures.",
    "evidence_agent": "Translate evidence gaps into client-safe document requests.",
    "case_schedule_agent": "Monitor internal, promised, and verified external dates separately.",
    "audit_program_agent": "Convert approved audit-program steps into appropriate client requests.",
    "rights_options_agent": "Draft sourced options for professional review without making elections.",
    "value_agent": "Report verified progress and outcomes without unsupported savings claims.",
    "communication_quality_agent": "Check clarity, scope, confidentiality, tone, and required approvals.",
}

SENSITIVE_EVENT_TYPES = {
    "risk_score", "fraud_indicator", "privileged_analysis", "draft_legal_position",
    "proposed_adjustment", "rights_election", "deadline", "authority_submission",
}


def evaluate_client_engagement(payload: dict[str, Any]) -> dict[str, Any]:
    client = payload.get("client", {})
    case = payload.get("case", {})
    agreement = payload.get("service_agreement", {})
    authorization = payload.get("authorization", {})
    events = payload.get("events", [])
    blockers: list[str] = []
    actions: list[dict[str, Any]] = []
    updates: list[dict[str, Any]] = []

    for field in ("client_token", "preferred_channel", "language", "update_cadence_days"):
        if not client.get(field):
            blockers.append(f"missing_client_{field}")
    for field in ("matter_id", "case_owner_token", "jurisdiction_module_id"):
        if not case.get(field):
            blockers.append(f"missing_case_{field}")

    if agreement.get("required", True):
        if agreement.get("status") not in {"signed", "effective"}:
            actions.append({"action": "obtain_service_agreement_signature", "client_action": True,
                            "external_send_requires_approval": True})
        if not agreement.get("template_version"):
            blockers.append("service_agreement_template_version_required")

    if authorization.get("representation_required"):
        if authorization.get("authorization_type") not in {"2848", "8821"}:
            blockers.append("valid_authorization_type_required")
        if authorization.get("status") not in {"submitted", "accepted"}:
            actions.append({"action": "obtain_taxpayer_authorization_signature", "client_action": True,
                            "external_send_requires_approval": True,
                            "joint_taxpayers_require_separate_review": True})
        if not authorization.get("tax_forms") or not authorization.get("tax_periods"):
            blockers.append("authorization_scope_required")

    for event in events:
        event_type = event.get("event_type", "general_status")
        reasons: list[str] = []
        if event.get("contains_privileged_material"):
            reasons.append("privileged_material_must_be_removed")
        if event_type == "deadline" and not event.get("professionally_verified"):
            reasons.append("unverified_deadline_cannot_be_sent_as_controlling")
        if event_type in SENSITIVE_EVENT_TYPES and not event.get("professional_approved"):
            reasons.append("professional_approval_required")
        if event.get("value_amount") is not None and not all(event.get(key) for key in
                ("value_baseline", "value_method", "value_evidence", "professional_approved")):
            reasons.append("value_claim_not_verified")
        updates.append({
            "event_id": event.get("event_id"),
            "release_status": "hold" if reasons else "eligible_for_client_release",
            "reasons": sorted(set(reasons)),
            "content_requirements": ["what_changed", "why_it_matters", "work_completed",
                                     "next_step", "client_action_if_any", "responsible_contact"],
            "risk_language": "plain_factual_non_alarmist",
        })

    return {
        "engagement_version": ENGAGEMENT_VERSION,
        "status": "action_required" if blockers else "controlled",
        "blockers": sorted(set(blockers)),
        "required_actions": actions,
        "client_updates": updates,
        "agent_contributions": AGENT_CONTRIBUTIONS,
        "controls": {
            "ai_may_draft": True,
            "ai_may_sign": False,
            "ai_may_make_client_election": False,
            "ai_may_send_sensitive_message_without_approval": False,
            "client_may_choose_channel_and_cadence": True,
            "client_questions_require_owned_response": True,
            "communication_history_append_only": True,
            "verified_value_only": True,
        },
    }


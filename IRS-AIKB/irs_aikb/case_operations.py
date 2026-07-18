"""Controlled staff, agent, authorization, deadline, and IRS action workflow."""
from __future__ import annotations

from typing import Any

CASE_OPERATIONS_VERSION = "0.1.0"
EXTERNAL_ACTIONS = {"irs_call", "request_transcript", "submit_document", "file_protest",
                    "request_manager_conference", "request_fast_track", "request_tas_assistance"}
HIGH_CONSEQUENCE_ACTIONS = {"submit_document", "file_protest", "request_fast_track",
                            "extend_statute", "agree_adjustment", "make_taxpayer_election"}


def evaluate_case_operations(payload: dict[str, Any]) -> dict[str, Any]:
    case = payload.get("case", {})
    team = payload.get("team", [])
    agents = payload.get("agents", [])
    authorization = payload.get("authorization", {})
    deadlines = payload.get("deadlines", [])
    audit_program = payload.get("audit_program", {})
    requested_action = payload.get("requested_action", {})
    blockers: list[str] = []
    warnings: list[str] = []

    for field in ("matter_id", "client_token", "taxpayer_token", "jurisdiction_module_id",
                  "case_owner_token", "professional_reviewer_token"):
        if not case.get(field):
            blockers.append(f"missing_case_{field}")
    if not team:
        blockers.append("case_team_required")
    elif not any(member.get("staff_token") == case.get("case_owner_token") for member in team):
        blockers.append("case_owner_not_assigned_to_team")

    for agent in agents:
        for field in ("agent_id", "contract_version", "human_approver_token", "allowed_actions",
                      "prohibited_actions"):
            if not agent.get(field):
                blockers.append(f"agent_{agent.get('agent_id','unknown')}_missing_{field}")
        if agent.get("may_communicate_externally"):
            blockers.append("ai_external_representation_prohibited")

    action = requested_action.get("action")
    if action in EXTERNAL_ACTIONS:
        if not requested_action.get("human_actor_token"):
            blockers.append("human_actor_required_for_external_action")
        if not requested_action.get("professional_approved_by_token"):
            blockers.append("professional_approval_required_for_external_action")
        if requested_action.get("jurisdiction_module_id") != case.get("jurisdiction_module_id"):
            blockers.append("external_action_jurisdiction_mismatch")

    if action in {"irs_call", "request_transcript", "submit_document"}:
        if authorization.get("type") not in {"2848", "8821"}:
            blockers.append("valid_irs_authorization_required")
        if authorization.get("status") != "accepted":
            blockers.append("irs_authorization_not_accepted")
        if requested_action.get("tax_form") not in authorization.get("tax_forms", []):
            blockers.append("action_tax_form_outside_authorization")
        if requested_action.get("tax_period") not in authorization.get("tax_periods", []):
            blockers.append("action_tax_period_outside_authorization")
    if action in {"irs_call", "file_protest", "request_manager_conference", "request_fast_track"}:
        actor = next((m for m in team if m.get("staff_token") == requested_action.get("human_actor_token")), {})
        if not actor.get("eligible_to_represent_before_irs"):
            blockers.append("human_actor_not_eligible_for_irs_representation")

    if action in HIGH_CONSEQUENCE_ACTIONS and not requested_action.get("client_decision_id"):
        blockers.append("recorded_client_decision_required")

    for item in deadlines:
        if item.get("deadline_type") not in {"verified_external", "irs_promised", "internal"}:
            blockers.append(f"deadline_{item.get('deadline_id','unknown')}_invalid_type")
        if item.get("deadline_type") == "verified_external":
            if not item.get("source_document_id") or not item.get("verified_by_token"):
                blockers.append(f"deadline_{item.get('deadline_id','unknown')}_verification_required")
        if item.get("status") == "open" and not item.get("responsible_staff_token"):
            blockers.append(f"deadline_{item.get('deadline_id','unknown')}_owner_required")

    if audit_program:
        for field in ("program_id", "program_version", "jurisdiction_module_id", "source_versions",
                      "approved_by_token", "procedures"):
            if not audit_program.get(field):
                blockers.append(f"audit_program_missing_{field}")
        if audit_program.get("jurisdiction_module_id") != case.get("jurisdiction_module_id"):
            blockers.append("audit_program_jurisdiction_mismatch")
        for procedure in audit_program.get("procedures", []):
            if not procedure.get("assigned_to_token") or not procedure.get("reviewer_token"):
                blockers.append(f"procedure_{procedure.get('procedure_id','unknown')}_assignment_review_required")
    else:
        warnings.append("customized_audit_program_not_created")

    return {
        "case_operations_version": CASE_OPERATIONS_VERSION,
        "matter_id": case.get("matter_id"),
        "action": action,
        "decision": "allow" if not blockers else "block",
        "blockers": sorted(set(blockers)),
        "warnings": sorted(set(warnings)),
        "controls": {
            "ai_may_prepare_work": True,
            "ai_may_contact_tax_authority": False,
            "authority_scope_enforced": True,
            "jurisdiction_separation_enforced": True,
            "external_deadlines_require_source_and_verification": True,
            "high_consequence_actions_require_client_decision": True,
            "external_actions_logged": True,
        },
    }


"""Controlled IDR, workpaper, taxpayer-rights, and controversy workflow."""

from __future__ import annotations

from datetime import date
from typing import Any

WORKFLOW_VERSION = "0.1.0"

TAXPAYER_RIGHTS = (
    "Be informed", "Quality service", "Pay no more than the correct amount",
    "Challenge the IRS position and be heard", "Appeal in an independent forum",
    "Finality", "Privacy", "Confidentiality", "Retain representation",
    "A fair and just tax system",
)


def _notice_controls(notices: list[dict[str, Any]], as_of: date) -> tuple[list[dict], list[str]]:
    controls, alerts = [], []
    for notice in notices:
        due_text = notice.get("response_due_date")
        days = None
        if due_text:
            due = date.fromisoformat(due_text)
            days = (due - as_of).days
            if days < 0: alerts.append(f"Past supplied deadline: {notice.get('notice_id', 'unknown')}")
            elif days <= 14: alerts.append(f"Deadline within 14 days: {notice.get('notice_id', 'unknown')}")
        controls.append({"notice_id": notice.get("notice_id"), "notice_type": notice.get("notice_type"),
                         "notice_date": notice.get("notice_date"), "response_due_date": due_text,
                         "days_remaining": days, "deadline_source": notice.get("deadline_source", "supplied_notice"),
                         "verified_by": notice.get("verified_by"),
                         "verification_status": "verified" if notice.get("verified_by") else "requires_verification"})
        if not due_text: alerts.append(f"Deadline missing: {notice.get('notice_id', 'unknown')}")
        if "deficiency" in str(notice.get("notice_type", "")).lower():
            alerts.append("Statutory notice of deficiency: immediate tax controversy counsel review required")
    return controls, alerts


def build_case_workflow(payload: dict[str, Any]) -> dict[str, Any]:
    assessment = payload.get("assessment", {})
    matter = payload.get("matter", {})
    missing = [key for key in ("matter_id", "client_token", "engagement_type") if not matter.get(key)]
    if missing: raise ValueError(f"missing matter controls: {', '.join(missing)}")
    as_of = date.fromisoformat(payload.get("as_of_date", date.today().isoformat()))
    notice_controls, deadline_alerts = _notice_controls(payload.get("notices", []), as_of)

    issues, idr = [], []
    for index, finding in enumerate(assessment.get("findings", []), 1):
        issue_id = finding["issue_id"]
        evidence = list(finding.get("evidence", []))
        issues.append({"workpaper_id": f"WP-{index:03d}", "issue_id": issue_id,
                       "title": finding["title"], "observed_fact": finding["observed_fact"],
                       "authority_refs": finding.get("authority_refs", []),
                       "audit_technique": finding.get("audit_technique"),
                       "taxpayer_explanations_to_test": finding.get("possible_explanations", []),
                       "conclusion_status": "open", "prepared_by": None, "reviewed_by": None})
        idr.append({"request_id": f"IDR-{index:03d}", "issue_id": issue_id,
                    "objective": finding["title"], "documents": evidence,
                    "scope_limit": "Only the identified matter, taxpayer, period, and issue",
                    "production_status": "draft_requires_approval",
                    "privilege_review": "required_before_release",
                    "response_tracking": {"requested": len(evidence), "received": 0,
                                          "complete": False, "exceptions": []}})

    controls = matter.get("controls", {})
    mandatory = {
        "engagement_authority": bool(controls.get("engagement_authority")),
        "form_2848_or_8821_scope_verified": bool(controls.get("authorization_scope_verified")),
        "statute_dates_verified": bool(controls.get("statute_dates_verified")),
        "privilege_protocol": bool(controls.get("privilege_protocol")),
        "document_preservation": bool(controls.get("document_preservation")),
        "secure_transmission": bool(controls.get("secure_transmission")),
    }
    blockers = [name for name, passed in mandatory.items() if not passed]
    blockers.extend(deadline_alerts)
    rights = [{"right": right, "status": "review_required", "impact": None} for right in TAXPAYER_RIGHTS]
    protest_outline = {
        "status": "draft_framework_only", "sections": [
            "Taxpayer and periods", "Proposed changes disputed", "Facts supporting each position",
            "Law and authority", "Requested resolution", "Attachments and evidence index",
            "Required declaration or representative statement—verify exact applicable language",
        ],
        "warning": "Do not sign or file until deadline, authority, facts, law, and declaration are reviewed."
    }
    return {"workflow_version": WORKFLOW_VERSION, "matter_id": matter["matter_id"],
            "as_of_date": as_of.isoformat(), "workflow_status": "blocked" if blockers else "ready_for_cpa_review",
            "mandatory_controls": mandatory, "blockers_and_alerts": blockers,
            "notice_controls": notice_controls, "taxpayer_rights_checklist": rights,
            "issue_workpapers": issues, "draft_idrs": idr, "appeals_protest_outline": protest_outline,
            "production_gate": {"automatic_transmission": False, "requires_cpa_approval": True,
                                "requires_privilege_review": True,
                                "counsel_required_for_deficiency_or_litigation": True},
            "sources": ["IRS Taxpayer Bill of Rights", "IRS Publication 1", "IRS Publication 5",
                        "IRS Publication 556", "IRS Preparing a Request for Appeals"],
            "limitations": ["Deadlines are taken from supplied notices and must be independently verified.",
                            "The workflow does not provide legal advice or file a protest or petition.",
                            "IDRs are internal readiness drafts, not representations of an IRS request."]}

"""Referral-partner registration and deny-by-default case access decisions."""
from __future__ import annotations

from datetime import datetime, timezone, timedelta
from typing import Any

ACCESS_VERSION = "0.1.0"
RECERTIFICATION_DAYS = 90

PERMISSIONS = {
    "view_status",
    "upload_documents",
    "view_selected_documents",
    "secure_messages",
    "view_selected_findings",
    "receive_released_report",
    "attend_meetings",
    "view_billing",
}

# These materials require an individual firm release even when the client has
# generally authorized sponsor collaboration.
RESTRICTED_ARTIFACT_CLASSES = {
    "attorney_client_privileged",
    "attorney_work_product",
    "firm_internal_workpaper",
    "draft_conclusion",
    "quality_review_note",
    "fraud_assessment",
    "other_taxpayer_information",
}


def _parse_time(value: Any) -> datetime | None:
    if not value:
        return None
    try:
        parsed = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
        return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
    except ValueError:
        return None


def evaluate_sponsor_access(payload: dict[str, Any], *, now: datetime | None = None) -> dict[str, Any]:
    """Return an auditable authorization decision for one requested sponsor action.

    A referral never implies access. Authorization requires active client consent,
    a matching grant, firm approval, a permitted action, matching scope, and (for
    restricted artifacts) an explicit document/report release.
    """
    sponsor = payload.get("sponsor", {})
    referral = payload.get("referral", {})
    consent = payload.get("consent", {})
    grant = payload.get("grant", {})
    request = payload.get("request", {})
    release = payload.get("release", {})
    checked_at = now or datetime.now(timezone.utc)
    reasons: list[str] = []

    for key, source in (
        ("sponsor_id", sponsor),
        ("referral_id", referral),
        ("consent_id", consent),
        ("access_grant_id", grant),
        ("client_token", request),
        ("matter_id", request),
        ("permission", request),
    ):
        if not source.get(key):
            reasons.append(f"missing_{key}")

    if sponsor.get("status") != "active":
        reasons.append("sponsor_not_active")
    if sponsor.get("security_status", "active") != "active":
        reasons.append("sponsor_security_suspended")
    if not _parse_time(sponsor.get("mfa_verified_at")):
        reasons.append("sponsor_mfa_required")
    if referral.get("sponsor_id") != sponsor.get("sponsor_id"):
        reasons.append("referral_sponsor_mismatch")

    # Referral-only is the permanent default until all authorization controls pass.
    if consent.get("decision") != "authorized" or consent.get("status") != "active":
        reasons.append("active_client_consent_required")
    if consent.get("client_token") != request.get("client_token"):
        reasons.append("consent_client_mismatch")
    if consent.get("sponsor_id") != sponsor.get("sponsor_id"):
        reasons.append("consent_sponsor_mismatch")
    if not consent.get("signed_evidence_hash"):
        reasons.append("signed_consent_evidence_required")

    if grant.get("status") != "active":
        reasons.append("access_grant_not_active")
    if not grant.get("firm_approved_by_token"):
        reasons.append("firm_approval_required")
    for key in ("sponsor_id", "client_token", "matter_id"):
        if grant.get(key) != (sponsor.get(key) if key == "sponsor_id" else request.get(key)):
            reasons.append(f"grant_{key}_mismatch")
    if grant.get("consent_id") != consent.get("consent_id"):
        reasons.append("grant_consent_mismatch")

    permission = request.get("permission")
    allowed = set(grant.get("permissions", []))
    if permission not in PERMISSIONS:
        reasons.append("unknown_permission")
    elif permission not in allowed:
        reasons.append("permission_not_granted")

    starts = _parse_time(grant.get("effective_at"))
    expires = _parse_time(grant.get("expires_at"))
    if not starts or not expires:
        reasons.append("valid_access_window_required")
    else:
        if checked_at < starts:
            reasons.append("access_not_yet_effective")
        if checked_at >= expires:
            reasons.append("access_expired")
    if consent.get("revoked_at") or grant.get("revoked_at"):
        reasons.append("access_revoked")
    recertified = _parse_time(grant.get("recertified_at"))
    if not recertified or checked_at - recertified > timedelta(days=RECERTIFICATION_DAYS):
        reasons.append("access_recertification_required")
    if request.get("matter_status") in {"closed", "terminated", "archived"}:
        reasons.append("matter_no_longer_active")

    delivery_mode = request.get("delivery_mode", "screen")
    if delivery_mode in {"download", "export"} and not request.get("step_up_verified"):
        reasons.append("step_up_verification_required")
    if delivery_mode == "export" and not grant.get("bulk_export_approved", False):
        reasons.append("bulk_export_not_approved")

    taxpayer_scope = set(grant.get("taxpayer_tokens", []))
    if taxpayer_scope and request.get("taxpayer_token") not in taxpayer_scope:
        reasons.append("taxpayer_outside_scope")
    try:
        year_scope = {int(x) for x in grant.get("tax_years", [])}
        requested_year = int(request.get("tax_year", -1))
    except (TypeError, ValueError):
        year_scope, requested_year = set(), -1
        reasons.append("invalid_tax_year_scope")
    if year_scope and requested_year not in year_scope:
        reasons.append("tax_year_outside_scope")
    category_scope = set(grant.get("document_categories", []))
    if request.get("document_category") and category_scope and request.get("document_category") not in category_scope:
        reasons.append("document_category_outside_scope")
    document_scope = set(grant.get("document_ids", []))
    if permission == "view_selected_documents" and request.get("document_id") not in document_scope:
        reasons.append("document_not_selected")

    artifact_class = request.get("artifact_class")
    if artifact_class in RESTRICTED_ARTIFACT_CLASSES:
        if release.get("status") != "released" or release.get("artifact_id") != request.get("artifact_id"):
            reasons.append("explicit_firm_release_required")
        if release.get("sponsor_id") != sponsor.get("sponsor_id"):
            reasons.append("release_sponsor_mismatch")

    reasons = sorted(set(reasons))
    allowed_decision = not reasons
    return {
        "access_version": ACCESS_VERSION,
        "decision": "allow" if allowed_decision else "deny",
        "referral_alone_grants_access": False,
        "sponsor_id": sponsor.get("sponsor_id"),
        "matter_id": request.get("matter_id"),
        "permission": permission,
        "reasons": reasons,
        "checked_at": checked_at.isoformat(),
        "audit_event_required": True,
        "minimum_necessary_enforced": True,
        "client_notification_required": allowed_decision and delivery_mode in {"download", "export"},
        "watermark_required": allowed_decision and delivery_mode in {"download", "export"},
        "download_label": (
            f"Confidential — released to sponsor {sponsor.get('sponsor_id')} — {checked_at.date().isoformat()}"
            if allowed_decision and delivery_mode in {"download", "export"} else None
        ),
    }


def preview_sponsor_workspace(payload: dict[str, Any], *, now: datetime | None = None) -> dict[str, Any]:
    """Build a staff-safe preview by evaluating every proposed resource individually."""
    visible, denied = [], []
    for resource in payload.get("resources", []):
        decision_payload = {key: value for key, value in payload.items() if key != "resources"}
        decision_payload["request"] = {**payload.get("request", {}), **resource.get("request", {})}
        decision_payload["release"] = resource.get("release", payload.get("release", {}))
        decision = evaluate_sponsor_access(decision_payload, now=now)
        record = {"resource_id": resource.get("resource_id"), "decision": decision["decision"],
                  "reasons": decision["reasons"]}
        (visible if decision["decision"] == "allow" else denied).append(record)
    return {"access_version": ACCESS_VERSION, "preview_only": True,
            "visible": visible, "denied": denied,
            "visible_count": len(visible), "denied_count": len(denied)}

"""Jurisdiction-module registry and production-readiness gates."""
from __future__ import annotations

from typing import Any

JURISDICTION_VERSION = "0.1.0"

MODULES = {
    "US-IRS": {
        "jurisdiction": "US",
        "authority": "Internal Revenue Service",
        "module_name": "Federal IRS Audit Intelligence Engine",
        "status": "foundation_active",
        "knowledge_status": "partially_populated",
        "tax_types": ["federal_income", "employment", "exempt_organization"],
    },
    "US-IL-IDOR": {
        "jurisdiction": "US-IL",
        "authority": "Illinois Department of Revenue",
        "module_name": "Illinois IDOR Audit Intelligence Engine",
        "status": "reserved_placeholder",
        "knowledge_status": "not_populated",
        "tax_types": ["income", "sales_use", "withholding", "specialty"],
    },
}


def module_registry() -> list[dict[str, Any]]:
    return [{"module_id": key, **value} for key, value in MODULES.items()]


def evaluate_jurisdiction_readiness(payload: dict[str, Any]) -> dict[str, Any]:
    """Prevent an unapproved jurisdiction from borrowing another module's rules."""
    module_id = payload.get("module_id")
    module = MODULES.get(module_id)
    requested_action = payload.get("requested_action", "case_intake")
    blockers: list[str] = []
    if not module:
        blockers.append("jurisdiction_module_not_registered")
    else:
        if requested_action in {"risk_scoring", "audit_analysis", "recommendation_release"}:
            if module["status"] != "production_approved":
                blockers.append("jurisdiction_module_not_production_approved")
            if module["knowledge_status"] != "professionally_validated":
                blockers.append("jurisdiction_knowledge_not_professionally_validated")
        requested_tax_type = payload.get("tax_type")
        if requested_tax_type and requested_tax_type not in module["tax_types"]:
            blockers.append("tax_type_not_registered_for_jurisdiction")
    if payload.get("rule_module_id") and payload.get("rule_module_id") != module_id:
        blockers.append("cross_jurisdiction_rule_use_prohibited")
    if payload.get("source_module_id") and payload.get("source_module_id") != module_id:
        blockers.append("cross_jurisdiction_source_use_requires_explicit_mapping")

    intake_only = module_id == "US-IL-IDOR" and requested_action == "case_intake" and not blockers
    return {
        "jurisdiction_version": JURISDICTION_VERSION,
        "module_id": module_id,
        "module": module,
        "requested_action": requested_action,
        "decision": "allow_intake_only" if intake_only else ("allow" if not blockers else "block"),
        "blockers": sorted(set(blockers)),
        "analysis_enabled": not blockers and requested_action != "case_intake",
        "federal_rules_substituted": False,
    }


"""Conservative penalty consideration and defense-development screening."""
from __future__ import annotations

def screen_penalties(facts: dict) -> dict:
    considerations = []
    if facts.get("underpayment_identified"):
        considerations.append({"penalty_family": "accuracy_related", "status": "consideration_only",
            "authority": ["IRC 6662", "IRM 20.1"], "required_development":
            ["Compute underpayment", "Identify specific conduct provision", "Document supervisory approval where applicable"]})
    if facts.get("late_filing"):
        considerations.append({"penalty_family": "failure_to_file", "status": "consideration_only",
            "authority": ["IRC 6651", "IRM 20.1"], "required_development": ["Verify due date and extensions", "Verify filing date"]})
    if facts.get("information_return_failures"):
        considerations.append({"penalty_family": "information_return", "status": "consideration_only",
            "authority": ["IRC 6721", "IRC 6722", "IRM 20.1"], "required_development": ["Count affected returns", "Verify correction dates"]})
    if facts.get("fraud_indicators"):
        considerations.append({"penalty_family": "fraud_or_criminal_referral", "status": "restricted_specialist_escalation",
            "authority": ["IRM 25.1"], "required_development": ["Preserve evidence", "Stop unsupported characterizations", "Escalate to authorized specialist/counsel"]})
    defenses = []
    for flag, name, evidence in (
        ("reasonable_cause_facts", "reasonable_cause", ["Chronology", "Circumstances beyond control", "Compliance history"]),
        ("professional_reliance_facts", "professional_reliance", ["Adviser competence", "Complete facts supplied", "Actual advice and reliance"]),
        ("substantial_authority_facts", "substantial_authority", ["Controlling and persuasive authorities", "Tax-year applicability"]),
        ("adequate_disclosure_facts", "adequate_disclosure", ["Filed disclosure", "Accuracy and completeness", "Applicable form/instructions"]),
    ):
        if facts.get(flag): defenses.append({"defense": name, "status": "facts_require_review", "evidence": evidence})
    return {"considerations": considerations, "defense_development": defenses,
            "conclusion": "specialist_review_required" if considerations else "no_consideration_triggered_by_supplied_facts",
            "limitations": ["No penalty or defense conclusion is automatic.", "Fraud indicators are not a finding of fraud."]}

"""Governance, organizational learning, and value control plane."""
from __future__ import annotations
from dataclasses import dataclass, asdict
from typing import Any

CONTROL_PLANE_VERSION = "0.5.0"
MATURITY_STAGES = ("concept_defined", "source_collected", "machine_extracted",
                   "technically_mapped", "professionally_reviewed", "sanitized_tested",
                   "pilot_validated", "production_approved", "outcome_calibrated",
                   "continuously_monitored")
PRODUCTION_MINIMUM = "production_approved"

@dataclass(frozen=True)
class GovernanceFinding:
    finding_id: str; severity: str; object_type: str; object_id: str; message: str; required_action: str

def _stage_index(stage: str) -> int:
    if stage not in MATURITY_STAGES: raise ValueError(f"invalid maturity stage: {stage}")
    return MATURITY_STAGES.index(stage)

def evaluate_capabilities(capabilities: list[dict[str, Any]]) -> dict:
    findings=[]; counts={stage:0 for stage in MATURITY_STAGES}
    for item in capabilities:
        stage=item["maturity_stage"]; counts[stage]+=1; cid=item["capability_id"]
        if not item.get("owner_token"):
            findings.append(GovernanceFinding(f"{cid}:owner","high","capability",cid,
                "Capability has no accountable owner.","Assign an accountable professional owner."))
        if _stage_index(stage) >= _stage_index("professionally_reviewed") and not item.get("reviewer_token"):
            findings.append(GovernanceFinding(f"{cid}:reviewer","critical","capability",cid,
                "Professional maturity is claimed without a reviewer.","Record reviewer and approval evidence."))
        if _stage_index(stage) >= _stage_index(PRODUCTION_MINIMUM):
            for field in ("test_suite_id","rollback_plan_id","monitoring_plan_id"):
                if not item.get(field): findings.append(GovernanceFinding(f"{cid}:{field}","critical","capability",cid,
                    f"Production capability lacks {field}.",f"Create and approve {field}."))
    total=len(capabilities); production=sum(_stage_index(x["maturity_stage"])>=_stage_index(PRODUCTION_MINIMUM) for x in capabilities)
    return {"capability_count":total,"production_approved_count":production,
            "production_coverage_pct":round(100*production/max(1,total),1),"maturity_counts":counts,
            "findings":[asdict(x) for x in findings]}

def evaluate_rule_registry(rules: list[dict[str, Any]]) -> dict:
    findings=[]
    for rule in rules:
        rid=rule["rule_id"]; stage=rule["maturity_stage"]
        required=("objective","applicability","calculation","authorities","evidence","exceptions",
                  "materiality","tests","owner_token","effective_from")
        missing=[key for key in required if not rule.get(key)]
        if missing: findings.append(GovernanceFinding(f"{rid}:fields","high","rule",rid,
            f"Rule is missing: {', '.join(missing)}.","Complete the governed rule definition."))
        if _stage_index(stage)>=_stage_index("production_approved") and not rule.get("approval_id"):
            findings.append(GovernanceFinding(f"{rid}:approval","critical","rule",rid,
                "Production rule has no approval record.","Return rule to review or attach approval."))
        if rule.get("performance",{}).get("override_rate",0)>.30:
            findings.append(GovernanceFinding(f"{rid}:override","moderate","rule",rid,
                "Reviewer override rate exceeds 30%.","Open recalibration learning candidate."))
    return {"rule_count":len(rules),"production_rule_count":sum(x["maturity_stage"]=="production_approved" for x in rules),
            "findings":[asdict(x) for x in findings]}

def evaluate_agent_contracts(agents: list[dict[str, Any]]) -> dict:
    findings=[]
    for agent in agents:
        aid=agent["agent_id"]
        for field in ("allowed_inputs","allowed_actions","prohibited_actions","required_sources",
                      "confidence_policy","escalation_policy","human_approver"):
            if not agent.get(field): findings.append(GovernanceFinding(f"{aid}:{field}","high","agent",aid,
                f"Agent contract lacks {field}.",f"Define {field} before activation."))
    return {"agent_count":len(agents),"findings":[asdict(x) for x in findings]}

def source_change_impact(change: dict, relationships: list[dict]) -> dict:
    affected={"concept":set(),"rule":set(),"assessment":set(),"matter":set()}; frontier={change["source_id"]}
    order=("concept","rule","assessment","matter")
    for target_type in order:
        next_frontier=set()
        for edge in relationships:
            if edge["from_id"] in frontier and edge["to_type"]==target_type:
                affected[target_type].add(edge["to_id"]); next_frontier.add(edge["to_id"])
        frontier=next_frontier
    return {"change_id":change["change_id"],"source_id":change["source_id"],
            "impact_status":"review_required","affected":{k:sorted(v) for k,v in affected.items()},
            "required_actions":["Technical owner reviews changed source","Affected rules are suspended or revalidated",
                                "Material prior assessments receive impact review","Client matters receive notifications when approved"]}

def evaluate_learning_candidates(candidates: list[dict]) -> dict:
    results=[]
    for item in candidates:
        gates={"evidence_reviewed":bool(item.get("evidence_reviewed")),
               "minimum_sample_met":int(item.get("sample_size",0))>=int(item.get("minimum_sample_size",20)),
               "validation_passed":bool(item.get("validation_passed")),
               "professional_approval":bool(item.get("approval_id")),
               "pilot_passed":bool(item.get("pilot_passed")),"rollback_ready":bool(item.get("rollback_plan_id"))}
        results.append({"candidate_id":item["candidate_id"],"gates":gates,
                        "promotion_status":"eligible_for_controlled_release" if all(gates.values()) else "not_promotable",
                        "failed_gates":[k for k,v in gates.items() if not v]})
    return {"candidates":results,"automatic_promotion":False}

def measure_value(outcomes: list[dict]) -> dict:
    measures=("exposure_remediated","penalties_mitigated","hours_saved","response_days_reduced",
              "repeat_findings_reduced","client_value")
    totals={key:round(sum(float(x.get(key,0) or 0) for x in outcomes),2) for key in measures}
    return {"outcome_count":len(outcomes),"totals":totals,
            "warning":"Values require documented baselines, attribution, reviewer approval, and no double counting."}

def evaluate_decision_memory(decisions: list[dict], events: list[dict]) -> dict:
    findings=[]
    required=("decision_id","matter_id","facts_snapshot_hash","source_versions","recommendation",
              "alternatives","professional_decision","decision_maker_token","decision_date")
    for item in decisions:
        missing=[key for key in required if not item.get(key)]
        if missing: findings.append(GovernanceFinding(f"{item.get('decision_id','unknown')}:memory","high","decision",
            item.get("decision_id","unknown"),f"Decision memory is missing: {', '.join(missing)}.",
            "Complete the contemporaneous decision record."))
    event_ids=[x.get("event_id") for x in events]
    if len(event_ids)!=len(set(event_ids)):
        findings.append(GovernanceFinding("events:duplicate","critical","event_log","global",
            "Event identifiers are not unique.","Reject duplicate events and investigate integrity."))
    return {"decision_count":len(decisions),"event_count":len(events),
            "append_only_required":True,"findings":[asdict(x) for x in findings]}

def evaluate_control_plane(payload: dict[str, Any]) -> dict:
    capability=evaluate_capabilities(payload.get("capabilities",[])); rules=evaluate_rule_registry(payload.get("rules",[]))
    agents=evaluate_agent_contracts(payload.get("agents",[])); learning=evaluate_learning_candidates(payload.get("learning_candidates",[]))
    impacts=[source_change_impact(x,payload.get("relationships",[])) for x in payload.get("source_changes",[])]
    value=measure_value(payload.get("outcomes",[])); findings=capability["findings"]+rules["findings"]+agents["findings"]
    memory=evaluate_decision_memory(payload.get("decisions",[]),payload.get("events",[])); findings+=memory["findings"]
    return {"control_plane_version":CONTROL_PLANE_VERSION,"governance_status":"action_required" if findings else "controlled",
            "capabilities":capability,"rules":rules,"agents":agents,"learning":learning,
            "source_change_impacts":impacts,"decision_memory":memory,"value":value,"governance_findings":findings,
            "principles":["No automatic production promotion","No cross-client learning without authorization",
                          "Every production object requires approval, tests, monitoring, and rollback",
                          "Outcomes inform learning but do not prove legal correctness"]}

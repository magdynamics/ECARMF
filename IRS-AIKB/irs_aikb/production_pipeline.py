"""End-to-end portfolio orchestration with explicit stage gates."""
from __future__ import annotations
from .benchmark import benchmark_features
from .case_workflow import build_case_workflow
from .chief_audit import assess_chief_audit
from .penalty_defense import screen_penalties
from .reconciliation import validate_return_package

PIPELINE_VERSION = "0.4.0"

def run_case(case: dict) -> dict:
    package = case["return_package"]
    reconciliation_input = case.get("reconciliation_input") or {
        "returns": [{"return_id": package["return_id"], "form_family": package["form_family"],
                     "values": package["current_values"], "flags": package.get("flags", {})}],
        "required_schedules": case.get("required_schedules", {}),
        "supplied_schedules": case.get("supplied_schedules", {}),
    }
    reconciliation = validate_return_package(reconciliation_input)
    assessment_input = {**package, "reconciliation_report": reconciliation}
    assessment = assess_chief_audit(assessment_input)
    benchmarks = benchmark_features(assessment["features"], case.get("benchmark_cells", {}))
    penalties = screen_penalties(case.get("penalty_facts", {}))
    workflow = None
    if case.get("matter"):
        workflow = build_case_workflow({"matter": case["matter"], "notices": case.get("notices", []),
                                        "as_of_date": case.get("as_of_date"), "assessment": assessment})
    blockers = []
    if reconciliation["scoring_gate"] != "eligible": blockers.append("return_reconciliation")
    if assessment["assessment_status"] == "preliminary_only": blockers.append("assessment_completeness")
    if workflow and workflow["workflow_status"] == "blocked": blockers.append("matter_controls")
    return {"pipeline_version": PIPELINE_VERSION, "case_id": case["case_id"],
            "status": "human_review_ready" if not blockers else "blocked_or_preliminary",
            "blockers": blockers, "reconciliation": reconciliation, "assessment": assessment,
            "benchmarks": benchmarks, "penalty_and_defense": penalties, "workflow": workflow}

def run_portfolio(cases: list[dict]) -> dict:
    results = [run_case(case) for case in cases]
    results.sort(key=lambda x: x["assessment"]["scores"]["portfolio_priority"], reverse=True)
    return {"pipeline_version": PIPELINE_VERSION, "case_count": len(results), "cases": results,
            "summary": {"human_review_ready": sum(x["status"] == "human_review_ready" for x in results),
                        "blocked_or_preliminary": sum(x["status"] != "human_review_ready" for x in results)},
            "ranking_note": "Priority orders review work; it is not audit probability."}

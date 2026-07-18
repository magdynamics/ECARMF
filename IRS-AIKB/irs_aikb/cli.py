"""Command-line entry point."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from .database import database_stats, initialize, load_source_manifest
from .extraction import ingest_registry
from .engine import assess
from .intake import intake_paths, write_intake_report
from .line_mapping import map_observations, validate_official_forms, validate_registry
from .portfolio import assess_portfolio
from .reconciliation import validate_return_package
from .supporting_schedules import validate_supporting_forms
from .chief_audit import assess_chief_audit
from .case_workflow import build_case_workflow
from .efile_xml import ingest_efile_xml
from .production_pipeline import run_portfolio
from .control_plane import evaluate_control_plane
from .client_upload import evaluate_upload_session
from .sponsor_access import evaluate_sponsor_access, preview_sponsor_workspace
from .jurisdiction import evaluate_jurisdiction_readiness, module_registry
from .client_engagement import evaluate_client_engagement


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="irs-aikb")
    commands = parser.add_subparsers(dest="command", required=True)

    init = commands.add_parser("init-db", help="Create or migrate the SQLite database")
    init.add_argument("--database", type=Path, required=True)

    load = commands.add_parser("load-manifest", help="Register verified sources")
    load.add_argument("--database", type=Path, required=True)
    load.add_argument("--manifest", type=Path, required=True)

    ingest = commands.add_parser("ingest-corpus", help="Extract a registered source corpus")
    ingest.add_argument("--database", type=Path, required=True)
    ingest.add_argument("--registry", type=Path, required=True)
    ingest.add_argument("--root", type=Path, default=Path("."))
    ingest.add_argument("--retrieval-date", required=True)

    stats = commands.add_parser("stats", help="Report database object counts")
    stats.add_argument("--database", type=Path, required=True)

    run = commands.add_parser("assess", help="Assess a JSON taxpayer profile")
    run.add_argument("profile", type=Path)
    portfolio = commands.add_parser(
        "assess-portfolio", help="Assess and rank an array of normalized return profiles"
    )
    portfolio.add_argument("portfolio", type=Path)
    intake = commands.add_parser(
        "intake-returns", help="Inventory and recognize tax-return PDFs securely"
    )
    intake.add_argument("paths", type=Path, nargs="+")
    intake.add_argument("--output", type=Path, required=True)
    intake.add_argument(
        "--include-values", action="store_true",
        help="Include populated PDF field values (sensitive; metadata-only is the default)",
    )
    mapping_check = commands.add_parser(
        "validate-line-mappings", help="Validate reviewed mappings against official blank forms"
    )
    mapping_check.add_argument("--forms-root", type=Path, required=True)
    mapping_check.add_argument("--output", type=Path, required=True)
    map_lines = commands.add_parser("map-lines", help="Map extracted JSON lines to canonical concepts")
    map_lines.add_argument("input", type=Path)
    map_lines.add_argument("--output", type=Path, required=True)
    reconcile = commands.add_parser(
        "validate-return-package", help="Run arithmetic and cross-return controls"
    )
    reconcile.add_argument("package", type=Path)
    reconcile.add_argument("--output", type=Path, required=True)
    support_check = commands.add_parser(
        "validate-supporting-mappings", help="Validate schedule mappings against official IRS PDFs"
    )
    support_check.add_argument("--forms-root", type=Path, required=True)
    support_check.add_argument("--output", type=Path, required=True)
    chief = commands.add_parser(
        "chief-audit-assess", help="Run the integrated explainable audit-intelligence assessment"
    )
    chief.add_argument("package", type=Path)
    chief.add_argument("--output", type=Path, required=True)
    workflow = commands.add_parser("build-case-workflow", help="Build controlled IDR and controversy workpapers")
    workflow.add_argument("input", type=Path)
    workflow.add_argument("--output", type=Path, required=True)
    xml = commands.add_parser("ingest-efile-xml", help="Securely inventory an authorized e-file XML export")
    xml.add_argument("input", type=Path); xml.add_argument("--output", type=Path, required=True)
    xml.add_argument("--include-values", action="store_true")
    production = commands.add_parser("run-production-portfolio", help="Run the gated end-to-end portfolio pipeline")
    production.add_argument("input", type=Path); production.add_argument("--output", type=Path, required=True)
    control = commands.add_parser("evaluate-control-plane", help="Evaluate governance, learning, and value controls")
    control.add_argument("input", type=Path); control.add_argument("--output", type=Path, required=True)
    upload = commands.add_parser("evaluate-client-upload", help="Validate client upload session and completeness")
    upload.add_argument("input", type=Path); upload.add_argument("--output", type=Path, required=True)
    sponsor = commands.add_parser("evaluate-sponsor-access", help="Evaluate scoped sponsor access under client consent")
    sponsor.add_argument("input", type=Path); sponsor.add_argument("--output", type=Path, required=True)
    sponsor_preview = commands.add_parser("preview-sponsor-workspace", help="Preview exactly what a sponsor can see")
    sponsor_preview.add_argument("input", type=Path); sponsor_preview.add_argument("--output", type=Path, required=True)
    commands.add_parser("list-jurisdiction-modules", help="List registered MAG Audit jurisdiction modules")
    jurisdiction = commands.add_parser("evaluate-jurisdiction", help="Apply jurisdiction readiness and non-substitution gates")
    jurisdiction.add_argument("input", type=Path); jurisdiction.add_argument("--output", type=Path, required=True)
    engagement = commands.add_parser("evaluate-client-engagement", help="Validate client transparency, signatures, and communication controls")
    engagement.add_argument("input", type=Path); engagement.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "init-db":
        initialize(args.database)
        print(json.dumps({"database": str(args.database), "status": "initialized"}))
        return 0

    if args.command == "load-manifest":
        count = load_source_manifest(args.database, args.manifest)
        print(json.dumps({"database": str(args.database), "sources_registered": count}))
        return 0

    if args.command == "ingest-corpus":
        counts = ingest_registry(args.database, args.registry, args.root, args.retrieval_date)
        print(json.dumps(counts, indent=2, sort_keys=True))
        return 0

    if args.command == "stats":
        print(json.dumps(database_stats(args.database), indent=2, sort_keys=True))
        return 0

    if args.command == "assess-portfolio":
        profiles = json.loads(args.portfolio.read_text(encoding="utf-8"))
        print(json.dumps(assess_portfolio(profiles), indent=2, sort_keys=True))
        return 0

    if args.command == "intake-returns":
        report = intake_paths(args.paths, include_values=args.include_values)
        write_intake_report(report, args.output)
        print(json.dumps({
            "output": str(args.output),
            "security_mode": report["security_mode"],
            "file_count": report["file_count"],
            "recognized_count": report["recognized_count"],
            "review_required_count": report["review_required_count"],
            "quarantined_count": report["quarantined_count"],
        }, indent=2, sort_keys=True))
        return 0

    if args.command == "validate-line-mappings":
        registry_errors = validate_registry()
        report = validate_official_forms(args.forms_root)
        report["registry_errors"] = registry_errors
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "forms_checked": report["forms_checked"],
                          "validated": report["validated"],
                          "registry_errors": len(registry_errors)}, indent=2))
        return 1 if registry_errors else 0

    if args.command == "map-lines":
        payload = json.loads(args.input.read_text(encoding="utf-8"))
        report = map_observations(payload["form_family"], int(payload["tax_year"]),
                                  payload.get("observations", []))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "mapped": len(report["facts"]),
                          "exceptions": len(report["exceptions"])}, indent=2))
        return 0

    if args.command == "validate-return-package":
        payload = json.loads(args.package.read_text(encoding="utf-8"))
        report = validate_return_package(payload)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "analysis_status": report["analysis_status"],
                          "passed": report["passed_count"], "failed": report["failed_count"],
                          "scoring_gate": report["scoring_gate"]}, indent=2))
        return 0

    if args.command == "validate-supporting-mappings":
        report = validate_supporting_forms(args.forms_root)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "forms_checked": report["forms_checked"],
                          "validated": report["validated"]}, indent=2))
        return 0 if report["validated"] == report["forms_checked"] else 1

    if args.command == "chief-audit-assess":
        payload = json.loads(args.package.read_text(encoding="utf-8"))
        report = assess_chief_audit(payload)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "status": report["assessment_status"],
                          "findings": len(report["findings"]),
                          "portfolio_priority": report["scores"]["portfolio_priority"]}, indent=2))
        return 0

    if args.command == "build-case-workflow":
        payload = json.loads(args.input.read_text(encoding="utf-8"))
        report = build_case_workflow(payload)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "status": report["workflow_status"],
                          "workpapers": len(report["issue_workpapers"]),
                          "draft_idrs": len(report["draft_idrs"])}, indent=2))
        return 0

    if args.command == "ingest-efile-xml":
        report = ingest_efile_xml(args.input, include_values=args.include_values)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), "status": report["recognition_status"],
                          "mapped_facts": report["mapped_fact_count"], "mode": report["security_mode"]}, indent=2)); return 0
    if args.command == "run-production-portfolio":
        report = run_portfolio(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps({"output": str(args.output), **report["summary"]}, indent=2)); return 0
    if args.command == "evaluate-control-plane":
        report=evaluate_control_plane(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
        print(json.dumps({"output":str(args.output),"status":report["governance_status"],
                          "findings":len(report["governance_findings"]),
                          "production_coverage_pct":report["capabilities"]["production_coverage_pct"]},indent=2)); return 0
    if args.command == "evaluate-client-upload":
        report=evaluate_upload_session(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
        print(json.dumps({"output":str(args.output),"status":report["status"],
                          "accepted":report["accepted_count"],"rejected":report["rejected_count"],
                          "blockers":len(report["blockers"])},indent=2)); return 0
    if args.command == "evaluate-sponsor-access":
        report=evaluate_sponsor_access(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
        print(json.dumps({"output":str(args.output),"decision":report["decision"],
                          "permission":report["permission"],"reasons":len(report["reasons"])},indent=2)); return 0
    if args.command == "preview-sponsor-workspace":
        report=preview_sponsor_workspace(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
        print(json.dumps({"output":str(args.output),"visible":report["visible_count"],
                          "denied":report["denied_count"]},indent=2)); return 0
    if args.command == "list-jurisdiction-modules":
        print(json.dumps(module_registry(),indent=2,sort_keys=True)); return 0
    if args.command == "evaluate-jurisdiction":
        report=evaluate_jurisdiction_readiness(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
        print(json.dumps({"output":str(args.output),"module_id":report["module_id"],
                          "decision":report["decision"],"blockers":len(report["blockers"])},indent=2)); return 0
    if args.command == "evaluate-client-engagement":
        report=evaluate_client_engagement(json.loads(args.input.read_text(encoding="utf-8")))
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
        print(json.dumps({"output":str(args.output),"status":report["status"],
                          "actions":len(report["required_actions"]),"updates":len(report["client_updates"])},indent=2)); return 0

    profile = json.loads(args.profile.read_text(encoding="utf-8"))
    print(json.dumps(assess(profile), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

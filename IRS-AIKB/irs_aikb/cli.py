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

    profile = json.loads(args.profile.read_text(encoding="utf-8"))
    print(json.dumps(assess(profile), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

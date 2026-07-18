"""Download and verify the core IRS annual return corpus for 2018-2025."""

from __future__ import annotations

import csv
import hashlib
import subprocess
from pathlib import Path

from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[1]
CORPUS = ROOT / "sources" / "annual-income-tax-forms"
MANIFEST = ROOT / "source-manifest" / "annual_return_forms_2018_2025.csv"
YEARS = range(2018, 2026)

PRODUCTS = {
    "1040": ("f1040", "i1040gi"),
    "1040-NR": ("f1040nr", "i1040nr"),
    "1065": ("f1065", "i1065"),
    "1120": ("f1120", "i1120"),
    "1120-S": ("f1120s", "i1120s"),
    "1041": ("f1041", "i1041"),
    "1120-F": ("f1120f", "i1120f"),
    "990": ("f990", "i990"),
    "990-EZ": ("f990ez", "i990ez"),
    "990-PF": ("f990pf", "i990pf"),
    "990-T": ("f990t", "i990t"),
}


def candidates(stem: str, year: int) -> list[str]:
    return [
        f"https://www.irs.gov/pub/irs-prior/{stem}--{year}.pdf",
        f"https://www.irs.gov/pub/irs-pdf/{stem}.pdf" if year == 2025 else "",
    ]


def download(url: str, destination: Path) -> bool:
    if not url:
        return False
    destination.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        [
            "curl.exe",
            "-fL",
            "--retry",
            "3",
            "--silent",
            "--show-error",
            url,
            "-o",
            str(destination),
        ],
        check=False,
    )
    if result.returncode:
        destination.unlink(missing_ok=True)
        return False
    return True


def main() -> None:
    rows: list[dict[str, object]] = []
    for form_name, (form_stem, instruction_stem) in PRODUCTS.items():
        for year in YEARS:
            for artifact_type, stem in (
                ("form", form_stem),
                ("instructions", instruction_stem),
            ):
                destination = (
                    CORPUS
                    / form_name
                    / str(year)
                    / f"{form_name.lower()}-{year}-{artifact_type}.pdf"
                )
                source_url = ""
                for url in candidates(stem, year):
                    if download(url, destination):
                        source_url = url
                        break

                row: dict[str, object] = {
                    "form": form_name,
                    "tax_year": year,
                    "artifact_type": artifact_type,
                    "status": "downloaded" if source_url else "not_available",
                    "official_url": source_url,
                    "local_path": destination.relative_to(ROOT).as_posix(),
                    "pages": "",
                    "bytes": "",
                    "sha256": "",
                    "retrieved_date": "2026-07-17",
                }
                if source_url:
                    row["pages"] = len(PdfReader(str(destination)).pages)
                    row["bytes"] = destination.stat().st_size
                    row["sha256"] = hashlib.sha256(destination.read_bytes()).hexdigest()
                rows.append(row)
                print(form_name, year, artifact_type, row["status"], row["pages"])

    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    with MANIFEST.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    downloaded = [row for row in rows if row["status"] == "downloaded"]
    print(
        "SUMMARY",
        f"downloaded={len(downloaded)}",
        f"expected={len(rows)}",
        f"pages={sum(int(row['pages']) for row in downloaded)}",
        f"bytes={sum(int(row['bytes']) for row in downloaded)}",
    )


if __name__ == "__main__":
    main()

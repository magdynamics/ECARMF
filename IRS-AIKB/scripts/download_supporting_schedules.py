"""Download an explicit allowlist of official IRS supporting schedules."""

from __future__ import annotations

import csv
import hashlib
from datetime import date
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from pypdf import PdfReader

ROOT = Path(__file__).parents[1]
DESTINATION = ROOT / "sources" / "supporting-schedules"
MANIFEST = ROOT / "source-manifest" / "supporting_schedules_2018_2025.csv"

PRODUCTS = {
    "1040-Schedule-C": ("f1040sc", "i1040sc"),
    "1040-Schedule-E": ("f1040se", "i1040se"),
    "1040-Schedule-F": ("f1040sf", "i1040sf"),
    "1065-Schedule-K1": ("f1065sk1", "i1065sk1"),
    "1120S-Schedule-K1": ("f1120ssk", "i1120ssk"),
    "1041-Schedule-K1": ("f1041sk1", "i1041sk1"),
    "1065-Schedule-M3": ("f1065sm3", "i1065sm3"),
    "1120-Schedule-M3": ("f1120sm3", "i1120sm3"),
    "1120S-Schedule-M3": ("f1120ssm3", "i1120ssm3"),
}


def download(url: str) -> bytes:
    request = Request(url, headers={"User-Agent": "IRS-AIKB source archiver/0.1"})
    with urlopen(request, timeout=60) as response:
        return response.read()


def main() -> int:
    rows = []
    for product, stems in PRODUCTS.items():
        for year in range(2018, 2026):
            for kind, stem in zip(("form", "instructions"), stems):
                filename = f"{stem}--{year}.pdf"
                url = f"https://www.irs.gov/pub/irs-prior/{filename}"
                target = DESTINATION / product / str(year) / filename
                status, error = "downloaded", ""
                try:
                    raw = target.read_bytes() if target.exists() else download(url)
                    if not raw.startswith(b"%PDF"):
                        raise ValueError("response_is_not_pdf")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    if not target.exists():
                        target.write_bytes(raw)
                    pages = len(PdfReader(str(target)).pages)
                    digest = hashlib.sha256(raw).hexdigest()
                except HTTPError as exc:
                    raw, pages, digest, status, error = b"", 0, "", "not_available", str(exc.code)
                except Exception as exc:
                    raw, pages, digest, status, error = b"", 0, "", "error", type(exc).__name__
                rows.append({"product": product, "tax_year": year, "document_type": kind,
                             "official_url": url, "local_path": target.relative_to(ROOT).as_posix(),
                             "status": status, "sha256": digest, "page_count": pages,
                             "byte_count": len(raw), "retrieval_date": date.today().isoformat(),
                             "error": error})
                print(product, year, kind, status, pages)
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    with MANIFEST.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
        writer.writeheader(); writer.writerows(rows)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

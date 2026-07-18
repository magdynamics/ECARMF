"""Source-corpus extraction with immutable provenance and review gates."""

from __future__ import annotations

import csv
import hashlib
from html.parser import HTMLParser
from pathlib import Path
import re
import sqlite3
from typing import Iterable

from pypdf import PdfReader


AUTHORITY_PATTERNS = (
    ("IRC", re.compile(r"\b(?:IRC|I\.R\.C\.)\s*(?:§|Section)?\s*([0-9]{1,4}[A-Za-z]?(?:\([^)]+\))*)", re.I)),
    ("TREAS_REG", re.compile(r"\b(?:Treas(?:ury)?\.?\s+Reg(?:ulation)?s?\.?)\s*(?:§|Section)?\s*([0-9]+\.[0-9A-Za-z-]+(?:\([^)]+\))*)", re.I)),
    ("IRM", re.compile(r"\bIRM\s+([0-9]+(?:\.[0-9]+){1,5})", re.I)),
    ("FORM", re.compile(r"\bForm\s+([0-9]{3,5}(?:-[A-Z])?)\b", re.I)),
    ("REV_RUL", re.compile(r"\bRev\.?\s+Rul\.?\s+([0-9]{2,4}-[0-9]+)", re.I)),
    ("REV_PROC", re.compile(r"\bRev\.?\s+Proc\.?\s+([0-9]{2,4}-[0-9]+)", re.I)),
)

TECHNIQUE_TERMS = re.compile(
    r"\b(examiner|interview|reconcile|verify|inspect|analy[sz]e|test|trace|vouch|request|compare)\b",
    re.I,
)


class MainContentParser(HTMLParser):
    """Extract heading-addressable text from the HTML main element."""

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.main_depth = 0
        self.skip_depth = 0
        self.heading_tag: str | None = None
        self.heading_parts: list[str] = []
        self.heading = "Web content"
        self.body_parts: list[str] = []
        self.sections: list[tuple[str, str]] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attributes = dict(attrs)
        classes = set((attributes.get("class") or "").split())
        is_container = tag == "main" or (tag == "div" and "region-content" in classes)
        void = tag in {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"}
        if not self.main_depth and is_container:
            self.main_depth = 1
        elif self.main_depth and not void:
            self.main_depth += 1
        if self.main_depth and tag in {"script", "style", "nav", "header", "footer"}:
            self.skip_depth += 1
        if self.main_depth and not self.skip_depth and tag in {"h1", "h2", "h3", "h4"}:
            self._flush()
            self.heading_tag = tag
            self.heading_parts = []

    def handle_endtag(self, tag: str) -> None:
        if self.main_depth and tag in {"script", "style", "nav", "header", "footer"} and self.skip_depth:
            self.skip_depth -= 1
        if tag == self.heading_tag:
            heading = " ".join(self.heading_parts).strip()
            if heading:
                self.heading = re.sub(r"\s+", " ", heading)
            self.heading_tag = None
        if self.main_depth:
            self.main_depth -= 1

    def handle_data(self, data: str) -> None:
        if not self.main_depth or self.skip_depth:
            return
        clean = re.sub(r"\s+", " ", data).strip()
        if not clean:
            return
        if self.heading_tag:
            self.heading_parts.append(clean)
        else:
            self.body_parts.append(clean)

    def _flush(self) -> None:
        body = " ".join(self.body_parts).strip()
        if body:
            self.sections.append((self.heading, body))
        self.body_parts = []

    def finish(self) -> list[tuple[str, str]]:
        self._flush()
        return self.sections


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _stable_id(prefix: str, value: str) -> str:
    return f"{prefix}-{hashlib.sha1(value.encode('utf-8')).hexdigest()[:16].upper()}"


def authority_candidates(text: str) -> set[tuple[str, str]]:
    found: set[tuple[str, str]] = set()
    for authority_type, pattern in AUTHORITY_PATTERNS:
        for match in pattern.finditer(text):
            found.add((authority_type, match.group(1)))
    return found


def technique_candidates(text: str) -> Iterable[str]:
    for sentence in re.split(r"(?<=[.!?])\s+", text):
        sentence = re.sub(r"\s+", " ", sentence).strip()
        if 35 <= len(sentence) <= 600 and TECHNIQUE_TERMS.search(sentence):
            yield sentence


def _pdf_sections(path: Path) -> tuple[int, list[tuple[str, int | None, str]]]:
    reader = PdfReader(path)
    sections = []
    for page_number, page in enumerate(reader.pages, start=1):
        body = page.extract_text() or ""
        body = re.sub(r"[ \t]+", " ", body).strip()
        sections.append((f"Page {page_number}", page_number, body))
    return len(reader.pages), sections


def _html_sections(path: Path) -> tuple[None, list[tuple[str, None, str]]]:
    parser = MainContentParser()
    parser.feed(path.read_text(encoding="utf-8", errors="replace"))
    return None, [(heading, None, body) for heading, body in parser.finish()]


def ingest_registry(database: Path, registry: Path, root: Path, retrieval_date: str) -> dict[str, int]:
    """Ingest every registered source into page/heading sections and FTS."""
    connection = sqlite3.connect(database)
    counts = {"sources": 0, "versions": 0, "sections": 0, "authorities": 0, "techniques": 0, "pages": 0}
    try:
        with registry.open(encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream))
        for row in rows:
            path = root / row["local_path"]
            if not path.is_file():
                raise FileNotFoundError(f"Registered source is missing: {path}")
            digest = sha256_file(path)
            version_id = f'{row["source_id"]}:{digest[:16]}'
            if path.suffix.lower() == ".pdf":
                page_count, sections = _pdf_sections(path)
                counts["pages"] += page_count
            else:
                page_count, sections = _html_sections(path)

            connection.execute(
                """INSERT OR REPLACE INTO source
                (source_id, source_type, title, official_url, authority_class, status, last_checked_date)
                VALUES (?, ?, ?, ?, ?, ?, ?)""",
                (row["source_id"], row["source_type"], row["title"], row["official_url"],
                 "examination_aid" if row["source_type"] != "AUTHORITY" else "published_guidance",
                 row["status"], retrieval_date),
            )
            connection.execute(
                """INSERT OR REPLACE INTO source_version
                (version_id, source_id, publication_date, retrieval_date, sha256, local_path, page_count)
                VALUES (?, ?, ?, ?, ?, ?, ?)""",
                (version_id, row["source_id"], row["publication_date"], retrieval_date,
                 digest, row["local_path"], page_count),
            )
            connection.execute("DELETE FROM section_fts WHERE section_id IN (SELECT section_id FROM section WHERE version_id=?)", (version_id,))
            connection.execute("DELETE FROM technique_fts WHERE technique_id IN (SELECT technique_id FROM technique WHERE source_section_id IN (SELECT section_id FROM section WHERE version_id=?))", (version_id,))
            connection.execute("DELETE FROM technique WHERE source_section_id IN (SELECT section_id FROM section WHERE version_id=?)", (version_id,))
            connection.execute("DELETE FROM section WHERE version_id=?", (version_id,))

            for ordinal, (heading, page_number, body) in enumerate(sections, start=1):
                section_id = f"{version_id}:S{ordinal:05d}"
                connection.execute(
                    """INSERT INTO section
                    (section_id, version_id, heading, page_start, page_end, body, review_status)
                    VALUES (?, ?, ?, ?, ?, ?, 'machine_extracted')""",
                    (section_id, version_id, heading, page_number, page_number, body),
                )
                connection.execute("INSERT INTO section_fts(section_id, heading, body) VALUES (?, ?, ?)", (section_id, heading, body))
                counts["sections"] += 1
                for authority_type, citation in authority_candidates(body):
                    authority_id = _stable_id("IRS-AUT", f"{authority_type}:{citation}")
                    connection.execute(
                        "INSERT OR IGNORE INTO authority(authority_id, authority_type, citation) VALUES (?, ?, ?)",
                        (authority_id, authority_type, citation),
                    )
                    connection.execute(
                        "INSERT OR IGNORE INTO section_authority(section_id, authority_id) VALUES (?, ?)",
                        (section_id, authority_id),
                    )
                for candidate_number, candidate in enumerate(technique_candidates(body), start=1):
                    technique_id = _stable_id("IRS-TEC", f"{section_id}:{candidate_number}:{candidate}")
                    connection.execute(
                        """INSERT INTO technique
                        (technique_id, name, objective, procedure_json, source_section_id, review_status)
                        VALUES (?, ?, ?, '[]', ?, 'machine_extracted')""",
                        (technique_id, candidate[:160], candidate, section_id),
                    )
                    connection.execute(
                        "INSERT INTO technique_fts(technique_id, name, objective) VALUES (?, ?, ?)",
                        (technique_id, candidate[:160], candidate),
                    )
                    counts["techniques"] += 1
            counts["sources"] += 1
            counts["versions"] += 1

        counts["authorities"] = connection.execute("SELECT count(*) FROM authority").fetchone()[0]
        connection.commit()
        return counts
    finally:
        connection.close()

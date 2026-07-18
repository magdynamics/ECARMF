"""Database initialization helpers."""

import csv
from pathlib import Path
import sqlite3

from .canonical import CANONICAL_CONCEPTS


def initialize(database: Path, schema: Path | None = None) -> None:
    schema_paths = ([schema] if schema else
                    sorted((Path(__file__).parents[1] / "migrations").glob("*.sql")))
    database.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(database)
    try:
        for schema_path in schema_paths:
            connection.executescript(schema_path.read_text(encoding="utf-8"))
        if connection.execute(
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='canonical_concept'"
        ).fetchone():
            connection.executemany(
                """INSERT OR IGNORE INTO canonical_concept
                (concept_id, label, data_type, concept_version) VALUES (?, ?, 'money', '0.1.0')""",
                CANONICAL_CONCEPTS.items(),
            )
        connection.commit()
    finally:
        connection.close()


def load_source_manifest(database: Path, manifest: Path) -> int:
    """Register verified source files and their immutable versions."""
    connection = sqlite3.connect(database)
    count = 0
    try:
        with manifest.open(encoding="utf-8", newline="") as stream:
            for row in csv.DictReader(stream):
                connection.execute(
                    """INSERT OR REPLACE INTO source
                    (source_id, source_type, title, official_url, authority_class,
                     status, last_checked_date)
                    VALUES (?, ?, ?, ?, 'examination_aid', ?, ?)""",
                    (row["source_id"], row["source_type"], row["title"],
                     row["official_url"], row["status"], row["retrieval_date"]),
                )
                version_id = f'{row["source_id"]}:{row["retrieval_date"]}'
                connection.execute(
                    """INSERT OR REPLACE INTO source_version
                    (version_id, source_id, publication_date, retrieval_date,
                     sha256, local_path, page_count)
                    VALUES (?, ?, ?, ?, ?, ?, ?)""",
                    (version_id, row["source_id"], row["publication_date"],
                     row["retrieval_date"], row["sha256"].lower(),
                     row["local_path"], int(row["page_count"])),
                )
                count += 1
        connection.commit()
        return count
    finally:
        connection.close()


def database_stats(database: Path) -> dict[str, int | None]:
    connection = sqlite3.connect(database)
    try:
        result = {
            table: connection.execute(f"SELECT count(*) FROM {table}").fetchone()[0]
            for table in ("source", "source_version", "section", "authority", "section_authority", "technique")
        }
        result["pdf_pages"] = connection.execute("SELECT sum(page_count) FROM source_version").fetchone()[0]
        result["html_sections"] = connection.execute(
            """SELECT count(*) FROM section s
            JOIN source_version v ON s.version_id=v.version_id
            JOIN source x ON v.source_id=x.source_id
            WHERE x.source_type='ATG_WEB'"""
        ).fetchone()[0]
        result["fts_sections"] = connection.execute("SELECT count(*) FROM section_fts").fetchone()[0]
        return result
    finally:
        connection.close()

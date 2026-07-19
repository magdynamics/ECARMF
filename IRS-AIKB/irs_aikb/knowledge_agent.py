"""Grounded retrieval for MAG Audit knowledge agents.

This module returns source evidence, not an unsourced legal conclusion.  A
generation layer may summarize the evidence only when it preserves citations
and the professional-review controls carried in the response.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass
from pathlib import Path
import re
import sqlite3


@dataclass(frozen=True)
class KnowledgeEvidence:
    section_id: str
    source_id: str
    source_title: str
    heading: str
    page_start: int | None
    page_end: int | None
    excerpt: str
    official_url: str
    source_status: str
    review_status: str
    authority_class: str


def _fts_query(question: str) -> str:
    tokens = re.findall(r"[A-Za-z0-9][A-Za-z0-9_-]{1,}", question.lower())
    ignored = {"what", "which", "with", "from", "this", "that", "have", "does", "should", "could", "would", "about", "into", "their", "there", "business", "is", "are", "the", "for"}
    terms = list(dict.fromkeys(token for token in tokens if token not in ignored))[:12]
    if not terms:
        raise ValueError("The knowledge question must contain searchable terms.")
    return " OR ".join(f'"{term}"' for term in terms)


def search_knowledge(database: Path, question: str, limit: int = 8) -> dict:
    """Return a governed evidence packet from the versioned IRS corpus."""
    if not 1 <= limit <= 25:
        raise ValueError("limit must be between 1 and 25")
    query = _fts_query(question)
    connection = sqlite3.connect(database)
    connection.row_factory = sqlite3.Row
    try:
        rows = connection.execute(
            """SELECT s.section_id, x.source_id, x.title AS source_title,
                      COALESCE(s.heading, 'Untitled section') AS heading,
                      s.page_start, s.page_end,
                      snippet(section_fts, 2, '', '', ' … ', 42) AS excerpt,
                      x.official_url, x.status AS source_status,
                      s.review_status, x.authority_class
               FROM section_fts
               JOIN section s ON s.section_id=section_fts.section_id
               JOIN source_version v ON v.version_id=s.version_id
               JOIN source x ON x.source_id=v.source_id
               WHERE section_fts MATCH ?
               ORDER BY bm25(section_fts), x.status='current' DESC
               LIMIT ?""",
            (query, limit),
        ).fetchall()
    finally:
        connection.close()
    evidence = [asdict(KnowledgeEvidence(**dict(row))) for row in rows]
    return {
        "question": question,
        "retrieval_query": query,
        "evidence_count": len(evidence),
        "evidence": evidence,
        "answer_status": "evidence_ready" if evidence else "insufficient_knowledge",
        "controls": {
            "citations_required": True,
            "current_law_validation_required": True,
            "professional_review_required": True,
            "unsupported_claims_blocked": True,
        },
    }

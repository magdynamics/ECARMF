"""Launch the local MAG Audit demonstration app with no external dependencies."""
from __future__ import annotations

import argparse
from functools import partial
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path
import threading
import webbrowser
import socket
import json
from urllib.parse import parse_qs, urlparse

from irs_aikb.knowledge_agent import search_knowledge
from irs_aikb.pilot_workspace import (case_documents, case_review_status, create_client_case,
    list_cases, quarantine_upload, start_case_ai_review)


class MagAuditHandler(SimpleHTTPRequestHandler):
    """Serve the preview and its read-only, grounded knowledge endpoint."""

    database: Path
    pilot_database: Path
    pilot_vault: Path

    def end_headers(self) -> None:
        # Prevent a stale cached pilot page from appearing frozen during active builds.
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        self.send_header("Pragma", "no-cache")
        super().end_headers()

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/api/pilot/cases":
            return self._json_response(200, {"cases": list_cases(self.pilot_database),
                "pilot_only": True})
        if parsed.path.startswith("/api/pilot/cases/") and parsed.path.endswith("/documents"):
            case_id=parsed.path.split("/")[4]
            return self._json_response(200,{"case_id":case_id,
                "documents":case_documents(self.pilot_database,case_id),"pilot_only":True})
        if parsed.path.startswith("/api/pilot/cases/") and parsed.path.endswith("/review-status"):
            case_id=parsed.path.split("/")[4]
            return self._json_response(200,case_review_status(self.pilot_database,case_id))
        if parsed.path != "/api/knowledge/search":
            return super().do_GET()
        question = parse_qs(parsed.query).get("q", [""])[0]
        try:
            payload = search_knowledge(self.database, question)
            status = 200
        except ValueError as error:
            payload = {"answer_status": "invalid_question", "error": str(error)}
            status = 400
        self._json_response(status, payload)

    def do_POST(self) -> None:
        is_review=self.path.startswith("/api/pilot/cases/") and self.path.endswith("/start-ai-review")
        if self.path not in {"/api/pilot/client-case", "/api/pilot/import"} and not is_review:
            return self._json_response(404, {"error": "endpoint not found"})
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > 35_000_000:
                raise ValueError("request is empty or exceeds the pilot limit")
            payload = json.loads(self.rfile.read(length).decode("utf-8"))
            if is_review:
                case_id=self.path.split("/")[4]
                result=start_case_ai_review(self.pilot_database,case_id,payload.get("requested_by","pilot_user"))
            elif self.path == "/api/pilot/client-case":
                result = create_client_case(self.pilot_database, payload)
            else:
                result = quarantine_upload(self.pilot_database, self.pilot_vault, payload)
            self._json_response(202 if is_review else 201, result)
        except (ValueError, json.JSONDecodeError) as error:
            self._json_response(400, {"error": str(error)})

    def _json_response(self, status: int, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    app_dir = Path(__file__).resolve().parent / "app"
    url = f"http://{args.host}:{args.port}/"
    with socket.socket() as probe:
        probe.settimeout(0.25)
        if probe.connect_ex((args.host, args.port)) == 0:
            print(f"MAG Audit is already running at {url}")
            if not args.no_browser:
                webbrowser.open(url)
            return 0
    handler_class = type("ConfiguredMagAuditHandler", (MagAuditHandler,), {
        "database": Path(__file__).resolve().parent / "data" / "mainstream_atg.db",
        "pilot_database": Path(__file__).resolve().parent / "data" / "pilot_workspace.db",
        "pilot_vault": Path(__file__).resolve().parent / "tmp" / "pilot_vault",
    })
    handler = partial(handler_class, directory=str(app_dir))
    server = ThreadingHTTPServer((args.host, args.port), handler)
    print(f"MAG Audit demonstration is available at {url}")
    print("Press Ctrl+C to stop it.")
    if not args.no_browser:
        threading.Timer(0.6, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

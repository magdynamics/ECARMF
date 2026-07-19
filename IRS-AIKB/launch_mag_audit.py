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


class MagAuditHandler(SimpleHTTPRequestHandler):
    """Serve the preview and its read-only, grounded knowledge endpoint."""

    database: Path

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path != "/api/knowledge/search":
            return super().do_GET()
        question = parse_qs(parsed.query).get("q", [""])[0]
        try:
            payload = search_knowledge(self.database, question)
            status = 200
        except ValueError as error:
            payload = {"answer_status": "invalid_question", "error": str(error)}
            status = 400
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
        "database": Path(__file__).resolve().parent / "data" / "mainstream_atg.db"
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

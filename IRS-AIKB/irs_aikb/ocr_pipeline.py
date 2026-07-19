"""Governed OCRmyPDF working-copy pipeline for pilot evidence."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import subprocess

from pypdf import PdfReader

from .pilot_workspace import record_document_derivative


OCR_ARGUMENTS=("--output-type","pdf","--rotate-pages","--deskew",
               "--optimize","1","--force-ocr")


def sha256(path: Path) -> str:
    digest=hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024*1024),b""):
            digest.update(block)
    return digest.hexdigest()


def build_command(executable: Path, source: Path, output: Path, sidecar: Path) -> list[str]:
    if source.resolve() == output.resolve():
        raise ValueError("OCR output must not overwrite the original evidence")
    return [str(executable),*OCR_ARGUMENTS,"--sidecar",str(sidecar),
            str(source),str(output)]


def run_ocr(database: Path, document: dict, executable: Path, tool_env: dict[str,str]) -> dict:
    source=Path(document["local_path"])
    working=source.parent.parent/"working"
    working.mkdir(parents=True,exist_ok=True)
    output=working/f"{source.stem}_fullocr_searchable.pdf"
    sidecar=working/f"{source.stem}_fullocr.txt"
    command=build_command(executable,source,output,sidecar)
    completed=subprocess.run(command,env=tool_env,text=True,capture_output=True,check=False)
    if completed.returncode:
        raise RuntimeError(completed.stderr[-4000:] or "OCRmyPDF failed")
    reader=PdfReader(str(output))
    pages=len(reader.pages)
    searchable=sum(bool((page.extract_text() or "").strip()) for page in reader.pages)
    source_bytes=source.stat().st_size
    output_bytes=output.stat().st_size
    quality="review_ready" if pages and searchable/pages >= .9 else "manual_quality_review"
    version_result=subprocess.run([str(executable),"--version"],env=tool_env,text=True,
                                  capture_output=True,check=False)
    tool_version=(version_result.stdout or version_result.stderr).strip().splitlines()[0]
    payload={"document_id":document["document_id"],"derivative_kind":"searchable_ocr_pdf",
        "local_path":str(output),"sha256":sha256(output),"byte_count":output_bytes,
        "page_count":pages,"tool_name":"OCRmyPDF","tool_version":tool_version or None,
        "configuration":json.dumps({"arguments":OCR_ARGUMENTS}),"quality_status":quality}
    derivative=record_document_derivative(database,payload)
    return {**derivative,"original_bytes":source_bytes,"searchable_pages":searchable,
            "size_ratio":round(output_bytes/source_bytes,4),
            "size_change_pct":round((output_bytes/source_bytes-1)*100,1),
            "sidecar_path":str(sidecar)}

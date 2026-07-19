"""Create governed searchable derivatives for all PDFs in one local pilot case."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sqlite3

from irs_aikb.ocr_pipeline import run_ocr


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument("case_id")
    parser.add_argument("--database",type=Path,default=Path("data/pilot_workspace.db"))
    parser.add_argument("--ocr",type=Path,required=True)
    parser.add_argument("--tesseract",type=Path,required=True)
    args=parser.parse_args()
    connection=sqlite3.connect(args.database)
    connection.row_factory=sqlite3.Row
    try:
        documents=[dict(row) for row in connection.execute(
            "SELECT document_id,local_path FROM pilot_document WHERE case_id=? AND duplicate_of IS NULL",
            (args.case_id,)).fetchall()]
    finally:
        connection.close()
    environment=dict(os.environ)
    environment["PATH"]=str(args.tesseract)+os.pathsep+environment.get("PATH","")
    results=[]
    for document in documents:
        if Path(document["local_path"]).suffix.lower()==".pdf":
            results.append(run_ocr(args.database,document,args.ocr,environment))
    print(json.dumps({"case_id":args.case_id,"documents":results},indent=2))


if __name__ == "__main__": main()

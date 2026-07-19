"""Inspect a locally stored pilot case after its malware scan gate."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).parents[1]))

from irs_aikb.pilot_workspace import inspect_case_documents


parser=argparse.ArgumentParser()
parser.add_argument("case_id")
parser.add_argument("--database",type=Path,default=Path(__file__).parents[1]/"data"/"pilot_workspace.db")
parser.add_argument("--scan-passed",action="store_true")
args=parser.parse_args()
print(json.dumps(inspect_case_documents(args.database,args.case_id,scan_passed=args.scan_passed),indent=2))

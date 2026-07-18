"""Client portal upload-session validation and completeness controls."""
from __future__ import annotations
import re
from collections import Counter
from typing import Any

UPLOAD_VERSION="0.1.0"
ALLOWED_EXTENSIONS={".pdf",".xml",".csv",".xlsx",".xls",".txt",".zip"}
MAX_FILE_BYTES=100_000_000
CATEGORIES={
 "filed_return":"Filed federal or state return and schedules",
 "efile_export":"Authorized tax-software or MeF XML export",
 "irs_notice":"IRS notice, letter, report, or IDR",
 "books":"Trial balance, general ledger, and financial statements",
 "banking":"Bank, merchant processor, and cash records",
 "payroll":"Payroll returns, W-2s, payroll registers, and worker files",
 "information_returns":"Forms 1099, 1098, K-1, and third-party statements",
 "fixed_assets":"Fixed-asset ledger, invoices, depreciation, and cost segregation",
 "inventory":"Inventory counts, valuation, purchases, and COGS support",
 "ownership_related_party":"Ownership, basis, loans, distributions, and related-party agreements",
 "legal_correspondence":"Potentially privileged legal or controversy communication",
 "other_support":"Other issue-specific evidence"
}

def required_document_groups(scope: dict[str,Any]) -> list[dict]:
 groups=[("filed_return","Filed return with every schedule and statement"),
         ("books","Trial balance or books-to-return reconciliation")]
 if scope.get("include_income_completeness",True): groups += [("banking","Bank/deposit records"),("information_returns","Third-party information returns")]
 if scope.get("has_payroll"): groups.append(("payroll","Payroll records and filed employment returns"))
 if scope.get("has_inventory"): groups.append(("inventory","Inventory and cost-of-goods-sold support"))
 if scope.get("has_fixed_assets"): groups.append(("fixed_assets","Fixed-asset and depreciation support"))
 if scope.get("is_passthrough"): groups.append(("ownership_related_party","K-1, basis, capital, debt, and distribution support"))
 if scope.get("active_irs_matter"): groups.append(("irs_notice","Every IRS notice, envelope, report, and request"))
 return [{"category":category,"description":description,"required":True} for category,description in groups]

def evaluate_upload_session(payload: dict[str,Any]) -> dict[str,Any]:
 session=payload.get("session",{}); scope=payload.get("scope",{}); files=payload.get("files",[])
 blockers=[]; warnings=[]; accepted=[]; rejected=[]
 for key in ("session_id","matter_id","client_token","uploader_token"):
  if not session.get(key): blockers.append(f"missing_{key}")
 for key in ("engagement_authority_confirmed","consent_version","purpose_acknowledged"):
  if not session.get(key): blockers.append(key)
 if not scope.get("tax_years") or not scope.get("return_types"): blockers.append("scope_tax_years_and_return_types_required")
 seen=set()
 for item in files:
  reasons=[]; fid=item.get("file_id","unknown"); name=str(item.get("original_name","")); ext="."+name.rsplit(".",1)[-1].lower() if "." in name else ""
  if ext not in ALLOWED_EXTENSIONS: reasons.append("extension_not_allowed")
  if int(item.get("byte_count",0) or 0)<=0 or int(item.get("byte_count",0) or 0)>MAX_FILE_BYTES: reasons.append("invalid_file_size")
  digest=str(item.get("sha256","")).lower()
  if not re.fullmatch(r"[0-9a-f]{64}",digest): reasons.append("invalid_sha256")
  if digest in seen: reasons.append("duplicate_file_hash")
  seen.add(digest)
  if item.get("malware_status")!="passed": reasons.append("malware_scan_not_passed")
  if item.get("category") not in CATEGORIES: reasons.append("invalid_document_category")
  if item.get("tax_year") not in scope.get("tax_years",[]): reasons.append("outside_tax_year_scope")
  if re.search(r"\b\d{3}[-_ ]?\d{2}[-_ ]?\d{4}\b",name): warnings.append({"file_id":fid,"warning":"filename_may_contain_tin"})
  if item.get("category")=="legal_correspondence" and item.get("privilege_status") not in {"candidate","confirmed"}:
   reasons.append("legal_correspondence_requires_privilege_isolation")
  record={"file_id":fid,"category":item.get("category"),"tax_year":item.get("tax_year"),"sha256":digest,
          "status":"rejected" if reasons else "accepted_to_quarantine","reasons":reasons}
  (rejected if reasons else accepted).append(record)
 counts=Counter(x["category"] for x in accepted); requirements=required_document_groups(scope)
 completeness=[]
 for req in requirements:
  present=counts[req["category"]]>0
  completeness.append({**req,"status":"received_unreviewed" if present else "missing","file_count":counts[req["category"]]})
  if not present: blockers.append(f"missing_required_category:{req['category']}")
 status="ready_for_intake_review" if not blockers and not rejected else "action_required"
 return {"upload_version":UPLOAD_VERSION,"session_id":session.get("session_id"),"status":status,
         "accepted_count":len(accepted),"rejected_count":len(rejected),"accepted":accepted,"rejected":rejected,
         "completeness":completeness,"blockers":sorted(set(blockers)),"warnings":warnings,
         "next_step":"intake_specialist_review" if status=="ready_for_intake_review" else "client_or_staff_correction",
         "controls":{"automatic_analysis":False,"automatic_external_transmission":False,
                     "originals_immutable":True,"privilege_isolation_required":True,"human_release_required":True}}

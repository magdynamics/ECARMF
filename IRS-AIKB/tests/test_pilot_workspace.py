import base64
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from irs_aikb.pilot_workspace import (create_client_case, inspect_case_documents,
                                      list_cases, quarantine_upload, record_document_derivative,
                                      case_review_status, document_assignment_history,
                                      reassign_document, start_case_ai_review)


class PilotWorkspaceTests(unittest.TestCase):
    def test_client_case_and_quarantined_document_persist(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            case=create_client_case(database,{"client_name":"Pilot Retail LLC",
                "return_type":"1120-S","tax_year":2024})
            self.assertEqual(len(list_cases(database)),1)
            upload=quarantine_upload(database,vault,{"case_id":case["case_id"],
                "original_name":"return.pdf","content_base64":base64.b64encode(b"%PDF pilot").decode()})
            self.assertEqual(upload["status"],"awaiting_malware_scan")
            self.assertFalse(upload["analysis_allowed"])
            self.assertTrue(any(vault.rglob("*.pdf")))

    def test_duplicate_is_preserved_and_flagged(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            case=create_client_case(database,{"client_name":"Pilot"})
            payload={"case_id":case["case_id"],"original_name":"a.pdf",
                     "content_base64":base64.b64encode(b"same").decode()}
            first=quarantine_upload(database,vault,payload); second=quarantine_upload(database,vault,payload)
            self.assertEqual(second["duplicate_of"],first["document_id"])
            self.assertEqual(second["status"],"duplicate_quarantined")

    def test_path_and_type_are_controlled(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            case=create_client_case(database,{"client_name":"Pilot"})
            with self.assertRaises(ValueError):
                quarantine_upload(database,vault,{"case_id":case["case_id"],
                    "original_name":"../../run.exe","content_base64":base64.b64encode(b"x").decode()})

    def test_inspection_is_blocked_without_scan_confirmation(self):
        with TemporaryDirectory() as folder:
            database=Path(folder)/"pilot.db"
            case=create_client_case(database,{"client_name":"Pilot"})
            result=inspect_case_documents(database,case["case_id"])
            self.assertEqual(result["status"],"blocked")
            self.assertIn("malware_scan_not_confirmed",result["blockers"])

    def test_derivative_does_not_replace_original_evidence(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            case=create_client_case(database,{"client_name":"Pilot"})
            upload=quarantine_upload(database,vault,{"case_id":case["case_id"],
                "original_name":"return.pdf","content_base64":base64.b64encode(b"original").decode()})
            derivative=record_document_derivative(database,{"document_id":upload["document_id"],
                "derivative_kind":"searchable_ocr_pdf","local_path":str(root/"working.pdf"),
                "sha256":"a"*64,"byte_count":100,"page_count":18,"tool_name":"OCRmyPDF",
                "configuration":"{}","quality_status":"review_ready"})
            self.assertTrue(derivative["derivative_id"].startswith("PDER-"))

    def test_user_can_start_bounded_review_after_ocr_is_ready(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            case=create_client_case(database,{"client_name":"Pilot"})
            upload=quarantine_upload(database,vault,{"case_id":case["case_id"],
                "original_name":"return.pdf","content_base64":base64.b64encode(b"original").decode()})
            working=root/"return_fullocr_searchable.pdf"; working.write_bytes(b"working")
            (root/"return_fullocr.txt").write_text("Form 1120-S\fIllinois Department of Revenue IL-1120-ST")
            record_document_derivative(database,{"document_id":upload["document_id"],
                "derivative_kind":"searchable_ocr_pdf","local_path":str(working),
                "sha256":"b"*64,"byte_count":7,"page_count":2,"tool_name":"OCRmyPDF",
                "tool_version":"17.8.1","configuration":"{}","quality_status":"review_ready"})
            self.assertTrue(case_review_status(database,case["case_id"])["can_start_ai_review"])
            result=start_case_ai_review(database,case["case_id"])
            self.assertEqual(result["status"],"awaiting_staff_verification")
            self.assertEqual(result["classified_documents"][0]["jurisdictions"],
                             ["Federal IRS","Illinois IDOR"])

    def test_document_reassignment_preserves_bytes_and_records_custody(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            source=create_client_case(database,{"client_name":"Incorrect client"})
            target=create_client_case(database,{"client_name":"Correct client"})
            upload=quarantine_upload(database,vault,{"case_id":source["case_id"],
                "original_name":"return.pdf","content_base64":base64.b64encode(b"immutable").decode()})
            original_path=next(vault.rglob("*.pdf")); original_hash=upload["sha256"]
            result=reassign_document(database,upload["document_id"],target["case_id"],
                "Uploaded under the wrong client during intake","case_manager")
            self.assertTrue(result["original_file_unchanged"])
            self.assertEqual(original_path.read_bytes(),b"immutable")
            self.assertEqual(result["sha256"],original_hash)
            self.assertEqual(len(document_assignment_history(database,upload["document_id"])),1)
            self.assertEqual(case_review_status(database,source["case_id"])["documents"],[])
            self.assertEqual(len(case_review_status(database,target["case_id"])["documents"]),1)

    def test_reassignment_requires_reason_and_prevents_target_duplicate(self):
        with TemporaryDirectory() as folder:
            root=Path(folder); database=root/"pilot.db"; vault=root/"vault"
            source=create_client_case(database,{"client_name":"Source"})
            target=create_client_case(database,{"client_name":"Target"})
            encoded=base64.b64encode(b"same evidence").decode()
            first=quarantine_upload(database,vault,{"case_id":source["case_id"],
                "original_name":"a.pdf","content_base64":encoded})
            quarantine_upload(database,vault,{"case_id":target["case_id"],
                "original_name":"b.pdf","content_base64":encoded})
            with self.assertRaises(ValueError):
                reassign_document(database,first["document_id"],target["case_id"],"wrong","manager")
            with self.assertRaises(ValueError):
                reassign_document(database,first["document_id"],target["case_id"],
                    "Incorrect client assignment","manager")


if __name__ == "__main__": unittest.main()

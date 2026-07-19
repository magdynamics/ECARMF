import base64
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from irs_aikb.pilot_workspace import (create_client_case, inspect_case_documents,
                                      list_cases, quarantine_upload,
                                      record_document_derivative)


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


if __name__ == "__main__": unittest.main()

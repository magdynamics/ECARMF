import unittest
from irs_aikb.case_workflow import build_case_workflow

class CaseWorkflowTests(unittest.TestCase):
    def _payload(self):
        return {"as_of_date": "2026-07-18", "matter": {"matter_id": "M1", "client_token": "C1",
                "engagement_type": "readiness", "controls": {
                    "engagement_authority": True, "authorization_scope_verified": True,
                    "statute_dates_verified": True, "privilege_protocol": True,
                    "document_preservation": True, "secure_transmission": True}},
                "assessment": {"findings": [{"issue_id": "I1", "title": "Income difference",
                    "observed_fact": "Deposits exceed receipts", "evidence": ["Bank statements"],
                    "authority_refs": ["IRM 4.10.4"], "audit_technique": "Bank analysis",
                    "possible_explanations": ["Loan proceeds"]}]}}

    def test_workpapers_and_idrs_are_drafts(self):
        report = build_case_workflow(self._payload())
        self.assertEqual(report["workflow_status"], "ready_for_cpa_review")
        self.assertEqual(len(report["taxpayer_rights_checklist"]), 10)
        self.assertEqual(report["draft_idrs"][0]["production_status"], "draft_requires_approval")
        self.assertFalse(report["production_gate"]["automatic_transmission"])

    def test_deficiency_notice_and_unverified_deadline_alert(self):
        payload = self._payload()
        payload["notices"] = [{"notice_id": "N1", "notice_type": "Statutory notice of deficiency",
                               "notice_date": "2026-07-01", "response_due_date": "2026-07-20"}]
        report = build_case_workflow(payload)
        self.assertEqual(report["workflow_status"], "blocked")
        self.assertTrue(any("counsel" in x.lower() for x in report["blockers_and_alerts"]))
        self.assertEqual(report["notice_controls"][0]["verification_status"], "requires_verification")

    def test_missing_matter_controls_block(self):
        payload = self._payload(); payload["matter"]["controls"] = {}
        self.assertEqual(build_case_workflow(payload)["workflow_status"], "blocked")

if __name__ == "__main__": unittest.main()

import unittest

from irs_aikb.case_operations import evaluate_case_operations


class CaseOperationsTests(unittest.TestCase):
    def base(self):
        return {
            "case": {"matter_id":"M","client_token":"C","taxpayer_token":"T","jurisdiction_module_id":"US-IRS",
                     "case_owner_token":"S1","professional_reviewer_token":"S2"},
            "team": [{"staff_token":"S1","eligible_to_represent_before_irs":True},
                     {"staff_token":"S2","eligible_to_represent_before_irs":True}],
            "agents": [{"agent_id":"A","contract_version":"1","human_approver_token":"S2",
                        "allowed_actions":["draft"],"prohibited_actions":["external_contact"]}],
            "authorization": {"type":"2848","status":"accepted","tax_forms":["1120-S"],"tax_periods":[2024]},
            "deadlines": [{"deadline_id":"D","deadline_type":"verified_external","source_document_id":"N",
                           "verified_by_token":"S2","responsible_staff_token":"S1","status":"open"}],
            "audit_program": {"program_id":"P","program_version":"1","jurisdiction_module_id":"US-IRS",
                              "source_versions":["SRC:1"],"approved_by_token":"S2",
                              "procedures":[{"procedure_id":"P1","assigned_to_token":"S1","reviewer_token":"S2"}]},
            "requested_action": {"action":"irs_call","human_actor_token":"S1","professional_approved_by_token":"S2",
                                 "jurisdiction_module_id":"US-IRS","tax_form":"1120-S","tax_period":2024},
        }

    def test_authorized_human_irs_call_is_allowed(self):
        self.assertEqual(evaluate_case_operations(self.base())["decision"], "allow")

    def test_ai_cannot_communicate_externally(self):
        data=self.base(); data["agents"][0]["may_communicate_externally"]=True
        self.assertIn("ai_external_representation_prohibited",evaluate_case_operations(data)["blockers"])

    def test_authorization_scope_is_enforced(self):
        data=self.base(); data["requested_action"]["tax_period"]=2023
        self.assertIn("action_tax_period_outside_authorization",evaluate_case_operations(data)["blockers"])

    def test_external_deadline_requires_source_and_verifier(self):
        data=self.base(); data["deadlines"][0].pop("verified_by_token")
        self.assertTrue(any("verification_required" in x for x in evaluate_case_operations(data)["blockers"]))

    def test_high_consequence_action_requires_client_decision(self):
        data=self.base(); data["requested_action"]["action"]="submit_document"
        self.assertIn("recorded_client_decision_required",evaluate_case_operations(data)["blockers"])


if __name__ == "__main__": unittest.main()


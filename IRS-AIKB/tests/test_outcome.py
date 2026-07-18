import unittest

from irs_aikb.outcome import evaluate_outcome_chain


class OutcomeTests(unittest.TestCase):
    def base(self):
        return {
            "case":{"matter_id":"M","client_token":"C","taxpayer_token":"T","jurisdiction_module_id":"US-IRS","professional_reviewer_token":"R"},
            "findings":[{"finding_id":"F","facts_snapshot_hash":"h","evidence_ids":["E"],"source_versions":["S:1"],"rule_version":"1","confidence":.8,"professional_conclusion":"supported","reviewed_by_token":"R","jurisdiction_module_id":"US-IRS"}],
            "recommendations":[{"recommendation_id":"REC","finding_id":"F","alternatives":["A"],"expected_benefit":{"type":"readiness"},"risks":["delay"],"owner_token":"O","professional_approved_by_token":"R"}],
            "actions":[{"action_id":"A","recommendation_id":"REC","assigned_to_token":"O","status":"completed","due_at":"2026-08-01","completion_evidence_ids":["E2"]}],
            "resolutions":[{"resolution_id":"RES","action_ids":["A"],"resolution_type":"remediated","final_position":{"status":"complete"},"evidence_ids":["E2"],"verified_by_token":"R"}],
            "outcomes":[{"outcome_id":"OUT","resolution_id":"RES","outcome_type":"readiness_improved","baseline":{"status":"gap"},"method":{"type":"verified_completion"},"evidence_ids":["E2"],"attribution":{"source":"action"},"verified_by_token":"R"}],
            "deliverables":[{"deliverable_id":"D","audience":"client","professional_approved_by_token":"R"}],
        }

    def test_complete_chain_is_verified(self):
        result=evaluate_outcome_chain(self.base())
        self.assertEqual(result["status"],"verified_outcome")
        self.assertEqual(result["release_status"],"eligible_for_professional_release")

    def test_machine_finding_without_review_is_blocked(self):
        data=self.base(); data["findings"][0].pop("reviewed_by_token")
        self.assertTrue(any("reviewed_by_token" in x for x in evaluate_outcome_chain(data)["blockers"]))

    def test_completed_action_requires_evidence(self):
        data=self.base(); data["actions"][0].pop("completion_evidence_ids")
        self.assertIn("action_A_completion_evidence_required",evaluate_outcome_chain(data)["blockers"])

    def test_financial_value_requires_double_counting_review(self):
        data=self.base(); data["outcomes"][0]["financial_value"]=1000
        self.assertIn("outcome_OUT_double_counting_review_required",evaluate_outcome_chain(data)["blockers"])

    def test_tax_authority_deliverable_needs_external_approval(self):
        data=self.base(); data["deliverables"][0]["audience"]="tax_authority"
        self.assertIn("deliverable_D_external_action_approval_required",evaluate_outcome_chain(data)["blockers"])


if __name__ == "__main__": unittest.main()


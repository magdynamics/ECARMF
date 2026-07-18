import unittest

from irs_aikb.client_engagement import evaluate_client_engagement


class ClientEngagementTests(unittest.TestCase):
    def base(self):
        return {
            "client": {"client_token": "C", "preferred_channel": "portal", "language": "en",
                       "update_cadence_days": 7},
            "case": {"matter_id": "M", "case_owner_token": "O", "jurisdiction_module_id": "US-IRS"},
            "service_agreement": {"required": True, "status": "signed", "template_version": "2026.1"},
            "authorization": {"representation_required": True, "authorization_type": "2848",
                              "status": "accepted", "tax_forms": ["1120-S"], "tax_periods": [2024]},
            "events": [{"event_id": "E", "event_type": "general_status"}],
        }

    def test_controlled_case_update(self):
        result = evaluate_client_engagement(self.base())
        self.assertEqual(result["status"], "controlled")
        self.assertEqual(result["client_updates"][0]["release_status"], "eligible_for_client_release")
        self.assertFalse(result["controls"]["ai_may_sign"])

    def test_unsigned_agreement_and_poa_create_client_actions(self):
        data = self.base(); data["service_agreement"]["status"] = "sent"
        data["authorization"]["status"] = "draft"
        actions = [item["action"] for item in evaluate_client_engagement(data)["required_actions"]]
        self.assertIn("obtain_service_agreement_signature", actions)
        self.assertIn("obtain_taxpayer_authorization_signature", actions)

    def test_sensitive_and_unverified_update_is_held(self):
        data = self.base(); data["events"] = [{"event_id": "D", "event_type": "deadline",
                                               "professionally_verified": False}]
        update = evaluate_client_engagement(data)["client_updates"][0]
        self.assertEqual(update["release_status"], "hold")
        self.assertIn("professional_approval_required", update["reasons"])
        self.assertIn("unverified_deadline_cannot_be_sent_as_controlling", update["reasons"])

    def test_unverified_value_claim_is_held(self):
        data = self.base(); data["events"] = [{"event_id": "V", "event_type": "general_status",
                                               "value_amount": 100000}]
        self.assertIn("value_claim_not_verified", evaluate_client_engagement(data)["client_updates"][0]["reasons"])


if __name__ == "__main__":
    unittest.main()


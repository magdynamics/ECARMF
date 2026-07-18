import unittest

from irs_aikb.jurisdiction import evaluate_jurisdiction_readiness, module_registry


class JurisdictionTests(unittest.TestCase):
    def test_idor_space_is_registered_but_not_populated(self):
        modules = {item["module_id"]: item for item in module_registry()}
        self.assertEqual(modules["US-IL-IDOR"]["status"], "reserved_placeholder")
        self.assertEqual(modules["US-IL-IDOR"]["knowledge_status"], "not_populated")

    def test_idor_case_intake_is_allowed(self):
        result = evaluate_jurisdiction_readiness({"module_id": "US-IL-IDOR",
            "tax_type": "sales_use", "requested_action": "case_intake"})
        self.assertEqual(result["decision"], "allow_intake_only")
        self.assertFalse(result["analysis_enabled"])

    def test_idor_scoring_is_blocked_until_knowledge_approved(self):
        result = evaluate_jurisdiction_readiness({"module_id": "US-IL-IDOR",
            "tax_type": "income", "requested_action": "risk_scoring"})
        self.assertEqual(result["decision"], "block")
        self.assertIn("jurisdiction_module_not_production_approved", result["blockers"])

    def test_federal_rule_cannot_be_substituted_for_idor(self):
        result = evaluate_jurisdiction_readiness({"module_id": "US-IL-IDOR",
            "tax_type": "income", "requested_action": "case_intake", "rule_module_id": "US-IRS"})
        self.assertIn("cross_jurisdiction_rule_use_prohibited", result["blockers"])


if __name__ == "__main__":
    unittest.main()

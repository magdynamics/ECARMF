import unittest
from irs_aikb.chief_audit import assess_chief_audit, calculate_features

class ChiefAuditTests(unittest.TestCase):
    def test_zero_denominator_is_not_computable(self):
        features = calculate_features({"current_values": {"income.gross_receipts": 0,
                                                           "income.gross_profit": 10}})
        self.assertIsNone(features["gross_margin"])

    def test_integrated_findings_are_explainable_and_separate(self):
        package = {
            "return_id": "R1", "form_family": "1120-S", "tax_year": 2024,
            "current_values": {"income.gross_receipts": 100, "income.gross_profit": 20,
                               "income.total": 100, "expense.other": 30,
                               "expense.wages": 40, "balance.total_assets": 100,
                               "balance.related_party_receivable": 20,
                               "taxable_income.total": -5},
            "historical_returns": [{"tax_year": 2023, "values": {
                "income.gross_receipts": 50, "income.gross_profit": 30,
                "taxable_income.total": -2}},
                {"tax_year": 2022, "values": {"taxable_income.total": -1}}],
            "external_values": {"bank_deposits": 110, "information_returns": 103,
                                "payroll_forms_wages": 30},
            "controls": {}, "reconciliation_report": {"scoring_gate": "eligible"},
        }
        report = assess_chief_audit(package)
        ids = {x["issue_id"] for x in report["findings"]}
        self.assertTrue({"INC-BANK-001", "INC-INFO-001", "TREND-GM-001",
                         "RPT-LOAN-001", "PAY-REC-001", "LOSS-MULTI-001"} <= ids)
        self.assertIn("adjustment_exposure", report["scores"])
        self.assertIn("possible_explanations", report["findings"][0])
        self.assertGreater(len(report["evidence_request"]), 5)

    def test_incomplete_reconciliation_forces_preliminary_status(self):
        report = assess_chief_audit({"return_id": "R", "form_family": "1040", "tax_year": 2024,
                                     "current_values": {"income.total": 1}})
        self.assertEqual(report["assessment_status"], "preliminary_only")
        self.assertEqual(report["scoring_gate"], "preliminary_only")

    def test_direct_issue_facts_do_not_require_inference(self):
        report = assess_chief_audit({"return_id": "R", "form_family": "1065", "tax_year": 2024,
            "current_values": {}, "issue_facts": {"basis_support_incomplete": True,
            "foreign_form_expected_missing": True}})
        self.assertEqual({"BASIS-001", "FOR-FORMS-001"},
                         {x["issue_id"] for x in report["findings"]})

if __name__ == "__main__": unittest.main()

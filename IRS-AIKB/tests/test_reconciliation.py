import unittest

from irs_aikb.reconciliation import validate_return_package


class ReconciliationTests(unittest.TestCase):
    def test_reconciled_entity_and_k1_package_passes(self):
        report = validate_return_package({
            "returns": [{"return_id": "ENTITY-1", "values": {
                "income.gross_receipts": 1000, "income.returns_allowances": 50,
                "income.net_receipts": 950, "cogs.total": 300,
                "income.gross_profit": 650, "balance.total_assets": 500,
                "balance.total_liabilities": 200, "equity.total": 300,
            }}],
            "entity_allocation_totals": [{"entity_return_id": "ENTITY-1",
                "concept_id": "passthrough.ordinary_income", "value": 100}],
            "allocations": [
                {"entity_return_id": "ENTITY-1", "recipient_return_id": "OWNER-1",
                 "concept_id": "passthrough.ordinary_income", "value": 60},
                {"entity_return_id": "ENTITY-1", "recipient_return_id": "OWNER-2",
                 "concept_id": "passthrough.ordinary_income", "value": 40},
            ],
            "recipient_reported_values": [
                {"recipient_return_id": "OWNER-1", "concept_id": "passthrough.ordinary_income", "value": 60},
                {"recipient_return_id": "OWNER-2", "concept_id": "passthrough.ordinary_income", "value": 40},
            ],
        })
        self.assertEqual(report["failed_count"], 0)
        self.assertEqual(report["scoring_gate"], "eligible")

    def test_mismatches_and_missing_schedule_block_final_scoring(self):
        report = validate_return_package({
            "returns": [{"return_id": "R1", "values": {
                "income.gross_receipts": 100, "income.returns_allowances": 0,
                "income.net_receipts": 80, "cogs.total": 10, "income.gross_profit": 70,
                "balance.total_assets": 100, "balance.total_liabilities": 20, "equity.total": 50,
            }}],
            "required_schedules": {"R1": ["K-1"]}, "supplied_schedules": {"R1": []},
        })
        categories = {x["category"] for x in report["findings"] if x["status"] == "failed"}
        self.assertTrue({"arithmetic", "balance_sheet", "completeness"} <= categories)
        self.assertEqual(report["scoring_gate"], "preliminary_only")

    def test_blank_values_are_not_treated_as_zero(self):
        report = validate_return_package({"returns": [{"return_id": "R1", "values": {}}]})
        self.assertTrue(any(x["status"] == "not_assessable" for x in report["findings"]))
        self.assertEqual(report["scoring_gate"], "preliminary_only")


if __name__ == "__main__":
    unittest.main()

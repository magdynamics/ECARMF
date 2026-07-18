import unittest

from irs_aikb.engine import assess, classify


class EngineTests(unittest.TestCase):
    def test_low_risk_empty_profile(self):
        result = assess({})
        self.assertEqual(result["score"], 0)
        self.assertEqual(result["classification"], "low")

    def test_demo_profile_is_critical_and_explainable(self):
        result = assess({
            "bank_deposits_to_reported_receipts": 1.12,
            "information_returns_to_books": 1.04,
            "records_quality": "unreconciled",
            "cash_receipts_pct": 0.25,
            "undocumented_related_party": True,
            "worker_classification_risk": True,
        })
        self.assertEqual(result["score"], 100)
        self.assertEqual(result["classification"], "critical")
        self.assertEqual(len(result["findings"]), 6)

    def test_numeric_boolean_is_rejected(self):
        with self.assertRaises(ValueError):
            assess({"cash_receipts_pct": True})

    def test_classification_boundaries(self):
        self.assertEqual(classify(9), "low")
        self.assertEqual(classify(10), "moderate")
        self.assertEqual(classify(25), "elevated")
        self.assertEqual(classify(45), "high")
        self.assertEqual(classify(70), "critical")


if __name__ == "__main__":
    unittest.main()

import unittest

from irs_aikb.portfolio import assess_portfolio, assess_return


def complete_profile(return_id: str) -> dict:
    return {
        "return_id": return_id,
        "taxpayer_id": "CLIENT-1",
        "entity_type": "1120-S",
        "tax_year": 2024,
        "records_quality": "complete",
        "return_to_books_reconciled": True,
        "third_party_reconciled": True,
        "contemporaneous_support": True,
        "statute_dates_verified": True,
        "rights_notices_retained": True,
        "authorized_representative_identified": True,
        "privilege_protocol_established": True,
        "document_preservation_active": True,
        "return_data_complete": True,
        "prior_years_available": True,
        "books_available": True,
        "third_party_data_available": True,
        "industry_benchmark_available": True,
    }


class PortfolioEngineTests(unittest.TestCase):
    def test_complete_return_has_separate_scores(self):
        result = assess_return(complete_profile("R-LOW"))
        self.assertEqual(result["scores"]["public_selection_indicators"], 0)
        self.assertEqual(result["scores"]["adjustment_exposure"], 0)
        self.assertEqual(result["scores"]["documentation_readiness"], 100)
        self.assertEqual(result["scores"]["controversy_readiness"], 100)
        self.assertEqual(result["bands"]["assessment_confidence"], "A")

    def test_risky_return_is_explainable(self):
        profile = complete_profile("R-HIGH")
        profile.update({
            "bank_deposits_to_reported_receipts": 1.12,
            "information_returns_to_books": 1.05,
            "large_unusual_questionable_item": True,
            "unresolved_income_difference": True,
            "material_unsupported_deductions": True,
            "undocumented_related_party": True,
            "records_quality": "unreconciled",
            "return_to_books_reconciled": False,
        })
        result = assess_return(profile)
        self.assertGreaterEqual(result["scores"]["public_selection_indicators"], 45)
        self.assertGreaterEqual(result["scores"]["adjustment_exposure"], 45)
        self.assertTrue(all(finding["source_reference"] for finding in result["findings"]))
        self.assertIn("not a probability", " ".join(result["limitations"]).lower())

    def test_portfolio_ranks_review_priority(self):
        low = complete_profile("R-LOW")
        high = complete_profile("R-HIGH")
        high.update({
            "unresolved_income_difference": True,
            "material_unsupported_deductions": True,
            "related_examination": True,
            "records_quality": "missing",
            "return_to_books_reconciled": False,
            "third_party_reconciled": False,
            "contemporaneous_support": False,
        })
        result = assess_portfolio([low, high])
        self.assertEqual(result["portfolio"][0]["return_id"], "R-HIGH")
        self.assertEqual(result["return_count"], 2)

    def test_required_identity_fields(self):
        with self.assertRaises(ValueError):
            assess_return({})


if __name__ == "__main__":
    unittest.main()

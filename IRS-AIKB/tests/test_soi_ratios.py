import unittest
from pathlib import Path

from irs_aikb.soi_ratios import compare_to_soi, load_1120s_industry_ratios


class SoiRatioTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.table=Path(__file__).parents[1]/"sources"/"soi"/"corporation"/"tables"/"22co61ccr.xlsx"

    def test_retail_aggregate_ratios_are_source_backed(self):
        cohort=load_1120s_industry_ratios(self.table,"Retail trade")
        self.assertEqual(cohort["tax_year"],2022)
        self.assertEqual(cohort["form_family"],"1120-S")
        self.assertGreater(cohort["return_count_estimate"],1000)
        self.assertGreater(cohort["ratios"]["gross_margin"],0)
        self.assertLess(cohort["ratios"]["gross_margin"],1)
        self.assertIn("irs.gov",cohort["source"]["official_url"])

    def test_comparison_is_context_not_audit_probability(self):
        cohort=load_1120s_industry_ratios(self.table,"Retail trade")
        result=compare_to_soi({"gross_margin":.35,"net_margin":.04},cohort)
        self.assertEqual(result["comparison_type"],"aggregate_context")
        self.assertIn("not proof",result["warning"])

    def test_unknown_industry_is_rejected(self):
        with self.assertRaises(ValueError):
            load_1120s_industry_ratios(self.table,"Imaginary industry")


if __name__ == "__main__": unittest.main()

import tempfile, unittest
from pathlib import Path
from irs_aikb.benchmark import benchmark
from irs_aikb.efile_xml import ingest_efile_xml
from irs_aikb.penalty_defense import screen_penalties
from irs_aikb.production_pipeline import run_portfolio

class V04Tests(unittest.TestCase):
    def test_xml_is_metadata_only_and_rejects_doctype(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/"r.xml"; p.write_text("<Return><TaxYr>2024</TaxYr><IRS1120S><GrossReceiptsOrSalesAmt>10</GrossReceiptsOrSalesAmt></IRS1120S></Return>")
            result=ingest_efile_xml(p); self.assertEqual(result["form_family"],"1120-S")
            self.assertNotIn("value",result["facts"][0])
            p.write_text("<!DOCTYPE x [<!ENTITY y 'z'>]><x>&y;</x>")
            with self.assertRaises(ValueError): ingest_efile_xml(p)
    def test_benchmark_needs_sample_and_detects_outlier(self):
        self.assertEqual(benchmark(1,[1]*19,benchmark_id="b")["status"],"not_assessable")
        peers=list(range(1,31)); self.assertTrue(benchmark(100,peers,benchmark_id="b")["outlier_flag"])
    def test_penalty_is_consideration_not_conclusion(self):
        result=screen_penalties({"underpayment_identified":True,"reasonable_cause_facts":True})
        self.assertEqual(result["considerations"][0]["status"],"consideration_only")
        self.assertEqual(result["defense_development"][0]["status"],"facts_require_review")
    def test_portfolio_gate(self):
        case={"case_id":"C","return_package":{"return_id":"R","form_family":"1120","tax_year":2024,
              "current_values":{"income.gross_receipts":100,"income.returns_allowances":0,"income.net_receipts":100,
              "cogs.total":30,"income.gross_profit":70,"balance.total_assets":100,
              "balance.total_liabilities":40,"equity.total":60}},
              "supplied_schedules":{"R":["Schedule L","Schedule M-1"]}}
        result=run_portfolio([case]); self.assertEqual(result["case_count"],1)
        self.assertIn(result["cases"][0]["status"],{"human_review_ready","blocked_or_preliminary"})

if __name__=="__main__": unittest.main()

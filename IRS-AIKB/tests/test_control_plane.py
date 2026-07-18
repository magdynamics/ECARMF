import unittest
from irs_aikb.control_plane import evaluate_control_plane, source_change_impact

class ControlPlaneTests(unittest.TestCase):
 def test_false_production_claim_is_blocked(self):
  result=evaluate_control_plane({"capabilities":[{"capability_id":"C1","maturity_stage":"production_approved",
    "owner_token":"O","reviewer_token":"R"}]})
  self.assertEqual(result["governance_status"],"action_required")
  self.assertTrue(any("rollback" in x["message"] for x in result["governance_findings"]))
 def test_learning_never_auto_promotes(self):
  result=evaluate_control_plane({"learning_candidates":[{"candidate_id":"L","evidence_reviewed":True,
   "sample_size":100,"minimum_sample_size":20,"validation_passed":True,"approval_id":"A",
   "pilot_passed":True,"rollback_plan_id":"R"}]})
  self.assertFalse(result["learning"]["automatic_promotion"])
  self.assertEqual(result["learning"]["candidates"][0]["promotion_status"],"eligible_for_controlled_release")
 def test_source_change_traces_downstream(self):
  edges=[{"from_id":"S","to_id":"C","to_type":"concept"},{"from_id":"C","to_id":"R","to_type":"rule"},
         {"from_id":"R","to_id":"A","to_type":"assessment"},{"from_id":"A","to_id":"M","to_type":"matter"}]
  result=source_change_impact({"change_id":"X","source_id":"S"},edges)
  self.assertEqual(result["affected"]["matter"],["M"])
 def test_value_requires_warning_and_decision_memory(self):
  result=evaluate_control_plane({"outcomes":[{"hours_saved":5}],"decisions":[{"decision_id":"D"}]})
  self.assertEqual(result["value"]["totals"]["hours_saved"],5)
  self.assertGreater(len(result["decision_memory"]["findings"]),0)

if __name__=="__main__": unittest.main()

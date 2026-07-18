import unittest
from irs_aikb.client_upload import evaluate_upload_session

class ClientUploadTests(unittest.TestCase):
 def base(self):
  return {"session":{"session_id":"S","matter_id":"M","client_token":"C","uploader_token":"U",
    "engagement_authority_confirmed":True,"consent_version":"1","purpose_acknowledged":True},
   "scope":{"tax_years":[2024],"return_types":["1120-S"],"include_income_completeness":False},
   "files":[{"file_id":"F1","original_name":"return.pdf","byte_count":100,"sha256":"a"*64,
             "malware_status":"passed","category":"filed_return","tax_year":2024},
            {"file_id":"F2","original_name":"trial-balance.xlsx","byte_count":100,"sha256":"b"*64,
             "malware_status":"passed","category":"books","tax_year":2024}]}
 def test_complete_upload_is_quarantined_for_review(self):
  result=evaluate_upload_session(self.base()); self.assertEqual(result["status"],"ready_for_intake_review")
  self.assertTrue(all(x["status"]=="accepted_to_quarantine" for x in result["accepted"]))
  self.assertFalse(result["controls"]["automatic_analysis"])
 def test_bad_file_and_missing_consent_block(self):
  data=self.base(); data["session"]["consent_version"]=""; data["files"][0]["malware_status"]="unknown"
  result=evaluate_upload_session(data); self.assertEqual(result["status"],"action_required")
  self.assertGreater(result["rejected_count"],0); self.assertIn("consent_version",result["blockers"])
 def test_privilege_and_scope_isolation(self):
  data=self.base(); data["files"][0].update({"category":"legal_correspondence","privilege_status":"unreviewed","tax_year":2023})
  reasons=evaluate_upload_session(data)["rejected"][0]["reasons"]
  self.assertIn("outside_tax_year_scope",reasons); self.assertIn("legal_correspondence_requires_privilege_isolation",reasons)
 def test_sponsor_referral_without_access_grant_is_blocked(self):
  data=self.base(); data["session"]["uploader_role"]="sponsor"
  result=evaluate_upload_session(data)
  self.assertIn("sponsor_upload_access_denied",result["blockers"])
  self.assertEqual(result["sponsor_access_decision"]["decision"],"deny")

if __name__=="__main__": unittest.main()

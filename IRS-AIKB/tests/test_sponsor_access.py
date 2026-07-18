import unittest
from datetime import datetime, timezone

from irs_aikb.sponsor_access import evaluate_sponsor_access, preview_sponsor_workspace


class SponsorAccessTests(unittest.TestCase):
    NOW = datetime(2026, 7, 18, 12, tzinfo=timezone.utc)

    def base(self):
        return {
            "sponsor": {"sponsor_id": "SP-1", "status": "active", "security_status": "active",
                        "mfa_verified_at": "2026-07-18T11:00:00Z"},
            "referral": {"referral_id": "REF-1", "sponsor_id": "SP-1"},
            "consent": {"consent_id": "CON-1", "sponsor_id": "SP-1", "client_token": "CL-1",
                        "decision": "authorized", "status": "active", "signed_evidence_hash": "hash"},
            "grant": {"access_grant_id": "GR-1", "consent_id": "CON-1", "sponsor_id": "SP-1",
                      "client_token": "CL-1", "matter_id": "MAT-1", "status": "active",
                      "firm_approved_by_token": "STAFF-1", "permissions": ["view_status"],
                      "taxpayer_tokens": ["TP-1"], "tax_years": [2024],
                      "recertified_at": "2026-07-01T00:00:00Z",
                      "effective_at": "2026-01-01T00:00:00Z", "expires_at": "2027-01-01T00:00:00Z"},
            "request": {"client_token": "CL-1", "matter_id": "MAT-1", "taxpayer_token": "TP-1",
                        "tax_year": 2024, "permission": "view_status"},
        }

    def test_matching_consent_and_grant_allow(self):
        self.assertEqual(evaluate_sponsor_access(self.base(), now=self.NOW)["decision"], "allow")

    def test_referral_without_consent_denies(self):
        data = self.base(); data["consent"] = {}
        result = evaluate_sponsor_access(data, now=self.NOW)
        self.assertEqual(result["decision"], "deny")
        self.assertIn("active_client_consent_required", result["reasons"])
        self.assertFalse(result["referral_alone_grants_access"])

    def test_permission_and_scope_are_exact(self):
        data = self.base(); data["request"].update({"permission": "view_selected_findings", "tax_year": 2023})
        result = evaluate_sponsor_access(data, now=self.NOW)
        self.assertIn("permission_not_granted", result["reasons"])
        self.assertIn("tax_year_outside_scope", result["reasons"])

    def test_revocation_and_expiration_deny(self):
        data = self.base(); data["grant"].update({"revoked_at": "2026-07-01T00:00:00Z", "expires_at": "2026-07-01T00:00:00Z"})
        result = evaluate_sponsor_access(data, now=self.NOW)
        self.assertIn("access_revoked", result["reasons"])
        self.assertIn("access_expired", result["reasons"])

    def test_restricted_artifact_needs_specific_release(self):
        data = self.base()
        data["grant"]["permissions"] = ["view_selected_findings"]
        data["request"].update({"permission": "view_selected_findings", "artifact_class": "fraud_assessment", "artifact_id": "A-1"})
        self.assertIn("explicit_firm_release_required", evaluate_sponsor_access(data, now=self.NOW)["reasons"])
        data["release"] = {"status": "released", "artifact_id": "A-1", "sponsor_id": "SP-1"}
        self.assertEqual(evaluate_sponsor_access(data, now=self.NOW)["decision"], "allow")

    def test_download_requires_step_up_and_returns_watermark(self):
        data = self.base(); data["request"]["delivery_mode"] = "download"
        self.assertIn("step_up_verification_required", evaluate_sponsor_access(data, now=self.NOW)["reasons"])
        data["request"]["step_up_verified"] = True
        result = evaluate_sponsor_access(data, now=self.NOW)
        self.assertEqual(result["decision"], "allow")
        self.assertTrue(result["watermark_required"])
        self.assertTrue(result["client_notification_required"])

    def test_security_suspension_and_stale_recertification_deny(self):
        data = self.base(); data["sponsor"]["security_status"] = "suspended"
        data["grant"]["recertified_at"] = "2026-01-01T00:00:00Z"
        reasons = evaluate_sponsor_access(data, now=self.NOW)["reasons"]
        self.assertIn("sponsor_security_suspended", reasons)
        self.assertIn("access_recertification_required", reasons)

    def test_closed_matter_auto_denies(self):
        data = self.base(); data["request"]["matter_status"] = "closed"
        self.assertIn("matter_no_longer_active", evaluate_sponsor_access(data, now=self.NOW)["reasons"])

    def test_preview_evaluates_each_resource(self):
        data = self.base()
        data["grant"].update({"permissions": ["view_selected_documents"], "document_ids": ["DOC-1"]})
        data["resources"] = [
            {"resource_id": "DOC-1", "request": {"permission": "view_selected_documents", "document_id": "DOC-1"}},
            {"resource_id": "DOC-2", "request": {"permission": "view_selected_documents", "document_id": "DOC-2"}},
        ]
        result = preview_sponsor_workspace(data, now=self.NOW)
        self.assertEqual(result["visible_count"], 1)
        self.assertEqual(result["denied_count"], 1)


if __name__ == "__main__":
    unittest.main()

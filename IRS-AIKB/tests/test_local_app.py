import unittest
from pathlib import Path


class LocalAppTests(unittest.TestCase):
    def test_demo_app_contains_major_components(self):
        html=(Path(__file__).parents[1]/"app"/"index.html").read_text(encoding="utf-8")
        for label in ("Command center","Client onboarding","Taxpayers & cases","Case workspace",
                      "Bulk document intake","Evidence vault","Returns & reconciliation",
                      "Tasks & deadlines","Risk analysis","Knowledge research","Audit program",
                      "Rights & IRS response","Staff & AI agents","Client communications",
                      "Sponsors & consent","Findings & outcomes","Jurisdictions"):
            self.assertIn(label,html)
        self.assertIn("synthetic data only",html)
        self.assertIn("Illinois IDOR",html)

    def test_demo_app_exposes_controlled_audit_workflow(self):
        html=(Path(__file__).parents[1]/"app"/"index.html").read_text(encoding="utf-8")
        for control in ("Form 2848","IRS response deadline","human approval",
                        "originals remain immutable","cannot change scope or approve output",
                        "do not predict IRS selection"):
            self.assertIn(control,html)

    def test_demo_app_exposes_grounded_advisor_and_specialist_agents(self):
        html=(Path(__file__).parents[1]/"app"/"index.html").read_text(encoding="utf-8")
        for capability in ("MAG Knowledge Advisor","Applicable techniques","Gross-profit percentage",
                           "Taxpayer rights","Case Orchestrator","Return & Ratio Analyst",
                           "Reconciliation Specialist","Document Verification Agent"):
            self.assertIn(capability,html)

    def test_pilot_review_has_actionable_verification_state(self):
        html=(Path(__file__).parents[1]/"app"/"index.html").read_text(encoding="utf-8")
        self.assertIn("Continue staff verification",html)
        self.assertIn("openPilotVerification",html)
        self.assertIn("Pilot document verification",html)

    def test_document_integrity_correction_is_user_accessible(self):
        html=(Path(__file__).parents[1]/"app"/"index.html").read_text(encoding="utf-8")
        self.assertIn("Document ownership and integrity",html)
        self.assertIn("Move safely",html)
        self.assertIn("Assignment history",html)

    def test_launcher_exists(self):
        root=Path(__file__).parents[1]
        self.assertTrue((root/"launch_mag_audit.py").is_file())
        self.assertTrue((root/"Launch MAG Audit.cmd").is_file())

    def test_launcher_reuses_existing_local_server(self):
        launcher=(Path(__file__).parents[1]/"launch_mag_audit.py").read_text(encoding="utf-8")
        self.assertIn("connect_ex",launcher)
        self.assertIn("already running",launcher)


if __name__ == "__main__": unittest.main()

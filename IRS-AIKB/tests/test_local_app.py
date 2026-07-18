import unittest
from pathlib import Path


class LocalAppTests(unittest.TestCase):
    def test_demo_app_contains_major_components(self):
        html=(Path(__file__).parents[1]/"app"/"index.html").read_text(encoding="utf-8")
        for label in ("Command center","Clients","Case workspace","Evidence vault","Jurisdictions",
                      "Audit program","Outcomes","Client updates","Sponsors & consent"):
            self.assertIn(label,html)
        self.assertIn("synthetic data only",html)
        self.assertIn("Illinois IDOR",html)

    def test_launcher_exists(self):
        root=Path(__file__).parents[1]
        self.assertTrue((root/"launch_mag_audit.py").is_file())
        self.assertTrue((root/"Launch MAG Audit.cmd").is_file())

    def test_launcher_reuses_existing_local_server(self):
        launcher=(Path(__file__).parents[1]/"launch_mag_audit.py").read_text(encoding="utf-8")
        self.assertIn("connect_ex",launcher)
        self.assertIn("already running",launcher)


if __name__ == "__main__": unittest.main()

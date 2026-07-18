import tempfile
import unittest
from pathlib import Path

from irs_aikb.intake import identify_form, identify_tax_year, inspect_pdf, intake_paths


class IntakeTests(unittest.TestCase):
    def test_specific_form_precedes_parent_family(self):
        form, confidence = identify_form("2024 Form 1120-S U.S. Income Tax Return")
        self.assertEqual(form, "1120-S")
        self.assertIn(confidence, {"high", "medium"})

    def test_year_recognition(self):
        self.assertEqual(identify_tax_year("Form 1040 2024 U.S. Individual Income Tax Return")[0], 2024)

    def test_official_blank_form_is_recognized_without_values(self):
        root = Path(__file__).parents[1] / "sources" / "annual-income-tax-forms"
        candidates = sorted(root.rglob("*.pdf"))
        sample = next((p for p in candidates if "2024" in str(p) and "1040" in p.name.lower()), None)
        if sample is None:
            self.skipTest("Downloaded official form corpus is not present")
        record = inspect_pdf(sample)
        self.assertEqual(record.form_family, "1040")
        self.assertEqual(record.tax_year, 2024)
        self.assertIsNone(record.field_values)
        self.assertEqual(len(record.sha256), 64)

    def test_non_pdf_paths_are_ignored(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "notes.txt"
            path.write_text("Form 1040 2024", encoding="utf-8")
            report = intake_paths([path])
            self.assertEqual(report["file_count"], 0)
            self.assertEqual(report["security_mode"], "metadata_only")


if __name__ == "__main__":
    unittest.main()

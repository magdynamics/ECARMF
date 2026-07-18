import csv
import unittest
from pathlib import Path

from irs_aikb.supporting_schedules import validate_supporting_forms


class SupportingScheduleTests(unittest.TestCase):
    def test_download_manifest_hashes_and_counts(self):
        root = Path(__file__).parents[1]
        manifest = root / "source-manifest" / "supporting_schedules_2018_2025.csv"
        with manifest.open(encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream))
        downloaded = [row for row in rows if row["status"] == "downloaded"]
        self.assertEqual(len(rows), 144)
        self.assertGreaterEqual(len(downloaded), 111)
        self.assertGreater(sum(int(row["page_count"]) for row in downloaded), 1000)
        self.assertTrue(all(len(row["sha256"]) == 64 for row in downloaded))

    def test_reviewed_labels_validate_across_2018_2025(self):
        root = Path(__file__).parents[1] / "sources" / "supporting-schedules"
        report = validate_supporting_forms(root)
        self.assertEqual(report["forms_checked"], 48)
        self.assertEqual(report["validated"], 48)


if __name__ == "__main__":
    unittest.main()

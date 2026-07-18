import unittest

from irs_aikb.line_mapping import map_observations, mappings_for, validate_registry


class LineMappingTests(unittest.TestCase):
    def test_registry_has_five_core_families_for_all_years(self):
        for form in ("1040", "1041", "1065", "1120", "1120-S"):
            for year in range(2018, 2026):
                self.assertGreater(len(mappings_for(form, year)), 0, (form, year))
        self.assertEqual(validate_registry(), [])

    def test_line_changes_are_versioned(self):
        old = {m.concept_id: m.source_line for m in mappings_for("1120-S", 2022)}
        new = {m.concept_id: m.source_line for m in mappings_for("1120-S", 2023)}
        self.assertEqual(old["taxable_income.total"], "21")
        self.assertEqual(new["taxable_income.total"], "22")

    def test_exact_mapping_rejects_unknown_and_label_conflict(self):
        report = map_observations("1120", 2024, [
            {"source_line": "1a", "source_label": "Gross receipts or sales", "value": 100},
            {"source_line": "30", "source_label": "Unrelated label", "value": 20},
            {"source_line": "99", "value": 1},
        ])
        self.assertEqual(len(report["facts"]), 1)
        self.assertEqual({e["reason"] for e in report["exceptions"]},
                         {"label_conflict", "no_reviewed_mapping"})


if __name__ == "__main__":
    unittest.main()

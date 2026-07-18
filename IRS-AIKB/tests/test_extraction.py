import unittest

from irs_aikb.extraction import MainContentParser, authority_candidates, technique_candidates


class ExtractionTests(unittest.TestCase):
    def test_html_is_split_by_headings_and_navigation_is_ignored(self):
        parser = MainContentParser()
        parser.feed("""<main><nav>Ignore me</nav><h1>Guide</h1><p>Opening text.</p>
                    <h2>Income</h2><p>Reconcile receipts to bank deposits.</p></main>""")
        sections = parser.finish()
        self.assertEqual(sections[0], ("Guide", "Opening text."))
        self.assertEqual(sections[1], ("Income", "Reconcile receipts to bank deposits."))

    def test_irs_region_content_container_is_supported(self):
        parser = MainContentParser()
        parser.feed('<div class="region region-content"><h2>Records</h2><p>Inspect the ledger.</p></div>')
        self.assertEqual(parser.finish(), [("Records", "Inspect the ledger.")])

    def test_authorities_are_normalized_as_candidates(self):
        found = authority_candidates("IRC § 61; Treas. Reg. § 1.183-2; IRM 4.10.4; Form 1120.")
        self.assertIn(("IRC", "61"), found)
        self.assertIn(("TREAS_REG", "1.183-2"), found)
        self.assertIn(("IRM", "4.10.4"), found)
        self.assertIn(("FORM", "1120"), found)

    def test_technique_candidates_require_procedural_language(self):
        text = "The examiner should reconcile bank deposits to reported receipts. A short note."
        candidates = list(technique_candidates(text))
        self.assertEqual(len(candidates), 1)


if __name__ == "__main__":
    unittest.main()

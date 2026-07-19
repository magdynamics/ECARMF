import unittest
from pathlib import Path

from irs_aikb.knowledge_agent import search_knowledge


class KnowledgeAgentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.database = Path(__file__).parents[1] / "data" / "mainstream_atg.db"

    def test_retail_question_returns_grounded_sources(self):
        result = search_knowledge(self.database, "audit techniques for a retail business", 6)
        self.assertEqual(result["answer_status"], "evidence_ready")
        self.assertTrue(result["controls"]["citations_required"])
        self.assertTrue(any("retail" in item["source_title"].lower() or
                            "retail" in item["excerpt"].lower()
                            for item in result["evidence"]))

    def test_unsupported_question_is_not_fabricated(self):
        result = search_knowledge(self.database, "zzzxxyyqqq qqqvvvnnn", 3)
        self.assertEqual(result["answer_status"], "insufficient_knowledge")
        self.assertEqual(result["evidence"], [])

    def test_empty_question_is_rejected(self):
        with self.assertRaises(ValueError):
            search_knowledge(self.database, "what is this")


if __name__ == "__main__":
    unittest.main()

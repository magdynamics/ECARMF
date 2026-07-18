from pathlib import Path
import sqlite3
import tempfile
import unittest

from irs_aikb.database import initialize, load_source_manifest


class DatabaseTests(unittest.TestCase):
    def test_schema_initializes_and_is_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "aikb.db"
            initialize(database)
            initialize(database)
            schema_connection = sqlite3.connect(database)
            try:
                tables = {
                    row[0] for row in schema_connection.execute(
                        "SELECT name FROM sqlite_master WHERE type='table'"
                    )
                }
            finally:
                schema_connection.close()
            self.assertIn("return_file", tables)
            self.assertIn("return_value", tables)
            connection = sqlite3.connect(database)
            try:
                tables = {row[0] for row in connection.execute(
                    "SELECT name FROM sqlite_master WHERE type IN ('table','view')"
                )}
            finally:
                connection.close()
            self.assertTrue({"source", "source_version", "technique", "assessment"} <= tables)
            connection = sqlite3.connect(database)
            try:
                concept_count = connection.execute(
                    "SELECT count(*) FROM canonical_concept"
                ).fetchone()[0]
            finally:
                connection.close()
            self.assertGreater(concept_count, 20)

    def test_verified_manifest_loads_all_sources(self):
        manifest = Path(__file__).parents[1] / "source-manifest" / "sources.csv"
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "aikb.db"
            initialize(database)
            self.assertEqual(load_source_manifest(database, manifest), 7)
            connection = sqlite3.connect(database)
            try:
                source_count = connection.execute("SELECT count(*) FROM source").fetchone()[0]
                page_count = connection.execute(
                    "SELECT sum(page_count) FROM source_version"
                ).fetchone()[0]
            finally:
                connection.close()
            self.assertEqual(source_count, 7)
            self.assertEqual(page_count, 1125)


if __name__ == "__main__":
    unittest.main()

from pathlib import Path
import unittest

from irs_aikb.ocr_pipeline import build_command


class OcrPipelineTests(unittest.TestCase):
    def test_command_preserves_original_and_creates_sidecar(self):
        command=build_command(Path("ocrmypdf.exe"),Path("original.pdf"),
                              Path("working.pdf"),Path("working.txt"))
        self.assertEqual(command[0],"ocrmypdf.exe")
        self.assertIn("--sidecar",command)
        self.assertEqual(command[-2:], ["original.pdf","working.pdf"])

    def test_overwrite_is_refused(self):
        with self.assertRaises(ValueError):
            build_command(Path("ocrmypdf.exe"),Path("same.pdf"),Path("same.pdf"),Path("x.txt"))


if __name__ == "__main__": unittest.main()

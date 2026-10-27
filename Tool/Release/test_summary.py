"""The PR's positive Unit filter must not report success when it matched nothing."""
import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import release


class UnitSummaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def report(self, name, executed, passed=0):
        (self.root / name).write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<ResultSummary><Counters executed="{executed}" passed="{passed}" failed="0"/>'
            '</ResultSummary></TestRun>', encoding="utf-8")

    def test_missing_reports_fail(self):
        with self.assertRaisesRegex(RuntimeError, "No TRX"):
            release.summary(self.root, unit_only=True)

    def test_all_empty_reports_fail(self):
        self.report("a.trx", 0)
        self.report("b.trx", 0)
        with self.assertRaisesRegex(RuntimeError, "executed no tests"):
            release.summary(self.root, unit_only=True)

    def test_one_project_with_units_suffices_and_scope_is_explicit(self):
        self.report("no-units.trx", 0)
        self.report("units.trx", 3, 3)
        output = io.StringIO()
        with patch.dict(release.os.environ, {"GITHUB_STEP_SUMMARY": ""}), contextlib.redirect_stdout(output):
            release.summary(self.root, unit_only=True)
        self.assertIn("Linux unit test results", output.getvalue())
        self.assertIn("| units.trx | 3 | 0 | 0 |", output.getvalue())
        self.assertIn("Only Category=Unit tests ran on Linux", output.getvalue())
        self.assertNotIn("Content-dependent tests explicitly skip", output.getvalue())


if __name__ == "__main__":
    unittest.main()

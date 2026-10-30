from pathlib import Path
import json
import subprocess
import sys
import tempfile
import unittest


class FormalBaselineGateTests(unittest.TestCase):
    def test_no_real_client_sample_cannot_be_reported_as_formal_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'formal'
            result = subprocess.run([sys.executable, str(Path(__file__).with_name('run.py')),
                                     '--out', str(output)], capture_output=True, text=True)
            self.assertEqual(1, result.returncode, result.stderr)
            report = json.loads((output / 'results.json').read_text(encoding='utf-8'))
            self.assertTrue(report['formalE2E'])
            self.assertEqual(0, report['approvedCases'])
            self.assertEqual(0, report['executedCases'])
            self.assertFalse(any(row['status'] == 'passed' for row in report['results']))


if __name__ == '__main__':
    unittest.main()

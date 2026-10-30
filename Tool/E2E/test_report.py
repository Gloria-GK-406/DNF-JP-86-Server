import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

from report import write_report


class ReportContractTests(unittest.TestCase):
    def test_zero_cases_must_fail_and_leave_a_failure_report(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertFalse(write_report(directory, {'runId': 'empty'}, []))
            suite = ET.parse(Path(directory) / 'junit.xml').getroot()
            self.assertGreater(int(suite.get('failures')), 0)

    def test_dependency_block_does_not_count_as_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            results = [{'caseId': 'first', 'status': 'failed', 'detail': 'bad packet'},
                       {'caseId': 'next', 'status': 'blocked', 'detail': 'first failed'}]
            self.assertFalse(write_report(directory, {}, results))
            suite = ET.parse(Path(directory) / 'junit.xml').getroot()
            self.assertEqual('1', suite.get('failures'))
            self.assertEqual('1', suite.get('skipped'))
            self.assertEqual('blocked', json.loads((Path(directory) / 'results.json').read_text())['results'][1]['status'])

    def test_only_a_nonempty_all_passed_suite_succeeds(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertTrue(write_report(directory, {}, [{'caseId': 'one', 'status': 'passed'}]))
            suite = ET.parse(Path(directory) / 'junit.xml').getroot()
            self.assertEqual('0', suite.get('failures'))


if __name__ == '__main__':
    unittest.main()

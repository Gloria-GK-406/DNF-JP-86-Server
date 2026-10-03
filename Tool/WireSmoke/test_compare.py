import subprocess
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


class CompareTests(unittest.TestCase):
    def compare(self, different=False, incomplete=False, missing_bytes=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            reference = root / 'reference.txt'
            ours = root / 'ours.txt'
            reference.write_text('SEND cmd=0x01 type=0x0004 len=16 body=(1B) [01]\n'
                                 + 'SEND cmd=0x00 type=0x0008 len=16 body=(1B) [02]\n' * 65)
            if missing_bytes:
                reference.write_text(reference.read_text().replace('[02]', ''))
            body = '03' if different else '02'
            ours.write_text('#0 C->S CMD 0x0004 (2B) 00 00\n'
                            + ('' if incomplete else '#1 S->C CMD 0x0004 (1B) 01\n'
                               + f'#2 S->C NOTI 0x0008 (1B) {body}\n' * 65))
            return subprocess.run([sys.executable, str(HERE / 'cmpentry2.py'), str(ours),
                                   '--reference', str(reference)], capture_output=True, text=True)

    def test_identical_capture_uses_supplied_reference_without_external_enum_file(self):
        result = self.compare()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn('byte for byte identical : 66', result.stdout)

    def test_byte_difference_returns_failure(self):
        result = self.compare(different=True)
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertIn('byte for byte identical : 1', result.stdout)

    def test_missing_entry_batch_is_failure(self):
        result = self.compare(incomplete=True)
        self.assertNotEqual(0, result.returncode)

    def test_reference_without_body_bytes_cannot_report_byte_identity(self):
        self.assertNotEqual(0, self.compare(missing_bytes=True).returncode)

    def test_quest_row_dump_fails_when_database_has_no_quest_schema(self):
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / 'empty.db'
            sqlite3.connect(database).close()
            result = subprocess.run([sys.executable, str(HERE / 'questrows.py'), str(database)],
                                    capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)


if __name__ == '__main__':
    unittest.main()

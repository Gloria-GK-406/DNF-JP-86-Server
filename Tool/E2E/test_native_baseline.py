from copy import deepcopy
import json
from pathlib import Path
import unittest
import tempfile
from unittest.mock import Mock, patch

from baseline import load, verify_document
from native_cases import NativeFlow

DATASET = Path(__file__).resolve().parents[2] / 'TestAssets/E2E/a21-initial-v1'


class NativeEvidenceGuards(unittest.TestCase):
    def setUp(self):
        self.document = json.loads((DATASET / 'native-login-create-20261107.json').read_text(encoding='utf-8'))
        self.document['reviewStatus'] = 'approved'
        self.material = json.loads((DATASET / 'manifest.json').read_text(encoding='utf-8'))

    def test_candidate_and_mismatched_material_cannot_run(self):
        candidate = deepcopy(self.document)
        candidate['reviewStatus'] = 'candidate'
        with self.assertRaisesRegex(AssertionError, 'not approved'):
            verify_document(candidate, self.material)
        self.document['source']['clientPvfSha256'] = '0' * 64
        with self.assertRaisesRegex(AssertionError, 'PVF'):
            verify_document(self.document, self.material)

    def test_unsupported_case_and_arbitrary_response_mask_cannot_run(self):
        changed = deepcopy(self.document)
        changed['cases'].append(dict(caseId='TRAINING-UNREVIEWED'))
        with self.assertRaisesRegex(AssertionError, 'cases'):
            verify_document(changed, self.material)
        self.document['flows']['emptyFirst']['greeting']['unixTimeOffset'] = 0
        with self.assertRaisesRegex(AssertionError, 'Clock masking'):
            verify_document(self.document, self.material)

    def test_changed_baseline_file_is_rejected_before_execution(self):
        material = deepcopy(self.material)
        material['nativeBaselines'] = [dict(file='changed.json', sha256='0' * 64)]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'changed.json').write_text(json.dumps(self.document), encoding='utf-8')
            with patch('baseline.validate', return_value=material), self.assertRaisesRegex(AssertionError, 'hash mismatch'):
                load(path)

    def test_native_exit_waits_for_ack_before_final_flush(self):
        flow = object.__new__(NativeFlow)
        flow.bindings = {}
        flow.flow = dict(finish=dict(actions=[
            dict(direction='C2S', selector=1, opcode=3, bodyHex='00'),
            dict(direction='S2C', selector=1, opcode=3, bodyHex='01'),
            dict(direction='C2S', selector=1, opcode=0x02B3, bodyHex='00')]))
        order = []
        flow.client = Mock()
        flow.client.send.side_effect = lambda selector, opcode, body: order.append(('send', opcode))
        def receive():
            order.append(('receive', 3))
            return 1, 3, b'\x01'
        flow.client.recv_frame.side_effect = receive
        flow.phase('finish')
        self.assertEqual([('send', 3), ('receive', 3), ('send', 0x02B3)], order)


if __name__ == '__main__':
    unittest.main()

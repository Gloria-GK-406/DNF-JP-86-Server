"""Offline checks for evidence loss and accidental capture of unrelated traffic."""
import struct
import unittest
import asyncio
import io
import json
from capture_admission import Frames, select, relay


def frame(selector, opcode, body, header):
    data = bytearray(header)
    data[0] = selector
    struct.pack_into('<H', data, 1, opcode)
    struct.pack_into('<I', data, 3, header + len(body))
    return bytes(data) + body


class CaptureTests(unittest.TestCase):
    def test_every_split_of_coalesced_stream_preserves_zero_body_and_order(self):
        expected = [frame(1, 0x0010, bytes(range(15)), 14), frame(1, 0x0084, b'', 14)]
        stream = b''.join(expected)
        for split in range(len(stream) + 1):
            parser = Frames(14)
            actual = list(parser.feed(stream[:split])) + list(parser.feed(stream[split:]))
            self.assertEqual(expected, actual)

    def test_same_opcode_wrong_family_is_not_recorded(self):
        self.assertIsNone(select('S2C', frame(1, 5, b'private', 15)))
        self.assertIsNotNone(select('S2C', frame(0, 5, b'\x00\x00', 15)))

    def test_login_chat_and_character_payloads_are_excluded(self):
        for direction, header in [('C2S', 14), ('S2C', 15)]:
            for selector in [0, 1]:
                for opcode in [1, 2, 4, 0x0013, 0x0035]:
                    self.assertIsNone(select(direction, frame(selector, opcode, b'secret', header)))

    def test_refusal_has_original_body(self):
        row = select('S2C', frame(1, 0x0010, bytes.fromhex('00AD02'), 15))
        self.assertEqual('00AD02', row['body_hex'])
        self.assertEqual(3, row['body_length'])

    def test_selection_records_only_fixed_fatigue_prefix(self):
        body = b'\x01' + b'private-id' + bytes.fromhex('0000BC000000') + b'private-name'
        row = select('S2C', frame(1, 4, body, 15))
        self.assertNotIn('body_hex', row)
        self.assertEqual([{'offset': 11, 'length': 6, 'hex': '0000BC000000'}], row['body_slices'])

    def test_invalid_size_fails_before_large_buffer_allocation(self):
        for size in [0, 13, 1024 * 1024 + 1]:
            data = bytearray(14)
            struct.pack_into('<I', data, 3, size)
            with self.assertRaises(ValueError):
                list(Frames(14).feed(data))


class RelayTests(unittest.IsolatedAsyncioTestCase):
    async def test_incomplete_markers_survive_in_evidence_and_bytes_forward_unchanged(self):
        class Writer:
            def __init__(self):
                self.data = b''
                self.eof = False
            def write(self, value): self.data += value
            async def drain(self): pass
            def can_write_eof(self): return True
            def write_eof(self): self.eof = True

        for data, reason in [(b'\x00' * 14, 'ValueError'), (b'\x01\x10', 'trailing_partial_frame')]:
            reader = asyncio.StreamReader()
            reader.feed_data(data)
            reader.feed_eof()
            writer, output = Writer(), io.StringIO()
            await relay(reader, writer, 'C2S', 'test', output)
            self.assertEqual(data, writer.data)
            self.assertTrue(writer.eof)
            records = [json.loads(line) for line in output.getvalue().splitlines()]
            self.assertTrue(any(row.get('capture_status') == 'incomplete' and row['reason'] == reason
                                for row in records))


if __name__ == '__main__':
    unittest.main()

import asyncio
import io
import json
import struct
import unittest
from datetime import datetime, timedelta, timezone

from capture_admission import Frames
from fixture_admission import Fixture, encode, relay


def profile(mode='permission', **extra):
    return {'mode': mode, 'dungeon_id': 150, 'state': 1,
            'expires_utc': (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat(), **extra}


def selection():
    return encode(1, 4, b'\1' + b'private-id' + bytes.fromhex('0000BC000000') + b'private-quest-tail')


class FixtureTests(unittest.TestCase):
    def test_requires_successful_character_selection_and_preserves_other_messages(self):
        f = Fixture(profile())
        notice = encode(0, 0x1b, b'original-selection')
        self.assertEqual([(notice, None)], f.transform(notice))
        f.transform(encode(1, 4, b'\0'))
        self.assertEqual([(notice, None)], f.transform(notice))
        f.transform(selection())
        unrelated = encode(0, 0x24, b'1234567890')
        self.assertEqual([(unrelated, None)], f.transform(unrelated))
        sent = f.transform(notice)
        self.assertEqual(bytes.fromhex('01009600000001'), sent[0][0][15:])
        self.assertEqual((notice, None), sent[1])
        self.assertEqual(22, struct.unpack_from('<I', sent[0][0], 3)[0])

    def test_nonempty_permissions_fail_closed_not_overwritten(self):
        f = Fixture(profile()); f.transform(selection())
        with self.assertRaises(ValueError):
            f.transform(encode(0, 5, b'\1\0\x96\0\0'))

    def test_fatigue_changes_only_two_bytes_and_never_permissions(self):
        original = selection()
        f = Fixture(profile('fatigue', used=188))
        patched = f.transform(original)[0][0]
        self.assertEqual(original[:30], patched[:30])
        self.assertEqual(original[32:], patched[32:])
        self.assertEqual(b'\xbc\0', patched[30:32])
        notice = encode(0, 5, b'\0\0')
        self.assertEqual([(notice, None)], f.transform(notice))
        altered = bytearray(original); altered[28] = 10
        with self.assertRaises(ValueError): f.transform(bytes(altered))

    def test_expiry_disables_rewriting_and_connection_state_is_not_shared(self):
        f = Fixture(profile()); f.transform(selection())
        f.profile['expires_utc'] = '2000-01-01T00:00:00+00:00'
        notice = encode(0, 5, b'\0\0')
        self.assertEqual([(notice, None)], f.transform(notice))
        self.assertFalse(Fixture(profile()).selected)

    def test_first_fatigue_field_changes_without_changing_max_or_third_field(self):
        original = selection()
        f = Fixture(profile('fatigue', used=187, field='Fatigue'))
        patched, action = f.transform(original)[0]
        self.assertEqual(original[:26], patched[:26])
        self.assertEqual(original[28:], patched[28:])
        self.assertEqual(bytes.fromhex('BB00BC000000'), patched[26:32])
        self.assertEqual('replace_selected_fatigue_offset_11', action)


class RelayTests(unittest.IsolatedAsyncioTestCase):
    async def test_fragmented_loopback_bytes_order_and_private_capture(self):
        # Actual TCP streams verify buffering across arbitrary transport splits.
        original = selection() + encode(0, 5, b'\0\0') + encode(0, 0x1b, b'original')
        log = io.StringIO()
        async def producer(reader, writer):
            for i in range(0, len(original), 3):
                writer.write(original[i:i+3]); await writer.drain()
            writer.write_eof()
            await reader.read()
            writer.close(); await writer.wait_closed()
        async with await asyncio.start_server(producer, '127.0.0.1', 0) as source:
            reader, writer = await asyncio.open_connection('127.0.0.1', source.sockets[0].getsockname()[1])
            class Sink:
                def __init__(self): self.data = b''; self.eof = False
                def write(self, data): self.data += data
                async def drain(self): pass
                def can_write_eof(self): return True
                def write_eof(self): self.eof = True
            sink = Sink()
            await relay(reader, sink, 'S2C', 'test', log, Fixture(profile()))
            writer.close(); await writer.wait_closed()
        frames = list(Frames(15).feed(sink.data))
        self.assertEqual(4, len(frames))
        self.assertEqual(selection(), frames[0])
        self.assertEqual(encode(0, 0x1b, b'original'), frames[-1])
        self.assertTrue(sink.eof)
        self.assertNotIn('private', log.getvalue())
        self.assertNotIn(b'private-quest-tail'.hex().upper(), log.getvalue())
        self.assertEqual(2, sum(json.loads(x)['stage'] == 'fixture' for x in log.getvalue().splitlines()))


if __name__ == '__main__':
    unittest.main()

import asyncio
import json
import struct
import tempfile
import unittest
from pathlib import Path

from directory_capture_proxy import (
    CHANNEL_HEADER_LENGTH,
    GAME_CLIENT_HEADER_LENGTH,
    GAME_SERVER_HEADER_LENGTH,
    FrameReassembler,
    RecordingPolicy,
    TargetedRecorder,
    build_test_frame,
    select_game_record,
)


class DirectoryCaptureProxyTests(unittest.TestCase):
    def test_split_and_coalesced_frames_preserve_order(self) -> None:
        first = build_test_frame(1, 0x0004, b"select", GAME_CLIENT_HEADER_LENGTH)
        second = build_test_frame(1, 0x000C, b"create", GAME_CLIENT_HEADER_LENGTH)
        third = build_test_frame(1, 0x000D, b"", GAME_CLIENT_HEADER_LENGTH)

        stream = FrameReassembler(GAME_CLIENT_HEADER_LENGTH)
        self.assertEqual([], stream.feed(first[:9]))
        self.assertEqual([first, second, third], stream.feed(first[9:] + second + third))

    def test_direction_and_selector_allowlist_is_exact(self) -> None:
        accepted = [
            ("C2S", 1, 0x0004, GAME_CLIENT_HEADER_LENGTH),
            ("C2S", 1, 0x000C, GAME_CLIENT_HEADER_LENGTH),
            ("C2S", 1, 0x000D, GAME_CLIENT_HEADER_LENGTH),
            ("S2C", 0, 0x0002, GAME_SERVER_HEADER_LENGTH),
            ("S2C", 0, 0x0006, GAME_SERVER_HEADER_LENGTH),
            ("S2C", 0, 0x0009, GAME_SERVER_HEADER_LENGTH),
        ]
        for direction, selector, message_type, header_length in accepted:
            with self.subTest(direction=direction, message_type=message_type):
                frame = build_test_frame(selector, message_type, b"body", header_length)
                self.assertIsNotNone(select_game_record(direction, frame, header_length))

        rejected = [
            ("C2S", 0, 0x0004, GAME_CLIENT_HEADER_LENGTH),
            ("S2C", 1, 0x0002, GAME_SERVER_HEADER_LENGTH),
            ("C2S", 1, 0x0002, GAME_CLIENT_HEADER_LENGTH),
            ("S2C", 0, 0x000C, GAME_SERVER_HEADER_LENGTH),
            ("S2C", 0, 0x023C, GAME_SERVER_HEADER_LENGTH),
        ]
        for direction, selector, message_type, header_length in rejected:
            with self.subTest(direction=direction, message_type=message_type):
                frame = build_test_frame(selector, message_type, b"secret", header_length)
                self.assertIsNone(select_game_record(direction, frame, header_length))

    def test_channel_observation_cannot_produce_a_record(self) -> None:
        self.assertFalse(RecordingPolicy("channel", "C2S").may_record)
        self.assertFalse(RecordingPolicy("channel", "S2C").may_record)
        self.assertTrue(RecordingPolicy("game", "C2S").may_record)

    def test_recorder_writes_monotonic_event_sequence(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "frames.jsonl"
            recorder = TargetedRecorder(path)
            try:
                record = select_game_record(
                    "C2S",
                    build_test_frame(1, 0x0004, b"a", GAME_CLIENT_HEADER_LENGTH),
                    GAME_CLIENT_HEADER_LENGTH,
                )
                assert record is not None
                recorder.write("game-1", 10010, record)
                recorder.write("game-1", 10010, record)
            finally:
                recorder.close()

            lines = [json.loads(line) for line in path.read_text("utf-8").splitlines()]
            self.assertEqual([1, 2], [line["event_sequence"] for line in lines])


if __name__ == "__main__":
    unittest.main()

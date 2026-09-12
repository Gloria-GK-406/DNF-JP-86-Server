import struct
import unittest

from capture_proxy import (
    CHANNEL_HEADER_LENGTH,
    GAME_CLIENT_HEADER_LENGTH,
    GAME_SERVER_HEADER_LENGTH,
    FrameReassembler,
    select_game_record,
)


def game_frame(selector: int, message_type: int, body: bytes, header_length: int) -> bytes:
    header = bytearray(header_length)
    header[0] = selector
    struct.pack_into("<H", header, 1, message_type)
    struct.pack_into("<I", header, 3, header_length + len(body))
    if header_length == GAME_CLIENT_HEADER_LENGTH:
        struct.pack_into("<H", header, 12, 7)
    return bytes(header) + body


def channel_frame(body: bytes) -> bytes:
    header = bytearray(CHANNEL_HEADER_LENGTH)
    struct.pack_into("<I", header, 2, CHANNEL_HEADER_LENGTH + len(body))
    return bytes(header) + body


class CaptureProxyTests(unittest.TestCase):
    def test_channel_and_game_reassembly_survive_split_and_coalesced_reads(self) -> None:
        channel = FrameReassembler(CHANNEL_HEADER_LENGTH, total_length_offset=2)
        first = channel_frame(b"one")
        second = channel_frame(b"two")
        self.assertEqual([], channel.feed(first[:5]))
        self.assertEqual([first, second], channel.feed(first[5:] + second))

        game = FrameReassembler(GAME_CLIENT_HEADER_LENGTH)
        request = game_frame(1, 0x000C, bytes.fromhex("000304000000000500020000"), GAME_CLIENT_HEADER_LENGTH)
        self.assertEqual([], game.feed(request[:13]))
        self.assertEqual([request], game.feed(request[13:]))

    def test_allowlist_keeps_only_party_settings_and_complete_creation_phases(self) -> None:
        frames = [
            game_frame(0, 0x0099, b"realtime", GAME_SERVER_HEADER_LENGTH),
            game_frame(0, 0x000B, b"endpoints", GAME_SERVER_HEADER_LENGTH),
            game_frame(0, 0x0009, b"roster", GAME_SERVER_HEADER_LENGTH),
            game_frame(0, 0x0018, b"unrelated", GAME_SERVER_HEADER_LENGTH),
        ]
        stream = FrameReassembler(GAME_SERVER_HEADER_LENGTH)
        rebuilt = stream.feed(b"".join(frames))
        selected = [
            select_game_record("S2C", frame, GAME_SERVER_HEADER_LENGTH)
            for frame in rebuilt
        ]
        selected = [record for record in selected if record is not None]

        self.assertEqual(["0x0099", "0x000B", "0x0009"], [record["type"] for record in selected])

        c2s = game_frame(1, 0x000C, b"create", GAME_CLIENT_HEADER_LENGTH)
        response = game_frame(1, 0x000C, b"response", GAME_SERVER_HEADER_LENGTH)
        self.assertIsNotNone(select_game_record("C2S", c2s, GAME_CLIENT_HEADER_LENGTH))
        self.assertIsNotNone(select_game_record("S2C", response, GAME_SERVER_HEADER_LENGTH))


if __name__ == "__main__":
    unittest.main()

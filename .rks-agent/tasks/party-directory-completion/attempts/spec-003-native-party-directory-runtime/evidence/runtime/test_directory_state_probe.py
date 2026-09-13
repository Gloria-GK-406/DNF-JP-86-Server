import struct
import unittest

from directory_state_probe import (
    MAX_PARTY_CAPACITY,
    VECTOR_BEGIN_OFFSET,
    VectorValidationError,
    describe_directory_candidate,
    inspect_cnuser_object,
    inspect_directory_object,
    inspect_party_object,
    iter_value_offsets,
)


class SyntheticMemory:
    def __init__(self) -> None:
        self._segments: dict[int, bytes] = {}

    def add(self, address: int, data: bytes) -> None:
        self._segments[address] = data

    def read(self, address: int, size: int) -> bytes:
        for base, data in self._segments.items():
            offset = address - base
            if 0 <= offset and offset + size <= len(data):
                return data[offset : offset + size]
        raise OSError("unmapped synthetic range")


class DirectoryStateProbeTests(unittest.TestCase):
    def test_valid_vector_returns_four_byte_party_ids(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x31000000
        begin = 0x32000000
        party_ids = [7, 0x10203]
        memory.add(
            object_address + VECTOR_BEGIN_OFFSET,
            struct.pack("<III", begin, begin + 8, begin + 16),
        )
        memory.add(begin, struct.pack("<II", *party_ids))

        state = inspect_directory_object(object_address, memory.read)

        self.assertEqual(party_ids, state.party_ids)
        self.assertEqual(2, state.count)
        self.assertEqual(4, state.capacity)

    def test_zero_initialized_vector_is_valid_and_empty(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x31000000
        memory.add(object_address + VECTOR_BEGIN_OFFSET, bytes(12))

        state = inspect_directory_object(object_address, memory.read)

        self.assertEqual([], state.party_ids)
        self.assertEqual(0, state.capacity)

    def test_invalid_pointer_shapes_are_rejected(self) -> None:
        invalid_shapes = [
            (0x32000008, 0x32000000, 0x32000010),
            (0x32000000, 0x32000008, 0x32000004),
            (0x32000001, 0x32000005, 0x32000009),
            (0, 0x32000000, 0x32000000),
            (
                0x32000000,
                0x32000000,
                0x32000000 + (MAX_PARTY_CAPACITY + 1) * 4,
            ),
        ]
        for begin, end, capacity_end in invalid_shapes:
            with self.subTest(begin=begin, end=end, capacity_end=capacity_end):
                memory = SyntheticMemory()
                object_address = 0x31000000
                memory.add(
                    object_address + VECTOR_BEGIN_OFFSET,
                    struct.pack("<III", begin, end, capacity_end),
                )
                with self.assertRaises(VectorValidationError):
                    inspect_directory_object(object_address, memory.read)

    def test_partial_or_unreadable_vector_storage_is_rejected(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x31000000
        begin = 0x32000000
        memory.add(
            object_address + VECTOR_BEGIN_OFFSET,
            struct.pack("<III", begin, begin + 8, begin + 8),
        )
        memory.add(begin, struct.pack("<I", 7))

        with self.assertRaises(VectorValidationError):
            inspect_directory_object(object_address, memory.read)

    def test_rejected_candidate_keeps_only_pointer_diagnostics(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x31000000
        memory.add(
            object_address + VECTOR_BEGIN_OFFSET,
            struct.pack("<III", 0x32000008, 0x32000000, 0x32000010),
        )

        observation = describe_directory_candidate(object_address, memory.read)

        self.assertEqual("rejected", observation["status"])
        self.assertEqual("0x32000008", observation["begin"])
        self.assertEqual("0x32000000", observation["end"])
        self.assertEqual("VectorValidationError", observation["reason"])

    def test_value_match_offsets_include_overlap_without_duplicates(self) -> None:
        needle = bytes.fromhex("D4 FF 1F 03")
        chunks = [b"aa" + needle[:2], needle[2:] + b"bb" + needle]

        self.assertEqual([2, 8], list(iter_value_offsets(chunks, needle)))

    def test_cnuser_readback_exposes_user_and_party_association(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x31000000
        expected_vtable = 0x032F6CBC
        raw = bytearray(0x448)
        struct.pack_into("<I", raw, 0x00, expected_vtable)
        struct.pack_into("<I", raw, 0x08, 2)
        struct.pack_into("<I", raw, 0x444, 0x32001000)
        memory.add(object_address, bytes(raw))

        state = inspect_cnuser_object(
            object_address, expected_vtable, memory.read
        )

        self.assertEqual(2, state.user_id)
        self.assertEqual(0x32001000, state.party_address)

    def test_party_readback_exposes_id_roster_and_leader(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x32001000
        expected_vtable = 0x032F7380
        raw = bytearray(0x70)
        struct.pack_into("<I", raw, 0x00, expected_vtable)
        struct.pack_into("<I", raw, 0x04, 7)
        struct.pack_into("<I", raw, 0x08, 3)
        struct.pack_into("<8I", raw, 0x2C, 2, 3, 0, 0, 0, 0, 0, 0)
        struct.pack_into("<I", raw, 0x4C, 0)
        memory.add(object_address, bytes(raw))

        state = inspect_party_object(object_address, expected_vtable, memory.read)

        self.assertEqual(7, state.party_id)
        self.assertEqual(3, state.title_index)
        self.assertEqual(3, state.to_json()["title_index"])
        self.assertEqual([2, 3], state.member_user_ids)
        self.assertEqual(0, state.leader_slot)
        self.assertEqual(2, state.leader_user_id)

    def test_party_readback_excludes_ffff_empty_slots(self) -> None:
        memory = SyntheticMemory()
        object_address = 0x32001000
        expected_vtable = 0x032F7380
        raw = bytearray(0x70)
        struct.pack_into("<I", raw, 0x00, expected_vtable)
        struct.pack_into("<I", raw, 0x04, 2)
        struct.pack_into("<8I", raw, 0x2C, 1, *([0xFFFF] * 7))
        struct.pack_into("<I", raw, 0x4C, 0)
        memory.add(object_address, bytes(raw))

        state = inspect_party_object(object_address, expected_vtable, memory.read)

        self.assertEqual([1], state.member_user_ids)
        self.assertEqual(1, state.leader_user_id)

    def test_runtime_object_readback_rejects_false_code_hits(self) -> None:
        memory = SyntheticMemory()
        expected_vtable = 0x032F6CBC
        raw = bytearray(0x448)
        struct.pack_into("<I", raw, 0x00, expected_vtable)
        struct.pack_into("<I", raw, 0x08, 0x8DDB3300)
        memory.add(0x019E0A62, bytes(raw))

        with self.assertRaises(VectorValidationError):
            inspect_cnuser_object(0x019E0A62, expected_vtable, memory.read)



if __name__ == "__main__":
    unittest.main()

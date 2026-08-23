import importlib.util
import struct
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "tools" / "rebuild_analysis_pe.py"
SPEC = importlib.util.spec_from_file_location("rebuild_analysis_pe", MODULE_PATH)
rebuild_analysis_pe = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(rebuild_analysis_pe)


def build_fixture():
    pe_offset = 0x80
    optional_offset = pe_offset + 4 + 20
    section_offset = optional_offset + 0xE0
    headers = bytearray(0x400)
    headers[0:2] = b"MZ"
    struct.pack_into("<I", headers, 0x3C, pe_offset)
    headers[pe_offset:pe_offset + 4] = b"PE\0\0"
    struct.pack_into(
        "<HHIIIHH", headers, pe_offset + 4,
        0x14C, 2, 0x5601674E, 0, 0, 0xE0, 0x010F,
    )
    struct.pack_into("<H", headers, optional_offset, 0x10B)
    struct.pack_into("<I", headers, optional_offset + 16, 0x1234)
    struct.pack_into("<I", headers, optional_offset + 28, 0x00400000)
    struct.pack_into("<I", headers, optional_offset + 32, 0x1000)
    struct.pack_into("<I", headers, optional_offset + 36, 0x200)
    struct.pack_into("<I", headers, optional_offset + 56, 0x4000)
    struct.pack_into("<I", headers, optional_offset + 60, 0x200)
    struct.pack_into("<I", headers, optional_offset + 64, 0xAABBCCDD)
    struct.pack_into("<I", headers, optional_offset + 92, 16)
    struct.pack_into("<II", headers, optional_offset + 96 + 4 * 8,
                     0xDEADBEEF, 0x100)
    struct.pack_into(
        "<8sIIIIIIHHI", headers, section_offset,
        b".text\0\0\0", 0x180, 0x1000, 0x200, 0x200,
        0, 0, 0, 0, 0x60000020,
    )
    struct.pack_into(
        "<8sIIIIIIHHI", headers, section_offset + 40,
        b".bss\0\0\0\0", 0x180, 0x2000, 0, 0,
        0, 0, 0, 0, 0xC0000080,
    )
    original = bytes(headers)
    memory = bytearray(0x4000)
    memory[:0x200] = headers[:0x200]
    memory[0x1000:0x1180] = bytes((i & 0xFF) for i in range(0x180))
    memory[0x2000:0x2180] = b"B" * 0x180
    return original, bytes(memory), optional_offset, section_offset


class RebuildAnalysisPeTests(unittest.TestCase):
    def test_materializes_zero_raw_section_and_preserves_rva_bytes(self):
        original, memory, optional_offset, section_offset = build_fixture()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            original_path = root / "original.exe"
            memory_path = root / "memory.bin"
            output_path = root / "analysis.exe"
            original_path.write_bytes(original)
            memory_path.write_bytes(memory)

            summary = rebuild_analysis_pe.rebuild_analysis_pe(
                original_path, memory_path, output_path
            )
            rebuilt = output_path.read_bytes()

        text_raw_size, text_raw_pointer = struct.unpack_from(
            "<II", rebuilt, section_offset + 16
        )
        bss_raw_size, bss_raw_pointer = struct.unpack_from(
            "<II", rebuilt, section_offset + 40 + 16
        )
        self.assertEqual((text_raw_size, text_raw_pointer), (0x200, 0x200))
        self.assertEqual((bss_raw_size, bss_raw_pointer), (0x200, 0x400))
        self.assertEqual(rebuilt[text_raw_pointer:text_raw_pointer + 0x180],
                         memory[0x1000:0x1180])
        self.assertEqual(rebuilt[bss_raw_pointer:bss_raw_pointer + 0x180],
                         memory[0x2000:0x2180])
        self.assertEqual(struct.unpack_from("<I", rebuilt,
                                            optional_offset + 64)[0], 0)
        self.assertEqual(rebuilt[optional_offset + 96 + 4 * 8:
                                 optional_offset + 96 + 5 * 8], b"\0" * 8)
        self.assertEqual(struct.unpack_from("<I", rebuilt,
                                            optional_offset + 16)[0], 0x1234)
        self.assertEqual(summary["section_count"], 2)
        self.assertEqual(summary["size_of_image"], 0x4000)


if __name__ == "__main__":
    unittest.main()

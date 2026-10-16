"""Read-only evidence for the separate frame header and raw wedding structs."""
from pathlib import Path
import hashlib
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

root = Path(__file__).resolve().parents[3]
data = (root.parent / 'DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin').read_bytes()
digest = hashlib.sha256(data).hexdigest()
assert digest == '6bd2746da3858e6f29b3e2d386af4938056269bad6c9fff9455e133e7d953507'
cs = Cs(CS_ARCH_X86, CS_MODE_32)
lines = ['image_base=00400000', 'dump_sha256=' + digest]
for name, rva, size in [
    ('proposal-struct', 0x88adb2, 0x42), ('response-struct', 0x889d90, 0x50),
    ('response-button-structs', 0x88a200, 0x230), ('separate-header', 0x234d1e0, 0xf4),
    ('raw-body-copy', 0x234ced0, 0x34), ('flush-length', 0x234e7e5, 0x40),
    ('ceremony-struct', 0x8955d0, 0x260),
]:
    lines.append('=== ' + name + ' ===')
    lines.extend(f'RVA {i.address - 0x400000:08X} VA {i.address:08X} {i.mnemonic} {i.op_str}'.rstrip()
                 for i in cs.disasm(data[rva:rva + size], rva + 0x400000))
Path(__file__).with_name('raw-struct-evidence.txt').write_bytes(('\n'.join(lines) + '\n').encode('utf-8'))

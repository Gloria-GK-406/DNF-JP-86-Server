from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
import hashlib

root = Path(__file__).resolve().parents[3]
data = (root.parent / 'DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin').read_bytes()
sha = hashlib.sha256(data).hexdigest()
assert sha == '6bd2746da3858e6f29b3e2d386af4938056269bad6c9fff9455e133e7d953507'
cs = Cs(CS_ARCH_X86, CS_MODE_32)
rows = ['image_base=00400000', 'dump_sha256=' + sha]
for name, rva, size in [('03F3-create-partner', 0x892ecc, 0x38), ('construct-appearance', 0x15df5c0, 0x85),
                        ('choose-part', 0x13a52d0, 0xa5), ('empty-sentinel-and-lookup', 0x13973a0, 0x50),
                        ('asset-lookup', 0x2352a80, 0x47), ('lookup-tree', 0x13942b0, 0x1e),
                        ('lookup-tree-next', 0x13923a0, 0x30), ('fault', 0x138b900, 0x48)]:
    rows.append('=== ' + name + ' ===')
    rows.extend(f'RVA {i.address-0x400000:08X} VA {i.address:08X} {i.mnemonic} {i.op_str}'.rstrip()
                for i in cs.disasm(data[rva:rva+size], rva+0x400000))
Path(__file__).with_name('client-evidence.txt').write_bytes(('\n'.join(rows)+'\n').encode('utf-8'))

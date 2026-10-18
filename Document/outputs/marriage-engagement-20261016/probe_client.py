from pathlib import Path
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
import hashlib, struct
root = Path(__file__).resolve().parents[3]
data = (root.parent / 'DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin').read_bytes()
sha = hashlib.sha256(data).hexdigest()
assert sha == '6bd2746da3858e6f29b3e2d386af4938056269bad6c9fff9455e133e7d953507'
cs = Cs(CS_ARCH_X86, CS_MODE_32)
rows = ['image_base=00400000', 'dump_sha256=' + sha]
for name, rva, size in [('info', 0x8959b0, 0x9a), ('state-update', 0x895290, 0x160), ('menu', 0x21a68c7, 0x5e), ('state-getter', 0x10317c0, 4), ('query', 0x893080, 0xdc), ('query-response', 0x892c60, 0x369), ('grow-type', 0x15dafb0, 0x70)]:
    rows.append('=== ' + name + ' ===')
    rows.extend(f'RVA {i.address-0x400000:08X} VA {i.address:08X} {i.mnemonic} {i.op_str}'.rstrip() for i in cs.disasm(data[rva:rva+size], rva+0x400000))
rows.append('=== menu state table ===')
rows.extend(f'{n} -> RVA {struct.unpack_from("<I", data, 0x21a752c+4*n)[0]-0x400000:08X}' for n in range(8))
Path(__file__).with_name('client-evidence.txt').write_bytes(('\n'.join(rows)+'\n').encode('utf-8'))

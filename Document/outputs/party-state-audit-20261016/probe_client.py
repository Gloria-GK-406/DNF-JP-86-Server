from pathlib import Path
import hashlib, importlib.util, json
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

root = Path(__file__).resolve().parents[3]
data = (root.parent / 'DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin').read_bytes()
assert hashlib.sha256(data).hexdigest() == '6bd2746da3858e6f29b3e2d386af4938056269bad6c9fff9455e133e7d953507'
dstr = root.parent / 'DNFClient-A21-protocol/dstr.dat'
spec = importlib.util.spec_from_file_location('dstr_reader', root / 'Tool/ClientStringPatch/patch_sao_strings.py')
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
strings = reader.decode(reader.resources(dstr)[('FILE', 102, 1042)]).decode('utf-8')
values = {int(k): v for line in strings.splitlines() if '>' in line for k, v in [line.split('>', 1)] if k.isdigit()}
rows = ['image_base=00400000', 'dump_sha256=' + hashlib.sha256(data).hexdigest(),
        'dstr_sha256=' + hashlib.sha256(dstr.read_bytes()).hexdigest(), 'BBBD=' + values[0xBBBD]]
cs = Cs(CS_ARCH_X86, CS_MODE_32)
for name, rva, size in [('UDP_HOST slot and chat', 0xD65640, 0x125),
                        ('current host getter', 0x15E7480, 0x10), ('member by slot', 0x15E7750, 0x50)]:
    rows.append('=== ' + name + ' ===')
    rows.extend(f'RVA {i.address-0x400000:08X} VA {i.address:08X} {i.mnemonic} {i.op_str}'.rstrip()
                for i in cs.disasm(data[rva:rva+size], rva+0x400000))
Path(__file__).with_name('client-evidence.txt').write_bytes(('\n'.join(rows)+'\n').encode('utf-8'))

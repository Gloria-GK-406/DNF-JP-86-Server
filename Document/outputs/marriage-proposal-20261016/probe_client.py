"""Read-only A21 proposal/response evidence; never updates dstr or the client."""
from pathlib import Path
import hashlib
import importlib.util
import json
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
DUMP = ROOT.parent / 'DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin'
DSTR = ROOT.parent / 'DNFClient-A21-protocol/dstr.dat'
spec = importlib.util.spec_from_file_location('read_dstr', ROOT / 'Tool/ClientStringPatch/patch_sao_strings.py')
decoder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(decoder)
data = DUMP.read_bytes()
assert hashlib.sha256(data).hexdigest().upper() == '6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507'
strings = decoder.decode(decoder.resources(DSTR)[('FILE', 102, 1042)]).decode('utf-8')
values = {int(key): value for line in strings.splitlines() if '>' in line
          for key, value in [line.split('>', 1)] if key.isdigit()}
selected = [0x5704, 0x11c34, 0x11c37, 0x11c4d, 0x11c65, 0x11c66, 0x11c67, 0x11c68, 0x11c88, 0x11c8a]
lines = [f'dump_sha256={hashlib.sha256(data).hexdigest()}',
         f'dstr_sha256={hashlib.sha256(DSTR.read_bytes()).hexdigest()}', 'image_base=00400000']
for key in selected:
    lines.append(f'{key:08X} {values.get(key)}')
lines.append('gender_table_jobs_0_to_13=' + json.dumps(list(data[0x8916e0:0x8916ee])))
dis = Cs(CS_ARCH_X86, CS_MODE_32)
for name, rva, length in [('proposal', 0x88aa80, 0x3b0), ('reply-buttons', 0x88a200, 0x230),
                          ('request-notification', 0x891140, 0x90), ('response-notification', 0x891960, 0x150),
                          ('response-ack', 0x891fd0, 0x230), ('proposal-ack', 0x894cd0, 0x295),
                          ('gender-map', 0x8916b0, 0x3e), ('wire-id', 0x8bcec0, 5),
                          ('engagement-prompt', 0x88a0d0, 0x130)]:
    lines.append(f'=== {name} ===')
    lines.extend(f'RVA {i.address-0x400000:08X} VA {i.address:08X} {i.mnemonic} {i.op_str}'.rstrip()
                 for i in dis.disasm(data[rva:rva+length], rva+0x400000))
(OUT/'client-evidence.txt').write_bytes(('\n'.join(lines)+'\n').encode('utf-8'))
print('Read-only proposal evidence written.')

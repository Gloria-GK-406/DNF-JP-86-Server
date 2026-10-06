from pathlib import Path
import hashlib, struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
root = Path(__file__).parent
dump = Path('E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin')
data = dump.read_bytes()
base = 0x400000
md = Cs(CS_ARCH_X86, CS_MODE_32)
ranges = [
 ('manager construct',0x4d29a0,0x4d29eb),
 ('manager handlers',0x4d2870,0x4d299a),
 ('shared service reset',0x5b64c0,0x5b64c8),
 ('open state gate',0x4f6050,0x4f6145),
 ('generic event factory',0xc5e180,0xc5e26a),
 ('bet success update',0x4f4300,0x4f4640),
 ('mail new notice entry',0x1189cc0,0x1189d38),
 ('mail list entry',0x1198e70,0x1199090),
 ('mail attachment ACK',0x111b340,0x111b390),
]
with (root/'coverage-raw.txt').open('w',encoding='utf-8') as out:
 out.write(f'Fixed dump SHA256={hashlib.sha256(data).hexdigest().upper()}\nVA base={base:08X}; RVA=VA-base\n')
 for name,a,b in ranges:
  out.write(f'\n## {name} VA {a:08X}..{b:08X}\n')
  for i in md.disasm(data[a-base:b-base],a):
   out.write(f'{i.address:08X} RVA={i.address-base:08X} {i.bytes.hex():22} {i.mnemonic:8} {i.op_str}\n')
 out.write('\n## Raw rel32 target candidates: shared inventory helpers (not complete CFG)\n')
 targets={0x16ef580:[],0x16ee6c0:[]}
 for p in range(min(len(data)-5,0x2c00000)):
  if data[p] in (0xe8,0xe9):
   target=base+p+5+struct.unpack_from('<i',data,p+1)[0]
   if target in targets: targets[target].append(base+p)
 for target,sites in targets.items():
  out.write(f'target VA={target:08X}: {len(sites)} raw E8/E9 candidates\n')
  out.write(' '.join(f'{a:08X}' for a in sites)+'\n')
print(root/'coverage-raw.txt')

from pathlib import Path
import argparse, struct, hashlib
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
BASE=0x400000
DUMP=Path('E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin')
p=argparse.ArgumentParser();p.add_argument('--ranges',nargs='*',default=[]);p.add_argument('--refs',nargs='*',default=[]);p.add_argument('--values',nargs='*',default=[]);p.add_argument('--push',nargs='*',default=[]);p.add_argument('--strings',nargs='*',default=[]);a=p.parse_args()
b=DUMP.read_bytes();md=Cs(CS_ARCH_X86,CS_MODE_32)
print('dump',DUMP,'sha256',hashlib.sha256(b).hexdigest().upper())
def ds(start,end):
 print(f'\nVA {start:08X}:{end:08X}')
 for i in md.disasm(b[start-BASE:end-BASE],start): print(f'{i.address:08X} {i.bytes.hex():<20} {i.mnemonic:<8} {i.op_str}')
for r in a.ranges:
 s,e=(int(x,0) for x in r.split(':'));ds(s,e)
for t in a.refs:
 target=int(t,0);print(f'\nREF {target:08X}')
 for opcode in (0xe8,0xe9):
  off=0
  while True:
   off=b.find(bytes([opcode]),off)
   if off<0:break
   if off+5<=len(b) and (BASE+off+5+struct.unpack_from('<i',b,off+1)[0])&0xffffffff==target:
    ds(BASE+max(0,off-30),BASE+off+35)
   off+=1
 v=struct.pack('<I',target);off=0
 while True:
  off=b.find(v,off)
  if off<0:break
  print('absolute',hex(BASE+off));off+=1
for t in a.values:
 target=int(t,0);v=struct.pack('<I',target);print(f'\nVALUE {target:08X}');off=0
 while True:
  off=b.find(v,off)
  if off<0:break
  ds(BASE+max(0,off-12),BASE+off+25);off+=1
for t in a.push:
 target=int(t,0);v=b'\x68'+struct.pack('<I',target);print(f'\nPUSH {target:08X}');off=0
 while True:
  off=b.find(v,off)
  if off<0:break
  if off<0x2900000: ds(BASE+off,BASE+off+45)
  off+=1
for t in a.strings:
 off=int(t,0)-BASE;print(hex(off+BASE),repr(b[off:off+200].split(b'\0')[0]))

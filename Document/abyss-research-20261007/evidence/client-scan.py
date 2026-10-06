from pathlib import Path
import argparse, struct, hashlib, re
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
P=Path('E:/Project/DNF/DNFClient-A21-protocol2/DNF.unpacked-b1-debug.exe')
p=argparse.ArgumentParser(); p.add_argument('--ranges',nargs='*',default=[]);p.add_argument('--refs',nargs='*',default=[]);p.add_argument('--strings',action='store_true');p.add_argument('--pattern',default='hell|abyss|深渊|地獄');p.add_argument('--decode',nargs='*',default=[]);p.add_argument('--identity',action='store_true');a=p.parse_args()
pe=pefile.PE(str(P)); b=pe.get_memory_mapped_image(); BASE=pe.OPTIONAL_HEADER.ImageBase;md=Cs(CS_ARCH_X86,CS_MODE_32);md.skipdata=True
def ds(s,e):
 print(f'\nRVA {s:08X}:{e:08X} VA {s+BASE:08X}')
 for i in md.disasm(b[s:e],s+BASE):print(f'RVA {i.address-BASE:08X} VA {i.address:08X} {i.bytes.hex():<20} {i.mnemonic:<8} {i.op_str}')
if a.identity:
 print('path',P,'SHA256',hashlib.sha256(P.read_bytes()).hexdigest().upper(),'ImageBase',hex(BASE),'SizeOfImage',hex(pe.OPTIONAL_HEADER.SizeOfImage),'Machine',hex(pe.FILE_HEADER.Machine))
for r in a.ranges:
 s,e=map(lambda x:int(x,0),r.split(':'));ds(s,e)
for r in a.refs:
 v=int(r,0)+BASE; print('REF target RVA',r,'VA',hex(v))
 for op in (0xe8,0xe9):
  off=0
  while True:
   off=b.find(bytes([op]),off,0x355e000)
   if off<0:break
   if off+5<=len(b) and (BASE+off+5+struct.unpack_from('<i',b,off+1)[0])&0xffffffff==v:ds(max(0,off-24),off+35)
   off+=1
 off=0
 while True:
  off=b.find(struct.pack('<I',v),off)
  if off<0:break
  print('absolute RVA',hex(off));off+=1
def decode(off):
 seed=b[off+1]&0xfe
 n=(struct.unpack_from('<H',b,off+2)[0]^((seed<<7)|seed))*2
 key=seed|0x9a714ca0; out=bytearray()
 if n>16384 or off+n+4>len(b):return ''
 for i in range(0,n//4*4,4):
  v=struct.unpack_from('<I',b,off+4+i)[0]^key;out+=struct.pack('<I',v);key=(key*0x1003f+v)&0xffffffff
 for i in range(n//4*4,n):
  v=b[off+4+i]^(key&255);out.append(v);key=(key*0x107+v)&0xffffffff
 return out.decode('utf-16le',errors='replace').rstrip('\0')
for r in a.decode:
 off=int(r,0); print(hex(off),repr(decode(off)))
if a.strings:
 for m in re.finditer(rb'[ -~]{5,}',b):
  s=m.group().decode()
  if re.search(a.pattern,s,re.I) and not re.search(r'shell|hellow|helloworld',s,re.I):print(hex(m.start()),s[:400])
 for m in re.finditer(b'\x00\x54',b):
  s=decode(m.start())
  if re.search(a.pattern,s,re.I) and len(s)<2000:print('decoded',hex(m.start()),repr(s))

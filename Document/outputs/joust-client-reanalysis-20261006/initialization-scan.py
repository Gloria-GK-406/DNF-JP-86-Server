from pathlib import Path
import struct,sys,re,contextlib
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
DUMP=Path('E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin')
data=DUMP.read_bytes(); BASE=0x400000
md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
def dis(a,b):
 for i in md.disasm(data[a-BASE:b-BASE],a): print(f'{i.address:08X} RVA={i.address-BASE:08X} {i.bytes.hex():20} {i.mnemonic:8} {i.op_str}')
def refs(value):
 pat=struct.pack('<I',value); p=0
 while True:
  p=data.find(pat,p)
  if p<0:break
  print(f'absolute ref {BASE+p:08X} RVA={p:08X}');p+=1
def calls(target):
 for p in range(len(data)-5):
  if data[p] in (0xe8,0xe9) and BASE+p+5+struct.unpack_from('<i',data,p+1)[0]==target:
   print(f'branch {BASE+p:08X} RVA={p:08X} byte={data[p]:02X}')
for arg in sys.argv[1:]:
 mode,val=arg.split(':',1)
 if mode=='dis':
  a,b=map(lambda s:int(s,0),val.split(':'));dis(a,b)
 elif mode=='ref':refs(int(val,0))
 elif mode=='call':calls(int(val,0))
 elif mode=='str':
  a=int(val,0)-BASE;print(data[a:a+180])
 elif mode=='table':
  a,b=map(lambda s:int(s,0),val.split(':'))
  for p in range(a-BASE,b-BASE,4): print(f'{BASE+p:08X}: {struct.unpack_from("<I",data,p)[0]:08X}')
 elif mode=='inventory':
  out=Path(val)
  with out.open('w',encoding='utf-8') as f,contextlib.redirect_stdout(f):
   print('Derived RTTI 0396012C code xrefs and surrounding complete padding-bounded functions')
   sites=[m.start() for m in re.finditer(re.escape(struct.pack('<I',0x396012c)),data) if m.start()<0x2c00000]
   for p in sites:
    start=data.rfind(b'\xcc\xcc\xcc\xcc',max(0,p-0x5000),p)+4
    while data[start]==0xcc:start+=1
    end=data.find(b'\xcc\xcc\xcc\xcc',p)
    print(f'\n### xref {BASE+p:08X}; function range {BASE+start:08X}:{BASE+end:08X}')
    md.skipdata=True;dis(BASE+start,BASE+end);md.skipdata=False
   print('\n### key45 scan before service getter calls (heuristic 120-byte window)')
   for m in re.finditer(b'\xe8',data[:0x2c00000]):
    p=m.start();target=BASE+p+5+struct.unpack_from('<i',data,p+1)[0]
    if target in (0x1415180,0x2b62f80) and b'\x6a\x45' in data[max(0,p-120):p]:
     print(f'call {BASE+p:08X} to {target:08X}');dis(BASE+p-80,BASE+p+80)
  print(f'wrote {out}, RTTI code sites={len(sites)}')

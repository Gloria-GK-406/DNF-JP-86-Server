from pathlib import Path
import struct
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
BASE=0x400000
b=Path('E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin').read_bytes()
items=[]
for reg,kind in [(0x1637660,'CMD'),(0x1637720,'NOTI')]:
 off=0
 while True:
  off=b.find(b'\xe8',off)
  if off<0:break
  if off+5<=len(b) and BASE+off+5+struct.unpack_from('<i',b,off+1)[0]==reg and b[off-2:off]==b'\x8b\xc8':
   if b[off-12]==0x68 and b[off-17]==0x68:
    op=struct.unpack_from('<I',b,off-11)[0];fn=struct.unpack_from('<I',b,off-16)[0]
    items.append((kind,op,fn,BASE+off))
   elif b[off-9]==0x6a and b[off-14]==0x68:
    op=b[off-8];fn=struct.unpack_from('<I',b,off-13)[0]
    items.append((kind,op,fn,BASE+off))
  off+=1
print('Matches',len(items),'pattern: PUSH reader, PUSH opcode, CALL singleton, MOV ecx,eax, CALL registration')
for kind,op,fn,va in items:
 if (kind=='CMD' and 0x4dd<=op<=0x4f0) or (kind=='NOTI' and 0x4af<=op<=0x4c3) or 0x1410000<=fn<=0x1420000:
  print(kind,f'{op:04X}',f'handler VA {fn:08X}',f'registration VA {va:08X}')
Path(__file__).with_name('protocol-set-registers-all.tsv').write_text('kind\topcode\thandler_va\tregistration_va\n'+''.join(f'{k}\t{o:04X}\t{f:08X}\t{v:08X}\n' for k,o,f,v in items),encoding='utf-8')

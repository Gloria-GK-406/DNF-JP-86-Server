import sys,struct
from pathlib import Path
sys.path.insert(0,'E:/Project/DNF/DNF-JP-86-Server/artifacts/pot-opening-20260922/analysis-deps')
from unicorn import *
from unicorn.x86_const import *
b=Path('E:/Project/DNF/DNF-JP-86-Server/artifacts/pot-debug-environment-20260922/live-image.bin').read_bytes()
u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(0x400000,(len(b)+4095)&~4095);u.mem_write(0x400000,b)
u.mem_map(0x100000,0x100000);u.mem_map(0x10000000,0x10000)
u.mem_map(0,0x10000);u.mem_write(0x18,struct.pack('<I',0x1000));u.mem_write(0x1030,struct.pack('<I',0x2000));u.mem_write(0x2008,struct.pack('<I',0x400000))

sp=0x1ff000;u.reg_write(UC_X86_REG_ESP,sp);u.mem_write(sp,struct.pack('<IIII',0x10001000,0,1,0))
# Handler gets result separately; successful body continues from source slot.
payload=struct.pack('<hhiiHBBH',65,9,20002,1,35,0,0,0)+bytes(15)+struct.pack('<I',4)+bytes(4)+struct.pack('<I',8)+bytes(8)+bytes(5)
# 18-byte item prefix, then unknown scalar/array/tail fields through offset 34.
if len(sys.argv)>1 and sys.argv[1]=='fixed':payload=payload[:23]+bytes(3)+payload[23:]
if len(sys.argv)>1 and sys.argv[1]=='old':payload=bytes.fromhex('FFFF00000000000000000000')
u.mem_write(0x10000000,payload+b'0'*8192);u.mem_write(0x3baae38,struct.pack('<I',0x10000000));u.mem_write(0x3baae3c,struct.pack('<I',len(payload)))
def d(a):return struct.unpack('<I',u.mem_read(a,4))[0]
# Pure UI/object setup is outside this byte-reader check. Native byte readers and VM length reader run unchanged.
stubs={0x101f200:0,0x16ee6c0:4,0x154b880:0,0x2a431a0:0,0x1102c30:0,0x2a431d0:0,0x166ebc0:0,0x2b4cdc0:0,0x1262520:0,0x108caf0:0,0x1085810:4}
reads=[]
def hook(u,a,n,x):
 if a in stubs:
  sp=u.reg_read(UC_X86_REG_ESP);ret=d(sp);u.reg_write(UC_X86_REG_EAX,0x10003000 if a in [0x1262520,0x108caf0] else 0);u.reg_write(UC_X86_REG_ESP,sp+4+stubs[a]);u.reg_write(UC_X86_REG_EIP,ret);return
 if a in [0x27c5310,0x27c52a0,0x27c5380,0x27c5230]:
  sp=u.reg_read(UC_X86_REG_ESP);size=4 if a==0x27c5310 else 2 if a==0x27c52a0 else d(sp+8)
  offset=d(0x3baae38)-0x10000000;reads.append((hex(a),offset,size))
  if offset+size>len(payload): print('OVERREAD',reads[-1], 'length',len(payload))
 if a==0x113fa9e:print('PARSE COMPLETE',d(0x3baae38)-0x10000000,'of',len(payload));u.emu_stop()
u.hook_add(UC_HOOK_CODE,hook)
try:u.emu_start(0x113f780,0x10001000,count=3000000)
except UcError as e:print(e,hex(u.reg_read(UC_X86_REG_EIP)))
print('reads',reads)

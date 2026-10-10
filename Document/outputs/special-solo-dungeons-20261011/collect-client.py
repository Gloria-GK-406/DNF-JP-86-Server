from capstone import Cs,CS_ARCH_X86,CS_MODE_32
import json,hashlib
p=r'E:/Project/DNF/DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin'
b=open(p,'rb').read(); md=Cs(CS_ARCH_X86,CS_MODE_32)
readers={0x27c52a0:'ReadU16',0x27c5310:'ReadU32',0x27c5230:'ReadBytes'}
print(json.dumps({'sha256':hashlib.sha256(b).hexdigest()}))
for rva in [0xd56690,0xd5ab20,0xd49030,0xa14ec0,0xa15ba0]:
 rows=[]
 for ins in md.disasm(b[rva:rva+170],rva+0x400000):
  line=hex(ins.address)+' '+ins.mnemonic+' '+ins.op_str
  if ins.mnemonic=='call':
   try: line+=' '+readers.get(int(ins.op_str,16),'')
   except: pass
  rows.append(line)
 print(json.dumps({'rva':hex(rva),'assembly':rows}))


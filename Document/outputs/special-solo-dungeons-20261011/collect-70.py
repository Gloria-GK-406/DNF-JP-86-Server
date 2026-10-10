import struct,json,re
p=r'E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r'
b=open(p,'rb').read(); shoff=struct.unpack_from('<I',b,32)[0]; entsize,n,si=struct.unpack_from('<HHH',b,46); ss=[struct.unpack_from('<IIIIIIIIII',b,shoff+i*entsize) for i in range(n)]; names=b[ss[si][4]:ss[si][4]+ss[si][5]]
def z(b,p): return b[p:b.find(b'\0',p)].decode('utf8','replace')
s=next(s for s in ss if z(names,s[0])=='.symtab'); st=ss[s[6]]; strings=b[st[4]:st[4]+st[5]]; 
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
symbols={}
for p in range(s[4],s[4]+s[5],s[9]):
 no,val,size,info,other,sec=struct.unpack_from('<IIIBBH',b,p)
 if info&15==2: symbols[val]=(z(strings,no),size)
md=Cs(CS_ARCH_X86,CS_MODE_32)
for address in [0x86440ee,0x83035c8,0x830410c,0x83053d2,0x8142fa6]:
 name,size=symbols[address]
 section=next(q for q in ss if q[3]<=address<q[3]+q[5])
 offset=section[4]+address-section[3]
 out=[]
 for ins in md.disasm(b[offset:offset+size],address):
  if ins.mnemonic=='call':
   try: target=int(ins.op_str,16)
   except: target=0
   out.append(hex(ins.address)+' '+ins.mnemonic+' '+ins.op_str+' '+symbols.get(target,('',0))[0])
  elif address==0x86440ee: out.append(hex(ins.address)+' '+ins.mnemonic+' '+ins.op_str)
 print(json.dumps(dict(address=hex(address),name=name,assembly=out)))


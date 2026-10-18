import struct,json,re,hashlib,sys
from pathlib import Path
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
p=Path('E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r')
b=p.read_bytes(); shoff=struct.unpack_from('<I',b,32)[0]; entsize,n,si=struct.unpack_from('<HHH',b,46); ss=[struct.unpack_from('<IIIIIIIIII',b,shoff+i*entsize) for i in range(n)]; names=b[ss[si][4]:ss[si][4]+ss[si][5]]
def z(b,p): return b[p:b.find(b'\0',p)].decode('utf8','replace')
s=next(s for s in ss if z(names,s[0])=='.symtab'); st=ss[s[6]]; strings=b[st[4]:st[4]+st[5]]
symbols={}
for pos in range(s[4],s[4]+s[5],s[9]):
 no,val,size,info,other,sec=struct.unpack_from('<IIIBBH',b,pos)
 if info&15==2: symbols[val]=(z(strings,no),size)
md=Cs(CS_ARCH_X86,CS_MODE_32)
pattern=re.compile(sys.argv[1] if len(sys.argv)>1 else 'timespiral|heras|break.?trap|trap.?buff',re.I)
matches=[(a,n,size) for a,(n,size) in symbols.items() if pattern.search(n)]
out=Path(__file__).parent
(out/'symbols.json').write_text(json.dumps({'sha256':hashlib.sha256(b).hexdigest(),'function_count':len(symbols),'pattern':pattern.pattern,'matches':[{'va':hex(a),'name':n,'size':s} for a,n,s in matches]},indent=2),encoding='utf8')
print('functions',len(symbols),'matches',len(matches))
string_matches=[]
for hit in re.finditer(rb'[\x20-\x7e]{6,}', b):
 value=hit.group().decode('ascii')
 if re.search(r'time.?spiral|break.?trap|trap.?buff|global_timespiral|heras|hellas|laborat',value,re.I):
  string_matches.append({'offset':hex(hit.start()),'text':value})
(out/'strings.json').write_text(json.dumps(string_matches,indent=2),encoding='utf8')
print('string matches',len(string_matches))
for a,name,size in matches:
 print(hex(a),size,name)
 if size>20000: continue
 section=next((q for q in ss if q[3]<=a<q[3]+q[5]),None)
 if section is None:continue
 offset=section[4]+a-section[3]
 rows=[]
 for ins in md.disasm(b[offset:offset+size],a):
  extra=''
  if ins.mnemonic in ('call','jmp'):
   try: extra=symbols.get(int(ins.op_str,16),('',0))[0]
   except ValueError:pass
  rows.append(f'{ins.address:08X} {ins.mnemonic:8} {ins.op_str} {extra}'.rstrip())
 (out/f'{a:08x}.asm.txt').write_text(name+'\n'+'\n'.join(rows),encoding='utf8')

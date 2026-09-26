"""Read-only direct-call inventory, validating candidates against symbol entry disassembly."""
import runpy,sys,struct
from pathlib import Path
sys.argv=['mail_elf.py','sym','^$']
g=runpy.run_path(str(Path(__file__).parents[1]/'mail-system-investigation/mail_elf.py'))
code,base=g['code'],g['base']; targets={0x85555e8,0x8555756}
seen=set()
for pos in range(len(code)-4):
 if code[pos]!=0xe8:continue
 target=base+pos+5+struct.unpack_from('<i',code,pos+1)[0]
 if target not in targets:continue
 sym=g['owner'](base+pos)
 if not sym:continue
 valid=any(i.address==base+pos and i.mnemonic=='call' and i.op_str==hex(target) for i in g['instructions'](sym))
 if valid and (base+pos,target) not in seen:
  seen.add((base+pos,target));print(f'{base+pos:08x} -> {target:08x} owner {sym[0]:08x} {sym[2]}')

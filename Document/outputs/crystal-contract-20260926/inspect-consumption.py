import hashlib,struct
from pathlib import Path
import pefile,capstone
p=Path("E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe")
print("SHA256",hashlib.sha256(p.read_bytes()).hexdigest())
pe=pefile.PE(str(p));m=pe.get_memory_mapped_image();base=pe.OPTIONAL_HEADER.ImageBase
d=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32)
# Full native predicate, then native basic blocks around both calls. The outer
# functions have VMP transfers: these excerpts do not claim full CFG recovery.
for a,b in [(0xE92E80,0xE92F64),(0xE97130,0xE971F9),(0xE98208,0xE982AB),(0xE9784B,0xE978A2)]:
 print("BLOCK VA",hex(a))
 for i in d.disasm(m[a-base:b-base],a):print(f"{i.address:08X} {i.bytes.hex()} {i.mnemonic} {i.op_str}".rstrip())
print("direct E8 callers of predicate")
for o in range(len(m)-5):
 if m[o]==232 and base+o+5+struct.unpack_from("<i",m,o+1)[0]==0xE92E80:print(hex(base+o))

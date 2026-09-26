import hashlib
from pathlib import Path
import pefile, capstone
path=Path("E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe")
print("SHA256",hashlib.sha256(path.read_bytes()).hexdigest())
pe=pefile.PE(str(path)); base=pe.OPTIONAL_HEADER.ImageBase; m=pe.get_memory_mapped_image()
d=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32)
for start,end in [(0x127BCB0,0x127BCE5),(0x127BD30,0x127BDB0),(0x127C9B0,0x127CC38),(0x127D300,0x127D4DA)]:
 print("FUNCTION VA",hex(start))
 for i in d.disasm(m[start-base:end-base],start):print(f"{hex(i.address)} {i.bytes.hex()} {i.mnemonic} {i.op_str}".rstrip())

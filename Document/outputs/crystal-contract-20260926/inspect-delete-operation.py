import hashlib, struct
from pathlib import Path
import pefile, capstone
exe = Path("E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe")
print("image", exe, "sha256", hashlib.sha256(exe.read_bytes()).hexdigest())
pe = pefile.PE(str(exe)); image = pe.get_memory_mapped_image(); base = pe.OPTIONAL_HEADER.ImageBase
print("ImageBase", hex(base))
dis = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
# Validated native function starts. Stop at the VMP transfer in useSkill;
# bytes following that transfer are not treated as a linear native function.
for start, end in [(0x16FDFC0,0x16FE413),(0x1836720,0x18367AC),(0x1870B80,0x1870FC4),(0x1612CD0,0x1612E20),(0x18983F0,0x18989AD)]:
 print("\nFUNCTION",hex(start))
 for i in dis.disasm(image[start-base:end-base],start): print(hex(i.address),i.bytes.hex(),i.mnemonic,i.op_str)
for address in [0x32E8690,0x32E8620,0x32E86E0,0x32E8D78,0x32E8D40,0x32D9430,0x32D9958]:
 r=address-base; seed=image[r+1]&254
 n=(((seed<<7)|seed)^struct.unpack_from("<H",image,r+2)[0])*2
 key=seed|0x9A714CA0; result=bytearray(); data=image[r+4:r+4+n]
 for j in range(n//4):
  value=struct.unpack_from("<I",data,j*4)[0]^key
  result+=struct.pack("<I",value);key=(key*0x1003F+value)&0xFFFFFFFF
 for b in data[n//4*4:]:
  value=b^(key&255);result.append(value);key=(key*0x107+value)&0xFFFFFFFF
 print("STRING",hex(address),result.decode("utf-16le").rstrip("\0"))

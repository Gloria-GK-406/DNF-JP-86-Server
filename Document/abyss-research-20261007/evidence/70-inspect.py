import sys, hashlib, bisect, re, json, os
from pathlib import Path
from elftools.elf.elffile import ELFFile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

target=Path(os.environ.get('ABYSS_ELF',r'E:\Project\DNF\Research\1995chen-dnf\other\等级补丁\70\df_game_r'))
with target.open('rb') as f:
    elf=ELFFile(f)
    sec=elf.get_section_by_name('.text'); code=sec.data(); base=sec['sh_addr']
    syms=sorted((s['st_value'],s['st_size'],s.name) for s in elf.get_section_by_name('.symtab').iter_symbols() if s['st_info']['type']=='STT_FUNC' and s['st_size'] and s['st_value'])
    starts=[s[0] for s in syms]; md=Cs(CS_ARCH_X86,CS_MODE_32)
    def owner(a):
        i=bisect.bisect_right(starts,a)-1
        return syms[i][2] if i>=0 and a<syms[i][0]+syms[i][1] else ''
    if sys.argv[1]=='identity':
        print(target, target.stat().st_size, hashlib.sha256(target.read_bytes()).hexdigest(),dict(elf.header))
        print([(s.name,hex(s['sh_addr']),s['sh_size']) for s in elf.iter_sections() if s.name in ('.symtab','.debug_info','.text')])
    elif sys.argv[1]=='compare':
        other=Path(r'E:\Project\DNF\Research\1995chen-dnf\build\dnf_data\home\template\init\df_game_r')
        with other.open('rb') as g:
            e2=ELFFile(g);sec2=e2.get_section_by_name('.text');c2=sec2.data();b2=sec2['sh_addr']
            print('template',hashlib.sha256(other.read_bytes()).hexdigest())
            for a,z,n in syms:
                if re.search(sys.argv[2],n,re.I) and not n.startswith(('_ZNSt','_ZNKSt','_ZSt','_ZNSa','_ZN9__gnu','_ZNK9__gnu')):
                    print(hex(a),n,'same-text' if code[a-base:a-base+z]==c2[a-b2:a-b2+z] else 'DIFFERENT')
    elif sys.argv[1]=='symbols':
        for a,z,n in syms:
            if re.search(sys.argv[2],n,re.I) and not n.startswith(('_ZNSt','_ZNKSt','_ZSt','_ZNSa','_ZN9__gnu','_ZNK9__gnu')): print(f'{a:08x} {z:5d} {n}')
    elif sys.argv[1]=='asm':
        targets={int(x,0) for x in sys.argv[2:]}
        for a,z,n in syms:
            if a not in targets: continue
            print(f'\nFUNCTION {a:08x} size={z} {n}')
            for i in md.disasm(code[a-base:a-base+z],a):
                dst=owner(int(i.op_str,16)) if i.mnemonic=='call' and i.op_str.startswith('0x') else ''
                print(f'{i.address:08x}: {i.bytes.hex():20s} {i.mnemonic:8s} {i.op_str:35s} {dst}')
    elif sys.argv[1]=='calls':
        targets={int(x,0) for x in sys.argv[2:]}
        for a,z,n in syms:
            if not base<=a<a+z<=base+len(code): continue
            for i in md.disasm(code[a-base:a-base+z],a):
                if i.mnemonic=='call' and i.op_str.startswith('0x') and int(i.op_str,16) in targets: print(f'{i.address:08x} {a:08x} {n} -> {i.op_str} {owner(int(i.op_str,16))}')
    elif sys.argv[1]=='strings':
        for s in elf.iter_sections():
            if s.name!='.rodata': continue
            for m in re.finditer(rb'[ -~]{5,}',s.data()):
                if re.search(sys.argv[2],m.group().decode(),re.I): print(hex(s['sh_addr']+m.start()),m.group().decode())
    elif sys.argv[1]=='xrefs':
        import struct
        for arg in sys.argv[2:]:
            a=int(arg,0); needle=struct.pack('<I',a); pos=0
            while True:
                pos=code.find(needle,pos)
                if pos<0:break
                print(hex(a),hex(base+pos),owner(base+pos));pos+=1
    elif sys.argv[1]=='types':
        wanted=set(sys.argv[2:]); dwarf=elf.get_dwarf_info()
        for cu in dwarf.iter_CUs():
            for die in cu.iter_DIEs():
                at=die.attributes.get('DW_AT_name')
                if die.tag not in ('DW_TAG_structure_type','DW_TAG_class_type') or not at or at.value.decode(errors='replace') not in wanted: continue
                print('TYPE',at.value.decode(errors='replace'),die.offset,die.attributes.get('DW_AT_byte_size'))
                for child in die.iter_children():
                    if child.tag!='DW_TAG_member':continue
                    an=child.attributes.get('DW_AT_name'); al=child.attributes.get('DW_AT_data_member_location')
                    print(an.value.decode(errors='replace') if an else '?',al.value if al else '?')
    elif sys.argv[1]=='data':
        import struct
        for arg in sys.argv[2:]:
            a=int(arg,0)
            for s in elf.iter_sections():
                if s['sh_type']=='SHT_NOBITS':continue
                if s['sh_addr']<=a<s['sh_addr']+s['sh_size']:
                    data=s.data()[a-s['sh_addr']:a-s['sh_addr']+120]
                    print(hex(a),'u32=',struct.unpack('<I',data[:4])[0],'float=',struct.unpack('<f',data[:4])[0],'string=',repr(data.split(b'\0')[0]));break
    elif sys.argv[1]=='read':
        a=int(sys.argv[2],0);z=int(sys.argv[3],0)
        for s in elf.iter_sections():
            if s['sh_addr']<=a<a+z<=s['sh_addr']+s['sh_size']:
                data=s.data()[a-s['sh_addr']:a-s['sh_addr']+z]
                print(data.hex());print(repr(data));break

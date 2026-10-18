"""Read-only disassembly for party target editing and cycle quest synchronization."""
import hashlib
from pathlib import Path
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

exe = Path('E:/Project/DNF/DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe')
sha = hashlib.sha256(exe.read_bytes()).hexdigest().upper()
assert sha == 'CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14'
pe = pefile.PE(str(exe), fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
assert base == 0x400000
windows = [
    ('Party edit: Story gets quest target; other categories serialize selector value', 0x009C6160, 0x009C622C),
    ('Party edit builder: category and retained target', 0x009C64E9, 0x009C651F),
    ('Normal bypasses mode-specific entry target comparison', 0x02720E04, 0x02720E90),
    ('CMD0308: nonzero gate and quest ID cause an accept request', 0x01105C70, 0x01105CBB),
    ('CMD001F: automatic directed entry continuation', 0x01138860, 0x01138946),
    ('NOTI023F: rebuild accepted quests, without CMD001F/0010 continuation', 0x01162440, 0x0116250D),
    ('Quest state reader creates a missing quest definition', 0x00E75F17, 0x00E75F70),
]
decoder = Cs(CS_ARCH_X86, CS_MODE_32)
rows = [f'executable={exe}', f'sha256={sha}', f'image_base={base:08X}',
        'Evidence: ClientStatic / High. VA = RVA + 00400000. No client files modified.', '']
for title, start, end in windows:
    rows.append(f'=== {title}; VA {start:08X}..{end:08X} ===')
    for ins in decoder.disasm(pe.get_data(start-base, end-start), start):
        rows.append(f'RVA {ins.address-base:08X} VA {ins.address:08X} {ins.bytes.hex():<20} {ins.mnemonic:<8} {ins.op_str}')
    rows.append('')
output = Path(__file__).with_name('native-flow-disassembly.txt')
output.write_text('\n'.join(rows).rstrip() + '\n', encoding='utf-8', newline='\n')
print(output)

import sys, runpy
d = runpy.run_path('Document/outputs/enemy-reward-investigation/inspect_enemy_elf.py')
for a,z,n in d['syms']:
    if hex(a) in sys.argv[2:]:
        print('\n'+n)
        for i in d['md'].disasm(d['code'][a-d['base']:a-d['base']+z],a):
            target = d['owner'](int(i.op_str,16)) if i.mnemonic=='call' and i.op_str.startswith('0x') else ''
            print(hex(i.address),i.mnemonic,i.op_str,target)

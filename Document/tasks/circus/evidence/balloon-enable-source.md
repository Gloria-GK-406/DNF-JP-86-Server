{"mode": "client", "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": {"private_bytes": 9146368, "working_set": 16539648, "peak_working_set": 16539648}}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x877a30 48
00877a30: push esi
00877a31: mov esi, ecx
00877a33: mov eax, dword ptr [esi]
00877a35: mov edx, dword ptr [eax + 8]
00877a38: call edx
00877a3a: mov word ptr [esi + 0x3c], 0
00877a40: pop esi
00877a41: ret
00877a42: int3
00877a43: int3
00877a44: int3
00877a45: int3
00877a46: int3
00877a47: int3
00877a48: int3
00877a49: int3
00877a4a: int3
00877a4b: int3
00877a4c: int3
00877a4d: int3
00877a4e: int3
00877a4f: int3
00877a50: mov al, byte ptr [ecx + 0x3d]
00877a53: ret
00877a54: int3
00877a55: int3
00877a56: int3
00877a57: int3
00877a58: int3
00877a59: int3
00877a5a: int3
00877a5b: int3
00877a5c: int3
00877a5d: int3
00877a5e: int3
00877a5f: int3

RANGE 0x877900 300
00877900: add byte ptr [eax], al
00877902: test byte ptr [ebp + 8], 1
00877906: je 0x877911
00877908: push esi
00877909: call 0x27c4030
0087790e: add esp, 4
00877911: mov eax, esi
00877913: pop esi
00877914: pop ebp
00877915: ret 4
00877918: int3
00877919: int3
0087791a: int3
0087791b: int3
0087791c: int3
0087791d: int3
0087791e: int3
0087791f: int3
00877920: push ebp
00877921: mov ebp, esp
00877923: mov eax, dword ptr [ebp + 8]
00877926: push esi
00877927: push eax
00877928: mov esi, ecx
0087792a: call 0x87a680
0087792f: xor eax, eax
00877931: mov dword ptr [esi + 0x1080], eax
00877937: mov byte ptr [esi + 0x1084], al
0087793d: mov dword ptr [esi], 0x30e8304
00877943: mov dword ptr [esi + 8], 0x30e82b4
0087794a: mov dword ptr [esi + 0xad0], 0x30e8254
00877954: mov eax, esi
00877956: pop esi
00877957: pop ebp
00877958: ret 4
0087795b: int3
0087795c: int3
0087795d: int3
0087795e: int3
0087795f: int3
00877960: push ebp
00877961: mov ebp, esp
00877963: pop ebp
00877964: jmp 0x87a720
00877969: int3
0087796a: int3
0087796b: int3
0087796c: int3
0087796d: int3
0087796e: int3
0087796f: int3
00877970: push ebp
00877971: mov ebp, esp
00877973: pop ebp
00877974: jmp 0x878ad0
00877979: int3
0087797a: int3
0087797b: int3
0087797c: int3
0087797d: int3
0087797e: int3
0087797f: int3
00877980: jmp 0x878b00
00877985: int3
00877986: int3
00877987: int3
00877988: int3
00877989: int3
0087798a: int3
0087798b: int3
0087798c: int3
0087798d: int3
0087798e: int3
0087798f: int3
00877990: push ebp
00877991: mov ebp, esp
00877993: mov eax, dword ptr [ebp + 8]
00877996: push esi
00877997: mov esi, ecx
00877999: mov dword ptr [esi + 0x1080], eax
0087799f: test eax, eax
008779a1: je 0x8779c6
008779a3: mov byte ptr [esi + 0x1084], 1
008779aa: call 0x195cd70
008779af: test eax, eax
008779b1: je 0x8779cd
008779b3: mov ecx, dword ptr [esi + 0x1080]
008779b9: push ecx
008779ba: mov ecx, eax
008779bc: call 0x119ce80
008779c1: pop esi
008779c2: pop ebp
008779c3: ret 4
008779c6: mov byte ptr [esi + 0x1084], 0
008779cd: pop esi
008779ce: pop ebp
008779cf: ret 4
008779d2: int3
008779d3: int3
008779d4: int3
008779d5: int3
008779d6: int3
008779d7: int3
008779d8: int3
008779d9: int3
008779da: int3
008779db: int3
008779dc: int3
008779dd: int3
008779de: int3
008779df: int3
008779e0: sub ecx, 0xad0
008779e6: jmp 0x8779f0
008779eb: int3
008779ec: int3
008779ed: int3
008779ee: int3
008779ef: int3
008779f0: push ebp
008779f1: mov ebp, esp
008779f3: push esi
008779f4: mov esi, ecx
008779f6: mov dword ptr [esi], 0x30e8304
008779fc: mov dword ptr [esi + 8], 0x30e82b4
00877a03: mov dword ptr [esi + 0xad0], 0x30e8254
00877a0d: call 0x87a5a0
00877a12: test byte ptr [ebp + 8], 1
00877a16: je 0x877a21
00877a18: push esi
00877a19: call 0x27c4030
00877a1e: add esp, 4
00877a21: mov eax, esi
00877a23: pop esi
00877a24: pop ebp
00877a25: ret 4
00877a28: int3
00877a29: int3
00877a2a: int3
00877a2b: int3
{"address": "0x30e8944", "absolute_refs": ["0x877a97", "0x877b0b"]}
{"address": "0x877a60", "absolute_refs": []}
{"memory_final": {"private_bytes": 17903616, "working_set": 26619904, "peak_working_set": 160825344}}

{"mode": "client", "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": {"private_bytes": 9101312, "working_set": 16494592, "peak_working_set": 16494592}}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x175a5e0 240
0175a5e0: into
0175a5e1: call edx
0175a5e3: mov eax, dword ptr [esi]
0175a5e5: mov edx, dword ptr [eax + 0x2e0]
0175a5eb: mov ecx, esi
0175a5ed: call edx
0175a5ef: cmp dword ptr [eax], 0x4e59
0175a5f5: jne 0x175a646
0175a5f7: push ebx
0175a5f8: push 0x3967ec0
0175a5fd: push 0x395eb54
0175a602: push ebx
0175a603: push 0x2c
0175a605: call 0x141a170
0175a60a: mov ecx, eax
0175a60c: call 0x1415180
0175a611: push eax
0175a612: call 0x2c6386a
0175a617: mov edi, eax
0175a619: add esp, 0x14
0175a61c: cmp edi, ebx
0175a61e: je 0x175a646
0175a620: mov eax, dword ptr [esi]
0175a622: mov edx, dword ptr [eax + 0x258]
0175a628: push 1
0175a62a: mov ecx, esi
0175a62c: call edx
0175a62e: mov ecx, edi
0175a630: call 0x877a50
0175a635: test al, al
0175a637: jne 0x175a646
0175a639: mov eax, dword ptr [esi]
0175a63b: mov edx, dword ptr [eax + 0x258]
0175a641: push ebx
0175a642: mov ecx, esi
0175a644: call edx
0175a646: mov eax, dword ptr [esi]
0175a648: mov edx, dword ptr [eax + 0x2e0]
0175a64e: mov ecx, esi
0175a650: call edx
0175a652: cmp byte ptr [eax + 0x59c], 0
0175a659: mov ecx, esi
0175a65b: je 0x175a73e
0175a661: mov eax, dword ptr [esi]
0175a663: mov edx, dword ptr [eax + 0x2e0]
0175a669: call edx
0175a66b: mov eax, dword ptr [eax + 0x598]
0175a671: push eax
0175a672: call 0x141a170
0175a677: mov ecx, eax
0175a679: call 0x14172d0
0175a67e: test al, al
0175a680: je 0x175a801
0175a686: call 0x1102c30
0175a68b: test eax, eax
0175a68d: je 0x175a737
0175a693: call 0x1102c30
0175a698: mov ecx, eax
0175a69a: call 0xa9aee0
0175a69f: test eax, eax
0175a6a1: je 0x175a737
0175a6a7: call 0x1102c30
0175a6ac: mov ecx, eax
0175a6ae: call 0xa9aee0
0175a6b3: mov ecx, eax
0175a6b5: call 0x2419650
0175a6ba: mov edx, dword ptr [esi]
0175a6bc: lea ecx, [ebp - 0x54]
0175a6bf: mov dword ptr [ebp - 0x38], eax
0175a6c2: mov eax, dword ptr [edx + 0x2e0]
0175a6c8: push ecx
0175a6c9: mov ecx, esi
0175a6cb: call eax

RANGE 0x175f220 190
0175f220: and al, 2
0175f223: add byte ptr [eax], al
0175f225: mov ecx, esi
0175f227: call eax
0175f229: test eax, eax
0175f22b: je 0x175f292
0175f22d: mov edx, dword ptr [esi]
0175f22f: mov eax, dword ptr [edx + 0x2e0]
0175f235: mov ecx, esi
0175f237: call eax
0175f239: cmp dword ptr [eax], 0x4e59
0175f23f: jne 0x175f292
0175f241: push 0
0175f243: push 0x3967ec0
0175f248: push 0x395eb54
0175f24d: push 0
0175f24f: push 0x2c
0175f251: call 0x141a170
0175f256: mov ecx, eax
0175f258: call 0x1415180
0175f25d: push eax
0175f25e: call 0x2c6386a
0175f263: mov edi, eax
0175f265: add esp, 0x14
0175f268: test edi, edi
0175f26a: je 0x175f528
0175f270: mov edx, dword ptr [esi]
0175f272: mov eax, dword ptr [edx + 0x258]
0175f278: push 1
0175f27a: mov ecx, esi
0175f27c: call eax
0175f27e: mov ecx, edi
0175f280: call 0x877a50
0175f285: test al, al
0175f287: je 0x175f57a
0175f28d: jmp 0x175f528
0175f292: mov edx, dword ptr [esi]
0175f294: mov eax, dword ptr [edx + 0x2e0]
0175f29a: mov ecx, esi
0175f29c: call eax
0175f29e: test eax, eax
0175f2a0: je 0x175f2de
0175f2a2: mov edx, dword ptr [esi]
0175f2a4: mov eax, dword ptr [edx + 0x2e0]
0175f2aa: mov ecx, esi
0175f2ac: call eax
0175f2ae: cmp byte ptr [eax + 0x59c], 0
0175f2b5: je 0x175f2de
0175f2b7: mov edx, dword ptr [esi]
0175f2b9: mov eax, dword ptr [edx + 0x2e0]
0175f2bf: mov ecx, esi
0175f2c1: call eax
0175f2c3: mov ecx, dword ptr [eax + 0x598]
0175f2c9: push ecx
0175f2ca: call 0x141a170
0175f2cf: mov ecx, eax
0175f2d1: call 0x14172d0
0175f2d6: test al, al
0175f2d8: je 0x175f57a

RANGE 0x22cd9b0 240
022cd9b0: mov ecx, dword ptr [ebp - 0x8c]
022cd9b6: mov edx, dword ptr [ecx]
022cd9b8: mov eax, dword ptr [edx + 4]
022cd9bb: call eax
022cd9bd: mov eax, dword ptr [ebx]
022cd9bf: cmp eax, 0x4e59
022cd9c4: je 0x22cd9d4
022cd9c6: cmp eax, 0x4e5a
022cd9cb: je 0x22cd9d4
022cd9cd: cmp eax, 0x4e5c
022cd9d2: jne 0x22cda2f
022cd9d4: xor eax, eax
022cd9d6: push eax
022cd9d7: lea ecx, [ebp - 0x70]
022cd9da: mov dword ptr [ebp - 0x70], eax
022cd9dd: mov dword ptr [ebp - 0x6c], eax
022cd9e0: call 0x42dd70
022cd9e5: lea ecx, [ebp - 0x70]
022cd9e8: push ecx
022cd9e9: mov ecx, esi
022cd9eb: mov byte ptr [ebp - 4], 0xf
022cd9ef: call 0x28894b0
022cd9f4: cmp dword ptr [ebp - 0x6c], 0
022cd9f8: mov byte ptr [ebp - 4], 0
022cd9fc: je 0x22cda2f
022cd9fe: mov edx, dword ptr [ebp - 0x6c]
022cda01: add edx, 4
022cda04: or eax, 0xffffffff
022cda07: lock xadd dword ptr [edx], eax
022cda0b: jne 0x22cda2f
022cda0d: mov ecx, dword ptr [ebp - 0x6c]
022cda10: mov edx, dword ptr [ecx]
022cda12: mov eax, dword ptr [edx]
022cda14: call eax
022cda16: mov ecx, dword ptr [ebp - 0x6c]
022cda19: add ecx, 8
022cda1c: or edx, 0xffffffff
022cda1f: lock xadd dword ptr [ecx], edx
022cda23: jne 0x22cda2f
022cda25: mov ecx, dword ptr [ebp - 0x6c]
022cda28: mov eax, dword ptr [ecx]
022cda2a: mov edx, dword ptr [eax + 4]
022cda2d: call edx
022cda2f: mov eax, dword ptr [ebx]
022cda31: cmp eax, 0x4e6e
022cda36: je 0x22cda3f
022cda38: cmp eax, 0x4e6f
022cda3d: jne 0x22cdab5
022cda3f: xor eax, eax
022cda41: push eax
022cda42: lea ecx, [ebp - 0xa0]
022cda48: mov dword ptr [ebp - 0xa0], eax
022cda4e: mov dword ptr [ebp - 0x9c], eax
022cda54: call 0x42dd70
022cda59: lea eax, [ebp - 0xa0]
022cda5f: push eax
022cda60: mov ecx, esi
022cda62: mov byte ptr [ebp - 4], 0x10
022cda66: call 0x28894b0
022cda6b: cmp dword ptr [ebp - 0x9c], 0
022cda72: mov byte ptr [ebp - 4], 0
022cda76: je 0x22cdab5
022cda78: mov ecx, dword ptr [ebp - 0x9c]
022cda7e: add ecx, 4
022cda81: or edx, 0xffffffff
022cda84: lock xadd dword ptr [ecx], edx
022cda88: jne 0x22cdab5
022cda8a: mov ecx, dword ptr [ebp - 0x9c]
022cda90: mov eax, dword ptr [ecx]
022cda92: mov edx, dword ptr [eax]
022cda94: call edx
022cda96: mov eax, dword ptr [ebp - 0x9c]
022cda9c: add eax, 8

RANGE 0x20c4e80 85

RANGE 0x28c3eb0 210
028c3eb0: in al, dx
028c3eb1: loopne 0x28c3eb7
028c3eb3: mov dword ptr [ebp - 0x5c], 0xde5
028c3eba: call 0x4cb700
028c3ebf: cmp byte ptr [0x4e0ec80], bl
028c3ec5: jne 0x28c42b0
028c3ecb: push 0x36210e4
028c3ed0: call 0x27bacc0
028c3ed5: add esp, 4
028c3ed8: lea ecx, [ebp - 0xc8]
028c3ede: push ecx
028c3edf: lea edx, [ebp - 0x2d0]
028c3ee5: push edx
028c3ee6: mov ecx, 0x4e0ec88
028c3eeb: mov dword ptr [ebp - 0xc8], eax
028c3ef1: mov dword ptr [ebp - 0xc4], 0xde8
028c3efb: call 0x4cb700
028c3f00: cmp byte ptr [0x4e0ec80], bl
028c3f06: jne 0x28c42b7
028c3f0c: push 0x36210a0
028c3f11: call 0x27bacc0
028c3f16: add esp, 4
028c3f19: mov dword ptr [ebp - 0x68], eax
028c3f1c: lea eax, [ebp - 0x68]
028c3f1f: push eax
028c3f20: lea ecx, [ebp - 0x180]
028c3f26: push ecx
028c3f27: mov ecx, 0x4e0ec88
028c3f2c: mov dword ptr [ebp - 0x64], 0xdeb
028c3f33: call 0x4cb700
028c3f38: cmp byte ptr [0x4e0ec80], bl
028c3f3e: jne 0x28c42be
028c3f44: push 0x3621060
028c3f49: call 0x27bacc0
028c3f4e: add esp, 4
028c3f51: lea edx, [ebp - 0x70]
028c3f54: mov dword ptr [ebp - 0x70], eax
028c3f57: push edx
028c3f58: lea eax, [ebp - 0x1a0]
028c3f5e: push eax
028c3f5f: mov ecx, 0x4e0ec88
028c3f64: mov dword ptr [ebp - 0x6c], 0xdee
028c3f6b: call 0x4cb700
028c3f70: cmp byte ptr [0x4e0ec80], bl
028c3f76: jne 0x28c42c5
028c3f7c: push 0x3621028
{"memory_final": {"private_bytes": 17432576, "working_set": 26116096, "peak_working_set": 148201472}}

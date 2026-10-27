{"mode": "client", "memory_budget": "384 MiB; mmap, max 40 matches per needle, disassembly only explicit ranges"}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x7db370 420
007db370: push ebp
007db371: mov ebp, esp
007db373: mov eax, dword ptr [ebp + 8]
007db376: mov edx, dword ptr [ebp + 0xc]
007db379: mov dword ptr [ecx + 0x138], eax
007db37f: mov dword ptr [ecx + 0x13c], edx
007db385: pop ebp
007db386: ret 8
007db389: int3
007db38a: int3
007db38b: int3
007db38c: int3
007db38d: int3
007db38e: int3
007db38f: int3
007db390: push ebp
007db391: mov ebp, esp
007db393: mov eax, dword ptr [ebp + 8]
007db396: push esi
007db397: push eax
007db398: mov esi, ecx
007db39a: call 0x1a00330
007db39f: xor eax, eax
007db3a1: mov byte ptr [esi + 0x140], al
007db3a7: mov dword ptr [esi + 0x138], eax
007db3ad: mov dword ptr [esi + 0x13c], eax
007db3b3: mov dword ptr [esi], 0x30c7e7c
007db3b9: mov eax, esi
007db3bb: pop esi
007db3bc: pop ebp
007db3bd: ret 4
007db3c0: push ebp
007db3c1: mov ebp, esp
007db3c3: push esi
007db3c4: mov esi, ecx
007db3c6: mov dword ptr [esi], 0x30c7e7c
007db3cc: call 0x19ffa80
007db3d1: test byte ptr [ebp + 8], 1
007db3d5: je 0x7db3e0
007db3d7: push esi
007db3d8: call 0x27c4030
007db3dd: add esp, 4
007db3e0: mov eax, esi
007db3e2: pop esi
007db3e3: pop ebp
007db3e4: ret 4
007db3e7: int3
007db3e8: int3
007db3e9: int3
007db3ea: int3
007db3eb: int3
007db3ec: int3
007db3ed: int3
007db3ee: int3
007db3ef: int3
007db3f0: push ebp
007db3f1: mov ebp, esp
007db3f3: push -1
007db3f5: push 0x2d99998
007db3fa: mov eax, dword ptr fs:[0]
007db400: push eax
007db401: sub esp, 0xc
007db404: push esi
007db405: mov eax, dword ptr [0x399cee0]
007db40a: xor eax, ebp
007db40c: push eax
007db40d: lea eax, [ebp - 0xc]
007db410: mov dword ptr fs:[0], eax
007db416: mov esi, ecx
007db418: call 0x1a004a0
007db41d: mov ecx, esi
007db41f: call 0x19ffd20
007db424: push eax
007db425: call 0xfce180
007db42a: mov esi, eax
007db42c: add esp, 4
007db42f: test esi, esi
007db431: je 0x7db50e
007db437: mov eax, dword ptr [esi]
007db439: mov edx, dword ptr [eax + 0x700]
007db43f: mov ecx, esi
007db441: call edx
007db443: mov ecx, esi
007db445: call 0x18c9e10
007db44a: push 1
007db44c: mov ecx, esi
007db44e: call 0x18c9df0
007db453: mov eax, dword ptr [esi]
007db455: mov edx, dword ptr [eax + 0x4c0]
007db45b: push 0
007db45d: mov ecx, esi
007db45f: call edx
007db461: push 0
007db463: push 1
007db465: push 0x30c7fa8
007db46a: call 0x27bacc0
007db46f: add esp, 4
007db472: push eax
007db473: push 0x30c7f90
007db478: call 0x27bacc0
007db47d: add esp, 4
007db480: push eax
007db481: lea eax, [ebp - 0x18]
007db484: push eax
007db485: call 0x7e5370
007db48a: fld1
007db48c: add esp, 0x10
007db48f: fstp dword ptr [esp]
007db492: push 1
007db494: push 0
007db496: push 0
007db498: push 0
007db49a: push 0
007db49c: push 0
007db49e: push -0x64
007db4a0: push 0
007db4a2: sub esp, 8
007db4a5: mov ecx, esp
007db4a7: lea edx, [ebp - 0x18]
007db4aa: mov dword ptr [ebp - 0x10], esp
007db4ad: push edx
007db4ae: mov dword ptr [ebp - 4], 0
007db4b5: mov dword ptr [ecx], 0
007db4bb: mov dword ptr [ecx + 4], 0
007db4c2: call 0x121d970
007db4c7: mov eax, dword ptr [esi]
007db4c9: mov edx, dword ptr [eax + 0x184]
007db4cf: push 0xa
007db4d1: mov ecx, esi
007db4d3: call edx
007db4d5: mov eax, dword ptr [ebp - 0x14]
007db4d8: mov dword ptr [ebp - 4], 0xffffffff
007db4df: test eax, eax
007db4e1: je 0x7db50e
007db4e3: mov esi, eax
007db4e5: add eax, 4
007db4e8: or ecx, 0xffffffff
007db4eb: lock xadd dword ptr [eax], ecx
007db4ef: jne 0x7db50e
007db4f1: mov edx, dword ptr [esi]
007db4f3: mov eax, dword ptr [edx]
007db4f5: mov ecx, esi
007db4f7: call eax
007db4f9: lea ecx, [esi + 8]
007db4fc: or edx, 0xffffffff
007db4ff: lock xadd dword ptr [ecx], edx
007db503: jne 0x7db50e
007db505: mov eax, dword ptr [esi]
007db507: mov edx, dword ptr [eax + 4]
007db50a: mov ecx, esi
007db50c: call edx
007db50e: mov ecx, dword ptr [ebp - 0xc]

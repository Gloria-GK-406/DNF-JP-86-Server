{"mode": "client", "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": {"private_bytes": 9093120, "working_set": 16478208, "peak_working_set": 16478208}}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x14d31a0 55
014d31a0: push ebp
014d31a1: mov ebp, esp
014d31a3: mov al, byte ptr [ebp + 8]
014d31a6: mov byte ptr [ecx + 0x3d], al
014d31a9: pop ebp
014d31aa: ret 4
014d31ad: int3
014d31ae: int3
014d31af: int3
014d31b0: cmp dword ptr [ecx + 0x40], 5
014d31b4: setg al
014d31b7: ret
014d31b8: int3
014d31b9: int3
014d31ba: int3
014d31bb: int3
014d31bc: int3
014d31bd: int3
014d31be: int3
014d31bf: int3
014d31c0: push ebx
014d31c1: push esi
014d31c2: push edi
014d31c3: mov esi, ecx
014d31c5: lea edi, [esi + 4]
014d31c8: push edi
014d31c9: call dword ptr [0x3029160]
014d31cf: mov bl, byte ptr [esi + 0x3c]
014d31d2: push edi

RANGE 0x1192c10 370
01192c10: push ebp
01192c11: mov ebp, esp
01192c13: push -1
01192c15: push 0x2d75fc0
01192c1a: mov eax, dword ptr fs:[0]
01192c20: push eax
01192c21: sub esp, 0x30
01192c24: push ebx
01192c25: push esi
01192c26: push edi
01192c27: mov eax, dword ptr [0x399cee0]
01192c2c: xor eax, ebp
01192c2e: push eax
01192c2f: lea eax, [ebp - 0xc]
01192c32: mov dword ptr fs:[0], eax
01192c38: xor ebx, ebx
01192c3a: lea eax, [ebp - 0x10]
01192c3d: push eax
01192c3e: mov dword ptr [ebp - 0x10], ebx
01192c41: mov dword ptr [ebp - 0x14], ebx
01192c44: mov dword ptr [ebp - 0x18], ebx
01192c47: mov dword ptr [ebp - 0x1c], ebx
01192c4a: call 0x27c5310
01192c4f: lea ecx, [ebp - 0x14]
01192c52: push ecx
01192c53: call 0x27c5310
01192c58: lea edx, [ebp - 0x18]
01192c5b: push edx
01192c5c: call 0x27c5310
01192c61: lea eax, [ebp - 0x1c]
01192c64: push eax
01192c65: call 0x27c5310
01192c6a: add esp, 0x10
01192c6d: lea ecx, [ebp - 0x3c]
01192c70: call 0x2916880
01192c75: lea ecx, [ebp - 0x3c]
01192c78: call 0x1548090
01192c7d: mov eax, dword ptr [ebp - 0x18]
01192c80: mov ecx, dword ptr [ebp - 0x10]
01192c83: mov edx, dword ptr [ebp - 0x14]
01192c86: mov esi, dword ptr [ebp - 0x1c]
01192c89: mov dword ptr [ebp - 0x34], ecx
01192c8c: mov dword ptr [ebp - 0x38], edx
01192c8f: mov dword ptr [ebp - 0x3c], eax
01192c92: mov dword ptr [ebp - 0x30], esi
01192c95: cmp eax, 0x4de
01192c9a: jne 0x1192cf4
01192c9c: cmp ecx, 4
01192c9f: jne 0x1192cf4
01192ca1: cmp edx, ebx
01192ca3: je 0x1192ccb
01192ca5: push 1
01192ca7: push eax
01192ca8: call 0xe1fa50
01192cad: mov ecx, dword ptr [ebp - 0x3c]
01192cb0: push ecx
01192cb1: call 0xe1be30
01192cb6: add esp, 0xc
01192cb9: mov ecx, dword ptr [ebp - 0xc]
01192cbc: mov dword ptr fs:[0], ecx
01192cc3: pop ecx
01192cc4: pop edi
01192cc5: pop esi
01192cc6: pop ebx
01192cc7: mov esp, ebp
01192cc9: pop ebp
01192cca: ret
01192ccb: push ebx
01192ccc: push 0x4de
01192cd1: call 0xe1fa50
01192cd6: mov edx, dword ptr [ebp - 0x3c]
01192cd9: push edx
01192cda: call 0xe1ba50
01192cdf: add esp, 0xc
01192ce2: mov ecx, dword ptr [ebp - 0xc]
01192ce5: mov dword ptr fs:[0], ecx
01192cec: pop ecx
01192ced: pop edi
01192cee: pop esi
01192cef: pop ebx
01192cf0: mov esp, ebp
01192cf2: pop ebp
01192cf3: ret
01192cf4: movq xmm0, qword ptr [ebp - 0x3c]
01192cf9: sub esp, 0x10
01192cfc: mov eax, esp
01192cfe: movq qword ptr [eax], xmm0
01192d02: movq xmm0, qword ptr [ebp - 0x34]
01192d07: movq qword ptr [eax + 8], xmm0
01192d0c: call 0x1102c30
01192d11: mov ecx, eax
01192d13: call 0xa9aee0
01192d18: mov ecx, eax
01192d1a: call 0x2706980
01192d1f: mov ecx, eax
01192d21: call 0x11c2670
01192d26: cmp dword ptr [ebp - 0x3c], 0xe7a
01192d2d: jne 0x1192ec0
01192d33: cmp dword ptr [ebp - 0x38], ebx
01192d36: je 0x1192e7d
01192d3c: call 0x1102c30
01192d41: mov ecx, eax
01192d43: call 0x19fc7d0
01192d48: mov ecx, eax
01192d4a: call 0xc4f430
01192d4f: cmp eax, 0x64
01192d52: jge 0x1192d7f
01192d54: push ebx
01192d55: push ebx
01192d56: push ebx
01192d57: push ebx
01192d58: push 0x1e
01192d5a: push 0xff3232ff
01192d5f: push 0xc744
01192d64: call 0x28c14c0
01192d69: mov ecx, dword ptr [0x3a5c9b8]
01192d6f: add esp, 4
01192d72: push eax
01192d73: call 0xa9af50
01192d78: mov ecx, eax
01192d7a: call 0x1a60620

RANGE 0x1192f80 160
01192f80: mov edx, dword ptr [eax + 4]
01192f83: mov ecx, edi
01192f85: call edx
01192f87: push ebx
01192f88: push 1
01192f8a: push 0x324a730
01192f8f: call 0x27bacc0
01192f94: add esp, 4
01192f97: push eax
01192f98: lea eax, [ebp - 0x24]
01192f9b: push eax
01192f9c: call 0x7e52b0
01192fa1: add esp, 0x10
01192fa4: push eax
01192fa5: mov ecx, esi
01192fa7: mov dword ptr [ebp - 4], 3
01192fae: call 0x171c570
01192fb3: lea ecx, [eax + 0x40]
01192fb6: call 0x28894b0
01192fbb: mov eax, dword ptr [ebp - 0x20]
01192fbe: mov dword ptr [ebp - 4], 0xffffffff
01192fc5: cmp eax, ebx
01192fc7: je 0x1192ff4
01192fc9: mov edi, eax
01192fcb: add eax, 4
01192fce: or ecx, 0xffffffff
01192fd1: lock xadd dword ptr [eax], ecx
01192fd5: jne 0x1192ff4
01192fd7: mov edx, dword ptr [edi]
01192fd9: mov eax, dword ptr [edx]
01192fdb: mov ecx, edi
01192fdd: call eax
01192fdf: lea ecx, [edi + 8]
01192fe2: or edx, 0xffffffff
01192fe5: lock xadd dword ptr [ecx], edx
01192fe9: jne 0x1192ff4
01192feb: mov eax, dword ptr [edi]
01192fed: mov edx, dword ptr [eax + 4]
01192ff0: mov ecx, edi
01192ff2: call edx
01192ff4: push ebx
01192ff5: push ebx
01192ff6: push 1
01192ff8: mov ecx, esi
01192ffa: call 0x1724da0
01192fff: jmp 0x119300e
01193001: cmp eax, ebx
01193003: jne 0x119300e
01193005: push ebx
01193006: call 0x161a060
0119300b: add esp, 4
0119300e: push ebx
0119300f: push 0x39660f4
01193014: push 0x395eb54
01193019: push ebx
0119301a: push 0x26

RANGE 0x175a5ef 90
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
{"memory_final": {"private_bytes": 17494016, "working_set": 26157056, "peak_working_set": 148242432}}

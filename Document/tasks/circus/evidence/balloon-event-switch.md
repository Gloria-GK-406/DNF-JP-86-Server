{"mode": "client", "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": {"private_bytes": 9105408, "working_set": 16490496, "peak_working_set": 16490496}}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x877a50 480
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
00877a60: push ebp
00877a61: mov ebp, esp
00877a63: push -1
00877a65: push 0x2cf0bb3
00877a6a: mov eax, dword ptr fs:[0]
00877a70: push eax
00877a71: push ecx
00877a72: push ebx
00877a73: push esi
00877a74: push edi
00877a75: mov eax, dword ptr [0x399cee0]
00877a7a: xor eax, ebp
00877a7c: push eax
00877a7d: lea eax, [ebp - 0xc]
00877a80: mov dword ptr fs:[0], eax
00877a86: mov esi, ecx
00877a88: mov dword ptr [ebp - 0x10], esi
00877a8b: lea ecx, [esi + 4]
00877a8e: mov dword ptr [ebp - 4], 0
00877a95: mov dword ptr [esi], 0x30e8944
00877a9b: call 0x28f2470
00877aa0: mov ebx, dword ptr [esi + 4]
00877aa3: push 0x30e8954
00877aa8: mov byte ptr [ebp - 4], 1
00877aac: call 0x27bacc0
00877ab1: add esp, 4
00877ab4: push eax
00877ab5: mov eax, dword ptr [ebx + 4]
00877ab8: lea ecx, [esi + 4]
00877abb: call eax
00877abd: mov edx, dword ptr [esi]
00877abf: mov eax, dword ptr [edx + 8]
00877ac2: mov ecx, esi
00877ac4: call eax
00877ac6: mov word ptr [esi + 0x3c], 0
00877acc: mov eax, esi
00877ace: mov ecx, dword ptr [ebp - 0xc]
00877ad1: mov dword ptr fs:[0], ecx
00877ad8: pop ecx
00877ad9: pop edi
00877ada: pop esi
00877adb: pop ebx
00877adc: mov esp, ebp
00877ade: pop ebp
00877adf: ret
00877ae0: push ebp
00877ae1: mov ebp, esp
00877ae3: push -1
00877ae5: push 0x2cd33b8
00877aea: mov eax, dword ptr fs:[0]
00877af0: push eax
00877af1: push ecx
00877af2: push esi
00877af3: mov eax, dword ptr [0x399cee0]
00877af8: xor eax, ebp
00877afa: push eax
00877afb: lea eax, [ebp - 0xc]
00877afe: mov dword ptr fs:[0], eax
00877b04: mov esi, ecx
00877b06: mov dword ptr [ebp - 0x10], esi
00877b09: mov dword ptr [esi], 0x30e8944
00877b0f: lea ecx, [esi + 4]
00877b12: mov dword ptr [ebp - 4], 0
00877b19: call 0x28ef780
00877b1e: mov ecx, esi
00877b20: mov dword ptr [ebp - 4], 0xffffffff
00877b27: call 0x140cdc0
00877b2c: test byte ptr [ebp + 8], 1
00877b30: je 0x877b3b
00877b32: push esi
00877b33: call 0x2c5f77e
00877b38: add esp, 4
00877b3b: mov eax, esi
00877b3d: mov ecx, dword ptr [ebp - 0xc]
00877b40: mov dword ptr fs:[0], ecx
00877b47: pop ecx
00877b48: pop esi
00877b49: mov esp, ebp
00877b4b: pop ebp
00877b4c: ret 4
00877b4f: int3
00877b50: push ebp
00877b51: mov ebp, esp
00877b53: mov eax, dword ptr [ebp + 8]
00877b56: sub esp, 0x10
00877b59: push ebx
00877b5a: push 1
00877b5c: mov ebx, ecx
00877b5e: mov ecx, dword ptr [0x3a5c910]
00877b64: push eax
00877b65: call 0x2752a80
00877b6a: test eax, eax
00877b6c: jne 0x877b77
00877b6e: xor al, al
00877b70: pop ebx
00877b71: mov esp, ebp
00877b73: pop ebp
00877b74: ret 4
00877b77: push esi
00877b78: push edi
00877b79: add eax, 0x1198
00877b7e: lea ecx, [ebp - 0x10]
00877b81: push ecx
00877b82: mov ecx, eax
00877b84: mov dword ptr [ebp - 4], eax
00877b87: call 0xe56100
00877b8c: mov edi, dword ptr [ebp - 0x10]
00877b8f: mov esi, dword ptr [ebp - 8]
00877b92: mov ecx, dword ptr [ebp - 4]
00877b95: lea edx, [ebp - 0x10]
00877b98: push edx
00877b99: call 0x293b470
00877b9e: test edi, edi
00877ba0: je 0x877c50
00877ba6: mov edx, dword ptr [edi]
00877ba8: test edx, edx
00877baa: je 0x877c50
00877bb0: mov ecx, dword ptr [eax]
00877bb2: test ecx, ecx
00877bb4: je 0x877bb8
00877bb6: mov ecx, dword ptr [ecx]
00877bb8: cmp edx, ecx
00877bba: jne 0x877c50
00877bc0: cmp esi, dword ptr [eax + 8]
00877bc3: je 0x877c60
00877bc9: lea ecx, [ebx + 0xad8]
00877bcf: mov eax, ecx
00877bd1: and eax, 0xf
00877bd4: lea edx, [ecx + 4]
00877bd7: cmp eax, 0xc
00877bda: ja 0x877c1a
00877bdc: movzx eax, byte ptr [eax + 0x877c80]
00877be3: jmp dword ptr [eax*4 + 0x877c6c]
00877bea: lea eax, [ebp + 8]
00877bed: push eax
00877bee: push edx
00877bef: call 0x27bc0f0
00877bf4: jmp 0x877c24
00877bf6: lea eax, [ebp + 8]
00877bf9: push eax
00877bfa: push edx
00877bfb: call 0x27bc160
00877c00: jmp 0x877c24
00877c02: lea eax, [ebp + 8]
00877c05: push eax
00877c06: push edx
00877c07: call 0x27bc1d0
00877c0c: jmp 0x877c24
00877c0e: lea eax, [ebp + 8]
00877c11: push eax
00877c12: push edx
00877c13: call 0x27bc240
00877c18: jmp 0x877c24
00877c1a: lea eax, [ebp + 8]
00877c1d: push eax
00877c1e: push edx
00877c1f: call 0x27bc390
00877c24: mov eax, dword ptr [edi]
00877c26: test eax, eax
00877c28: je 0x877c50
00877c2a: test esi, esi
00877c2c: je 0x877c50

RANGE 0x8774e0 250
008774e0: cmp edi, 4
008774e3: jl 0x877474
008774e5: cmp dword ptr [esi + 8], ebx
008774e8: jne 0x877500
008774ea: cmp dword ptr [esi + 0xc], ebx
008774ed: jne 0x877500
008774ef: mov dword ptr [ebp - 4], 1
008774f6: jmp 0x8776a2
008774fb: call 0x2c623e1
00877500: mov ecx, dword ptr [0x3a5c980]
00877506: call 0x19e3290
0087750b: mov edi, eax
0087750d: cmp edi, ebx
0087750f: je 0x877561
00877511: call 0x1102c30
00877516: mov ecx, eax
00877518: call 0x11038e0
0087751d: test al, al
0087751f: je 0x877561
00877521: push ebx
00877522: push 0x11eae
00877527: call 0x28c14c0
0087752c: mov ecx, dword ptr [0x3a5c9b8]
00877532: add esp, 4
00877535: push eax
00877536: push 0x209
0087753b: call 0x2304c70
00877540: lea ecx, [ebp - 0x3c]
00877543: mov dword ptr [ebp - 4], 2
0087754a: call 0x2b3d210
0087754f: mov eax, dword ptr [ebp - 0x34]
00877552: push eax
00877553: call 0x2c5f77e
00877558: mov ecx, dword ptr [ebp - 0x3c]
0087755b: push ecx
0087755c: jmp 0x8776b7
00877561: mov ecx, edi
00877563: call 0x27c6f20
00877568: test eax, eax
0087756a: je 0x87767c
00877570: mov ecx, edi
00877572: call 0x27c6f20
00877577: lea ecx, [eax + 0x14]
0087757a: mov eax, ecx
0087757c: and eax, 0xf
0087757f: lea edx, [ecx + 4]
00877582: cmp eax, 0xc
00877585: ja 0x8775c5
00877587: movzx eax, byte ptr [eax + 0x8776ec]
0087758e: jmp dword ptr [eax*4 + 0x8776d8]
00877595: lea eax, [ebp + 8]
00877598: push eax
00877599: push edx
0087759a: call 0x27bc0f0
0087759f: jmp 0x8775cf
008775a1: lea eax, [ebp + 8]
008775a4: push eax
008775a5: push edx
008775a6: call 0x27bc160
008775ab: jmp 0x8775cf
008775ad: lea eax, [ebp + 8]
008775b0: push eax
008775b1: push edx
008775b2: call 0x27bc1d0
008775b7: jmp 0x8775cf
008775b9: lea eax, [ebp + 8]
008775bc: push eax
008775bd: push edx
008775be: call 0x27bc240
008775c3: jmp 0x8775cf
008775c5: lea eax, [ebp + 8]
008775c8: push eax
008775c9: push edx
008775ca: call 0x27bc390
008775cf: mov ecx, dword ptr [ebp + 8]
008775d2: cmp ecx, dword ptr [esi + 8]

RANGE 0x14196b0 100
014196b0: push ebp
014196b1: mov ebp, esp
014196b3: push -1
014196b5: push 0x2da1617
014196ba: mov eax, dword ptr fs:[0]
014196c0: push eax
014196c1: mov eax, 0x27ec
014196c6: call 0x2c63c10
014196cb: mov eax, dword ptr [0x399cee0]
014196d0: xor eax, ebp
014196d2: mov dword ptr [ebp - 0x10], eax
014196d5: push ebx
014196d6: push esi
014196d7: push edi
014196d8: push eax
014196d9: lea eax, [ebp - 0xc]
014196dc: mov dword ptr fs:[0], eax
014196e2: mov dword ptr [ebp - 0x27a0], ecx
014196e8: mov eax, dword ptr [ebp + 8]
014196eb: lea ecx, [ebp - 0x25c0]
014196f1: mov dword ptr [ebp - 0x27a4], eax
014196f7: mov dword ptr [ebp - 0x278c], 0xff6e00ff
01419701: call 0x2918050
01419706: xor ebx, ebx
01419708: lea ecx, [ebp - 0x2790]
0141970e: push ecx
0141970f: mov dword ptr [ebp - 4], ebx
{"memory_final": {"private_bytes": 17485824, "working_set": 26136576, "peak_working_set": 148213760}}

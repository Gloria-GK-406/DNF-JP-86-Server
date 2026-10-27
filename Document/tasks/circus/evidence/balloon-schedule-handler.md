{"mode": "client", "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": {"private_bytes": 9265152, "working_set": 16637952, "peak_working_set": 16637952}}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x11931f0 600
011931f0: outsd dx, dword ptr [esi]
011931f1: sub byte ptr [eax], al
011931f3: mov ecx, eax
011931f5: call 0x1415180
011931fa: push eax
011931fb: call 0x2c6386a
01193200: mov ecx, eax
01193202: add esp, 0x14
01193205: cmp ecx, ebx
01193207: je 0x1193227
01193209: mov eax, dword ptr [ebp - 0x38]
0119320c: cmp eax, ebx
0119320e: je 0x1193221
01193210: cmp eax, 1
01193213: jne 0x1193218
01193215: push eax
01193216: jmp 0x1193222
01193218: cmp eax, 2
0119321b: jne 0x1193227
0119321d: push 1
0119321f: jmp 0x1193222
01193221: push ebx
01193222: call 0x8a6950
01193227: cmp dword ptr [ebp - 0x3c], 0x1007
0119322e: jne 0x1193274
01193230: push ebx
01193231: push 0x3967ec0
01193236: push 0x395eb54
0119323b: push ebx
0119323c: push 0x2c
0119323e: call 0x141a170
01193243: mov ecx, eax
01193245: call 0x1415180
0119324a: push eax
0119324b: call 0x2c6386a
01193250: mov ecx, eax
01193252: add esp, 0x14
01193255: cmp ecx, ebx
01193257: je 0x1193274
01193259: mov eax, dword ptr [ebp - 0x38]
0119325c: cmp eax, ebx
0119325e: je 0x119326e
01193260: cmp eax, 1
01193263: je 0x119326a
01193265: cmp eax, 2
01193268: jne 0x1193274
0119326a: push 1
0119326c: jmp 0x119326f
0119326e: push ebx
0119326f: call 0x14d31a0
01193274: mov ecx, dword ptr [ebp - 0xc]
01193277: mov dword ptr fs:[0], ecx
0119327e: pop ecx
0119327f: pop edi
01193280: pop esi
01193281: pop ebx
01193282: mov esp, ebp
01193284: pop ebp
01193285: ret
01193286: int3
01193287: int3
01193288: int3
01193289: int3
0119328a: int3
0119328b: int3
0119328c: int3
0119328d: int3
0119328e: int3
0119328f: int3
01193290: push ebp
01193291: mov ebp, esp
01193293: push -1
01193295: push 0x2d75feb
0119329a: mov eax, dword ptr fs:[0]
011932a0: push eax
011932a1: sub esp, 0xd8
011932a7: push ebx
011932a8: push esi
011932a9: push edi
011932aa: mov eax, dword ptr [0x399cee0]
011932af: xor eax, ebp
011932b1: push eax
011932b2: lea eax, [ebp - 0xc]
011932b5: mov dword ptr fs:[0], eax
011932bb: lea eax, [ebp - 0x2c]
011932be: xor ebx, ebx
011932c0: push eax
011932c1: mov byte ptr [ebp - 0xd], bl
011932c4: mov dword ptr [ebp - 0x2c], ebx
011932c7: call 0x27c5310
011932cc: lea ecx, [ebp - 0xd]
011932cf: push 1
011932d1: push ecx
011932d2: call 0x27c5230
011932d7: mov edx, dword ptr [0x3a5c9b0]
011932dd: add esp, 0xc
011932e0: push 1
011932e2: push edx
011932e3: call 0x119b940
011932e8: mov ecx, eax
011932ea: call 0x119b2e0
011932ef: mov ecx, eax
011932f1: call 0x171c5a0
011932f6: call 0x19e7810
011932fb: test al, al
011932fd: je 0x1193578
01193303: call 0x1102c30
01193308: mov eax, dword ptr [0x3a5c9b0]
0119330d: push eax
0119330e: call 0x119b940
01193313: mov ecx, eax
01193315: call 0x119b2e0
0119331a: mov esi, eax
0119331c: call 0x1102c30
01193321: mov ecx, dword ptr [0x3a5c9b0]
01193327: push ecx
01193328: call 0x119b940
0119332d: mov ecx, eax
0119332f: call 0x119b390
01193334: mov edi, eax
01193336: mov ecx, edi
01193338: mov dword ptr [ebp - 0x30], edi
0119333b: call 0xc2e760
01193340: mov dword ptr [ebp - 0x14], eax
01193343: cmp esi, ebx
01193345: je 0x1193578
0119334b: mov ecx, esi
0119334d: call 0x171c570
01193352: test eax, eax
01193354: je 0x1193578
0119335a: mov ecx, esi
0119335c: call 0x171c570
01193361: mov dword ptr [ebp - 0x18], eax
01193364: cmp eax, ebx
01193366: je 0x1193578
0119336c: cmp edi, ebx
0119336e: je 0x1193578
01193374: mov ecx, edi
01193376: call 0xc2e5f0
0119337b: test eax, eax
0119337d: je 0x1193578
01193383: mov edi, dword ptr [ebp - 0x14]
01193386: cmp edi, ebx
01193388: je 0x1193578
0119338e: mov eax, dword ptr [ebp - 0x18]
01193391: cmp dword ptr [eax + 0x72c], 1
01193398: jne 0x1193578
0119339e: mov edx, dword ptr [eax + 0x734]
011933a4: add eax, 0x730
011933a9: mov dword ptr [ebp - 0x14], eax
011933ac: cmp edx, dword ptr [eax + 8]
011933af: je 0x1193578
011933b5: mov eax, dword ptr [edi + 0x800]
011933bb: add edi, 0x7fc
011933c1: mov dword ptr [ebp - 0x34], edi
011933c4: cmp eax, dword ptr [edi + 8]
011933c7: je 0x1193578
011933cd: lea ecx, [ebp - 0x24]
011933d0: push ecx
011933d1: mov ecx, edi
011933d3: call 0xe56100
011933d8: lea edx, [ebp - 0x54]
011933db: push edx
011933dc: mov ecx, edi
011933de: call 0x293b470
011933e3: push eax
011933e4: lea ecx, [ebp - 0x24]
011933e7: call 0x2abdfa0
011933ec: cmp al, bl
011933ee: je 0x1193578
011933f4: lea ecx, [ebp - 0x24]
011933f7: call 0x8b2050
011933fc: mov edi, dword ptr [eax]
011933fe: lea eax, [ebp - 0x18]
01193401: push eax
01193402: sub esp, 0xc
01193405: mov ecx, esp
01193407: mov dword ptr [ebp - 0x28], esp
0119340a: push ecx
0119340b: mov ecx, dword ptr [ebp - 0x14]
0119340e: mov dword ptr [ebp - 0x18], edi
01193411: call 0x293b470
01193416: mov ecx, dword ptr [ebp - 0x14]
01193419: sub esp, 0xc
0119341c: mov edx, esp
0119341e: mov dword ptr [ebp - 0x28], esp
01193421: push edx
01193422: call 0xe56100
01193427: lea eax, [ebp - 0xcc]
0119342d: push eax
0119342e: call 0xba92f0
01193433: add esp, 0x20
01193436: lea ecx, [ebp - 0xe4]
0119343c: push ecx
0119343d: mov ecx, dword ptr [ebp - 0x14]
01193440: call 0x293b470
01193445: push eax

RANGE 0x140d980 100
0140d980: inc ebp
0140d981: in al, dx
0140d982: mov dword ptr [eax], 0x5d890000

RANGE 0x1725310 140
01725310: mov esp, 0x84fff18f
01725315: sal byte ptr [esi + ebp - 0x75], 0x83
0172531a: je 0x172531d
0172531c: add byte ptr [eax], al
0172531e: cmp eax, 0x1007
01725323: je 0x1725346
01725325: cmp eax, 0x1012
0172532a: je 0x1725346
0172532c: mov ecx, ebx
0172532e: call 0x171c310
01725333: test eax, eax
01725335: je 0x1725346
01725337: push esi
01725338: mov ecx, ebx
0172533a: call 0x171c310
0172533f: mov ecx, eax
01725341: call 0x1a164c0
01725346: mov edx, dword ptr [esi]
01725348: mov eax, dword ptr [edx + 0x44]
0172534b: push esi
0172534c: mov ecx, esi
0172534e: call eax
01725350: push eax
01725351: call 0x10a5490
01725356: mov ecx, eax
01725358: call 0x10a55d0
0172535d: push esi
0172535e: mov ecx, ebx
01725360: call 0x1721ab0
01725365: call 0x63bb70
0172536a: test al, al
0172536c: je 0x1725376
0172536e: push esi
0172536f: mov ecx, ebx
01725371: call 0x1723e90
01725376: mov ecx, dword ptr [ebp - 0xc]
01725379: mov dword ptr fs:[0], ecx
01725380: pop ecx
01725381: pop edi
01725382: pop esi
01725383: pop ebx
01725384: mov esp, ebp
01725386: pop ebp
01725387: ret 0x28
0172538a: mov dword ptr [ebp + 0x24], ecx
0172538d: lea ecx, [ebp + 0x24]
01725390: push ecx
01725391: lea ecx, [ebp - 0x1c]
01725394: call 0x2c616cd

RANGE 0x117e790 100
0117e790: xchg ebp, eax
0117e791: add ebp, dword ptr [edx]
0117e794: push 0
0117e796: push 0x1f
0117e798: call 0x2301e60
0117e79d: push eax
0117e79e: call 0x2c6386a
0117e7a3: mov esi, eax
0117e7a5: add esp, 0x14
0117e7a8: test esi, esi
0117e7aa: je 0x117f062
0117e7b0: mov eax, dword ptr [ebp - 0x216]
0117e7b6: cmp eax, -1
0117e7b9: je 0x117e7d8
0117e7bb: cmp byte ptr [ebp - 0x20b], 0
0117e7c2: setne cl
0117e7c5: movzx edx, cl
0117e7c8: push edx
0117e7c9: lea ecx, [ebp - 0xcc]
0117e7cf: push ecx
0117e7d0: push eax
0117e7d1: mov ecx, esi
0117e7d3: call 0x12333f0
0117e7d8: mov ecx, esi
0117e7da: call 0x1233200
0117e7df: mov esi, eax
0117e7e1: test esi, esi
0117e7e3: je 0x117f062
0117e7e9: push 0
0117e7eb: push 0
0117e7ed: movzx edx, byte ptr [ebp - 0x1c9]
{"memory_final": {"private_bytes": 17547264, "working_set": 26234880, "peak_working_set": 148320256}}

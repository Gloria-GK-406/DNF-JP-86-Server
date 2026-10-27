{"mode": "client", "memory_budget": "384 MiB target; 512 MiB abort; mmap, max 40 matches per needle, disassembly only explicit ranges", "memory": {"private_bytes": 8794112, "working_set": 16105472, "peak_working_set": 16105472}}
{"path": "E:\\Project\\DNF\\DNFClient-A21-debug\\dumps\\dnf-dump-20260922-194214-883-23144\\DNF-memory-rva.bin", "size": 122085376, "sha256": "aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d"}

RANGE 0x11495c0 82
011495c0: push ebp
011495c1: mov ebp, esp
011495c3: sub esp, 8
011495c6: lea eax, [ebp - 8]
011495c9: push eax
011495ca: mov dword ptr [ebp - 8], 0
011495d1: mov dword ptr [ebp - 4], 0
011495d8: call 0x27c5310
011495dd: lea ecx, [ebp - 4]
011495e0: push ecx
011495e1: call 0x27c5310
011495e6: add esp, 8
011495e9: call 0x1102c30
011495ee: push eax
011495ef: call 0x119b940
011495f4: mov ecx, eax
011495f6: call 0x119b2b0
011495fb: test eax, eax
011495fd: je 0x114960e
011495ff: mov edx, dword ptr [ebp - 4]
01149602: mov ecx, dword ptr [ebp - 8]
01149605: push edx
01149606: push ecx
01149607: mov ecx, eax
01149609: call 0x7db370
0114960e: mov esp, ebp
01149610: pop ebp
01149611: ret

RANGE 0x7db370 25
007db370: push ebp
007db371: mov ebp, esp
007db373: mov eax, dword ptr [ebp + 8]
007db376: mov edx, dword ptr [ebp + 0xc]
007db379: mov dword ptr [ecx + 0x138], eax
007db37f: mov dword ptr [ecx + 0x13c], edx
007db385: pop ebp
007db386: ret 8
{"memory_final": {"private_bytes": 17162240, "working_set": 25821184, "peak_working_set": 147906560}}

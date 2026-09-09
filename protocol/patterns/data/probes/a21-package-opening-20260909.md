# A21 package opening instruction evidence

Client SHA-256 baseline: `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`.

Source dump: `E:\Project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260831-083553-079-22620\DNF-memory-rva.bin`. Addresses below are RVA; absolute operands retain VA (image base 0x400000). Capstone 5.0.7, x86, copied instruction bytes; bounded ranges may stop inside later instructions.

## Batch and single construction

```text
010B29BD 6850040000         push 0x450
010B29C2 e889a02901         call 0x234ca50
010B29C7 8bc8               mov ecx, eax
010B29C9 e812a82901         call 0x234d1e0
010B29CE 6a00               push 0
010B29D0 e87ba02901         call 0x234ca50
010B29D5 8bc8               mov ecx, eax
010B29D7 e804a92901         call 0x234d2e0
010B29DC 57                 push edi
010B29DD e86ea02901         call 0x234ca50
010B29E2 8bc8               mov ecx, eax
010B29E4 e827a92901         call 0x234d310
010B29E9 6848b39800         push 0x98b348
010B29EE e85da02901         call 0x234ca50
010B29F3 8bc8               mov ecx, eax
010B29F5 e846a92901         call 0x234d340
010B29FA 53                 push ebx
010B29FB e850a02901         call 0x234ca50
010B2A00 8bc8               mov ecx, eax
010B2A02 e809a92901         call 0x234d310
010B2A07 6847b39800         push 0x98b347
010B2A0C e83fa02901         call 0x234ca50
010B2A11 8bc8               mov ecx, eax
010B2A13 e828a92901         call 0x234d340
010B2A18 0fb78674010000     movzx eax, word ptr [esi + 0x174]
010B2A1F 50                 push eax
010B2A20 eb2d               jmp 0x10b2a4f
010B2A22 68d0000000         push 0xd0
010B2A27 e824a02901         call 0x234ca50
010B2A2C 8bc8               mov ecx, eax
010B2A2E e8ada72901         call 0x234d1e0
010B2A33 6a00               push 0
010B2A35 e816a02901         call 0x234ca50
010B2A3A 8bc8               mov ecx, eax
010B2A3C e89fa82901         call 0x234d2e0
010B2A41 57                 push edi
010B2A42 e809a02901         call 0x234ca50
010B2A47 8bc8               mov ecx, eax
010B2A49 e8c2a82901         call 0x234d310
010B2A4E 53                 push ebx
010B2A4F e8fc9f2901         call 0x234ca50
010B2A54 8bc8               mov ecx, eax
010B2A56 e8b5a82901         call 0x234d310
010B2A5B e870bc2901         call 0x234e6d0
010B2A60 8b4e04             mov ecx, dword ptr [esi + 4]
```

## No-material construction

```text
01203A28 50                 push eax
01203A29 6850040000         push 0x450
01203A2E e81d901401         call 0x234ca50
01203A33 8bc8               mov ecx, eax
01203A35 e8a6971401         call 0x234d1e0
01203A3A 6a04               push 4
01203A3C e80f901401         call 0x234ca50
01203A41 8bc8               mov ecx, eax
01203A43 e898981401         call 0x234d2e0
01203A48 57                 push edi
01203A49 e802901401         call 0x234ca50
01203A4E 8bc8               mov ecx, eax
01203A50 e8bb981401         call 0x234d310
01203A55 8b4e24             mov ecx, dword ptr [esi + 0x24]
01203A58 51                 push ecx
01203A59 e8f28f1401         call 0x234ca50
01203A5E 8bc8               mov ecx, eax
01203A60 e8db981401         call 0x234d340
01203A65 68ffff0000         push 0xffff
01203A6A e8e18f1401         call 0x234ca50
01203A6F 8bc8               mov ecx, eax
01203A71 e89a981401         call 0x234d310
01203A76 6aff               push -1
01203A78 e8d38f1401         call 0x234ca50
01203A7D 8bc8               mov ecx, eax
01203A7F e8bc981401         call 0x234d340
01203A84 6a0a               push 0xa
01203A86 eb3d               jmp 0x1203ac5
01203A88 83f801             cmp eax, 1
01203A8B 7549               jne 0x1203ad6
01203A8D 83ffff             cmp edi, -1
01203A90 7444               je 0x1203ad6
01203A92 6a00               push 0
01203A94 68d0000000         push 0xd0
01203A99 e8b28f1401         call 0x234ca50
01203A9E 8bc8               mov ecx, eax
01203AA0 e83b971401         call 0x234d1e0
01203AA5 6a04               push 4
01203AA7 e8a48f1401         call 0x234ca50
01203AAC 8bc8               mov ecx, eax
01203AAE e82d981401         call 0x234d2e0
01203AB3 57                 push edi
01203AB4 e8978f1401         call 0x234ca50
01203AB9 8bc8               mov ecx, eax
01203ABB e850981401         call 0x234d310
01203AC0 68ffff0000         push 0xffff
01203AC5 e8868f1401         call 0x234ca50
01203ACA 8bc8               mov ecx, eax
01203ACC e83f981401         call 0x234d310
01203AD1 e8faab1401         call 0x234e6d0
```

## Basic package construction

```text
00E676A0 e83b5b4e01         call 0x234d1e0
00E676A5 0fb79694010000     movzx edx, word ptr [esi + 0x194]
00E676AC 52                 push edx
00E676AD e89e534e01         call 0x234ca50
00E676B2 8bc8               mov ecx, eax
00E676B4 e8575c4e01         call 0x234d310
00E676B9 e812704e01         call 0x234e6d0
00E676BE 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00E676C4 6a00               push 0
00E676C6 6aff               push -1
```

## Selection construction

```text
0111B50A e8d11c2301         call 0x234d1e0
0111B50F 57                 push edi
0111B510 e83b152301         call 0x234ca50
0111B515 8bc8               mov ecx, eax
0111B517 e8f41d2301         call 0x234d310
0111B51C 0fb693e8010000     movzx edx, byte ptr [ebx + 0x1e8]
0111B523 52                 push edx
0111B524 e827152301         call 0x234ca50
0111B529 8bc8               mov ecx, eax
0111B52B e8b01d2301         call 0x234d2e0
0111B530 0fb683ec010000     movzx eax, byte ptr [ebx + 0x1ec]
0111B537 50                 push eax
0111B538 e813152301         call 0x234ca50
0111B53D 8bc8               mov ecx, eax
0111B53F e89c1d2301         call 0x234d2e0
0111B544 83bba801000000     cmp dword ptr [ebx + 0x1a8], 0
0111B54B 7421               je 0x111b56e
0111B54D 8b8ba8010000       mov ecx, dword ptr [ebx + 0x1a8]
0111B553 8b11               mov edx, dword ptr [ecx]
0111B555 8b02               mov eax, dword ptr [edx]
0111B557 ffd0               call eax
0111B559 83f801             cmp eax, 1
0111B55C 7510               jne 0x111b56e
0111B55E 8b8ba8010000       mov ecx, dword ptr [ebx + 0x1a8]
0111B564 8b11               mov edx, dword ptr [ecx]
0111B566 8b8298000000       mov eax, dword ptr [edx + 0x98]
0111B56C ffd0               call eax
0111B56E 8b8bf0010000       mov ecx, dword ptr [ebx + 0x1f0]
0111B574 83790800           cmp dword ptr [ecx + 8], 0
0111B578 0f8588000000       jne 0x111b606
0111B57E 8d55f0             lea edx, [ebp - 0x10]
0111B581 52                 push edx
0111B582 8d8bd4010000       lea ecx, [ebx + 0x1d4]
0111B588 33ff               xor edi, edi
0111B58A e871ab93ff         call 0xa56100
0111B58F 8b08               mov ecx, dword ptr [eax]
0111B591 85c9               test ecx, ecx
0111B593 7408               je 0x111b59d
0111B595 8b09               mov ecx, dword ptr [ecx]
0111B597 85c9               test ecx, ecx
0111B599 7402               je 0x111b59d
0111B59B 8b39               mov edi, dword ptr [ecx]
0111B59D 8b7008             mov esi, dword ptr [eax + 8]
0111B5A0 8d45f0             lea eax, [ebp - 0x10]
0111B5A3 50                 push eax
0111B5A4 8d8bd4010000       lea ecx, [ebx + 0x1d4]
0111B5AA e8c1fe4101         call 0x253b470
0111B5AF 85ff               test edi, edi
0111B5B1 744e               je 0x111b601
0111B5B3 8b17               mov edx, dword ptr [edi]
0111B5B5 85d2               test edx, edx
0111B5B7 7448               je 0x111b601
0111B5B9 8b08               mov ecx, dword ptr [eax]
0111B5BB 85c9               test ecx, ecx
0111B5BD 7402               je 0x111b5c1
0111B5BF 8b09               mov ecx, dword ptr [ecx]
0111B5C1 3bd1               cmp edx, ecx
0111B5C3 753c               jne 0x111b601
0111B5C5 3b7008             cmp esi, dword ptr [eax + 8]
0111B5C8 0f8479000000       je 0x111b647
0111B5CE 8bc2               mov eax, edx
0111B5D0 85c0               test eax, eax
0111B5D2 742d               je 0x111b601
0111B5D4 85f6               test esi, esi
0111B5D6 7429               je 0x111b601
0111B5D8 397004             cmp dword ptr [eax + 4], esi
0111B5DB 7724               ja 0x111b601
0111B5DD 3b7008             cmp esi, dword ptr [eax + 8]
0111B5E0 731f               jae 0x111b601
0111B5E2 8b0e               mov ecx, dword ptr [esi]
0111B5E4 51                 push ecx
0111B5E5 e866142301         call 0x234ca50
0111B5EA 8bc8               mov ecx, eax
0111B5EC e84f1d2301         call 0x234d340
0111B5F1 8b07               mov eax, dword ptr [edi]
0111B5F3 85c0               test eax, eax
0111B5F5 740a               je 0x111b601
0111B5F7 3b7008             cmp esi, dword ptr [eax + 8]
0111B5FA 7305               jae 0x111b601
0111B5FC 83c644             add esi, 0x44
0111B5FF eb9f               jmp 0x111b5a0
0111B601 e8db6d7401         call 0x28623e1
0111B606 8b8bec020000       mov ecx, dword ptr [ebx + 0x2ec]
0111B60C 8b83f0020000       mov eax, dword ptr [ebx + 0x2f0]
0111B612 2bc1               sub eax, ecx
0111B614 c1f802             sar eax, 2
0111B617 33f6               xor esi, esi
0111B619 85c0               test eax, eax
0111B61B 742a               je 0x111b647
0111B61D 3bf0               cmp esi, eax
0111B61F 73e0               jae 0x111b601
0111B621 8b14b1             mov edx, dword ptr [ecx + esi*4]
0111B624 52                 push edx
0111B625 e826142301         call 0x234ca50
0111B62A 8bc8               mov ecx, eax
0111B62C e80f1d2301         call 0x234d340
0111B631 8b8bec020000       mov ecx, dword ptr [ebx + 0x2ec]
0111B637 8b83f0020000       mov eax, dword ptr [ebx + 0x2f0]
0111B63D 2bc1               sub eax, ecx
0111B63F 46                 inc esi
0111B640 c1f802             sar eax, 2
0111B643 3bf0               cmp esi, eax
0111B645 72da               jb 0x111b621
0111B647 8b7508             mov esi, dword ptr [ebp + 8]
0111B64A 8b4608             mov eax, dword ptr [esi + 8]
0111B64D 2b4604             sub eax, dword ptr [esi + 4]
0111B650 c1f802             sar eax, 2
0111B653 50                 push eax
0111B654 e8f7132301         call 0x234ca50
0111B659 8bc8               mov ecx, eax
0111B65B e8801c2301         call 0x234d2e0
0111B660 8d45f0             lea eax, [ebp - 0x10]
0111B663 50                 push eax
0111B664 8bce               mov ecx, esi
0111B666 e895aa93ff         call 0xa56100
0111B66B 8b5df0             mov ebx, dword ptr [ebp - 0x10]
0111B66E 8b7df8             mov edi, dword ptr [ebp - 8]
0111B671 8d4df0             lea ecx, [ebp - 0x10]
0111B674 51                 push ecx
0111B675 8bce               mov ecx, esi
0111B677 e8f4fd4101         call 0x253b470
0111B67C 85db               test ebx, ebx
0111B67E 7481               je 0x111b601
0111B680 8b13               mov edx, dword ptr [ebx]
0111B682 85d2               test edx, edx
0111B684 0f8477ffffff       je 0x111b601
0111B68A 8b08               mov ecx, dword ptr [eax]
0111B68C 85c9               test ecx, ecx
0111B68E 7402               je 0x111b692
0111B690 8b09               mov ecx, dword ptr [ecx]
0111B692 3bd1               cmp edx, ecx
0111B694 0f8567ffffff       jne 0x111b601
0111B69A 3b7808             cmp edi, dword ptr [eax + 8]
0111B69D 7465               je 0x111b704
0111B69F 8bc2               mov eax, edx
0111B6A1 85c0               test eax, eax
0111B6A3 0f8458ffffff       je 0x111b601
0111B6A9 85ff               test edi, edi
0111B6AB 0f8450ffffff       je 0x111b601
0111B6B1 397804             cmp dword ptr [eax + 4], edi
0111B6B4 0f8747ffffff       ja 0x111b601
0111B6BA 3b7808             cmp edi, dword ptr [eax + 8]
0111B6BD 0f833effffff       jae 0x111b601
0111B6C3 8b37               mov esi, dword ptr [edi]
0111B6C5 8b5614             mov edx, dword ptr [esi + 0x14]
0111B6C8 52                 push edx
0111B6C9 e882132301         call 0x234ca50
0111B6CE 8bc8               mov ecx, eax
0111B6D0 e86b1c2301         call 0x234d340
0111B6D5 0fb6461c           movzx eax, byte ptr [esi + 0x1c]
0111B6D9 50                 push eax
0111B6DA e871132301         call 0x234ca50
0111B6DF 8bc8               mov ecx, eax
0111B6E1 e8fa1b2301         call 0x234d2e0
0111B6E6 8b03               mov eax, dword ptr [ebx]
0111B6E8 85c0               test eax, eax
0111B6EA 0f8411ffffff       je 0x111b601
0111B6F0 3b7808             cmp edi, dword ptr [eax + 8]
0111B6F3 0f8308ffffff       jae 0x111b601
0111B6F9 8b7508             mov esi, dword ptr [ebp + 8]
0111B6FC 83c704             add edi, 4
0111B6FF e96dffffff         jmp 0x111b671
0111B704 e8c72f2301         call 0x234e6d0
0111B709 8b4dfc             mov ecx, dword ptr [ebp - 4]
0111B70C 8b11               mov edx, dword ptr [ecx]
0111B70E 8b4278             mov eax, dword ptr [edx + 0x78]
0111B711 ffd0               call eax
0111B713 5e                 pop esi
0111B714 5f                 pop edi
0111B715 5b                 pop ebx
0111B716 8be5               mov esp, ebp
0111B718 5d                 pop ebp
```

## Avatar construction

```text
00EBBA8B 53                 push ebx
00EBBA8C 6a00               push 0
00EBBA8E 6807020000         push 0x207
00EBBA93 e8b80f4901         call 0x234ca50
00EBBA98 8bc8               mov ecx, eax
00EBBA9A e841174901         call 0x234d1e0
00EBBA9F 57                 push edi
00EBBAA0 e8ab0f4901         call 0x234ca50
00EBBAA5 8bc8               mov ecx, eax
00EBBAA7 e864184901         call 0x234d310
00EBBAAC 8b862c040000       mov eax, dword ptr [esi + 0x42c]
00EBBAB2 2b8628040000       sub eax, dword ptr [esi + 0x428]
00EBBAB8 81c624040000       add esi, 0x424
00EBBABE c1f802             sar eax, 2
00EBBAC1 50                 push eax
00EBBAC2 8975fc             mov dword ptr [ebp - 4], esi
00EBBAC5 e8860f4901         call 0x234ca50
00EBBACA 8bc8               mov ecx, eax
00EBBACC e80f184901         call 0x234d2e0
00EBBAD1 8d4df0             lea ecx, [ebp - 0x10]
00EBBAD4 51                 push ecx
00EBBAD5 8bce               mov ecx, esi
00EBBAD7 e824a6b9ff         call 0xa56100
00EBBADC 8b5df0             mov ebx, dword ptr [ebp - 0x10]
00EBBADF 8b7df8             mov edi, dword ptr [ebp - 8]
00EBBAE2 8d55f0             lea edx, [ebp - 0x10]
00EBBAE5 52                 push edx
00EBBAE6 8bce               mov ecx, esi
00EBBAE8 e883f96701         call 0x253b470
00EBBAED 85db               test ebx, ebx
00EBBAEF 7461               je 0xebbb52
00EBBAF1 8b13               mov edx, dword ptr [ebx]
00EBBAF3 85d2               test edx, edx
00EBBAF5 745b               je 0xebbb52
00EBBAF7 8b08               mov ecx, dword ptr [eax]
00EBBAF9 85c9               test ecx, ecx
00EBBAFB 7402               je 0xebbaff
00EBBAFD 8b09               mov ecx, dword ptr [ecx]
00EBBAFF 3bd1               cmp edx, ecx
00EBBB01 754f               jne 0xebbb52
00EBBB03 3b7808             cmp edi, dword ptr [eax + 8]
00EBBB06 744f               je 0xebbb57
00EBBB08 8bc2               mov eax, edx
00EBBB0A 85c0               test eax, eax
00EBBB0C 7444               je 0xebbb52
00EBBB0E 85ff               test edi, edi
00EBBB10 7440               je 0xebbb52
00EBBB12 397804             cmp dword ptr [eax + 4], edi
00EBBB15 773b               ja 0xebbb52
00EBBB17 3b7808             cmp edi, dword ptr [eax + 8]
00EBBB1A 7336               jae 0xebbb52
00EBBB1C 8b37               mov esi, dword ptr [edi]
00EBBB1E 8b4614             mov eax, dword ptr [esi + 0x14]
00EBBB21 50                 push eax
00EBBB22 e8290f4901         call 0x234ca50
00EBBB27 8bc8               mov ecx, eax
00EBBB29 e812184901         call 0x234d340
00EBBB2E 0fb64e1c           movzx ecx, byte ptr [esi + 0x1c]
00EBBB32 51                 push ecx
00EBBB33 e8180f4901         call 0x234ca50
00EBBB38 8bc8               mov ecx, eax
00EBBB3A e8a1174901         call 0x234d2e0
00EBBB3F 8b03               mov eax, dword ptr [ebx]
00EBBB41 85c0               test eax, eax
00EBBB43 740d               je 0xebbb52
00EBBB45 3b7808             cmp edi, dword ptr [eax + 8]
00EBBB48 7308               jae 0xebbb52
00EBBB4A 8b75fc             mov esi, dword ptr [ebp - 4]
00EBBB4D 83c704             add edi, 4
00EBBB50 eb90               jmp 0xebbae2
00EBBB52 e88a689a01         call 0x28623e1
00EBBB57 e8742b4901         call 0x234e6d0
```

## Shared reward row reader

```text
00D152C0 55                 push ebp
00D152C1 8bec               mov ebp, esp
00D152C3 6aff               push -1
00D152C5 683123d702         push 0x2d72331
00D152CA 64a100000000       mov eax, dword ptr fs:[0]
00D152D0 50                 push eax
00D152D1 81ec20010000       sub esp, 0x120
00D152D7 a1e0ce9903         mov eax, dword ptr [0x399cee0]
00D152DC 33c5               xor eax, ebp
00D152DE 8945f0             mov dword ptr [ebp - 0x10], eax
00D152E1 53                 push ebx
00D152E2 56                 push esi
00D152E3 57                 push edi
00D152E4 50                 push eax
00D152E5 8d45f4             lea eax, [ebp - 0xc]
00D152E8 64a300000000       mov dword ptr fs:[0], eax
00D152EE 8b450c             mov eax, dword ptr [ebp + 0xc]
00D152F1 8b7d10             mov edi, dword ptr [ebp + 0x10]
00D152F4 8d8d04ffffff       lea ecx, [ebp - 0xfc]
00D152FA 33db               xor ebx, ebx
00D152FC 51                 push ecx
00D152FD 8985e8feffff       mov dword ptr [ebp - 0x118], eax
00D15303 89bdfcfeffff       mov dword ptr [ebp - 0x104], edi
00D15309 c68523ffffff01     mov byte ptr [ebp - 0xdd], 1
00D15310 881f               mov byte ptr [edi], bl
00D15312 899d04ffffff       mov dword ptr [ebp - 0xfc], ebx
00D15318 e883ff6a01         call 0x23c52a0
00D1531D 8b8504ffffff       mov eax, dword ptr [ebp - 0xfc]
00D15323 33d2               xor edx, edx
00D15325 83c404             add esp, 4
00D15328 663bd0             cmp dx, ax
00D1532B 0f8373030000       jae 0xd156a4
00D15331 33c9               xor ecx, ecx
00D15333 899d00ffffff       mov dword ptr [ebp - 0x100], ebx
00D15339 663bc8             cmp cx, ax
00D1533C 0f8362030000       jae 0xd156a4
00D15342 8d9510ffffff       lea edx, [ebp - 0xf0]
00D15348 52                 push edx
00D15349 899d10ffffff       mov dword ptr [ebp - 0xf0], ebx
00D1534F 899d1cffffff       mov dword ptr [ebp - 0xe4], ebx
00D15355 899d0cffffff       mov dword ptr [ebp - 0xf4], ebx
00D1535B 899decfeffff       mov dword ptr [ebp - 0x114], ebx
00D15361 889d1affffff       mov byte ptr [ebp - 0xe6], bl
00D15367 e834ff6a01         call 0x23c52a0
00D1536C 8d851cffffff       lea eax, [ebp - 0xe4]
00D15372 50                 push eax
00D15373 e898ff6a01         call 0x23c5310
00D15378 8d8d0cffffff       lea ecx, [ebp - 0xf4]
00D1537E 51                 push ecx
00D1537F e88cff6a01         call 0x23c5310
00D15384 8d95ecfeffff       lea edx, [ebp - 0x114]
00D1538A 52                 push edx
00D1538B e810ff6a01         call 0x23c52a0
00D15390 8d851affffff       lea eax, [ebp - 0xe6]
00D15396 6a01               push 1
00D15398 50                 push eax
00D15399 e892fe6a01         call 0x23c5230
00D1539E 8d8dd4feffff       lea ecx, [ebp - 0x12c]
00D153A4 51                 push ecx
00D153A5 e846172900         call 0xfa6af0
00D153AA 83c41c             add esp, 0x1c
00D153AD 8b951cffffff       mov edx, dword ptr [ebp - 0xe4]
00D153B3 8b08               mov ecx, dword ptr [eax]
00D153B5 52                 push edx
00D153B6 895dfc             mov dword ptr [ebp - 4], ebx
00D153B9 e832d32800         call 0xfa26f0
00D153BE 0807               or byte ptr [edi], al
00D153C0 8b85d8feffff       mov eax, dword ptr [ebp - 0x128]
00D153C6 c745fcffffffff     mov dword ptr [ebp - 4], 0xffffffff
00D153CD 3bc3               cmp eax, ebx
00D153CF 742b               je 0xd153fc
00D153D1 8bf0               mov esi, eax
00D153D3 83c004             add eax, 4
00D153D6 83c9ff             or ecx, 0xffffffff
00D153D9 f00fc108           lock xadd dword ptr [eax], ecx
00D153DD 751d               jne 0xd153fc
00D153DF 8b16               mov edx, dword ptr [esi]
00D153E1 8b02               mov eax, dword ptr [edx]
00D153E3 8bce               mov ecx, esi
00D153E5 ffd0               call eax
00D153E7 8d4e08             lea ecx, [esi + 8]
00D153EA 83caff             or edx, 0xffffffff
00D153ED f00fc111           lock xadd dword ptr [ecx], edx
00D153F1 7509               jne 0xd153fc
00D153F3 8b06               mov eax, dword ptr [esi]
00D153F5 8b5004             mov edx, dword ptr [eax + 4]
00D153F8 8bce               mov ecx, esi
00D153FA ffd2               call edx
00D153FC 8d85f0feffff       lea eax, [ebp - 0x110]
00D15402 50                 push eax
00D15403 899df0feffff       mov dword ptr [ebp - 0x110], ebx
00D15409 e802ff6a01         call 0x23c5310
00D1540E e81dd8feff         call 0xd02c30
00D15413 8d4de0             lea ecx, [ebp - 0x20]
00D15416 51                 push ecx
00D15417 e864265200         call 0x1237a80
00D1541C 83c408             add esp, 8
00D1541F e8fcd01400         call 0xe62520
00D15424 8bc8               mov ecx, eax
00D15426 e8c5be1400         call 0xe612f0
00D1542B e8c076f7ff         call 0xc8caf0
00D15430 8bc8               mov ecx, eax
00D15432 e88937f7ff         call 0xc88bc0
00D15437 8d9517ffffff       lea edx, [ebp - 0xe9]
00D1543D 6a01               push 1
00D1543F 52                 push edx
00D15440 889d17ffffff       mov byte ptr [ebp - 0xe9], bl
00D15446 e8e5fd6a01         call 0x23c5230
00D1544B 8d8515ffffff       lea eax, [ebp - 0xeb]
00D15451 6a01               push 1
00D15453 50                 push eax
00D15454 889d15ffffff       mov byte ptr [ebp - 0xeb], bl
00D1545A e8d1fd6a01         call 0x23c5230
00D1545F 8d8d16ffffff       lea ecx, [ebp - 0xea]
00D15465 6a01               push 1
00D15467 51                 push ecx
00D15468 889d16ffffff       mov byte ptr [ebp - 0xea], bl
00D1546E e8bdfd6a01         call 0x23c5230
00D15473 8d95e4feffff       lea edx, [ebp - 0x11c]
00D15479 52                 push edx
00D1547A e821fe6a01         call 0x23c52a0
00D1547F 8d850bffffff       lea eax, [ebp - 0xf5]
00D15485 6a01               push 1
00D15487 50                 push eax
00D15488 e8a3fd6a01         call 0x23c5230
00D1548D 8d8df4feffff       lea ecx, [ebp - 0x10c]
00D15493 6a01               push 1
00D15495 51                 push ecx
00D15496 e895fd6a01         call 0x23c5230
00D1549B 8b95f4feffff       mov edx, dword ptr [ebp - 0x10c]
00D154A1 83c42c             add esp, 0x2c
00D154A4 52                 push edx
00D154A5 e84676f7ff         call 0xc8caf0
00D154AA 8bc8               mov ecx, eax
00D154AC e85f03f7ff         call 0xc85810
00D154B1 8d851bffffff       lea eax, [ebp - 0xe5]
00D154B7 6a01               push 1
00D154B9 50                 push eax
00D154BA 889d1bffffff       mov byte ptr [ebp - 0xe5], bl
00D154C0 e86bfd6a01         call 0x23c5230
00D154C5 8d8d22ffffff       lea ecx, [ebp - 0xde]
00D154CB 6a01               push 1
00D154CD 51                 push ecx
00D154CE 889d22ffffff       mov byte ptr [ebp - 0xde], bl
00D154D4 e857fd6a01         call 0x23c5230
00D154D9 8d9519ffffff       lea edx, [ebp - 0xe7]
00D154DF 6a01               push 1
00D154E1 52                 push edx
00D154E2 889d19ffffff       mov byte ptr [ebp - 0xe7], bl
00D154E8 e843fd6a01         call 0x23c5230
00D154ED 83c418             add esp, 0x18
00D154F0 8d8df8feffff       lea ecx, [ebp - 0x108]
00D154F6 e835118001         call 0x2516630
00D154FB 8d85f8feffff       lea eax, [ebp - 0x108]
00D15501 50                 push eax
00D15502 e8d9a66301         call 0x234fbe0
00D15507 83c404             add esp, 4
00D1550A 8d4de8             lea ecx, [ebp - 0x18]
00D1550D e82e118001         call 0x2516640
00D15512 8d4de8             lea ecx, [ebp - 0x18]
00D15515 51                 push ecx
00D15516 e8c5a66301         call 0x234fbe0
00D1551B 8d9518ffffff       lea edx, [ebp - 0xe8]
00D15521 6a01               push 1
00D15523 52                 push edx
00D15524 889d18ffffff       mov byte ptr [ebp - 0xe8], bl
00D1552A e801fd6a01         call 0x23c5230
00D1552F 83c40c             add esp, 0xc
00D15532 b86d010000         mov eax, 0x16d
00D15537 66398510ffffff     cmp word ptr [ebp - 0xf0], ax
00D1553E 7509               jne 0xd15549
00D15540 385d08             cmp byte ptr [ebp + 8], bl
00D15543 0f843f010000       je 0xd15688
00D15549 8b8d1cffffff       mov ecx, dword ptr [ebp - 0xe4]
00D1554F 51                 push ecx
00D15550 e82b16ddff         call 0xae6b80
00D15555 83c404             add esp, 4
00D15558 83f801             cmp eax, 1
00D1555B 740a               je 0xd15567
00D1555D c7850cffffff01000000 mov dword ptr [ebp - 0xf4], 1
00D15567 baffff0000         mov edx, 0xffff
00D1556C 33f6               xor esi, esi
00D1556E 66399510ffffff     cmp word ptr [ebp - 0xf0], dx
00D15575 753d               jne 0xd155b4
00D15577 6a01               push 1
00D15579 8d8d24ffffff       lea ecx, [ebp - 0xdc]
00D1557F e8cc6c4300         call 0x114c250
00D15584 53                 push ebx
00D15585 6a01               push 1
00D15587 50                 push eax
00D15588 8b851cffffff       mov eax, dword ptr [ebp - 0xe4]
00D1558E 50                 push eax
00D1558F c745fc01000000     mov dword ptr [ebp - 4], 1
00D15596 e885fc9600         call 0x1685220
00D1559B 83c410             add esp, 0x10
00D1559E 8d8d24ffffff       lea ecx, [ebp - 0xdc]
00D155A4 8bf0               mov esi, eax
00D155A6 c745fcffffffff     mov dword ptr [ebp - 4], 0xffffffff
00D155AD e81e3232ff         call 0x387d0
00D155B2 eb4c               jmp 0xd15600
00D155B4 8b8d1cffffff       mov ecx, dword ptr [ebp - 0xe4]
00D155BA 51                 push ecx
00D155BB e85017ddff         call 0xae6d10
00D155C0 50                 push eax
00D155C1 e8fa345300         call 0x1248ac0
00D155C6 ba6d010000         mov edx, 0x16d
00D155CB 83c408             add esp, 8
00D155CE 8bf8               mov edi, eax
00D155D0 66399510ffffff     cmp word ptr [ebp - 0xf0], dx
00D155D7 7527               jne 0xd15600
00D155D9 3bfb               cmp edi, ebx
00D155DB 7431               je 0xd1560e
00D155DD 8b851cffffff       mov eax, dword ptr [ebp - 0xe4]
00D155E3 50                 push eax
00D155E4 8bcf               mov ecx, edi
00D155E6 e8959f5d00         call 0x12ef580
00D155EB 83f8ff             cmp eax, -1
00D155EE 7410               je 0xd15600
00D155F0 50                 push eax
00D155F1 8bcf               mov ecx, edi
00D155F3 e8c8905d00         call 0x12ee6c0
00D155F8 8bf0               mov esi, eax
00D155FA 889d23ffffff       mov byte ptr [ebp - 0xdd], bl
00D15600 b9ffff0000         mov ecx, 0xffff
00D15605 66398d10ffffff     cmp word ptr [ebp - 0xf0], cx
00D1560C 744c               je 0xd1565a
00D1560E 389d23ffffff       cmp byte ptr [ebp - 0xdd], bl
00D15614 7444               je 0xd1565a
00D15616 3bf3               cmp esi, ebx
00D15618 7544               jne 0xd1565e
00D1561A 8b950cffffff       mov edx, dword ptr [ebp - 0xf4]
00D15620 52                 push edx
00D15621 8d8d24ffffff       lea ecx, [ebp - 0xdc]
00D15627 e8246c4300         call 0x114c250
00D1562C 53                 push ebx
00D1562D 6a01               push 1
00D1562F 50                 push eax
00D15630 8b851cffffff       mov eax, dword ptr [ebp - 0xe4]
00D15636 50                 push eax
00D15637 c745fc02000000     mov dword ptr [ebp - 4], 2
00D1563E e8ddfb9600         call 0x1685220
00D15643 83c410             add esp, 0x10
00D15646 8d8d24ffffff       lea ecx, [ebp - 0xdc]
00D1564C 8bf0               mov esi, eax
00D1564E c745fcffffffff     mov dword ptr [ebp - 4], 0xffffffff
00D15655 e8763132ff         call 0x387d0
00D1565A 3bf3               cmp esi, ebx
00D1565C 7424               je 0xd15682
00D1565E 8b8d0cffffff       mov ecx, dword ptr [ebp - 0xf4]
00D15664 8d95dcfeffff       lea edx, [ebp - 0x124]
00D1566A 898de0feffff       mov dword ptr [ebp - 0x120], ecx
00D15670 8b8de8feffff       mov ecx, dword ptr [ebp - 0x118]
00D15676 52                 push edx
00D15677 89b5dcfeffff       mov dword ptr [ebp - 0x124], esi
00D1567D e81e6933ff         call 0x4bfa0
00D15682 8bbdfcfeffff       mov edi, dword ptr [ebp - 0x104]
00D15688 8b8500ffffff       mov eax, dword ptr [ebp - 0x100]
00D1568E 0fb78d04ffffff     movzx ecx, word ptr [ebp - 0xfc]
00D15695 40                 inc eax
00D15696 898500ffffff       mov dword ptr [ebp - 0x100], eax
00D1569C 3bc1               cmp eax, ecx
00D1569E 0f8c9efcffff       jl 0xd15342
00D156A4 8b4df4             mov ecx, dword ptr [ebp - 0xc]
00D156A7 64890d00000000     mov dword ptr fs:[0], ecx
00D156AE 59                 pop ecx
00D156AF 5f                 pop edi
00D156B0 5e                 pop esi
00D156B1 5b                 pop ebx
00D156B2 8b4df0             mov ecx, dword ptr [ebp - 0x10]
00D156B5 33cd               xor ecx, ebp
00D156B7 e837d1b401         call 0x28627f3
00D156BC 8be5               mov esp, ebp
00D156BE 5d                 pop ebp
00D156BF c3                 ret
```

## Batch response

```text
00D170C0 55                 push ebp
00D170C1 8bec               mov ebp, esp
00D170C3 6aff               push -1
00D170C5 683124d702         push 0x2d72431
00D170CA 64a100000000       mov eax, dword ptr fs:[0]
00D170D0 50                 push eax
00D170D1 81ecb0060000       sub esp, 0x6b0
00D170D7 a1e0ce9903         mov eax, dword ptr [0x399cee0]
00D170DC 33c5               xor eax, ebp
00D170DE 8945f0             mov dword ptr [ebp - 0x10], eax
00D170E1 53                 push ebx
00D170E2 56                 push esi
00D170E3 57                 push edi
00D170E4 50                 push eax
00D170E5 8d45f4             lea eax, [ebp - 0xc]
00D170E8 64a300000000       mov dword ptr fs:[0], eax
00D170EE 33db               xor ebx, ebx
00D170F0 385d0c             cmp byte ptr [ebp + 0xc], bl
00D170F3 0f846c0e0000       je 0xd17f65
00D170F9 8d85edf9ffff       lea eax, [ebp - 0x613]
00D170FF 6a01               push 1
00D17101 50                 push eax
00D17102 889dedf9ffff       mov byte ptr [ebp - 0x613], bl
00D17108 889deef9ffff       mov byte ptr [ebp - 0x612], bl
00D1710E 899da8f9ffff       mov dword ptr [ebp - 0x658], ebx
00D17114 e817e16a01         call 0x23c5230
00D17119 8d8deef9ffff       lea ecx, [ebp - 0x612]
00D1711F 6a01               push 1
00D17121 51                 push ecx
00D17122 e809e16a01         call 0x23c5230
00D17127 8d95a8f9ffff       lea edx, [ebp - 0x658]
00D1712D 52                 push edx
00D1712E e86de16a01         call 0x23c52a0
00D17133 8d85ccf9ffff       lea eax, [ebp - 0x634]
00D17139 50                 push eax
00D1713A 889deff9ffff       mov byte ptr [ebp - 0x611], bl
00D17140 899dccf9ffff       mov dword ptr [ebp - 0x634], ebx
00D17146 899dd0f9ffff       mov dword ptr [ebp - 0x630], ebx
00D1714C e84fe16a01         call 0x23c52a0
00D17151 8d8dd0f9ffff       lea ecx, [ebp - 0x630]
00D17157 51                 push ecx
00D17158 e843e16a01         call 0x23c52a0
00D1715D 0fb795ccf9ffff     movzx edx, word ptr [ebp - 0x634]
00D17164 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D1716A 83c41c             add esp, 0x1c
00D1716D 52                 push edx
00D1716E e84d755d00         call 0x12ee6c0
00D17173 8bf0               mov esi, eax
00D17175 3bf3               cmp esi, ebx
00D17177 0f849b0d0000       je 0xd17f18
00D1717D 8b06               mov eax, dword ptr [esi]
00D1717F 8b5028             mov edx, dword ptr [eax + 0x28]
00D17182 8bce               mov ecx, esi
00D17184 ffd2               call edx
00D17186 6a01               push 1
00D17188 6a32               push 0x32
00D1718A 8d7818             lea edi, [eax + 0x18]
00D1718D 6a04               push 4
00D1718F 57                 push edi
00D17190 e87b5fb301         call 0x284d110
00D17195 8b3f               mov edi, dword ptr [edi]
00D17197 8b0d14c9a503       mov ecx, dword ptr [0x3a5c914]
00D1719D 83c410             add esp, 0x10
00D171A0 6a01               push 1
00D171A2 57                 push edi
00D171A3 e8d8b86301         call 0x2352a80
00D171A8 8bf8               mov edi, eax
00D171AA 8b06               mov eax, dword ptr [esi]
00D171AC 8b5028             mov edx, dword ptr [eax + 0x28]
00D171AF 8bce               mov ecx, esi
00D171B1 ffd2               call edx
00D171B3 6a01               push 1
00D171B5 6a32               push 0x32
00D171B7 8d7018             lea esi, [eax + 0x18]
00D171BA 6a04               push 4
00D171BC 56                 push esi
00D171BD e84e5fb301         call 0x284d110
00D171C2 8b36               mov esi, dword ptr [esi]
00D171C4 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D171CA 83c410             add esp, 0x10
00D171CD 56                 push esi
00D171CE e83d825d00         call 0x12ef410
00D171D3 0fb7b5a8f9ffff     movzx esi, word ptr [ebp - 0x658]
00D171DA 0fafb708010000     imul esi, dword ptr [edi + 0x108]
00D171E1 3bf0               cmp esi, eax
00D171E3 7515               jne 0xd171fa
00D171E5 0fb785ccf9ffff     movzx eax, word ptr [ebp - 0x634]
00D171EC 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D171F2 50                 push eax
00D171F3 e808a15d00         call 0x12f1300
00D171F8 eb5d               jmp 0xd17257
00D171FA 7d23               jge 0xd1721f
00D171FC 0fb78dccf9ffff     movzx ecx, word ptr [ebp - 0x634]
00D17203 51                 push ecx
00D17204 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D1720A e8b1745d00         call 0x12ee6c0
00D1720F 3bc3               cmp eax, ebx
00D17211 7444               je 0xd17257
00D17213 8b10               mov edx, dword ptr [eax]
00D17215 8bc8               mov ecx, eax
00D17217 8b4250             mov eax, dword ptr [edx + 0x50]
00D1721A 56                 push esi
00D1721B ffd0               call eax
00D1721D eb38               jmp 0xd17257
00D1721F 68582d2403         push 0x3242d58
00D17224 e8973a6a01         call 0x23bacc0
00D17229 83c404             add esp, 4
00D1722C 50                 push eax
00D1722D 6883270000         push 0x2783
00D17232 68e82c2403         push 0x3242ce8
00D17237 e8843a6a01         call 0x23bacc0
00D1723C 83c404             add esp, 4
00D1723F 50                 push eax
00D17240 6890122403         push 0x3241290
00D17245 e8763a6a01         call 0x23bacc0
00D1724A 83c404             add esp, 4
00D1724D 50                 push eax
00D1724E 53                 push ebx
00D1724F e81c706a01         call 0x23be270
00D17254 83c414             add esp, 0x14
00D17257 80bdedf9ffff04     cmp byte ptr [ebp - 0x613], 4
00D1725E 0f84ef000000       je 0xd17353
00D17264 0fb78dd0f9ffff     movzx ecx, word ptr [ebp - 0x630]
00D1726B 51                 push ecx
00D1726C 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D17272 e849745d00         call 0x12ee6c0
00D17277 8bf0               mov esi, eax
00D17279 8b16               mov edx, dword ptr [esi]
00D1727B 8b4228             mov eax, dword ptr [edx + 0x28]
00D1727E 8bce               mov ecx, esi
00D17280 ffd0               call eax
00D17282 6a01               push 1
00D17284 6a32               push 0x32
00D17286 8d7818             lea edi, [eax + 0x18]
00D17289 6a04               push 4
00D1728B 57                 push edi
00D1728C e87f5eb301         call 0x284d110
00D17291 8b3f               mov edi, dword ptr [edi]
00D17293 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D17299 83c410             add esp, 0x10
00D1729C 57                 push edi
00D1729D e86e815d00         call 0x12ef410
00D172A2 8b16               mov edx, dword ptr [esi]
00D172A4 8bf8               mov edi, eax
00D172A6 8b4228             mov eax, dword ptr [edx + 0x28]
00D172A9 8bce               mov ecx, esi
00D172AB ffd0               call eax
00D172AD 6a01               push 1
00D172AF 6a32               push 0x32
00D172B1 8d7018             lea esi, [eax + 0x18]
00D172B4 6a04               push 4
00D172B6 56                 push esi
00D172B7 e8545eb301         call 0x284d110
00D172BC 8b36               mov esi, dword ptr [esi]
00D172BE 8b0d14c9a503       mov ecx, dword ptr [0x3a5c914]
00D172C4 83c410             add esp, 0x10
00D172C7 6a01               push 1
00D172C9 56                 push esi
00D172CA e8b1b76301         call 0x2352a80
00D172CF 0fb7b5a8f9ffff     movzx esi, word ptr [ebp - 0x658]
00D172D6 0fafb008010000     imul esi, dword ptr [eax + 0x108]
00D172DD 3bf7               cmp esi, edi
00D172DF 7515               jne 0xd172f6
00D172E1 0fb78dd0f9ffff     movzx ecx, word ptr [ebp - 0x630]
00D172E8 51                 push ecx
00D172E9 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D172EF e80ca05d00         call 0x12f1300
00D172F4 eb5d               jmp 0xd17353
00D172F6 7d23               jge 0xd1731b
00D172F8 0fb795d0f9ffff     movzx edx, word ptr [ebp - 0x630]
00D172FF 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D17305 52                 push edx
00D17306 e8b5735d00         call 0x12ee6c0
00D1730B 3bc3               cmp eax, ebx
00D1730D 7444               je 0xd17353
00D1730F 8b10               mov edx, dword ptr [eax]
00D17311 8bc8               mov ecx, eax
00D17313 8b4250             mov eax, dword ptr [edx + 0x50]
00D17316 56                 push esi
00D17317 ffd0               call eax
00D17319 eb38               jmp 0xd17353
00D1731B 68a02c2403         push 0x3242ca0
00D17320 e89b396a01         call 0x23bacc0
00D17325 83c404             add esp, 4
00D17328 50                 push eax
00D17329 6899270000         push 0x2799
00D1732E 68e82c2403         push 0x3242ce8
00D17333 e888396a01         call 0x23bacc0
00D17338 83c404             add esp, 4
00D1733B 50                 push eax
00D1733C 6890122403         push 0x3241290
00D17341 e87a396a01         call 0x23bacc0
00D17346 83c404             add esp, 4
00D17349 50                 push eax
00D1734A 53                 push ebx
00D1734B e8206f6a01         call 0x23be270
00D17350 83c414             add esp, 0x14
00D17353 6a08               push 8
00D17355 899d7cf9ffff       mov dword ptr [ebp - 0x684], ebx
00D1735B e800c130ff         call 0x23460
00D17360 83c404             add esp, 4
00D17363 3bc3               cmp eax, ebx
00D17365 0f847a0b0000       je 0xd17ee5
00D1736B 89857cf9ffff       mov dword ptr [ebp - 0x684], eax
00D17371 33c9               xor ecx, ecx
00D17373 8908               mov dword ptr [eax], ecx
00D17375 33d2               xor edx, edx
00D17377 895004             mov dword ptr [eax + 4], edx
00D1737A 8b957cf9ffff       mov edx, dword ptr [ebp - 0x684]
00D17380 8d8d7cf9ffff       lea ecx, [ebp - 0x684]
00D17386 890a               mov dword ptr [edx], ecx
00D17388 899d80f9ffff       mov dword ptr [ebp - 0x680], ebx
00D1738E 899d84f9ffff       mov dword ptr [ebp - 0x67c], ebx
00D17394 899d88f9ffff       mov dword ptr [ebp - 0x678], ebx
00D1739A 8d85eff9ffff       lea eax, [ebp - 0x611]
00D173A0 50                 push eax
00D173A1 51                 push ecx
00D173A2 6a01               push 1
00D173A4 895dfc             mov dword ptr [ebp - 4], ebx
00D173A7 c685ecf9ffff00     mov byte ptr [ebp - 0x614], 0
00D173AE e80ddfffff         call 0xd152c0
00D173B3 8a95eff9ffff       mov dl, byte ptr [ebp - 0x611]
00D173B9 6a08               push 8
00D173BB 8895eff9ffff       mov byte ptr [ebp - 0x611], dl
00D173C1 899dd8f9ffff       mov dword ptr [ebp - 0x628], ebx
00D173C7 e894c030ff         call 0x23460
00D173CC 83c410             add esp, 0x10
00D173CF 3bc3               cmp eax, ebx
00D173D1 0f84db0a0000       je 0xd17eb2
00D173D7 8985d8f9ffff       mov dword ptr [ebp - 0x628], eax
00D173DD 33c9               xor ecx, ecx
00D173DF 8908               mov dword ptr [eax], ecx
00D173E1 33d2               xor edx, edx
00D173E3 895004             mov dword ptr [eax + 4], edx
00D173E6 8b8dd8f9ffff       mov ecx, dword ptr [ebp - 0x628]
00D173EC 8d85d8f9ffff       lea eax, [ebp - 0x628]
00D173F2 8901               mov dword ptr [ecx], eax
00D173F4 899ddcf9ffff       mov dword ptr [ebp - 0x624], ebx
00D173FA 899de0f9ffff       mov dword ptr [ebp - 0x620], ebx
00D17400 899de4f9ffff       mov dword ptr [ebp - 0x61c], ebx
00D17406 8d95c0f9ffff       lea edx, [ebp - 0x640]
00D1740C 52                 push edx
00D1740D 8d8d7cf9ffff       lea ecx, [ebp - 0x684]
00D17413 c645fc01           mov byte ptr [ebp - 4], 1
00D17417 e854408201         call 0x253b470
00D1741C 8b08               mov ecx, dword ptr [eax]
00D1741E 33ff               xor edi, edi
00D17420 89bd70f9ffff       mov dword ptr [ebp - 0x690], edi
00D17426 3bcb               cmp ecx, ebx
00D17428 740e               je 0xd17438
00D1742A 8b09               mov ecx, dword ptr [ecx]
00D1742C 3bcb               cmp ecx, ebx
00D1742E 7408               je 0xd17438
00D17430 8b39               mov edi, dword ptr [ecx]
00D17432 89bd70f9ffff       mov dword ptr [ebp - 0x690], edi
00D17438 8b4008             mov eax, dword ptr [eax + 8]
00D1743B 8d8dc0f9ffff       lea ecx, [ebp - 0x640]
00D17441 51                 push ecx
00D17442 8d8d7cf9ffff       lea ecx, [ebp - 0x684]
00D17448 898578f9ffff       mov dword ptr [ebp - 0x688], eax
00D1744E e8adecd3ff         call 0xa56100
00D17453 8b08               mov ecx, dword ptr [eax]
00D17455 33f6               xor esi, esi
00D17457 89b59cf9ffff       mov dword ptr [ebp - 0x664], esi
00D1745D 3bcb               cmp ecx, ebx
00D1745F 740e               je 0xd1746f
00D17461 8b09               mov ecx, dword ptr [ecx]
00D17463 3bcb               cmp ecx, ebx
00D17465 7408               je 0xd1746f
00D17467 8b31               mov esi, dword ptr [ecx]
00D17469 89b59cf9ffff       mov dword ptr [ebp - 0x664], esi
00D1746F 8b5008             mov edx, dword ptr [eax + 8]
00D17472 8995a4f9ffff       mov dword ptr [ebp - 0x65c], edx
00D17478 3bf3               cmp esi, ebx
00D1747A 0f8493020000       je 0xd17713
00D17480 8b0e               mov ecx, dword ptr [esi]
00D17482 3bcb               cmp ecx, ebx
00D17484 0f8489020000       je 0xd17713
00D1748A 3bfb               cmp edi, ebx
00D1748C 7504               jne 0xd17492
00D1748E 33c0               xor eax, eax
00D17490 eb02               jmp 0xd17494
00D17492 8b07               mov eax, dword ptr [edi]
00D17494 3bc8               cmp ecx, eax
00D17496 0f8577020000       jne 0xd17713
00D1749C 3b9578f9ffff       cmp edx, dword ptr [ebp - 0x688]
00D174A2 0f94c0             sete al
00D174A5 84c0               test al, al
00D174A7 0f94c0             sete al
00D174AA 84c0               test al, al
00D174AC 0f8428020000       je 0xd176da
00D174B2 3bcb               cmp ecx, ebx
00D174B4 0f8459020000       je 0xd17713
00D174BA 3bd3               cmp edx, ebx
00D174BC 0f8451020000       je 0xd17713
00D174C2 395104             cmp dword ptr [ecx + 4], edx
00D174C5 0f8748020000       ja 0xd17713
00D174CB 3b5108             cmp edx, dword ptr [ecx + 8]
00D174CE 0f833f020000       jae 0xd17713
00D174D4 8b32               mov esi, dword ptr [edx]
00D174D6 8b7a04             mov edi, dword ptr [edx + 4]
00D174D9 89b568f9ffff       mov dword ptr [ebp - 0x698], esi
00D174DF 89bd6cf9ffff       mov dword ptr [ebp - 0x694], edi
00D174E5 3bf3               cmp esi, ebx
00D174E7 0f849c010000       je 0xd17689
00D174ED 8b16               mov edx, dword ptr [esi]
00D174EF 8b4228             mov eax, dword ptr [edx + 0x28]
00D174F2 8bce               mov ecx, esi
00D174F4 ffd0               call eax
00D174F6 85c0               test eax, eax
00D174F8 0f848b010000       je 0xd17689
00D174FE 8b16               mov edx, dword ptr [esi]
00D17500 8b422c             mov eax, dword ptr [edx + 0x2c]
00D17503 6a01               push 1
00D17505 8bce               mov ecx, esi
00D17507 ffd0               call eax
00D17509 84c0               test al, al
00D1750B 0f8478010000       je 0xd17689
00D17511 8b16               mov edx, dword ptr [esi]
00D17513 8b4228             mov eax, dword ptr [edx + 0x28]
00D17516 8bce               mov ecx, esi
00D17518 ffd0               call eax
00D1751A 6a01               push 1
00D1751C 6a32               push 0x32
00D1751E 8d7018             lea esi, [eax + 0x18]
00D17521 6a04               push 4
00D17523 56                 push esi
00D17524 e8e75bb301         call 0x284d110
00D17529 8b0e               mov ecx, dword ptr [esi]
00D1752B 83c410             add esp, 0x10
00D1752E 8d95c0f9ffff       lea edx, [ebp - 0x640]
00D17534 898dd4f9ffff       mov dword ptr [ebp - 0x62c], ecx
00D1753A 52                 push edx
00D1753B 8d8dd8f9ffff       lea ecx, [ebp - 0x628]
00D17541 e8baebd3ff         call 0xa56100
00D17546 8b9dc0f9ffff       mov ebx, dword ptr [ebp - 0x640]
00D1754C 8bbdc8f9ffff       mov edi, dword ptr [ebp - 0x638]
00D17552 8d855cf9ffff       lea eax, [ebp - 0x6a4]
00D17558 50                 push eax
00D17559 8d8dd8f9ffff       lea ecx, [ebp - 0x628]
00D1755F e80c3f8201         call 0x253b470
00D17564 85db               test ebx, ebx
00D17566 0f84a7010000       je 0xd17713
00D1756C 8b13               mov edx, dword ptr [ebx]
00D1756E 85d2               test edx, edx
00D17570 0f849d010000       je 0xd17713
00D17576 8b08               mov ecx, dword ptr [eax]
00D17578 85c9               test ecx, ecx
00D1757A 7402               je 0xd1757e
00D1757C 8b09               mov ecx, dword ptr [ecx]
00D1757E 3bd1               cmp edx, ecx
00D17580 0f858d010000       jne 0xd17713
00D17586 3b7808             cmp edi, dword ptr [eax + 8]
00D17589 0f84d9000000       je 0xd17668
00D1758F 8bc2               mov eax, edx
00D17591 85c0               test eax, eax
00D17593 0f847a010000       je 0xd17713
00D17599 85ff               test edi, edi
00D1759B 0f8472010000       je 0xd17713
00D175A1 397804             cmp dword ptr [eax + 4], edi
00D175A4 0f8769010000       ja 0xd17713
00D175AA 3b7808             cmp edi, dword ptr [eax + 8]
00D175AD 0f8360010000       jae 0xd17713
00D175B3 833f00             cmp dword ptr [edi], 0
00D175B6 7468               je 0xd17620
00D175B8 85c0               test eax, eax
00D175BA 0f8453010000       je 0xd17713
00D175C0 397804             cmp dword ptr [eax + 4], edi
00D175C3 0f874a010000       ja 0xd17713
00D175C9 3b7808             cmp edi, dword ptr [eax + 8]
00D175CC 0f8341010000       jae 0xd17713
00D175D2 8b0f               mov ecx, dword ptr [edi]
00D175D4 8b11               mov edx, dword ptr [ecx]
00D175D6 8b4228             mov eax, dword ptr [edx + 0x28]
00D175D9 ffd0               call eax
00D175DB 85c0               test eax, eax
00D175DD 7441               je 0xd17620
00D175DF 8b03               mov eax, dword ptr [ebx]
00D175E1 85c0               test eax, eax
00D175E3 0f842a010000       je 0xd17713
00D175E9 397804             cmp dword ptr [eax + 4], edi
00D175EC 0f8721010000       ja 0xd17713
00D175F2 3b7808             cmp edi, dword ptr [eax + 8]
00D175F5 0f8318010000       jae 0xd17713
00D175FB 8b0f               mov ecx, dword ptr [edi]
00D175FD 8b11               mov edx, dword ptr [ecx]
00D175FF 8b4228             mov eax, dword ptr [edx + 0x28]
00D17602 ffd0               call eax
00D17604 6a01               push 1
00D17606 6a32               push 0x32
00D17608 8d7018             lea esi, [eax + 0x18]
00D1760B 6a04               push 4
00D1760D 56                 push esi
00D1760E e8fd5ab301         call 0x284d110
00D17613 8b8dd4f9ffff       mov ecx, dword ptr [ebp - 0x62c]
00D17619 83c410             add esp, 0x10
00D1761C 390e               cmp dword ptr [esi], ecx
00D1761E 7421               je 0xd17641
00D17620 8b03               mov eax, dword ptr [ebx]
00D17622 85c0               test eax, eax
00D17624 0f84e9000000       je 0xd17713
00D1762A 3b7808             cmp edi, dword ptr [eax + 8]
00D1762D 0f83e0000000       jae 0xd17713
00D17633 83c708             add edi, 8
00D17636 89bdc8f9ffff       mov dword ptr [ebp - 0x638], edi
00D1763C e911ffffff         jmp 0xd17552
00D17641 8b03               mov eax, dword ptr [ebx]
00D17643 85c0               test eax, eax
00D17645 0f84c8000000       je 0xd17713
00D1764B 397804             cmp dword ptr [eax + 4], edi
00D1764E 0f87bf000000       ja 0xd17713
00D17654 3b7808             cmp edi, dword ptr [eax + 8]
00D17657 0f83b6000000       jae 0xd17713
00D1765D 8b956cf9ffff       mov edx, dword ptr [ebp - 0x694]
00D17663 015704             add dword ptr [edi + 4], edx
00D17666 eb3f               jmp 0xd176a7
00D17668 8b8568f9ffff       mov eax, dword ptr [ebp - 0x698]
00D1766E 8b8d6cf9ffff       mov ecx, dword ptr [ebp - 0x694]
00D17674 8d954cf9ffff       lea edx, [ebp - 0x6b4]
00D1767A 89854cf9ffff       mov dword ptr [ebp - 0x6b4], eax
00D17680 898d50f9ffff       mov dword ptr [ebp - 0x6b0], ecx
00D17686 52                 push edx
00D17687 eb13               jmp 0xd1769c
00D17689 8d8544f9ffff       lea eax, [ebp - 0x6bc]
00D1768F 89b544f9ffff       mov dword ptr [ebp - 0x6bc], esi
00D17695 89bd48f9ffff       mov dword ptr [ebp - 0x6b8], edi
00D1769B 50                 push eax
00D1769C 8d8dd8f9ffff       lea ecx, [ebp - 0x628]
00D176A2 e8f94833ff         call 0x4bfa0
00D176A7 8b8d9cf9ffff       mov ecx, dword ptr [ebp - 0x664]
00D176AD 8b01               mov eax, dword ptr [ecx]
00D176AF 85c0               test eax, eax
00D176B1 7460               je 0xd17713
00D176B3 8b95a4f9ffff       mov edx, dword ptr [ebp - 0x65c]
00D176B9 3b5008             cmp edx, dword ptr [eax + 8]
00D176BC 7355               jae 0xd17713
00D176BE 8385a4f9ffff08     add dword ptr [ebp - 0x65c], 8
00D176C5 8bbd70f9ffff       mov edi, dword ptr [ebp - 0x690]
00D176CB 8b95a4f9ffff       mov edx, dword ptr [ebp - 0x65c]
00D176D1 8bf1               mov esi, ecx
00D176D3 33db               xor ebx, ebx
00D176D5 e99efdffff         jmp 0xd17478
00D176DA 8d85c0f9ffff       lea eax, [ebp - 0x640]
00D176E0 50                 push eax
00D176E1 8d8dd8f9ffff       lea ecx, [ebp - 0x628]
00D176E7 e8843d8201         call 0x253b470
00D176EC 8d8d70f9ffff       lea ecx, [ebp - 0x690]
00D176F2 51                 push ecx
00D176F3 8d8dd8f9ffff       lea ecx, [ebp - 0x628]
00D176F9 e802ead3ff         call 0xa56100
00D176FE 8b85c0f9ffff       mov eax, dword ptr [ebp - 0x640]
00D17704 6820c61001         push 0x110c620
00D17709 3bc3               cmp eax, ebx
00D1770B 7406               je 0xd17713
00D1770D 8b08               mov ecx, dword ptr [eax]
00D1770F 3bcb               cmp ecx, ebx
00D17711 7505               jne 0xd17718
00D17713 e8c9acb401         call 0x28623e1
00D17718 8b8570f9ffff       mov eax, dword ptr [ebp - 0x690]
00D1771E 3bc3               cmp eax, ebx
00D17720 7504               jne 0xd17726
00D17722 33c0               xor eax, eax
00D17724 eb02               jmp 0xd17728
00D17726 8b00               mov eax, dword ptr [eax]
00D17728 3bc8               cmp ecx, eax
00D1772A 7405               je 0xd17731
00D1772C e8b0acb401         call 0x28623e1
00D17731 8b8dc8f9ffff       mov ecx, dword ptr [ebp - 0x638]
00D17737 8b9578f9ffff       mov edx, dword ptr [ebp - 0x688]
00D1773D 8bc1               mov eax, ecx
00D1773F 2bc2               sub eax, edx
00D17741 c1f803             sar eax, 3
00D17744 50                 push eax
00D17745 51                 push ecx
00D17746 52                 push edx
00D17747 e804d1ffff         call 0xd14850
00D1774C 8d9554f9ffff       lea edx, [ebp - 0x6ac]
00D17752 52                 push edx
00D17753 899d54f9ffff       mov dword ptr [ebp - 0x6ac], ebx
00D17759 e842db6a01         call 0x23c52a0
00D1775E 6a08               push 8
00D17760 899dacf9ffff       mov dword ptr [ebp - 0x654], ebx
00D17766 e8f5bc30ff         call 0x23460
00D1776B 83c418             add esp, 0x18
00D1776E 3bc3               cmp eax, ebx
00D17770 0f8409070000       je 0xd17e7f
00D17776 8985acf9ffff       mov dword ptr [ebp - 0x654], eax
00D1777C 33c9               xor ecx, ecx
00D1777E 8908               mov dword ptr [eax], ecx
00D17780 33d2               xor edx, edx
00D17782 895004             mov dword ptr [eax + 4], edx
00D17785 8b8dacf9ffff       mov ecx, dword ptr [ebp - 0x654]
00D1778B 33ff               xor edi, edi
00D1778D 8d85acf9ffff       lea eax, [ebp - 0x654]
00D17793 8901               mov dword ptr [ecx], eax
00D17795 89bdb0f9ffff       mov dword ptr [ebp - 0x650], edi
00D1779B 89bdb4f9ffff       mov dword ptr [ebp - 0x64c], edi
00D177A1 89bdb8f9ffff       mov dword ptr [ebp - 0x648], edi
00D177A7 8d95ecf9ffff       lea edx, [ebp - 0x614]
00D177AD 52                 push edx
00D177AE 50                 push eax
00D177AF 6a01               push 1
00D177B1 c645fc02           mov byte ptr [ebp - 4], 2
00D177B5 e806dbffff         call 0xd152c0
00D177BA 8a8decf9ffff       mov cl, byte ptr [ebp - 0x614]
00D177C0 088deff9ffff       or byte ptr [ebp - 0x611], cl
00D177C6 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D177CC 83c40c             add esp, 0xc
00D177CF 68f3000000         push 0xf3
00D177D4 e8e7981e01         call 0x1f010c0
00D177D9 8bf0               mov esi, eax
00D177DB 8d955cf9ffff       lea edx, [ebp - 0x6a4]
00D177E1 52                 push edx
00D177E2 8bce               mov ecx, esi
00D177E4 e8873c8201         call 0x253b470
00D177E9 8b08               mov ecx, dword ptr [eax]
00D177EB 33db               xor ebx, ebx
00D177ED 3bcf               cmp ecx, edi
00D177EF 7408               je 0xd177f9
00D177F1 8b09               mov ecx, dword ptr [ecx]
00D177F3 3bcf               cmp ecx, edi
00D177F5 7402               je 0xd177f9
00D177F7 8b19               mov ebx, dword ptr [ecx]
00D177F9 8b4008             mov eax, dword ptr [eax + 8]
00D177FC 8d8d5cf9ffff       lea ecx, [ebp - 0x6a4]
00D17802 51                 push ecx
00D17803 8bce               mov ecx, esi
00D17805 898598f9ffff       mov dword ptr [ebp - 0x668], eax
00D1780B e8f0e8d3ff         call 0xa56100
00D17810 8b08               mov ecx, dword ptr [eax]
00D17812 33ff               xor edi, edi
00D17814 85c9               test ecx, ecx
00D17816 7408               je 0xd17820
00D17818 8b09               mov ecx, dword ptr [ecx]
00D1781A 85c9               test ecx, ecx
00D1781C 7402               je 0xd17820
00D1781E 8b39               mov edi, dword ptr [ecx]
00D17820 8b7008             mov esi, dword ptr [eax + 8]
00D17823 85ff               test edi, edi
00D17825 0f84e8feffff       je 0xd17713
00D1782B 8b07               mov eax, dword ptr [edi]
00D1782D 85c0               test eax, eax
00D1782F 0f84defeffff       je 0xd17713
00D17835 85db               test ebx, ebx
00D17837 7504               jne 0xd1783d
00D17839 33c9               xor ecx, ecx
00D1783B eb02               jmp 0xd1783f
00D1783D 8b0b               mov ecx, dword ptr [ebx]
00D1783F 3bc1               cmp eax, ecx
00D17841 0f85ccfeffff       jne 0xd17713
00D17847 3bb598f9ffff       cmp esi, dword ptr [ebp - 0x668]
00D1784D 7447               je 0xd17896
00D1784F 85c0               test eax, eax
00D17851 0f84bcfeffff       je 0xd17713
00D17857 85f6               test esi, esi
00D17859 0f84b4feffff       je 0xd17713
00D1785F 397004             cmp dword ptr [eax + 4], esi
00D17862 0f87abfeffff       ja 0xd17713
00D17868 3b7008             cmp esi, dword ptr [eax + 8]
00D1786B 0f83a2feffff       jae 0xd17713
00D17871 8b0e               mov ecx, dword ptr [esi]
00D17873 85c9               test ecx, ecx
00D17875 7407               je 0xd1787e
00D17877 8b11               mov edx, dword ptr [ecx]
00D17879 8b4278             mov eax, dword ptr [edx + 0x78]
00D1787C ffd0               call eax
00D1787E 8b07               mov eax, dword ptr [edi]
00D17880 85c0               test eax, eax
00D17882 0f848bfeffff       je 0xd17713
00D17888 3b7008             cmp esi, dword ptr [eax + 8]
00D1788B 0f8382feffff       jae 0xd17713
00D17891 83c604             add esi, 4
00D17894 eb8d               jmp 0xd17823
00D17896 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D1789C 689c010000         push 0x19c
00D178A1 e81a981e01         call 0x1f010c0
00D178A6 8d8d5cf9ffff       lea ecx, [ebp - 0x6a4]
00D178AC 8bf0               mov esi, eax
00D178AE 51                 push ecx
00D178AF 8bce               mov ecx, esi
00D178B1 e8ba3b8201         call 0x253b470
00D178B6 8b08               mov ecx, dword ptr [eax]
00D178B8 33db               xor ebx, ebx
00D178BA 85c9               test ecx, ecx
00D178BC 7408               je 0xd178c6
00D178BE 8b09               mov ecx, dword ptr [ecx]
00D178C0 85c9               test ecx, ecx
00D178C2 7402               je 0xd178c6
00D178C4 8b19               mov ebx, dword ptr [ecx]
00D178C6 8b5008             mov edx, dword ptr [eax + 8]
00D178C9 8d855cf9ffff       lea eax, [ebp - 0x6a4]
00D178CF 50                 push eax
00D178D0 8bce               mov ecx, esi
00D178D2 899598f9ffff       mov dword ptr [ebp - 0x668], edx
00D178D8 e823e8d3ff         call 0xa56100
00D178DD 8b08               mov ecx, dword ptr [eax]
00D178DF 33ff               xor edi, edi
00D178E1 85c9               test ecx, ecx
00D178E3 7408               je 0xd178ed
00D178E5 8b09               mov ecx, dword ptr [ecx]
00D178E7 85c9               test ecx, ecx
00D178E9 7402               je 0xd178ed
00D178EB 8b39               mov edi, dword ptr [ecx]
00D178ED 8b7008             mov esi, dword ptr [eax + 8]
00D178F0 85ff               test edi, edi
00D178F2 0f841bfeffff       je 0xd17713
00D178F8 8b07               mov eax, dword ptr [edi]
00D178FA 85c0               test eax, eax
00D178FC 0f8411feffff       je 0xd17713
00D17902 85db               test ebx, ebx
00D17904 7504               jne 0xd1790a
00D17906 33c9               xor ecx, ecx
00D17908 eb02               jmp 0xd1790c
00D1790A 8b0b               mov ecx, dword ptr [ebx]
00D1790C 3bc1               cmp eax, ecx
00D1790E 0f85fffdffff       jne 0xd17713
00D17914 3bb598f9ffff       cmp esi, dword ptr [ebp - 0x668]
00D1791A 7447               je 0xd17963
00D1791C 85c0               test eax, eax
00D1791E 0f84effdffff       je 0xd17713
00D17924 85f6               test esi, esi
00D17926 0f84e7fdffff       je 0xd17713
00D1792C 397004             cmp dword ptr [eax + 4], esi
00D1792F 0f87defdffff       ja 0xd17713
00D17935 3b7008             cmp esi, dword ptr [eax + 8]
00D17938 0f83d5fdffff       jae 0xd17713
00D1793E 8b0e               mov ecx, dword ptr [esi]
00D17940 85c9               test ecx, ecx
00D17942 7407               je 0xd1794b
00D17944 8b11               mov edx, dword ptr [ecx]
00D17946 8b4278             mov eax, dword ptr [edx + 0x78]
00D17949 ffd0               call eax
00D1794B 8b07               mov eax, dword ptr [edi]
00D1794D 85c0               test eax, eax
00D1794F 0f84befdffff       je 0xd17713
00D17955 3b7008             cmp esi, dword ptr [eax + 8]
00D17958 0f83b5fdffff       jae 0xd17713
00D1795E 83c604             add esi, 4
00D17961 eb8d               jmp 0xd178f0
00D17963 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D17969 68df020000         push 0x2df
00D1796E e84d971e01         call 0x1f010c0
00D17973 8d8d5cf9ffff       lea ecx, [ebp - 0x6a4]
00D17979 8bf0               mov esi, eax
00D1797B 51                 push ecx
00D1797C 8bce               mov ecx, esi
00D1797E e8ed3a8201         call 0x253b470
00D17983 8b08               mov ecx, dword ptr [eax]
00D17985 33db               xor ebx, ebx
00D17987 85c9               test ecx, ecx
00D17989 7408               je 0xd17993
00D1798B 8b09               mov ecx, dword ptr [ecx]
00D1798D 85c9               test ecx, ecx
00D1798F 7402               je 0xd17993
00D17991 8b19               mov ebx, dword ptr [ecx]
00D17993 8b5008             mov edx, dword ptr [eax + 8]
00D17996 8d855cf9ffff       lea eax, [ebp - 0x6a4]
00D1799C 50                 push eax
00D1799D 8bce               mov ecx, esi
00D1799F 899598f9ffff       mov dword ptr [ebp - 0x668], edx
00D179A5 e856e7d3ff         call 0xa56100
00D179AA 8b08               mov ecx, dword ptr [eax]
00D179AC 33ff               xor edi, edi
00D179AE 85c9               test ecx, ecx
00D179B0 7408               je 0xd179ba
00D179B2 8b09               mov ecx, dword ptr [ecx]
00D179B4 85c9               test ecx, ecx
00D179B6 7402               je 0xd179ba
00D179B8 8b39               mov edi, dword ptr [ecx]
00D179BA 8b7008             mov esi, dword ptr [eax + 8]
00D179BD 8d4900             lea ecx, [ecx]
00D179C0 85ff               test edi, edi
00D179C2 0f844bfdffff       je 0xd17713
00D179C8 8b07               mov eax, dword ptr [edi]
00D179CA 85c0               test eax, eax
00D179CC 0f8441fdffff       je 0xd17713
00D179D2 85db               test ebx, ebx
00D179D4 7504               jne 0xd179da
00D179D6 33c9               xor ecx, ecx
00D179D8 eb02               jmp 0xd179dc
00D179DA 8b0b               mov ecx, dword ptr [ebx]
00D179DC 3bc1               cmp eax, ecx
00D179DE 0f852ffdffff       jne 0xd17713
00D179E4 3bb598f9ffff       cmp esi, dword ptr [ebp - 0x668]
00D179EA 7447               je 0xd17a33
00D179EC 85c0               test eax, eax
00D179EE 0f841ffdffff       je 0xd17713
00D179F4 85f6               test esi, esi
00D179F6 0f8417fdffff       je 0xd17713
00D179FC 397004             cmp dword ptr [eax + 4], esi
00D179FF 0f870efdffff       ja 0xd17713
00D17A05 3b7008             cmp esi, dword ptr [eax + 8]
00D17A08 0f8305fdffff       jae 0xd17713
00D17A0E 8b0e               mov ecx, dword ptr [esi]
00D17A10 85c9               test ecx, ecx
00D17A12 7407               je 0xd17a1b
00D17A14 8b11               mov edx, dword ptr [ecx]
00D17A16 8b4278             mov eax, dword ptr [edx + 0x78]
00D17A19 ffd0               call eax
00D17A1B 8b07               mov eax, dword ptr [edi]
00D17A1D 85c0               test eax, eax
00D17A1F 0f84eefcffff       je 0xd17713
00D17A25 3b7008             cmp esi, dword ptr [eax + 8]
00D17A28 0f83e5fcffff       jae 0xd17713
00D17A2E 83c604             add esi, 4
00D17A31 eb8d               jmp 0xd179c0
00D17A33 80bdedf9ffff04     cmp byte ptr [ebp - 0x613], 4
00D17A3A 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D17A40 0f84b0010000       je 0xd17bf6
00D17A46 80bdeef9ffff00     cmp byte ptr [ebp - 0x612], 0
00D17A4D 0f84e1000000       je 0xd17b34
00D17A53 6a00               push 0
00D17A55 6a00               push 0
00D17A57 689c010000         push 0x19c
00D17A5C e80fd21e01         call 0x1f04c70
00D17A61 6a00               push 0
00D17A63 6824b09703         push 0x397b024
00D17A68 68a0e79503         push 0x395e7a0
00D17A6D 8bf8               mov edi, eax
00D17A6F 6a00               push 0
00D17A71 57                 push edi
00D17A72 e8f3bdb401         call 0x286386a
00D17A77 83c414             add esp, 0x14
00D17A7A 85c0               test eax, eax
00D17A7C 7442               je 0xd17ac0
00D17A7E 6a01               push 1
00D17A80 6a02               push 2
00D17A82 6a00               push 0
00D17A84 6824b09703         push 0x397b024
00D17A89 68a0e79503         push 0x395e7a0
00D17A8E 6a00               push 0
00D17A90 57                 push edi
00D17A91 e8d4bdb401         call 0x286386a
00D17A96 83c414             add esp, 0x14
00D17A99 8bc8               mov ecx, eax
00D17A9B e850d92800         call 0xfa53f0
00D17AA0 6a01               push 1
00D17AA2 6a00               push 0
00D17AA4 6824b09703         push 0x397b024
00D17AA9 68a0e79503         push 0x395e7a0
00D17AAE 6a00               push 0
00D17AB0 57                 push edi
00D17AB1 e8b4bdb401         call 0x286386a
00D17AB6 83c414             add esp, 0x14
00D17AB9 8bc8               mov ecx, eax
00D17ABB e810dc2800         call 0xfa56d0
00D17AC0 8b8db4f9ffff       mov ecx, dword ptr [ebp - 0x64c]
00D17AC6 8b85b0f9ffff       mov eax, dword ptr [ebp - 0x650]
00D17ACC 8bd9               mov ebx, ecx
00D17ACE 2bd8               sub ebx, eax
00D17AD0 c1fb03             sar ebx, 3
00D17AD3 85db               test ebx, ebx
00D17AD5 0f84c7020000       je 0xd17da2
00D17ADB 33f6               xor esi, esi
00D17ADD 85db               test ebx, ebx
00D17ADF 0f8ebd020000       jle 0xd17da2
00D17AE5 85ff               test edi, edi
00D17AE7 7441               je 0xd17b2a
00D17AE9 2bc8               sub ecx, eax
00D17AEB c1f903             sar ecx, 3
00D17AEE 3bf1               cmp esi, ecx
00D17AF0 0f831dfcffff       jae 0xd17713
00D17AF6 8b4cf004           mov ecx, dword ptr [eax + esi*8 + 4]
00D17AFA 8b14f0             mov edx, dword ptr [eax + esi*8]
00D17AFD 51                 push ecx
00D17AFE 52                 push edx
00D17AFF 56                 push esi
00D17B00 6a00               push 0
00D17B02 6824b09703         push 0x397b024
00D17B07 68a0e79503         push 0x395e7a0
00D17B0C 6a00               push 0
00D17B0E 57                 push edi
00D17B0F e856bdb401         call 0x286386a
00D17B14 83c414             add esp, 0x14
00D17B17 8bc8               mov ecx, eax
00D17B19 e872d62800         call 0xfa5190
00D17B1E 8b8db4f9ffff       mov ecx, dword ptr [ebp - 0x64c]
00D17B24 8b85b0f9ffff       mov eax, dword ptr [ebp - 0x650]
00D17B2A 46                 inc esi
00D17B2B 3bf3               cmp esi, ebx
00D17B2D 7cb6               jl 0xd17ae5
00D17B2F e96e020000         jmp 0xd17da2
00D17B34 68ffff0000         push 0xffff
00D17B39 6a00               push 0
00D17B3B 68f3000000         push 0xf3
00D17B40 e82bd11e01         call 0x1f04c70
00D17B45 6a00               push 0
00D17B47 68dca99603         push 0x396a9dc
00D17B4C 68a0e79503         push 0x395e7a0
00D17B51 8bf8               mov edi, eax
00D17B53 6a00               push 0
00D17B55 57                 push edi
00D17B56 e80fbdb401         call 0x286386a
00D17B5B 83c414             add esp, 0x14
00D17B5E 85c0               test eax, eax
00D17B60 7423               je 0xd17b85
00D17B62 68d7110100         push 0x111d7
00D17B67 6a00               push 0
00D17B69 68dca99603         push 0x396a9dc
00D17B6E 68a0e79503         push 0x395e7a0
00D17B73 6a00               push 0
00D17B75 57                 push edi
00D17B76 e8efbcb401         call 0x286386a
00D17B7B 83c414             add esp, 0x14
00D17B7E 8bc8               mov ecx, eax
00D17B80 e84b2f3501         call 0x206aad0
00D17B85 8b8de0f9ffff       mov ecx, dword ptr [ebp - 0x620]
00D17B8B 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17B91 8bd9               mov ebx, ecx
00D17B93 2bd8               sub ebx, eax
00D17B95 c1fb03             sar ebx, 3
00D17B98 33f6               xor esi, esi
00D17B9A 85db               test ebx, ebx
00D17B9C 0f8e00020000       jle 0xd17da2
00D17BA2 85ff               test edi, edi
00D17BA4 7446               je 0xd17bec
00D17BA6 2bc8               sub ecx, eax
00D17BA8 c1f903             sar ecx, 3
00D17BAB 3bf1               cmp esi, ecx
00D17BAD 0f8360fbffff       jae 0xd17713
00D17BB3 8b4cf004           mov ecx, dword ptr [eax + esi*8 + 4]
00D17BB7 8b14f0             mov edx, dword ptr [eax + esi*8]
00D17BBA 6a00               push 0
00D17BBC 6a00               push 0
00D17BBE 51                 push ecx
00D17BBF 6a01               push 1
00D17BC1 52                 push edx
00D17BC2 6a00               push 0
00D17BC4 68dca99603         push 0x396a9dc
00D17BC9 68a0e79503         push 0x395e7a0
00D17BCE 6a00               push 0
00D17BD0 57                 push edi
00D17BD1 e894bcb401         call 0x286386a
00D17BD6 83c414             add esp, 0x14
00D17BD9 8bc8               mov ecx, eax
00D17BDB e8705d3501         call 0x206d950
00D17BE0 8b8de0f9ffff       mov ecx, dword ptr [ebp - 0x620]
00D17BE6 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17BEC 46                 inc esi
00D17BED 3bf3               cmp esi, ebx
00D17BEF 7cb1               jl 0xd17ba2
00D17BF1 e9ac010000         jmp 0xd17da2
00D17BF6 80bdeef9ffff00     cmp byte ptr [ebp - 0x612], 0
00D17BFD 68ffff0000         push 0xffff
00D17C02 6a00               push 0
00D17C04 0f84e6000000       je 0xd17cf0
00D17C0A 68df020000         push 0x2df
00D17C0F e85cd01e01         call 0x1f04c70
00D17C14 8b8de0f9ffff       mov ecx, dword ptr [ebp - 0x620]
00D17C1A 8bd8               mov ebx, eax
00D17C1C 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17C22 8bf9               mov edi, ecx
00D17C24 2bf8               sub edi, eax
00D17C26 c1ff03             sar edi, 3
00D17C29 33f6               xor esi, esi
00D17C2B 85ff               test edi, edi
00D17C2D 7e4c               jle 0xd17c7b
00D17C2F 90                 nop
00D17C30 85db               test ebx, ebx
00D17C32 7442               je 0xd17c76
00D17C34 2bc8               sub ecx, eax
00D17C36 c1f903             sar ecx, 3
00D17C39 3bf1               cmp esi, ecx
00D17C3B 0f83d2faffff       jae 0xd17713
00D17C41 8b4cf004           mov ecx, dword ptr [eax + esi*8 + 4]
00D17C45 8b14f0             mov edx, dword ptr [eax + esi*8]
00D17C48 6a00               push 0
00D17C4A 51                 push ecx
00D17C4B 52                 push edx
00D17C4C 6a00               push 0
00D17C4E 68d0b39603         push 0x396b3d0
00D17C53 68a0e79503         push 0x395e7a0
00D17C58 6a00               push 0
00D17C5A 53                 push ebx
00D17C5B e80abcb401         call 0x286386a
00D17C60 83c414             add esp, 0x14
00D17C63 8bc8               mov ecx, eax
00D17C65 e8e6038aff         call 0x5b8050
00D17C6A 8b8de0f9ffff       mov ecx, dword ptr [ebp - 0x620]
00D17C70 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17C76 46                 inc esi
00D17C77 3bf7               cmp esi, edi
00D17C79 7cb5               jl 0xd17c30
00D17C7B 8b8db4f9ffff       mov ecx, dword ptr [ebp - 0x64c]
00D17C81 8b85b0f9ffff       mov eax, dword ptr [ebp - 0x650]
00D17C87 8bf9               mov edi, ecx
00D17C89 2bf8               sub edi, eax
00D17C8B c1ff03             sar edi, 3
00D17C8E 85ff               test edi, edi
00D17C90 0f840c010000       je 0xd17da2
00D17C96 33f6               xor esi, esi
00D17C98 85ff               test edi, edi
00D17C9A 0f8e02010000       jle 0xd17da2
00D17CA0 85db               test ebx, ebx
00D17CA2 7442               je 0xd17ce6
00D17CA4 2bc8               sub ecx, eax
00D17CA6 c1f903             sar ecx, 3
00D17CA9 3bf1               cmp esi, ecx
00D17CAB 0f8362faffff       jae 0xd17713
00D17CB1 8b4cf004           mov ecx, dword ptr [eax + esi*8 + 4]
00D17CB5 8b14f0             mov edx, dword ptr [eax + esi*8]
00D17CB8 6a01               push 1
00D17CBA 51                 push ecx
00D17CBB 52                 push edx
00D17CBC 6a00               push 0
00D17CBE 68d0b39603         push 0x396b3d0
00D17CC3 68a0e79503         push 0x395e7a0
00D17CC8 6a00               push 0
00D17CCA 53                 push ebx
00D17CCB e89abbb401         call 0x286386a
00D17CD0 83c414             add esp, 0x14
00D17CD3 8bc8               mov ecx, eax
00D17CD5 e876038aff         call 0x5b8050
00D17CDA 8b8db4f9ffff       mov ecx, dword ptr [ebp - 0x64c]
00D17CE0 8b85b0f9ffff       mov eax, dword ptr [ebp - 0x650]
00D17CE6 46                 inc esi
00D17CE7 3bf7               cmp esi, edi
00D17CE9 7cb5               jl 0xd17ca0
00D17CEB e9b2000000         jmp 0xd17da2
00D17CF0 68f3000000         push 0xf3
00D17CF5 e876cf1e01         call 0x1f04c70
00D17CFA 6a00               push 0
00D17CFC 68dca99603         push 0x396a9dc
00D17D01 68a0e79503         push 0x395e7a0
00D17D06 8bf8               mov edi, eax
00D17D08 6a00               push 0
00D17D0A 57                 push edi
00D17D0B e85abbb401         call 0x286386a
00D17D10 83c414             add esp, 0x14
00D17D13 85c0               test eax, eax
00D17D15 7423               je 0xd17d3a
00D17D17 68d7110100         push 0x111d7
00D17D1C 6a00               push 0
00D17D1E 68dca99603         push 0x396a9dc
00D17D23 68a0e79503         push 0x395e7a0
00D17D28 6a00               push 0
00D17D2A 57                 push edi
00D17D2B e83abbb401         call 0x286386a
00D17D30 83c414             add esp, 0x14
00D17D33 8bc8               mov ecx, eax
00D17D35 e8962d3501         call 0x206aad0
00D17D3A 8b8de0f9ffff       mov ecx, dword ptr [ebp - 0x620]
00D17D40 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17D46 8bd9               mov ebx, ecx
00D17D48 2bd8               sub ebx, eax
00D17D4A c1fb03             sar ebx, 3
00D17D4D 33f6               xor esi, esi
00D17D4F 85db               test ebx, ebx
00D17D51 7e4f               jle 0xd17da2
00D17D53 85ff               test edi, edi
00D17D55 7446               je 0xd17d9d
00D17D57 2bc8               sub ecx, eax
00D17D59 c1f903             sar ecx, 3
00D17D5C 3bf1               cmp esi, ecx
00D17D5E 0f83aff9ffff       jae 0xd17713
00D17D64 8b4cf004           mov ecx, dword ptr [eax + esi*8 + 4]
00D17D68 8b14f0             mov edx, dword ptr [eax + esi*8]
00D17D6B 6a00               push 0
00D17D6D 6a00               push 0
00D17D6F 51                 push ecx
00D17D70 6a01               push 1
00D17D72 52                 push edx
00D17D73 6a00               push 0
00D17D75 68dca99603         push 0x396a9dc
00D17D7A 68a0e79503         push 0x395e7a0
00D17D7F 6a00               push 0
00D17D81 57                 push edi
00D17D82 e8e3bab401         call 0x286386a
00D17D87 83c414             add esp, 0x14
00D17D8A 8bc8               mov ecx, eax
00D17D8C e8bf5b3501         call 0x206d950
00D17D91 8b8de0f9ffff       mov ecx, dword ptr [ebp - 0x620]
00D17D97 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17D9D 46                 inc esi
00D17D9E 3bf3               cmp esi, ebx
00D17DA0 7cb1               jl 0xd17d53
00D17DA2 6aff               push -1
00D17DA4 33f6               xor esi, esi
00D17DA6 80bdeff9ffff01     cmp byte ptr [ebp - 0x611], 1
00D17DAD 6aff               push -1
00D17DAF 56                 push esi
00D17DB0 56                 push esi
00D17DB1 6aff               push -1
00D17DB3 7507               jne 0xd17dbc
00D17DB5 680d120100         push 0x1120d
00D17DBA eb05               jmp 0xd17dc1
00D17DBC 680c120100         push 0x1120c
00D17DC1 e8fa967a01         call 0x24c14c0
00D17DC6 83c404             add esp, 4
00D17DC9 50                 push eax
00D17DCA e8510c5300         call 0x1248a20
00D17DCF 8b85b0f9ffff       mov eax, dword ptr [ebp - 0x650]
00D17DD5 83c418             add esp, 0x18
00D17DD8 3bc6               cmp eax, esi
00D17DDA 7409               je 0xd17de5
00D17DDC 50                 push eax
00D17DDD e89c79b401         call 0x285f77e
00D17DE2 83c404             add esp, 4
00D17DE5 8b85acf9ffff       mov eax, dword ptr [ebp - 0x654]
00D17DEB 50                 push eax
00D17DEC 89b5b0f9ffff       mov dword ptr [ebp - 0x650], esi
00D17DF2 89b5b4f9ffff       mov dword ptr [ebp - 0x64c], esi
00D17DF8 89b5b8f9ffff       mov dword ptr [ebp - 0x648], esi
00D17DFE e87b79b401         call 0x285f77e
00D17E03 8b85dcf9ffff       mov eax, dword ptr [ebp - 0x624]
00D17E09 83c404             add esp, 4
00D17E0C 89b5acf9ffff       mov dword ptr [ebp - 0x654], esi
00D17E12 3bc6               cmp eax, esi
00D17E14 7409               je 0xd17e1f
00D17E16 50                 push eax
00D17E17 e86279b401         call 0x285f77e
00D17E1C 83c404             add esp, 4
00D17E1F 8b8dd8f9ffff       mov ecx, dword ptr [ebp - 0x628]
00D17E25 51                 push ecx
00D17E26 89b5dcf9ffff       mov dword ptr [ebp - 0x624], esi
00D17E2C 89b5e0f9ffff       mov dword ptr [ebp - 0x620], esi
00D17E32 89b5e4f9ffff       mov dword ptr [ebp - 0x61c], esi
00D17E38 e84179b401         call 0x285f77e
00D17E3D 8b8580f9ffff       mov eax, dword ptr [ebp - 0x680]
00D17E43 83c404             add esp, 4
00D17E46 89b5d8f9ffff       mov dword ptr [ebp - 0x628], esi
00D17E4C 3bc6               cmp eax, esi
00D17E4E 7409               je 0xd17e59
00D17E50 50                 push eax
00D17E51 e82879b401         call 0x285f77e
00D17E56 83c404             add esp, 4
00D17E59 8b957cf9ffff       mov edx, dword ptr [ebp - 0x684]
00D17E5F 52                 push edx
00D17E60 89b580f9ffff       mov dword ptr [ebp - 0x680], esi
00D17E66 89b584f9ffff       mov dword ptr [ebp - 0x67c], esi
00D17E6C 89b588f9ffff       mov dword ptr [ebp - 0x678], esi
00D17E72 e80779b401         call 0x285f77e
00D17E77 83c404             add esp, 4
00D17E7A e92b020000         jmp 0xd180aa
00D17E7F 8d85d4f9ffff       lea eax, [ebp - 0x62c]
00D17E85 50                 push eax
00D17E86 8d8dc0f9ffff       lea ecx, [ebp - 0x640]
00D17E8C 899dd4f9ffff       mov dword ptr [ebp - 0x62c], ebx
00D17E92 e83698b401         call 0x28616cd
00D17E97 68988d7603         push 0x3768d98
00D17E9C 8d8dc0f9ffff       lea ecx, [ebp - 0x640]
00D17EA2 51                 push ecx
00D17EA3 c785c0f9ffff70380403 mov dword ptr [ebp - 0x640], 0x3043870
00D17EAD e822a3b401         call 0x28621d4
00D17EB2 8d9558f9ffff       lea edx, [ebp - 0x6a8]
00D17EB8 52                 push edx
00D17EB9 8d8d90f9ffff       lea ecx, [ebp - 0x670]
00D17EBF 899d58f9ffff       mov dword ptr [ebp - 0x6a8], ebx
00D17EC5 e80398b401         call 0x28616cd
00D17ECA 68988d7603         push 0x3768d98
00D17ECF 8d8590f9ffff       lea eax, [ebp - 0x670]
00D17ED5 50                 push eax
00D17ED6 c78590f9ffff70380403 mov dword ptr [ebp - 0x670], 0x3043870
00D17EE0 e8efa2b401         call 0x28621d4
00D17EE5 8d8dd4f9ffff       lea ecx, [ebp - 0x62c]
00D17EEB 51                 push ecx
00D17EEC 8d8dc0f9ffff       lea ecx, [ebp - 0x640]
00D17EF2 899dd4f9ffff       mov dword ptr [ebp - 0x62c], ebx
00D17EF8 e8d097b401         call 0x28616cd
00D17EFD 68988d7603         push 0x3768d98
00D17F02 8d95c0f9ffff       lea edx, [ebp - 0x640]
00D17F08 52                 push edx
00D17F09 c785c0f9ffff70380403 mov dword ptr [ebp - 0x640], 0x3043870
00D17F13 e8bca2b401         call 0x28621d4
00D17F18 68f8292403         push 0x32429f8
00D17F1D e89e2d6a01         call 0x23bacc0
00D17F22 83c404             add esp, 4
00D17F25 50                 push eax
00D17F26 6819280000         push 0x2819
00D17F2B 68e82c2403         push 0x3242ce8
00D17F30 e88b2d6a01         call 0x23bacc0
00D17F35 83c404             add esp, 4
00D17F38 50                 push eax
00D17F39 6890122403         push 0x3241290
00D17F3E e87d2d6a01         call 0x23bacc0
00D17F43 83c404             add esp, 4
00D17F46 50                 push eax
00D17F47 53                 push ebx
00D17F48 e823636a01         call 0x23be270
00D17F4D 83c414             add esp, 0x14
00D17F50 683a0c0000         push 0xc3a
00D17F55 e8d6acfeff         call 0xd02c30
00D17F5A 50                 push eax
00D17F5B 6858020000         push 0x258
00D17F60 e93a010000         jmp 0xd1809f
00D17F65 0fb64510           movzx eax, byte ptr [ebp + 0x10]
00D17F69 83e869             sub eax, 0x69
00D17F6C 0f84f1000000       je 0xd18063
00D17F72 83e815             sub eax, 0x15
00D17F75 746f               je 0xd17fe6
00D17F77 83e854             sub eax, 0x54
00D17F7A 7451               je 0xd17fcd
00D17F7C 68282c2403         push 0x3242c28
00D17F81 e83a2d6a01         call 0x23bacc0
00D17F86 83c404             add esp, 4
00D17F89 50                 push eax
00D17F8A 684c280000         push 0x284c
00D17F8F 68e82c2403         push 0x3242ce8
00D17F94 e8272d6a01         call 0x23bacc0
00D17F99 83c404             add esp, 4
00D17F9C 50                 push eax
00D17F9D 6890122403         push 0x3241290
00D17FA2 e8192d6a01         call 0x23bacc0
00D17FA7 83c404             add esp, 4
00D17FAA 50                 push eax
00D17FAB 53                 push ebx
00D17FAC e8bf626a01         call 0x23be270
00D17FB1 83c414             add esp, 0x14
00D17FB4 53                 push ebx
00D17FB5 68ea020000         push 0x2ea
00D17FBA e801957a01         call 0x24c14c0
00D17FBF 83c404             add esp, 4
00D17FC2 50                 push eax
00D17FC3 68fa000000         push 0xfa
00D17FC8 e9d2000000         jmp 0xd1809f
00D17FCD 53                 push ebx
00D17FCE 6805120100         push 0x11205
00D17FD3 e8e8947a01         call 0x24c14c0
00D17FD8 83c404             add esp, 4
00D17FDB 50                 push eax
00D17FDC 689e020000         push 0x29e
00D17FE1 e9b9000000         jmp 0xd1809f
00D17FE6 68fe010000         push 0x1fe
```

## Single response

```text
00D156C0 55                 push ebp
00D156C1 8bec               mov ebp, esp
00D156C3 6aff               push -1
00D156C5 68d923d702         push 0x2d723d9
00D156CA 64a100000000       mov eax, dword ptr fs:[0]
00D156D0 50                 push eax
00D156D1 81ecb0080000       sub esp, 0x8b0
00D156D7 a1e0ce9903         mov eax, dword ptr [0x399cee0]
00D156DC 33c5               xor eax, ebp
00D156DE 8945f0             mov dword ptr [ebp - 0x10], eax
00D156E1 53                 push ebx
00D156E2 56                 push esi
00D156E3 57                 push edi
00D156E4 50                 push eax
00D156E5 8d45f4             lea eax, [ebp - 0xc]
00D156E8 64a300000000       mov dword ptr fs:[0], eax
00D156EE 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D156F4 6839010000         push 0x139
00D156F9 e8c2b91e01         call 0x1f010c0
00D156FE 8b4804             mov ecx, dword ptr [eax + 4]
00D15701 8b4008             mov eax, dword ptr [eax + 8]
00D15704 2bc1               sub eax, ecx
00D15706 c1f802             sar eax, 2
00D15709 85c0               test eax, eax
00D1570B 7409               je 0xd15716
00D1570D 8b09               mov ecx, dword ptr [ecx]
00D1570F 8b11               mov edx, dword ptr [ecx]
00D15711 8b4278             mov eax, dword ptr [edx + 0x78]
00D15714 ffd0               call eax
00D15716 33db               xor ebx, ebx
00D15718 385d0c             cmp byte ptr [ebp + 0xc], bl
00D1571B 0f8423180000       je 0xd16f44
00D15721 8d8d93f7ffff       lea ecx, [ebp - 0x86d]
00D15727 6a01               push 1
00D15729 51                 push ecx
00D1572A 889d93f7ffff       mov byte ptr [ebp - 0x86d], bl
00D15730 e8fbfa6a01         call 0x23c5230
00D15735 8d9564f7ffff       lea edx, [ebp - 0x89c]
00D1573B 6a01               push 1
00D1573D 52                 push edx
00D1573E 889d64f7ffff       mov byte ptr [ebp - 0x89c], bl
00D15744 e8e7fa6a01         call 0x23c5230
00D15749 0fb68593f7ffff     movzx eax, byte ptr [ebp - 0x86d]
00D15750 83c410             add esp, 0x10
00D15753 83f805             cmp eax, 5
00D15756 0f872d190000       ja 0xd17089
00D1575C ff2485a8701101     jmp dword ptr [eax*4 + 0x11170a8]
00D15763 68c02b2403         push 0x3242bc0
00D15768 e853556a01         call 0x23bacc0
00D1576D 83c404             add esp, 4
00D15770 50                 push eax
00D15771 688b1a0000         push 0x1a8b
00D15776 68602b2403         push 0x3242b60
00D1577B e840556a01         call 0x23bacc0
00D15780 83c404             add esp, 4
00D15783 50                 push eax
00D15784 6890122403         push 0x3241290
00D15789 e832556a01         call 0x23bacc0
00D1578E 83c404             add esp, 4
00D15791 50                 push eax
00D15792 53                 push ebx
00D15793 e8d88b6a01         call 0x23be370
00D15798 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D1579E 83c414             add esp, 0x14
00D157A1 53                 push ebx
00D157A2 68dca99603         push 0x396a9dc
00D157A7 68a0e79503         push 0x395e7a0
00D157AC 53                 push ebx
00D157AD 68ffff0000         push 0xffff
00D157B2 53                 push ebx
00D157B3 68f3000000         push 0xf3
00D157B8 e8b3f41e01         call 0x1f04c70
00D157BD 50                 push eax
00D157BE e8a7e0b401         call 0x286386a
00D157C3 83c414             add esp, 0x14
00D157C6 68d7110100         push 0x111d7
00D157CB 8bc8               mov ecx, eax
00D157CD 898598f7ffff       mov dword ptr [ebp - 0x868], eax
00D157D3 e8f8523501         call 0x206aad0
00D157D8 8d85acf7ffff       lea eax, [ebp - 0x854]
00D157DE 50                 push eax
00D157DF 899dacf7ffff       mov dword ptr [ebp - 0x854], ebx
00D157E5 899da8f7ffff       mov dword ptr [ebp - 0x858], ebx
00D157EB e8b0fa6a01         call 0x23c52a0
00D157F0 8d8da8f7ffff       lea ecx, [ebp - 0x858]
00D157F6 51                 push ecx
00D157F7 e8a4fa6a01         call 0x23c52a0
00D157FC 8b95a8f7ffff       mov edx, dword ptr [ebp - 0x858]
00D15802 8b85acf7ffff       mov eax, dword ptr [ebp - 0x854]
00D15808 83c408             add esp, 8
00D1580B 52                 push edx
00D1580C 50                 push eax
00D1580D 68f02a2403         push 0x3242af0
00D15812 e8a9546a01         call 0x23bacc0
00D15817 83c404             add esp, 4
00D1581A 50                 push eax
00D1581B 689f1a0000         push 0x1a9f
00D15820 68602b2403         push 0x3242b60
00D15825 e896546a01         call 0x23bacc0
00D1582A 83c404             add esp, 4
00D1582D 50                 push eax
00D1582E 6890122403         push 0x3241290
00D15833 e888546a01         call 0x23bacc0
00D15838 83c404             add esp, 4
00D1583B 50                 push eax
00D1583C 53                 push ebx
00D1583D e82e8a6a01         call 0x23be270
00D15842 8b8dacf7ffff       mov ecx, dword ptr [ebp - 0x854]
00D15848 83c41c             add esp, 0x1c
00D1584B 51                 push ecx
00D1584C 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D15852 e8698e5d00         call 0x12ee6c0
00D15857 8b95a8f7ffff       mov edx, dword ptr [ebp - 0x858]
00D1585D 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D15863 52                 push edx
00D15864 8bf0               mov esi, eax
00D15866 e8558e5d00         call 0x12ee6c0
00D1586B 8bf8               mov edi, eax
00D1586D 3bf3               cmp esi, ebx
00D1586F 0f8418020000       je 0xd15a8d
00D15875 3bfb               cmp edi, ebx
00D15877 0f8410020000       je 0xd15a8d
00D1587D 8b06               mov eax, dword ptr [esi]
00D1587F 8b5028             mov edx, dword ptr [eax + 0x28]
00D15882 8bce               mov ecx, esi
00D15884 ffd2               call edx
00D15886 8d4814             lea ecx, [eax + 0x14]
00D15889 e842f432ff         call 0x44cd0
00D1588E 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D15894 50                 push eax
00D15895 e8769b5d00         call 0x12ef410
00D1589A 898594f7ffff       mov dword ptr [ebp - 0x86c], eax
00D158A0 8b07               mov eax, dword ptr [edi]
00D158A2 8b5028             mov edx, dword ptr [eax + 0x28]
00D158A5 8bcf               mov ecx, edi
00D158A7 ffd2               call edx
00D158A9 8d4814             lea ecx, [eax + 0x14]
00D158AC e81ff432ff         call 0x44cd0
00D158B1 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D158B7 50                 push eax
00D158B8 e8539b5d00         call 0x12ef410
00D158BD 89859cf7ffff       mov dword ptr [ebp - 0x864], eax
00D158C3 8b06               mov eax, dword ptr [esi]
00D158C5 8b5028             mov edx, dword ptr [eax + 0x28]
00D158C8 6a01               push 1
00D158CA 8bce               mov ecx, esi
00D158CC ffd2               call edx
00D158CE 8d4814             lea ecx, [eax + 0x14]
00D158D1 e8faf332ff         call 0x44cd0
00D158D6 8b0d14c9a503       mov ecx, dword ptr [0x3a5c914]
00D158DC 50                 push eax
00D158DD e89ed16301         call 0x2352a80
00D158E2 8bb008010000       mov esi, dword ptr [eax + 0x108]
00D158E8 8b07               mov eax, dword ptr [edi]
00D158EA 8b5028             mov edx, dword ptr [eax + 0x28]
00D158ED 6a01               push 1
00D158EF 8bcf               mov ecx, edi
00D158F1 ffd2               call edx
00D158F3 8d4814             lea ecx, [eax + 0x14]
00D158F6 e8d5f332ff         call 0x44cd0
00D158FB 8b0d14c9a503       mov ecx, dword ptr [0x3a5c914]
00D15901 50                 push eax
00D15902 e879d16301         call 0x2352a80
00D15907 8b8008010000       mov eax, dword ptr [eax + 0x108]
00D1590D 8985a0f7ffff       mov dword ptr [ebp - 0x860], eax
00D15913 8b8594f7ffff       mov eax, dword ptr [ebp - 0x86c]
00D15919 99                 cdq
00D1591A f7fe               idiv esi
00D1591C 8bc8               mov ecx, eax
00D1591E 8b859cf7ffff       mov eax, dword ptr [ebp - 0x864]
00D15924 899594f7ffff       mov dword ptr [ebp - 0x86c], edx
00D1592A 99                 cdq
00D1592B f7bda0f7ffff       idiv dword ptr [ebp - 0x860]
00D15931 89959cf7ffff       mov dword ptr [ebp - 0x864], edx
00D15937 8bf8               mov edi, eax
00D15939 3bcb               cmp ecx, ebx
00D1593B 0f8e96000000       jle 0xd159d7
00D15941 3bfb               cmp edi, ebx
00D15943 0f8e8e000000       jle 0xd159d7
00D15949 83f901             cmp ecx, 1
00D1594C 751c               jne 0xd1596a
00D1594E 399d94f7ffff       cmp dword ptr [ebp - 0x86c], ebx
00D15954 7516               jne 0xd1596c
00D15956 8b8dacf7ffff       mov ecx, dword ptr [ebp - 0x854]
00D1595C 51                 push ecx
00D1595D 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D15963 e898b95d00         call 0x12f1300
00D15968 eb02               jmp 0xd1596c
00D1596A 7c20               jl 0xd1598c
00D1596C 8b95acf7ffff       mov edx, dword ptr [ebp - 0x854]
00D15972 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D15978 52                 push edx
00D15979 e8428d5d00         call 0x12ee6c0
00D1597E 3bc3               cmp eax, ebx
00D15980 740a               je 0xd1598c
00D15982 8b10               mov edx, dword ptr [eax]
00D15984 8bc8               mov ecx, eax
00D15986 8b4250             mov eax, dword ptr [edx + 0x50]
00D15989 56                 push esi
00D1598A ffd0               call eax
00D1598C 83ff01             cmp edi, 1
00D1598F 751c               jne 0xd159ad
00D15991 399d9cf7ffff       cmp dword ptr [ebp - 0x864], ebx
00D15997 7516               jne 0xd159af
00D15999 8b8da8f7ffff       mov ecx, dword ptr [ebp - 0x858]
00D1599F 51                 push ecx
00D159A0 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D159A6 e855b95d00         call 0x12f1300
00D159AB eb02               jmp 0xd159af
00D159AD 7c60               jl 0xd15a0f
00D159AF 8b95a8f7ffff       mov edx, dword ptr [ebp - 0x858]
00D159B5 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D159BB 52                 push edx
00D159BC e8ff8c5d00         call 0x12ee6c0
00D159C1 3bc3               cmp eax, ebx
00D159C3 744a               je 0xd15a0f
00D159C5 8b8da0f7ffff       mov ecx, dword ptr [ebp - 0x860]
00D159CB 8b10               mov edx, dword ptr [eax]
00D159CD 8b5250             mov edx, dword ptr [edx + 0x50]
00D159D0 51                 push ecx
00D159D1 8bc8               mov ecx, eax
00D159D3 ffd2               call edx
00D159D5 eb38               jmp 0xd15a0f
00D159D7 68702a2403         push 0x3242a70
00D159DC e8df526a01         call 0x23bacc0
00D159E1 83c404             add esp, 4
00D159E4 50                 push eax
00D159E5 68e41a0000         push 0x1ae4
00D159EA 68602b2403         push 0x3242b60
00D159EF e8cc526a01         call 0x23bacc0
00D159F4 83c404             add esp, 4
00D159F7 50                 push eax
00D159F8 6890122403         push 0x3241290
00D159FD e8be526a01         call 0x23bacc0
00D15A02 83c404             add esp, 4
00D15A05 50                 push eax
00D15A06 53                 push ebx
00D15A07 e864886a01         call 0x23be270
00D15A0C 83c414             add esp, 0x14
00D15A0F 8d8d68f7ffff       lea ecx, [ebp - 0x898]
00D15A15 e846a63700         call 0x1090060
00D15A1A 895dfc             mov dword ptr [ebp - 4], ebx
00D15A1D 8d85b3f7ffff       lea eax, [ebp - 0x84d]
00D15A23 50                 push eax
00D15A24 8d8d68f7ffff       lea ecx, [ebp - 0x898]
00D15A2A 51                 push ecx
00D15A2B 6a01               push 1
00D15A2D 889db3f7ffff       mov byte ptr [ebp - 0x84d], bl
00D15A33 e888f8ffff         call 0xd152c0
00D15A38 8b9598f7ffff       mov edx, dword ptr [ebp - 0x868]
00D15A3E 53                 push ebx
00D15A3F 52                 push edx
00D15A40 8d8568f7ffff       lea eax, [ebp - 0x898]
00D15A46 50                 push eax
00D15A47 e8c483ffff         call 0xd0de10
00D15A4C 83c418             add esp, 0x18
00D15A4F 80bdb3f7ffff01     cmp byte ptr [ebp - 0x84d], 1
00D15A56 6aff               push -1
00D15A58 6aff               push -1
00D15A5A 53                 push ebx
00D15A5B 53                 push ebx
00D15A5C 6aff               push -1
00D15A5E 7507               jne 0xd15a67
00D15A60 680d120100         push 0x1120d
00D15A65 eb05               jmp 0xd15a6c
00D15A67 680c120100         push 0x1120c
00D15A6C e84fba7a01         call 0x24c14c0
00D15A71 83c404             add esp, 4
00D15A74 50                 push eax
00D15A75 e8a62f5300         call 0x1248a20
00D15A7A 83c418             add esp, 0x18
00D15A7D 8d8d68f7ffff       lea ecx, [ebp - 0x898]
00D15A83 e8f8e8b400         call 0x1864380
00D15A88 e9fc150000         jmp 0xd17089
00D15A8D 68f8292403         push 0x32429f8
00D15A92 e829526a01         call 0x23bacc0
00D15A97 83c404             add esp, 4
00D15A9A 50                 push eax
00D15A9B 68af1b0000         push 0x1baf
00D15AA0 68602b2403         push 0x3242b60
00D15AA5 e816526a01         call 0x23bacc0
00D15AAA 83c404             add esp, 4
00D15AAD 50                 push eax
00D15AAE 6890122403         push 0x3241290
00D15AB3 e808526a01         call 0x23bacc0
00D15AB8 83c404             add esp, 4
00D15ABB 50                 push eax
00D15ABC 53                 push ebx
00D15ABD e8ae876a01         call 0x23be270
00D15AC2 83c414             add esp, 0x14
00D15AC5 683a0c0000         push 0xc3a
00D15ACA e861d1feff         call 0xd02c30
00D15ACF 50                 push eax
00D15AD0 6858020000         push 0x258
00D15AD5 e9a4150000         jmp 0xd1707e
00D15ADA 68c02b2403         push 0x3242bc0
00D15ADF e8dc516a01         call 0x23bacc0
00D15AE4 83c404             add esp, 4
00D15AE7 50                 push eax
00D15AE8 68b81b0000         push 0x1bb8
00D15AED 68602b2403         push 0x3242b60
00D15AF2 e8c9516a01         call 0x23bacc0
00D15AF7 83c404             add esp, 4
00D15AFA 50                 push eax
00D15AFB 6890122403         push 0x3241290
00D15B00 e8bb516a01         call 0x23bacc0
00D15B05 83c404             add esp, 4
00D15B08 50                 push eax
00D15B09 53                 push ebx
00D15B0A e861876a01         call 0x23be270
00D15B0F 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D15B15 83c414             add esp, 0x14
00D15B18 53                 push ebx
00D15B19 68dca99603         push 0x396a9dc
00D15B1E 68a0e79503         push 0x395e7a0
00D15B23 53                 push ebx
00D15B24 68ffff0000         push 0xffff
00D15B29 53                 push ebx
00D15B2A 68f3000000         push 0xf3
00D15B2F e83cf11e01         call 0x1f04c70
00D15B34 50                 push eax
00D15B35 e830ddb401         call 0x286386a
00D15B3A 83c414             add esp, 0x14
00D15B3D 68d7110100         push 0x111d7
00D15B42 8bc8               mov ecx, eax
00D15B44 89859cf7ffff       mov dword ptr [ebp - 0x864], eax
00D15B4A e8814f3501         call 0x206aad0
00D15B4F 8d8dacf7ffff       lea ecx, [ebp - 0x854]
00D15B55 51                 push ecx
00D15B56 899dacf7ffff       mov dword ptr [ebp - 0x854], ebx
00D15B5C 899da8f7ffff       mov dword ptr [ebp - 0x858], ebx
00D15B62 e839f76a01         call 0x23c52a0
00D15B67 8d95a8f7ffff       lea edx, [ebp - 0x858]
00D15B6D 52                 push edx
00D15B6E e82df76a01         call 0x23c52a0
00D15B73 8b85a8f7ffff       mov eax, dword ptr [ebp - 0x858]
00D15B79 8b8dacf7ffff       mov ecx, dword ptr [ebp - 0x854]
00D15B7F 83c408             add esp, 8
00D15B82 50                 push eax
00D15B83 51                 push ecx
00D15B84 68f02a2403         push 0x3242af0
00D15B89 e832516a01         call 0x23bacc0
00D15B8E 83c404             add esp, 4
00D15B91 50                 push eax
00D15B92 68c81b0000         push 0x1bc8
00D15B97 68602b2403         push 0x3242b60
00D15B9C e81f516a01         call 0x23bacc0
00D15BA1 83c404             add esp, 4
00D15BA4 50                 push eax
00D15BA5 6890122403         push 0x3241290
00D15BAA e811516a01         call 0x23bacc0
```

## Package popup response

```text
00D3A830 55                 push ebp
00D3A831 8bec               mov ebp, esp
00D3A833 6aff               push -1
00D3A835 68ec31d702         push 0x2d731ec
00D3A83A 64a100000000       mov eax, dword ptr fs:[0]
00D3A840 50                 push eax
00D3A841 81ecec000000       sub esp, 0xec
00D3A847 a1e0ce9903         mov eax, dword ptr [0x399cee0]
00D3A84C 33c5               xor eax, ebp
00D3A84E 8945f0             mov dword ptr [ebp - 0x10], eax
00D3A851 53                 push ebx
00D3A852 56                 push esi
00D3A853 57                 push edi
00D3A854 50                 push eax
00D3A855 8d45f4             lea eax, [ebp - 0xc]
00D3A858 64a300000000       mov dword ptr fs:[0], eax
00D3A85E 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D3A864 33ff               xor edi, edi
00D3A866 57                 push edi
00D3A867 6aff               push -1
00D3A869 6899010000         push 0x199
00D3A86E e81d9f1c01         call 0x1f04790
00D3A873 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D3A879 57                 push edi
00D3A87A 6aff               push -1
00D3A87C 689a010000         push 0x19a
00D3A881 e80a9f1c01         call 0x1f04790
00D3A886 807d0c00           cmp byte ptr [ebp + 0xc], 0
00D3A88A 0f8429030000       je 0xd3abb9
00D3A890 8d8520ffffff       lea eax, [ebp - 0xe0]
00D3A896 50                 push eax
00D3A897 89bd20ffffff       mov dword ptr [ebp - 0xe0], edi
00D3A89D 89bd14ffffff       mov dword ptr [ebp - 0xec], edi
00D3A8A3 89bd30ffffff       mov dword ptr [ebp - 0xd0], edi
00D3A8A9 89bd2cffffff       mov dword ptr [ebp - 0xd4], edi
00D3A8AF 89bd24ffffff       mov dword ptr [ebp - 0xdc], edi
00D3A8B5 e8e6a96801         call 0x23c52a0
00D3A8BA 8d8d14ffffff       lea ecx, [ebp - 0xec]
00D3A8C0 51                 push ecx
00D3A8C1 e84aaa6801         call 0x23c5310
00D3A8C6 8d9528ffffff       lea edx, [ebp - 0xd8]
00D3A8CC 52                 push edx
00D3A8CD 89bd28ffffff       mov dword ptr [ebp - 0xd8], edi
00D3A8D3 e838aa6801         call 0x23c5310
00D3A8D8 8d8530ffffff       lea eax, [ebp - 0xd0]
00D3A8DE 50                 push eax
00D3A8DF e8bca96801         call 0x23c52a0
00D3A8E4 83c410             add esp, 0x10
00D3A8E7 33f6               xor esi, esi
00D3A8E9 6639bd30ffffff     cmp word ptr [ebp - 0xd0], di
00D3A8F0 762f               jbe 0xd3a921
00D3A8F2 57                 push edi
00D3A8F3 68dca99603         push 0x396a9dc
00D3A8F8 68a0e79503         push 0x395e7a0
00D3A8FD 57                 push edi
00D3A8FE 6afd               push -3
00D3A900 e82b83fcff         call 0xd02c30
00D3A905 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D3A90B 50                 push eax
00D3A90C 68f3000000         push 0xf3
00D3A911 e85aa31c01         call 0x1f04c70
00D3A916 50                 push eax
00D3A917 e84e8fb201         call 0x286386a
00D3A91C 83c414             add esp, 0x14
00D3A91F 8bf0               mov esi, eax
00D3A921 33c9               xor ecx, ecx
00D3A923 33db               xor ebx, ebx
00D3A925 663b8d30ffffff     cmp cx, word ptr [ebp - 0xd0]
00D3A92C 0f83c0000000       jae 0xd3a9f2
00D3A932 8d952cffffff       lea edx, [ebp - 0xd4]
00D3A938 52                 push edx
00D3A939 e8d2a96801         call 0x23c5310
00D3A93E 8d8524ffffff       lea eax, [ebp - 0xdc]
00D3A944 50                 push eax
00D3A945 e8c6a96801         call 0x23c5310
00D3A94A 83c408             add esp, 8
00D3A94D 81bd2cffffff3dec2800 cmp dword ptr [ebp - 0xd4], 0x28ec3d
00D3A957 752a               jne 0xd3a983
00D3A959 57                 push edi
00D3A95A 57                 push edi
00D3A95B 57                 push edi
00D3A95C 57                 push edi
00D3A95D 57                 push edi
00D3A95E 68ff784eff         push 0xff4e78ff
00D3A963 681f1e0100         push 0x11e1f
00D3A968 e8536b7801         call 0x24c14c0
00D3A96D 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D3A973 83c404             add esp, 4
00D3A976 50                 push eax
00D3A977 e8d40596ff         call 0x69af50
00D3A97C 8bc8               mov ecx, eax
00D3A97E e89d5c9200         call 0x1660620
00D3A983 3bf7               cmp esi, edi
00D3A985 745b               je 0xd3a9e2
00D3A987 8b8d24ffffff       mov ecx, dword ptr [ebp - 0xdc]
00D3A98D 51                 push ecx
00D3A98E 8d8d34ffffff       lea ecx, [ebp - 0xcc]
00D3A994 e8b7184100         call 0x114c250
00D3A999 8b952cffffff       mov edx, dword ptr [ebp - 0xd4]
00D3A99F 57                 push edi
00D3A9A0 6a01               push 1
00D3A9A2 50                 push eax
00D3A9A3 52                 push edx
00D3A9A4 897dfc             mov dword ptr [ebp - 4], edi
00D3A9A7 e874a89400         call 0x1685220
00D3A9AC 83c410             add esp, 0x10
00D3A9AF 8d8d34ffffff       lea ecx, [ebp - 0xcc]
00D3A9B5 8bf8               mov edi, eax
00D3A9B7 c745fcffffffff     mov dword ptr [ebp - 4], 0xffffffff
00D3A9BE e80dde2fff         call 0x387d0
00D3A9C3 6a00               push 0
00D3A9C5 6a00               push 0
00D3A9C7 6a01               push 1
00D3A9C9 57                 push edi
00D3A9CA 8bce               mov ecx, esi
00D3A9CC e87f2a3301         call 0x206d450
00D3A9D1 85ff               test edi, edi
00D3A9D3 740b               je 0xd3a9e0
00D3A9D5 8b07               mov eax, dword ptr [edi]
00D3A9D7 8b500c             mov edx, dword ptr [eax + 0xc]
00D3A9DA 6a01               push 1
00D3A9DC 8bcf               mov ecx, edi
00D3A9DE ffd2               call edx
00D3A9E0 33ff               xor edi, edi
00D3A9E2 0fb78530ffffff     movzx eax, word ptr [ebp - 0xd0]
00D3A9E9 43                 inc ebx
00D3A9EA 3bd8               cmp ebx, eax
00D3A9EC 0f8c40ffffff       jl 0xd3a932
00D3A9F2 0fb78d20ffffff     movzx ecx, word ptr [ebp - 0xe0]
00D3A9F9 51                 push ecx
00D3A9FA 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00D3AA00 e8bb3c5b00         call 0x12ee6c0
00D3AA05 8bd8               mov ebx, eax
00D3AA07 8b8528ffffff       mov eax, dword ptr [ebp - 0xd8]
00D3AA0D 899d1cffffff       mov dword ptr [ebp - 0xe4], ebx
00D3AA13 83f801             cmp eax, 1
00D3AA16 0f8582010000       jne 0xd3ab9e
00D3AA1C 3bf7               cmp esi, edi
00D3AA1E 0f846b020000       je 0xd3ac8f
00D3AA24 6a07               push 7
00D3AA26 8bce               mov ecx, esi
00D3AA28 e8c3ff3201         call 0x206a9f0
00D3AA2D 8b16               mov edx, dword ptr [esi]
00D3AA2F 8b527c             mov edx, dword ptr [edx + 0x7c]
00D3AA32 8d850cffffff       lea eax, [ebp - 0xf4]
00D3AA38 50                 push eax
00D3AA39 8bce               mov ecx, esi
00D3AA3B ffd2               call edx
00D3AA3D 8b08               mov ecx, dword ptr [eax]
00D3AA3F c745fc01000000     mov dword ptr [ebp - 4], 1
00D3AA46 e8050596ff         call 0x69af50
00D3AA4B 898508ffffff       mov dword ptr [ebp - 0xf8], eax
00D3AA51 8b8510ffffff       mov eax, dword ptr [ebp - 0xf0]
00D3AA57 c745fcffffffff     mov dword ptr [ebp - 4], 0xffffffff
00D3AA5E 3bc7               cmp eax, edi
00D3AA60 742b               je 0xd3aa8d
00D3AA62 8bf8               mov edi, eax
00D3AA64 83c004             add eax, 4
00D3AA67 83c9ff             or ecx, 0xffffffff
00D3AA6A f00fc108           lock xadd dword ptr [eax], ecx
00D3AA6E 751d               jne 0xd3aa8d
00D3AA70 8b17               mov edx, dword ptr [edi]
00D3AA72 8b02               mov eax, dword ptr [edx]
00D3AA74 8bcf               mov ecx, edi
00D3AA76 ffd0               call eax
00D3AA78 8d4f08             lea ecx, [edi + 8]
00D3AA7B 83caff             or edx, 0xffffffff
00D3AA7E f00fc111           lock xadd dword ptr [ecx], edx
00D3AA82 7509               jne 0xd3aa8d
00D3AA84 8b07               mov eax, dword ptr [edi]
00D3AA86 8b5004             mov edx, dword ptr [eax + 4]
00D3AA89 8bcf               mov ecx, edi
00D3AA8B ffd2               call edx
00D3AA8D 8b06               mov eax, dword ptr [esi]
```

## Avatar response

```text
00EBB120 55                 push ebp
00EBB121 8bec               mov ebp, esp
00EBB123 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00EBB129 6a01               push 1
00EBB12B e8103f4400         call 0x12ff040
00EBB130 8b0dd0c9a503       mov ecx, dword ptr [0x3a5c9d0]
00EBB136 6a37               push 0x37
00EBB138 e893084400         call 0x12fb9d0
00EBB13D 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00EBB143 6a00               push 0
00EBB145 6aff               push -1
00EBB147 683f010000         push 0x13f
00EBB14C e83f960401         call 0x1f04790
00EBB151 807d0c00           cmp byte ptr [ebp + 0xc], 0
00EBB155 0f8484000000       je 0xebb1df
00EBB15B 8d450c             lea eax, [ebp + 0xc]
00EBB15E 57                 push edi
00EBB15F 50                 push eax
00EBB160 c7450c00000000     mov dword ptr [ebp + 0xc], 0
00EBB167 e834a15001         call 0x23c52a0
00EBB16C 0fb74d0c           movzx ecx, word ptr [ebp + 0xc]
00EBB170 83c404             add esp, 4
00EBB173 51                 push ecx
00EBB174 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00EBB17A e841354300         call 0x12ee6c0
00EBB17F 8bf8               mov edi, eax
00EBB181 85ff               test edi, edi
00EBB183 7444               je 0xebb1c9
00EBB185 8b17               mov edx, dword ptr [edi]
00EBB187 8b4228             mov eax, dword ptr [edx + 0x28]
00EBB18A 56                 push esi
00EBB18B 8bcf               mov ecx, edi
00EBB18D ffd0               call eax
00EBB18F 6a01               push 1
00EBB191 6a32               push 0x32
00EBB193 8d7018             lea esi, [eax + 0x18]
00EBB196 6a04               push 4
00EBB198 56                 push esi
00EBB199 e8721f9901         call 0x284d110
00EBB19E 8b36               mov esi, dword ptr [esi]
00EBB1A0 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00EBB1A6 83c410             add esp, 0x10
00EBB1A9 56                 push esi
00EBB1AA e861424300         call 0x12ef410
00EBB1AF 5e                 pop esi
00EBB1B0 85c0               test eax, eax
00EBB1B2 7e15               jle 0xebb1c9
00EBB1B4 83f801             cmp eax, 1
00EBB1B7 7513               jne 0xebb1cc
00EBB1B9 0fb74d0c           movzx ecx, word ptr [ebp + 0xc]
00EBB1BD 51                 push ecx
00EBB1BE 8b0dbcc9a503       mov ecx, dword ptr [0x3a5c9bc]
00EBB1C4 e837614300         call 0x12f1300
00EBB1C9 5f                 pop edi
00EBB1CA 5d                 pop ebp
00EBB1CB c3                 ret
00EBB1CC 83f802             cmp eax, 2
00EBB1CF 7cf8               jl 0xebb1c9
00EBB1D1 8b17               mov edx, dword ptr [edi]
00EBB1D3 8b4250             mov eax, dword ptr [edx + 0x50]
00EBB1D6 6a01               push 1
00EBB1D8 8bcf               mov ecx, edi
00EBB1DA ffd0               call eax
00EBB1DC 5f                 pop edi
00EBB1DD 5d                 pop ebp
00EBB1DE c3                 ret
00EBB1DF 53                 push ebx
00EBB1E0 8a5d10             mov bl, byte ptr [ebp + 0x10]
00EBB1E3 80fb16             cmp bl, 0x16
00EBB1E6 7470               je 0xebb258
00EBB1E8 0fb6cb             movzx ecx, bl
00EBB1EB 51                 push ecx
00EBB1EC 6868f92703         push 0x327f968
00EBB1F1 e8cafa4f01         call 0x23bacc0
00EBB1F6 83c404             add esp, 4
00EBB1F9 50                 push eax
00EBB1FA 6838f92703         push 0x327f938
00EBB1FF e8bcfa4f01         call 0x23bacc0
00EBB204 83c404             add esp, 4
00EBB207 50                 push eax
00EBB208 6a58               push 0x58
00EBB20A 6868f92703         push 0x327f968
00EBB20F e8acfa4f01         call 0x23bacc0
00EBB214 83c404             add esp, 4
00EBB217 50                 push eax
00EBB218 6898f82703         push 0x327f898
00EBB21D e89efa4f01         call 0x23bacc0
00EBB222 83c404             add esp, 4
00EBB225 50                 push eax
00EBB226 6a00               push 0
00EBB228 e843305001         call 0x23be270
00EBB22D 83c41c             add esp, 0x1c
00EBB230 80fbd6             cmp bl, 0xd6
00EBB233 7520               jne 0xebb255
00EBB235 6a00               push 0
00EBB237 687d1e0100         push 0x11e7d
00EBB23C e87f626001         call 0x24c14c0
00EBB241 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00EBB247 83c404             add esp, 4
00EBB24A 50                 push eax
00EBB24B 6809020000         push 0x209
00EBB250 e81b9a0401         call 0x1f04c70
00EBB255 5b                 pop ebx
00EBB256 5d                 pop ebp
00EBB257 c3                 ret
00EBB258 6a00               push 0
00EBB25A 6819030000         push 0x319
00EBB25F ebdb               jmp 0xebb23c
00EBB261 cc                 int3
00EBB262 cc                 int3
00EBB263 cc                 int3
00EBB264 cc                 int3
00EBB265 cc                 int3
00EBB266 cc                 int3
00EBB267 cc                 int3
00EBB268 cc                 int3
00EBB269 cc                 int3
00EBB26A cc                 int3
00EBB26B cc                 int3
00EBB26C cc                 int3
00EBB26D cc                 int3
00EBB26E cc                 int3
00EBB26F cc                 int3
00EBB270 55                 push ebp
00EBB271 8bec               mov ebp, esp
00EBB273 81ec20020000       sub esp, 0x220
00EBB279 a1e0ce9903         mov eax, dword ptr [0x399cee0]
00EBB27E 33c5               xor eax, ebp
00EBB280 8945fc             mov dword ptr [ebp - 4], eax
00EBB283 8b4508             mov eax, dword ptr [ebp + 8]
00EBB286 53                 push ebx
00EBB287 8bd9               mov ebx, ecx
00EBB289 8985ecfdffff       mov dword ptr [ebp - 0x214], eax
00EBB28F 83f8ff             cmp eax, -1
00EBB292 0f8455030000       je 0xebb5ed
00EBB298 83bb0c04000000     cmp dword ptr [ebx + 0x40c], 0
```

## Premium subtype dispatcher

```text
00D6EA50 55                 push ebp
00D6EA51 8bec               mov ebp, esp
00D6EA53 6aff               push -1
00D6EA55 684b4ad702         push 0x2d74a4b
00D6EA5A 64a100000000       mov eax, dword ptr fs:[0]
00D6EA60 50                 push eax
00D6EA61 81ec38040000       sub esp, 0x438
00D6EA67 a1e0ce9903         mov eax, dword ptr [0x399cee0]
00D6EA6C 33c5               xor eax, ebp
00D6EA6E 8945f0             mov dword ptr [ebp - 0x10], eax
00D6EA71 56                 push esi
00D6EA72 50                 push eax
00D6EA73 8d45f4             lea eax, [ebp - 0xc]
00D6EA76 64a300000000       mov dword ptr fs:[0], eax
00D6EA7C 8d85dcfbffff       lea eax, [ebp - 0x424]
00D6EA82 50                 push eax
00D6EA83 e818686501         call 0x23c52a0
00D6EA88 0fb785dcfbffff     movzx eax, word ptr [ebp - 0x424]
00D6EA8F 48                 dec eax
00D6EA90 83c404             add esp, 4
00D6EA93 83f80d             cmp eax, 0xd
00D6EA96 0f87bd040000       ja 0xd6ef59
00D6EA9C 0fb6889cef1601     movzx ecx, byte ptr [eax + 0x116ef9c]
00D6EAA3 ff248d84ef1601     jmp dword ptr [ecx*4 + 0x116ef84]
00D6EAAA 8d95e0fbffff       lea edx, [ebp - 0x420]
00D6EAB0 52                 push edx
00D6EAB1 c785e0fbffff00000000 mov dword ptr [ebp - 0x420], 0
00D6EABB e8e0676501         call 0x23c52a0
00D6EAC0 0fb7b5e0fbffff     movzx esi, word ptr [ebp - 0x420]
00D6EAC7 83c404             add esp, 4
00D6EACA e8b1b63d01         call 0x214a180
00D6EACF 3bc6               cmp eax, esi
00D6EAD1 0f8382040000       jae 0xd6ef59
00D6EAD7 6a00               push 0
00D6EAD9 56                 push esi
00D6EADA e8c1b53d01         call 0x214a0a0
00D6EADF 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D6EAE5 83c408             add esp, 8
00D6EAE8 688a000000         push 0x8a
00D6EAED e82e2d1901         call 0x1f01820
00D6EAF2 84c0               test al, al
00D6EAF4 0f845f040000       je 0xd6ef59
00D6EAFA 8b0db8c9a503       mov ecx, dword ptr [0x3a5c9b8]
00D6EB00 6a00               push 0
00D6EB02 688a000000         push 0x8a
00D6EB07 e8b4251901         call 0x1f010c0
00D6EB0C 8bc8               mov ecx, eax
00D6EB0E e8bd9e84ff         call 0x5b89d0
00D6EB13 8b08               mov ecx, dword ptr [eax]
00D6EB15 85c9               test ecx, ecx
00D6EB17 0f843c040000       je 0xd6ef59
00D6EB1D 6a00               push 0
00D6EB1F e8cc2c3e01         call 0x21517f0
00D6EB24 e930040000         jmp 0xd6ef59
00D6EB29 8d85e7fbffff       lea eax, [ebp - 0x419]
00D6EB2F 6a01               push 1
00D6EB31 660f57c0           xorpd xmm0, xmm0
00D6EB35 50                 push eax
00D6EB36 c685e7fbffffff     mov byte ptr [ebp - 0x419], 0xff
00D6EB3D 660f1385d0fbffff   movlpd qword ptr [ebp - 0x430], xmm0
00D6EB45 e8e6666501         call 0x23c5230
00D6EB4A 8d8dd0fbffff       lea ecx, [ebp - 0x430]
00D6EB50 6a08               push 8
00D6EB52 51                 push ecx
00D6EB53 e828686501         call 0x23c5380
```

## Premium jump tables

Dispatcher uses subtype minus 1. Subtype 2 maps via index 1 to VA 0x116EB29 (RVA 0xD6EB29).

```text
VA 0116EF9C 0001050105050002050503050504
VA 0116EF84 aaea160129eb160113ee16013dee160106ef160159ef1601
```

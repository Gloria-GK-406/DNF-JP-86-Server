# A21 client static evidence

Image base: `0x00400000`.

Handler windows are bounded disassembly, not recovered function boundaries. No live packets or gameplay acceptance were collected.

## NOTI registrations 008E-0092; VA 0x0115F7C5; RVA 0x00D5F7C5

```asm
0115F7C5: push     0
0115F7C7: push     0x114a8f0
0115F7CC: push     0x8e
0115F7D1: mov      ecx, esi
0115F7D3: call     0x163c4a0
0115F7D8: push     0
0115F7DA: push     0x1159850
0115F7DF: push     0x8f
0115F7E4: mov      ecx, esi
0115F7E6: call     0x163c4a0
0115F7EB: push     0
0115F7ED: push     0x1153a40
0115F7F2: push     0x90
0115F7F7: mov      ecx, esi
0115F7F9: call     0x163c4a0
0115F7FE: push     0
0115F800: push     0x1148850
0115F805: push     0x91
0115F80A: mov      ecx, esi
0115F80C: call     0x163c4a0
0115F811: push     0
0115F813: push     0x1146de0
0115F818: push     0x92
0115F81D: mov      ecx, esi
0115F81F: call     0x163c4a0
```

## NOTI 008E handler initial window; VA 0x0114A8F0; RVA 0x00D4A8F0

```asm
0114A8F0: push     ebp
0114A8F1: mov      ebp, esp
0114A8F3: sub      esp, 0x214
0114A8F9: mov      eax, dword ptr [0x399cee0]
0114A8FE: xor      eax, ebp
0114A900: mov      dword ptr [ebp - 4], eax
0114A903: mov      ecx, dword ptr [0x3a5c990]
0114A909: push     ebx
0114A90A: push     esi
0114A90B: call     0x19e74c0
0114A910: mov      ecx, dword ptr [0x3a5c980]
0114A916: mov      esi, eax
0114A918: call     0x19e3290
0114A91D: cmp      eax, esi
0114A91F: jne      0x114a93a
0114A921: call     dword ptr [0x3029608]
0114A927: mov      esi, eax
0114A929: call     0x1102c30
0114A92E: mov      ecx, eax
0114A930: call     0x28d6d00
0114A935: mov      dword ptr [eax + 0x58], esi
0114A938: jmp      0x114a964
0114A93A: call     dword ptr [0x3029608]
0114A940: mov      esi, eax
0114A942: call     0x1102c30
0114A947: mov      ecx, eax
0114A949: call     0x28d6d00
0114A94E: mov      dword ptr [eax + 0x5c], esi
0114A951: call     0x1102c30
0114A956: mov      ecx, eax
0114A958: call     0x28d6d00
0114A95D: mov      dword ptr [eax + 0x58], 0xffffffff
0114A964: xor      ebx, ebx
0114A966: xor      eax, eax
0114A968: jmp      0x114a970
0114A96A: lea      ebx, [ebx]
0114A970: cmp      eax, 0x80
0114A975: jae      0x114aa90
0114A97B: mov      dword ptr [eax + 0x3a8d7f8], ebx
0114A981: add      eax, 4
0114A984: cmp      eax, 0x64
0114A987: jl       0x114a970
0114A989: mov      dword ptr [0x3a8d87c], ebx
0114A98F: mov      dword ptr [0x3a8d880], ebx
0114A995: mov      dword ptr [0x3a8d884], ebx
0114A99B: call     0x1102c30
0114A9A0: mov      ecx, eax
0114A9A2: call     0x19fc7f0
0114A9A7: mov      ecx, eax
0114A9A9: call     0x26d7c90
0114A9AE: call     0x1102c30
0114A9B3: mov      ecx, eax
0114A9B5: call     0x19fc7f0
0114A9BA: mov      ecx, eax
0114A9BC: call     0x26d7bf0
0114A9C1: lea      eax, [ebp - 0x214]
0114A9C7: push     eax
0114A9C8: mov      dword ptr [ebp - 0x214], 0x2af8
0114A9D2: mov      dword ptr [ebp - 0x210], ebx
0114A9D8: call     0x27c5310 ; ReadU32
0114A9DD: lea      ecx, [ebp - 0x210]
0114A9E3: push     ecx
0114A9E4: call     0x27c52a0 ; ReadU16
0114A9E9: mov      edx, dword ptr [ebp - 0x210]
0114A9EF: add      esp, 8
0114A9F2: push     edx
0114A9F3: call     0x1102c30
0114A9F8: mov      ecx, eax
0114A9FA: call     0x19fc7f0
0114A9FF: mov      ecx, eax
0114AA01: call     0x26d74c0
0114AA06: mov      eax, dword ptr [ebp - 0x214]
0114AA0C: push     eax
0114AA0D: call     0x10cdca0
0114AA12: mov      esi, eax
0114AA14: add      esp, 4
0114AA17: cmp      esi, ebx
0114AA19: je       0x114add5
0114AA1F: push     edi
0114AA20: push     esi
0114AA21: call     0x1102c30
0114AA26: mov      ecx, eax
0114AA28: call     0x28d6d00
0114AA2D: mov      ecx, eax
0114AA2F: call     0x2711ed0
0114AA34: call     0x1102c30
0114AA39: mov      ecx, eax
0114AA3B: call     0x28d6d00
0114AA40: mov      ecx, eax
0114AA42: call     0x2712d60
0114AA47: mov      edx, dword ptr [esi]
0114AA49: mov      edi, eax
0114AA4B: mov      eax, dword ptr [edx + 0xc]
0114AA4E: push     ebx
0114AA4F: mov      ecx, esi
0114AA51: call     eax
0114AA53: push     esi
0114AA54: mov      ecx, edi
0114AA56: call     0x198c6d0
0114AA5B: push     ebx
0114AA5C: call     0x10cbda0
0114AA61: lea      ecx, [esi + 0xae4]
0114AA67: mov      eax, ecx
0114AA69: and      eax, 0xf
0114AA6C: add      esp, 4
0114AA6F: mov      edi, 0xff
0114AA74: mov      dword ptr [ebp - 0x208], edi
0114AA7A: lea      edx, [ecx + 4]
0114AA7D: cmp      eax, 0xc
0114AA80: ja       0x114aad1
0114AA82: movzx    eax, byte ptr [eax + 0x114adfc]
0114AA89: jmp      dword ptr [eax*4 + 0x114ade8]
0114AA90: call     0x2c623e1
0114AA95: push     edx
0114AA96: lea      edx, [ebp - 0x208]
0114AA9C: push     edx
0114AA9D: call     0x27bbbe0
0114AAA2: jmp      0x114aade
0114AAA4: push     edx
0114AAA5: lea      eax, [ebp - 0x208]
0114AAAB: push     eax
0114AAAC: call     0x27bbca0
0114AAB1: jmp      0x114aade
0114AAB3: push     edx
0114AAB4: lea      edx, [ebp - 0x208]
0114AABA: push     edx
0114AABB: call     0x27bbd50
0114AAC0: jmp      0x114aade
0114AAC2: push     edx
0114AAC3: lea      eax, [ebp - 0x208]
0114AAC9: push     eax
0114AACA: call     0x27bbe10
0114AACF: jmp      0x114aade
0114AAD1: push     edx
0114AAD2: lea      edx, [ebp - 0x208]
0114AAD8: push     edx
0114AAD9: call     0x27bba60
0114AADE: lea      ecx, [esi + 0xaec]
0114AAE4: mov      eax, ecx
0114AAE6: and      eax, 0xf
0114AAE9: mov      dword ptr [ebp - 0x208], edi
0114AAEF: lea      edx, [ecx + 4]
0114AAF2: cmp      eax, 0xc
0114AAF5: ja       0x114ab41
0114AAF7: movzx    eax, byte ptr [eax + 0x114ae20]
0114AAFE: jmp      dword ptr [eax*4 + 0x114ae0c]
0114AB05: push     edx
0114AB06: lea      edx, [ebp - 0x208]
0114AB0C: push     edx
0114AB0D: call     0x27bbbe0
0114AB12: jmp      0x114ab4e
0114AB14: push     edx
0114AB15: lea      eax, [ebp - 0x208]
0114AB1B: push     eax
0114AB1C: call     0x27bbca0
0114AB21: jmp      0x114ab4e
0114AB23: push     edx
0114AB24: lea      edx, [ebp - 0x208]
0114AB2A: push     edx
0114AB2B: call     0x27bbd50
0114AB30: jmp      0x114ab4e
0114AB32: push     edx
0114AB33: lea      eax, [ebp - 0x208]
0114AB39: push     eax
0114AB3A: call     0x27bbe10
```

## NOTI 008E branch continuation; VA 0x0114AADE; RVA 0x00D4AADE

```asm
0114AADE: lea      ecx, [esi + 0xaec]
0114AAE4: mov      eax, ecx
0114AAE6: and      eax, 0xf
0114AAE9: mov      dword ptr [ebp - 0x208], edi
0114AAEF: lea      edx, [ecx + 4]
0114AAF2: cmp      eax, 0xc
0114AAF5: ja       0x114ab41
0114AAF7: movzx    eax, byte ptr [eax + 0x114ae20]
0114AAFE: jmp      dword ptr [eax*4 + 0x114ae0c]
0114AB05: push     edx
0114AB06: lea      edx, [ebp - 0x208]
0114AB0C: push     edx
0114AB0D: call     0x27bbbe0
0114AB12: jmp      0x114ab4e
0114AB14: push     edx
0114AB15: lea      eax, [ebp - 0x208]
0114AB1B: push     eax
0114AB1C: call     0x27bbca0
0114AB21: jmp      0x114ab4e
0114AB23: push     edx
0114AB24: lea      edx, [ebp - 0x208]
0114AB2A: push     edx
0114AB2B: call     0x27bbd50
0114AB30: jmp      0x114ab4e
0114AB32: push     edx
0114AB33: lea      eax, [ebp - 0x208]
0114AB39: push     eax
0114AB3A: call     0x27bbe10
0114AB3F: jmp      0x114ab4e
0114AB41: push     edx
0114AB42: lea      edx, [ebp - 0x208]
0114AB48: push     edx
0114AB49: call     0x27bba60
0114AB4E: lea      ecx, [esi + 0xaf4]
0114AB54: mov      eax, ecx
0114AB56: and      eax, 0xf
0114AB59: mov      dword ptr [ebp - 0x208], edi
0114AB5F: lea      edx, [ecx + 4]
0114AB62: cmp      eax, 0xc
0114AB65: ja       0x114abb1
0114AB67: movzx    eax, byte ptr [eax + 0x114ae44]
0114AB6E: jmp      dword ptr [eax*4 + 0x114ae30]
0114AB75: push     edx
0114AB76: lea      edx, [ebp - 0x208]
0114AB7C: push     edx
0114AB7D: call     0x27bbbe0
0114AB82: jmp      0x114abbe
0114AB84: push     edx
0114AB85: lea      eax, [ebp - 0x208]
0114AB8B: push     eax
0114AB8C: call     0x27bbca0
0114AB91: jmp      0x114abbe
0114AB93: push     edx
0114AB94: lea      edx, [ebp - 0x208]
0114AB9A: push     edx
0114AB9B: call     0x27bbd50
0114ABA0: jmp      0x114abbe
0114ABA2: push     edx
0114ABA3: lea      eax, [ebp - 0x208]
0114ABA9: push     eax
0114ABAA: call     0x27bbe10
0114ABAF: jmp      0x114abbe
0114ABB1: push     edx
0114ABB2: lea      edx, [ebp - 0x208]
0114ABB8: push     edx
0114ABB9: call     0x27bba60
0114ABBE: lea      ecx, [esi + 0xafc]
0114ABC4: mov      eax, ecx
0114ABC6: and      eax, 0xf
0114ABC9: mov      dword ptr [ebp - 0x208], edi
0114ABCF: lea      edx, [ecx + 4]
0114ABD2: pop      edi
0114ABD3: cmp      eax, 0xc
0114ABD6: ja       0x114ac22
0114ABD8: movzx    eax, byte ptr [eax + 0x114ae68]
0114ABDF: jmp      dword ptr [eax*4 + 0x114ae54]
0114ABE6: push     edx
0114ABE7: lea      edx, [ebp - 0x208]
0114ABED: push     edx
0114ABEE: call     0x27bbbe0
0114ABF3: jmp      0x114ac2f
0114ABF5: push     edx
0114ABF6: lea      eax, [ebp - 0x208]
0114ABFC: push     eax
0114ABFD: call     0x27bbca0
0114AC02: jmp      0x114ac2f
0114AC04: push     edx
0114AC05: lea      edx, [ebp - 0x208]
0114AC0B: push     edx
0114AC0C: call     0x27bbd50
0114AC11: jmp      0x114ac2f
0114AC13: push     edx
0114AC14: lea      eax, [ebp - 0x208]
0114AC1A: push     eax
0114AC1B: call     0x27bbe10
0114AC20: jmp      0x114ac2f
0114AC22: push     edx
0114AC23: lea      edx, [ebp - 0x208]
0114AC29: push     edx
0114AC2A: call     0x27bba60
0114AC2F: mov      eax, 0x3a39164
0114AC34: and      eax, 0xf
0114AC37: mov      dword ptr [0x3a390c0], ebx
0114AC3D: mov      dword ptr [ebp - 0x208], ebx
0114AC43: cmp      eax, 0xc
0114AC46: ja       0x114acb6
0114AC48: movzx    eax, byte ptr [eax + 0x114ae8c]
0114AC4F: jmp      dword ptr [eax*4 + 0x114ae78]
0114AC56: push     0x3a39168
0114AC5B: lea      ecx, [ebp - 0x208]
0114AC61: push     ecx
0114AC62: mov      ecx, 0x3a39164
0114AC67: call     0x27bbbe0
0114AC6C: jmp      0x114accc
0114AC6E: push     0x3a39168
0114AC73: lea      edx, [ebp - 0x208]
0114AC79: push     edx
0114AC7A: mov      ecx, 0x3a39164
0114AC7F: call     0x27bbca0
0114AC84: jmp      0x114accc
0114AC86: push     0x3a39168
0114AC8B: lea      eax, [ebp - 0x208]
0114AC91: push     eax
0114AC92: mov      ecx, 0x3a39164
0114AC97: call     0x27bbd50
0114AC9C: jmp      0x114accc
0114AC9E: push     0x3a39168
0114ACA3: lea      ecx, [ebp - 0x208]
0114ACA9: push     ecx
0114ACAA: mov      ecx, 0x3a39164
0114ACAF: call     0x27bbe10
0114ACB4: jmp      0x114accc
0114ACB6: push     0x3a39168
0114ACBB: lea      edx, [ebp - 0x208]
0114ACC1: push     edx
0114ACC2: mov      ecx, 0x3a39164
0114ACC7: call     0x27bba60
0114ACCC: mov      ecx, dword ptr [0x3a5ad44]
0114ACD2: call     0x1946eb0
0114ACD7: mov      ecx, dword ptr [0x3a5ad44]
0114ACDD: push     1
0114ACDF: call     0x193f290
0114ACE4: lea      eax, [ebp - 0x20a]
0114ACEA: push     1
0114ACEC: push     eax
0114ACED: mov      byte ptr [ebp - 0x20a], bl
0114ACF3: call     0x27c5230 ; ReadBytes
0114ACF8: movzx    ecx, byte ptr [ebp - 0x20a]
0114ACFF: add      esp, 8
0114AD02: push     ecx
0114AD03: call     0x1102c30
0114AD08: mov      ecx, eax
0114AD0A: call     0x28d6d00
0114AD0F: mov      ecx, eax
0114AD11: call     0x2723150
0114AD16: call     0x164b880
0114AD1B: test     al, al
0114AD1D: je       0x114ad89
0114AD1F: mov      ecx, esi
0114AD21: call     0x171c570
0114AD26: cmp      byte ptr [eax + 0x308], bl
0114AD2C: jne      0x114ad36
0114AD2E: cmp      byte ptr [eax + 0x360], bl
0114AD34: je       0x114ad89
0114AD36: mov      ecx, dword ptr [0x3b3dfb8]
0114AD3C: call     0x1833e50
0114AD41: cmp      eax, 3
0114AD44: je       0x114ad89
0114AD46: mov      edx, 2
0114AD4B: sub      edx, eax
0114AD4D: push     edx
0114AD4E: push     0x11411
0114AD53: call     0x28c14c0
0114AD58: add      esp, 4
0114AD5B: push     eax
0114AD5C: lea      eax, [ebp - 0x204]
0114AD62: push     0x200
0114AD67: push     eax
0114AD68: call     0x442570
0114AD6D: add      esp, 0x10
0114AD70: lea      ecx, [ebp - 0x204]
0114AD76: push     ecx
0114AD77: mov      ecx, dword ptr [0x3a5c9b8]
0114AD7D: call     0xa9af50
0114AD82: mov      ecx, eax
0114AD84: call     0x1a62df0
0114AD89: mov      ecx, esi
0114AD8B: call     0x1726d30
0114AD90: lea      edx, [ebp - 0x209]
0114AD96: push     1
0114AD98: push     edx
0114AD99: mov      byte ptr [ebp - 0x209], 0xb
0114ADA0: call     0x27c5230 ; ReadBytes
0114ADA5: mov      ecx, dword ptr [0x3a5c990]
0114ADAB: add      esp, 8
0114ADAE: cmp      ecx, ebx
0114ADB0: je       0x114add5
0114ADB2: call     0x130de20
0114ADB7: test     eax, eax
0114ADB9: je       0x114add5
0114ADBB: movzx    ecx, byte ptr [ebp - 0x209]
0114ADC2: push     ecx
0114ADC3: mov      ecx, dword ptr [0x3a5c990]
0114ADC9: call     0x130de20
0114ADCE: mov      ecx, eax
0114ADD0: call     0x1587b00
0114ADD5: mov      ecx, dword ptr [ebp - 4]
0114ADD8: pop      esi
0114ADD9: xor      ecx, ebp
0114ADDB: pop      ebx
0114ADDC: call     0x2c627f3
0114ADE1: mov      esp, ebp
0114ADE3: pop      ebp
0114ADE4: ret
0114ADE5: lea      ecx, [ecx]
```

## NOTI 008F handler; VA 0x01159850; RVA 0x00D59850

```asm
01159850: push     ebp
01159851: mov      ebp, esp
01159853: push     -1
01159855: push     0x2d740d6
0115985A: mov      eax, dword ptr fs:[0]
01159860: push     eax
01159861: sub      esp, 0x88
01159867: push     ebx
01159868: push     esi
01159869: push     edi
0115986A: mov      eax, dword ptr [0x399cee0]
0115986F: xor      eax, ebp
01159871: push     eax
01159872: lea      eax, [ebp - 0xc]
01159875: mov      dword ptr fs:[0], eax
0115987B: call     0x1102c30
01159880: mov      ecx, eax
01159882: call     0x19fc7f0
01159887: mov      ecx, eax
01159889: call     0x26d8950
0115988E: movzx    eax, ax
01159891: lea      ecx, [ebp - 0x30]
01159894: xor      ebx, ebx
01159896: push     ecx
01159897: mov      dword ptr [ebp - 0x34], eax
0115989A: mov      dword ptr [ebp - 0x30], ebx
0115989D: mov      dword ptr [ebp - 0x3c], ebx
011598A0: call     0x27c52a0 ; ReadU16
011598A5: lea      edx, [ebp - 0x3c]
011598A8: push     edx
011598A9: call     0x27c5310 ; ReadU32
011598AE: mov      eax, dword ptr [ebp - 0x30]
011598B1: add      esp, 8
011598B4: push     eax
011598B5: call     0x1102c30
011598BA: mov      ecx, eax
011598BC: call     0x19fc7f0
011598C1: mov      ecx, eax
011598C3: call     0x26d7b50
011598C8: movzx    ecx, word ptr [ebp - 0x30]
011598CC: and      ecx, 0x80000001
011598D2: jns      0x11598d9
011598D4: dec      ecx
011598D5: or       ecx, 0xfffffffe
011598D8: inc      ecx
011598D9: push     9
011598DB: mov      byte ptr [ebp - 0x11], cl
011598DE: call     0x119b940
011598E3: mov      ecx, eax
011598E5: call     0x119b7c0
011598EA: mov      esi, eax
011598EC: mov      dword ptr [ebp - 0x38], esi
011598EF: cmp      esi, ebx
011598F1: je       0x1159dd0
011598F7: lea      edx, [ebp - 0x40]
011598FA: push     edx
011598FB: mov      byte ptr [ebp - 0x10], bl
011598FE: mov      dword ptr [ebp - 0x40], ebx
01159901: call     0x27c5310 ; ReadU32
01159906: lea      eax, [ebp - 0x10]
01159909: push     1
0115990B: push     eax
0115990C: call     0x27c5230 ; ReadBytes
01159911: movzx    eax, byte ptr [ebp - 0x11]
01159915: add      esp, 0xc
01159918: push     ebx
01159919: push     eax
0115991A: mov      ecx, esi
0115991C: call     0x1720580
01159921: mov      esi, eax
01159923: cmp      esi, ebx
01159925: je       0x1159e4c
0115992B: mov      ecx, esi
0115992D: call     0x172a040
01159932: mov      ecx, dword ptr [ebp - 0x40]
01159935: mov      dword ptr [esi], ecx
01159937: movzx    edx, byte ptr [ebp - 0x10]
0115993B: push     edx
0115993C: lea      ecx, [esi + 4]
0115993F: call     0x11543f0
01159944: mov      dword ptr [ebp - 0x1c], ebx
01159947: cmp      byte ptr [ebp - 0x10], bl
0115994A: jbe      0x1159a68
01159950: lea      eax, [ebp - 0x28]
01159953: push     eax
01159954: mov      dword ptr [ebp - 0x28], ebx
01159957: mov      byte ptr [ebp - 0xe], bl
0115995A: mov      byte ptr [ebp - 0xd], bl
0115995D: mov      dword ptr [ebp - 0x18], ebx
01159960: mov      byte ptr [ebp - 0x13], bl
01159963: mov      byte ptr [ebp - 0x12], bl
01159966: call     0x27c5310 ; ReadU32
0115996B: lea      ecx, [ebp - 0x24]
0115996E: push     ecx
0115996F: mov      dword ptr [ebp - 0x24], ebx
01159972: call     0x27c52a0 ; ReadU16
01159977: lea      edx, [ebp - 0x18]
0115997A: push     edx
0115997B: call     0x27c5310 ; ReadU32
01159980: lea      eax, [ebp - 0xe]
01159983: push     1
01159985: push     eax
01159986: call     0x27c5230 ; ReadBytes
0115998B: lea      ecx, [ebp - 0xd]
0115998E: push     1
01159990: push     ecx
01159991: call     0x27c5230 ; ReadBytes
01159996: lea      edx, [ebp - 0x13]
01159999: push     1
0115999B: push     edx
0115999C: call     0x27c5230 ; ReadBytes
011599A1: lea      eax, [ebp - 0x12]
011599A4: push     1
011599A6: push     eax
011599A7: call     0x27c5230 ; ReadBytes
011599AC: mov      ecx, dword ptr [esi]
011599AE: add      esp, 0x2c
011599B1: push     1
011599B3: push     ecx
011599B4: mov      ecx, dword ptr [0x3a5c91c]
011599BA: call     0x2752a80
011599BF: jmp      0x4f6a244
011599C4: push     0x64
011599C6: push     ebx
011599C7: jne      0x1159a01
011599C9: cmp      byte ptr [ebp - 0x13], bl
011599CC: movzx    edx, byte ptr [ebp - 0x12]
011599D0: push     1
011599D2: push     edx
011599D3: movzx    edx, byte ptr [ebp - 0xd]
011599D7: setne    al
011599DA: movzx    ecx, al
011599DD: movzx    eax, byte ptr [ebp - 0xe]
011599E1: push     ecx
011599E2: mov      ecx, dword ptr [ebp - 0x18]
011599E5: push     edx
011599E6: movzx    edx, word ptr [ebp - 0x24]
011599EA: push     eax
011599EB: mov      eax, dword ptr [ebp - 0x28]
011599EE: push     ecx
011599EF: push     edx
011599F0: push     eax
011599F1: lea      ecx, [ebp - 0x94]
011599F7: call     0x171ed10
011599FC: mov      dword ptr [ebp - 4], ebx
011599FF: jmp      0x1159a3a
01159A01: cmp      byte ptr [ebp - 0x13], bl
01159A04: movzx    ecx, byte ptr [ebp - 0x12]
01159A08: push     ebx
01159A09: push     ecx
01159A0A: movzx    ecx, byte ptr [ebp - 0xd]
01159A0E: setne    dl
01159A11: movzx    eax, dl
01159A14: movzx    edx, byte ptr [ebp - 0xe]
01159A18: push     eax
01159A19: mov      eax, dword ptr [ebp - 0x18]
01159A1C: push     ecx
01159A1D: movzx    ecx, word ptr [ebp - 0x24]
01159A21: push     edx
01159A22: mov      edx, dword ptr [ebp - 0x28]
01159A25: push     eax
01159A26: push     ecx
01159A27: push     edx
01159A28: lea      ecx, [ebp - 0x94]
01159A2E: call     0x171ed10
01159A33: mov      dword ptr [ebp - 4], 1
01159A3A: push     eax
01159A3B: lea      ecx, [esi + 4]
01159A3E: call     0x11579b0
01159A43: lea      ecx, [ebp - 0x90]
01159A49: mov      dword ptr [ebp - 4], 0xffffffff
01159A50: call     0x27bc0e0
01159A55: mov      eax, dword ptr [ebp - 0x1c]
01159A58: movzx    ecx, byte ptr [ebp - 0x10]
01159A5C: inc      eax
01159A5D: mov      dword ptr [ebp - 0x1c], eax
01159A60: cmp      eax, ecx
01159A62: jl       0x1159950
01159A68: lea      edx, [ebp - 0xf]
01159A6B: push     1
01159A6D: push     edx
01159A6E: mov      byte ptr [ebp - 0xf], bl
01159A71: call     0x27c5230 ; ReadBytes
01159A76: movzx    eax, byte ptr [ebp - 0xf]
01159A7A: add      esp, 8
01159A7D: lea      edi, [esi + 0x18]
01159A80: push     eax
01159A81: mov      ecx, edi
01159A83: call     0x1154520
01159A88: xor      esi, esi
01159A8A: cmp      byte ptr [ebp - 0xf], bl
01159A8D: jbe      0x1159b54
01159A93: lea      ecx, [ebp - 0xd]
01159A96: push     1
01159A98: push     ecx
01159A99: mov      byte ptr [ebp - 0xd], bl
01159A9C: mov      dword ptr [ebp - 0x44], ebx
01159A9F: mov      dword ptr [ebp - 0x18], ebx
01159AA2: mov      dword ptr [ebp - 0x2c], ebx
01159AA5: mov      byte ptr [ebp - 0xe], bl
01159AA8: mov      dword ptr [ebp - 0x24], ebx
01159AAB: mov      dword ptr [ebp - 0x28], ebx
01159AAE: call     0x27c5230 ; ReadBytes
01159AB3: lea      edx, [ebp - 0x20]
01159AB6: push     edx
01159AB7: mov      dword ptr [ebp - 0x20], ebx
01159ABA: call     0x27c52a0 ; ReadU16
01159ABF: lea      eax, [ebp - 0x2c]
01159AC2: push     eax
01159AC3: call     0x27c5310 ; ReadU32
01159AC8: lea      ecx, [ebp - 0x44]
01159ACB: push     ecx
01159ACC: call     0x27c5310 ; ReadU32
01159AD1: lea      edx, [ebp - 0x18]
01159AD4: push     edx
01159AD5: call     0x27c52a0 ; ReadU16
01159ADA: lea      eax, [ebp - 0xe]
01159ADD: push     1
01159ADF: push     eax
01159AE0: call     0x27c5230 ; ReadBytes
01159AE5: lea      ecx, [ebp - 0x24]
01159AE8: push     ecx
01159AE9: call     0x27c52a0 ; ReadU16
01159AEE: lea      edx, [ebp - 0x28]
01159AF1: push     edx
01159AF2: call     0x27c52a0 ; ReadU16
01159AF7: movzx    eax, word ptr [ebp - 0x28]
01159AFB: movzx    ecx, word ptr [ebp - 0x24]
01159AFF: movzx    edx, byte ptr [ebp - 0xe]
01159B03: add      esp, 0x28
01159B06: push     ebx
01159B07: push     eax
01159B08: movzx    eax, word ptr [ebp - 0x18]
01159B0C: push     ecx
01159B0D: mov      ecx, dword ptr [ebp - 0x44]
01159B10: push     edx
01159B11: mov      edx, dword ptr [ebp - 0x2c]
01159B14: push     eax
01159B15: movzx    eax, word ptr [ebp - 0x20]
01159B19: push     ecx
01159B1A: movzx    ecx, byte ptr [ebp - 0xd]
01159B1E: push     edx
01159B1F: push     eax
01159B20: push     ecx
01159B21: lea      ecx, [ebp - 0x78]
01159B24: call     0x171eec0
01159B29: push     eax
01159B2A: mov      ecx, edi
01159B2C: mov      dword ptr [ebp - 4], 2
01159B33: call     0x1157a70
01159B38: lea      ecx, [ebp - 0x74]
01159B3B: mov      dword ptr [ebp - 4], 0xffffffff
01159B42: call     0x27bc0e0
01159B47: movzx    edx, byte ptr [ebp - 0xf]
01159B4B: inc      esi
01159B4C: cmp      esi, edx
01159B4E: jl       0x1159a93
01159B54: mov      eax, dword ptr [ebp - 0x3c]
01159B57: mov      esi, dword ptr [ebp - 0x38]
01159B5A: push     eax
01159B5B: mov      ecx, esi
01159B5D: call     0x171c680
01159B62: push     esi
01159B63: call     0x279bce0
01159B68: add      esp, 4
01159B6B: mov      ecx, esi
01159B6D: call     0x171c570
01159B72: mov      ecx, eax
01159B74: call     0x28a2f20
01159B79: sub      esp, 0x20
01159B7C: mov      esi, esp
01159B7E: mov      dword ptr [ebp - 0x20], esp
01159B81: push     8
01159B83: mov      edi, eax
01159B85: mov      dword ptr [ebp - 0x2c], esi
01159B88: mov      dword ptr [esi], ebx
01159B8A: call     0x423460
01159B8F: add      esp, 4
01159B92: cmp      eax, ebx
01159B94: je       0x1159e5e
01159B9A: mov      dword ptr [esi], eax
01159B9C: xor      ecx, ecx
01159B9E: mov      dword ptr [eax], ecx
01159BA0: mov      dword ptr [eax + 4], ebx
01159BA3: mov      ecx, dword ptr [esi]
01159BA5: mov      dword ptr [ecx], esi
01159BA7: xor      edx, edx
01159BA9: mov      dword ptr [esi + 0x18], 7
01159BB0: mov      dword ptr [esi + 0x14], ebx
01159BB3: mov      eax, edi
01159BB5: mov      word ptr [esi + 4], dx
01159BB9: mov      dword ptr [ebp - 4], 3
01159BC0: lea      edx, [eax + 2]
01159BC3: mov      cx, word ptr [eax]
01159BC6: add      eax, 2
01159BC9: cmp      cx, bx
01159BCC: jne      0x1159bc3
01159BCE: sub      eax, edx
01159BD0: sar      eax, 1
01159BD2: push     eax
01159BD3: push     edi
01159BD4: mov      ecx, esi
01159BD6: call     0x42acb0
01159BDB: mov      dword ptr [ebp - 4], 0xffffffff
01159BE2: call     0x10608a0
01159BE7: add      esp, 0x20
01159BEA: call     0x1102c30
01159BEF: mov      ecx, eax
01159BF1: call     0x163e1e0
01159BF6: cmp      eax, 2
01159BF9: jne      0x1159ce4
01159BFF: mov      edi, dword ptr [ebp - 0x38]
01159C02: movzx    esi, byte ptr [ebp - 0x11]
01159C06: lea      ecx, [edi + 0xad4]
01159C0C: mov      eax, ecx
01159C0E: and      eax, 0xf
01159C11: mov      dword ptr [ebp - 0x18], esi
01159C14: lea      edx, [ecx + 4]
01159C17: cmp      eax, 0xc
01159C1A: ja       0x1159c5a
01159C1C: movzx    eax, byte ptr [eax + 0x1159e98]
01159C23: jmp      dword ptr [eax*4 + 0x1159e84]
01159C2A: push     edx
01159C2B: lea      edx, [ebp - 0x18]
01159C2E: push     edx
01159C2F: call     0x27bbbe0
01159C34: jmp      0x1159c64
01159C36: push     edx
01159C37: lea      eax, [ebp - 0x18]
01159C3A: push     eax
01159C3B: call     0x27bbca0
01159C40: jmp      0x1159c64
01159C42: push     edx
01159C43: lea      edx, [ebp - 0x18]
01159C46: push     edx
01159C47: call     0x27bbd50
01159C4C: jmp      0x1159c64
01159C4E: push     edx
01159C4F: lea      eax, [ebp - 0x18]
01159C52: push     eax
01159C53: call     0x27bbe10
01159C58: jmp      0x1159c64
01159C5A: push     edx
01159C5B: lea      edx, [ebp - 0x18]
01159C5E: push     edx
01159C5F: call     0x27bba60
01159C64: lea      ecx, [edi + 0xadc]
01159C6A: mov      eax, ecx
01159C6C: and      eax, 0xf
01159C6F: mov      dword ptr [ebp - 0x1c], ebx
01159C72: lea      edx, [ecx + 4]
01159C75: cmp      eax, 0xc
01159C78: ja       0x1159cb8
01159C7A: movzx    eax, byte ptr [eax + 0x1159ebc]
01159C81: jmp      dword ptr [eax*4 + 0x1159ea8]
01159C88: push     edx
01159C89: lea      edx, [ebp - 0x1c]
01159C8C: push     edx
01159C8D: call     0x27bbbe0
01159C92: jmp      0x1159cc2
01159C94: push     edx
01159C95: lea      eax, [ebp - 0x1c]
01159C98: push     eax
01159C99: call     0x27bbca0
01159C9E: jmp      0x1159cc2
01159CA0: push     edx
01159CA1: lea      edx, [ebp - 0x1c]
01159CA4: push     edx
01159CA5: call     0x27bbd50
01159CAA: jmp      0x1159cc2
01159CAC: push     edx
01159CAD: lea      eax, [ebp - 0x1c]
01159CB0: push     eax
01159CB1: call     0x27bbe10
01159CB6: jmp      0x1159cc2
01159CB8: push     edx
01159CB9: lea      edx, [ebp - 0x1c]
01159CBC: push     edx
01159CBD: call     0x27bba60
01159CC2: push     ebx
01159CC3: push     esi
01159CC4: mov      ecx, edi
01159CC6: call     0x171c570
01159CCB: mov      eax, dword ptr [eax + 0x34]
01159CCE: push     eax
01159CCF: call     0x1102c30
01159CD4: mov      ecx, eax
01159CD6: call     0x28d6d00
01159CDB: mov      ecx, eax
01159CDD: call     0x575b1d8
01159CE2: jmp      0x1159d02
01159CE4: call     0x1102c30
01159CE9: mov      ecx, eax
01159CEB: call     0x19fc7f0
01159CF0: movzx    ecx, byte ptr [ebp - 0x11]
01159CF4: mov      edx, dword ptr [eax]
01159CF6: push     ebx
01159CF7: push     ecx
01159CF8: mov      ecx, eax
01159CFA: mov      eax, dword ptr [edx + 0xa8]
01159D00: call     eax
01159D02: mov      cx, word ptr [ebp - 0x34]
01159D06: cmp      cx, word ptr [ebp - 0x30]
01159D0A: je       0x1159d8e
01159D10: xor      eax, eax
01159D12: mov      dword ptr [ebp - 0x20], eax
01159D15: jmp      0x1159d20
01159D17: lea      esp, [esp]
01159D1E: mov      edi, edi
01159D20: mov      ecx, dword ptr [0x3a5c990]
01159D26: push     eax
01159D27: call     0x19e7770
01159D2C: cmp      eax, ebx
01159D2E: je       0x1159d82
01159D30: mov      edx, dword ptr [eax]
01159D32: mov      ecx, eax
01159D34: mov      eax, dword ptr [edx + 0x6a0]
01159D3A: call     eax
01159D3C: mov      esi, eax
01159D3E: mov      dword ptr [ebp - 0x34], esi
01159D41: cmp      esi, ebx
01159D43: je       0x1159d82
01159D45: mov      ecx, esi
01159D47: call     0x2394660
01159D4C: xor      edi, edi
01159D4E: mov      dword ptr [ebp - 0x2c], eax
01159D51: cmp      eax, ebx
01159D53: jle      0x1159d82
01159D55: jmp      0x1159d5a
01159D57: mov      esi, dword ptr [ebp - 0x34]
01159D5A: push     edi
01159D5B: mov      ecx, esi
01159D5D: call     0x1a01970
01159D62: mov      esi, eax
01159D64: cmp      esi, ebx
01159D66: je       0x1159d7c
01159D68: push     1
01159D6A: mov      ecx, esi
01159D6C: call     0x19ff780
01159D71: test     al, al
01159D73: je       0x1159d7c
01159D75: mov      ecx, esi
01159D77: call     0xad9bd0
01159D7C: inc      edi
01159D7D: cmp      edi, dword ptr [ebp - 0x2c]
01159D80: jl       0x1159d57
01159D82: mov      eax, dword ptr [ebp - 0x20]
01159D85: inc      eax
01159D86: mov      dword ptr [ebp - 0x20], eax
01159D89: cmp      eax, 8
01159D8C: jl       0x1159d20
01159D8E: mov      ecx, dword ptr [0x3a5c9b8]
01159D94: push     ebx
01159D95: push     3
01159D97: call     0x2301e60
01159D9C: test     eax, eax
01159D9E: jne      0x1159dd0
01159DA0: mov      esi, dword ptr [ebp - 0x38]
01159DA3: mov      ecx, esi
01159DA5: call     0x171c990
01159DAA: test     al, al
01159DAC: jne      0x1159dd0
01159DAE: mov      ecx, esi
01159DB0: call     0x171c570
01159DB5: mov      ecx, dword ptr [eax + 0x34]
01159DB8: push     ecx
01159DB9: mov      ecx, dword ptr [0x3a5c9b8]
01159DBF: push     ebx
01159DC0: push     3
01159DC2: call     0x2304c70
01159DC7: push     1
01159DC9: mov      ecx, esi
01159DCB: call     0x171c9a0
01159DD0: mov      ecx, dword ptr [0x3a5c980]
01159DD6: call     0x19e3290
01159DDB: mov      edi, eax
01159DDD: call     0x1102c30
01159DE2: test     eax, eax
01159DE4: je       0x1159df9
01159DE6: push     1
01159DE8: call     0x1102c30
01159DED: mov      ecx, eax
01159DEF: call     0x1102c80
01159DF4: cmp      eax, 1
01159DF7: je       0x1159e0c
01159DF9: push     3
01159DFB: call     0x1102c30
01159E00: mov      ecx, eax
01159E02: call     0x1102c80
01159E07: cmp      eax, 3
01159E0A: jne      0x1159e4c
01159E0C: cmp      edi, ebx
01159E0E: je       0x1159e4c
01159E10: mov      ecx, edi
01159E12: call     0x19dc390
01159E17: test     eax, eax
01159E19: jle      0x1159e4c
01159E1B: push     edi
01159E1C: call     0x1102c30
01159E21: mov      ecx, eax
01159E23: call     0x1103400
01159E28: test     al, al
01159E2A: je       0x1159e4c
01159E2C: mov      ecx, edi
01159E2E: call     0x19dc390
01159E33: mov      ecx, edi
01159E35: mov      esi, eax
01159E37: call     0x1985f30
01159E3C: cmp      eax, ebx
01159E3E: je       0x1159e4c
01159E40: push     1
01159E42: push     ebx
01159E43: push     esi
01159E44: push     esi
01159E45: mov      ecx, eax
01159E47: call     0x18469b0
01159E4C: mov      ecx, dword ptr [ebp - 0xc]
01159E4F: mov      dword ptr fs:[0], ecx
01159E56: pop      ecx
01159E57: pop      edi
01159E58: pop      esi
01159E59: pop      ebx
01159E5A: mov      esp, ebp
01159E5C: pop      ebp
01159E5D: ret
```

## NOTI 0090 handler; VA 0x01153A40; RVA 0x00D53A40

```asm
01153A40: push     ebp
01153A41: mov      ebp, esp
01153A43: sub      esp, 0x22c
01153A49: mov      eax, dword ptr [0x399cee0]
01153A4E: xor      eax, ebp
01153A50: mov      dword ptr [ebp - 4], eax
01153A53: mov      ecx, dword ptr [0x3a5c9b8]
01153A59: push     ebx
01153A5A: xor      ebx, ebx
01153A5C: cmp      ecx, ebx
01153A5E: je       0x1153f1e
01153A64: call     0x2300c20
01153A69: test     eax, eax
01153A6B: je       0x1153f1e
01153A71: mov      ecx, dword ptr [0x3a5c9b8]
01153A77: call     0x2300c10
01153A7C: test     eax, eax
01153A7E: je       0x1153f1e
01153A84: mov      ecx, dword ptr [0x3a5c9b8]
01153A8A: push     ebx
01153A8B: call     0x2300c20
01153A90: mov      ecx, eax
01153A92: call     0x261dc40
01153A97: mov      ecx, dword ptr [0x3a5c9b8]
01153A9D: push     ebx
01153A9E: call     0x2300c10
01153AA3: mov      ecx, eax
01153AA5: call     0x2619c00
01153AAA: call     0x1102c30
01153AAF: mov      ecx, eax
01153AB1: call     0x163e3d0
01153AB6: test     eax, eax
01153AB8: je       0x1153acd
01153ABA: call     0x1102c30
01153ABF: mov      ecx, eax
01153AC1: call     0x163e3d0
01153AC6: mov      ecx, eax
01153AC8: call     0x198c750
01153ACD: push     esi
01153ACE: push     edi
01153ACF: lea      eax, [ebp - 0x208]
01153AD5: push     1
01153AD7: push     eax
01153AD8: mov      byte ptr [ebp - 0x208], bl
01153ADE: call     0x27c5230 ; ReadBytes
01153AE3: lea      ecx, [ebp - 0x220]
01153AE9: push     ecx
01153AEA: mov      dword ptr [ebp - 0x220], ebx
01153AF0: mov      dword ptr [ebp - 0x228], 1
01153AFA: mov      dword ptr [ebp - 0x224], ebx
01153B00: call     0x27c5310 ; ReadU32
01153B05: lea      edx, [ebp - 0x228]
01153B0B: push     edx
01153B0C: call     0x27c5310 ; ReadU32
01153B11: lea      eax, [ebp - 0x209]
01153B17: push     1
01153B19: push     eax
01153B1A: mov      byte ptr [ebp - 0x209], bl
01153B20: mov      byte ptr [ebp - 0x207], bl
01153B26: call     0x27c5230 ; ReadBytes
01153B2B: lea      ecx, [ebp - 0x224]
01153B31: push     ecx
01153B32: call     0x27c5310 ; ReadU32
01153B37: lea      edx, [ebp - 0x207]
01153B3D: push     1
01153B3F: push     edx
01153B40: call     0x27c5230 ; ReadBytes
01153B45: mov      edx, dword ptr [ebp - 0x224]
01153B4B: add      esp, 0x24
01153B4E: cmp      byte ptr [ebp - 0x208], bl
01153B54: setne    al
01153B57: cmp      byte ptr [ebp - 0x209], bl
01153B5D: movzx    ecx, al
01153B60: mov      eax, dword ptr [ebp - 0x220]
01153B66: push     ecx
01153B67: mov      ecx, dword ptr [ebp - 0x228]
01153B6D: push     edx
01153B6E: push     eax
01153B6F: setne    dl
01153B72: cmp      byte ptr [ebp - 0x207], bl
01153B78: push     ecx
01153B79: movzx    eax, dl
01153B7C: setne    cl
01153B7F: movzx    edx, cl
01153B82: mov      ecx, dword ptr [0x3a5c9b8]
01153B88: push     eax
01153B89: push     edx
01153B8A: call     0x2300c10
01153B8F: mov      ecx, eax
01153B91: call     0x2619480
01153B96: cmp      byte ptr [ebp - 0x207], bl
01153B9C: je       0x1153d23
01153BA2: xor      edi, edi
01153BA4: push     ebx
01153BA5: lea      eax, [ebp - 0x204]
01153BAB: push     0x100
01153BB0: push     eax
01153BB1: call     0x274fcb0
01153BB6: lea      ecx, [ebp - 0x205]
01153BBC: push     1
01153BBE: push     ecx
01153BBF: call     0x27c5230 ; ReadBytes
01153BC4: lea      edx, [ebp - 0x206]
01153BCA: push     1
01153BCC: push     edx
01153BCD: call     0x27c5230 ; ReadBytes
01153BD2: lea      eax, [ebp - 0x204]
01153BD8: add      esp, 0x1c
01153BDB: lea      edx, [eax + 2]
01153BDE: mov      edi, edi
01153BE0: mov      cx, word ptr [eax]
01153BE3: add      eax, 2
01153BE6: cmp      cx, bx
01153BE9: jne      0x1153be0
01153BEB: sub      eax, edx
01153BED: sar      eax, 1
01153BEF: je       0x1153c86
01153BF5: mov      ecx, dword ptr [0x3a5c9b8]
01153BFB: push     edi
01153BFC: call     0x2300c10
01153C01: mov      ecx, eax
01153C03: call     0x2619420
01153C08: test     eax, eax
01153C0A: je       0x1153c86
01153C0C: mov      ecx, dword ptr [0x3a5c9b8]
01153C12: push     edi
01153C13: call     0x2300c10
01153C18: mov      ecx, eax
01153C1A: call     0x2619420
01153C1F: lea      edx, [ebp - 0x204]
01153C25: lea      ecx, [edx + 2]
01153C28: jmp      0x1153c30
01153C2A: lea      ebx, [ebx]
01153C30: mov      si, word ptr [edx]
01153C33: add      edx, 2
01153C36: cmp      si, bx
01153C39: jne      0x1153c30
01153C3B: sub      edx, ecx
01153C3D: sar      edx, 1
01153C3F: push     edx
01153C40: lea      ecx, [ebp - 0x204]
01153C46: push     ecx
01153C47: mov      ecx, eax
01153C49: call     0x42acb0
01153C4E: mov      ecx, dword ptr [0x3a5c9b8]
01153C54: push     edi
01153C55: call     0x2300c10
01153C5A: mov      ecx, eax
01153C5C: call     0x2619420
01153C61: mov      dl, byte ptr [ebp - 0x205]
01153C67: mov      byte ptr [eax + 0x20], dl
01153C6A: mov      ecx, dword ptr [0x3a5c9b8]
01153C70: push     edi
01153C71: call     0x2300c10
01153C76: mov      ecx, eax
01153C78: call     0x2619420
01153C7D: mov      cl, byte ptr [ebp - 0x206]
01153C83: mov      byte ptr [eax + 0x21], cl
01153C86: inc      edi
01153C87: cmp      edi, 8
01153C8A: jl       0x1153ba4
01153C90: lea      edx, [ebp - 0x210]
01153C96: push     edx
01153C97: call     0x27c52a0 ; ReadU16
01153C9C: lea      eax, [ebp - 0x214]
01153CA2: push     eax
01153CA3: call     0x27c5310 ; ReadU32
01153CA8: lea      ecx, [ebp - 0x218]
01153CAE: push     ecx
01153CAF: call     0x27c5310 ; ReadU32
01153CB4: mov      ecx, dword ptr [0x3a5c9b8]
01153CBA: add      esp, 0xc
01153CBD: push     ebx
01153CBE: call     0x2300c10
01153CC3: mov      ecx, eax
01153CC5: call     0x2619420
01153CCA: test     eax, eax
01153CCC: je       0x1153d23
01153CCE: mov      ecx, dword ptr [0x3a5c9b8]
01153CD4: movzx    esi, word ptr [ebp - 0x210]
01153CDB: push     ebx
01153CDC: call     0x2300c10
01153CE1: mov      ecx, eax
01153CE3: call     0x2619420
01153CE8: mov      dword ptr [eax + 0x24], esi
01153CEB: mov      ecx, dword ptr [0x3a5c9b8]
01153CF1: push     ebx
01153CF2: call     0x2300c10
01153CF7: mov      ecx, eax
01153CF9: call     0x2619420
01153CFE: mov      edx, dword ptr [ebp - 0x214]
01153D04: mov      dword ptr [eax + 0x28], edx
01153D07: mov      ecx, dword ptr [0x3a5c9b8]
01153D0D: push     ebx
01153D0E: call     0x2300c10
01153D13: mov      ecx, eax
01153D15: call     0x2619420
01153D1A: mov      ecx, dword ptr [ebp - 0x218]
01153D20: mov      dword ptr [eax + 0x2c], ecx
01153D23: mov      dword ptr [ebp - 0x21c], ebx
01153D29: xor      esi, esi
01153D2B: jmp      0x1153d30
01153D2D: lea      ecx, [ecx]
01153D30: xor      ebx, ebx
01153D32: push     0
01153D34: lea      edx, [ebp - 0x204]
01153D3A: push     0x100
01153D3F: push     edx
01153D40: call     0x274fcb0
01153D45: lea      eax, [ebp - 0x205]
01153D4B: push     1
01153D4D: push     eax
01153D4E: call     0x27c5230 ; ReadBytes
01153D53: lea      ecx, [ebp - 0x206]
01153D59: push     1
01153D5B: push     ecx
01153D5C: call     0x27c5230 ; ReadBytes
01153D61: lea      eax, [ebp - 0x204]
01153D67: add      esp, 0x1c
01153D6A: lea      edx, [eax + 2]
01153D6D: lea      ecx, [ecx]
01153D70: mov      cx, word ptr [eax]
01153D73: add      eax, 2
01153D76: test     cx, cx
01153D79: jne      0x1153d70
01153D7B: sub      eax, edx
01153D7D: sar      eax, 1
01153D7F: je       0x1153e22
01153D85: mov      ecx, dword ptr [0x3a5c9b8]
01153D8B: push     ebx
01153D8C: push     esi
01153D8D: call     0x2300c10
01153D92: mov      ecx, eax
01153D94: call     0x2619440
01153D99: test     eax, eax
01153D9B: je       0x1153e22
01153DA1: mov      ecx, dword ptr [0x3a5c9b8]
01153DA7: push     ebx
01153DA8: push     esi
01153DA9: call     0x2300c10
01153DAE: mov      ecx, eax
01153DB0: call     0x2619440
01153DB5: lea      edx, [ebp - 0x204]
01153DBB: lea      ecx, [edx + 2]
01153DBE: mov      edi, edi
01153DC0: mov      di, word ptr [edx]
01153DC3: add      edx, 2
01153DC6: test     di, di
01153DC9: jne      0x1153dc0
01153DCB: sub      edx, ecx
01153DCD: sar      edx, 1
01153DCF: push     edx
01153DD0: lea      edx, [ebp - 0x204]
01153DD6: push     edx
01153DD7: mov      ecx, eax
01153DD9: call     0x42acb0
01153DDE: mov      ecx, dword ptr [0x3a5c9b8]
01153DE4: push     ebx
01153DE5: push     esi
01153DE6: call     0x2300c10
01153DEB: mov      ecx, eax
01153DED: call     0x2619440
01153DF2: mov      cl, byte ptr [ebp - 0x205]
01153DF8: push     ebx
01153DF9: mov      byte ptr [eax + 0x20], cl
01153DFC: mov      ecx, dword ptr [0x3a5c9b8]
01153E02: push     esi
01153E03: call     0x2300c10
01153E08: mov      ecx, eax
01153E0A: call     0x2619440
01153E0F: mov      dl, byte ptr [ebp - 0x206]
01153E15: mov      byte ptr [eax + 0x21], dl
01153E18: test     esi, esi
01153E1A: jne      0x1153e22
01153E1C: inc      dword ptr [ebp - 0x21c]
01153E22: inc      ebx
01153E23: cmp      ebx, 8
01153E26: jl       0x1153d32
01153E2C: lea      eax, [ebp - 0x210]
01153E32: push     eax
01153E33: call     0x27c52a0 ; ReadU16
01153E38: lea      ecx, [ebp - 0x214]
01153E3E: push     ecx
01153E3F: call     0x27c5310 ; ReadU32
01153E44: lea      edx, [ebp - 0x218]
01153E4A: push     edx
01153E4B: call     0x27c5310 ; ReadU32
01153E50: mov      ecx, dword ptr [0x3a5c9b8]
01153E56: add      esp, 0xc
01153E59: push     0
01153E5B: push     esi
01153E5C: call     0x2300c10
01153E61: mov      ecx, eax
01153E63: call     0x2619440
01153E68: test     eax, eax
01153E6A: je       0x1153ec7
01153E6C: mov      ecx, dword ptr [0x3a5c9b8]
01153E72: movzx    edi, word ptr [ebp - 0x210]
01153E79: push     0
01153E7B: push     esi
01153E7C: call     0x2300c10
01153E81: mov      ecx, eax
01153E83: call     0x2619440
01153E88: push     0
01153E8A: mov      dword ptr [eax + 0x24], edi
01153E8D: mov      ecx, dword ptr [0x3a5c9b8]
01153E93: push     esi
01153E94: call     0x2300c10
01153E99: mov      ecx, eax
01153E9B: call     0x2619440
01153EA0: mov      ecx, dword ptr [ebp - 0x214]
01153EA6: push     0
01153EA8: mov      dword ptr [eax + 0x28], ecx
01153EAB: mov      ecx, dword ptr [0x3a5c9b8]
01153EB1: push     esi
01153EB2: call     0x2300c10
01153EB7: mov      ecx, eax
01153EB9: call     0x2619440
01153EBE: mov      edx, dword ptr [ebp - 0x218]
01153EC4: mov      dword ptr [eax + 0x2c], edx
01153EC7: cmp      word ptr [ebp - 0x210], 0x64
01153ECF: jb       0x1153ee5
01153ED1: mov      ecx, dword ptr [0x3a5c9b8]
01153ED7: push     1
01153ED9: call     0x2300c10
01153EDE: mov      ecx, eax
01153EE0: call     0x2619460
01153EE5: inc      esi
01153EE6: cmp      esi, 5
01153EE9: jl       0x1153d30
01153EEF: mov      eax, dword ptr [ebp - 0x21c]
01153EF5: mov      ecx, dword ptr [0x3a5c9b8]
01153EFB: push     eax
01153EFC: call     0x2300c10
01153F01: mov      ecx, eax
01153F03: call     0x2619470
01153F08: mov      ecx, dword ptr [0x3a5c9b8]
01153F0E: push     1
01153F10: call     0x2300c10
01153F15: mov      ecx, eax
01153F17: call     0x2619c00
01153F1C: pop      edi
01153F1D: pop      esi
01153F1E: mov      ecx, dword ptr [ebp - 4]
01153F21: xor      ecx, ebp
01153F23: pop      ebx
01153F24: call     0x2c627f3
01153F29: mov      esp, ebp
01153F2B: pop      ebp
01153F2C: ret
```

## NOTI 0091 handler; VA 0x01148850; RVA 0x00D48850

```asm
01148850: push     ebp
01148851: mov      ebp, esp
01148853: mov      ecx, dword ptr [0x3a5c9b8]
01148859: sub      esp, 0x14
0114885C: push     edi
0114885D: xor      edi, edi
0114885F: cmp      ecx, edi
01148861: je       0x114899a
01148867: call     0x2300c20
0114886C: test     eax, eax
0114886E: je       0x114899a
01148874: push     ebx
01148875: lea      eax, [ebp - 0x14]
01148878: push     esi
01148879: push     eax
0114887A: mov      dword ptr [ebp - 0x14], edi
0114887D: call     0x27c5310 ; ReadU32
01148882: add      esp, 4
01148885: mov      byte ptr [ebp - 1], 0
01148889: mov      dword ptr [ebp - 0x10], edi
0114888C: mov      dword ptr [ebp - 0xc], edi
0114888F: mov      dword ptr [ebp - 8], edi
01148892: xor      esi, esi
01148894: jmp      0x1148898
01148896: xor      edi, edi
01148898: mov      ecx, dword ptr [0x3a5c990]
0114889E: push     esi
0114889F: call     0x19e7750
011488A4: test     eax, eax
011488A6: setne    bl
011488A9: test     bl, bl
011488AB: je       0x11488cb
011488AD: mov      ecx, dword ptr [0x3a5c9b8]
011488B3: push     esi
011488B4: push     esi
011488B5: call     0x2300c20
011488BA: mov      ecx, eax
011488BC: call     0x10c91d0
011488C1: mov      ecx, eax
011488C3: call     0x10c91c0
011488C8: inc      dword ptr [ebp - 8]
011488CB: lea      ecx, [ebp - 1]
011488CE: push     1
011488D0: push     ecx
011488D1: call     0x27c5230 ; ReadBytes
011488D6: add      esp, 8
011488D9: cmp      byte ptr [ebp - 1], 0
011488DD: jbe      0x114892c
011488DF: nop
011488E0: lea      edx, [ebp - 0x10]
011488E3: push     edx
011488E4: call     0x27c5310 ; ReadU32
011488E9: lea      eax, [ebp - 0xc]
011488EC: push     eax
011488ED: call     0x27c5310 ; ReadU32
011488F2: add      esp, 8
011488F5: test     bl, bl
011488F7: je       0x1148923
011488F9: mov      ecx, dword ptr [ebp - 0xc]
011488FC: mov      edx, dword ptr [ebp - 0x10]
011488FF: push     ecx
01148900: mov      ecx, dword ptr [0x3a5c9b8]
01148906: push     edx
01148907: push     edi
01148908: push     esi
01148909: call     0x2300c20
0114890E: mov      ecx, eax
01148910: call     0x10c91d0
01148915: mov      ecx, eax
01148917: call     0x261c0f0
0114891C: mov      ecx, eax
0114891E: call     0x261fa20
01148923: movzx    eax, byte ptr [ebp - 1]
01148927: inc      edi
01148928: cmp      edi, eax
0114892A: jl       0x11488e0
0114892C: inc      esi
0114892D: cmp      esi, 4
01148930: jl       0x1148896
01148936: mov      ecx, dword ptr [ebp - 0x14]
01148939: push     ecx
0114893A: mov      ecx, dword ptr [0x3a5c9b8]
01148940: call     0x2300c20
01148945: mov      ecx, eax
01148947: call     0x261be30
0114894C: mov      edx, dword ptr [ebp - 8]
0114894F: mov      ecx, dword ptr [0x3a5c9b8]
01148955: push     edx
01148956: call     0x2300c20
0114895B: mov      ecx, eax
0114895D: call     0xcb7040
01148962: mov      eax, dword ptr [0x3a5c9b0]
01148967: push     eax
01148968: call     0x119b940
0114896D: mov      ecx, eax
0114896F: call     0x119b2e0
01148974: push     1
01148976: push     1
01148978: mov      esi, eax
0114897A: call     0x10cbf20
0114897F: push     eax
01148980: mov      ecx, esi
01148982: call     0x171c570
01148987: mov      ecx, dword ptr [eax + 0x34]
0114898A: push     ecx
0114898B: mov      ecx, dword ptr [0x3a5c9ec]
01148991: push     1
01148993: call     0xe75b80
01148998: pop      esi
01148999: pop      ebx
0114899A: pop      edi
0114899B: mov      esp, ebp
0114899D: pop      ebp
0114899E: ret
```

## NOTI 0092 handler; VA 0x01146DE0; RVA 0x00D46DE0

```asm
01146DE0: push     ebp
01146DE1: mov      ebp, esp
01146DE3: push     ecx
01146DE4: mov      ecx, dword ptr [0x3a5c9b8]
01146DEA: test     ecx, ecx
01146DEC: je       0x1146e27
01146DEE: call     0x2300c20
01146DF3: test     eax, eax
01146DF5: je       0x1146e27
01146DF7: lea      eax, [ebp - 1]
01146DFA: push     1
01146DFC: push     eax
01146DFD: mov      byte ptr [ebp - 1], 0
01146E01: call     0x27c5230 ; ReadBytes
01146E06: mov      ecx, dword ptr [0x3a5c9b8]
01146E0C: add      esp, 8
01146E0F: cmp      byte ptr [ebp - 1], 0
01146E13: je       0x1146e19
01146E15: push     9
01146E17: jmp      0x1146e1b
01146E19: push     5
01146E1B: call     0x2300c20
01146E20: mov      ecx, eax
01146E22: call     0x10c9200
01146E27: call     0x105d020
01146E2C: mov      esp, ebp
01146E2E: pop      ebp
01146E2F: ret
```

## ReadU16; VA 0x027C52A0; RVA 0x023C52A0

```asm
027C52A0: push     ebp
027C52A1: mov      ebp, esp
027C52A3: mov      eax, dword ptr [0x3baae38]
027C52A8: mov      cx, word ptr [eax]
027C52AB: mov      edx, dword ptr [ebp + 8]
027C52AE: add      eax, 2
027C52B1: sub      dword ptr [0x3baae3c], 2
027C52B8: mov      word ptr [edx], cx
027C52BB: mov      dword ptr [0x3baae38], eax
```

## ReadU32; VA 0x027C5310; RVA 0x023C5310

```asm
027C5310: push     ebp
027C5311: mov      ebp, esp
027C5313: mov      eax, dword ptr [0x3baae38]
027C5318: mov      ecx, dword ptr [eax]
027C531A: mov      edx, dword ptr [ebp + 8]
027C531D: add      eax, 4
027C5320: sub      dword ptr [0x3baae3c], 4
027C5327: mov      dword ptr [edx], ecx
027C5329: mov      dword ptr [0x3baae38], eax
```

## CMD 009F start sender block; VA 0x026D7556; RVA 0x022D7556

```asm
026D7556: push     0
026D7558: push     0x9f
026D755D: call     0x274ca50
026D7562: mov      ecx, eax
026D7564: call     0x274d1e0 ; BeginCommand
026D7569: push     1
026D756B: call     0x274ca50
026D7570: mov      ecx, eax
026D7572: call     0x274d2e0 ; WriteU8
026D7577: call     0x274e6d0 ; Send
```

## CMD 009F clear sender block; VA 0x026D8AFF; RVA 0x022D8AFF

```asm
026D8AFF: push     0
026D8B01: push     0x9f
026D8B06: call     0x274ca50
026D8B0B: mov      ecx, eax
026D8B0D: call     0x274d1e0 ; BeginCommand
026D8B12: push     2
026D8B14: call     0x274ca50
026D8B19: mov      ecx, eax
026D8B1B: call     0x274d2e0 ; WriteU8
026D8B20: xor      esi, esi
026D8B22: mov      ecx, dword ptr [0x3a5ad44]
026D8B28: push     esi
026D8B29: call     0x193f330
026D8B2E: lea      ecx, [eax + 0xc0]
026D8B34: mov      eax, ecx
026D8B36: and      eax, 0xf
026D8B39: lea      edx, [ecx + 4]
026D8B3C: cmp      eax, 0xc
026D8B3F: ja       0x26d8b7f
026D8B41: movzx    eax, byte ptr [eax + 0x26d8bc8]
026D8B48: jmp      dword ptr [eax*4 + 0x26d8bb4]
026D8B4F: lea      eax, [ebp - 4]
026D8B52: push     eax
026D8B53: push     edx
026D8B54: call     0x27bc0f0
026D8B59: jmp      0x26d8b89
026D8B5B: lea      eax, [ebp - 4]
026D8B5E: push     eax
026D8B5F: push     edx
026D8B60: call     0x27bc160
026D8B65: jmp      0x26d8b89
026D8B67: lea      eax, [ebp - 4]
026D8B6A: push     eax
026D8B6B: push     edx
026D8B6C: call     0x27bc1d0
026D8B71: jmp      0x26d8b89
026D8B73: lea      eax, [ebp - 4]
026D8B76: push     eax
026D8B77: push     edx
026D8B78: call     0x27bc240
026D8B7D: jmp      0x26d8b89
026D8B7F: lea      eax, [ebp - 4]
026D8B82: push     eax
026D8B83: push     edx
026D8B84: call     0x27bc390
026D8B89: cvttss2si ecx, dword ptr [ebp - 4]
026D8B8E: movzx    edx, cx
026D8B91: push     edx
026D8B92: call     0x274ca50
026D8B97: mov      ecx, eax
026D8B99: call     0x274d310 ; WriteU16
026D8B9E: inc      esi
026D8B9F: cmp      esi, 8
026D8BA2: jl       0x26d8b22
026D8BA8: call     0x274e6d0 ; Send
026D8BAD: pop      esi
026D8BAE: mov      esp, ebp
026D8BB0: pop      ebp
026D8BB1: ret
```

## WriteU8; VA 0x0274D2E0; RVA 0x0234D2E0

```asm
0274D2E0: push     ebp
0274D2E1: mov      ebp, esp
0274D2E3: cmp      byte ptr [ecx + 8], 0
0274D2E7: jne      0x274d2f2
0274D2E9: mov      eax, 0x80000054
0274D2EE: pop      ebp
0274D2EF: ret      4
0274D2F2: inc      dword ptr [ecx + 0x2bcc2c]
0274D2F8: push     1
0274D2FA: lea      eax, [ebp + 8]
0274D2FD: push     eax
0274D2FE: call     0x5790fe9
0274D303: pop      ebp
```

## WriteU16; VA 0x0274D310; RVA 0x0234D310

```asm
0274D310: push     ebp
0274D311: mov      ebp, esp
0274D313: cmp      byte ptr [ecx + 8], 0
0274D317: jne      0x274d322
0274D319: mov      eax, 0x80000054
0274D31E: pop      ebp
0274D31F: ret      4
0274D322: add      dword ptr [ecx + 0x2bcc2c], 2
0274D329: push     2
0274D32B: lea      eax, [ebp + 8]
0274D32E: push     eax
0274D32F: call     0x5790fe9
0274D334: pop      ebp
0274D335: ret      4
```

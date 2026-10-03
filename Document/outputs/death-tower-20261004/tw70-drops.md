
## _GLOBAL__I__ZN8WongWork11CDeathTower14CPacketHandler20makePickupItemHeaderEtt
```text
0846991D: push ebp
0846991E: mov ebp, esp
08469920: sub esp, 0x18
08469923: mov dword ptr [esp + 4], 0xffff
0846992B: mov dword ptr [esp], 1
08469932: call 0x84698dd ; _Z41__static_initialization_and_destruction_0ii
08469937: leave
08469938: ret
```

## _ZN8WongWork11CDeathTower14CPacketHandler18makeDropItemNotifyEtiitRK8map_item
```text
0845FF1A: push ebp
0845FF1B: mov ebp, esp
0845FF1D: sub esp, 0x28
0845FF20: mov edx, dword ptr [ebp + 0xc]
0845FF23: mov eax, dword ptr [ebp + 0x18]
0845FF26: mov word ptr [ebp - 0xc], dx
0845FF2A: mov word ptr [ebp - 0x10], ax
0845FF2E: mov eax, dword ptr [ebp + 8]
0845FF31: mov dword ptr [esp + 8], 0x28
0845FF39: mov dword ptr [esp + 4], 0
0845FF41: mov dword ptr [esp], eax
0845FF44: call 0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
0845FF49: movzx edx, word ptr [ebp - 0xc]
0845FF4D: mov eax, dword ptr [ebp + 8]
0845FF50: mov dword ptr [esp + 4], edx
0845FF54: mov dword ptr [esp], eax
0845FF57: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FF5C: mov eax, dword ptr [ebp + 8]
0845FF5F: mov edx, dword ptr [ebp + 0x10]
0845FF62: mov dword ptr [esp + 4], edx
0845FF66: mov dword ptr [esp], eax
0845FF69: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FF6E: mov eax, dword ptr [ebp + 8]
0845FF71: mov edx, dword ptr [ebp + 0x14]
0845FF74: mov dword ptr [esp + 4], edx
0845FF78: mov dword ptr [esp], eax
0845FF7B: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FF80: movzx edx, word ptr [ebp - 0x10]
0845FF84: mov eax, dword ptr [ebp + 8]
0845FF87: mov dword ptr [esp + 4], edx
0845FF8B: mov dword ptr [esp], eax
0845FF8E: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FF93: mov eax, dword ptr [ebp + 0x1c]
0845FF96: mov eax, dword ptr [eax + 0x12]
0845FF99: mov edx, eax
0845FF9B: mov eax, dword ptr [ebp + 8]
0845FF9E: mov dword ptr [esp + 4], edx
0845FFA2: mov dword ptr [esp], eax
0845FFA5: call 0x80cb93c ; _ZN18InterfacePacketBuf7put_intEi
0845FFAA: mov eax, dword ptr [ebp + 0x1c]
0845FFAD: add eax, 0x10
0845FFB0: mov dword ptr [esp], eax
0845FFB3: call 0x81507dc ; _ZNK10Inven_Item11GetItemAttrEv
0845FFB8: movzx edx, al
0845FFBB: mov eax, dword ptr [ebp + 8]
0845FFBE: mov dword ptr [esp + 4], edx
0845FFC2: mov dword ptr [esp], eax
0845FFC5: call 0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
0845FFCA: mov eax, dword ptr [ebp + 0x1c]
0845FFCD: add eax, 0x10
0845FFD0: mov dword ptr [esp], eax
0845FFD3: call 0x80f783a ; _ZNK10Inven_Item12get_add_infoEv
0845FFD8: mov edx, dword ptr [ebp + 8]
0845FFDB: mov dword ptr [esp + 4], eax
0845FFDF: mov dword ptr [esp], edx
0845FFE2: call 0x80cb93c ; _ZN18InterfacePacketBuf7put_intEi
0845FFE7: mov eax, dword ptr [ebp + 0x1c]
0845FFEA: movzx eax, word ptr [eax + 0x1b]
0845FFEE: movzx edx, ax
0845FFF1: mov eax, dword ptr [ebp + 8]
0845FFF4: mov dword ptr [esp + 4], edx
0845FFF8: mov dword ptr [esp], eax
0845FFFB: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
08460000: mov eax, dword ptr [ebp + 0x1c]
08460003: mov eax, dword ptr [eax + 0x1d]
08460006: mov edx, eax
08460008: mov eax, dword ptr [ebp + 8]
0846000B: mov dword ptr [esp + 4], edx
0846000F: mov dword ptr [esp], eax
08460012: call 0x80cb93c ; _ZN18InterfacePacketBuf7put_intEi
08460017: mov eax, dword ptr [ebp + 0x1c]
0846001A: add eax, 0x21
0846001D: mov dword ptr [esp], eax
08460020: call 0x8150732 ; _ZNK17stAmplifyOption_t14getAbilityTypeEv
08460025: movzx edx, al
08460028: mov eax, dword ptr [ebp + 8]
0846002B: mov dword ptr [esp + 4], edx
0846002F: mov dword ptr [esp], eax
08460032: call 0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08460037: mov eax, dword ptr [ebp + 0x1c]
0846003A: add eax, 0x21
0846003D: mov dword ptr [esp], eax
08460040: call 0x8150772 ; _ZNK17stAmplifyOption_t15getAbilityValueEv
08460045: movzx edx, ax
08460048: mov eax, dword ptr [ebp + 8]
0846004B: mov dword ptr [esp + 4], edx
0846004F: mov dword ptr [esp], eax
08460052: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
08460057: mov eax, dword ptr [ebp + 0x1c]
0846005A: lea edx, [eax + 0x10]
0846005D: mov eax, dword ptr [ebp + 8]
08460060: mov dword ptr [esp + 4], edx
08460064: mov dword ptr [esp], eax
08460067: call 0x815098e ; _ZN18InterfacePacketBuf10put_packetERK10Inven_Item
0846006C: mov eax, dword ptr [ebp + 0x1c]
0846006F: movzx eax, byte ptr [eax]
08460072: test al, al
08460074: je 0x846007d
08460076: mov eax, 0
0846007B: jmp 0x8460082
0846007D: mov eax, 1
08460082: mov edx, dword ptr [ebp + 8]
08460085: mov dword ptr [esp + 4], eax
08460089: mov dword ptr [esp], edx
0846008C: call 0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08460091: mov eax, dword ptr [ebp + 8]
08460094: mov dword ptr [esp + 4], 1
0846009C: mov dword ptr [esp], eax
0846009F: call 0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
084600A4: leave
084600A5: ret
```

## _ZN8WongWork11CDeathTower14CPacketHandler16appendPickupItemEPctii
```text
0845FDC0: push ebp
0845FDC1: mov ebp, esp
0845FDC3: sub esp, 0x38
0845FDC6: mov eax, dword ptr [ebp + 0x10]
0845FDC9: mov word ptr [ebp - 0x1c], ax
0845FDCD: mov dword ptr [ebp - 0xc], 0
0845FDD4: jmp 0x845fdf5
0845FDD6: mov eax, dword ptr [ebp - 0xc]
0845FDD9: add eax, dword ptr [ebp + 0xc]
0845FDDC: movzx eax, byte ptr [eax]
0845FDDF: movsx edx, al
0845FDE2: mov eax, dword ptr [ebp + 8]
0845FDE5: mov dword ptr [esp + 4], edx
0845FDE9: mov dword ptr [esp], eax
0845FDEC: call 0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
0845FDF1: add dword ptr [ebp - 0xc], 1
0845FDF5: cmp dword ptr [ebp - 0xc], 3
0845FDF9: setle al
0845FDFC: test al, al
0845FDFE: jne 0x845fdd6
0845FE00: movzx edx, word ptr [ebp - 0x1c]
0845FE04: mov eax, dword ptr [ebp + 8]
0845FE07: mov dword ptr [esp + 4], edx
0845FE0B: mov dword ptr [esp], eax
0845FE0E: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FE13: mov eax, dword ptr [ebp + 8]
0845FE16: mov edx, dword ptr [ebp + 0x14]
0845FE19: mov dword ptr [esp + 4], edx
0845FE1D: mov dword ptr [esp], eax
0845FE20: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FE25: mov eax, dword ptr [ebp + 8]
0845FE28: mov edx, dword ptr [ebp + 0x18]
0845FE2B: mov dword ptr [esp + 4], edx
0845FE2F: mov dword ptr [esp], eax
0845FE32: call 0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
0845FE37: mov eax, dword ptr [ebp + 8]
0845FE3A: mov dword ptr [esp + 4], 1
0845FE42: mov dword ptr [esp], eax
0845FE45: call 0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
0845FE4A: leave
0845FE4B: ret
```

## _ZN8WongWork11CDeathTower16handlePickupItemEP5CUseribc
```text
08464036: push ebp
08464037: mov ebp, esp
08464039: push esi
0846403A: push ebx
0846403B: sub esp, 0x110
08464041: mov edx, dword ptr [ebp + 0x14]
08464044: mov eax, dword ptr [ebp + 0x18]
08464047: mov byte ptr [ebp - 0xbc], dl
0846404D: mov byte ptr [ebp - 0xc0], al
08464053: lea eax, [ebp - 0xb8]
08464059: mov dword ptr [esp], eax
0846405C: call 0x81512cc ; _ZN8map_itemC2Ev
08464061: lea eax, [ebp - 0x28]
08464064: mov dword ptr [esp], eax
08464067: call 0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
0846406C: mov eax, dword ptr [ebp + 8]
0846406F: lea edx, [eax + 0x14]
08464072: lea eax, [ebp - 0xb8]
08464078: mov dword ptr [esp + 8], eax
0846407C: mov eax, dword ptr [ebp + 0x10]
0846407F: mov dword ptr [esp + 4], eax
08464083: mov dword ptr [esp], edx
08464086: call 0x8461534 ; _ZN8WongWork11CDeathTower6CStage8peekItemEiR8map_item
0846408B: xor eax, 1
0846408E: test al, al
08464090: je 0x84640ef
08464092: cmp byte ptr [ebp - 0xc0], 3
08464099: jle 0x84640b8
0846409B: mov dword ptr [esp + 8], 0x51
084640A3: mov dword ptr [esp + 4], 0x2e
084640AB: lea eax, [ebp - 0x28]
084640AE: mov dword ptr [esp], eax
084640B1: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
084640B6: jmp 0x84640d3
084640B8: mov dword ptr [esp + 8], 0x15
084640C0: mov dword ptr [esp + 4], 0x2e
084640C8: lea eax, [ebp - 0x28]
084640CB: mov dword ptr [esp], eax
084640CE: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
084640D3: lea eax, [ebp - 0x28]
084640D6: mov dword ptr [esp + 4], eax
084640DA: mov eax, dword ptr [ebp + 0xc]
084640DD: mov dword ptr [esp], eax
084640E0: call 0x86485ba ; _ZN5CUser4SendER11PacketGuard
084640E5: mov ebx, 0
084640EA: jmp 0x8464657
084640EF: mov eax, dword ptr [ebp + 8]
084640F2: mov eax, dword ptr [eax]
084640F4: mov dword ptr [esp], eax
084640F7: call 0x859a16a ; _ZN6CParty16get_member_countEv
084640FC: mov dword ptr [ebp - 0x1c], eax
084640FF: cmp dword ptr [ebp - 0x1c], 0
08464103: jg 0x846413c
08464105: mov dword ptr [esp + 8], 0x15
0846410D: mov dword ptr [esp + 4], 0x2e
08464115: lea eax, [ebp - 0x28]
08464118: mov dword ptr [esp], eax
0846411B: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
08464120: lea eax, [ebp - 0x28]
08464123: mov dword ptr [esp + 4], eax
08464127: mov eax, dword ptr [ebp + 0xc]
0846412A: mov dword ptr [esp], eax
0846412D: call 0x86485ba ; _ZN5CUser4SendER11PacketGuard
08464132: mov ebx, 0
08464137: jmp 0x8464657
0846413C: mov eax, dword ptr [ebp - 0xa6]
08464142: test eax, eax
08464144: jne 0x846425e
0846414A: mov eax, dword ptr [ebp + 8]
0846414D: lea edx, [eax + 0x14]
08464150: lea eax, [ebp - 0xb8]
08464156: mov dword ptr [esp + 8], eax
0846415A: mov eax, dword ptr [ebp + 0x10]
0846415D: mov dword ptr [esp + 4], eax
08464161: mov dword ptr [esp], edx
08464164: call 0x84615e4 ; _ZN8WongWork11CDeathTower6CStage10pickupItemEiR8map_item
08464169: xor eax, 1
0846416C: test al, al
0846416E: je 0x84641a7
08464170: mov dword ptr [esp + 8], 0x15
08464178: mov dword ptr [esp + 4], 0x2e
08464180: lea eax, [ebp - 0x28]
08464183: mov dword ptr [esp], eax
08464186: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
0846418B: lea eax, [ebp - 0x28]
0846418E: mov dword ptr [esp + 4], eax
08464192: mov eax, dword ptr [ebp + 0xc]
08464195: mov dword ptr [esp], eax
08464198: call 0x86485ba ; _ZN5CUser4SendER11PacketGuard
0846419D: mov ebx, 0
084641A2: jmp 0x8464657
084641A7: mov dword ptr [ebp - 0x38], 0
084641AE: mov dword ptr [ebp - 0x34], 0
084641B5: mov dword ptr [ebp - 0x30], 0
084641BC: mov dword ptr [ebp - 0x2c], 0
084641C3: mov dword ptr [ebp - 0x48], 0
084641CA: mov dword ptr [ebp - 0x44], 0
084641D1: mov dword ptr [ebp - 0x40], 0
084641D8: mov dword ptr [ebp - 0x3c], 0
084641DF: lea eax, [ebp - 0x48]
084641E2: mov dword ptr [esp + 0x14], eax
084641E6: lea eax, [ebp - 0x38]
084641E9: mov dword ptr [esp + 0x10], eax
084641ED: lea eax, [ebp - 0xb8]
084641F3: mov dword ptr [esp + 0xc], eax
084641F7: mov eax, dword ptr [ebp - 0x1c]
084641FA: mov dword ptr [esp + 8], eax
084641FE: mov eax, dword ptr [ebp + 0xc]
08464201: mov dword ptr [esp + 4], eax
08464205: mov eax, dword ptr [ebp + 8]
08464208: mov dword ptr [esp], eax
0846420B: call 0x8466b02 ; _ZN8WongWork11CDeathTower16_pickupItemMoneyEP5CUseriRK8map_itemPiS6_
08464210: mov eax, dword ptr [ebp + 0x10]
08464213: movzx ebx, ax
08464216: mov eax, dword ptr [ebp + 0xc]
08464219: mov dword ptr [esp], eax
0846421C: call 0x80da37c ; _ZNK5CUser13get_unique_idEv
08464221: movzx eax, ax
08464224: mov dword ptr [esp + 8], ebx
08464228: mov dword ptr [esp + 4], eax
0846422C: lea eax, [ebp - 0x28]
0846422F: mov dword ptr [esp], eax
08464232: call 0x845fc6c ; _ZN8WongWork11CDeathTower14CPacketHandler20makePickupItemHeaderEtt
08464237: mov eax, dword ptr [ebp + 8]
0846423A: mov eax, dword ptr [eax]
0846423C: mov dword ptr [esp + 0xc], eax
08464240: lea eax, [ebp - 0x48]
08464243: mov dword ptr [esp + 8], eax
08464247: lea eax, [ebp - 0x38]
0846424A: mov dword ptr [esp + 4], eax
0846424E: lea eax, [ebp - 0x28]
08464251: mov dword ptr [esp], eax
08464254: call 0x845fcc4 ; _ZN8WongWork11CDeathTower14CPacketHandler17appendPickupMoneyEPiS2_P6CParty
08464259: jmp 0x8464621
0846425E: mov dword ptr [ebp - 0x4c], 0
08464265: mov eax, dword ptr [ebp - 0xa6]
0846426B: mov ebx, eax
0846426D: call 0x80cc19b ; _Z14G_CDataManagerv
08464272: mov dword ptr [esp + 4], ebx
08464276: mov dword ptr [esp], eax
08464279: call 0x835fa32 ; _ZNK12CDataManager9find_itemEi
0846427E: mov dword ptr [ebp - 0x18], eax
08464281: cmp dword ptr [ebp - 0x18], 0
08464285: jne 0x8464291
08464287: mov ebx, 0
0846428C: jmp 0x8464657
08464291: mov dword ptr [ebp - 0x14], 0
08464298: mov eax, dword ptr [ebp - 0x18]
0846429B: mov dword ptr [esp], eax
0846429E: call 0x80f12fa ; _ZNK5CItem12is_stackableEv
084642A3: test al, al
084642A5: je 0x84642cc
084642A7: mov eax, dword ptr [ebp - 0x18]
084642AA: mov eax, dword ptr [eax]
084642AC: add eax, 0xc
084642AF: mov edx, dword ptr [eax]
084642B1: mov eax, dword ptr [ebp - 0x18]
084642B4: mov dword ptr [esp], eax
084642B7: call edx
084642B9: cmp eax, 9
084642BC: sete al
084642BF: test al, al
084642C1: je 0x8464308
084642C3: mov dword ptr [ebp - 0x14], 7
084642CA: jmp 0x8464308
084642CC: mov eax, dword ptr [ebp - 0x18]
084642CF: mov eax, dword ptr [eax]
084642D1: add eax, 0x14
084642D4: mov edx, dword ptr [eax]
084642D6: mov eax, dword ptr [ebp - 0x18]
084642D9: mov dword ptr [esp], eax
084642DC: call edx
084642DE: test al, al
084642E0: je 0x84642eb
084642E2: mov dword ptr [ebp - 0x14], 7
084642E9: jmp 0x8464308
084642EB: mov eax, dword ptr [ebp - 0x18]
084642EE: mov eax, dword ptr [eax]
084642F0: add eax, 0x10
084642F3: mov edx, dword ptr [eax]
084642F5: mov eax, dword ptr [ebp - 0x18]
084642F8: mov dword ptr [esp], eax
084642FB: call edx
084642FD: test al, al
084642FF: je 0x8464308
08464301: mov dword ptr [ebp - 0x14], 1
08464308: movsx edx, byte ptr [ebp - 0xc0]
0846430F: movzx eax, byte ptr [ebp - 0xbc]
08464316: mov ecx, dword ptr [ebp - 0x18]
08464319: mov dword ptr [esp + 0x1c], ecx
0846431D: mov dword ptr [esp + 0x18], edx
08464321: mov dword ptr [esp + 0x14], eax
08464325: lea eax, [ebp - 0xb8]
0846432B: mov dword ptr [esp + 0x10], eax
0846432F: mov eax, dword ptr [ebp - 0x1c]
08464332: mov dword ptr [esp + 0xc], eax
08464336: lea eax, [ebp - 0x4c]
08464339: mov dword ptr [esp + 8], eax
0846433D: mov eax, dword ptr [ebp + 0xc]
08464340: mov dword ptr [esp + 4], eax
08464344: mov eax, dword ptr [ebp + 8]
08464347: mov dword ptr [esp], eax
0846434A: call 0x8466cfe ; _ZN8WongWork11CDeathTower11_pickupItemEP5CUserPciRK8map_itembcPK5CItem
0846434F: mov dword ptr [ebp - 0x10], eax
08464352: cmp dword ptr [ebp - 0x10], 0
08464356: jne 0x846438f
08464358: mov dword ptr [esp + 8], 0x16
08464360: mov dword ptr [esp + 4], 0x2e
08464368: lea eax, [ebp - 0x28]
0846436B: mov dword ptr [esp], eax
0846436E: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
08464373: lea eax, [ebp - 0x28]
08464376: mov dword ptr [esp + 4], eax
0846437A: mov eax, dword ptr [ebp + 0xc]
0846437D: mov dword ptr [esp], eax
08464380: call 0x86485ba ; _ZN5CUser4SendER11PacketGuard
08464385: mov ebx, 0
0846438A: jmp 0x8464657
0846438F: mov dword ptr [ebp - 0xc], 0xffffffff
08464396: lea eax, [ebp - 0xb8]
0846439C: add eax, 0x10
0846439F: mov dword ptr [esp], eax
084643A2: call 0x8150800 ; _ZNK10Inven_Item16isAvatarItemTypeEv
084643A7: test al, al
084643A9: je 0x846440f
084643AB: mov eax, dword ptr [ebp - 0x10]
084643AE: mov dword ptr [esp], eax
084643B1: call 0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
084643B6: lea edx, [ebp - 0x64]
084643B9: mov dword ptr [esp + 8], edx
084643BD: mov dword ptr [esp + 4], eax
084643C1: mov dword ptr [esp], 5
084643C8: call 0x84b7750 ; _ZN8WongWork12CGenUniqueNo8genIPGNoENS0_15ENUM_IPGNO_TYPEEjPc
084643CD: movzx eax, word ptr [ebp - 0x9d]
084643D4: movsx edx, al
084643D7: mov eax, dword ptr [ebp - 0xa6]
084643DD: mov dword ptr [esp + 0x14], 0x10
084643E5: lea ecx, [ebp - 0x64]
084643E8: mov dword ptr [esp + 0x10], ecx
084643EC: mov dword ptr [esp + 0xc], edx
084643F0: mov dword ptr [esp + 8], 0
084643F8: mov dword ptr [esp + 4], eax
084643FC: mov eax, dword ptr [ebp - 0x10]
084643FF: mov dword ptr [esp], eax
08464402: call 0x8681224 ; _ZN5CUser13addAvatarItemEmccPKc20eAvatarItemAddReason
08464407: mov dword ptr [ebp - 0xc], eax
0846440A: jmp 0x84644cc
0846440F: mov eax, dword ptr [ebp - 0x10]
08464412: mov dword ptr [esp], eax
08464415: call 0x80da28e ; _ZN15CUserCharacInfo18getCurCharacInvenWEv
0846441A: mov dword ptr [esp + 0x4c], 1
08464422: mov dword ptr [esp + 0x48], 1
0846442A: mov dword ptr [esp + 0x44], 4
08464432: mov edx, dword ptr [ebp - 0xa8]
08464438: mov dword ptr [esp + 4], edx
0846443C: mov edx, dword ptr [ebp - 0xa4]
08464442: mov dword ptr [esp + 8], edx
08464446: mov edx, dword ptr [ebp - 0xa0]
0846444C: mov dword ptr [esp + 0xc], edx
08464450: mov edx, dword ptr [ebp - 0x9c]
08464456: mov dword ptr [esp + 0x10], edx
0846445A: mov edx, dword ptr [ebp - 0x98]
08464460: mov dword ptr [esp + 0x14], edx
08464464: mov edx, dword ptr [ebp - 0x94]
0846446A: mov dword ptr [esp + 0x18], edx
0846446E: mov edx, dword ptr [ebp - 0x90]
08464474: mov dword ptr [esp + 0x1c], edx
08464478: mov edx, dword ptr [ebp - 0x8c]
0846447E: mov dword ptr [esp + 0x20], edx
08464482: mov edx, dword ptr [ebp - 0x88]
08464488: mov dword ptr [esp + 0x24], edx
0846448C: mov edx, dword ptr [ebp - 0x84]
08464492: mov dword ptr [esp + 0x28], edx
08464496: mov edx, dword ptr [ebp - 0x80]
08464499: mov dword ptr [esp + 0x2c], edx
0846449D: mov edx, dword ptr [ebp - 0x7c]
084644A0: mov dword ptr [esp + 0x30], edx
084644A4: mov edx, dword ptr [ebp - 0x78]
084644A7: mov dword ptr [esp + 0x34], edx
084644AB: mov edx, dword ptr [ebp - 0x74]
084644AE: mov dword ptr [esp + 0x38], edx
084644B2: mov edx, dword ptr [ebp - 0x70]
084644B5: mov dword ptr [esp + 0x3c], edx
084644B9: movzx edx, byte ptr [ebp - 0x6c]
084644BD: mov byte ptr [esp + 0x40], dl
084644C1: mov dword ptr [esp], eax
084644C4: call 0x8502d86 ; _ZN10CInventory23insertItemIntoInventoryE10Inven_Item14eItemAddReasonbb
084644C9: mov dword ptr [ebp - 0xc], eax
084644CC: cmp dword ptr [ebp - 0xc], -1
084644D0: jne 0x8464509
084644D2: mov dword ptr [esp + 8], 4
084644DA: mov dword ptr [esp + 4], 0x2e
084644E2: lea eax, [ebp - 0x28]
084644E5: mov dword ptr [esp], eax
084644E8: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
084644ED: lea eax, [ebp - 0x28]
084644F0: mov dword ptr [esp + 4], eax
084644F4: mov eax, dword ptr [ebp + 0xc]
084644F7: mov dword ptr [esp], eax
084644FA: call 0x86485ba ; _ZN5CUser4SendER11PacketGuard
084644FF: mov ebx, 0
08464504: jmp 0x8464657
08464509: mov eax, dword ptr [ebp - 0x18]
0846450C: mov eax, dword ptr [eax]
0846450E: add eax, 0x4c
08464511: mov edx, dword ptr [eax]
08464513: mov eax, dword ptr [ebp - 0x18]
08464516: mov dword ptr [esp], eax
08464519: call edx
0846451B: test al, al
0846451D: je 0x846456b
0846451F: mov dword ptr [esp], 0x941f714
08464526: call 0x823445e ; _ZN11CSystemTime10getCurDateEv
0846452B: mov ebx, eax
0846452D: mov eax, dword ptr [ebp - 0x10]
08464530: mov dword ptr [esp], eax
08464533: call 0x822f3a0 ; _ZNK15CUserCharacInfo27get_charac_10_level_sectionEv
08464538: mov dword ptr [esp + 0x18], ebx
0846453C: mov dword ptr [esp + 0x14], eax
08464540: mov dword ptr [esp + 0x10], 0x8c63333
08464548: mov dword ptr [esp + 0xc], 1
08464550: mov dword ptr [esp + 8], 0x8c6333b
08464558: mov dword ptr [esp + 4], 0x8c63344
08464560: mov eax, dword ptr [ebp - 0x10]
08464563: mov dword ptr [esp], eax
08464566: call 0x860fdd2 ; _ZN14statistc_proxy3addEP5CUserPKcS3_jS3_z
0846456B: mov eax, dword ptr [ebp + 8]
0846456E: lea edx, [eax + 0x14]
08464571: lea eax, [ebp - 0xb8]
08464577: mov dword ptr [esp + 8], eax
0846457B: mov eax, dword ptr [ebp + 0x10]
0846457E: mov dword ptr [esp + 4], eax
08464582: mov dword ptr [esp], edx
08464585: call 0x84615e4 ; _ZN8WongWork11CDeathTower6CStage10pickupItemEiR8map_item
0846458A: xor eax, 1
0846458D: test al, al
0846458F: je 0x84645c8
08464591: mov dword ptr [esp + 8], 0x15
08464599: mov dword ptr [esp + 4], 0x2e
084645A1: lea eax, [ebp - 0x28]
084645A4: mov dword ptr [esp], eax
084645A7: call 0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
084645AC: lea eax, [ebp - 0x28]
084645AF: mov dword ptr [esp + 4], eax
084645B3: mov eax, dword ptr [ebp + 0xc]
084645B6: mov dword ptr [esp], eax
084645B9: call 0x86485ba ; _ZN5CUser4SendER11PacketGuard
084645BE: mov ebx, 0
084645C3: jmp 0x8464657
084645C8: mov eax, dword ptr [ebp + 0x10]
084645CB: movzx ebx, ax
084645CE: mov eax, dword ptr [ebp + 0xc]
084645D1: mov dword ptr [esp], eax
084645D4: call 0x80da37c ; _ZNK5CUser13get_unique_idEv
084645D9: movzx eax, ax
084645DC: mov dword ptr [esp + 8], ebx
084645E0: mov dword ptr [esp + 4], eax
084645E4: lea eax, [ebp - 0x28]
084645E7: mov dword ptr [esp], eax
084645EA: call 0x845fc6c ; _ZN8WongWork11CDeathTower14CPacketHandler20makePickupItemHeaderEtt
084645EF: mov eax, dword ptr [ebp - 0x10]
084645F2: mov dword ptr [esp], eax
084645F5: call 0x80da37c ; _ZNK5CUser13get_unique_idEv
084645FA: movzx eax, ax
084645FD: mov edx, dword ptr [ebp - 0x14]
08464600: mov dword ptr [esp + 0x10], edx
08464604: mov edx, dword ptr [ebp - 0xc]
08464607: mov dword ptr [esp + 0xc], edx
0846460B: mov dword ptr [esp + 8], eax
0846460F: lea eax, [ebp - 0x4c]
08464612: mov dword ptr [esp + 4], eax
08464616: lea eax, [ebp - 0x28]
08464619: mov dword ptr [esp], eax
0846461C: call 0x845fdc0 ; _ZN8WongWork11CDeathTower14CPacketHandler16appendPickupItemEPctii
08464621: mov eax, dword ptr [ebp + 8]
08464624: mov eax, dword ptr [eax]
08464626: lea edx, [ebp - 0x28]
08464629: mov dword ptr [esp + 4], edx
0846462D: mov dword ptr [esp], eax
08464630: call 0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08464635: mov ebx, 1
0846463A: jmp 0x8464657
0846463C: mov ebx, edx
0846463E: mov esi, eax
08464640: lea eax, [ebp - 0x28]
08464643: mov dword ptr [esp], eax
08464646: call 0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
0846464B: mov eax, esi
0846464D: mov edx, ebx
0846464F: mov dword ptr [esp], eax
08464652: call 0x8ae3750 ; _Unwind_Resume
08464657: lea eax, [ebp - 0x28]
0846465A: mov dword ptr [esp], eax
0846465D: call 0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08464662: mov eax, ebx
08464664: add esp, 0x110
0846466A: pop ebx
0846466B: pop esi
0846466C: pop ebp
0846466D: ret
```

## _ZN8WongWork11CDeathTower14CPacketHandler20makePickupItemHeaderEtt
```text
0845FC6C: push ebp
0845FC6D: mov ebp, esp
0845FC6F: sub esp, 0x28
0845FC72: mov edx, dword ptr [ebp + 0xc]
0845FC75: mov eax, dword ptr [ebp + 0x10]
0845FC78: mov word ptr [ebp - 0xc], dx
0845FC7C: mov word ptr [ebp - 0x10], ax
0845FC80: mov eax, dword ptr [ebp + 8]
0845FC83: mov dword ptr [esp + 8], 0x27
0845FC8B: mov dword ptr [esp + 4], 0
0845FC93: mov dword ptr [esp], eax
0845FC96: call 0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
0845FC9B: movzx edx, word ptr [ebp - 0x10]
0845FC9F: mov eax, dword ptr [ebp + 8]
0845FCA2: mov dword ptr [esp + 4], edx
0845FCA6: mov dword ptr [esp], eax
0845FCA9: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FCAE: movzx edx, word ptr [ebp - 0xc]
0845FCB2: mov eax, dword ptr [ebp + 8]
0845FCB5: mov dword ptr [esp + 4], edx
0845FCB9: mov dword ptr [esp], eax
0845FCBC: call 0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
0845FCC1: leave
0845FCC2: ret
```

## _ZN8WongWork11CDeathTower6CStage10consistMapERKNS0_11CDungeonMgrEPK4CMapPS0_
```text
08460634: push ebp
08460635: mov ebp, esp
08460637: push edi
08460638: push esi
08460639: push ebx
0846063A: sub esp, 0xfbc
08460640: mov eax, dword ptr [ebp + 0x10]
08460643: mov dword ptr [esp], eax
08460646: call 0x8134776 ; _ZNK4CMap9get_indexEv
0846064B: mov edx, dword ptr [ebp + 8]
0846064E: mov dword ptr [edx + 0x10], eax
08460651: lea eax, [ebp - 0xa80]
08460657: mov dword ptr [esp], eax
0846065A: call 0x80e247e ; _ZNSt6vectorIjSaIjEEC1Ev
0846065F: call 0x80cc19b ; _Z14G_CDataManagerv
08460664: mov eax, dword ptr [eax + 0x869c]
0846066A: mov dword ptr [esp], eax
0846066D: call 0x8150880 ; _ZNK16CAICharacterList10getAPCountEv
08460672: mov dword ptr [esp + 4], eax
08460676: lea eax, [ebp - 0xa98]
0846067C: mov dword ptr [esp], eax
0846067F: call 0x8238cdc ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEEC2ERKS6_
08460684: mov dword ptr [ebp - 0x3c], 0
0846068B: lea eax, [ebp - 0xf50]
08460691: mov dword ptr [esp], eax
08460694: call 0x81511c8 ; _ZN11map_monsterC2Ev
08460699: mov eax, dword ptr [ebp + 0x10]
0846069C: mov dword ptr [esp], eax
0846069F: call 0x81508fc ; _ZNK4CMap15getAICharactersEv
084606A4: mov dword ptr [ebp - 0x38], eax
084606A7: mov eax, dword ptr [ebp - 0x38]
084606AA: mov dword ptr [esp], eax
084606AD: call 0x81526de ; _ZNKSt6vectorI27STAICharacterMapArrangeDataSaIS0_EE5emptyEv
084606B2: xor eax, 1
084606B5: test al, al
084606B7: je 0x8460940
084606BD: mov dword ptr [ebp - 0x3c], 0
084606C4: lea eax, [ebp - 0xad4]
084606CA: mov edx, dword ptr [ebp - 0x38]
084606CD: mov dword ptr [esp + 4], edx
084606D1: mov dword ptr [esp], eax
084606D4: call 0x8152722 ; _ZNKSt6vectorI27STAICharacterMapArrangeDataSaIS0_EE5beginEv
084606D9: sub esp, 4
084606DC: lea eax, [ebp - 0xad8]
084606E2: mov edx, dword ptr [ebp - 0x38]
084606E5: mov dword ptr [esp + 4], edx
084606E9: mov dword ptr [esp], eax
084606EC: call 0x815274e ; _ZNKSt6vectorI27STAICharacterMapArrangeDataSaIS0_EE3endEv
084606F1: sub esp, 4
084606F4: jmp 0x8460920
084606F9: lea eax, [ebp - 0xad4]
084606FF: mov dword ptr [esp], eax
08460702: call 0x81527bc ; _ZNK9__gnu_cxx17__normal_iteratorIPK27STAICharacterMapArrangeDataSt6vectorIS1_SaIS1_EEEdeEv
08460707: mov dword ptr [ebp - 0x30], eax
0846070A: mov eax, dword ptr [ebp - 0x30]
0846070D: mov eax, dword ptr [eax]
0846070F: mov ebx, eax
08460711: call 0x80cc19b ; _Z14G_CDataManagerv
08460716: mov eax, dword ptr [eax + 0x869c]
0846071C: mov dword ptr [esp + 4], ebx
08460720: mov dword ptr [esp], eax
08460723: call 0x834a9d2 ; _ZN16CAICharacterList3getEj
08460728: mov dword ptr [ebp - 0x2c], eax
0846072B: cmp dword ptr [ebp - 0x2c], 0
0846072F: jne 0x846073b
08460731: mov ebx, 0
08460736: jmp 0x846147a
0846073B: mov eax, dword ptr [ebp - 0x2c]
0846073E: mov dword ptr [esp], eax
08460741: call 0x8150850 ; _ZNK12CAICharacter6getIdxEv
08460746: mov dword ptr [ebp - 0xa74], eax
0846074C: lea eax, [ebp - 0xa74]
08460752: mov dword ptr [esp + 4], eax
08460756: lea eax, [ebp - 0xa80]
0846075C: mov dword ptr [esp], eax
0846075F: call 0x81527c6 ; _ZNSt6vectorIjSaIjEE9push_backEOj
08460764: mov eax, dword ptr [ebp - 0x2c]
08460767: mov dword ptr [esp], eax
0846076A: call 0x8150866 ; _ZNK12CAICharacter18getAppearancePointEv
0846076F: test eax, eax
08460771: setne al
08460774: test al, al
08460776: je 0x846083a
0846077C: mov eax, dword ptr [ebp - 0x2c]
0846077F: mov dword ptr [esp], eax
08460782: call 0x8150866 ; _ZNK12CAICharacter18getAppearancePointEv
08460787: mov dword ptr [ebp - 0xa70], eax
0846078D: lea eax, [ebp - 0xadc]
08460793: lea edx, [ebp - 0xa70]
08460799: mov dword ptr [esp + 8], edx
0846079D: lea edx, [ebp - 0xa98]
084607A3: mov dword ptr [esp + 4], edx
084607A7: mov dword ptr [esp], eax
084607AA: call 0x80cd2ee ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEE4findERS3_
084607AF: sub esp, 4
084607B2: lea eax, [ebp - 0xa6c]
084607B8: lea edx, [ebp - 0xa98]
084607BE: mov dword ptr [esp + 4], edx
084607C2: mov dword ptr [esp], eax
084607C5: call 0x80cd31a ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEE3endEv
084607CA: sub esp, 4
084607CD: lea eax, [ebp - 0xa6c]
084607D3: mov dword ptr [esp + 4], eax
084607D7: lea eax, [ebp - 0xadc]
084607DD: mov dword ptr [esp], eax
084607E0: call 0x80da3c8 ; _ZNKSt17_Rb_tree_iteratorISt4pairIKiiEEneERKS3_
084607E5: test al, al
084607E7: je 0x846083a
084607E9: lea eax, [ebp - 0xadc]
084607EF: mov dword ptr [esp], eax
084607F2: call 0x80cd340 ; _ZNKSt17_Rb_tree_iteratorISt4pairIKiiEEptEv
084607F7: mov ebx, eax
084607F9: lea eax, [ebp - 0xadc]
084607FF: mov dword ptr [esp], eax
08460802: call 0x80cd340 ; _ZNKSt17_Rb_tree_iteratorISt4pairIKiiEEptEv
08460807: mov eax, dword ptr [eax + 4]
0846080A: sub eax, 1
0846080D: mov dword ptr [ebp - 0xa68], eax
08460813: mov dword ptr [ebp - 0xa64], 0
0846081D: lea eax, [ebp - 0xa68]
08460823: mov dword ptr [esp + 4], eax
08460827: lea eax, [ebp - 0xa64]
0846082D: mov dword ptr [esp], eax
08460830: call 0x808e218 ; _ZSt3maxIiERKT_S2_S2_
08460835: mov eax, dword ptr [eax]
08460837: mov dword ptr [ebx + 4], eax
0846083A: mov dword ptr [esp + 8], 0x34c
08460842: mov dword ptr [esp + 4], 0
0846084A: lea eax, [ebp - 0xf50]
08460850: mov dword ptr [esp], eax
08460853: call 0x807dcc0 ;
08460858: mov eax, dword ptr [ebp - 0x3c]
0846085B: mov dword ptr [ebp - 0xf50], eax
08460861: mov eax, dword ptr [ebp - 0x30]
08460864: mov eax, dword ptr [eax]
08460866: mov dword ptr [ebp - 0xf44], eax
0846086C: mov eax, dword ptr [ebp + 8]
0846086F: mov eax, dword ptr [eax + 0x48]
08460872: mov dword ptr [ebp - 0xf4c], eax
08460878: lea edx, [eax + 1]
0846087B: mov eax, dword ptr [ebp + 8]
0846087E: mov dword ptr [eax + 0x48], edx
08460881: mov byte ptr [ebp - 0xf48], 5
08460888: mov eax, dword ptr [ebp - 0x2c]
0846088B: mov dword ptr [esp], eax
0846088E: call 0x815085a ; _ZNK12CAICharacter8getLevelEv
08460893: mov byte ptr [ebp - 0xf40], al
08460899: mov eax, dword ptr [ebp - 0x30]
0846089C: mov eax, dword ptr [eax + 0x10]
0846089F: mov dword ptr [ebp - 0xf24], eax
084608A5: mov byte ptr [ebp - 0xf3f], 0
084608AC: mov byte ptr [ebp - 0xf3e], 0
084608B3: lea eax, [ebp - 0xf50]
084608B9: mov dword ptr [esp + 4], eax
084608BD: lea eax, [ebp - 0xa60]
084608C3: mov dword ptr [esp], eax
084608C6: call 0x8151aea ; _ZN11map_monsterC1ERKS_
084608CB: mov eax, dword ptr [ebp + 8]
084608CE: lea edx, [eax + 0xc]
084608D1: lea eax, [ebp - 0xa60]
084608D7: mov dword ptr [esp + 4], eax
084608DB: mov dword ptr [esp], edx
084608DE: call 0x8151612 ; _ZN7MapInfo7Add_MobE11map_monster
084608E3: jmp 0x8460900
084608E5: mov ebx, edx
084608E7: mov esi, eax
084608E9: lea eax, [ebp - 0xa60]
084608EF: mov dword ptr [esp], eax
084608F2: call 0x81515ae ; _ZN11map_monsterD1Ev
084608F7: mov eax, esi
084608F9: mov edx, ebx
084608FB: jmp 0x8461462
08460900: lea eax, [ebp - 0xa60]
08460906: mov dword ptr [esp], eax
08460909: call 0x81515ae ; _ZN11map_monsterD1Ev
0846090E: add dword ptr [ebp - 0x3c], 1
08460912: lea eax, [ebp - 0xad4]
08460918: mov dword ptr [esp], eax
0846091B: call 0x81527a6 ; _ZN9__gnu_cxx17__normal_iteratorIPK27STAICharacterMapArrangeDataSt6vectorIS1_SaIS1_EEEppEv
08460920: lea eax, [ebp - 0xad8]
08460926: mov dword ptr [esp + 4], eax
0846092A: lea eax, [ebp - 0xad4]
08460930: mov dword ptr [esp], eax
08460933: call 0x815277a ; _ZN9__gnu_cxxneIPK27STAICharacterMapArrangeDataSt6vectorIS1_SaIS1_EEEEbRKNS_17__normal_iteratorIT_T0_EESC_
08460938: test al, al
0846093A: jne 0x84606f9
08460940: mov eax, dword ptr [ebp + 0x10]
08460943: mov dword ptr [esp], eax
08460946: call 0x81508f0 ; _ZNK4CMap17getAPCRandomPointEv
0846094B: mov dword ptr [ebp - 0xa9c], eax
08460951: mov eax, dword ptr [ebp - 0xa9c]
08460957: test eax, eax
08460959: je 0x8460d28
0846095F: mov dword ptr [ebp - 0x3c], 0x40
08460966: jmp 0x8460d02
0846096B: mov eax, dword ptr [ebp - 0xa9c]
08460971: mov dword ptr [esp], eax
08460974: call 0x86b1b87 ; _Z12get_rand_inti
08460979: add eax, 1
0846097C: mov ebx, eax
0846097E: call 0x80cc19b ; _Z14G_CDataManagerv
08460983: mov eax, dword ptr [eax + 0x869c]
08460989: mov dword ptr [esp + 4], ebx
0846098D: mov dword ptr [esp], eax
08460990: call 0x834aaf4 ; _ZN16CAICharacterList7getByAPEj
08460995: mov dword ptr [ebp - 0x28], eax
08460998: cmp dword ptr [ebp - 0x28], 0
0846099C: jne 0x84609a8
0846099E: mov ebx, 0
084609A3: jmp 0x846147a
084609A8: lea eax, [ebp - 0x714]
084609AE: lea edx, [ebp - 0xa80]
084609B4: mov dword ptr [esp + 4], edx
084609B8: mov dword ptr [esp], eax
084609BB: call 0x80e2a8a ; _ZNSt6vectorIjSaIjEE3endEv
084609C0: sub esp, 4
084609C3: mov eax, dword ptr [ebp - 0x28]
084609C6: mov dword ptr [esp], eax
084609C9: call 0x8150850 ; _ZNK12CAICharacter6getIdxEv
084609CE: mov dword ptr [ebp - 0x70c], eax
084609D4: lea eax, [ebp - 0x708]
084609DA: lea edx, [ebp - 0xa80]
084609E0: mov dword ptr [esp + 4], edx
084609E4: mov dword ptr [esp], eax
084609E7: call 0x80e2a8a ; _ZNSt6vectorIjSaIjEE3endEv
084609EC: sub esp, 4
084609EF: lea eax, [ebp - 0x704]
084609F5: lea edx, [ebp - 0xa80]
084609FB: mov dword ptr [esp + 4], edx
084609FF: mov dword ptr [esp], eax
08460A02: call 0x80e384e ; _ZNSt6vectorIjSaIjEE5beginEv
08460A07: sub esp, 4
08460A0A: lea eax, [ebp - 0x710]
08460A10: lea edx, [ebp - 0x70c]
08460A16: mov dword ptr [esp + 0xc], edx
08460A1A: mov edx, dword ptr [ebp - 0x708]
08460A20: mov dword ptr [esp + 8], edx
08460A24: mov edx, dword ptr [ebp - 0x704]
08460A2A: mov dword ptr [esp + 4], edx
08460A2E: mov dword ptr [esp], eax
08460A31: call 0x815266b ; _ZSt4findIN9__gnu_cxx17__normal_iteratorIPjSt6vectorIjSaIjEEEEjET_S7_S7_RKT0_
08460A36: sub esp, 4
08460A39: lea eax, [ebp - 0x714]
08460A3F: mov dword ptr [esp + 4], eax
08460A43: lea eax, [ebp - 0x710]
08460A49: mov dword ptr [esp], eax
08460A4C: call 0x81112f0 ; _ZN9__gnu_cxxneIPjSt6vectorIjSaIjEEEEbRKNS_17__normal_iteratorIT_T0_EESA_
08460A51: test al, al
08460A53: je 0x8460b1f
08460A59: lea eax, [ebp - 0x700]
08460A5F: lea edx, [ebp - 0xa98]
08460A65: mov dword ptr [esp + 4], edx
08460A69: mov dword ptr [esp], eax
08460A6C: call 0x8151fa0 ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEE5beginEv
08460A71: sub esp, 4
08460A74: lea eax, [ebp - 0x700]
08460A7A: mov dword ptr [esp + 4], eax
08460A7E: lea eax, [ebp - 0xae4]
08460A84: mov dword ptr [esp], eax
08460A87: call 0x8151fc6 ; _ZNSt23_Rb_tree_const_iteratorISt4pairIKiiEEC1ERKSt17_Rb_tree_iteratorIS2_E
08460A8C: lea eax, [ebp - 0x6fc]
08460A92: lea edx, [ebp - 0xa9c]
08460A98: mov dword ptr [esp + 8], edx
08460A9C: lea edx, [ebp - 0xa98]
08460AA2: mov dword ptr [esp + 4], edx
08460AA6: mov dword ptr [esp], eax
08460AA9: call 0x81527f0 ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEE11upper_boundERS3_
08460AAE: sub esp, 4
08460AB1: lea eax, [ebp - 0x6fc]
08460AB7: mov dword ptr [esp + 4], eax
08460ABB: lea eax, [ebp - 0xae8]
08460AC1: mov dword ptr [esp], eax
08460AC4: call 0x8151fc6 ; _ZNSt23_Rb_tree_const_iteratorISt4pairIKiiEEC1ERKSt17_Rb_tree_iteratorIS2_E
08460AC9: mov dword ptr [ebp - 0x24], 0
08460AD0: jmp 0x8460af4
08460AD2: lea eax, [ebp - 0xae4]
08460AD8: mov dword ptr [esp], eax
08460ADB: call 0x80c6f10 ; _ZNKSt23_Rb_tree_const_iteratorISt4pairIKiiEEptEv
08460AE0: mov eax, dword ptr [eax + 4]
08460AE3: add dword ptr [ebp - 0x24], eax
08460AE6: lea eax, [ebp - 0xae4]
08460AEC: mov dword ptr [esp], eax
08460AEF: call 0x8151f82 ; _ZNSt23_Rb_tree_const_iteratorISt4pairIKiiEEppEv
08460AF4: lea eax, [ebp - 0xae8]
08460AFA: mov dword ptr [esp + 4], eax
08460AFE: lea eax, [ebp - 0xae4]
08460B04: mov dword ptr [esp], eax
08460B07: call 0x80c6efc ; _ZNKSt23_Rb_tree_const_iteratorISt4pairIKiiEEneERKS3_
08460B0C: test al, al
08460B0E: jne 0x8460ad2
08460B10: cmp dword ptr [ebp - 0x24], 0
08460B14: jne 0x8460d01
08460B1A: jmp 0x8460d28
08460B1F: mov eax, dword ptr [ebp - 0x28]
08460B22: mov dword ptr [esp], eax
08460B25: call 0x8150850 ; _ZNK12CAICharacter6getIdxEv
08460B2A: mov dword ptr [ebp - 0x6f8], eax
08460B30: lea eax, [ebp - 0x6f8]
08460B36: mov dword ptr [esp + 4], eax
08460B3A: lea eax, [ebp - 0xa80]
08460B40: mov dword ptr [esp], eax
08460B43: call 0x81527c6 ; _ZNSt6vectorIjSaIjEE9push_backEOj
08460B48: mov eax, dword ptr [ebp - 0x28]
08460B4B: mov dword ptr [esp], eax
08460B4E: call 0x8150866 ; _ZNK12CAICharacter18getAppearancePointEv
08460B53: mov dword ptr [ebp - 0x6f4], eax
08460B59: lea eax, [ebp - 0xae0]
08460B5F: lea edx, [ebp - 0x6f4]
08460B65: mov dword ptr [esp + 8], edx
08460B69: lea edx, [ebp - 0xa98]
08460B6F: mov dword ptr [esp + 4], edx
08460B73: mov dword ptr [esp], eax
08460B76: call 0x80cd2ee ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEE4findERS3_
08460B7B: sub esp, 4
08460B7E: lea eax, [ebp - 0x6f0]
08460B84: lea edx, [ebp - 0xa98]
08460B8A: mov dword ptr [esp + 4], edx
08460B8E: mov dword ptr [esp], eax
08460B91: call 0x80cd31a ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEE3endEv
08460B96: sub esp, 4
08460B99: lea eax, [ebp - 0x6f0]
08460B9F: mov dword ptr [esp + 4], eax
08460BA3: lea eax, [ebp - 0xae0]
08460BA9: mov dword ptr [esp], eax
08460BAC: call 0x80da3c8 ; _ZNKSt17_Rb_tree_iteratorISt4pairIKiiEEneERKS3_
08460BB1: test al, al
08460BB3: je 0x8460c06
08460BB5: lea eax, [ebp - 0xae0]
08460BBB: mov dword ptr [esp], eax
08460BBE: call 0x80cd340 ; _ZNKSt17_Rb_tree_iteratorISt4pairIKiiEEptEv
08460BC3: mov ebx, eax
08460BC5: lea eax, [ebp - 0xae0]
08460BCB: mov dword ptr [esp], eax
08460BCE: call 0x80cd340 ; _ZNKSt17_Rb_tree_iteratorISt4pairIKiiEEptEv
08460BD3: mov eax, dword ptr [eax + 4]
08460BD6: sub eax, 1
08460BD9: mov dword ptr [ebp - 0x6ec], eax
08460BDF: mov dword ptr [ebp - 0x6e8], 0
08460BE9: lea eax, [ebp - 0x6ec]
08460BEF: mov dword ptr [esp + 4], eax
08460BF3: lea eax, [ebp - 0x6e8]
08460BF9: mov dword ptr [esp], eax
08460BFC: call 0x808e218 ; _ZSt3maxIiERKT_S2_S2_
08460C01: mov eax, dword ptr [eax]
08460C03: mov dword ptr [ebx + 4], eax
08460C06: mov ebx, dword ptr [ebp - 0xa9c]
08460C0C: mov eax, dword ptr [ebp - 0x28]
08460C0F: mov dword ptr [esp], eax
08460C12: call 0x8150866 ; _ZNK12CAICharacter18getAppearancePointEv
08460C17: mov edx, ebx
08460C19: sub edx, eax
08460C1B: mov eax, edx
08460C1D: mov dword ptr [ebp - 0xa9c], eax
08460C23: mov dword ptr [esp + 8], 0x34c
08460C2B: mov dword ptr [esp + 4], 0
08460C33: lea eax, [ebp - 0xf50]
08460C39: mov dword ptr [esp], eax
08460C3C: call 0x807dcc0 ;
08460C41: mov eax, dword ptr [ebp - 0x3c]
08460C44: mov dword ptr [ebp - 0xf50], eax
08460C4A: mov eax, dword ptr [ebp - 0x28]
08460C4D: mov dword ptr [esp], eax
08460C50: call 0x8150850 ; _ZNK12CAICharacter6getIdxEv
08460C55: mov dword ptr [ebp - 0xf44], eax
08460C5B: mov eax, dword ptr [ebp + 8]
08460C5E: mov eax, dword ptr [eax + 0x48]
08460C61: mov dword ptr [ebp - 0xf4c], eax
08460C67: lea edx, [eax + 1]
08460C6A: mov eax, dword ptr [ebp + 8]
08460C6D: mov dword ptr [eax + 0x48], edx
08460C70: mov byte ptr [ebp - 0xf48], 5
08460C77: mov eax, dword ptr [ebp - 0x28]
08460C7A: mov dword ptr [esp], eax
08460C7D: call 0x815085a ; _ZNK12CAICharacter8getLevelEv
08460C82: mov byte ptr [ebp - 0xf40], al
08460C88: mov dword ptr [ebp - 0xf24], 0x64
08460C92: mov byte ptr [ebp - 0xf3f], 0
08460C99: mov byte ptr [ebp - 0xf3e], 0
08460CA0: lea eax, [ebp - 0xf50]
08460CA6: mov dword ptr [esp + 4], eax
08460CAA: lea eax, [ebp - 0x6e4]
08460CB0: mov dword ptr [esp], eax
08460CB3: call 0x8151aea ; _ZN11map_monsterC1ERKS_
08460CB8: mov eax, dword ptr [ebp + 8]
08460CBB: lea edx, [eax + 0xc]
08460CBE: lea eax, [ebp - 0x6e4]
08460CC4: mov dword ptr [esp + 4], eax
08460CC8: mov dword ptr [esp], edx
08460CCB: call 0x8151612 ; _ZN7MapInfo7Add_MobE11map_monster
08460CD0: jmp 0x8460ced
08460CD2: mov ebx, edx
08460CD4: mov esi, eax
08460CD6: lea eax, [ebp - 0x6e4]
08460CDC: mov dword ptr [esp], eax
08460CDF: call 0x81515ae ; _ZN11map_monsterD1Ev
08460CE4: mov eax, esi
08460CE6: mov edx, ebx
08460CE8: jmp 0x8461462
08460CED: lea eax, [ebp - 0x6e4]
08460CF3: mov dword ptr [esp], eax
08460CF6: call 0x81515ae ; _ZN11map_monsterD1Ev
08460CFB: add dword ptr [ebp - 0x3c], 1
08460CFF: jmp 0x8460d02
08460D01: nop
08460D02: call 0x80cc19b ; _Z14G_CDataManagerv
08460D07: mov eax, dword ptr [eax + 0x869c]
08460D0D: mov dword ptr [esp], eax
08460D10: call 0x8150872 ; _ZN16CAICharacterList8getMinAPEv
08460D15: mov edx, dword ptr [ebp - 0xa9c]
08460D1B: cmp eax, edx
08460D1D: setle al
08460D20: test al, al
08460D22: jne 0x846096b
08460D28: mov dword ptr [ebp - 0x3c], 0
08460D2F: lea eax, [ebp - 0xaa8]
08460D35: mov dword ptr [esp], eax
08460D38: call 0x808e1ac ; _ZNSt6vectorIiSaIiEEC1Ev
08460D3D: lea eax, [ebp - 0xab0]
08460D43: mov edx, dword ptr [ebp + 0x10]
08460D46: mov dword ptr [esp + 4], edx
08460D4A: mov dword ptr [esp], eax
08460D4D: call 0x8134796 ; _ZNK4CMap16get_monster_listEv
08460D52: sub esp, 4
08460D55: lea eax, [ebp - 0x398]
08460D5B: lea edx, [ebp - 0xab0]
08460D61: mov dword ptr [esp + 4], edx
08460D65: mov dword ptr [esp], eax
08460D68: call 0x815281c ; _ZNSt4listI11_mapMonsterSaIS0_EE5beginEv
08460D6D: sub esp, 4
08460D70: lea eax, [ebp - 0x398]
08460D76: mov dword ptr [esp + 4], eax
08460D7A: lea eax, [ebp - 0xab4]
08460D80: mov dword ptr [esp], eax
08460D83: call 0x8152842 ; _ZNSt20_List_const_iteratorI11_mapMonsterEC1ERKSt14_List_iteratorIS0_E
08460D88: jmp 0x8460f81
08460D8D: lea eax, [ebp - 0xab4]
08460D93: mov dword ptr [esp], eax
08460D96: call 0x8139874 ; _ZNKSt20_List_const_iteratorI11_mapMonsterEdeEv
08460D9B: mov dword ptr [ebp - 0x20], eax
08460D9E: mov dword ptr [esp + 8], 0x34c
08460DA6: mov dword ptr [esp + 4], 0
08460DAE: lea eax, [ebp - 0xf50]
08460DB4: mov dword ptr [esp], eax
08460DB7: call 0x807dcc0 ;
08460DBC: mov eax, dword ptr [ebp - 0x3c]
08460DBF: mov dword ptr [ebp - 0xf50], eax
08460DC5: mov eax, dword ptr [ebp - 0x20]
08460DC8: movzx eax, byte ptr [eax + 4]
08460DCC: test al, al
08460DCE: je 0x8460e24
08460DD0: mov eax, dword ptr [ebp + 0xc]
08460DD3: mov dword ptr [esp], eax
08460DD6: call 0x8469ac0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr16getStandardLevelEv
08460DDB: mov edx, eax
08460DDD: mov eax, dword ptr [ebp - 0x20]
08460DE0: mov eax, dword ptr [eax + 8]
08460DE3: lea eax, [edx + eax]
08460DE6: mov byte ptr [ebp - 0xf40], al
08460DEC: movzx eax, byte ptr [ebp - 0xf40]
08460DF3: cmp al, 0xc7
08460DF5: ja 0x8460e05
08460DF7: movzx eax, byte ptr [ebp - 0xf40]
08460DFE: movzx eax, al
08460E01: test eax, eax
08460E03: jle 0x8460e17
08460E05: movzx eax, byte ptr [ebp - 0xf40]
08460E0C: cmp al, 0xc8
08460E0E: jbe 0x8460e1c
08460E10: mov eax, 0xffffffc8
08460E15: jmp 0x8460e1c
08460E17: mov eax, 1
08460E1C: mov byte ptr [ebp - 0xf40], al
08460E22: jmp 0x8460e30
08460E24: mov eax, dword ptr [ebp - 0x20]
08460E27: mov eax, dword ptr [eax + 8]
08460E2A: mov byte ptr [ebp - 0xf40], al
08460E30: mov eax, dword ptr [ebp - 0x20]
08460E33: mov eax, dword ptr [eax]
08460E35: cmp eax, -1
08460E38: jne 0x8460e60
08460E3A: movzx eax, byte ptr [ebp - 0xf40]
08460E41: movzx ebx, al
08460E44: call 0x80cc19b ; _Z14G_CDataManagerv
08460E49: mov eax, dword ptr [eax + 8]
08460E4C: mov dword ptr [esp + 4], ebx
08460E50: mov dword ptr [esp], eax
08460E53: call 0x834ff20 ; _ZN15CMonsterManager19get_levelrandom_mobEi
08460E58: mov dword ptr [ebp - 0xf44], eax
08460E5E: jmp 0x8460e6b
08460E60: mov eax, dword ptr [ebp - 0x20]
08460E63: mov eax, dword ptr [eax]
08460E65: mov dword ptr [ebp - 0xf44], eax
08460E6B: mov eax, dword ptr [ebp - 0x20]
08460E6E: mov eax, dword ptr [eax + 0x10]
08460E71: mov byte ptr [ebp - 0xf3f], al
08460E77: mov eax, dword ptr [ebp - 0x20]
08460E7A: mov eax, dword ptr [eax + 0x14]
08460E7D: mov byte ptr [ebp - 0xf3e], al
08460E83: mov eax, dword ptr [ebp - 0x20]
08460E86: mov eax, dword ptr [eax + 0x18]
08460E89: mov byte ptr [ebp - 0xf48], al
08460E8F: mov eax, dword ptr [ebp + 8]
08460E92: mov eax, dword ptr [eax + 0x48]
08460E95: mov dword ptr [ebp - 0xf4c], eax
08460E9B: lea edx, [eax + 1]
08460E9E: mov eax, dword ptr [ebp + 8]
08460EA1: mov dword ptr [eax + 0x48], edx
08460EA4: movzx eax, byte ptr [ebp - 0xf48]
08460EAB: test al, al
08460EAD: jne 0x8460ecb
08460EAF: lea eax, [ebp - 0xf50]
08460EB5: mov dword ptr [esp], eax
08460EB8: call 0x8151182 ; _ZN11map_monster14isNamedMonsterEv
08460EBD: xor eax, 1
08460EC0: test al, al
08460EC2: je 0x8460ecb
08460EC4: mov eax, 1
08460EC9: jmp 0x8460ed0
08460ECB: mov eax, 0
08460ED0: test al, al
08460ED2: je 0x8460eef
08460ED4: lea eax, [ebp - 0xf50]
08460EDA: add eax, 4
08460EDD: mov dword ptr [esp + 4], eax
08460EE1: lea eax, [ebp - 0xaa8]
08460EE7: mov dword ptr [esp], eax
08460EEA: call 0x8111126 ; _ZNSt6vectorIiSaIiEE9push_backERKi
08460EEF: mov eax, dword ptr [ebp - 0x20]
08460EF2: movzx eax, byte ptr [eax + 0x1c]
08460EF6: mov byte ptr [ebp - 0xf3d], al
08460EFC: mov eax, dword ptr [ebp - 0x20]
08460EFF: mov eax, dword ptr [eax + 0x20]
08460F02: mov byte ptr [ebp - 0xf3c], al
08460F08: mov eax, dword ptr [ebp - 0x20]
08460F0B: mov eax, dword ptr [eax + 0x28]
08460F0E: mov dword ptr [ebp - 0xf24], eax
08460F14: lea eax, [ebp - 0xf50]
08460F1A: mov dword ptr [esp + 4], eax
08460F1E: lea eax, [ebp - 0x38c]
08460F24: mov dword ptr [esp], eax
08460F27: call 0x8151aea ; _ZN11map_monsterC1ERKS_
08460F2C: mov eax, dword ptr [ebp + 8]
08460F2F: lea edx, [eax + 0xc]
08460F32: lea eax, [ebp - 0x38c]
08460F38: mov dword ptr [esp + 4], eax
08460F3C: mov dword ptr [esp], edx
08460F3F: call 0x8151612 ; _ZN7MapInfo7Add_MobE11map_monster
08460F44: jmp 0x8460f61
08460F46: mov ebx, edx
08460F48: mov esi, eax
08460F4A: lea eax, [ebp - 0x38c]
08460F50: mov dword ptr [esp], eax
08460F53: call 0x81515ae ; _ZN11map_monsterD1Ev
08460F58: mov eax, esi
08460F5A: mov edx, ebx
08460F5C: jmp 0x8461412
08460F61: lea eax, [ebp - 0x38c]
08460F67: mov dword ptr [esp], eax
08460F6A: call 0x81515ae ; _ZN11map_monsterD1Ev
08460F6F: lea eax, [ebp - 0xab4]
08460F75: mov dword ptr [esp], eax
08460F78: call 0x8139860 ; _ZNSt20_List_const_iteratorI11_mapMonsterEppEv
08460F7D: add dword ptr [ebp - 0x3c], 1
08460F81: lea eax, [ebp - 0x390]
08460F87: lea edx, [ebp - 0xab0]
08460F8D: mov dword ptr [esp + 4], edx
08460F91: mov dword ptr [esp], eax
08460F94: call 0x813ba0c ; _ZNSt4listI11_mapMonsterSaIS0_EE3endEv
08460F99: sub esp, 4
08460F9C: lea eax, [ebp - 0x390]
08460FA2: mov dword ptr [esp + 4], eax
08460FA6: lea eax, [ebp - 0x394]
08460FAC: mov dword ptr [esp], eax
08460FAF: call 0x8152842 ; _ZNSt20_List_const_iteratorI11_mapMonsterEC1ERKSt14_List_iteratorIS0_E
08460FB4: lea eax, [ebp - 0x394]
08460FBA: mov dword ptr [esp + 4], eax
08460FBE: lea eax, [ebp - 0xab4]
08460FC4: mov dword ptr [esp], eax
08460FC7: call 0x8135dd2 ; _ZNKSt20_List_const_iteratorI11_mapMonsterEneERKS1_
08460FCC: test al, al
08460FCE: jne 0x8460d8d
08460FD4: lea eax, [ebp - 0xac4]
08460FDA: mov dword ptr [esp], eax
08460FDD: call 0x815088e ; _ZN8_mapItemC2Ev
08460FE2: mov eax, dword ptr [ebp + 0xc]
08460FE5: mov dword ptr [esp], eax
08460FE8: call 0x8469aec ; _ZNK8WongWork11CDeathTower11CDungeonMgr28getSpecialPassiveObjectItemsEv
08460FED: mov dword ptr [ebp - 0x34], eax
08460FF0: lea eax, [ebp - 0xb68]
08460FF6: mov dword ptr [esp], eax
08460FF9: call 0x814a706 ; _ZN8WongWork19stGenerateRefData_tC2Ev
08460FFE: lea eax, [ebp - 0xbb0]
08461004: mov dword ptr [esp], eax
08461007: call 0x814a706 ; _ZN8WongWork19stGenerateRefData_tC2Ev
0846100C: mov eax, dword ptr [ebp + 0xc]
0846100F: mov dword ptr [esp], eax
08461012: call 0x8469ad6 ; _ZNK8WongWork11CDeathTower11CDungeonMgr18getObjectDropItemsEv
08461017: mov dword ptr [ebp - 0xb7c], eax
0846101D: lea eax, [ebp - 0xacc]
08461023: mov edx, dword ptr [ebp + 0x10]
08461026: mov dword ptr [esp + 4], edx
0846102A: mov dword ptr [esp], eax
0846102D: call 0x81508c6 ; _ZNK4CMap13get_item_listEv
08461032: sub esp, 4
08461035: lea eax, [ebp - 0xad0]
0846103B: lea edx, [ebp - 0xacc]
08461041: mov dword ptr [esp + 4], edx
08461045: mov dword ptr [esp], eax
08461048: call 0x8152852 ; _ZNSt4listI8_mapItemSaIS0_EE5beginEv
0846104D: sub esp, 4
08461050: jmp 0x8461340
08461055: lea eax, [ebp - 0xad0]
0846105B: mov dword ptr [esp], eax
0846105E: call 0x81528b0 ; _ZNKSt14_List_iteratorI8_mapItemEdeEv
08461063: mov edx, dword ptr [eax]
08461065: mov dword ptr [ebp - 0xac4], edx
0846106B: mov edx, dword ptr [eax + 4]
0846106E: mov dword ptr [ebp - 0xac0], edx
08461074: mov edx, dword ptr [eax + 8]
08461077: mov dword ptr [ebp - 0xabc], edx
0846107D: mov eax, dword ptr [eax + 0xc]
08461080: mov dword ptr [ebp - 0xab8], eax
08461086: mov eax, dword ptr [ebp - 0xac4]
0846108C: mov dword ptr [esp + 4], eax
08461090: mov eax, dword ptr [ebp - 0x34]
08461093: mov dword ptr [esp], eax
08461096: call 0x81528be ; _ZNKSt6vectorI21stDungeonAssignItem_tSaIS0_EE2atEj
0846109B: mov dword ptr [ebp - 0x1c], eax
0846109E: mov eax, dword ptr [ebp - 0x1c]
084610A1: mov eax, dword ptr [eax]
084610A3: cmp eax, -1
084610A6: je 0x84610b5
084610A8: mov eax, dword ptr [ebp - 0x1c]
084610AB: mov eax, dword ptr [eax]
084610AD: mov byte ptr [ebp - 0xb30], al
084610B3: jmp 0x84610fb
084610B5: mov eax, dword ptr [ebp + 0xc]
084610B8: mov dword ptr [esp], eax
084610BB: call 0x8469ac0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr16getStandardLevelEv
084610C0: test eax, eax
084610C2: jle 0x84610d6
084610C4: mov eax, dword ptr [ebp + 0xc]
084610C7: mov dword ptr [esp], eax
084610CA: call 0x8469ac0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr16getStandardLevelEv
084610CF: cmp eax, 0xc8
084610D4: jle 0x84610dd
084610D6: mov eax, 1
084610DB: jmp 0x84610e2
084610DD: mov eax, 0
084610E2: test al, al
084610E4: jne 0x846133f
084610EA: mov eax, dword ptr [ebp + 0xc]
084610ED: mov dword ptr [esp], eax
084610F0: call 0x8469ac0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr16getStandardLevelEv
084610F5: mov byte ptr [ebp - 0xb30], al
084610FB: lea eax, [ebp - 0xb20]
08461101: mov dword ptr [esp], eax
08461104: call 0x814ab06 ; _ZN8WongWork18stGenerateResult_tC1Ev
08461109: mov eax, dword ptr [ebp - 0xac0]
0846110F: mov byte ptr [ebp - 0xb2f], al
08461115: mov eax, dword ptr [ebp - 0x1c]
08461118: add eax, 4
0846111B: mov dword ptr [ebp - 0xb34], eax
08461121: call 0x80cc19b ; _Z14G_CDataManagerv
08461126: mov dword ptr [esp], eax
08461129: call 0x814a6ea ; _ZN12CDataManager16getItemGeneratorEv
0846112E: mov dword ptr [esp + 4], 1
08461136: mov dword ptr [esp], eax
08461139: call 0x814a7d0 ; _ZN8WongWork17CItemGeneratorMgr12getGeneratorENS0_15eGenerateType_tE
0846113E: mov edx, dword ptr [eax]
08461140: add edx, 0xc
08461143: mov ecx, dword ptr [edx]
08461145: lea edx, [ebp - 0xb20]
0846114B: mov dword ptr [esp + 8], edx
0846114F: lea edx, [ebp - 0xb68]
08461155: mov dword ptr [esp + 4], edx
08461159: mov dword ptr [esp], eax
0846115C: call ecx
0846115E: call 0x80cc19b ; _Z14G_CDataManagerv
08461163: mov dword ptr [esp], eax
08461166: call 0x814a6ea ; _ZN12CDataManager16getItemGeneratorEv
0846116B: mov dword ptr [esp + 4], 4
08461173: mov dword ptr [esp], eax
08461176: call 0x814a7d0 ; _ZN8WongWork17CItemGeneratorMgr12getGeneratorENS0_15eGenerateType_tE
0846117B: mov edx, dword ptr [eax]
0846117D: add edx, 0xc
08461180: mov ecx, dword ptr [edx]
08461182: lea edx, [ebp - 0xb20]
08461188: mov dword ptr [esp + 8], edx
0846118C: lea edx, [ebp - 0xbb0]
08461192: mov dword ptr [esp + 4], edx
08461196: mov dword ptr [esp], eax
08461199: call ecx
0846119B: lea eax, [ebp - 0xaec]
084611A1: lea edx, [ebp - 0xb20]
084611A7: mov dword ptr [esp + 4], edx
084611AB: mov dword ptr [esp], eax
084611AE: call 0x814ad04 ; _ZNSt6vectorI10Inven_ItemSaIS0_EE5beginEv
084611B3: sub esp, 4
084611B6: lea eax, [ebp - 0xaf0]
084611BC: lea edx, [ebp - 0xb20]
084611C2: mov dword ptr [esp + 4], edx
084611C6: mov dword ptr [esp], eax
084611C9: call 0x81528ea ; _ZNSt6vectorI10Inven_ItemSaIS0_EE3endEv
084611CE: sub esp, 4
084611D1: jmp 0x84612e9
084611D6: lea eax, [ebp - 0xc04]
084611DC: mov dword ptr [esp], eax
084611DF: call 0x81512cc ; _ZN8map_itemC2Ev
084611E4: mov byte ptr [ebp - 0xc04], 1
084611EB: mov eax, dword ptr [ebp + 8]
084611EE: mov eax, dword ptr [eax + 0x4c]
084611F1: mov dword ptr [ebp - 0xc00], eax
084611F7: lea edx, [eax + 1]
084611FA: mov eax, dword ptr [ebp + 8]
084611FD: mov dword ptr [eax + 0x4c], edx
08461200: mov eax, dword ptr [ebp - 0xab8]
08461206: mov byte ptr [ebp - 0xc03], al
0846120C: lea eax, [ebp - 0xaec]
08461212: mov dword ptr [esp], eax
08461215: call 0x814ad28 ; _ZNK9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEdeEv
0846121A: mov edx, dword ptr [eax]
0846121C: mov dword ptr [ebp - 0xbf4], edx
08461222: mov edx, dword ptr [eax + 4]
08461225: mov dword ptr [ebp - 0xbf0], edx
0846122B: mov edx, dword ptr [eax + 8]
0846122E: mov dword ptr [ebp - 0xbec], edx
08461234: mov edx, dword ptr [eax + 0xc]
08461237: mov dword ptr [ebp - 0xbe8], edx
0846123D: mov edx, dword ptr [eax + 0x10]
08461240: mov dword ptr [ebp - 0xbe4], edx
08461246: mov edx, dword ptr [eax + 0x14]
08461249: mov dword ptr [ebp - 0xbe0], edx
0846124F: mov edx, dword ptr [eax + 0x18]
08461252: mov dword ptr [ebp - 0xbdc], edx
08461258: mov edx, dword ptr [eax + 0x1c]
0846125B: mov dword ptr [ebp - 0xbd8], edx
08461261: mov edx, dword ptr [eax + 0x20]
08461264: mov dword ptr [ebp - 0xbd4], edx
0846126A: mov edx, dword ptr [eax + 0x24]
0846126D: mov dword ptr [ebp - 0xbd0], edx
08461273: mov edx, dword ptr [eax + 0x28]
08461276: mov dword ptr [ebp - 0xbcc], edx
0846127C: mov edx, dword ptr [eax + 0x2c]
0846127F: mov dword ptr [ebp - 0xbc8], edx
08461285: mov edx, dword ptr [eax + 0x30]
08461288: mov dword ptr [ebp - 0xbc4], edx
0846128E: mov edx, dword ptr [eax + 0x34]
08461291: mov dword ptr [ebp - 0xbc0], edx
08461297: mov edx, dword ptr [eax + 0x38]
0846129A: mov dword ptr [ebp - 0xbbc], edx
084612A0: movzx eax, byte ptr [eax + 0x3c]
084612A4: mov byte ptr [ebp - 0xbb8], al
084612AA: mov eax, dword ptr [ebp + 8]
084612AD: add eax, 0xc
084612B0: mov dword ptr [ebp - 0xf5c], eax
084612B6: lea edx, [esp + 4]
084612BA: lea ebx, [ebp - 0xc04]
084612C0: mov eax, 0x15
084612C5: mov edi, edx
084612C7: mov esi, ebx
084612C9: mov ecx, eax
084612CB: rep movsd dword ptr es:[edi], dword ptr [esi]
084612CD: mov eax, dword ptr [ebp - 0xf5c]
084612D3: mov dword ptr [esp], eax
084612D6: call 0x81517e0 ; _ZN7MapInfo8Add_ItemE8map_item
084612DB: lea eax, [ebp - 0xaec]
084612E1: mov dword ptr [esp], eax
084612E4: call 0x815293c ; _ZN9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEppEv
084612E9: lea eax, [ebp - 0xaf0]
084612EF: mov dword ptr [esp + 4], eax
084612F3: lea eax, [ebp - 0xaec]
084612F9: mov dword ptr [esp], eax
084612FC: call 0x8152910 ; _ZN9__gnu_cxxneIP10Inven_ItemSt6vectorIS1_SaIS1_EEEEbRKNS_17__normal_iteratorIT_T0_EESB_
08461301: test al, al
08461303: jne 0x84611d6
08461309: lea eax, [ebp - 0xad0]
0846130F: mov dword ptr [esp], eax
08461312: call 0x8152952 ; _ZNSt14_List_iteratorI8_mapItemEppEv
08461317: lea eax, [ebp - 0xb20]
0846131D: mov dword ptr [esp], eax
08461320: call 0x814ab4c ; _ZN8WongWork18stGenerateResult_tD2Ev
08461325: jmp 0x8461340
08461327: mov ebx, edx
08461329: mov esi, eax
0846132B: lea eax, [ebp - 0xb20]
08461331: mov dword ptr [esp], eax
08461334: call 0x814ab4c ; _ZN8WongWork18stGenerateResult_tD2Ev
08461339: mov eax, esi
0846133B: mov edx, ebx
0846133D: jmp 0x84613aa
0846133F: nop
08461340: lea eax, [ebp - 0x40]
08461343: lea edx, [ebp - 0xacc]
08461349: mov dword ptr [esp + 4], edx
0846134D: mov dword ptr [esp], eax
08461350: call 0x8152878 ; _ZNSt4listI8_mapItemSaIS0_EE3endEv
08461355: sub esp, 4
08461358: lea eax, [ebp - 0x40]
0846135B: mov dword ptr [esp + 4], eax
0846135F: lea eax, [ebp - 0xad0]
08461365: mov dword ptr [esp], eax
08461368: call 0x815289c ; _ZNKSt14_List_iteratorI8_mapItemEneERKS1_
0846136D: test al, al
0846136F: je 0x8461388
08461371: mov eax, dword ptr [ebp + 0x14]
08461374: mov eax, dword ptr [eax]
08461376: mov eax, dword ptr [eax + 0xcd8]
0846137C: cmp eax, 1
0846137F: je 0x8461388
08461381: mov eax, 1
08461386: jmp 0x846138d
08461388: mov eax, 0
0846138D: test al, al
0846138F: jne 0x8461055
08461395: mov ebx, 1
0846139A: lea eax, [ebp - 0xacc]
084613A0: mov dword ptr [esp], eax
084613A3: call 0x81508b2 ; _ZNSt4listI8_mapItemSaIS0_EED2Ev
084613A8: jmp 0x84613da
084613AA: mov ebx, edx
084613AC: mov esi, eax
084613AE: lea eax, [ebp - 0xacc]
084613B4: mov dword ptr [esp], eax
084613B7: call 0x81508b2 ; _ZNSt4listI8_mapItemSaIS0_EED2Ev
084613BC: mov eax, esi
084613BE: mov edx, ebx
084613C0: jmp 0x84613c2
084613C2: mov ebx, edx
084613C4: mov esi, eax
084613C6: lea eax, [ebp - 0xbb0]
084613CC: mov dword ptr [esp], eax
084613CF: call 0x814aaf0 ; _ZN8WongWork19stGenerateRefData_tD2Ev
084613D4: mov eax, esi
084613D6: mov edx, ebx
084613D8: jmp 0x84613ea
084613DA: lea eax, [ebp - 0xbb0]
084613E0: mov dword ptr [esp], eax
084613E3: call 0x814aaf0 ; _ZN8WongWork19stGenerateRefData_tD2Ev
084613E8: jmp 0x8461402
084613EA: mov ebx, edx
084613EC: mov esi, eax
084613EE: lea eax, [ebp - 0xb68]
084613F4: mov dword ptr [esp], eax
084613F7: call 0x814aaf0 ; _ZN8WongWork19stGenerateRefData_tD2Ev
084613FC: mov eax, esi
084613FE: mov edx, ebx
08461400: jmp 0x8461412
08461402: lea eax, [ebp - 0xb68]
08461408: mov dword ptr [esp], eax
0846140B: call 0x814aaf0 ; _ZN8WongWork19stGenerateRefData_tD2Ev
08461410: jmp 0x846142a
08461412: mov ebx, edx
08461414: mov esi, eax
08461416: lea eax, [ebp - 0xab0]
0846141C: mov dword ptr [esp], eax
0846141F: call 0x8134782 ; _ZNSt4listI11_mapMonsterSaIS0_EED1Ev
08461424: mov eax, esi
08461426: mov edx, ebx
08461428: jmp 0x846143a
0846142A: lea eax, [ebp - 0xab0]
08461430: mov dword ptr [esp], eax
08461433: call 0x8134782 ; _ZNSt4listI11_mapMonsterSaIS0_EED1Ev
08461438: jmp 0x8461452
0846143A: mov ebx, edx
0846143C: mov esi, eax
0846143E: lea eax, [ebp - 0xaa8]
08461444: mov dword ptr [esp], eax
08461447: call 0x8083dda ; _ZNSt6vectorIiSaIiEED1Ev
0846144C: mov eax, esi
0846144E: mov edx, ebx
08461450: jmp 0x8461462
08461452: lea eax, [ebp - 0xaa8]
08461458: mov dword ptr [esp], eax
0846145B: call 0x8083dda ; _ZNSt6vectorIiSaIiEED1Ev
08461460: jmp 0x846147a
08461462: mov ebx, edx
08461464: mov esi, eax
08461466: lea eax, [ebp - 0xf50]
0846146C: mov dword ptr [esp], eax
0846146F: call 0x81515ae ; _ZN11map_monsterD1Ev
08461474: mov eax, esi
08461476: mov edx, ebx
08461478: jmp 0x846148a
0846147A: lea eax, [ebp - 0xf50]
08461480: mov dword ptr [esp], eax
08461483: call 0x81515ae ; _ZN11map_monsterD1Ev
08461488: jmp 0x84614a2
0846148A: mov ebx, edx
0846148C: mov esi, eax
0846148E: lea eax, [ebp - 0xa98]
08461494: mov dword ptr [esp], eax
08461497: call 0x80c6ac6 ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEED1Ev
0846149C: mov eax, esi
0846149E: mov edx, ebx
084614A0: jmp 0x84614b2
084614A2: lea eax, [ebp - 0xa98]
084614A8: mov dword ptr [esp], eax
084614AB: call 0x80c6ac6 ; _ZNSt3mapIiiSt4lessIiESaISt4pairIKiiEEED1Ev
084614B0: jmp 0x84614d0
084614B2: mov ebx, edx
084614B4: mov esi, eax
084614B6: lea eax, [ebp - 0xa80]
084614BC: mov dword ptr [esp], eax
084614BF: call 0x80e2492 ; _ZNSt6vectorIjSaIjEED2Ev
084614C4: mov eax, esi
084614C6: mov edx, ebx
084614C8: mov dword ptr [esp], eax
084614CB: call 0x8ae3750 ; _Unwind_Resume
084614D0: lea eax, [ebp - 0xa80]
084614D6: mov dword ptr [esp], eax
084614D9: call 0x80e2492 ; _ZNSt6vectorIjSaIjEED2Ev
084614DE: mov eax, ebx
084614E0: lea esp, [ebp - 0xc]
084614E3: add esp, 0
084614E6: pop ebx
084614E7: pop esi
084614E8: pop edi
084614E9: pop ebp
084614EA: ret
```

# 70 reference: selected DeathTower function disassembly

Only direct-call targets are annotated. This does not prove A21 behavior.

## 0x082A74BC _ZN8WongWork11CDeathTower6CStage17resetCurrentStageEv

```asm
082A74BC: push     ebp
082A74BD: mov      ebp, esp
082A74BF: mov      eax, dword ptr [ebp + 8]
082A74C2: mov      dword ptr [eax], 1
082A74C8: pop      ebp
082A74C9: ret
```

## 0x084617DA _ZN8WongWork11CDeathTower6CStage13moveNextStageEj

```asm
084617DA: push     ebp
084617DB: mov      ebp, esp
084617DD: sub      esp, 0x18
084617E0: mov      eax, dword ptr [ebp + 8]
084617E3: mov      dword ptr [esp], eax
084617E6: call     0x8461b3e ; _ZN8WongWork11CDeathTower6CStage15checkClearStageEv
084617EB: xor      eax, 1
084617EE: test     al, al
084617F0: jne      0x84617fc
084617F2: mov      eax, dword ptr [ebp + 8]
084617F5: mov      eax, dword ptr [eax]
084617F7: cmp      eax, dword ptr [ebp + 0xc]
084617FA: jb       0x8461803
084617FC: mov      eax, 1
08461801: jmp      0x8461808
08461803: mov      eax, 0
08461808: test     al, al
0846180A: je       0x8461813
0846180C: mov      eax, 0
08461811: jmp      0x8461825
08461813: mov      eax, dword ptr [ebp + 8]
08461816: mov      eax, dword ptr [eax]
08461818: lea      edx, [eax + 1]
0846181B: mov      eax, dword ptr [ebp + 8]
0846181E: mov      dword ptr [eax], edx
08461820: mov      eax, 1
08461825: leave
08461826: ret
```

## 0x08461B3E _ZN8WongWork11CDeathTower6CStage15checkClearStageEv

```asm
08461B3E: push     ebp
08461B3F: mov      ebp, esp
08461B41: push     esi
08461B42: push     ebx
08461B43: sub      esp, 0x370
08461B49: lea      eax, [ebp - 0x35c]
08461B4F: mov      dword ptr [esp], eax
08461B52: call     0x81511c8 ; _ZN11map_monsterC2Ev
08461B57: mov      eax, dword ptr [ebp + 8]
08461B5A: lea      edx, [eax + 0x18]
08461B5D: lea      eax, [ebp - 0x10]
08461B60: mov      dword ptr [esp + 4], edx
08461B64: mov      dword ptr [esp], eax
08461B67: call     0x8152608 ; _ZNSt3mapIi11map_monsterSt4lessIiESaISt4pairIKiS0_EEE5beginEv
08461B6C: sub      esp, 4
08461B6F: jmp      0x8461bb9
08461B71: lea      eax, [ebp - 0x10]
08461B74: mov      dword ptr [esp], eax
08461B77: call     0x815264c ; _ZNKSt17_Rb_tree_iteratorISt4pairIKi11map_monsterEEptEv
08461B7C: add      eax, 4
08461B7F: mov      dword ptr [esp + 4], eax
08461B83: lea      eax, [ebp - 0x35c]
08461B89: mov      dword ptr [esp], eax
08461B8C: call     0x8151d3a ; _ZN11map_monsteraSERKS_
08461B91: mov      eax, dword ptr [ebp - 0x330]
08461B97: cmp      eax, 0x64
08461B9A: jne      0x8461bae
08461B9C: movzx    eax, byte ptr [ebp - 0x349]
08461BA3: test     al, al
08461BA5: jne      0x8461bae
08461BA7: mov      ebx, 0
08461BAC: jmp      0x8461c0c
08461BAE: lea      eax, [ebp - 0x10]
08461BB1: mov      dword ptr [esp], eax
08461BB4: call     0x815262e ; _ZNSt17_Rb_tree_iteratorISt4pairIKi11map_monsterEEppEv
08461BB9: mov      eax, dword ptr [ebp + 8]
08461BBC: lea      edx, [eax + 0x18]
08461BBF: lea      eax, [ebp - 0xc]
08461BC2: mov      dword ptr [esp + 4], edx
08461BC6: mov      dword ptr [esp], eax
08461BC9: call     0x8152486 ; _ZNSt3mapIi11map_monsterSt4lessIiESaISt4pairIKiS0_EEE3endEv
08461BCE: sub      esp, 4
08461BD1: lea      eax, [ebp - 0xc]
08461BD4: mov      dword ptr [esp + 4], eax
08461BD8: lea      eax, [ebp - 0x10]
08461BDB: mov      dword ptr [esp], eax
08461BDE: call     0x81524ac ; _ZNKSt17_Rb_tree_iteratorISt4pairIKi11map_monsterEEneERKS4_
08461BE3: test     al, al
08461BE5: jne      0x8461b71
08461BE7: mov      ebx, 1
08461BEC: jmp      0x8461c0c
08461BEE: mov      ebx, edx
08461BF0: mov      esi, eax
08461BF2: lea      eax, [ebp - 0x35c]
08461BF8: mov      dword ptr [esp], eax
08461BFB: call     0x81515ae ; _ZN11map_monsterD1Ev
08461C00: mov      eax, esi
08461C02: mov      edx, ebx
08461C04: mov      dword ptr [esp], eax
08461C07: call     0x8ae3750 ; _Unwind_Resume
08461C0C: lea      eax, [ebp - 0x35c]
08461C12: mov      dword ptr [esp], eax
08461C15: call     0x81515ae ; _ZN11map_monsterD1Ev
08461C1A: mov      eax, ebx
08461C1C: lea      esp, [ebp - 8]
08461C1F: add      esp, 0
08461C22: pop      ebx
08461C23: pop      esi
08461C24: pop      ebp
08461C25: ret
```

## 0x08461C5A _ZN8WongWork11CDeathTower6CStage8setStateENS0_16ENUM_STAGE_STATEE

```asm
08461C5A: push     ebp
08461C5B: mov      ebp, esp
08461C5D: mov      eax, dword ptr [ebp + 0xc]
08461C60: cmp      eax, 1
08461C63: je       0x8461c6c
08461C65: cmp      eax, 2
08461C68: je       0x8461c80
08461C6A: jmp      0x8461c95
08461C6C: mov      eax, dword ptr [ebp + 8]
08461C6F: mov      eax, dword ptr [eax + 0xf8]
08461C75: test     eax, eax
08461C77: je       0x8461c9c
08461C79: mov      eax, 0
08461C7E: jmp      0x8461cb1
08461C80: mov      eax, dword ptr [ebp + 8]
08461C83: mov      eax, dword ptr [eax + 0xf8]
08461C89: cmp      eax, 1
08461C8C: je       0x8461c9f
08461C8E: mov      eax, 0
08461C93: jmp      0x8461cb1
08461C95: mov      eax, 0
08461C9A: jmp      0x8461cb1
08461C9C: nop
08461C9D: jmp      0x8461ca0
08461C9F: nop
08461CA0: mov      eax, dword ptr [ebp + 8]
08461CA3: mov      edx, dword ptr [ebp + 0xc]
08461CA6: mov      dword ptr [eax + 0xf8], edx
08461CAC: mov      eax, 1
08461CB1: pop      ebp
08461CB2: ret
```

## 0x08461FC0 _ZN8WongWork11CDeathTower9CPlayData18generateRewardItemEiiRKNS0_11CDungeonMgrEjjP6CParty

```asm
08461FC0: push     ebp
08461FC1: mov      ebp, esp
08461FC3: push     edi
08461FC4: push     esi
08461FC5: push     ebx
08461FC6: sub      esp, 0x16c
08461FCC: mov      eax, dword ptr [ebp + 0xc]
08461FCF: mov      edx, dword ptr [ebp + 8]
08461FD2: imul     eax, eax, 0x268
08461FD8: lea      eax, [edx + eax]
08461FDB: lea      edx, [eax + 0x18]
08461FDE: mov      eax, dword ptr [ebp + 0x10]
08461FE1: mov      dword ptr [edx], eax
08461FE3: mov      dword ptr [ebp - 0x24], 0
08461FEA: jmp      0x846251c
08461FEF: lea      eax, [ebp - 0x140]
08461FF5: mov      dword ptr [esp], eax
08461FF8: call     0x814a706 ; _ZN8WongWork19stGenerateRefData_tC2Ev
08461FFD: lea      eax, [ebp - 0x88]
08462003: mov      dword ptr [esp], eax
08462006: call     0x814ab06 ; _ZN8WongWork18stGenerateResult_tC1Ev
0846200B: cmp      dword ptr [ebp + 0x20], 0
0846200F: je       0x8462031
08462011: mov      dword ptr [esp + 8], 0
08462019: lea      eax, [ebp - 0x88]
0846201F: add      eax, 0xc
08462022: mov      dword ptr [esp + 4], eax
08462026: mov      eax, dword ptr [ebp + 0x20]
08462029: mov      dword ptr [esp], eax
0846202C: call     0x85b61be ; _ZN6CParty12GetLuckPointEP10CLuckPointb
08462031: mov      dword ptr [esp], 0x3e8
08462038: call     0x86b1b87 ; _Z12get_rand_inti
0846203D: mov      dword ptr [ebp - 0x20], eax
08462040: call     0x80cc19b ; _Z14G_CDataManagerv
08462045: mov      eax, dword ptr [eax + 0x5e20]
0846204B: imul     eax, dword ptr [ebp + 0x1c]
0846204F: cmp      eax, dword ptr [ebp - 0x20]
08462052: seta     al
08462055: test     al, al
08462057: je       0x846229f
0846205D: mov      byte ptr [ebp - 0x10b], 0
08462064: mov      eax, dword ptr [ebp + 0x18]
08462067: mov      byte ptr [ebp - 0x10c], al
0846206D: mov      byte ptr [ebp - 0x10a], 1
08462074: mov      dword ptr [ebp - 0x108], 1
0846207E: mov      word ptr [ebp - 0x104], 1
08462087: mov      word ptr [ebp - 0x102], 1
08462090: mov      word ptr [ebp - 0x100], 1
08462099: mov      word ptr [ebp - 0xfe], 1
084620A2: lea      eax, [ebp - 0xb8]
084620A8: mov      dword ptr [esp], eax
084620AB: call     0x814ab06 ; _ZN8WongWork18stGenerateResult_tC1Ev
084620B0: call     0x80cc19b ; _Z14G_CDataManagerv
084620B5: mov      dword ptr [esp], eax
084620B8: call     0x814a6ea ; _ZN12CDataManager16getItemGeneratorEv
084620BD: mov      dword ptr [esp + 4], 2
084620C5: mov      dword ptr [esp], eax
084620C8: call     0x814a7d0 ; _ZN8WongWork17CItemGeneratorMgr12getGeneratorENS0_15eGenerateType_tE
084620CD: mov      edx, dword ptr [eax]
084620CF: add      edx, 0xc
084620D2: mov      ecx, dword ptr [edx]
084620D4: lea      edx, [ebp - 0xb8]
084620DA: mov      dword ptr [esp + 8], edx
084620DE: lea      edx, [ebp - 0x140]
084620E4: mov      dword ptr [esp + 4], edx
084620E8: mov      dword ptr [esp], eax
084620EB: call     ecx
084620ED: cmp      dword ptr [ebp + 0x20], 0
084620F1: je       0x846218c
084620F7: lea      eax, [ebp - 0x34]
084620FA: lea      edx, [ebp - 0xb8]
08462100: mov      dword ptr [esp + 4], edx
08462104: mov      dword ptr [esp], eax
08462107: call     0x814ad04 ; _ZNSt6vectorI10Inven_ItemSaIS0_EE5beginEv
0846210C: sub      esp, 4
0846210F: mov      eax, dword ptr [ebp + 0x20]
08462112: mov      dword ptr [esp + 4], eax
08462116: lea      eax, [ebp - 0x3c]
08462119: mov      dword ptr [esp], eax
0846211C: call     0x8469976 ; _ZN10QuickParty16RewardData_ParamC1EP6CParty
08462121: mov      eax, dword ptr [ebp + 0x14]
08462124: mov      dword ptr [esp], eax
08462127: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846212C: mov      dword ptr [esp], eax
0846212F: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08462134: mov      dword ptr [ebp - 0x38], eax
08462137: lea      eax, [ebp - 0x58]
0846213A: mov      dword ptr [esp], eax
0846213D: call     0x84699c6 ; _ZN10QuickParty17RewardData_ResultC2Ev
08462142: lea      eax, [ebp - 0x34]
08462145: mov      dword ptr [esp], eax
08462148: call     0x814ad28 ; _ZNK9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEdeEv
0846214D: mov      eax, dword ptr [eax + 7]
08462150: mov      dword ptr [ebp - 0x58], eax
08462153: lea      eax, [ebp - 0x58]
08462156: mov      dword ptr [esp + 0x10], eax
0846215A: mov      eax, dword ptr [ebp - 0x3c]
0846215D: mov      edx, dword ptr [ebp - 0x38]
08462160: mov      dword ptr [esp + 8], eax
08462164: mov      dword ptr [esp + 0xc], edx
08462168: mov      dword ptr [esp + 4], 0xb
08462170: mov      eax, dword ptr [ebp + 0x20]
08462173: mov      dword ptr [esp], eax
08462176: call     0x859b992 ; _ZN6CParty16set_basic_rewardEN10QuickParty15BasicRewardTypeENS0_16RewardData_ParamERNS0_17RewardData_ResultE
0846217B: lea      eax, [ebp - 0x34]
0846217E: mov      dword ptr [esp], eax
08462181: call     0x814ad28 ; _ZNK9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEdeEv
08462186: mov      edx, dword ptr [ebp - 0x58]
08462189: mov      dword ptr [eax + 7], edx
0846218C: lea      eax, [ebp - 0x30]
0846218F: lea      edx, [ebp - 0xb8]
08462195: mov      dword ptr [esp + 4], edx
08462199: mov      dword ptr [esp], eax
0846219C: call     0x814ad04 ; _ZNSt6vectorI10Inven_ItemSaIS0_EE5beginEv
084621A1: sub      esp, 4
084621A4: lea      eax, [ebp - 0x30]
084621A7: mov      dword ptr [esp], eax
084621AA: call     0x814ad28 ; _ZNK9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEdeEv
084621AF: mov      edx, dword ptr [ebp + 0xc]
084621B2: mov      ecx, dword ptr [ebp - 0x24]
084621B5: imul     ecx, ecx, 0x3d
084621B8: imul     edx, edx, 0x268
084621BE: lea      edx, [ecx + edx]
084621C1: add      edx, 0x10
084621C4: add      edx, dword ptr [ebp + 8]
084621C7: add      edx, 0xc
084621CA: mov      dword ptr [esp + 4], eax
084621CE: mov      dword ptr [esp], edx
084621D1: call     0x814a62e ; _ZN10Inven_Item7setCopyERKS_
084621D6: mov      eax, dword ptr [ebp + 0xc]
084621D9: mov      edx, dword ptr [ebp - 0x24]
084621DC: imul     edx, edx, 0x3d
084621DF: imul     eax, eax, 0x268
084621E5: lea      eax, [edx + eax]
084621E8: add      eax, 0x10
084621EB: add      eax, dword ptr [ebp + 8]
084621EE: add      eax, 0xc
084621F1: mov      dword ptr [esp], eax
084621F4: call     0x80f783a ; _ZNK10Inven_Item12get_add_infoEv
084621F9: mov      dword ptr [ebp - 0x150], eax
084621FF: fild     dword ptr [ebp - 0x150]
08462205: fstp     dword ptr [ebp - 0x154]
0846220B: call     0x80cc19b ; _Z14G_CDataManagerv
08462210: fld      dword ptr [eax + 0x5e2c]
08462216: fmul     dword ptr [ebp - 0x154]
0846221C: fnstcw   word ptr [ebp - 0x14a]
08462222: movzx    eax, word ptr [ebp - 0x14a]
08462229: mov      ah, 0xc
0846222B: mov      word ptr [ebp - 0x14c], ax
08462232: fldcw    word ptr [ebp - 0x14c]
08462238: fistp    dword ptr [ebp - 0x150]
0846223E: fldcw    word ptr [ebp - 0x14a]
08462244: mov      eax, dword ptr [ebp - 0x150]
0846224A: mov      edx, dword ptr [ebp + 0xc]
0846224D: mov      ecx, dword ptr [ebp - 0x24]
08462250: imul     ecx, ecx, 0x3d
08462253: imul     edx, edx, 0x268
08462259: lea      edx, [ecx + edx]
0846225C: add      edx, 0x10
0846225F: add      edx, dword ptr [ebp + 8]
08462262: add      edx, 0xc
08462265: mov      dword ptr [esp + 4], eax
08462269: mov      dword ptr [esp], edx
0846226C: call     0x80cb884 ; _ZN10Inven_Item12set_add_infoEi
08462271: lea      eax, [ebp - 0xb8]
08462277: mov      dword ptr [esp], eax
0846227A: call     0x814ab4c ; _ZN8WongWork18stGenerateResult_tD2Ev
0846227F: jmp      0x84624bb
08462284: mov      ebx, edx
08462286: mov      esi, eax
08462288: lea      eax, [ebp - 0xb8]
0846228E: mov      dword ptr [esp], eax
08462291: call     0x814ab4c ; _ZN8WongWork18stGenerateResult_tD2Ev
08462296: mov      eax, esi
08462298: mov      edx, ebx
0846229A: jmp      0x84624c2
0846229F: call     0x80cc19b ; _Z14G_CDataManagerv
084622A4: mov      eax, dword ptr [eax + 0x5e24]
084622AA: imul     eax, dword ptr [ebp + 0x1c]
084622AE: cmp      eax, dword ptr [ebp - 0x20]
084622B1: seta     al
084622B4: test     al, al
084622B6: je       0x84624bb
084622BC: mov      byte ptr [ebp - 0x10b], 0
084622C3: mov      eax, dword ptr [ebp + 0x18]
084622C6: mov      byte ptr [ebp - 0x10c], al
084622CC: mov      eax, 0x3f800000
084622D1: mov      dword ptr [ebp - 0x108], eax
084622D7: mov      eax, dword ptr [ebp + 0x14]
084622DA: mov      dword ptr [esp], eax
084622DD: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
084622E2: mov      dword ptr [ebp - 0x100], eax
084622E8: mov      dword ptr [ebp - 0x104], 0
084622F2: call     0x80cc19b ; _Z14G_CDataManagerv
084622F7: mov      dword ptr [esp], eax
084622FA: call     0x814a6ea ; _ZN12CDataManager16getItemGeneratorEv
084622FF: mov      dword ptr [esp + 4], 2
08462307: mov      dword ptr [esp], eax
0846230A: call     0x814a7d0 ; _ZN8WongWork17CItemGeneratorMgr12getGeneratorENS0_15eGenerateType_tE
0846230F: mov      edx, dword ptr [eax]
08462311: add      edx, 8
08462314: mov      ecx, dword ptr [edx]
08462316: lea      edx, [ebp - 0x88]
0846231C: mov      dword ptr [esp + 8], edx
08462320: lea      edx, [ebp - 0x140]
08462326: mov      dword ptr [esp + 4], edx
0846232A: mov      dword ptr [esp], eax
0846232D: call     ecx
0846232F: lea      eax, [ebp - 0x88]
08462335: mov      dword ptr [esp], eax
08462338: call     0x814ad32 ; _ZNKSt6vectorI10Inven_ItemSaIS0_EE5emptyEv
0846233D: xor      eax, 1
08462340: test     al, al
08462342: je       0x84624bb
08462348: lea      eax, [ebp - 0x2c]
0846234B: lea      edx, [ebp - 0x88]
08462351: mov      dword ptr [esp + 4], edx
08462355: mov      dword ptr [esp], eax
08462358: call     0x814ad04 ; _ZNSt6vectorI10Inven_ItemSaIS0_EE5beginEv
0846235D: sub      esp, 4
08462360: lea      eax, [ebp - 0x2c]
08462363: mov      dword ptr [esp], eax
08462366: call     0x814ad28 ; _ZNK9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEdeEv
0846236B: mov      edx, dword ptr [ebp + 0xc]
0846236E: mov      ecx, dword ptr [ebp - 0x24]
08462371: imul     ecx, ecx, 0x3d
08462374: imul     edx, edx, 0x268
0846237A: lea      edx, [ecx + edx]
0846237D: add      edx, 0x10
08462380: add      edx, dword ptr [ebp + 8]
08462383: add      edx, 0xc
08462386: mov      dword ptr [esp + 4], eax
0846238A: mov      dword ptr [esp], edx
0846238D: call     0x814a62e ; _ZN10Inven_Item7setCopyERKS_
08462392: lea      eax, [ebp - 0x28]
08462395: lea      edx, [ebp - 0x88]
0846239B: mov      dword ptr [esp + 4], edx
0846239F: mov      dword ptr [esp], eax
084623A2: call     0x814ad04 ; _ZNSt6vectorI10Inven_ItemSaIS0_EE5beginEv
084623A7: sub      esp, 4
084623AA: lea      eax, [ebp - 0x28]
084623AD: mov      dword ptr [esp], eax
084623B0: call     0x814ad28 ; _ZNK9__gnu_cxx17__normal_iteratorIP10Inven_ItemSt6vectorIS1_SaIS1_EEEdeEv
084623B5: mov      edx, dword ptr [eax]
084623B7: mov      dword ptr [ebp - 0xf5], edx
084623BD: mov      edx, dword ptr [eax + 4]
084623C0: mov      dword ptr [ebp - 0xf1], edx
084623C6: mov      edx, dword ptr [eax + 8]
084623C9: mov      dword ptr [ebp - 0xed], edx
084623CF: mov      edx, dword ptr [eax + 0xc]
084623D2: mov      dword ptr [ebp - 0xe9], edx
084623D8: mov      edx, dword ptr [eax + 0x10]
084623DB: mov      dword ptr [ebp - 0xe5], edx
084623E1: mov      edx, dword ptr [eax + 0x14]
084623E4: mov      dword ptr [ebp - 0xe1], edx
084623EA: mov      edx, dword ptr [eax + 0x18]
084623ED: mov      dword ptr [ebp - 0xdd], edx
084623F3: mov      edx, dword ptr [eax + 0x1c]
084623F6: mov      dword ptr [ebp - 0xd9], edx
084623FC: mov      edx, dword ptr [eax + 0x20]
084623FF: mov      dword ptr [ebp - 0xd5], edx
08462405: mov      edx, dword ptr [eax + 0x24]
08462408: mov      dword ptr [ebp - 0xd1], edx
0846240E: mov      edx, dword ptr [eax + 0x28]
08462411: mov      dword ptr [ebp - 0xcd], edx
08462417: mov      edx, dword ptr [eax + 0x2c]
0846241A: mov      dword ptr [ebp - 0xc9], edx
08462420: mov      edx, dword ptr [eax + 0x30]
08462423: mov      dword ptr [ebp - 0xc5], edx
08462429: mov      edx, dword ptr [eax + 0x34]
0846242C: mov      dword ptr [ebp - 0xc1], edx
08462432: mov      edx, dword ptr [eax + 0x38]
08462435: mov      dword ptr [ebp - 0xbd], edx
0846243B: movzx    eax, byte ptr [eax + 0x3c]
0846243F: mov      byte ptr [ebp - 0xb9], al
08462445: mov      eax, dword ptr [ebp - 0xf3]
0846244B: mov      ebx, eax
0846244D: call     0x80cc19b ; _Z14G_CDataManagerv
08462452: mov      eax, dword ptr [eax + 0xc]
08462455: mov      dword ptr [esp + 4], ebx
08462459: mov      dword ptr [esp], eax
0846245C: call     0x8511e8a ; _ZN9CItemList9find_itemEi
08462461: mov      dword ptr [ebp - 0x1c], eax
08462464: cmp      dword ptr [ebp - 0x1c], 0
08462468: jne      0x8462471
0846246A: mov      ebx, 0
0846246F: jmp      0x84624da
08462471: mov      eax, dword ptr [ebp - 0x1c]
08462474: mov      dword ptr [esp], eax
08462477: call     0x8110c54 ; _ZNK5CItem9get_gradeEv
0846247C: mov      ebx, eax
0846247E: mov      eax, dword ptr [ebp - 0x1c]
08462481: mov      dword ptr [esp], eax
08462484: call     0x80f12d6 ; _ZNK5CItem10get_rarityEv
08462489: mov      edi, eax
0846248B: movzx    eax, byte ptr [ebp - 0xf4]
08462492: movzx    esi, al
08462495: mov      eax, dword ptr [ebp + 0xc]
08462498: mov      dword ptr [esp + 4], eax
0846249C: mov      eax, dword ptr [ebp + 0x20]
0846249F: mov      dword ptr [esp], eax
084624A2: call     0x8145764 ; _ZN6CParty8get_userEi
084624A7: mov      dword ptr [esp + 0xc], ebx
084624AB: mov      dword ptr [esp + 8], edi
084624AF: mov      dword ptr [esp + 4], esi
084624B3: mov      dword ptr [esp], eax
084624B6: call     0x8550a84 ; _ZN10CLuckPoint12UseLuckPointEP5CUseriii
084624BB: mov      ebx, 1
084624C0: jmp      0x84624da
084624C2: mov      ebx, edx
084624C4: mov      esi, eax
084624C6: lea      eax, [ebp - 0x88]
084624CC: mov      dword ptr [esp], eax
084624CF: call     0x814ab4c ; _ZN8WongWork18stGenerateResult_tD2Ev
084624D4: mov      eax, esi
084624D6: mov      edx, ebx
084624D8: jmp      0x84624fe
084624DA: lea      eax, [ebp - 0x88]
084624E0: mov      dword ptr [esp], eax
084624E3: call     0x814ab4c ; _ZN8WongWork18stGenerateResult_tD2Ev
084624E8: test     ebx, ebx
084624EA: lea      eax, [ebp - 0x140]
084624F0: mov      dword ptr [esp], eax
084624F3: call     0x814aaf0 ; _ZN8WongWork19stGenerateRefData_tD2Ev
084624F8: add      dword ptr [ebp - 0x24], 1
084624FC: jmp      0x846251c
084624FE: mov      ebx, edx
08462500: mov      esi, eax
08462502: lea      eax, [ebp - 0x140]
08462508: mov      dword ptr [esp], eax
0846250B: call     0x814aaf0 ; _ZN8WongWork19stGenerateRefData_tD2Ev
08462510: mov      eax, esi
08462512: mov      edx, ebx
08462514: mov      dword ptr [esp], eax
08462517: call     0x8ae3750 ; _Unwind_Resume
0846251C: mov      eax, dword ptr [ebp - 0x24]
0846251F: cmp      eax, dword ptr [ebp + 0x10]
08462522: setl     al
08462525: test     al, al
08462527: jne      0x8461fef
0846252D: lea      esp, [ebp - 0xc]
08462530: add      esp, 0
08462533: pop      ebx
08462534: pop      esi
08462535: pop      edi
08462536: pop      ebp
08462537: ret
```

## 0x08462538 _ZN8WongWork11CDeathTower9CPlayData17generateRewardExpEiijP6CPartyRNS0_11CDungeonMgrE

```asm
08462538: push     ebp
08462539: mov      ebp, esp
0846253B: push     esi
0846253C: push     ebx
0846253D: sub      esp, 0x130
08462543: mov      eax, dword ptr [ebp + 0xc]
08462546: mov      dword ptr [esp + 4], eax
0846254A: mov      eax, dword ptr [ebp + 0x18]
0846254D: mov      dword ptr [esp], eax
08462550: call     0x8145764 ; _ZN6CParty8get_userEi
08462555: mov      dword ptr [ebp - 0x18], eax
08462558: mov      dword ptr [ebp - 0x1c], 0
0846255F: mov      eax, dword ptr [ebp + 0x1c]
08462562: mov      dword ptr [esp], eax
08462565: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846256A: mov      dword ptr [esp], eax
0846256D: call     0x822b4ce ; _ZNK8CDungeon27limitOfStackableItemInTowerEv
08462572: test     al, al
08462574: je       0x84625eb
08462576: mov      eax, dword ptr [ebp + 0x10]
08462579: imul     eax, dword ptr [ebp + 0x14]
0846257D: mov      edx, 0
08462582: mov      dword ptr [ebp - 0xd8], eax
08462588: mov      dword ptr [ebp - 0xd4], edx
0846258E: fild     qword ptr [ebp - 0xd8]
08462594: fstp     dword ptr [ebp - 0xe4]
0846259A: call     0x80cc19b ; _Z14G_CDataManagerv
0846259F: mov      edx, dword ptr [ebp + 0x10]
084625A2: add      edx, 0x1464
084625A8: fld      dword ptr [eax + edx*4 + 0xc]
084625AC: fmul     dword ptr [ebp - 0xe4]
084625B2: fnstcw   word ptr [ebp - 0xda]
084625B8: movzx    eax, word ptr [ebp - 0xda]
084625BF: mov      ah, 0xc
084625C1: mov      word ptr [ebp - 0xdc], ax
084625C8: fldcw    word ptr [ebp - 0xdc]
084625CE: fistp    qword ptr [ebp - 0xd8]
084625D4: fldcw    word ptr [ebp - 0xda]
084625DA: mov      eax, dword ptr [ebp - 0xd8]
084625E0: mov      edx, dword ptr [ebp - 0xd4]
084625E6: mov      dword ptr [ebp - 0x1c], eax
084625E9: jmp      0x846265e
084625EB: mov      eax, dword ptr [ebp + 0x10]
084625EE: imul     eax, dword ptr [ebp + 0x14]
084625F2: mov      edx, 0
084625F7: mov      dword ptr [ebp - 0xd8], eax
084625FD: mov      dword ptr [ebp - 0xd4], edx
08462603: fild     qword ptr [ebp - 0xd8]
08462609: fstp     dword ptr [ebp - 0xe0]
0846260F: call     0x80cc19b ; _Z14G_CDataManagerv
08462614: mov      edx, dword ptr [ebp + 0x10]
08462617: add      edx, 0x15f4
0846261D: fld      dword ptr [eax + edx*4 + 0xc]
08462621: fmul     dword ptr [ebp - 0xe0]
08462627: fnstcw   word ptr [ebp - 0xda]
0846262D: movzx    eax, word ptr [ebp - 0xda]
08462634: mov      ah, 0xc
08462636: mov      word ptr [ebp - 0xdc], ax
0846263D: fldcw    word ptr [ebp - 0xdc]
08462643: fistp    qword ptr [ebp - 0xd8]
08462649: fldcw    word ptr [ebp - 0xda]
0846264F: mov      eax, dword ptr [ebp - 0xd8]
08462655: mov      edx, dword ptr [ebp - 0xd4]
0846265B: mov      dword ptr [ebp - 0x1c], eax
0846265E: mov      eax, dword ptr [ebp - 0x1c]
08462661: cmp      eax, 0x10c8e0
08462666: jbe      0x84626bf
08462668: call     0x80cc19b ; _Z14G_CDataManagerv
0846266D: mov      edx, dword ptr [ebp + 0x10]
08462670: add      edx, 0x1464
08462676: fld      dword ptr [eax + edx*4 + 0xc]
0846267A: mov      eax, dword ptr [ebp - 0x1c]
0846267D: fstp     qword ptr [esp + 0x20]
08462681: mov      edx, dword ptr [ebp + 0x14]
08462684: mov      dword ptr [esp + 0x1c], edx
08462688: mov      edx, dword ptr [ebp + 0x10]
0846268B: mov      dword ptr [esp + 0x18], edx
0846268F: mov      dword ptr [esp + 0x14], eax
08462693: mov      dword ptr [esp + 0x10], 0x8c6324c
0846269B: mov      dword ptr [esp + 0xc], 0x2ff
084626A3: mov      dword ptr [esp + 8], 0x8c64020
084626AB: mov      dword ptr [esp + 4], 0x8c632a7
084626B3: mov      dword ptr [esp], 1
084626BA: call     0x8ad3c0a ; _ZN10LogManager9logFormatEiPKcS1_iS1_z
084626BF: lea      eax, [ebp - 0x64]
084626C2: mov      dword ptr [esp], eax
084626C5: call     0x814a800 ; _ZN23stClearRewardBonusExp_tC2Ev
084626CA: lea      eax, [ebp - 0xc4]
084626D0: mov      dword ptr [esp], eax
084626D3: call     0x814a8b8 ; _ZN27stClearRewardExpParameter_tC2Ev
084626D8: mov      eax, dword ptr [ebp + 0x18]
084626DB: mov      dword ptr [esp], eax
084626DE: call     0x814a9c2 ; _ZN6CParty14getAvatarBonusEv
084626E3: fstp     dword ptr [ebp - 0xbc]
084626E9: mov      eax, dword ptr [ebp - 0x18]
084626EC: mov      dword ptr [esp + 4], eax
084626F0: mov      eax, dword ptr [ebp + 0x18]
084626F3: mov      dword ptr [esp], eax
084626F6: call     0x814a9e2 ; _ZN6CParty16getCreatureBonusEP5CUser
084626FB: fstp     dword ptr [ebp - 0xb8]
08462701: mov      eax, dword ptr [0x941f730]
08462706: mov      dword ptr [esp + 4], 8
0846270E: mov      dword ptr [esp], eax
08462711: call     0x8115998 ; _ZN13CEventManager14GetRepeatEventEi
08462716: mov      edx, dword ptr [eax]
08462718: add      edx, 0x34
0846271B: mov      ecx, dword ptr [edx]
0846271D: mov      edx, dword ptr [ebp - 0x18]
08462720: mov      dword ptr [esp + 4], edx
08462724: mov      dword ptr [esp], eax
08462727: call     ecx
08462729: test     al, al
0846272B: je       0x8462775
0846272D: mov      eax, dword ptr [0x941f730]
08462732: mov      dword ptr [esp + 4], 8
0846273A: mov      dword ptr [esp], eax
0846273D: call     0x8115998 ; _ZN13CEventManager14GetRepeatEventEi
08462742: mov      dword ptr [esp], eax
08462745: call     0x814aae4 ; _ZN19CPCRoomBurningEvent12GetExpFactorEv
0846274A: mov      ecx, eax
0846274C: mov      edx, 0x51eb851f
08462751: mov      eax, ecx
08462753: imul     edx
08462755: sar      edx, 5
08462758: mov      eax, ecx
0846275A: sar      eax, 0x1f
0846275D: mov      ecx, edx
0846275F: sub      ecx, eax
08462761: mov      eax, ecx
08462763: mov      dword ptr [ebp - 0xcc], eax
08462769: fild     dword ptr [ebp - 0xcc]
0846276F: fstp     dword ptr [ebp - 0xb4]
08462775: mov      eax, dword ptr [0x941f730]
0846277A: mov      dword ptr [esp + 4], 3
08462782: mov      dword ptr [esp], eax
08462785: call     0x8115998 ; _ZN13CEventManager14GetRepeatEventEi
0846278A: mov      edx, dword ptr [eax]
0846278C: add      edx, 0x34
0846278F: mov      edx, dword ptr [edx]
08462791: mov      dword ptr [esp + 4], 0
08462799: mov      dword ptr [esp], eax
0846279C: call     edx
0846279E: test     al, al
084627A0: je       0x84627d9
084627A2: mov      eax, dword ptr [0x941f730]
084627A7: mov      dword ptr [esp + 4], 3
084627AF: mov      dword ptr [esp], eax
084627B2: call     0x8115998 ; _ZN13CEventManager14GetRepeatEventEi
084627B7: mov      dword ptr [esp], eax
084627BA: call     0x814aad8 ; _ZN15CExpDoubleEvent12GetExpFactorEv
084627BF: mov      dword ptr [ebp - 0xcc], eax
084627C5: fild     dword ptr [ebp - 0xcc]
084627CB: fld      dword ptr [0x8c640b8]
084627D1: fdivp    st(1)
084627D3: fstp     dword ptr [ebp - 0xb0]
084627D9: mov      eax, dword ptr [0x941f730]
084627DE: mov      dword ptr [esp + 4], 0xf
084627E6: mov      dword ptr [esp], eax
084627E9: call     0x8115998 ; _ZN13CEventManager14GetRepeatEventEi
084627EE: mov      dword ptr [ebp - 0x14], eax
084627F1: mov      eax, dword ptr [ebp - 0x14]
084627F4: mov      eax, dword ptr [eax]
084627F6: add      eax, 0x34
084627F9: mov      edx, dword ptr [eax]
084627FB: mov      eax, dword ptr [ebp - 0x14]
084627FE: mov      dword ptr [esp + 4], 0
08462806: mov      dword ptr [esp], eax
08462809: call     edx
0846280B: test     al, al
0846280D: je       0x8462832
0846280F: mov      eax, dword ptr [ebp - 0x18]
08462812: mov      dword ptr [esp], eax
08462815: call     0x8110cba ; _ZN15CUserCharacInfo23getCurCharacUsedFatigueEv
0846281A: movzx    eax, ax
0846281D: mov      dword ptr [esp + 4], eax
08462821: mov      eax, dword ptr [ebp - 0x14]
08462824: mov      dword ptr [esp], eax
08462827: call     0x810a0ec ; _ZNK20CBurningFatigueEvent15getBonusExpRateEt
0846282C: mov      dword ptr [ebp - 0xa8], eax
08462832: call     0x80cc19b ; _Z14G_CDataManagerv
08462837: mov      dword ptr [esp], eax
0846283A: call     0x814a6f8 ; _ZNK12CDataManager16GetChannelScriptEv
0846283F: mov      dword ptr [ebp - 0xc], eax
08462842: mov      eax, dword ptr [ebp + 0x1c]
08462845: mov      dword ptr [esp], eax
08462848: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846284D: mov      dword ptr [esp], eax
08462850: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08462855: mov      esi, eax
08462857: call     0x80cc181 ; _Z14G_CEnvironmentv
0846285C: mov      dword ptr [esp], eax
0846285F: call     0x814aaca ; _ZN12CEnvironment14get_channel_noEv
08462864: mov      ebx, eax
08462866: call     0x80cc181 ; _Z14G_CEnvironmentv
0846286B: mov      eax, dword ptr [eax + 0x378]
08462871: movzx    eax, al
08462874: mov      dword ptr [esp + 0xc], esi
08462878: mov      dword ptr [esp + 8], ebx
0846287C: mov      dword ptr [esp + 4], eax
08462880: mov      eax, dword ptr [ebp - 0xc]
08462883: mov      dword ptr [esp], eax
08462886: call     0x88dce3c ; _ZNK16channel_script_t12getBonusRateEhjj
0846288B: fstp     dword ptr [ebp - 0xa4]
08462891: mov      eax, dword ptr [ebp - 0x18]
08462894: mov      dword ptr [esp], eax
08462897: call     0x812ce36 ; _ZNK5CUser22IsHavePremiumAdvantageEv
0846289C: test     al, al
0846289E: je       0x84628cd
084628A0: mov      eax, dword ptr [ebp - 0x18]
084628A3: mov      dword ptr [esp], eax
084628A6: call     0x812ce28 ; _ZNK5CUser14GetPremiumInfoEv
084628AB: mov      dword ptr [esp], eax
084628AE: call     0x814aa08 ; _ZNK8WongWork12CUserPremium19GetAdvantageExpRateEv
084628B3: mov      dword ptr [ebp - 0xcc], eax
084628B9: fild     dword ptr [ebp - 0xcc]
084628BF: fld      dword ptr [0x8c640b8]
084628C5: fdivp    st(1)
084628C7: fstp     dword ptr [ebp - 0xa0]
084628CD: call     0x814a6e0 ; _ZN12CServerEvent10GetExpRateEv
084628D2: mov      dword ptr [ebp - 0xcc], eax
084628D8: fild     dword ptr [ebp - 0xcc]
084628DE: fld      dword ptr [0x8c640b8]
084628E4: fdivp    st(1)
084628E6: fstp     dword ptr [ebp - 0x9c]
084628EC: mov      eax, dword ptr [ebp - 0x1c]
084628EF: mov      dword ptr [esp + 0xc], eax
084628F3: mov      eax, dword ptr [ebp - 0x18]
084628F6: mov      dword ptr [esp + 8], eax
084628FA: mov      dword ptr [esp + 4], 0
08462902: mov      eax, dword ptr [ebp + 0x18]
08462905: mov      dword ptr [esp], eax
08462908: call     0x85ada30 ; _ZN6CParty20CalcurateManageBonusEN15ManageBonusType1TEP5CUserj
0846290D: fstp     dword ptr [ebp - 0x88]
08462913: lea      eax, [ebp - 0x64]
08462916: mov      dword ptr [esp + 0x10], eax
0846291A: lea      eax, [ebp - 0xc4]
08462920: mov      dword ptr [esp + 0xc], eax
08462924: lea      eax, [ebp - 0x1c]
08462927: mov      dword ptr [esp + 8], eax
0846292B: mov      eax, dword ptr [ebp - 0x18]
0846292E: mov      dword ptr [esp + 4], eax
08462932: mov      eax, dword ptr [ebp + 0x18]
08462935: mov      dword ptr [esp], eax
08462938: call     0x85ad278 ; _ZN6CParty22getClearRewardBonusExpEP5CUserRjRK27stClearRewardExpParameter_tR23stClearRewardBonusExp_t
0846293D: mov      dword ptr [ebp - 0x10], eax
08462940: cmp      dword ptr [ebp - 0x10], 0x10c8e0
08462947: jbe      0x8462a09
0846294D: mov      eax, dword ptr [ebp - 0x10]
08462950: mov      dword ptr [esp + 0x14], eax
08462954: mov      dword ptr [esp + 0x10], 0x8c632b8
0846295C: mov      dword ptr [esp + 0xc], 0x324
08462964: mov      dword ptr [esp + 8], 0x8c64020
0846296C: mov      dword ptr [esp + 4], 0x8c632a7
08462974: mov      dword ptr [esp], 1
0846297B: call     0x8ad3c0a ; _ZN10LogManager9logFormatEiPKcS1_iS1_z
08462980: mov      eax, dword ptr [ebp - 0xa8]
08462986: fld      dword ptr [ebp - 0x9c]
0846298C: fld      dword ptr [ebp - 0xa0]
08462992: fld      dword ptr [ebp - 0xa4]
08462998: fld      dword ptr [ebp - 0xb0]
0846299E: fld      dword ptr [ebp - 0xb4]
084629A4: fld      dword ptr [ebp - 0xb8]
084629AA: fld      dword ptr [ebp - 0xbc]
084629B0: fxch     st(6)
084629B2: mov      dword ptr [esp + 0x4c], eax
084629B6: fstp     qword ptr [esp + 0x44]
084629BA: fxch     st(4)
084629BC: fstp     qword ptr [esp + 0x3c]
084629C0: fxch     st(2)
084629C2: fstp     qword ptr [esp + 0x34]
084629C6: fstp     qword ptr [esp + 0x2c]
084629CA: fstp     qword ptr [esp + 0x24]
084629CE: fstp     qword ptr [esp + 0x1c]
084629D2: fstp     qword ptr [esp + 0x14]
084629D6: mov      dword ptr [esp + 0x10], 0x8c632ec
084629DE: mov      dword ptr [esp + 0xc], 0x32e
084629E6: mov      dword ptr [esp + 8], 0x8c64020
084629EE: mov      dword ptr [esp + 4], 0x8c632a7
084629F6: mov      dword ptr [esp], 1
084629FD: call     0x8ad3c0a ; _ZN10LogManager9logFormatEiPKcS1_iS1_z
08462A02: mov      dword ptr [ebp - 0x10], 0x10c8e0
08462A09: mov      edx, dword ptr [ebp + 0xc]
08462A0C: mov      eax, dword ptr [ebp + 8]
08462A0F: lea      ecx, [edx + 0x26c]
08462A15: mov      edx, dword ptr [ebp - 0x10]
08462A18: mov      dword ptr [eax + ecx*4 + 8], edx
08462A1C: add      esp, 0x130
08462A22: pop      ebx
08462A23: pop      esi
08462A24: pop      ebp
08462A25: ret
```

## 0x08462AAE _ZN8WongWork11CDeathTower7onTimerE13TIMER_MESSAGEj

```asm
08462AAE: push     ebp
08462AAF: mov      ebp, esp
08462AB1: push     edi
08462AB2: push     esi
08462AB3: push     ebx
08462AB4: sub      esp, 0x14c
08462ABA: mov      eax, dword ptr [ebp + 0x10]
08462ABD: mov      dword ptr [esp + 8], eax
08462AC1: mov      eax, dword ptr [ebp + 0xc]
08462AC4: mov      dword ptr [esp + 4], eax
08462AC8: mov      eax, dword ptr [ebp + 8]
08462ACB: mov      dword ptr [esp], eax
08462ACE: call     0x8469c68 ; _ZN8WongWork11CDeathTower14_checkTimerKeyE13TIMER_MESSAGEj
08462AD3: xor      eax, 1
08462AD6: test     al, al
08462AD8: je       0x8462ae4
08462ADA: mov      eax, 0
08462ADF: jmp      0x84636e7
08462AE4: mov      eax, dword ptr [ebp + 8]
08462AE7: mov      dword ptr [esp], eax
08462AEA: call     0x8469c5c ; _ZN8WongWork11CDeathTower13getTowerStateEv
08462AEF: test     eax, eax
08462AF1: sete     al
08462AF4: test     al, al
08462AF6: je       0x8462b02
08462AF8: mov      eax, 0
08462AFD: jmp      0x84636e7
08462B02: cmp      dword ptr [ebp + 0xc], 0x49
08462B06: jne      0x8462d01
08462B0C: mov      eax, dword ptr [ebp + 8]
08462B0F: mov      eax, dword ptr [eax]
08462B11: mov      dword ptr [esp], eax
08462B14: call     0x859a16a ; _ZN6CParty16get_member_countEv
08462B19: mov      dword ptr [ebp - 0x60], eax
08462B1C: cmp      dword ptr [ebp - 0x60], 0
08462B20: jne      0x8462b37
08462B22: mov      eax, dword ptr [ebp + 8]
08462B25: mov      dword ptr [esp], eax
08462B28: call     0x8467b20 ; _ZN8WongWork11CDeathTower8_destroyEv
08462B2D: mov      eax, 1
08462B32: jmp      0x84636e7
08462B37: mov      byte ptr [ebp - 0x59], 0
08462B3B: mov      dword ptr [ebp - 0x54], 0
08462B42: jmp      0x8462c24
08462B47: mov      dword ptr [ebp - 0x50], 0
08462B4E: mov      eax, dword ptr [ebp + 8]
08462B51: mov      eax, dword ptr [eax]
08462B53: mov      edx, dword ptr [ebp - 0x54]
08462B56: mov      dword ptr [esp + 4], edx
08462B5A: mov      dword ptr [esp], eax
08462B5D: call     0x8145764 ; _ZN6CParty8get_userEi
08462B62: mov      dword ptr [ebp - 0x50], eax
08462B65: cmp      dword ptr [ebp - 0x50], 0
08462B69: je       0x8462b86
08462B6B: mov      eax, dword ptr [ebp + 8]
08462B6E: mov      eax, dword ptr [eax]
08462B70: mov      edx, dword ptr [ebp - 0x54]
08462B73: mov      dword ptr [esp + 4], edx
08462B77: mov      dword ptr [esp], eax
08462B7A: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08462B7F: xor      eax, 1
08462B82: test     al, al
08462B84: je       0x8462b8d
08462B86: mov      eax, 1
08462B8B: jmp      0x8462b92
08462B8D: mov      eax, 0
08462B92: test     al, al
08462B94: jne      0x8462c1f
08462B9A: mov      eax, dword ptr [ebp + 8]
08462B9D: lea      edx, [eax + 0x110]
08462BA3: mov      eax, dword ptr [ebp - 0x54]
08462BA6: mov      dword ptr [esp + 4], eax
08462BAA: mov      dword ptr [esp], edx
08462BAD: call     0x8469ba2 ; _ZN8WongWork11CDeathTower9CPlayData13isMemberReadyEi
08462BB2: xor      eax, 1
08462BB5: test     al, al
08462BB7: je       0x8462c20
08462BB9: lea      eax, [ebp - 0x6c]
08462BBC: mov      dword ptr [esp], eax
08462BBF: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08462BC4: lea      eax, [ebp - 0x6c]
08462BC7: mov      dword ptr [esp], eax
08462BCA: call     0x846012c ; _ZN8WongWork11CDeathTower14CPacketHandler17makeFinishLoadingEv
08462BCF: lea      eax, [ebp - 0x6c]
08462BD2: mov      dword ptr [esp + 4], eax
08462BD6: mov      eax, dword ptr [ebp - 0x50]
08462BD9: mov      dword ptr [esp], eax
08462BDC: call     0x86485ba ; _ZN5CUser4SendER11PacketGuard
08462BE1: mov      eax, dword ptr [ebp - 0x50]
08462BE4: mov      dword ptr [esp + 4], eax
08462BE8: mov      eax, dword ptr [ebp + 8]
08462BEB: mov      dword ptr [esp], eax
08462BEE: call     0x8465ac2 ; _ZN8WongWork11CDeathTower15handleLeaveUserEP5CUser
08462BF3: mov      byte ptr [ebp - 0x59], 1
08462BF7: lea      eax, [ebp - 0x6c]
08462BFA: mov      dword ptr [esp], eax
08462BFD: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08462C02: jmp      0x8462c20
08462C04: mov      ebx, edx
08462C06: mov      esi, eax
08462C08: lea      eax, [ebp - 0x6c]
08462C0B: mov      dword ptr [esp], eax
08462C0E: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08462C13: mov      eax, esi
08462C15: mov      edx, ebx
08462C17: mov      dword ptr [esp], eax
08462C1A: call     0x8ae3750 ; _Unwind_Resume
08462C1F: nop
08462C20: add      dword ptr [ebp - 0x54], 1
08462C24: cmp      dword ptr [ebp - 0x54], 3
08462C28: setle    al
08462C2B: test     al, al
08462C2D: jne      0x8462b47
08462C33: mov      eax, dword ptr [ebp + 8]
08462C36: mov      eax, dword ptr [eax]
08462C38: mov      dword ptr [esp], eax
08462C3B: call     0x859a16a ; _ZN6CParty16get_member_countEv
08462C40: mov      dword ptr [ebp - 0x58], eax
08462C43: cmp      byte ptr [ebp - 0x59], 0
08462C47: je       0x8462ce7
08462C4D: cmp      dword ptr [ebp - 0x58], 0
08462C51: jle      0x8462ce7
08462C57: mov      dword ptr [ebp - 0x4c], 0
08462C5E: jmp      0x8462c84
08462C60: mov      eax, dword ptr [ebp + 8]
08462C63: lea      edx, [eax + 0x110]
08462C69: mov      dword ptr [esp + 8], 1
08462C71: mov      eax, dword ptr [ebp - 0x4c]
08462C74: mov      dword ptr [esp + 4], eax
08462C78: mov      dword ptr [esp], edx
08462C7B: call     0x8469b86 ; _ZN8WongWork11CDeathTower9CPlayData14setMemberReadyEib
08462C80: add      dword ptr [ebp - 0x4c], 1
08462C84: cmp      dword ptr [ebp - 0x4c], 3
08462C88: setle    al
08462C8B: test     al, al
08462C8D: jne      0x8462c60
08462C8F: lea      eax, [ebp - 0x78]
08462C92: mov      dword ptr [esp], eax
08462C95: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08462C9A: lea      eax, [ebp - 0x78]
08462C9D: mov      dword ptr [esp], eax
08462CA0: call     0x846012c ; _ZN8WongWork11CDeathTower14CPacketHandler17makeFinishLoadingEv
08462CA5: mov      eax, dword ptr [ebp + 8]
08462CA8: mov      eax, dword ptr [eax]
08462CAA: lea      edx, [ebp - 0x78]
08462CAD: mov      dword ptr [esp + 4], edx
08462CB1: mov      dword ptr [esp], eax
08462CB4: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08462CB9: jmp      0x8462cd6
08462CBB: mov      ebx, edx
08462CBD: mov      esi, eax
08462CBF: lea      eax, [ebp - 0x78]
08462CC2: mov      dword ptr [esp], eax
08462CC5: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08462CCA: mov      eax, esi
08462CCC: mov      edx, ebx
08462CCE: mov      dword ptr [esp], eax
08462CD1: call     0x8ae3750 ; _Unwind_Resume
08462CD6: lea      eax, [ebp - 0x78]
08462CD9: mov      dword ptr [esp], eax
08462CDC: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08462CE1: nop
08462CE2: jmp      0x84636e2
08462CE7: cmp      dword ptr [ebp - 0x58], 0
08462CEB: jne      0x84636e1
08462CF1: mov      eax, dword ptr [ebp + 8]
08462CF4: mov      dword ptr [esp], eax
08462CF7: call     0x8467b20 ; _ZN8WongWork11CDeathTower8_destroyEv
08462CFC: jmp      0x84636e2
08462D01: cmp      dword ptr [ebp + 0xc], 0x4b
08462D05: jne      0x8462ff0
08462D0B: mov      eax, dword ptr [ebp + 8]
08462D0E: mov      dword ptr [esp], eax
08462D11: call     0x8469c5c ; _ZN8WongWork11CDeathTower13getTowerStateEv
08462D16: cmp      eax, 2
08462D19: setle    al
08462D1C: test     al, al
08462D1E: je       0x8462d2a
08462D20: mov      eax, 0
08462D25: jmp      0x84636e7
08462D2A: mov      dword ptr [ebp - 0x48], 0
08462D31: jmp      0x8462f79
08462D36: mov      dword ptr [ebp - 0x44], 0
08462D3D: mov      eax, dword ptr [ebp + 8]
08462D40: mov      eax, dword ptr [eax]
08462D42: mov      edx, dword ptr [ebp - 0x48]
08462D45: mov      dword ptr [esp + 4], edx
08462D49: mov      dword ptr [esp], eax
08462D4C: call     0x8145764 ; _ZN6CParty8get_userEi
08462D51: mov      dword ptr [ebp - 0x44], eax
08462D54: cmp      dword ptr [ebp - 0x44], 0
08462D58: je       0x8462d75
08462D5A: mov      eax, dword ptr [ebp + 8]
08462D5D: mov      eax, dword ptr [eax]
08462D5F: mov      edx, dword ptr [ebp - 0x48]
08462D62: mov      dword ptr [esp + 4], edx
08462D66: mov      dword ptr [esp], eax
08462D69: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08462D6E: xor      eax, 1
08462D71: test     al, al
08462D73: je       0x8462d7c
08462D75: mov      eax, 1
08462D7A: jmp      0x8462d81
08462D7C: mov      eax, 0
08462D81: test     al, al
08462D83: jne      0x8462f74
08462D89: lea      eax, [ebp - 0x84]
08462D8F: mov      dword ptr [esp], eax
08462D92: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08462D97: mov      eax, dword ptr [ebp + 8]
08462D9A: add      eax, 0x110
08462D9F: mov      dword ptr [esp + 4], 3
08462DA7: mov      dword ptr [esp], eax
08462DAA: call     0x8469c06 ; _ZN8WongWork11CDeathTower9CPlayData13getRewardItemEi
08462DAF: mov      edi, eax
08462DB1: mov      eax, dword ptr [ebp + 8]
08462DB4: add      eax, 0x110
08462DB9: mov      dword ptr [esp + 4], 3
08462DC1: mov      dword ptr [esp], eax
08462DC4: call     0x8469bec ; _ZN8WongWork11CDeathTower9CPlayData18getRewardItemCountEi
08462DC9: mov      dword ptr [ebp - 0xfc], eax
08462DCF: mov      eax, dword ptr [ebp + 8]
08462DD2: add      eax, 0x110
08462DD7: mov      dword ptr [esp + 4], 2
08462DDF: mov      dword ptr [esp], eax
08462DE2: call     0x8469c06 ; _ZN8WongWork11CDeathTower9CPlayData13getRewardItemEi
08462DE7: mov      dword ptr [ebp - 0xf8], eax
08462DED: mov      eax, dword ptr [ebp + 8]
08462DF0: add      eax, 0x110
08462DF5: mov      dword ptr [esp + 4], 2
08462DFD: mov      dword ptr [esp], eax
08462E00: call     0x8469bec ; _ZN8WongWork11CDeathTower9CPlayData18getRewardItemCountEi
08462E05: mov      dword ptr [ebp - 0xf4], eax
08462E0B: mov      eax, dword ptr [ebp + 8]
08462E0E: add      eax, 0x110
08462E13: mov      dword ptr [esp + 4], 1
08462E1B: mov      dword ptr [esp], eax
08462E1E: call     0x8469c06 ; _ZN8WongWork11CDeathTower9CPlayData13getRewardItemEi
08462E23: mov      dword ptr [ebp - 0xf0], eax
08462E29: mov      eax, dword ptr [ebp + 8]
08462E2C: add      eax, 0x110
08462E31: mov      dword ptr [esp + 4], 1
08462E39: mov      dword ptr [esp], eax
08462E3C: call     0x8469bec ; _ZN8WongWork11CDeathTower9CPlayData18getRewardItemCountEi
08462E41: mov      dword ptr [ebp - 0xec], eax
08462E47: mov      eax, dword ptr [ebp + 8]
08462E4A: add      eax, 0x110
08462E4F: mov      dword ptr [esp + 4], 0
08462E57: mov      dword ptr [esp], eax
08462E5A: call     0x8469c06 ; _ZN8WongWork11CDeathTower9CPlayData13getRewardItemEi
08462E5F: mov      esi, eax
08462E61: mov      eax, dword ptr [ebp + 8]
08462E64: add      eax, 0x110
08462E69: mov      dword ptr [esp + 4], 0
08462E71: mov      dword ptr [esp], eax
08462E74: call     0x8469bec ; _ZN8WongWork11CDeathTower9CPlayData18getRewardItemCountEi
08462E79: mov      ebx, eax
08462E7B: mov      eax, dword ptr [ebp + 8]
08462E7E: lea      edx, [eax + 0x110]
08462E84: mov      eax, dword ptr [ebp - 0x48]
08462E87: mov      dword ptr [esp + 4], eax
08462E8B: mov      dword ptr [esp], edx
08462E8E: call     0x8469c1e ; _ZN8WongWork11CDeathTower9CPlayData12getRewardExpEi
08462E93: mov      dword ptr [esp + 0x24], edi
08462E97: mov      edx, dword ptr [ebp - 0xfc]
08462E9D: mov      dword ptr [esp + 0x20], edx
08462EA1: mov      edx, dword ptr [ebp - 0xf8]
08462EA7: mov      dword ptr [esp + 0x1c], edx
08462EAB: mov      edx, dword ptr [ebp - 0xf4]
08462EB1: mov      dword ptr [esp + 0x18], edx
08462EB5: mov      edx, dword ptr [ebp - 0xf0]
08462EBB: mov      dword ptr [esp + 0x14], edx
08462EBF: mov      edx, dword ptr [ebp - 0xec]
08462EC5: mov      dword ptr [esp + 0x10], edx
08462EC9: mov      dword ptr [esp + 0xc], esi
08462ECD: mov      dword ptr [esp + 8], ebx
08462ED1: mov      dword ptr [esp + 4], eax
08462ED5: lea      eax, [ebp - 0x84]
08462EDB: mov      dword ptr [esp], eax
08462EDE: call     0x84602c4 ; _ZN8WongWork11CDeathTower14CPacketHandler17makeDisplayRewardEiiPK10Inven_ItemiS4_iS4_iS4_
08462EE3: mov      eax, dword ptr [ebp + 8]
08462EE6: lea      edx, [eax + 0x110]
08462EEC: mov      eax, dword ptr [ebp - 0x48]
08462EEF: mov      dword ptr [esp + 4], eax
08462EF3: mov      dword ptr [esp], edx
08462EF6: call     0x8469c06 ; _ZN8WongWork11CDeathTower9CPlayData13getRewardItemEi
08462EFB: mov      ebx, eax
08462EFD: mov      eax, dword ptr [ebp + 8]
08462F00: lea      edx, [eax + 0x110]
08462F06: mov      eax, dword ptr [ebp - 0x48]
08462F09: mov      dword ptr [esp + 4], eax
08462F0D: mov      dword ptr [esp], edx
08462F10: call     0x8469bec ; _ZN8WongWork11CDeathTower9CPlayData18getRewardItemCountEi
08462F15: mov      dword ptr [esp + 0xc], ebx
08462F19: mov      edx, dword ptr [ebp - 0x44]
08462F1C: mov      dword ptr [esp + 8], edx
08462F20: mov      dword ptr [esp + 4], eax
08462F24: mov      eax, dword ptr [ebp + 8]
08462F27: mov      dword ptr [esp], eax
08462F2A: call     0x846966c ; _ZN8WongWork11CDeathTower23deathTowerCardStatisticEiP5CUserPK10Inven_Item
08462F2F: lea      eax, [ebp - 0x84]
08462F35: mov      dword ptr [esp + 4], eax
08462F39: mov      eax, dword ptr [ebp - 0x44]
08462F3C: mov      dword ptr [esp], eax
08462F3F: call     0x86485ba ; _ZN5CUser4SendER11PacketGuard
08462F44: jmp      0x8462f64
08462F46: mov      ebx, edx
08462F48: mov      esi, eax
08462F4A: lea      eax, [ebp - 0x84]
08462F50: mov      dword ptr [esp], eax
08462F53: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08462F58: mov      eax, esi
08462F5A: mov      edx, ebx
08462F5C: mov      dword ptr [esp], eax
08462F5F: call     0x8ae3750 ; _Unwind_Resume
08462F64: lea      eax, [ebp - 0x84]
08462F6A: mov      dword ptr [esp], eax
08462F6D: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08462F72: jmp      0x8462f75
08462F74: nop
08462F75: add      dword ptr [ebp - 0x48], 1
08462F79: cmp      dword ptr [ebp - 0x48], 3
08462F7D: setle    al
08462F80: test     al, al
08462F82: jne      0x8462d36
08462F88: mov      dword ptr [esp + 4], 4
08462F90: mov      eax, dword ptr [ebp + 8]
08462F93: mov      dword ptr [esp], eax
08462F96: call     0x8469c34 ; _ZN8WongWork11CDeathTower13setTowerStateENS0_16ENUM_TOWER_STATEE
08462F9B: mov      dword ptr [esp + 4], 0x4c
08462FA3: mov      eax, dword ptr [ebp + 8]
08462FA6: mov      dword ptr [esp], eax
08462FA9: call     0x830edec ; _ZN8WongWork11CDeathTower12_genTimerKeyE13TIMER_MESSAGE
08462FAE: mov      ebx, eax
08462FB0: mov      eax, dword ptr [ebp + 8]
08462FB3: mov      esi, dword ptr [eax + 4]
08462FB6: call     0x80f647c ; _Z12G_TimerQueuev
08462FBB: mov      dword ptr [esp + 0x18], 0x4c
08462FC3: mov      dword ptr [esp + 0x14], ebx
08462FC7: mov      dword ptr [esp + 0x10], 5
08462FCF: mov      dword ptr [esp + 0xc], 0x4c
08462FD7: mov      dword ptr [esp + 8], esi
08462FDB: mov      dword ptr [esp + 4], 6
08462FE3: mov      dword ptr [esp], eax
08462FE6: call     0x8630e16 ; _ZN10TimerQueue11InsertTimerEN10TimerEntry8OBJ_TYPEEi13TIMER_MESSAGEiij
08462FEB: jmp      0x84636e2
08462FF0: cmp      dword ptr [ebp + 0xc], 0x4c
08462FF4: jne      0x84635a4
08462FFA: mov      eax, dword ptr [ebp + 8]
08462FFD: mov      dword ptr [esp], eax
08463000: call     0x8469c5c ; _ZN8WongWork11CDeathTower13getTowerStateEv
08463005: cmp      eax, 3
08463008: setle    al
0846300B: test     al, al
0846300D: je       0x8463019
0846300F: mov      eax, 0
08463014: jmp      0x84636e7
08463019: mov      byte ptr [ebp - 0x3d], 1
0846301D: mov      dword ptr [ebp - 0x3c], 0
08463024: jmp      0x84630bf
08463029: mov      dword ptr [ebp - 0x38], 0
08463030: mov      eax, dword ptr [ebp + 8]
08463033: mov      eax, dword ptr [eax]
08463035: mov      edx, dword ptr [ebp - 0x3c]
08463038: mov      dword ptr [esp + 4], edx
0846303C: mov      dword ptr [esp], eax
0846303F: call     0x8145764 ; _ZN6CParty8get_userEi
08463044: mov      dword ptr [ebp - 0x38], eax
08463047: cmp      dword ptr [ebp - 0x38], 0
0846304B: je       0x8463068
0846304D: mov      eax, dword ptr [ebp + 8]
08463050: mov      eax, dword ptr [eax]
08463052: mov      edx, dword ptr [ebp - 0x3c]
08463055: mov      dword ptr [esp + 4], edx
08463059: mov      dword ptr [esp], eax
0846305C: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08463061: xor      eax, 1
08463064: test     al, al
08463066: je       0x846306f
08463068: mov      eax, 1
0846306D: jmp      0x8463074
0846306F: mov      eax, 0
08463074: test     al, al
08463076: jne      0x84630ba
08463078: lea      eax, [ebp - 0xe1]
0846307E: mov      dword ptr [esp], eax
08463081: call     0x80cb854 ; _ZN10Inven_ItemC2Ev
08463086: mov      eax, dword ptr [ebp - 0x38]
08463089: mov      dword ptr [esp], eax
0846308C: call     0x80da27e ; _ZNK15CUserCharacInfo18getCurCharacInvenREv
08463091: lea      edx, [ebp - 0xe1]
08463097: mov      dword ptr [esp + 8], edx
0846309B: mov      dword ptr [esp + 4], 0x1057
084630A3: mov      dword ptr [esp], eax
084630A6: call     0x84fbf2c ; _ZNK10CInventory12GetInvenDataEiR10Inven_Item
084630AB: mov      dword ptr [ebp - 0x34], eax
084630AE: cmp      dword ptr [ebp - 0x34], 0
084630B2: jns      0x84630bb
084630B4: mov      byte ptr [ebp - 0x3d], 0
084630B8: jmp      0x84630ce
084630BA: nop
084630BB: add      dword ptr [ebp - 0x3c], 1
084630BF: cmp      dword ptr [ebp - 0x3c], 3
084630C3: setle    al
084630C6: test     al, al
084630C8: jne      0x8463029
084630CE: lea      eax, [ebp - 0x90]
084630D4: mov      dword ptr [esp], eax
084630D7: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
084630DC: movzx    eax, byte ptr [ebp - 0x3d]
084630E0: mov      dword ptr [esp + 4], eax
084630E4: lea      eax, [ebp - 0x90]
084630EA: mov      dword ptr [esp], eax
084630ED: call     0x84604b0 ; _ZN8WongWork11CDeathTower14CPacketHandler15makeDisplayEPLPEb
084630F2: mov      eax, dword ptr [ebp + 8]
084630F5: mov      eax, dword ptr [eax]
084630F7: lea      edx, [ebp - 0x90]
084630FD: mov      dword ptr [esp + 4], edx
08463101: mov      dword ptr [esp], eax
08463104: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08463109: mov      dword ptr [ebp - 0x30], 0
08463110: jmp      0x84634ea
08463115: mov      dword ptr [ebp - 0x2c], 0
0846311C: mov      eax, dword ptr [ebp + 8]
0846311F: mov      eax, dword ptr [eax]
08463121: mov      edx, dword ptr [ebp - 0x30]
08463124: mov      dword ptr [esp + 4], edx
08463128: mov      dword ptr [esp], eax
0846312B: call     0x8145764 ; _ZN6CParty8get_userEi
08463130: mov      dword ptr [ebp - 0x2c], eax
08463133: cmp      dword ptr [ebp - 0x2c], 0
08463137: je       0x8463154
08463139: mov      eax, dword ptr [ebp + 8]
0846313C: mov      eax, dword ptr [eax]
0846313E: mov      edx, dword ptr [ebp - 0x30]
08463141: mov      dword ptr [esp + 4], edx
08463145: mov      dword ptr [esp], eax
08463148: call     0x8145868 ; _ZN6CParty14checkValidUserEi
0846314D: xor      eax, 1
08463150: test     al, al
08463152: je       0x846315b
08463154: mov      eax, 1
08463159: jmp      0x8463160
0846315B: mov      eax, 0
08463160: test     al, al
08463162: jne      0x84634e5
08463168: mov      dword ptr [ebp - 0x28], 0
0846316F: jmp      0x8463400
08463174: mov      eax, dword ptr [ebp + 8]
08463177: lea      edx, [eax + 0x110]
0846317D: mov      eax, dword ptr [ebp - 0x30]
08463180: mov      dword ptr [esp + 4], eax
08463184: mov      dword ptr [esp], edx
08463187: call     0x8469c06 ; _ZN8WongWork11CDeathTower9CPlayData13getRewardItemEi
0846318C: mov      dword ptr [ebp - 0x24], eax
0846318F: mov      eax, dword ptr [ebp - 0x28]
08463192: imul     eax, eax, 0x3d
08463195: add      eax, dword ptr [ebp - 0x24]
08463198: mov      eax, dword ptr [eax + 2]
0846319B: cmp      eax, -1
0846319E: je       0x84633fb
084631A4: mov      dword ptr [ebp - 0x20], 0xffffffff
084631AB: mov      eax, dword ptr [ebp - 0x28]
084631AE: imul     eax, eax, 0x3d
084631B1: add      eax, dword ptr [ebp - 0x24]
084631B4: mov      eax, dword ptr [eax + 2]
084631B7: test     eax, eax
084631B9: jne      0x84632c7
084631BF: mov      eax, dword ptr [ebp - 0x28]
084631C2: imul     eax, eax, 0x3d
084631C5: add      eax, dword ptr [ebp - 0x24]
084631C8: mov      dword ptr [esp], eax
084631CB: call     0x80f783a ; _ZNK10Inven_Item12get_add_infoEv
084631D0: mov      ebx, eax
084631D2: mov      eax, dword ptr [ebp - 0x2c]
084631D5: mov      dword ptr [esp], eax
084631D8: call     0x80da28e ; _ZN15CUserCharacInfo18getCurCharacInvenWEv
084631DD: mov      dword ptr [esp + 0x10], 0
084631E5: mov      dword ptr [esp + 0xc], 1
084631ED: mov      dword ptr [esp + 8], 5
084631F5: mov      dword ptr [esp + 4], ebx
084631F9: mov      dword ptr [esp], eax
084631FC: call     0x84ff29c ; _ZN10CInventory10gain_moneyEi15eMoneyAddReasonbi
08463201: mov      dword ptr [ebp - 0x1c], eax
08463204: mov      eax, dword ptr [ebp - 0x28]
08463207: imul     eax, eax, 0x3d
0846320A: add      eax, dword ptr [ebp - 0x24]
0846320D: mov      dword ptr [esp], eax
08463210: call     0x80f783a ; _ZNK10Inven_Item12get_add_infoEv
08463215: cmp      eax, dword ptr [ebp - 0x1c]
08463218: setg     al
0846321B: test     al, al
0846321D: je       0x846324e
0846321F: mov      ebx, dword ptr [ebp - 0x1c]
08463222: mov      eax, dword ptr [ebp - 0x28]
08463225: imul     eax, eax, 0x3d
08463228: add      eax, dword ptr [ebp - 0x24]
0846322B: mov      dword ptr [esp], eax
0846322E: call     0x80f783a ; _ZNK10Inven_Item12get_add_infoEv
08463233: mov      dword ptr [esp + 0xc], ebx
08463237: mov      dword ptr [esp + 8], eax
0846323B: mov      dword ptr [esp + 4], 0
08463243: mov      eax, dword ptr [ebp - 0x2c]
08463246: mov      dword ptr [esp], eax
08463249: call     0x867c844 ; _ZN5CUser19SendMoneyFullReasonE22ENUM_MONEY_FULL_REASONmm
0846324E: mov      dword ptr [ebp - 0x20], 0
08463255: mov      eax, dword ptr [ebp + 8]
08463258: add      eax, 0xc
0846325B: mov      dword ptr [esp], eax
0846325E: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08463263: mov      dword ptr [esp], eax
08463266: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
0846326B: cmp      eax, 0x2af8
08463270: sete     al
08463273: test     al, al
08463275: je       0x846329f
08463277: mov      ebx, dword ptr [ebp - 0x1c]
0846327A: call     0x860da17 ; _Z31GetInstanceCirculationStatisticv
0846327F: mov      dword ptr [esp + 0xc], ebx
08463283: mov      edx, dword ptr [ebp - 0x2c]
08463286: mov      dword ptr [esp + 8], edx
0846328A: mov      dword ptr [esp + 4], 4
08463292: mov      dword ptr [esp], eax
08463295: call     0x860daaa ; _ZN21CCirculationStatistic23AddCirculationStatisticE27CIRCULATION_STATISTIC_FIELDP5CUserj
0846329A: jmp      0x8463370
0846329F: mov      ebx, dword ptr [ebp - 0x1c]
084632A2: call     0x860da17 ; _Z31GetInstanceCirculationStatisticv
084632A7: mov      dword ptr [esp + 0xc], ebx
084632AB: mov      edx, dword ptr [ebp - 0x2c]
084632AE: mov      dword ptr [esp + 8], edx
084632B2: mov      dword ptr [esp + 4], 5
084632BA: mov      dword ptr [esp], eax
084632BD: call     0x860daaa ; _ZN21CCirculationStatistic23AddCirculationStatisticE27CIRCULATION_STATISTIC_FIELDP5CUserj
084632C2: jmp      0x8463370
084632C7: mov      eax, dword ptr [ebp - 0x28]
084632CA: imul     eax, eax, 0x3d
084632CD: mov      ebx, eax
084632CF: add      ebx, dword ptr [ebp - 0x24]
084632D2: mov      eax, dword ptr [ebp - 0x2c]
084632D5: mov      dword ptr [esp], eax
084632D8: call     0x80da28e ; _ZN15CUserCharacInfo18getCurCharacInvenWEv
084632DD: mov      dword ptr [esp + 0x4c], 1
084632E5: mov      dword ptr [esp + 0x48], 1
084632ED: mov      dword ptr [esp + 0x44], 0xb
084632F5: mov      edx, dword ptr [ebx]
084632F7: mov      dword ptr [esp + 4], edx
084632FB: mov      edx, dword ptr [ebx + 4]
084632FE: mov      dword ptr [esp + 8], edx
08463302: mov      edx, dword ptr [ebx + 8]
08463305: mov      dword ptr [esp + 0xc], edx
08463309: mov      edx, dword ptr [ebx + 0xc]
0846330C: mov      dword ptr [esp + 0x10], edx
08463310: mov      edx, dword ptr [ebx + 0x10]
08463313: mov      dword ptr [esp + 0x14], edx
08463317: mov      edx, dword ptr [ebx + 0x14]
0846331A: mov      dword ptr [esp + 0x18], edx
0846331E: mov      edx, dword ptr [ebx + 0x18]
08463321: mov      dword ptr [esp + 0x1c], edx
08463325: mov      edx, dword ptr [ebx + 0x1c]
08463328: mov      dword ptr [esp + 0x20], edx
0846332C: mov      edx, dword ptr [ebx + 0x20]
0846332F: mov      dword ptr [esp + 0x24], edx
08463333: mov      edx, dword ptr [ebx + 0x24]
08463336: mov      dword ptr [esp + 0x28], edx
0846333A: mov      edx, dword ptr [ebx + 0x28]
0846333D: mov      dword ptr [esp + 0x2c], edx
08463341: mov      edx, dword ptr [ebx + 0x2c]
08463344: mov      dword ptr [esp + 0x30], edx
08463348: mov      edx, dword ptr [ebx + 0x30]
0846334B: mov      dword ptr [esp + 0x34], edx
0846334F: mov      edx, dword ptr [ebx + 0x34]
08463352: mov      dword ptr [esp + 0x38], edx
08463356: mov      edx, dword ptr [ebx + 0x38]
08463359: mov      dword ptr [esp + 0x3c], edx
0846335D: movzx    edx, byte ptr [ebx + 0x3c]
08463361: mov      byte ptr [esp + 0x40], dl
08463365: mov      dword ptr [esp], eax
08463368: call     0x8502d86 ; _ZN10CInventory23insertItemIntoInventoryE10Inven_Item14eItemAddReasonbb
0846336D: mov      dword ptr [ebp - 0x20], eax
08463370: cmp      dword ptr [ebp - 0x20], 0
08463374: js       0x84633fc
0846337A: lea      eax, [ebp - 0xa4]
08463380: mov      dword ptr [esp], eax
08463383: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08463388: mov      eax, dword ptr [ebp - 0x28]
0846338B: imul     eax, eax, 0x3d
0846338E: add      eax, dword ptr [ebp - 0x24]
08463391: mov      dword ptr [esp], eax
08463394: call     0x83481e2 ; _ZNK10Inven_Item12GetItemSpaceEv
08463399: mov      edx, dword ptr [ebp - 0x20]
0846339C: mov      dword ptr [esp + 0xc], edx
084633A0: mov      dword ptr [esp + 8], eax
084633A4: mov      eax, dword ptr [ebp - 0x2c]
084633A7: mov      dword ptr [esp + 4], eax
084633AB: lea      eax, [ebp - 0xa4]
084633B1: mov      dword ptr [esp], eax
084633B4: call     0x846050e ; _ZN8WongWork11CDeathTower14CPacketHandler18makeUpdateItemListEP5CUser14ENUM_ITEMSPACEi
084633B9: lea      eax, [ebp - 0xa4]
084633BF: mov      dword ptr [esp + 4], eax
084633C3: mov      eax, dword ptr [ebp - 0x2c]
084633C6: mov      dword ptr [esp], eax
084633C9: call     0x86485ba ; _ZN5CUser4SendER11PacketGuard
084633CE: jmp      0x84633eb
084633D0: mov      ebx, edx
084633D2: mov      esi, eax
084633D4: lea      eax, [ebp - 0xa4]
084633DA: mov      dword ptr [esp], eax
084633DD: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
084633E2: mov      eax, esi
084633E4: mov      edx, ebx
084633E6: jmp      0x8463573
084633EB: lea      eax, [ebp - 0xa4]
084633F1: mov      dword ptr [esp], eax
084633F4: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
084633F9: jmp      0x84633fc
084633FB: nop
084633FC: add      dword ptr [ebp - 0x28], 1
08463400: mov      eax, dword ptr [ebp + 8]
08463403: lea      edx, [eax + 0x110]
08463409: mov      eax, dword ptr [ebp - 0x30]
0846340C: mov      dword ptr [esp + 4], eax
08463410: mov      dword ptr [esp], edx
08463413: call     0x8469bec ; _ZN8WongWork11CDeathTower9CPlayData18getRewardItemCountEi
08463418: cmp      eax, dword ptr [ebp - 0x28]
0846341B: setg     al
0846341E: test     al, al
08463420: jne      0x8463174
08463426: mov      eax, dword ptr [ebp + 8]
08463429: lea      edx, [eax + 0x110]
0846342F: mov      eax, dword ptr [ebp - 0x30]
08463432: mov      dword ptr [esp + 4], eax
08463436: mov      dword ptr [esp], edx
08463439: call     0x8469c1e ; _ZN8WongWork11CDeathTower9CPlayData12getRewardExpEi
0846343E: mov      edx, dword ptr [ebp - 0x2c]
08463441: mov      dword ptr [esp + 8], 0
08463449: mov      dword ptr [esp + 4], eax
0846344D: mov      dword ptr [esp], edx
08463450: call     0x864e402 ; _ZN15CUserCharacInfo18calcHelpAbuseRatioEjb
08463455: mov      dword ptr [ebp - 0x94], 0
0846345F: mov      dword ptr [ebp - 0x98], 0
08463469: mov      eax, dword ptr [ebp + 8]
0846346C: lea      edx, [eax + 0x110]
08463472: mov      eax, dword ptr [ebp - 0x30]
08463475: mov      dword ptr [esp + 4], eax
08463479: mov      dword ptr [esp], edx
0846347C: call     0x8469c1e ; _ZN8WongWork11CDeathTower9CPlayData12getRewardExpEi
08463481: mov      dword ptr [esp + 0x18], 0
08463489: mov      dword ptr [esp + 0x14], 0
08463491: mov      dword ptr [esp + 0x10], 2
08463499: lea      edx, [ebp - 0x98]
0846349F: mov      dword ptr [esp + 0xc], edx
084634A3: lea      edx, [ebp - 0x94]
084634A9: mov      dword ptr [esp + 8], edx
084634AD: mov      dword ptr [esp + 4], eax
084634B1: mov      eax, dword ptr [ebp - 0x2c]
084634B4: mov      dword ptr [esp], eax
084634B7: call     0x866a3fe ; _ZN5CUser11gain_exp_spEiRiS0_13eExpAddReasonib
084634BC: test     al, al
084634BE: je       0x84634e6
084634C0: mov      dword ptr [esp + 0xc], 1
084634C8: mov      dword ptr [esp + 8], 2
084634D0: mov      dword ptr [esp + 4], 2
084634D8: mov      eax, dword ptr [ebp - 0x2c]
084634DB: mov      dword ptr [esp], eax
084634DE: call     0x867ba5c ; _ZN5CUser14SendNotiPacketENS_11eSendTargetE15ENUM_NOTIPACKETi
084634E3: jmp      0x84634e6
084634E5: nop
084634E6: add      dword ptr [ebp - 0x30], 1
084634EA: cmp      dword ptr [ebp - 0x30], 3
084634EE: setle    al
084634F1: test     al, al
084634F3: jne      0x8463115
084634F9: mov      dword ptr [esp + 4], 5
08463501: mov      eax, dword ptr [ebp + 8]
08463504: mov      dword ptr [esp], eax
08463507: call     0x8469c34 ; _ZN8WongWork11CDeathTower13setTowerStateENS0_16ENUM_TOWER_STATEE
0846350C: mov      eax, dword ptr [ebp + 8]
0846350F: mov      eax, dword ptr [eax]
08463511: mov      dword ptr [esp + 4], 2
08463519: mov      dword ptr [esp], eax
0846351C: call     0x814578c ; _ZN6CParty12SetEPLPStateEc
08463521: mov      dword ptr [esp + 4], 0x4d
08463529: mov      eax, dword ptr [ebp + 8]
0846352C: mov      dword ptr [esp], eax
0846352F: call     0x830edec ; _ZN8WongWork11CDeathTower12_genTimerKeyE13TIMER_MESSAGE
08463534: mov      ebx, eax
08463536: mov      eax, dword ptr [ebp + 8]
08463539: mov      esi, dword ptr [eax + 4]
0846353C: call     0x80f647c ; _Z12G_TimerQueuev
08463541: mov      dword ptr [esp + 0x18], 0x4d
08463549: mov      dword ptr [esp + 0x14], ebx
0846354D: mov      dword ptr [esp + 0x10], 0x3c
08463555: mov      dword ptr [esp + 0xc], 0x4d
0846355D: mov      dword ptr [esp + 8], esi
08463561: mov      dword ptr [esp + 4], 6
08463569: mov      dword ptr [esp], eax
0846356C: call     0x8630e16 ; _ZN10TimerQueue11InsertTimerEN10TimerEntry8OBJ_TYPEEi13TIMER_MESSAGEiij
08463571: jmp      0x8463591
08463573: mov      ebx, edx
08463575: mov      esi, eax
08463577: lea      eax, [ebp - 0x90]
0846357D: mov      dword ptr [esp], eax
08463580: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08463585: mov      eax, esi
08463587: mov      edx, ebx
08463589: mov      dword ptr [esp], eax
0846358C: call     0x8ae3750 ; _Unwind_Resume
08463591: lea      eax, [ebp - 0x90]
08463597: mov      dword ptr [esp], eax
0846359A: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
0846359F: jmp      0x84636e2
084635A4: cmp      dword ptr [ebp + 0xc], 0x4d
084635A8: jne      0x84635fa
084635AA: mov      eax, dword ptr [ebp + 8]
084635AD: mov      eax, dword ptr [eax]
084635AF: mov      dword ptr [esp], eax
084635B2: call     0x814581c ; _ZN6CParty12GetEPLPStateEv
084635B7: cmp      al, 2
084635B9: setne    al
084635BC: test     al, al
084635BE: je       0x84635ca
084635C0: mov      eax, 0
084635C5: jmp      0x84636e7
084635CA: mov      eax, dword ptr [ebp + 8]
084635CD: mov      eax, dword ptr [eax]
084635CF: mov      dword ptr [esp], eax
084635D2: call     0x84699da ; _ZN6CParty18GetSelectedEPLPCmdEv
084635D7: cmp      al, 3
084635D9: sete     al
084635DC: test     al, al
084635DE: je       0x84635ea
084635E0: mov      eax, 0
084635E5: jmp      0x84636e7
084635EA: mov      eax, dword ptr [ebp + 8]
084635ED: mov      dword ptr [esp], eax
084635F0: call     0x8467f6e ; _ZN8WongWork11CDeathTower19_onFinishDeathTowerEv
084635F5: jmp      0x84636e2
084635FA: cmp      dword ptr [ebp + 0xc], 0x4e
084635FE: jne      0x8463630
08463600: mov      eax, dword ptr [ebp + 8]
08463603: mov      eax, dword ptr [eax]
08463605: mov      dword ptr [esp], eax
08463608: call     0x814581c ; _ZN6CParty12GetEPLPStateEv
0846360D: cmp      al, 3
0846360F: setne    al
08463612: test     al, al
08463614: je       0x8463620
08463616: mov      eax, 0
0846361B: jmp      0x84636e7
08463620: mov      eax, dword ptr [ebp + 8]
08463623: mov      dword ptr [esp], eax
08463626: call     0x8467f6e ; _ZN8WongWork11CDeathTower19_onFinishDeathTowerEv
0846362B: jmp      0x84636e2
08463630: cmp      dword ptr [ebp + 0xc], 0x4a
08463634: jne      0x84636e2
0846363A: mov      eax, dword ptr [ebp + 8]
0846363D: mov      dword ptr [esp], eax
08463640: call     0x8469c5c ; _ZN8WongWork11CDeathTower13getTowerStateEv
08463645: cmp      eax, 1
08463648: setle    al
0846364B: test     al, al
0846364D: je       0x8463659
0846364F: mov      eax, 0
08463654: jmp      0x84636e7
08463659: mov      eax, dword ptr [ebp + 8]
0846365C: mov      eax, dword ptr [eax]
0846365E: mov      dword ptr [esp], eax
08463661: call     0x859a16a ; _ZN6CParty16get_member_countEv
08463666: test     eax, eax
08463668: setle    al
0846366B: test     al, al
0846366D: je       0x8463681
0846366F: mov      eax, dword ptr [ebp + 8]
08463672: mov      dword ptr [esp], eax
08463675: call     0x8467b20 ; _ZN8WongWork11CDeathTower8_destroyEv
0846367A: mov      eax, 1
0846367F: jmp      0x84636e7
08463681: mov      eax, dword ptr [ebp + 8]
08463684: mov      dword ptr [esp], eax
08463687: call     0x8467188 ; _ZN8WongWork11CDeathTower15_checkMemberDieEv
0846368C: test     al, al
0846368E: je       0x84636a5
08463690: mov      dword ptr [esp + 4], 0
08463698: mov      eax, dword ptr [ebp + 8]
0846369B: mov      dword ptr [esp], eax
0846369E: call     0x8467208 ; _ZN8WongWork11CDeathTower23_beginTowerClearProcessEb
084636A3: jmp      0x84636e2
084636A5: mov      eax, dword ptr [ebp + 8]
084636A8: add      eax, 0x14
084636AB: mov      dword ptr [esp], eax
084636AE: call     0x8469b02 ; _ZN8WongWork11CDeathTower6CStage15getCurrentStageEv
084636B3: mov      ebx, eax
084636B5: mov      eax, dword ptr [ebp + 8]
084636B8: add      eax, 0xc
084636BB: mov      dword ptr [esp], eax
084636BE: call     0x8469ab4 ; _ZN8WongWork11CDeathTower11CDungeonMgr11getEndStageEv
084636C3: cmp      ebx, eax
084636C5: sete     al
084636C8: test     al, al
084636CA: je       0x84636e2
084636CC: mov      dword ptr [esp + 4], 1
084636D4: mov      eax, dword ptr [ebp + 8]
084636D7: mov      dword ptr [esp], eax
084636DA: call     0x8467208 ; _ZN8WongWork11CDeathTower23_beginTowerClearProcessEb
084636DF: jmp      0x84636e2
084636E1: nop
084636E2: mov      eax, 1
084636E7: add      esp, 0x14c
084636ED: pop      ebx
084636EE: pop      esi
084636EF: pop      edi
084636F0: pop      ebp
084636F1: ret
```

## 0x08463890 _ZN8WongWork11CDeathTower24_checkStartGameConditionERh

```asm
08463890: push     ebp
08463891: mov      ebp, esp
08463893: push     esi
08463894: push     ebx
08463895: sub      esp, 0x130
0846389B: lea      eax, [ebp - 0x10c]
084638A1: mov      ebx, eax
084638A3: mov      esi, 3
084638A8: jmp      0x84638b8
084638AA: mov      dword ptr [esp], ebx
084638AD: call     0x80cb854 ; _ZN10Inven_ItemC2Ev
084638B2: add      ebx, 0x3d
084638B5: sub      esi, 1
084638B8: cmp      esi, -1
084638BB: setne    al
084638BE: test     al, al
084638C0: jne      0x84638aa
084638C2: mov      dword ptr [esp + 8], 0xf4
084638CA: mov      dword ptr [esp + 4], 0
084638D2: lea      eax, [ebp - 0x10c]
084638D8: mov      dword ptr [esp], eax
084638DB: call     0x807dcc0
084638E0: mov      dword ptr [esp + 8], 0x10
084638E8: mov      dword ptr [esp + 4], 0
084638F0: lea      eax, [ebp - 0x18]
084638F3: mov      dword ptr [esp], eax
084638F6: call     0x807dcc0
084638FB: mov      eax, dword ptr [ebp + 8]
084638FE: add      eax, 0xc
08463901: mov      dword ptr [esp], eax
08463904: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08463909: mov      edx, dword ptr [ebp + 8]
0846390C: mov      edx, dword ptr [edx]
0846390E: lea      ecx, [ebp - 0x18]
08463911: mov      dword ptr [esp + 0x10], ecx
08463915: lea      ecx, [ebp - 0x10c]
0846391B: mov      dword ptr [esp + 0xc], ecx
0846391F: mov      ecx, dword ptr [ebp + 0xc]
08463922: mov      dword ptr [esp + 8], ecx
08463926: mov      dword ptr [esp + 4], eax
0846392A: mov      dword ptr [esp], edx
0846392D: call     0x859e528 ; _ZN6CParty24CheckEnterAncientDungeonEPK8CDungeonRhP10Inven_ItemPi
08463932: xor      eax, 1
08463935: test     al, al
08463937: je       0x8463940
08463939: mov      eax, 0x11
0846393E: jmp      0x8463983
08463940: mov      eax, dword ptr [ebp + 8]
08463943: add      eax, 0xc
08463946: mov      dword ptr [esp], eax
08463949: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846394E: mov      edx, dword ptr [ebp + 8]
08463951: mov      edx, dword ptr [edx]
08463953: lea      ecx, [ebp - 0x18]
08463956: mov      dword ptr [esp + 0xc], ecx
0846395A: lea      ecx, [ebp - 0x10c]
08463960: mov      dword ptr [esp + 8], ecx
08463964: mov      dword ptr [esp + 4], eax
08463968: mov      dword ptr [esp], edx
0846396B: call     0x859eac2 ; _ZN6CParty22UseAncientDungeonItemsEPK8CDungeonP10Inven_ItemPi
08463970: xor      eax, 1
08463973: test     al, al
08463975: je       0x846397e
08463977: mov      eax, 1
0846397C: jmp      0x8463983
0846397E: mov      eax, 0
08463983: add      esp, 0x130
08463989: pop      ebx
0846398A: pop      esi
0846398B: pop      ebp
0846398C: ret
```

## 0x0846398E _ZN8WongWork11CDeathTower15handleStartGameEic17ENUM_DUNGEON_TYPE

```asm
0846398E: push     ebp
0846398F: mov      ebp, esp
08463991: push     esi
08463992: push     ebx
08463993: sub      esp, 0x60
08463996: mov      eax, dword ptr [ebp + 0x10]
08463999: mov      byte ptr [ebp - 0x4c], al
0846399C: mov      byte ptr [ebp - 0x21], 0
084639A0: cmp      dword ptr [ebp + 0x14], 1
084639A4: jne      0x84639d2
084639A6: mov      dword ptr [esp], 4
084639AD: call     0x8725800 ; __cxa_allocate_exception
084639B2: mov      edx, eax
084639B4: mov      dword ptr [edx], 8
084639BA: mov      dword ptr [esp + 8], 0
084639C2: mov      dword ptr [esp + 4], 0x8b75f40
084639CA: mov      dword ptr [esp], eax
084639CD: call     0x8724c50 ; __cxa_throw
084639D2: mov      eax, dword ptr [ebp + 8]
084639D5: add      eax, 0xc
084639D8: mov      dword ptr [esp], eax
084639DB: call     0x82a7498 ; _ZN8WongWork11CDeathTower11CDungeonMgr5resetEv
084639E0: mov      eax, dword ptr [ebp + 8]
084639E3: lea      edx, [eax + 0xc]
084639E6: mov      eax, dword ptr [ebp + 0xc]
084639E9: mov      dword ptr [esp + 4], eax
084639ED: mov      dword ptr [esp], edx
084639F0: call     0x84605a2 ; _ZN8WongWork11CDeathTower11CDungeonMgr14initDungeonMgrEi
084639F5: xor      eax, 1
084639F8: test     al, al
084639FA: je       0x8463a28
084639FC: mov      dword ptr [esp], 4
08463A03: call     0x8725800 ; __cxa_allocate_exception
08463A08: mov      edx, eax
08463A0A: mov      dword ptr [edx], 0x16
08463A10: mov      dword ptr [esp + 8], 0
08463A18: mov      dword ptr [esp + 4], 0x8b75f40
08463A20: mov      dword ptr [esp], eax
08463A23: call     0x8724c50 ; __cxa_throw
08463A28: mov      dword ptr [ebp - 0x20], 0
08463A2F: lea      eax, [ebp - 0x21]
08463A32: mov      dword ptr [esp + 4], eax
08463A36: mov      eax, dword ptr [ebp + 8]
08463A39: mov      dword ptr [esp], eax
08463A3C: call     0x8463890 ; _ZN8WongWork11CDeathTower24_checkStartGameConditionERh
08463A41: mov      dword ptr [ebp - 0x20], eax
08463A44: cmp      dword ptr [ebp - 0x20], 0
08463A48: setne    al
08463A4B: test     al, al
08463A4D: je       0x8463a7a
08463A4F: mov      dword ptr [esp], 4
08463A56: call     0x8725800 ; __cxa_allocate_exception
08463A5B: mov      edx, eax
08463A5D: mov      ecx, dword ptr [ebp - 0x20]
08463A60: mov      dword ptr [edx], ecx
08463A62: mov      dword ptr [esp + 8], 0
08463A6A: mov      dword ptr [esp + 4], 0x8b75f40
08463A72: mov      dword ptr [esp], eax
08463A75: call     0x8724c50 ; __cxa_throw
08463A7A: mov      eax, dword ptr [ebp + 8]
08463A7D: add      eax, 0x14
08463A80: mov      dword ptr [esp], eax
08463A83: call     0x84614ec ; _ZN8WongWork11CDeathTower6CStage5resetEv
08463A88: mov      eax, dword ptr [ebp + 8]
08463A8B: add      eax, 0x14
08463A8E: mov      dword ptr [esp], eax
08463A91: call     0x82a74bc ; _ZN8WongWork11CDeathTower6CStage17resetCurrentStageEv
08463A96: mov      eax, dword ptr [ebp + 8]
08463A99: add      eax, 0x14
08463A9C: mov      dword ptr [esp], eax
08463A9F: call     0x8469b02 ; _ZN8WongWork11CDeathTower6CStage15getCurrentStageEv
08463AA4: mov      edx, dword ptr [ebp + 8]
08463AA7: add      edx, 0xc
08463AAA: mov      dword ptr [esp + 4], eax
08463AAE: mov      dword ptr [esp], edx
08463AB1: call     0x84605ee ; _ZN8WongWork11CDeathTower11CDungeonMgr11getStageMapEj
08463AB6: mov      dword ptr [ebp - 0x1c], eax
08463AB9: cmp      dword ptr [ebp - 0x1c], 0
08463ABD: jne      0x8463aeb
08463ABF: mov      dword ptr [esp], 4
08463AC6: call     0x8725800 ; __cxa_allocate_exception
08463ACB: mov      edx, eax
08463ACD: mov      dword ptr [edx], 0x15
08463AD3: mov      dword ptr [esp + 8], 0
08463ADB: mov      dword ptr [esp + 4], 0x8b75f40
08463AE3: mov      dword ptr [esp], eax
08463AE6: call     0x8724c50 ; __cxa_throw
08463AEB: mov      eax, dword ptr [ebp + 8]
08463AEE: lea      ecx, [eax + 0xc]
08463AF1: mov      eax, dword ptr [ebp + 8]
08463AF4: lea      edx, [eax + 0x14]
08463AF7: mov      eax, dword ptr [ebp + 8]
08463AFA: mov      dword ptr [esp + 0xc], eax
08463AFE: mov      eax, dword ptr [ebp - 0x1c]
08463B01: mov      dword ptr [esp + 8], eax
08463B05: mov      dword ptr [esp + 4], ecx
08463B09: mov      dword ptr [esp], edx
08463B0C: call     0x8460634 ; _ZN8WongWork11CDeathTower6CStage10consistMapERKNS0_11CDungeonMgrEPK4CMapPS0_
08463B11: xor      eax, 1
08463B14: test     al, al
08463B16: je       0x8463b44
08463B18: mov      dword ptr [esp], 4
08463B1F: call     0x8725800 ; __cxa_allocate_exception
08463B24: mov      edx, eax
08463B26: mov      dword ptr [edx], 1
08463B2C: mov      dword ptr [esp + 8], 0
08463B34: mov      dword ptr [esp + 4], 0x8b75f40
08463B3C: mov      dword ptr [esp], eax
08463B3F: call     0x8724c50 ; __cxa_throw
08463B44: mov      eax, dword ptr [ebp + 8]
08463B47: add      eax, 0x110
08463B4C: mov      dword ptr [esp], eax
08463B4F: call     0x8461f18 ; _ZN8WongWork11CDeathTower9CPlayData5resetEv
08463B54: mov      eax, dword ptr [ebp + 8]
08463B57: mov      eax, dword ptr [eax]
08463B59: mov      edx, dword ptr [ebp + 8]
08463B5C: add      edx, 0x110
08463B62: mov      dword ptr [esp + 4], eax
08463B66: mov      dword ptr [esp], edx
08463B69: call     0x8461d02 ; _ZN8WongWork11CDeathTower9CPlayData19makeStartMemberInfoEP6CParty
08463B6E: mov      dword ptr [ebp - 0x18], 0
08463B75: jmp      0x8463cab
08463B7A: mov      dword ptr [ebp - 0x14], 0
08463B81: mov      eax, dword ptr [ebp + 8]
08463B84: mov      eax, dword ptr [eax]
08463B86: mov      edx, dword ptr [ebp - 0x18]
08463B89: mov      dword ptr [esp + 4], edx
08463B8D: mov      dword ptr [esp], eax
08463B90: call     0x8145764 ; _ZN6CParty8get_userEi
08463B95: mov      dword ptr [ebp - 0x14], eax
08463B98: cmp      dword ptr [ebp - 0x14], 0
08463B9C: je       0x8463bb9
08463B9E: mov      eax, dword ptr [ebp + 8]
08463BA1: mov      eax, dword ptr [eax]
08463BA3: mov      edx, dword ptr [ebp - 0x18]
08463BA6: mov      dword ptr [esp + 4], edx
08463BAA: mov      dword ptr [esp], eax
08463BAD: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08463BB2: xor      eax, 1
08463BB5: test     al, al
08463BB7: je       0x8463bc0
08463BB9: mov      eax, 1
08463BBE: jmp      0x8463bc5
08463BC0: mov      eax, 0
08463BC5: test     al, al
08463BC7: jne      0x8463ca6
08463BCD: mov      eax, dword ptr [ebp + 8]
08463BD0: add      eax, 0xc
08463BD3: mov      dword ptr [esp], eax
08463BD6: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08463BDB: mov      dword ptr [esp], eax
08463BDE: call     0x814559a ; _ZNK8CDungeon13get_min_levelEv
08463BE3: mov      dword ptr [ebp - 0x10], eax
08463BE6: mov      eax, dword ptr [ebp - 0x14]
08463BE9: mov      dword ptr [esp], eax
08463BEC: call     0x80da2b8 ; _ZNK15CUserCharacInfo16get_charac_levelEv
08463BF1: cmp      eax, dword ptr [ebp - 0x10]
08463BF4: setl     al
08463BF7: test     al, al
08463BF9: je       0x8463c27
08463BFB: mov      dword ptr [esp], 4
08463C02: call     0x8725800 ; __cxa_allocate_exception
08463C07: mov      edx, eax
08463C09: mov      dword ptr [edx], 0xe
08463C0F: mov      dword ptr [esp + 8], 0
08463C17: mov      dword ptr [esp + 4], 0x8b75f40
08463C1F: mov      dword ptr [esp], eax
08463C22: call     0x8724c50 ; __cxa_throw
08463C27: mov      dword ptr [esp + 4], 0xa
08463C2F: mov      eax, dword ptr [ebp - 0x14]
08463C32: mov      dword ptr [esp], eax
08463C35: call     0x867edb2 ; _ZN5CUser9set_stateE8ch_state
08463C3A: mov      eax, dword ptr [ebp + 8]
08463C3D: lea      edx, [eax + 0x110]
08463C43: mov      dword ptr [esp + 8], 1
08463C4B: mov      eax, dword ptr [ebp - 0x18]
08463C4E: mov      dword ptr [esp + 4], eax
08463C52: mov      dword ptr [esp], edx
08463C55: call     0x8469b5a ; _ZN8WongWork11CDeathTower9CPlayData14setMemberAliveEib
08463C5A: mov      eax, dword ptr [ebp + 8]
08463C5D: mov      eax, dword ptr [eax]
08463C5F: mov      eax, dword ptr [eax + 0xcd8]
08463C65: cmp      eax, 1
08463C68: je       0x8463c88
08463C6A: call     0x860e15b ; _Z29GetInstanceDungeonEntranceLogv
08463C6F: mov      dword ptr [esp + 8], 0
08463C77: mov      edx, dword ptr [ebp + 0xc]
08463C7A: mov      dword ptr [esp + 4], edx
08463C7E: mov      dword ptr [esp], eax
08463C81: call     0x860e1ee ; _ZN19CDungeonEntranceLog24IncrementDungeonEntranceEib
08463C86: jmp      0x8463ca7
08463C88: call     0x860e15b ; _Z29GetInstanceDungeonEntranceLogv
08463C8D: mov      dword ptr [esp + 8], 1
08463C95: mov      edx, dword ptr [ebp + 0xc]
08463C98: mov      dword ptr [esp + 4], edx
08463C9C: mov      dword ptr [esp], eax
08463C9F: call     0x860e1ee ; _ZN19CDungeonEntranceLog24IncrementDungeonEntranceEib
08463CA4: jmp      0x8463ca7
08463CA6: nop
08463CA7: add      dword ptr [ebp - 0x18], 1
08463CAB: cmp      dword ptr [ebp - 0x18], 3
08463CAF: setle    al
08463CB2: test     al, al
08463CB4: jne      0x8463b7a
08463CBA: mov      eax, dword ptr [ebp + 8]
08463CBD: mov      eax, dword ptr [eax]
08463CBF: mov      dword ptr [esp + 4], 0x2d
08463CC7: mov      dword ptr [esp], eax
08463CCA: call     0x859ae76 ; _ZN6CParty13gen_timer_keyE13TIMER_MESSAGE
08463CCF: mov      eax, dword ptr [ebp + 8]
08463CD2: mov      eax, dword ptr [eax]
08463CD4: mov      dword ptr [esp + 4], 0xffffffff
08463CDC: mov      dword ptr [esp], eax
08463CDF: call     0x8145828 ; _ZN6CParty18SetSelectedEPLPCmdEc
08463CE4: mov      eax, dword ptr [ebp + 8]
08463CE7: mov      eax, dword ptr [eax]
08463CE9: add      eax, 0xb24
08463CEE: mov      dword ptr [esp + 4], 0
08463CF6: mov      dword ptr [esp], eax
08463CF9: call     0x830aaa4 ; _ZN13CBattle_Field24getRandomBuffDungeonTypeEi
08463CFE: mov      edx, dword ptr [ebp + 8]
08463D01: mov      edx, dword ptr [edx]
08463D03: mov      dword ptr [esp + 4], eax
08463D07: mov      dword ptr [esp], edx
08463D0A: call     0x859b814 ; _ZN6CParty18choose_random_buffEN10QuickParty17RandomBuffDungeonE
08463D0F: lea      eax, [ebp - 0x30]
08463D12: mov      dword ptr [esp], eax
08463D15: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08463D1A: mov      dword ptr [esp + 8], 0x8e
08463D22: mov      dword ptr [esp + 4], 0
08463D2A: lea      eax, [ebp - 0x30]
08463D2D: mov      dword ptr [esp], eax
08463D30: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08463D35: mov      eax, dword ptr [ebp + 0xc]
08463D38: mov      dword ptr [esp + 4], eax
08463D3C: lea      eax, [ebp - 0x30]
08463D3F: mov      dword ptr [esp], eax
08463D42: call     0x80cb93c ; _ZN18InterfacePacketBuf7put_intEi
08463D47: mov      eax, dword ptr [ebp + 8]
08463D4A: add      eax, 0xc
08463D4D: mov      dword ptr [esp], eax
08463D50: call     0x8469ab4 ; _ZN8WongWork11CDeathTower11CDungeonMgr11getEndStageEv
08463D55: mov      dword ptr [esp + 4], eax
08463D59: lea      eax, [ebp - 0x30]
08463D5C: mov      dword ptr [esp], eax
08463D5F: call     0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
08463D64: mov      eax, dword ptr [ebp + 8]
08463D67: mov      eax, dword ptr [eax]
08463D69: mov      eax, dword ptr [eax + 0xcd8]
08463D6F: mov      dword ptr [esp + 4], eax
08463D73: lea      eax, [ebp - 0x30]
08463D76: mov      dword ptr [esp], eax
08463D79: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463D7E: mov      eax, dword ptr [ebp + 8]
08463D81: mov      eax, dword ptr [eax]
08463D83: test     eax, eax
08463D85: je       0x8463da5
08463D87: mov      eax, dword ptr [ebp + 8]
08463D8A: mov      eax, dword ptr [eax]
08463D8C: mov      dword ptr [esp], eax
08463D8F: call     0x830ed3a ; _ZN6CParty20get_random_buff_typeEv
08463D94: mov      dword ptr [esp + 4], eax
08463D98: lea      eax, [ebp - 0x30]
08463D9B: mov      dword ptr [esp], eax
08463D9E: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463DA3: jmp      0x8463db8
08463DA5: mov      dword ptr [esp + 4], 0xb
08463DAD: lea      eax, [ebp - 0x30]
08463DB0: mov      dword ptr [esp], eax
08463DB3: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463DB8: mov      dword ptr [esp + 4], 1
08463DC0: lea      eax, [ebp - 0x30]
08463DC3: mov      dword ptr [esp], eax
08463DC6: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08463DCB: mov      eax, dword ptr [ebp + 8]
08463DCE: mov      eax, dword ptr [eax]
08463DD0: lea      edx, [ebp - 0x30]
08463DD3: mov      dword ptr [esp + 4], edx
08463DD7: mov      dword ptr [esp], eax
08463DDA: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08463DDF: lea      eax, [ebp - 0x30]
08463DE2: mov      dword ptr [esp], eax
08463DE5: call     0x80cb8e6 ; _ZN18InterfacePacketBuf5clearEv
08463DEA: mov      dword ptr [esp + 8], 0x8f
08463DF2: mov      dword ptr [esp + 4], 0
08463DFA: lea      eax, [ebp - 0x30]
08463DFD: mov      dword ptr [esp], eax
08463E00: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08463E05: mov      eax, dword ptr [ebp + 8]
08463E08: lea      edx, [eax + 0x14]
08463E0B: lea      eax, [ebp - 0x30]
08463E0E: mov      dword ptr [esp + 4], eax
08463E12: mov      dword ptr [esp], edx
08463E15: call     0x8461828 ; _ZN8WongWork11CDeathTower6CStage15makeStagePacketER11PacketGuard
08463E1A: mov      dword ptr [esp + 4], 1
08463E22: lea      eax, [ebp - 0x30]
08463E25: mov      dword ptr [esp], eax
08463E28: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08463E2D: mov      eax, dword ptr [ebp + 8]
08463E30: mov      eax, dword ptr [eax]
08463E32: lea      edx, [ebp - 0x30]
08463E35: mov      dword ptr [esp + 4], edx
08463E39: mov      dword ptr [esp], eax
08463E3C: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08463E41: mov      eax, dword ptr [ebp + 8]
08463E44: mov      dword ptr [esp], eax
08463E47: call     0x84670f8 ; _ZN8WongWork11CDeathTower17_checkMemberReadyEv
08463E4C: mov      dword ptr [esp + 4], 2
08463E54: mov      eax, dword ptr [ebp + 8]
08463E57: mov      dword ptr [esp], eax
08463E5A: call     0x8469c34 ; _ZN8WongWork11CDeathTower13setTowerStateENS0_16ENUM_TOWER_STATEE
08463E5F: mov      eax, dword ptr [ebp + 8]
08463E62: mov      dword ptr [esp], eax
08463E65: call     0x8467b50 ; _ZN8WongWork11CDeathTower18_onStartDeathTowerEv
08463E6A: jmp      0x8463e81
08463E6C: mov      ebx, edx
08463E6E: mov      esi, eax
08463E70: lea      eax, [ebp - 0x30]
08463E73: mov      dword ptr [esp], eax
08463E76: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08463E7B: mov      eax, esi
08463E7D: mov      edx, ebx
08463E7F: jmp      0x8463e91
08463E81: lea      eax, [ebp - 0x30]
08463E84: mov      dword ptr [esp], eax
08463E87: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08463E8C: jmp      0x8464028
08463E91: cmp      edx, 1
08463E94: je       0x8463e9e
08463E96: mov      dword ptr [esp], eax
08463E99: call     0x8ae3750 ; _Unwind_Resume
08463E9E: mov      dword ptr [esp], eax
08463EA1: call     0x8725ce0 ; __cxa_begin_catch
08463EA6: mov      eax, dword ptr [eax]
08463EA8: mov      dword ptr [ebp - 0xc], eax
08463EAB: cmp      dword ptr [ebp - 0xc], 0x11
08463EAF: jne      0x8463f68
08463EB5: lea      eax, [ebp - 0x3c]
08463EB8: mov      dword ptr [esp], eax
08463EBB: call     0x858dd4c ; _ZN11PacketGuardC2Ev
08463EC0: mov      dword ptr [esp + 8], 0x10
08463EC8: mov      dword ptr [esp + 4], 1
08463ED0: lea      eax, [ebp - 0x3c]
08463ED3: mov      dword ptr [esp], eax
08463ED6: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08463EDB: mov      dword ptr [esp + 4], 0
08463EE3: lea      eax, [ebp - 0x3c]
08463EE6: mov      dword ptr [esp], eax
08463EE9: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463EEE: mov      dword ptr [esp + 4], 0x11
08463EF6: lea      eax, [ebp - 0x3c]
08463EF9: mov      dword ptr [esp], eax
08463EFC: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463F01: movzx    eax, byte ptr [ebp - 0x21]
08463F05: movzx    eax, al
08463F08: mov      dword ptr [esp + 4], eax
08463F0C: lea      eax, [ebp - 0x3c]
08463F0F: mov      dword ptr [esp], eax
08463F12: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463F17: mov      dword ptr [esp + 4], 1
08463F1F: lea      eax, [ebp - 0x3c]
08463F22: mov      dword ptr [esp], eax
08463F25: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08463F2A: mov      eax, dword ptr [ebp + 8]
08463F2D: mov      eax, dword ptr [eax]
08463F2F: lea      edx, [ebp - 0x3c]
08463F32: mov      dword ptr [esp + 4], edx
08463F36: mov      dword ptr [esp], eax
08463F39: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08463F3E: jmp      0x8463f58
08463F40: mov      ebx, edx
08463F42: mov      esi, eax
08463F44: lea      eax, [ebp - 0x3c]
08463F47: mov      dword ptr [esp], eax
08463F4A: call     0x858de80 ; _ZN11PacketGuardD1Ev
08463F4F: mov      eax, esi
08463F51: mov      edx, ebx
08463F53: jmp      0x8464013
08463F58: lea      eax, [ebp - 0x3c]
08463F5B: mov      dword ptr [esp], eax
08463F5E: call     0x858de80 ; _ZN11PacketGuardD1Ev
08463F63: jmp      0x8463ffc
08463F68: lea      eax, [ebp - 0x48]
08463F6B: mov      dword ptr [esp], eax
08463F6E: call     0x858dd4c ; _ZN11PacketGuardC2Ev
08463F73: mov      dword ptr [esp + 8], 0x10
08463F7B: mov      dword ptr [esp + 4], 1
08463F83: lea      eax, [ebp - 0x48]
08463F86: mov      dword ptr [esp], eax
08463F89: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08463F8E: mov      dword ptr [esp + 4], 0
08463F96: lea      eax, [ebp - 0x48]
08463F99: mov      dword ptr [esp], eax
08463F9C: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463FA1: mov      eax, dword ptr [ebp - 0xc]
08463FA4: mov      dword ptr [esp + 4], eax
08463FA8: lea      eax, [ebp - 0x48]
08463FAB: mov      dword ptr [esp], eax
08463FAE: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08463FB3: mov      dword ptr [esp + 4], 1
08463FBB: lea      eax, [ebp - 0x48]
08463FBE: mov      dword ptr [esp], eax
08463FC1: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08463FC6: mov      eax, dword ptr [ebp + 8]
08463FC9: mov      eax, dword ptr [eax]
08463FCB: lea      edx, [ebp - 0x48]
08463FCE: mov      dword ptr [esp + 4], edx
08463FD2: mov      dword ptr [esp], eax
08463FD5: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08463FDA: jmp      0x8463ff1
08463FDC: mov      ebx, edx
08463FDE: mov      esi, eax
08463FE0: lea      eax, [ebp - 0x48]
08463FE3: mov      dword ptr [esp], eax
08463FE6: call     0x858de80 ; _ZN11PacketGuardD1Ev
08463FEB: mov      eax, esi
08463FED: mov      edx, ebx
08463FEF: jmp      0x8464013
08463FF1: lea      eax, [ebp - 0x48]
08463FF4: mov      dword ptr [esp], eax
08463FF7: call     0x858de80 ; _ZN11PacketGuardD1Ev
08463FFC: mov      eax, dword ptr [ebp + 8]
08463FFF: mov      dword ptr [esp], eax
08464002: call     0x8467b20 ; _ZN8WongWork11CDeathTower8_destroyEv
08464007: mov      ebx, 0
0846400C: call     0x8725c30 ; __cxa_end_catch
08464011: jmp      0x846402d
08464013: mov      ebx, edx
08464015: mov      esi, eax
08464017: call     0x8725c30 ; __cxa_end_catch
0846401C: mov      eax, esi
0846401E: mov      edx, ebx
08464020: mov      dword ptr [esp], eax
08464023: call     0x8ae3750 ; _Unwind_Resume
08464028: mov      ebx, 1
0846402D: mov      eax, ebx
0846402F: add      esp, 0x60
08464032: pop      ebx
08464033: pop      esi
08464034: pop      ebp
08464035: ret
```

## 0x0846595A _ZN8WongWork11CDeathTower13handleDieUserEP5CUser

```asm
0846595A: push     ebp
0846595B: mov      ebp, esp
0846595D: push     esi
0846595E: push     ebx
0846595F: sub      esp, 0x20
08465962: mov      dword ptr [ebp - 0xc], 0
08465969: mov      dword ptr [ebp - 0xc], 0
08465970: jmp      0x8465a81
08465975: mov      eax, dword ptr [ebp + 8]
08465978: mov      eax, dword ptr [eax]
0846597A: mov      edx, dword ptr [ebp - 0xc]
0846597D: mov      dword ptr [esp + 4], edx
08465981: mov      dword ptr [esp], eax
08465984: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08465989: test     al, al
0846598B: je       0x8465a7d
08465991: mov      eax, dword ptr [ebp + 8]
08465994: mov      eax, dword ptr [eax]
08465996: mov      edx, dword ptr [ebp - 0xc]
08465999: mov      dword ptr [esp + 4], edx
0846599D: mov      dword ptr [esp], eax
084659A0: call     0x8145764 ; _ZN6CParty8get_userEi
084659A5: cmp      eax, dword ptr [ebp + 0xc]
084659A8: sete     al
084659AB: test     al, al
084659AD: je       0x8465a7d
084659B3: mov      eax, dword ptr [ebp + 8]
084659B6: lea      edx, [eax + 0x110]
084659BC: mov      eax, dword ptr [ebp - 0xc]
084659BF: mov      dword ptr [esp + 4], eax
084659C3: mov      dword ptr [esp], edx
084659C6: call     0x8469b76 ; _ZN8WongWork11CDeathTower9CPlayData13isMemberAliveEi
084659CB: xor      eax, 1
084659CE: test     al, al
084659D0: je       0x84659f7
084659D2: mov      dword ptr [esp + 8], 0x12
084659DA: mov      dword ptr [esp + 4], 0x2b
084659E2: mov      eax, dword ptr [ebp + 0xc]
084659E5: mov      dword ptr [esp], eax
084659E8: call     0x867bf42 ; _ZN5CUser18SendCmdErrorPacketE14ENUM_CMDPACKETh
084659ED: mov      eax, 0
084659F2: jmp      0x8465aba
084659F7: mov      eax, dword ptr [ebp + 8]
084659FA: lea      edx, [eax + 0x110]
08465A00: mov      dword ptr [esp + 8], 0
08465A08: mov      eax, dword ptr [ebp - 0xc]
08465A0B: mov      dword ptr [esp + 4], eax
08465A0F: mov      dword ptr [esp], edx
08465A12: call     0x8469b5a ; _ZN8WongWork11CDeathTower9CPlayData14setMemberAliveEib
08465A17: lea      eax, [ebp - 0x18]
08465A1A: mov      dword ptr [esp], eax
08465A1D: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465A22: mov      eax, dword ptr [ebp + 0xc]
08465A25: mov      dword ptr [esp], eax
08465A28: call     0x80da37c ; _ZNK5CUser13get_unique_idEv
08465A2D: movzx    eax, ax
08465A30: mov      dword ptr [esp + 4], eax
08465A34: lea      eax, [ebp - 0x18]
08465A37: mov      dword ptr [esp], eax
08465A3A: call     0x8460176 ; _ZN8WongWork11CDeathTower14CPacketHandler12makeDieStateEt
08465A3F: mov      eax, dword ptr [ebp + 8]
08465A42: mov      eax, dword ptr [eax]
08465A44: lea      edx, [ebp - 0x18]
08465A47: mov      dword ptr [esp + 4], edx
08465A4B: mov      dword ptr [esp], eax
08465A4E: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08465A53: jmp      0x8465a70
08465A55: mov      ebx, edx
08465A57: mov      esi, eax
08465A59: lea      eax, [ebp - 0x18]
08465A5C: mov      dword ptr [esp], eax
08465A5F: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465A64: mov      eax, esi
08465A66: mov      edx, ebx
08465A68: mov      dword ptr [esp], eax
08465A6B: call     0x8ae3750 ; _Unwind_Resume
08465A70: lea      eax, [ebp - 0x18]
08465A73: mov      dword ptr [esp], eax
08465A76: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465A7B: jmp      0x8465a90
08465A7D: add      dword ptr [ebp - 0xc], 1
08465A81: cmp      dword ptr [ebp - 0xc], 3
08465A85: setle    al
08465A88: test     al, al
08465A8A: jne      0x8465975
08465A90: mov      eax, dword ptr [ebp + 8]
08465A93: mov      dword ptr [esp], eax
08465A96: call     0x8467188 ; _ZN8WongWork11CDeathTower15_checkMemberDieEv
08465A9B: test     al, al
08465A9D: je       0x8465ab5
08465A9F: mov      eax, dword ptr [ebp + 8]
08465AA2: mov      dword ptr [esp], eax
08465AA5: call     0x8467cc2 ; _ZN8WongWork11CDeathTower14_onFinishStageEv
08465AAA: mov      eax, dword ptr [ebp + 8]
08465AAD: mov      dword ptr [esp], eax
08465AB0: call     0x8467e00 ; _ZN8WongWork11CDeathTower21_onPrepareFinishTowerEv
08465AB5: mov      eax, 1
08465ABA: add      esp, 0x20
08465ABD: pop      ebx
08465ABE: pop      esi
08465ABF: pop      ebp
08465AC0: ret
```

## 0x08465AC2 _ZN8WongWork11CDeathTower15handleLeaveUserEP5CUser

```asm
08465AC2: push     ebp
08465AC3: mov      ebp, esp
08465AC5: push     esi
08465AC6: push     ebx
08465AC7: sub      esp, 0x20
08465ACA: mov      dword ptr [ebp - 0xc], 0
08465AD1: mov      dword ptr [ebp - 0xc], 0
08465AD8: jmp      0x8465b50
08465ADA: mov      eax, dword ptr [ebp + 8]
08465ADD: mov      eax, dword ptr [eax]
08465ADF: mov      edx, dword ptr [ebp - 0xc]
08465AE2: mov      dword ptr [esp + 4], edx
08465AE6: mov      dword ptr [esp], eax
08465AE9: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08465AEE: test     al, al
08465AF0: je       0x8465b4c
08465AF2: mov      eax, dword ptr [ebp + 8]
08465AF5: mov      eax, dword ptr [eax]
08465AF7: mov      edx, dword ptr [ebp - 0xc]
08465AFA: mov      dword ptr [esp + 4], edx
08465AFE: mov      dword ptr [esp], eax
08465B01: call     0x8145764 ; _ZN6CParty8get_userEi
08465B06: cmp      eax, dword ptr [ebp + 0xc]
08465B09: sete     al
08465B0C: test     al, al
08465B0E: je       0x8465b4c
08465B10: mov      eax, dword ptr [ebp + 8]
08465B13: mov      eax, dword ptr [eax]
08465B15: mov      dword ptr [esp + 8], 2
08465B1D: mov      edx, dword ptr [ebp + 0xc]
08465B20: mov      dword ptr [esp + 4], edx
08465B24: mov      dword ptr [esp], eax
08465B27: call     0x859c114 ; _ZN6CParty10leave_userEP5CUser20ENUM_PARTY_INFO_TYPE
08465B2C: mov      eax, dword ptr [ebp + 8]
08465B2F: lea      edx, [eax + 0x110]
08465B35: mov      dword ptr [esp + 8], 0
08465B3D: mov      eax, dword ptr [ebp - 0xc]
08465B40: mov      dword ptr [esp + 4], eax
08465B44: mov      dword ptr [esp], edx
08465B47: call     0x8469b5a ; _ZN8WongWork11CDeathTower9CPlayData14setMemberAliveEib
08465B4C: add      dword ptr [ebp - 0xc], 1
08465B50: cmp      dword ptr [ebp - 0xc], 3
08465B54: setle    al
08465B57: test     al, al
08465B59: jne      0x8465ada
08465B5F: call     0x80da3a7 ; _Z11G_GameWorldv
08465B64: mov      edx, dword ptr [ebp + 0xc]
08465B67: mov      dword ptr [esp + 4], edx
08465B6B: mov      dword ptr [esp], eax
08465B6E: call     0x86c6a16 ; _ZN9GameWorld16out_from_dungeonEP5CUser
08465B73: lea      eax, [ebp - 0x18]
08465B76: mov      dword ptr [esp], eax
08465B79: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465B7E: mov      dword ptr [esp + 8], 2
08465B86: mov      dword ptr [esp + 4], 0
08465B8E: lea      eax, [ebp - 0x18]
08465B91: mov      dword ptr [esp], eax
08465B94: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08465B99: mov      dword ptr [esp + 4], 0
08465BA1: lea      eax, [ebp - 0x18]
08465BA4: mov      dword ptr [esp], eax
08465BA7: call     0x80cb920 ; _ZN18InterfacePacketBuf8put_byteEi
08465BAC: mov      dword ptr [esp + 4], 1
08465BB4: lea      eax, [ebp - 0x18]
08465BB7: mov      dword ptr [esp], eax
08465BBA: call     0x80d9ea4 ; _ZN18InterfacePacketBuf9put_shortEi
08465BBF: lea      eax, [ebp - 0x18]
08465BC2: mov      dword ptr [esp + 8], 0
08465BCA: mov      dword ptr [esp + 4], eax
08465BCE: mov      eax, dword ptr [ebp + 0xc]
08465BD1: mov      dword ptr [esp], eax
08465BD4: call     0x865a44e ; _ZN5CUser15make_basic_infoEPcc
08465BD9: mov      dword ptr [esp + 4], 1
08465BE1: lea      eax, [ebp - 0x18]
08465BE4: mov      dword ptr [esp], eax
08465BE7: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08465BEC: call     0x80da3a7 ; _Z11G_GameWorldv
08465BF1: mov      edx, dword ptr [ebp + 0xc]
08465BF4: mov      dword ptr [esp + 8], edx
08465BF8: lea      edx, [ebp - 0x18]
08465BFB: mov      dword ptr [esp + 4], edx
08465BFF: mov      dword ptr [esp], eax
08465C02: call     0x813495c ; _ZN9GameWorld8send_allER11PacketGuardP5CUser
08465C07: mov      eax, dword ptr [ebp + 0xc]
08465C0A: mov      dword ptr [esp + 4], eax
08465C0E: mov      eax, dword ptr [ebp + 8]
08465C11: mov      dword ptr [esp], eax
08465C14: call     0x84636f2 ; _ZN8WongWork11CDeathTower11onLeaveUserEP5CUser
08465C19: mov      eax, dword ptr [ebp + 8]
08465C1C: mov      eax, dword ptr [eax]
08465C1E: mov      dword ptr [esp], eax
08465C21: call     0x859a16a ; _ZN6CParty16get_member_countEv
08465C26: test     eax, eax
08465C28: jle      0x8465c40
08465C2A: mov      eax, dword ptr [ebp + 8]
08465C2D: mov      dword ptr [esp], eax
08465C30: call     0x8467188 ; _ZN8WongWork11CDeathTower15_checkMemberDieEv
08465C35: test     al, al
08465C37: je       0x8465c40
08465C39: mov      eax, 1
08465C3E: jmp      0x8465c45
08465C40: mov      eax, 0
08465C45: test     al, al
08465C47: je       0x8465c5f
08465C49: mov      eax, dword ptr [ebp + 8]
08465C4C: mov      dword ptr [esp], eax
08465C4F: call     0x8467cc2 ; _ZN8WongWork11CDeathTower14_onFinishStageEv
08465C54: mov      eax, dword ptr [ebp + 8]
08465C57: mov      dword ptr [esp], eax
08465C5A: call     0x8467e00 ; _ZN8WongWork11CDeathTower21_onPrepareFinishTowerEv
08465C5F: mov      ebx, 1
08465C64: lea      eax, [ebp - 0x18]
08465C67: mov      dword ptr [esp], eax
08465C6A: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465C6F: mov      eax, ebx
08465C71: add      esp, 0x20
08465C74: pop      ebx
08465C75: pop      esi
08465C76: pop      ebp
08465C77: ret
08465C78: mov      ebx, edx
08465C7A: mov      esi, eax
08465C7C: lea      eax, [ebp - 0x18]
08465C7F: mov      dword ptr [esp], eax
08465C82: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465C87: mov      eax, esi
08465C89: mov      edx, ebx
08465C8B: mov      dword ptr [esp], eax
08465C8E: call     0x8ae3750 ; _Unwind_Resume
```

## 0x08465C94 _ZN8WongWork11CDeathTower13handleMoveMapEv

```asm
08465C94: push     ebp
08465C95: mov      ebp, esp
08465C97: push     esi
08465C98: push     ebx
08465C99: sub      esp, 0x60
08465C9C: mov      eax, dword ptr [ebp + 8]
08465C9F: mov      dword ptr [esp], eax
08465CA2: call     0x8467188 ; _ZN8WongWork11CDeathTower15_checkMemberDieEv
08465CA7: test     al, al
08465CA9: je       0x8465d15
08465CAB: lea      eax, [ebp - 0x1c]
08465CAE: mov      dword ptr [esp], eax
08465CB1: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465CB6: mov      dword ptr [esp + 8], 0x16
08465CBE: mov      dword ptr [esp + 4], 0x30
08465CC6: lea      eax, [ebp - 0x1c]
08465CC9: mov      dword ptr [esp], eax
08465CCC: call     0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
08465CD1: mov      eax, dword ptr [ebp + 8]
08465CD4: mov      eax, dword ptr [eax]
08465CD6: lea      edx, [ebp - 0x1c]
08465CD9: mov      dword ptr [esp + 4], edx
08465CDD: mov      dword ptr [esp], eax
08465CE0: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08465CE5: mov      ebx, 0
08465CEA: lea      eax, [ebp - 0x1c]
08465CED: mov      dword ptr [esp], eax
08465CF0: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465CF5: jmp      0x846601b
08465CFA: mov      ebx, edx
08465CFC: mov      esi, eax
08465CFE: lea      eax, [ebp - 0x1c]
08465D01: mov      dword ptr [esp], eax
08465D04: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465D09: mov      eax, esi
08465D0B: mov      edx, ebx
08465D0D: mov      dword ptr [esp], eax
08465D10: call     0x8ae3750 ; _Unwind_Resume
08465D15: mov      eax, dword ptr [ebp + 8]
08465D18: add      eax, 0x14
08465D1B: mov      dword ptr [esp], eax
08465D1E: call     0x8469b18 ; _ZN8WongWork11CDeathTower6CStage8getStateEv
08465D23: cmp      eax, 1
08465D26: setle    al
08465D29: test     al, al
08465D2B: je       0x8465d97
08465D2D: lea      eax, [ebp - 0x28]
08465D30: mov      dword ptr [esp], eax
08465D33: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465D38: mov      dword ptr [esp + 8], 0x13
08465D40: mov      dword ptr [esp + 4], 0x30
08465D48: lea      eax, [ebp - 0x28]
08465D4B: mov      dword ptr [esp], eax
08465D4E: call     0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
08465D53: mov      eax, dword ptr [ebp + 8]
08465D56: mov      eax, dword ptr [eax]
08465D58: lea      edx, [ebp - 0x28]
08465D5B: mov      dword ptr [esp + 4], edx
08465D5F: mov      dword ptr [esp], eax
08465D62: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08465D67: mov      ebx, 0
08465D6C: lea      eax, [ebp - 0x28]
08465D6F: mov      dword ptr [esp], eax
08465D72: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465D77: jmp      0x846601b
08465D7C: mov      ebx, edx
08465D7E: mov      esi, eax
08465D80: lea      eax, [ebp - 0x28]
08465D83: mov      dword ptr [esp], eax
08465D86: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465D8B: mov      eax, esi
08465D8D: mov      edx, ebx
08465D8F: mov      dword ptr [esp], eax
08465D92: call     0x8ae3750 ; _Unwind_Resume
08465D97: mov      eax, dword ptr [ebp + 8]
08465D9A: add      eax, 0xc
08465D9D: mov      dword ptr [esp], eax
08465DA0: call     0x8469ab4 ; _ZN8WongWork11CDeathTower11CDungeonMgr11getEndStageEv
08465DA5: mov      edx, dword ptr [ebp + 8]
08465DA8: add      edx, 0x14
08465DAB: mov      dword ptr [esp + 4], eax
08465DAF: mov      dword ptr [esp], edx
08465DB2: call     0x84617da ; _ZN8WongWork11CDeathTower6CStage13moveNextStageEj
08465DB7: test     al, al
08465DB9: je       0x8465fb4
08465DBF: mov      eax, dword ptr [ebp + 8]
08465DC2: add      eax, 0x14
08465DC5: mov      dword ptr [esp], eax
08465DC8: call     0x8469b02 ; _ZN8WongWork11CDeathTower6CStage15getCurrentStageEv
08465DCD: mov      edx, dword ptr [ebp + 8]
08465DD0: add      edx, 0xc
08465DD3: mov      dword ptr [esp + 4], eax
08465DD7: mov      dword ptr [esp], edx
08465DDA: call     0x84605ee ; _ZN8WongWork11CDeathTower11CDungeonMgr11getStageMapEj
08465DDF: mov      dword ptr [ebp - 0x10], eax
08465DE2: cmp      dword ptr [ebp - 0x10], 0
08465DE6: jne      0x8465e14
08465DE8: mov      dword ptr [esp], 4
08465DEF: call     0x8725800 ; __cxa_allocate_exception
08465DF4: mov      edx, eax
08465DF6: mov      dword ptr [edx], 0x15
08465DFC: mov      dword ptr [esp + 8], 0
08465E04: mov      dword ptr [esp + 4], 0x8b75f40
08465E0C: mov      dword ptr [esp], eax
08465E0F: call     0x8724c50 ; __cxa_throw
08465E14: mov      eax, dword ptr [ebp + 8]
08465E17: add      eax, 0x14
08465E1A: mov      dword ptr [esp], eax
08465E1D: call     0x84614ec ; _ZN8WongWork11CDeathTower6CStage5resetEv
08465E22: mov      eax, dword ptr [ebp + 8]
08465E25: lea      ecx, [eax + 0xc]
08465E28: mov      eax, dword ptr [ebp + 8]
08465E2B: lea      edx, [eax + 0x14]
08465E2E: mov      eax, dword ptr [ebp + 8]
08465E31: mov      dword ptr [esp + 0xc], eax
08465E35: mov      eax, dword ptr [ebp - 0x10]
08465E38: mov      dword ptr [esp + 8], eax
08465E3C: mov      dword ptr [esp + 4], ecx
08465E40: mov      dword ptr [esp], edx
08465E43: call     0x8460634 ; _ZN8WongWork11CDeathTower6CStage10consistMapERKNS0_11CDungeonMgrEPK4CMapPS0_
08465E48: xor      eax, 1
08465E4B: test     al, al
08465E4D: je       0x8465f18
08465E53: mov      dword ptr [esp], 4
08465E5A: call     0x8725800 ; __cxa_allocate_exception
08465E5F: mov      edx, eax
08465E61: mov      dword ptr [edx], 1
08465E67: mov      dword ptr [esp + 8], 0
08465E6F: mov      dword ptr [esp + 4], 0x8b75f40
08465E77: mov      dword ptr [esp], eax
08465E7A: call     0x8724c50 ; __cxa_throw
08465E7F: cmp      edx, 1
08465E82: je       0x8465e8c
08465E84: mov      dword ptr [esp], eax
08465E87: call     0x8ae3750 ; _Unwind_Resume
08465E8C: mov      dword ptr [esp], eax
08465E8F: call     0x8725ce0 ; __cxa_begin_catch
08465E94: mov      eax, dword ptr [eax]
08465E96: mov      dword ptr [ebp - 0xc], eax
08465E99: lea      eax, [ebp - 0x40]
08465E9C: mov      dword ptr [esp], eax
08465E9F: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465EA4: mov      eax, dword ptr [ebp - 0xc]
08465EA7: mov      dword ptr [esp + 8], eax
08465EAB: mov      dword ptr [esp + 4], 0x30
08465EB3: lea      eax, [ebp - 0x40]
08465EB6: mov      dword ptr [esp], eax
08465EB9: call     0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
08465EBE: mov      eax, dword ptr [ebp + 8]
08465EC1: mov      eax, dword ptr [eax]
08465EC3: lea      edx, [ebp - 0x40]
08465EC6: mov      dword ptr [esp + 4], edx
08465ECA: mov      dword ptr [esp], eax
08465ECD: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08465ED2: mov      ebx, 0
08465ED7: lea      eax, [ebp - 0x40]
08465EDA: mov      dword ptr [esp], eax
08465EDD: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465EE2: jmp      0x8465f0e
08465EE4: mov      ebx, edx
08465EE6: mov      esi, eax
08465EE8: lea      eax, [ebp - 0x40]
08465EEB: mov      dword ptr [esp], eax
08465EEE: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465EF3: mov      eax, esi
08465EF5: mov      edx, ebx
08465EF7: jmp      0x8465ef9
08465EF9: mov      ebx, edx
08465EFB: mov      esi, eax
08465EFD: call     0x8725c30 ; __cxa_end_catch
08465F02: mov      eax, esi
08465F04: mov      edx, ebx
08465F06: mov      dword ptr [esp], eax
08465F09: call     0x8ae3750 ; _Unwind_Resume
08465F0E: call     0x8725c30 ; __cxa_end_catch
08465F13: jmp      0x846601b
08465F18: lea      eax, [ebp - 0x34]
08465F1B: mov      dword ptr [esp], eax
08465F1E: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465F23: mov      dword ptr [esp + 8], 0x8f
08465F2B: mov      dword ptr [esp + 4], 0
08465F33: lea      eax, [ebp - 0x34]
08465F36: mov      dword ptr [esp], eax
08465F39: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08465F3E: mov      eax, dword ptr [ebp + 8]
08465F41: lea      edx, [eax + 0x14]
08465F44: lea      eax, [ebp - 0x34]
08465F47: mov      dword ptr [esp + 4], eax
08465F4B: mov      dword ptr [esp], edx
08465F4E: call     0x8461828 ; _ZN8WongWork11CDeathTower6CStage15makeStagePacketER11PacketGuard
08465F53: mov      dword ptr [esp + 4], 1
08465F5B: lea      eax, [ebp - 0x34]
08465F5E: mov      dword ptr [esp], eax
08465F61: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08465F66: mov      eax, dword ptr [ebp + 8]
08465F69: mov      eax, dword ptr [eax]
08465F6B: lea      edx, [ebp - 0x34]
08465F6E: mov      dword ptr [esp + 4], edx
08465F72: mov      dword ptr [esp], eax
08465F75: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08465F7A: mov      eax, dword ptr [ebp + 8]
08465F7D: mov      dword ptr [esp], eax
08465F80: call     0x84670f8 ; _ZN8WongWork11CDeathTower17_checkMemberReadyEv
08465F85: jmp      0x8465fa2
08465F87: mov      ebx, edx
08465F89: mov      esi, eax
08465F8B: lea      eax, [ebp - 0x34]
08465F8E: mov      dword ptr [esp], eax
08465F91: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465F96: mov      eax, esi
08465F98: mov      edx, ebx
08465F9A: mov      dword ptr [esp], eax
08465F9D: call     0x8ae3750 ; _Unwind_Resume
08465FA2: lea      eax, [ebp - 0x34]
08465FA5: mov      dword ptr [esp], eax
08465FA8: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465FAD: mov      ebx, 1
08465FB2: jmp      0x846601b
08465FB4: lea      eax, [ebp - 0x4c]
08465FB7: mov      dword ptr [esp], eax
08465FBA: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08465FBF: mov      dword ptr [esp + 8], 0x16
08465FC7: mov      dword ptr [esp + 4], 0x30
08465FCF: lea      eax, [ebp - 0x4c]
08465FD2: mov      dword ptr [esp], eax
08465FD5: call     0x845fe4c ; _ZN8WongWork11CDeathTower14CPacketHandler15makeCommonErrorE14ENUM_CMDPACKET10ENUM_ERROR
08465FDA: mov      eax, dword ptr [ebp + 8]
08465FDD: mov      eax, dword ptr [eax]
08465FDF: lea      edx, [ebp - 0x4c]
08465FE2: mov      dword ptr [esp + 4], edx
08465FE6: mov      dword ptr [esp], eax
08465FE9: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
08465FEE: mov      ebx, 0
08465FF3: lea      eax, [ebp - 0x4c]
08465FF6: mov      dword ptr [esp], eax
08465FF9: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08465FFE: jmp      0x846601b
08466000: mov      ebx, edx
08466002: mov      esi, eax
08466004: lea      eax, [ebp - 0x4c]
08466007: mov      dword ptr [esp], eax
0846600A: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
0846600F: mov      eax, esi
08466011: mov      edx, ebx
08466013: mov      dword ptr [esp], eax
08466016: call     0x8ae3750 ; _Unwind_Resume
0846601B: mov      eax, ebx
0846601D: add      esp, 0x60
08466020: pop      ebx
08466021: pop      esi
08466022: pop      ebp
08466023: ret
```

## 0x08466150 _ZN8WongWork11CDeathTower18handleStageCommandEP5CUserh

```asm
08466150: push     ebp
08466151: mov      ebp, esp
08466153: sub      esp, 0x28
08466156: mov      eax, dword ptr [ebp + 0x10]
08466159: mov      byte ptr [ebp - 0xc], al
0846615C: mov      eax, dword ptr [ebp + 8]
0846615F: mov      eax, dword ptr [eax]
08466161: mov      dword ptr [esp], eax
08466164: call     0x8145780 ; _ZN6CParty10getManagerEv
08466169: cmp      eax, dword ptr [ebp + 0xc]
0846616C: setne    al
0846616F: test     al, al
08466171: je       0x846617d
08466173: mov      eax, 0
08466178: jmp      0x8466205
0846617D: movzx    eax, byte ptr [ebp - 0xc]
08466181: cmp      eax, 1
08466184: je       0x846618d
08466186: cmp      eax, 2
08466189: je       0x84661d4
0846618B: jmp      0x84661f8
0846618D: mov      eax, dword ptr [ebp + 8]
08466190: add      eax, 0x14
08466193: mov      dword ptr [esp + 4], 1
0846619B: mov      dword ptr [esp], eax
0846619E: call     0x8461c5a ; _ZN8WongWork11CDeathTower6CStage8setStateENS0_16ENUM_STAGE_STATEE
084661A3: xor      eax, 1
084661A6: test     al, al
084661A8: je       0x84661b1
084661AA: mov      eax, 1
084661AF: jmp      0x8466205
084661B1: mov      eax, dword ptr [ebp + 8]
084661B4: mov      dword ptr [esp], eax
084661B7: call     0x8467188 ; _ZN8WongWork11CDeathTower15_checkMemberDieEv
084661BC: test     al, al
084661BE: je       0x84661c7
084661C0: mov      eax, 1
084661C5: jmp      0x8466205
084661C7: mov      eax, dword ptr [ebp + 8]
084661CA: mov      dword ptr [esp], eax
084661CD: call     0x8467cac ; _ZN8WongWork11CDeathTower13_onStartStageEv
084661D2: jmp      0x8466200
084661D4: mov      eax, dword ptr [ebp + 8]
084661D7: add      eax, 0x14
084661DA: mov      dword ptr [esp + 4], 2
084661E2: mov      dword ptr [esp], eax
084661E5: call     0x8461c5a ; _ZN8WongWork11CDeathTower6CStage8setStateENS0_16ENUM_STAGE_STATEE
084661EA: xor      eax, 1
084661ED: test     al, al
084661EF: je       0x84661ff
084661F1: mov      eax, 1
084661F6: jmp      0x8466205
084661F8: mov      eax, 0
084661FD: jmp      0x8466205
084661FF: nop
08466200: mov      eax, 1
08466205: leave
08466206: ret
```

## 0x08467188 _ZN8WongWork11CDeathTower15_checkMemberDieEv

```asm
08467188: push     ebp
08467189: mov      ebp, esp
0846718B: sub      esp, 0x28
0846718E: mov      dword ptr [ebp - 0xc], 0
08467195: mov      dword ptr [ebp - 0xc], 0
0846719C: jmp      0x84671e6
0846719E: mov      edx, dword ptr [ebp - 0xc]
084671A1: mov      eax, dword ptr [ebp + 8]
084671A4: mov      eax, dword ptr [eax]
084671A6: mov      dword ptr [esp + 4], edx
084671AA: mov      dword ptr [esp], eax
084671AD: call     0x8145868 ; _ZN6CParty14checkValidUserEi
084671B2: test     al, al
084671B4: je       0x84671d9
084671B6: mov      eax, dword ptr [ebp - 0xc]
084671B9: mov      edx, dword ptr [ebp + 8]
084671BC: add      edx, 0x110
084671C2: mov      dword ptr [esp + 4], eax
084671C6: mov      dword ptr [esp], edx
084671C9: call     0x8469b76 ; _ZN8WongWork11CDeathTower9CPlayData13isMemberAliveEi
084671CE: test     al, al
084671D0: je       0x84671d9
084671D2: mov      eax, 1
084671D7: jmp      0x84671de
084671D9: mov      eax, 0
084671DE: test     al, al
084671E0: jne      0x84671f3
084671E2: add      dword ptr [ebp - 0xc], 1
084671E6: cmp      dword ptr [ebp - 0xc], 3
084671EA: setbe    al
084671ED: test     al, al
084671EF: jne      0x846719e
084671F1: jmp      0x84671f4
084671F3: nop
084671F4: cmp      dword ptr [ebp - 0xc], 4
084671F8: jne      0x8467201
084671FA: mov      eax, 1
084671FF: jmp      0x8467206
08467201: mov      eax, 0
08467206: leave
08467207: ret
```

## 0x08467208 _ZN8WongWork11CDeathTower23_beginTowerClearProcessEb

```asm
08467208: push     ebp
08467209: mov      ebp, esp
0846720B: push     edi
0846720C: push     esi
0846720D: push     ebx
0846720E: sub      esp, 0xcc
08467214: mov      eax, dword ptr [ebp + 0xc]
08467217: mov      byte ptr [ebp - 0xac], al
0846721D: mov      eax, dword ptr [ebp + 8]
08467220: mov      dword ptr [esp], eax
08467223: call     0x8469c5c ; _ZN8WongWork11CDeathTower13getTowerStateEv
08467228: cmp      eax, 2
0846722B: setg     al
0846722E: test     al, al
08467230: jne      0x8467b13
08467236: mov      dword ptr [ebp - 0x40], 0
0846723D: mov      eax, dword ptr [ebp + 8]
08467240: add      eax, 0x110
08467245: mov      dword ptr [esp], eax
08467248: call     0x8469b26 ; _ZN8WongWork11CDeathTower9CPlayData17getStartMemberCntEv
0846724D: mov      dword ptr [ebp - 0x3c], eax
08467250: mov      eax, dword ptr [ebp - 0x3c]
08467253: mov      dword ptr [esp], eax
08467256: call     0x82a774c ; _ZN8WongWork18CDeathTowerRanking12getRankTableEj
0846725B: mov      dword ptr [ebp - 0x38], eax
0846725E: movzx    eax, byte ptr [ebp - 0xac]
08467265: mov      dword ptr [esp + 4], eax
08467269: mov      eax, dword ptr [ebp + 8]
0846726C: mov      dword ptr [esp], eax
0846726F: call     0x8467e60 ; _ZN8WongWork11CDeathTower8_onClearEb
08467274: mov      eax, dword ptr [ebp + 8]
08467277: mov      eax, dword ptr [eax]
08467279: mov      eax, dword ptr [eax + 0xcd8]
0846727F: cmp      eax, 1
08467282: jne      0x846737a
08467288: mov      dword ptr [esp + 4], 5
08467290: mov      eax, dword ptr [ebp + 8]
08467293: mov      dword ptr [esp], eax
08467296: call     0x8469c34 ; _ZN8WongWork11CDeathTower13setTowerStateENS0_16ENUM_TOWER_STATEE
0846729B: mov      eax, dword ptr [ebp + 8]
0846729E: mov      eax, dword ptr [eax]
084672A0: mov      dword ptr [esp + 4], 2
084672A8: mov      dword ptr [esp], eax
084672AB: call     0x814578c ; _ZN6CParty12SetEPLPStateEc
084672B0: mov      dword ptr [esp + 4], 0x4d
084672B8: mov      eax, dword ptr [ebp + 8]
084672BB: mov      dword ptr [esp], eax
084672BE: call     0x830edec ; _ZN8WongWork11CDeathTower12_genTimerKeyE13TIMER_MESSAGE
084672C3: mov      ebx, eax
084672C5: mov      eax, dword ptr [ebp + 8]
084672C8: mov      esi, dword ptr [eax + 4]
084672CB: call     0x80f647c ; _Z12G_TimerQueuev
084672D0: mov      dword ptr [esp + 0x18], 0x4d
084672D8: mov      dword ptr [esp + 0x14], ebx
084672DC: mov      dword ptr [esp + 0x10], 0x3c
084672E4: mov      dword ptr [esp + 0xc], 0x4d
084672EC: mov      dword ptr [esp + 8], esi
084672F0: mov      dword ptr [esp + 4], 6
084672F8: mov      dword ptr [esp], eax
084672FB: call     0x8630e16 ; _ZN10TimerQueue11InsertTimerEN10TimerEntry8OBJ_TYPEEi13TIMER_MESSAGEiij
08467300: lea      eax, [ebp - 0x68]
08467303: mov      dword ptr [esp], eax
08467306: call     0x858dd4c ; _ZN11PacketGuardC2Ev
0846730B: mov      dword ptr [esp + 8], 0xfe
08467313: mov      dword ptr [esp + 4], 0
0846731B: lea      eax, [ebp - 0x68]
0846731E: mov      dword ptr [esp], eax
08467321: call     0x80cb8fc ; _ZN18InterfacePacketBuf10put_headerEii
08467326: mov      dword ptr [esp + 4], 1
0846732E: lea      eax, [ebp - 0x68]
08467331: mov      dword ptr [esp], eax
08467334: call     0x80cb958 ; _ZN18InterfacePacketBuf8finalizeEb
08467339: mov      eax, dword ptr [ebp + 8]
0846733C: mov      eax, dword ptr [eax]
0846733E: lea      edx, [ebp - 0x68]
08467341: mov      dword ptr [esp + 4], edx
08467345: mov      dword ptr [esp], eax
08467348: call     0x859d14e ; _ZN6CParty13send_to_partyER11PacketGuard
0846734D: jmp      0x846736a
0846734F: mov      ebx, edx
08467351: mov      esi, eax
08467353: lea      eax, [ebp - 0x68]
08467356: mov      dword ptr [esp], eax
08467359: call     0x858de80 ; _ZN11PacketGuardD1Ev
0846735E: mov      eax, esi
08467360: mov      edx, ebx
08467362: mov      dword ptr [esp], eax
08467365: call     0x8ae3750 ; _Unwind_Resume
0846736A: lea      eax, [ebp - 0x68]
0846736D: mov      dword ptr [esp], eax
08467370: call     0x858de80 ; _ZN11PacketGuardD1Ev
08467375: jmp      0x8467b14
0846737A: mov      dword ptr [esp + 4], 3
08467382: mov      eax, dword ptr [ebp + 8]
08467385: mov      dword ptr [esp], eax
08467388: call     0x8469c34 ; _ZN8WongWork11CDeathTower13setTowerStateENS0_16ENUM_TOWER_STATEE
0846738D: mov      dword ptr [esp + 4], 0x4b
08467395: mov      eax, dword ptr [ebp + 8]
08467398: mov      dword ptr [esp], eax
0846739B: call     0x830edec ; _ZN8WongWork11CDeathTower12_genTimerKeyE13TIMER_MESSAGE
084673A0: mov      ebx, eax
084673A2: mov      eax, dword ptr [ebp + 8]
084673A5: mov      esi, dword ptr [eax + 4]
084673A8: call     0x80f647c ; _Z12G_TimerQueuev
084673AD: mov      dword ptr [esp + 0x18], 0x4b
084673B5: mov      dword ptr [esp + 0x14], ebx
084673B9: mov      dword ptr [esp + 0x10], 1
084673C1: mov      dword ptr [esp + 0xc], 0x4b
084673C9: mov      dword ptr [esp + 8], esi
084673CD: mov      dword ptr [esp + 4], 6
084673D5: mov      dword ptr [esp], eax
084673D8: call     0x8630e16 ; _ZN10TimerQueue11InsertTimerEN10TimerEntry8OBJ_TYPEEi13TIMER_MESSAGEiij
084673DD: mov      eax, dword ptr [ebp + 8]
084673E0: add      eax, 0x14
084673E3: mov      dword ptr [esp], eax
084673E6: call     0x8469b02 ; _ZN8WongWork11CDeathTower6CStage15getCurrentStageEv
084673EB: mov      dword ptr [ebp - 0x5c], eax
084673EE: cmp      byte ptr [ebp - 0xac], 0
084673F5: je       0x846740c
084673F7: mov      eax, dword ptr [ebp + 8]
084673FA: add      eax, 0x110
084673FF: mov      dword ptr [esp], eax
08467402: call     0x8469bd4 ; _ZN8WongWork11CDeathTower9CPlayData11getPlayTimeEv
08467407: mov      dword ptr [ebp - 0x40], eax
0846740A: jmp      0x8467446
0846740C: mov      eax, dword ptr [ebp + 8]
0846740F: add      eax, 0x110
08467414: mov      dword ptr [esp], eax
08467417: call     0x8469be0 ; _ZN8WongWork11CDeathTower9CPlayData15getLastPlayTimeEv
0846741C: mov      dword ptr [ebp - 0x40], eax
0846741F: mov      eax, dword ptr [ebp - 0x5c]
08467422: sub      eax, 1
08467425: mov      dword ptr [ebp - 0x58], eax
08467428: mov      dword ptr [ebp - 0x54], 1
0846742F: lea      eax, [ebp - 0x58]
08467432: mov      dword ptr [esp + 4], eax
08467436: lea      eax, [ebp - 0x54]
08467439: mov      dword ptr [esp], eax
0846743C: call     0x808e218 ; _ZSt3maxIiERKT_S2_S2_
08467441: mov      eax, dword ptr [eax]
08467443: mov      dword ptr [ebp - 0x5c], eax
08467446: mov      dword ptr [ebp - 0x34], 0
0846744D: jmp      0x8467b02
08467452: mov      dword ptr [ebp - 0x30], 0
08467459: mov      eax, dword ptr [ebp + 8]
0846745C: mov      eax, dword ptr [eax]
0846745E: mov      edx, dword ptr [ebp - 0x34]
08467461: mov      dword ptr [esp + 4], edx
08467465: mov      dword ptr [esp], eax
08467468: call     0x8145764 ; _ZN6CParty8get_userEi
0846746D: mov      dword ptr [ebp - 0x30], eax
08467470: cmp      dword ptr [ebp - 0x30], 0
08467474: je       0x8467491
08467476: mov      eax, dword ptr [ebp + 8]
08467479: mov      eax, dword ptr [eax]
0846747B: mov      edx, dword ptr [ebp - 0x34]
0846747E: mov      dword ptr [esp + 4], edx
08467482: mov      dword ptr [esp], eax
08467485: call     0x8145868 ; _ZN6CParty14checkValidUserEi
0846748A: xor      eax, 1
0846748D: test     al, al
0846748F: je       0x8467498
08467491: mov      eax, 1
08467496: jmp      0x846749d
08467498: mov      eax, 0
0846749D: test     al, al
0846749F: jne      0x8467afd
084674A5: mov      eax, dword ptr [ebp - 0x30]
084674A8: mov      dword ptr [esp + 4], 0
084674B0: mov      dword ptr [esp], eax
084674B3: call     0x84699e6 ; _ZN15CUserCharacInfo26set_charac_party_bonus_expEi
084674B8: mov      eax, dword ptr [ebp - 0x30]
084674BB: mov      dword ptr [esp + 4], 0
084674C3: mov      dword ptr [esp], eax
084674C6: call     0x8469a20 ; _ZN15CUserCharacInfo27set_charac_member_bonus_expEi
084674CB: mov      eax, dword ptr [ebp - 0x30]
084674CE: mov      dword ptr [esp + 4], 0
084674D6: mov      dword ptr [esp], eax
084674D9: call     0x8469a02 ; _ZN15CUserCharacInfo32set_charac_fatigue_buf_bonus_expEi
084674DE: mov      eax, dword ptr [ebp - 0x30]
084674E1: mov      dword ptr [esp + 4], 0
084674E9: mov      dword ptr [esp], eax
084674EC: call     0x8469a3e ; _ZN15CUserCharacInfo30set_charac_seria_buf_bonus_expEi
084674F1: mov      dword ptr [ebp - 0x2c], 0
084674F8: mov      esi, dword ptr [ebp - 0x5c]
084674FB: mov      eax, dword ptr [ebp - 0x30]
084674FE: mov      dword ptr [esp], eax
08467501: call     0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
08467506: mov      ebx, eax
08467508: mov      eax, dword ptr [ebp + 8]
0846750B: add      eax, 0xc
0846750E: mov      dword ptr [esp], eax
08467511: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467516: mov      dword ptr [esp], eax
08467519: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
0846751E: mov      edx, dword ptr [ebp - 0x40]
08467521: mov      dword ptr [esp + 0x14], edx
08467525: mov      dword ptr [esp + 0x10], esi
08467529: mov      dword ptr [esp + 0xc], ebx
0846752D: mov      edx, dword ptr [ebp - 0x3c]
08467530: mov      dword ptr [esp + 8], edx
08467534: mov      dword ptr [esp + 4], eax
08467538: mov      eax, dword ptr [ebp + 8]
0846753B: mov      dword ptr [esp], eax
0846753E: call     0x846805e ; _ZN8WongWork11CDeathTower19_checkRenewMyRecordEjjjjj
08467543: mov      dword ptr [ebp - 0x28], eax
08467546: cmp      byte ptr [ebp - 0xac], 0
0846754D: je       0x84675bf
0846754F: cmp      dword ptr [ebp - 0x28], 1
08467553: jne      0x846755c
08467555: mov      eax, 1
0846755A: jmp      0x8467561
0846755C: mov      eax, 0
08467561: mov      dword ptr [ebp - 0x2c], eax
08467564: mov      ebx, dword ptr [ebp - 0x5c]
08467567: mov      eax, dword ptr [ebp + 8]
0846756A: add      eax, 0xc
0846756D: mov      dword ptr [esp], eax
08467570: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467575: mov      dword ptr [esp], eax
08467578: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
0846757D: mov      edx, dword ptr [ebp - 0x40]
08467580: mov      dword ptr [esp + 0x10], edx
08467584: mov      dword ptr [esp + 0xc], ebx
08467588: mov      edx, dword ptr [ebp - 0x3c]
0846758B: mov      dword ptr [esp + 8], edx
0846758F: mov      dword ptr [esp + 4], eax
08467593: mov      eax, dword ptr [ebp + 8]
08467596: mov      dword ptr [esp], eax
08467599: call     0x846801e ; _ZN8WongWork11CDeathTower20_checkRenewTopRecordEjjjj
0846759E: test     al, al
084675A0: je       0x84675b2
084675A2: cmp      byte ptr [ebp - 0xac], 0
084675A9: je       0x84675b2
084675AB: mov      eax, 1
084675B0: jmp      0x84675b7
084675B2: mov      eax, 0
084675B7: test     al, al
084675B9: je       0x84675bf
084675BB: add      dword ptr [ebp - 0x2c], 2
084675BF: mov      ebx, dword ptr [ebp - 0x5c]
084675C2: mov      eax, dword ptr [ebp + 8]
084675C5: add      eax, 0xc
084675C8: mov      dword ptr [esp], eax
084675CB: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
084675D0: mov      dword ptr [esp], eax
084675D3: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
084675D8: mov      dword ptr [esp], eax
084675DB: call     0x8469d69 ; _ZN8WongWork18CDeathTowerRanking23makeDungeonIdx2TowerIdxEj
084675E0: mov      edx, dword ptr [ebp - 0x40]
084675E3: mov      dword ptr [esp + 0xc], edx
084675E7: mov      dword ptr [esp + 8], ebx
084675EB: mov      dword ptr [esp + 4], eax
084675EF: mov      eax, dword ptr [ebp - 0x38]
084675F2: mov      dword ptr [esp], eax
084675F5: call     0x8468de0 ; _ZN8WongWork18CDeathTowerRanking14getVirtualRankEjjj
084675FA: mov      dword ptr [ebp - 0x24], eax
084675FD: mov      eax, dword ptr [ebp + 8]
08467600: add      eax, 0xc
08467603: mov      dword ptr [esp], eax
08467606: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846760B: mov      dword ptr [esp], eax
0846760E: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08467613: cmp      eax, 0x2af8
08467618: sete     al
0846761B: test     al, al
0846761D: je       0x8467625
0846761F: mov      byte ptr [ebp - 0x1d], 0
08467623: jmp      0x8467629
08467625: mov      byte ptr [ebp - 0x1d], 1
08467629: mov      eax, dword ptr [ebp + 8]
0846762C: add      eax, 0x110
08467631: mov      dword ptr [esp], eax
08467634: call     0x8469b26 ; _ZN8WongWork11CDeathTower9CPlayData17getStartMemberCntEv
08467639: mov      esi, eax
0846763B: mov      eax, dword ptr [ebp - 0x5c]
0846763E: mov      dword ptr [ebp - 0xb4], eax
08467644: mov      eax, dword ptr [ebp + 8]
08467647: add      eax, 0x110
0846764C: mov      dword ptr [esp], eax
0846764F: call     0x8469b34 ; _ZN8WongWork11CDeathTower9CPlayData18getStartMemberInfoEv
08467654: mov      ebx, eax
08467656: movsx    edi, byte ptr [ebp - 0x1d]
0846765A: call     0x860f451 ; _Z38GetInstanceDeathTowerPlaydataStatisticv
0846765F: mov      dword ptr [esp + 0x14], esi
08467663: mov      edx, dword ptr [ebp - 0xb4]
08467669: mov      dword ptr [esp + 0x10], edx
0846766D: mov      dword ptr [esp + 0xc], ebx
08467671: mov      dword ptr [esp + 8], edi
08467675: mov      edx, dword ptr [ebp - 0x34]
08467678: mov      dword ptr [esp + 4], edx
0846767C: mov      dword ptr [esp], eax
0846767F: call     0x860f45c ; _ZN28CDeathTowerPlayDataStatistic22SendDeathTowerPlayInfoEicPK30stDeathTowerRecordMemberInfo_tjj
08467684: mov      edi, dword ptr [ebp - 0x5c]
08467687: mov      eax, dword ptr [ebp - 0x30]
0846768A: mov      dword ptr [esp], eax
0846768D: call     0x80da2b8 ; _ZNK15CUserCharacInfo16get_charac_levelEv
08467692: movsx    esi, al
08467695: movsx    ebx, byte ptr [ebp - 0x1d]
08467699: call     0x860f337 ; _Z35GetInstanceDeathTowerValueStatisticv
0846769E: mov      dword ptr [esp + 0x10], edi
084676A2: mov      dword ptr [esp + 0xc], esi
084676A6: mov      dword ptr [esp + 8], ebx
084676AA: mov      dword ptr [esp + 4], 1
084676B2: mov      dword ptr [esp], eax
084676B5: call     0x860f342 ; _ZN25CDeathTowerValueStatistic28SendDeathTowerValueStatisticE32DEATHTOWER_VALUE_STATISTIC_FIELDccj
084676BA: mov      eax, dword ptr [ebp - 0x30]
084676BD: mov      dword ptr [esp], eax
084676C0: call     0x80da2b8 ; _ZNK15CUserCharacInfo16get_charac_levelEv
084676C5: movsx    esi, al
084676C8: movsx    ebx, byte ptr [ebp - 0x1d]
084676CC: call     0x860f337 ; _Z35GetInstanceDeathTowerValueStatisticv
084676D1: mov      dword ptr [esp + 0x10], 1
084676D9: mov      dword ptr [esp + 0xc], esi
084676DD: mov      dword ptr [esp + 8], ebx
084676E1: mov      dword ptr [esp + 4], 0
084676E9: mov      dword ptr [esp], eax
084676EC: call     0x860f342 ; _ZN25CDeathTowerValueStatistic28SendDeathTowerValueStatisticE32DEATHTOWER_VALUE_STATISTIC_FIELDccj
084676F1: cmp      dword ptr [ebp - 0x28], 0
084676F5: je       0x846786e
084676FB: mov      edi, dword ptr [ebp - 0x5c]
084676FE: mov      eax, dword ptr [ebp + 8]
08467701: add      eax, 0x110
08467706: mov      dword ptr [esp], eax
08467709: call     0x8469b34 ; _ZN8WongWork11CDeathTower9CPlayData18getStartMemberInfoEv
0846770E: mov      esi, eax
08467710: mov      eax, dword ptr [ebp - 0x30]
08467713: mov      dword ptr [esp], eax
08467716: call     0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
0846771B: mov      ebx, eax
0846771D: mov      eax, dword ptr [ebp + 8]
08467720: add      eax, 0xc
08467723: mov      dword ptr [esp], eax
08467726: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846772B: mov      dword ptr [esp], eax
0846772E: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08467733: mov      dword ptr [esp], eax
08467736: call     0x8469d69 ; _ZN8WongWork18CDeathTowerRanking23makeDungeonIdx2TowerIdxEj
0846773B: mov      edx, dword ptr [ebp - 0x40]
0846773E: mov      dword ptr [esp + 0x14], edx
08467742: mov      dword ptr [esp + 0x10], edi
08467746: mov      dword ptr [esp + 0xc], esi
0846774A: mov      edx, dword ptr [ebp - 0x3c]
0846774D: mov      dword ptr [esp + 8], edx
08467751: mov      dword ptr [esp + 4], ebx
08467755: mov      dword ptr [esp], eax
08467758: call     0x842a35e ; _ZN29DB_UpdateDeathTowerBestRecord11makeRequestEjjjPK30stDeathTowerRecordMemberInfo_tjj
0846775D: cmp      dword ptr [ebp - 0x24], 0
08467761: jle      0x846780c
08467767: mov      edi, dword ptr [ebp - 0x24]
0846776A: mov      eax, dword ptr [ebp + 8]
0846776D: add      eax, 0xc
08467770: mov      dword ptr [esp], eax
08467773: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467778: mov      dword ptr [esp], eax
0846777B: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08467780: mov      dword ptr [esp], eax
08467783: call     0x8469d69 ; _ZN8WongWork18CDeathTowerRanking23makeDungeonIdx2TowerIdxEj
08467788: mov      esi, eax
0846778A: mov      eax, dword ptr [ebp - 0x30]
0846778D: mov      dword ptr [esp], eax
08467790: call     0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
08467795: mov      ebx, eax
08467797: mov      eax, dword ptr [0x940be28]
0846779C: mov      dword ptr [esp + 4], 0
084677A4: mov      dword ptr [esp], eax
084677A7: call     0x811208a ; _ZN15CServerProxyMgrI19CMonitorServerProxyE14GetServerProxyE17ENUM_SERVER_GROUP
084677AC: mov      dword ptr [esp + 0x10], edi
084677B0: mov      edx, dword ptr [ebp - 0x3c]
084677B3: mov      dword ptr [esp + 0xc], edx
084677B7: mov      dword ptr [esp + 8], esi
084677BB: mov      dword ptr [esp + 4], ebx
084677BF: mov      dword ptr [esp], eax
084677C2: call     0x8471240 ; _ZN19CMonitorServerProxy15updateTowerRankEjjjj
084677C7: mov      esi, dword ptr [ebp - 0x24]
084677CA: mov      eax, dword ptr [ebp - 0x30]
084677CD: mov      dword ptr [esp], eax
084677D0: call     0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
084677D5: mov      ebx, eax
084677D7: mov      eax, dword ptr [ebp + 8]
084677DA: add      eax, 0xc
084677DD: mov      dword ptr [esp], eax
084677E0: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
084677E5: mov      dword ptr [esp], eax
084677E8: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
084677ED: mov      dword ptr [esp], eax
084677F0: call     0x8469d69 ; _ZN8WongWork18CDeathTowerRanking23makeDungeonIdx2TowerIdxEj
084677F5: mov      dword ptr [esp + 0xc], esi
084677F9: mov      dword ptr [esp + 8], ebx
084677FD: mov      dword ptr [esp + 4], eax
08467801: mov      eax, dword ptr [ebp - 0x38]
08467804: mov      dword ptr [esp], eax
08467807: call     0x84684ce ; _ZN8WongWork18CDeathTowerRanking13updateRankingEjjj
0846780C: mov      edi, dword ptr [ebp - 0x5c]
0846780F: mov      eax, dword ptr [ebp + 8]
08467812: add      eax, 0x110
08467817: mov      dword ptr [esp], eax
0846781A: call     0x8469b34 ; _ZN8WongWork11CDeathTower9CPlayData18getStartMemberInfoEv
0846781F: mov      esi, eax
08467821: mov      eax, dword ptr [ebp - 0x30]
08467824: mov      dword ptr [esp], eax
08467827: call     0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
0846782C: mov      ebx, eax
0846782E: mov      eax, dword ptr [ebp + 8]
08467831: add      eax, 0xc
08467834: mov      dword ptr [esp], eax
08467837: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
0846783C: mov      dword ptr [esp], eax
0846783F: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08467844: mov      dword ptr [esp], eax
08467847: call     0x8469d69 ; _ZN8WongWork18CDeathTowerRanking23makeDungeonIdx2TowerIdxEj
0846784C: mov      edx, dword ptr [ebp - 0x40]
0846784F: mov      dword ptr [esp + 0x14], edx
08467853: mov      dword ptr [esp + 0x10], edi
08467857: mov      dword ptr [esp + 0xc], esi
0846785B: mov      dword ptr [esp + 8], ebx
0846785F: mov      dword ptr [esp + 4], eax
08467863: mov      eax, dword ptr [ebp - 0x38]
08467866: mov      dword ptr [esp], eax
08467869: call     0x8468914 ; _ZN8WongWork18CDeathTowerRanking16registBestRecordEjjPK30stDeathTowerRecordMemberInfo_tjj
0846786E: lea      eax, [ebp - 0x74]
08467871: mov      dword ptr [esp], eax
08467874: call     0x8469a8c ; _ZN8WongWork11CDeathTower14CPacketHandlerC1Ev
08467879: mov      eax, dword ptr [ebp - 0x24]
0846787C: mov      dword ptr [ebp - 0xb0], eax
08467882: mov      eax, dword ptr [ebp + 8]
08467885: add      eax, 0xc
08467888: mov      dword ptr [esp], eax
0846788B: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467890: mov      dword ptr [esp], eax
08467893: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08467898: mov      edi, eax
0846789A: cmp      dword ptr [ebp - 0x28], 1
0846789E: sete     al
084678A1: movzx    esi, al
084678A4: mov      ebx, dword ptr [ebp - 0x5c]
084678A7: mov      eax, dword ptr [ebp - 0x30]
084678AA: mov      dword ptr [esp], eax
084678AD: call     0x80cbc4e ; _ZNK15CUserCharacInfo14getCurCharacNoEv
084678B2: mov      edx, dword ptr [ebp - 0xb0]
084678B8: mov      dword ptr [esp + 0x1c], edx
084678BC: mov      dword ptr [esp + 0x18], edi
084678C0: mov      dword ptr [esp + 0x14], esi
084678C4: mov      edx, dword ptr [ebp - 0x40]
084678C7: mov      dword ptr [esp + 0x10], edx
084678CB: mov      dword ptr [esp + 0xc], ebx
084678CF: mov      dword ptr [esp + 8], eax
084678D3: mov      eax, dword ptr [ebp - 0x3c]
084678D6: mov      dword ptr [esp + 4], eax
084678DA: lea      eax, [ebp - 0x74]
084678DD: mov      dword ptr [esp], eax
084678E0: call     0x84601da ; _ZN8WongWork11CDeathTower14CPacketHandler19makeDisplayRangkingEjjjjbjj
084678E5: lea      eax, [ebp - 0x74]
084678E8: mov      dword ptr [esp + 4], eax
084678EC: mov      eax, dword ptr [ebp - 0x30]
084678EF: mov      dword ptr [esp], eax
084678F2: call     0x86485ba ; _ZN5CUser4SendER11PacketGuard
084678F7: mov      dword ptr [ebp - 0x1c], 0
084678FE: mov      dword ptr [ebp - 0x78], 0
08467905: mov      eax, dword ptr [ebp + 8]
08467908: add      eax, 0xc
0846790B: mov      dword ptr [esp], eax
0846790E: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467913: mov      dword ptr [esp], eax
08467916: call     0x822b4ce ; _ZNK8CDungeon27limitOfStackableItemInTowerEv
0846791B: test     al, al
0846791D: je       0x8467936
0846791F: call     0x80cc19b ; _Z14G_CDataManagerv
08467924: mov      edx, dword ptr [ebp - 0x5c]
08467927: add      edx, 0x152c
0846792D: mov      eax, dword ptr [eax + edx*4 + 0xc]
08467931: mov      dword ptr [ebp - 0x1c], eax
08467934: jmp      0x846794b
08467936: call     0x80cc19b ; _Z14G_CDataManagerv
0846793B: mov      edx, dword ptr [ebp - 0x5c]
0846793E: add      edx, 0x16bc
08467944: mov      eax, dword ptr [eax + edx*4 + 0xc]
08467948: mov      dword ptr [ebp - 0x1c], eax
0846794B: mov      eax, dword ptr [ebp - 0x2c]
0846794E: mov      edx, dword ptr [ebp - 0x1c]
08467951: lea      eax, [edx + eax]
08467954: mov      dword ptr [ebp - 0x50], eax
08467957: mov      eax, dword ptr [ebp + 8]
0846795A: add      eax, 0xc
0846795D: mov      dword ptr [esp], eax
08467960: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467965: mov      dword ptr [esp], eax
08467968: call     0x81455ca ; _ZNK8CDungeon29getTowerMaxClearRewardItemNumEv
0846796D: mov      dword ptr [ebp - 0x4c], eax
08467970: lea      eax, [ebp - 0x50]
08467973: mov      dword ptr [esp + 4], eax
08467977: lea      eax, [ebp - 0x4c]
0846797A: mov      dword ptr [esp], eax
0846797D: call     0x8236091 ; _ZSt3minIjERKT_S2_S2_
08467982: mov      eax, dword ptr [eax]
08467984: mov      dword ptr [ebp - 0x78], eax
08467987: mov      dword ptr [ebp - 0x48], 0xa
0846798E: lea      eax, [ebp - 0x78]
08467991: mov      dword ptr [esp + 4], eax
08467995: lea      eax, [ebp - 0x48]
08467998: mov      dword ptr [esp], eax
0846799B: call     0x80df90e ; _ZSt3minIiERKT_S2_S2_
084679A0: mov      eax, dword ptr [eax]
084679A2: mov      dword ptr [ebp - 0x78], eax
084679A5: mov      eax, dword ptr [ebp + 8]
084679A8: mov      esi, dword ptr [eax]
084679AA: mov      dword ptr [ebp - 0x44], 0xa
084679B1: lea      eax, [ebp - 0x5c]
084679B4: mov      dword ptr [esp + 4], eax
084679B8: lea      eax, [ebp - 0x44]
084679BB: mov      dword ptr [esp], eax
084679BE: call     0x8236091 ; _ZSt3minIjERKT_S2_S2_
084679C3: mov      ebx, dword ptr [eax]
084679C5: mov      eax, dword ptr [ebp - 0x30]
084679C8: mov      dword ptr [esp], eax
084679CB: call     0x80da2b8 ; _ZNK15CUserCharacInfo16get_charac_levelEv
084679D0: mov      edx, eax
084679D2: mov      eax, dword ptr [ebp + 8]
084679D5: lea      edi, [eax + 0xc]
084679D8: mov      eax, dword ptr [ebp - 0x78]
084679DB: mov      ecx, dword ptr [ebp + 8]
084679DE: add      ecx, 0x110
084679E4: mov      dword ptr [esp + 0x18], esi
084679E8: mov      dword ptr [esp + 0x14], ebx
084679EC: mov      dword ptr [esp + 0x10], edx
084679F0: mov      dword ptr [esp + 0xc], edi
084679F4: mov      dword ptr [esp + 8], eax
084679F8: mov      eax, dword ptr [ebp - 0x34]
084679FB: mov      dword ptr [esp + 4], eax
084679FF: mov      dword ptr [esp], ecx
08467A02: call     0x8461fc0 ; _ZN8WongWork11CDeathTower9CPlayData18generateRewardItemEiiRKNS0_11CDungeonMgrEjjP6CParty
08467A07: mov      dword ptr [ebp - 0x7c], 0
08467A0E: mov      eax, dword ptr [ebp - 0x30]
08467A11: mov      dword ptr [esp], eax
08467A14: call     0x80da2b8 ; _ZNK15CUserCharacInfo16get_charac_levelEv
08467A19: mov      ebx, eax
08467A1B: call     0x80cc19b ; _Z14G_CDataManagerv
08467A20: lea      edx, [ebp - 0x7c]
08467A23: mov      dword ptr [esp + 8], edx
08467A27: mov      dword ptr [esp + 4], ebx
08467A2B: mov      dword ptr [esp], eax
08467A2E: call     0x8360a6e ; _ZNK12CDataManager14get_mob_rewardEiRj
08467A33: mov      eax, dword ptr [ebp + 8]
08467A36: mov      eax, dword ptr [eax]
08467A38: mov      dword ptr [esp + 4], eax
08467A3C: lea      eax, [ebp - 0x84]
08467A42: mov      dword ptr [esp], eax
08467A45: call     0x8469976 ; _ZN10QuickParty16RewardData_ParamC1EP6CParty
08467A4A: lea      eax, [ebp - 0xa0]
08467A50: mov      dword ptr [esp], eax
08467A53: call     0x84699c6 ; _ZN10QuickParty17RewardData_ResultC2Ev
08467A58: mov      eax, dword ptr [ebp - 0x7c]
08467A5B: mov      dword ptr [ebp - 0xa0], eax
08467A61: mov      eax, dword ptr [ebp + 8]
08467A64: mov      ecx, dword ptr [eax]
08467A66: lea      eax, [ebp - 0xa0]
08467A6C: mov      dword ptr [esp + 0x10], eax
08467A70: mov      eax, dword ptr [ebp - 0x84]
08467A76: mov      edx, dword ptr [ebp - 0x80]
08467A79: mov      dword ptr [esp + 8], eax
08467A7D: mov      dword ptr [esp + 0xc], edx
08467A81: mov      dword ptr [esp + 4], 0
08467A89: mov      dword ptr [esp], ecx
08467A8C: call     0x859b8fa ; _ZN6CParty17set_random_rewardEN10QuickParty14RandomBuffTypeENS0_16RewardData_ParamERNS0_17RewardData_ResultE
08467A91: mov      eax, dword ptr [ebp - 0xa0]
08467A97: mov      dword ptr [ebp - 0x7c], eax
08467A9A: mov      eax, dword ptr [ebp + 8]
08467A9D: lea      esi, [eax + 0xc]
08467AA0: mov      eax, dword ptr [ebp + 8]
08467AA3: mov      ecx, dword ptr [eax]
08467AA5: mov      edx, dword ptr [ebp - 0x7c]
08467AA8: mov      eax, dword ptr [ebp - 0x5c]
08467AAB: mov      ebx, dword ptr [ebp + 8]
08467AAE: add      ebx, 0x110
08467AB4: mov      dword ptr [esp + 0x14], esi
08467AB8: mov      dword ptr [esp + 0x10], ecx
08467ABC: mov      dword ptr [esp + 0xc], edx
08467AC0: mov      dword ptr [esp + 8], eax
08467AC4: mov      eax, dword ptr [ebp - 0x34]
08467AC7: mov      dword ptr [esp + 4], eax
08467ACB: mov      dword ptr [esp], ebx
08467ACE: call     0x8462538 ; _ZN8WongWork11CDeathTower9CPlayData17generateRewardExpEiijP6CPartyRNS0_11CDungeonMgrE
08467AD3: jmp      0x8467af0
08467AD5: mov      ebx, edx
08467AD7: mov      esi, eax
08467AD9: lea      eax, [ebp - 0x74]
08467ADC: mov      dword ptr [esp], eax
08467ADF: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08467AE4: mov      eax, esi
08467AE6: mov      edx, ebx
08467AE8: mov      dword ptr [esp], eax
08467AEB: call     0x8ae3750 ; _Unwind_Resume
08467AF0: lea      eax, [ebp - 0x74]
08467AF3: mov      dword ptr [esp], eax
08467AF6: call     0x8469aa0 ; _ZN8WongWork11CDeathTower14CPacketHandlerD2Ev
08467AFB: jmp      0x8467afe
08467AFD: nop
08467AFE: add      dword ptr [ebp - 0x34], 1
08467B02: cmp      dword ptr [ebp - 0x34], 3
08467B06: setle    al
08467B09: test     al, al
08467B0B: jne      0x8467452
08467B11: jmp      0x8467b14
08467B13: nop
08467B14: add      esp, 0xcc
08467B1A: pop      ebx
08467B1B: pop      esi
08467B1C: pop      edi
08467B1D: pop      ebp
08467B1E: ret
```

## 0x08467B50 _ZN8WongWork11CDeathTower18_onStartDeathTowerEv

```asm
08467B50: push     ebp
08467B51: mov      ebp, esp
08467B53: push     edi
08467B54: push     ebx
08467B55: sub      esp, 0x130
08467B5B: lea      ebx, [ebp - 0x110]
08467B61: mov      eax, 0
08467B66: mov      edx, 0x40
08467B6B: mov      edi, ebx
08467B6D: mov      ecx, edx
08467B6F: rep stosd dword ptr es:[edi], eax
08467B71: mov      eax, dword ptr [ebp + 8]
08467B74: mov      eax, dword ptr [eax]
08467B76: lea      edx, [ebp - 0x110]
08467B7C: mov      dword ptr [esp + 4], edx
08467B80: mov      dword ptr [esp], eax
08467B83: call     0x8145882 ; _ZN6CParty14getMemberNamesEPc
08467B88: mov      dword ptr [ebp - 0x10], 0
08467B8F: jmp      0x8467c93
08467B94: mov      dword ptr [ebp - 0xc], 0
08467B9B: mov      eax, dword ptr [ebp + 8]
08467B9E: mov      eax, dword ptr [eax]
08467BA0: mov      edx, dword ptr [ebp - 0x10]
08467BA3: mov      dword ptr [esp + 4], edx
08467BA7: mov      dword ptr [esp], eax
08467BAA: call     0x8145764 ; _ZN6CParty8get_userEi
08467BAF: mov      dword ptr [ebp - 0xc], eax
08467BB2: cmp      dword ptr [ebp - 0xc], 0
08467BB6: je       0x8467bd3
08467BB8: mov      eax, dword ptr [ebp + 8]
08467BBB: mov      eax, dword ptr [eax]
08467BBD: mov      edx, dword ptr [ebp - 0x10]
08467BC0: mov      dword ptr [esp + 4], edx
08467BC4: mov      dword ptr [esp], eax
08467BC7: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08467BCC: xor      eax, 1
08467BCF: test     al, al
08467BD1: je       0x8467bda
08467BD3: mov      eax, 1
08467BD8: jmp      0x8467bdf
08467BDA: mov      eax, 0
08467BDF: test     al, al
08467BE1: jne      0x8467c8e
08467BE7: mov      eax, dword ptr [ebp + 8]
08467BEA: mov      eax, dword ptr [eax]
08467BEC: mov      eax, dword ptr [eax + 0xcd8]
08467BF2: mov      ebx, eax
08467BF4: mov      eax, dword ptr [ebp + 8]
08467BF7: add      eax, 0xc
08467BFA: mov      dword ptr [esp], eax
08467BFD: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467C02: mov      dword ptr [esp], eax
08467C05: call     0x81455a6 ; _ZNK8CDungeon14GetDungeonNameEv
08467C0A: mov      edx, dword ptr [ebp - 0xc]
08467C0D: lea      ecx, [edx + 0x79700]
08467C13: mov      dword ptr [esp + 0x10], ebx
08467C17: lea      edx, [ebp - 0x110]
08467C1D: mov      dword ptr [esp + 0xc], edx
08467C21: mov      dword ptr [esp + 8], 0
08467C29: mov      dword ptr [esp + 4], eax
08467C2D: mov      dword ptr [esp], ecx
08467C30: call     0x86849a2 ; _ZN15cUserHistoryLog12EnterDungeonEPKciS1_i
08467C35: mov      eax, dword ptr [ebp - 0xc]
08467C38: mov      dword ptr [esp], eax
08467C3B: call     0x814aa5e ; _ZN5CUser18getCurCharacQuestWEv
08467C40: add      eax, 0x75d8
08467C45: mov      dword ptr [esp], eax
08467C48: call     0x814aa16 ; _ZN18_Quest_Authen_Data5resetEv
08467C4D: mov      eax, dword ptr [ebp + 8]
08467C50: add      eax, 0xc
08467C53: mov      dword ptr [esp], eax
08467C56: call     0x82345f0 ; _ZNK8WongWork11CDeathTower11CDungeonMgr10getDungeonEv
08467C5B: mov      dword ptr [esp], eax
08467C5E: call     0x80fdcf0 ; _ZNK8CDungeon9get_indexEv
08467C63: mov      ebx, eax
08467C65: mov      eax, dword ptr [ebp - 0xc]
08467C68: mov      dword ptr [esp], eax
08467C6B: call     0x814aa5e ; _ZN5CUser18getCurCharacQuestWEv
08467C70: mov      dword ptr [esp + 0xc], 0xffffffff
08467C78: mov      dword ptr [esp + 8], ebx
08467C7C: mov      dword ptr [esp + 4], 0
08467C84: mov      dword ptr [esp], eax
08467C87: call     0x86abdb0 ; _ZN9UserQuest15set_authen_dataE15QUEST_CONDITIONii
08467C8C: jmp      0x8467c8f
08467C8E: nop
08467C8F: add      dword ptr [ebp - 0x10], 1
08467C93: cmp      dword ptr [ebp - 0x10], 3
08467C97: setle    al
08467C9A: test     al, al
08467C9C: jne      0x8467b94
08467CA2: add      esp, 0x130
08467CA8: pop      ebx
08467CA9: pop      edi
08467CAA: pop      ebp
08467CAB: ret
```

## 0x08467CC2 _ZN8WongWork11CDeathTower14_onFinishStageEv

```asm
08467CC2: push     ebp
08467CC3: mov      ebp, esp
08467CC5: push     ebx
08467CC6: sub      esp, 0x34
08467CC9: mov      eax, dword ptr [ebp + 8]
08467CCC: add      eax, 0x14
08467CCF: mov      dword ptr [esp], eax
08467CD2: call     0x8461c40 ; _ZN8WongWork11CDeathTower6CStage13onFinishStageEv
08467CD7: mov      eax, dword ptr [ebp + 8]
08467CDA: add      eax, 0x14
08467CDD: mov      dword ptr [esp], eax
08467CE0: call     0x82345fa ; _ZN8WongWork11CDeathTower6CStage17getStageClearTimeEv
08467CE5: mov      edx, dword ptr [ebp + 8]
08467CE8: add      edx, 0x110
08467CEE: mov      dword ptr [esp + 4], eax
08467CF2: mov      dword ptr [esp], edx
08467CF5: call     0x8469bb2 ; _ZN8WongWork11CDeathTower9CPlayData11addPlayTimeEj
08467CFA: mov      dword ptr [ebp - 0x10], 0
08467D01: jmp      0x8467deb
08467D06: mov      dword ptr [ebp - 0xc], 0
08467D0D: mov      eax, dword ptr [ebp + 8]
08467D10: mov      eax, dword ptr [eax]
08467D12: mov      edx, dword ptr [ebp - 0x10]
08467D15: mov      dword ptr [esp + 4], edx
08467D19: mov      dword ptr [esp], eax
08467D1C: call     0x8145764 ; _ZN6CParty8get_userEi
08467D21: mov      dword ptr [ebp - 0xc], eax
08467D24: cmp      dword ptr [ebp - 0xc], 0
08467D28: je       0x8467d45
08467D2A: mov      eax, dword ptr [ebp + 8]
08467D2D: mov      eax, dword ptr [eax]
08467D2F: mov      edx, dword ptr [ebp - 0x10]
08467D32: mov      dword ptr [esp + 4], edx
08467D36: mov      dword ptr [esp], eax
08467D39: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08467D3E: xor      eax, 1
08467D41: test     al, al
08467D43: je       0x8467d4c
08467D45: mov      eax, 1
08467D4A: jmp      0x8467d51
08467D4C: mov      eax, 0
08467D51: test     al, al
08467D53: jne      0x8467de6
08467D59: mov      eax, dword ptr [ebp + 8]
08467D5C: add      eax, 0x14
08467D5F: mov      dword ptr [esp], eax
08467D62: call     0x82345fa ; _ZN8WongWork11CDeathTower6CStage17getStageClearTimeEv
08467D67: cmp      eax, 0x176f
08467D6C: setbe    al
08467D6F: test     al, al
08467D71: je       0x8467dad
08467D73: mov      eax, dword ptr [ebp - 0xc]
08467D76: mov      dword ptr [esp], eax
08467D79: call     0x80f995a ; _ZN5CUser15getHackAnalyzerEv
08467D7E: mov      dword ptr [esp + 0x14], 0
08467D86: mov      dword ptr [esp + 0x10], 0
08467D8E: mov      dword ptr [esp + 0xc], 1
08467D96: mov      dword ptr [esp + 8], 0x19a
08467D9E: mov      edx, dword ptr [ebp - 0xc]
08467DA1: mov      dword ptr [esp + 4], edx
08467DA5: mov      dword ptr [esp], eax
08467DA8: call     0x80f8c7e ; _ZN8WongWork13CHackAnalyzer16addServerHackCntEP5CUserNS_13ENUM_HACKTYPEEjjj
08467DAD: mov      eax, dword ptr [ebp + 8]
08467DB0: add      eax, 0x14
08467DB3: mov      dword ptr [esp], eax
08467DB6: call     0x8469b0c ; _ZN8WongWork11CDeathTower6CStage16getStageMapIndexEv
08467DBB: mov      ebx, eax
08467DBD: mov      eax, dword ptr [ebp - 0xc]
08467DC0: mov      dword ptr [esp], eax
08467DC3: call     0x814aa5e ; _ZN5CUser18getCurCharacQuestWEv
08467DC8: mov      dword ptr [esp + 0xc], 0
08467DD0: mov      dword ptr [esp + 8], ebx
08467DD4: mov      dword ptr [esp + 4], 1
08467DDC: mov      dword ptr [esp], eax
08467DDF: call     0x86abdb0 ; _ZN9UserQuest15set_authen_dataE15QUEST_CONDITIONii
08467DE4: jmp      0x8467de7
08467DE6: nop
08467DE7: add      dword ptr [ebp - 0x10], 1
08467DEB: cmp      dword ptr [ebp - 0x10], 3
08467DEF: setle    al
08467DF2: test     al, al
08467DF4: jne      0x8467d06
08467DFA: add      esp, 0x34
08467DFD: pop      ebx
08467DFE: pop      ebp
08467DFF: ret
```

## 0x08467E00 _ZN8WongWork11CDeathTower21_onPrepareFinishTowerEv

```asm
08467E00: push     ebp
08467E01: mov      ebp, esp
08467E03: push     esi
08467E04: push     ebx
08467E05: sub      esp, 0x20
08467E08: mov      dword ptr [esp + 4], 0x4a
08467E10: mov      eax, dword ptr [ebp + 8]
08467E13: mov      dword ptr [esp], eax
08467E16: call     0x830edec ; _ZN8WongWork11CDeathTower12_genTimerKeyE13TIMER_MESSAGE
08467E1B: mov      ebx, eax
08467E1D: mov      eax, dword ptr [ebp + 8]
08467E20: mov      esi, dword ptr [eax + 4]
08467E23: call     0x80f647c ; _Z12G_TimerQueuev
08467E28: mov      dword ptr [esp + 0x18], 0x4a
08467E30: mov      dword ptr [esp + 0x14], ebx
08467E34: mov      dword ptr [esp + 0x10], 5
08467E3C: mov      dword ptr [esp + 0xc], 0x4a
08467E44: mov      dword ptr [esp + 8], esi
08467E48: mov      dword ptr [esp + 4], 6
08467E50: mov      dword ptr [esp], eax
08467E53: call     0x8630e16 ; _ZN10TimerQueue11InsertTimerEN10TimerEntry8OBJ_TYPEEi13TIMER_MESSAGEiij
08467E58: add      esp, 0x20
08467E5B: pop      ebx
08467E5C: pop      esi
08467E5D: pop      ebp
08467E5E: ret
```

## 0x08467E60 _ZN8WongWork11CDeathTower8_onClearEb

```asm
08467E60: push     ebp
08467E61: mov      ebp, esp
08467E63: sub      esp, 0x38
08467E66: mov      eax, dword ptr [ebp + 0xc]
08467E69: mov      byte ptr [ebp - 0x1c], al
08467E6C: mov      dword ptr [ebp - 0x14], 0
08467E73: cmp      byte ptr [ebp - 0x1c], 0
08467E77: je       0x8467e8e
08467E79: mov      eax, dword ptr [ebp + 8]
08467E7C: add      eax, 0x110
08467E81: mov      dword ptr [esp], eax
08467E84: call     0x8469bd4 ; _ZN8WongWork11CDeathTower9CPlayData11getPlayTimeEv
08467E89: mov      dword ptr [ebp - 0x14], eax
08467E8C: jmp      0x8467ea1
08467E8E: mov      eax, dword ptr [ebp + 8]
08467E91: add      eax, 0x110
08467E96: mov      dword ptr [esp], eax
08467E99: call     0x8469be0 ; _ZN8WongWork11CDeathTower9CPlayData15getLastPlayTimeEv
08467E9E: mov      dword ptr [ebp - 0x14], eax
08467EA1: mov      dword ptr [ebp - 0x10], 0
08467EA8: jmp      0x8467f5c
08467EAD: mov      dword ptr [ebp - 0xc], 0
08467EB4: mov      eax, dword ptr [ebp + 8]
08467EB7: mov      eax, dword ptr [eax]
08467EB9: mov      edx, dword ptr [ebp - 0x10]
08467EBC: mov      dword ptr [esp + 4], edx
08467EC0: mov      dword ptr [esp], eax
08467EC3: call     0x8145764 ; _ZN6CParty8get_userEi
08467EC8: mov      dword ptr [ebp - 0xc], eax
08467ECB: cmp      dword ptr [ebp - 0xc], 0
08467ECF: je       0x8467eec
08467ED1: mov      eax, dword ptr [ebp + 8]
08467ED4: mov      eax, dword ptr [eax]
08467ED6: mov      edx, dword ptr [ebp - 0x10]
08467ED9: mov      dword ptr [esp + 4], edx
08467EDD: mov      dword ptr [esp], eax
08467EE0: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08467EE5: xor      eax, 1
08467EE8: test     al, al
08467EEA: je       0x8467ef3
08467EEC: mov      eax, 1
08467EF1: jmp      0x8467ef8
08467EF3: mov      eax, 0
08467EF8: test     al, al
08467EFA: jne      0x8467f57
08467EFC: mov      eax, dword ptr [ebp + 8]
08467EFF: mov      eax, dword ptr [eax]
08467F01: mov      edx, dword ptr [ebp - 0x10]
08467F04: mov      dword ptr [esp + 4], edx
08467F08: mov      dword ptr [esp], eax
08467F0B: call     0x8145764 ; _ZN6CParty8get_userEi
08467F10: mov      dword ptr [esp + 4], 0
08467F18: mov      dword ptr [esp], eax
08467F1B: call     0x8469a5c ; _ZN5CUser23setChattingMessageCountEi
08467F20: mov      ecx, dword ptr [ebp - 0x14]
08467F23: mov      edx, 0x10624dd3
08467F28: mov      eax, ecx
08467F2A: imul     edx
08467F2C: sar      edx, 6
08467F2F: mov      eax, ecx
08467F31: sar      eax, 0x1f
08467F34: mov      ecx, edx
08467F36: sub      ecx, eax
08467F38: movzx    eax, byte ptr [ebp - 0x1c]
08467F3C: mov      edx, dword ptr [ebp - 0xc]
08467F3F: add      edx, 0x79700
08467F45: mov      dword ptr [esp + 8], ecx
08467F49: mov      dword ptr [esp + 4], eax
08467F4D: mov      dword ptr [esp], edx
08467F50: call     0x8684ac4 ; _ZN15cUserHistoryLog16DungeonClearInfoEil
08467F55: jmp      0x8467f58
08467F57: nop
08467F58: add      dword ptr [ebp - 0x10], 1
08467F5C: cmp      dword ptr [ebp - 0x10], 3
08467F60: setle    al
08467F63: test     al, al
08467F65: jne      0x8467ead
08467F6B: leave
08467F6C: ret
```

## 0x08467F6E _ZN8WongWork11CDeathTower19_onFinishDeathTowerEv

```asm
08467F6E: push     ebp
08467F6F: mov      ebp, esp
08467F71: sub      esp, 0x28
08467F74: mov      dword ptr [ebp - 0x10], 0
08467F7B: jmp      0x8467fe5
08467F7D: mov      dword ptr [ebp - 0xc], 0
08467F84: mov      eax, dword ptr [ebp + 8]
08467F87: mov      eax, dword ptr [eax]
08467F89: mov      edx, dword ptr [ebp - 0x10]
08467F8C: mov      dword ptr [esp + 4], edx
08467F90: mov      dword ptr [esp], eax
08467F93: call     0x8145764 ; _ZN6CParty8get_userEi
08467F98: mov      dword ptr [ebp - 0xc], eax
08467F9B: cmp      dword ptr [ebp - 0xc], 0
08467F9F: je       0x8467fbc
08467FA1: mov      eax, dword ptr [ebp + 8]
08467FA4: mov      eax, dword ptr [eax]
08467FA6: mov      edx, dword ptr [ebp - 0x10]
08467FA9: mov      dword ptr [esp + 4], edx
08467FAD: mov      dword ptr [esp], eax
08467FB0: call     0x8145868 ; _ZN6CParty14checkValidUserEi
08467FB5: xor      eax, 1
08467FB8: test     al, al
08467FBA: je       0x8467fc3
08467FBC: mov      eax, 1
08467FC1: jmp      0x8467fc8
08467FC3: mov      eax, 0
08467FC8: test     al, al
08467FCA: jne      0x8467fe0
08467FCC: mov      eax, dword ptr [ebp - 0xc]
08467FCF: mov      dword ptr [esp + 4], eax
08467FD3: mov      eax, dword ptr [ebp + 8]
08467FD6: mov      dword ptr [esp], eax
08467FD9: call     0x84636f2 ; _ZN8WongWork11CDeathTower11onLeaveUserEP5CUser
08467FDE: jmp      0x8467fe1
08467FE0: nop
08467FE1: add      dword ptr [ebp - 0x10], 1
08467FE5: cmp      dword ptr [ebp - 0x10], 3
08467FE9: setle    al
08467FEC: test     al, al
08467FEE: jne      0x8467f7d
08467FF0: mov      eax, dword ptr [ebp + 8]
08467FF3: mov      eax, dword ptr [eax]
08467FF5: mov      dword ptr [esp], eax
08467FF8: call     0x85aca60 ; _ZN6CParty15ReturnToVillageEv
08467FFD: mov      eax, dword ptr [ebp + 8]
08468000: mov      dword ptr [esp], eax
08468003: call     0x8469c5c ; _ZN8WongWork11CDeathTower13getTowerStateEv
08468008: test     eax, eax
0846800A: setg     al
0846800D: test     al, al
0846800F: je       0x846801c
08468011: mov      eax, dword ptr [ebp + 8]
08468014: mov      dword ptr [esp], eax
08468017: call     0x8467b20 ; _ZN8WongWork11CDeathTower8_destroyEv
0846801C: leave
0846801D: ret
```

## 0x08469B02 _ZN8WongWork11CDeathTower6CStage15getCurrentStageEv

```asm
08469B02: push     ebp
08469B03: mov      ebp, esp
08469B05: mov      eax, dword ptr [ebp + 8]
08469B08: mov      eax, dword ptr [eax]
08469B0A: pop      ebp
08469B0B: ret
```

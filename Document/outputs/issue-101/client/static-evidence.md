# GH #101 A21 客户端静态证据摘录

完整论证与未知项见同目录 [`findings.md`](findings.md)。本页保留主线程更新协议页所需的最小
可引用证据。

## 样本绑定

- A21 `DNF.exe` SHA-256：
  `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`
- RVA 转储：
  `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`
- 转储 SHA-256：
  `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`
- `manifest.json` SHA-256：
  `DFE449F82311AF3FF320F56AAA0A9311DFD6108CE6F19F405641049EFAD99D1A`
- manifest：`image_base=0x00400000`、`size_of_image=122085376`、`hole_bytes=0`。
- 下文均为 RVA；`VA = RVA + 0x00400000`。

## 共同原语

相同转储直接确认宽度：

```asm
0234D2F2 inc [ecx+0x2BCC2C]       ; WriteU8
0234D2F8 push 1
0234D322 add [ecx+0x2BCC2C], 2    ; WriteU16
0234D329 push 2
0234D352 add [ecx+0x2BCC2C], 4    ; WriteU32
0234D359 push 4

023C5238 mov cl, byte ptr [eax]   ; ReadU8
023C523D inc eax
023C52A8 mov cx, word ptr [eax]   ; ReadU16
023C52AE add eax, 2
023C5318 mov ecx, dword ptr [eax] ; ReadU32
023C531D add eax, 4
```

## C2S CMD `0x0010` 普通路径

客户端字符串 census 把 RVA `0x0232D990` / VA `0x0272D990` 标为
`CNSelectDungeonModule::sendSelectDungeonPacket`。入口检查：

```asm
0232D9BE mov ebx, [ebp+8]       ; dungeonId
0232D9C9 test ebx, ebx
0232D9CB js 0232DAAE            ; 负数不发
0232D9D1 cmp dword ptr [ebp+C], 4
0232D9D5 ja 0232DAAE            ; difficulty > 4 不发
0232DEE6 mov edi, [ebp+C]       ; difficulty
```

普通构包直线路径 RVA `0x0232DF3A..0x0232E067`：

```asm
0232DF3C push 10h
0232DF45 call 0234D1E0 ; Begin(type=0x0010)
0232DF4A push ebx
0232DF52 call 0234D340 ; u32 dungeonId
0232DF57 push edi
0232DF5F call 0234D2E0 ; u8 difficulty
0232DF80 call 0234D2E0 ; u8
0232DFFD call 0234D2E0 ; u8
0232E011 call 0234D310 ; u16
0232E016 push 0
0232E01F call 0234D340 ; u32 constant zero
0232E033 call 0234D2E0 ; u8
0232E062 call 0234D2E0 ; u8
0232E067 call 0234E6D0 ; Finalize
```

普通 body：

```text
+0  u32 dungeonId
+4  u8  difficulty (0..4)
+5  u8  unknown/module state
+6  u8  unknown/derived
+7  u16 unknown/module word
+9  u32 constant 0
+13 u8  unknown/module flag
+14 u8  unknown/dungeon-derived flag or 0
```

合计 **15B**。共享正文校验选择器 RVA `0x00026710` 对 CMD type `0x0010` 返回 0：

```asm
00026727 sub eax, 1Ch ; type 0x10
0002672A cmp eax, 12h
0002672D ja 00026791
00026791 xor al, al   ; 不追加 4B
```

归档 A21 中转记录 `protocol/patterns/data/traces/a21-live-entry-20260825.md:140` 也观察到
线上 body **15B**。其文件 SHA-256：
`EEBBFB11CB6A025A5F18B88A215C1D7EB2A44C36D8FB55831CA13D4629156ADE`。

该线上记录来自教学自动突发，只佐证 A21 的一个教学实例同为 15B，不能证明原生普通 UI
实际调用了上述命名函数。机器 census 另有多个 `0x0010` 构包点；普通函数的布局结论来自
静态写入链，不外推所有特殊分支。

## S2C CMD `0x0010` 拒绝消费

handler RVA `0x00D0C080` / VA `0x0110C080`：

```asm
00D0C08A mov ebx, [ebp+C]        ; result
00D0C0AD test bl, bl
00D0C0B0 jne 00D0C14C           ; success: no extra read
00D0C0B6 movzx eax, byte [ebp+10h] ; errorCode
00D0C0BB call 00D0A760           ; SelectDungeonErrorHandler
```

共享处理器 RVA `0x00D0A760` / VA `0x0110A760`：

```asm
00D0A7DA call 023C5230           ; exactly one ReadU8(context)
00D0A7F5 mov esi, [ebp+8]        ; errorCode
00D0A810 movzx ecx, byte [ebp-0xA11] ; context
00D0A817 push ecx
00D0A818 push esi
...
00D0A842 jmp dword ptr [edx*4+0110BF04h] ; error switch
```

结合 3B 拒绝帧，客户端期待 `00 | errorCode | context`。除下列三个核心码外，其余上游命名
仍只能作线索。

### 核心错误码补充：`0x01`、`0x16`、`0xAD`

switch 索引表 RVA `0x00D0BF8C`、目标表 RVA `0x00D0BF04`：

| errorCode | 目标 | context | A21 客户端结果 |
|---:|---|---|---|
| `0x01` | 默认收尾 `0x00D0BEE6` | 不使用 | 无固定 localization；本 handler 静默 |
| `0x16` | RVA `0x00D0AB0A` | 作为 0..7 成员槽位 | 文案 ID `0xBFA5`，插入成员名和疲劳值 |
| `0xAD` | RVA `0x00D0B475` | 作为 0..7 成员槽位 | 按成员状态选 `0x1138A` 或 `0x697F` |

成员槽位由 helper 自身确认：

```asm
015E7753 mov eax, [ebp+8]            ; context
015E7756 cmp eax, 7
015E7759 ja 015E7763
015E775B mov eax, [ecx+eax*4+30h]    ; 8 个成员对象槽
```

`0x16` 与 `0xAD` 两个目标都先把 `[ebp-0xA11]` 的 context 交给 RVA `0x015E7750`，
随后 RVA `0x023C6F20` 返回成员对象的 `+0x14` 名字字段。因此这两个错误码下的 context
可以静态确认为**队伍成员槽位 0..7**，不是普通数值参数。

A21 `dstr.dat` SHA-256
`60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`，FILE 资源 102/104
的 UTF-8 文本：

- `0xBFA5`：`玩家[%s]，因入场所需的疲劳值(%d)不足，无法挑战该地下城。`
  日文：`%sさんがダンジョン進入要求疲労度(%d)不足のため、該当ダンジョンに入場できません。`
- `0x1138A`：`玩家[%s]的入场权限不足，无法进入该地下城。`
  日文：`%sさんの入場権限が足りなくて、該当ダンジョンに入場できません。`
- `0x697F`：`因[%s]超过进入上限次数，无法进入该地下城。`
  日文：`%sさんが入場可能回数を超過しているため、該当ダンジョンに入場できません。`

`0xAD` 的两条文本由客户端根据该成员对象的附加状态选择，errorCode/context 本身没有再带
一个“权限不足或次数上限”的子类型。该内部状态谓词的业务字段名仍为 Unknown。

`0x01` 小于 switch 明确范围 `7..249`；switch 前的动态分支只接受 `0x97/0x98`，故
`0x01` 在本 handler 中不加载提示文本。它可以表示服务端的通用拒绝，但客户端表现是静默，
不能从静态客户端给它命名为 DungeonUnavailable。

## S2C NOTI `0x0005`

handler RVA `0x00D45E40` / VA `0x01145E40`：

```asm
00D45E51 call 023C52A0 ; ReadU16(count)
00D45E5C cmp ax, -1
00D45E60 jne 00D45EB5
...
00D45EB2 mov [ebp-0xC], 0 ; 0xFFFF: 提示后把本地循环数置 0

00D45ED4 call 023C5310 ; repeated ReadU32(dungeonId)
00D45EDF call 023C5230 ; repeated ReadU8(clearState)
```

正常 body 是 `u16 count + count * {u32 dungeonId, u8 clearState}`，严格 **5B/项**。
`count=0xFFFF` 不消费条目，也不调用映射 setter。

clearState 链：

```asm
00D45F31 call 00CCDEE0 ; effectiveOldState(dungeonId)
00D45F36 movzx edx, byte [ebp-1] ; newState
00D45F3D cmp edx, eax
00D45F3F jle 00D45F7D  ; 只有 new > old 才走 UI 更新
...
00D45F84 push eax      ; 原始 newState
00D45F85 push ecx      ; dungeonId
00D45F86 call 00CCDD40 ; 每项无条件写映射

02331125 call 00CCDEE0
0233112D cmp eax, [edi+0x3E4] ; selectedDifficulty
02331133 jge 02331145         ; state >= selected，直接比较，无 +/-1
```

因此该字节是客户端用于难度选择上界的状态/序数。尚未知：0..4 对应的玩家可见难度名、
特殊副本 metadata 分支的值域，以及原版服务端额外准入/疲劳政策。客户端 UI 门不能证明这些
服务端规则。

证据级别：以上布局和消费链为 A21 客户端静态走图（协议证据等级 4）；空列表 `00 00`
有归档线上字节（等级 2），但非空 5B 条目仍建议用中转补样本。

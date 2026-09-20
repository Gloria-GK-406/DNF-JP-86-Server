# GH #101：A21 普通地下城选择与权限消费静态取证

## 范围与结论

本报告只裁决 A21 客户端的普通 `CMD 0x0010 SELECT_DUNGEON` 构包、同 type
拒绝响应的消费形状，以及 `NOTI 0x0005 DUNGEON_PERMISSION` 的读取和客户端消费。
没有启动或操作客户端，也没有启用与 `0050_A21RuntimeFix` 冲突的
`0100_NetworkHook`。

结论：

1. A21 普通选择函数 `CNSelectDungeonModule::sendSelectDungeonPacket` 发送 **15 字节**
   body；前两字段是 `u32 dungeonId`、`u8 difficulty`。因此当前服务端把 ID 读成
   `u16`、把 `body[2]` 当 difficulty 的实现不适用于 A21；difficulty 在 offset 4。
2. 该普通 body 的静态写入序列为
   `u32,u8,u8,u8,u16,u32,u8,u8`，合计 15 字节。`CMD 0x0010` 不命中正文
   末尾 4 字节校验追加表；真实 A21 中转记录也观察到 15 字节，不是 19 字节。
3. A21 的 `CMD 0x0010` 响应 handler 在 `result != 0` 时不读附加 body；
   `result == 0` 时把 CMD 信封交来的一个 `errorCode` 传给共享拒绝处理器，后者再从
   reader 读取**恰好一个 `u8 context`**。结合现存 3 字节拒绝帧，拒绝形状为
   `00 | errorCode | context`。本报告不沿用上游对具体错误码业务含义的命名。
4. A21 的 `NOTI 0x0005` 正常形状是
   `u16 count + count * (u32 dungeonId + u8 clearState)`，即 **5 字节/项**。
   `count == 0xFFFF` 是整帧控制哨兵：客户端显示提示、把本地循环次数置零，不消费条目，
   也不清空权限映射。
5. 每个普通条目的 `clearState` 原值被无条件写入按 dungeonId 索引的客户端映射；仅当
   `newState > effectiveOldState` 时额外触发 UI 更新。选择模块直接比较
   `effectiveState >= selectedDifficulty`，没有在这条比较路径上做 `+1/-1`。这支持把
   clearState 描述为客户端的难度上界/解锁序数，但不证明各数值的名称，也不证明原版
   服务端应采用什么准入、疲劳或组队政策。

## 样本绑定

| 工件 | SHA-256 | 用途 |
|---|---|---|
| `E:/Project/DNF/DNFClient-A21-protocol/DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` | A21 客户端身份 |
| `E:/Project/DNF/DNFClient-A21-protocol/DNF.unpacked-b1-debug.exe` | `CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14` | 可运行重建 PE，仅作交叉标识 |
| `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` | `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507` | 本报告全部反汇编来源 |
| 同目录 `manifest.json` | `DFE449F82311AF3FF320F56AAA0A9311DFD6108CE6F19F405641049EFAD99D1A` | `image_base=0x00400000`、`size=122085376`、`hole_bytes=0` |
| `protocol/patterns/data/traces/a21-live-entry-20260825.md` | `EEBBFB11CB6A025A5F18B88A215C1D7EB2A44C36D8FB55831CA13D4629156ADE` | A21 中转观察到 `0x0010` body 15B（第 140 行） |
| `protocol/patterns/data/traces/a21-reference-entry-bytes-20260825.md` | `A26A47211AF8F295696D19D97B6078E0A04D325F401827574919C0B44EAE87E0` | 归档参照会话完整 `0x0010` 15B（第 173–174 行）与空权限表（第 45–46 行） |
| `protocol/patterns/data/traces/a21-dungeon-wire-20260826.md` | `09B3FD6B598B99770AC98EE74D1D79EC484C194D7A2F4E3397CBE55B4D8C3DA7` | 按 A21 实机会话字节重放，`0x0010` 15B（第 134 行）；不是第二份客户端抓包 |

转储文件按 RVA 索引；下文 `VA = RVA + 0x00400000`。

## 共同读写原语的独立核验

字段宽度不是由旧协议页的函数名猜测。相同转储中的原语直接推进固定字节数：

```asm
; WriteU8, RVA 0234D2E0
0234D2F2 inc dword ptr [ecx+0x2BCC2C]
0234D2F8 push 1

; WriteU16, RVA 0234D310
0234D322 add dword ptr [ecx+0x2BCC2C], 2
0234D329 push 2

; WriteU32, RVA 0234D340
0234D352 add dword ptr [ecx+0x2BCC2C], 4
0234D359 push 4

; ReadU8, RVA 023C5230
023C5238 mov cl, byte ptr [eax]
023C523D inc eax
023C523E dec dword ptr [0x03BAAE3C]

; ReadU16, RVA 023C52A0
023C52A8 mov cx, word ptr [eax]
023C52AE add eax, 2
023C52B1 sub dword ptr [0x03BAAE3C], 2

; ReadU32, RVA 023C5310
023C5318 mov ecx, dword ptr [eax]
023C531D add eax, 4
023C5320 sub dword ptr [0x03BAAE3C], 4
```

证据级别：A21 客户端静态走图（协议证据等级 4）；宽度由原语自身指令确认。

## `CMD 0x0010` 普通构包路径

### 身份与参数约束

字符串/源文件 census 将 VA `0x0272D990`（RVA `0x0232D990`）标为：

- `DNFClient\\RDAR\\CNSelectDungeonModule.cpp`
- `CNSelectDungeonModule::sendSelectDungeonPacket`
- `check difficulty failed`

函数入口直接约束两个参数：

```asm
0232D9BE mov ebx, dword ptr [ebp+8] ; dungeonId，后续可能被客户端映射成最终 ID
0232D9C9 test ebx, ebx
0232D9CB js 0232DAAE               ; 负数拒绝发送
0232D9D1 cmp dword ptr [ebp+0xC], 4
0232D9D5 ja 0232DAAE               ; difficulty 只允许 0..4
0232DEE6 mov edi, dword ptr [ebp+0xC]
```

这只证明客户端发送门；服务端仍需自行验证，不能把客户端门当作安全边界。

### 普通直线路径的正文

RVA `0x0232DF3A`–`0x0232E067`：

```asm
0232DF3A push 0
0232DF3C push 0x10
0232DF3E call 0234CA50 ; GetWriter
0232DF45 call 0234D1E0 ; Begin CMD 0x0010

0232DF4A push ebx
0232DF52 call 0234D340 ; WriteU32(final dungeonId)

0232DF57 push edi
0232DF5F call 0234D2E0 ; WriteU8(difficulty)

0232DF6D push 3        ; 特殊 module mode 时
; 或 0232DF71 movzx ecx, byte ptr [esi+0x3EC]
0232DF80 call 0234D2E0 ; WriteU8(module mode/state)

0232DFFD call 0234D2E0 ; WriteU8(由 [esi+0x3F8] 的多态小对象导出)
0232E011 call 0234D310 ; WriteU16([esi+0x57EADC])
0232E016 push 0
0232E01F call 0234D340 ; WriteU32(常量 0)
0232E033 call 0234D2E0 ; WriteU8([esi+0x57EAD8])
0232E062 call 0234D2E0 ; WriteU8(副本对象派生值，找不到时 0)
0232E067 call 0234E6D0 ; Finalize
```

因此普通正文是：

| Offset | Width | 静态来源 | 结论 |
|---:|---:|---|---|
| 0 | 4 | `WriteU32` | 最终 dungeonId |
| 4 | 1 | `WriteU8` | difficulty，函数入参，允许 0..4 |
| 5 | 1 | `WriteU8` | module mode/state；精确业务名未知 |
| 6 | 1 | `WriteU8` | 小对象派生值；精确业务名未知 |
| 7 | 2 | `WriteU16` | module word；精确业务名未知 |
| 9 | 4 | `WriteU32` | 常量 0 |
| 13 | 1 | `WriteU8` | module flag；精确业务名未知 |
| 14 | 1 | `WriteU8` | dungeon-derived flag 或 0；精确业务名未知 |

普通业务正文合计 `4+1+1+1+2+4+1+1 = 15` 字节。归档参照字节
`15 27 00 00 00 00 00 FF FF 00 00 00 00 00 00` 也按此切成：

```text
u32 dungeonId = 0x00002715 (10005)
u8  difficulty = 0
u8  field2 = 0
u8  field3 = 0
u16 field4 = 0xFFFF
u32 field5 = 0
u8  field6 = 0
u8  field7 = 0
```

### 没有 4 字节正文校验尾

Finalize 的共享检查函数 RVA `0x00026710` 接收 selector 类别和 type。对 CMD 类别 1，
type `0x0010` 在 `0x00026727` 先减 `0x1C`，随后无符号范围检查失败并落到
`0x00026791 xor al,al`，所以不追加校验：

```asm
0002671A movsx eax, word ptr [ebp+0xC] ; type
00026727 sub eax, 0x1C                 ; 0x10 -> 0xFFFFFFF4
0002672A cmp eax, 0x12
0002672D ja 00026791
00026791 xor al, al
```

共享 Finalize 在 RVA `0x01034D42` 测试该返回值；零值直接跳过附加逻辑。A21 中转记录
`a21-live-entry-20260825.md:140` 观察到教学自动突发的线上 body 也是 15B。这只佐证同一
type 的该教学实例没有成为 19B，不能证明原生普通 UI 实际调用了上述命名函数。

### 适用边界

机器 census 为同一 type 枚举了 8 个构包点，且有一个特殊点的自动提取结果与其余点不一致。
本报告静态确认带客户端原名的普通选择函数；现有 wire 则是教学自动突发，两者是分开的证据。
不把普通函数的 15B 布局外推为所有特殊玩法、教学旁路或多态构包点都同形。若服务端未来要
支持这些分支，应分别取得调用来源或样本。

证据级别：前两字段、普通 15B 路径、校验表不命中为 A21 静态走图（等级 4）；线上 15B
为中转捕获的客户端帧（等级 2）。二者独立闭合。

## `CMD 0x0010` 拒绝响应消费

注册 handler RVA `0x00D0C080`（VA `0x0110C080`）：

```asm
00D0C08A mov ebx, dword ptr [ebp+0xC] ; CMD 信封传入 result
00D0C0AD test bl, bl
00D0C0B0 jne 00D0C14C                ; 非零：直接返回，不读附加 body
00D0C0B6 movzx eax, byte ptr [ebp+0x10] ; 信封传入 errorCode
00D0C0BA push eax
00D0C0BB call 00D0A760                ; SelectDungeonErrorHandler(errorCode)
```

共享拒绝处理器 RVA `0x00D0A760`（VA `0x0110A760`）只进行一次 reader 调用：

```asm
00D0A7CA lea eax, [ebp-0xA11]
00D0A7D3 mov byte ptr [ebp-0xA11], 0
00D0A7DA call 023C5230                ; ReadU8(context)
00D0A7F5 mov esi, dword ptr [ebp+8]   ; errorCode

00D0A810 movzx ecx, byte ptr [ebp-0xA11]
00D0A817 push ecx                     ; context
00D0A818 push esi                     ; errorCode 经对象查找后参与前置判定
...
00D0A82D lea eax, [esi-7]
00D0A830 cmp eax, 0xF2
00D0A83B movzx edx, byte ptr [eax+0x0110BF8C]
00D0A842 jmp dword ptr [edx*4+0x0110BF04]
```

所以 `context` 不是一个可省略的装饰字节：共享处理器会读取它，且在错误码 switch 前已有
一条同时使用 errorCode/context 的条件路径。现有上游表把部分 context 命名为队员槽位，
但这个具体业务名没有被本次 A21 静态证据独立确认。错误码 switch 范围与提示分支存在，
本次有界任务没有穷举 34 个目标的文本含义。

证据级别：A21 客户端静态走图（等级 4）；现存 3 字节拒绝帧只证明当前/参照服务端发送形状，
不能单独证明原版服务端拒绝政策或每个错误码含义。

### 三个核心错误码的 A21 分支

只解析本任务点名的 `0x01`、`0x16`、`0xAD`。索引表 RVA `0x00D0BF8C` 和目标表
RVA `0x00D0BF04` 给出：

```text
0x01 -> 默认收尾 RVA 00D0BEE6（不在显式 switch 范围 7..249）
0x16 -> index 9  -> RVA 00D0AB0A
0xAD -> index 20 -> RVA 00D0B475
```

switch 前的动态处理器 RVA `0x000C16E0` 只匹配 `0x97/0x98`：它从 errorCode 减
`0x97` 后只接受零或一；因此 `0x01` 也不会在前置分支被消费。结论是 `0x01` 在本 handler
中不加载固定文案，直接静默收尾。客户端静态证据不能给它命名为 DungeonUnavailable。

`0x16` 目标先用 context 查询成员：

```asm
00D0AB0A movzx eax, byte ptr [ebp-0xA11] ; context
00D0AB17 push eax
00D0AB18 call 015E7750
00D0AB21 je 00D0BEE6                     ; 槽位无对象则静默
...
00D0AB80 call 023C6F20                   ; member + 0x14：名字
00D0AB89 push 0xBFA5                     ; localization id
00D0AB8E call 024C14C0
```

它还计算一个正整数，默认 1，若当前副本对象 `[+0x984] > 0` 则使用该值，并作为第二个格式
参数传给 `0xBFA5`。文本直接说明它是入场所需疲劳值：

```text
玩家[%s]，因入场所需的疲劳值(%d)不足，无法挑战该地下城。
```

`0xAD` 目标同样先用 context 查询成员，并从成员对象取名字：

```asm
00D0B475 movzx ecx, byte ptr [ebp-0xA11]
00D0B47C push ecx
00D0B483 call 015E7750
00D0B48C je 00D0BEE6
...
00D0B4BB call 023C6F20                   ; member name
00D0B4C3 push 0x1138A
...
00D0B4F3 call 023C6F20                   ; member name
00D0B4FB push 0x697F
```

两条文本分别是：

```text
0x1138A 玩家[%s]的入场权限不足，无法进入该地下城。
0x697F  因[%s]超过进入上限次数，无法进入该地下城。
```

成员 lookup RVA `0x015E7750` 自身闭合了 context 的含义：

```asm
015E7753 mov eax, dword ptr [ebp+8]
015E7756 cmp eax, 7
015E7759 ja 015E7763
015E775B mov eax, dword ptr [ecx+eax*4+0x30]
```

因此至少在 `0x16/0xAD` 分支中，context 是**队伍成员槽位 0..7**。`0xAD` 的两种提示由
成员对象的附加状态选择；那个状态字段的业务名没有独立证据，保持 Unknown。

文案来自 A21 `dstr.dat`，SHA-256
`60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`；FILE 资源 102/104，
`SC01 + u32 length + rolling-XOR` 解密后按 UTF-8 读取。日文对应文本为：

```text
0xBFA5  %sさんがダンジョン進入要求疲労度(%d)不足のため、該当ダンジョンに入場できません。
0x1138A %sさんの入場権限が足りなくて、該当ダンジョンに入場できません。
0x697F  %sさんが入場可能回数を超過しているため、該当ダンジョンに入場できません。
```

证据级别：switch 目标、槽位寻址、文案 ID 为 A21 静态走图（等级 4）；文本为同一 A21
资源文件的静态解密。是否在真实拒绝时实际显示仍缺 A21 实机拒绝差分，不升级为
`ClientVerified`。

## `NOTI 0x0005 DUNGEON_PERMISSION`

handler RVA `0x00D45E40`（VA `0x01145E40`）先读计数：

```asm
00D45E46 lea eax, [ebp-0xC]
00D45E51 call 023C52A0                ; ReadU16(count)
00D45E5C cmp ax, -1
00D45E60 jne 00D45EB5
...
00D45EB0 xor eax, eax
00D45EB2 mov dword ptr [ebp-0xC], eax ; 0xFFFF 变成本地循环次数 0
```

正常循环每项：

```asm
00D45ED0 lea edx, [ebp-8]
00D45ED4 call 023C5310                ; ReadU32(dungeonId)
00D45ED9 lea eax, [ebp-1]
00D45EDF call 023C5230                ; ReadU8(clearState)
```

所以普通 body 长度是 `2 + count * 5`。当前服务端
`StateSynchronizationPayloadCodec.Entry.cs:460-465` 写的是 `u16 dungeonId + u8 state`，
会使非空 A21 列表从第一项之后错位；现有捕获只有 `00 00` 空列表，不能用空样本验证条目宽度，
条目宽度由 handler 的直接读指令确认。

### clearState 的消费

每项先读取旧的有效状态；只有上升时触发 UI 更新：

```asm
00D45F2D push dword ptr [ebp-8]       ; dungeonId
00D45F31 call 00CCDEE0                ; effectiveState(dungeonId)
00D45F36 movzx edx, byte ptr [ebp-1]  ; new clearState
00D45F3D cmp edx, eax
00D45F3F jle 00D45F7D                 ; new <= old：跳过 UI 上升通知
...
00D45F58 movzx ecx, byte ptr [ebp-1]
00D45F5F push ecx
00D45F60 push edx                     ; dungeonId
...
00D45F78 call 01132AC0                ; UI/controller 更新

00D45F7D movzx eax, byte ptr [ebp-1]
00D45F84 push eax                     ; 原始 clearState
00D45F85 push ecx                     ; dungeonId
00D45F86 call 00CCDD40                ; 无论是否上升都写映射
```

有效 getter RVA `0x00CCDEE0` 对特定 GM/副本属性分支返回副本定义的 `[+0x89C]`；普通分支
查询全局映射 `0x03A39124`，缺项返回 0，否则返回映射值。选择模块不作偏移转换，直接比较：

```asm
02331118 mov eax, dword ptr [edi+0x3E0] ; 当前 dungeonId
02331125 call 00CCDEE0                  ; effectiveState
0233112D cmp eax, dword ptr [edi+0x3E4] ; selectedDifficulty
02331133 jge 02331145                   ; state >= selected 时允许该选择路径
```

另一路普通发送调用点 RVA `0x0233AC8C` 从所选记录 `[record+8]` 取 difficulty，随后在
`0x0233AC95` 调用 `sendSelectDungeonPacket`；发送函数将该值原样写为 offset 4 的 `u8`。
这些指令支持“clearState/effectiveState 是客户端选择难度的上界序数”，但仍有三项未知：

- 数值 0..4 各自对应的玩家可见难度名没有在本次静态路径中确认；
- 特殊 dungeon metadata `[+0x89C]` 的来源和值域没有在本次任务中穷举；
- 客户端 UI 门只能说明它通常不发送更高难度，不能证明原版服务端是否还检查角色等级、
  任务、队长、物品、疲劳或每日次数。

证据级别：条目宽度、映射写入、比较方向、无 `+1/-1` 为 A21 客户端静态走图（等级 4）；
空列表 `00 00` 为归档线上字节（等级 2），只佐证 count 的零值形状。

## 建议的最小后续测试

1. 在正确 5B 条目的隔离夹具或后续 #102 修复版上，用中转捕获一次**非空** `NOTI 0x0005`，
   保存单帧 body；至少包含两个 dungeonId。当前服务端的 3B 条目不能用于有效权限差分。
2. 在隔离服务端依次发送同一 dungeonId 的 state `0,1,2,4`，只操作原生难度 UI，记录可选
   档位和最终 `0x0010` offset 4；这可确定数值到难度名的映射以及是否存在显示层偏移。
3. 对 `CMD 0x0010` 拒绝体固定 `result=0`，分别改变 errorCode/context，记录客户端提示；
   这用于命名错误码与 context，不能由当前上游表替代。
4. 服务端 decoder 修正后，以归档 15B 样本断言 `dungeonId=10005`、`difficulty=0`，并用
   `dungeonId > 65535` 的合成 body 防止再次退回 `u16`。

## 复核工具

- `disasm_a21.py`：对 RVA 索引转储按范围反汇编，同时打印 RVA/VA。
- `xrefs_a21.py`：枚举目标 RVA 的直接 `rel32 call/jmp`，用于确认普通发送函数的调用点。

二者只读取输入转储，不写客户端目录，也不启动进程。

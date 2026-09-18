# A21 任务、修理、耐久与任务快照静态复核（2026-09-18）

## 基线与判据

- 客户端：`E:/Project/DNF/DNFClient-A21-protocol/DNF.exe`，SHA-256
  `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- 证据：同版本 `dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`；
  `manifest.json` 报 image base `0x00400000`、`hole_bytes=0`。以下地址都是 RVA，
  可直接作该文件偏移。用 x86 Capstone 按函数入口重新反汇编；没有从当前服务端源码
  推断客户端读法。
- 原语在该 dump 上逐条核验：`0x0234D2E0` 写 1 字节，`0x0234D310` 写 2 字节；
  `0x023C5230` 读 1 字节，`0x023C52A0` 读 2 字节，`0x023C5310` 读 4 字节。
  每个函数的 `inc/add`、剩余长度扣减与目标地址的 `byte/word/dword` 写一致。

本页证明**静态可达路径的字节读写**，不证明实际游戏已走该分支、服务端已发同形帧，
也不单独证明 UI 表现。动态往返仍须 A21 帧与画面记录。

## `CMD 0x0020 GIVEUP_QUEST`

客户端构包函数 `0x00C7EDE0`：`0x00C7EDEC..0x00C7EDF7` 建
`type=0x0020` 信封；`0x00C7EDFC` 压入常数 `0x20` 并于 `0x00C7EE05`
调用 2 字节写入；`0x00C7EE0A` 取参数 questId 并于 `0x00C7EE15`
再次调用 2 字节写入。静态直线路径的 body 恰为
`u16 0x0020 | u16 questId`，共 4 字节，前缀不是由服务端名称反推的。

关键指令（RVA；`0x0234D310` 是逐条核验的 WriteU16）：

```asm
00C7EDFC push 0x20
00C7EE05 call 0x0234D310
00C7EE0A mov eax, dword ptr [ebp + 8]
00C7EE0D push eax
00C7EE15 call 0x0234D310
```

成功响应 handler `0x00D29A90` 先检查由 CMD 接收层交来的成功标志
（`0x00D29AA3`）；成功后 `0x00D29ABF` 只再读一个 `u16`，随后把值
交给任务管理链。即成功 body 的静态读法是 `01 | u16 questId`。
失败分支 `0x00D29C81` 检查 reason `0x12`、`0x14`、`0x7E`；
其余 reason 直接落回（例如本仓/上游的 `0x13` 并无专用弹窗分支）。
这些是 A21 静态 Confirmed；放弃动作的真实请求/响应与任务 UI 仍未捕获。

## `CMD 0x0022 FINISH_QUEST`：同宽度，不同末字段

两处构包器都产出 10 字节，但**取值并不一致**：

| 构包路径 | `u16` 顺序（由 A21 WriteU16 指令读出） | 末字段 |
|---|---|---|
| `0x00C7ED10`，构包点 `0x00C7ED27` | `0x0022, arg1, arg2, arg3, 0xFFFF` | 恒定 `0xFFFF` |
| `0x00B4D307`，构包点 `0x00B4D312` | `0x0022, [ebx], 0xFFFF, 1, u16(edi)` | `edi` 取自 `0x012EF580`，经 `test edi,edi; jle`，能发包时源值为正数，线上的低 16 位不恒定 |

第二条路径的关键指令：

```asm
00B4D2FC call 0x012EF580
00B4D301 mov edi, eax
00B4D303 test edi, edi
00B4D305 jle 0x00B4D366
00B4D317 push 0x22
00B4D320 call 0x0234D310   ; WriteU16
00B4D354 push edi
00B4D35C call 0x0234D310   ; 最末的 WriteU16
```

因此客户端静态可达路径**推翻“offset 8 永远为 FFFF”**，但不证明这条路径在
当前任务中已实际触发，也不证明 `edi` 的业务意义。当前服务端
`FinishQuestPayloadCodec.Decode` 对 offset 8 强制要求 `0xFFFF`，会拒绝该路径产生的
非 `0xFFFF` 取值；
上游解析器也要求 `0xFFFF`，说明这一段不能再把上游或当前实现当客户端真相。
尚需运行时确认触发任务、具体末值与客户端期待的回复；本轮不改服务端业务。

## `CMD 0x0017 REPAIR_EQUIPMENT`

客户端至少有两种构包形状，不可把本仓的固定 8 字节 decoder 说成该 type 的全集：

| 构包点 | 可见写入 | 静态长度 | 边界 |
|---|---|---:|---|
| `0x01FEC800` 的 `0x01FEC815` 分支及 `0x01FEC89E` 分支 | `u8 0; u16 slot-or-FFFF; u16 material-or-FFFF; u16 0; u8 flag` | 8 | 当前普通 NPC 修理 decoder 接受此形状；flag 的业务意义未完成验证 |
| `0x0103FC10` 的 `0x0103FC7A` 分支 | `u8 0; u16 FFFF; u16 FFFF; u16 1` | 7 | 客户端真实可构造的另一形状；当前固定 8 字节 decoder 会拒绝，触发条件/语义尚待运行时取证 |

成功响应 handler `0x00D22C40` 在成功标志为非零时直接调用
`ReadU32(0x00D22C82)`、`ReadU8(0x00D22C90)`、
`ReadU16(0x00D22C9C)`、`ReadU16(0x00D22CA8)`。
故当前可见成功路径消费 `01 | u32 | u8 | u16 | u16`，共 10 字节；
其中 `u8` 和首个 `u16` 参与容器/槽位分支，第二个 `u16` 被拿来与零比较。
`u32` 很可能是修后钱包值，但仅凭这一段调用不能把语义标为 Confirmed。
失败分支 `0x00D231FB` 对 reason `0x0A` 有专门消息路径，另见
`0x11/0x13/0x15/0x16/0x7E` 等分支。没有成对原始帧，不能把本仓
对所有拒绝统一回 `0x0A` 的策略视为客户端协议事实。

## `CMD 0x0030 DECREASE_DURABILITY`

构包函数 `0x0126FE00` 在 `0x0126FE11` 建 `type=0x0030` 信封，
`0x0126FE16..0x0126FE25` 从角色 `+0x60C` 取低字节、写一个 `u8`。
成功响应 handler `0x00D06AB0` 在 `0x00D06AB6` 查成功标志，
然后仅于 `0x00D06ACB` 读一个 `u8`，把它交给穿戴对象查询。
静态可见的往返形状为请求 `u8 slot`、成功回复 `01 | u8 slot`；
这个 slot 语义由对象查询支持为 High，但当前尚缺真实成对帧及耐久 UI 对照。

```asm
0126FE16 movzx eax, byte ptr [esi + 0x60C]
0126FE1D push eax
0126FE25 call 0x0234D2E0   ; WriteU8
00D06ACB call 0x023C5230   ; ReadU8
```

## `NOTI 0x023F ACCEPTED_QUEST`

handler `0x00D62440` 于 `0x00D62451` 读 `u32 count`，
`0x00D62470..0x00D624B2` 循环 `count` 次，依次于
`0x00D62477` 读 `u16 questId`、`0x00D62483` 读 `u32 triggerValue`。
每项经 `0x00D624A7` 交给任务状态对象；`questId=0xFFFF` 会跳过该应用调用。
循环后还有 UI/任务管理的间接调用，但这条 handler 直线路径没有额外读取原语。
因此静态强证据支持 `u32 count + count × (u16,u32)`，长度 `4+6n`；
间接调用不能在没有动态读取 trace 时机械排除，所以不改生成器的“部分可得”机器裁决。
目前亦无本通知的 A21 原始帧/读取 trace，不能把当前服务端的整份快照推送
升级为 `ClientVerified`。

```asm
00D62451 call 0x023C5310   ; ReadU32(count)
00D62477 call 0x023C52A0   ; ReadU16(questId)
00D62483 call 0x023C5310   ; ReadU32(triggerValue)
00D62493 cmp ax, cx         ; cx = 0xFFFF
00D62496 je 0x00D624AC     ; skip apply
00D624B2 jl 0x00D62470     ; repeat until count
```

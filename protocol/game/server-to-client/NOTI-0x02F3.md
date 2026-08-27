# NOTI `0x02F3` `ADVANCE_ALTAR_SPAWN_UNIT`

<!-- rks:machine:begin -->

> **本区由静态分析自动生成，重新运行会整体覆盖；请勿手工编辑。**
> 需要修正字段含义时改 `patterns/data/meanings/*.tsv`，需要修正布局请改提取器。
> 本区之外的一切内容属于人工区，重新生成不会触碰。

## 身份

| 项 | 值 | 来源 |
|---|---|---|
| Endpoint | Game | 帧协议基线 |
| Direction | Server → Client | 接收注册表普查 |
| Command 族 | `0x00`（NOTI） | 客户端两张注册表的分表证据 |
| Type | `0x02F3` | 同上 |
| 名称 | `RAID_OTHER_CHANNEL_USER_PVP_SCORE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 70 + n×74   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xf]` |
| 1 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x20]` |
| 3 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x38]` |
| 7 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x68]` |
| 11 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x64]` |
| 15 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x48]` |
| 19 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x44]` |
| 23 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x40]` |
| 27 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x60]` |
| 31 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x5c]` |
| 35 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x58]` |
| 39 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x54]` |
| 43 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x50]` |
| 47 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x4c]` |
| 51 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x3c]` |
| 55 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x28]` |
| 59 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x2c]` |
| 63 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x24]` |
| 65 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x28]` |
| 69 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xe]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 4]` |
| &nbsp;&nbsp;↳&nbsp;1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |
| &nbsp;&nbsp;↳&nbsp;2 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x110]` |
| &nbsp;&nbsp;↳&nbsp;3 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x19]` |
| &nbsp;&nbsp;↳&nbsp;4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xb8]` |
| &nbsp;&nbsp;↳&nbsp;8 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x100]` |
| &nbsp;&nbsp;↳&nbsp;12 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xd]` |
| &nbsp;&nbsp;↳&nbsp;13 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xe8]` |
| &nbsp;&nbsp;↳&nbsp;14 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xe4]` |
| &nbsp;&nbsp;↳&nbsp;15 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xe0]` |
| &nbsp;&nbsp;↳&nbsp;19 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xd4]` |
| &nbsp;&nbsp;↳&nbsp;23 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xd0]` |
| &nbsp;&nbsp;↳&nbsp;27 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xd8]` |
| &nbsp;&nbsp;↳&nbsp;31 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x108]` |
| &nbsp;&nbsp;↳&nbsp;35 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x104]` |
| &nbsp;&nbsp;↳&nbsp;39 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xfc]` |
| &nbsp;&nbsp;↳&nbsp;43 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xf8]` |
| &nbsp;&nbsp;↳&nbsp;47 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xf4]` |
| &nbsp;&nbsp;↳&nbsp;51 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xf0]` |
| &nbsp;&nbsp;↳&nbsp;55 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xec]` |
| &nbsp;&nbsp;↳&nbsp;59 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x15]` |
| &nbsp;&nbsp;↳&nbsp;60 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1a]` |
| &nbsp;&nbsp;↳&nbsp;61 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x10]` |
| &nbsp;&nbsp;↳&nbsp;62 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |
| &nbsp;&nbsp;↳&nbsp;66 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |
| &nbsp;&nbsp;↳&nbsp;70 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x023440C0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

# NOTI `0x02C3` `AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM`

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
| Type | `0x02C3` | 同上 |
| 名称 | `RENT_GUILD_SUPPORT_CHARAC` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 65 + n1×4 + n2×4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x5e]` |
| 1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x5e]` |
| 2 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x94]` |
| 4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x9c]` |
| 8 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |
| 9 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |
| 10 | 1 | `u8` | unknown | — | 接收指令，取自 `[esi + 4]` |
| 11 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x80]` |
| 13 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x13e]` |
| 14 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x140]` |
| 15 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x148]` |
| 19 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x184]` |
| 23 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x194]` |
| 24 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x1b8]` |
| 26 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14c]` |
| 30 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x17c]` |
| 34 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x170]` |
| 35 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x13f]` |
| 36 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x190]` |
| 38 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14c]` |
| 42 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x180]` |
| 46 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x141]` |
| 47 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1a4]` |
| 48 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x143]` |
| 49 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x19c]` |
| 51 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x142]` |
| 52 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1c0]` |
| 53 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1a8]` |
| 54 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1a0]` |
| 55 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x198]` |
| 56 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x188]` |
| 57 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x5d]` |
| 58 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x6a]` |
| 59 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |
| 60 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp + 8]` |
| 64 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x61]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x69]` |
| &nbsp;&nbsp;↳&nbsp;1 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x88]` |
| &nbsp;&nbsp;↳&nbsp;3 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x5f]` |
| **重复组 g2 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 4]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x012159D0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

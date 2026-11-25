# NOTI `0x01F9` `INTEGRATE_MATCHING_PLAYER_UID_LIST`

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
| Type | `0x01F9` | 同上 |
| 名称 | `INTEGRATE_MATCHING_PLAYER_UID_LIST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 23 + n×2   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x361]` |
| 1 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x398]` |
| 5 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x356]` |
| 6 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 4]` |
| 8 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x352]` |
| 9 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x350]` |
| 13 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x358]` |
| 14 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x2f8]` |
| 18 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x355]` |
| 19 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x390]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x359]` |
| &nbsp;&nbsp;↳&nbsp;1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp + esi - 0x26c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00E11220` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

**ClientStatic / ThirdPartyReference。** 匹配对阵面板读取 `u8 group + u32 room + u8 n`，1vs1 每项 `u16 UID + u8 side + u32 value + u8 grade + u32 RP + u8 value`。读取路径为 RVA `00E1124E–00E113A8`。先投影双方角色和 P2P 端点，客户端随后原样回报 `room` 至 [CMD 0x023B](../client-to-server/0x023B.md)。

**CurrentImplementation（修正前）。** 两席按参照形状使用阵营 1 和 2，`u32 value=1`，末尾 `u8 value=0`。段位读现有角色战绩。RP 使用当前赛季正式 RP，定级完成前填 0。A21 正式 RP 显示待实机。

普通 3v3 按同一名单形状发送六条成员记录，阵营 1、2 各三名。A21 六席显示待实机验证。

## A21 入场页「战斗力」

**ClientStatic / Confirmed。** 匹配组 1 的每名选手条目偏移 3 处首个 `u32` 由 RVA `00E11359–00E11360` 读取，`00E1115D–00E11162` 传给对阵页选手数据，随后经 `00E111CA` 交给界面。旧服务端在此写常数 1。CH81 实机截图里两边均显示 1，排查时两账号的正式 GP 分别为 1080、920。

**CurrentImplementation。** 此处填入配对时的正式分数，普通匹配取角色 RP，巅峰王者战取账号 GP，定级未完成时填 0。后一个 `u32` 沿用现有分数投影，帧长与匹配流程保持原样。

**ClientVerified，2026-11-26。** CH81 00:15:44 在修正版上完成配对，用户确认双方 VS 页战斗力显示已恢复正常。该场数据库记录的赛前 GP 为 1086 和 914，数值取自服务端结算记录。

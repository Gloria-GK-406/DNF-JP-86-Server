# NOTI `0x002B` `PVP_SEAT_STATE`

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
| Type | `0x002B` | 同上 |
| 名称 | `PVP_SEAT_STATE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |
| 2 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 3]` |
| 3 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 2]` |
| 4 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x1c]` |
| 5 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 4]` |
| 6 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x1c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D65950` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 2026-11-14 决斗场一期实现（TASK-06，CurrentImplementation，待实机）

`u16 房间号, u8 模式, u8 n`，再 `n × (u8 座位, u8 state, u16 uid)`；state 同 [NOTI-0x0029](NOTI-0x0029.md)（上游
`BuildSeatStateBody`/`BuildOccupiedSeatStatesBody`，静态下界 8 字节一致，High）。座位的占用者、阵营或开关状态变化时发给同端口
大厅就绪者；模式变化时列出全部占用座位；房间销毁前把最后的占用座位发成空座。准备标志不在其中。

实机前不标 ClientVerified；无 `.hex` 样本，布局来自上游转录与静态走图，字节由编码/解码测试固定。

## 2026-11-14 对局期间的座位移除（TASK-07，CurrentImplementation，待实机）

房间处于对局阶段（Loading 之后到回等待之前）时有成员离场，同端口大厅立即收到该座位的 `0x002B`（房主继任另有 `0x002A`），
房内其余成员不收，改记为"欠帧"：客户端结果界面结束前仍引用对局角色（上游 `PvpRoomHandler.Departures` 注释），上游同样按接收者
延后。欠帧在以下时刻以"当时房间"补发（一帧列全部 8 个座位的 `0x002B` → `0x002A`）：该成员的 `0x0039` 被领域接受、下一局开局
（排在 `0x002C`/`0x002D` 之前）、该成员退房或被踢（排在其回城与本次房间帧之后）。该成员会话结束、房间销毁时丢弃；确认超时回等待
不补发（Spec RQ-035）。Fighting 中离场也延后，依据同上（上游 `deferCombatDeparture` 按房间处于开战状态判定）。

实机前不标 ClientVerified。

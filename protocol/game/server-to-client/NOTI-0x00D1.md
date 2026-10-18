# NOTI `0x00D1` `BLOOD_DUNGEON_STATE_REWARD`

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
| Type | `0x00D1` | 同上 |
| 名称 | `BLOOD_DUNGEON_STATE_REWARD` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 2 + n×9   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 平坦字段（重复组之前的 u8）：本次已完成的轮数 currentRound。重复组内同偏移字段的含义未知：上游 BuildReward 的循环体只写 2 个 int32（无组内 u8），与本协议提取到的 9 字节组不吻合，故不为组内字段下结论 | Confirmed | 上游 BloodAltarPacketBuilder.cs:114 BuildReward 写 (byte)currentRound；调用点 BloodAltarDungeonCoordinator.cs:748 传 plan.CompletedRounds；组结构不吻合见 BloodAltarPacketBuilder.cs:119-138，取自 `[ebp - 2]` |
| 1 | 1 | `u8` | 平坦字段（重复组之前的 u8）：本副本的最大轮数 maxRound。重复组内同偏移字段的含义未知，理由同 offset 0 | Confirmed | 上游 BloodAltarPacketBuilder.cs:115 写 (byte)maxRound；调用点 BloodAltarDungeonCoordinator.cs:749 传 plan.MaxRounds；组结构不吻合见 BloodAltarPacketBuilder.cs:119-138，取自 `[ebp - 2]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |
| &nbsp;&nbsp;↳&nbsp;1 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |
| &nbsp;&nbsp;↳&nbsp;5 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 8]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D48A60` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-10-13 祭坛A21复核

校正自动区9B重复组：completed/max各u8，之后固定四参与者槽，每槽count u8+n×(itemId i32,count i32)，行宽8B。空槽也写count0，金币ID0、空卡ID-1。A21 RVA D48A60。

ClientStatic / CurrentImplementation，尚无本轮WireCaptured或ClientVerified。本人工结论覆盖旧自动区冲突推断。[查证、顺序和边界](../../../Document/tasks/altar-dungeons/research-and-implementation.md)。

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

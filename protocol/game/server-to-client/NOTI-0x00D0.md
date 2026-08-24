# NOTI `0x00D0` `BLOOD_DUNGEON_STATE_RANKING`

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
| Type | `0x00D0` | 同上 |
| 名称 | `BLOOD_DUNGEON_STATE_RANKING` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 24   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 本次血之祭坛通关用时，毫秒 | Confirmed | 上游 BloodAltarPacketBuilder.cs:99 BuildRanking 第一个 WriteUInt32 写 playTimeMilliseconds；调用点 BloodAltarDungeonCoordinator.cs:636 传 plan.ClearTimeMilliseconds，取自 `[ebp - 4]` |
| 4 | 4 | `u32` | 本次已完成的轮数 currentRound | Confirmed | 上游 BloodAltarPacketBuilder.cs:100 写 currentRound；调用点 BloodAltarDungeonCoordinator.cs:637 传 plan.CompletedRounds，取自 `[ebp - 0x18]` |
| 8 | 4 | `u32` | 历史最佳用时槽位 bestTimeMilliseconds，毫秒；上游没有历史记录，直接回填本次通关用时 | Confirmed | 上游 BloodAltarPacketBuilder.cs:101 写 bestTimeMilliseconds；调用点 BloodAltarDungeonCoordinator.cs:638 第二次传同一个 plan.ClearTimeMilliseconds，取自 `[ebp - 0xc]` |
| 12 | 4 | `u32` | 历史最佳轮数槽位 bestRound；上游没有历史记录，直接回填本次完成轮数 | Confirmed | 上游 BloodAltarPacketBuilder.cs:102 写 bestRound；调用点 BloodAltarDungeonCoordinator.cs:639 第二次传同一个 plan.CompletedRounds，取自 `[ebp - 0x10]` |
| 16 | 4 | `u32` | 本副本的最大轮数 maxRound | Confirmed | 上游 BloodAltarPacketBuilder.cs:103 写 maxRound；调用点 BloodAltarDungeonCoordinator.cs:640 传 plan.MaxRounds，取自 `[ebp - 8]` |
| 20 | 4 | `u32` | 本次结算发放的经验值 rewardExperience | Confirmed | 上游 BloodAltarPacketBuilder.cs:104 写 rewardExperience；调用点 BloodAltarDungeonCoordinator.cs:641 传 plan.RewardExperience，取自 `[ebp - 4]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D47070` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

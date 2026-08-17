# NOTI `0x027A` `RAID_BUFF_SYSTEM`

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
| Type | `0x027A` | 同上 |
| 名称 | `RAID_BUFF_SYSTEM` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 15   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | buff 组数：其后按组重复，每组为 u8 buff 类型 + u8 条目数 + 条目数 x 12 字节条目 | Confirmed | 上游 Network/Builders/Raid/RaidPacketBuilder.cs:409 WriteByte((byte)groups.Count)，组结构见 :414-424，取自 `[ebp - 0xe]` |
| 1 | 1 | `u8` | 第 1 组的 buff 类型 BuffType；上游按客户端 buff 类型表 AntonClientBuffTypes 逐类型各建一组 | High | 上游 RaidPacketBuilder.cs:414 WriteByte(group.BuffType)；组的产生见 Network/Handlers/RaidHandler.Situation.cs:212-216 与 :238-256，取自 `[ebp - 0xf]` |
| 2 | 1 | `u8` | 第 1 组的条目数：该 buff 类型当前生效中的条目个数；无生效活动时为 0（空数组） | High | 上游 RaidPacketBuilder.cs:415 WriteByte((byte)group.Entries.Count)；无活动时 Entries 为空见 Network/Handlers/RaidHandler.Situation.cs:215 与 :245，取自 `[ebp - 0xd]` |
| 3 | 2 | `u16` | 第 1 组第 1 条的队伍序号 PartyIndex | High | 上游 RaidPacketBuilder.cs:420 WriteUInt16(entry.PartyIndex)；赋值见 Network/Handlers/RaidHandler.Situation.cs:249 PartyIndex = value2.PartyIndex，取自 `[ebp - 0x14]` |
| 5 | 2 | `u16` | 第 1 组第 1 条的施放者 UserId | High | 上游 RaidPacketBuilder.cs:421 WriteUInt16(entry.UserId)；赋值见 Network/Handlers/RaidHandler.Situation.cs:250 UserId = value2.UserId，取自 `[ebp - 0x18]` |
| 7 | 4 | `u32` | 第 1 组第 1 条的 buff 生效截止 Unix 时间戳 ActiveUntilTimestamp | High | 上游 RaidPacketBuilder.cs:422 WriteUInt32(entry.ActiveUntilTimestamp)；赋值见 Network/Handlers/RaidHandler.Situation.cs:251，取自 `[ebp - 0x24]` |
| 11 | 4 | `u32` | 第 1 组第 1 条的冷却截止 Unix 时间戳 CooldownUntilTimestamp；上游在它不晚于当前时间时把该条活动整条移除 | High | 上游 RaidPacketBuilder.cs:423 WriteUInt32(entry.CooldownUntilTimestamp)；赋值见 Network/Handlers/RaidHandler.Situation.cs:252，过期移除见 :227-230，取自 `[ebp - 0x28]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0090B680` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

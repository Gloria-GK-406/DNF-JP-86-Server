# NOTI `0x027B` `RAID_MONSTER_HP`

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
| Type | `0x027B` | 同上 |
| 名称 | `RAID_MONSTER_HP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 11 + n×4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 小队(situation)条目数，其后按该数量重复小队块 | High | 上游 RaidHandler.Situation.cs:420 Build(0, 635=0x027B, RaidPacketBuilder.BuildRaidMonsterHp(statuses)) 为直接构造点；RaidPacketBuilder.cs:437 WriteByte((byte)dungeons.Count)，取自 `[ebp - 0xf]` |
| 1 | 2 | `u16` | 小队索引 SituationIndex，取自该组的 PartyIndex | High | 上游 RaidPacketBuilder.cs:443 WriteUInt16(dungeon.SituationIndex)；RaidHandler.Situation.cs:367 SituationIndex = group.PartyIndex，取自 `[ebp - 4]` |
| 3 | 1 | `u8` | 该小队成员数，决定其后连续 u16 成员 userId 的个数 | High | 上游 RaidPacketBuilder.cs:444 WriteByte((byte)dungeon.MemberIds.Count)，取自 `[ebp - 0x84]` |
| 4 | 2 | `u16` | 第 1 个成员的 userId（本布局固定段 11 字节对应成员数=1） | High | 上游 RaidPacketBuilder.cs:445-446 foreach memberId WriteUInt16；RaidHandler.Situation.cs:368 MemberIds = group.MemberKeys，取自 `[ebp - 0x18]` |
| 6 | 4 | `u32` | 该小队已消耗的复活币数 UsedCoinCount | High | 上游 RaidPacketBuilder.cs:447 WriteUInt32(dungeon.UsedCoinCount)；RaidHandler.Situation.cs:369 GetAntonReportedUsedCoinCount，取自 `[ebp - 0x2c]` |
| 10 | 1 | `u8` | 尾部运行时数值个数，即 11+n×4 中的 n | High | 上游 RaidPacketBuilder.cs:448 WriteByte((byte)dungeon.RuntimeValues.Count)；:449-450 随后写 n 个 u32，取自 `[ebp - 0xe]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | 该小队的一个运行时数值 RuntimeValues[i]（本包名为 RAID_MONSTER_HP；上游把客户端上报的同名 CMD 数值缓存后原样广播） | High | 上游 RaidPacketBuilder.cs:449-450 foreach runtimeValue WriteUInt32；RaidHandler.Situation.cs:370-372 RuntimeValues 取自 _raidMonsterRuntimeValues 缓存；:168 该缓存由 RAID_MONSTER_HP 请求写入，取自 `[ebp - 0x30]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0090B9B0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |

NOTI 的 body **没有**结果信封，整个 body 归 handler——这与 CMD 相反，见 [game-receive](../../patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。

支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

尚无实机样本。取得后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，并在此说明产生它的客户端动作。

## 服务端实现映射

尚未实现。

## 待实机补全

- [ ] 触发时机（什么玩家动作/服务端状态导致它出现）
- [ ] 响应流程（是否有响应、响应身份与顺序）
- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->

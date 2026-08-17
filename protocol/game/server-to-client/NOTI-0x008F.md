# NOTI `0x008F` `START_DEATH_TOWER_MAP`

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
| Type | `0x008F` | 同上 |
| 名称 | `START_DEATH_TOWER_MAP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 23   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | 当前层数，1-based（= tower.CurrentStage + 1） | Confirmed | 上游 DeathTowerPacketBuilder.cs:42 `w.WriteUInt16((ushort)(tower.CurrentStage + 1))`，取自 `[ebp - 0x40]` |
| 2 | 4 | `u32` | 本层随机种子 randomSeed | Confirmed | 上游 DeathTowerPacketBuilder.cs:43 `w.WriteUInt32(randomSeed)`；调用点 DeathTowerCoordinator.cs:1521-1524 传 stageSeed，同一值写入 tower.BeginStage 与房间 RoomState.Seed（:1604），取自 `[ebp - 0x40]` |
| 6 | 2 | `u16` | 本层地图 id | Confirmed | 上游 DeathTowerPacketBuilder.cs:44 `w.WriteUInt16((ushort)tower.GetCurrentMapId())`，取自 `[ebp - 0x44]` |
| 8 | 1 | `u8` | 本层怪物条目数 n（上限 255） | Confirmed | 上游 DeathTowerPacketBuilder.cs:45 `w.WriteByte((byte)monsterCount)`，:38 monsterCount = Min(monsters.Count, byte.MaxValue)，取自 `[ebp - 0x10]` |
| 9 | 4 | `u32` | 第 1 条怪物条目的 APC 列表索引 ListIndex（怪物条目 14B/条、共 n 条，本表只覆盖第 1 条） | Confirmed | 上游 DeathTowerPacketBuilder.cs:48-51 循环体首个 `w.WriteUInt32((uint)m.ListIndex)`；DeathTowerCoordinator.cs:1558 把同值映射为战斗侧 MonsterSumInfo.TemplateOrder，取自 `[ebp - 0x12]` |
| 13 | 2 | `u16` | 第 1 条怪物条目的怪物实例唯一 id MonsterUniqueId（战斗侧的 sequence id） | Confirmed | 上游 DeathTowerPacketBuilder.cs:52 `w.WriteUInt16(m.MonsterUniqueId)`；DeathTowerCoordinator.cs:1559 映射为 MonsterSumInfo.PacketIndex，:1576-1578 首只的该值作为房间 firstSequence，取自 `[ebp - 0x24]` |
| 15 | 4 | `u32` | 第 1 条怪物条目的怪物模板 id MonsterIndex | Confirmed | 上游 DeathTowerPacketBuilder.cs:53 `w.WriteUInt32((uint)m.MonsterIndex)`；DeathTowerCoordinator.cs:1554 映射为 MonsterSumInfo.Code，取自 `[ebp - 0x18]` |
| 19 | 1 | `u8` | 第 1 条怪物条目的怪物等级 MonsterLevel | Confirmed | 上游 DeathTowerPacketBuilder.cs:54 `w.WriteByte(m.MonsterLevel)`；DeathTowerCoordinator.cs:1555 映射为 MonsterSumInfo.Level，取自 `[ebp - 0xe]` |
| 20 | 1 | `u8` | 第 1 条怪物条目的怪物类型 MonsterType | Confirmed | 上游 DeathTowerPacketBuilder.cs:55 `w.WriteByte(m.MonsterType)`；DeathTowerCoordinator.cs:1556 映射为 MonsterSumInfo.Type，取自 `[ebp - 0xd]` |
| 21 | 1 | `u8` | 第 1 条怪物条目的是否箱子怪 IsBoxMonster | Confirmed | 上游 DeathTowerPacketBuilder.cs:56 `w.WriteByte(m.IsBoxMonster)`；DeathTowerCoordinator.cs:1557 据此取反得战斗侧 IsBlocking = (IsBoxMonster == 0)，取自 `[ebp - 0x13]` |
| 22 | 1 | `u8` | 第 1 条怪物条目的箱子索引 BoxIndex | Confirmed | 上游 DeathTowerPacketBuilder.cs:57 `w.WriteByte(m.BoxIndex)`，字段定义见 Game/DeathTower/DeathTowerStageModels.cs:11，取自 `[ebp - 0x12]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00906BC0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

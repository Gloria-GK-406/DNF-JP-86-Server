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
已读出字段合计 = 25   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | 当前层数，1-based（= tower.CurrentStage + 1） | Confirmed | 上游 DeathTowerPacketBuilder.cs:42 `w.WriteUInt16((ushort)(tower.CurrentStage + 1))`，取自 `[ebp - 0x3c]` |
| 2 | 4 | `u32` | 本层随机种子 randomSeed | Confirmed | 上游 DeathTowerPacketBuilder.cs:43 `w.WriteUInt32(randomSeed)`；调用点 DeathTowerCoordinator.cs:1521-1524 传 stageSeed，同一值写入 tower.BeginStage 与房间 RoomState.Seed（:1604），取自 `[ebp - 0x3c]` |
| 6 | 4 | `u32` | 本层地图 id | Confirmed | 上游 DeathTowerPacketBuilder.cs:44 `w.WriteUInt16((ushort)tower.GetCurrentMapId())`，取自 `[ebp - 0x40]` |
| 10 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x10]` |
| 11 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x12]` |
| 15 | 2 | `u16` | 第 1 条怪物条目的怪物模板 id MonsterIndex | Confirmed | 上游 DeathTowerPacketBuilder.cs:53 `w.WriteUInt32((uint)m.MonsterIndex)`；DeathTowerCoordinator.cs:1554 映射为 MonsterSumInfo.Code，取自 `[ebp - 0x24]` |
| 17 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |
| 21 | 1 | `u8` | 第 1 条怪物条目的是否箱子怪 IsBoxMonster | Confirmed | 上游 DeathTowerPacketBuilder.cs:56 `w.WriteByte(m.IsBoxMonster)`；DeathTowerCoordinator.cs:1557 据此取反得战斗侧 IsBlocking = (IsBoxMonster == 0)，取自 `[ebp - 0xe]` |
| 22 | 1 | `u8` | 第 1 条怪物条目的箱子索引 BoxIndex | Confirmed | 上游 DeathTowerPacketBuilder.cs:57 `w.WriteByte(m.BoxIndex)`，字段定义见 Game/DeathTower/DeathTowerStageModels.cs:11，取自 `[ebp - 0xd]` |
| 23 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x13]` |
| 24 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x12]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D59850` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-10-03 静态补证与字段纠偏

ClientStatic / Confirmed：handler VA `01159850` 的完整使用路径为：

```text
u16 level + u32 seed + u32 mapId + u8 actorCount
actorCount × (u32 listIndex + u16 uid + u32 templateId + u8 level + u8 type + u8 box + u8 boxIndex)
u8 itemCount
itemCount × (u8 + u16 + u32 + u32 + u16 + u8 + u16 + u16)
```

演员行14B，物品行18B；无物品时body为 `12 + actorCount×14` B。mapId明确为u32。机器区部分表的字段命名来自对照源码，位置与含义不能直接作为完整布局：例如其offset15实际为UID，不是模板ID；模板ID从offset17开始。以本次镜像读取链为准，不能照抄对照源码中矛盾的WriteUInt16注释。

APC也走演员列表，类型5与任务目标类型2须分开。当前迷妄塔物品掉落参数为0，编码器只走itemCount=0；未实现或验证非零物品行语义。地图/演员内容、固定镜像身份和VA证据见 [研究报告](../../../Document/IllusionTowerResearch-20261003.md)。尚无本次实帧或ClientVerified。

## 2026-10-04 死亡之塔接入

CurrentImplementation / Confirmed：当前PVF死亡塔45张地图没有[item]预置行；怪物/APC的[death tower item]属于击杀掉落，通过NOTI0026发布，未向008F写入上游旧格式的APC物品表。008F的itemCount仍为0；非零物品行继续保留ClientStatic边界。

实现、证据与待实机项目见 [死亡之塔记录](../../../Document/DeathTowerImplementation-20261004.md)。尚未将死亡塔标为ClientVerified。

# NOTI `0x00D2` `BLOOD_MONSTER_SPAWN`

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
| Type | `0x00D2` | 同上 |
| 名称 | `BLOOD_MONSTER_SPAWN` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4 + n×17   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | 平坦字段（重复组之前的 u16）：本波怪物数量 n，等于 wave.Monsters.Count 截断到 ushort。重复组内同偏移的 u8：BloodAltarSpawn.Variant，上游恒写常量 19 | Confirmed | 上游 BloodAltarPacketBuilder.cs:40-41 BuildMonsterSpawn 先算 count 再 WriteUInt16((ushort)count)；组内 Variant 见 :45 写 spawn.Variant，取值见 BloodAltarDungeonRuntime.cs:92 DefaultMonsterVariant=19 与 :382 唯一赋值处，取自 `[ebp - 0x24]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| &nbsp;&nbsp;↳&nbsp;1 | 2 | `u16` | 怪物实例序列号 SequenceId（u16，运行时自增）；即客户端上报 DIE_BLOOD_MONSTER 时回传的 id | Confirmed | 上游 BloodAltarPacketBuilder.cs:46 写 spawn.SequenceId；BloodAltarDungeonRuntime.cs:383 以 _nextSequence++ 赋值；上游 DungeonCommandParser.cs:202-207 从死亡命令 body 逐个读 u16 sequence，BloodAltarDungeonCoordinator.cs:91-103 按此 id 结算击杀，取自 `[ebp - 0x20]` |
| &nbsp;&nbsp;↳&nbsp;3 | 4 | `u32` | 怪物代码 MonsterCode（u32） | Confirmed | 上游 BloodAltarPacketBuilder.cs:47 写 (uint)spawn.MonsterCode；取值见 BloodAltarDungeonRuntime.cs:384 scheduled.Monster.MonsterCode，取自 `[ebp - 8]` |
| &nbsp;&nbsp;↳&nbsp;7 | 1 | `u8` | 怪物类别 MonsterType；上游在最终地图的最后一轮波次写 3，其余一律写 0 | Confirmed | 上游 BloodAltarPacketBuilder.cs:48 写 spawn.MonsterType；取值见 BloodAltarDungeonRuntime.cs:385-388，取自 `[ebp - 2]` |
| &nbsp;&nbsp;↳&nbsp;8 | 1 | `u8` | 怪物等级 Level | Confirmed | 上游 BloodAltarPacketBuilder.cs:49 写 spawn.Level；取值见 BloodAltarDungeonRuntime.cs:389 Definition.BasisLevel，取自 `[ebp - 1]` |
| &nbsp;&nbsp;↳&nbsp;9 | 2 | `u16` | 怪物体型缩放 Scale（u16），单位为百分比，等于阶段 Scale × 100 四舍五入 | Confirmed | 上游 BloodAltarPacketBuilder.cs:50 写 spawn.Scale；取值见 BloodAltarDungeonRuntime.cs:390-391 Math.Round(scheduled.Phase.Scale * 100f)，取自 `[ebp - 0x1c]` |
| &nbsp;&nbsp;↳&nbsp;11 | 2 | `u16` | 怪物生成坐标 X（u16） | Confirmed | 上游 BloodAltarPacketBuilder.cs:51 写 spawn.X；取值见 BloodAltarDungeonRuntime.cs:392 scheduled.Monster.X，取自 `[ebp - 0x18]` |
| &nbsp;&nbsp;↳&nbsp;13 | 2 | `u16` | 怪物生成坐标 Y（u16） | Confirmed | 上游 BloodAltarPacketBuilder.cs:52 写 spawn.Y；取值见 BloodAltarDungeonRuntime.cs:393 scheduled.Monster.Y，取自 `[ebp - 0x14]` |
| &nbsp;&nbsp;↳&nbsp;15 | 2 | `u16` | 怪物生成坐标 Z（u16） | Confirmed | 上游 BloodAltarPacketBuilder.cs:53 写 spawn.Z；取值见 BloodAltarDungeonRuntime.cs:394 scheduled.Monster.Z，取自 `[ebp - 0x10]` |
| 2\* | 2 | `u16` | 重复组之后的尾部 u16：本波怪物的生成间隔毫秒；最终地图波固定写 0 | High | 上游 BloodAltarPacketBuilder.cs:55 在循环之后写 wave.TailValue；取值见 BloodAltarDungeonRuntime.cs:405-408 IsFinalMapWave ? 0 : Monster.SpawnIntervalMilliseconds，取自 `[ebp - 0x24]`。**位置为名义值**（按 n=0 排布） |

带 `*` 的偏移是**名义值**：偏移累加时**没有计入其前重复组的任何一次重复**，即按重复次数 **n = 0** 排布。真实位置 = 名义值 + Σ(该组宽度 × 实际重复次数)。实现时必须按计数字段累加，不能照抄这个数字。

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x008F5D10` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

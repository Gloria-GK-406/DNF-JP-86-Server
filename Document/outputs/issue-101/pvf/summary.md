# GH #101 PVF 普通地下城准入静态取证

- 归档：`E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`
- SHA-256：`19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`
- 文件长度：`72894573` 字节
- 口径：只读 `dungeon/dungeon.lst` 及其列出的 `.dgn`。这是 PVF 静态声明，不是原版服务端政策，也不证明客户端实际执行这些规则。

## 范围

- list 行：1047；归档内找到：1047。
- 当前 `PvfDungeonDefinitionSource` 形状可投影的 Maze 文件：346；无可投影 Maze：701。
- 声明 `[no fatigue]`：506 文件；声明 `[story mode]`：71 文件。
- `[quest connection]` 标签：984 次，其中 984 次符合当前读取器接受的 2/3 整数形状。
- 至少一个普通 Maze：340 文件；至少一个合法 `[quest connection]` Maze：105 文件；两者混合：99；仅任务连接 Maze：6。

`dungeons.tsv` 保留每个真实 dungeonId/path、声明等级、免疲劳、Maze 数、任务连接与难度相关标签。`MinimumLevelProjected` 与 `ConsumesFatigueProjected` 是当前读取器的投影值；其余字段尽量保留 PVF 原始声明。这里的 `ordinary` 不是 PVF 标签，而是按当前服务端规则定义为“可投影且没有合法 quest connection 的 Maze”。

## 当前读取器消费核对（基准 `28efa005fa5945d0c8d27de8076ac50ea83f2c7f`）

- `PvfDungeonDefinitionSource` 读取 `[minimum required level]`、`[no fatigue]`、Maze 结构和 `[quest connection]`。缺少等级时投影为 1；缺少 `[no fatigue]` 时投影为消耗疲劳。
- 生产代码没有读取 `DungeonDefinition.MinimumRequiredLevel` 或 `ConsumesFatigue`；前者目前不形成等级拒绝，后者也尚未改变固定的入场疲劳报告。不能把“当前服务端未消费”写成“PVF 不存在”。
- `DungeonLayoutRuleService` 消费 Maze/任务连接：交互式入口只选无连接 Maze，定向入口按任务 active/cleared 和连接第三整数的最低难度筛选。`[story mode]` 不参与这条边界。
- `[difficulty]`、`[designate dungeon difficulty]`、`[difficulty level]`、`[free difficulty]`、`[event dungeon difficulty]` 均未由 `PvfDungeonDefinitionSource` 读取；这些静态声明本身不足以推出原版服务端的难度开放政策。

## 难度与疲劳相关声明分布

- `[minimum required level]`：1042 文件。
- `[no fatigue]`：506 文件；`[enter without fatigue]`：163 文件；`[use fatigue only start dungeon]`：52 文件。这三种标签并存，不能仅凭英文名互相替换语义。
- `[difficulty]`：385 文件（文件值分布：`0`×11、`1`×338、`2`×29、`3`×6、一个文件重复 `1;1`）。
- `[designate dungeon difficulty]`：217 文件；`[difficulty level]`：107；`[free difficulty]`：7；`[event dungeon difficulty]`：5。
- 留在可投影 Maze/文件级连接上的第三整数（当前读取器解释为最低难度）分布：`-1`×887、`0`×62、`1`×24、`2`×7、`4`×2，共 982 条；另 2 条位于无房间、会被读取器丢弃的 Maze。该整数只约束任务连接 Maze，不等同于地下城整体难度开放。

## 建议实机样本：普通与等级边界

| dungeonId | path | name | minLevel | noFatigue | mazeCount | ordinary/quest | difficulty | designate | questConnection | special/required |
|---:|---|---|---:|---:|---:|---:|---|---|---|---|
| 144 | `dungeon/StormOfMetastasis/Abnoba/Redcrystal_Forest.dgn` | 炽晶森林 | 1 | 0 | 5 | 1/4 |  | 0 | m1:0,1776,-1;m2:0,1777,-1;m3:0,14873,-1;m4:0,14874,-1 |  |
| 145 | `dungeon/StormOfMetastasis/Abnoba/Icecrystal_Forest.dgn` | 冰晶森林 | 5 | 0 | 5 | 1/4 |  | 0 | m1:0,1780,-1;m2:0,1779,-1;m3:0,14875,-1;m4:0,14876,-1 |  |

`144` 是普通基准：交互式入口应落到 m0；定向任务入口可落到 m1-m4。`145` 的 `[minimum required level]` 为 5，可用于等级不足/恰好达标边界，但客户端普通入口是否展示并可选择仍需实机确认。

## 建议实机样本：免疲劳

| dungeonId | path | name | minLevel | noFatigue | mazeCount | ordinary/quest | difficulty | designate | questConnection | special/required |
|---:|---|---|---:|---:|---:|---:|---|---|---|---|
| 10000 | `dungeon/Tutorial/Tutorial.dgn` | 卡利丛林 | 1 | 1 | 1 | 1/0 |  |  |  |  |
| 10004 | `dungeon/Tutorial/Tutorial_knight.dgn` | 卡利丛林外围 | 1 | 1 | 1 | 1/0 |  |  |  |  |
| 100 | `dungeon/SouthernDale/Grimseeker.dgn` | 南部溪谷 | 27 | 1 | 1 | 1/0 |  |  |  | [event dungeon];required=2680738,1,1 |
| 5006 | `dungeon/DarkelfTemple/TempleEnterance_1.dgn` | 神殿入口 | 40 | 1 | 1 | 1/0 |  |  |  | [license dungeon];required=3033,10,1 |

`10000/10004` 是受控教程触发，不应假设能从普通选图 UI 进入。非教程 `100` 是活动地下城并有 required item/schedule，`5006` 是 license dungeon 且有 required item；二者在当前 Maze 分区会落为 Ordinary，但不是纯普通入口样本，UI 可达性与原版准入政策都需实机确认。

## 结构边界样本

| dungeonId | path | name | minLevel | noFatigue | mazeCount | ordinary/quest | difficulty | designate | questConnection | special/required |
|---:|---|---|---:|---:|---:|---:|---|---|---|---|
| 144 | `dungeon/StormOfMetastasis/Abnoba/Redcrystal_Forest.dgn` | 炽晶森林 | 1 | 0 | 5 | 1/4 |  | 0 | m1:0,1776,-1;m2:0,1777,-1;m3:0,14873,-1;m4:0,14874,-1 |  |
| 145 | `dungeon/StormOfMetastasis/Abnoba/Icecrystal_Forest.dgn` | 冰晶森林 | 5 | 0 | 5 | 1/4 |  | 0 | m1:0,1780,-1;m2:0,1779,-1;m3:0,14875,-1;m4:0,14876,-1 |  |
| 12 | `dungeon/Act2/Hallofdoll.dgn` | 人偶玄关 | 55 | 0 | 2 | 0/2 | 1 | 1 | m1:0,4308,2;m2:0,4656,2 | [crack of dimension dungeon] |
| 3540 | `dungeon/Event/chn_Dragon_Dungeon/chn_Dragon_dungeon_03_event.dgn` | 圣地 : 龙之魂 | 84 | 1 | 1 | 0/1 |  | 0 | 0,20727,-1 |  |

`12` 的可投影 Maze 全部带任务连接；`3540` 的连接在文件级，因而覆盖它的全部 Maze。这两种都证明“有 Maze”不等于“普通入口可选”。


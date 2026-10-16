# 赫拉斯研究所：当前 PVF 首轮只读取证

日期：2026-10-16。基准提交：`6e5b4ebd62b1e236aa273693c82d3dd094eec9a1`。

证据等级：以下为 ContentStatic，读取当前归档得到；没有运行客户端、没有验证线上协议，也没有修改 PVF 或服务。原文位于本目录 `pvf/`。使用现有 `Tool/PvfProbe/bin/Release/net10.0/PvfProbe.exe`，按 `lst`/`dump`/`files` 读取，没有新建构建。

## 最重要的结论

当前客户端与服务端都有赫拉斯研究所，但两侧共同使用 **3×3 简化布局**，并共同保留旧 **5×5 陷阱配置**。两侧 DGN3900 的完整探针文本逐行一致，`etc/global_timespiral.etc` 也逐行一致，因此这不是本次发现的客户端和服务端内容不同步。

3×3 当前 DGN 的选图清单中没有任何一个陷阱图；如果一期按当前内容开放，不能把旧配置存在直接当作随机陷阱已处于有效关卡链。恢复旧版陷阱需要额外决定布局范围，并验证客户端和服务端坐标、门、传送一致性。

完整DGN经token探针复核，共136个token，已读到EOF，没有截断；其中 **没有 `[time spiral]` 标志**。如果上游用该标志决定进入专用机制，那么当前内容不能直接启用那个判定。是否补标志、恢复布局属于后续实现决策，不能在首轮调查中悄悄修内容。

归档：

| 角色 | 路径 | SHA256 |
|---|---|---|
| 服务端 | `artifacts/sao-ticket-20261014/Script.server.pvf` | `91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4` |
| 当前客户端 | `E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf` | `FDDBB938361D4CA817598AE479E2F951B47E1DB64E096943167D20014A97AF98` |

## 入口与次数

- 地下城 ID **3900**，`dungeon/GTimeSpiral/TimeSpiral.dgn`，名称赫拉斯研究所。
- `worldmap/granflorishillside.wdm` 的 `[dungeon]` 明确列出3900；所在世界地图为格兰之森山麓。
- DGN `[schedule] 2367`，但这只证明内容引用活动2367，不证明开放日时段或活动列表表现。
- 最低等级50；basis level 50；推荐50–86；`[limit party count] 1`；`[designate dungeon difficulty] 1`；`[prohibit practice] 1`。
- `[no fatigue]`、`[enter without fatigue] 1`、`[no giveup panalty]` 均存在。怪物等级替代开关 `[mob level charac level replace flag] 1`。
- `etc/dungeonetc/dungeonincountinfo.etc` 的角色次数表存在四元组 **`0 3900 3 0`**；次数值3有静态内容依据。重置原文 `[reset info] 0 -1`，其枚举和具体重置时刻不能仅凭文本具名。
- 同文件 `[account dungeon incount info]` 没有3900；`dungeonincountforaccount.etc` 仅声明 `94 8`。
- `etc/chn_schedule.etc` 的3900行是 `3900 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 -1`；不能将其等同 schedule2367 的正式开放时间。
- 旧钥匙 **10093393**，文本说凭此进入研究所，类型 `[quest] 0`；**当前 DGN 没有耗票声明**，因此是否消耗这个钥匙需另外的服务端/活动证据。
- **490002847 华丽的赫拉斯研究所入场券**：`[action type] [add enter limit dungeonincount] 1 3900`，明确为增加一次挑战。城镇/赛丽亚房间可用，冷却10000，堆叠上限10，每日删除，`[daily purchase limit] chraracter 2`。这是物品购买限制，不应误写成研究所基础入场次数。

## 当前3×3关卡

开始坐标 `(0,0)`；Boss坐标 `(1,1)`。

| 当前坐标 | 可选 MAP ID |
|---|---|
| 0,0 | 27100 |
| 0,1 | 27101 |
| 0,2 | 27108 |
| 1,0 | 27111 |
| 1,1 Boss | 27112 |
| 1,2 | 27114 |
| 2,0 | 27116 / 25644 / 25645 |
| 2,1 | 27123 |
| 2,2 | 27124 |

MAP文件名中的旧坐标如27112的 `(2,2)` 不等于当前DGN里的摆放坐标 `(1,1)`；实现应读DGN映射，不能按文件名推坐标。

- 27112 Boss表固定 **64923 ML-771 初号机**，含NPC1000。它的 MOB `[item]` 为空，不能据此推出没有服务端掉落。
- 27116/25644/25645 分别放置 champion **64930/64931/64932**，对应初号机 heavy/close/sword 三种MOB。
- 27108 放 champion **110920 业火之菲茨**；27124 放 champion **110921 暴击格莱尼**。
- DGN named monster 中还有64700、64701；实际分别映射旧event/golden_egg与chn_golden_goblin_junior，两者name字段为空，不支持将其认定为赫拉斯隐藏Boss。当前选图、怪表没有确认额外隐藏Boss进入条件。

## 旧陷阱资源及机制链

`etc/global_timespiral.etc` 列出七个陷阱 MAP：**27103、27119、27121、27110、27107、27113、27117**。每个有 `[teleport trap]` → `[move list]`，行形状为四元组：目标坐标x/y、权重、最后一个标志。坐标覆盖0–4，显然不匹配当前3×3；最后标志0/1的精确语义还需客户端/上游解释。

例如27117表：`3 1 900000 0` 与 `2 1 100000 1`。这里只能确认权重比9:1，不能直接把标志解释成成功/失败或Buff开关。

机制链已有静态证据：

1. trap MAP 的 `[special passive object]` 出现 **12577**。
2. `passiveobject.lst` 12577 → `Actionobject/map/Timespiral/Timespiral_Trap.obj`。
3. OBJ `[trap] 1`，基本ACT `Action/Timespiral_Trap.act`，销毁ACT `Action/Timespiral_Trap_destroy.act`，HP MAX5000000。
4. 基本ACT通过 `[ALL CHARACTER TEAM]`、`[IS OBJECT TYPE] [CHARACTER] [MONSTER]`、`[CHECKUP OBJECT] 0` 触发行为 `[PULL APPENDAGE] 3 2 60000`。

这条链证明客户端存在陷阱标记与动作资源，**没有证明传送、Buff由ACT独立完成，也没有确定C2S/S2C协议**。

有另一个 `passiveobject/mapobject/breakableobject/timespiral_trap.obj`，`[trap] 1`、`[int data] 1 2 1 0 0 0 500`；不能因同名替代当前MAP明确引用的12577。

随机增益静态声明：

| 类型 | 权重 | 原始属性值 | 时间 |
|---|---|---|---|
| 0 | 30 | physical attack100 / magical attack100 | 60000 |
| 1 | 30 | physical attack75 / magical attack75 | 60000 |
| 2 | 30 | physical attack50 / magical attack50 | 60000 |
| 3 | 10 | move speed50 / attack speed50 / cast speed50 | 60000 |

60000常见为毫秒，但属性值是百分比、加法值或倍率编码需客户端解释，不在本轮静态文本中强行具名。

## 奖励相关内容

- **490002846 赫拉斯研究所的零件**：说明可在NPC吟荷兑换道具；`[material] 2`，`[need material] 10092630 2`。
- **10092630 能量残骸**：`[material] 2`，包含旧绝对到期时间 `2016-09-27 06:00:00`，当前不能直接照用而不检查现有物品生命周期。
- **10092628 机械碎片**：`[quest] 0`；**10092629 MV-002的零件**：`[material] 2`。
- **10093401 秘密机械保险柜**：说明随机获得赫拉斯神器装备或Lv65以上魔法石，`[upgradable legacy] 1`，消耗材料10092630×10；`[int data]` 已保存完整候选ID/权重/数量三元组。这个文本与奖励池并不证明研究所击杀时会发该箱。
- 归档中保留 `equipment/character/partset/helixlab_rare_*` 和 `helixlab_unique_*` 套装内容，及event/helix怪物资源。

本轮没有找到可从地下城DGN直接读出的完整通关掉落表、兑换商店关联、隐藏Boss触发条件；需要结合70server逻辑、当前掉落内容源或客户端协议。尤其不能将旧钥匙、旧到期材料和当前日服活动奖励混成一套规则。

## 建议接下来的取证决策

### 主代理补充：现行零件独立掉落

子代理完成后，主代理用PvfProbe完整读取`etc/independent_drop.etc`至闭合标签，并按本仓
`PvfIndependentDropTableSource.Parse`的17列和可选list结构解析全部3046行；匹配结果保存为
[independent-drop-rows.json](pvf/independent-drop-rows.json)，原始邻近文本见
[independent-drop-matches.txt](pvf/independent-drop-matches.txt)。
ordinal2380..2391是六个当前怪物各两条零件490002846记录。
按当前`IndependentDropRuleService`的概率基数1000000、成员试次数及上限解释，
64923为必出2个加一次60%出1个；64930/64931/64932/110920/110921各为必出1个加一次60%出1个。
这是内容和当前规则的静态交叉结果，不是本轮实机概率验证。没有匹配到旧能量残骸10092630或保险柜10093401的独立掉落行。
因此已找到当前零件产出真源，但兑换商店关联、隐藏Boss条件和其他奖励仍按上述未知保留。

先按双方一致的3×3研究所确认一期入口、3次计数、等级/单人/疲劳规则与实际通关奖励；把旧5×5陷阱、随机Buff、隐藏Boss作为单独恢复范围。若用户希望一期即含原陷阱，则应先解决布局来源和协议触发，而不是仅接global配置。

# 地下城通关条件（#356）

## 问题

主线 1842「人偶之村」完成不了（issue 标题写成了「人偶之春」）。地下城 160『绝望的棋局』给 1842 配了专用迷宫：
`[quest connection] 0 1842 -1`，并带 `[clear condition] [destroy object] 10601 1`。这个迷宫的 Boss 房（map 59199）
没有怪，只有 NPC 伊利娅和物件 10601。10601 是 `[time limite] 4000` 的 dummy 物件，会自己销毁。

实机复现（2026-11-16，Client-08，测试服 41080，账号 1023999260 slot 2）：
- 角色卡在 Boss 房，没有结算。
- 客户端在 15:00:07 发出 0x0027 DIE_MONSTER，body 开头 `69 29`，即物件 10601（来自 `clienthack-a21-pickup-trace.log`）；
  早约 0.5 秒有 STORY_PAUSE `pause=1`。
- 服务端的物件分支（`GameMonsterDeathCommandHandler` 中 `message.ObjectCode` 分支）只处理物件掉落，不判定通关，
  也不下发 0x001F。

## A21 PVF 统计（content 0.3.3-dev.1）

`[clear condition]` 共 96 块，分布在 41 个地下城：

| 子类型 | 条目 | 地下城 | 参数 | 用途 |
|---|---:|---:|---|---|
| destroy object | 79 | 26 | `物件 数量`，全部是 1（10601 有 51 条，16072 有 13 条） | 主线剧情迷宫：时空之门 70–76、厄运之城 160、冰墙噩梦 167–172、诺斯匹斯、精灵森林 516、无法地带 517 等 |
| hunt boss | 10 | 8 | `怪物 数量` | 特殊副本：2008、2010、2011、2016，以及循环版 8004、8006、8007、8011 |
| clear map | 6 | 6 | `地图 1`，或 `list 2 A B 1` | 140、2005、2006、3108、8001、8002 |
| limit time | 1 | 1 | `600 1200` | 只在测试副本 4000 |
| hunt monster / hunt apc | 0 | 0 | — | A21 数据里没有出现，但客户端和 70 都能解析 |

相关标志：
- `[ignore default dungeon clear]`：10 次，用在 2008、2010、2016、8004、8006、8011、领地战 7054–7056、地产活动 95。
- `[team clear condition]`：6 次，只在领地战 2vs2（7054–7056），每个地下城两队各一组 `[hunt boss]`。
  例：`0 [hunt boss] 61387 1 3 59373 59381 59377`。

另有 19 个带条件的迷宫没有 `[quest connection]`，这些迷宫无论是否在做任务都会被选中（例如 517 的第 0 个迷宫、
2005–2016 等特殊副本）。

## 证据

### 70Reference（`df_game_r`，SHA-256 `CF45F4E8…2EB7`）

70 没有剧情模式，但有"任务专用迷宫 + `[clear condition]`"机制，写法和 A21 相同：
70 的 `quest/fairyforest.dgn` 写的就是 `[quest connection] 0 4122 -1 [clear condition] [destroy object] 10601 1`。
70 没有 `[team clear condition]`，也没有 `[ignore default dungeon clear]`。以下均为【反汇编确认】。

- **解析**：`ImportDungeonScript` 在 0x088E8764 读 `[clear condition]`，生成 vector<{type,id,count}>，
  type 取值：destroy object=0、clear map=1、hunt monster=2、hunt apc=3、hunt boss=4。clear map 的计数强制为 1。
  `[quest connection]` 在 0x088E8665 读 type/questId/最低难度。
- **选迷宫**：`GetAppropriateMaze` @ 0x082FF128 先找"任务进行中"（type 0）的迷宫，再找"任务已完成"（type 1）的。
  `CheckQuestConnection` @ 0x085B6478 只要任一有效队员满足即可。Quest+0x120 等于 1 的任务在多人队伍时跳过；该值来自任务的 `[cant giveup]`（`importQuestScript` 0x08A64F59 写 QuestScript+0x270，`Quest::set_quest` 0x08352991 拷到 +0x120），1 = 不可放弃且迷宫只限单人，2 = 只是不可放弃。
  type 0 还要求难度不低于最低难度。命中多个随机取一，都不命中就退回普通迷宫。
- **计数**：`CClearCondition::ClearCondition` @ 0x082FEFCE。
  - 物件：客户端上报 0x0027（物件标志=1）→ `CParty::DestroyPassiveObject` @ 0x085B6540，代码必须在当前地图的
    `[passive object]` 列表里 → 计数。
  - 击杀：`kill_monster` @ 0x085A3AD0 按类别计 hunt boss / hunt apc / hunt monster。
  - 清房：`prepare_dungeon_clear` @ 0x085AA598 计 clear map。
- **通关**：`ClearDungeon` @ 0x085A9330 只执行一次，给每个队员发 NOTI 0x001F（空 body）。
  `IsClearDungeon` @ 0x0822D08C：有条件时只看条件，没条件时看 Boss 终点。之后 SET_PLAY_RESULT 走与 Boss 通关相同的
  评分和翻牌路径。条件计满后拒绝换房（0x085A8FF5）。

### A21 客户端（ClientStatic，`DNF.unpacked-b1-full-e5a34bbf.exe`，VA = 0x400000 + RVA）

- **`[clear condition]`**：`ImportDungeonScript` @ 0x02AAB190 → 0x02AB0C6E → 解析函数 0x02AA0770。
  - type：destroy object=0、clear map=1、hunt monster=2、hunt apc=3、hunt boss=4、**limit time=5**。
  - 结构：+4 是目标 id，`list` 写法时存 N；+8 是计数；+0xC 是 isList；+0x10 是 list 的 id 数组。
  - clear map 的计数强制为 1（0x02AA0B37）。一块只算一条，多块就是多条。
- **`[team clear condition]`**（0x02AB0BF3）：
  - 先读队号，只能是 0 或 1，越界报 "team index over"。
  - 只有 team + hunt boss 时，才再读 named 数量和对应 id（报错字符串 "territoryNamedMonsterCount"）。
    `61387 1 3 59373 59381 59377` 依次是：Boss id、计数、named 数量 3、三个 named 怪 id。
  - 运行时客户端把两队条件并进同一张表，不保留队号（0x01742CD7–0x01742DFD），所以客户端分不出胜负。
- **`[ignore default dungeon clear]`**（0x02AB8810）：只写了 dungeonData+0xA10=1，明文代码里没有地方读它。
  判断它的语义在服务端（Hypothesis/High）。
- **本地计数只用于演出**：客户端会按条件本地递减计数，但计满只触发收尾慢动作 R_STAGE_FINALE，不发包，
  也不让通关生效。通关以 NOTI 0x001F 为准：处理函数 0x01149950 显示通关文字，并把 CNRDDungeon+0xB6C 置为 2。
  没有 0x001F 时 Boss 房既不开出口也不进结算。
- **没有专门上报条件进度的 C2S 包**：服务端用 0x0027（击杀，或带物件标志的物件）和房间清空状态自己计数。
- **领地战胜负**：NOTI 0x02C8 TERRITORY_COMBAT_CLEAR_DUNGEON（u8、u16、u16，含义待定），C2S 0x02DC。
  named 列表的用途在明文代码里找不到。

### 上游 ServerS4A21 / 第三方

- 两者都解析 `[clear condition]`（`Tool/PvfLib/Models/DungeonFile.cs:1424-1500`），状态在 `ClearConditionState.cs`。
- 判通关的规则是：条件命中，或者（到达 Boss 终点且没有声明忽略默认通关），见 `DungeonCombatHandler.cs:387`。
- 另有两条 70 没有的兜底：用 STORY_PAUSE 或任务 trigger 消耗 destroy object。实机抓包已确认 A21 会上报物件，
  所以不需要这两条。
- 两者都没有实现 `[team clear condition]`。

## 方案

范围（用户 2026-11-16 裁定）：三种条件（destroy object、hunt boss、clear map）加忽略默认通关；对齐 70 的迷宫选择；
limit time 只解析不执行；`[team clear condition]` 这次排除，留到实现领地战时一起做（见第 5 节）。

### 1. 内容解析（Infrastructure）

- `PvfDungeonDefinitionSource` 按顺序读迷宫内的 `[clear condition]` … `[/clear condition]`，每块一条，
  得到 `MazeDefinition.ClearConditions`（`{Kind, Targets, Count}`）。`list N a b … count` 的 Targets 是多个 id，
  命中其中任一即可；clear map 的 Count 固定为 1。一块写了多条的情况会被拒绝（A21 数据里没有）。
- `[team clear condition]` 整块跳过，不建模型（第 5 节）。
- `[ignore default dungeon clear]` 按地下城级处理（客户端也只存在 dungeonData 上），得到
  `DungeonDefinition.IgnoresDefaultClear`。
- limit time（type 5）读进模型，`IsCounted=false`，不参与判定。
- 任务的 `[cant giveup]` 读到 `QuestDefinition.GiveUpRestriction`，值为 1 的任务迷宫只允许单人进入（第 2 节）。
  A21 里值为 1 的任务有 16 个，值为 2 的有 96 个。

### 2. 迷宫选择（`DungeonStructureRuleService`）

对齐 70 `GetAppropriateMaze` / `CheckQuestConnection`：

- 先在"任务进行中"（type 0）的迷宫里选，没有再到"任务已完成"（type 1）里选。
- 是否在做或已完成，按全队任一成员判断：进副本时合并全队的任务记录（`SelectDungeonCommandPhases`），
  不再只看队长。
- 队伍超过 1 人时，`[cant giveup]` 为 1 的任务迷宫不参与选择。
- 选中迷宫的条件和地下城的忽略标志随布局固定（`DungeonLayout.ClearConditions` / `IgnoresDefaultClear`）。

### 3. 条件进度（不另设计数器）

70 给每条条件一个计数器。本服的副本状态已经保存了全部所需事实，所以直接由事实推出进度
（`DungeonClearConditionRules`），避免两份状态互相漂移：

| 条件 | 进度来源 |
|---|---|
| destroy object | 副本内已消耗的房间物件（`DestroyedObjectCodes`）。物件只能通过 0x0027 物件分支消耗，而该分支只接受当前房间声明过的物件代码，等同 70 对 `[passive object]` 的校验 |
| hunt boss / hunt monster | 已提交的击杀记录（`Kills`），按怪物代码匹配。客户端本地计数不看代码，只影响演出 |
| hunt apc | 同上；击杀记录只含怪物，所以 APC 条件无法满足。A21 数据里没有 hunt apc |
| clear map | 已清空房间的地图号（`ClearedRoomMapIds`），命中任一目标地图即满足 |

### 4. 通关开放（`DungeonClearDomainService.TryOpenClearAsync`）

按 70 `IsClearDungeon` 的口径（用户裁定）：

| 迷宫 | 通关开放条件 |
|---|---|
| 有 `[clear condition]` | 只看条件是否全部满足；Boss 房清空不算 |
| 没有条件，地下城声明了 `[ignore default dungeon clear]` | 不按 Boss 房清空开放（A21 里只有地产活动 95 是这种情况） |
| 没有条件，也没有忽略标志 | 保持现在的规则：Boss 房清空 |

- 物件分支（`GameMonsterDeathCommandHandler`）在物件消耗提交后尝试开放通关，开放时复用
  `DungeonClearCommandProjection` 下发 0x001F 等帧。击杀和拾取原本就会尝试开放通关。
- 条件迷宫可以在任意房间开放通关，每局只开放一次（以 `ClearedAt` 为准）。
- 任务"通关地下城"的事实：条件迷宫的 Boss 击杀不再产生 `QuestDungeonCleared`，改为在结算时产生
  （`DungeonClearSettlementDomainService`，与镜像副本同一处）。
- 条件满足后拒绝换房（对齐 70 0x085A8FF5）。

### 5. 队伍通关条件（本次排除）

`[team clear condition]` 只出现在领地战 2vs2（7054–7056），领地战副本本身（组队、阵营、NOTI 0x02C8 胜负结算）
服务端完全没有实现，0x02C8 三个字段的含义也没有证据。用户裁定本次排除，**在实现领地战时一起实现**。
到时需要：按队号分开计数；named 怪列表的语义；用 0x02C8（u8、u16、u16）下发胜负。客户端证据见上文
"A21 客户端"一节。本次解析器只识别这个标签并跳过，保证这三个地下城的其余内容照常读取。

## 用户裁定（2026-11-16）

1. 队伍通关条件本次排除，留到实现领地战时做。
2. 有条件的迷宫只认条件，Boss 房清空不算通关（按 70）。
3. 迷宫选择按 70 对齐（先进行中、全队任一成员、单人任务迷宫），limit time 只解析。

## 验证

- 自动测试：
  - `DungeonClearConditionTests`（Unit）：物件条件开放通关且只开放一次，开放后拒绝换房；条件未满时 Boss 房清空不通关；
    hunt boss 按代码计数；clear map list 任一地图满足；忽略标志关闭默认通关；只有 limit time 的迷宫保持默认规则。
  - `DungeonStructureRuleServiceTests`：进行中优先于已完成；条件与忽略标志随布局固定；`[cant giveup]` 1 的任务迷宫
    单人可进、组队不选。
  - `DungeonClearConditionContentTests`（真实 PVF）：160 的 1842 迷宫、2005 list、2008 hunt boss + 忽略标志、
    4000 limit time、领地战整块跳过；全档案普查 79/10/6/1 条且无解析失败；任务 `[cant giveup]`。
- 全量测试：本分支有 50 条失败，与主干同一份 dev PVF 下的 48 条既有失败对比，新增 2 条：
  一条是禁止生产构造函数用可选依赖的架构测试，一条是物件分支在非条件迷宫多做了通关判定。两条都已修复后重跑通过；
  其余为既有问题，与本改动无关。
- 实机：2026-11-16 用户在测试服（本分支构建，dev PVF `a97132dd`，Client-08，账号 1023999260 slot 2）按下节步骤测试，
  反馈全部通过：1842 剧情 Boss 房能通关结算并交任务，普通进『绝望的棋局』与其他回归正常。逐步的观察记录没有单独留存。

## 用户测试步骤

测试服：worktree `DNF-JP-86-Server-clearcond`，端口 41080，库为 `.task-evidence/355-elf-guardian/dfo-server.db`
（主干玩家库副本，账号 1023999260 slot 2 角色已标记 1841 完成）；客户端 `Client-08`。

1. 1842「人偶之村」：接任务，进『绝望的棋局』剧情迷宫，到 Boss 房。预期：对话后约 4 秒出现通关，能结算翻牌；
   回城能交 1842。
2. 普通进『绝望的棋局』（不带任务）。预期：走普通迷宫，打死 Boss 正常通关（回归）。
3. 任选一个带 destroy object 的其他剧情任务迷宫（如时空之门、冰墙噩梦）。预期同 1。
4. 回归：普通地下城、镜像、绝望之塔照常通关与结算；组队进普通地下城正常。

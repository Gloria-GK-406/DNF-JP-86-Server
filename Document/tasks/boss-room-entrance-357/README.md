# Boss 房入场条件（#357 陷落的村庄）

## 问题

特殊地下城 2010「陷落的村庄」（`dungeon/Special/MeltdownHelpus.dgn`，循环版 8006）里不出"居民"，
地下城也通不了关。用户原本不确定这个版本是否有居民机制；结论是**有**，是服务端没有实现。

这个地下城的玩法：
- **居民**：瘟疫鼠潘多 56611、超级蘑菇人伯南泽 56617、蘑菇怪人夏图 56618。它们写在每张普通房地图的
  `[monster condition]`（条件怪）里，不在普通怪 `[monster]` 段。
- **小地图**：`[dungeon minimap icon setting]` 用 `[with monster]` 给这三个居民所在的房间标"被困小镇"图标。
- **召唤 Boss**：`[boss room entrance condition]` 规定三个居民都狩猎后执行 `[clear action] [summon monster] 1 69264`，
  召唤血蝎迪奥女王。召唤位置来自每张普通房地图的 `[conditional summon monster]`。
- **通关**：迷宫通关条件是 `[hunt boss] 69264 1`，并声明了 `[ignore default dungeon clear]`（见 #356）。

服务端原来的问题：生成房间时把条件怪和条件召唤都过滤掉了（`PvfDungeonRoomActorSource.GetAsync`），
所以居民不出现，Boss 召唤不出来，地下城也就无法通关。

## A21 PVF 统计（content 0.3.3-dev.1）

- 带 `[special dungeon]` 的地下城 12 个（2005–2016 与 195）；带 `[boss room entrance condition]` 的 dgn 有 8 处。
  地图里有 121 处 `[monster condition]`、142 处 `[conditional summon monster]`。
- 执行召唤动作的只有这两组：
  - **2010/8006**：救出 3 个居民后召唤 69264；
  - **2016/8011**：狩猎 4 个 61574 后召唤 56616。2016 的图标是写死的 `[grid]`（四格），而且 61574 正好只写在这四格的地图里。
- 2005 的动作是 `[time condition]`（绅士潜入），本次只识别、不执行。

## 证据

### A21 客户端（ClientStatic，`DNF.unpacked-b1-full-e5a34bbf.exe`，VA = 0x400000 + RVA）

- **dgn 解析**（`0x02AB38E6` 一带）：`[boss room entrance condition]` 是**一个段**，`[no check enter boss room]`、
  `[clear action]`、`[summon monster] N code`、`[hunt monster] N` 加 N 组三元组都是段内的记号。
  - 客户端从不按 `[hunt monster]` 计数：条件类型唯一的读者（`0x0177920A`）只判断"破坏物件"，所以计数由服务端负责。
  - 三元组后两个数的含义从客户端读不出来。
- **0x022F**（`0x0114C580`）：`u16 n` 加 n 组 `(u8 x, u8 y, u32 code)`，客户端按代码匹配 `[with monster]` 放图标。
- **0x0138**（`0x01193290`）：读 `u32, u8` 后丢弃。队长的客户端随后对**当前地图**中召唤码匹配的
  `[conditional summon monster]` 行发 0x0211。
- **0x0211 请求**（`0x00E46954`）：19 字节，含 kind、code、请求号、地图、房间格、条数。
- **0x0211 回包**（`0x01109050`）：成功是 `01 | 请求号 | n | n×(u16 序号, u32 code, u8 等级档, u16 等级)`，
  没有额外尾部；拒绝是 `00 | reason | 请求号`。
- **0x001D 的 flag0/flag1**（`0x01745F85`）：含义是"挂到 createMap 第 k 组"，不是隐藏模板。召唤流程不经过 0x001D。

### 上游 ServerS4A21 / 第三方（逻辑一致）

- 进本时按 `SpecialDungeonRunCoordinator` 558-619 选房：候选是普通房（不是起点、不是 Boss 格），且该图的
  `[monster condition]` 含目标代码；每个目标不放回地随机挑一个房间。
- 计数（`SpecialDungeonMechanismApplicationService` 528-573）：目标代码要在它被分配的房间里死亡才算。
- 召唤：0x0138 → 客户端 0x0211 → 服务端校验后回包，在最后救人的房间原地召唤。
- 上游另往 0x001D 塞隐藏模板（flag0=1、序号 6），本仓按 A21 客户端证据**不这样做**。
- 上游从没给 2010 写过专门代码，全靠这套通用机制。`[special dungeon]` 和 `[escape hell]` 与本机制无关。

## 实现

| 位置 | 内容 |
|---|---|
| `PvfDungeonDefinitionSource.ParseBossRoomEntrance` | 段内记号流解析为 `DungeonBossRoomEntranceCondition`：召唤码、召唤数、`[hunt monster]` 三元组。其他动作记入 `UnsupportedAction`，不执行 |
| `PvfDungeonMapDefinitionSource` | 地图定义带上 `ConditionalMonsterCodes`，即该图 `[monster condition]` 里的代码 |
| `DungeonStructureRuleService.PlanEntranceAsync` | 在 `PrepareLayoutAsync` 里按上游规则选房，用副本种子随机，结果固定为 `DungeonLayout.EntrancePlan`。只考虑有唯一固定地图的格子 |
| `DungeonRoomRuleService` | 目标房生成时把该图对应的 `[monster condition]` 行作为普通敌对怪插在 AI 角色之前，挡清房；`DescribeConditionalSummonsAsync` 给出召唤怪及其等级 |
| `DungeonEntranceConditionRules` 经 `IDungeonSettlementRuleService` | 从击杀记录推出目标是否全部狩猎：同一代码、同一房间各要一次击杀 |
| `MonsterDeathDomainService` | 结算时算出这一击是否补齐最后一个目标（`CompletesEntranceCondition`），协议据此在击杀帧后发 0x0138 |
| `DungeonEntranceDomainService` | 处理 0x0211：校验后取本房未用序号、生成掉落，经 `StageDynamicCell` 把召唤怪加进当前房 |
| 协议 | `Game/EntranceSummon` 切片（0x0211）；`Game/Dungeons/DungeonEntranceProjection`（0x0138、0x022F）；进本时 0x022F 紧随 DUNGEON_INFO |

召唤怪的击杀走普通 0x0027，会被 #356 的 `[hunt boss]` 条件计数，从而开放通关。

### Boss 格可以离开

本仓原有规则"进了 Boss 房就不能离开"（`DungeonRunState.MayLeaveCurrentRoom`），是 2026-08-26 实机出现
"清完 Boss 房又走回空房间"后加的，上游没有这条规则。2010 的 Boss 格（迷宫 1 是 (1,3) 的 17118，迷宫 0 是 (2,0) 的 17122）
只是放普通怪的房间，真 Boss 要救完别处的居民后才召唤。实机里玩家走进 Boss 格后被锁住
（`the Boss room at (1,3) may not be left`），又因为条件迷宫只认条件而不能通关，形成死局。

现规则：地下城的入场条件声明了 `[no check enter boss room]` 时，Boss 格可以离开。上游只解析这个标签、从不使用。

### 任务 13503 的居民计数

13503「[个性技能]拯救被劫持的居民」要求 `hunt enemy 2010 -1 19034 3 2`（另有 19036、19038 两条），目标是被动物件
「赫顿玛尔废墟居民1/2/3」。这些物件不在任何地图声明里，由客户端在看守倒下后放出；救出时客户端发
SET_QUEST_TRIGGER 逐一减计数（实机抓到 selector 0x20/0x40/0x10）。

原规则（`QuestTriggerDomainService`）只在"该任务的专属迷宫"里接受被动物件上报，2010 是没有 `[quest connection]`
的普通局，于是全部被拒。现改为：
- 地下城为该任务连了专属迷宫时，仍须身处该迷宫。1776 的物件只存在于 144 的剧情迷宫，原测试照旧拒绝普通局的上报。
- 地下城没有为该任务连任何迷宫时，物件只能来自它的普通流程，身处目标指明的地下城即可。
- 目标是"任意地下城"时，仍只认专属迷宫。

## 待实机确认

- 0x0138 → 0x0211 的往返在组队下是否成立（单人已实机通过；`0x019E7810` 队长判断）。
- 0x0211 回包只发给请求者是否足够；其他队员如何看到召唤体。
- 救出居民后小地图图标是否消失。
- 2016 的 `[hunt monster] 61574 1 4` 中 "1 4" 的含义；目前按"每条一次击杀"处理。

## 验证

- 自动测试：
  - `DungeonBossRoomEntranceContentTests`（真实 PVF）：2010、2016、2005 的解析结果；地图条件怪；2010 三个居民各占一个普通房且随种子变化；2016 正好占满固定图标的四格。
  - `DungeonEntranceConditionTests`（Unit）：目标须在被分配的房间死亡；相同目标各需一次击杀；居民只出现在目标房且排在 AI 之前；召唤等级；队长在救齐后只能召唤一次，召唤体进入当前房并挡清房。
  - `EntranceSummonCodecTests`（Unit）：0x0211 请求与回包、0x022F、0x0138 的逐字节编码。
- 实机（2026-11-16，单人，Client-08）：用户确认通过。13503 已接后 2010 可在选图找到，居民出现并计入 13503，
  可离开 Boss 格，救齐后服务端日志 `Entrance summon staged ... room=17117 code=69264`，击杀女王后通关。

## 用户测试步骤

测试服：worktree `DNF-JP-86-Server-entrance`，端口 41080，库为 `.task-evidence/355-elf-guardian/dfo-server.db`；客户端 `Client-08`（trace）。

0. 先找阿尔伯特·伯恩斯坦（NPC 27）接 13503「[个性技能]拯救被劫持的居民」。厄运之城的
   `worldmap/meltdown.wdm` 把 2010 登记为 `2010 13503`：A21 客户端只有在 13503 已接或已完成时，才把
   2010 的格子放进选图（ClientStatic，`0x00E64AC0`）；格子位于 `WorldMap/UI/Meltdown.xui` 的 BUTTON6（550,89）。
1. 进『陷落的村庄』（2010）。预期：小地图有三个"被困小镇"图标。
2. 走到有图标的房间。预期：该房出现对应居民（潘多 / 伯南泽 / 夏图）。
3. 击杀三个居民。预期：击杀最后一个后，Boss 血蝎迪奥女王在该房间出现。
4. 击杀 Boss。预期：出现通关结算，可以翻牌。
5. 回归：普通地下城、1842 剧情迷宫、镜像、绝望之塔照常。

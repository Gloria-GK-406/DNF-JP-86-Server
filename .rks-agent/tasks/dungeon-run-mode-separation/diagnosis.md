---
kind: rks-diagnosis
contract:
  path: ./TaskContract.md
  revision: 2
state: supported
---

# 诊断：普通/剧情地下城被压成一次无类型运行

## 复现与现象

环境为主干 `2b323fb628b90faa3549fab24e0dd3c47901dd73` 的 Release Host，PVF 为 `DNFClient-A21-protocol/Script.pvf`。用户以角色 2、活动任务 1776 进入 dungeon 144：

1. 第一次运行随机选中 maze 1，路线 `55612 -> 55605 -> 55606 -> 55613 -> 55614 -> 55607`，与任务 1776 的连接 maze 相同。
2. boss 房中客户端发送 `SET_QUEST_TRIGGER quest=1776 selector=0x10 decrement`，服务端记录 `Unchanged trigger=1`；通关结算和卡牌流程仍按普通副本完成。
3. Release SQLite `character_active_quests` 保持 `character_id=2, quest_id=1776, trigger_value=1`。
4. 第二次运行手动退出后服务端记录 `run ended=True`，客户端仍继续发送死亡与 `MOVE_MAP`；后续移动被记录为 `no run in progress, ignoring`。

回归目标分别是：同任务状态下显式普通/剧情入口可区分；1776 的被动对象通道报告只在匹配剧情运行中推进到 0；服务端计数怪物仍只接受可信死亡；GiveUp 后客户端与服务端同时回到城镇状态。

## 控制流追踪

### 1. 客户端使用同一进入指令，但前置选择路径不同

- A21 最终提交进入均使用 `CMD 0x0010 SELECT_DUNGEON`。已确认的 body 前部只有 dungeon、difficulty 和深渊标志；当前服务端只读取前 5 字节。
- `CMD 0x000F ENTER_SELECT_DUNGEON` 紧邻其前，当前实现读取一个 `u16 Selection`，但只用于日志，没有保存为下一次进入的上下文。
- 既有实帧证明，玩家打开普通选图界面时 `0x000F = 00 00 00 00`；教学的定向自动进入为 `0x000F = dungeonId`，并立即发送同 dungeon 的 `0x0010`。

对 A21 重建 exe 的最小静态比较进一步确认：

- 通用定向构包函数 RVA `0x003434E0` 先用传入 dungeon id 构造 `0x000F`，再用同一个 dungeon id 构造 `0x0010`。
- Normal 队伍路径在 RVA `0x00D38860` 检查模式值 `==2`，随后构造 `0x000F` 时写 `0`，再构造 `0x0010`。
- Cycle 路径在 RVA `0x00D418AC` 检查模式值 `==1`，同样先写 `0x000F=0` 再构造 `0x0010`。

因此 opcode 不是模式判据。当前可证实的入口上下文是：`0x000F=0` 表示打开交互选图；`0x000F=dungeonId` 表示客户端定向进入该 dungeon。任务状态只能在定向进入后帮助解释连接 maze 是否合格。A21 的任务 1776 实机仍需在最终验证中确认使用该定向形状；若不使用，必须保留阻塞并继续取证，不能改成“有任务即剧情”。

### 2. 内容层丢掉了任务连接

PVF dungeon 144 明确包含：

```text
[maze info]
[quest connection]
0 1776 -1
...
map 0 0 55612 ... boss 5 0 55607
```

现有 `PvfDungeonDefinitionSource` 不识别 `[quest connection]`，`MazeDefinition` 和 `DungeonDefinition` 也没有对应字段。`DungeonLayoutRuleService.ResolveAsync` 虽接收活动/完成任务 facts，却明确不使用并按 seed 在所有 maze 中取模。第一趟正确是随机命中，不是剧情路由成功。

### 3. 运行态没有目的

`DungeonVisitDomainService` 只取得 `DungeonLayout`，`DungeonRunDomainService.Begin` 只保存 layout 和 difficulty。后续代码无法知道选中 maze 是普通候选、活动任务连接还是已完成任务连接，因而：

- `PLAY_RESULT` offset 7 永远硬编码为 `0`（非任务 maze）；
- 完成规则只能使用通用房间清空/boss 逻辑；
- 无法把关联任务和该次选择冻结到运行中。

### 4. 首轮推断把 enemy type 误当成怪物 rank

任务 1776 是 `[hunt enemy]`，`int data = 144, -1, 13099, 3, 1`，含义为 dungeon 144、任意难度、enemy 13099、enemy type 3、数量 1。首轮实现错误地把最后的 type 3 当成 `MonsterRank.Boss`，并把整个 `hunt enemy` 家族归入 Server authority。

2026-09-14 A21 双路径实机证伪了这一推断。剧情入口明确得到 `Directed -> maze 1 -> QuestLinked(1776, Active)`；进入 boss 房后客户端发送 `SET_QUEST_TRIGGER quest=1776 selector=0x10 decrement`，随后报告的 `0x0027 actor=13083` 恰好是 map 55607 `[passive object]` 中的对象 code，而不是 START_MAP 静态 monster 序号。PVF 同一房间也把目标 13099 列在 `[passive object]` 中；旧 A21 规则表进一步确认 enemy type `1/2/3` 分别是 Monster/AI Character/Passive Object。

因此 type 1/2 仍应由服务器可信 actor 死亡推进，type 3 是客户端地图脚本掌握的被动对象事件。它可以接受客户端通道报告，但必须绑定当前冻结的同 quest、dungeon、difficulty 的 QuestLinked Active 运行，不能在普通副本或任意地点靠发包完成。

### 5. GiveUp 只删除运行态

`GiveUpGameHandler` 调用 `runs.End(characterId)` 后只发送一字节成功确认。相邻的正常离开和返回村庄路径已经复用 `TownReturnProjection.Frames(...)`。一旦确认帧让客户端停止自助回城，而服务器又没有主动投影城镇状态，就会形成已实机观察到的状态分裂。

## 根因假设与验证

### 假设

根因不是“随机算法选错一个索引”，而是领域模型把不同进入意图、任务连接资格和运行目的压扁为一个无类型 `DungeonLayout`；任务进度和退出又没有接入可信运行事件。局部修补随机选择会使所有持有任务的普通进入被剧情规则污染。

### 最小验证

1. PVF probe 找到 dungeon 144 的连接 1776 maze，证伪“内容没有剧情路线”。
2. 相同 `DungeonId` 同时存在无连接和多个任务连接 maze，证伪“按 dungeon id 分类即可”。
3. A21 普通选择实帧 `0x000F=0` 与定向构包静态路径 `0x000F=dungeonId` 不同，支持“意图来自前置选择上下文”。
4. `DungeonLayoutRuleService` 源码明确忽略 facts，解释随机现象。
5. 首轮加入 canonical kill 后，实机仍显示 `SET_QUEST_TRIGGER 1776/0x10 -> Unchanged 1`、被动对象 actor 不在静态 room actor 表、boss room clear 后完成门拒绝，证明剩余缺口是 target type 权限分类而非结算编码。
6. GiveUp 后 `runs.End=True`、随后 `MOVE_MAP no run`，并与正常离开的 TownReturnProjection 差异对照，解释无法过图。

假设得到支持。

## 受保护行为与修复边界

- 普通交互选图必须只选择无任务连接 maze，即使角色持有该 dungeon 的活动任务。
- 定向进入只有在命中合格任务连接时才形成任务关联运行；无连接的定向教学入口仍可形成普通运行。
- 任务连接选择与运行目的在入口冻结，任务日志后续变化不能改写本局。
- type 1/2 任务推进从 canonical kill 事实发生；type 3 只接受绑定对应 QuestLinked 运行和声明通道的客户端 trigger。
- GiveUp 的回城属于所有普通 maze 型运行的公共退出，不进入剧情策略分支。
- 未证实的剧情卡牌/展示差异不在本次自行推断；仅投影已确认的 `questMaze` 身份和有证据的完成行为。

## 实机补充：任务奖励整表误发

2026-09-14 修订服务实机已确认任务 1776 的 trigger 从 1 到 0、boss 出口打开、`questMaze=1`、任务完成及任务 1777 接取。完成日志同时记录 `quest=1776 ... items=13`，SQLite 中 item uid 195..207 在同一秒生成，证实并非客户端显示重复。

真实 PVF 的 1776 声明在 `[reward int data]` 中使用 `itemId [job] jobId growType count` 表达按职业自动发放，并且 `[reward selection int data]` 为空。当前 `PvfQuestDefinitionSource` 只保存其中的整数，丢失 `[job]` 结构；`FinishQuestCommand.ItemRewardsOf` 再把扁平整数两两解释为 item/count，于是把整张职业表投影为 13 次装备发放。客户端本次没有提交选择（`0xFFFF`），所以根因不是玩家漏选，而是内容结构和业务规则被压平。

同一数据开头的 `0 0` 是金币公式标记；当前路径也把它静默丢弃。`FinishQuestRequest` 已解析 `RewardSelection` 与 `CompletionCount`，但 Handler 仅记录 warning，CMD 不携带二者。完成 CMD 又在任务日志写入后逐项发奖，对正常 refusal 返回会让事务提交，存在任务已清除但奖励部分完成的后置失败路径。

回归目标是：真实任务 1776/job0/grow0/no-selection 只发 `101010437 x1`；真正的 selection 数据按过滤后候选选择一项；金币作为 ItemId=0 通知；任一结算失败全部回滚。用户明确要求不处理已经错误发放的历史物品。

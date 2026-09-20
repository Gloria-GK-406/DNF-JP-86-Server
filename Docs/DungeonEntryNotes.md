# 副本入场：信息收集笔记

> 历史调查适用范围（2026-09-20，基准 `28efa005`）：正文是 2026-08-17 的旧客户端/旧归档快照，
> “至今未实现”等表述不描述当前服务端。当前 A21 的请求布局、权限条目和疲劳取证见
> [#101 取证总表](../Document/普通地下城准入规则取证-101.md)，不得将本文的旧计数、宽度和上游政策直接用于当前实现。

`0x0010 SELECT_DUNGEON` 至今未实现，因此 1 级角色走不完新手教程。本页收集实现它所需的事实，
**不下实现结论**。协议字段与样本在 `protocol/`，本页只记服务端要建什么模型、依据是什么。

调查日期 2026-08-17。上游 = `.worktrees/legacy-reference`，归档 = `DNFClient-debug/Script.pvf`
（前 1 MiB SHA-256 `e8d8523f…`）。

## 一、归档里**没有**一份副本类型表

这是本次收集最重要的一条，它直接否掉了"按 `[dungeon type]` 建枚举"这个起点。

`[dungeon type]` 只出现在 **284 / 839** 个副本上，取值只有六种，而且它标记的是特例家族，
不是常规情况：

| `[dungeon type]` | 数量 | 备注 |
|---|---:|---|
| （无此标签） | **555** | 含教程副本与全部 Act 副本 |
| `village attack` | 266 | id 20000 起 |
| `advance altar` | 13 | |
| `tower of death` | 2 | id 11000 / 11001 |
| `lost land` / `tower of boss` / `wedding couple` | 各 1 | |

**绝望之塔不在这张表里**——它由另一个标签 `[tower of despair]` 标记（100 个副本），
`[tower of despair sao]` 同样 100 个。

上游据此把"类型"拆成**四种彼此无关的判据**：

| 类型 | 判据 | 来源 |
|---|---|---|
| 死亡之塔 | `[dungeon type] == "tower of death"` | 归档标签 |
| 绝望之塔 | `[tower of despair]` + 角色进度表改写入场 id | 归档标签 + 存档 |
| 特殊副本（海上追击 / 时空裂缝 / 夺金 / 封印森林 / 根特潜入） | **匹配资源路径子串**（`SeaChase`、`TimeBreak`、`Seizemoney`、`SealForest`、`GentInfiltrate`） | 文件路径 |
| 互动训练室 | 路径含 `*trainingroom*` **且**结构满足（经验倍率 `0.001`、队伍上限 1、有 `[designate dungeon difficulty]`） | 路径 + 结构 |

**所以"副本类型"是服务端的业务分类，不是内容里的声明。** 任何按单一枚举建模的设计，
建的都是一个归档里不存在的东西。

## 二、真正的分界是**结构家族**，不是类型名

比较三个副本的标签集合就能看出来：

| 副本 | 标签数 | 有迷宫字段？ |
|---|---:|---|
| 10000 教程（`Tutorial/Tutorial.dgn`，卡利丛林） | 19 | **有**：`[maze info]` `[size]` `[greed]` `[map specification]` `[start map]` `[boss map]` `[pathgate object]` |
| 1 洛兰（`Act1/Lorien.dgn`） | 27 | 有，另加难度、事件怪、`[schedule]`、`[revision table]` |
| 11000 死亡之塔（`Towers/TowerOfDeath.dgn`） | 25 | **没有**。用 `[death tower map indexes]`，另有 `[tower dialog]` `[tower recovery]` `[tower fp cubepiece]` |

`[maze info]` 覆盖 **836 / 839**。所以绝大多数副本属于同一个结构家族，
而死亡之塔是**结构上另一种东西**——它没有迷宫可选，这正是上游给它单开一条
不走普通 `START_MAP` 的路的原因，而不是因为它"类型不同"。

组队相关的标签也不是类型，是修饰：`[recommend party]` 180、`[limit party count]` 49、
`[raid dungeon]` 14、`[necessary party]` 6。

## 三、教程副本是最朴素的那一个

19 个标签里没有难度、没有事件怪、没有 `[revision table]`、没有任何队伍标签，
`[minimum required level] 1`、`[no fatigue]`。迷宫是单个 5×3 网格：

```text
[size] 5 3
[greed] AAAAAAIIAA / BBFFFFPPEE / AAAAAACCAA     ← 每格 2 字符
[map specification] (0,1)=61000 (1,1)=61001 (2,1)=61002 (3,1)=61003
                    (4,1)=61005 (3,2)=61004 (3,0)=61006
[start map] 0 1        [boss map] 3 0
```

**意义**：把教程跑通不需要任何类型建模，它走的就是默认的迷宫家族路径。

## 四、上游 `0x0010` 的成功路径

`DungeonEntryHandler.HandleSelectDungeonCore`（`Network/Handlers/Dungeon/`，1336 行中的一段）：

1. 解析请求（副本 id / 难度 / 两个深渊标志）
2. **绝望之塔改写入场 id**：`TowerOfDespairProgress.ResolveEntryDungeonId(cid, requestedId)` 可以把
   请求的 id 换成另一个
3. 连锁副本来源解析
4. **运行槽未变检查**（并发守卫，全程反复做：每次 await 之后都重验 `IsCurrentDungeonRun`）
5. 载入进行中与已通关任务
6. **入场资格**：`WorldMap.EvaluateDungeonAdmission(dungeonId, activeQuestIds, clearedQuestIds)`
   —— 任务门禁；不通过则回 `DungeonAdmissionReject.DungeonUnavailable`
7. 队伍查询 + 人数（1–4）
8. **奖励策略** → `DungeonInteractionPolicy` 决定是否允许组队状态入场
9. 经验加成快照
10. **类型分流**：死亡之塔走 `BeginTowerRun` + 专属入场帧，然后 return
11. 否则 `BeginRun` → 深渊模式判定 → 任务快照 → **选迷宫** → 随机 boss 位置 → 随机出生点 →
    解析起始房间 map id → 随机化对象 → 通关条件 → 竞技场/血色祭坛准备 → 冻结选择快照 →
    激活运行 → 发帧 → **把同队队员也拉进同一实例**

### 客户端要的帧

`SendDungeonSelectPacketsTo`：

| 帧 | 内容 |
|---|---|
| NOTI `0x001C` DUNGEON_INFO | 副本 id、难度、迷宫序号、boss 格坐标、深渊房坐标、附加小地图图标组 |
| 选择态 | `DungeonMechanismCoordinator.SendSelectionStateAsync` |
| **START_MAP** | `DungeonMapHandler.SendStartMapAsync` —— 把角色放进第一个房间 |
| NOTI `0x019F` | 助战标签（无则发空 body） |

`START_MAP` 自身还按家族分叉：血色祭坛发 `START_BLOOD_MAP`、绝望之塔追加两条
`USER_APC_INFO_TOD`、竞技场追加 `TOURNAMENT_INFO` + `TOURNAMENT_MAP_INFO`、
深渊追加 `0x02A6` 怪物信息。

## 五、当前实现（2026-09-14）

普通 maze 家族已有独立运行存储、房间移动、actor、死亡去重、掉落和结算。普通与任务关联运行
复用这些机制，差异由三处类型化值和规则表达：

| 边界 | 当前实现 |
|---|---|
| 入口意图 | `0x000F=0` 为交互式普通选择，非零为定向 dungeon；绑定当前角色的精确连接代次，并由下一次 `0x0010` 一次性消费 |
| 内容资格 | `PvfDungeonDefinitionSource` 保留 dungeon/maze 的 `[quest connection]`；两字段连接的难度缺省为 `-1`。普通候选只含无连接 maze，定向候选按活动/完成任务和难度过滤 |
| 运行目的 | `DungeonLayout.Purpose` 在入口固定为 Ordinary 或 QuestLinked，并随 `DungeonRunState` 保存；后续不重新读取任务状态改写本局 |
| 任务目标 | `[hunt enemy]` 的 enemy type 1/2 是服务器 actor 事实；type 3 是客户端地图脚本的被动对象报告，但只在同 quest/dungeon/difficulty 的 Active QuestLinked 运行中接受声明通道。经验与服务端目标推进使用同一事务，失败释放精确死亡 claim 供重试 |
| 完成/结算 | 普通运行保持 boss 房清空判据；活动任务运行还要求冻结的关联任务 trigger 到 0，完成态连接要求该任务已完成；`PLAY_RESULT.questMaze` 来自冻结目的 |
| 退出 | 未证实的卡牌差异继续共享。GiveUp 与 EPLP 复用同一回城投影 |

网络 session 只负责找到当前角色和回复当前连接，不进入 maze 选择或运行目的；`PartyCategory` 也不
充当副本运行类型。

## 六、待决与风险

- **并发守卫是上游那段代码里最密的东西**：每个 await 之后都重验运行槽与房间身份。
  它防的是"选本过程中被挤号/换本"。我们的命令总线已有账号级独占锁，是否足以替代这一整套
  重验，需要单独判断——两者防的不是同一件事（锁防并发进入，重验防中途失效）。
- **上游的 `0x0010` 成功路径里没有 CMD 响应**；客户端为它注册了 handler `0x008C56F0`，
  按已有判据"注册不等于必须回"，但这一条尚无实机对照。
- 第 2 步（绝望之塔改写 id）会让"客户端请求的副本"与"实际进入的副本"不同。
  这不是教程需要的，但它说明**入场 id 不能当成不可变输入**。
- 2026-09-14 双路径实机已确认：普通入口 `0x000F=0 -> maze 0 -> Ordinary`；任务 1776 剧情入口
  `0x000F=144 -> maze 1 -> QuestLinked(1776, Active)`。同场还确认 1776 在 boss 房发送
  `SET_QUEST_TRIGGER selector=0x10 decrement`；其 target type 3 是 map 中的 Passive Object，不能按
  monster rank 解释。

## 八、疲劳值规则（用户 2026-08-27 口述，尚未实现）

现状：`CharacterRosterService.cs:214` 里写死 `new CharacterFatigueState(188, 0)`，全仓没有任何地方
递减它。今天有一个**不记账的接缝** `ICharacterFatigueDomainService`（实现
`UnaccountedCharacterFatigueDomainService`：答一对定值，报给它的一律丢弃），进本处
`SelectDungeonHandler` 会向它报一次消耗——报告落在接缝上，发出去的那个 188 不受影响。
规则本身已经定了，实现时照这个来：

### 扣减规则

| 规则 | 说明 |
|---|---|
| 何时扣 | **进入一间「新房间」时扣 1** |
| 什么算新房间 | **有怪且还没打过的房间**。已清过的房间不算 |
| 走回头路 | **不扣**。打完再回去是旧房间 |
| 下界 | **最小 0，不会变成负数** |
| 归零之后 | **不能再进副本** |

留意两处已有的东西正好对得上：`MOVE_MAP` 的处理里已经有 `first=True/False`（是不是第一次进这间
房），房间清空也已经在跟踪（boss 房清空会开出口）。「有怪」这一条要看房间的 actor 列表非空——
空房间不该扣。

### 总量与恢复

同一次口述里还有三条，此前只写在代码注释里、`Docs/` 与 `protocol/` 均无出处，现在落在这里，
**代码注释与本节互相指得到**（`ICharacterFatigueDomainService` 的 `<remarks>` 引本节，本节引它）：

| 规则 | 说明 |
|---|---|
| 每日总量 | **基础 156**，另有**黑钻 +32**；156 + 32 = **188**，正是实机会话发出的那个数 |
| 恢复 | **抗疲劳药剂**可以把已花掉的点数还回来（接缝上的 `RefundAsync` 就是给它留的，今天无人调用） |
| 重置 | **每日重置** |

**「每日重置」的具体时刻仓内无记载。** 代码注释与本节都只写到「每日」；几点重置、按哪个时区，
至今没有任何仓内出处，实现之前要先问所有者补上，不要凭印象填。

### 与档案标记的已知冲突（今天无后果，记真账之前必须解决）

副本自身有 `[no fatigue]` 标记（`DungeonDefinition.ConsumesFatigue` 已经读出来了），教学本就是
不耗疲劳的，实现时别漏。**而今天的报告点没有问它**：
`SelectDungeonHandler.FatigueAnEntryCosts` 是个**无条件报 1** 的占位常量
（它自己的 `<remarks>` 把这一点写明了），而全仓 `ConsumesFatigue` 的唯一读者是
`PvfDungeonDefinitionSourceTests.cs:32` 那条钉解析的判据 —— 它断言教程本 `10000` 为 `false`。
也就是说：**按上面写下的规则，教程本进本该扣 0，占位值报的是 1。**
今天接缝不记账，所以这个冲突在线上无可观察后果；一旦开始记真账，**调用点必须先问档案
这本扣不扣**，还要按上表判断那间房有没有怪、是不是第一次进。

## 七、引用

- 归档探针：`protocol/patterns/data/probes/` 下的 `pvf-dungeon-list-20260817.md`
- 协议侧：`protocol/game/client-to-server/0x0010.md`、`0x000F.md`、
  `protocol/patterns/flow-dungeon-entry.md`
- 上游文件：`Network/Handlers/Dungeon/DungeonEntryHandler.cs`、`DungeonMapHandler.cs`、
  `DungeonRunLifecycle.cs`、`Game/DeathTower/DeathTowerData.cs`、
  `GameWorld/SpecialDungeonDefinitionCatalog.cs`、`GameWorld/DungeonRewardPolicyData.cs`

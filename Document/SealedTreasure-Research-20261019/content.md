# 封印的藏宝地（DGN 3557）PVF 内容与当前生产实现差距

日期：2026-10-19
研究对象：`artifacts/scheduled-notice-pvf-20261019/Script.server.pvf`
SHA-256：`C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06`

## 1. 结论

当前服务 PVF 已包含 DGN 3557 的完整静态内容：5×5 固定房间、257 个怪物放置、34 种房间物件、四属性精灵与封印石 ACT、中央宝箱、专属掉落池、两种门票、每日补给声明、世界地图/活动日程引用以及 3 个相关任务文件。机器可读的逐 token、逐房间证据在 [`content/inventory.json`](content/inventory.json)，生成程序在 [`content/PvfContentProbe/Program.cs`](content/PvfContentProbe/Program.cs)。

当前生产源码的通用地下城路径具备读取和物化 3557 拓扑、难度、最低等级、经验倍率、普通怪物、普通/特殊物件、怪物声明掉落、中央宝箱声明掉落和白金宝箱开箱池的支持面；但它没有 3557 专用入口路由，也没有解释以下内容：`[event required item]` 备选门票、单人限制、禁止练习、`[pathgate object]`、`[schedule]`、`[spicies dungeon]`，以及精灵/封印石 ACT 的 party-symbol 跨房状态。本轮探针只引用 `Dfo.Pvf`，没有逐一调用 `IDungeonCellFactory` 物化 25 张图，也没有做入口或客户端可玩性集成测试，因此这只是源码支持面，不是“生产已实际生成完整 3557”的运行证据。

因此，“内容存在”不等于“现服可按活动原规则游玩”。最大的可见差距是：

1. 通用入场只接受并消耗 `10003196`，不会把每日补给的 `10003235` 当作备选门票。
2. PVF 明确声明单人，但通用入场没有按该字段拒绝多人队伍。
3. 精灵死亡写入 party-symbol 参数、对应封印石销毁的循环是客户端 ACT 内容；服务端没有 party-symbol 状态或同步语义。单人客户端是否能完全本地执行，本轮未实机验证。
4. 三个任务文件均未列入 `n_quest/quest.lst`，所以当前生产任务目录不会加载它们。
5. PVF 没有 `[no fatigue]`。当前通用实现会把入场房及每个首次进入的房间各扣 1 点疲劳；这与历史攻略“不耗疲劳”的说法冲突，不能用历史说法覆盖当前 PVF 与代码事实。

本文证据分为：

- **ContentStatic**：目标 PVF 的静态 token、列表解析和文件关联。
- **CurrentImplementation**：当前 HEAD `e168f0ccd53649bbf6b577de46bea5eb0ef7e661` 的生产源码行为。
- **Unknown**：未运行服务、未读生产数据库/GM 覆盖、未启动 A21 客户端、未抓取本功能协议，因此不能提升为运行态或 ClientVerified。

## 2. 可复现取证

探针直接引用仓库当前的 `Dfo.Pvf`，没有使用旧 70 版本 reader，也没有修改 PVF：

```powershell
dotnet run --project Document/SealedTreasure-Research-20261019/content/PvfContentProbe/PvfContentProbe.csproj `
  -c Release -- `
  artifacts/scheduled-notice-pvf-20261019/Script.server.pvf `
  Document/SealedTreasure-Research-20261019/content/inventory.json
```

两次执行均成功，最终摘要为：`maps=25 monsters=47 objects=34 refs=152 customFiles=18`。`inventory.json` 还保存了 PVF 文件数、哈希、DGN/MAP/MOB/OBJ/STK/QST 原始 section/token、列表解析结果、引用上下文、每日补给、日程与 WDM，便于复核本文没有手工猜测字段边界。

两个客户端目录的 `Script.pvf` 哈希与本次服务 PVF 不同；本文只陈述服务 PVF 内容，不把它外推为任一客户端当前实际加载的内容。

## 3. DGN 3557 静态定义（ContentStatic）

来源：`dungeon/Event/chn_Species_Dungeon/chn_Species_dungeon.dgn`，完整 token 见 `inventory.json` 的 `dungeon.dump`。

| 字段 | 当前 PVF 值 | 解释边界 |
|---|---:|---|
| 名称 | 封印的藏宝地 | ContentStatic |
| 类型标记 | `[spicies dungeon]` | 原拼写如此；生产 parser 未读取 |
| 最低/基准等级 | 70 / 73 | 通用 parser 已读取 |
| 推荐等级 | 70..86 | 生产定义未投影此字段 |
| 经验倍率 | 1.2 | 通用 parser 已读取 |
| 普通门票 | `10003196 ×1`，consume=1 | 通用 parser/入场已读取和消耗 |
| 活动门票 | `10003235 ×1`，consume=1 | `[event required item]`，通用 parser 未读取 |
| 指定难度 | 2 | 通用 parser 已读取 |
| 队伍人数上限 | 1 | `[limit party count]`，通用 parser 未读取 |
| 练习模式 | 禁止（1） | `[prohibit practice]`，通用 parser 未读取 |
| pathgate 模板 | 621..630 | 通用 parser 未读取 |
| 迷宫 | 5×5，25 格全部有唯一 MAP | 起点 `(0,0)`，Boss `(4,4)` |
| 特殊物件掉落组 | group 0，level=-1，`10003717` weight=3500 | 中央宝箱 action 使用 group 0，specific attempts=1 |
| 日程引用 | 2331 | 字段语义未由当前生产代码解释 |
| `[no fatigue]` | 不存在 | 当前 parser 默认 `ConsumesFatigue=true` |

`[greed]` 原值为：

```text
JJFFNNFFMM
 LLFFPPFFOO
 LLFFPPFFOO
 LLFFPPFFOO
 DDFFHHFFGG
```

`worldmap/granflorishillside.wdm` 把它放在“格兰之森山麓”，`[dungeon]` 原序列为 `100 -1 3557 -1 4123 -1 3541 -1 3700 -1 3900 -1`。`etc/chn_schedule.etc` 含 3557 行 `3557 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 -1`。这些都是 ContentStatic；各操作数的原生业务语义没有在本轮从客户端或服务协议验证。

`etc/chn_fatigueaccelchkdungeon.etc` 还把 3557 列入 `[unable dungeon]`。这只能说明疲劳加速器限制，不能据此推导“不消耗疲劳”。

## 4. 25 个房间与怪物/物件

全部 257 个怪物放置均为：`relativeLevel=1`、`level=0`、`randomDrops=1`、`fixedDrops=1`、`[fixed]`；其中 256 个普通级，只有 `56051 派普·乔` 为 Boss 级。下表覆盖全部 47 种怪物模板；每个物件的精确坐标、每条放置原值与完整普通物件列表在 `inventory.json.maps`。

| MAP | 怪物放置 | 机制/关键物件 |
|---:|---|---|
| 25313 | 伪装树魔×3、悲鸣蚂蚁×6、丛林蜘蛛×4 | 起点；大型草×15 |
| 25314 | 石飞虫 `61325`×11、石飞虫 `61507`×22、悲鸣蚂蚁×7 | 大型草×5 |
| 25315 | 魔界花袄索×5、悲鸣蚂蚁×6、丛林蜘蛛×7 | 大型草×21 |
| 25316 | 魔界花袄索×4、树精长老×1、树精×3、木本植物×2、暗精灵 `110348`×1 | 暗精灵房 |
| 25317 | 悲鸣蚂蚁×5、树精×4、丛林蜘蛛×2 | 草物件 |
| 25318 | 丛林僵尸×6、魔女莉斯×1、嗜血盗尸者×4、火精灵 `110349`×1 | 火精灵房 |
| 25319 | 丛林僵尸×6、鬼魂×1、嗜血盗尸者×4 | 树/桶/箱堆 |
| 25320 | 骸骨投掷兵×2、堕落守护者×4、民兵队长贺加斯×1 | 水封印石 `54294` |
| 25321 | 丛林僵尸×2、鬼魂×4、嗜血盗尸者×7 | 树/桶 |
| 25322 | 鬼魂×3、哀嚎暗精灵僵尸×3、悲鸣暗精灵僵尸×3 | 树/桶/箱堆 |
| 25323 | 猫妖×8、牛头前锋×2 | 无普通物件 |
| 25324 | 牛头前锋×9、波动猎手伽雷斯×8 | 暗封印石 `54291` |
| 25325 | 空骨鱼×6、光之城主赛格哈特 `64793`×6、烈焰彼诺修 `64794`×10 | 中央箱 `15150`、石像/骸骨/岩石/木十字架 |
| 25326 | 暗咒猫妖×6、牛头前锋×2 | 光封印石 `54293` |
| 25327 | 毒猫王×7、牛头前锋×2 | 无普通物件 |
| 25328 | 烈焰哥布林×1、冰霜哥布林×4、落雷凯诺×1、罪恶布法罗队长×1 | 无普通物件 |
| 25329 | 哥布林×1、烈焰哥布林×6、漫游者麦吉×1 | 无普通物件 |
| 25330 | 冰霜哥布林×3、落雷凯诺×1、祭司凯迪尼斯×1 | 火封印石 `54292` |
| 25331 | 哥布林×1、投掷哥布林×1、冰霜哥布林×1、祭司尼格兰斯×1 | 小石柱×9 |
| 25332 | 投掷哥布林×2、冰霜哥布林×1、森林魔法师×1、水精灵 `110351`×1 | 水精灵房 |
| 25333 | 妖变的石像×2、饿鬼达鲁×1 | 草/石柱/安眠石 |
| 25334 | 妖变的石像×2、咒术师纳扎罗×1、光精灵 `110350`×1 | 光精灵房 |
| 25335 | 妖变的石像×2、贪婪的多佩里温×1 | 草/石柱/安眠石 |
| 25336 | 妖变的石像×4、混沌心魔×1 | 草/石柱/安眠石 |
| 25337 | 派普·乔 `56051`×1、咒术师纳扎罗×1 | Boss 房；草/石柱/安眠石 |

房间实际使用的 34 种物件 ID 为：`243,244,249,272,284,301,302,306,640..651,818,822,840,8453,8471,8472,15150,54199,54200,54214,54291..54294`。其中 621..630 是 DGN 指定的路径门模板，不是 MAP `[passive object]` 放置行：

| ID | 模板名 |
|---:|---|
| 621 / 622 / 623 | 浅栖之地左侧门 / 左侧领主门 / 左侧墙 |
| 624 / 625 / 626 | 右侧门 / 右侧领主门 / 右侧墙 |
| 627 / 628 | 上侧门 / 上侧领主门 |
| 629 / 630 | 下侧门 / 下侧领主门 |

## 5. 四属性封印循环

四个属性精灵与四块封印石围绕中央房间成对分布：

| 属性 | 精灵（房间） | ACT 原始参数 | 封印石（房间） |
|---|---|---:|---|
| 暗 | `110348 瑟冥特克`（25316） | 1 | `54291`（25324） |
| 火 | `110349 亚德炎`（25318） | 2 | `54292`（25330） |
| 光 | `110350 雷沃斯`（25334） | 4 | `54293`（25326） |
| 水 | `110351 冰奈斯`（25332） | 8 | `54294`（25320） |

18 个专属 MOB/OBJ/ACT 文件已全部收进 `inventory.json.customBehaviorFiles`。四个精灵的死亡 ACT 都先检查 party symbol：第一次触发执行 `SET PARTY SYMBOL 0`，再执行对应的 `SET BIT PARTY SYMBOL 1/2/4/8` 并显示“某属性封印已解除”；重复路径检查对应参数的既有状态。四块封印石 ACT 用同一原始参数执行 `CHECK BIT PARTY SYMBOL`，命中后 `DESTROY`（光封印石另显示一次消息）。封印石均是不可穿越、blocking area=200 的普通层物件。`BIT` 是 ACT 指令的原始名称；本轮没有追到 native 实现，不能断言 `1/2/4/8` 是掩码还是位序号，也不能把四元素状态编码成 `0x0F` 当作已证事实。

这是明确的 **ContentStatic 客户端脚本状态机**。当前 `Server/src` 与测试源码中没有 `party symbol`、`CHECK BIT PARTY SYMBOL` 或 `SET BIT PARTY SYMBOL` 的实现；通用物件源只投影物件代码/坐标，并只对内建电梯控制器附加机制。因此：

- 通用源码路径具备把四块石头投影为房间物件并处理通用物件销毁声明的能力；本轮没有做 25 图工厂集成或客户端收包验证；
- 服务器不会根据四个精灵死亡维护或广播这四个原始参数对应的状态，也不会据此控制路径门；
- 因 DGN 同时声明单人，原活动可能依赖单客户端本地 ACT 完成循环，但当前 A21 是否确实这样运行、断线/重进如何恢复、多人同步如何表现均为 **Unknown**。

## 6. 门票、每日补给、商城与次数

### 6.1 两种导航人偶

| ID | 名称 | 类型/交易 | stack limit | PVF 用途 |
|---:|---|---|---:|---|
| 10003196 | 导航人偶 | `[quest]` / `[trade]` | 2 | `[required item]` |
| 10003235 | 导航人偶 | `[quest]` / `[trade]` | 1 | `[event required item]`；每日补给 |

`etc/chn_server_limititemusageinfo.etc` 的精确 tuple 为 `10003235 1 "2030-01-01 06:00:00" 0`。以本轮日期看规则未过期，mode 0 在当前实现中是 `RefillToTarget`，目标数量为 1。

这里必须区分默认能力与运行态：`PvfEntryLifecycleContentSource` 会解析 tuple（`PvfEntryLifecycleContentSource.cs:62-75,101-140`），`DailyRefillConfigurationDomainService.EffectiveAsync` 会合并 PVF、`DisabledPvf` 与 `Custom` 覆盖（`DailyRefillConfigurationDomainService.cs:75-83`），选角入口会实际调用补给（`SelectCharacterEntryInventoryDomainService.cs:103-114`）。本轮未读取生产 GM 补给配置、玩家领取记录或数据库，所以只能说“默认代码路径会在选角批次补到 1”，不能说某个角色已经拿到票。

`10003235` 不属于当前实现的两个独立 06:00 周期物品；`DailyRefillRuleService` 仅对 `690001556`、`2680738` 使用独立 game-day 周期，其余按 UTC+8 日历日期判断（`ItemArrivalRuleService.cs:163-188`）。PVF tuple 中的 `06:00:00` 是到期时间的一部分，不会自动把这张票的每日边界改成 06:00。

### 6.2 商城与入场次数

`etc/cerashop.etc` 和 `etc/eventcharacter/cerashop.etc` 都引用 product `102494`、item `10003196`、数量 1，并出现数值 `100000` 与标签“1个”。这强烈像一条 100000 金币购买普通门票的静态商品行，但本轮没有另行验证该旧表每一列的客户端语义，故不把 `100000` 提升为已验证价格。

没有发现 3557 专属生产 quota 路由。当前可见次数约束来自门票持有/消耗；`10003235` 自身 stack limit=1、默认每日补到 1，也不能自动证明原服就是“每日仅 1 次”。

生产缺口是结构性的：`PvfDungeonDefinitionSource` 只声明并解析 `[required item]`（`PvfDungeonDefinitionSource.cs:42-55,210-223`），`DungeonAdmissionDomainService` 只检查/消耗投影到 definition 的 required items（`DungeonAdmissionDomainService.cs:88-115`）。所以每日免费票 `10003235` 虽能被补给，却不能作为 3557 的当前通用入口凭证；入口会要求 `10003196`。

## 7. 中央箱、掉落与开箱

### 7.1 中央箱 `15150`

MAP 25325 的特殊物件：`code=15150, x=578, y=322, z=0`，action 为 `[item] 0 1 -1 -1 -1`。对象模板是 `ActionObject/Map/Mission/secretbox2.obj`，不可穿越，`[hp max]=5000`、`[hp destroy]=1`、team=100。

DGN group 0 是：level override `-1`、单条物品 `10003717`、weight `3500`。当前 `PvfDungeonRoomDropSource` 会把特殊物件 action 与 DGN 掉落组连接（`PvfDungeonRoomDropSource.cs:33-64,139-188`）；`specific=1`，`random=-1` 被归一为 0 个通用随机尝试。`ObjectDropRuleService` 使用固定 10000 票并进行一次独立 specific 抽选（`ObjectDropRuleService.cs:11-22,25-40`），所以 **CurrentImplementation 的名义概率为 3500/10000，即 35%/次**。这只描述当前服务算法，不证明 A21 原服概率或端点细节。

当前服务在创建房间时冻结这个声明掉落，并把它作为 `DeclaredObject` ground item 放进首次房间投影（`DungeonCellFactory.cs:29-43,131-137`；`DungeonVisitDomainService.cs:179-207`）；物件销毁事务自己的 pending loot 只来自 quest loot（`DungeonCellFactory.cs:119-129`）。因此“宝箱被打碎后才出现白金箱”的原生活动表现尚未由当前服务端状态机明确建模，需用客户端实测确认当前投影是否仍由客户端按 object index 延迟显示。

### 7.2 白金宝箱内容

`10003717` 是可交易“封印的白金宝箱”；`10003195` 是 `[free]` 的同名版本。两者当前 PVF 的 `[booster info]` 完全相同：1 抽、147 条奖励、每条数量 1，总权重 100000；前 4 条（`2540006..2540009`）权重各 690，其余 143 条权重各 680。完整 147 个物品 ID/权重在 `inventory.json.items`。

当前 `PvfPackageOpeningDefinitionSource` 支持 `[booster]`，按 `draw count + (item,weight,count)` 解析池（`PvfPackageOpeningDefinitionSource.cs:262-282,371-405`）；`PackageOpeningRuleService` 按总权重抽取一次并准备奖励（`PackageOpeningRuleService.cs:22-56`）。因此此开箱内容在源码默认能力范围内；本轮没有执行实际开箱或检查每个奖励模板的运行时可创建性。

### 7.3 怪物声明掉落

47 种模板中，当前 PVF 有非空 `[item]` 的模板如下；每对数值是 `itemId/weight`：

| 怪物 ID | 名称 | 声明掉落 |
|---:|---|---|
| 1 | 哥布林 | 1000/50，1047/200，1004/100 |
| 2 | 投掷哥布林 | 1000/50，1047/200，1004/100 |
| 7 | 烈焰哥布林 | 1062/50，1065/100，1047/200 |
| 8 | 冰霜哥布林 | 1062/50，1065/100，1047/200 |
| 9 | 落雷凯诺 | 1001/50，1047/200 |
| 210 | 丛林僵尸 | 1013/100，1066/100，1047/100 |
| 220 | 空骨鱼 | 1063/50，1002/100，1047/200 |
| 5001 | 猫妖 | 1062/50，1065/100，1047/200 |
| 5004 | 毒猫王 | 1013/200，1047/200，1065/100 |
| 56160 | 民兵队长贺加斯 | 3227/4225 |
| 62639 | 树精 | 3113/1000 |
| 64793 | 光之城主赛格哈特 | 1063/50，1002/100，1047/200 |
| 64794 | 烈焰彼诺修 | 3016/1500，1001/50，1002/100，1047/200 |
| 110348 | 瑟冥特克 | 3114/1600 |
| 110349 | 亚德炎 | 4084/800 |
| 110350 | 雷沃斯 | 3115/1600 |

`110351 冰奈斯` 的 `[item]` 为空。生产源会遍历 `monster.lst` 并解析所有非空 pair（`PvfMonsterDeclaredDropTableSource.cs:15-58`）；每次在 `max(10000, 权重和)` 上选最多一条（`MonsterDeclaredDropRuleService.cs:24-46`）。所以 1600 在当前实现中通常表示 16% 名义率，多个条目的剩余区间为空。每个 MAP 放置又声明 `randomDrops=1`，生产 `ToActor` 只把该值带入掉落上下文，未使用同一行的 `fixedDrops=1`（`PvfDungeonRoomActorSource.cs:59-67`；原字段解析在 `PvfMapActorDeclarationParser.cs:105-129`）。这意味着固定掉落计数目前是未实现/未解释字段。

## 8. 相关任务

PVF 中存在三个文件，但三个都 **未列入** `n_quest/quest.lst`：

| ID | 名称/等级 | 类型与原值 | 奖励 |
|---:|---|---|---|
| 20747 | 调查封印的藏宝地；daily | `[condition under clear]`，`[int data] 3557 -1` | 无实际奖励行 |
| 20748 | 揭秘之旅；daily | `[hunt enemy]`，`3557 -1 15150 3 1` | 无实际奖励行 |
| 20749 | 被怪物隐藏的宝箱；daily；前置 20748 | `[hunt enemy]`，`3557 -1 15150 3 1` | 无实际奖励行 |

后两个目标可按当前通用 hunt-enemy 五字段语法解释为：DGN 3557、任意难度、目标对象代码 15150、敌人类型 3（passive object）、次数 1；`QuestKillObjectiveParser` 确实支持 passive object（`QuestKillObjectiveParser.cs:8-76`）。但生产目录只从 `n_quest/quest.lst` 建表（`PvfQuestDefinitionSource.cs:71-75,151-178`），所以这三个孤儿文件不会被发现，通用物件任务进度能力也不会让它们自动生效。

## 9. 生产实现覆盖矩阵

| 功能 | 状态 | 证据/影响 |
|---|---|---|
| DGN 3557 列表发现 | 源码支持 | 通用 `dungeon.lst` + DGN parser |
| 5×5 拓扑、起点、Boss、greed | 源码支持 | `PvfDungeonDefinitionSource.cs:255-315` 等 |
| 最低等级 70、basis 73、EXP 1.2 | 源码支持 | `PvfDungeonDefinitionSource.cs:114-178` |
| 指定难度 2 | 源码支持 | `PvfDungeonDefinitionSource.cs:185-204` |
| 普通门票 10003196 | 源码支持 | definition + admission 检查/消耗 |
| 活动门票 10003235 作为替代 | **缺失** | parser 不读取 `[event required item]` |
| 10003235 默认每日补到 1 | 有能力，运行态未知 | 可被 GM DisabledPvf/Custom 覆盖，未读 DB/玩家账本 |
| 单人限制、禁止练习 | **缺失** | 通用 parser/entry 未读取这些字段 |
| DGN 活动日程与 WDM 显示门 | **缺失/Unknown** | 静态内容存在，生产未解释 |
| 疲劳 | 当前会消耗 | 无 `[no fatigue]`；入场和每个首次房间各扣 1（`DungeonVisitDomainService.cs:91-120,142-176`） |
| 25 MAP 与 257 怪物 | 源码具备支持面，未集成验证 | 通用 actor parser/room factory；本轮未逐图调用 `IDungeonCellFactory` |
| 34 种普通/特殊物件 | 源码具备基础投影能力 | `PvfDungeonRoomDropSource.cs:68-100,139-188`；未做客户端验证 |
| pathgate 621..630 | **未按 DGN 字段建模** | 通用拓扑只按 greed 允许移动 |
| 四精灵/封印石四元素状态 | **服务端缺失** | ACT 有客户端脚本；原始参数为 1/2/4/8，native 编码语义 Unknown |
| 中央箱声明掉落 | 已解析；时序待实机验证 | 当前源码名义 35%，房间创建时冻结/投影 |
| 怪物 `[item]` | 源码支持 | 声明池 + 当前通用掉落链 |
| MAP `fixedDrops=1` | **未使用** | `ToActor` 只传 `RandomDropCount` |
| 两种白金箱 booster 池 | parser/roll 源码支持 | 未执行端到端开箱 |
| 20747..20749 任务 | **不可发现** | 文件未进入 `quest.lst` |
| DGN 3557 专用入口/次数服务 | **不存在** | `EnterDungeonCommandHandler.cs:20-53` 最终落通用 normal/dimension 路径，源码无 `3557` 特判 |

## 10. 建议的实现与验证顺序

1. 先把 `[event required item]` 投影为明确的“备选入场材料”，并为 DGN 3557 的 `10003196`/`10003235` 二选一消费写领域测试；不要把两张票误实现为同时需要。
2. 投影并执行 `limit party count=1`、`prohibit practice=1`；为 2 人队伍和练习模式增加拒绝证据。
3. 明确 party-symbol 的权威端。若保持客户端本地 ACT，需要用真实 A21 单人运行验证四个精灵、石头、跨房返回与断线重进；若协议显示服务端同步，再按 native 证据建立参数化 run state 和相应广播，不预设其为 4-bit 掩码。
4. 核对 `[pathgate object]` 在 A21 进房包中的选择/门状态，避免通用 greed 拓扑让玩家绕过封印。
5. 先取得 20747..20749 在原生客户端/原服中的可达证据，例如 NPC/日程入口、可接受任务列表或实机任务交互；只有证据表明它们原生可达时，才评估加入服务 PVF 的 `quest.lst`。没有可达证据时应保持孤儿/失效活动残留状态，不能仅凭文件存在自动开放任务。
6. 实机核对中央箱的 35% 与出现时机。目前 35% 只是当前服务算法，且源码在房间创建时冻结 ground drop；需要验证客户端是否把 object-index 关联解释为“打碎后出现”。
7. 通过正常入场实际跑完至少一条从起点到 Boss 的合法路线，记录疲劳、门票、房门、封印、宝箱、掉落、开箱和任务事件；完成前应维持 `CurrentImplementation/Unknown`，不要标记 ClientVerified。

## 11. 本轮未做

- 未启动服务或客户端，未执行登录、入场、战斗、开箱、断线重连或抓包。
- 未读取/修改生产 DB、GM 每日补给覆盖、玩家库存或领取账本。
- 未修改生产代码、PVF、客户端、服务配置或 GitHub。
- 未把历史攻略、源码单测或静态 ACT 当作 A21 实机行为证据。

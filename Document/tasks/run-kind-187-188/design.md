# #187 + #188 设计稿：run 玩法种类与魔数能力化

主干基线 `bb41a19a8`。issue 写于 `a88fceaf`，下文行号按当前 main 重新定位。
状态：2026-10-29 用户已裁定，§5 四项均采用推荐方案：分 4 个 PR 依次完成；清场改为同一事务内的种类钩子；
issue 未列的同类魔数一并删除；统一“是否在副本中”的来源，并修正 Boss 塔漏判（单独提交）。

## 1. 现状（按当前 main 核对）

PR #220 已把 13 个命令的“按所在位置试探两塔”收成 `DungeonLocator` / `DungeonPlace`
（Protocol/Game/Dungeons/DungeonLocator.cs），本设计不再处理那部分。仍未解决的有：

| 信号 | 当前位置 |
|---|---|
| `DungeonLayout` 上 8 个互斥的可空计划：Sao/Despair/Tournament/Altar/Guardian/Hell/Mirror/GiantDragonTimeReward，外加 `EntryKind`、`NoGiveupPenalty` | Abstractions/World/IDungeonStructureRuleService.cs:127-146；约 70 处 `Layout.X` 用点散在 28 个文件 |
| 识别口径不一 | SAO 看 `Layout.Sao`，南部溪谷/陶/封印宝藏/金银角只有 `Supports(id)`，赫拉斯在协议层写死 3900 |
| 入场 15 路 `Supports ? EnterAsync :` 三元链 | Protocol/Game/DungeonEntry/GameSelectDungeonCommand.cs:164-198 |
| 清场一半硬编码 | MonsterDeathDomainService.cs:92-97 直接调南部溪谷、SAO 的 `CompleteClearAsync`；`DungeonMapCleared` 另有 4 个订阅者（任务、镜像、婚姻×2） |
| 种类判定散在 handler | 复活 DungeonRevivalDomainService.cs:18-37；团灭 DungeonReturnTimerCommands.cs:56-98；首领奖励 MonsterDeathDomainService.cs:70；开 clear DungeonClearDomainService.cs:27-28；不发卡 GameSetPlayResultCommandHandler.cs:130/225/345；重挑排除 GamePartyTransitionCommandHandler.cs:255-257 |
| “是否在副本中”拼凑 | 权威是 `IDungeonParticipationDomainService`；仍有 7 处只判幻影塔、不判 Boss 塔：CargoTransfer:23、InventoryExpansion:152、RecoverStaminaSlice:149、GameSortItemCommand:46、CreatureRuntimeDelivery:177、RenameCreatureHandler:87、GameFinishQuestCommand:254 |
| 13 份近同 `EnterAsync` 骨架 | 其中 5 份会查两次等级，阈值来源不同（Sao、Despair、Tau、Heras、Altar） |
| 8 个 `*EntranceEventDomainService` | `DescribeAsync`/`ChangeAsync`/`Text` 逐字相同 |
| `Status == "active"` 9 处 | 依赖 GM 展示字符串 |
| 次数族 5 处枚举 | DungeonAdmission:32/133/203、UsageQuotaDomainService:12-129、快照:22-49、UsageQuotaProjection:55-83、CharacterEntryStateServices:290-300；“上限 0”在 admission 里对次元表示拒绝，对其它族表示不限 |
| 两塔复制 | 两份 ActionProjection 改名后只剩 86 行差异；三份 `Project*TowerPartyDepartureAsync` 逐字拷贝；`Coordinate*TowerPartyAsync` 改名后差异为空；塔快照存 `IsLeader` 副本，靠 `SynchronizeLeader` 追 |
| `DungeonRunDomainService` | 2415 行，单锁 `gate`，可分 12 个簇；接口 57 个成员中 51 个是默认实现（3 个抛异常、29 个常量桩、7 个转发、12 个带逻辑），只为 12 个手写 fake 少写方法 |

#188 魔数（当前位置）：

| 魔数 | 含义 | 位置 |
|---|---|---|
| 11000 / 11001 | 死亡之塔（使用塔背包与地面拾取）/ 幻影塔 | IllusionTowerDomainService:64/129/180/256/321，IllusionEntryCommandPhases:51/107，ReplayStateDomainService:44，PvfIllusionTowerContentSource:18-26/82-83，接口默认参数 `= 11001` 共 9 处 |
| 101 | 婚房城镇 | Protocol 4 个文件（TownMovementProjection、TownArrivalProjection、WeddingTimerCommands、GameTownMovementCommands），以及 HousingState、OnlineCharacterLocationDomainService、CharacterStateAccess、EntryPlacementRuleService、PvfMarriageHousingContentSource |
| 20011 | 婚房商店 NPC | BuyItemMessageDecoder:15，GameBuyItemCommand:49/64 |
| 3541 + 难度 2 | 情侣副本 | CharacterEntryAssembly:378-380（`CoupleDungeonRule.DungeonId` 已有常量但没被复用） |
| 214 | SAO 入口活动 | SaoTowerDomainService:21，SaoTowerEntranceEventDomainService:13 |
| 10_006_007 | 任务清除券 | QuestClearCouponRuleService:17，QuestClearCouponSettlementService:21，GameQuestClearCouponCommand:160 |
| `FlagIndex == 31` | 完成教程 | GameTaggedIdReportCommand:68 |
| 同类但 issue 未列 | Boss 塔 3505、赫拉斯 3900、金银角 4123、祭坛 "11006/11007"、比武 "120/121/122" | ExperienceCalculationDomainService:52/61；UsageQuotaProjection:62-63、OrdinaryLoadingCommandPhases:85、CharacterEntryStateServices:298-300；UsageQuotaDomainService:119/121 |

## 2. 设计

### 2.1 两个轴，分开建模

- **运行引擎**：`DungeonParticipationMode { Ordinary, IllusionTower, BossTower }` 已经是封闭判别值，
  `DungeonLocator` 按它分派。两塔各自有 run 模型，不走 `DungeonLayout`。保持不变。
- **玩法种类**：只存在于普通引擎内部，新引入，替换 `DungeonLayout` 上的可空计划。

两轴不合并。原因：塔的事务、成员表与投影都另成体系，硬塞进同一个枚举后，每个 switch 都要为塔写“不适用”分支。

### 2.2 判别值：`DungeonRunKind` + `DungeonRunMode`

```csharp
// Abstractions/World
public enum DungeonRunKind : byte
{
    Ordinary, Dimension, Hell, Mirror, Sao, Despair, Tournament, Altar, Guardian,
    GiantDragon, SouthernDale, TauKingdom, SealedTreasure, Heras, GoldSilverHorn, Couple,
}

/// 冻结在入场时的玩法种类及其计划；只能由下面这些嵌套类型构造。
public abstract record DungeonRunMode
{
    private DungeonRunMode() { }
    public abstract DungeonRunKind Kind { get; }
    public sealed record Ordinary : DungeonRunMode { ... }               // 含 Dimension/Cycle 等无计划种类
    public sealed record Hell(HellEntryPlan Plan) : DungeonRunMode { ... }
    public sealed record Sao(SaoTowerEntryPlan Plan) : DungeonRunMode { ... }
    // Despair / Tournament / Altar / Guardian / Mirror / GiantDragon(TimeReward) 同理
}
```

- `DungeonLayout` 删除 8 个可空计划和 `EntryKind`，换成一个必填的 `Mode`。
  `NoGiveupPenalty`（血之副本属性）保留为内容事实，不算种类。
- **穷举由编译器守护**：对 `Kind` 的 switch 表达式不写 `_`。项目级只抑制 CS8524（未命名枚举值），
  保留 CS8509。新增一个种类时，所有没覆盖它的 switch 都会编译失败（`TreatWarningsAsErrors`）。
  另加一条结构测试作反例：扫描生产代码，凡是对 `DungeonRunKind` 写了 `_ =>` 兜底的都拒绝。
- 计划的生产者不变：各特殊服务目前写 `layout with { Sao = ... }`，改为 `layout with { Mode = new DungeonRunMode.Sao(...) }`。

### 2.3 能力：一张表回答“这种玩法怎么办”

只是数据或开关的差异，集中到一个规则类型，按 `Kind` 穷举一次：

```csharp
public sealed record DungeonRunCapabilities(
    RevivalPolicy Revival,              // Allowed / Forbidden(Guardian) / Tournament / Altar
    bool OpensClearOnBossKill,          // Tournament、Altar 为 false
    bool GrantsBossClearReward,         // Tournament、Altar 为 false
    bool AppliesFailureWeakness,        // Altar 为 false（NoGiveupPenalty 另行叠加）
    bool DrawsClearCards,               // Despair 为 false
    bool UnlocksDifficulty,             // Mirror、Despair 为 false
    bool AllowsOrdinaryRechallenge,     // 地狱、镜像、SAO、绝望之塔、比武、祭坛、守护者为 false
    PartyWipePolicy PartyWipe,          // OrdinaryTimer / ModeOwned(Altar, Tournament) / None(Guardian)
    UsageQuotaFamily? QuotaFamily);     // 取代 OrdinaryLoadingCommandPhases 按 Layout 或 3900 选族
```

调用方从 `if (layout.Altar is null && layout.Tournament is null ...)` 改为问能力。
复活、首领奖励、开 clear、失败虚弱、发卡、解锁难度、重挑排除这七处条件链都收到这张表。

### 2.4 策略接口：只给真正有行为的钩子

| 接口 | 实现者 | 替换 |
|---|---|---|
| `IDungeonRunClearHook.OnBossRoomClearedAsync` | SAO、南部溪谷 | MonsterDeathDomainService:92-97 的硬编码调用，以及构造器里可选的 `sao`/`southern` |
| `IDungeonRunKillHook.CanKill / AdvanceAsync` | 比武大会 | MonsterDeathDomainService:36-38/87-90 与可选依赖 `tournaments` |
| `IDungeonRunWipeHook.OnPartyWipedAsync` | 祭坛 `FailAsync`、比武大会 `EliminateAsync` | DungeonReturnTimerCommands:57-96 的分支 |
| `IDungeonKindEntry`：`Kind`、`SupportsAsync(context)`、`EnterAsync` | 13 个特殊入场服务 + 两个偏离骨架的（比武、守护者） | GameSelectDungeonCommand:164-198 的三元链。顺序改由一个领域入口 `DungeonEntryRoutingDomainService` 持有，Protocol 只调它 |

- 注册：按 `Kind` 建字典。启动时检查同一 `Kind` 不能重复注册；能力表声明有钩子的种类必须恰好注册一个实现，否则启动失败。
- 拾取不需要钩子：普通 `GroundPickupDomainService` 不按种类分支，塔的拾取已由 `DungeonLocator` 分派。
- Protocol 的帧选择（入场帧、loading 帧、移图帧、Hell/Despair 字段）属于协议知识，留在 Protocol，
  改成 `switch (layout.Mode)` 模式匹配，帧内容不变。

### 2.5 “是否在副本中”只有一个来源

- `IDungeonParticipationDomainService` 是唯一来源，补一个只读查询 `Where(characterId) → DungeonParticipationMode?`。
- 删除各处 `runs.Find(...) is not null || illusion.IsInTower(...)` 这类拼接。
  两座塔与 `DungeonRunDomainService` 对 participation 的依赖改为必填（遵循 #183：新改的依赖一律必填）。
- 塔自己的 membership 表仍按 exact session 保存“在塔里的哪个会话”，用于找 run。
  “在不在副本里”这一问不再由它回答。

### 2.6 #188 魔数改为显式能力

| 魔数 | 改法 |
|---|---|
| 11000/11001 | 新增 `IllusionTowerVariant { Death, Illusion }`，只在内容源里与 dungeonId、PVF 字段名对应一次。`IllusionTowerDefinition`/快照暴露 `UsesTowerInventory`；删掉 9 处 `= 11001` 默认参数，参数改为必填 |
| 101 | 在 Data/World 定义唯一常量，`TownAreaKey`、`CharacterPositionState` 暴露 `IsCoupleRoom`。领域、Infrastructure、Protocol 都改问它 |
| 20011 | 婚房 NPC 常量只由解码器使用。CMD 改按消息类型（`HousingBuyItemRequest`）分支，不再重复比较 NPC id |
| 3541 + 难度 2 | 情侣副本 id 和可见难度由情侣规则给出，`CharacterEntryAssembly` 只调用它 |
| 214 | `ISaoTowerRuleService.EventId`，与其它活动的 `rules.EventId` 一致 |
| 10_006_007 | 由 `IQuestClearCouponRuleService` 暴露券 id，结算和日志都引用它 |
| FlagIndex 31 | 教程奖励规则服务提供 `CompletesTutorial(flag)` |
| 3505 / 3900 / 4123 / "11006"… | 由种类能力（次数族）和塔规则替代。是否纳入本次见裁定 3 |

### 2.7 活动开放判定

`IEventManagementDomainService` 增加 `IsOpenAsync(eventId, now)`，复用各活动服务里同一个 `Active(c, now)` 谓词。
9 处 `Status == "active"` 全部改调它，GM 展示字符串不再参与玩法判定。

### 2.8 两塔去重（第二个 PR）

1. 先补金样本：两人队伍进 Boss 塔的入场帧（现有黄金只覆盖幻影塔 11000），以及两塔的击杀、拾取、结算、离队回包全帧 hex。
2. 提取 `ITowerRunKind` 策略，承接 Boss 与幻影塔之间实际存在的分歧点：
   - 结算额外帧与奖励槽位数（8 / 4）；
   - 继续判定（CanRetry / CanContinue）；
   - 自动 Start；
   - 扣料回包（DeleteItemResponse）；
   - 拒绝码映射；
   - 工作单元收件人（目前一边带参与者，一边为空）；
   - 拾取；
   - 回城时的塔背包标志。

   两份 ActionProjection、两份 DepartureQueue、三份 `Project*Departure`、两份 `Coordinate*` 各合成一份。
3. 删除 `TowerMember.IsLeader` 和 `SynchronizeLeader`，队长改为每次从队伍服务查询。
   这一步牵涉“队长离开时以 `Members[0]` 补位”的语义，单独提交，并用金样本和用户测试验证。

### 2.9 拆分 `DungeonRunDomainService`（第三个 PR，纯搬迁）

1. **同类 partial 拆分**：按簇拆成 partial 文件，包括查找、入场就绪、移动、死亡击杀、地面掉落、通关结算、翻牌、计时、参与者状态、离队结束、内部模型。
   类型名、成员与 `Writing` 字符串都不变，`GameplayWriteEntryTableTests` 无需改动。
2. **窄接口**：
   - 以 `IDungeonRunQuery`（两个 `Find`）为基接口，约 44 个只读消费方改注入它；
   - 其余按入场、死亡、掉落、结算、翻牌、计时、离队拆出窄接口；
   - `IDungeonRunDomainService` 只作为继承这些窄接口的聚合接口，保留给 `ProtocolAlgorithmFixture` 等按聚合解析的测试机制。
3. **删默认接口方法**：
   - 删除 51 个默认实现；
   - 删除 4 个生产从不调用的抽象重载（`TryMove` 2 参、`TrySettle`、`FixClearRewardCards` 4 参、`End` 1 参）；
   - 12 个手写 fake 改为实现自己用到的窄接口，同时删除它们身上 4 个早已不在接口上的残留方法。
4. **暂不拆成独立类**：claim 表被 `Remove`、`Release`、`TryMoveCore` 三处交叉清理和读取，抽成独立类会牵动普查和写入口表。等第 1、2 步落地后再评估。

### 2.10 入场骨架、入口活动与次数族（第四个 PR）

- 抽出共同的入场骨架：守卫 → 活动开放 → 读取定义 → 检查等级（只查一次，阈值来源统一）→ admission → 解析 layout → 设置 Mode → 进入。13 个服务只提供各自的差异。
- 8 个入口活动服务的公共部分（Describe、Change、Text）提取为一个基础实现，各服务只提供 EventId、初始配置与 EVENT_INFO 投影。
- 次数族收成一张表，每族给出 key、配额上限的读取方式与“上限 0”的统一语义；admission、写入、快照和投影都读这张表。
  “上限 0”目前对次元表示拒绝、对其它族表示不限，统一语义属于行为变更，单独提交。

## 3. 与并行会话的边界

| 会话 | 重叠 | 处理 |
|---|---|---|
| #180+#181（Protocol 业务收回 CMD/领域） | 会改 TownMovementProjection、TownArrivalProjection、婚礼相关 handler；也可能改 DungeonClear、GroundPickup、GameMonsterDeath、GamePartyTransition 等 handler | 101 先在领域与数据层落地 `IsCoupleRoom`；4 个 Protocol 文件里的替换放在本 PR 最后一个提交，开 PR 前变基到最新 main 再改。<br>本 PR 在 handler 里只把 `Layout.X` 换成能力或 `Mode` 匹配，不搬业务归属。若对方先把某段业务移进领域，就在对方的新位置上替换。<br>入场三元链移进领域服务，与 #181 方向一致，提交前会先确认对方是否动了 GameSelectDungeonCommand |
| #184（Func 去环与服务定位） | `ReplayStateDomainService` 用 `services.GetService<IIllusionTowerDomainService>()`；`PlayingCharacter` 持有 `Func<IIllusionTowerDomainService>?` | 本 PR 只把 `{ DungeonId: 11000 }` 改为 `UsesTowerInventory`，不动注入方式 |
| #183（可选依赖必填化，暂缓） | 本 PR 删除或新增的依赖 | 一律必填，不写 `= null` 或 `?? new` |

## 4. 第一个 PR 的提交顺序

纯搬迁与语义改动分开提交；每个提交都能编译且测试全绿。

1. `test:` 钉行为。为 Hell、Despair、Altar、Tournament、Mirror、Guardian 的入场帧和 loading 帧补全帧金样本（`[PvfFact]`），并补种类判定的表征测试（复活、团灭、首领奖励、发卡、重挑）。
2. `refactor:` 引入 `DungeonRunKind`/`DungeonRunMode`，`Layout.X` 机械替换为 `Mode` 匹配，行为不变。
3. `refactor:` 能力表，集中七处条件链；加穷举守护（CS8524 抑制与反例测试）。
4. `refactor:` 清场、击杀、团灭三类钩子；`MonsterDeathDomainService` 删除可选的 `tournaments`/`sao`/`southern`。
5. `refactor:` 入场路由注册表，替换三元链。
6. `refactor:` #188 魔数，每类一个提交（101 的 Protocol 替换放最后）。
7. `refactor:` 活动开放判定 `IsOpenAsync`。
8. （待裁定 2）统一“是否在副本中”的来源；若同时修正 Boss 塔漏判，单独提交。

验证：按 Document/BuildAndTest.md 构建并跑全量测试；用当前主干数据库准备用户测试，覆盖普通副本、Hell、镜像、SAO、绝望之塔、祭坛、比武大会、守护者祭坛、南部溪谷、婚房进出与婚房商店、两塔。

## 5. 待用户裁定

1. **范围**：拆成 4 个 PR、本会话依次完成（推荐）；或一个大 PR；或本会话只做第一个 PR。
2. **SAO / 南部溪谷清场**：改为同一事务内按种类分派的钩子，保持原子性（推荐）；或改订阅 `DungeonMapCleared`，变为跨作业的可靠事件，属于行为变更。
3. **issue 未列的同类魔数**（3505 / 3900 / 4123 / "11006" / "120"…）：一并删除（推荐）；或只删 issue 列出的。
4. **Boss 塔 7 处漏判**：统一来源，同时修正为“Boss 塔内同样视为在副本中”（推荐，单独提交并纳入用户测试）；或只统一来源、保持现有行为，另开 issue。

## 6. 实施结果与偏差（PR1）

落地后的现行规则见 [DungeonRunKinds.md](../../DungeonRunKinds.md)。相对 §2 的设计，有以下调整：

- **团灭**：没有做成领域钩子。祭坛失败与比武淘汰都要立刻投出各自的协议帧，因此做成能力表里的 `DungeonPartyWipePolicy` 枚举，
  由 `DungeonReturnTimerCommands` 按枚举分派。复活同理，做成 `DungeonRevivalPolicy`。
- **击杀**：比武大会的 `CanKill`/`AdvanceAsync` 返回比武专有的结果，泛化成钩子并没有收益，因此保留按 `Mode` 的匹配，但依赖改为必填。
- **魔数**：
  - `FlagIndex == 31` 已由 #180/#181 改为 `TaggedIdReportRequest.CompletesTutorial`，本 PR 不再改动。
  - 祭坛 `"11006"/"11007"` 与比武 `"120"…"122"` 是次数键的格式校验，和 Persistence 的旧次数键格式绑定，归入 PR4 的次数族统一。
- **“是否在副本中”**：
  - 仓库移动、仓库升级与金币存取、仓库整理、疲劳恢复改为只问副本占用登记；
  - 宠物通知与任务完成外观的收件人改为经 `DungeonLocator` 定位，Boss 塔与幻影塔同等对待。
  - `DungeonAdmissionDomainService` 已经通过占用登记覆盖 Boss 塔，它的一串可选依赖留给 #183；`PlayingCharacter` 的 `Func` 留给 #184。
- **变异检查**（把一种玩法的能力改回普通后，看现有测试是否失败）：
  - 赫拉斯一项没有测试覆盖，已补 `DungeonLoadingProjectionDeliveryTests.A_loaded_room_refreshes_the_quota_its_run_kind_spends`；
  - 地狱由 `LeaveDungeonTests` 覆盖；
  - 其余种类的结果见 PR 描述。

## 7. 基线失败（与本任务无关）

使用 PVF `~/Downloads/Script.pvf`（SHA-1 前缀 02bbb930），主干 `bb41a19a8` 的 Application.Tests 就有 8 个失败。
本任务每个提交后失败的仍是这 8 个，没有新增：

- `GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy`：3536–3540 共 5 个，`InvalidDataException: Giant dragon dungeon or native entrance is missing/incompatible`；
- `DimensionDungeonTests` 的 2 个：`Dungeon '7100' maze '0': cell (1,0) has no compatible map`；
- `AvatarCompoundTests.Rejections_leave_all_inputs_unchanged(reason: "expire")`：拒绝分支返回了收据。

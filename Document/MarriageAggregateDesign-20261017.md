# 婚姻聚合重构设计稿（单实体、多接口）

- 日期：2026-10-17
- 基准：`task/next-round-20261013` @ `5a0cbed0`
- 状态：**设计稿，未实施**
- 关系图：`MarriageAggregateDesign-20261017.html`（同目录）

> **落地状态（2026-10-17）：已在 `task/next-round-20261013` 实现，尚未合入 main。**
> 下面的正文保留为当时的设计推导；与实现不一致的地方以本段为准，正文里对应处标了「见落地状态」。
> 合并实体的过程记录在 `.rks-agent/tasks/marriage-domain-aggregate/`（TASK-001～006）；单实体结构的审核与去环方案见
> [MarriageAggregateReview-And-DI-Refactor-20261017.md](MarriageAggregateReview-And-DI-Refactor-20261017.md)，今天的结构按它 §5 落地，差异见下表。
>
> **今天的形状**（`Server/src/Dfo.Server.Application/Marriage/`，关系图见同名 .html）：三个单元，构造依赖单向、无环。
>
> - **M** `MarriageDomainService`：求婚、预约、婚礼、心意券。实现 `IMarriageQueryDomainService`、`IMarriageCoupleDomainService`、
>   `IMarriageVenueDomainService`，DI 解析出同一个实例。构造器接 R 的读写接口、T、7 个组件（`MarriageEligibility`、`ReservationDesk`、
>   `ChurchRooms`、`WeddingSettlement`、`GuestList`、`Guestbook`、`LovePoints`）和 `TimeProvider`，不接任何 store。
>   `LovePoints` 普通注入 `IApplyEffectsDomainService` 扣券。
> - **T** `MarriageQuestDomainService`：实现 `IMarriageQuestDomainService`（四个签名不变），任务领域只解析到它。
>   `MarriageTrialClearSubscriber` 直接转交它的 `RecordTrialClearAsync`；M 在申请和接受预约、以及 `ViewAsync.TrialsCleared`
>   里调它的 `ReadyForCeremonyAsync`（两人都已通关试炼与材料任务、且四条绑定都指向这段关系），这条判断只在 T 里有。
>   T 只读 R；依赖集合与基线 `5a0cbed0` 的 `MarriageQuestDomainService` 相同，只是求婚 store 换成 R 的只读接口、
>   `IMarriageDungeonContentSource` 换成规则服务 `IMarriageTrialRuleService`。
> - **R** `CoupleLedger`：关系真源。一个实例对外两个接口，都是 Application 内部的 `internal` 接口：只读 `IMarriageRelations`
>   （按会话、按参与者、按一对、按 id 读关系，读求婚记录，以及纯函数 `Belongs`、`IsPair`）、读写 `IMarriageRelationWriter : IMarriageRelations`
>   （建关系、阶段 CAS、心意点）。不进 `Dfo.Server.Abstractions`，所以 Protocol 的命令处理器拿不到，绕不过婚姻用例写阶段。
>   R 只依赖关系 store、求婚 store、`IMarriageProposalRuleService`、`TimeProvider`；不读预约 store，只给持久化的原始阶段。
>
> 阶段存在 `marriage_relations`，只由 `CoupleLedger` 经 `IMarriageRelationDomainStore` 写入。
> 旧库在启动建表时由 `MarriageRelationMigration` 回填，回填之后把关系行心意点抬到对应 phase=1 婚礼行旧列的值（两者取大）；
> 回填表里那类异常（已订婚、仍有 Reserved 预约、但没有进行中或已完成的婚礼行，含婚礼已取消的情况）由 `MarriageRelationAudit` 在启动时计数，大于 0 才写一条 Warning。
>
> **与设计稿的偏差**
>
> | 设计稿 | 实现 | 原因 |
> |---|---|---|
> | 4 个接口由同一实例实现（§4、§5.5） | Query / Couple / Venue 由 M 实现；Quest 由 T 实现 | 任务服务 → T 时若 T 就是 M，M 经心意券 → 通用效果 → 效果处理器 → … → 任务服务回到自身，构成依赖环（审核 F1） |
> | 9 个组件互不依赖、实体只接组件与时钟（§5.1、§5.3 第 2 条） | M 的 7 个组件互不依赖；M 另接 R 的读写接口和 T；T 接 R 的只读接口 | 关系与婚前任务被两个上层流程共用，改成共享下层而不是平级组件 |
> | 「是不是一对」由 Ledger 判定（§5.3 的 `Couple`、§5.4 的 `ledger.LoadPair`） | 判定在 R：`ForSessionAsync`、`ForParticipantAsync`、`ForPairAsync`，以及接口上的纯函数 `IMarriageRelations.Belongs`、`IsPair`。每个公开操作只解析一次关系，组件之间传 `MarriageRelation` | — |
> | 惰性过期由 Ledger 在加载时执行（§3.4） | 写路径的到期结算在 M 的用例里做；读路径的「有效阶段」投影在 M 的 Query（`ViewAsync`）；R 只给原始阶段 | 投影要读预约，预约 store 只归 M 的 `ReservationDesk` |
> | 接受预约一步完成（§4 `RespondReservationAsync` 注释、§4.1、§5.4） | 仍是两步。`RespondReservationAsync` 只做预约答复和 T6 的阶段迁移；`MarriageCeremonyCommandHandler` 在同一事务里，结果为 Reserved 时再调新增的 `IMarriageCoupleDomainService.OpenWeddingAsync`，由它分配教堂、两人入场、扣费、建婚礼行。任一步失败照旧整笔回滚 | 保留现有命令结构和失败语义，不改协议层 |
> | Query 上的只读 `ChurchAsync`（§4、§4.1） | Venue 上的 `PrepareChurchAsync`，缺房时会分配教堂，是写操作 | 现有测试在「只接受预约、未开婚礼」时直接调用它并断言分到房间；改成只读就要改这些断言 |
> | `IsMarried` 与婚礼 phase 改读 `View.Stage`（§4.1） | Query 保留 `IsMarriedAsync(relation)`（读关系阶段）和 `WeddingPhaseAsync(ceremony)`（读单场婚礼的 phase） | `TownProjectionSlice.cs` 要的是外观所在那场婚礼的 phase，宾客没有对应的 View。`IsMarriedAsync` 和 `ViewAsync` 今天都没有生产调用方，只有测试调用 |
> | 婚礼 store 一个接口（§5.2） | 宾客三方法拆到 `IMarriageWeddingGuestDomainStore`，只注入 `GuestList`；`IMarriageWeddingDomainStore` 只注入 `WeddingSettlement`。Persistence 里仍是 `SqliteMarriageWeddingDomainStore` 一个类实现两个接口 | 让「每个 store 只归一个类型」能由判据检查 |
> | 试炼订阅转交 `TrialQuests`（§5.1、§5.5） | `TrialQuests` 并入 T；订阅类转交 T 的 `RecordTrialClearAsync`，T 自己经 R 的只读接口判定这一对 | — |
> | `TrialQuests` 依赖婚姻地下城内容（§5.2） | T 经规则服务 `IMarriageTrialRuleService.IsTrialMapAsync` 判定地图，内容源只由规则服务注入 | AGENTS「内容源只被规则服务注入」 |
> | 资格组件检查任务（§5.1、§5.2） | `MarriageEligibility` 不再读任务日志；预约前的任务检查在 T | 规则与绑定判断只留一处 |
> | 未写 03F3 对方资料归谁 | 由 `MarriageEligibility.ProfileAsync` 构造 | 它只需要角色和入场库存两个只读依赖，归「只读的人物事实」最近 |
> | CAS 失败返回 `Stale`（§5.3 第 3 条） | 分两种：改子记录之前先检查手上关系的阶段，不接受该触发就返回现有的 `Stale`；子记录改过之后一律经 `MustCommitAsync` 提交，CAS 失败就抛异常、整笔回滚。`MustCommitAsync` 返回提交后的关系，同一操作在惰性到期之后用它继续，不重读。返回 bool 的 `CommitAsync` 只剩测试调用，不在读写接口上 | 子记录改了而阶段没改，只能回滚，不能当正常拒绝返回 |
> | `ActiveWeddingsAsync` | 返回 ceremony id 列表，与 §4 签名一致；调度器 `MarriageWeddingScheduler` 只用 id | — |
> | `stage CHECK(stage BETWEEN 0 AND 5)`（§6.1） | `BETWEEN 0 AND 15`，§11 第 1 问按「现在就放宽」处理，离婚阶段不需要重建表 | — |
> | 03D0 state 由阶段规则类的 `WireState` 投影（§3.1） | 快照带上 `Stage`，由 Protocol 侧的 `MarriageEngagementPayload.WireState` 按 §3.1 的表投影；`MarriageStageRules` 里不再有 `WireState`。`IsMarried`、`IsCeremonyReserved` 改为从 `Stage` 派生的只读属性 | 阶段到线上字节的对应属于协议投影，按 AGENTS 分层归 Protocol |
> | 心意点双写一个版本后停写旧列（§6.3、§9 PR3） | 仍双写；另在启动迁移里按「两列取大」对账关系行（见已知限制） | 停写需要后续单独改动 |
> | 4 个 PR，B1 单独提交（§2、§9） | 合并实体四步在同一任务分支依次完成，合成一个提交 | 用户选定的流程 |
>
> **修正清单 B1-1～B1-5**（取代 §8 的 B1；逐条的修正前后实测值和对照用例见 `.rks-agent/tasks/marriage-domain-aggregate/tasks/TASK-004-unified-pair.md`「修正清单」）
>
> | 编号 | 行为 | 修正后 |
> |---|---|---|
> | B1-1 | 心意点：会话账号不拥有该已婚角色 | 账号列表退回只含会话本人的账号；请求带的是这对夫妻的账号时得 -1，只带会话本人账号时得 -3；券不扣、点数不变。原先只看角色 |
> | B1-2 | 祝词簿新人路径：会话账号不拥有该新人角色 | 读、删都得 -1。原先只比角色 id，能读能删 |
> | B1-3 | 一次加载后 `Stale` / `Ineligible` 的先后 | 逐个核对过，没有变化 |
> | B1-4 | 已关闭关系（stage=5 而参与者仍 active）在各入口 | `ViewAsync` 返回 null；其余入口结果与原先相同 |
> | B1-5 | `ViewAsync` 缺任务内容时 | 返回视图，`TrialsCleared = false`。原先抛 `FileNotFoundException` |
>
> B2（心意点归属关系）、B3（结婚后不再查预约状态）按 §8 落地。
>
> **已知限制**
>
> - **心意点：单向兼容加启动对账。** `SqliteMarriageRelationDomainStore.AddLovePointsAsync` 在关系行条件累加成功后，把同一关系 phase=1 婚礼行的 `love_points` 设成关系行的值，所以从新版本退回旧版本时旧版本读得到点数。反方向靠启动对账：`MarriageRelationMigration` 每次启动把关系行心意点设为「关系行、该关系 phase=1 婚礼行」两者的较大值。成立的前提是心意点只增不减、新旧版本不同时运行、新版本加点会把两列写成一致——于是只有旧二进制能让旧列领先。判据：`MarriageRelationMigrationTests.Startup_lifts_relation_love_points_to_a_higher_old_wedding_column_and_nothing_else`。
> - **阶段不对账，不支持「回退旧版本后继续推进婚姻、再升级」。** 回填只给没有关系行的求婚补行（`INSERT OR IGNORE`），已有行的阶段不改；较低的阶段可能是合法的取消结果，不能按「取大」覆盖。退回旧二进制期间的阶段推进切回新版本后不会反映到关系行上；回退窗口里由旧二进制开出的婚礼，切回后关系行仍停在 Engaged 或 Requested，到点推进时提交 Married / Engaged 被迁移表拒绝，`MustCommitAsync` 抛异常、事务回滚，调度器每秒重试一次，这场婚礼既不完成也不退款。回退期间不要做婚姻操作；已经做了的，切回前恢复与新版本配套的数据库备份。
> - **子记录改动后关系 CAS 失败，对外是断连，不是 `Stale`。** `MustCommitAsync` 抛 `InvalidOperationException`，命令事务整笔回滚（`MarriageEntityTests` 的两条回滚用例，其中一条经命令总线和 03F1 handler）。婚姻 handler 不捕获这个异常（`RespondToCeremonyAsync` 只捕获 `MarriageWeddingUnavailableException`），它经 `GameNetworkPlugin` 记一条 Error「Game handler failed」后重新抛出：发起方收不到这条请求的回包、对方收不到通知，服务端关闭这条连接，其他连接照常（`HandlerFailureConnectionIntegrationTests`）。调度器路径则是记「Wedding advancement failed」、下一秒重试。写关系的命令都在账号 Exclusive 租约和命令事务内执行，按代码推导正常客户端操作造不出这种冲突；未实机复现。
> - **阶段值 6–15 已预留，但 Protocol 的 `WireState` 遇到它们会抛异常**；加离婚阶段时要同时补上 03D0 投影。快照的 `Stage` 默认为 Engaged，构造快照时必须显式赋值，否则会静默投影成 state 1。
> - **依赖方向不是编译期保证**，由架构判据 `MarriageArchitectureTests` 守着，每条规则各有一条反例用例：关系 store 只被 `CoupleLedger` 接受、R 的构造器只接关系 store / 求婚 store / 求婚规则 / 时钟；`IMarriageRelationWriter` 只被 M 接受，T 接受 `IMarriageRelations`，没有类型直接接受 `CoupleLedger`；按生产模块的真实注册（工厂注册按解析出的实例类型）递归展开，T 与 R 的传递依赖里没有 M、`IApplyEffectsDomainService`、Query / Couple / Venue 接口，T 也不到 `QuestFactsDomainService`；`Application/Marriage` 下的构造器不接 `Func<>`、`Lazy<>`、`IServiceProvider`；M 不接任何 store，每个婚姻 store 只有一个所有者；M 的 7 个组件互不出现在彼此的构造器里，M 的接口参数只有 `IMarriageRelationWriter`。另有一条用生产模块建容器、逐个解析各婚姻接口、`IApplyEffectsDomainService` 与试炼订阅类的用例。
>
> **真实库副本迁移**：两份近期运行库副本（各有 Rejected 求婚 1、Engaged 求婚 1、Reserved 预约 1、phase=1 婚礼 1）迁移后，阶段为 Married 1、Closed 1、其余阶段 0，异常 0；`integrity_check` 为 ok；原有 92 张表的行数、内容和结构都没变；第二次打开无变化。记录见 `.rks-agent/tasks/marriage-domain-aggregate/evidence/TASK-005-db-copy-migration.txt`。这次迁移早于心意点启动对账。
>
> **实机回归**：清单见 [MarriageAggregateRegression-20261017.md](MarriageAggregateRegression-20261017.md)，由用户执行。

一句话：把现有 8 个婚姻领域服务收进**一个实体** `MarriageDomainService`。

- 实体内部分为 **9 个小组件**和 **1 个纯规则类**；
- 对外按调用方发布 **4 个窄接口**，由同一个实例实现（见落地状态）；
- 情侣的「当前阶段」改为**一条记录直接存储**，不再从三张表推导。

---

## 1. 现状

### 1.1 八个服务与依赖

| 服务 | 行数 | 直接注入的**别家** store |
|---|---:|---|
| Proposal | 90 | — |
| Engagement | 39 | Ceremony、Wedding |
| Ceremony | 167 | Proposal、Quest、Wedding |
| Church | 115 | Proposal、Ceremony，另有 Wedding（可空注入） |
| Wedding | 319 | Ceremony、Church；另外还注入了 Church **服务** |
| Quest | 100 | Proposal |
| LovePoint | 55 | Proposal、Wedding |
| Guestbook | 54 | Proposal、Wedding、Church |

合计约 1000 行。几乎每个服务都在直接读写别人的表，边界只存在于名义上。

### 1.2 阶段靠推导，不是记录

- `MarriageProposalState` 只有 `Pending / Engaged / Rejected / Expired`，**没有「已婚」**。
- 「已婚」是从婚礼表推出来的：`marriage_weddings.phase=1`，见 `SqliteMarriageWeddingDomainStore.MarriedAsync`。
- 「已预约」也是推出来的：`marriage_ceremonies.state=1`，见 `IsReservedAsync`；`MarriageEngagementDomainService` 还要先排除已婚。
- 03D0 线上状态再从上面两个布尔值拼出来：`MarriageEngagementSlice.cs:47` 的 `IsMarried ? 4 : IsCeremonyReserved ? 2 : 1`。
- **结婚后，ceremony 记录永远停在 `Reserved`**：只有缺席取消会把它改成 `Expired`。所以任何地方判断「已预约」之前，都必须先排除已婚，否则会误判。

### 1.3 「是不是一对」写了六遍，口径不一

| 位置 | 核对 AccountId | 排除已婚 |
|---|---|---|
| `MarriageCeremonyDomainService.RelationAsync` | 双向 | 是 |
| `MarriageChurchDomainService.ReservedAsync` | 只核本人 | 是 |
| `MarriageQuestDomainService.RelationAsync` | 是 | 否 |
| `MarriageEngagementDomainService.ReadAsync` | 只核本人 | 读取，不拦截 |
| `MarriageLovePointDomainService` | **否** | 是 |
| `MarriageGuestbookDomainService.AccessAsync` | **否**（key 直接来自客户端） | 否 |

### 1.4 其它事实

- **心意点存在婚礼行上**：`marriage_weddings.love_points`。它属于这对人，不属于某一场婚礼。
- **接受预约和建婚礼已经在同一个命令内完成**（`MarriageCeremonyCommandHandler`）。准备失败时抛异常，整笔回滚，然后由 slice 再派一条 response=2 的命令把预约改为过期。
- **多账号租约已有机制**：`IMultiAccountCommand`。情侣命令同时租两个账号，婚礼推进按宾客名单租多个账号。
- **测试覆盖**：Application 层有 41 条婚姻用例。`MarriageChurchTests` 和 `MarriageEngagementTests` 已经覆盖婚礼准备、开场结算、请柬与礼金、取消、心意点、最后一分钟锁、婚前任务的主路径；多个失败码分支、清理重试、祝词簿访问矩阵等还没有逐条覆盖。（初稿写成「这些部分没有应用层用例」，那是只按文件名检索得出的结论，已更正。）

## 2. 目标与非目标

**目标**
1. 「两人现在处于哪个阶段」只有一个真源：关系记录上的 `stage`。其它功能根据 `stage` 决定怎么处理，不再推理。
2. 对外只有一个婚姻实体；内部按职责拆成小组件，组件之间不靠公开接口互相调用。
3. 每个操作只加载一次情侣记录，阶段迁移只写一次，用 revision 做 CAS。

**非目标**
- 不改任何线上字节：03D0、03D4、03CF、03F7 的回包状态码保持不变。
- 不新增玩法：离婚、婚戒升级以后另做，这里只预留阶段值。
- 不在同一个 PR 里修行为缺陷，修正项单独列在 §8，单独提交。

## 3. 状态机

### 3.1 阶段（持久化整数，**只追加，不重排**）

| 值 | 阶段 | 含义 | 03D0 state |
|---:|---|---|---:|
| 0 | `Proposed` | 已求婚，等对方答复 | 0 |
| 1 | `Engaged` | 已订婚；婚前任务进行中或已完成，未预约 | 1 |
| 2 | `Requested` | 已提交婚礼预约，等对方确认 | 1 |
| 3 | `Preparing` | 预约已生效，教堂已分配，倒计时中 | 2 |
| 4 | `Married` | 婚礼完成 | 4 |
| 5 | `Closed` | 求婚被拒或过期，关系结束 | 0（无关系） |
| 6+ | 预留 | 离婚流程（对应 03F0 action 5/6/7、03D0 state 5/6/7） | — |

没有关系记录时也投影为 0。03D0 state 3 在客户端的语义未知，服务端不发。

**为什么「婚前任务已完成」不单独设一个阶段**
- 任务完成由任务日志提交，不由婚姻聚合提交。如果再存一份，就成了第二个真源。
- 阶段只记录**本聚合自己提交的迁移**。
- 任务是否完成，作为 `Engaged → Requested` 的守卫条件来检查；同时在视图里给出派生字段 `TrialsCleared`，供 UI 判断。

### 3.2 迁移表

| # | 从 | 触发 | 守卫 | 到 | 同一事务内的副作用（负责组件） |
|---|---|---|---|---|---|
| T1 | — | 求婚 | 双方在线、同城同区、等级达标、异性配对、双方都没有活跃关系 | Proposed | 建关系行和两条参与者记录（Ledger） |
| T2 | Proposed | 接受 | 未过期、同城、资格复核 | Engaged | — |
| T3 | Proposed | 拒绝 | — | Closed | 参与者 active=0 |
| T4 | Proposed | 过期（超时或任一方下线，惰性触发） | — | Closed | 参与者 active=0 |
| T5 | Engaged | 申请预约 | tier 与 waitIndex 合法、同城、两人的试炼和材料任务都已完成且绑定本关系、申请人金币足够 | Requested | 建预约记录 Pending（Reservation） |
| T6 | Requested | 接受预约 | 未过期，T5 守卫全部复核，双方会话仍有效 | Preparing | 预约改为 Reserved（Reservation）；分配教堂、两人入场（Church）；扣费、建婚礼行 Preparing（Settlement） |
| T7 | Requested | 拒绝 / 过期 / 复核失败 | — | Engaged | 预约改为 Rejected 或 Expired（Reservation） |
| T8 | Preparing | 到点，两人都在场且已入场 | — | Married | 婚礼行改为 Completed，写 married_at；发回礼、礼金分成、宾客回礼；站位；清理排队（Settlement） |
| T9 | Preparing | 到点，有人缺席 | — | Engaged | 婚礼行改为 Cancelled；预约改为 Expired；邮件退婚礼费用和宾客礼金；改写离场坐标；清理排队（Settlement） |

**不改变阶段、但依赖阶段的操作**

| 操作 | 要求的阶段 |
|---|---|
| 新人进入教堂（03F7 flag 0） | Preparing |
| 发请柬、宾客送礼金、宾客入场 | Preparing（宾客按婚礼 ID 判断） |
| 心意点 | Married |
| 祝词簿 | 新人：Engaged 及之后可读、可删；宾客：婚礼未取消且已入场时可写 |
| 婚前任务：接受、完成 | Engaged 及之后（现状只看是否订婚，不排除预约中或已婚，保持一致） |
| 试炼通关计数 | Engaged 及之后（同上） |
| 婚礼清理重试 | 不限：以婚礼行为准，不看阶段 |

### 3.3 不变量（每条都要有判据测试）

| 编号 | 不变量 |
|---|---|
| I1 | 一个角色至多一条活跃关系（沿用 `ux_marriage_proposal_character`） |
| I2 | `stage ∈ {Requested, Preparing, Married}` ⇔ `ceremony_id` 非空 |
| I3 | `stage = Preparing` ⇔ 该关系恰好有一条 phase=0 的婚礼行 |
| I4 | `stage = Married` ⇔ 该关系恰好有一条 phase=1 的婚礼行（沿用 `ux_marriage_weddings_married`） |
| I5 | 只有 Ledger 写 `stage`；每次写都是 `WHERE revision=@expected` 的 CAS |

### 3.4 惰性过期

求婚和预约的过期目前是在访问时惰性判断的，触发条件是超时或任一方不在线。保持这个语义，但收拢到一处：
- **写路径**：Ledger 在加载时执行到期迁移（T4、T7），与本次操作同一个事务。
- **读路径**（Query 接口）**不写库**：它计算「有效阶段」，例如 Requested 已过期就显示为 Engaged，留给下一次写操作真正落库。

## 4. 对外接口（4 个，同一实例实现；见落地状态）

按**调用方**划分，不按实现划分。只读接口可以放心到处注入；写接口只给少数命令处理器。

```csharp
// 只读视图：任何地方都可以注入，不写库
public interface IMarriageQueryDomainService
{
    ValueTask<MarriageView?> ViewAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<MarriageEngagementSnapshot> SnapshotAsync(AccountId account, CharacterId character, CancellationToken ct); // 03D0/03F3
    ValueTask<MarriageProposal?> ReceivedProposalAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageCeremony?> ReceivedReservationAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageChurchSnapshot?> ChurchAsync(SelectedSessionIdentity actor, CancellationToken ct);      // 03D4 重发（见落地状态）
    ValueTask<MarriageChurchSnapshot?> AppearanceAsync(SelectedSessionIdentity actor, CancellationToken ct);  // 03CF
    ValueTask<bool> CanCrossAsync(SelectedSessionIdentity actor, int channel, byte town, byte area, CancellationToken ct);
    ValueTask<bool> CanLeaveAsync(SelectedSessionIdentity actor, byte town, byte area, CancellationToken ct);
    ValueTask<ushort> RemainingInvitationsAsync(Guid ceremony, CharacterId actor, CancellationToken ct);
    ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct);
    ValueTask<IReadOnlyList<AccountId>> CoupleAccountsAsync(SelectedSessionIdentity actor, CancellationToken ct);  // 租约
    ValueTask<IReadOnlyList<AccountId>> WeddingAccountsAsync(Guid ceremony, CancellationToken ct);               // 租约
    ValueTask<IReadOnlyList<Guid>> ActiveWeddingsAsync(CancellationToken ct);                                    // 调度器
}

// 新人发起的写：阶段迁移 T1–T7 的唯一入口
public interface IMarriageCoupleDomainService
{
    ValueTask<MarriageProposalResult> ProposeAsync(SelectedSessionIdentity proposer, SelectedSessionIdentity recipient, CancellationToken ct);
    ValueTask<MarriageProposalResult> RespondProposalAsync(SelectedSessionIdentity recipient, SelectedSessionIdentity proposer, byte response, CancellationToken ct);
    ValueTask<MarriageCeremonyResult> RequestReservationAsync(SelectedSessionIdentity applicant, SelectedSessionIdentity partner, int tier, int waitIndex, CancellationToken ct);
    ValueTask<MarriageCeremonyResult> RespondReservationAsync(SelectedSessionIdentity partner, SelectedSessionIdentity applicant, byte response, CancellationToken ct); // 接受时一并完成入场，结果带 Admissions（见落地状态）
    ValueTask<MarriageLovePointResult> UseLovePointAsync(SelectedSessionIdentity actor, BusinessSlot slot, IReadOnlyList<AccountId> accounts, CancellationToken ct);
}

// 婚礼现场：教堂、宾客、祝词、到点推进（T8、T9）
public interface IMarriageVenueDomainService
{
    ValueTask<MarriageChurchEntryResult> EnterChurchAsync(SelectedSessionIdentity actor, uint key, uint area, byte flag, CancellationToken ct); // 03F7：新人和宾客合一
    ValueTask<MarriageWeddingLetterResult> InviteAsync(SelectedSessionIdentity actor, byte mode, uint target, CancellationToken ct);
    ValueTask<int> GiftAsync(SelectedSessionIdentity actor, uint key, int gold, CancellationToken ct);
    ValueTask<MarriageWeddingTransition?> AdvanceAsync(Guid ceremony, CancellationToken ct);
    ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct);
    ValueTask<MarriageGuestbookResult> ReadGuestbookAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct);
    ValueTask<int> PostGuestbookAsync(SelectedSessionIdentity actor, uint key, byte[] text, CancellationToken ct);
    ValueTask<int> DeleteGuestbookAsync(SelectedSessionIdentity actor, uint key, uint id, CancellationToken ct);
}

// 婚前任务：给任务领域用，签名不变
public interface IMarriageQuestDomainService { /* EligibleQuestIds / CanAccept / Bind / CanComplete，与现状相同 */ }
```

`MarriageView` 的内容：RelationId、Stage、Revision、Self、Partner、CeremonyId、MarriedAt、LovePoints，以及派生字段 TrialsCleared。

**为什么是 4 个，而不是 1 个**
- 只读调用方很多：TownProjection、选角准备、状态重放、三个任务服务、多个 slice。它们拿不到写能力，看构造器就知道每个调用方能做什么，这是仓库约定能成立的前提。
- 测试替身只需实现用到的那个面。
- 实现只有一份，所以多接口几乎没有成本。

### 4.1 旧方法 → 新接口

| 旧 | 新 |
|---|---|
| Proposal.ProposeAsync / RespondAsync | Couple.ProposeAsync / RespondProposalAsync |
| Proposal.FindReceivedAsync、Ceremony.FindReceivedAsync | Query.ReceivedProposalAsync / ReceivedReservationAsync |
| Ceremony.RequestAsync | Couple.RequestReservationAsync |
| Ceremony.RespondAsync + Wedding.PrepareAsync（`MarriageCeremonyCommandHandler` 里两步） | Couple.RespondReservationAsync（一步；见落地状态） |
| Engagement.ReadAsync | Query.SnapshotAsync |
| Church.PrepareAsync（教堂只在 T6 时分配） | Query.ChurchAsync（只读；见落地状态） |
| Church.EnterAsync + Wedding.EnterGuestAsync | Venue.EnterChurchAsync |
| Church.CanCrossAsync、Wedding.CanLeaveAsync / AppearanceAsync / RemainingAsync / InvitedAsync / AccountsAsync / PendingAsync / PhaseAsync / IsMarriedAsync | Query.*（Phase 和 IsMarried 改为读 View.Stage；见落地状态） |
| Wedding.InviteAsync / PayAsync / AdvanceAsync / CompleteCleanupAsync | Venue.InviteAsync / GiftAsync / AdvanceAsync / CompleteCleanupAsync |
| LovePoint.AccountsAsync / UseAsync | Query.CoupleAccountsAsync / Couple.UseLovePointAsync |
| Guestbook.Read / Post / DeleteAsync | Venue.*GuestbookAsync |
| Quest.* | Quest.*（不变） |

### 4.2 谁注入哪个面

| 调用方 | Query | Couple | Venue | Quest |
|---|:-:|:-:|:-:|:-:|
| MarriageProposalSlice（求婚、预约的 handler） | ✓ | | | |
| MarriageProposalCommand / MarriageCeremonyCommand 处理器 | | ✓ | | |
| MarriageEngagementSlice（03F3） | ✓ | | | |
| MarriageChurchSlice（03F7） | | | ✓ | |
| MarriageWeddingSlice（请柬、礼金、调度、清理） | ✓ | | ✓ | |
| MarriageLovePointSlice | ✓ | ✓ | | |
| MarriageGuestbookSlice | | | ✓ | |
| TownProjectionSlice | ✓ | | | |
| SelectCharacterPreparationService、ReplayStateDomainService | ✓ | | | ✓ |
| QuestCompletion / QuestFacts / QuestOfferRefresh、AcceptQuestCommand | | | | ✓ |

## 5. 实体与内部组件

### 5.1 形状（见落地状态）

```
MarriageDomainService (internal sealed partial)        ← 唯一实体，实现 4 个接口
  MarriageDomainService.Query.cs / .Couple.cs / .Venue.cs / .Quest.cs   ← 按接口拆文件
  构造器只接 9 个组件 + TimeProvider，不直接碰任何 store

  核心  CoupleLedger        关系账本：加载、身份核验、阶段 CAS 写入
        MarriageStageRules  纯规则（static）：迁移表、03D0 投影
        MarriageEligibility 资格：在线、同城、等级、性别、任务、金币
  流程  ReservationDesk     预约记录
        ChurchRooms         教堂房间、入场名单、站位、快照、跨区/离场判定
        WeddingSettlement   婚礼行、到点结算、退款、回礼、离场坐标、清理队列
  现场  GuestList           请柬、礼金、宾客入场资格
        Guestbook           祝词簿
  附属  TrialQuests         婚前任务绑定、试炼计数、DungeonMapCleared 处理
        LovePoints          心意点
```

### 5.2 组件职责与表的归属（每张表只归一个组件）

| 组件 | 独占的表 | 主要外部依赖 |
|---|---|---|
| CoupleLedger | `marriage_relations`（新）、`marriage_proposals`、`marriage_proposal_participants` | — |
| MarriageEligibility | —（只读） | 会话、TownArea、角色、任务日志、ProposalRule、CeremonyRule |
| ReservationDesk | `marriage_ceremonies` | CeremonyRule |
| ChurchRooms | `marriage_church_rooms`、`marriage_church_admissions` | ChurchRule、位置、位置持久化、TownArea |
| WeddingSettlement | `marriage_weddings`、`marriage_wedding_cleanup` | 邮件、物品投递、角色金币、角色 store（离线坐标）、ChurchRule |
| GuestList | `marriage_wedding_guests` | 在线快照、角色金币 |
| Guestbook | `marriage_guest_comments` | GuestbookRule |
| TrialQuests | `marriage_quest_bindings`、`marriage_trial_clears` | 任务内容与可用性规则、任务日志、事件服务、婚姻地下城内容 |
| LovePoints | `marriage_relations.love_points` 的写入经由 Ledger | 道具使用规则、效果结算、背包槽、LovePointRule |

### 5.3 组件规则

1. **只有 CoupleLedger 写阶段。** 关系 store 只注入 Ledger。LovePoints 加点也走 `ledger.AddLovePointsAsync`，因为它写的是关系行。
2. **组件之间不互相调用**，由实体编排（见落地状态）。组件只接收实体传下来的 `Couple` 值（同一操作内加载的那一份）和必要的参数。
3. **每个操作只加载一次、提交一次**：`Load → 守卫 → 各组件副作用 → ledger.Commit(couple, next)`。CAS 失败时返回 `Stale`。（见落地状态）
4. **组件是具体的 internal 类型，不提供公开接口**：在 DI 里以具体类型注册为 singleton（它们本身无状态，状态都在 store 和环境事务里）。
5. **规则服务保持原样**。按 AGENTS 的三层约定，组件是业务层，由它们去问 `IMarriage*RuleService`。

### 5.4 内部数据流示例：接受预约（T6）（见落地状态）

```
Couple.RespondReservationAsync(partner, applicant, 0)
  couple = ledger.LoadPair(applicant, partner)            // 一次加载 + 统一身份核验 + 惰性过期
  StageRules.Next(couple.Stage, AcceptReservation) == Preparing ?
  request = reservation.Pending(couple)                   // 读预约
  eligibility.Recheck(couple, request)                    // 同城、任务、金币
  reservation.Accept(request)                             // ceremony → Reserved
  admissions = church.Allocate(couple, request)           // 分房间 + 两人入场
  settlement.Open(couple, request)                        // 扣费 + 婚礼行 Preparing
  ledger.Commit(couple, Preparing, ceremony: request.Id)  // CAS
  return Result(..., Admissions = admissions)
```

任一步失败都抛异常，整笔回滚，与现状一致；之后仍由 slice 派 response=2 把预约改为过期。之后可以考虑用 `RequireRollback()` 加一次过期命令，把这一步收回实体内部，不在本次范围内。

### 5.5 DI 注册

```csharp
services.TryAddSingleton<CoupleLedger>();  /* … 其余 8 个组件同样 … */
services.TryAddSingleton<MarriageDomainService>();
services.TryAddSingleton<IMarriageQueryDomainService>(sp => sp.GetRequiredService<MarriageDomainService>());
services.TryAddSingleton<IMarriageCoupleDomainService>(sp => sp.GetRequiredService<MarriageDomainService>());
services.TryAddSingleton<IMarriageVenueDomainService>(sp => sp.GetRequiredService<MarriageDomainService>());
services.TryAddSingleton<IMarriageQuestDomainService>(sp => sp.GetRequiredService<MarriageDomainService>()); // 见落地状态
services.AddDomainSubscriber<DungeonMapCleared, MarriageTrialClearSubscriber>("…", Reliable); // 薄订阅类，转交 TrialQuests（见落地状态）
```

订阅不挂在实体上，改用薄订阅类。原因：`AddDomainSubscriber` 内部会以 `TryAddScoped<THandler>` 注册，如果把实体本身当订阅者，生命周期会和 singleton 注册互相纠缠。

## 6. 数据迁移

### 6.1 新表（主键与现有 relation id 相同，所有外键原样有效）

```sql
CREATE TABLE IF NOT EXISTS marriage_relations (
  id TEXT PRIMARY KEY REFERENCES marriage_proposals(id),
  stage INTEGER NOT NULL CHECK(stage BETWEEN 0 AND 5),  -- 见落地状态
  revision INTEGER NOT NULL CHECK(revision>=1),
  stage_since INTEGER NOT NULL,
  ceremony_id TEXT REFERENCES marriage_ceremonies(id),
  married_at INTEGER,                          -- 可空：旧数据没有完成时刻，不编造
  love_points INTEGER NOT NULL DEFAULT 0 CHECK(love_points>=0),
  CHECK((stage IN (2,3,4)) = (ceremony_id IS NOT NULL))
);
```

- `married_at` **不加**「stage=4 时必须非空」的约束：`MarriageWeddingMigrationTests` 已经确认，旧婚礼行的 `completed_at` 为 0 表示未知。
- `stage` 的 CHECK 以后扩离婚阶段时要放宽。SQLite 改 CHECK 需要重建表，所以离婚 PR 要自带一次重建迁移。另一种做法是现在就写 `BETWEEN 0 AND 15`，二选一，实施时决定。

### 6.2 回填（启动建表后执行，`INSERT OR IGNORE`，幂等）

| 现状 | 回填阶段 | ceremony_id | 其它 |
|---|---|---|---|
| proposal Rejected / Expired | Closed | NULL | — |
| proposal Pending | Proposed | NULL | 过期的交给惰性迁移处理 |
| Engaged，且有 phase=1 的婚礼 | Married | 该婚礼的 ceremony | married_at = completed_at（为 0 时写 NULL）；love_points 从该婚礼行复制 |
| Engaged，且有 phase=0 的婚礼 | Preparing | 该婚礼的 ceremony | — |
| Engaged，且有 state=0 的 ceremony | Requested | 该 ceremony | 已过期的交给惰性迁移处理 |
| Engaged，ceremony 为 Reserved 但没有婚礼行 | Engaged | NULL | **异常**：按现有流程不应出现。计数写日志，不自动修复 |
| 其它 Engaged | Engaged | NULL | — |

- `stage_since` 一律写迁移时刻，不从旧表推猜。
- revision 从 1 开始。

### 6.3 切换与回退

- **不做双源读**：同一个版本内，所有阶段判断都改读 `marriage_relations`。
- **心意点双写一个版本**：同时写 `marriage_relations.love_points` 和旧的 `marriage_weddings.love_points`。如果需要回退二进制，旧版本读到的点数不会丢；确认稳定后的下一个 PR 再停止写旧列（列本身保留，SQLite 删列成本高）。
- 其它旧表结构不动，ceremony 和婚礼行作为明细和历史保留。

## 7. 并发与事务

- **情侣命令**：沿用 `IMultiAccountCommand`，同时租两个账号（租约名单由 `Query.CoupleAccountsAsync` 预取，执行时由实体复核，与现状一致）。
- **第二道防线**：关系行 revision 的 CAS。即使租约名单在预取后发生变化，也不会出现两个迁移同时成功。
- **婚礼推进**：沿用按 `WeddingAccountsAsync` 租多个账号，Transactional。
- **试炼订阅**：Reliable 订阅，不在账号租约内。它只写 bindings 和 clears，不改阶段，所以不需要 CAS。关系在并发中被关闭时，计数记在旧 relation id 上，无害。

## 8. 行为变化清单

**刻意的修正（单独提交，PR 里单列）**

| # | 变化 | 可见影响 |
|---|---|---|
| B1（见落地状态） | 六处「是不是一对」统一由 Ledger 判定：双方都核对账号和角色，并统一拒绝 Closed | LovePoint、Guestbook 原先完全不核账号，Church、Engagement 只核本人。角色不会换账号，所以正常玩法下看不出差别；这一项的意义在于消除口径分叉 |
| B2 | 心意点从婚礼行迁到关系行 | 线上无变化 |
| B3 | 结婚后不再查 ceremony 的状态 | 无变化，只是去掉了「先排除已婚」的必要性 |

**必须保持不变**
- 03D0 state 的取值；03D4、03CF、03F7 的字节和状态码（-1…-10）；
- 退款、礼金分成和回礼的金额与次数；
- 离场 03CF 的语义（见 `Opus-WeddingInvestigation-Report-20261016.md` §4.1）。

## 9. 实施步骤（建议 4 个 PR；见落地状态）

1. **PR0 表征测试（不改生产代码）**：按行为目录查漏，补齐还没有逐条覆盖的分支，先把现有行为钉住。
2. **PR1 关系表与 Ledger**：
   - 加 `marriage_relations`、回填、`CoupleLedger`、`MarriageStageRules`；
   - 旧的 8 个服务改为通过 Ledger 判断阶段，接口不变；
   - 心意点开始双写。
3. **PR2 合并实体**：
   - 拆出 9 个组件，`MarriageDomainService` 实现 4 个新接口；
   - 调用方切换到新接口，删除旧接口和旧类；
   - 架构判据一并加入。
4. **PR3 行为修正**：B1；停止心意点旧列的写入。

每个 PR 只把 PATCH 版本号递增一次。PR0 到 PR2 合并后线上字节应完全不变，用 §10.6 的回归清单验证。

## 10. 测试计划

1. **阶段规则（纯函数）**：
   - 对 6 个阶段 × 全部触发做穷举表测试，每个组合要么得到期望的下一个阶段，要么被拒绝。**这张表本身就是规格。**
   - 测 `WireState` 的全部映射。
2. **Ledger**：
   - CAS 冲突返回 Stale；
   - 账号不匹配被拒；
   - 惰性过期在写路径落库，在读路径只计算不写库；
   - 对 I1、I2、I5 各写判据。
3. **组件**：
   - 结算：完成和取消两条分支的金额、邮件数量、离线角色坐标改写、清理重试不擦掉新婚礼的上下文；
   - 宾客：请柬的 -1、-4、-5、-6、-7 和随机派送数量；礼金的上下限和幂等；宾客入场的 -10 和 -6；
   - 祝词簿：新人、已入场宾客、未入场宾客、取消婚礼四类访问矩阵；
   - 心意点：-1、-2、-3 与上限；
   - 婚前任务：绑定、计数、事件过滤（非 boss 房、三人队、非本关系的搭档）。
4. **迁移**：
   - §6.2 每一行一个用例，包括 `completed_at=0` 的旧已婚行和异常行计数；
   - 重复启动幂等；`PRAGMA integrity_check` 通过；
   - 对 I3、I4 做判据：回填后对每条关系核对婚礼行数量。
5. **架构判据**（反射遍历构造器）：
   - 只有 `CoupleLedger` 能接受关系 store；
   - 9 个组件互不依赖（见落地状态）；
   - `MarriageDomainService` 不接受任何 `*DomainStore`；
   - 同时写一条反例用例，证明判据确实会拒绝违规。
6. **回归**：
   - 现有 41 条婚姻用例和 codec 测试除改名外保持通过；
   - 双端实机走 `Opus-WeddingInvestigation-Report-20261016.md` §6 的清单，外加心意点和祝词簿各一遍。

## 11. 未决问题

1. 离婚阶段的 CHECK 现在就放宽，还是到时候重建表（§6.1）。
2. 祝词簿婚后也能读、能删，放在 Venue 里在语义上略牵强。另一种做法是放进 Couple，倾向保持 Venue。
3. `Requested` 是否值得单独成为一个阶段：它的作用是把「已有待答预约」从推导变成显式记录，代价是多两条迁移。倾向保留。
4. 任务服务里对婚姻服务的可空注入（`IMarriageQuestDomainService? marriage = null`）要不要顺手改成必需注入，不在本次范围内。

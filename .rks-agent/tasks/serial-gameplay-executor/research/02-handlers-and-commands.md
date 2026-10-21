# 调研 02：协议处理器与命令结构

基准 `a02bce46`，只读盘点。P/ = `Server/src/Dfo.Server.Protocol/`，A/ = `Server/src/Dfo.Server.Application/`。

## 汇总

| 项 | 数量 |
|---|---|
| `ClientMessageHandler<T>` 类 | 141（ProtocolModule 173 条 RegisterMessageHandler 对应 141 个类型） |
| a 纯协议或只记日志 | 18（加只读后记日志的 BossDieCheck 共 19） |
| b 一条命令 + 回包 | 86 |
| c 同一 handler ≥2 次 SendAsync | 16 个类；按文件 18 个 handler 文件 + 5 个编排文件，共 23 |
| d 绕过命令直改领域状态 | 不发命令的 7 个，另有发命令的若干 |
| e 委托 Delivery/Coordinator/Projection | 42 个类注入，其中 13 个纯委托 |
| 注入执行门的 handler | 42 个，其中 28 个自己 Acquire/Enter |
| `ICommandHandler` | 204：Transactional 170、Direct 21、ReadOnly 9、Workflow 4 |
| 生产代码构造的命令类型 | 200：184 种只在一个文件构造，16 种多处构造 |

## 1. handler 形状

- a：ChannelConnect、GameExit、ConnectionCheck、GenCeraTicket、MercenaryInfo、HiddenCharacterInfo、ContentsPlayInfo、UnansweredReport、SecurityStatus、ClientSpec×2、ClientHardwareDetail、SessionMetrics、RosterFollowUp、MapEntryReport、StoryPause、CreatureReports×2（例 `P/Game/Exit/GameExitHandler.cs:13`）。
- b：物品/效果类为主（例 `P/Game/SellItem/SellItemHandler.cs:14`），不少在命令前做 GameSlot→BusinessSlot 映射和只读查询。
- c 顺序多次提交（真编排）：SelectCharacter 15 次（`SelectCharacterHandler.cs:107-419`）；ReturnToSelection（Prepare→Complete）；CreateCharacter（Create→查 Roster，`CreateCharacterSlice.cs:116/138`）；GameLogin（Redeem→ActivateHotkey，`GameLoginHandler.cs:45/87`）；Rent/ChargeRentPoint（变更→ReadRentalRefresh，`RentalSlice.cs:117/130`、`:149/152`）；WeddingResponse（Ceremony→两次 PrepareChurch，`MarriageProposalSlice.cs:197-217`）；TournamentReward（Begin→Select，`TournamentSlice.cs:205/210`）；MonsterDeath（ObjectDestruction→ProcessMonsterDeath，`MonsterDeathSlice.cs:426/437`）。分支择一：BuyItem、Disjoint、DeleteItem、TitleBookMutation、JoustQuery、WeddingProposal、PremiumServiceUsage（例 `TitleBookSlice.cs:125-128`）。编排文件：BossTowerProjectionDelivery 14 次、IllusionTowerProjectionDelivery 10 次、两个 PartyCoordination 各 5 次、DungeonPartyWipeService 2 次。
- d（违反「handler 只允许四类」）：`DungeonEntrySlice.cs:476` `entryIntents.Consume`（另 :485/524/537 RestoreIfAbsent）、`:761` `visits.TryMoveAsync`；`DungeonSelectionSlice.cs:237` ClearExact、`:271` `runs.EndAsync`、`:631` `entryIntents.Record`、`:678` `currentState.RefreshAsync`；`TownProjectionSlice.cs:361/364` `persistence.PersistAsync`、`:368` CrossInto、`:446` StepTo；`PartyCommandSlice.cs:531` `endpoints.TryObserve`；`DungeonClearSlice.cs:852` `runs.TryClaimSettlement` 及 5 处 ReleaseSettlement；`GameLoginHandler.cs:149` `TryDisconnectConnection`。6 个任务类 handler 调 `offers.RefreshAsync`（例 `AcceptQuestHandler.cs:129`）。
- e 纯委托 13：Altar×3→AltarProtocolCoordinator；Mailbox×5→MailboxDelivery；LotteryOpening→LotteryOpeningDelivery；FinishLoading、IllusionTowerStage；CardSelectRightState、ScoreScrollState→DungeonCardPresentationCoordinator（`ResultScreenSlice.cs:215`）。SelectDungeon 先经 d 再交 DungeonEntryProjectionDelivery 或两塔 delivery（`DungeonEntrySlice.cs:542`）。

## 2. 命令处理器

- 执行方式：Transactional 170、Direct 21（含 Party 系，`ProtocolModule.cs:799`）、ReadOnly 9、Workflow 4（Publish、PvpReady、Compensate、CompleteReturnToSelection，`ProtocolModule.cs:560`）。
- OnlineUser 门：Exclusive 54、Shared 2、Unscoped 1、未声明 147。Account 门：Exclusive 120、Shared 4、未声明 80。
- 作用域接口：IAccountCommand 102、IOnlineUserCommand 56、ISelectedSessionCommand 32、IRequestSettlementCommand 39、IMultiSelectedSessionCommand 8、IMultiAccountCommand 17、都没实现 79。
- 构造点：只在一处的 184 种中 121 种在 handler/切片、29 种只在 Delivery 或后台、34 种只在 GM/HTTP；多处构造 16 种（LeavePartyCommand 4；TransferPartyLeadership、RemovePartyMember、SaveClientSettings、ReadPremiumSnapshot 各 3；三个 RefillOnline*Ticket 由选角与日刷新共用；SelectCard、SelectTournamentReward、AltarAction、ActivateClientHotkeyProfile 各 2）。
- 非入站协议来源：后台（Tau、SouthernDale、BossTower 三个 DailyRefill，OnlineExpiredItemCleanup，JoustMaintenance，CharacterNameTagExpiration，AltarScheduler，TournamentRewardSweep，PremiumDailyRefresh，WeaknessRecovery，DungeonPartyWipe）；GM 端点 34 条命令。

## 3. 多阶段工作流

1. SelectCharacter（`SelectCharacterHandler.cs:103-273`）：持会话门独占；Prepare(T) → CleanupExpired(T) → EnsurePuppets(T) → InitializeHousing(T) → PrepareState(T) → 发校验过的状态消息 → DailyRefill(T) → Publish(W) → 三张 Refill 票 → Hotkey(D) → PvpReady(W) → Quests(T) → Pet(T) → 配额投影。每阶段各自事务；失败派发 `CompensateSelectedCharacterSessionCommand`（:213，Workflow，逐项尽力并聚合失败），清理通告在 catch 中投递，释放门后才关连接（:257）。
2. ReturnToSelection：会话门再在线门（均独占）；Prepare(T) → Complete(W)；失败只投递清理通告并关连接，无补偿命令。
3. OnlineUserConnectionObserver：完全不经总线；先 lifecycle 门再 execution 门，直调 `cleanup.CleanupAsync`（内含补偿）与 `TryDisconnectConnection`，binding 变了就重试。
4. DungeonEntryProjectionDelivery：先提交 `EnterDungeonCommand`（:102），再校验接收方与 cohort（:131-170），失败 `RollbackEntry` 直调 `runs.TryAbortEntry`（:509，命令外的内存级回滚）；部分 cohort 失败关相关连接；:103-105 有直接 new `EnterDungeonCommandHandler` 绕过总线的后备路径。
5. 两塔/队伍（`IllusionTowerPartyCoordination.cs:101-239`，Boss 同构）：拿 run 投影门与多账号租约后先提交塔命令，再发 Direct party 命令（:165）；第二阶段失败不回滚塔，只记错误并关相关会话（:201-207）。BossTowerProjectionDelivery 有 Load→Start 连续提交（:214/218）。

## 4. 执行上下文 AsyncLocal

- `DomainTransactionScope`（`A/Transactions/DomainTransactionScope.cs:16`）：环境事务，同一 flow 只能一个；UnitOfWork(60) 开启，覆盖 Transactional 与 ReadOnly。无环境事务时领域服务自开边界并抢全局门（`A/World/TransactionalDungeonVisitDomainService.cs:17-19`）。
- `SelectedSessionCommandScope`（`SelectedSessionExecutionBehavior.cs:48`）：45 设置 recipient，58 校验。
- `EffectOperationContext`（`A/Effects/EffectOperationContext.cs:30-32`）：三个 AsyncLocal；Ledger 按入站消息设置 frame（`EffectOperationLedger.cs:69`）；`BeginMain` 规定一次网络效果请求只能有一个主结算命令（:66）——与「一协议一 CMD」天然吻合。
- `NetworkCommandAuthorization`（`A/Commands/NetworkCommandAuthorization.cs:8`）：dispatcher 按消息设置（`NetworkMessageDispatcher.cs:28`），100 与 58 调 DemandCurrent。
- 会话门帧（`SelectedSessionTransitionGate.cs:9`）；`CommandBus.DispatchDepth`（`A/Commands/CommandBus.cs:9`，运行期禁止嵌套 dispatch）。
- 以上均为 flow 级、挂在单例上。真正依赖「每条命令一个 DI scope」的只有 Scoped 的 8 个管线行为、命令处理器与 Invoker（`CommandDispatchingExtensions.cs:36-73`）以及 `ClientAppearanceVisibilityProjection`（`ProtocolModule.cs:237`）。handler 是 Transient，每消息一个 scope。
- 全局门不可重入：CMD 持有它时走到「无环境事务自开边界」会死锁。
- AGENTS.md 过时：管线实际 8 层（45/57/58 未记），有 ReadOnly，Workflow 实际 4 条。

## 5. 架构判据

- `CommandHandlerArchitectureTests`（`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs:58-94`）：命令处理器构造参数只能是 Abstractions 中 `*DomainService` / `*DomainStore`，豁免 TimeProvider、IPartyService；不得依赖 ICommandBus。**协议级 CMD 吸收 sender、Delivery、门、closer、ILogger、`IInventorySlotMappingRuleService`、`ISelectedSessionPositionPersistence` 后正面冲突**；子命令须改成应用服务。
- `NotificationHandlerArchitectureTests`（`.../Notifications/NotificationHandlerArchitectureTests.cs:54`）：通知 handler 只能注入 DomainService；不直接冲突。
- `MarriageArchitectureTests`：婚姻用例图单向、store 唯一所有者、禁 Func/Lazy/IServiceProvider（:87-92）；新 CMD 落进 Marriage 命名空间或引入延迟解析时冲突。
- `InventorySlotBoundaryArchitectureTests`（:17-27、:104）：六条物品命令只能暴露 BusinessSlot、协议模型只能 GameSlot；CMD 直接携带入站 GameSlot 会冲突。
- `EffectSettlementRegistrationTests`（`.../Sessions/EffectSettlementRegistrationTests.cs:13`）：钉死结算请求、主结算命令、非主选中会话命令三份名单且要求 Transactional；改名或合并须重写。
- `EventNotificationBoundaryTests`：只允许少数类型依赖通知总线。
- ItemIdentity、PartyBoundary、HttpApi/Host 程序集引用测试基本不受影响。`CommandBusTests.cs:93` 断言嵌套 dispatch 抛异常。

## 6. 共用编排类

TownAreaProjectionDelivery 14 处；UsageQuotaProjectionDelivery 12 处；PartyNotificationDelivery 5 处；ReplayStateProjectionDelivery（EffectOperationLedger、JoustSlice；自己 borrow 门并发 ReadReplayStateCommand，`ReplayStateProjectionDelivery.cs:36-38`）；IllusionTowerProjectionDelivery 11、BossTowerProjectionDelivery 9；DungeonRunDeliveryRegistry 16；DungeonProjectionGate 14；DungeonCardPresentationCoordinator 5；ICleanupNotificationDelivery 3；Weakness、AchievementTitleProjectionGate 各 5。

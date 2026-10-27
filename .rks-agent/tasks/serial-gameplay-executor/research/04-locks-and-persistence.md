# 调研 04：锁、并发原语与持久化

基准 `a02bce46`，只读盘点。路径相对 `Server/src/`。

**与 AGENTS.md 不一致**：管线实际顺序 0 Logging → 45 SelectedSessionExecution（会话门 Shared 借用）→ 50 OnlineUser → 55 Account → 57 Transactional（全局单名额）→ 58 SelectedSessionSettlement → 60 UnitOfWork → 100 Policy；另有 `ReadOnly` 执行方式（`UnitOfWorkBehavior.cs:28` 开只读事务）。AGENTS.md 写的 `PersistOnTeardownAsync` 已不存在，对应 `SelectedSessionPositionPersistence.AloneAsync`。

## 门与信号量

| 门 | 定义 | 使用点 | 总线内/外 | 保护什么 |
|---|---|---|---|---|
| `UserExecutionGate<T>` 基类 | `Application/OnlineUsers/OnlineUserExecutionGate.cs:8` | — | — | 按 key 读写租约 |
| `OnlineUserExecutionGate` | 同文件 :263 | 管线 `OnlineUserExecutionBehavior.cs:30`；`ReturnToSelectionHandler.cs:29`；`OnlineUserConnectionObserver.cs:101` | 内 1 外 2 | 同一在线用户的命令与断线清理互斥 |
| `AccountExecutionGate` | :265 | 只有 `AccountExecutionBehavior.cs:41` | 内 | 同账号命令共享/独占 |
| `SelectedSessionTransitionGate` | `OnlineUsers/SelectedSessionTransitionGate.cs:7` | 管线 45 借用；`SelectedSessionSafety.cs:64`；Protocol 约 60 文件 90 多处 | 主要在外 | 会话切换/拆除串行 + 围住「读→发送」 |
| `TownAreaProjectionGate` | `TownAreaProjectionDelivery.cs:17` | 同文件 :722 | 外 | 同区域投影顺序 |
| `DungeonProjectionGate` | `Dungeons/DungeonLoadingProjectionDelivery.cs:13` | DungeonLoading :86、DungeonEntryProjectionDelivery :158、DungeonClear :242、LeaveDungeon :200、CharacterDeath :108、UseRevivalCoin :81、PartyWipe :76、Altar Handlers :27 / Scheduler :55、Tournament Slice :198 / RewardSweep :38、RoomMechanism :88、PremiumServiceUsageReport :46 | 外（含后台） | 同副本投影顺序 |
| `BossTowerProjectionGate` / `BossTowerEntryGate` | `BossTowerProjectionDelivery.cs:44/45` | 同文件 14 处；`BossTowerPartyCoordination.cs:86` | 外 | 塔 run 投影 / 角色入场 |
| `IllusionTowerProjectionGate` / `IllusionTowerEntryGate` | `IllusionTowerProjectionDelivery.cs:38/39` | 同文件 12 处；`IllusionTowerPartyCoordination.cs:85` | 外 | 同上 |
| `AchievementTitleProjectionGate`（TitleBook 64 路 SemaphoreSlim） | `TitleBook/TitleBookSlice.cs:91-93` | AchievementSlice :69、:88（订阅者）；SelectCharacterHandler :144；TitleAppearanceSlice :57；TitleBookSlice :119、:188（订阅者）；MoveItemSpaceSlice :859 | 外，含事件循环 | 按角色给称号投影与 ACK 排序 |
| Party 按角色 SemaphoreSlim | `PartyNotificationSlice.cs:462、:467` | PartyTownProjectionDelivery :60、:175；PartyNotificationSlice :658 | 外 | 跨队伍代际投影顺序 |
| `PartyDeliveryStream.Gate`（按代际） | `PartyNotificationSlice.cs:512` | :661、:733；PartyTownProjectionDelivery :65、:197 | 外 | 同一队伍代际的投递顺序 |
| `TransactionalExecutionGate` | `Pipeline/TransactionalExecutionBehavior.cs:40` | 见下 | 内 + 外 | 同一时刻一个 SQLite 写者 |

基类特性：FIFO（有等待者时不插队，队首 Shared 连续放行，:206）；读写；**不可重入**（只有会话门用 AsyncLocal `Frame` 链实现借用，并拒绝 Shared→Exclusive 升级，:130）；取消时移出队列并继续放行（:81）；空闲 entry 引用计数归零后回收（:171）；释放幂等。会话门另有 `AcquireManyExclusiveAsync`（:12）。

清单外 SemaphoreSlim：`EffectOperationLedger` 按连接一把（:37）、`DomainEventDispatcher` groupGates（:50）、`WeaknessRecoveryService.cs:31`、Joust 维护/规则、CharacterStatRule 初始化、ScheduledTask slots、PVF 内容源。

## TransactionalExecutionGate 与绕过它的 SQLite 写

使用者：管线 :34；`DomainEventDispatcher.cs:101`（重试）、:115（Reliable）、:131（LiveOnly），`ReadDueAsync`（:68）在门外读；`DungeonLifecycleDomainService.cs:19`（断线记离场事件）；`TransactionalDungeonVisitDomainService.cs:18`、`IllusionTowerDomainService.cs:48`、`BossTowerDomainService.cs:59`（无环境事务时自开）。

不经它却写或可能写 SQLite：
1. `SelectedSessionPositionPersistence.AloneAsync`（`Characters/CharacterStateAccess.cs:159-173`）：拆除时自开事务写位置。
2. `CreatureRuntimeDomainService.WithinAsync`（:264-284）：`scope.Begin()` 后提交或自 new；调用方 `LeaveAsync` 来自拆除（`SelectCharacterLifecycleServices.cs:122`）。
3. `ItemGrantDelivery.AddAsync`（`Inventory/ItemGrantDelivery.cs:21`）：无环境事务时自开。
4. `MailboxDomainService.RunAsync`（`Mail/MailboxDomainService.cs:173`）：同上。
5. 启动期：`EpicBookDomainService.InitializeAsync`（:71）；`CharacterExperienceStoragePolicy.InstallAsync`（`Persistence/Schema/...:15`，直接开连接、非 deferred）；`CharacterDatabaseSchema.Ensure`（`SqliteDatabase` 构造中，:43）及各 Migration。
6. `AmbientStoreAccess`（`CharacterStateAccess.cs:27`）：易失写自开一次性边界（:94），持久写在 :86 被拒；`ReadSnapshotAsync`（:37）开非只读 `Begin()` 但从不提交。
7. GM 与 HttpApi 全走 `ICommandBus`；ReadOnly 与 Direct 命令不经此门。
8. 约 25 处 `AcquireWriteAsync`（如 `SqliteMailboxRepository.cs:22`）用 `UPDATE … WHERE 0` 提前拿写保留，防 deferred 升级失败。

SQLite：每用例一个 `DomainTransaction`；每数据库文件一个 `SqliteDatabaseParticipant`，懒开连接；`BeginTransaction(deferred: true)`（`SqliteDatabaseParticipant.cs:89、:143`）；只读 `Mode=ReadOnly`（:102）；事务外读每次新连接（`SqliteDatabase.cs:70`），事务外写拒绝（:94）。PRAGMA 只有 `journal_mode=WAL`（`CharacterDatabaseSchema.cs:145`）和一处迁移 `defer_foreign_keys`；无 `busy_timeout`，用默认 30 秒；连接池默认开。

## 短 lock(sync)

- Application 190：DungeonRunDomainService 系 64、OnlineStateRegistry 24、CreatureRuntime 13、PartyService 11、LoginSessionRegistry 10、Weakness 8、TownArea 7、DungeonParticipation 7、Tournament 6、BossTower 6、AltarDungeonEngine 6、HousingState 6、IllusionTower 5、SelectedSessionTransitionGate 4、OnlineUserExecutionGate 3、CharacterContainer 3、ScheduledTask 2、SelectedSessionSafety 2、WorldDrop / MonsterDeclaredDrop / EffectOperationContext 各 1。
- Protocol 11：DungeonRunDeliveryRegistry 6、DungeonCardPresentationCoordinator 4、PartyClientProjectionService 1。
- Persistence 3（RunItemDomainStore :40、:150、:157）；Networking 1（GameInboundFrameCounter :12，传输线程）；GmWeb 5（GmLoginLimiter、GmIconPackStore，HTTP 线程）；Infrastructure 106（PVF 缓存）。

单执行器后仍会被总线外线程访问：OnlineStateRegistry（断线回调、LoginSessionMaintenanceService `LoginSessionRuntime.cs:45`、后台 `IsInWorld`）；LoginSessionRegistry（后台扫描、`CloseRequests` Channel）；DungeonRunDomainService（RoomMechanismMaintenanceService 直调 `AdvanceRoomMechanisms` `RoomMechanismProjection.cs:76`、DungeonPartyWipeService :57/:63、拆除）；CreatureRuntime（CreatureClockService 的 `DueSessions`/`DrainProjections`）；BossTower/IllusionTower/Tournament/AltarDungeonEngine（各自 Sweep/Scheduler）；PartyService/TownArea/DungeonParticipation（拆除）；DungeonCardPresentationCoordinator（延时自动翻牌）；GameInboundFrameCounter、GmWeb 两类；事件分发循环中的两个称号订阅者（AchievementSlice :88、TitleBookSlice :188）。

## 其他并发原语

| 原语 | 计数 | 代表 |
|---|---|---|
| Interlocked | App 12 / Proto 5 | DungeonRun `nextClaim` |
| volatile | App 9，其余各 1 | `CharacterStatRuleService.cs:16` |
| ConcurrentDictionary | App 8 / Proto 2 / Net 1 / Infra 5 | `OnlineCharacterRuntime.cs:21`、`TouchSocketConnectionRegistry.cs:9` |
| ConcurrentQueue | App 1 / Proto 2 | `NotificationRetryQueue.cs:29`、两塔 |
| Channel<T> | App 5 / Proto 1 | `DomainEventDispatcher.cs:15`、`ScheduledTask.cs:23`、`LoginSessionRegistry.cs:33`、`DungeonRoomMechanismWakeSignal.cs:8`、`AltarScheduler.cs:13` |

AsyncLocal 共 8 处（全在 Application）：会话门帧 `SelectedSessionTransitionGate.cs:9`、`DomainTransactionScope.cs:16`、`SelectedSessionCommandScope`（`SelectedSessionExecutionBehavior.cs:48`）、`NetworkCommandAuthorization.cs:8`、`CommandBus.cs:9`（`DispatchDepth`）、`EffectOperationContext.cs:30-32`（3 个）。

## 改造后要删或改写的测试

- 测门本身：`OnlineUserExecutionGateTests`(5)、`SelectedSessionTransitionGateTests`(12)、`TransactionalCommandSerializationTests`(3)、`MultiAccountExecutionTests`(2)、`CommandBusTests` 3 个租约释放方法。
- 「先读后写等 30 秒」：`Persistence.Tests/Accounts/AuthenticationConcurrencyTests`（第二个登录最多等 40 秒）。
- 整类并发：`BossTowerProjectionConcurrencyTests`、`IllusionTowerProjectionConcurrencyTests`（各 11）。
- 部分方法测门或排队：Application.Tests 的 `SelectCharacterLifecycleTests`(4)、`OnlineUserServiceTests`(3)、`EffectOperationReplayTests`、`ReplayStateSnapshotTests`、`OnlineExpiredItemCleanupTests`、`CommonExperienceEffectTests`(3)；Persistence.Tests `LoginSessionQueuedCommandTests`；Protocol.Tests `PartyCommandHandlerTests`(5)、`PartyTownProjectionDeliveryTests`、`TownProjectionHandlerTests`、`LeaveDungeonTests`(3)、`DungeonLoadingProjectionDeliveryTests`(2)、`SelectDungeonHandlerTests`、`PartySelectDungeonHandlerTests`、`TitleBookProtocolTests`(2)、`TownAreaProjectionDeliveryTests`。
- 约 25 个 `Concurrent_*` 方法测并发互斥结果：业务语义可保留，并发构造要改写（CeraShop、EpicCraft、ItemSale、VendingMachine、WarehouseStorage、MarriageProposal、MarriageCeremony、Mailbox、QuestHelperPuppet、IllusionTowerDomainService、DungeonVisit；Host.Tests 的 GmCera、GmEvents、GmOfficialMail、GmLoginLimiter）。

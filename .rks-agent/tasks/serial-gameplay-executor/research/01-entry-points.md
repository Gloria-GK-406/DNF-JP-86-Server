# 调研 01：会改状态、写库或发包的全部入口

基准 `a02bce46`，只读盘点。路径相对 `Server/src/`。简称：会话门＝`SelectedSessionTransitionGate`，在线门＝`OnlineUserExecutionGate`，账号门＝`AccountExecutionGate`，投影门＝`DungeonProjectionGate` 与两塔投影门，全局门＝`TransactionalExecutionGate`。

## 汇总

| 类别 | 入口数 | 全部经命令总线？ | 代表位置 |
|---|---|---|---|
| 网络收包 | 2 插件、1 dispatcher、1 ledger | 否（handler 内有直改、直发） | `Networking/Runtime/NetworkPlugins.cs:54`、`:135` |
| 连接生命周期 | 2 observer、1 closer | 否 | `Protocol/Game/OnlineUserConnectionObserver.cs:28`、`:66` |
| 后台服务 | 自有 33，另 TouchSocket TCP 2、Kestrel；调度任务 5 | 否 | §3 |
| 领域事件订阅 | 52 个注册：Reliable 17、LiveOnly 22、Runtime/Direct 13 | 否 | `Application/DomainEvents/DomainEventDispatcher.cs:112`、`:127` |
| HTTP | 启动器 3、GM 约 45 | 写操作基本经总线；1 处直发 | `GmWeb/GmCeraEndpoints.cs:52` |
| 启动与预热 | 5 | 否（在监听前） | `GameServer/GameServerStartupService.cs:76`、`:86` |
| 其他异步流 | 4 类 | 否 | `Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:54`、`:168` |

## 1. 网络收包

- `NetworkPlugins.cs:54`（Channel）、`:135`（Game）在 `OnTcpReceived` 里 await `DispatchAsync` 后才 `InvokeNext`：同一连接逐包串行，跨连接并发。TouchSocket 关闭时会等当前收包回调结束（`TouchSocketNetworkConnectionCloser.cs:33` 注释）。
- `NetworkMessageDispatcher.cs:12`：每消息新 DI scope，检查 `SelectedSessionSafety` 与 `LoginSessionRegistry.IsSuperseded`，再调 handler。
- `EffectOperationLedger.cs:17`：按物理连接一把 SemaphoreSlim；重放到已提交请求时直接 `delivery.RefreshAsync`（约 :58）发包，不经总线。
- Channel 端点：Connect 直接回包；ScriptVersion/Script/Info 走 Direct 命令；无连接 observer。
- Game handler 直改：`GameLoginHandler.cs:149` `TryDisconnectConnection`、:152 请求关连接；`TownProjectionSlice.cs:368` `CrossInto`；`MarriageChurchSlice.cs:126` `CrossInto`；`DungeonClearSlice.cs:879` 等 `ReleaseSettlement`；多个 handler 直接 SendCmd/SendNoti/ProjectAsync。

## 2. 连接生命周期

- `NetworkPlugins.cs:20` Connected、`:31` Closed 依次 await 全部 `INetworkConnectionObserver`。
- `OnlineUserConnectionObserver`：Connected（:37）`RegisterConnection` 后直接发初始 NOTI，不取门；Disconnected（:66）先会话门再在线门，调 `cleanup.CleanupAsync`（领域服务，不经总线）、`cleanupDelivery.DeliverAsync`、`TryDisconnectConnection`；孤儿路径（:84）只取会话门。
- `LoginSessionConnectionObserver`（`Application/Authentication/LoginSessionRuntime.cs:9`）：断开时改内存 `LoginSessionRegistry`，不取门。
- cleanup 内部自开事务：`CreatureRuntimeDomainService.cs:269` `WithinAsync`；`DungeonLifecycleDomainService.RecordDepartureAsync`（:19）自取全局门开事务；BossTower/IllusionTowerDomainService（:59、:48）同样。
- 关连接：`TouchSocketNetworkConnectionCloser.cs:18`、`:26` 为 `_ = CloseDetachedAsync`（Task.Yield 后 CloseAsync），`OnTcpClosed` 与清理跑在另一条异步流上，与发起方并发。调用点 12 处：BossTowerPartyCoordination :403、IllusionTowerPartyCoordination :422、DungeonEntryProjectionDelivery :530、ReturnToSelectionHandler :64、SelectCharacterHandler :262/:264、CreatureRuntimeDelivery :80/:178、GameLoginHandler :152、SelectedSessionSafety :71、LoginSessionRuntime :48、两塔 CloseQuietly（BossTowerProjectionDelivery :914、IllusionTowerProjectionDelivery :856）。

## 3. 后台服务

| 服务（位置） | 触发 | 做什么 | 经总线？ | 取门 |
|---|---|---|---|---|
| DomainEventHostedService（`DomainEventDispatcher.cs:221`） | 每订阅组一循环，唤醒/到期；启动扫描遗留 | 见 §4 | 否 | 全局门 |
| ScheduledTaskHostedService（`Scheduling/ScheduledTaskHostedService.cs:125`） | **Task.Run**，最多并发 4 | 5 个任务：name-tag（每分钟）、inventory.expiration、tau/southern/boss-tower 补票（每日） | 是，逐会话 SendAsync | 管线内 |
| NotificationRetryDrainService（`Notifications/NotificationRetryDrainService.cs:32`） | PeriodicTimer | 运输层重试发包 | 否 | 无 |
| JoustMaintenanceService（`Events/JoustMaintenanceService.cs:14`、`:68`） | 启动静默恢复，之后每秒 | `AdvanceJoustLifecycleCommand`；`DirectAsync(JoustLifecycleChanged)` 公告 | 推进经，公告不经 | 自有 SemaphoreSlim |
| LoginSessionMaintenanceService（`LoginSessionRuntime.cs:22`） | 每 30 秒 `SweepExpired` + Channel 消费者 | `TryDisconnectConnection` 后 RequestClose | 否 | 无 |
| DungeonPartyWipeService（`CharacterDeath/DungeonPartyWipeService.cs:52`） | 每 250ms | `EndPartyWipe`/`EndClearReturn` 命令；直调 `registry.RemoveParticipant`、`cards.Cancel`、`towns.EnterAsync` | 部分 | 投影门＋会话门 |
| Fatigue（`Fatigue/FatigueProjection.cs:69`） | 每分钟 | 直读领域服务、直接 SendNoti | 否 | 会话门 |
| Premium（`StateSynchronization/PremiumDailyRefreshService.cs:87`） | 每分钟 | 读命令＋直接发包 | 部分 | 会话门 |
| Mirror、Dimension、GoldSilverHorn、ReviveCoin、AltarQuota、TournamentQuota、HerasQuota（7 个） | 每分钟 | ProjectAsync | 否 | 会话门 |
| ItemUsage（`ItemUsageProjection.cs:118`） | 每分钟 | 读命令＋SendNoti | 部分 | 会话门 |
| Weakness（`Weakness/WeaknessRecoveryService.cs:55`） | 按间隔 | `AdvanceWeaknessRecoveryCommand`＋发包 | 部分 | 投递时会话门 |
| CreatureClock（`Creatures/CreatureRuntimeDelivery.cs:147`） | 每秒 | `AdvanceCreatureClockCommand`；`DrainProjections` 发包；失败关连接 | 部分 | 会话门 |
| AltarScheduler（`Altar/AltarScheduler.cs:48`） | Channel 唤醒/到期 | `AltarActionCommand`＋ProjectAsync | 部分 | 投影门＋会话门 |
| MarriageWeddingScheduler（`MarriageProposal/MarriageWeddingSlice.cs:205`） | 每秒 | Advance/Cleanup 命令；**`MoveAsync` 内直接 `CrossInto`（:189）**并发包 | 部分 | 会话门 |
| TournamentRewardSweep（`Tournament/TournamentRewardSweepService.cs:21`） | 每 50ms | `SelectTournamentRewardCommand`＋SendExplicit | 部分 | 投影门＋会话门 |
| RoomMechanismMaintenance（`RoomMechanisms/RoomMechanismProjection.cs:77`） | 唤醒/deadline | **`runs.AdvanceRoomMechanisms` 直改运行态**（`DungeonRunDomainService.Mechanisms.cs:7`）；DirectAsync 发包 | 否 | 改状态只有领域内 lock；发包取投影门 |
| BossTower/IllusionTower ReturnSweep（`BossTowerProjectionDelivery.cs:982`、`IllusionTowerProjectionDelivery.cs:922`） | 每 500ms，排空 ConcurrentQueue | Leave/Start 命令；发包；关连接 | 部分 | 投影门＋会话门 |
| GameServerStartup、GmBootstrap、HttpApiStartup、ChannelScriptStartup、MarriageRelationAudit、3 个 RegistryValidation、A21UdpDiscovery | 启动时 / UDP | 见 §6；UDP 只回 UDP 包 | — | — |

## 4. 领域事件订阅者（28 个文件，注册 52 个）

- Reliable 17：全局门内新事务写库，部分再 EnqueueAsync。quest.map-clear / inventory / mirror-map-clear / mirror-leave / altar-complete / wedding-trial；marriage.capsule-clear；achievement 7、title.unlock；**altar.departure、tournament.departure 直接删内存运行态**（`AltarDungeonDomainServices.cs:84`、`TournamentDungeonDomainService.cs:229`），不在会话门内。
- LiveOnly 22：先提交消费标记，之后在门外 ProjectAsync。name-tag、过期物品、外观、lucky-star、capsule×2、achievement、title×5、sao、southern×2、boss/tau 票、quest/mirror 进度、usage-quota、mail、activity。
- Runtime/Direct 13：调用方事务内同步执行。12 个是 `UsageQuotaDomainService` 写配额；`DungeonRoomMechanismNotificationSubscriber` 发包；`JoustLifecycleNotificationSubscriber` 由维护服务在事务外调用、发包。

## 5. HTTP

- 启动器（`HttpApi/HttpApiApplicationExtensions.cs:43` 起）：bootstrap 只读；Register、Login 走 `RegisterAccountCommand` / `AuthenticateAndIssueTicketCommand`。
- GM 写操作全部经总线（Login/Logout、Initialization、DailyRefill、Events、Mail Edit/Send/RecordFailure、Cera Grant/RecordFailure）。直接调领域服务的只读：auth/status、`GmAccessBoundary.cs:28` IdentifyAsync、Mail items。**`GmCeraEndpoints.cs:52` `reminders.RemindAsync` 会 ProjectAsync 给在线账号发包（`GmCeraDomainService.cs:14`），不经总线、不取门。**
- GmLoginLimiter、GmIconPackStore 是 GM 自身内存状态，不属于游戏状态。

## 6. 启动与预热（监听前）

`GameServerStartupService.cs:76` `EpicBook.InitializeAsync`（自开事务 MigrateLegacy，`EpicBookDomainService.cs:71`）；`:86` `ICharacterExperienceStoragePolicy.InstallAsync`（归一旧数据、装触发器）；`GmBootstrapService.cs:23` 经总线；Joust 启动恢复经总线；DomainEvent 遗留扫描走 §4。

## 7. 其他异步流

- `DungeonCardPresentationCoordinator.cs:54`、`:168`：`_ = RunLayoutAsync` / `_ = RunAutoFlipAsync`，Task.Delay 2 秒/4 秒后直调 `runs.TryOpenCardLayout`（:135）、`runs.ScheduleClearReturn`（:166）并直接发包；自动翻牌走 `SelectCardCommand`，不取投影门或会话门。
- 两塔 departure 队列（ConcurrentQueue）由 sink 写入、ReturnSweep 消费。
- `ItemGrantDelivery.cs:21` 无事务时自开；`AmbientStoreAccess` 易失写自开一次性边界。
- Infrastructure 的 Task.Run（`PvfItemCatalogSource.cs:63`、`PvfArchiveIdentity.cs:22`）只是内容缓存。

## 不经命令总线却改状态、写库或发包（改造重点）

1. OnlineUserConnectionObserver 的 Connected/Disconnected（在线注册、cleanup 落库、离场事件、NOTI）。
2. Closer 的 detached close（`OnTcpClosed` 在发起方之外另一条流）。
3. LoginSessionConnectionObserver 与 LoginSessionMaintenanceService（`TryDisconnectConnection`、SweepExpired）。
4. RoomMechanismMaintenanceService 直调 `AdvanceRoomMechanisms`。
5. DungeonCardPresentationCoordinator 的 fire-and-forget 计时器。
6. DungeonPartyWipeService 的 RemoveParticipant、cards.Cancel、Town Enter/MoveExact。
7. MarriageWeddingScheduler 经 `MoveAsync` 调 `CrossInto`。
8. DomainEventDispatcher：Reliable 在全局门内写库（另有 2 个 departure 订阅直改内存），LiveOnly 与重试在门外发包。
9. 只发包的后台服务：NotificationRetryDrain、Fatigue、Premium、ItemUsage、7 个 Quota、Weakness/Creature 投影、Joust 公告、Tournament/Altar/Tower 投影。
10. EffectOperationLedger 已提交重放 RefreshAsync。
11. GM Cera RemindAsync。
12. 自取全局门、自开事务的领域服务：DungeonLifecycle、TransactionalDungeonVisit、BossTower、IllusionTower、CreatureRuntime.WithinAsync（在 observer/后台流上绕过管线写库）。
13. 启动期 EpicBook 迁移与经验 InstallAsync（监听前，风险低）。
14. 收包流 handler 直改（GameLogin、SetUserArea、WeddingChurchEnter、SetPlayResult）：整个 dispatch 变 CMD 即覆盖。

另：5 个调度任务经总线，但通过 Task.Run 并发（最多 4），本身是与收包并行的多消费者来源。

# 入口对账（TASK-16，T-004b 收口）

- 日期 2026-10-22。基准提交 `c3115f9a`，加上本单元唯一的生产改动（`DungeonCardPresentationCoordinator.cs`，§4）。探针跑在这份最终代码上。
- 本文件是 T-004c 进入条件（`t004c-entry.md`）的证据底稿。各单元原有的对账行（TASK-09～TASK-15 的 Checks）在这里汇总，并在基准上复核数字与测试名；不重新设计证据。
- 引用的每个测试名都用 `logs/task-16-evidence-names.txt` 核对过：在基准上 `git grep` 找到定义，本单元新增的在工作区找到并注明。

## 1. 探针终核

原始记录：`logs/task-16-probe-{Application,Persistence,Protocol,Host,Networking}.txt`（轮次 p1）。汇总与工具输出：`logs/task-16-probe-summary.txt`。

### 1.1 逐族终值（fam.py，四个项目，记录／命中）

| 族 | TASK-07 最终轮（量级对照） | TASK-14 p1 | TASK-16 p1 | 允许的终值 | 结论 |
|---|---|---|---|---|---|
| 3.1 dispatcher/Received | 5208／16257 | 1／1 | 1／1 | 1／1 | 符合。Networking `InboundExecutorWireTests`，`NetworkMessageDispatcher.DispatchTelemetryAsync` → `LoginSessionRegistry.IsSuperseded`，即遥测路径的只读检查（TASK-09 裁定第 1 条） |
| 3.1 Connected/Closed | 202／1191 | 0／0 | 0／0 | 0／0 | 符合 |
| 3.2 效果账本 | 17／102 | 1／1 | 1／1 | 1／1 | 符合。`EffectOperationReplayTests.Refusal_only_unselected_operation_cannot_pay_after_a_selection_arrives`（`:371`），顺延 T-004c |
| 3.3 连接观察者 | 83／118 | 79／114 | 79／114 | 79／114 | 符合。排除轮 x1 不是 0，见 §1.4 |
| 3.4 登录会话 | 1／1 | 0／0 | 0／0 | 0／0 | 符合 |
| 3.5 事件 | 461／1899 | 17／17 | 17／17 | 改正后 0／0 | 符合。`residual.py`：17 条都是 `DrainOnceAsync` 返回后的测试续行（16 条测试直接调总线，归 3.10；1 条 P1b-read），与 TASK-12 note 1 相同 |
| 3.6 卡牌计时器 | 24／141 | 0／0 | 0／0 | 0／0 | 符合 |
| 3.7 GM/HTTP | 1031／2198 | 0／0 | 0／0 | 0／0 | 符合 |
| 3.8 后台与调度 | 223／4601 | 0／0 | 0／0 | 0／0 | 符合 |
| 3.9 启动与停机 | 190／376 | 0／0 | 0／0 | 0／0 | 符合。没有来自 `ContentWarmup` 的记录（预热不经 P1／P2／P3 挂点） |
| 3.10 命令总线 | 4171／18235 | 3518／10203 | 3513／10203 | 只允许测试直接调用总线 | 符合，归因见 §1.2 |

- Persistence 单列：3.10 为 46／180（TASK-14 也是 46／180，TASK-07 r6 为 40／193），其余各族 0／0。
- TASK-07 一栏只作量级对照：3.5～3.10 的归族口径自 TASK-10 起按 `fam.py` 重建，不逐条归因。
- 3.10 按项目：Application 3212／9303，Protocol 13／25，Host 233／664，Networking 55／211。加法：3212 ＋ 13 ＋ 233 ＋ 55 ＝ 3513；9303 ＋ 25 ＋ 664 ＋ 211 ＝ 10203。这就是 T-004c 翻转之后应当消失的量。
- 各项目 (A) 总数与各族之和一致：Application 3212 ＋ 79 ＋ 17 ＋ 1 ＝ 3309；Networking 55 ＋ 1 ＝ 56；Protocol 13；Host 233；Persistence 46。
- 停止口径（裁定第 6 条）没有触发：3.1～3.9 没有表外记录，3.10 没有无法归因的记录。

### 1.2 3.10 的归因（结构论证）

**没有测试帧的 (A) 记录**（`shape.py`；S1 ＝ 撞 40 帧上限、可见最外层入口是总线或管线行为；S2 ＝ 完整栈、底部是线程池续体）：

| 项目 | 3.10 S1 | 3.10 S2 | 3.3 S2 | 无法归形 |
|---|---|---|---|---|
| Application | 286／4299 | 20／57 | 4／10 | 0 |
| Persistence | 4／121 | 4／7 | 0 | 0 |
| Protocol | 6／16 | 2／2 | 0 | 0 |
| Host | 13／97 | 12／52 | 0 | 0 |
| Networking | 28／161 | 0 | 0 | 0 |

Application 加法：286 ＋ 20 ＋ 4 ＝ 310 条，4299 ＋ 57 ＋ 10 ＝ 4366 次，与 `hostnotest.py` 的「无测试帧 310／4366」相等。

**结构论证**：

1. 生产代码中在执行器外调用 `ICommandBus.SendAsync` 的位置为 0（§1.3）。
2. 作业里的代码，作业令牌随 await 流动，它的续体在作业结束前都读作「在执行器上」。所以执行器外经总线进入的记录，只能是在任何作业之外开始的调用，也就是测试直接调用总线。
3. 作业结束后才继续的续体（作业令牌已失效）只可能来自分离点，而分离点不调用总线：卡牌协调器的计时器只在定时 CMD 的作业体里调用总线（§3）。

S1、S2 两种形状都落在这个论证里：S1 看不到调用方，S2 的调用方帧在续体恢复时丢失，但它们都从总线或管线进入，而生产代码不在作业外进入总线。排除轮不作为 3.10 的归因依据（TASK-14 第 2 轮非阻断 2）。

### 1.3 生产代码中的总线调用点（基准 `c3115f9a`）

方法：`buscensus.py`（scratchpad `t16/`）对每个命名了 `ICommandBus` 的文件，取每个类给总线起的名字（构造参数或字段），数 `<名字>.SendAsync(` 的行，并按类声明归类。输出原样在 `logs/task-16-boundary.txt`。

| 项目 | 命名 `ICommandBus` 的文件 | 总线 `SendAsync` 行 | 归属 |
|---|---|---|---|
| Networking | 0 | 0 | — |
| GameServer | 0 | 0 | 启动作业经 `IGameplayQueue`（TASK-14） |
| Infrastructure | 0 | 0 | — |
| Persistence | 0 | 0 | — |
| Protocol | 111 | 191 | 见下 |
| Application、GmWeb、HttpApi | — | — | TASK-14 Checks 8 已逐行核对：GmWeb 与 HttpApi 38 行全在过滤器包住的委托、`HttpGameplay.RunAsync` 体或启动作业体里；Application 10 个文件是总线本身、订阅注册检查与已迁入的定时源／调度作业 |

Protocol 的 191 行：

- **135 行在消息 handler 类里**（派生 `ClientMessageHandler<…>` 或实现 `I*MessageHandler`）：只由 `NetworkMessageDispatcher` 在入站作业里调用（附录 A 第 1 行）。
- **9 行在 7 个定时源里**（`AltarScheduler`、`DungeonPartyWipeService`、`CreatureClockService`、`MarriageWeddingScheduler`、`PremiumDailyRefreshService`、`TournamentRewardSweepService`、`WeaknessRecoveryService`）：总线调用都在 `GameplayTimerWork`／合并源包住的 tick 作业体里（TASK-11 E1 逐行；探针 3.8 为 0）。
- **47 行在 16 个其他类里**，逐个追到调用方（`callers.py`，同一日志）：
  - `AltarProtocolCoordinator`、`AltarProjectionDelivery`：调用方是祭坛的 handler、`DungeonClearSlice`／`LeaveDungeonSlice`／`MonsterDeathSlice` 的 handler、`AltarScheduler` 与 `DungeonPartyWipeService` 的 tick；
  - `BossTowerPartyCoordinator`（文件内的 `BossTowerPartyAttempt` 行）、`IllusionTowerPartyCoordinator`：队伍与通关 handler；
  - `BossTowerProjectionDelivery`、`IllusionTowerProjectionDelivery`（文件内的投影门类行）：进本、选图、死亡、拾取、怪物死亡、读取完成、离开副本等 handler，以及同文件里的两塔离场扫描服务的 tick；
  - `DungeonEntryProjectionDelivery`：`SelectDungeonHandler`；`DungeonLoadingProjectionDelivery`（经 `IDungeonLoadingProjectionDelivery`）：`FinishLoadingSlice` 的 handler；
  - `LotteryOpeningDelivery`、`MailboxDelivery`：同文件与 `MailboxSendWire` 的 handler；
  - `EntryAllowanceHandler`、`FatiguePotionHandler`（不是消息 handler，是 `QuestClearCouponSlice` 的 handler 注入的协作者）；
  - `ReplayStateProjectionDelivery`（经 `IReplayStateProjectionDelivery`）：`EffectOperationLedger`（入站作业内，附录 A 第 2 行）与 `JoustBetHandler`；
  - `ItemUsageProjectionDelivery`：`FatiguePotionHandler`、`SelectCharacterHandler`、`ItemUsageDailyRefreshService` 的 tick；
  - `TournamentProjectionDelivery`：`TournamentRewardHandler`、`MonsterDeathHandler`、`DungeonPartyWipeService` 与 `TournamentRewardSweepService` 的 tick；
  - `DungeonCardPresentationCoordinator`：自动翻牌，只在 `timer:DungeonCardPresentationCoordinator/auto-flip` 的作业体里（附录 A 第 6 行）。
- 加法：135 ＋ 9 ＋ 47 ＝ 191。

结论：生产代码中在执行器外调用 `ICommandBus.SendAsync` 的位置为 0。

### 1.4 排除轮 x1 与 P1b

- **x1**（Application，排除 T-004c 的 76 条冲突用例；过滤器以 scratchpad `t07/conflicts.py` 为底，按 `task-07-conflicts.md` §7～§12 的去向重新生成，文本与方法清单落在 `logs/task-16-probe-summary.txt` 末尾；4510 － 4457 ＝ 53 个用例实例被排除）：3.1、3.2、3.4、3.6～3.9 为 0；3.5 仍是那 17 条测试续行；**3.3 为 42／54，不是计划预期的 0**。
  - 42 条全部来自不是冲突用例的 `UserServiceTests`（文件 `OnlineUserServiceTests.cs`）方法：它们在测试线程上直接调用 `OnlineUserConnectionObserver.ConnectedAsync`／`DisconnectedAsync`（其中 12 次命中的测试帧只剩 `TestGameplay` 辅助帧）。
  - 生产中观察者唯一的调用方是 `NetworkConnectionLifecycleDispatcher`，在生命周期作业里（基准上 `git grep INetworkConnectionObserver`、`OnlineUserConnectionObserver`：除定义与 `ProtocolModule` 注册外，只有分发器的构造参数）。所以执行器外的观察者记录只能是测试直接调用；它们在允许的 79／114 之内，也符合「`OnlineUserServiceTests.cs` 中的用例直接构造观察者」这条依据。计划里「排除轮为 0」的预期错了：直接调用观察者的不只是那 7 条冲突用例。
  - p1 中 3.3 的 79 条按测试类：`UserServiceTests` 55 次、`SelectCharacterLifecycleTests` 25 ＋ 2 次（含其 `RecordingCleanup` 替身）、`UserDomainServices` 测试辅助 10 次、`TestGameplay` 辅助帧 12 次、无测试帧 10 次（S2，续体恢复丢失调用方帧）；55 ＋ 27 ＋ 10 ＋ 12 ＋ 10 ＝ 114。
- **P1b**（`p1bsplit.py`）：五个项目中 P1b-teardown、P1b-volatile、P1b-creature 的 (A)／(B)／(C) 全部为 0。只剩 P1b-read：Application (A) 3／(B) 143／(C) 3，Protocol (B) 1，Networking (B) 1。P1b-read 的 (B) 计数作为给 T-004c 的信息（判据要区分只读的一次性事务与写单元，`task-07-probe.md` §4.1）。
- **(B)／(C) 记录**（第 2 轮，审查 B1）：本节以上只看 (A)。五个项目执行器外的 (B)／(C) 记录逐组对到冲突清单的去向，并用 Protocol 排除轮 x2（排除整份清单 98 项）、x3（只排除 T-004c 的 77 条）补证，见 `t004c-entry.md` §4.1 与 `logs/task-16-probe-round2.txt`。
  - 执行器外的取门（P3）全部属于 T-004c 冲突用例：x1 中 Application 为 0，x3 中 Protocol 为 0，18 条 (C) 也在其中。
  - 其中 `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection` 按主控方裁定从 T-006 改归 T-004c（`task-07-conflicts.md` §13）。

### 1.5 TASK-03 三处门外写终核

| 位置 | 动态 | 静态 | 结论 |
|---|---|---|---|
| `SetUserAreaHandler` 跨区落位置（`TownProjectionSlice.cs` 中经 `ISelectedSessionPositionPersistence.PersistAsync`） | P1b-teardown 在执行器外 0；P1 中无 `TownProjection`／`SetUserArea` 为入口的 (A) 记录（3.1 只剩那条遥测） | handler 在入站作业里，持有环境事务时走 `ambient` 分支，否则 `AloneAsync`，两者都在作业里 | 0，TASK-09 归零后复核仍为 0 |
| `CharacterStateAccess.cs` 的 `AloneAsync`（拆除时落位置，唯一调用点 `:156`） | P1b-teardown (A)／(B)／(C) 全部为 0 | `PersistAsync` 的上游：`SelectedSessionCleanupService`（`SelectCharacterLifecycleServices.cs:114`，调用方是 `OnlineUserConnectionObserver` 即 `lifecycle:disconnected`——含孤儿清理与 `TouchSocketTransportDrain` 的兜底拆除——以及 `ReturnToSelectionCommand` 与选角阶段命令，均为入站作业）；`SelectCharacterPreparationService`（选角，入站）；`TownPositionEffectHandler`（效果结算，入站）；`ChurchRooms`（入站）；`WeddingSettlement`（`MarriageWeddingScheduler` 的 tick 与婚礼 handler）；`TownProjectionSlice`（入站） | 0。上游全部是附录 A 第 1、3、5 行的入口，都在执行器上 |
| `CreatureRuntimeDomainService.cs:274`（访问器不是 `DomainTransactionScope` 时自建事务） | P1b-creature 为 0 | 生产中 `IDomainTransactionAccessor` 只有一个实现 `DomainTransactionScope`（`CommandDispatchingExtensions.cs:34` 注册）；测试中构造 `CreatureRuntimeDomainService` 的 4 处（`CreatureRuntimeDomainServiceTests:573`、`CreatureTransformationTests:172`、`:300`、Persistence 测试项目的 `CreatureLifecycleTests:144`）都传 `DomainTransactionScope`；测试里的 5 个替身访问器（`OneUseCaseAtATime`、`ClosedTransactionAccessor`、`SwappableTransactionAccessor`、`OpenTransactionAccessor`、`DungeonParticipationDomainServiceTests.Accessor`）没有一个注入它 | 0 次命中，分支不可达；调用入口 `CreatureClockService` 已在执行器上（TASK-11 E1）。反例由 T-004c 的判据自己补，写入 `t004c-entry.md` |

### 1.6 还原证明（同 TASK-14 Checks 9）

- 快照前后 sha1 都是 `bf76d2e14c39c051407e5a8cb6b5c0ae3b1044a6`，`cmp` 相等；`Server/src` 未跟踪文件前后逐字节相同（都为空）。
- `snapcheck`：索引 1589／工作区 1589，相等 1588，不等的 1 个就是本单元的协调器；未跟踪 0。
- `patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；之后 `grep TestGameplay|GameplayProbe Server/src` 为 0。
- `patch.py` 的全部匹配模式在本单元代码上都命中，没有改模式（P2 共 172 处，其中协调器 3 处锁）。
- 之后以 `--no-incremental` 重新构建，再跑正式全量（`logs/task-16-build-test.txt`）。
- 探针轮的失败不作为证据：p1 与 x1 的 Application 各有 2 条失败，都是那 2 条 `DimensionDungeonTests`。

## 2. 附录 A／B 对账

### 2.1 附录 A：来源 → CMD 类型 → 证据

测试用各单元的编号；编号与方法名的对照在各单元 Result 的「新增测试」一节，类名都经 `logs/task-16-evidence-names.txt` 核对。

| 行 | 来源（种类） | CMD 类型 | 经真实入口驱动的测试 | 探针族终值 | 留给后续 |
|---|---|---|---|---|---|
| 1 入站协议 | Connection | `inbound:<ClientMessageType>` | W1、W3（`InboundExecutorWireTests`）、D1（`GameplayDispatchTests`）；遥测：W4、D5、D6、D8 | 3.1 Received 1／1（遥测只读检查） | 「每个消息类型一个协议 CMD」的归并：T-008 |
| 2 效果账本重放 | 随所属入站 CMD | 同第 1 行（例 `inbound:GameUseStackable`） | D7（`EffectDispatchOnExecutorTests`） | 3.2 1／1（`:371`） | `:371` 冲突用例：T-004c；请求级上下文改为显式数据：T-008 |
| 3 连接生命周期 | Lifecycle | `lifecycle:connected`、`lifecycle:disconnected`（孤儿清理与排空兜底拆除归后者） | W5～W8（`LifecycleExecutorWireTests`）、S1（`ShutdownOrderWireTests`）、S2（`ShutdownPositionPersistenceTests`）、LD1（`ConnectionLifecycleDispatchTests`）、L1、L4（`ConnectionCallbackBoundaryTests`） | 3.1 Connected/Closed 0／0；3.3 79／114（测试直接构造观察者） | `lifecycle:close-cleanup` 与继承链：T-007；关连接排在已登记消息之后：T-006；3.3 的 7 条冲突：T-004c |
| 4 登录会话 | Lifecycle、Timer | `lifecycle:disconnected`、`lifecycle:close-superseded`、`timer:LoginSessionMaintenanceService` | M1、M2（`LoginSessionMaintenanceExecutorTests`）、W9 与 W7 登录变体（`LifecycleExecutorWireTests`） | 3.4 0／0 | 关连接改发件箱条目：T-006；继承链：T-007 |
| 5 后台与调度 | Timer | `timer:<类型名>`（20 个服务）、`timer:ScheduledTaskHostedService/<任务 ID>`（5 个） | E1（`TimerSourceReconciliationTests`，27 行）、E2a、E2b、E3（`TimerSourceDownstreamTests`）、E2c（`ScheduledTaskExecutorTests`）、E2d（`NotificationRetryDrainExecutorTests`）。E1 只证明经执行器入队；「整段在作业内」靠代码审读、E2 与探针（TASK-11 主控方补充 N1） | 3.8 0／0 | `Task.Run` 外壳、并发槽、`deliveryGate`、Joust `ticks`：T-005；直发改发件箱、删除重试排空服务：T-006；继承链：T-007 |
| 6 卡牌计时器 | Timer | `timer:DungeonCardPresentationCoordinator/layout`、`/auto-flip` | C1～C4、C7（`DungeonCardPresentationCoordinatorTests`）、C5、C6（`CardTimerShutdownTests`）；排空之后到达的调度：T1、T2（本单元，同一测试类） | 3.6 0／0 | 计时器 CMD 是否继承调度它的链：T-007 |
| 7 两塔离场队列 | Timer | `timer:BossTowerReturnSweepService`、`timer:IllusionTowerReturnSweepService` | E1 两行；`:149` 的后继 `IllusionTowerPublicFlowTests.A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone` | 3.8 0／0 | `:149`：T-004c |
| 8 事件订阅 | Event、StartupRecovery | `event:<订阅 ID>`、`startup:event/<订阅 ID>`；Direct 留在调用方 CMD | N1～N10（`DomainEventDispatchTests`，`.Executor.cs`）；52 个订阅（Reliable 17、LiveOnly 22、Direct 13）在基准上用 `subcheck.py` 复核不变 | 3.5 改正后 0／0 | 发件箱化：T-006；重试的继承链与深度超限落定：T-007；组门：T-005 |
| 9 GM | Gm | `gm:<METHOD> <路由模板>`（40 条） | R1 `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption`、E1 `GmGameplayRouteTests.Each_gm_request_runs_downstream_as_one_gm_cmd`、L2、H1（`CredentialEntryHttpTests`）；51 条路由逐条在 `logs/task-14-routes.txt` | 3.7 0／0 | Cera 提醒改发件箱：T-006；双 Error 日志：T-008 |
| 10 启动器 | Http | `http:POST /api/launcher/v1/register`、`http:POST /api/launcher/v1/login` | E2／H1 `CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds`、R1 | 3.7 0／0 | — |
| 11 启动恢复与启动作业 | StartupRecovery | `startup:JoustMaintenanceService`、`startup:event/<订阅 ID>`、`startup:GameServerStartupService/epic-book`、`/experience-storage`、`startup:GmBootstrapService` | E1 的 Joust 两行（V8 证明区分力）；N4（M4 证明区分力）；S1 `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs`（V7、V8） | 3.9 0／0 | RQ-061 启动期例外的取舍：T-004c |

「留给后续」一栏的事项在 `follow-ups.md` 各有一行，正文只写在那里。

### 2.2 附录 A 的文字修正（本单元已写入 `spec.md`）

| 行 | 原文 | 现文 | 依据 |
|---|---|---|---|
| 1 | `NetworkPlugins.cs:54/135` → dispatcher → 141 个 handler | 两个 `OnTcpReceived`（`:62`、`:144`）→ dispatcher → 173 条消息注册（173 个消息类型，141 个 handler 类型），其中 14 个遥测消息类型（2 个 handler 类型）在执行器外，其余 159 条（139 个 handler 类型）在执行器上 | 临时用例（不提交，运行后已删除）：对空 `ServiceCollection` 调 `ProtocolModule.ConfigureServices`，枚举 `MessageHandlerRegistration` 描述符；输出 `logs/task-16-handler-count.txt`。与 TASK-09 的反射结果相同 |
| 2 | `EffectOperationLedger.cs:58` | `EffectOperationLedger.DispatchAsync` | 基准上该方法在 `:17`，改用方法名避免行号漂移 |
| 3 | 只列两个类 | 补上经 `NetworkConnectionLifecycleDispatcher` 入队、孤儿清理与排空兜底拆除归断开，以及 CMD 类型 | TASK-10 |
| 4 | `LoginSessionRuntime.cs:9/22` | `:11` 观察者、`:28` 维护服务；补 `lifecycle:close-superseded` | 基准行号 |
| 5 | 33 个后台服务与 5 个调度任务 | 22 个后台服务（其中调度服务承载 5 个调度任务），并写出 33 的分解 22 ＋ 1 ＋ 1 ＋ 4 ＋ 5 ＝ 33；基准上注册 34 个，多的是 `GameplayExecutorLifetime` | 基准检索 `AddHostedService`／`IHostedService` 注册 34 处，按 TASK-11 §1 归属表逐个对上 |
| 6 | `DungeonCardPresentationCoordinator.cs:54/168` | 方法名 `ScheduleAfterSettlement`、`OpenLayoutAsync`；补 CMD 类型与排空后容错 | 本单元改了该文件，行号会变 |
| 7 | 定时 CMD（或离场时直接入队） | 定时 CMD，写明不采用「离场时直接入队」的理由（sink 在拆除 CMD 里调用，入队并等待即自我等待） | TASK-11 §1 |
| 8 | 52 个事件订阅（…） | 数字不变，注明在基准复核；补 CMD 类型 | `subcheck.py` 在基准源码上：registrations 52，LiveOnly 22、Reliable 17、Direct 13 |
| 9 | GM 写操作 34 条、Cera 提醒、读内存玩法状态的 GM 查询 | GM 路由 51 条：40 条 GM CMD，11 条豁免；「34」的口径不足以确定，改按路由计 | R1 打出的路由清单（`logs/task-14-routes.txt`），R1 在本单元正式全量中通过 |
| 10 | 启动器注册与登录（`:78/124`） | 3 条路由：2 条 HTTP CMD、1 条豁免 `bootstrap`；去掉行号 | 同上 |
| 11 | 只列 Joust 与遗留 delivery | 补上三个启动作业与全部 CMD 类型 | TASK-14 §4 |

另在表后加了一句指向本文件 §2.1 的对账指针。「新的 CMD」一列的设计含义没有改；第 7 行是把已裁定的选择写实（裁定第 8 条）。

### 2.3 附录 B：安全理由 → 判据或测试

| 附录 B 行 | 安全理由 | 判据、测试或静态依据 |
|---|---|---|
| 解包与帧适配、入站帧计数 | 只处理字节 | 不需要运行期判据：解包器只产出不可变消息，`NetworkPlugins` 在 `RunDetachedAsync` 流边界里解包后交给 dispatcher。静态依据：Networking 中命名 `ICommandBus` 的文件 0 个（§1.3），P1／P2／P3 在执行器外没有以解包器为入口的记录 |
| 发送端：编码、队列、交付、超时 | 只读发件箱交来的不可变消息 | 期限关闭的分离：`ExecutorSendDeadlineTests`；`OutboundSender` 未接线（T-006 接线时复核）。发送端不调用总线（§1.3） |
| 传输层的连接注册（含本单元补齐的传输排空、回调流边界、TouchSocket 服务宿主） | 传输自身的状态；拆除本身是生命周期 CMD | 排空：L3、L4、L5（`ConnectionCallbackBoundaryTests`）、`TransportDrainRegistrationTests`；流边界：`ReceivedStreamBoundaryTests`、`ConnectionCallbackBoundaryTests`、`DetachmentSiteTests`（本单元） |
| UDP 发现 | 只回 UDP 包 | 不需要运行期判据：`A21UdpDiscoveryService` 不注入任何领域服务（基准源码），不在 `IGameplayQueue` 持有者里 |
| PVF 内容源与缓存 | 只读或不可变 | R1 钉住豁免路由；预热行的 W1、W2 |
| GM 自身的内存状态 | 不属于玩法状态 | R1 |
| 只读 SQLite 的查询（含本单元补齐的 `ReadDueAsync`、`NextDueAsync`） | WAL 快照，不写库 | R1（豁免路由）；登录两段式 L1（`AuthenticationTwoPhaseTests`）、L2；事件读取：N1～N10 证明消费与完成标记在 CMD 内 |
| 口令哈希等纯计算 | 没有共享状态 | H1～H5（`PasswordComputationLimiterTests`、`CredentialEntryHttpTests`） |
| 纯遥测 handler（本单元改正了第一列的举例） | 不改状态、不写库、不发包 | D5、D6、D8（`GameplayDispatchTests`）、W4（`InboundExecutorWireTests`）；启动检查：注册为遥测的 handler 构造器只能注入日志，否则 `MessageHandlerRegistry` 构造即抛 |
| 启动预热 | 开始监听前加载完毕 | W1（`ContentWarmupTests.AssertWarmedAsync`，在 S1 中调用）、W2（`ContentWarmupTests` 结构判据及反例）、F1 |
| 开始监听前的启动期工作（含本单元补齐的 `HttpApiStartupService` 与 3 个注册表校验服务） | 开始接受连接与 HTTP 之前，执行器上没有其他工作 | S1（`GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs`）证明三个写库的启动工作已是作业；补齐的 4 个服务不注入领域服务、不写库（基准源码），探针 3.9 为 0 |
| 不碰玩法状态的 HTTP 路由（含本单元补齐的 GM 预缓冲） | 只返回常量、静态文件、令牌或就绪标志；预缓冲只读 HTTP 请求 | R1 及其反例；B1、B2（`GmGameplayRouteTests`） |
| 生产者的入队侧（本单元按 RQ-011 新增） | 只组织作业、入队、等待，或写出作业已算好的不可变结果 | 探针 3.1～3.9 只剩允许的残留（§1.1）；`IGameplayQueue` 持有者以基准 37 行检索为准（`logs/task-16-boundary.txt`）；正式全量 `GameplaySelfWait` 为 0；自我等待检测 `GameplayExecutorSelfWaitTests` |

### 2.4 执行器外实际存在的代码 → 附录 B 的行

方法：列出生产中不在作业里运行的代码（托管服务、传输回调、HTTP 管线、`IGameplayQueue` 的持有者、分离点），逐项对到附录 B 的一行；探针 P1／P2／P3 在 3.1～3.9 只剩允许残留，说明没有漏掉碰玩法状态的执行器外代码。

| 执行器外的代码 | 附录 B 的行 |
|---|---|
| 解包器、帧适配器、`GameInboundFrameCounter` | 解包与帧适配 |
| `TouchSocketNetworkMessageSender` 与编码器、`ExecutorSendDeadline` 的分离关闭、未接线的 `OutboundSender` | 发送端 |
| `TouchSocketConnectionRegistry`、`TouchSocketNetworkConnectionCloser`、`TouchSocketTransportDrain`、`NetworkPlugins` 的流边界、TouchSocket 的 TCP 服务 | 传输层的连接注册（本单元补齐） |
| `A21UdpDiscoveryService` | UDP 发现 |
| 内容源、`ContentWarmup`、GM `server-info`／`tooltip` | PVF 内容源与缓存、启动预热 |
| `GmLoginLimiter`、`GmIconPackStore` | GM 自身的内存状态 |
| `GmAccessBoundary` 身份识别、`auth/status`、`mail/items`、两个凭据 reader 的查询、`MarriageRelationAudit`、`DomainEventDispatcher` 的 `ReadDueAsync` 与 `RunGroupAsync` 调用的 `NextDueAsync` | 只读 SQLite 的查询（本单元补齐最后一项） |
| `PasswordComputationLimiter` | 口令哈希 |
| `HiddenCharacterInfoHandler`、`UnansweredReportHandler`，dispatcher 遥测路径的 `IsBlocked`／`IsSuperseded` | 纯遥测 handler |
| 建表与迁移、`ChannelScriptStartupService`、`StartingAsync` 的 PVF 初始化、`HttpApiStartupService`、3 个 `*RegistryValidationService` | 开始监听前的启动期工作（本单元补齐后两项） |
| GM `health`、`/gm/{**path}`、`auth/csrf`、`auth/session`，启动器 `bootstrap`，GM 过滤器的请求体预缓冲 | 不碰玩法状态的 HTTP 路由（本单元补齐最后一项） |
| `NetworkMessageDispatcher`／`NetworkConnectionLifecycleDispatcher` 的入队并等待；22 个定时源与 `LoginSessionMaintenanceService`、`JoustMaintenanceService` 的计时循环、`CoalescingGameplaySource`、`GameplayTimerWork`；`ScheduledTaskHostedService` 的计时循环、`Task.Run` 与并发槽；卡牌协调器的延时等待与 `DrainAsync`；`DomainEventDispatcher` 的组循环与组门；`HttpGameplay.RunAsync`、启动器与 GM 登录端点在 reader 返回后的等待及 `IResult` 写出；`GameServerStartupService`、`GmBootstrapService` 对启动作业的等待；`GameplayExecutorLifetime` | 生产者的入队侧（本单元新增） |

## 3. 分离点终核

### 3.1 检索

在基准上检索 `SuppressFlow|RunDetached|Task\.Run|Task\.Factory|ContinueWith|ThreadPool\.|new Thread`，以及「`_ = ` 后接返回任务的调用」（命令与命中原样在 `logs/task-16-boundary.txt`）。`Task.Factory`、`ThreadPool.`、`new Thread` 命中 0。

### 3.2 分离点表

| # | 位置 | 启动方式 | 单元 | 谁等待它 | 证据 |
|---|---|---|---|---|---|
| 1 | `GameplayExecutor.EnsureStarted` 的消费循环（`:130`、`:134`） | `SuppressFlow`（已抑制时不嵌套）＋ `Task.Run`，任务丢弃 | TASK-02 | `StopAsync`／`DisposeAsync` 等循环结束（执行器外；在本执行器上调用即自我等待） | `GameplayExecutorSelfWaitTests`、`GameplayJobTokenTests` |
| 2 | `ExecutorSendDeadline.StartDetachedClose`（`:42`、`:46`） | `SuppressFlow` ＋ `Task.Run`，结果丢弃 | TASK-08 | 无 | `ExecutorSendDeadlineTests` |
| 3 | `TouchSocketNetworkConnectionCloser.CloseDetachedAsync`（`SuppressFlow` 在 `:37`；`:18`、`:26` 是丢弃返回任务的调用点） | `SuppressFlow` 下调用以 `Task.Yield` 起头的方法，结果丢弃 | TASK-10 | 无 | `DetachedCloseTests` C1、C2 |
| 4 | `DungeonCardPresentationCoordinator.StartDetached` | `SuppressFlow`（已抑制时不嵌套）＋ `Task.Yield`，结果丢弃 | TASK-11 | 只有 `DrainAsync` 经在途计数等待，调用方是 `GameplayExecutorLifetime`（执行器外） | C3（TASK-11 V5 证明 `Task.Yield` 必要）、C7 |
| 5 | `NetworkPlugins` 四个回调（`:23`、`:33`、`:62`、`:144`） | `GameplayJob.RunDetachedAsync` 流边界，不是分出的工作 | TASK-09、TASK-10 | TouchSocket | `ReceivedStreamBoundaryTests`、`ConnectionCallbackBoundaryTests` L1～L3 |
| 6 | `OutboundSender.Detached`（`:89`）：准入循环、每连接排空循环、失败后的关闭（`:285`） | `SuppressFlow` ＋ `Task.Run` | TASK-05 | 发送端自身；未接线 | 只登记，T-006 接线时复核 |
| 7 | `ScheduledTaskHostedService.Start`（`:129`） | `Task.Run`，存进 `job.Execution` | 既有，T-005 删除 | 宿主 `StopAsync`（执行器外） | 不是作业的分离点：由执行器外的计时循环启动，每次尝试再经 `GameplayTimerWork` 入队成作业 |
| 8 | `PvfItemCatalogSource` 的 `searchable`（`:75`） | `await Task.Run(...)` | 既有 | 调用方当场等待 | 不是分离点：被等待，继承令牌是对的；体内是纯计算，不入队、不取门 |
| 9 | `PvfArchiveIdentity` 的哈希任务（`:22`） | `Task.Run`，缓存在字段 | 既有 | 之后任何调用方 | 纯文件哈希，不入队、不取门；只被 GM `server-info` 豁免路由读（执行器外）。记录，不改 |
| 10 | `ExecutorSendDeadline.Observe`（`:72`）、`OutboundSender.Observe`（`:254`） | `ContinueWith` 只读异常 | TASK-08、TASK-05 | 无 | 不触达玩法状态 |

检索的其余命中都不是分离点：`_ = await …` 是等待后丢弃结果；`PvfAvatarCompoundDefinitionSource.cs:34` 与 `PvfDungeonDefinitionSource.cs:103` 丢弃的是同步完成的 `ValueTask`（方法体在锁内同步填缓存）；`EpicBookDomainService.cs:34`、`ExperienceRuleService.cs:68-70`、`PvfLuckyStarRentalContentSource.cs:25` 丢弃的是 `Lazy` 值或同步结果；`SelectCharacterLifecycleServices.cs:216` 丢弃的是 `bool`；各 `PeriodicTimer` 在定时源自己的循环里（附录 B「生产者的入队侧」）。

### 3.3 证明（逐行结论）

1. **在 `SuppressFlow`／`RunDetached` 下启动**：第 1～6 行读启动点代码即可确认；运行期由「证据」列的用例证明：它们都在作业里触发分离工作，并断言分出的工作看不到作业令牌（C3 证明零延时的计时器不在调度它的作业栈上入队）。第 7～10 行不是从作业分出的工作，理由写在表里。
2. **不被作业 await**：第 1～6 行返回的任务全部被丢弃或只由执行器外的 `StopAsync`／`DrainAsync` 等待（静态：每个返回值的全部引用就是表中「谁等待它」一列）。正式全量 `GameplaySelfWait` 为 0。自我等待检测只覆盖「入队后等待」，「作业 await 一个会入队并等待的分离任务」这种检测不到的死锁，只能由这一半静态阅读排除——**这一条的依据是代码阅读**（TASK-04 N-2 第一条）。
3. **结构判据** `Server/tests/Dfo.Server.Application.Tests/Execution/DetachmentSiteTests.cs`（本单元新增，TASK-04 Remaining 第 2 条 R2-2）：
   - `RunDetached`／`RunDetachedAsync` 的调用只允许出现在 `Networking/Runtime/NetworkPlugins.cs`，定义所在的 `Abstractions/Execution/GameplayJob.cs` 除外；注释（含 `cref`）不计；
   - `ExecutionContext.SuppressFlow` 只允许出现在上表第 1、2、3、4、6 行的五个文件；
   - 白名单写在判据旁，每项一句理由；白名单里的文件若不再出现该原语，判据同样变红（过期豁免）；加一处必须同时改判据与本表；
   - 反例：`A_detachment_outside_the_listed_sites_is_refused`（6 例，含「在另一种原语的白名单文件里」的两例）与 `Comments_and_the_listed_sites_are_not_refused`；
   - 变异 V2、V3 证明它扫到了真实源码树（`logs/task-16-mutations.txt`）；
   - 判据只看出现位置，不证明「不被 await」，后者由第 2 条承担，判据的类注释写明了这一点。

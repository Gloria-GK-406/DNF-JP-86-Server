# T-004 收口对账

- 日期 2026-10-23。基准提交 `0c9c59c8`（TASK-18）加上 TASK-19 未提交的改动。
- 用途：判定 T-004「全部入口迁入执行器」是否完成，以及 T-005 进入条件中属于 T-004 的那一半是否成立。
- 写法：本文只写「交付 → 哪个 TASK → 证据位置 → 状态」。未关闭事项的内容在 `follow-ups.md`，这里只引用 F 编号。
- 证据底稿：
  - 各单元的 Result 在 `tasks/TASK-06.md`～`tasks/TASK-19.md`；
  - 日志在 `logs/`；
  - 入口对账在 `entry-reconciliation.md`（TASK-16，基准 `c3115f9a`）；
  - 冲突用例在 `task-07-conflicts.md` §7～§15。
- 状态词：
  - **完成**：T-004 范围内的部分都有证据；
  - **完成，部分移交**：T-004 范围内完成，Plan 或裁定把其余部分分给了后续步骤；
  - **未满足**：属于 Spec 验收，今天不成立。
- **实际拆分**（裁定第 4 条：只在此写明，`plan.md` 不改）：Plan 写「预计拆成三个单元」，实际拆成 14 个单元。
  - T-004a：TASK-06、TASK-07，共 2 个；
  - T-004b：TASK-08～TASK-16，共 9 个（含只修测试竞态的 TASK-13）；
  - T-004c：TASK-17、TASK-18、TASK-19，共 3 个；
  - 2 ＋ 9 ＋ 3 ＝ 14。
  - 完成条件「零违规全量」的证据是两次正式全量（TASK-18、TASK-19），见 §5。

## 1. Plan T-004 行逐项

| 交付 | 完成于 | 证据 | 状态 |
|---|---|---|---|
| 入站：dispatcher 入队并等待，请求级帧在作业内 | TASK-09；TASK-19 补 AC-003 第 3 条 | `GameplayDispatchTests.Request_frames_are_built_inside_the_job_and_reach_the_commands_it_runs`；`InboundExecutorWireTests` 5 条；`logs/task-09-wire-x10.txt`、`logs/task-19-ac003.txt` | 完成，部分移交：F-30、F-32、F-34 |
| 连接生命周期 | TASK-10 | `LifecycleExecutorWireTests` W5～W8；`logs/task-10-wire-x10.txt` | 完成，部分移交：F-20、F-21、F-26 |
| 登录会话维护 | TASK-10 | `LoginSessionMaintenanceExecutorTests`；`LifecycleExecutorWireTests.A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor` | 完成，部分移交：F-26、F-62、F-65 |
| 后台与调度 | TASK-11（TASK-13 修测试竞态） | `TimerSourceReconciliationTests.Each_timer_source_runs_its_ticks_as_its_own_cmd_on_the_executor`；`TimerSourceDownstreamTests`；`logs/task-11-new-x10.txt` | 完成，部分移交：F-07、F-08、F-19、F-22、F-28 |
| 延时计时器 | TASK-11；TASK-16 补协调器容错 | `DungeonCardPresentationCoordinatorTests.A_due_layout_timer_sends_its_frames_from_a_timer_cmd_on_the_worlds_executor`；`logs/task-16-mutations.txt` | 完成，部分移交：F-28 |
| 两塔离场队列 | TASK-11 | `IllusionTowerPublicFlowTests.A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone`；`entry-reconciliation.md` §2.1 第 7 行 | 完成 |
| 事件消费 | TASK-12 | `DomainEventDispatchTests.Executor.cs` N1～N10；`logs/task-12-new-x10.txt` | 完成，部分移交：F-10、F-19、F-27、F-30、F-31 |
| GM／HTTP | TASK-14 | `GmGameplayRouteTests.Each_gm_request_runs_downstream_as_one_gm_cmd`；`HttpGameplayTests`；`logs/task-14-routes.txt` | 完成，部分移交：F-09、F-25、F-33、F-66 |
| 效果账本重放 | TASK-09 | `EffectDispatchOnExecutorTests.An_effect_request_and_its_replay_both_run_as_the_inbound_job` | 完成，部分移交：F-34 |
| 口令哈希外移 | TASK-14 | `PasswordComputationLimiterTests`；`CredentialEntryHttpTests` | 完成，部分移交：F-02（测量） |
| 纯遥测留外 | TASK-09 | `InboundExecutorWireTests.A_telemetry_frame_is_handled_off_the_executor_after_the_previous_job_completed`；spec 附录 B 纯遥测行 | 完成，部分移交：F-35 |
| `SendAsync` 过渡内联规则（最后翻转） | TASK-17 | `CommandBusExecutorTests`；`logs/task-17-mutations.txt` | 完成，部分移交：F-59（队列层泄漏流） |
| 门断言 | TASK-17 | `GameplayGateAssertionTests`、`ProtocolGateAssertionTests`；`logs/task-17-violations.txt` | 完成，部分移交：F-17、F-19（组门与并发槽的删除） |
| 分离点 `SuppressFlow` | TASK-08～TASK-11，TASK-16 终核 | `DetachmentSiteTests.Detached_work_starts_only_at_the_listed_sites`；`entry-reconciliation.md` §3 | 完成，部分移交：F-24、F-70 |
| 直发 2 秒期限（含 VC-8 提前的完成核对） | TASK-08；核对在 TASK-19（§2） | `DirectSendDeadlineRealSocketTests.A_job_sending_to_a_client_that_stops_reading_times_out_closes_it_and_frees_the_executor`；`logs/task-08-deadline-x10.txt`、`logs/task-19-vc8.txt` | 完成，部分移交：F-20、F-23、F-63 |
| 改写会挂起的测门与交错测试 | TASK-06／07 建清单，TASK-09～TASK-18 处置 | `task-07-conflicts.md` §15.4：139 条全部有去向；T-004c 的 77 条已无顺延 | 完成，部分移交：T-006 52 条、T-008 1 条、F-67（交 T-005 的 3 条） |
| 等待关系与停机顺序 | TASK-10；TASK-16 §4 | `GameplayExecutorLifetimeTests`；`ShutdownOrderWireTests`；`ShutdownPositionPersistenceTests`；`HostShutdownOrderTests`；`logs/task-10-host-stop.txt` | 完成，部分移交：F-20、F-21 |
| `Lazy` 内容源预热 | TASK-15（第 3 轮） | `ContentWarmupTests`；`logs/task-15-build-test.txt`；spec 附录 B 预热行 | 完成，部分移交：F-01～F-06 |
| RQ-061 前一半（写 SQLite 的工作单元开启、附录 F 指定的写入口） | TASK-18 | `GameplayWriteAssertionTests`；`GameplayWriteEntryTableTests`；`logs/task-18-entry-table.txt`、`logs/task-18-violations.txt`、`logs/task-19-violations.txt` | 完成，部分移交：F-12（附录 C）、F-62（名单外状态）、F-64（发件箱一半） |
| 「除表征与入口对账外，行为不变」 | 各单元 | 表征 8 通过、2 跳过，金样本未改（`logs/task-18-build-test.txt`、`logs/task-19-build-test.txt`）。已登记的可见差异：VC-8 提前（附录 G）、`ScheduledTaskTests` 与 `DomainEventDispatchTests.A_slow_live_hint_postpones_other_groups_and_still_runs_outside_the_business_transaction` 的意图改写（RQ-013、RQ-043）、停机兜底拆除（F-21） | 完成 |

### 1.1 本单元补足的三项

| 项 | 证据 | 结论 |
|---|---|---|
| AC-003 第 3 条（F-13） | 用例 `InboundExecutorWireTests.A_flooding_connection_never_has_more_than_one_inbound_cmd_waiting_and_cannot_starve_another`。隔离 10 次、正式全量 1 次都通过；变异 S6 下变红（`logs/task-19-ac003.txt`、`logs/task-19-mutations.txt`） | 已关闭 |
| VC-8 提前的完成核对（F-14） | `logs/task-19-vc8.txt`；结论见 §2 | 已核对，T-006 之后消失 |
| `GameplayJobHost` 结构判据与归属断言（F-36 后一半） | `GameplayJobHostStructureTests` 10 条；变异 S1～S5（`logs/task-19-structure.txt`、`logs/task-19-mutations.txt`） | 已关闭；AGENTS.md 那一半仍归 T-009 |

**AC-003 第 3 条的解读**（裁定第 3 条）：
- 「待执行」指已经入队、作业体还没开始执行的入站 CMD。测试内的 `IGameplayQueue` 计数装饰器在每次入队与每次开始时逐次记录，不是采样。
- 「另一个连接照常执行」用两个时点验证：灌包开始时、灌包进行到一半时，另一个连接各发一帧。这两帧都必须排在灌包连接的下一帧之前执行。
- 装饰器只做观测，入站路径的其余部分全是生产实现。

## 2. VC-8 提前的完成核对

**结论**：已接线、写客户端连接的传输写入只有一处，即 `TouchSocketNetworkMessageSender.WriteAsync` 内的 `:110`。这一处对作业上的每一帧都套 2 秒期限，与 CMD 来源无关。

扫描另外找到一处已接线的传输写入：`A21UdpDiscoveryService.cs:127` 的 UDP `SendToAsync`。它不是连接发送，也不在作业上（F-63），详见下文。

扫描结果与逐行归类见 `logs/task-19-vc8.txt`：
- **写入点**：`Outbound/OutboundTransport.cs:81` 与 `Outbound/OutboundSender.cs:223` 是 T-003 的组件，没有接线。`Server/src` 中没有注册或构造 `IOutboundSender`、`OutboundSender`、`TouchSocketOutboundTransport`、`GameplayOutboxes` 的地方。
- **发送器**：`INetworkMessageSender` 的实现只有 `TouchSocketNetworkMessageSender`（`NetworkingModule.cs:50`）。`IGameMessageSender` 的唯一实现 `GameMessageSender` 委托给它。
- **投递服务**：28 处投递服务调用，最终都落到 `IGameMessageSender`，或经 `IEventService` → `NotificationBus` 落到 `INetworkMessageSender`。
- **期限覆盖**：`SendAsync` 与 `SendBatchAsync` 的每一帧都经 `WriteAsync`（`:101`、`:57`），`SendExplicitAsync` 委托给 `SendAsync`（`:137`）。`ExecutorSendDeadline.AwaitAsync` 只按 `GameplayJob.IsOnExecutor` 分支（`:26`），不读 CMD 类型或来源。
- **UDP 发现服务**：
  - 那次 `SendToAsync` 是 `BackgroundService` 自己的接收循环对 NAT 探测包的回包；
  - 文件中没有 `GameplayJob`、`IGameplayQueue`、`GameplayWork`，所以它不在任何作业上，执行器也不会等它；
  - 它不属于 VC-8 所说的「慢客户端在连接上的发送」；
  - 但「唯一写入点」这句话按字面不成立，T-006 设 RQ-036 判据时要把它列为豁免（F-63）。

**按来源的作业内直发证据**：

| 来源 | 证据 | 类型 |
|---|---|---|
| 入站 | `GameLoginIntegrationTests.Login_TraversesTouchSocketAccountRepositoryAndOnlineUserAggregate`：真实宿主与发送器，客户端从 socket 逐字节比对登录回包；作业归属由 `InboundExecutorWireTests.A_game_and_a_channel_message_each_run_as_an_inbound_job_rooted_at_their_connection` 断言 | 线级用例 |
| 生命周期 | `LifecycleExecutorWireTests.A_client_disconnect_runs_connect_and_disconnect_as_lifecycle_jobs_after_the_connections_inbound_job`：客户端从 socket 读到初始 NOTI；断言 `lifecycle:connected` 作业在执行器上。「NOTI 在该作业内发出」是从观察者在同一作业中依次执行推出的，用例没有在发送点直接断言 | 线级用例 |
| 定时 | `TimerSourceDownstreamTests.A_fatigue_refresh_sends_from_its_timer_cmd_whether_the_loop_or_the_entry_drives_it` 证明发送在 `timer:` 作业内，但发送器是替身；路径为 `FatigueProjection.cs:66` `SendNotiAsync` → `GameMessageSender` → `TouchSocketNetworkMessageSender` | 只算代码路径 |
| 事件 | `SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript`：真实宿主。LiveOnly 投影帧（南谷补给 → 背包刷新）是转录中的必达帧，经 `NotificationBus.cs:214` 送出；作业归属来自代码链（`DomainEventDispatcher` → `GameplayTimerWork.RunAsync`，来源 `Event`） | 线级用例（归属靠代码链） |
| GM | `GmGameplayRouteTests.Each_gm_request_runs_downstream_as_one_gm_cmd` 证明 Cera 提醒在 `gm:` 作业内调用；`GmTestHost` 不开网络，所以只算代码路径：`GmCeraDomainService.cs:23` `ProjectAsync` → `NotificationBus.cs:214` → `TouchSocketNetworkMessageSender` | 只算代码路径 |

- 期限机制本身由 `ExecutorSendDeadlineTests`（9 条）与 `DirectSendDeadlineRealSocketTests`（2 条）证明，两者都在本单元的正式全量中通过。
- 正式全量中 `direct send deadline exceeded` 出现 0 次。
- 定时与 GM 两个来源没有真实 socket 的线级用例。这不影响结论：期限只按「是否在作业上」分支，不看来源；两个来源的作业归属也都有用例证明。
- 计划 §4.3 不为此加长期判据，T-006 之后这条论证整体失效（F-20）。

## 3. 覆盖栏的 RQ 与 AC

| RQ | T-004 内满足的部分与证据 | 按 Plan 留给后续 |
|---|---|---|
| RQ-005 | 收包回调等待作业完成：AC-003 的三条线级用例 | 「完成」改按发件箱交给发送端计：T-006 |
| RQ-006 | `CoalescingGameplaySourceTests`（TASK-04），接线在 TASK-10、TASK-11 | — |
| RQ-007 | 哈希在执行器外、有并发上限：`PasswordComputationLimiterTests` | 测量：M-1（F-02） |
| RQ-008 | 执行器不等关连接与传输回调：`LifecycleExecutorWireTests.A_send_to_a_resetting_client_does_not_hold_the_executor_past_the_deadline`、`DetachedCloseTests` | 执行器等待作业上直发的交付（每帧 ≤ 2 s），收包回调因此间接等待交付：T-006（F-20）；关闭回调所等的生命周期作业里仍有直发（最长 2 秒）：T-006（F-20、F-23；TASK-10 裁定第 6 条） |
| RQ-009 | 停机顺序：`ShutdownOrderWireTests`、`ShutdownPositionPersistenceTests`、`HostShutdownOrderTests` | F-21 |
| RQ-010 | 附录 A 各入口迁入（§1）；TASK-18 的写断言与门断言零违规 | 名单外状态：T-009（F-62）；发件箱一半：T-006（F-64） |
| RQ-011 | `entry-reconciliation.md` §2.3、§2.4 | 附录 B 生产者行补名两项：T-009（F-62） |
| RQ-012 | 第一句（生命周期以 CMD 执行）：TASK-10 | 第二句（关连接排在已登记消息交付之后）：T-006（F-20、F-65）；继承链：T-007（F-26） |
| RQ-013 | 定时只入队 CMD：TASK-11 | 调度任务不再用 `Task.Run`：T-005（F-19） |
| RQ-014 | 事件消费以 CMD 执行：TASK-12 | 发件箱：T-006；重试继承链：T-007（F-27） |
| RQ-015 | GM／HTTP 以 CMD 执行：TASK-14 | Cera 提醒改发件箱：T-006（F-25） |
| RQ-040 | 事实记录、订阅与启动扫描不变：TASK-12、TASK-16 复核 | — |
| RQ-041 | Reliable 一条 delivery 一个 CMD：N1、N3、N10 | 继承链：T-007（F-27） |
| RQ-042 | LiveOnly 的跳过与不补播：N2 | 「投影进入发件箱」：T-006（Plan T-006 行「`ProjectAsync` 改为登记发件箱」） |
| RQ-043 | 组内串行与退避：N5 | 组门删除：T-005（F-19）；慢订阅：M-1（F-10） |
| RQ-091 | `Lazy` 预热：TASK-15 | 达标判定与 C／D 类：M-1（F-02～F-06） |

| AC | 状态 | 证据或缺口 |
|---|---|---|
| AC-003 | **满足** | 第 1 条 `InboundExecutorWireTests.The_next_frame_of_a_connection_starts_only_after_the_previous_job_completes`；第 2 条 `Two_connections_sending_alternately_keep_their_own_order_and_never_overlap`；第 3 条 `A_flooding_connection_never_has_more_than_one_inbound_cmd_waiting_and_cannot_starve_another`（解读见 §1.1） |
| AC-004 | **满足** | `TimerSourceDownstreamTests.A_due_tick_while_the_previous_one_waits_is_not_enqueued_beside_it`；`CoalescingGameplaySourceTests.A_tick_while_the_previous_cmd_is_queued_is_coalesced_and_ticks_resume_after_it_completes` |
| AC-005 | 执行位置一半满足；**测量一半未做** | `PasswordComputationLimiterTests`；测量归 M-1（F-02） |
| AC-006 | **未满足的 Spec 验收**（裁定第 2 条） | 已满足的部分：附录 A 对账行与附录 B 理由（`entry-reconciliation.md` §2）；附录 F 名单内的写入口有判据、两次全量零违规（`GameplayWriteEntryTableTests`；`logs/task-18-violations.txt`、`logs/task-19-violations.txt`）。**缺口**：RQ-061 判据只挂附录 F 名单（13 个类加 `AltarStateStore`），名单以外的进程内玩法状态（例如幻影塔的 `TowerInventoryDomainService`）不在判据内，所以「代码中检索不到在执行器外改状态的路径」今天不成立；附录 B 生产者一行尚缺两个名字（F-62）。**承接者 T-009**：复核名单并补挂点、补名（F-62）。最终核验之前必须成立 |
| AC-007 | 三种情形以 CMD 清理、关自己不自锁、停机不丢位置：满足；**「帧在断开之前到达」的严格形式未满足**（W7 只断言前缀） | `LifecycleExecutorWireTests` W5～W9、K；`ShutdownPositionPersistenceTests`；严格形式归 T-006（F-20） |
| AC-015 | **满足** | `DomainEventDispatchTests.Executor.cs` N1～N10 与 `DomainEventDispatchTests.cs` 的既有用例（映射见 TASK-12 §9.1） |

AC-006 不阻断 T-005，理由见裁定第 2 条：
- T-004 的完成条件只要求「附录 F 指定的玩法状态写入口」；
- T-005 要删的锁，所保护的状态都在名单内。

## 4. 附录 A／B 对账的最终状态

- **TASK-16 的结论仍然成立**（`entry-reconciliation.md` §2）：
  - 附录 A 每一行都有「来源 → CMD 类型 → 测试 → 探针终值」；
  - 附录 B 每一行都有理由与依据；
  - 执行器外实际存在的代码，全部对到附录 B 的某一行。
- **之后的变化**（`entry-reconciliation.md` 是带日期的记录，不回改）：
  - TASK-17：门断言开启。组门与调度并发槽不挂门断言，改为反向断言（F-17）。
  - TASK-18：
    - 附录 B「开始监听前的启动期工作」一行末句改为「不需要」（F-16）；同一格前半句「作为 RQ-061 的启动期例外」未随之改（F-75）。
    - 附录 F 挂点补入 `AltarStateStore`。
  - TASK-18 评审：附录 B 生产者一行要补名两项（`LoginSessionMaintenanceService` 的关闭触发出队循环、卡牌协调器的 `Dispose`），由 T-009 处理（F-62）。
- `entry-reconciliation.md` §2.1「留给后续」一栏中写 T-004c 的 4 行，今天都已关闭：
  - 第 2 行 `:371`：TASK-17 改写；
  - 第 3 行 3.3：TASK-17 处置；
  - 第 7 行 `:149`：TASK-17 删除；
  - 第 11 行启动期例外：TASK-18 判定不需要（F-16）。

## 5. 完成条件

| 条件 | 证据 | 结论 |
|---|---|---|
| 全量测试与表征测试通过 | 两次正式全量，失败都只有基线已有的 2 条 `DimensionDungeonTests`（`baseline.md`），表征 8 通过、2 跳过，金样本未改：<br>• TASK-18：8213 条（`logs/task-18-build-test.txt`）；<br>• TASK-19：8224 条（`logs/task-19-build-test.txt`） | 成立 |
| 门断言和 RQ-061 判据在一次全量运行中零违规 | `logs/task-18-violations.txt`、`logs/task-19-violations.txt`。两次都是 71 条记录，全部属于反例类与正向对照；反例类以外 0、没有测试帧 0、`exe:` 0。TASK-19 那次与 TASK-18 逐类相同 | 成立 |

## 6. TASK-08～TASK-18 的交接核对

### 6.1 方法

- 由只读调研代理逐个读 TASK-08～TASK-18，抽出所有「交给某单元／后续」的条目，与 `follow-ups.md` 的 F-01～F-62 交叉核对。
- 本单元对其中「对不上」的每一行，都回到 TASK 文件原文复核。
- 抽取表与逐行位置在 `logs/task-19-handover.txt`。

### 6.2 结果

- 抽出条目数：14 ＋ 16 ＋ 12 ＋ 13 ＋ 12 ＋ 1 ＋ 17 ＋ 11 ＋ 10 ＋ 14 ＋ 11 ＝ 131（依次为 TASK-08～TASK-18；按 `logs/task-19-handover.txt` 的表逐行计数。调研代理原报 TASK-11 为 14、合计 132，表中实为 13 行）。
- 对上 116 条：对到 F 编号，或对到已关闭的证据位置。
- 对不上 15 条：3 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 2 ＋ 3 ＋ 3 ＝ 15（TASK-08、09、10、14、15、16、17、18，没有对不上条目的单元不列）。116 ＋ 15 ＝ 131。
- 这 15 条归并为 12 个事项，处置如下。没有一项属于 §5.3 的「阻断 T-005」。

| 事项 | 原文位置 | 处置 |
|---|---|---|
| 容器释放的 10 秒排空只能按耗时间接核对（审查 N-2） | TASK-08:401、TASK-09:455 | F-69。本单元对比了 TASK-18 与 TASK-19 两次全量的逐条耗时：多出 ≥ 9 秒的 3 条都是读真实 PVF 的世界用例，隔离下仍是 10～11 秒。这 3 条在 TASK-10～TASK-17 的全量里都是 9～23 秒，TASK-18 那次的 1～3 秒才是例外。不是排空的特征 |
| Weakness／ItemSale 区域直接调 handler，曾预计翻转后会自我等待 | TASK-08:350、:408 | F-72，已在本单元核对关闭：TASK-17、TASK-18、TASK-19 三次全量都通过，`GameplaySelfWait` 0 次 |
| 跨容器 SQLite 竞争用例是否保留 | TASK-08:351、:408 | F-68，交 T-005 |
| 顶替关闭改为登录 CMD 发件箱里的关连接条目 | TASK-10:610、:631 | F-65，交 T-006 |
| GM／HTTP 与启动作业的 `NewRoot`「只核对」 | TASK-14:907 | F-66，交 T-007 |
| 重型宿主建议 1、2 没有归属 | TASK-15:960～965、TASK-16:489 | F-71，归测试基础设施 |
| `DetachmentSiteTests` 拦不住作业内新增的未抑制 `Task.Run`，要靠人工检索 | TASK-16:526 | F-70，人工把关 |
| RQ-061 的发件箱登记一半 | TASK-17:265、TASK-18:44 | F-64，交 T-006 |
| `GameplayJobHost` 结构判据的去向（F-36 归属列仍写 T-009） | TASK-17:266 | F-36 拆分：结构判据一半在 TASK-19 关闭 |
| `NetworkMessageDispatcherTests:118` 的封锁时点「交独立评审核对」，TASK-17 评审没有点名 | TASK-17:455 | F-73，已由 TASK-19 第 1 轮结果审查核对并关闭；既有弱点记为信息 |
| `CommandBusTests` 3 条交 T-005，只写在已关闭的 F-11 里 | TASK-18:610 | F-67，交 T-005 |
| N4：Host 方案 A 的接线没有判据，处置写「交 T-006 之后的 Host 取证收口复核」 | TASK-18:626 | F-61 的归属列改为同样的内容 |

### 6.3 反向核对

`follow-ups.md` 中有 5 处来源描述与原文不符：F-02、F-15、F-56、F-58、F-62。都只是行号、数字或出处标注的问题，不影响去向，已在原行改正。

### 6.4 另外两项

- **Plan T-004 行的措辞**：与实际拆分及「两次全量」的证据形状不符，记为 F-74，由主控方在规划 M-1／T-005 时修正（裁定第 4 条）。
- **TASK-18 待裁定的那几条**：都已在 TASK-18 的裁定与处置中落定，没有遗留。

## 7. 移交清单（只列 F 编号）

| 去向 | F 编号 | 数目 |
|---|---|---:|
| M-1 | F-02、F-03、F-04、F-05、F-06、F-07、F-08、F-09、F-10 | 9 |
| T-005 | F-17（删除一半）、F-19、F-67、F-68 | 4 |
| T-006 | F-20、F-21、F-22、F-23、F-24、F-25、F-61（T-006 之后的 Host 取证收口复核）、F-63、F-64、F-65 | 10 |
| T-007 | F-26、F-27、F-28、F-29、F-59、F-66 | 6 |
| T-008 | F-12（附录 C）、F-30、F-31、F-32、F-33、F-34、F-58（帧改为显式数据时复核） | 7 |
| T-009 | F-35、F-36（AGENTS.md 一半）、F-60、F-62（含 AC-006）、F-75、F-76 | 6 |
| 每个后续单元 | F-69 | 1 |
| 测试基础设施 | F-45、F-55、F-71 | 3 |
| 取证缺口 | F-37、F-38、F-39 | 3 |
| 人工把关、出现偶发或增加写入方时 | F-43、F-44、F-70 | 3 |
| 信息 | F-40、F-41、F-42、F-46 | 4 |
| 主控方 | F-01（立项）、F-74（规划时修正 Plan） | 2 |

合计 9 ＋ 4 ＋ 10 ＋ 6 ＋ 7 ＋ 6 ＋ 1 ＋ 3 ＋ 3 ＋ 3 ＋ 4 ＋ 2 ＝ 58。§6.2 中 `:118` 那一项已在第 2 轮关闭，不再列入；F-76 是第 2 轮新增，归 T-009。

## 8. 结论

- **T-004 完成。**
  - Plan T-004 行的 20 项交付都有证据（§1）；
  - 两项完成条件都成立（§5）；
  - 交接条目全部有去向（§6）。
  - 留给后续的部分都是 Plan 或已有裁定分配给 T-004 之后步骤的，逐项列在 §7。
- **未满足的 Spec 验收**：
  - **AC-006** 中「检索不到执行器外改状态的路径」一半，承接者 T-009（F-62）；
  - AC-005 的测量一半，承接者 M-1（F-02）；
  - AC-007「帧在断开之前到达」的严格形式，承接者 T-006（F-20）。
  - 三项都必须在最终核验之前成立，今天不得记为已满足。
- **T-005 进入条件中属于 T-004 的一半成立**：门断言与 RQ-061 判据在两次正式全量中零违规，生产中没有执行器外的门持有者或附录 F 写者被判据发现。
  - 组门与调度并发槽在执行器外持有并等待作业，这是设计如此：它们正是 T-005 要删的东西，并且有反向断言守着（F-17、F-19）。
- **T-005 还依赖 M-1**：M-1 尚未执行，本对账不替它下结论。

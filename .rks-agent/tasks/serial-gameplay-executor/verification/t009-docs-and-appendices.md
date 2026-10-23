# T-009 文档与附录最终对账（TASK-33：T-009d）

- 日期 2026-10-24。冻结点 `2bdb1afd`（`git diff 2bdb1afd -- Server Tool` 为空，`logs/task-33-verify.txt`）；对照基准 `a02bce46`。
- 用途：TASK-34 的 AC 状态表与 A 最终汇报从这里取输入；AGENTS.md 与 `Docs/Agent/ArchitecturePhilosophy.md` 的现状图改写（RQ-100 的 A 一半、根 SC-10），Spec 附录 A、B、E、F、G、H 在冻结点上的最终对账，RQ-024 与 AC-009、AC-022 的 A 一半。
- 写法：仿 `t007-closure.md`，只写「要求 → 冻结点上的位置 → 证据 → 状态」。未关闭事项只引 F 编号，内容在 `follow-ups.md`；推导过程在 `tasks/TASK-33.md`。
- 文件:行 指冻结点；路径相对 `Server/`，另有注明的除外。日志路径相对 `verification/`。
- 本单元不构建、不跑测试：「通过」取 TASK-31 的正式全量（`logs/task-31-build-test.txt`，原始输出 scratchpad `t31/formal-raw.txt`），A 封存状态的最终全量在 TASK-34（TASK-33 主控方裁定第 6 条）。

## 1. RQ-100 的 A 一半与根 SC-10

| `tasks/TASK-33.md` §2.3 行 | AGENTS.md 节（改后行号） | 改了什么 | 归属 |
|---|---|---|---|
| 1 | 「架构设计」一句话与走向图（81～108） | 加执行器与发件箱；dispatcher 入队 `inbound:` 作业并等待；出站一步改为登记时编码 → 发件箱 → 作业结束后交给 `OutboundSender` | A |
| 2 | 「Server 解决方案结构」表（44、47） | Application 补执行器与发件箱；Networking 的「发送器」改为出站编码器与发送端 | A |
| 3 | 「1) 领域服务」（128） | 删去已不存在的 `OnlineUsers/` 目录（「执行门」随之消失）；「唯一那把锁」是保留的短锁，照写 | A |
| 4 | 新增「玩法执行器、发件箱与发送端」（282～345） | 一条队列、生产者、不加门、不等网络、运行期判据、请求级帧（过渡）、调用链、失败与日志、发件箱（含来源关闭的过渡约定）、发送端（含日志级别与已知缺口）、停机 | A |
| 5 | 「2) 命令执行总线」（346～415） | 契约按冻结点列；`SendAsync` 两种语义（过渡）；管线 0／45／58／60／100（过渡）；删去门与租约一段；多命令多事务（过渡）；注册一段去掉访问方式与已删除的方法名，Workflow 四条按冻结点写；同一段里基准 `a02bce46` 上就已漂移的三处一并按冻结点改正（`LoginGameUser` → `RedeemGameLogin`，删去不存在的 `AcknowledgeGameExit`，「`ApplicationModule` 只留 `AddCommandDispatching()`」改为两处都注册命令） | A |
| 6 | 「3) Socket 层」（420、434～440） | dispatcher 等作业完成；端点校验在登记时；连接生命周期是 `lifecycle:` 作业 | A |
| 7 | 「handler 业务语义规则」（470、484～485、494～497） | 理由一句删去「并发门」；清理通告投递点的 `文件:行` 按冻结点改正；`SelectCharacterHandler` 例外一段去掉取门与 lease | B 的节，最小改动 |
| 8 | 「端到端示例：GameLogin」（511～519） | 第 2、3、4 步改为作业、没有租约、登记进发件箱 | B 的节，最小改动 |
| 9 | 「SelectCharacter」（526～531） | 去掉门与 lease；关闭排在已登记消息之后 | A |
| 10 | 「领域事件与即时投影」（549、553～559、587、596～599、608、613～619） | `ProjectAsync` 登记进发件箱；组循环是生产者、删去准入门；删去重试上限与投递期限；写明 `NotificationOutcome.Delivered` 今天的含义（F-122 的 A 一半）；日志一段按冻结点；表外一处 `文件:行` 漂移改正（`SelectCharacterLifecycleServices.cs:204`） | A |
| 11 | 「判据偶发失败」（842～851） | 新增内容数据偶发一条（F-125），用例名算身份 | A |
| 12 | 「其他与主设计的偏差」（861～864、870） | `CLIENT_SPEC_STATISTIC` 改为不算纯遥测、在执行器上（F-35）；补「handler 仍在作业内编排多条细粒度命令」 | A |
| 13 | 「快速开发」（923、928～930） | 第 4 步删去 AccessMode；新增第 6 步扇出变长字段的上限约定，不设判据（F-118、F-116 的 (c)） | B 的节，加一条约定 |
| 14 | 「构建与运行」（900～901、909～910） | `Networking.Tests` 的数按冻结点换（`logs/task-31-isolated.txt`、`logs/task-31-build-test.txt`）；「同账号有闸」一句改为写事务只在执行器上 | A |
| 15 | 「接口后缀标明类别」表（952） | 加入队点 `IGameplayQueue` 一行（F-36 的 AGENTS.md 一半） | A |

- 篇幅：AGENTS.md 新增 170 行、删除 83 行（`git diff --stat 2bdb1afd -- AGENTS.md`），新增一节 64 行（含空行）。删掉的过期文字少于新增的文字：执行器一节本身是新内容，旧文里没有对应的段落可删。
- `ArchitecturePhilosophy.md`：文件头引用块加一行改写记录；§1 走向图加执行器作业与发件箱、`SendAsync` 注明在作业里内联（过渡）、出站改为登记；「第二条出站路」一段改为登记；§5 两条改写（多命令编排、执行方式与管线以 AGENTS.md 为准）。§2～§4 未动。
- 引用核对：`logs/task-33-agents-refs.txt`；自身规矩与过渡态一致：`logs/task-33-retrievals.txt` R1、R2。
- 状态：**满足**（以本单元结果评审为准）。B 所属节里 A 以前就漂移的名字没有改，列在 §13 第 5 条（F-128）。

## 2. Spec 附录的改动

改动逐行在 `spec.md` 修订记录 R4；授权依据：附录的事实性修订与 VC-10 随本单元结果评审生效，不另做 Spec 修订审查（TASK-33 主控方裁定第 1 条）；VC-10 由 TASK-30 主控方裁定第 3 条授权。改动与 R4 的逐对核对在 `logs/task-33-boundary.txt`。RQ、AC、Spec 正文与 frontmatter 不改。

## 3. 附录 A（AC-006 的附录 A 一半）

检索命令与结果全在 `logs/task-33-appendices.txt`「Appendix A」。继承链口径（`t007-closure.md` §2）：事件作业与作业登记的关闭引起的 `lifecycle:disconnected` 各加一跳，入站、定时、GM、HTTP、启动恢复与传输层自发的关闭是新根。

| 行 | 来源 | 作业类型（冻结点上的位置） | 经真实入口驱动的测试 | 状态 |
|---|---|---|---|---|
| 1 | 入站协议 | `inbound:<ClientMessageType>`（`MessageHandlerRegistry.cs:89`）；注册 173 条、141 个 handler 类型，遥测 14 条、2 个 handler 类型，与基准 `c3115f9a` 相同（scratchpad `t33/handlers.py`） | `InboundExecutorWireTests`、`GameplayDispatchTests.Request_frames_are_built_inside_the_job_and_reach_the_commands_it_runs` | 满足 |
| 2 | 效果账本重放 | 随所属入站作业（`NetworkMessageDispatcher.cs:84`） | `EffectDispatchOnExecutorTests`、`EffectReplayWireTests.Resending_a_committed_effect_request_on_the_same_connection_replays_without_a_second_charge_and_keeps_the_connection` | 满足 |
| 3 | 连接生命周期 | `lifecycle:connected`、`lifecycle:disconnected`、`lifecycle:depth-exceeded`（`NetworkConnectionLifecycleDispatcher.cs:29`、`:33`、`:59`）；关连接是 `OutboxConnectionCloser` 登记的请求（R4 改正） | `LifecycleExecutorWireTests`、`ShutdownOrderWireTests`、`ShutdownPositionPersistenceTests`、`ConnectionLifecycleDispatchTests`、`ConnectionCallbackBoundaryTests`、`LifecycleCallChainTests.A_disconnect_after_a_cmd_registered_close_is_a_child_of_that_cmd`、`LifecycleCallChainTests.A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection`、`OutboxWireTests.A_cmd_closing_another_connection_closes_it_after_the_frames_it_registered_for_it` | 满足 |
| 4 | 登录会话 | `timer:LoginSessionMaintenanceService`（`LoginSessionRuntime.cs:44`）、`lifecycle:disconnected`；`lifecycle:close-superseded` 在 `Server/src` 中 0 处 | `LoginSessionMaintenanceExecutorTests.A_superseded_connection_is_disconnected_and_then_closed_by_the_cmd_that_superseded_it`、`LifecycleExecutorWireTests.A_superseded_connection_is_closed_by_the_login_cmd_and_its_teardown_is_that_cmds_child`、`CredentialEntryHttpTests.A_launcher_login_that_supersedes_a_game_connection_is_the_parent_of_its_teardown`（顶替关闭继承 HTTP 登录作业的链；本行文字与之一致，不改） | 满足 |
| 5 | 后台与调度 | `timer:<类型名>` 字面量 22 处（检索式 `"timer:\w+"`，含两塔扫描 2 处常量；卡牌计时器的 2 处带 `/`，另行检索，见第 6 行；另有调度任务的插值 1 处）；冻结点上本行 21 个服务、托管服务注册 33 个（R4 改正，scratchpad `t33/hosted.sh`） | `TimerSourceReconciliationTests`、`TimerSourceDownstreamTests`、`ScheduledTaskExecutorTests`；基准证据中的 `NotificationRetryDrainExecutorTests` 随重试排空服务删除（TASK-23） | 满足 |
| 6 | 卡牌计时器 | `timer:DungeonCardPresentationCoordinator/layout`、`/auto-flip`（`DungeonCardPresentationCoordinator.cs:64`、`:217`） | `DungeonCardPresentationCoordinatorTests`、`CardTimerShutdownTests`；排空之后到达的调度：`DungeonCardPresentationCoordinatorTests.Settlement_scheduled_after_the_coordinator_drained_is_ignored_without_throwing`、`DungeonCardPresentationCoordinatorTests.A_layout_opened_after_the_drain_still_answers_but_starts_no_auto_flip`。本行文字与之一致，不改 | 满足 |
| 7 | 两塔离场队列 | `timer:BossTowerReturnSweepService`、`timer:IllusionTowerReturnSweepService` | `IllusionTowerPublicFlowTests.A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone` | 满足 |
| 8 | 事件订阅 | `event:<订阅 ID>`、`startup:event/<订阅 ID>`（`DomainEventDispatcher.cs:117`）、`event:<订阅 ID>/depth-exceeded`（`:130`，R4 补）；订阅 52 个（Reliable 17、LiveOnly 22、Direct 13），scratchpad `t29/subcheck.py` 在冻结点复核不变 | `DomainEventDispatchTests.An_event_cmd_is_a_child_of_the_cmd_that_published_its_fact`、`DomainEventDispatchTests.A_reliable_delivery_past_the_depth_limit_is_settled_terminal_and_its_root_connection_closed`、`DomainEventDispatchTests.Deliveries_left_by_an_earlier_process_run_as_startup_recovery_and_new_facts_as_event_cmds` | 满足 |
| 9、10 | GM、启动器 | `gm:`／`http:`（`HttpGameplay.cs:111`、`:112`）；`c3115f9a`..`2bdb1afd` 之间 GmWeb、HttpApi 没有一行路由声明或豁免元数据变化（scratchpad `t33/routes.sh`），51 条与 3 条的数沿用 | `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption`、`GmGameplayRouteTests.Each_gm_request_runs_downstream_as_one_gm_cmd`、`CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds` | 满足 |
| 11 | 启动恢复与启动作业 | `startup:` 四个字面量与 `startup:event/` | `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs` | 满足 |

所列测试在 TASK-31 正式全量中通过。**AC-006 的附录 A 一半：满足。**

## 4. 附录 B（AC-006 的附录 B 一半）

| 行 | 安全理由 | 判据或检索（冻结点） | 状态 |
|---|---|---|---|
| 解包与帧适配、入站帧计数 | 只处理字节 | `GameInboundFrameCounter` 在；`ReceivedStreamBoundaryTests` | 满足 |
| 发送端 | 只读发件箱交来的不可变帧；编码在登记时、在作业内（R4 补） | `OutboundSender`、`RegistryOutboundEncoder` 在；`OutboundSenderTests.Delivery_runs_off_the_executor_even_when_submitted_from_a_job`、`OutboundRealSocketTests.A_job_that_writes_to_a_connection_directly_fails_as_a_program_error`、`TransportWriteSourceTests`、`DetachmentSiteTests`；基准证据 `ExecutorSendDeadlineTests` 随期限关闭删除（TASK-23） | 满足 |
| 传输层的连接注册 | 传输自身的状态 | `TouchSocketConnectionRegistry`、`TouchSocketTransportDrain`、`NetworkPlugins` 四个回调的 `RunDetachedAsync`（`NetworkPlugins.cs:25`、`:42`、`:71`、`:159`）；`ConnectionCallbackBoundaryTests`、`TransportDrainRegistrationTests` | 满足 |
| UDP 发现 | 只回 UDP 包 | `A21UdpDiscoveryService` 不在 `IGameplayQueue` 持有者里 | 满足 |
| PVF 内容源、GM 自身状态、只读 SQLite 查询 | 只读或不属于玩法状态 | `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption`、`AuthenticationTwoPhaseTests` | 满足 |
| 口令哈希 | 没有共享状态 | `PasswordComputationLimiterTests`、`CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds` | 满足 |
| 纯遥测 handler | 不改状态、不写库、不发包 | 2 个 handler、14 个消息类型（冻结点复核）；启动检查 `MessageHandlerRegistry.cs:74` | 满足 |
| 启动预热 | 开始监听前加载完毕 | `ContentWarmupTests.Participants` 71 个（冻结点复核） | 满足 |
| 开始监听前的启动期工作 | 开始接受连接与 HTTP 之前 | `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs` | 满足 |
| 不碰玩法状态的 HTTP 路由（含预缓冲） | 只读 HTTP 请求本身 | `HttpGameplay.cs:87`、`:93`；`GmGameplayRouteTests.A_slowly_sent_body_is_read_off_the_executor_before_its_cmd_is_queued`、`HttpGameplayTests.A_body_over_the_limit_is_read_whole_when_the_size_feature_is_read_only`、`HttpGameplayTests.A_body_over_the_limit_is_read_whole_when_there_is_no_size_feature`；只读或缺失情形的临时文件偏离 R4 已写入（F-33） | 满足 |
| 生产者的入队侧 | 只组织作业、入队、等待 | `IGameplayQueue` 原检索式（scratchpad `t16/boundary.sh` 第 12 行）在冻结点 38 行：37 ＋ 1（`CommandBus`）＋ 1（`GameplayTimerWork` 第二个重载）－ 1（`NotificationRetryDrainService`）＝ 38，文件数 33 不变（scratchpad `t33/q.sh`，R4 写入）；`GameplayExecutorSelfWaitTests` | 满足 |
| 请求级帧（过渡） | 在作业内进入与退出 | `GameplayStateCensusTests.Exemptions` 的 E3 四项：`NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`、`CommandBus`（`t009-criteria.md` §9），与 AGENTS.md 过渡态第 4 项（§13 T4）一致 | 满足 |

AC-006 中「代码中检索不到在执行器外改状态、写库或发包的路径」一半引 `t009-criteria.md` §1、§2 与 TASK-31 的取证（`logs/task-31-violations.txt`：反例类与对照以外 0，`exe:` 0），不重跑。**AC-006 的附录 B 一半：满足。**

## 5. 附录 E（AC-014）

十七行的证据用例名（`t006-closure.md` §3，编号见 `t006-test-dispositions.md` TASK-24 §4）经 26 个改名换算后，在冻结点上全部找到（`logs/task-33-appendices.txt`「Appendix E」）。DR-15 另有 TASK-31 的线级用例 `HandlerFailureConnectionIntegrationTests.A_handler_that_throws_inside_its_job_closes_only_its_connection_and_is_logged_once`（`t009-logs-and-cases.md` §5）。`retryLimit` 与 `NotificationRetry*` 在 `Server/src` 中 0 处（DR-13、DR-14）。DR-16 维持「满足，附条件」（`t006-closure.md` §3 末）。附录 E 的「今天」「新的承接方式」与冻结点不矛盾，不改。**AC-014：满足，附条件（DR-16）。**

## 6. 附录 F（AC-017）

- **「删除」栏**：名字清单（`logs/task-33-deleted-names.txt`）在冻结点 `Server/src` 中只有同名无关的命中：`Lease` 22 行都是 `TownAreaMembershipLease.Lease` 这类属性与 `DungeonEffectRecoveryService` 的常量，不是被删的私有嵌套类型；R1 形式 `\.Gate\b` 8 行与 TASK-26 相同，都是内容规则里的 `Gate` 成员，没有一行在 `Protocol/Game/Party` 下。`SemaphoreSlim` 只在 5 个保留的文件里（PVF 与规则初始化 4 个、`PasswordComputationLimiter`），`Task.Run` 只在执行器、发送端与两个 PVF 文件里，`ScheduledTaskHostedService` 中 0；`LeaseSet`、`groupGates`、`AcquireManyExclusiveAsync`、`participantGates`、`EnterParticipantsAsync` 0。
- **「保留」栏**：参与者集合（`CommandContracts.cs:37`）、短 `lock` 184／11／3（R4 补冻结点的数）、`GameInboundFrameCounter`、`TouchSocketConnectionRegistry`、`GmLoginLimiter`、`GmIconPackStore`、`AcquireWriteAsync` 14 处声明、`PasswordComputationLimiter` 的信号量，都在。
- **「改为执行器内访问」**：名单的唯一出处 `GameplayWriteEntryTableTests.Types` 42 项 ＝ 挂点 40 ＋ 不持有状态的写入口 2（`GameplayStateCensusTests.StatelessEntries`）；`GameplayStateCensusTests.Exemptions` 33 项 ＝ E1 7 ＋ E2 21 ＋ E3 4 ＋ R061 1，与 `t009-criteria.md` §2.3 的「持有者 73 ＝ 挂点 40 ＋ 豁免 33」一致（按源码数条目，不运行判据）。判据 `GameplayStateCensusTests.Every_singleton_holding_mutable_state_is_a_write_entry_or_a_listed_exemption`、`GameplayWriteEntryTableTests.Every_member_of_the_listed_types_is_classified_and_exactly_the_writes_assert`、`GameplayWriteEntryTableTests.Transactions_are_created_only_at_the_listed_sites_and_the_writes_assert`、`LockRemovalSourceTests`、`GameplayWriteAssertionTests`，在 TASK-31 正式全量中通过。
- **F5，有意例外**：`CharacterContainerDomainService` 的 `FindContainer`、`GetContainer`、`GetContainers` 在事务内会登记参与者，分类表却按读登记（`GameplayWriteEntryTableTests.cs:382`～`:384`，`ReadNote(StagedRead)`，理由常量在 `:490`）。按 TASK-18 的六种形状，「登记参与者」本应算写；这里只做暂存，不写就不提交，执行器外走加锁直读（TASK-30 第 1 轮评审 F5，评审认为可以接受）。交 TASK-34 在 A 最终汇报的「判据的已知边界」中列出（§13 第 2 条）。

**AC-017：满足。**

## 7. 附录 H 中属于 A 的三行（AC-009④）

| 行 | A 的一半 | 冻结点上的证据 | 状态 |
|---|---|---|---|
| dispatcher | 检查在执行器上、作业开始时执行（AsyncLocal 帧，过渡） | `IsBlocked`、`IsSuperseded`、`CaptureAuthority`、`NetworkCommandAuthorization.Enter` 都在作业体 `HandleOnExecutorAsync` 内（`NetworkMessageDispatcher.cs:64`、`:67`、`:68`、`:70`）；遥测路径的两处只读检查在 `:108`、`:110`（附录 B 遥测行）；`GameplayDispatchTests.Request_frames_are_built_inside_the_job_and_reach_the_commands_it_runs` | 满足 |
| 45 `SelectedSessionExecutionBehavior` | 取锁部分删除 | 该文件中含 gate／semaphore／lease／borrow／acquire 的词只有 `CommandExecutionDelegate`；`t005-closure.md` §4 的逐项在冻结点上的行号：`BeginMain` `:12`、`DemandRecipient` `:13` 与 `:25`、`Capture` `:15`、账号一致 `:16`、参与者集合 `:19`、逐个 `DemandCurrent` `:23`～`:24`、作用域 `:26`、逐个 `BlockAsync` `:32`；`SelectedSessionExecutionBehaviorTests` 的五条在 | 满足 |
| 0 `CommandLoggingBehavior` | 执行器对每个作业记录调用链 | 开始、失败、取消、完成四条模板都含根、父、深度（`GameplayExecutor.cs:267`、`:313`、`:324`、`:333`）；`GameplayExecutorLoggingTests.Start_and_end_logs_carry_root_parent_depth_source_and_type_and_the_end_log_both_timings`、`GameplayExecutorLoggingTests.The_failure_log_is_an_error_with_the_exception_and_the_chain`、`GameplayExecutorLoggingTests.The_failure_log_names_the_source_connection`；`CommandLoggingBehavior` 本身仍在（B，§13 T5） | 满足 |

附录 H 文字按 R3-1 已拆清，不改。

## 8. 附录 G 与 VC-10

VC-1～VC-9 的证据（`t006-closure.md` §4、`t005-closure.md` §5）在冻结点上都找到（`logs/task-33-appendices.txt`「Appendix G」）。

VC-10 的证据（TASK-33 主控方裁定第 5 条：组合，附条件）：

1. 「后续阶段全部跳过」：`PartyNotificationTests.A_failed_delivery_in_one_formation_stage_skips_every_later_stage`——失败的阶段 1 只有被邀请者一人，断言邀请者的条目全为 `Skipped`；
2. 「组内其后的条目不论收件人都跳过」：`OutboundSenderTests.A_required_failure_stops_its_group_and_a_best_effort_failure_does_not`——所有者的必达条目失败后，同组里发往另外两个连接的条目都是 `Skipped`、这两个连接不被关闭。机制在 `OutboundSender.cs:366`（`BreaksGroup`），XO-6 的阶段条目以 `OutboundDelivery.Required` 进入同一个有序组（`PartyNotificationSlice.cs:677`）；
3. 「其余成员随后由断线清理收到当前队伍状态」：各段有测试——失败者被关闭（`PartyNotificationTests.The_watermark_advances_at_registration_and_a_member_whose_delivery_fails_is_closed`）、发送端关闭之后被关连接的拆除作为生命周期作业运行（`OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once`）、成员拆除时给其余成员当前队伍快照（`SelectedSessionPartyCleanupTests.Member_cleanup_clears_the_departing_member_and_refreshes_survivors`）；从失败的阶段一直到其余成员收到快照的整条衔接是读码得出的，没有一条测试直接证明。VC-10 的文字照此写明。

**VC-10：满足，附条件（证据为组合；第 3 句的衔接为读码）。**

## 9. RQ-024 的 A 一半

脚本 scratchpad `t33/commit_points.py`，输出与脚本全文在 `logs/task-33-commit-points.txt`。

| 比对 | `a02bce46` | `2bdb1afd` | 差异 | 归类 |
|---|---|---|---|---|
| 命令注册（命令类型 → 执行方式） | 204 条：Transactional 170、Direct 21、ReadOnly 9、Workflow 4 | 204 条，同样的分布 | 两侧都有 204，执行方式相同 204，变了 0，只在一侧 0 | 无差异（访问方式参数随 T-005 删除，不比） |
| 事务开启点（`new DomainTransaction(` 与 `DomainTransactionScope` 的开启调用） | 20 | 22 | ① `AmbientStoreAccess.ReadSnapshotAsync` 的 `scope.Begin()` 换成 `scope.BeginSnapshot()`，新开启方法 `DomainTransactionScope.BeginSnapshot` 多一处 `new DomainTransaction(`；② `DomainEventDispatcher.SettleOnExecutorAsync` 新增一处 `transactions.Begin()` | ① 一对一：两侧都是写模式、从不提交的快照读，换方法只为不经写断言与发件箱单元（TASK-18 裁定第 3 条），不是提交点；② 新的系统作业 `event:<订阅 ID>/depth-exceeded` 落定超限 delivery（TASK-29，RQ-053(b)），基准上没有对应的作业，它不改变任何已有用例的提交点 |
| 作业层的事务 | — | — | Reliable 消费与完成标记同事务（`ReliableAsync`）、失败另起事务记重试（`ProcessAsync`）、LiveOnly 先提交消费决定（`LiveOnlyAsync`）、`EpicBook` 启动作业（`InitializeAsync`）、拆除落位置（`SelectedSessionPositionPersistence.AloneAsync`）：两侧开启点相同 | 一对一 |

**RQ-024 的 A 一半：满足**——A 期间未合并、未拆分提交点；新增的一个开启点属于新增的系统作业，有授权的单元裁定。

## 10. AC-009 ①②④（③ 引 TASK-31）

脚本 scratchpad `t33/protection.py`，输出、方法清单与脚本全文在 `logs/task-33-protection-tests.txt`。

- **选取**（`a02bce46` 的 `Server/tests`，方法体引用所列名字之一，或类名含所列片段）：195 个方法。按类：(a) 具名判定 45、(b) 效果账本 45、(c) 会话隔离 30、(d) 登录顶替 4、(e) 槽位边界 83，45 ＋ 45 ＋ 30 ＋ 4 ＋ 83 ＝ 207，其中 12 个成员关系由同一方法兼属两类或三类；按类组合 a 39、ab 1、abc 1、ac 4、b 38、bc 5、c 20、d 4、e 83，39 ＋ 1 ＋ 1 ＋ 4 ＋ 38 ＋ 5 ＋ 20 ＋ 4 ＋ 83 ＝ 195。
- **冻结点上的对应**：同名 190 ＋ 经 TASK-31 改名 3 ＋ 删除 2 ＝ 195。
- **断言行多重集合**（含 `Assert.` 或 `Skip.` 的行，去空白）：相同 181，不同 12；181 ＋ 12 ＋ 2 ＝ 195。不同与删除的 14 条逐条有处置记录：

| 方法（冻结点名） | 差异 | 处置记录 |
|---|---|---|
| `EffectOperationReplayTests.An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` | 删除 | `task-07-conflicts.md` §14.1；保护意图由 `EffectOperationReplayTests.A_flow_leaked_from_a_closed_operation_cannot_reach_the_bus` 承接（F-58） |
| `EffectOperationReplayTests.Waiting_on_the_connection_gate_keeps_the_original_selected_session` | 删除 | `task-07-conflicts.md` §7（TASK-09 处置：随每连接信号量删除，见 `tasks/TASK-09.md` Result） |
| `LoginSessionCommitRaceTests.RolledBackHandoffDoesNotCloseTheExistingConnection` | 关闭断言改为记录关连接器 | `t006-test-dispositions.md` TASK-23 §3 S11 |
| `LoginSessionReconnectTests.OldDisconnectCannotRevokeOrExpireReplacementConnection` | 同上 | `t006-test-dispositions.md` TASK-23 §3 S12 |
| `JoustIntegrationTests.Native_query_holds_selection_lease_until_exact_physical_batch_is_sent` | 删取门探针与挂起断言 | `task-07-conflicts.md` §14.2（`:743` 一行） |
| `OnlineExpiredItemCleanupTests.Queued_cleanup_revalidates_published_session_after_account_gate` | 删账号门与挂起断言 | `task-07-conflicts.md` §14.2（`:113` 一行） |
| `ReplayStateSnapshotTests.Another_selected_settlement_cannot_commit_between_the_replay_read_and_its_batch_delivery` | 删交错断言；`TrackedEntryCount` 随门删除 | `task-07-conflicts.md` §14.2（`:31` 一行）；`t005-test-dispositions.md` TASK-22 G-N1、G-N2 |
| `ReplayStateSnapshotTests.Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_through_its_batch`（旧名 `Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_and_exclusive_lease_through_batch`） | 删独占等待与共享不能升级；`TrackedEntryCount` 随门删除 | `task-07-conflicts.md` §14.2（`:60` 一行）；`t005-test-dispositions.md` TASK-21 G2、TASK-22 G-N1、G-N2 |
| `ReplayStateSnapshotTests.A_replacement_selected_before_the_original_read_runs_rejects_that_read`（旧名 `Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read`） | 删挂起断言；`TrackedEntryCount` 随门删除 | `task-07-conflicts.md` §14.2（`:177` 一行）；`t005-test-dispositions.md` TASK-22 G-N1、G-N2 |
| `ReplayStateSnapshotTests.A_late_current_read_failure_preserves_all_stored_inventory_and_balances` | `TrackedEntryCount` 随门删除 | `t005-test-dispositions.md` TASK-22 G-N1、G-N2（三条中的一条，删 5 处） |
| `CommonExperienceEffectTests.Unknown_fence_precedes_transaction_gate_release_and_queued_effects_never_price` | 管线探针改为「第二条排在第一条之后」的断言 | `task-07-conflicts.md` §14.2（`:164` 一行） |
| `CommonExperienceEffectTests.Publication_fence_is_synchronous_even_when_discard_waits_for_lifecycle_exclusive` | 删外部持门者与挂起断言 | `task-07-conflicts.md` §14.2（`:191` 一行） |
| `LoginSessionQueuedCommandTests.ReplacedLoginCannotWriteAfterWaitingForTheAccountGateEvenAfterDisconnectCallback` | 删账号门与挂起断言 | `task-07-conflicts.md` §14.2（Persistence 一行） |
| `TitleBookMutationHandlerTests.Worn_requests_reply_with_the_native_equipment_coordinates_and_complete_ack_before_projection` | 删挂起断言 | `task-07-conflicts.md` §14.2；`t005-test-dispositions.md` TASK-21 G3 |

- **通过**：冻结点上的 193 个方法在 TASK-31 正式全量中都有「已通过」行，合计 331 行（Theory 按行计）；没有一个不在日志里。
- **②** `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`：断言行两侧都是 8 行、多重集合相同；方法体不逐字相同，差异只有三处包装（`registry.Register` 包进 `TestGameplay.Run`，其后的段落包进 `TestGameplay.RunAsync(provider, …)` 与收尾的 `});`），TASK-30 §3.3 的包装形状；正式全量中通过 1 行。
- **③** 已在 TASK-31 关闭：`EffectReplayWireTests.Resending_a_committed_effect_request_on_the_same_connection_replays_without_a_second_charge_and_keeps_the_connection`（`t009-logs-and-cases.md` §5）。**④** 见 §7。

**AC-009 的 A 一半：满足，附条件**——①的「通过」取 TASK-31 的正式全量，TASK-34 在 A 封存状态的最终全量里按方法清单逐条确认（§13 第 6 条）。

## 11. AC-022 的 A 一半

脚本 scratchpad `t33/deleted_names.py`，输出与脚本全文在 `logs/task-33-deleted-names.txt`。

- 名字清单：① 在 `a02bce46` 的 `Server/src` 中声明、在 `2bdb1afd` 中不再声明的类型 37 个；② 附录 F「删除」栏点名的成员与机制 25 个；③ F-92、F-99、F-105 点名的名字 11 个；37 ＋ 25 ＋ 11 ＝ 73，跨类重复 24，去重后 49。
- `Server/src` 冻结点：只有 `Lease` 一个名字有命中，22 行都是同名无关的成员（§6）。
- 现状文档 `AGENTS.md`、`Docs/Agent/*.md`：改完后 0 处。
- 其余文档（TASK-33 主控方裁定第 2 条）：`Docs/` 其余 5 个文件 6 处、`Document/` 34 个文件 59 处、`protocol/` 0。逐个归类在日志里：除 `Docs/ProtocolSliceArchitectureNotes.md` 之外都是带日期或任务归属的记录（保留原文的记录，即 AC-022 所说的那一类）；`Docs/ProtocolSliceArchitectureNotes.md` 不带日期、描述「当前形态」，第 162 行把 `SelectedSessionTransitionGate` 写成 `SelectCharacterHandler` 今天持有的依赖，已过期，不在本单元改，列进移交（§13 第 5 条，F-128）。

**AC-022 的 A 一半：满足。**

## 12. AC 状态（交 TASK-34 的 AC 状态表照抄）

| 验收 | 状态 | 依据 |
|---|---|---|
| AC-006 | 满足 | §3、§4 |
| AC-009（A 一半） | 满足，附条件 | §10（①的「通过」待 TASK-34 最终全量确认）、§7（④）；③ 见 TASK-31 |
| AC-014 | 满足，附条件 | §5（DR-16） |
| AC-017 | 满足 | §6 |
| AC-022（A 一半） | 满足 | §11（检索范围按 TASK-33 主控方裁定第 2 条：现状文档为 0；范围外的一份过期现状描述列入 F-128） |
| RQ-024（A 一半） | 满足 | §9 |
| RQ-100（A 一半）、根 SC-10 | 满足 | §1 |
| VC-10 | 满足，附条件 | §8 |

## 13. 移交 TASK-34

1. **AGENTS.md 中写为过渡态的六项**（SC-12 过渡机制清单的起点；清单是正式出处，二者不一致时 TASK-34 可以改 AGENTS.md 中写为过渡态的那几段，TASK-33 主控方裁定第 9 条）：

| # | 过渡态 | AGENTS.md 中的位置 | 「新代码不要依赖」的内容 |
|---|---|---|---|
| T1 | `ICommandBus.SendAsync` 在作业里内联执行，执行器外入队并等待（`bus:<命令类型名>`） | 「命令执行总线」第 2 条 | 不要在执行器外调用总线来完成玩法动作；命令处理器仍不得派发命令 |
| T2 | handler 在入站作业内经总线顺序派发多条细粒度命令，每条 Transactional 命令一个事务 | 「命令执行总线」末条 | 不要假设一个入站请求只有一个事务；跨命令的补偿仍由 handler 派发专用补偿命令 |
| T3 | 管线五层（0／45／58／60／100）包在每一条细粒度命令外（过渡的是这个粒度，不是五个行为本身） | 「命令执行总线」管线一条 | 不要依赖「一条命令一套管线」（TASK-33 结果评审 L3 补） |
| T4 | 请求级上下文是作业内的 AsyncLocal 帧：`NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`、`CommandBus` 的静态分派深度 | 「玩法执行器、发件箱与发送端」请求级上下文一段 | 不要在作业之外读写这些帧，也不要新增同类的 AsyncLocal 帧 |
| T5 | 细粒度命令失败时 `CommandLoggingBehavior` 与执行器各记一条 `Error` | 同一节「失败与日志」 | 读日志时同一次失败今天可能有两条 |
| T6 | 失败作业关闭来源连接依赖作业已打开发件箱 | 同一节「发件箱」 | 新的带来源连接的作业在开始时打开发件箱 |

2. **遗留问题与判据的已知边界**（列入 A 最终汇报）：F-116 的 (a)(b)（发送端或登记点的通用防线，交主控方立项）；F-118 是否设判据（AGENTS.md 只写了约定，「这条没有判据守着」）；F-125 的生产缺口（PVF 缺 `[minimum level]` 时奖励结算抛错）；F5（§6，`CharacterContainerDomainService` 三个查找按读登记的有意例外）；F-110 的结构保证（T6）；F-122 的改名（AGENTS.md 已写明今天的含义）；F-128 ①（AGENTS.md 的注册一段已写 `RedeemGameLogin`，端到端示例第 3 步仍是检索为 0 的旧命令名，同一文件对同一件事给了两个名字；归子 Spec B，提示 B 的首个单元先改这一节，TASK-33 结果评审 L2）；F-127（「连接已不在」漏掉写端已完成的形态，AGENTS.md 已按今天的事实写成已知缺口，交子 Spec B 首个单元）。
3. **Spec 修订**（列入 A 最终汇报的「Spec 修订」一节）：VC-10 是主控方授权的 Spec 验收修订；附录 A、B、F 的事实性修订见 R4。二者随本单元结果评审生效。
4. **AC 状态**：§12。
5. **本单元发现、不在本单元修的事实错误**（F-128）：
   - AGENTS.md「端到端示例：GameLogin」（B 所属的节，只做了最小改动）里基准 `a02bce46` 上就已漂移的名字：第 1 步「loginId ＋ 密码哈希」（今天是 `MId` 与 `Ticket`）；第 3 步 `LoginGameUserCommand`、`LoginGameUserCommandHandler`、`IAccountDomainService.LoginAsync`、`GetOrCreate`、`OnlineUser.Authenticate`（今天是 `RedeemGameLoginCommand` 经认证服务签发与核对票据）；
   - `Docs/ProtocolSliceArchitectureNotes.md:162`：把 `SelectedSessionTransitionGate` 写成 `SelectCharacterHandler` 今天持有的依赖（不带日期的现状描述，已过期）；
   - `Document/host-component-composition.md`「生命周期」第 4 步只写「由宿主统一停止各监听器与释放服务」，没有写执行器之前要排空生产者与拆除连接（不算错，不完整；AGENTS.md 的停机一段已写）。
6. **AC-009① 的方法清单**（`logs/task-33-protection-tests.txt` 末尾的 193 个方法）：TASK-34 在最终全量中逐条确认通过，结果写进 AC 状态表。
7. **新的 F 编号**：F-128（第 5 条）。本单元关闭 F-35、F-36（AGENTS.md 一半，结构判据一半已在 TASK-19 关闭）、F-89、F-92、F-98、F-99、F-105、F-118；更新 F-110、F-116、F-122、F-125、F-127。

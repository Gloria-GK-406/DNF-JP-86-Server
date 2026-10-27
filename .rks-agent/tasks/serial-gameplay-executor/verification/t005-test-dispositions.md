# T-005 测试处置记录

- 日期 2026-10-23。TASK-21（T-005a，Protocol 层删锁）基准提交 `b1959f3e`；TASK-22（T-005b）在本文件后面续写。
- 分类沿用 TASK-21 Detailed Plan §6.2：E 编译迫使、K 门的反例、G 门内次序、B 交错下的业务结果、V 空转；新增用例记 N。
- 每条删除都写承接者（名字可检索），没有承接者的写明保护意图为什么已由规格消除（TASK-17 主控方裁定第 7 条）。
- 机械核对：`logs/task-21-diff-check.txt`（方法集合、断言行多重集合）。

## TASK-21

### 1. 数量

| 项目 | 基准 | 新增 N | 删除 D | 结果 |
|---|---:|---:|---:|---:|
| Application.Tests | 4573 | 9 | 0 | 4582 |
| Protocol.Tests | 1704 | 6 | 11 | 1699 |
| 其余六个项目 | 1947 | 0 | 0 | 1947 |
| 合计 | 8224 | 15 | 11 | 8228 |

其余六个项目：HttpApi 54 ＋ Pvf 80 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 175 ＋ Host 191 ＝ 1947。
合计：8224 ＋ 15 － 11 ＝ 8228；4582 ＋ 1699 ＋ 1947 ＝ 8228。

改动的测试文件 23 个：已有文件 22 个（Application 3、Protocol 19），新文件 1 个。

### 2. 删除（D ＝ 11）

| 行 | 用例 | 类 | 保护意图 | 承接／消除 |
|---|---|---|---|---|
| K1 | `ProtocolGateAssertionTests.A_projection_or_entry_gate_cannot_be_taken_off_the_executor`（Theory 6 行：城镇区域、副本局、首领塔投影与入场、幻影塔投影与入场） | K | 执行器外取这六把门即抛 | 规格消除：六个门类型已删除（判据 `LockRemovalSourceTests.No_deleted_gate_name_is_left_in_the_production_sources` 守着不再出现）；「执行器上不等门」由 RQ-001 单消费者与 TASK-17～19 零违规全量承接 |
| K2 | `ProtocolGateAssertionTests.The_title_projection_gate_cannot_be_taken_off_the_executor` | K | 执行器外取称号投影门即抛 | 同上（`AchievementTitleProjectionGate` 已删除） |
| K3 | `ProtocolGateAssertionTests.On_the_executor_the_title_projection_gate_is_taken_released_and_never_waited_on` | K | 执行器上取称号门不等待；被占时作业立即失败；取消优先 | 同上；称号投影不插进进入序列（DR-16）在过渡期由作业内被 await 的直发保证（TASK-21 计划 §1，裁定第 3 条），T-006 在发件箱上复核 |
| K4 | `ProtocolGateAssertionTests.The_weakness_delivery_gate_cannot_be_taken_off_the_executor` | K | 执行器外取虚弱投递门即抛 | 同上（`deliveryGate` 已删除，判据 `Every_production_semaphore_is_on_the_list_and_the_list_has_no_stale_entry` 不再列该文件） |
| K5 | `ProtocolGateAssertionTests.A_contended_weakness_delivery_gate_fails_the_job_at_once` | K | 虚弱投递门被占时作业立即失败 | 同上 |
| G4 | `MarriageProposalCodecTests.Two_party_delivery_is_ordered_through_peer_notification_and_command_can_borrow_fences` | G | 被测对象是 `WeddingProposalExecution.RunAsync`：两方取门、命令借用门帧、第二个请求排在第一个的 ACK 与 NOTI 之后 | 机制随门删除（包装函数已删除，借用帧归 TASK-22）。「后一个请求排在前一个的发送之后」由执行器承接：`GameplayExecutorOrderTests.Concurrent_producers_see_their_work_run_in_enqueue_order_one_at_a_time` |

K1 是 6 例，K2～K5 各 1 例，G4 1 例：6 ＋ 4 ＋ 1 ＝ 11。`ProtocolGateAssertionTests` 保留 4 条队伍用例（T-006），类注释的记录数改为 4。

### 3. 改写（G，删掉门相关的断言行，业务断言保留）

| 行 | 用例 | 旧断言 → 新断言 | 保护意图 |
|---|---|---|---|
| G1 | `LeaveDungeonTests.Failed_exit_delivery_releases_the_transition_without_repeating_the_exit` | `Assert.Equal(0, gate.TrackedEntryCount);` → 删除；测试里的门变量与 `StoryHandler` 的门参数一并删除。其余断言（抛 IOException、局已结束、重发不重复退出）不变 | 「发送失败后门被释放」随门删除（handler 不再取门） |
| G2 | `ReplayStateSnapshotTests.Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_and_exclusive_lease_through_batch` | 持共享租约时 `RefreshAsync` 抛 `InvalidOperationException`（「共享租约不能升级为独占」）→ 删除这一段（3 行）。其余断言（精确连接、批次内容、新旧库存、错误连接被拒、批次数 3）不变 | 升级拒绝是借用帧的机制：生产中没有在 45 号行为之内借用独占的调用点（`logs/task-21-upgrade.txt`：0 处），删除取门后这一差异不可达 |
| G3 | `TitleBookMutationHandlerTests.Worn_requests_reply_with_the_native_equipment_coordinates_and_complete_ack_before_projection`（Theory 6 行） | 「下一个作业立即取得称号门」那一段（注释与 `gate.EnterAsync`，没有 Assert 行）→ 删除；全部 Assert 不变 | 「handler 释放了称号投影门」随门删除；ACK 先于投影由作业边界保证（同 K3） |
| G4 | 见 §2 | — | — |

### 4. 编译迫使（E，只删门的实参、参数、声明或 using，断言多重集合不变）

| 文件 | 改动 |
|---|---|
| `Application.Tests/OnlineUsers/OnlineUserServiceTests.cs` | 观察者构造去掉两把门（三处）；一个未再使用的 `var gate` 删除 |
| `Application.Tests/Game/SelectCharacterLifecycleTests.cs` | 观察者与 `SelectCharacterHandler` 构造去掉门；`Fixture.CreateAsync` 的 `achievementGate` 参数（无调用方传入）删除 |
| `Protocol.Tests/Game/AccountRoster/OtherCharacterDetailFlowTests.cs` | 两处构造去掉门 |
| `Protocol.Tests/SharedEffectProjectionTests.cs` | 一处 |
| `Protocol.Tests/Game/ItemUsageProjectionTests.cs` | 一处 |
| `Protocol.Tests/Game/UsageQuotaProjectionTests.cs` | 三处 |
| `Protocol.Tests/Game/TownProjection/TownAreaProjectionDeliveryTests.cs` | 一处 |
| `Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs` | 三处 |
| `Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs` | 一处 |
| `Protocol.Tests/Game/StateSynchronization/TitleBookMutationHandlerTests.cs` | 构造去掉称号门（另见 G3） |
| `Protocol.Tests/Game/StateSynchronization/TitleBookProtocolTests.cs` | 三处订阅者构造去掉称号门 |
| `Protocol.Tests/Game/Dungeons/DungeonLoadingProjectionDeliveryTests.cs` | 构造去掉两把门；`TestWorld.TransitionGate`（无读者）删除 |
| `Protocol.Tests/Game/CharacterDeath/CharacterDeathHandlerTests.cs` | 一处 |
| `Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs` | 七处 handler 构造与一处投影构造去掉门；幻影塔投影哨兵的实参由 17 个减为 14 个 |
| `Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs` | 三处构造去掉门；`TestWorld.TransitionGate`（无读者）删除 |
| `Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs` | 七处构造去掉门；`AltarProtocolCoordinator`、`GiveUpGameHandler` 的门实参删除（另见 G1） |
| `Protocol.Tests/Game/BossTower/BossTowerProjectionConcurrencyTests.cs` | 七处协调器构造与一处投影构造去掉门；一个未再使用的 `var gate` 删除（另见 N） |
| `Protocol.Tests/Game/IllusionTower/IllusionTowerProjectionConcurrencyTests.cs` | 同上 |
| `Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs` | 十七处 handler 构造去掉门；三个未再使用的 `var gate` 删除 |

### 5. 新增（N ＝ 15）

| 行 | 用例 | 内容 |
|---|---|---|
| N1～N9 | `Application.Tests/Execution/LockRemovalSourceTests`：`No_deleted_gate_name_is_left_in_the_production_sources`（J1）、`Every_production_semaphore_is_on_the_list_and_the_list_has_no_stale_entry`（J2）、`A_global_name_is_named_in_any_file`（C1）、`A_name_left_only_in_a_comment_is_still_named`（C2）、`A_protocol_scoped_name_is_named_in_protocol_and_not_in_application`（C3）、`An_access_mode_is_allowed_in_the_protocol_module_only`（C4）、`A_semaphore_outside_the_list_is_named`（C5）、`A_listed_file_without_a_semaphore_is_a_stale_entry`（C6）、`A_name_inside_a_longer_identifier_is_not_named`（C7） | AC-017 的 Protocol 一半；`logs/task-21-lock-scan.txt` |
| N10 | `BossTowerProjectionConcurrencyTests.A_caller_whose_session_is_not_current_is_refused_at_once_without_a_tower_or_party_write` | R4(b)：`ExactSessions` 为假时直接返回 `Rejected(null, CallerNotCurrent)`，不写塔、不写队伍（变异 S5） |
| N11 | `BossTowerProjectionConcurrencyTests.Leave_party_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change` | VC-7 离队（变异 S7） |
| N12 | `BossTowerProjectionConcurrencyTests.Change_leader_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change` | VC-7 换队长 |
| N13 | `BossTowerProjectionConcurrencyTests.Remove_member_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change` | VC-7 踢出队员 |
| N14 | `BossTowerProjectionConcurrencyTests.Give_up_outside_a_tower_runs_the_ordinary_path_once_and_answers_before_the_town` | VC-7 放弃副本（变异 S6） |
| N15 | `IllusionTowerProjectionConcurrencyTests.A_caller_whose_session_is_not_current_is_refused_at_once_without_a_tower_or_party_write` | R4(b)，幻影塔 |

9 ＋ 6 ＝ 15。N10～N15 在基准代码上同样通过：它们钉住的是保留下来的行为（`ExactSessions` 的确定性拒绝；协调器不管时普通路径执行一次并回包），区分力由变异 S5～S7 证明，不是 TDD 的 RED。

### 6. VC-7 的落实与证据

附录 G VC-7 按主控方裁定第 4 条补了「踢出队员」：放弃副本、离队、换队长、踢出队员在重试用尽时不回包、只记日志或回拒绝包的分支消失。

1. **静态**：七个循环逐条分类（`logs/task-21-loops.txt`）；L1～L4 的 `reroute` 条件属 (a)「串行下恒不成立」，随循环删除；L5、L6 的 `ExactSessions` 属 (b)，结果逐字保留；L7 属 (a)。§2.0 检索式中 `reroute`、`RetryLimit` 在 Protocol 中为 0（`logs/task-21-sites.txt`「after」）。
2. **行为**：N11～N14 在执行器上（`TestGameplay`）运行，装配首领塔协调器且它答「不归我管」（没有塔局），断言：
   - 离队、换队长、踢人：队伍命令恰好执行一次（`PartyCalls == 1`），队伍状态按请求改变，队伍通知送达（`Projected` 非空），踢人不回拒绝包；
   - 放弃副本：`GiveUpGameResponse` 恰好一条且排在城镇投影之前，没有队伍命令（没有局）。
   已有用例 `LeaveDungeonTests.GiveUp_sends_weakness_once_after_success_and_before_town`、`PartyCommandHandlerTests.Party_handlers_dispatch_the_six_party_write_commands` 也覆盖普通路径，但没有装配两塔协调器；`Leaving_party_in_a_run_detaches_only_the_departing_character`、`Removing_a_member_in_a_run_detaches_only_the_removed_character` 测的是命令处理器，不经过消息 handler。
3. **区分力**：变异 S6（放弃副本在回包前返回）使 N14 与两条已有放弃副本用例变红；S7（离队在 `RunPartyAsync` 前返回）使 N11 与 `Party_handlers_dispatch_the_six_party_write_commands` 变红（`logs/task-21-mutations.txt`）。

### 7. 交 TASK-22

空转用例（V）与只是名字里提到门的用例，逐条见 `logs/task-21-vacuous.txt`：

| 行 | 用例 | 建议 |
|---|---|---|
| V1 | `OnlineUserServiceTests.DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate` | 删除持门的部分，或整条删除（断言由 `DisconnectSelectedSession_RemovesConnectionAfterExactCleanupAdvancesGeneration` 承接） |
| V2 | `SelectCharacterLifecycleTests.CrossConnectionDisplacement_CleansCurrentOwnerBeforeRegisteringReplacement` | 去掉关连接回调里的取门与 `Fixture.TransitionGate`，清理次序的断言保留 |
| V3、V4 | 两塔 `Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release` | `ReentrantCloser` 换成普通 `Closer`，隔离断言保留 |
| V5、V6 | `PartyCommandHandlerTests.Successful_command_notifications_are_delivered_after_both_transition_gates_release`、`Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases` | `GateReentryNotificationBus` 换成计数总线，发布次数断言保留 |
| V7 | `LeaveDungeonTests.Give_up_completes_through_real_command_locks_and_releases_the_session`（isAltar=false 一行空转） | 随门类型删除最后的取门 |

另有仍经管线 45 号行为取门、本单元之后**不空转**的门泄漏断言（`ReplayStateSnapshotTests` 的 `TrackedEntryCount`）与测试自己使用 Application 门的用例（`LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline`、`CommonExperienceEffectTests` 两条、`JoustQueryHandlerTests`、`OnlineIdentityTests`），随 TASK-22 删除门类型时处置。

## TASK-22

基准提交 `09e4983c`（TASK-21 已提交）。机械核对：`logs/task-22-diff-check.txt`（`diffcheck7.py 09e4983c --lines` 与 `methods.py`）。

### 1. 数量

| 项目 | 基准 | 新增 N | 删除 D | 结果 |
|---|---:|---:|---:|---:|
| Application.Tests | 4582 | 9 | 45 | 4546 |
| Protocol.Tests | 1699 | 0 | 0 | 1699 |
| 其余六个项目 | 1947 | 0 | 0 | 1947 |
| 合计 | 8228 | 9 | 45 | 8192 |

- 其余六个项目：HttpApi 54 ＋ Pvf 80 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 175 ＋ Host 191 ＝ 1947。
- 合计：8228 ＋ 9 － 45 ＝ 8192；4546 ＋ 1699 ＋ 1947 ＝ 8192。
- 与计划 §6.6 的差异：Protocol 的 D 预期 1（N-V3），实际 0，见 §3 的 G-N3；Application 的 N 预期 6～8，实际 9（45 号行为补了 5 条，见 §5）。
- 改动的测试文件 49 个：删除 4 个、修改 43 个（Application 22、Protocol 19、Persistence 1、Host 1）、新建 2 个（`Commands/SelectedSessionExecutionBehaviorTests.cs`、`Execution/ViolationCapture.cs`）；4 ＋ 43 ＋ 2 ＝ 49。

### 2. 删除（D ＝ 45，全在 Application.Tests）

| 行 | 用例 | 例数 | 类 | 承接／消除 |
|---|---|---:|---|---|
| D1 | `GameplayGateAssertionTests` 的 5 个 Theory（`Taking_a_gate_off_the_executor_throws_and_reports_it`、`On_the_executor_an_uncontended_gate_is_taken_and_released`、`On_the_executor_a_contended_gate_throws_at_once_and_fails_the_job_even_if_swallowed`、`A_wait_violation_leaks_nothing_and_the_gate_opens_once_its_holder_releases`、`A_cancelled_caller_gets_cancellation_and_no_violation`，各 5 行） | 25 | K | 规格消除：五把门（在线用户、账号、生命周期、事务准入、生命周期借用）的类型已删除，判据 J1 守着不再出现；「执行器上不等门」由 RQ-001 单消费者与 TASK-17～22 的零违规全量承接；`GameplayGateAssertion` 机制本身仍由 `ProtocolGateAssertionTests` 的 4 条队伍用例覆盖（T-006） |
| D2 | `GameplayGateAssertionTests` 的 8 个 Fact：`Borrowing_inside_the_job_that_holds_the_lease_is_not_a_violation`、`A_borrow_frame_carried_off_the_executor_is_refused`、`Acquiring_many_contended_on_the_second_account_throws_and_releases_the_first`、`The_effect_ledger_gate_cannot_be_taken_off_the_executor`、`A_contended_effect_ledger_gate_fails_the_job_at_once`、`The_joust_tick_gate_cannot_be_taken_off_the_executor`、`A_contended_joust_tick_gate_fails_the_job_at_once`、`Taking_a_scheduled_task_slot_on_the_executor_is_a_self_wait` | 8 | K | 同上：借用帧、账本信号量、Joust `ticks`、调度并发槽全部删除（J1、J2）；最后一条是 F-17 并发槽的反向断言，随槽删除；组门的反向断言 `DomainEventDispatchTests.Draining_from_inside_a_job_fails_at_once_as_a_program_error` 保留 |
| D3 | `OnlineUserExecutionGateTests.DifferentOnlineUsersDoNotBlockEachOther` | 1 | K | 规格消除（门删除） |
| D4 | `SelectedSessionTransitionGateTests.HeldSharedExplicitlyRefusesExclusiveUpgradeWithoutLeakingOrChangingSharedBorrow`、`DifferentAccountAndGateDoNotBorrowAndCancelledBorrowCannotGainRights` | 2 | K | 规格消除（门与借用帧删除）；升级拒绝在生产中不可达（`logs/task-21-upgrade.txt`） |
| D5 | `MultiAccountExecutionTests.All_accounts_are_locked_once_and_released_when_execution_fails`、`A_second_account_held_by_an_earlier_job_fails_at_once_and_releases_the_first`、`A_repeated_account_is_taken_once_and_the_group_runs` | 3 | K | 规格消除（`IMultiAccountCommand` 与 55 号行为删除）；「失败之后下一条照常执行」由 `GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs` 承接 |
| D6 | `CommandBusTests.ExclusiveAccountLease_IsReleasedWhenHandlerThrows`、`SharedOnlineUserLease_IsReleasedWhenHandlerThrows`、`ExclusiveOnlineUserLease_IsReleasedWhenHandlerIsCancelled` | 3 | K | F-67、`task-07-conflicts.md` §15.1：承接者 `GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs` |
| D7 | `CommandBusTests.OnlineUserScopedRegistration_RequiresAnOnlineUserCommand`、`AccountScopedRegistration_RequiresAnAccountCommand` | 2 | K | 规格消除：访问方式不存在了，「注册了访问方式却没实现接口」无从发生 |
| D8 | `ScheduledTaskTests.Invalid_configuration_fails_before_callbacks` 的 `parallelism` 一行 | 1 | K | 规格消除：并发上限删除；其余 4 行不变（`Running(jobs)` 去掉并发实参） |

加法：25 ＋ 8 ＋ 1 ＋ 2 ＋ 3 ＋ 3 ＋ 2 ＋ 1 ＝ 45。方法集合核对：少了 24 个方法（5 个 Theory ＋ 19 个 Fact），与上表逐一对应；D8 是数据行，方法仍在。

`GameplayGateAssertionTests.cs` 中的测试支持类 `ViolationCapture`（`CommandBusExecutorTests`、`GameplayWriteAssertionTests` 在用）原样移到新文件 `Execution/ViolationCapture.cs`，类体逐字未改。

### 3. 改写（删掉门相关的语句或断言，业务断言保留；方法名不改，F-87）

| 行 | 用例 | 改动 | 保护意图 |
|---|---|---|---|
| G-B1 | `CommandBusTests.EveryProductionBehaviourResolvesForACommandResultType` | 期望的行为列表删去 50、55、57 三项（多行 `Assert.Equal`，断言行多重集合不变） | 每个生产行为都能按结果类型解析；列表就是今天的管线 0、45、58、60、100 |
| G-U1 | `UnitOfWorkBehaviorTests.UnitOfWork_OpensInsideTheAccountAndTransactionalGatesAndOutsideTheExecutionPolicy` | 三条涉及 55／57 的次序断言 → 45 ＜ 58、58 ＜ 60、45 ＜ 60 三条；60 ＜ 100 不变；摘要注释改写（多行 `Assert.True`，断言行多重集合不变） | 结算行为包住事务、事务包住执行；「事务在账号租约之内」随 55、57 消除 |
| G-R1 | `CommandRegistrationTests.GameTicketRedemptionIsTransactionalAndOnlineUserExclusive` | 删两条访问方式断言，执行方式断言保留 | 兑换命令是事务命令 |
| G-R2 | `PartyCommandCodecTests`（队伍读命令的注册断言） | 删两条访问方式断言，执行方式断言保留 | 这些命令是 Direct |
| G-E1 | `DomainEventDispatchTests.Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released` | 删测试里持准入门的那一层；提交、`Count == 0`、组循环随后消费、`Count == 3` 不变 | 发布事务的提交不等待消费；消费在发布作业结束之后发生 |
| G-E2 | `DomainEventDispatchTests.A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off`（N5，主控方裁定第 1 条） | 改由 `Dispatcher.RunAsync` 的组循环驱动，每次提交唤醒它；推进冻结时钟后再发布四条事实；`DrainOnceAsync` 返回值的三条断言（1、1、`drains.Sum() == 5`）改为「等到状态达成」（退避中 1 条；计数 6；计数 14 且无待处理）；`Count == 6`、`attempt_count = 1`、`Count == 14`、待处理 0、`MaximumOutstanding == 1` 不变 | AC-015「订阅组内顺序与失败退避后的继续处理」；组内串行由一个排空循环保证（变异 S12） |
| G-E3 | `DomainEventDispatchTests.Draining_from_inside_a_job_fails_at_once_as_a_program_error` | 只改一行注释（不再说循环持着组门）；代码与断言不变 | — |
| G-S1 | `ScheduledTaskTests.Global_limit_queues_one_instance_and_coalesces_waiting_runs`（主控方裁定第 2 条） | 去掉 `Running(…, 1)` 的并发实参；断言不变 | 排队中的 RunOnce 运行按最新时刻合并为一次；同一时刻只有一个回调在执行（变异 S11） |
| G-S2 | `ScheduledTaskTests.Retry_delay_does_not_hold_the_global_execution_slot` | 去掉并发实参；断言不变 | 重试退避不占执行器，别的任务照常执行（变异 S10）；另有承接者 `ScheduledTaskExecutorTests.Each_attempt_is_its_own_timer_job_and_the_retry_delay_leaves_the_executor_free` |
| G-S3 | `ScheduledTaskTests.Factory_uses_configured_singletons_and_explicit_options_override_defaults` | 删「显式选项覆盖默认值」一半（`AddTaskScheduling(new(1))` 与其断言），工厂与单例一半保留 | 工厂注册的任务用配置的单例 |
| G-S4 | `ScheduledTaskTests.Running` 夹具 | 删 `concurrency` 参数 | 编译迫使 |
| G-N1、G-N2 | `ReplayStateSnapshotTests` 三条（TASK-21 N-V1、N-V2） | 删 `TrackedEntryCount` 断言 5 处与 `gate` 变量；`exclusiveOwner` 借用外壳删除，里面的 `RefreshAsync` 与批次数断言（2、3）保留 | 「门被释放」随门删除；精确连接、批次内容、新旧库存、错误连接被拒不变 |
| G-N3 | `LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline`（TASK-21 N-V3） | **改写而不是删除**（与计划 §6.2 不同）：删测试自己持的 `AcquireManyExclusiveAsync` 两行与访问方式实参；经真实管线执行 `LeaveDungeonCommand`、`EndedCharacters` 与 `runs.Ended` 两条断言保留 | 借用帧的意图随门由规格消除（停止条件 6 不触发）；「离开命令经真实管线结束副本」在测试中没有别的用例（检索 `LeaveDungeonCommand` 的测试使用只有这一条经管线），所以保留用例 |
| G-N4 | `CommonExperienceEffectTests.Effect_uow_borrows_outer_exclusive_and_keeps_teardown_waiting_until_settlement`（N-V4） | 删外层 `AcquireAsync` 与结尾的再取门、`lifecycle` 变量；经验 30 的两条断言保留 | 结算与拆除不交错由作业边界保证；结算结果不变 |
| G-N5 | `CommonExperienceEffectTests.Logger_failure_does_not_replace_unknown_cause_and_exact_close_callback_waits_for_lease_release`（N-V4） | 删结尾的取门与 `CallbackCloser.Lifecycle`；其余断言保留 | 日志失败不替换原因、精确关连接、连接被封锁 |
| V1 | `OnlineUserServiceTests.DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate` | 删持门的准备（门变量与 `blocker`）；「在线用户被移除」断言保留 | 没有选中会话的断线移除在线用户 |
| V2 | `SelectCharacterLifecycleTests.CrossConnectionDisplacement_CleansCurrentOwnerBeforeRegisteringReplacement` | 删关连接回调（只取门）与 `Fixture.TransitionGate`；清理次序的四条断言保留 | 跨连接顶替先清理当前所有者再登记替代者 |
| V3、V4 | 两塔 `Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release` | `ReentrantCloser` 换成文件里已有的普通 `Closer`（两者记录行为相同），`ReentrantCloser` 类删除；隔离断言保留 | 队伍包装失败时隔离双方会话 |
| V5、V6 | `PartyCommandHandlerTests.Successful_command_notifications_are_delivered_after_both_transition_gates_release`、`Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases` | `GateReentryNotificationBus` 换成 `CountingNotificationBus`（只计数）；发布次数断言（1、3）保留；一段注释改写 | 成功命令的通知按计划次数送出 |
| V7 | `LeaveDungeonTests.Give_up_completes_through_real_command_locks_and_releases_the_session`（两行 Theory） | 删 `transitions` 变量、注册与结尾的取门，删访问方式实参；业务断言保留 | 放弃副本经真实命令完成 |

### 4. 编译迫使（E，只删门的实参、参数、声明、注册或 `using`，断言多重集合不变）

| 文件 | 改动 |
|---|---|
| 24 个文件 | 删 `using Dfo.Server.Application.OnlineUsers;`（命名空间已空）：Application 6（`GameLoginTests`、`SelectCharacterLifecycleTests`、`OnlineExpiredItemCleanupTests`、`OnlineUserServiceTests`、`OnlineIdentityTests`、`ReplayStateSnapshotTests`）、Persistence 1（`LoginSessionQueuedCommandTests`）、Protocol 17；6 ＋ 1 ＋ 17 ＝ 24 |
| 11 个文件 | 删访问方式实参共 16 处：`GameLoginTests` 1、`SelectCharacterLifecycleTests` 1、`SelectCharacterReturnTests` 1、`MarriageCeremonyHandlerTests` 2、`MarriageCeremonyTests` 1、`MarriageChurchTests` 1、`MarriageProposalTests` 1、`EffectOperationReplayTests` 3、`GmEventsHttpTests` 2、`LoginSessionQueuedCommandTests` 1、`LeaveDungeonTests` 2；1 ＋ 1 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＋ 1 ＋ 3 ＋ 2 ＋ 1 ＋ 2 ＝ 16 |
| `Application.Tests/Game/MoveItemSpaceHandlerTests.cs` | 测试自建的 `TransactionalExecutionGate`、`Dispatch` 的门参数与取门删除 |
| `Application.Tests/Sessions/OnlineIdentityTests.cs` | `SelectedSessionSafety` 构造去掉门 |
| `Application.Tests/World/DungeonVisitDomainServiceTests.cs` | `TransactionalDungeonVisitDomainService` 构造去掉门 |
| `Application.Tests/Execution/GameplayWriteAssertionTests.cs` | 三处 `new TransactionalExecutionGate()` 与两塔构造实参删除；一行注释改写 |
| `Application.Tests/DomainEvents/DomainEventDispatchTests.cs` | 夹具 `SendAsync` 删准入门一行（另见 G-E1） |
| `Protocol.Tests/Game/Joust/JoustQueryHandlerTests.cs` | 门变量、注册与 `SelectedSessionSafety` 构造实参删除 |
| `Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs` | 门注册删除 |

### 5. 新增（N ＝ 9）

| 行 | 用例 | 内容 |
|---|---|---|
| N1 | `LockRemovalSourceTests.No_other_waiting_primitive_is_used_in_the_production_sources`（J3） | 生产源码中其他等待原语为 0；输出每个名称的命中 |
| N2 | `LockRemovalSourceTests.A_pipeline_behaviour_name_is_named_outside_application_and_a_longer_identifier_is_not`（C8） | 合成字典：`AccountExecutionBehavior` 在 `Dfo.Server.Host/…` 被点名，`IMultiAccountCommandShim` 不点名 |
| N3 | `LockRemovalSourceTests.Another_waiting_primitive_is_named_in_any_file`（C9） | 合成字典：`ReaderWriterLockSlim` 被点名，`SemaphoreSlim` 与短 `lock` 不被这一支点名 |
| N4 | `DomainEventDispatchTests.Concurrent_drains_of_one_group_consume_each_delivery_once_and_never_retry_a_backed_off_one_early`（主控方裁定第 1 条） | 执行器被占住时两个 `DrainOnceAsync("counter")` 读到同一条到期事实：先执行的失败并退避，后一个的 `TryBeginAsync` 拿不到它，订阅者只被调用 1 次；再发三条，执行器被占住时两个测试线程排空加运行中的组循环一起读：`Complete` 3 次、`Fail` 1 次、`TryBegin` 次数 ＞ 4（确有竞争）、订阅者共 4 次、计数 3、退避中的那条仍待处理。组门删除后 AC-015 与 Host 两个文件所依赖的就是这条性质 |
| N5 | `SelectedSessionExecutionBehaviorTests.A_command_whose_account_differs_from_its_session_never_reaches_its_handler` | 附录 H 第 45 行 H4（变异 S6） |
| N6 | `SelectedSessionExecutionBehaviorTests.A_participant_set_without_the_reporting_session_never_reaches_its_handler` | H5（变异 S4） |
| N7 | `SelectedSessionExecutionBehaviorTests.A_participant_that_is_no_longer_current_stops_the_command_before_its_handler` | H6（变异 S7） |
| N8 | `SelectedSessionExecutionBehaviorTests.An_uncertain_outcome_fences_every_participant_not_only_the_reporter` | H7：结果不确定时报告者与另一参与者都被封锁；58 号只封锁报告者，第二个参与者只有 45 号的逐个封锁会封（变异 S5） |
| N9 | `SelectedSessionExecutionBehaviorTests.The_handler_runs_with_the_captured_recipient_as_its_selected_session_scope` | H8：处理器执行时 `SelectedSessionCommandScope.Current` 是捕获的接收者，之后恢复为空（变异 S8） |

3 ＋ 1 ＋ 5 ＝ 9。

- N5～N9：计划 §6.5 预计 H4、H5 各一例、H6／H8 视变异结果。变异 S4～S8 第 1 轮（只有已有用例）在 Application 与 Protocol 全部用例下**一条都没有变红**（S8 第 1 轮变异体编译不过，第 2 轮改写），包括计划点名给 H7 的 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`，所以五项各补一例。补上之后 S4～S8 各使对应的一条变红（`logs/task-22-mutations.txt`）。
- 这五条钉的是本单元原样保留的语句，在本单元代码上写出即通过；区分力由变异证明，不是 TDD 的 RED。
- N1～N3：判据扩到全局后 J1、J2 先 RED（`logs/task-22-lock-scan.txt`），J3 在基准上即为 0；区分力由变异 S1～S3 证明。

### 6. 保留不改

- `TransactionalCommandSerializationTests` 两例（TASK-18 变异 X1 已证明不依赖门），本单元全量通过。
- F-68 的跨容器 SQLite 竞争用例（`MailboxCoreIntegrationTests.ConcurrentClaim…`、`QuestHelperPuppetTests` 两条、`SaoTowerStoreTests.Two_database_sessions…`）：测 SQLite 写保留与提交冲突，不经门，`AcquireWriteAsync` 属附录 F「保留」；本单元全量通过。
- `ProtocolGateAssertionTests` 4 条、`PartyTownProjectionDeliveryTests` 2 条（T-006）。
- 用例名里仍提到门、租约或「门释放之后」的（G-U1、G-N3～G-N5、V1、V3～V7、G-S2、G-S3、G-R1）一律不改名，并入 F-87（改名随 T-008）。

### 7. 评审后（L1，TASK-22 主控方处置第 1 条）

| 行 | 用例 | 处置 |
|---|---|---|
| N10 | `LockRemovalSourceTests.A_listed_file_with_one_more_semaphore_than_pinned_is_named` | 新增：名单上的文件比钉住的处数多一处时被点名 |
| N11 | `LockRemovalSourceTests.Wait_handles_spin_waits_and_explicit_lock_entry_are_named_and_a_lock_statement_is_not` | 新增：`EventWaitHandle`、`WaitHandle`、`SpinWait`、`Lock` 的 `Enter()`／`EnterScope()`／`TryEnter(` 被点名，`lock` 语句与有参的 `Enter(…)` 不点名 |
| G-L1 | `LockRemovalSourceTests.A_semaphore_outside_the_list_is_named`、`A_listed_file_without_a_semaphore_is_a_stale_entry` | 合成名单改用按处数生成的 `Listed()`，断言不变 |

两条新反例先 RED（判据未改时失败）后 GREEN（`logs/task-22-review-fixes.txt`）。Application.Tests 由 4546 变为 4546 ＋ 2 ＝ 4548；本单元合计 N ＝ 9 ＋ 2 ＝ 11，D ＝ 45。

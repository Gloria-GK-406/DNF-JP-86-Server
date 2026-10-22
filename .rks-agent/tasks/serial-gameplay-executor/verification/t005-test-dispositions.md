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

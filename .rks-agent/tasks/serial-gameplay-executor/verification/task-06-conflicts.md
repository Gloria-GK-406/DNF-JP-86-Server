# TASK-06 冲突用例清单（Persistence / Host / Networking / Protocol）

- 日期 2026-10-22，基准 HEAD `0223e646`。行号是 HEAD 上的方法声明行。
- 范围：TASK-06 §2.5，只收本单元四个项目。Application.Tests 的部分与合并总表由 TASK-07 完成。
- 本单元**只做决定，不执行**：下列用例在工作区保持原样，没有包进 `TestGameplay`（§0）。
- 执行单元的分派规则（§0）：
  - 经入口驱动、靠交错制造时序的用例，归 T-004b；
  - 直接构造或取用门、在门上制造争用的用例，归 T-004c；
  - K5 投递语义归 T-006。T-004b 加 2 秒直发期限时要核对 K5 仍然通过。
- 「改写」指把交错改为由执行器驱动的顺序断言，或在无法改成顺序时删除并写明理由，具体由执行单元决定。

## K2 登录竞态与排队（Persistence）

| 文件:方法 | 冲突机制 | 处置决定 | 执行单元 | 理由 |
|---|---|---|---|---|
| `Persistence.Tests/Accounts/AuthenticationConcurrencyTests.cs:10` `OverlappingPasswordReadsCannotLeaveTheSecondLoginWithAnUnwritableSqliteSnapshot` | 经总线并发发出两条登录。口令替身用 `ManualResetEventSlim` 卡住第一条，等第二条读到快照后才放行；第二条最长等 40 秒 | 改写：口令哈希移出执行器之后，「两条登录同时持有 SQLite 快照」这一前提不再成立，按新的先哈希、后单条 CMD 结构重写，或删除并写明理由 | T-004b（迁口令哈希、RQ-007 时）；若拖到 T-004c 翻转才失败，由 T-004c 执行 | 经入口驱动，靠交错制造时序 |
| `Persistence.Tests/Accounts/LoginSessionQueuedCommandTests.cs:14` `ReplacedLoginCannotWriteAfterWaitingForTheAccountGateEvenAfterDisconnectCallback` | 测试直接取 `AccountExecutionGate` 独占租约，把 `QueuedWrite` 经总线排在门后；同时用 `SuppressFlow`＋`Task.Run` 发顶替登录，再回调断连 | 改写：把「排在门后的写」改成执行器上的顺序 CMD（顶替登录 → 断连 → 旧会话写被拒） | T-004c | 测试在执行器外取门并制造门上排队；门断言与 `SendAsync` 翻转会同时让它失败 |
| `Persistence.Tests/Accounts/AuthenticationConsumptionTests.cs:8` `ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected`（**新增，计划初始清单没有**） | 测试在执行器外开写事务并绑定票据，在事务未提交时，用 `SuppressFlow`＋`Task.Run` 让另一条 `fixture.Consume` 并发绑定同一票据，断言被拒 | 改写或删除：串行执行器下两个未提交绑定不会重叠，改成「先提交、再重复绑定」的顺序断言，或删除并写明理由 | T-004c | 依赖两个写事务真并发。外层写事务若放进作业，与它内部再入队的 `Consume` 会互相等待（T-004b 注册容器执行器之后）；不放进作业，T-004c 的 RQ-061 判据又会拒绝执行器外的写事务开启 |

同组中确认**不是**冲突的：

- `AuthenticationRegistrationTests.RegistrationPersistsDistinctModernRecordsAndOnlyOneConcurrentDuplicateWins`：6 条注册都经 `bus.SendAsync` 发出（§2.3「发起方是总线的，不动」）。T-004c 翻转后它们自然串行，「只有一条成功」这一不变量照样成立。

## K4 只有部分方法依赖门排队或交错（Persistence）

| 文件 | 结论 |
|---|---|
| `Persistence.Tests/Characters/SqliteEntryLifecycleRecoveryTests.cs` | 不冲突。`WhenAll(AttemptAsync, AttemptAsync)` 的两个发起方各自经 `CharacterSide.InUseCaseAsync`，每次调用都由夹具单独包成作业，符合 §2.3。夹具不带容器，所以这两个作业永远落在各自新建的执行器上，并发保留；断言本来就容忍其中一条被 SQLite 拒绝。 |
| `Persistence.Tests/Inventory/SqliteCharacterCarriedGoldTableTests.cs` | 不在迁移范围，也不冲突。`Additions_made_at_the_same_moment_all_reach_the_figure` 直接并发调用 SQLite 表网关，不经门，不开写事务，也不碰附录 F 类型。 |

## K3 用 TCS 或门制造交错（Protocol）

| 文件:方法 | 冲突机制 | 处置决定 | 执行单元 | 理由 |
|---|---|---|---|---|
| `Protocol.Tests/Game/Party/PartyTownProjectionDeliveryTests.cs:134` `ReadRepairWaitsForTheMutationGateAndRequeriesCurrentPartyState` | 测试先占住 `PartyProjectionStreamRegistry` 的流门（`stream.Gate.WaitAsync`），直接调用的读修复投递在门后挂起；改状态后再放门 | 改写为顺序断言，或随流门删除（附录 F）一并删除 | T-004c | 直接调用 Delivery，并在门上制造争用 |
| `…/PartyTownProjectionDeliveryTests.cs:188` `NewGenerationMutationCannotPassABlockedOldGenerationReadRepair` | `BlockingFirstRosterNotificationBus` 用 TCS 卡住旧代读修复；新代变更投递必须排在其后 | 同上 | T-004c | 两个直接调用的投递真并发交错 |
| `Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs:164` `AreaCrossingWaitsForTheSameAccountPartyTransitionGate` | 测试取 `SelectedSessionTransitionGate` 独占租约，直接调用的 `SetUserAreaHandler` 在门后挂起，放门后才推进 | 改写为顺序断言，或删除 | T-004c | 测试在执行器外取门并制造争用 |
| `Protocol.Tests/Game/Dungeons/DungeonLoadingProjectionDeliveryTests.cs:78` `Replacement_waits_until_the_ready_command_and_result_validation_are_complete` | 加载投递被 TCS 卡在 ready 命令里（持过渡门）；`Task.Run` 的顶替方取同一门，必须等待 | 改写或删除 | T-004c | 直接调用 Delivery，并在门上与第二个发起方争用 |
| `…/DungeonLoadingProjectionDeliveryTests.cs:107` `Replacement_also_waits_until_the_loading_complete_frame_is_delivered` | 同上，卡点在加载完成帧的投递回调 | 改写或删除 | T-004c | 同上 |
| `Protocol.Tests/Game/BossTower/BossTowerProjectionConcurrencyTests.cs:292` `A_third_member_disconnect_during_capture_revalidates_and_completes` | 测试为第三名成员取过渡门租约，协调器的转移在门后挂起；挂起期间测试让该成员离塔、离队、释放会话 | 改写或删除 | T-004c | 测试在执行器外取门并制造争用 |
| `Protocol.Tests/Game/IllusionTower/IllusionTowerProjectionConcurrencyTests.cs:291` `A_third_member_disconnect_during_capture_revalidates_and_completes` | 同上（幻影塔） | 改写或删除 | T-004c | 同上 |
| `Protocol.Tests/Game/CardReward/DungeonCardPresentationCoordinatorTests.cs:109` `Simultaneous_party_auto_flips_retry_a_contended_column_and_award_each_member_once` | 两名成员的自动翻牌计时器同时到期，争用同一列后重试 | 改写：计时器迁入执行器后，两个到期 CMD 串行，重写「争用后重试」的取证方式 | T-004b（迁卡牌计时器时） | 计时器入口驱动，靠交错制造时序 |
| `…/DungeonCardPresentationCoordinatorTests.cs:235` `Auto_flip_retries_the_same_personal_confirmation_after_a_transient_conflict` | 自动翻牌计时器到期，假总线首次返回冲突后重试 | 同上 | T-004b | 同上 |

计划初始清单写「`BossTowerProjectionConcurrencyTests`(11)、`IllusionTowerProjectionConcurrencyTests`(11)」。逐个阅读后，两个文件各 11 个方法中只有上面这 1 个在测试里取门、制造争用。
- 其余 10 个都是顺序调用：直接调用协调器或投递，配真实 `PartyService`。
- 其中 `Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release` 用的 `ReentrantCloser` 会在关连接回调里再取一次门，但那发生在协调器放门之后，不构成争用。
- 这 10 个已经整体包裹。

## K4 只有部分方法依赖门排队或交错（Protocol）

| 文件:方法 | 冲突机制 | 处置决定 | 执行单元 | 理由 |
|---|---|---|---|---|
| `Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs:313` `Set_party_info_rechecks_the_exact_session_after_acquiring_its_gate` | 测试取门（`blocker`），handler 在门后挂起；挂起期间测试替换会话 | 改写或删除 | T-004c | 测试在执行器外取门并制造争用 |
| `…/PartyCommandHandlerTests.cs:466` `A_target_replaced_before_its_gate_is_acquired_is_not_dispatched` | 同上，挂起期间替换目标 | 同上 | T-004c | 同上 |
| `…/PartyCommandHandlerTests.cs:507` `A_higher_account_inviter_replaced_while_waiting_for_the_lower_gate_is_not_dispatched` | 测试同时持低号、邀请方两把门 | 同上 | T-004c | 同上 |
| `…/PartyCommandHandlerTests.cs:553` `Reverse_direction_pair_requests_complete_under_one_stable_gate_order` | 测试持门，两个反向请求 `Task.WhenAll` 并发，验证锁序不死锁 | 改写或删除：串行执行器下不存在反向锁序 | T-004c | 同上 |
| `Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs:147` `Exit_owns_the_transition_until_its_last_town_frame_is_sent` | 发送被 TCS 卡住时，测试另取同一过渡门，必须等待 | 改写或删除 | T-004c | 同上 |
| `…/LeaveDungeonTests.cs:203` `An_exit_queued_for_an_old_run_does_not_end_its_replacement` | 测试持门，旧的退出在门后排队，挂起期间替换副本 | 同上 | T-004c | 同上 |
| `…/LeaveDungeonTests.cs:661` `A_return_waiting_on_the_transition_gate_cannot_overwrite_a_replacement` | 测试持门，返回城镇在门后排队，挂起期间替换会话 | 同上 | T-004c | 同上 |
| `…/LeaveDungeonTests.cs:242` `Exit_command_borrows_the_projection_lease_through_the_real_pipeline`（**新增，计划初始清单写「(3)」**） | 测试从容器取过渡门并 `AcquireManyExclusiveAsync`，在持门状态下经真实总线发 `LeaveDungeonCommand`，验证命令「借用」已持有的租约 | 改写：门删除前改为在执行器作业内持门再发命令；或随门的借用机制一并删除 | T-004c | 「先持门再发命令」（过渡期规则 1 点名的写法）；`SendAsync` 翻转后命令进入执行器，而门在执行器外持有 |
| `Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:54` `Entry_transaction_cannot_reach_the_visit_until_exit_projection_releases_its_lease` | 测试持门，用 `Task.Run` 经真实总线发入场命令，断言命令在门后等待 | 改写或删除 | T-004c | 测试在执行器外持门并与总线命令争用 |
| `Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs:66` `Selection_waits_for_exit_before_recording_intent_or_sending_frames` | 测试持 `exitLease`，选择 handler 在门后挂起 | 改写或删除 | T-004c | 同上 |
| `…/PartySelectDungeonHandlerTests.cs:547` `Cancelling_while_acquiring_commit_gates_rolls_back_before_leaving_town` | 测试持门，handler 在取提交门时挂起，随后取消 | 同上 | T-004c | 同上 |
| `Protocol.Tests/Game/StateSynchronization/TitleBookProtocolTests.cs:73` `Mutation_projection_waits_for_ack_and_never_turns_storage_into_an_unlock` | 测试先进入 `AchievementTitleProjectionGate`，直接调用的事件订阅者在门后挂起 | 改写或删除 | T-004c | 测试取门并制造争用（订阅者在生产中由事件分发器驱动） |
| `…/TitleBookProtocolTests.cs:88` `Mirror_removal_projection_keeps_generic_move_reconciliation_and_can_skip_carried_inventory_for_book_operations` | 同上 | 同上 | T-004c | 同上 |
| `…/TitleBookProtocolTests.cs:153` `Achievement_progress_projection_waits_until_request_ack_releases_the_character_gate` | 同上（成就订阅者） | 同上 | T-004c | 同上 |
| `Protocol.Tests/Game/StateSynchronization/TitleBookMutationHandlerTests.cs:27` `Worn_requests_reply_with_the_native_equipment_coordinates_and_complete_ack_before_projection` | handler 卡在发送替身的 TCS 上（持投影门），测试断言此时投影门取不到，放行后才取到 | 改写或删除 | T-004c | 测试与直接调用的 handler 在门上争用 |
| `Protocol.Tests/MarriageProposalCodecTests.cs:149` `Two_party_delivery_is_ordered_through_peer_notification_and_command_can_borrow_fences` | TCS 卡住第一方投递，第二方并发；`Task.WhenAll` 等待两者 | 改写或删除 | T-004c | 两个直接调用的发起方真并发交错 |
| `Protocol.Tests/Game/Party/PartyNotificationTests.cs:791` `Revisions_of_one_party_generation_are_delivered_as_contiguous_phases`（**新增，fork B 阅读时发现**） | 两个 `PartyNotificationDelivery` 共用流注册表；`BlockingNotificationBus` 用 TCS 卡住旧修订，断言新修订在其释放前未完成 | 改写或删除 | T-004c | 两个直接调用的投递真并发交错 |

逐个阅读后与初始清单的出入：

| 用例 | 初始清单 | 实际 | 说明 |
|---|---|---|---|
| `PartyCommandHandlerTests` | 5 | 4 | `Successful_command_notifications_are_delivered_after_both_transition_gates_release` 与 `Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases` 的通知总线替身会在 handler 放门**之后**再取同一门，不构成争用，已整体包裹 |
| `LeaveDungeonTests` | 3 | 4 | 多出 `Exit_command_borrows_the_projection_lease_through_the_real_pipeline`，见上表 |
| `TitleBookProtocolTests` | 2 | 3 | 三个订阅者用例的形状完全相同 |
| `TownAreaProjectionDeliveryTests` | 在列 | 0 | 8 个方法都是顺序调用，门在场但不争用，已整体包裹 |
| `SelectDungeonHandlerTests`、`PartySelectDungeonHandlerTests`、`TitleBookMutationHandlerTests`、`MarriageProposalCodecTests` | 在列，未给数 | 1、2、1、1 | 见上表 |

## K5 投递期限与重试（不包裹，执行单元 T-006，T-004b 引入 2 秒直发期限时核对）

| 文件 | 方法 | 机制 |
|---|---|---|
| `Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs` | 全部 6 个：`The_owner_is_told_before_the_people_around_them`、`Existing_owner_and_town_notices_finish_before_party_projection`、`Each_half_becomes_the_frames_it_always_did`、`The_peers_notices_become_the_frames_they_always_did`、`A_notice_kind_nobody_mapped_is_named`、`An_empty_half_is_not_delivered` | 清理通告「本人先于周围的人」的直接发送与人群投影顺序，T-006 改为记录发件箱条目 |
| `Networking.Tests/Runtime/PreparedBatchTests.cs`（初始清单写在 Protocol 下，实际在 Networking） | 5 个：`A_late_registration_endpoint_or_actual_frame_limit_failure_sends_nothing` 等 | 预备批次的编码、帧上限与部分发送失败语义 |
| `Networking.Tests/Integration/TouchSocketNetworkMessageSenderExplicitDeliveryTests.cs` | 3 个 | 显式收件人投递：必达顺序、逐目标失败隔离 |
| `Networking.Tests/Integration/HandlerFailureConnectionIntegrationTests.cs` | 2 个 | handler 抛出后关连接、其余连接照常服务（真实 socket） |
| `Networking.Tests/Runtime/ExactConnectionCloseTests.cs` | 2 个 | 精确上下文关连接与调度关闭 |
| `Networking.Tests/Integration/OutboundSendFailureTests.cs` | 4 个 | 端点不符、无编码器、登录失败体不可编码等发送拒绝 |

这几个文件本单元都没有改动：它们要么是真实传输入口，要么直接测发送器，都不经过 T1～T5。

## 不是冲突、但依赖 T-004b 迁入入口的用例（交给 T-004b 对账）

这些用例是 §2.4 的入口用例：入口调用本身没有包，测试里也不取门。T-004c 打开门断言后，若对应入口尚未迁入执行器，它们会变红。

| 文件:方法 | 入口 | 说明 |
|---|---|---|
| `Protocol.Tests/Game/ItemUsageProjectionTests.cs:49` `Daily_worker_only_refreshes_changed_business_day_and_does_not_deliver_to_replaced_session` | `ItemUsageDailyRefreshService.RefreshAsync`（后台每日刷新的 tick） | worker 内部取测试新建的 `SelectedSessionTransitionGate`。只包了最后一行直接投递 |
| `Protocol.Tests/Game/UsageQuotaProjectionTests.cs:53` `Gold_silver_horn_online_reset_refresh_retries_failures_without_sending_every_tick` | 金银号角每日刷新 worker 的 `RefreshAsync` | 同上 |
| `…/UsageQuotaProjectionTests.cs:87` `Tournament_online_refresh_follows_daily_reset_and_retries_failed_delivery` | 比武每日刷新 worker | 同上 |
| `…/UsageQuotaProjectionTests.cs:218` `Mirror_online_worker_refreshes_at_local_midnight_and_retries_failed_delivery` | 镜像每日刷新 worker | 同上 |
| `Host.Tests/GmPlayerSamplingHttpTests.cs:41` `SessionInvalidatedWhileReadingPositionIsExcludedBeforePagination` | GM HTTP 读取 | 注入的 `duringRead` 回调在入口内部做 `Cleanup` 与断连，属于入口内部的交错注入，没有包。GM 入口迁入执行器后，它在同一作业内内联执行 |
| `Host.Tests/GmCera*`、`GmEvents*`、`GmOfficialMail*`、`GmLoginLimiter*` 的并发用例 | GM HTTP | 按 §2.5 末尾的裁定不算冲突 |

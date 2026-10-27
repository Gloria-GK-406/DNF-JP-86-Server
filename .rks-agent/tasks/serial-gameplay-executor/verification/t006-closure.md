# T-006 收口对账

- 日期 2026-10-23。基准提交 `a861c4de`（TASK-25）加上 TASK-26 未提交的改动。
- 用途：判定 T-006「投递切换到发件箱」是否完成，以及测量闸 M-2 的进入条件是否成立。也是外部审核读 T-006 的入口。
- 写法：仿 `t005-closure.md`，只写「交付 → 哪个 TASK → 证据位置 → 状态」。未关闭事项只引用 F 编号，内容在 `follow-ups.md`；推导过程留在各 TASK 文件，这里不抄。
- 证据底稿：
  - `tasks/TASK-23.md`～`tasks/TASK-26.md` 的 Result 与主控方裁定；
  - 日志 `logs/task-23-*.txt`～`logs/task-26-*.txt`；
  - 测试处置 `t006-test-dispositions.md` 的 TASK-23、TASK-24、TASK-25 三节（TASK-26 的测试增量见 §7 的加法与 TASK-26 的 Result）。
- 状态词：
  - **完成**：T-006 范围内的部分都有证据；
  - **完成，部分移交**：T-006 范围内完成，Plan 或裁定把其余部分分给了后续步骤；
  - **满足**：Spec 验收按字面有证据；
  - **满足，附条件**：Spec 验收按主控方裁定，以降级的证据层级、按裁定的解读或限定的适用范围满足。条件写在同一行，交外部审核时逐条说明，不得当作无条件满足引用；
  - **部分满足**：Spec 验收的一部分今天成立，其余归后续步骤，今天不得记为已满足。
- **拆分**（TASK-23 主控方裁定第 0 条、TASK-25 主控方裁定第 1 条）：T-006a ＝ TASK-23（投递翻转），T-006b ＝ TASK-24（附录 E 逐行承接、删队伍两把投递门），T-006c ＝ TASK-25（替身改写、F-102），T-006d ＝ TASK-26（本收口），共 4 个单元；第 1 轮评审后主控方在 T-006 内另立 TASK-27（修复 F-115：客户端提供、长度可变、又扇出给其他连接的字段加上限），T-006 因此共 5 个单元，TASK-27 尚未执行（TASK-26「主控方处置（第 1 轮评审后）」第 1 条）。

## 1. Plan T-006 行逐项

| 交付 | TASK | 证据 | 状态 |
|---|---|---|---|
| 执行器上的发送器、`ProjectAsync`、关连接都改为登记发件箱 | TASK-23 | `OutboxNetworkMessageSender`、`OutboxConnectionCloser` 是执行器上唯一的发送与关闭实现（`logs/task-24-verify.txt` V1）；直发实现删除，名称检索为 0（§7）；`OutboxRegistrationTests` 的 N12 7 条（`t006-test-dispositions.md` TASK-23 §6） | 完成 |
| 工作单元的提交钩子与组装点（RQ-031(0)） | TASK-23 | `OutboxWorkUnitTests` 4 条（N5）：`Of_two_writable_transactions_only_the_one_that_commits_is_flushed`、`A_composition_in_the_unit_sees_the_committed_state_and_not_a_later_stage`、`Entries_of_a_transaction_whose_outcome_is_uncertain_are_dropped`、`What_is_registered_under_a_read_only_transaction_counts_as_outside_a_unit`；TASK-25 变异 M5 下其中 `Of_two_writable_transactions_only_the_one_that_commits_is_flushed`、`Entries_of_a_transaction_whose_outcome_is_uncertain_are_dropped` 2 条变红（`logs/task-25-mutations.txt` 第 4 轮） | 完成 |
| 失败路径的刷出规则（RQ-031(c)） | TASK-23 | N6 `OutboxWireTests.A_failing_cmd_sends_what_it_kept_in_order_and_only_then_closes_its_source`；b1、b2 严格形式（§7） | 完成 |
| 组装失败按收件人隔离（RQ-031(d)），含 F-102 的整批登记 | TASK-23、TASK-25 | N14 `NotificationDeliveryTests.A_recipient_whose_composition_failed_is_never_composed_again`；N13 `NotificationDeliveryTests.A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered`；F-102 的 `A_recipient_whose_last_message_cannot_be_encoded_registers_none_of_its_messages`、`A_recipient_with_several_messages_is_resolved_once_and_registered_as_one_batch`（TASK-25 §8） | 完成 |
| 接上 T-003 的发送端 | TASK-23 | `NetworkingModule` 注册 `IOutboundSender` → `OutboundSender`（TASK-23 Result Changed）；N1 `OutboxWireTests.An_inbound_cmds_reply_is_with_the_sender_when_the_dispatch_completes_and_reaches_the_client`；N9 | 完成 |
| 按附录 E 逐项改写 DR-1～DR-17 | TASK-24（TASK-25 换驱动） | §3 | 完成 |
| 删除重试队列与排空服务 | TASK-23 | J1 名称规则 `NotificationRetryQueue`、`NotificationRetryDrainService`、`INotificationRetryDrain`、`NotificationDeliveryOptions` 命中 0（`logs/task-23-lock-scan.txt`）；本单元检索 R3 为 0（`logs/task-26-retrievals.txt`） | 完成 |
| 改写「发送即记录」的替身 | TASK-25 | 替身清单终态 `logs/task-25-doubles.txt`：基准 100 个类 ＝ 改写 70 ＋ 不改 3 ＋ 交 T-008 0 ＋ 投影 27；18 条 F 类改为登记点注入（TASK-25 §3）。投影替身 27 个只分类不改写（TASK-25 主控方裁定第 2 条）；没人用的替身 F-111、C2 包装独立发件箱 F-112 交 T-008 | 完成，部分移交 |
| 开启 RQ-003「等待网络」检查 | TASK-23 | `GameplayNetworkWaitAssertion.Touching` 在 `OutboundConnection` 的发送与关闭入口；反例 N4 `OutboundRealSocketTests.A_job_that_writes_to_a_connection_directly_fails_as_a_program_error`（取证 `network-wait` 1 条） | 完成 |

T-006 结束时的全量与取证（TASK-26 正式全量 `logs/task-26-build-test.txt`、`logs/task-26-violations.txt`）：8238 条，失败只有 2 条 `DimensionDungeonTests`（`baseline.md`），跳过 18；表征测试 10 通过、0 跳过；取证 40 条 ＝ `bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37，反例类与对照以外 0，没有测试帧 0，`exe:` 0。

## 2. 附录 F「删除」栏的投递三项与 AC-017

| 项 | 删于 | 判据或检索证据 | 状态 |
|---|---|---|---|
| 队伍按角色的 `SemaphoreSlim`（`participantGates`、`EnterParticipantsAsync`、`PartyParticipantLease`） | TASK-24 | J1 加入这三个名称并全局 0；J2 名单删去 `PartyNotificationSlice.cs`（`logs/task-24-lock-scan.txt`）；AC-017 检索式二 0 行（R2） | 完成 |
| `PartyDeliveryStream.Gate`（类名保留，TASK-24 主控方裁定第 5 条） | TASK-24 | J2 名单删去两文件后两文件 `SemaphoreSlim` 为 0；AC-017 检索式一 8 行，都是含义无关的同名 `.Gate` 成员，没有一行在 `Protocol/Game/Party` 下（R1） | 完成 |
| `NotificationRetryQueue` 与 `NotificationRetryDrainService` | TASK-23 | J1 全局 0；R3 0 行 | 完成 |

另：`GameplayGateAssertion` 与 `GameplayGateWaitException` 随队伍两把门删除（TASK-24），J1 守着；取证源迁到 `GameplayAssertionDiagnostics`（F-84）。

**AC-017：满足（T-006 范围）。**
- 「删除栏在 `Server/src` 中检索为零」：T-005 的部分见 `t005-closure.md` §2；投递三项见上表。判据 `LockRemovalSourceTests` 14 条在 TASK-26 正式全量中通过（R6）。
- 「运行期判据在执行器外调用时抛出（反例用例）」：RQ-061 写断言一半自 TASK-18，反例 `GameplayWriteAssertionTests`（取证 24 条）；发件箱一半自 TASK-23，反例 N3 四条：`OutboxRegistrationTests.A_send_off_the_executor_is_a_program_error_reported_as_evidence`、`A_close_off_the_executor_is_a_program_error_reported_as_evidence`、`An_ordered_scope_off_the_executor_does_nothing`、`NotificationDeliveryTests.A_projection_off_the_executor_is_a_program_error_reported_as_evidence`。
- 附录 F「保留」栏缺 `PasswordComputationLimiter` 一项（F-85，T-009），不属于「删除」栏。

## 3. 附录 E 十七行与 AC-014

输入是 `t006-test-dispositions.md` TASK-24 §6 的逐行证据表。证据用例名在 TASK-25 之后重新检索，全部找到（R5，`logs/task-26-retrievals.txt`）。「TASK-25 换驱动」指该文件里的替身在 TASK-25 改为经真实登记适配器与发件箱观察，断言除处置记录 TASK-25 §6 列出的之外不变。

| 行 | 保护意图 → 新承接方式 | 证据（`t006-test-dispositions.md` TASK-24 §4 的编号） | 状态 |
|---|---|---|---|
| DR-1 选角 | 失败的会话不留在半入场状态 → 命令失败照旧补偿；传输失败由发送端关连接、生命周期 CMD 拆除 | T1 `SelectCharacterLifecycleTests.An_entry_whose_delivery_fails_in_transport_is_closed_by_the_sender_without_compensation`、R1 `An_entry_message_that_cannot_be_registered_compensates_and_closes_the_requester`；拆除 `PartyCharacterizationTests.A_disconnect_in_town_announces_the_departure_to_the_other_player`、N9。TASK-25 换驱动 | 完成 |
| DR-2 进本 | 副本局里不留没收到进入消息的成员 → 失败连接关闭、其余进入（VC-3） | T2 `PartySelectDungeonHandlerTests.A_follower_whose_transport_fails_is_closed_alone_and_the_party_still_enters`、R2；XO-1 两条。TASK-25 换驱动 | 完成 |
| DR-3 两塔 | 失败者不留在塔局里 → RQ-036 关连接，生命周期 CMD 清理 | T3、T4（两塔各 `A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower`）、R3 4 条。TASK-25 换驱动 | 完成 |
| DR-4 怪物死亡、祭坛、清算 | 状态已提交而客户端不知道时不让会话继续 → 传输失败只关失败连接（VC-4）；CMD 内非传输失败照旧封锁 | T5 `MonsterDeathHandlerTests.A_participant_whose_death_batch_fails_in_transport_is_closed_alone_and_nobody_is_fenced`、T6、T7、R4、W2；AC-009 点名的 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`（断言行不变）；清算 KB `SetPlayResultHandlerTests.After_payment_lost_claim_cancel_or_missing_progress_never_releases_for_repayment`。`MonsterDeathHandlerTests` TASK-25 换驱动 | 完成 |
| DR-5 队伍水位线 | 成员不错过队伍状态 → 登记时推进，失败连接关闭 | T8 `PartyNotificationTests.The_watermark_advances_at_registration_and_a_member_whose_delivery_fails_is_closed`、R5 2 条 | 完成 |
| DR-6 配额刷新 | 不漏发配额状态 → 登记时记录 | T9 7 行 `UsageQuotaProjectionTests.A_quota_refresh_that_fails_in_transport_counts_as_sent_and_closes_the_connection`、R6 7 行 | 完成 |
| DR-7 GM Cera | 让 GM 知道提醒可能没到 → 「已提交发送」或「不在线」（VC-6） | `GmCeraReminderTests` 6 条（含 T10、R7 与评审后的 Theory 2 行）；V6 `GmCeraHttpTests.CommittedGrantSurvivesReminderExceptionAndProjectionReadsCurrentWallet` | 完成（对外字段变化 F-107） |
| DR-8 房间机制 | 机制状态一定送达 → 失败连接关闭，重连后投影 | T11、R8（`RoomMechanismProjectionFailureTests`） | 完成 |
| DR-9 生物 | ACK 不先于外观 → 依次登记进同一发件箱 | T12 `CreatureProjectionDeliveryTests.An_owner_whose_rename_notice_fails_in_transport_never_gets_the_acknowledgement_first`、R9。TASK-25 换驱动 | 完成 |
| DR-10 清理通告 | 顺序与必达 → RQ-034 | XO-5：`CleanupNotificationDeliveryTests.A_slow_owner_is_delivered_before_the_peers_and_the_party`、`An_owner_whose_delivery_fails_skips_the_peers_and_the_party` | 完成 |
| DR-11 回选角 | 不留半退出状态 → RQ-036 | T13、R10（`ReturnToSelectionDeliveryFailureTests`） | 完成 |
| DR-12 初始 NOTI | 连接不处于未初始化状态 → RQ-036 | T14、R11（`InitialConnectionDeliveryFailureTests`） | 完成 |
| DR-13 钱包、PvP 重试 | 偶发失败时补送 → 不再重试（VC-5） | N14；`ProtocolModule` 不再有 `retryLimit`（TASK-23 §6） | 完成 |
| DR-14 期限、重试队列、排空服务 | 慢连接不拖累发送方 → RQ-036／RQ-037 | `OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`、`Exceeding_the_queue_byte_limit_closes_the_connection`；J1 | 完成 |
| DR-15 handler 抛异常 | 未知错误不让会话继续 → RQ-031(c) | N6；b1、b2（§7）；W7 `LifecycleExecutorWireTests.A_cmd_closing_its_own_source_connection_does_not_wait_on_itself` | 完成 |
| DR-16 称号投影 | 称号投影不插进进入序列 → 按 RQ-032 排在选角之后 | T15 `TitleProjectionOrderTests.A_title_projection_queued_during_an_entry_is_delivered_after_the_whole_entry`（证明边界见 §11） | 完成，附条件 |
| DR-17 队伍流门 | 队伍投影不乱序、不回退 → 同一条 CMD 内，按完成顺序交付 | T16 `PartyNotificationTests.Two_party_cmds_deliver_their_projections_in_completion_order_without_a_stream_gate`；墓碑 `PartyTownProjectionDeliveryTests.ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot` | 完成 |

**AC-014：满足，附条件**：DR-16 的证据用占位作业代表进入序列，不证明真实选角序列中称号事件的入队时机（TASK-24 评审发现 4）。其余十六行按字面满足。

**RQ-031(b) 的证据**是 `OutboxWorkUnitTests`（N5）。TASK-25 改写的 C2 用例各自持有独立的发件箱，登记不属于被测容器的工作单元，**不作为 RQ-031(b) 的证据**（F-112，TASK-25 Remaining 2 与 M5）。

## 4. 附录 G

| VC | 证据 | 状态 |
|---|---|---|
| VC-1 多段 CMD 统一落线 | `SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript`，金样本 a 不变 | 完成 |
| VC-2 回滚单元内的消息不发出 | `OutboxWorkUnitTests`（N5）；`logs/task-23-in-unit.txt`：生产 CMD 的单元内登记 0，按「仅限理论情形」成立 | 完成 |
| VC-3 组队进本**或两塔进入**时成员发送失败只断开该成员 | T2、T3、T4。文字在 TASK-23 扩展到两塔（§10） | 完成 |
| VC-4 提交后的传输失败只关失败连接 | T5、T6、T7 | 完成 |
| VC-5 钱包、PvP 不再重试 | N14 | 完成 |
| VC-6 GM Cera 只有「已提交发送」「不在线」 | `GmCeraReminderTests` 6 条与 V6。实际取值：`attempted`（保留字符串，含义为「已提交发送」）与 `offline`；组装失败与提醒异常照常上抛，HTTP 500，响应体 `outcome` 为 `unknown`（F-107） | 完成（取值见 §10） |
| VC-7 | 见 `t005-closure.md` §5 | 完成（T-005） |
| VC-8 慢客户端被断开 | N9 `OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once`；`OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`；提前核对与「T-006 之后消失」两半都已落实（F-14，本单元确认关闭） | 完成 |

## 5. 附录 D 与 AC-012

有序组的产出点与交付等级见 TASK-23 §7：组内默认 BestEffort，XO-5 的所有者与 XO-6 的阶段为 Required（TASK-23 主控方裁定第 5 条）。证据都在发送端层，断言交付次序（附录 D「口径」）。

| XO | 「前序变慢、未超上限仍成立」 | 「前序超过上限即关闭、后续照常」 | 状态 |
|---|---|---|---|
| XO-1 进本 | `PartySelectDungeonHandlerTests.A_slow_follower_is_delivered_its_entry_before_the_requesting_leader` | XO 专属：`A_follower_over_the_limit_is_closed_and_the_requesting_leader_is_still_entered` | 完成 |
| XO-2 离开副本 | `PartySelectDungeonHandlerTests.A_slow_peer_is_delivered_the_decision_before_the_leaders_acknowledgement` | 发送端通用 | 完成 |
| XO-3 领主之塔 | `BossTowerProjectionConcurrencyTests.A_slow_member_is_delivered_the_tower_entry_before_the_requester` | 发送端通用 | 完成 |
| XO-4 幻影塔 | `IllusionTowerProjectionConcurrencyTests.A_slow_member_is_delivered_the_tower_entry_before_the_requester` | 发送端通用 | 完成 |
| XO-5 清理通告 | `CleanupNotificationDeliveryTests.A_slow_owner_is_delivered_before_the_peers_and_the_party` | 发送端通用；所有者失败即跳过后续 `An_owner_whose_delivery_fails_skips_the_peers_and_the_party` | 完成 |
| XO-6 队伍阶段 | `PartyNotificationTests.A_slow_recipient_in_one_formation_stage_holds_the_later_stages_back` | 发送端通用；阶段失败即跳过后续 `A_failed_delivery_in_one_formation_stage_skips_every_later_stage` | 完成，附条件（F-98） |
| XO-7 婚礼 | `TownAreaProjectionDeliveryTests.A_wedding_tick_delivers_both_positions_before_either_partners_cinema`（新娘连接放慢 300 ms） | 发送端通用 | 完成 |

发送端通用用例：`OutboundSenderTests.A_predecessor_slower_than_the_limit_is_closed_and_the_group_goes_on`、`A_slow_entry_before_an_early_finished_middle_one_is_still_cut_at_the_limit`、`An_ordered_entry_waits_for_a_slow_predecessor_within_the_limit`。单连接顺序：`OutboundSenderTests.One_connection_receives_its_entries_in_submission_order`。

**AC-012：满足，附条件**：
- 「前序超过等待上限」一半，XO-1 有专属用例，XO-2～XO-7 由发送端通用用例承担（有序组是同一个机制，组内次序由各产出点的专属用例钉住）；
- XO-6 标 Required 是对今天行为的近似：失败阶段里排在失败者之后的收件人收不到这一阶段（F-98，TASK-23 Revision 2 裁定第 2 条）。

## 6. 验收与需求

| 项 | 证据 | 状态 |
|---|---|---|
| **AC-007** 断线、关连接、被顶替三种情形；由 CMD 发起的关闭在此前登记的帧之后；自关来源不自锁；停机落库 | 断线：`lifecycle:disconnected` 拆除，`PartyCharacterizationTests.A_disconnect_in_town_announces_the_departure_to_the_other_player`、N2 `OutboxWireTests.A_reply_to_a_client_that_disconnected_first_is_dropped_and_the_cmd_succeeds`。关连接：N7 `OutboxWireTests.A_cmd_closing_another_connection_closes_it_after_the_frames_it_registered_for_it`（线级，帧先于 FIN）。被顶替：N7 `LoginSessionMaintenanceExecutorTests.A_superseded_connection_is_disconnected_and_then_closed_by_the_cmd_that_superseded_it`（关闭登记在顶替的那条 CMD 里，F-65）。自关不自锁：W7。停机：`HostShutdownOrderTests.Sigterm_with_an_open_game_connection_stops_the_executor_after_its_teardown_and_exits_cleanly`、`ShutdownOrderWireTests.Stopping_the_host_runs_every_open_connections_teardown_on_the_executor_before_it_stops`、`ShutdownPositionPersistenceTests.A_player_in_town_when_the_host_stops_has_the_runtime_position_written_back`。停机排空：兜底拆除 `TouchSocketTransportDrain.cs:28-49`、`TryRemove` 仲裁 `:32` 与 `NetworkPlugins.cs:48` 仍在；变异 MF4a、MF4b 各使 `ConnectionCallbackBoundaryTests` 3 条变红，含 `The_transport_drain_tears_down_a_connection_whose_close_callback_never_comes`（`logs/task-26-mutations.txt`；F-21 本单元关闭） | 满足，附条件：「此前登记的消息」按连接计（TASK-23 裁定第 2 条，§10） |
| **AC-011** 单元测试六条 | ① 第二个单元回滚只刷出第一条：`OutboxWorkUnitTests.Of_two_writable_transactions_only_the_one_that_commits_is_flushed`；② 已提交单元内、单元外登记后未处理异常：N6；③ 某收件人组装失败只丢他的、记 `Warning`、不关他的连接、其他收件人照常、发布本身成功：`NotificationDeliveryTests.A_handler_that_throws_for_one_recipient_fails_only_that_one`（其他人照常登记、失败者不被关闭）、`A_failed_delivery_is_said_out_loud`（`Warning` 带原异常）、N14；④ 祭坛：R4 `AltarProjectionFailureTests.A_participant_whose_altar_projection_cannot_be_composed_fences_every_participant_and_closes_only_the_source`（按更正后的措辞，§10）；⑤ 组装读到已提交的状态：`OutboxWorkUnitTests.A_composition_in_the_unit_sees_the_committed_state_and_not_a_later_stage`；⑥ 判据阻止 CMD 直接调用发送器：`CommandHandlerArchitectureTests.ProductionCommandHandlers_OnlyTakeDomainShapes` 对全部生产命令处理器成立，本单元给 `ATypeOutsideTheVocabularyIsRefused` 补 `INetworkMessageSender`、`IGameMessageSender` 两行，变异 MF5（放行以 `Sender` 结尾的类型）下两行变红；执行器上唯一的发送实现是登记适配器，直发实现已删（R3） | 满足 |
| **AC-011** 线级三条 | ① 怪物死亡提交后失败，封锁并关闭每个参与者：W2 `PostCommitFailureWireTests.A_monster_death_failing_after_its_commit_fences_and_closes_every_participant`；② 选角准入被拒，两条拒绝消息按序送达后断开：b1；③ 选角发布后补偿，同伴收到清理通告：降到发送端层，`SelectCharacterLifecycleTests.A_selection_compensated_after_publication_registers_the_peers_cleanup_notice_before_the_close`（F-108） | 满足，附条件：线级第 3 条按 TASK-24 裁定第 3 条、TASK-25 裁定第 5 条以发送端层证据代替（表征宿主上请求者总落在不共享在场的城镇门口，F-108） |
| **AC-013** 发送端测试 | Required 失败中止该目标、BestEffort 按目标隔离：`OutboundSenderTests.A_required_failure_stops_its_group_and_a_best_effort_failure_does_not`；整批编码与身份校验：N13 `OutboxRegistrationTests.A_batch_whose_last_message_cannot_be_encoded_throws_at_the_call_and_registers_none_of_it`、`OutboxRegistrationTests.A_prepared_batch_is_registered_for_exactly_the_context_given_even_one_no_longer_registered`、`OutboundRealSocketTests.An_entry_for_an_equal_but_not_identical_context_is_stale`；连接被替换时丢弃：`OutboundSenderTests.An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one`；超时或超限关闭：DR-14 两条；被卡住的连接不影响其他连接与执行器：N9、`OutboundSenderTests.A_stuck_connection_neither_delays_others_nor_blocks_the_submitter` | 满足 |
| **AC-013** 队列上限与选角 | 判据 `OutboundQueueLimitTests.The_queue_limit_holds_twice_the_largest_selection`、`The_criterion_rejects_a_limit_below_twice_the_bound`；选角全序列不触发超限：实测最大 41,891 B（`logs/task-23-selection-bytes.txt`，本单元探针相同），表征运行 `OverLimit` 0。人群项（本单元 Outcome 1，`measurement/selection-outbound-bound.md`「人群项（TASK-26）」、`logs/task-26-crowd.txt`）：按字面的实测口径，选角 41,891 ＋ 实测人群项 0 ＝ 41,891 B，城镇换区最大 936 B，都 ≤ 524,288 B；按保守上界口径人数没有上界，适用范围为同区他人 ≤ 782 人（`inbound:GameSetUserArea`）、PvP 大厅他人 ≤ 133 人（选角）时在 2 倍余量之内，超过 1,048,576 B（1,565 人／1,140 人）时请求者连接按 RQ-036 被关闭、不拖累执行器与其他连接；20 人规模下分别还能多 763 人、114 人。「未计入」第一条（无生产写入方的列表）维持 TASK-05 口径不计入（主控方裁定第 2 条） | 满足，附条件：适用范围如左（主控方裁定第 1 条，选项 C）；无生产写入方的字段不被导入或写库放大（F-114）；立项 F-113。另：保守上界对恶意输入不成立，由 TASK-27 修复（F-115）——改包客户端设一个约 1 MiB 的队伍标题，就能让走进该区域的任何玩家因 `OverLimit` 断线，这是 T-006 引入的回归，不是可以承接的条件 |
| **RQ-008** 执行器不等待关连接、交付或传输回调；收包回调与 HTTP 不等交付 | 执行器侧：RQ-003 网络等待检查与反例 N4；作业结束只把条目交给发送端（N1：dispatcher 完成时回包已在发送端）；`OutboundSenderTests.Delivery_runs_off_the_executor_even_when_submitted_from_a_job`、`A_stuck_connection_neither_delays_others_nor_blocks_the_submitter`；N9。收包回调：插件在作业完成后返回，不 `throw;` 给 TouchSocket（TASK-23 Changed「传输回调」）；HTTP 的 GM 作业同样只登记 | 满足 |
| **RQ-012** 生命周期 CMD；由 CMD 发起的关闭在此前登记的消息交付之后 | AC-007 一行；关闭条目排在该连接自己的队列里（`OutboundSenderTests.A_close_request_runs_after_the_frames_registered_before_it`、`A_close_request_is_carried_out_even_when_its_group_broke`） | 满足，附条件：「此前登记的消息」按连接计（§10） |
| **RQ-036** 失败策略、期限、队列上限与 2 倍余量 | 传输写入点判据 `TransportWriteSourceTests`（3 条，名单 `OutboundTransport.cs` 1 处 ＋ UDP 发现 1 处，R6）；期限与队列上限 DR-14 两条、`OutboundSenderTests.Up_to_the_limit_is_admitted`、`A_send_that_ignores_its_cancellation_still_times_out`；2 倍余量判据同 AC-013；2 秒只在 `OutboundLimits` 两处（R4） | 满足，附条件：人群项的适用范围同 AC-013；保守上界对恶意输入不成立，由 TASK-27 修复（F-115） |
| **RQ-061 发件箱一半** | `GameplayOutbox.Register` 经 `GameplayWriteAssertion.Writing`；反例 N3 四条（§2）；本单元正式全量 `write-off-executor` 37 条都在反例类与对照里 | 满足；取证窄缺口 F-97（执行器外调用的消息恰好不可编码时抛编码错误而不写取证，T-009 复核） |
| **VC-8** | §4 | 完成 |

## 7. 「T-006 完成的条件」七条（TASK-23 后续单元边界提纲）

| 条件 | 证据 | 状态 |
|---|---|---|
| Plan T-006 行逐项有证据 | §1 | 成立 |
| 投递三项与 T-006 删除的名称在 `Server/src` 检索为零 | §2；R3 0 行（`logs/task-26-retrievals.txt`） | 成立 |
| `ExecutorSendDeadline` 为零，2 秒只在 `OutboundLimits` 两个约定位置 | R3 0 行；R4 3 行 ＝ `OutboundLimits.cs:19` `DeliveryTimeout` ＋ `:22` `OrderedWaitLimit` ＋ `DungeonCardPresentationCoordinator.cs:18` 的卡牌布局计时（2 s 与 4 s，玩法节奏，与投递无关） | 成立 |
| 作业内没有直接传输写入，由运行期检查与传输写入点判据守着 | RQ-003 检查与 N4；`TransportWriteSourceTests` 在 TASK-26 正式全量中通过 | 成立 |
| 附录 E 每行有经执行器与发件箱的证据 | §3 | 成立（DR-16 附条件） |
| b1、b2 严格形式开启并通过 | `SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close`（b1）、`A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close`（b2），TASK-23 去掉 `Skip`，四个单元的正式全量都通过。b1 对「关闭越过帧」不是区分性用例，确定性的区分由 N6 承担（F-100） | 成立 |
| 每个单元一次正式全量，取证反例类外为 0 | TASK-23 46 条、TASK-24 40 条、TASK-25 40 条、TASK-26 40 条，反例类与对照以外都是 0，`exe:` 都是 0（`logs/task-23-violations.txt`～`logs/task-26-violations.txt`） | 成立 |

测试数量的加法（按实际运行的例数，Theory 按行计）：
- T-006 开始时 8194 ＝ TASK-22 正式全量 8192 ＋ 评审后新增 2（TASK-23 合同 Required checks 2）；
- TASK-23：8194 ＋ 40 － 43 ＝ 8191（处置记录 TASK-23 §1）；
- TASK-24：8191 ＋ 45 － 7 ＝ 8229；评审后 ＋ 2 ＝ 8231（TASK-24 §1、§7）；
- TASK-25：8231 ＋ 2 － 1 ＝ 8232（TASK-25 §1）；
- TASK-26：8232 ＋ 6 － 0 ＝ 8238。N 6 ＝ Host 4（`HostOutputViolationTests.An_assertion_exception_in_a_line_of_host_output_is_recognised` 的 `GameplaySelfWaitException` 一行 ＋ `HostOutputWiringSourceTests` 3 条）＋ Application 2（`CommandHandlerArchitectureTests.ATypeOutsideTheVocabularyIsRefused` 两行）；逐项目 Host 190 ＋ 4 ＝ 194，Application 4536 ＋ 2 ＝ 4538，其余六个项目不变。
- 合起来：8194 ＋ (40 ＋ 45 ＋ 2 ＋ 2 ＋ 6) － (43 ＋ 7 ＋ 1) ＝ 8194 ＋ 95 － 51 ＝ 8238。
- TASK-26 第 1 轮评审后 Host 再加 1 条（`HostOutputWiringSourceTests.A_process_started_with_an_object_initializer_is_named`），工作区为 8238 ＋ 1 ＝ 8239；Host 194 ＋ 1 ＝ 195。正式全量没有重跑（只改了这一个判据文件与文档），记的仍是 8238 那一次（`logs/task-26-review-fixes.txt`）。

## 8. 移交清单（只列 F 编号与归属）

| 归属 | F 编号 |
|---|---|
| T-007 | F-26～F-29、F-59、F-66 |
| T-008 | F-07、F-12（附录 C 一半）、F-30～F-34、F-58（复核一半）、F-86、F-87（改名一半）、F-88、F-93、F-98、F-109、F-110、F-111、F-112 |
| T-009 | F-35、F-36（AGENTS.md 一半）、F-60、F-62、F-75、F-76、F-85、F-89、F-92、F-97、F-99、F-105 |
| M-2 与之后 | F-02、F-05（未覆盖的一半）、F-10（未观测的一半）、F-69（每个后续单元的耗时对比）；F-77～F-82（后续性能优化，交主控方立项） |
| TASK-27（T-006 内，修复后 T-006 才算完成） | F-115（客户端可触发的第三方断线） |
| 主控方 | F-01、F-113（人群项）、F-114（无生产写入方字段的长度约束） |
| 外部审核 | F-107（GM Cera 对外字段）、F-108（AC-011 线级第 3 条降级） |
| 测试基础设施与取证缺口 | F-37、F-38、F-39、F-45、F-55、F-71、F-103 |
| 人工把关与条件触发 | F-06（预热识别边界）、F-43（增加修改 GM 口令的写入方时） |

状态为「信息」、不需要动作的（F-40、F-41、F-42、F-44、F-46、F-70、F-95、F-96、F-100）不列。

## 9. 结论与 M-2 进入条件

本单元关闭：F-14、F-21、F-61。

- **T-006 尚未完成**：TASK-27（F-115）完成之前，T-006 不算完成（主控方处置第 1 条）。下面各条是 TASK-23～TASK-26 的状态。
- **T-006 已执行部分的交付在 TASK-23～TASK-26 的改动上都有证据**（§1～§7）。Plan T-006 行九项中八项完成，替身改写一项「完成，部分移交」（投影替身只分类、F-111、F-112 交 T-008）；附录 F 投递三项删除；附录 E 十七行与附录 G 各项都有经执行器与发件箱的证据。
- **按字面满足、不带条件的 Spec 验收**：AC-011 单元测试六条、AC-013 发送端测试、AC-017（T-006 范围）、RQ-008。
- **满足，附条件**（条件写在 §3～§6 对应行，交外部审核逐条说明，不得当作无条件满足引用）：
  - AC-007、RQ-012：「此前登记的消息」按连接计；
  - AC-011 线级：第 3 条降到发送端层（F-108）；
  - AC-012：超限一半除 XO-1 外由发送端通用用例承担，XO-6 的近似（F-98）；
  - AC-013、RQ-036：人群项按适用范围（F-113、F-114）；另外保守上界对恶意输入不成立，由 TASK-27 修复（F-115），这一点不是条件，是待修的回归；
  - AC-014：DR-16 的证明边界；
  - RQ-061 发件箱一半：取证窄缺口 F-97（不影响「抛出」本身）。
- **未满足、今天不得记为已满足的 Spec 验收**：T-006 范围内没有（F-115 作为待修回归另列，见上，修复前 T-006 不算完成）。T-006 范围之外仍未满足的，见 `t005-closure.md` §7（AC-009 的附录 H 最终对账归 T-008；AC-006 归 T-009，F-62；AC-005 的测量一半归 M-2）。
- **M-2 的进入条件**：Plan Work 表中 M-2 依赖 T-006。
  1. T-006 的交付全部有证据：**今天不成立**，缺 TASK-27（F-115）；TASK-23～TASK-26 的部分成立，以 TASK-26 经结果审查并提交为准；
  2. AC-011、AC-013 的状态：都是「满足，附条件」，条件已写明；AC-013 的保守上界对恶意输入不成立，由 TASK-27 修复后才能按适用范围记。F-113 交主控方立项，标明不阻断 M-2；
  3. 没有未处置的停止条件：TASK-26 的七条停止条件都没有触发（人群项实测值最大 41,891 B，没有突破 524,288 B；附录 E 证据名检索（R5）找不到 0；取证 `exe:` 0、反例类外 0；没有改生产文件、`OutboundLimits`、`spec.md`、`plan.md`；表征与金样本未改，失败只有基线 2 条；扫描判据没有发现第四个启动进程的测试文件）。
  - 所以 **M-2 的进入条件是 TASK-26 与 TASK-27 都经结果审查并提交**；今天不成立。TASK-27 完成后，由 TASK-27 在本文件末尾追加落地状态。
- **M-2 规划时接手**（不阻断）：
  - 工具：`measurement/m1-summary.md` §8 的交代——统一用能解析执行器作业行的新 dll、加跑更大的灌包窗口（`--flood-window 256`）、S3 按 2 s 口径读；
  - `judge.py` 中「预期，不计」规则（F-83 那一类断线竞态失败）的去留：F-83 已在 TASK-23 关闭，T-006 之后这类失败应当消失；TASK-20 评审 L2 指出该规则没有时间窗，M-2 复用前要复核；
  - F-02（RQ-091 达标判定）；F-77～F-82（不阻断）；F-69。
- `plan.md` 的 T-006 行是否补写实际拆分（TASK-23～TASK-27），由主控方提交时处理（TASK-26 主控方裁定第 4 条）。

## 10. T-006 期间的 Spec 修订与解读（交外部审核）

逐行对到 `git diff a4422480 a861c4de -- spec.md`（4 处改动行，`logs/task-26-retrievals.txt` 核对 10）；TASK-26 不改 `spec.md`（`git diff a861c4de -- spec.md` 为空）。

| 位置 | 原文 → 现文（或解读） | 性质 | 授权 | 提交 |
|---|---|---|---|---|
| 附录 A 第 4 行（登录会话的连接观察与维护扫描）「新的 CMD」一栏 | 「生命周期 CMD（连接观察随 `lifecycle:disconnected`；顶替后的关闭 `lifecycle:close-superseded`）与定时 CMD」→「……与定时 CMD；顶替后的关闭由顶替发生的那条 CMD 登记进它的发件箱（T-006，F-65），被顶替连接的清理随它的 `lifecycle:disconnected`」 | 文字已改，事实性更新 | TASK-23 主控方裁定第 6 条 | `50d7bce4` |
| 附录 G VC-3 | 「组队进本时某个成员发送失败：只断开该成员，其他成员正常进入」，依据 DR-2 →「组队进本或两塔进入时……其他成员与请求者正常进入（……两塔进入会中止其余成员的发送、关闭全部隔离成员与请求者）」，依据 DR-2、DR-3 | 文字已改，放宽可见行为 | TASK-23 第 1 轮评审 F1 的主控方处置第 1 条 | `50d7bce4` |
| RQ-031(d) 末句 | 「按 RQ-023(c) 封锁并关闭全部参与者」→「按 RQ-023(c) 封锁全部参与者，入站时来源连接关闭」 | 文字已改，事实性更正（代码只封锁、不关闭） | TASK-24 主控方裁定第 1 条 | `1ee1c91e` |
| AC-011 第 4 条 | 同上 | 文字已改，事实性更正 | TASK-24 主控方裁定第 1 条 | `1ee1c91e` |
| VC-6 的取值 | 文字未改。取值为 `attempted`（保留字符串，含义「已提交发送」）与 `offline`；组装失败与提醒异常上抛，HTTP 500，`outcome` 为 `unknown`，不再有 `unconfirmed` | 文字未改，取值与对外字段的裁定（F-107） | TASK-24 主控方裁定第 2 条、评审后处置第 1 条 | `1ee1c91e` |
| RQ-012、RQ-031(c)「此前登记的消息」 | 文字未改。解读为按连接计：关闭连接 X 只等此前登记给 X 的消息；跨连接先后只由附录 D 的有序组表达 | 文字未改，解读 | TASK-23 主控方裁定第 2 条 | `50d7bce4` |
| RQ-034「整批编码……在发送端保持」 | 文字未改。编码改在登记点完整完成，发送端只收帧；编码失败仍在调用点抛出 | 文字未改，与字面的偏离 | TASK-23 主控方裁定第 1 条（备选 B） | `50d7bce4` |
| `NotificationOutcome.Delivered`（Abstractions 共享契约） | 含义由「已交给传输」改为「已登记，提交发送」（或连接已不在而被丢弃），不改名；改名交 T-008 | 契约含义改变 | TASK-23 主控方裁定第 4 条 | `50d7bce4` |
| AC-011 线级第 3 条 | 证据降到「执行器 ＋ 真实登记适配器 ＋ 真实发送端」层（F-108） | 验收证据层级的偏离 | TASK-24 主控方裁定第 3 条；TASK-25 主控方裁定第 5 条 | `1ee1c91e` |
| AC-013 的人群项 | 文字未改。按字面的实测口径满足；保守上界口径写适用范围（§6），立项 F-113；「未计入」第一条维持不计入，立项 F-114 | 验收记法的裁定 | TASK-26 主控方裁定（执行前）第 1、2 条 | 本单元 |

## 11. 已接受的差异与证明边界

| 差异或边界 | 依据 | 状态 |
|---|---|---|
| 两塔进入在交付失败时的可见变化：只关失败者，其余成员与请求者照常进入（VC-3 扩展） | TASK-23 第 1 轮评审 F1 与主控方处置第 1 条 | 已接受，交外部审核 |
| GM Cera 对外字段 `reminder` 只剩 `attempted`／`offline`，提醒异常时 HTTP 500 | F-107 | 已接受，交外部审核 |
| F-102 的批次化：一个收件人的多条通知由多次登记变为一批；该批任一条不可编码时这个收件人一条都不登记 | TASK-25 §8 | 已接受 |
| XO-6 标 Required 的近似 | F-98 | 已接受，T-008 判断是否需要阶段语义 |
| 登记点先编码、后判位置的取证窄缺口 | F-97 | 已接受，T-009 复核 |
| b1 对「关闭越过帧」不是区分性用例，由 N6 承担 | F-100 | 信息 |
| TASK-24 评审发现 3：D6、D7 删掉吞咽分支后，关闭请求若抛出，原始失败可能被关闭异常取代；能走到的只有执行器外的程序错误 | TASK-24 Result Remaining | 只作记录 |
| TASK-24 评审发现 4：T15 证明「进入作业期间入队的称号投影在整个进入之后送出」，不证明真实选角序列中称号事件的入队时机 | TASK-24 Result Remaining | DR-16、AC-014 附条件 |
| RQ-031(b) 的证据边界：只有 `OutboxWorkUnitTests`；TASK-25 的 C2 用例不是 | F-112 | T-008 |
| F-61 的已知残余：打包进程里没有取证记录器，只能看输出；子进程中被 `catch` 吞掉、又没有打印的违规仍看不见。扫描判据 `HostOutputWiringSourceTests` 的识别范围：文件里出现单词 `ProcessStartInfo`（构造或对象初始化器写法）或调用 `Process.Start(` 即算启动进程（第 1 轮评审后由「`new ProcessStartInfo(`」放宽，补对象初始化器反例）；别的写法（例如不经这两个记号构造 `Process` 再调实例 `process.Start()`）认不出。认出之后只查文件里同时有两个输出事件的订阅与 `ObserveHostOutput` 调用，不查三者相连，也不分辨一个文件里有几个进程（`EndToEndHostHarness.cs` 另用 `Process.Start("kill", …)` 发停机信号，按文件计已接线）；`Tool/` 下的进程启动不在 `Server/tests`，不在范围。识别名单不含 `GameplayExecutorStoppedException`（停机时合法打印）与 `GameplayDepthExceededException`（RQ-053 的超限处理，T-007） | TASK-26 §3；`logs/task-26-f61.txt` | 信息（F-61 已关闭） |
| 人群项：城镇在场与 PvP 大厅没有人数上限，保守口径只成立于适用范围之内；PvP 大厅没有线级实测（表征宿主只有普通频道），按公式 | §6 AC-013；F-113 | 已接受（主控方裁定第 1 条），交主控方立项 |
| 队伍标题只受 1 MiB 入站帧约束：改包客户端可让走进该区域的任何玩家因 `OverLimit` 断线，基准上 2 s 直发期限内能收完、不会断开。这是 T-006 引入、可由客户端触发的第三方断线，风险中，保守上界对恶意输入不成立 | F-115；TASK-26 主控方处置（第 1 轮评审后）第 1 条 | **不接受**，由 TASK-27 修复；修复前 T-006 不算完成 |

## 12. 落地状态（TASK-27，2026-10-23，基准 `8c531c9b`）

上文 §1～§11 一字不动；与它们不同的今天的状态记在这里。证据：`logs/task-27-verify.txt`、`logs/task-27-census.txt`、`logs/task-27-red-green.txt`、`logs/task-27-build-test.txt`、`logs/task-27-violations.txt`、`logs/task-27-mutations.txt`、`logs/task-27-new-x10.txt`、`logs/task-27-isolated.txt`、`logs/task-27-retrievals.txt`、`logs/task-27-diff-check.txt`、`logs/task-27-boundary.txt`、`logs/task-27-closure-refs.txt`。

本单元关闭：F-115。

**1. F-115 的修复。**
- 改动：`SetPartyInfoDecoder`（`Server/src/Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs:216` 常量 `MaximumTitleBytes` ＝ 256，`:245-249` 检查）。标题长度超过 256 B 时抛 `InvalidDataException`，与该解码器对负长度、非法 UTF-8、长度不自洽的拒绝方式相同，传输层记 `Game decoder … threw` 后关闭发送者连接；不截断。检查排在长度自洽检查之前，宣称超长而 body 被截断的请求也按超长拒绝，日志写明上限。
- 依据等级：推断。客户端上限没有证据，已知只有下界 1 B 与 12 B（`protocol/game/client-to-server/0x000C.md` 2026-10-23 一节）；取证立项 F-117，结果大于 256 B 时必须上调。
- 证据用例：P1 `PartyCommandCodecTests.Set_party_info_accepts_a_title_of_exactly_the_maximum_length`（2 行）、P2 `PartyCommandCodecTests.Set_party_info_rejects_a_title_longer_than_the_maximum`（3 行）、P3 `PartyNotificationTests.Full_roster_with_a_maximum_length_title_is_eighty_bytes_plus_the_title`、W1 `PartyTitleFanOutWireTests.A_maximum_length_party_title_keeps_a_walk_in_within_the_per_person_bound`、W2 `PartyTitleFanOutWireTests.A_party_title_at_the_limit_reaches_a_player_walking_in`。
- 证据形态按 TASK-27「主控方处置（执行中断后）」调整：不做「超长标题使他人断线」的线级复现；基准先红由 P2 承担——在不改生产代码时 3 行都变红（两行「No exception was thrown」，截断那行的拒绝原因不含上限），修复后变绿；W1 改为尺寸用例，实测走进区域的 CMD 649 B ＝ 52 ＋ 261 ＋ 336 ≤ 52 ＋ 926 ＝ 978，名单帧 336 B ＝ 80 ＋ 256（`logs/task-27-red-green.txt`）。
- 变异：MT1（去掉检查）P2 3 行红；MT2（`>=`）P1 2 行与 W1、W2 红；MT3（上限减半）P1、P2 共 5 行与 W1、W2 红，P3 绿；MT4（检查挪到自洽检查之后）P2 截断那行红。每项都有指定的红用例（`logs/task-27-mutations.txt`）。
- Spec：附录 G 新增 VC-9（见第 9 条）。

**2. 同类字段普查**（`logs/task-27-census.txt`，第一位执行者写的脚本，本轮复跑一次，输出逐字节相同）：
- 注册 184 ＝ GameCmd 180 ＋ Channel 4；不同的解码器类 175（184 － 9 ＝ 175：`ItemSocketDecoder` ×4、`JoustQueryDecoder` ×2、`WarehouseDecoder` ×6 多占的 3 ＋ 1 ＋ 5 ＝ 9 个键）。
- 长度规则：定长 89 ＋ 有界变长 16 ＋ 开放 70 ＝ 175。保留客户端可变字节的类 36 ＝ 6 ＋ 8 ＋ 22，不保留的 139，36 ＋ 139 ＝ 175。
- 这 36 个类的字节去向：无上限扇出 1 ＋ 认证 1 ＋ 丢弃 7 ＋ 有界扇出 5 ＋ 只记日志 15 ＋ 只回本人 7 ＝ 36。
- 协议清单里名字像文本或社交的 C→S 协议 161 ＝ 已注册 25 ＋ 未注册 136；未注册的帧被传输层按「Unhandled Game frame」丢弃。

| 字段（协议） | 入站约束 | 到达其他连接的路径 | 处置 |
|---|---|---|---|
| 队伍标题（`SET_PARTY_INFO` 0x000C） | 修复前只拒负数、要求自洽；修复后 ≤ 256 B | 名单帧 80 ＋ T 给区域观察者与新到者，设置帧 37 ＋ T 给队员 | **本单元加上限** |
| 角色名（`CREATE_CHARACTER` 0x0005） | 创建规则 ≤ 18 B（`CharacterCreationRuleService.cs:22`） | 0x0002 在场快照等，含在每人 539 B 内 | 已有上限 |
| 宠物名（`RENAME_CREATURE` 0x0064） | 规则 ≤ 12 B、无控制字符（`PetInstance.cs:80` 等三处） | 改名通知 33 B；0x0002 名牌，含在每人 539 B 内 | 已有上限 |
| 婚礼留言（`UPDATE_GUEST_COMMENT` 0x03F8） | ≤ 20 B，每本 ≤ 100 条 | 别人读留言簿时进读者自己的 CMD：15 ＋ 13 ＋ 6,200 ＝ 6,228 B | 已有上限 |
| 邮件收件人与正文（0x005E、0x013B） | 请求 ≤ 1,024 B，正文 ≤ 512 B，收件人 ≤ 19 B | 收件人只收定长提醒；正文在收件人自己打开邮箱时随页送出 | 已有上限 |

结论：需要加上限的只有队伍标题，停止条件 1 没有触发。

**3. AC-013、RQ-036 的现状态。** 仍是「满足，附条件」，条件只剩两条：同区他人或 PvP 大厅的人数（F-113，适用范围按第 4 条重算）、无生产写入方的字段不被导入或写库放大（F-114）。§6 两行与 §9 中「保守上界对恶意输入不成立，由 TASK-27 修复（F-115）」一句自本节起作废，原文保留。客户端可变长度字段中，本单元唯一新增上界的单帧是队伍名单帧，最大 80 ＋ 256 ＝ 336 B（0x0002 在场快照同样含客户端提供的字节，单帧约 539 B，已在每人上界内）。256 B 没有客户端证据（F-117）只影响「合法玩家会不会被误拒」，不影响上界：上界由服务端强制。

**4. F-113 重算**（每人 670 ＋ 256 ＝ 926 B）：
- `inbound:GameSetUserArea`：N_max ＝ ⌊(524,288 － 52) ÷ 926⌋ ＝ ⌊524,236 ÷ 926⌋ ＝ 566（566 × 926 ＝ 524,116 ≤ 524,236；567 × 926 ＝ 525,042 ＞ 524,236）。超过 1,048,576 B 的人数：1,048,576 － 52 ＝ 1,048,524，1,132 × 926 ＝ 1,048,232 ≤ 1,048,524，1,133 × 926 ＝ 1,049,158 ＞ 1,048,524，所以是 1,133。20 人规模（他人 19）：52 ＋ 19 × 926 ＝ 52 ＋ 17,594 ＝ 17,646 B，还能多 566 － 19 ＝ 547 人。
- 选角（PvP 频道）不含队伍帧，133／1,140 不变；其余城镇进入为 ⌊(524,288 － F) ÷ 926⌋。
- 改记于 `measurement/selection-outbound-bound.md`「落地状态（TASK-27）」与 F-113。

**5. T-006 是否完成。** T-006 的五个单元（TASK-23～TASK-27）在 T-006 范围内的交付都有证据，F-115 已修复，§9「T-006 尚未完成」的唯一理由不再成立。T-006 的完成以 TASK-27 经结果审查（合同 Result review：required）并提交为准；在那之前记为「交付有证据，待 TASK-27 结果审查」。

**6. M-2 进入条件**（§9 的三条，逐条）：
1. T-006 的交付全部有证据：**成立**（TASK-23～TASK-26 已提交；TASK-27 的证据见第 1 条），以 TASK-27 经结果审查并提交为准。
2. AC-011、AC-013 的状态：都是「满足，附条件」，条件已写明（AC-013 见第 3 条）。F-113、F-114、F-116、F-117、F-118 均不阻断 M-2。
3. 没有未处置的停止条件：TASK-27 的六条都没有触发——普查只有队伍标题（1）；W1 的线级复现按中断后处置取消，基准先红由 P2 承担且原因正确（2）；现有用例、表征测试与金样本的标题最长 12 B，全量里都通过（3，`logs/task-27-verify.txt` V9）；没有改 `OutboundLimits`、发送端、发件箱、登记点，`spec.md` 只加 VC-9 一行，`plan.md`、`AGENTS.md` 未改（4，`logs/task-27-boundary.txt`）；取证 `exe:` 0、反例类与对照以外 0，失败只有基线 2 条（5）；拒绝方式在解码器内做得到（6）。
- 所以 **TASK-27 经结果审查并提交后，M-2 可以进入**；今天待该审查。

**7. §7 测试数量的加法续写**（按实际运行的例数，Theory 按行计）：
- 8238（TASK-26 正式全量）→ 8239（TASK-26 评审后 Host ＋ 1）→ 8239 ＋ 8 － 0 ＝ 8247（TASK-27 正式全量，`logs/task-27-build-test.txt`）。
- N 8 ＝ Protocol 6（P1 2 行 ＋ P2 3 行 ＋ P3 1 条）＋ Networking 2（W1、W2）；逐项目 Protocol 1746 ＋ 6 ＝ 1752，Networking 179 ＋ 2 ＝ 181，其余六个项目不变。
- 结果：通过 8227，失败 2（基线 `DimensionDungeonTests`），跳过 18；8227 ＋ 2 ＋ 18 ＝ 8247。表征测试 10 通过、0 跳过；取证 40 条 ＝ 2 ＋ 1 ＋ 37，反例类与对照以外 0，`exe:` 0。

**8. §8 移交清单的变化。** TASK-27 一行（F-115）关闭。新增：F-116（发送端或登记点的通用防线，T-009 或主控方立项，不阻断 M-2）、F-117（队伍标题的客户端上限待 ClientStatic 取证，归属用户或客户端研究仓库）、F-118（AGENTS.md 新增协议固定动作补「扇出字段必须有上限」，T-009）。F-113 的归属不变（主控方），内容按第 4 条更新；F-114 的宠物文本一句更正（宠物改名是生产写入方，≤ 12 B，每人 539 B 不变）。

**9. §10 Spec 修订续行。**

| 位置 | 原文 → 现文（或解读） | 性质 | 授权 | 提交 |
|---|---|---|---|---|
| 附录 G VC-9 | 新增：`SET_PARTY_INFO` 的队伍标题超过 256 B 时请求被拒绝（解码失败），发送者连接被关闭；今天这样的标题被接受并扇出给队员与区域内的人 | 文字已改，新增可见变化 | TASK-27 主控方裁定（执行前）第 3 条 | 本单元 |

**10. §11 的变化。** 队伍标题一行由「不接受，由 TASK-27 修复」变为**已修复**（第 1 条）。新增一条已接受的证明边界：

| 差异或边界 | 依据 | 状态 |
|---|---|---|
| 队伍标题上限 256 B 没有客户端证据，是推断；客户端若能产生更长的标题，合法玩家改标题时会被断线（VC-9） | F-117；TASK-27 主控方裁定（执行前）第 1 条 | 已接受，取证后若更大必须上调 |

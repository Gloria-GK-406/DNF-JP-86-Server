# T-006 测试处置记录

- 日期 2026-10-23。TASK-23（T-006a，投递翻转）基准提交 `a4422480`；TASK-24、TASK-25 在本文件后面续写。
- 分类沿用 TASK-23 Detailed Plan §10.2：E 编译迫使、R 重试与期限、S「发送在执行之内」、W 线级、F「发送即记录」替身；新增用例记 N。
- 每条删除都写承接者（名字可检索），没有承接者的写明保护意图为什么已由规格消除（TASK-17 主控方裁定第 7 条）。
- 机械核对：`logs/task-23-diff-check.txt`（方法集合、断言行多重集合）。

## TASK-23

### 1. 数量（按实际运行的例数，Theory 按行计）

| 项目 | 基准 | 新增 N | 删除 D | 结果 |
|---|---:|---:|---:|---:|
| Application.Tests | 4548 | 8 | 24 | 4532 |
| Networking.Tests | 175 | 22 | 19 | 178 |
| Protocol.Tests | 1699 | 10 | 0 | 1709 |
| 其余五个项目 | 1772 | 0 | 0 | 1772 |
| 合计 | 8194 | 40 | 43 | 8191 |

其余五个项目：HttpApi 54 ＋ Pvf 80 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Host 191 ＝ 1772。
合计：8194 ＋ 40 － 43 ＝ 8191；4532 ＋ 178 ＋ 1709 ＋ 1772 ＝ 8191。
Networking 的 N 与 D 各含 3 条「随文件改名的改写」（`TouchSocketNetworkMessageSenderExplicitDeliveryTests` → `OutboxExplicitDeliveryTests`，方法名不变，见 §3 S6）；不计这 3 条时 N 37、D 40。

### 2. 删除（D ＝ 43）

| 行 | 用例 | 类 | 保护意图 | 承接／消除 |
|---|---|---|---|---|
| R1 | `NotificationRetryTests` 整个文件 9 条（`A_retry_asks_the_handler_again_rather_than_resending_what_it_had`、`A_retry_goes_to_the_connection_they_are_on_now`、`An_exact_session_retry_does_not_follow_a_replacement_session`、`A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit`、`A_notification_that_declares_no_retries_queues_nobody`、`Somebody_who_left_before_the_retry_is_let_go`、`Giving_up_on_a_recipient_is_said_out_loud`、`A_pass_called_off_leaves_what_it_had_not_reached`、`A_pass_works_through_what_was_waiting_and_no_further`） | R | 失败收件人的重试：重组、去向、上限、放弃时记日志、排空的边界 | 规格消除：DR-13、DR-14、VC-5 不再重试，附录 F 删除重试队列。「失败不重试、下一轮也不再组装」另加 N14 `NotificationDeliveryTests.A_recipient_whose_composition_failed_is_never_composed_again` |
| R2 | `NotificationRetryDrainServiceTests` 整个文件 5 例（3 个 Fact ＋ `An_interval_that_cannot_tick_is_refused_at_startup` 2 行） | R | 排空循环的周期、停止、抛出不停、间隔校验 | 规格消除：排空服务删除（附录 F）；判据 J1 守着名字不再出现 |
| R3 | `NotificationRetryDrainExecutorTests` 整个文件 2 条 | R | 每轮排空是执行器上的定时 CMD；停止时已入队的一轮照常执行 | 同 R2；定时源对账表删去这一行（R7） |
| R4 | `NotificationDeliveryTests` 的期限 5 条：`A_recipient_who_runs_out_of_time_is_a_failure`、`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`、`A_recipient_who_ran_out_of_time_is_named_as_that_and_not_as_an_error`、`Something_thrown_after_the_deadline_is_still_reported_as_what_it_was`、`A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason` | R | 每收件人 2 秒期限：超时为失败、期限取配置值、超时与异常分开记 | 期限随 `NotificationDeliveryOptions` 删除（DR-14）。「慢连接不拖累别人、超时即关闭」由发送端承接：`OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`、`OutboundSenderTests.A_send_that_ignores_its_cancellation_still_times_out`、`OutboundRealSocketTests.A_client_that_stops_reading_times_out_and_is_closed_while_another_keeps_receiving`、N9 `OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once`；「组装抛出的异常照样记下」由 `A_failed_delivery_is_said_out_loud`、`A_handler_that_throws_for_one_recipient_fails_only_that_one` 承接 |
| R5 | `NotificationHandlerArchitectureTests.The_pvp_publication_declares_the_allowance_it_meant_to` | R | PvP 参与者通知声明重试 1 次 | 规格消除：DR-13 不再重试，`RetryLimit` 删除。PvP 通知的处理器注册由启动时的目录完备性检查守着（`NotificationRegistrationTests`） |
| R6 | `LoginSessionMaintenanceExecutorTests.A_superseded_connection_is_disconnected_and_closed_in_one_lifecycle_job` | R | 顶替的连接在 `lifecycle:close-superseded` 作业里先移除在线身份、再关闭 | `lifecycle:close-superseded` 随裁定第 6 条删除（F-65，`spec.md` 附录 A 第 4 行）。承接：N `LoginSessionMaintenanceExecutorTests.A_superseded_connection_is_disconnected_and_then_closed_by_the_cmd_that_superseded_it`（同一顺序、在执行器上、在顶替发生的那条 CMD 里）；线级 `LifecycleExecutorWireTests.A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor`（保留，名字按 F-87 不改） |
| R7 | `TimerSourceReconciliationTests.Each_timer_source_runs_its_ticks_as_its_own_cmd_on_the_executor` 的 `NotificationRetryDrainService` 一行 | R | 排空服务的每次节拍是一条定时 CMD | 规格消除：定时源随排空服务删除 |
| R8 | `ExecutorSendDeadlineTests` 整个文件 9 条（`On_the_executor_a_send_that_never_completes_times_out_and_closes_once`、`Off_the_executor_the_send_is_awaited_without_a_deadline`、`On_the_executor_a_send_that_completes_in_time_passes`、`On_the_executor_the_sends_own_timeout_is_passed_through_and_does_not_close`、`A_failing_timeout_log_neither_hides_the_timeout_nor_stops_the_close`、`On_the_executor_cancellation_is_a_cancellation_and_does_not_close`、`An_abandoned_send_that_fails_later_leaves_no_unobserved_exception`、`A_detached_close_started_in_a_job_runs_off_the_executor_while_the_job_still_runs`、`A_failing_detached_close_is_logged_as_a_warning`） | R | 过渡期规则 4：作业里的直发写入 2 秒期限、超时关闭一次、取消不关闭、未观察异常、分离关闭 | 过渡期规则 4 到期，`ExecutorSendDeadline` 删除。期限：`OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`；分离的关闭不在执行器上：`OutboundSenderTests.Delivery_runs_off_the_executor_even_when_submitted_from_a_job`；未观察异常：`OutboundSender.Observe`，由 `OutboundSenderTests.A_send_that_ignores_its_cancellation_still_times_out` 走到；关闭失败只记警告：`OutboundSender.CloseAsync` 的 `catch`（发送端的关闭在执行器外，不会抛给作业） |
| R9 | `DirectSendDeadlineRealSocketTests` 整个文件 2 条（`A_job_sending_to_a_client_that_stops_reading_times_out_closes_it_and_frees_the_executor`、`The_default_direct_send_deadline_is_two_seconds`） | R | 真实 socket 上直发期限释放执行器；默认 2 秒 | 同 R8。真实 socket：N9 与 `OutboundRealSocketTests.A_client_that_stops_reading_times_out_and_is_closed_while_another_keeps_receiving`；2 秒：`OutboundLimits.DeliveryTimeout`（`OutboundSendOptions.Default`，`OutboundQueueLimitTests` 守着上限常量） |
| R10 | `OutboundSenderTests.A_batch_that_cannot_be_encoded_sends_none_of_it` | E | 发送端准入时编码失败：整批不发、关闭 | 裁定第 1 条（备选 B）：发送端不再编码，`EncodingFailed` 删除。承接 N13 `OutboxRegistrationTests.A_batch_whose_last_message_cannot_be_encoded_throws_at_the_call_and_registers_none_of_it` 与 `PreparedBatchTests` 的三条编码用例 |
| R11 | `ExactConnectionCloseTests` 整个文件 2 条（`Same_id_replacement_is_not_closed_by_old_context_but_exact_current_context_is`、`Scheduled_close_captures_session_once_and_never_relooks_up_its_id`） | S | 关连接器的精确关闭：同 ID 替换不被旧上下文关闭；排期的关闭不再按 ID 查 | 被测类型 `TouchSocketNetworkConnectionCloser` 按 V2 删除。承接 N12 `OutboxRegistrationTests.An_exact_close_of_a_replaced_context_is_not_registered_and_one_of_the_current_context_is`、`OutboundSenderTests.An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one`（关闭条目按精确上下文解析） |
| R12 | `DetachedCloseTests` 整个文件 2 条（`A_close_requested_inside_a_job_runs_off_the_executor_while_the_job_still_runs`、`A_failing_close_is_logged_as_a_warning_and_not_thrown_to_the_requester`） | S | 作业里请求的关闭在执行器外执行；关闭失败只记警告 | 同 R11。关闭在作业结束后由发送端的分离循环执行：`OutboundSenderTests.Delivery_runs_off_the_executor_even_when_submitted_from_a_job`、`OutboundSenderTests.A_close_request_runs_after_the_frames_registered_before_it`；作业里直接关闭是程序错误：N4 的 `CloseAsync` 一半（`GameplayNetworkWaitAssertion.Touching("OutboundConnection.CloseAsync")`） |
| — | `TouchSocketNetworkMessageSenderExplicitDeliveryTests` 3 条 | S | 见 §3 S6 | 文件改名为 `OutboxExplicitDeliveryTests`，方法名不变；计入 D 3 与 N 3 |

加法：R1 9 ＋ R2 5 ＋ R3 2 ＋ R4 5 ＋ R5 1 ＋ R6 1 ＋ R7 1 ＝ 24（Application）；R8 9 ＋ R9 2 ＋ R10 1 ＋ R11 2 ＋ R12 2 ＋ 改名 3 ＝ 19（Networking）；24 ＋ 19 ＝ 43。

### 3. 改写（S，经执行器与真实发件箱；保护意图不变）

| 行 | 用例 | 旧断言 → 新断言 | 保护意图 |
|---|---|---|---|
| S1 | `NotificationDeliveryTests` 10 条：`Each_recipient_is_told_what_the_handler_made_for_them`、`A_recipient_whose_delivery_fails_does_not_stop_the_others`、`Someone_who_left_is_told_apart_from_someone_whose_delivery_failed`、`A_recipient_the_handler_declines_hears_nothing_and_is_not_a_failure`、`A_handler_that_throws_for_one_recipient_fails_only_that_one`、`The_callers_own_cancellation_is_not_reported_as_a_failed_recipient`、`Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`、`An_exact_session_is_gone_when_its_generation_changes_on_the_same_connection`、`A_presence_lookup_that_throws_costs_only_that_recipient`、`A_failed_delivery_is_said_out_loud`、`The_summary_accounts_for_every_recipient`（第一条与最后一条断言未改） | 世界 `NotificationWorld` 改为：每次发布是世界执行器上的一个作业，总线经真实登记适配器登记，作业结束交给真实 `OutboundSender`，传输可令某连接失败。「发送了几次、发给谁」→「登记了哪些条目、哪些被交付」（`Sender.Sent` 只算交付成功的条目，`Sender.Registered` 是全部条目）。`OnSend` 钩子 → `Composing`（组装第一个收件人时触发同样的变化）。`FailFor` 的「传输失败」在 `A_recipient_whose_delivery_fails_does_not_stop_the_others` 中保留为发送端层的失败：三人都已登记（`Delivered`），失败者的连接被关闭，其余照常交付；其余用例的「失败」改为组装或编码失败（`A_failed_delivery_is_said_out_loud`、`Someone_who_left…`），并断言连接不关闭、CMD 成功（AC-011 第 3 条） | 一个收件人的问题只是他自己的；离开、婉拒、失败三者分开；调用方取消不算收件人失败 |
| S2 | `NotificationInWorldTests.A_connection_revoked_after_resolution_is_gone_at_delivery` | `Sender.OnSend = … Revoke(Second)` → `Composing = …`（第一个收件人组装时撤销第二个）；断言不变 | 解析之后被撤销的连接在轮到时为 Gone |
| S3 | `GameplayOutboxTests` 全部 14 条 | 驱动方式：`outboxes.RunAsync(source, body)` → 执行器上带来源的作业（`GameplayWork.Source`），刷出由作业结束钩子做；条目构造带帧（§5.1）。`Registering_outside_a_job_is_a_program_error` 删去 `RunAsync` 那一行，加三行取证断言（`write-off-executor`，`Writing GameplayOutbox.Register`）。其余断言不变 | T-003 的发件箱规则不变；刷出与在哪个执行器无关 |
| S4 | `CleanupNotificationDeliveryTests` 4 个方法 6 例：`The_owner_is_told_before_the_people_around_them`、`Existing_owner_and_town_notices_finish_before_party_projection`、`Each_half_becomes_the_frames_it_always_did`、`An_empty_half_is_not_delivered`（3 行） | 记录替身 → 真实发件箱（`Protocol.Tests/Outbox/XoDelivery`）：断言登记的条目——所有者条目 Required 且组内序号 1，同伴与队伍投影依次取后续序号；`Published` 的通知类型与人群断言不变；`order` 列表改由条目顺序算出 | XO-5、DR-10 |
| S5 | `PreparedBatchTests` 5 个方法 10 例 | 被测类型 `TouchSocketNetworkMessageSender` 删除，改为在作业里驱动登记适配器，编码器用真实的 `RegistryOutboundEncoder`。编码相关的 3 个方法：「传输上什么都没发」→「发件箱里没有这一批的条目」；`Equal_id_context_replacement…`：编码时被替换的旧上下文 → 条目登记在调用方给的上下文上，交付时为 `Stale`，新旧会话都没收到帧（旧断言「旧会话收到 2 帧」「第二次发送抛出」随直发删除）；`Buffers_are_owned…`：所有权一半改为「条目拥有自己的帧拷贝」，传输失败一半移到发送端层（承接见用例注释） | 整批编码全有或全无；精确上下文不重定向（RQ-035）；帧所有权 |
| S6 | `TouchSocketNetworkMessageSenderExplicitDeliveryTests` 3 条 → `OutboxExplicitDeliveryTests`（同名） | `DeliverExplicitAsync` 的回调记录 → 作业里经登记适配器 `SendExplicitAsync`，断言登记的条目：直接收件人一条 Required 批次，其余目标各一条 BestEffort 批次，排除所有者；直接消息编码失败 → 调用点抛出、什么都不登记；某目标编码失败或已断开 → 只跳过该目标 | §5 表 `SendExplicitAsync` 一行；目标失败的隔离由发送端承接（`OutboundSenderTests.A_required_failure_stops_its_group_and_a_best_effort_failure_does_not`） |
| S7 | `LifecycleExecutorWireTests.A_cmd_closing_its_own_source_connection_does_not_wait_on_itself`（W7） | `Assert.InRange(received.Count, 0, 1)` → `Assert.Single(received)`（严格形式，F-20）；`Count("direct send deadline exceeded")` → `Count("Waiting for the network")` | AC-007：帧先于 FIN；作业期间没有传输写入 |
| S8 | `LifecycleExecutorWireTests.A_send_to_a_resetting_client_does_not_hold_the_executor_past_the_deadline` | 4096 × 64 KiB 发送、等待超时 → 15 × 64 KiB 只登记（低于 1 MiB 队列上限，客户端重置时发送端仍在写，而不是先按 OverLimit 关掉它）：作业在客户端重置之前就结束，紧接着入队的作业 1 秒内执行；拆除仍是作业之后的生命周期 CMD | 执行器根本不等交付（RQ-008、AC-013） |
| S9 | `EplpRechallengeFrameTests.Native_check_reaches_a_tcp_client_through_the_production_sender` | 测试线程上直接 `SendNotiAsync` → 包进 `TestGameplay.RunAsync(host.Services, …)`；断言不变 | 生产发送路径把帧送到 TCP 客户端 |
| S10 | `UnknownInboundFrameIntegrationTests.Real_TCP_metadata_hashes_complete_raw_body_and_counts_unknown_frames_before_decode` | 测试替换的 dispatcher 在传输回调里直接回包 → 经 `IGameplayQueue` 在一个作业里回包；断言不变 | 入站元数据与计数 |
| S11 | `LoginSessionCommitRaceTests.RolledBackHandoffDoesNotCloseTheExistingConnection` | `Assert.False(sessions.CloseRequests.TryRead(out _))` → 注入记录关连接器，`Assert.Empty(closes.Requested)` | 回滚的换绑不关闭已有连接 |
| S12 | `Persistence.Tests/Accounts/LoginSessionReconnectTests.OldDisconnectCannotRevokeOrExpireReplacementConnection` | `Assert.True(sessions.CloseRequests.TryRead(out var requested)); Assert.Equal(old, requested);` → 夹具注入记录关连接器，`Assert.Equal([old], closes.Requested)` | 换绑请求关闭旧连接（Owned boundary 外的测试项目，编译迫使，见 TASK-23 Result「与计划不同」） |
| S13 | `Host.Tests/GmPlayerDirectoryHttpTests.RealRuntimeLifecycleCountsBeforePaginationAndUsesCurrentPositionChannelAndPublication` | 夹具原先移除维护服务以暂停顶替清理；顶替清理移进登录 CMD 后，夹具另注册一个不带在线服务与关连接器的 `LoginSessionRegistry`，效果相同；断言不变 | 确定性的生命周期夹具 |
| S14 | `Host.Tests/GmEventsHttpTests.Durable_clear_command_decoder_dispatcher_claim_and_online_close_refresh_the_native_frames` | 两处「`DrainOnceAsync()` 之后立刻读替身收到的帧」之后各加一次 `Settled(host)`（向宿主执行器入队一个空作业，等它完成）；断言不变。原因：派发组循环可能先于用例取走 LiveOnly 投递，此时 `DrainOnceAsync` 读不到它、立刻返回 0，而取走它的作业还在执行器上组装；空作业排在它之后，完成即说明它已发完。第二次正式全量中失败 1 次，隔离 10 次中失败 8 次；加诊断在基准 `a4422480` 的副本上同样观察到 `drained=0` 与「读帧时为 0、500 ms 后为 11」，所以竞争在基准上就存在，本单元只改变了它的输赢比例（F-104）；改后隔离 10 次、整类 3 次全过 | GM 停用活动后，在线会话收到日历、积分与状态帧 |

### 4. 编译迫使（E，断言多重集合不变）

| 文件 | 改动 |
|---|---|
| `Networking.Tests/Outbound/OutboundTestSupport.cs` | 删去 `TextEncoder`；`Batch` 帮手带帧；`Sender` 去掉编码器 |
| `Networking.Tests/Outbound/OutboundSenderTests.cs` | 一处构造去掉 `TextEncoder`（另见 R10） |
| `Networking.Tests/Outbound/OutboundRealSocketTests.cs` | `Batch` 帮手带帧；`Sender` 去掉 `RegistryOutboundEncoder`；`Transport()` 帮手（N4 用） |
| `Networking.Tests/Runtime/ConnectionCallbackBoundaryTests.cs` | `TouchSocketTransportDrain` 构造去掉关连接器日志（两处） |
| `Application.Tests/Execution/TimerSourceHost.cs` | 删去 `NotificationDeliveryOptions` 注册 |
| `Application.Tests/Notifications/NotificationRegistrationTests.cs` | 删去 `RetryLimit: 0`、`retryLimit: 2` 实参与 `Assert.Equal(2, registration.RetryLimit)`（同一用例的处理器、组装器断言不变；用例名按 F-87 不改） |
| `Application.Tests/Authentication/LoginSessionMaintenanceExecutorTests.cs` | `Service` 帮手去掉在线服务与关连接器（另见 R6 与 N） |
| `Application.Tests/Execution/GameplayWriteEntryTableTests.cs` | 删去 `LoginSessionRegistry.get_CloseRequests()` 一行（成员已删除） |
| `Application.Tests/Execution/DetachmentSiteTests.cs` | 名单删去 `ExecutorSendDeadline.cs`、`TouchSocketNetworkConnectionCloser.cs` 两行，`OutboundSender.cs` 的理由去掉「not wired until T-006」 |
| `Application.Tests/Execution/LockRemovalSourceTests.cs` | J1 加入「T-006 投递」8 个名称，类注释改为「TASK-24 的一项」 |
| `Networking.Tests/Characterization/SelectCharacterCompensationCharacterizationTests.cs` | 只去掉 `:65`、`:115` 两处 `Skip` 实参（b1、b2 严格形式开启） |
| `Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs`、`Game/Party/PartyNotificationTests.cs` | 夹具加可选的真实发件箱与有序组参数（N8 用），已有用例的调用不变 |

§3 共 14 行；改写不改变例数，§1 的数量不受影响。

### 5. 保留不改（W 与已核对的 F）

| 用例 | 为什么仍有效 |
|---|---|
| `HandlerFailureConnectionIntegrationTests` 2 条 | 线级：handler 抛出后来源经发件箱关闭，其余连接照常服务；不改即过 |
| `OutboundSendFailureTests` 4 条 | 裁定第 1 条：在测试线程上经宿主的 `INetworkMessageSender`（登记适配器）发送，§5.1 的次序是编码先于位置检查，异常类型与文字沿用；变异 S24 证明次序就是它们不改即过的原因 |
| `EffectOperationReplayTests.Later_readonly_transactions_do_not_replace_the_primary_footprint_or_commit_state`、`Second_primary_command_cannot_pay_or_overwrite_the_first_committed_entry` | 计划 §10.3 预定改写为「第二条先后执行的 CMD」。替身 `f.Sender` 的回调在 handler 的发送调用点执行，生产中这一点就是登记点，仍在操作的作业里、主命令之后，所以回调里的总线调用本来就是「同一作业里、主命令之后的第二条 CMD」；断言不变即成立，未改（见 TASK-23 Result「与计划不同」第 6 条） |
| `LifecycleExecutorWireTests.A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor` | 顶替关闭改由登录 CMD 的发件箱执行；「被顶替的连接关闭、清理在执行器上」不变；名字按 F-87 不改 |
| 其余注入「发送即记录」替身的用例（F 类） | 不改，清点见 `logs/task-23-doubles.txt`，交 TASK-25；不作本单元的证据 |

### 6. 新增（N ＝ 40，其中 3 条是 §3 S6 的改名）

| # | 用例 | 钉住 |
|---|---|---|
| N1 | `OutboxWireTests.An_inbound_cmds_reply_is_with_the_sender_when_the_dispatch_completes_and_reaches_the_client` | RQ-005：dispatcher 完成时回包已交给发送端；回包到达客户端 |
| N2 | `OutboxRegistrationTests.A_send_or_close_for_a_connection_no_longer_registered_is_dropped_and_the_job_succeeds`；`OutboxWireTests.A_reply_to_a_client_that_disconnected_first_is_dropped_and_the_cmd_succeeds` | F-83 |
| N3 | `OutboxRegistrationTests.A_send_off_the_executor_is_a_program_error_reported_as_evidence`、`A_close_off_the_executor_is_a_program_error_reported_as_evidence`、`An_ordered_scope_off_the_executor_does_nothing`；`NotificationDeliveryTests.A_projection_off_the_executor_is_a_program_error_reported_as_evidence` | RQ-061 发件箱一半的反例（RQ-072） |
| N4 | `OutboundRealSocketTests.A_job_that_writes_to_a_connection_directly_fails_as_a_program_error` | RQ-003 等待网络的反例 |
| N5 | `OutboxWorkUnitTests` 4 条 | AC-011 第 1、5 条；§4.1 |
| N6 | `OutboxWireTests.A_failing_cmd_sends_what_it_kept_in_order_and_only_then_closes_its_source` | AC-011 第 2 条；RQ-031(c) |
| N7 | `OutboxWireTests.A_cmd_closing_another_connection_closes_it_after_the_frames_it_registered_for_it`；`LoginSessionMaintenanceExecutorTests.A_superseded_connection_is_disconnected_and_then_closed_by_the_cmd_that_superseded_it` | AC-007 严格形式；顶替关闭在顶替的 CMD 里 |
| N8 | XO-1 `PartySelectDungeonHandlerTests.A_slow_follower_is_delivered_its_entry_before_the_requesting_leader`、`A_follower_over_the_limit_is_closed_and_the_requesting_leader_is_still_entered`；XO-2 `PartySelectDungeonHandlerTests.A_slow_peer_is_delivered_the_decision_before_the_leaders_acknowledgement`；XO-3 `BossTowerProjectionConcurrencyTests.A_slow_member_is_delivered_the_tower_entry_before_the_requester`；XO-4 `IllusionTowerProjectionConcurrencyTests.A_slow_member_is_delivered_the_tower_entry_before_the_requester`；XO-5 `CleanupNotificationDeliveryTests.A_slow_owner_is_delivered_before_the_peers_and_the_party`、`An_owner_whose_delivery_fails_skips_the_peers_and_the_party`；XO-6 `PartyNotificationTests.A_slow_recipient_in_one_formation_stage_holds_the_later_stages_back`、`A_failed_delivery_in_one_formation_stage_skips_every_later_stage`；XO-7 `TownAreaProjectionDeliveryTests.A_wedding_tick_delivers_both_positions_before_either_partners_cinema` | AC-012；裁定第 5 条 |
| N9 | `OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once` | VC-8；AC-013；F-21 停服之外的关闭回调 |
| N10 | `TransportWriteSourceTests` 3 条 | F-63 |
| N12 | `OutboxRegistrationTests.A_send_by_id_registers_one_required_batch_for_the_connection_registered_under_it`、`A_prepared_batch_is_registered_for_exactly_the_context_given_even_one_no_longer_registered`、`An_exact_close_of_a_replaced_context_is_not_registered_and_one_of_the_current_context_is`、`A_close_by_id_closes_the_connection_registered_under_it`；`OutboxExplicitDeliveryTests` 3 条（改名，S6） | §5 表 |
| N13 | `OutboxRegistrationTests.A_batch_whose_last_message_cannot_be_encoded_throws_at_the_call_and_registers_none_of_it`；`OutboxWireTests.A_cmd_whose_message_cannot_be_encoded_fails_and_its_earlier_frames_arrive_before_the_close`；`NotificationDeliveryTests.A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` | 裁定第 1 条；§5.1 |
| N14 | `NotificationDeliveryTests.A_recipient_whose_composition_failed_is_never_composed_again` | VC-5；RQ-031(d) |

逐项目：Application 8 ＝ N3 1 ＋ N5 4 ＋ N7 1 ＋ N13 1 ＋ N14 1；Networking 22 ＝ N1 1 ＋ N2 2 ＋ N3 3 ＋ N4 1 ＋ N6 1 ＋ N7 1 ＋ N9 1 ＋ N10 3 ＋ N12 7 ＋ N13 2；Protocol 10 ＝ N8 10。8 ＋ 22 ＋ 10 ＝ 40。

### 7. 交 TASK-24（过渡期用例，裁定第 3 条）

注入「会抛异常的替身」、在本单元之后只证明附录 E 死分支的用例（逐条列名见 TASK-23 Result「Remaining」）。

## TASK-24

- 日期 2026-10-23，基准提交 `50d7bce4`（TASK-23 已提交）。T-006b：附录 E 逐行承接、删除队伍两把投递门与门断言、取证源迁移。
- 分类沿用 TASK-23 的 E、R、S、W、F，另加 **K**（门反例，规格消除）与 **KE**（保留分支的可达性证据）。可达性三类 D／KE／KB 的定义见 TASK-24 Detailed Plan §2；逐处记录在 `logs/task-24-sites.txt`。
- 证据口径：T 开头的是传输失败证据（在测试传输 `XoTransport` 上制造），R 开头的是保留分支的可达性证据（在登记点的编码接缝 `FaultingEncoder` 上注入，或经真实通知总线让组装失败）；两者都经真实执行器、真实登记适配器与发件箱、真实 `OutboundSender`。
- **更正 TASK-23 的标注**（主控方裁定第 4 条）：TASK-23 Result 把 29 条过渡期用例记为「只证明死代码」。按本单元的可达性判定，它们注入的发送调用在登记点仍会因编码、端点或参数校验失败而抛出（KE），或注入的是组装失败、业务失败（KB），所以多数仍证明保留下来的分支；逐条结论见 §5。
- 机械核对：`logs/task-24-diff-check.txt`。

### 1. 数量（按实际运行的例数，Theory 按行计）

| 项目 | 基准 | 新增 N | 删除 D | 结果 |
|---|---:|---:|---:|---:|
| Application.Tests | 4532 | 3 | 0 | 4535 |
| Networking.Tests | 178 | 1 | 0 | 179 |
| Protocol.Tests | 1709 | 41 | 6 | 1744 |
| Host.Tests | 191 | 0 | 1 | 190 |
| 其余四个项目 | 1581 | 0 | 0 | 1581 |
| 合计 | 8191 | 45 | 7 | 8229 |

其余四个项目：HttpApi 54 ＋ Pvf 80 ＋ Persistence 477 ＋ Infrastructure 970 ＝ 1581。
合计：8191 ＋ 45 － 7 ＝ 8229；4535 ＋ 179 ＋ 1744 ＋ 190 ＋ 1581 ＝ 8229。
Protocol 的 N 41 ＝ T2 1 ＋ 两塔 6 ＋ T5 1 ＋ 祭坛 2 ＋ T7 1 ＋ 队伍 4 ＋ 配额 14（两个 Theory × 7 行）＋ GM Cera 4 ＋ 房间机制 2 ＋ T12 1 ＋ 回选角 2 ＋ 初始通知 2 ＋ T15 1。

### 2. 删除（D ＝ 7）

| 行 | 用例 | 类 | 保护意图 | 承接／消除 |
|---|---|---|---|---|
| K1 | `ProtocolGateAssertionTests` 整个文件 4 条：`Party_participant_gates_cannot_be_taken_off_the_executor`、`A_contended_participant_gate_throws_and_releases_the_ones_already_taken`、`The_party_delivery_stream_gate_cannot_be_taken_off_the_executor`、`A_contended_party_delivery_stream_gate_fails_the_job_at_once` | K | 队伍的流门与按角色门只在执行器上取、取不到即失败 | 规格消除：两把门删除（附录 F）；「队伍投递只在执行器上发生」由 RQ-061 发件箱一半承接：`NotificationDeliveryTests.A_projection_off_the_executor_is_a_program_error_reported_as_evidence`、`OutboxRegistrationTests.A_send_off_the_executor_is_a_program_error_reported_as_evidence`；判据 J1／J2 守着门成员不再出现 |
| K2 | `PartyTownProjectionDeliveryTests.A_read_repair_finding_its_stream_gate_taken_fails_the_job_at_once`、`A_moved_party_refresh_finding_its_stream_gate_taken_fails_the_job_at_once` | K | 城镇读修复遇到被占的流门即失败 | 规格消除：流门删除；单执行器下没有可等的门。旧修订不回放由同文件 `ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot` 继续钉住 |
| E1 | `HostOutputViolationTests.An_assertion_exception_in_a_line_of_host_output_is_recognised` 中以 `GameplayGateWaitException` 为显示名的一行 InlineData | E | 识别名单里的类型名会被记为 exe 记录 | 被识别的类型删除；同一 Theory 的其余两行（`GameplayOffExecutorException`、`GameplayLeakedFlowException`）仍在 |

§5 的 29 条过渡期用例没有一条删除（见 §5 末尾的分布）。V13 判定的 `CommonExperienceEffectTests.CallbackCloser`：它的 `RequestCloseAsync` 抛出的意图是「这里禁止按 ID 关闭」，不是模拟关闭失败；D 类删除后异常不再被吞，但它本来就不被调用，用例不改即过。

### 3. 改写（方法不增不减）

| 行 | 用例 | 旧 → 新 | 保护意图 |
|---|---|---|---|
| R2 | `PartySelectDungeonHandlerTests.A_leader_delivery_failure_isolates_the_member_that_already_received_start_map` | 替身 `Sender(failRequiredConnection)` 在领队的 `SendExplicitAsync` 抛出 → `XoDelivery` 的 `FaultingEncoder` 在领队的 `StartMapNotification` 编码时抛出（类型与文字沿用 `InvalidOperationException("required delivery failed")`）；`world.Sender.Messages(Member)` 含 START_MAP → 成员的 START_MAP 批次已登记、且排在第一条关闭条目之前；`world.Closer.Closed == [Member, Leader]` → 关闭条目依次为成员、领队（隔离）与领队（RQ-031(c) 来源关闭）；回滚与城镇成员资格的断言不变 | DR-2 保留分支：已收到进入消息者被隔离，进本回滚 |
| R9 | `CreatureProjectionDeliveryTests.Peer_send_failure_closes_peer_and_continues_without_closing_owner` | 替身 `Sender { Failing }` → `FaultingEncoder`（`IOException("send failed")`）；`sender.Sent == [owner, failed, next]` → 登记的条目为 `[owner, next]`（失败者那一条没有登记）；`closer.Closed == [failed]` → 关闭条目 `[failed]`；`success` 仍为真 | DR-9 保留分支：逐收件人失败只关该收件人，后面的照常 |
| W1 | `GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect` | 宿主装饰器从 `IGameMessageSender`（第一条 `GameLoginResponse` 抛 `IOException`）移到 `IOutboundMessageEncoder`（同一消息、同一异常）；断言不变 | 登录回包登记失败：身份移除、可重连；同时是 D9 之后登录拒绝路径的线级证据 |
| V6 | `GmCeraHttpTests.CommittedGrantSurvivesReminderExceptionAndProjectionReadsCurrentWallet` | 提醒抛出时：`200` ＋ `reminder == "unconfirmed"` → `500`，审计只有一条 `Succeeded`（没有被记成 `RolledBack`）；同编号重放：`200` → `500`（替身每次都抛），钱包仍是 59（没有第二次入账）；中间的组装断言不变 | VC-6（主控方裁定第 2 条）：提醒异常照常上抛；已提交的发放不因提醒失败被误记为回滚，也不重复入账 |
| E2 | `GameplayAssertionRecorder.cs`、`ViolationCapture.cs`、`OutboxRegistrationTests.cs` | `GameplayGateAssertion.DiagnosticSourceName` → `GameplayAssertionDiagnostics.DiagnosticSourceName`（字符串值不变）；识别名单去掉 `GameplayGateWaitException` | 取证源迁移（F-84） |
| E3 | `GameplayAssertionRecorderControlTests`（链接进七个程序集） | `GameplayGateAssertion.Entering("recorder-control")` → `GameplayWriteAssertion.Writing("recorder-control")`；断言不变（`GameplayOffExecutorException`，消息含 `recorder-control`）；kind 由 `gate-off-executor` 变为 `write-off-executor` | 正向对照 |
| E4 | `LockRemovalSourceTests` | J1 加 `GameplayGateAssertion`、`GameplayGateWaitException`、`EnterParticipantsAsync`、`PartyParticipantLease`、`participantGates`；J2 删 `PartyNotificationSlice.cs`、`GameplayAssertions.cs` 两行；类注释改为附录 F「删除」栏全部点名 | AC-017（判据） |
| E5 | `XoDelivery.cs` 由 `Protocol.Tests/Outbox/` 移到 `tests/Support/`，链接进 Protocol.Tests 与 Application.Tests（只有一份实现，命名空间不变） | 加 `FaultingEncoder`、`configure`（加入真实事件服务与通知总线）、`RunFromAsync`（入站来源，作业开始即打开发件箱，同 dispatcher）、`Connect(NetworkConnectionContext)`、`Messages`、`CloseRegistered`、`ClosedAsync`、`Context`；`GameMessageSender` 按名字创建（它是 Protocol 的 internal） | 测试支撑 |
| E6 | `SelectCharacterLifecycleTests` 夹具、`CreatureProjectionDeliveryTests.Create`、`PartySelectDungeonHandlerTests.World` | 夹具加可选的 `gameSender`、`connectionCloser`、`cleanupDelivery`、`failQuest`，`RecordingCleanup.Plan`；`Create` 的参数类型放宽为接口；`World` 在给了 `outbound` 时用它的关连接器。未传这些参数的用例行为不变 | 让新用例经发件箱驱动 |

`Recorder.Entries` 中 `IsClose` 的条目是「登记了关闭」，`XoTransport.IsClosed` 是「发送端执行了关闭」；用例按要证明的事选用其一，传输失败一律只断言后者、并断言没有关闭条目。发送端先报告失败条目、再在分离的任务里关闭连接，所以「发送端执行了关闭」经 `XoDelivery.ClosedAsync` 等待读取（第一轮 ×10 中 Protocol 有 4 轮共 5 次在 `SettledAsync` 之后直接读 `IsClosed` 读到假，见 `logs/task-24-new-x10.txt`）。

### 4. 新增（N ＝ 45）

| # | 用例 | 钉住 |
|---|---|---|
| T1 | `SelectCharacterLifecycleTests.An_entry_whose_delivery_fails_in_transport_is_closed_by_the_sender_without_compensation` | DR-1：传输失败只由发送端关闭，不派发补偿 |
| R1 | `SelectCharacterLifecycleTests.An_entry_message_that_cannot_be_registered_compensates_and_closes_the_requester` | DR-1 保留分支：登记失败补偿、在已登记条目之后关闭 |
| AC3 | `SelectCharacterLifecycleTests.A_selection_compensated_after_publication_registers_the_peers_cleanup_notice_before_the_close` | AC-011 线级第 3 条（降级到发送端层，裁定第 3 条，Result Why） |
| T2 | `PartySelectDungeonHandlerTests.A_follower_whose_transport_fails_is_closed_alone_and_the_party_still_enters` | DR-2、VC-3 |
| T3 | `BossTowerProjectionConcurrencyTests.A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower` | DR-3、VC-3 |
| T4 | `IllusionTowerProjectionConcurrencyTests.A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower` | DR-3、VC-3 |
| R3 | 两塔各 `A_member_entry_that_cannot_be_registered_isolates_every_member_and_the_requester`、`A_departure_projection_that_cannot_be_registered_for_one_member_closes_only_him`（4 条） | DR-3 保留分支：进入的 `deliveryFailure` 与 `SendToExactAsync` 失败集合 |
| T5 | `MonsterDeathHandlerTests.A_participant_whose_death_batch_fails_in_transport_is_closed_alone_and_nobody_is_fenced` | DR-4、VC-4 |
| T6 | `AltarProjectionFailureTests.A_participant_whose_altar_projection_fails_in_transport_is_closed_alone_and_nobody_is_fenced` | DR-4、VC-4 |
| R4 | `AltarProjectionFailureTests.A_participant_whose_altar_projection_cannot_be_composed_fences_every_participant_and_closes_only_the_source` | AC-011 第 4 条（按裁定第 1 条更正后的措辞） |
| T7 | `SetPlayResultHandlerTests.A_settlement_whose_frames_fail_in_transport_closes_the_connection_and_stays_settled` | DR-4、VC-4（清算） |
| W2 | `PostCommitFailureWireTests.A_monster_death_failing_after_its_commit_fences_and_closes_every_participant` | AC-011 线级第 1 条（真实 TouchSocket；点名用例之外补「关闭」） |
| T8 | `PartyNotificationTests.The_watermark_advances_at_registration_and_a_member_whose_delivery_fails_is_closed` | DR-5 |
| T16 | `PartyNotificationTests.Two_party_cmds_deliver_their_projections_in_completion_order_without_a_stream_gate` | DR-17 |
| R5 | `PartyNotificationTests.A_party_projection_that_fails_to_compose_once_is_composed_again_inside_the_cmd`、`A_party_projection_that_fails_to_compose_twice_reports_the_stage_failed` | Plan R-5（队伍投影在 CMD 内再组装一次）；DR-5 保留分支 |
| T9 | `UsageQuotaProjectionTests.A_quota_refresh_that_fails_in_transport_counts_as_sent_and_closes_the_connection`（7 行：Altar、Dimension、GoldSilverHorn、ReviveCoin、Mirror、Heras、Tournament） | DR-6（V11：七个刷新不共用判定，所以每个一行） |
| R6 | `UsageQuotaProjectionTests.A_quota_refresh_that_cannot_be_composed_is_not_recorded_and_is_composed_again`（7 行） | DR-6 保留分支 |
| V6 | `GmCeraReminderTests` 4 条：`An_online_account_is_reminded_and_the_result_is_attempted`、`An_offline_account_in_the_batch_makes_the_result_offline_and_the_online_one_is_still_reminded`、`A_reminder_that_cannot_be_composed_is_an_error_after_the_others_were_reminded`（R7）、`A_reminder_that_fails_in_transport_is_still_attempted_and_its_connection_is_closed`（T10） | DR-7、VC-6、Plan R-5（按裁定第 2 条） |
| T11／R8 | `RoomMechanismProjectionFailureTests.A_room_mechanism_projection_that_fails_in_transport_completes_and_closes_the_connection`、`A_room_mechanism_projection_that_cannot_be_composed_is_thrown_for_a_retry` | DR-8、Plan R-5 |
| T12 | `CreatureProjectionDeliveryTests.An_owner_whose_rename_notice_fails_in_transport_never_gets_the_acknowledgement_first` | DR-9 |
| T13／R10 | `ReturnToSelectionDeliveryFailureTests.A_reply_that_fails_in_transport_is_closed_by_the_sender_and_the_teardown_is_not_repeated`、`A_reply_that_cannot_be_registered_closes_the_connection_and_the_failure_is_thrown` | DR-11 |
| T14／R11 | `InitialConnectionDeliveryFailureTests.An_initial_notice_that_fails_in_transport_is_closed_by_the_sender_and_the_disconnect_removes_the_identity`、`An_initial_notice_that_cannot_be_registered_removes_the_identity_and_is_thrown` | DR-12 |
| T15 | `TitleProjectionOrderTests.A_title_projection_queued_during_an_entry_is_delivered_after_the_whole_entry` | DR-16（V12：称号投影由 `AchievementEventSubscriber` 经 `ProjectAsync` 发出，LiveOnly 订阅，作为单独的作业在执行器上运行） |

逐项目：Application 3 ＝ T1 1 ＋ R1 1 ＋ AC3 1；Networking 1 ＝ W2 1；Protocol 41（加法见 §1）。3 ＋ 1 ＋ 41 ＝ 45。

### 5. 过渡期用例逐条复核（TASK-23 Remaining 29 条，裁定第 6 条）

| DR | 用例 | 处置 | 依据（注入点 → §2 来源） |
|---|---|---|---|
| DR-1 | `SelectCharacterLifecycleTests.RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection` | KE 保留 → F 类；R1 新增 | 替身在尾段 `SendExplicitAsync` 的直接收件人上抛出 → 直接收件人编码失败 |
| | `…PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt` | KE 保留 → F 类 | PvP 直接收件人的 `SendExplicitAsync` → 同上 |
| | `…PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks` | 保留（没有注入） | 替身里 `failPvpTarget` 是空操作（注释写明目标的传输失败由发送端承接），用例实测成功路径与 PvP 公示人群；名字过时，登记 F-87 |
| DR-2 | `PartySelectDungeonHandlerTests.A_projection_failure_cannot_split_party_members_between_town_and_run` | KB 保留 | 注入的是 `IPartyTownProjectionDelivery.RefreshMovedPartyAsync` 抛出（领域读取、事务守卫等非传输来源） |
| | `…A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town` | KE 保留 → F 类 | 成员是 `SendExplicitAsync` 的直接收件人 |
| | `…A_leader_delivery_failure_isolates_the_member_that_already_received_start_map` | KE 改写（R2，§3） | 同上 |
| | `PartyMoveMapHandlerTests.Publication_failure_logs_the_committed_room_and_preserves_the_exception` | KE 保留 → F 类 | 直接发送抛出 |
| DR-3 | `BossTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session` | KE 保留 → F 类 | `SendToExactAsync` 内对该成员的直接发送；保护意图是精确会话 |
| | `IllusionTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session` | KE 保留 → F 类 | 同上 |
| DR-4 | `MonsterDeathHandlerTests.Death_send_failure_cannot_erase_the_committed_shared_clear_fact` | KE 保留 → F 类 | 报告者是直接收件人 |
| | `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` | KB 保留（AC-009，断言行不变） | 注入的是运行时认领发布被拒（`RejectParticipantCharacter`），不是发送 |
| | `GetItemHandlerTests.Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact` | KE 保留 → F 类 | 直接发送抛出 |
| | `IllusionTowerRewardProjectionTests.Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | KE 保留 → F 类 | 「两次尝试」依据的是发送抛出（`SendCommittedBatchAsync` 的再试一次）；登记点的编码失败仍会抛出，再试一次照旧失败后关闭 |
| DR-6 | `UsageQuotaProjectionTests.Tournament_online_refresh_follows_daily_reset_and_retries_failed_delivery` | KB 保留；R6 新增 | 替身返回 `Failed`，翻转后 `Failed` 只剩组装或编码失败 |
| | `…Mirror_online_worker_refreshes_at_local_midnight_and_retries_failed_delivery` | KB 保留 | 同上 |
| | `…Gold_silver_horn_online_reset_refresh_retries_failures_without_sending_every_tick` | KB 保留 | 同上 |
| | `UsageQuotaTests.Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts` | KB 保留 | 投影失败（`bus.Fail`），与传输无关 |
| DR-9 | `CreatureProjectionDeliveryTests.Peer_send_failure_closes_peer_and_continues_without_closing_owner` | KE 改写（R9，§3） | `SendNotiAsync` 逐收件人 |
| | `…Owner_send_failure_closes_owner_and_reports_failure` | KE 保留 → F 类 | 同上 |
| DR-12 | `OnlineUserServiceTests.FailedInitialNotice_RemovesExactRegisteredIdentity` | KE 保留 → F 类；R11 新增 | `SendNotiAsync`（Application.Tests 里够不到 Protocol 的 internal 发送器，所以新增在 Protocol.Tests） |
| 离开副本 | `LeaveDungeonTests.Failed_exit_delivery_releases_the_transition_without_repeating_the_exit` | KE 保留 → F 类 | 同伴与本人都是直接收件人 |
| 登录 | `GameLoginTests.MessageHandler_SendFailureConsumesTheSuccessButRemovesItsPublishedIdentity` | KE 保留 → F 类 | `SendCmdAsync` |
| | `GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect` | KE 改写（W1，§3） | 同上 |
| 提交后不回滚 | `SkillLearningCommandTests.A_send_failure_leaves_the_committed_learning_for_relogin` | KE 保留 → F 类 | 直接发送 |
| | `CeraShopPurchaseTests.Send_failure_does_not_repeat_or_undo_the_committed_purchase` | KE 保留 → F 类 | 同上 |
| | `NpcShopPurchaseTests.Delivery_failure_neither_repeats_the_purchase_nor_rolls_back_its_commit` | KE 保留 → F 类 | 同上 |
| | `ItemSaleTests.Failed_delivery_does_not_retry_or_reverse_the_sale` | KE 保留 → F 类 | 同上 |
| | `EffectOperationReplayTests.A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request` | KE 保留 → F 类（F-101 同文件，TASK-25 一并改驱动） | 直接收件人的 `SendExplicitAsync` |
| | `MailboxCoreIntegrationTests.Rolled_back_or_failed_mail_never_notifies_and_delivery_errors_do_not_undo_commit` | KB 保留 | 注入在 `ProjectAsync` 抛出；`ProjectAsync` 翻转后仍可因事务守卫、执行器外抛出 |

分布：KE 改写 3（R2、R9、W1）＋ KE 保留 → F 类 18 ＋ KB 保留 7 ＋ 没有注入而保留 1 ＋ 删除 0 ＝ 29。
KE 保留的 18 条：DR-1 2 ＋ DR-2 2 ＋ DR-3 2 ＋ DR-4 3 ＋ DR-9 1 ＋ DR-12 1 ＋ 离开副本 1 ＋ 登录 1 ＋ 提交后不回滚 5 ＝ 18。
KB 保留的 7 条：DR-2 1 ＋ DR-4 1 ＋ DR-6 4 ＋ 提交后不回滚 1 ＝ 7。
对 F 类的 18 条，驱动方式（抛异常的替身换成登记点注入）交 TASK-25 与替身改写一起做；`logs/task-23-doubles.txt` 的清单不变。

### 6. 附录 E 与附录 G 逐行证据表（Required check 12；TASK-25 收口对账的输入）

| 行 | 本单元的生产处置 | 经执行器与发件箱的证据 |
|---|---|---|
| DR-1 选角 | 删 D7（关闭循环的吞咽）；工作流 `catch` 与补偿不动 | T1、R1（§4）；断线后的拆除：`PartyCharacterizationTests.A_disconnect_in_town_announces_the_departure_to_the_other_player`、TASK-23 N9 `OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once` |
| DR-2 进本 | 删 D5；XO-1 循环的 `catch`、隔离与回滚不动，注释改为登记点失败 | T2、R2；TASK-23 N8 XO-1 两条 |
| DR-3 两塔 | 删 D1～D4；进入的 `deliveryFailure` 与 `SendToExactAsync` 失败集合不动 | T3、T4（VC-3）、R3 4 条 |
| DR-4 怪物死亡、祭坛、清算 | 删 D10；三处主分支不动 | T5、T6、T7（VC-4）；R4（AC-011 第 4 条）；W2（AC-011 线级第 1 条）；`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`（AC-009）；`SetPlayResultHandlerTests.After_payment_lost_claim_cancel_or_missing_progress_never_releases_for_repayment`（清算的 KB） |
| DR-5 队伍水位线 | 删两把门；水位线逻辑不动 | T8、R5 2 条 |
| DR-6 配额刷新 | 不改 | T9 7 行、R6 7 行 |
| DR-7 GM Cera | 结果映射改为 `attempted`／`offline`，组装失败与 HTTP 层提醒异常上抛（裁定第 2 条） | `GmCeraReminderTests` 4 条（含 T10、R7）；`GmCeraHttpTests.CommittedGrantSurvivesReminderExceptionAndProjectionReadsCurrentWallet`（V6） |
| DR-8 房间机制 | 不改 | T11、R8 |
| DR-9 生物 | 删 D8；逐收件人 `catch` 不动 | T12、R9 |
| DR-10 清理通告 | — | TASK-23 N8 XO-5：`CleanupNotificationDeliveryTests.A_slow_owner_is_delivered_before_the_peers_and_the_party`、`An_owner_whose_delivery_fails_skips_the_peers_and_the_party`（复核仍在） |
| DR-11 回选角 | 删 D6；`catch`、清理通告与 `failure.Throw()` 不动 | T13、R10 |
| DR-12 初始 NOTI | 不改 | T14、R11 |
| DR-13 钱包、PvP 重试 | — | TASK-23 N14 `NotificationDeliveryTests.A_recipient_whose_composition_failed_is_never_composed_again`；`ProtocolModule` 不再有 `retryLimit`（TASK-23 §6，复核检索为 0） |
| DR-14 期限、重试队列、排空服务 | — | `OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`、`Exceeding_the_queue_byte_limit_closes_the_connection`；J1 守 `NotificationRetryQueue`、`NotificationRetryDrainService`（AC-017 检索为 0） |
| DR-15 handler 抛异常 | — | TASK-23 N6 `OutboxWireTests.A_failing_cmd_sends_what_it_kept_in_order_and_only_then_closes_its_source`；b1 `SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close`；b2 `A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close`；W7 `LifecycleExecutorWireTests.A_cmd_closing_its_own_source_connection_does_not_wait_on_itself` |
| DR-16 称号投影 | 不改 | T15 |
| DR-17 队伍流门 | 删两把门 | T16；`PartyTownProjectionDeliveryTests.ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot`（墓碑） |

附录 G：VC-3 ＝ T2、T3、T4；VC-4 ＝ T5、T6、T7；VC-6 ＝ `GmCeraReminderTests` 4 条与 V6。TASK-23 已落实的四项复核仍可检索：VC-1 `SelectCharacterSuccessCharacterizationTests`（金样本 a 不变）；VC-2 `OutboxWorkUnitTests`（N5）与 `logs/task-23-in-unit.txt`；VC-5 N14；VC-8 N9 与 `OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`。检索输出在 `logs/task-24-dr-evidence.txt`。

### 7. 评审后修改（TASK-24 主控方处置第 1～2 条）

| # | 用例 | 变化 | 钉住 |
|---|---|---|---|
| V6（再改写） | `GmCeraHttpTests.CommittedGrantSurvivesReminderExceptionAndProjectionReadsCurrentWallet` | 提醒抛出时的 500 另断言响应体：`message` 为「点券已到账，提醒未能发出；勿新建编号重发。」、`outcome` 为 `unknown`；`gm_cera_operations` 1 行；重放同一个 500，操作行仍 1、钱包仍 59。方法数不变 | 主控方处置第 1 条；F-107 |
| N46～N47 | `GmCeraReminderTests.An_account_whose_session_the_reminder_cannot_reach_makes_the_result_offline`（Theory 2 行：`gone`、`declined`） | 新增 | 发现 2：`Gone`／`Declined` → `offline`；变异 M22 下两行都红 |

数量：Protocol 1744 ＋ 2 ＝ 1746；合计 8229 ＋ 2 ＝ 8231；N 45 ＋ 2 ＝ 47；D 不变。附录 G 的 VC-6 因此是 `GmCeraReminderTests` 6 条与 V6。证据 `logs/task-24-review.txt`。

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

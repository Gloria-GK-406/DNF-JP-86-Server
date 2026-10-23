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

## TASK-25

- 日期 2026-10-23，基准提交 `1ee1c91e`（TASK-24 已提交）。T-006c：「发送即记录」替身改为经真实登记适配器与发件箱观察；18 条 F 类改为登记点注入；F-102 通知总线每收件人整批登记；F-101、F-104 复核。
- 分类沿用 TASK-23、TASK-24 的 E、R、S、W、F、K、KE，另加五类替身处置：**C1**（容器级，去掉覆盖、容器用真实适配器）、**C2**（构造注入，换成支撑提供的真实发送器／关闭器）、**C3**（不改）、**T8**（交 T-008）、**P**（投影替身，只分类）；注入另分 **G**（守卫：旧替身拒绝的路径，登记点照样拒绝，用例通过时不触发）。断言变化按 Detailed Plan §9 三类：**§9-1** 登记点语义的有意差异、**§9-2** 替身内部断言由真实组件承接、**§9-3** 类型名或成员名的机械替换。
- 共用支撑（tests/Support，只有一份实现）：`RecordedOutbound.cs`（真实 `GameplayOutboxes`、`OutboxNetworkMessageSender`、`OutboxConnectionCloser` 之上的记录与门面：发件箱交给发送端时记录条目，作业内读取时补上当前发件箱里未刷出的条目，回滚的单元不计；回调在登记点、编码之前运行；失败只在登记点编码器上注入）、`RecordedOutbound.Game.cs`（`Game`、`GameExplicits` 等 Protocol 类型的视图，只链接到能引用 Protocol 的 Application、Protocol、Host）、`OutboundRegistrationParts.cs`（从 `XoDelivery.cs` 拆出的 `XoSubmissions`、`XoConnections`、`FaultingEncoder`、`NameEncoder`，命名空间不变，XO 用例不改一字；`XoConnections` 加「首次见到即登记」，`FaultingEncoder` 加内层编码器与按连接判定的注入）。容器用 `RecordedOutbound.AddTo(services)` 换上真实适配器与按协议注册的编码器（`RegistrationEncoder`，拒绝文字与网络模块的注册表编码器一致）。
- 「首次见到即登记」的后果：用这种上下文表的用例里，F-83 的「连接已不在即丢弃」不会发生；需要它的 F-83 证据仍在 `OutboxRegistrationTests` 与 XO 用例里（它们不用首次登记）。
- 机械核对：`logs/task-25-diff-check.txt`；替身清单：`logs/task-25-doubles.txt`；注入清单：`logs/task-25-injections.txt`。

### 1. 数量（按实际运行的例数，Theory 按行计）

| 项目 | 基准 | 新增 N | 删除 D | 结果 |
|---|---:|---:|---:|---:|
| Application.Tests | 4535 | 2 | 1 | 4536 |
| Protocol.Tests | 1746 | 0 | 0 | 1746 |
| Networking.Tests | 179 | 0 | 0 | 179 |
| Host.Tests | 190 | 0 | 0 | 190 |
| 其余四个项目 | 1581 | 0 | 0 | 1581 |
| 合计 | 8231 | 2 | 1 | 8232 |

其余四个项目：HttpApi 54 ＋ Pvf 80 ＋ Persistence 477 ＋ Infrastructure 970 ＝ 1581。
合计：8231 ＋ 2 － 1 ＝ 8232；4536 ＋ 1746 ＋ 179 ＋ 190 ＋ 1581 ＝ 8232。N、D 逐条见 §7、§5。

### 2. 替身处置（与 `logs/task-25-doubles.txt` 一致）

- 基准 100 个类（`t23/doubles.py` 的 94 ＋ 主构造器换行漏数的 6）：C1 16 ＋ C2 54 ＋ C3 3 ＋ P 27 ＝ 100；T8 0。
- 改写的 70 个（C1 ＋ C2）终态：仍实现发送／关闭接口、三个方法都转给真实适配器的包装 65，只读门面 4（`ShopSender`、`LearningSender`、`ReusingConnectionSender`、`CaptureSender`），删除 1（`IllusionTowerPublicFlowTests.NoopConnectionCloser`，连同它的容器覆盖；容器用夹具的真实关闭器，没有用例读它）。65 ＋ 4 ＋ 1 ＝ 70。
- 包装里保留的「到达登记点的调用」记录（不是「发出了什么」的记录，原断言要读它）：`IllusionTowerRewardProjectionTests.FailingSender.Attempts`（重试复用同一个投递对象，`Assert.Same` 要求按调用记）、两塔 `Sender.Attempts`（连接号）、`DungeonCardPresentationCoordinatorTests.Seen`（登记点所在的作业与调用链）、`SelectCharacterLifecycleTests` 的 ledger 与 `StateMessageCount`、`ShopSender.Attempts`（见 §6.1）。
- C3 3 个：`ExplicitMessageDeliveryContractTests.RecordingSender`（被测的就是 `INetworkMessageSender` 默认方法的契约）、`SessionGenerationOwnershipTests.NoopSender`／`RecordingSender`（文件内只有声明，没有任何用例使用）。
- `CommonExperienceEffectTests.CallbackCloser` 改为转给真实关闭器的包装，但保留 `RequestCloseAsync`（按 ID 关闭）抛 `InvalidOperationException("ID-only close is forbidden here.")`：它是测试自身的不变量检查——这个流程只允许按精确上下文关闭，走了按 ID 的关闭就让用例失败（TASK-24 V13：生产中这条路不被调用）；不是失败注入。主控方处置第 2 条接受。
- P 27 个只分类（裁定第 2 条）：记录型 9、空操作型 7、失败注入型＋记录型 8（含抽象基类 `ProjectionEventService`）、失败注入型 1、阻塞型＋记录型 2（`PartyNotificationTests.BlockingNotificationBus`、`PartyTownProjectionDeliveryTests.BlockingFirstRosterNotificationBus`）；9 ＋ 7 ＋ 8 ＋ 1 ＋ 2 ＝ 27（逐类见 `logs/task-25-doubles.txt` §3）。两个阻塞型逐个核对：文件内都只有声明、没有任何用例使用（用它们的门反例已在 TASK-24 K2 随门删除），按空转登记交 T-008（F-111）。失败注入型都是「投影或组装失败」（KB），不改。

### 3. 18 条 F 类（TASK-24 §5，驱动替换；断言除 §6.1 的 3 处 `Attempts` 外不变）

| DR | 用例 | 注入 旧 → 新 | 异常 |
|---|---|---|---|
| DR-1 | `SelectCharacterLifecycleTests.RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection` | 替身在首条直接消息为 `TrailingEquipmentLockSnapshotMessage` 的 `SendExplicitAsync` 抛出 → 编码器对该投递的直接消息失败 | `IOException("required delivery failed")` |
| | `…PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt` | 首条直接消息为 `PvpLobbySnapshotMessage` 的显式投递 → 同上 | `IOException("PvP direct delivery failed")` |
| DR-2 | `PartySelectDungeonHandlerTests.A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town` | `Sender(failRequiredConnection)` 对成员为直接收件人的显式投递抛出 → `FailDirect(Member)` | `InvalidOperationException("required delivery failed")` |
| | `PartyMoveMapHandlerTests.Publication_failure_logs_the_committed_room_and_preserves_the_exception` | `Sender { Failure }` 显式投递抛出 → 编码器对显式投递的直接消息抛同一个异常实例 | 用例自己的 `failure` 实例 |
| DR-3 | `BossTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session`、`IllusionTowerProjectionConcurrencyTests` 同名 | `Sender(connection => …)` 按直接收件人抛出 → `Encoder.Fail((连接, 消息) => 直接消息 ? failure(连接) : null)` | 用例给的异常 |
| DR-4 | `MonsterDeathHandlerTests.Death_send_failure_cannot_erase_the_committed_shared_clear_fact`、`GetItemHandlerTests.Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact` | `ThrowingSender` 三个方法都抛 → 编码器对一切直接消息失败 | `IOException("send failed")` |
| | `IllusionTowerRewardProjectionTests.Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | `FailingSender { FailuresRemaining = int.MaxValue }` 每次显式投递抛 → 每次尝试在登记点对直接消息失败；「两次尝试」是同一注入连续命中两次（`Attempts` 记到达登记点的两次调用） | `IOException("Injected committed-batch delivery failure.")` |
| DR-9 | `CreatureProjectionDeliveryTests.Owner_send_failure_closes_owner_and_reports_failure` | `Sender { Failing = owner }` → `outbound.Fail(owner, 全部消息)` | `IOException("send failed")` |
| DR-12 | `OnlineUserServiceTests.FailedInitialNotice_RemovesExactRegisteredIdentity`（文件名；类名是 `UserServiceTests`） | `RecordingInitialSender(fail: true)` 的 NOTI 抛 → 编码器对 NOTI 失败 | `IOException("initial notice failed")` |
| 离开副本 | `LeaveDungeonTests.Failed_exit_delivery_releases_the_transition_without_repeating_the_exit` | `BeforeSend = _ => throw …` → `Failure = () => …`，编码器对显式投递的直接消息失败 | `IOException("send failed")` |
| 登录 | `GameLoginTests.MessageHandler_SendFailureConsumesTheSuccessButRemovesItsPublishedIdentity` | `RecordingSender(failAtAttempt: 2)` → 第 2 次发送的消息在登记点编码失败 | `IOException("send failed")` |
| 提交后不回滚 | `SkillLearningCommandTests.A_send_failure_leaves_the_committed_learning_for_relogin` | `BeforeSend = () => throw …` → `Failure = () => …`，单条发送在登记点失败 | `IOException("closed socket")` |
| | `CeraShopPurchaseTests.Send_failure_does_not_repeat_or_undo_the_committed_purchase`、`NpcShopPurchaseTests.Delivery_failure_neither_repeats_the_purchase_nor_rolls_back_its_commit`、`ItemSaleTests.Failed_delivery_does_not_retry_or_reverse_the_sale` | `BeforeSend = () => throw …` → `FailSends(() => …)`，单条发送与整批登记在登记点失败；`Attempts` 口径改义见 §6.1 | `IOException("closed socket")`／`IOException("test connection failure")` |
| | `EffectOperationReplayTests.A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request` | `FailExplicit` 由「替身抛出」改为编码器对显式投递的直接消息失败 | `IOException("Fixture Explicit publication failed after settlement.")` |

加法：DR-1 2 ＋ DR-2 2 ＋ DR-3 2 ＋ DR-4 3 ＋ DR-9 1 ＋ DR-12 1 ＋ 离开副本 1 ＋ 登录 1 ＋ 提交后不回滚 5 ＝ 18。`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`（AC-009）方法体与基准逐字相同，断言 8 行多重集合相同（`logs/task-25-diff-check.txt` 末尾）。

### 4. 其余注入（§6.2，`logs/task-25-injections.txt`）

- 登记点注入 79 处（第 1 轮评审后；之前 78，多出的 1 处是 `GameLoginTests.RecordingSender` 补上的守卫）：KE 18（由开关或构造参数打开，都是直接收件人的编码失败，TASK-24 §2 来源表「直接收件人、`SendAsync`、`SendBatchAsync` 的编码或端点失败」一行）＋ G 61（守卫：旧替身拒绝的路径——例如「只接受显式投递」的替身收到 CMD／NOTI——在登记点照样以原异常拒绝；用例通过时它们不触发，触发即说明生产改走了旧替身不允许的路径）。
- 打开注入的用例 33 个：18 条 F 类 ＋ 15 条其他（`ShopSender.FailBatch` 8，含 `JoustIntegrationTests` 一条；`FailExplicit` 3；`FailingSends` 1；`SelectCharacterLifecycleTests.StaticFailure_DoesNotInvokeDailyRefillMutation` 1；`IllusionTowerRewardProjectionTests` 另两条 2；8 ＋ 3 ＋ 1 ＋ 1 ＋ 2 ＝ 15）。全部是 KE，生产中登记点的编码失败仍会抛出，所以都保留，断言不变；旧 `FailBatch`「先记录、再抛出」改为「登记点失败、不登记」后，读失败批次的断言 0 处（失败之后各用例都先 `Clear()` 再读）。
- KB（投影或组装失败）只在 P 类替身里，按裁定第 2 条不改。
- 「不再抛出」类：§6.2 范围内 0 条。唯一一条删除（§5）是 C1 改写时发现的传输模型替身，不在 §6.2 的开关里。
- `EquipmentEnchantHandlerTests.A_partial_notification_failure_can_be_replayed_without_a_second_debit`：`BeforeSend` 按 `Attempts == 2` 抛 → `FailingSends` 在登记点注入（主控方处置第 3 条），连同 §6.1 的两处断言一起改。

### 5. 删除（D ＝ 1）

| 用例 | 类 | 保护意图 | 承接 |
|---|---|---|---|
| `CollectionBoxNetworkSettlementTests.Refusal_is_not_delivered_to_a_connection_replaced_before_transport_lookup` | 不再抛出 | 拒绝帧绑定登记时的那个物理连接，连接号被复用后不会送到替代连接。用例断言调用点抛 `IOException`，这个异常只来自替身对「物理连接已被替换」的模拟；登记后由发送端判 `Stale`（RQ-035），登记点不再抛出 | 同文件改写后的 `Ack_cannot_follow_a_reused_connection_id_after_the_inventory_batch`（同一个 `SendBatchAsync(context.Connection)` 发送点，连接表在批次之后改指替代连接，确认回执仍登记给原上下文）；`OutboundSenderTests.An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one`；`OutboundRealSocketTests.An_entry_for_an_equal_but_not_identical_context_is_stale` |

### 6. 改写（方法不增不减；只列断言有变化或位置有变化的）

#### 6.1 §9-1：F-102 引起的改义（主控方处置第 1 条：上限 30 处，本单元 24 处）

`Batches` 的含义：基准上 `ShopFixture` 的通知投影经 `SendAsync` 逐条登记，`Batches` 里只有 handler 自己的整批登记，在这些用例里就是「重放当前状态」的那一批（库存快照）。F-102 之后投影也是每收件人一批，`Batches` 里多出投影批。改写把原断言限定到重放批（`EffectRequestTransport.IsStateReplay`：批里有 `InventorySnapshotMessage` 类型的消息；该类型是 Protocol 的 internal，按全名取得一次，改名即在取得处失败），保住「重放只登记一批」的区分力；被拒请求的三处（#10、#17、#18）改为比较请求前后的 `Batches.Count`；另在首个请求产生投影的用例里加一条断言，说明投影（确认回执连同它的增量）作为**一整批**登记（RQ-031(d)）。

| # | 文件:行（改后） | 旧 → 新 | 依据 |
|---|---|---|---|
| 1 | `EquipmentAmplificationTests.cs:126-127` | `Assert.Single(f.Sender.Batches)` → `Assert.Single(f.Sender.Batches, IsStateReplay)`；加：含 `ReinforceItemResponse` 的批唯一、3 条消息 | F-102 |
| 2 | `EquipmentOutworldHandlerTests.cs:35-36` | 同上（`PurifyItemResponse`，3 条） | F-102 |
| 3 | `LotteryOpeningTests.cs:46-47` | 同上（`LotteryOpeningResponse`，3 条；3 行 Theory） | F-102 |
| 4 | `LotteryOpeningTests.cs:97-98` | 同上（4 条） | F-102 |
| 5 | `LotteryOpeningTests.cs:114-115` | 同上（3 条） | F-102 |
| 6 | `LotteryOpeningTests.cs:191-192` | 同上（4 条，含 `PremiumServiceStateMessage`） | F-102 |
| 7 | `VendingMachineTests.cs:51-52` | `Assert.Equal(2, Batches.Count)` → `Assert.Equal(2, Batches.Count(IsStateReplay))`；加：含 `VendingMachineResponse` 的批 2 个（两次真实使用各一批投影） | F-102 |
| 8 | `VendingMachineTests.cs:150-151` | `Assert.Single(Batches)` → `Assert.Single(Batches, IsStateReplay)`；加：投影批唯一、2 条消息 | F-102 |
| 9 | `EquipmentEnchantHandlerTests.cs:43-44` | 同 #1（`EnchantByBeadResponse`，3 条） | F-102 |
| 10 | `EquipmentEnchantHandlerTests.cs:146` | `Assert.Empty(Batches)` → 被拒请求前记 `var batchesBefore = Batches.Count`，之后 `Assert.Equal(batchesBefore, Batches.Count)`（被逐出的序号什么批次都不登记；前面 130 次正常附魔的投影批不计）。第 1 轮评审后修改，原为 `DoesNotContain(Batches, IsStateReplay)` | F-102 |
| 11 | `EquipmentEnchantHandlerTests.cs:192-193` | `Assert.Single(Batches)` → `Assert.Single(Batches, IsStateReplay)`；加：含 `EnchantByBeadResponse` 的批 2 个（序号 7、8 两次附魔） | F-102 |
| 12 | `EquipmentForgingHandlerTests.cs:35-36` | 同 #1（`ForgingResponse`，3 条） | F-102 |
| 13 | `HerasEntryCouponTests.cs:147-148` | `var replay = Assert.Single(Batches)` → `Assert.Single(Batches, IsStateReplay)`；加：含 `QuestClearCouponResponse` 的批唯一、2 条消息（3 行 Theory） | F-102 |
| 14 | `MagicSealHandlerTests.cs:117` | `Assert.Single(Batches)` → `Assert.Single(Batches, IsStateReplay)`（投影批只有两条增量，没有确认回执，不加第二条） | F-102 |
| 15 | `EquippedUpgradeTicketTests.cs:98-99` | 同 #1（`ReinforceItemResponse`，4 条；3 行 Theory） | F-102 |
| 16 | `EquipmentReinforcementHandlerTests.cs:153-154` | 同 #1（3 条） | F-102 |
| 17 | `EquipmentReinforcementHandlerTests.cs:172` | 同 #10（改了请求的同序号被拒，什么批次都不登记） | F-102 |
| 18 | `EquipmentReinforcementHandlerTests.cs:189` | 同 #10（换角色后不重放前一角色的回执） | F-102 |
| 19 | `EquipmentReinforcementHandlerTests.cs:313-314` | 同 #1（3 条；2 行 Theory） | F-102 |
| 20 | `NpcShopPurchaseTests.cs:240-241` | `Assert.Equal(1, shop.Sender.Attempts)` 文字不变，口径改义；加 `Assert.Empty(shop.Sender.Messages)`（第 1 轮评审后） | F-102 |
| 21 | `ItemSaleTests.cs:244-245` | 同上 | F-102 |
| 22 | `CeraShopPurchaseTests.cs:366-367` | 同上（`shop.Host.Sender`） | F-102 |
| 23 | `EquipmentEnchantHandlerTests.cs:88` | `Assert.Single(shop.Sender.Messages)` → `Assert.Empty(shop.Sender.Messages)` | F-102、RQ-031(d) |
| 24 | `EquipmentEnchantHandlerTests.cs:94` | `Assert.Equal(7, Messages.Count)` → `Assert.Equal(6, Messages.Count)` | F-102、RQ-031(d) |

加法：`Batches` 19（#1～#19）＋ `Attempts` 3（#20～#22）＋ 部分通知失败 2（#23、#24）＝ 24 ≤ 30。另：`NotificationDeliveryTests.A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 的登记列表断言（§8）是计划内唯一一处有意改义，不计入这 24 处。新增断言行 18 处（#1～#9、#11～#13、#15、#16、#19 各一行，#20～#22 各一行；#3、#13、#15、#19 的 Theory 行数不另计）。

`Attempts` 的口径（#20～#22）：
- 旧：`ShopSender.Attempts` 数到达登记点的 `SendAsync` 调用。这三个用例里 handler 的回执经通知投影发出，总线逐条 `SendAsync`；第一条就在登记点失败，总线把该收件人记为 `Failed` 不再继续，所以是 1。`NpcShopPurchaseTests` 与 `CeraShopPurchaseTests` 的投影各组装 3 条消息，注入一旦失效，三条都会到达登记点，`Attempts` 是 3，所以 `Assert.Equal(1, …)` 同时钉住「失败发生在第一条、之后不再尝试」；`ItemSaleTests` 的投影只有 1 条（`SellItemResponse`），注入失效时也是 1。
- 新：`Attempts` 数到达登记点的单条发送与整批登记调用（`SendAsync` ＋ `SendBatchAsync`，显式投递不计）。F-102 之后总线对一个收件人只登记一批，无论这一批在登记点失败与否都是 1。所以 `Assert.Equal(1, …)` 只剩「不重试」的含义；「失败生效、注入点之后什么都没登记」的区分力由新加的 `Assert.Empty(…Sender.Messages)` 承接——注入失效时三条用例的投影都会登记，这一行变红（M7 局部重跑：3 条全红，`logs/task-25-mutations.txt` round 4）。第 2 轮执行时写的「基准上也一样」只对 `ItemSaleTests` 成立，已在第 1 轮评审后改正。

部分通知失败（#23、#24）：旧注入在第 2 条消息上抛出，前 1 条已登记，所以首个请求后 `Messages` 有 1 条、重放后 1 ＋ 6 ＝ 7 条；F-102 之后一个收件人的消息是一整批，注入改为整批在登记点失败（`FailingSends`），首个请求后 0 条、重放后 0 ＋ 6 ＝ 6 条。用例要证明的「通知失败之后可重放、不二次扣费」不变（`Assert.Equal(1, shop.Count(1000))` 两处不变）；「部分」二字已无对象，名字随 T-008 改，登记 F-87。

#### 6.2 §9-2 与断言所在行的其他变化

| 文件 | 变化 | 承接／说明 |
|---|---|---|
| `NpcShopPurchaseTests.cs`（`ShopSender.SendBatchAsync`） | 删 `Assert.Equal(connection.EndpointKind, registration.Endpoint)`、`Assert.All(frames, frame => Assert.InRange(frame.Length, 15, 1_048_576))` | §9-2：端点不符由 `RegistrationEncoder.EncodeBatch` 在登记点抛出（文字同 `RegistryOutboundEncoder`）；帧长上限由 `GameServerFrameEncoder.Encode` 与 `EncodeBatch` 的帧长检查承接，队列字节上限由 `OutboundSender` 承接 |
| `CommonExperienceEffectTests.cs`（`CallbackCloser`） | `Assert.Equal(CancellationToken.None,ct); Expected = expected;` → `Assert.Equal(CancellationToken.None,ct);` 单独一行，`Expected` 改为在登记之后取自关闭条目 | 断言表达式不变；`Expected` 由「调用参数」变为「登记了关闭的那个上下文」，更严（关闭被丢弃时为 null） |
| `CharacterDeathHandlerTests.cs`（`Sender.SendExplicitAsync`） | 同一行里的 `Deliveries.Add(delivery); return …` 改为转给真实发送器 | 断言 `Assert.Equal(Current.ConnectionId, connectionId)` 不变，仍在登记点 |
| `DungeonLoadingProjectionDeliveryTests.cs`（`WeaknessSender`）、`WeaknessProjectionTests.cs`（`Sender`）、`ItemUsageProjectionTests.cs`（`Sender`） | `Assert.IsType`／`Assert.IsAssignableFrom` 从发送方法移到读取视图的属性里 | 断言表达式不变，作用对象由「被调用的消息」变为「登记了的消息」 |

#### 6.3 位置变化：作业外调用放进作业（§5.3，断言不变）

`HerasEntryCouponTests.Malformed_requests_finish_with_refusal_without_touching_the_bag`（2 行）、`ChannelConnectHandlerTests.HandleAsync_SendsTheDateBasedConnectKeyToTheSameConnection`、`ChannelControlMessageHandlerTests` 3 条、`GameExitMessageHandlerTests.HandleAsync_AcknowledgesTheExitOnTheSameConnection`、`ExitFlushHandlerTests.TaggedIdReport_AcknowledgesAndLogsIdAndFlag`／`TaggedIdReport_ToleratesAnUnattributedConnection`／`RosterFollowUp_FlagsTheSentinelButNotOtherValues`、`SelectDungeonHandlerTests.Refusals_preserve_the_actual_reason_and_allow_retry`（15 行）、`ReturnToSelectionTests.The_return_batch_uses_the_reference_order_and_cmd_envelope`（2 行）。方法 11 个、运行例 27 个（加法见 `logs/task-25-verify.txt` V10）。都是 handler 或投递方法在作业外被直接调用，登记点要求在作业里；没有一条进入 `violations.py` 的期望表。

#### 6.4 作业内读取

C2 门面在作业内读取时包含当前发件箱里尚未刷出的条目（回滚的单元不计），与旧替身「调用即可见」的时机一致；这类读取证明不了「作业结束才刷出」（RQ-031(a)）与「回滚丢弃」在刷出时的效果。作业之后读取的改写用例（`ShopFixture` 全部 106 个文件、`SkillLearningCommandTests`、两个 Channel 用例、`CardRewardTests`、`GmEventsHttpTests` 等）读到的都是发件箱交给发送端的条目：变异 M3（刷出时丢弃全部条目）下抽查的 `NpcShopPurchaseTests`、`SkillLearningCommandTests`、`ChannelControlMessageHandlerTests`、`CardRewardTests`、`GmEventsHttpTests` 都变红；后四个中前三个的基准版本在同一变异下保持绿（旧替身在调用点记录，看不到刷出）。`GmEventsHttpTests` 的基准版本在 F-102 之后不加变异也红（总线自己查连接，宿主的连接表里没有用例的合成连接），所以做不了对照；`ShopFixture` 的基准版本无法单独编译，也没有对照（`logs/task-25-mutations.txt`）。
- 工作单元：领域事务从容器里的 `GameplayOutboxes` 开工作单元。C1 用例的容器就是 `RecordedOutbound.AddTo` 换上的那一个，所以回滚丢弃（RQ-031(b)）对它们生效；C2 包装各自持有独立的 `RecordedOutbound`，与被测对象容器的工作单元不是同一个发件箱，回滚丢弃对它们不生效。旧替身两种情况都看不到回滚，这一点没有变弱；但这些 C2 用例不能作为 RQ-031(b) 的证据（F-112，交 T-008）。回滚丢弃本身的证据仍在 `OutboxWorkUnitTests`（TASK-23 N5；M5 下 2 条变红，§11）。

### 7. 新增（N ＝ 2）

| # | 用例 | 钉住 |
|---|---|---|
| N1 | `NotificationDeliveryTests.A_recipient_whose_last_message_cannot_be_encoded_registers_none_of_its_messages` | RQ-031(d)、F-102：三条消息中最后一条不可编码，该收件人一条都不登记、记 `Failed` 与一条 `Warning`（`InvalidDataException`），连接不被关闭，其他收件人照常登记 |
| N2 | `NotificationDeliveryTests.A_recipient_with_several_messages_is_resolved_once_and_registered_as_one_batch` | F-102：多条消息的收件人只查一次连接上下文，按组装顺序登记为一条批次 |

两条都先 RED 后 GREEN（`logs/task-25-f102-stop.txt` §1、§2）；N2 的「只查一次」由 M2 守着。

### 8. F-102

- 生产改动只在 `Dfo.Server.Application/Notifications/NotificationBus.cs`：加一个带 `INetworkConnectionContexts` 的构造器（没有上下文表的容器视为连接都不在，丢弃并记 Debug，结果 `Delivered`，同 F-83）；每个收件人组装之后只查一次上下文，经公开接缝 `INetworkMessageSender.SendBatchAsync` 整批编码、登记一条批次（裁定第 3 条 A）。`INotificationBus.cs` 的注释已经是「composed, encoded and registered」，没有要改的句子；注册处 `NotificationDispatchingExtensions` 不用改（容器按可解析的最长构造器选）。
- 改写的那一条断言：`A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 的登记列表 `[(First, "first part"), (Second, "for-…")]` → `[(Second, "for-…")]`，依据 RQ-031(d)「只丢弃该收件人的这批条目」、F-102；方法注释同步。
- V12：Game 与 Channel 编码器都在帧编码器里先检查帧长上限，通知产不出超限帧，换成整批编码没有新增的拒绝。
- 引起的其余断言改义见 §6.1（24 处，经主控方处置第 1 条放宽上限后改写）。

### 9. F-101

`EffectOperationReplayTests` 的三条用例（`Later_readonly_transactions_do_not_replace_the_primary_footprint_or_commit_state`、`Second_primary_command_cannot_pay_or_overwrite_the_first_committed_entry`、`A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request`）都经 `ShopFixture`：回调 `f.Sender.BeforeExplicit` 由 `RecordedOutbound` 的包装在调用真实登记适配器之前执行——就是登记点，仍在入站作业里、主命令之后；第三条的 `FailExplicit` 改为编码器对直接消息失败。三条断言行多重集合与基准相同（文件不在 diffcheck 的改动列表里），全量与 ×10 通过。F-101 关闭。

### 10. F-104

19 个用到 `DrainOnceAsync` 的文件逐个判定（宿主是否开着派发组循环）：
- Application 17 个：都是普通 `ServiceProvider` 容器，托管服务不启动，`DrainOnceAsync` 是唯一的排空者，返回时投递作业已完成并刷出（RQ-005），不改。`DomainEventDispatchTests`／`.Executor` 自己调 `Dispatcher.RunAsync` 的用例，组循环在 `DrainOnceAsync` 之前已停或 `DrainOnceAsync` 本身就是被测者，不改。
- Host `GmEventsHttpTests`：TASK-23 S14 已在读帧前加屏障；本单元在另外两处「`DrainOnceAsync()` 之后紧接 `Clear()`」处补同一屏障（`:291`、`:305`），防止组循环正在处理的投递在 `Clear()` 之后才登记、混进下一段读取。断言不变。
- Host `GmOfficialMailHttpTests.ReminderExceptionAfterCommitNeverRegrantsAndSuccessRemainsQueryable`：宿主开着组循环，`DrainOnceAsync()` 之后紧接读 `notices.Count`；加同样的执行器屏障（`Settled`），断言不变。
- F-104 关闭。

### 11. 第 1 轮评审后修改（主控方处置第 1～5 条）

| # | 位置 | 修改 | 证据 |
|---|---|---|---|
| 1 | `NpcShopPurchaseTests.cs:241`、`ItemSaleTests.cs:245`、`CeraShopPurchaseTests.cs:367` | 各加 `Assert.Empty(…Sender.Messages)`：该收件人在注入点之后什么都没登记；§6.1 #20～#22 的推导改正 | M7 局部重跑（round 4）3 条全红，M7 合计 15 ＋ 3 ＝ 18 条全红 |
| 2 | `EquipmentEnchantHandlerTests.cs:146`、`EquipmentReinforcementHandlerTests.cs:172`、`:189` | 被拒请求前后比较 `Batches.Count`（§6.1 #10、#17、#18） | Inventory 全部类过滤运行通过 |
| 2 | `EffectRequestTransport.cs`（`IsStateReplay`） | 按类型判定：`InventorySnapshotMessage` 的 `Type` 按全名从 Protocol 程序集取得一次（`throwOnError: true`），不再比较类型名字符串 | 同上 |
| 3 | `follow-ups.md` F-112 | C2 包装各自持有独立发件箱，这些用例证明不了「事务回滚但消息已发」不会发生，不能作为 RQ-031(b) 的证据；交 T-008，TASK-26 收口引用 | — |
| 4 | `GameLoginTests.cs`（`RecordingSender`） | 补 `outbound.RefuseExplicit(() => new InvalidOperationException("This handler does not use explicit-recipient delivery."))`，与同类文件一致（旧替身就是这样拒绝显式投递） | `GameLoginTests` 过滤运行通过 |
| 5 | M5 | 加跑 `OutboxWorkUnitTests`：2 条变红（`Of_two_writable_transactions_only_the_one_that_commits_is_flushed`、`Entries_of_a_transaction_whose_outcome_is_uncertain_are_dropped`），是 RQ-031(b) 的证据 | `logs/task-25-mutations.txt` round 4 |

断言行：新增 3 行（#1），改写 3 行（#2 的三处 `DoesNotContain` → `Equal`）；方法不增不减。

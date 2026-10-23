# T-009 日志面与保护用例收口（TASK-31：T-009b）

- 日期 2026-10-24。基准提交 `122576d5`（TASK-30）加上 TASK-31 未提交的改动。本单元提交后即为代码冻结点（§8）。
- 用途：判定 Plan T-009 中留在 A 的日志面（F-30、F-31、F-121）、三条保护用例（F-32、HTTP 根的继承、AC-009 的 A 一半第 ③ 条）、F-33 与测试名整理（F-87 改名一半、F-111、F-123）是否完成；TASK-32 的 S5 判定口径（§4）、TASK-33 的对账与 TASK-34 的测试数量加法（§7）从这里取输入。
- 写法：仿 `t009-criteria.md`，只写「要求 → 实现位置 → 证据 → 状态」。未关闭事项只引 F 编号，内容在 `follow-ups.md`；推导过程在 `tasks/TASK-31.md`。
- 文件:行 指 TASK-31 完成时的工作区；路径相对 `Server/`，另有注明的除外。日志路径相对 `verification/`。
- 本单元关闭：F-30、F-31、F-32、F-33、F-87、F-111、F-121、F-123。

## 1. F-30：作业体失败只记一次 Error

### 1.1 普查

`src` 中全部 70 处 `Error`／`Critical` 日志调用逐处归类（`logs/task-31-producers.txt`）：执行器自己的 4、作业体内的 51、与作业无关的 3、`CommandLoggingBehavior` 1（主控方裁定第 5 条，不动）、「在作业外等待作业结果并在 catch 里记录」的 11；4 ＋ 51 ＋ 3 ＋ 1 ＋ 11 ＝ 70。

| 位置（改后行号） | 规划表 | 改法 |
|---|---|---|
| `src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs:91`（Channel）、`:187`（Game） | 第 1 行 | 作业体失败 → `Debug`，异常与帧字段保留；拒绝入队仍 `Error` 并重抛 |
| `src/Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs:255` | 第 2 行 | 作业体失败 → `Debug`（消息与 TraceId 不变）；其他异常仍 `Error` |
| `src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:301` | 第 3 行 | 作业体失败 → `Warning`，不附异常；扫描与持久化在执行器外的失败仍 `Error` 带异常（`:303`） |
| `src/Dfo.Server.Protocol/Game/IllusionTowerProjectionDelivery.cs:844`、`BossTowerProjectionDelivery.cs:893` | 第 4 行 | 作业体失败 → `Warning`，不附异常（「服务继续」是额外事实） |
| `src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:76`、`:228`（`RunTimerAsync` 的两个 `failed` 回调） | 第 5 行 | 作业体失败 → `Debug` |
| `src/Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs:158` | 第 6 行 | 最后一次尝试的作业体失败 → `Warning`，不附异常（「重试用尽」是额外事实）；每次重试的 `Warning` 不在范围 |
| `src/Dfo.Server.Application/Events/JoustMaintenanceService.cs:76` | 第 8 行列为作业体内；实际是 `ExecuteAsync` 包住 `TickAsync` 的 catch | 作业体失败 → `Warning`，不附异常（「下个 tick 重评」是额外事实） |
| `src/Dfo.Server.Networking/Runtime/TouchSocketTransportDrain.cs:48` | 规划表外，新认出 | 作业体失败 → `Debug` |

规划表其余各行的核实：第 5 行的两个计时器作业体自己 catch（`:160`、`:278`）在作业体内，不改；第 7 行「只 catch 取消的循环」确认没有 `Error`；第 8 行除 Joust 外的各处（`AltarScheduler` 1、`DungeonPartyWipeService` 3、`TournamentRewardSweepService` 1、`MarriageWeddingSlice` 1）都在作业体内。没有一处需要改结构才能区分「作业体失败」与「拒绝入队」：11 处拿到的都是作业抛出的同一个异常对象（`await` 原样传出，`EndedAJob` 认得出）。

生命周期关闭回调（V5）：`OnTcpClosed` 里作业失败的异常交给 TouchSocket 后，`TcpSessionClientBase` 经 `EasyTask.SafeWaitAsync` 把它收成 `Result` 丢弃，`PluginManager` 不记日志（TouchSocket 4.3.1 的 IL，`logs/task-31-verify.txt` V5）。不存在第二条记录，这一处不改。

两种降级的取舍：只复述失败的降为 `Debug`（排查时打开 Debug 能对照帧号、TraceId）；带额外事实的降为 `Warning` 且不附异常（堆栈已在执行器那一条里）。拒绝路径（执行器已停止、深度超限）不变：执行器没有为它记失败。

### 1.2 执行器失败日志带连接

`src/Dfo.Server.Application/Execution/GameplayExecutor.cs:314`：失败日志在 `source {Source}` 之后加 `connection {SourceConnection}`（无连接时为空）。开始、完成、取消三条模板不变；失败行的解析式 `Gameplay (.+?) failed: ` 不受影响。用例 `GameplayExecutorLoggingTests.The_failure_log_names_the_source_connection`。

### 1.3 改写的既有日志断言（主控方裁定第 1 条；RQ-083）

| 位置 | 旧断言 | 新断言 | 保护意图 |
|---|---|---|---|
| `OutboxWireTests.A_failing_cmd_sends_what_it_kept_in_order_and_only_then_closes_its_source` | `Assert.Equal(1, server.Log.Count("Game handler failed"))` | `Assert.Equal(1, server.Log.Count("Gameplay inbound:GameExit failed", LogLevel.Error))` 与 `Assert.Equal(0, server.Log.Count("Game handler failed", LogLevel.Error))` | 这次失败被记了下来；改后还要求只在执行器记一次 |
| `OutboxWireTests.A_cmd_whose_message_cannot_be_encoded_fails_and_its_earlier_frames_arrive_before_the_close` | 同上 | 同上 | 同上 |
| `InventorySlotMappingFailureIntegrationTests` 的 `WaitForErrorAsync` | `logs.ErrorsContaining("Game handler failed")` | `logs.ErrorsContaining("Gameplay inbound:")` | 等到的那条 `Error` 带原异常；异常类型与消息断言不变 |

V6 检索没有别的断言生产者 `Error` 或发送端 `Warning` 的位置（`logs/task-31-verify.txt` V6）。`LifecycleLog` 加了按级别计数的 `Count(fragment, level)`，已有成员不改。线上字节、帧序与连接开关的断言一处不改。

### 1.4 用例

`DomainEventDispatchTests.A_group_whose_failure_record_fails_backs_off_with_a_warning_and_one_error`、`ScheduledTaskExecutorTests.An_exhausted_task_is_logged_once_by_the_executor_and_warned_without_its_stack`（§2.4 的定时生产者取它：两塔扫描的 `ExecuteAsync` 要驱动 500 ms 周期定时器而没有现成夹具，卡牌计时器的作业体自己接住异常、作业几乎不会失败）、`HttpGameplayTests.A_gm_cmd_that_fails_is_logged_once_by_the_executor_and_the_middleware_only_at_debug`；入站一条由 §5.1 的用例承担。

状态：满足。命令层 `CommandLoggingBehavior` 那一条随 B（附录 H 第 0 行），列入 TASK-34 的遗留问题与 SC-12 的过渡机制清单（主控方裁定第 5 条）。

## 2. F-31：「执行器已停止」带停止原因

| 位置 | 改后 |
|---|---|
| `src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:288` | 附异常；`InnerException` 为空（正常停机）仍 `Information`，非空（消费循环故障）为 `Warning` |
| `src/Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs:109` | 附异常，级别仍 `Warning` |
| `src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:373` | 同第一处的规则 |

用例 `DomainEventDispatchTests.A_group_loop_ended_by_a_faulted_executor_logs_the_cause_as_a_warning`（测试内的队列替身按消费循环故障的形状拒绝事件 CMD，`GameplayExecutorStoppedException` 的构造器是公开的）；`DomainEventDispatchTests.Group_loops_end_quietly_when_the_executor_has_already_stopped` 的断言不改、照旧通过。状态：满足。

## 3. F-121：发送端对「连接已不在」降级

- 判定：`src/Dfo.Server.Networking/Outbound/OutboundSender.cs:236` 的 `IsGone`，在两处以 `Failed` 结束队列之前（`:206` 同步抛出、`:228` 等待抛出）再问一次 `transport.Resolve`；为 null 即「连接已不在」。不按异常类型判定，与 `ProcessAsync` 判 `Stale` 用同一个问法。只看 `Failed`：`TimedOut`、`OverLimit`、有序等待超时是服务端的判定，照旧 `Warning`。
- 记法：判定作为参数从 `ConnectionQueue.End` 传到 `Complete` 与 `FailConnection`；「连接已不在」时两行为 `Information`，模板带固定后缀 `; the connection was already gone`（`:257`、`:293`），前缀不变。结果、丢弃余下条目、观察者收到的 `Failed` 与关闭都不变；`OutboundOutcome`、`OutboundDeliveryReport`、`IOutboundDeliveryObserver` 不改。
- 用例：`OutboundSenderTests.A_failed_send_to_a_connection_that_is_already_gone_is_logged_as_information`、`OutboundSenderTests.A_failed_send_on_a_connection_that_is_still_there_stays_a_warning`、`OutboundSenderTests.A_timed_out_send_stays_a_warning_even_when_the_connection_is_gone`。后两条在基准上就通过，它们守的是判定不被写宽（变异 MF7a、MF7c，`logs/task-31-mutations.txt`）。
- 正式全量中的表现（`logs/task-31-build-test.txt` 文首）：发送端的 7 行与 TASK-30 相同（`Failed` 6 ＋ `TimedOut` 1）；其中 4 行 `Failed` 换成带后缀的 `Information`，2 行仍是 `Warning`。单独串行跑 Networking 时，`Failed` 的 5 对出自 `LifecycleExecutorWireTests.A_send_to_a_resetting_client_does_not_hold_the_executor` 的 5 次重置（第 1 次 `Warning`、其余 4 次 `Information`），`TimedOut` 出自 `OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once`。对端重置时，发送失败与传输层摘除登记谁先发生没有保证，所以同一种重置有时得 `Warning`；这是判定按事实问出来的结果，见 F-126。

开始与完成日志的级别：**维持**（开始 `Trace`，完成与取消 `Debug`），不改代码：

- RQ-054 只要求字段，不要求级别；字段由 `GameplayExecutorLoggingTests` 守着；
- 每个作业两行，空闲时每秒约 30 个定时作业（F-08），负载下每秒上千个，放到 `Information` 会让生产日志以作业流水为主；
- 需要它们的测量一直用 debug 运行取（M-1、M-2 的 `*-debug-*`），TASK-32 沿用；
- 失败日志是 `Error`，带完整链与 §1.2 的连接字段，出问题时不依赖开始与完成行。

状态：满足（真实进程中的证据交 TASK-32，§4 第 4 条）。

## 4. 交给 TASK-32 的 S5 口径（M-3 判定规则）

出处：TASK-31「主控方裁定（执行前）」第 2 条。TASK-32 原样采用，并在 M-3 的判定日志中注明出处是该裁定。

1. **S5 第 5 项**（发送端层告警）＝ `Warning` 及以上的 `Outbound connection … closed: TimedOut|Failed`、`Outbound entry for …: TimedOut|Failed|OverLimit`、`Closing an outbound connection failed`、`An outbound delivery observer failed`、通知总线告警；收尾窗口内不计，窗口外计入。另沿用 M-2 的豁免（`TASK-28.md` §1.3 第 5 项）：(d) 轮中工具在 LOGIN 应答后主动断开的登录连接，断开后 1 s 内的这类行归为「工具主动断开」，不计。除这两处以外，其余这些行就是「真正的发送失败」。（第 2 轮评审后由主控方修订：原文写「口径与 M-2 相同」，但漏了这条豁免，对 Warning 行比 M-2 更严。）
2. **`OverLimit` 任何时刻都计入**，不变。
3. **「连接已不在」的 `Information` 行另行计数**（按后缀 `; the connection was already gone` 识别），逐条按工具行为归因：
   - 就绪探测连接：工具连上游戏端口后、没有发任何入站帧就关闭的那条连接（`HostProcess.WaitForBootstrapAsync`）；按连接行为识别，不按 host.log 行号；
   - (d) 轮的登录连接：工具在 LOGIN 应答后主动关闭；
   - 收尾窗口内的全部。
   归因不上的计入 S5（保持 M-2 的严格度：工具没有先断开的连接，服务端也不该遇到「连接已不在」）。
4. **级别生效的证据**：构成 B 每次运行就绪探测的那两行应以 `Information` 带后缀出现、`Warning` 中为 0；若仍出现 `Warning` 的 `Failed`，说明 §3 的判定没有覆盖真实进程里的这条路径，按 S5 命中处理并回报。例外：若工具先断开的连接上出现 `Warning` 的 `Failed`，且异常是 `SocketException`（Broken pipe 或 Connection reset），按 F-126 记录并回报，不当作「判定没有覆盖这条路径」的证据。若探测连接上这两行都没有出现，记为「本次无级别证据」，不算命中。
5. `Writing is not allowed after writer was completed` 的计数口径不变（它出现在异常文本里，不随级别变化）。

供判读的一条事实（不改上面的规则）：本单元的线级用例里，对端重置后的发送失败有时在传输层摘除登记之前就被观察到，于是记成 `Warning` 的 `Failed`（§3，F-126）。M-3 若在工具主动断开的连接上看到这种行，按第 4 条回报，由主控方判断。

## 5. 三条保护用例

| 要求 | 用例 | 证据 | 状态 |
|---|---|---|---|
| F-32（DR-15、RQ-031(c)）：作业内的生产 handler 抛异常 → 该连接无回包并读到 FIN，其余连接照常服务；日志只有执行器那一条 `Error`，带原异常与来源连接 | `HandlerFailureConnectionIntegrationTests.A_handler_that_throws_inside_its_job_closes_only_its_connection_and_is_logged_once`：装饰 `ISelectedSessionDomainService`（婚礼应答 handler 在作业内首先调用的领域服务），在第一个 `inbound:GameWeddingResponse` 作业里抛出 | 基准上线上断言全过、只在日志断言处失败（`logs/task-31-red-green.txt`）；变异 MF1、MF5、MF9 | 满足 |
| HTTP 根的继承（RQ-050；`t007-closure.md` 评审补记第 1 条）：经真实 HTTP 登录顶替一条已登录的游戏连接，拆除作业的唯一祖先跳是 `http:POST /api/launcher/v1/login` | `CredentialEntryHttpTests.A_launcher_login_that_supersedes_a_game_connection_is_the_parent_of_its_teardown` | 基准上即为绿（第一次 RED 运行失败在测试自身缺 `GameLogin:Environment` 配置，补上后在基准上通过，`logs/task-31-red-green.txt`）；变异 MF10 | 满足 |
| AC-009 的 A 一半第 ③ 条：经真实 TCP 连接以同一帧序号重发已结算的效果请求 → 重放，不二次扣除，连接不断 | `EffectReplayWireTests.Resending_a_committed_effect_request_on_the_same_connection_replays_without_a_second_charge_and_keeps_the_connection`（USE_STACKABLE 用幸运星 10002998） | 基准上即为绿；变异 MF11 | 满足 |

三条的局部取舍：

- F-32 不装饰 `INetworkMessageDispatcher`，异常从作业体里抛出；同文件的 `HandlerFailureConnectionIntegrationTests.A_frame_whose_handler_throws_gets_no_reply_and_its_connection_is_closed_while_others_are_served` 不改，它守拒绝路径（重抛 → 传输层关闭），两条合起来覆盖 `NetworkPlugins` 的两支。
- HTTP 根的用例给 `LauncherHost` 的配置加了一个 `GameLogin:Environment`，只在用例内（`LauncherHost` 本身不改）：缺它时 LOGIN 应答无法组包，登录被拒（拒绝原因 delivery_failed）。它与 `LifecycleExecutorWireTests.A_superseded_connection_is_closed_by_the_login_cmd_and_its_teardown_is_that_cmds_child`（父链是 `bus:` 根）互补：那条证明继承机制，这条证明生产入口的根是 `Http`。
- AC-009③：起始角色背包里只有复活币那一行，游戏协议不能放物品，所以在创建角色前往 `initialization_settings` 直接写一行角色初始化配置（服务器自己给新角色发物品的途径），按 `CharacterizationHost` 的规矩在用例里写明理由；`Characterization/` 下的文件只用不改。重发帧在用例文件里自己拼，帧序号用反射读 `WireClient` 的私有计数。`CharacterizationHost` 没有日志接缝，「执行器日志里没有失败作业」改由「连接仍开着、随后 `EXIT` 得到 `[1]`」证明：失败作业会关闭它的来源连接。本条补上的是 AC-009 的 A 一半第 ③ 条，①②④ 由 TASK-33 对账。

## 6. F-33：预缓冲与基准等价

- 改法：`src/Dfo.Server.HttpApi/HttpGameplay.cs:84-94`。上限特性可写时照旧设 `MaxRequestBodySize` 并 `EnableBuffering(bodyLimit, bodyLimit)`（`:87`）；只读或缺失时 `EnableBuffering(bodyLimit)`，不设缓冲上限（`:93`）。读体上限就是服务器当前的上限，由服务器在读的时候执行，超限抛出的是与基准相同的异常，照旧重放给委托。方法可见性不变（仍 `private`），经 `RunsOnGameplay` 的过滤器进入。
- 偏离（主控方裁定第 3 条）：只读或缺失这一种情形下，超过 32 KiB 的部分落到临时文件（`FileBufferingReadStream` 的行为），仍在执行器外；TASK-14 §2.2「只用内存、不落临时文件」在可写情形下照旧成立。列入 TASK-34 的遗留问题。
- 用例：`HttpGameplayTests.A_body_over_the_limit_is_read_whole_when_the_size_feature_is_read_only`、`HttpGameplayTests.A_body_over_the_limit_is_read_whole_when_there_is_no_size_feature`（40 KiB 分块 JSON 体，委托读到的字节与原始体逐字节相同、`Position` 为 0）；基准上两条都抛 `IOException: Buffer limit exceeded.`。可写情形由 B1 三形态（`GmGameplayRouteTests`）继续守着，断言不改，本单元全量通过。
- 证据边界：可写情形与基准的等价由 B1 证明；只读与缺失情形下读体上限由服务器执行这一点与基准相同，由上面两条用例证明缓冲本身不再截断；Kestrel 在端点过滤器阶段把特性置为只读的真实情形（请求体已开始读、或连接已升级）没有线上复现手段，属于推理证据。

状态：满足。

## 7. 测试处置

### 7.1 新增（N ＝ 13）

Application 4：`GameplayExecutorLoggingTests.The_failure_log_names_the_source_connection`、`DomainEventDispatchTests.A_group_whose_failure_record_fails_backs_off_with_a_warning_and_one_error`、`DomainEventDispatchTests.A_group_loop_ended_by_a_faulted_executor_logs_the_cause_as_a_warning`、`ScheduledTaskExecutorTests.An_exhausted_task_is_logged_once_by_the_executor_and_warned_without_its_stack`；HttpApi 3：`HttpGameplayTests.A_gm_cmd_that_fails_is_logged_once_by_the_executor_and_the_middleware_only_at_debug`、`HttpGameplayTests.A_body_over_the_limit_is_read_whole_when_the_size_feature_is_read_only`、`HttpGameplayTests.A_body_over_the_limit_is_read_whole_when_there_is_no_size_feature`；Networking 5：`OutboundSenderTests.A_failed_send_to_a_connection_that_is_already_gone_is_logged_as_information`、`OutboundSenderTests.A_failed_send_on_a_connection_that_is_still_there_stays_a_warning`、`OutboundSenderTests.A_timed_out_send_stays_a_warning_even_when_the_connection_is_gone`、`HandlerFailureConnectionIntegrationTests.A_handler_that_throws_inside_its_job_closes_only_its_connection_and_is_logged_once`、`EffectReplayWireTests.Resending_a_committed_effect_request_on_the_same_connection_replays_without_a_second_charge_and_keeps_the_connection`；Host 1：`CredentialEntryHttpTests.A_launcher_login_that_supersedes_a_game_connection_is_the_parent_of_its_teardown`。4 ＋ 3 ＋ 5 ＋ 1 ＝ 13（规划估计 12～13：规划里落在 Protocol 的那一条定时生产者用例，实际落在 Application，见 §1.4）。D ＝ 0。

测试辅助的改动（不是断言）：`LifecycleLog.Count(fragment, level)`；`DomainEventDispatchTests` 的 `Fixture` 加可选参数 `queue`，`LogRecorder` 的 `LogEntry` 加可选的 `Exception`。

### 7.2 删除的测试替身（F-111）

`PartyNotificationTests.BlockingNotificationBus`、`PartyTownProjectionDeliveryTests.BlockingFirstRosterNotificationBus`、`SessionGenerationOwnershipTests.NoopSender`、`SessionGenerationOwnershipTests.RecordingSender`。V9 复核四个类型都只有声明。`SessionGenerationOwnershipTests.cs` 因此不再用到的 `using Dfo.Server.Abstractions.Networking;` 一并删除，别的不整理。

### 7.3 改名（R ＝ 26，F-87 改名一半、F-123）

断言、方法体、特性标注一字不改（`logs/task-31-diff-check.txt`）；读完断言后没有一条名字今天仍成立，26 条全改。

| # | 旧名 | 新名 | 名字依据的断言 |
|---|---|---|---|
| 1 | `UnitOfWork_OpensInsideTheAccountAndTransactionalGatesAndOutsideTheExecutionPolicy` | `UnitOfWorkBehaviorTests.UnitOfWork_OpensInsideTheSelectedSessionSettlementAndOutsideTheExecutionPolicy` | 四条 `Order` 比较：选中会话检查 < 结算 < 工作单元 < 执行策略 |
| 2 | `Exit_command_borrows_the_projection_lease_through_the_real_pipeline` | `LeaveDungeonTests.Exit_command_ends_the_run_through_the_real_pipeline` | `result.EndedCharacters`、`runs.Ended` |
| 3 | `Give_up_completes_through_real_command_locks_and_releases_the_session` | `LeaveDungeonTests.Give_up_completes_through_the_real_command_bus_and_releases_the_session` | `runs.Ended`、`deliveries.Find` 为空、`GiveUpGameResponse` 一条 |
| 4 | `Effect_uow_borrows_outer_exclusive_and_keeps_teardown_waiting_until_settlement` | `CommonExperienceEffectTests.Effect_award_settles_inside_its_clearing_cmd` | `Award!.Total` 为 30、库里 exp 为 30 |
| 5 | `Logger_failure_does_not_replace_unknown_cause_and_exact_close_callback_waits_for_lease_release` | `CommonExperienceEffectTests.Logger_failure_does_not_replace_unknown_cause_and_the_exact_connection_is_closed_and_blocked` | `InnerException` 是 `IOException`、`closer.Expected` 是该连接、`IsBlocked` |
| 6 | `DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate` | `UserServiceTests.DisconnectWithoutSelectedSession_CompletesAndRemovesTheOnlineUser`（文件 `OnlineUserServiceTests.cs`） | 2 秒内完成、`TryGetByConnection` 为假 |
| 7 | 两塔 `Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release`（幻影塔） | `IllusionTowerProjectionConcurrencyTests.Party_wrapper_failure_after_its_inner_write_isolates_and_closes_both_sessions` | 失败结果、塔记录与队伍成员移除、两条连接都被关闭 |
| 8 | 同上（Boss 塔） | `BossTowerProjectionConcurrencyTests.Party_wrapper_failure_after_its_inner_write_isolates_and_closes_both_sessions` | 同上 |
| 9 | `Successful_command_notifications_are_delivered_after_both_transition_gates_release` | `PartyCommandHandlerTests.Successful_command_notifications_are_delivered_once` | `published.Count` 为 1 |
| 10 | `Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases` | `PartyCommandHandlerTests.Set_party_info_sends_one_command_and_delivers_its_result_as_three_full_state_phases` | 一条 `SetPartyInfoCommand`、`published.Count` 为 3 |
| 11 | `Retry_delay_does_not_hold_the_global_execution_slot` | `ScheduledTaskTests.A_retry_delay_lets_another_task_run_meanwhile` | 重试等待期间另一任务跑完、`calls` 仍为 1、之后重试；调度器里已没有全局执行槽 |
| 12 | `Global_limit_queues_one_instance_and_coalesces_waiting_runs` | `ScheduledTaskTests.Runs_execute_one_at_a_time_and_waiting_runs_coalesce` | `maximum` 为 1、共 3 次（运行中的 ＋ 补跑 ＋ 合并的一次）；全局上限已删 |
| 13 | `Factory_uses_configured_singletons_and_explicit_options_override_defaults` | `ScheduledTaskTests.Factory_task_is_built_from_configured_singletons_and_runs` | 工厂用注册的 `TestScheduleConfig` 建任务并跑到 `called`；名字里的「显式选项覆盖默认值」今天没有断言 |
| 14 | `GameTicketRedemptionIsTransactionalAndOnlineUserExclusive` | `CommandRegistrationTests.GameTicketRedemptionIsTransactional` | 只断言 `Transactional` |
| 15 | `Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released` | `DomainEventDispatchTests.Commit_returns_at_once_and_the_worker_consumes_the_delivery_afterwards` | 提交 5 秒内返回且当时计数为 0、之后计数为 3 |
| 16 | `Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_and_exclusive_lease_through_batch` | `ReplayStateSnapshotTests.Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_through_its_batch` | 同一连接、两批之间读到新库存、换了连接上下文即拒绝 |
| 17 | `Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read` | `ReplayStateSnapshotTests.A_replacement_selected_before_the_original_read_runs_rejects_that_read` | 先选另一角色、原读取的命令抛 `InvalidOperationException` |
| 18 | `A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor` | `LifecycleExecutorWireTests.A_superseded_connection_is_closed_by_the_login_cmd_and_its_teardown_is_that_cmds_child`（F-123，规划定名） | `AssertLifecycleChild` 的祖先跳是 `bus:AuthenticateAndIssueTicketCommand` |
| 19 | `A_send_to_a_resetting_client_does_not_hold_the_executor_past_the_deadline` | `LifecycleExecutorWireTests.A_send_to_a_resetting_client_does_not_hold_the_executor` | 重置前作业已结束、下一个作业 1 秒内运行；执行器上已没有期限 |
| 20 | `TouchSocketNetworkMessageSender_PreservesRequiredAndPerTargetOrderAndExcludesOwner` | `OutboxExplicitDeliveryTests.ExplicitDelivery_PreservesRequiredAndPerTargetOrderAndExcludesOwner` | 登记出的三条条目与顺序；被测的是登记适配器，旧发送器类型已删 |
| 21 | `TouchSocketNetworkMessageSender_RequiredDirectFailurePropagatesAndStopsDelivery` | `OutboxExplicitDeliveryTests.ExplicitDelivery_RequiredDirectFailurePropagatesAndStopsDelivery` | 抛 `direct`、没有条目 |
| 22 | `TouchSocketNetworkMessageSender_BestEffortFailureStopsOnlyThatTarget` | `OutboxExplicitDeliveryTests.ExplicitDelivery_BestEffortFailureStopsOnlyThatTarget` | 只少了失败的那个收件人 |
| 23 | `A_registered_handler_is_found_with_the_retry_limit_it_declared` | `NotificationRegistrationTests.A_registered_handler_is_found_with_its_handler_and_composer_types` | `HandlerType`、`ComposerType`；重试上限已删 |
| 24 | `PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks` | `SelectCharacterLifecycleTests.Pvp_plan_publishes_the_newcomer_to_its_audience_and_the_selection_completes_every_stage` | 阶段账本走到 `premium`、没有关闭、PvP 公示一条、人群是角色 4242、新人是已发布的角色 |
| 25 | `Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | `IllusionTowerRewardProjectionTests.Persistent_registration_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | 两次尝试、关闭该连接、清理后塔记录与会话都没了、资产不变；注入点是登记点 |
| 26 | `A_partial_notification_failure_can_be_replayed_without_a_second_debit` | `EquipmentEnchantHandlerTests.A_failed_notification_batch_can_be_replayed_without_a_second_debit` | 第一次无消息、重放 6 条消息一批、不二次扣除；F-102 之后一个收件人的通知是一整批 |

计数：17（1～17）＋ 2（18、19）＋ 3（20～22）＋ 1（23）＋ 2（24、25）＋ 1（26）＝ 26 个方法，分属 17 个文件；第 3 条是 `Theory`（2 行），所以改名涉及的用例数是 27。第 24 条：替身里 `failPvpTarget` 今天是空操作，但它的断言仍在检查阶段顺序与 PvP 公示，照实命名后有用，不提请删除。

### 7.4 数量

正式全量 8315 ＝ 8302 ＋ 13 － 0；R ＝ 26 不计入 N、D（主控方裁定第 4 条 (b)）。逐项目加法见 `logs/task-31-build-test.txt` 文首。

## 8. 代码冻结与移交

- 冻结点：本单元的提交（主控方提交时把哈希写进 `follow-ups.md` 文首 TASK-31 一行与本节）。TASK-32～TASK-34 开始前各自核对 `git diff <冻结点> -- Server` 为空；之后若必须改代码，回到主控方。
- TASK-32：§4 的五条口径；F-121 在真实进程中的证据（§4 第 4 条）；F-126。
- TASK-33：附录 G 的 VC-10 仍归它（TASK-30 主控方裁定第 3 条）；本单元的日志变化是否需要写进 AGENTS.md「领域事件与即时投影」一节的日志描述，由它判断；AC-009 的 ①②④ 对账；AGENTS.md 的偶发清单（主控方裁定第 6 条）。
- TASK-34：测试数量加法中本单元的 N ＝ 13、D ＝ 0、R ＝ 26（§7）；遗留问题里加 F-30 的命令层一半（主控方裁定第 5 条）与 F-33 的临时文件偏离（主控方裁定第 3 条）。
- 未关闭、交出的：F-69（续写）、F-125（本单元正式全量未出现）、F-126。

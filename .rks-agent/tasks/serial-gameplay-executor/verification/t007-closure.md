# T-007 收口对账（调用链贯通与深度上限）

- 日期 2026-10-23。基准提交 `bc735cd0`（`Server/src` 与 `06ca5e0f` 相同）加上 TASK-29 未提交的改动。
- 用途：判定 Plan T-007「调用链贯通与深度上限」是否完成；外部审核读 T-007 的入口。
- 写法：仿 `t006-closure.md`，只写「要求 → 实现位置 → 证据 → 状态」。未关闭事项只引用 F 编号，内容在 `follow-ups.md`；推导过程在 `tasks/TASK-29.md`。
- 文件:行 指 TASK-29 完成时的工作区；路径相对 `Server/src/Dfo.Server.Application/`，另有注明的除外。
- 状态词同 `t006-closure.md`：完成／完成，部分移交／满足／满足，附条件／部分满足。

## 1. 需求与验收逐项

| 要求 | 实现位置 | 证据 | 状态 |
|---|---|---|---|
| RQ-050 事件 CMD 是发布它的 CMD 的子（深度 ＋1、根继承、来源连接继承） | 发布：`DomainEvents/EventService.cs:38-41` 按 delivery 记链；消费：`DomainEvents/DomainEventDispatcher.cs:108-114`（`ChainOf`） | `DomainEventDispatchTests.An_event_cmd_is_a_child_of_the_cmd_that_published_its_fact`、`DomainEventDispatchTests.A_fact_published_by_an_event_cmd_extends_the_chain_by_one`；变异 MT1 | 满足 |
| RQ-050 关连接引起的生命周期 CMD 是登记关闭的 CMD 的子 | 记录：`Execution/Outbox/GameplayOutbox.cs:80-84`、`:98`（交给发送端之前）；读取：`Dispatching/NetworkConnectionLifecycleDispatcher.cs:45`；存储 `Execution/GameplayCloseCauses.cs:19-39` | `LifecycleCallChainTests.A_disconnect_after_a_cmd_registered_close_is_a_child_of_that_cmd`、`LifecycleCallChainTests.A_disconnect_after_a_failed_cmd_closed_its_source_is_a_child_of_that_cmd`、`LifecycleCallChainTests.A_close_cause_is_used_once`；线级 4 处（§5.2）；变异 MT5、MT6、MT8 | 满足，附条件（§3 第 5 条的残余） |
| RQ-050 同一 delivery 的失败重试继承首次消费时的链 | 非终态失败不遗忘：`DomainEvents/DomainEventDispatcher.cs:206` | `DomainEventDispatchTests.A_retry_keeps_the_chain_of_its_first_consumption`；变异 MT2 | 满足 |
| RQ-050 末句：传输层自发的关闭、深度检查发起的关闭引起的生命周期 CMD 是新根 | 无原因即新根：`Dispatching/NetworkConnectionLifecycleDispatcher.cs:45`；深度守卫的关闭不记原因：`Execution/Outbox/GameplayOutbox.cs:150-158`、`Execution/GameplayCloseCauses.cs:61-73` | `LifecycleCallChainTests.A_disconnect_without_a_registered_close_is_a_new_root`；`LifecycleExecutorWireTests` 中客户端断开、重置、入站作业中断开的 3 个用例断言不变（§5.2）；`DomainEventDispatchTests.A_reliable_delivery_past_the_depth_limit_is_settled_terminal_and_its_root_connection_closed` 的最后一步；变异 MT4 | 满足 |
| RQ-050 定时、入站、GM、HTTP、启动恢复是新根 | §2 | §2 的守护用例 | 满足 |
| RQ-051 链只在进程内存，按 delivery 记录，表结构不变；别的进程留下的 delivery 是启动恢复新根 | `DomainEvents/DomainEventCallChains.cs`；`DomainEvents/DomainEventDispatcher.cs:110`；回滚撤销 `DomainEvents/EventService.cs:98-102`；落定后遗忘 `DomainEvents/DomainEventDispatcher.cs:161`、`:206`、`:222`、`:245` | `DomainEventDispatchTests.A_rolled_back_fact_leaves_no_chain_behind`（变异 MT7）；`DomainEventDispatchTests.Deliveries_left_by_an_earlier_process_run_as_startup_recovery_and_new_facts_as_event_cmds`（不变）；`Persistence` 与建表无改动（`logs/task-29-boundary.txt`） | 满足 |
| RQ-052 深度常量与判据 | `Dfo.Server.Abstractions/Execution/GameplayCallChain.cs` 的 `GameplayCallDepth.Limit ＝ 32`，未改 | 事件图复核：订阅注册与发布点与 `event-chain.md` 的基准逐项相同，L ＝ 10（`logs/task-29-event-chain.txt`）；`GameplayCallDepthTests.The_depth_limit_covers_twice_the_longest_legal_chain_of_appendix_A` 不变 | 满足 |
| RQ-053 正文：不入队、记一条含完整链的 `Error` | 执行器未改：`Execution/GameplayExecutor.cs:48-57` | `GameplayCallDepthTests.Work_at_the_limit_runs_and_one_past_it_is_refused_with_the_full_chain_logged`；D1、L5 断言日志里的链从根开始；变异 MT9 | 满足 |
| RQ-053(a) 根来源是客户端连接的，关闭该连接，且这次关闭引起的生命周期 CMD 是新根 | `Execution/GameplayCloseCauses.cs:61-73`（按裁定第 6(b) 条以 `SourceConnection` 非空判定）；事件超限在落定 CMD 内、生命周期超限在守卫 CMD 内调用 | `DomainEventDispatchTests.A_reliable_delivery_past_the_depth_limit_is_settled_terminal_and_its_root_connection_closed`、`DomainEventDispatchTests.A_live_delivery_past_the_depth_limit_is_completed_with_the_depth_reason`、`LifecycleCallChainTests.A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection`；变异 MT4 | 满足 |
| RQ-053(b) 超限的 delivery 由不继承链的系统 CMD 落定（Reliable 写 Terminal，LiveOnly 以深度超限为理由写完成），不执行订阅处理 | `DomainEvents/DomainEventDispatcher.cs:92-96`、`:126-165`；系统 CMD 类型 `event:<订阅 ID>/depth-exceeded`，新根、深度 0，执行器不加豁免分支 | D1、D2、`DomainEventDispatchTests.A_chain_without_a_connection_past_the_limit_is_settled_and_only_logged`；变异 MT3 | 满足 |
| RQ-053(c) 其余情况只记日志 | 无连接的链只有执行器的 `Error` 与落定 | `DomainEventDispatchTests.A_chain_without_a_connection_past_the_limit_is_settled_and_only_logged` | 满足 |
| RQ-053(d)（本单元按主控方裁定第 1 条加入 `spec.md`）关连接引起的生命周期 CMD 超限时改作新根执行拆除，再按 (a) 关根连接 | `Dispatching/NetworkConnectionLifecycleDispatcher.cs:51-65`；守卫 CMD 类型 `lifecycle:depth-exceeded` | `LifecycleCallChainTests.A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection`；变异 MT10 | 满足 |
| RQ-054 开始、结束、失败日志带根 ID、父 ID、深度与 CMD 类型 | `Execution/GameplayExecutor.cs:267`（开始，Trace）、`:313`（失败，Error）、`:324`（取消，Debug）、`:333`（完成，Debug）；格式未改 | `GameplayExecutorLoggingTests.Start_and_end_logs_carry_root_parent_depth_source_and_type_and_the_end_log_both_timings`、`GameplayExecutorLoggingTests.The_failure_log_is_an_error_with_the_exception_and_the_chain`；继承链的真实值：E1 与 L1 断言完成日志里的父 ID 与深度 1 | 满足（级别见 §4 第 3 条） |
| AC-016 | 上面各行 | 事件链超限：不入队、`Error` 含完整链、根连接关闭（D1、D2）；Reliable 为 Terminal、LiveOnly 已完成且不再被取出，分发循环之后进入等待（`DomainEventDispatchTests.After_settling_a_delivery_past_the_limit_the_group_loop_waits_instead_of_spinning`，变异 MT3 下变红）；无连接链只记日志（D3）；重试继承原链（E3）；正常 CMD 日志带根、父、深度（E1、L1）；判据钉住常量（RQ-052 行） | 满足 |
| RQ-012 继承链一半（`t004-closure.md` RQ-012 行移交 T-007） | 同 RQ-050 第 2 行 | 同上 | 满足，附条件（同上） |
| RQ-014、RQ-041 继承链一半（`t004-closure.md` RQ-014、RQ-041 行移交 T-007） | 同 RQ-050 第 1、3 行 | 同上 | 满足 |

队列层泄漏流（F-59，主控方裁定第 2 条）：`Execution/GameplayExecutor.cs:44-45` 在深度检查之前拒绝带着已结束作业令牌的流，取证类 `queue-leaked-flow`（`Dfo.Server.Abstractions/Execution/GameplayAssertions.cs:82`）。反例 `GameplayQueueLeakTests.Enqueueing_from_a_flow_leaked_from_a_finished_job_is_refused`、对照 `GameplayQueueLeakTests.A_flow_detached_from_a_job_enqueues_as_a_new_root`；正式全量取证 41 条，多出的 1 条就是这个反例（`logs/task-29-violations.txt`），生产中没有这样的入队。总线的判定在前，`bus-leaked-flow` 与它的两条用例不变。

## 2. 各来源的根与父

| 来源 | 位置 | 来源种类 | 带连接 | RQ-050 依据 | 守它的用例 |
|---|---|---|---|---|---|
| 入站 | `Dispatching/NetworkMessageDispatcher.cs:47` | Connection | 是 | 入站是新根 | `InboundExecutorWireTests` 的 `AssertInboundJob`（深度 0） |
| 总线的执行器外分支 | `Commands/CommandBus.cs:34` | Connection | 否 | 只有测试走到（`entry-reconciliation.md` §1.3） | —（不改） |
| 生命周期：`lifecycle:connected`、没有原因的 `lifecycle:disconnected`、超限后重入队的拆除 | `Dispatching/NetworkConnectionLifecycleDispatcher.cs:33`、`:45`、`:56`（`NewRoot(connection)`，`:76-77`） | Lifecycle | 是（这个连接） | 末句 | `ConnectionLifecycleDispatchTests`（深度 0，不变）、`LifecycleCallChainTests.A_disconnect_without_a_registered_close_is_a_new_root` |
| 生命周期：CMD 登记的关闭引起的 `lifecycle:disconnected` | `Dispatching/NetworkConnectionLifecycleDispatcher.cs:45`（`Child`） | Lifecycle | 继承根的 | 第 2 句 | §1 RQ-050 第 2 行 |
| 守卫 CMD `lifecycle:depth-exceeded` | `Dispatching/NetworkConnectionLifecycleDispatcher.cs:59` | Lifecycle | 否 | RQ-053(a)、(d) | `LifecycleCallChainTests.A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection` |
| 定时与调度 | `Execution/GameplayTimerWork.cs:23`、`:39` | Timer（默认） | 否 | 定时是新根 | `TimerSourceReconciliationTests.Each_timer_source_runs_its_ticks_as_its_own_cmd_on_the_executor` 等 |
| 卡牌计时器 | `Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:362` | Timer | 否 | 按主控方裁定第 4 条保持新根 | `DungeonCardPresentationCoordinatorTests`（`.Timers.cs`） |
| 登录会话维护 | `Authentication/LoginSessionRuntime.cs:43` | Timer | 否 | 定时是新根 | `LoginSessionMaintenanceExecutorTests.An_expiry_sweep_runs_as_a_timer_job_on_the_executor` |
| GM | `Dfo.Server.HttpApi/HttpGameplay.cs:34`（来源由路由组参数给出：`Dfo.Server.GmWeb/GmWebModule.cs:33`、`GmAuthenticationEndpoints.cs:29`、`:50`） | Gm | 否 | GM 是新根 | `HttpGameplayTests`；`CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds` |
| HTTP（启动器注册、登录） | 同上（`Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs:94`、`:179`） | Http | 否 | HTTP 是新根 | `CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds` |
| 启动作业：GM bootstrap、`EpicBook` 迁移与经验存储策略 | `Dfo.Server.GmWeb/GmBootstrapService.cs:28`、`Dfo.Server.GameServer/GameServerStartupService.cs:100` | StartupRecovery | 否 | 启动恢复是新根 | 规划时列出的启动作业用例（不变） |
| Joust 静默恢复 | `Events/JoustMaintenanceService.cs:24`（经 `GameplayTimerWork` 的来源参数）；周期 tick `:25`、`:72` 是 Timer | StartupRecovery／Timer | 否 | 启动恢复、定时 | 不变 |
| 别的进程留下的 delivery | `DomainEvents/DomainEventDispatcher.cs:110` | StartupRecovery | 否 | RQ-051 | `DomainEventDispatchTests.Deliveries_left_by_an_earlier_process_run_as_startup_recovery_and_new_facts_as_event_cmds` |
| 本进程、有记录的 delivery | `DomainEvents/DomainEventDispatcher.cs:111-112`（`Child`） | Event | 继承根的 | 第 2 句 | E1、E2、E3 |
| 本进程、没有记录的 delivery（在执行器外发布，只有测试） | `DomainEvents/DomainEventDispatcher.cs:113` | Event | 否 | — | 既有事件用例 |
| 落定 CMD `event:<订阅 ID>/depth-exceeded` | `DomainEvents/DomainEventDispatcher.cs:133` | Event | 否 | RQ-053(b) | D1 |

生产中的 `Child(` 只有两处（`logs/task-29-retrievals.txt` R2）。F-66 据此关闭；F-28 按主控方裁定第 4 条关闭。

## 3. 裁定与局部决定

1. 不拆分（主控方裁定第 0 条）。
2. 生命周期 CMD 超限改作新根重新入队拆除，再关根连接（裁定第 1 条）：`spec.md` RQ-053 加 (d) 一句，这是主控方授权的 Spec 修订。不会因此失控：每次这样的新根都要消耗一个已断开的连接。
3. 队列层也拒绝泄漏流（裁定第 2 条）：改的是执行器入队点的共享契约。`GameplayLeakAssertion` 为此加了常量 `QueueLeakedFlowKind` 与一个带取证类的 `LeakedFrom` 重载（原重载改为调用它，行为不变）：取证源的 `Report` 是 Abstractions 的内部成员，只加常量做不到以新类上报。
4. 不引入 `lifecycle:close-cleanup`（裁定第 3 条）：CMD 登记的关闭与传输层关闭引起的断开都用 `lifecycle:disconnected`，二者由链区分（有父／无父）。附录 A 第 3 行不变。
5. 关闭原因在 Application 的发件箱交给发送端之前记录（裁定第 5 条，方案 A），按连接上下文实例记录（`ConditionalWeakTable`，弱引用）。V8 核对过：登记、关闭器 `TryGet`、断开回调 `TryRemove` 取到的是同一个实例（`logs/task-29-verify.txt`）。残余见 §4 第 2 条。
6. 两处判定口径（裁定第 6 条）：作业失败时补登记的来源关闭、发送端拒收整箱时的兜底关闭都算 CMD 登记的关闭；「根来源是客户端连接」按 `SourceConnection` 非空判定。
7. `SourceConnection` 不改成 RQ-035 的精确身份（回应 TASK-23 §3.2 末句）：深度守卫按连接 ID 取当前登记的上下文，与 T-006 关闭器的既有写法相同；连接 ID 内含 TouchSocket 会话 ID（`Dfo.Server.Networking/Runtime/TouchSocketConnectionRegistry.cs:98-102`），根连接已断开时取不到，只记 Debug。
8. `GameplayDepthExceededException` 不进打包进程输出的违规识别名单（回应 TASK-26 §3.4）：超限是有处理的结果（落定、关连接、记日志），不是断言违规；失控链由正式全量的日志信号发现。
9. 事件链存储与关闭原因存储是调用链元数据，不是玩法状态，不受 RQ-061 写入口约束：前者写在执行器上、由执行器外的分发循环读，后者在发件箱交付时写、由传输层回调读；两者都不参与任何玩法判定。
10. 「系统 CMD 豁免深度检查」不需要执行器旁路：落定 CMD 与守卫 CMD 都是新根，深度 0，天然通过检查。

## 4. 已知限制

1. 提交结果不确定（`TransactionOutcomeUncertainException`）的事务既不提交也不回滚它的事件批，记下的链不会被读到，留到进程结束；数量不超过这类事务发布的事实数 × 订阅数。
2. 裁定第 5 条的残余：CMD 已登记关闭、发送端还没执行时，传输层若先因交付失败或客户端断开而关闭了这个连接，这次断开仍按继承处理，而 RQ-050 末句字面要求新根。影响只在链的字段与深度上；附录 A 担心的「交付失败 → 关同伴 → 同伴的清理通告再失败」级联中没有 CMD 登记的关闭，仍全是新根。
3. 开始日志是 Trace、完成与取消是 Debug，生产默认级别下不输出；RQ-054 只要求字段。级别复核并入 F-121（T-009）。
4. 深度拒绝与落定的日志行在正式全量的控制台输出里是 0 条：新用例都用各自的日志记录器断言这些行，不写控制台（`logs/task-29-build-test.txt`）。

## 5. 测试处置

### 5.1 新增（N ＝ 15，全在 Application）

- E 4：`DomainEventDispatchTests.An_event_cmd_is_a_child_of_the_cmd_that_published_its_fact`、`DomainEventDispatchTests.A_fact_published_by_an_event_cmd_extends_the_chain_by_one`、`DomainEventDispatchTests.A_retry_keeps_the_chain_of_its_first_consumption`、`DomainEventDispatchTests.A_rolled_back_fact_leaves_no_chain_behind`；
- D 4：`DomainEventDispatchTests.A_reliable_delivery_past_the_depth_limit_is_settled_terminal_and_its_root_connection_closed`、`DomainEventDispatchTests.A_live_delivery_past_the_depth_limit_is_completed_with_the_depth_reason`、`DomainEventDispatchTests.A_chain_without_a_connection_past_the_limit_is_settled_and_only_logged`、`DomainEventDispatchTests.After_settling_a_delivery_past_the_limit_the_group_loop_waits_instead_of_spinning`；
- L 5：`LifecycleCallChainTests.A_disconnect_after_a_cmd_registered_close_is_a_child_of_that_cmd`、`LifecycleCallChainTests.A_disconnect_after_a_failed_cmd_closed_its_source_is_a_child_of_that_cmd`、`LifecycleCallChainTests.A_disconnect_without_a_registered_close_is_a_new_root`、`LifecycleCallChainTests.A_close_cause_is_used_once`、`LifecycleCallChainTests.A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection`；
- Q 2：`GameplayQueueLeakTests.Enqueueing_from_a_flow_leaked_from_a_finished_job_is_refused`、`GameplayQueueLeakTests.A_flow_detached_from_a_job_enqueues_as_a_new_root`。

加法：4 ＋ 4 ＋ 5 ＋ 2 ＝ 15；删除 D ＝ 0。正式全量 8247 ＋ 15 － 0 ＝ 8262，只有 Application 变化（4538 ＋ 15 ＝ 4553）。

### 5.2 `LifecycleExecutorWireTests` 改写的 4 处

4 处的关闭都核实为 CMD 登记：`:96`、`:136` 由探针 handler 调关闭器；`:114` 由 `GameLoginHandler` 拒绝登录时调关闭器（`Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs:150`）；`:159` 由登录 CMD 提交后 `LoginSessionRegistry` 关闭被顶替的连接（`Authentication/LoginSessionRegistry.cs:103`）。新辅助 `AssertLifecycleChild` 断言：在执行器上、来源 Lifecycle、类型 `lifecycle:disconnected`、深度 1、来源连接、唯一的祖先跳，给了父链时再断言父 ID 与根 ID。`AssertLifecycleJob` 原样保留给其余 3 个用例。

| 调用点 | 用例 | 旧断言 | 新断言 | 保护意图 |
|---|---|---|---|---|
| `:96` | `A_cmd_closing_its_own_source_connection_does_not_wait_on_itself` | 在执行器上；来源 Lifecycle；来源连接 ＝ 本连接；类型 `lifecycle:disconnected`；深度 0 | 在执行器上；来源 Lifecycle；类型同；深度 1；来源连接 ＝ 本连接；祖先跳 (Connection, `inbound:GameExit`)；父 ID ＝ 入站作业（记录器 `inbound-enter` 的链）的 ID，根 ID 相同 | 清理是执行器上的生命周期 CMD、晚于关闭它的 CMD（同一用例的 `Order` 断言与 FIN 断言不动）；新增：继承 |
| `:114` | `A_login_refused_by_the_real_handler_closes_the_connection_and_cleans_up_on_the_executor` | 同上 | 同上，祖先跳 (Connection, `inbound:GameLogin`)（真实 handler 没有 `inbound-enter` 记录，所以只断言祖先跳） | 拒绝登录关闭连接、清理在执行器上；新增：继承登录 CMD 的链 |
| `:136` | `A_cmd_closing_another_connection_has_its_cleanup_run_after_the_cmd` | 深度 0，来源连接 ＝ 被关的空闲连接 | 深度 1；来源连接 ＝ 请求方连接；祖先跳 (Connection, `inbound:GameExit`)；父 ID ＝ 请求方入站作业 ID | 清理晚于请求方 CMD、请求方没有被断开（其余断言不动）；新增：继承与来源连接 |
| `:159` | `A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor` | 深度 0，来源连接 ＝ 被顶替的连接 | 深度 1；来源连接为空；祖先跳 (Connection, `bus:AuthenticateAndIssueTicketCommand`) | 被顶替连接的清理在执行器上（F-65）；新增：继承登录 CMD 的链。**与计划不同**：计划写祖先跳为 Http 的 `http:` CMD；这个用例经 `TestCredentials.LoginAsync` 在执行器外调用总线，根是总线执行器外分支的 `bus:` CMD（来源 Connection、无连接），HTTP 入口的登录不经这条路。断言按实际的根写 |

客户端断开、发送失败与重置的 3 个用例（`:40`、`:41`、`:65`、`:205`，`AssertLifecycleJob`）断言不变，作为 RQ-050 末句「传输层关闭是新根」的证据。用例名 `…closed_by_maintenance…` 与 F-65 之后的事实不符，本单元不改名（改名会改方法集合）。

## 6. 移交

- 附录 A 第 3、4、6、8 行与两个新系统 CMD 类型（`event:<订阅 ID>/depth-exceeded`、`lifecycle:depth-exceeded`）的最终对账、`entry-reconciliation.md` 的改写：T-009（F-99）。
- 开始与完成日志的级别：T-009（F-121）。
- 用例改名 `A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor`：F-123。
- 耗时对比续写：F-69。

本单元关闭：F-26、F-27、F-28、F-29、F-59、F-66

## 评审后补记（TASK-29 独立快审，approved）

- **HTTP 根的继承只靠读码**：顶替关闭继承 HTTP 登录 CMD 的链（TASK-23 待裁定第 6 条）在生产中走 `HttpApiApplicationExtensions.cs:180` → `HttpGameplay.cs:34`，根为 Http。`LifecycleExecutorWireTests` 中对应的用例经 `TestCredentials.LoginAsync` 在执行器外直接调总线，父链是 `bus:AuthenticateAndIssueTicketCommand`。继承机制与来源无关（`GameplayOutbox` 在 Flush 时记录 `GameplayJob.Current`），读码成立，但 HTTP 根这一条没有直接用例；移交 T-009 判断是否补一条。
- **另外两类孤儿项**（补充 §4 第 1 条）：`DomainEventDispatcher.SettleOnExecutorAsync` 中 LiveOnly 的 `TryBeginAsync` 返回 false（被别的消费者认领）时直接返回、不调用 `Forget`；`ReliableAsync`／`LiveOnlyAsync` 中 `TryBeginAsync` 返回 false 时同样不遗忘。两者只在并发认领的竞态下出现，每条记录最多残留一项，进程结束即清空，不会无界增长。
- **日志措辞**：`GameplayDepthGuard.CloseRootConnection` 在容器没有接网络（`contexts` 为 null）时，也会记「is no longer registered」。只影响 Debug 日志的措辞，生产中已注册 `INetworkConnectionContexts`，不受影响。

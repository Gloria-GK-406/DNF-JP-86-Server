# T-009 判据收口（TASK-30：RQ-061 名单补全与 A 的判据收口）

- 日期 2026-10-24。基准提交 `a9c79e98` 加上 TASK-30 未提交的改动（含第 1 轮评审后的修改，见 `tasks/TASK-30.md` 的「第 1 轮评审后修改」）。
- 用途：判定 Plan T-009 中「RQ-061 运行期判据的最终核对」「A 的判据加固与反例对账」「A 期间改写的判据对账」三项，以及附录 F 名单与附录 B 补名这一半是否完成；TASK-33 的附录最终对账、TASK-34 的 A 最终汇报与子 Spec B 从这里取输入。
- 写法：仿 `t007-closure.md`，只写「要求 → 实现位置 → 证据 → 状态」。未关闭事项只引用 F 编号，内容在 `follow-ups.md`；推导过程在 `tasks/TASK-30.md`。
- 文件:行 指 TASK-30 完成时的工作区；路径相对 `Server/`，另有注明的除外。日志路径相对 `verification/`。

## 1. RQ-061：三类入口、没有开关、没有启动期例外

| 要求 | 实现位置 | 证据 | 状态 |
|---|---|---|---|
| 写工作单元在执行器外开启即抛 | `src/Dfo.Server.Application/Transactions/DomainTransactionScope.cs:42`（`Begin` 先断言）；7 个 `new DomainTransaction(` 开启点 | 事务判据 `GameplayWriteEntryTableTests.Transactions_are_created_only_at_the_listed_sites_and_the_writes_assert` 及 3 条反例（不变） | 满足 |
| 发件箱登记在执行器外即抛 | `src/Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs` 的 `GameplayOutboxes.Current`；登记适配器先判位置（§6） | `OutboxRegistrationTests.A_send_off_the_executor_is_a_program_error_reported_as_evidence`、`OutboxRegistrationTests.A_close_off_the_executor_is_a_program_error_reported_as_evidence`、`OutboxRegistrationTests.An_unencodable_message_off_the_executor_is_refused_by_position_first`（本单元） | 满足 |
| 附录 F 名单的写入口在执行器外即抛 | 名单扩大到普查出的全部进程内玩法状态（§2）；挂点 40 个持有者、69 处新增 `Writing` 调用 | 分类判据 `GameplayWriteEntryTableTests.Every_member_of_the_listed_types_is_classified_and_exactly_the_writes_assert`；普查判据 `GameplayStateCensusTests.Every_singleton_holding_mutable_state_is_a_write_entry_or_a_listed_exemption`；运行期反例：`GameplayWriteAssertionTests` 的执行器外违规 53 条 | 满足 |
| 判据没有开关 | `src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs:41-49`：只判 `GameplayJob.Active`；`IsEnabled` 只决定写不写取证（`:24-25`） | `logs/task-30-retrievals.txt` R2 | 满足 |
| 没有启动期例外（F-16、F-60） | 启动期写入作为启动作业运行（`startup:` 三类，附录 B「开始监听前的启动期工作」行）；仍在执行器外的建表与迁移不经两类入口 | 正式全量与三个隔离运行的取证中没有测试帧的记录为 0、`exe:` 为 0（`logs/task-30-violations.txt`、`logs/task-30-isolated.txt`）；附录 B 该行前半句已改正（§7） | 满足 |

RQ-061 的正文不改。名单扩大使它的实际覆盖面变大（主控方裁定第 1 条），交外部审核时说明：附录 F「改为执行器内访问」一栏今天的含义是「普查出的全部进程内玩法状态」，不是原来点名的 12 个类。

## 2. 普查与名单（AC-006 中「附录 F 名单以外的进程内玩法状态不在判据内」一半）

### 2.1 规则

写在 `tests/Dfo.Server.Application.Tests/Execution/GameplayStateCensusTests.cs` 的类 remarks 里，这里不重抄。与规划（TASK-30 §2.1）的差别：

- 主构造器捕获的参数（编译器生成的可写字段）按声明类型判定，不按「非 readonly」判定；但构造器之外有 `stfld` 写它的，按可写计（评审 F2；反例为普查 Theory 的 reassigned parameter 一行，变异 MU12）。生产中今天没有这样的单例：`GameplayOutbox+OrderedScope.set_Delivery` 写它的捕获参数，但它是每个作业一份的对象，不是单例，也不是单例的字段；
- 规则 2 加入 `AsyncLocal<>`、`ThreadLocal<>`（否则请求级帧认不出，裁定 1(b) 的豁免无从落地）；接口元素的 readonly 数组（注入的服务集合）不算状态；
- 规则 3 不把引用另一个已注册单例的字段算作自己的状态（那个单例在自己名下计）；被单独归类的组件类型（如 `CoalescingGameplaySource`）不再让持有它的类型成为持有者，组件本身作为持有者出现；
- 多一个归类 `R061`：写工作单元本身（`DomainTransactionScope`），由事务判据守着，不放进 `Types`；
- 多一张「不持有状态的写入口」表，2 项：`AltarDungeonEngine`（附录 F 点名，状态在调用方传入的 `AltarStateStore`）；`WeaknessDomainService+Weakness`（`For` 交出的私有句柄，写的是所有者的条目，评审 F1）。

识别边界：认不出藏在 `object` 或接口类型字段后的状态；认不出其他程序集类型里的状态（例如 `GameplayOutboxes` 的 `GameplayJobSlot`、`Random`、通道、信号量；后两者归附录 F）；认不出闭包或异步状态机捕获的集合；不判断静态常量表初始化之后有没有被写。后两种由读码兜底：判据输出列出托管服务长时运行方法捕获的集合（6 个，其中跨调用存活的只有 `RoomMechanismMaintenanceService.ExecuteAsync` 的 `delivered`，读码确认只在定时 CMD 作业体内读写），15 张常量表的每个引用点都读过，没有元素写入（`logs/task-30-census.txt`）。

### 2.2 范围之外的项目

- `Persistence`：状态在 SQLite；内存中的易失 store 只经 `SessionIn(transaction)` 写，事务只在 7 处开启，开启点由事务判据钉住并要求先有写断言。
- `Infrastructure`：内容，附录 B「PVF 内容源与缓存、规则服务的不可变规则」。
- `Networking`：传输注册表与发送端，附录 B 已列。
- `GmWeb`、`HttpApi`、`GameServer`、`Host`：规划原型找到 3 个持有者（`GmIconPackStore`、`GmLoginLimiter`、`HttpApiReadiness`），都在附录 B（`logs/task-30-verify.txt` V12、`logs/task-30-retrievals.txt` R5）。不进判据：判据放在 `Dfo.Server.Application.Tests`，它不引用这些项目。

### 2.3 普查结果（加法全文在 `logs/task-30-census.txt`）

- 持有者 73 ＝ 单例 53 ＋ 静态字段 17 ＋ 组件 2 ＋ 句柄 1；＝ 挂点 40 ＋ 豁免 33 ＋ 未列出 0。
- 挂点 40 ＝ 原有 13 ＋ 本单元 27。本单元的 27：
  - Application 10：`ClientSettingsDomainService`、`WeaknessDomainService`、`CharacterContainerDomainService`、`HousingState`、`ExpertStoreDomainService`、`PvpLobbyDomainService`、`DungeonEntryIntentDomainService`、`JoustMaintenanceService`、`SelectedSessionSafety`、`EffectOperationLedger`（裁定 1(a)：后两者是附录 F「保留」项，子 Spec B 迁移时断言随之迁移）；
  - Protocol 17：`DungeonRunDeliveryRegistry`；10 个每日刷新（`AltarQuota`、`DimensionQuota`、`GoldSilverHornQuota`、`ItemUsage`、`MirrorQuota`、`Premium`、`ReviveCoinQuota`、`HerasQuota`、`TournamentQuota`、`Fatigue` 的 `*DailyRefreshService`）；`PartyProjectionStreamRegistry` 与句柄 `PartyDeliveryStream`；`PartyClientIdRegistry`；`BossTowerDepartureProjectionQueue`、`IllusionTowerDepartureProjectionQueue`；`JoustBetHandler`（静态的按连接物理身份）。10 ＋ 1 ＋ 10 ＋ 2 ＋ 1 ＋ 2 ＋ 1 ＝ 27。
- 豁免 33 ＝ E1 7（执行器、调度服务、关闭原因、事件链存储、事件注册表、事件唤醒通道、`CoalescingGameplaySource`）＋ E2 21（规则缓存 6、应用层常量表 9、编码常量字节 6）＋ E3 4（`NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`、`CommandBus` 的静态分派深度，指名附录 B 新行「请求级帧（过渡）」）＋ R061 1。每项的附录 B 行与理由在 `GameplayStateCensusTests.Exemptions`。
- 与规划表（§2.3，24 个挂点）的差异：规划的 24 个都在；多出 3 个（两个离场投影队列、`JoustBetHandler`）；规划写的 3 个「投影」是文件名，持有者是文件里的刷新服务；`PartyClientProjectionService` 的 `next` 实际在同文件的 `PartyClientIdRegistry`。逐行对照在 `logs/task-30-census.txt`。

### 2.4 挂点的写法与分类

- 每个写成员方法体开头一行 `GameplayWriteAssertion.Writing(nameof(类型) + "." + nameof(成员))`；单行表达式体的成员改为块体（生产 diff 新增行的形状逐条列在 `logs/task-30-boundary.txt`）。分类表由 201 行增为 296 行（`DFO_ENTRY_TABLE_OUT` 渲染：写 194 ＋ 读 88 ＋ 豁免 14 ＝ 296，42 个类型）；新增 95 行 ＝ 写 57 ＋ 读 26 ＋ 豁免 12（基准 137 ＋ 62 ＋ 2 ＝ 201）。其中 6 行是私有句柄 `Weakness` 的成员（写 4：`SetWeakness`、`Recover`、`PauseRecovery`、`ResumeRecovery`；读 2：`get_Value`、`RecoveryGoldCost`）。豁免 12：每日刷新 10 个 `RefreshAsync` 与 `JoustMaintenanceService.TickAsync` 只把作业体入队并等待，指名附录 B「生产者的入队侧」，`JoustMaintenanceService.StartAsync` 以启动作业运行恢复，指名「开始监听前的启动期工作」。读里带注的 4 条：`WeaknessDomainService.For`（交出句柄）与 `CharacterContainerDomainService` 的三个查找（事务内读暂存副本）。
- 新增 `Writing` 调用 69 处 ＝ 分类表里的写成员 57 ＋ 分类表列不到的成员 12（每日刷新的 `RefreshCoreAsync` 10、`JoustMaintenanceService.AdvanceAndPublishAsync` 1、`JoustBetHandler.HandleMessageAsync` 1）；生产中共 143 ＋ 69 ＝ 212 处。私有的 `Weakness.SetRecovery` 不再断言：它只经 `PauseRecovery`、`ResumeRecovery` 进入，两者各自断言。
- 分类表列不到的写：每日刷新的 `RefreshCoreAsync`（私有）、`JoustMaintenanceService.AdvanceAndPublishAsync`（私有）、`JoustBetHandler.HandleMessageAsync`（protected）。它们的断言只由运行期反例证明（变异 MU1c 只让反例变红，分类判据看不到，是有意记下的边界）。`WeaknessDomainService` 的私有句柄第 1 轮也在此列，且只有 `Recover` 有反例；评审 F1 后把句柄类型列进 `Types` 与「不持有状态的写入口」表，分类表逐成员登记，反例补上 `SetWeakness`、`PauseRecovery` 两次执行器外调用（变异 MU11a、MU11b），以后新增的写成员也会被分类判据要求断言。
- `PartyDeliveryStream`：自动属性的两个 setter 改为带 `field` 的访问器以容纳断言；`CurrentMembers`、`FullStateMembers` 两个 getter 交出调用方会改动的活集合，按「既读又写按写」归写，getter 也断言。这是挂点中唯一改了成员形状的一处，签名、可见性与逻辑不变。
## 3. A 期间改写的既有判据（RQ-071、AC-018 的 A 一半）

按 `t005-test-dispositions.md`、`t006-test-dispositions.md`、`task-07-conflicts.md` 与 `spec.md` R3-1 RQ-071 一行收集，再按 `git diff a02bce46 a9c79e98 -- Server/tests` 中文件名含 Architecture／Registration／Boundary／Source／Structure 的已有文件核对。`task-07-conflicts.md` 中处置为改写的都是时序类行为用例，没有判据类。行号前者为 `a02bce46`，后者为 `a9c79e98`。

| # | 用例 | 旧断言 → 新断言 | 保护意图 | 反例 | 出处 |
|---|---|---|---|---|---|
| 1 | `CommandHandlerArchitectureTests.ATypeOutsideTheVocabularyIsRefused` | 5 行 InlineData → 7 行，新增 `INetworkMessageSender`、`IGameMessageSender`（`:111`、`:112`） | 命令处理器不能直接注入发送器（AC-011 第 6 条） | 用例本身是 `ProductionCommandHandlers_OnlyTakeDomainShapes` 的反例；新增两行的区分力：`logs/task-26-mutations.txt` MF5 | R3-1 RQ-071；TASK-26 §5 |
| 2 | `CommandBusTests.EveryProductionBehaviourResolvesForACommandResultType` | 期望 8 个管线行为（`:170-177`）→ 5 个（`:116-120`），删去 50、55、57 | 每个生产管线行为都能按结果类型解析，期望集合就是今天的管线 | 变异 MR1（`logs/task-30-mutations.txt`） | `t005-test-dispositions.md` TASK-22 §3 G-B1 |
| 3 | `CommandBusTests.OnlineUserScopedRegistration_RequiresAnOnlineUserCommand` | 删除 | 规格消除（访问方式删除） | 不适用 | 同上 §2 D7 |
| 4 | `CommandBusTests.AccountScopedRegistration_RequiresAnAccountCommand` | 删除 | 同上 | 不适用 | 同上 D7 |
| 5 | `UnitOfWorkBehaviorTests.UnitOfWork_OpensInsideTheAccountAndTransactionalGatesAndOutsideTheExecutionPolicy` | Account／Transactional 的次序 → SelectedSessionExecution ＜ Settlement ＜ UnitOfWork ＜ ExecutionPolicy（`:122-131`）；方法名未改（F-87） | 结算包住事务、事务包住执行 | 变异 MR2 | 同上 §3 G-U1 |
| 6 | `CommandRegistrationTests.GameTicketRedemptionIsTransactionalAndOnlineUserExclusive` | 删去访问方式两条，只剩 `ExecutionKind ＝ Transactional`（`:121`） | 兑换命令是事务命令 | 变异 MR3 | 同上 §3 G-R1 |
| 7 | `PartyCommandCodecTests.Production_registers_all_eight_wire_keys_and_handlers_plus_seven_party_commands` | 删去访问方式两条，只剩 `ExecutionKind ＝ Direct`（`:346`） | 七条队伍命令都是 Direct | 变异 MR4 | 同上 §3 G-R2 |
| 8 | `NotificationRegistrationTests.A_registered_handler_is_found_with_the_retry_limit_it_declared` | 删去 `RetryLimit` 断言，只剩处理器与组装器类型 | 注册表按通知类型找到所注册的处理器 | `NotificationRegistrationTests.A_notification_nobody_registered_is_named_when_it_is_asked_about`、`NotificationRegistrationTests.A_notification_registered_twice_is_refused`、`NotificationRegistrationTests.A_catalogued_notification_with_no_handler_is_refused` | `t006-test-dispositions.md` TASK-23 §4 |
| 9 | `NotificationHandlerArchitectureTests.The_pvp_publication_declares_the_allowance_it_meant_to` | 删除（连同「注册的是哪个处理器」一条） | 规格消除（DR-13 删重试） | 不适用；「是哪个处理器」今天只由目录完备性检查间接守着，见 F-124 | 同上 §2 R5 |
| 10 | 〔边界〕`ScheduledTaskTests.Invalid_configuration_fails_before_callbacks` | 5 行 → 4 行（删 `parallelism`） | 非法配置在回调前失败 | 每行本身是拒绝用例 | `t005-test-dispositions.md` TASK-22 §2 D8 |
| 11 | 〔边界〕`ScheduledTaskTests.Factory_uses_configured_singletons_and_explicit_options_override_defaults` | 删并发上限断言；`Assert.Single(…GetServices<IHostedService>())` 改为 `OfType<ScheduledTaskHostedService>()`（TASK-11） | 工厂注册的任务用配置的单例，且恰有一个调度器 | 变异 MR10 | TASK-22 §3 G-S3；`tasks/TASK-11.md:639` |

属于子 Spec B、只列名的：`CommandHandlerArchitectureTests` 的注入面重写、`CommandBusTests.SendAsync_RejectsNestedCommandDispatch`、`EffectSettlementRegistrationTests`、`InventorySlotBoundaryArchitectureTests`、`EventNotificationBoundaryTests`——A 期间都没改（#1 的两行反例除外）。

## 4. A 新建的判据（RQ-072 的 A 一半）

「反例」一栏写证明它会拒绝的用例；没有合成反例的写变异编号（本单元 `logs/task-30-mutations.txt` 的 MR 系列，或以往单元的日志）。

| # | 判据 | 它拒绝什么 | 反例 | 区分力证据 |
|---|---|---|---|---|
| N01 | `PasswordComputationLimiterTests.Domain_services_and_command_handlers_take_neither_the_hasher_nor_the_limiter` | 领域服务或命令处理器注入口令哈希或其限流器 | `PasswordComputationLimiterTests.A_domain_service_that_takes_the_hasher_is_refused`；本单元补 `PasswordComputationLimiterTests.A_command_handler_that_takes_the_limiter_is_refused`（另两支） | 变异 MU10b |
| N02 | `PasswordComputationLimiterTests.Hashing_inside_a_job_fails_at_once_as_a_program_error` | 作业内调用限流器 | 用例本身 | `logs/task-14-mutations.txt` V5 |
| N03 | `DetachmentSiteTests.Detached_work_starts_only_at_the_listed_sites` | 名单外的分离点；名单上已不分离的文件 | `DetachmentSiteTests.A_detachment_outside_the_listed_sites_is_refused`；过期一支由变异 MR11 证明 | `logs/task-16-mutations.txt` V2、V3；MR11 |
| N04 | `GameplayCallDepthTests.The_depth_limit_covers_twice_the_longest_legal_chain_of_appendix_A` | 深度上限不覆盖两倍最长合法链 | `GameplayCallDepthTests.The_depth_criterion_refuses_a_limit_below_twice_the_chain_or_below_eight` | 反例本身 |
| N05 | `GameplayCallDepthTests.Every_source_kind_of_the_spec_exists` | 来源种类不等于规格的七种 | 变异 MR5 | MR5 |
| N06 | `GameplayCallDepthTests.Work_at_the_limit_runs_and_one_past_it_is_refused_with_the_full_chain_logged`、`GameplayCallDepthTests.A_default_chain_is_refused_as_an_argument_error` | 超限作业与空链 | 用例本身 | `logs/task-29-mutations.txt` MT9 |
| N07 | `GameplayJobHostStructureTests.Only_the_sealed_gameplay_executor_derives_from_the_job_host_in_the_loaded_production_assemblies` | 第二个宿主；执行器不是 sealed | `GameplayJobHostStructureTests.A_second_job_host_is_named_by_the_reflection_criterion`；sealed 一支由变异 MR9 证明 | `logs/task-19-mutations.txt` S5、S5b；MR9 |
| N08 | `GameplayJobHostStructureTests.Only_the_two_host_files_name_the_job_host_in_the_production_sources`（本单元加固，§5） | 两个宿主文件以外命名宿主，含转义、字符串内 `//`、项目文件别名 | `GameplayJobHostStructureTests.A_job_host_declared_in_a_third_source_file_is_named_by_the_source_criterion`；本单元补 `GameplayJobHostStructureTests.The_source_criterion_sees_through_an_escape_a_string_and_a_project_alias`（3 行） | `logs/task-19-mutations.txt` S5；MU5、MU6、MU7 |
| N09 | `GameplayJobHostStructureTests` 的 5 条所有权用例与正向对照 | 宿主使用别的执行器签发的令牌 | 用例本身 | `logs/task-19-mutations.txt` S1～S4、S7～S9 |
| N10 | `GameplayWriteAssertionTests`（本单元起 52 个方法、61 个用例；执行器外违规 53 条） | RQ-061 运行期：执行器外写入即抛并取证 | 各条本身（`*_refuses_*`）；本单元补 27 条 | `logs/task-18-mutations.txt` R5 等；本单元 MU1a、MU1b、MU1c、MU11a、MU11b，RED 第一段 |
| N11 | `GameplayWriteEntryTableTests.Every_member_of_the_listed_types_is_classified_and_exactly_the_writes_assert` | 未分类成员、写成员不断言、读成员断言、过期或错名的行 | `GameplayWriteEntryTableTests.An_unclassified_member_is_refused`、`GameplayWriteEntryTableTests.A_write_member_without_the_assertion_is_refused`、`GameplayWriteEntryTableTests.A_read_member_that_asserts_and_a_stale_or_misnamed_row_are_refused`、`GameplayWriteEntryTableTests.Asynchronous_writes_are_seen_through_their_state_machine` | `logs/task-18-mutations.txt` R6、R7；MU1a、MU1b |
| N12 | `GameplayWriteEntryTableTests.Transactions_are_created_only_at_the_listed_sites_and_the_writes_assert` | 名单外的事务开启点；写开启点先于断言；第二个快照调用者 | 同文件 3 条 | `logs/task-18-mutations.txt` R1～R4、R8 |
| N13～N15 | `LockRemovalSourceTests` 的 J1、J2、J3 | 删除栏的名称、名单外信号量、其他等待原语 | C1～C9、N10、N11 | `logs/task-21-mutations.txt`、`logs/task-22-mutations.txt`、`logs/task-24-mutations.txt` |
| N16 | `GameplayDispatchTests.A_telemetry_handler_that_injects_anything_but_logging_is_refused_at_startup` | 遥测 handler 注入日志以外的依赖 | 用例本身（喂入有状态的遥测 handler） | 用例本身就是对生产启动检查的反例 |
| N17 | `GameplayJobTokenTests.The_abstractions_keep_their_internals_from_the_application` | Abstractions 对 Application 开放 internal | 变异 MR6 | MR6 |
| N18 | 〔边界〕`GameplayExecutionRegistrationTests.Command_dispatching_alone_registers_one_executor_behind_the_queue` | 只调 `AddCommandDispatching` 的容器没有唯一执行器 | 变异 MR7 | MR7 |
| N19 | 〔边界〕`TimerSourceReconciliationTests.Each_timer_source_runs_its_ticks_as_its_own_cmd_on_the_executor` | 定时源的节拍不是自己类型的 CMD | — | `logs/task-11-mutations.txt` V1、V8 |
| N20 | `NotificationDeliveryTests.A_projection_off_the_executor_is_a_program_error_reported_as_evidence` | 执行器外的即时投影 | 用例本身 | `logs/task-23-mutations.txt` S9 |
| N21 | 〔对照〕`GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder` | 不拒绝：证明取证记录器装好 | 不适用 | `logs/task-18-mutations.txt` R11 |
| N22 | `ContentWarmupTests.W2_every_lazy_or_first_use_index_holder_is_warmed_or_exempt_with_a_reason` | 未预热也未豁免的首次索引 | `ContentWarmupTests.W2_rejects_an_unlisted_lazy_field_and_an_unlisted_first_use_index` | `logs/task-15-mutations.txt` V11 |
| N23 | `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption` | 既不是 CMD 也不是带理由豁免的路由 | `GmGameplayRouteTests.A_route_that_is_neither_a_cmd_nor_an_exemption_with_a_reason_is_refused` | `logs/task-14-mutations.txt` V1；`logs/task-14-red.txt` |
| N24 | `HostOutputViolationTests.An_assertion_exception_in_a_line_of_host_output_is_recognised` | 宿主输出里的断言异常漏记 | `HostOutputViolationTests.Other_lines_are_not_violations`（不误报） | `logs/task-18-mutations.txt` R12；`logs/task-26-mutations.txt` MF1 |
| N25 | `HostOutputWiringSourceTests.Every_test_file_that_starts_a_process_passes_its_output_to_the_recorder` | 启动进程却不接记录器的测试文件 | 同文件 3 条 | `logs/task-26-mutations.txt` MF2、MF3 |
| N26 | `OutboxRegistrationTests` 的 N3 两条 | 执行器外的发送与关闭登记 | 用例本身；本单元补 `OutboxRegistrationTests.An_unencodable_message_off_the_executor_is_refused_by_position_first` | `logs/task-23-mutations.txt` S9；MU9 |
| N27 | 〔对照〕`OutboxRegistrationTests.An_ordered_scope_off_the_executor_does_nothing` | 不拒绝 | 不适用 | — |
| N28 | `OutboundRealSocketTests.A_job_that_writes_to_a_connection_directly_fails_as_a_program_error` | 作业内直接写连接 | 用例本身 | `logs/task-23-mutations.txt` S10 |
| N29 | `TransportWriteSourceTests.Only_the_listed_files_write_to_a_transport` | 名单外的传输写入形状 | `TransportWriteSourceTests.A_session_send_in_any_other_file_is_named`、`TransportWriteSourceTests.A_second_write_in_a_listed_file_and_a_listed_file_without_one_are_named`；本单元补 `TransportWriteSourceTests.A_sequence_send_and_a_udp_send_in_any_other_file_are_named`（另两种形状） | `logs/task-23-mutations.txt` S20；MU10a |
| N30 | `OutboundQueueLimitTests.The_queue_limit_holds_twice_the_largest_selection` | 队列上限低于选角上界两倍 | `OutboundQueueLimitTests.The_criterion_rejects_a_limit_below_twice_the_bound` | 反例本身 |
| N31 | 〔边界〕`OutboundQueueLimitTests.The_selection_bound_is_the_sum_of_its_inputs`、`OutboundQueueLimitTests.The_same_rows_reproduce_the_measured_selection` | 选角上界与各行之和不符 | 变异 MR8（前者） | MR8 |
| N32 | `TransportDrainRegistrationTests.The_networking_module_registers_the_transport_drain_as_a_producer_drain` | 排空未注册为生产者排空 | — | `logs/task-10-mutations.txt` round 2「drain-unregistered」 |
| N33 | 本单元：`GameplayStateCensusTests.Every_singleton_holding_mutable_state_is_a_write_entry_or_a_listed_exemption` | 未列出的持有者、无理由豁免、过期行、两表重复、不持有状态的写入口不再成立 | `GameplayStateCensusTests.The_census_refuses_an_unlisted_holder_an_unreasoned_exemption_and_a_stale_row`（5 行） | RED 第一段（26 个未列出）；MU2、MU3、MU4、MU12 |
| N34 | 本单元：`GameplayJobHostClosureTests.Only_the_sealed_gameplay_executor_derives_from_the_job_host_in_the_full_production_closure` | 完整生产闭包中的第二个宿主；闭包少了四个只由源码一支覆盖的程序集 | 反射核心与 `GameplayJobHostStructureTests.A_second_job_host_is_named_by_the_reflection_criterion` 共用 | MU8、MR9 |

另有程序错误判定类的运行期守卫（`CommandBusExecutorTests.L1_a_flow_leaked_from_a_finished_job_is_refused_and_never_queued`、`EffectOperationReplayTests.A_flow_leaked_from_a_closed_operation_cannot_reach_the_bus`、`GameplayQueueLeakTests.Enqueueing_from_a_flow_leaked_from_a_finished_job_is_refused`、`GameplayExecutorSelfWaitTests` 等），它们本身就是反例，证据见 `logs/task-18-mutations.txt` R9、`logs/task-29-red-green.txt`、`logs/task-19-mutations.txt` S7。

缺反例的处置（§7.3）：合成反例补 2 条（N01 两支、N29 两种形状），其余以变异证明（MR1～MR11）；N19、N32 已有以往单元的变异证据。

## 5. F-76：`GameplayJobHost` 结构判据加固

| 要求 | 实现位置 | 证据 | 状态 |
|---|---|---|---|
| 反射一支扩到完整生产闭包 | `tests/Dfo.Server.Host.Tests/GameplayJobHostClosureTests.cs`；闭包收集与宿主判定抽到 `tests/Dfo.Server.Application.Tests/Execution/GameplayJobHostClosure.cs`，两个测试项目共用（Host.Tests 的 csproj 加一行链接） | 闭包包含 GameServer、GmWeb、Host、HttpApi；变异 MU8（排除 GmWeb 即红）、MR9（去掉 sealed 即红） | 满足 |
| 源码一支：Unicode 转义 | `GameplayJobHostStructureTests.Unescaped` | Theory 行 escape；变异 MU5 | 满足 |
| 源码一支：字符串里的 `//` | `GameplayJobHostStructureTests.WithoutComments` 改为最小词法扫描（普通、逐字、插值字符串与字符字面量；原始字符串整段保留） | Theory 行 string；变异 MU6 | 满足 |
| 源码一支：项目文件里的 `<Using>` 别名 | 扫 `src/**/*.csproj`、`Directory.Build.props`、`Directory.Build.targets` 的 `<Using>` 项（先做 XML 实体解码） | Theory 行 alias；变异 MU7；生产扫描读到的项目文件数见判据输出 | 满足 |
| 二者的关系 | 反射一支为主判据，源码一支兜底闭包之外（`Dfo.Server.Utilities` 没有源文件；新项目在接入宿主之前） | 两个文件的 remarks | 满足 |

识别边界：原始插值字符串洞里的注释按字面保留（只会多认，不会漏认）；`Dfo.Server.Utilities` 不在闭包里。已有 9 个用例的断言不变（`logs/task-30-diff-check.txt`）。

## 6. F-97：登记适配器先判位置、后编码

主控方裁定第 2 条推翻 TASK-23 裁定（Revision 2）第 1 条里与次序相关的部分：当时为了让 `OutboundSendFailureTests` 4 条不改而取「先编码」，代价是执行器外 ＋ 不可编码同时成立时不写取证。编码本身仍在登记点完成，不可编码的消息仍让发出它的调用失败。

| 要求 | 实现位置 | 证据 | 状态 |
|---|---|---|---|
| 三个发送入口为「找连接 → 位置检查 → 编码 → 登记」 | `src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:51-53`、`:76-78`、`:100-102`、`:120-131`；类 remarks 改写 | `OutboxRegistrationTests.An_unencodable_message_off_the_executor_is_refused_by_position_first`（三个入口各一次，取证 3 条，规划只算了 1 条）；变异 MU9；`logs/task-30-retrievals.txt` R7 | 满足 |
| `OutboundSendFailureTests` 4 条改在作业内调用，断言不变 | 4 个发送包进 `TestGameplay.RunAsync(fixture.Services, …)`；夹具加 `Services` 属性 | 断言行多重集合不变（`logs/task-30-diff-check.txt`）；正式全量通过、取证无记录 | 满足 |

## 7. Spec 附录的改动与授权依据

| 位置 | 改动 | 依据 |
|---|---|---|
| 附录 F「保留」 | 补 `PasswordComputationLimiter` 的信号量 | F-85；附录 F「实施时如果发现本表以外的并发原语，补入本表」 |
| 附录 F「改为执行器内访问」 | 补「名单」一段：唯一出处是 `GameplayWriteEntryTableTests.Types` 与 `GameplayStateCensusTests.Exemptions`，守它的是普查判据，识别边界在判据 remarks | 裁定 1；RQ-011 |
| 附录 B「生产者的入队侧」 | 补 `LoginSessionMaintenanceService` 的关闭触发出队循环与卡牌协调器的 `Dispose` | F-62 评审补记 |
| 附录 B「开始监听前的启动期工作」 | 「作为 RQ-061 的启动期例外」改为现状 | F-75 |
| 附录 B 新行「请求级帧（过渡）」 | 请求级 AsyncLocal 帧为什么不挂点，由子 Spec B 改为显式数据 | 裁定 1(b)；RQ-011 |

RQ-061 的正文与 Spec 正文不改（`logs/task-30-boundary.txt`）。名单扩大属于 Spec 自身「实施时补入」的授权，覆盖面扩大交外部审核时说明（§1 末）。

## 8. 测试处置

- N ＝ 40（25 个方法）：`GameplayStateCensusTests` 1 ＋ Theory 5 行；`GameplayWriteAssertionTests` 27（17 个 Fact ＋ 每日刷新 Theory 10 行）；`GameplayJobHostStructureTests.The_source_criterion_sees_through_an_escape_a_string_and_a_project_alias` 3 行；`PasswordComputationLimiterTests.A_command_handler_that_takes_the_limiter_is_refused` 1；`GameplayJobHostClosureTests` 1；`OutboxRegistrationTests.An_unencodable_message_off_the_executor_is_refused_by_position_first` 1；`TransportWriteSourceTests.A_sequence_send_and_a_udp_send_in_any_other_file_are_named` 1。6 ＋ 27 ＋ 3 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＝ 40。D ＝ 0。
- 与规划（33）的差别：运行期反例 h 为 27（规划 24，多出普查找到的 3 个）；普查反例 5 行（加了静态字段一行供 MU4、评审 F2 的捕获参数一行供 MU12）；§7.3 补了 k ＝ 2。24 ＋ 1 ＋ 3 ＋ 1 ＋ 3 ＋ 1 ＝ 33，换成 27 ＋ 1 ＋ 5 ＋ 1 ＋ 3 ＋ 1 ＋ 2 ＝ 40。
- §3.3 包装：RED 第二段 610 个失败（Application 559 ＋ Protocol 50 ＋ Networking 1）来自 85 个调用点，三轮包装后再多 6 个调用点（第一轮暴露 5 个、第二轮 1 个），共 91 处，分布在 45 个测试文件（清单在 `logs/task-30-boundary.txt` 的 wrap 一栏）。包装只有两种形状：单条语句包成 `TestGameplay.Run(...)`／`RunAsync(...)`，或几条语句（含断言）整体放进一个作业块；断言文本不改，被包装文件的断言行多重集合不变（`logs/task-30-diff-check.txt`）。线级测试只有 `IllusionTowerStageFrameIntegrationTests` 的 3 行容器登记被包装，断言不变。

## 9. 移交

- 给 TASK-33：附录 A、B、E、F、H 的最终对账在冻结代码上做；附录 B 的「请求级帧（过渡）」行与 E3 名单要和过渡机制清单一致。
- 给 TASK-34：A 最终汇报要提的共享语义变化——附录 F「改为执行器内访问」扩大为普查名单（§1 末）；F-97 推翻了 TASK-23 裁定的次序部分（§6）；过渡机制清单（根 SC-12）收入请求级帧 4 项（`NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`、`CommandBus` 的静态分派深度），由子 Spec B 改为显式数据；`EffectOperationLedger`、`SelectedSessionSafety` 的断言随 B 迁移。
- 新事项：F-124（`NotificationHandlerArchitectureTests` 删除的「pvp 处理器是哪个」断言今天只间接守着）。

本单元关闭：F-60、F-62、F-75、F-76、F-85、F-97。

## 10. 第 1 轮评审后的记录

- 评审 F1、F2 已按主控方处置修改（§2.1、§2.4、§4 N10、N33）。取证由 71 条增为 73 条：`Weakness` 句柄的反例多报 `SetWeakness`、`PauseRecovery` 两条。重跑的检查在 `logs/task-30-rework.txt`。
- 评审 F4（两处包装改为传 services）未改：`SelectCharacterEntryLifecycleServiceTests` 与 `SelectCharacterProductionStateTests` 的被测服务都是手工构造的，用例里没有容器，同文件已有的 `TestGameplay` 调用也都不传 services；`TestGameplay` 的约定是「有容器时传入」。详见 `tasks/TASK-30.md`。
- 评审 F3、F5、F6 只作记录，内容由主控方补记（评审正文见 `reviews/task-30-result-review.md`）：
  - **F3**：藏在其他程序集类型后面的状态，普查认不出（remarks 已声明这条边界）。评审对两个程序集所有 readonly 字段做了 IL 探针：类型来自 Abstractions／Data／BCL 的，单例上只找到执行机制（如 `GameplayOutboxes.slot`、唤醒用的 `Channel`）；没有以接口类型字段直接 `new` 出可变集合的写法；指向同程序集类的静态只读字段都指向不持有状态的 record。结论是今天没有漏掉的玩法状态。
  - **F5**：`CharacterContainerDomainService` 的 `Find`／`Get`／`GetContainers` 在事务内会登记参与者，却按读归类（`ReadNote(StagedRead)`）。理由是只做暂存，不写就不提交；在执行器外走的是加锁直读。评审认为可以接受。TASK-33 做附录对账时须知悉：按 TASK-18 的六种形状，「登记参与者」算写，这里是有意的例外。
  - **F6**：`SendExplicitAsync` 里有两条路径不先做位置检查。一是 `ValidateExplicitDelivery` 排在位置检查之前，投递格式错误时先抛格式错误；二是整份投递为空或全部被跳过时，不做位置检查。这是基准就有的行为，不在 F-97「编码与位置检查的先后」范围内。
- 评审 F4 的处置由主控方确认：维持不改，理由如上（用例没有容器，为此新建空容器不改变行为）。
- 重跑中新见的偶发：`IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter` 在 Application 单独运行中失败一次，原因是随机奖励选中了 PVF 缺 `[minimum level]` 字段的装备（`equipment/creature/artifact_green/orb_wind.equ`，`PvfEquipmentDefinitionSource.cs:160`）；单独复跑 5 次全过；栈中没有本单元改动的代码。记为 F-125。

# T-005 收口对账

- 日期 2026-10-23。基准提交 `09e4983c`（TASK-21）加上 TASK-22 未提交的改动。
- 用途：判定 T-005「删锁」是否完成，以及 T-006 的进入条件是否成立。
- 写法：仿 `t004-closure.md`，只写「交付 → 哪个 TASK → 证据位置 → 状态」。未关闭事项只引用 F 编号，内容在 `follow-ups.md`。
- 证据底稿：
  - `tasks/TASK-21.md`、`tasks/TASK-22.md` 的 Result；
  - 日志 `logs/task-21-*.txt`、`logs/task-22-*.txt`；
  - 测试处置 `t005-test-dispositions.md` 的 TASK-21、TASK-22 两节。
- 状态词：
  - **完成**：T-005 范围内的部分都有证据；
  - **完成，部分移交**：T-005 范围内完成，Plan 或裁定把其余部分分给了后续步骤；
  - **部分满足**：Spec 验收的一部分今天成立，其余归后续步骤，今天不得记为已满足。
- **拆分**（TASK-21 主控方裁定第 1 条）：T-005a ＝ TASK-21（Protocol 层），T-005b ＝ TASK-22（Application 层、命令契约与本收口），共 2 个单元。

## 1. Plan T-005 行逐项

| 交付 | TASK | 证据 | 状态 |
|---|---|---|---|
| 进入条件一：T-004 的门断言与 RQ-061 判据零违规 | TASK-18、TASK-19 | `t004-closure.md` §5、§8 | 完成 |
| 进入条件二：M-1 通过 | TASK-20 | `measurement/m1-summary.md` §1 | 完成 |
| Protocol 自有的 7 个门类型与虚弱投递门 | TASK-21 | 判据 J1 的 11 个全局名称规则命中 0（`logs/task-21-lock-scan.txt`、`logs/task-22-lock-scan.txt`）；虚弱投递门由 J2 名单不含该文件守着 | 完成 |
| Application 的门：`SelectedSessionTransitionGate`（含借用帧）、`UserExecutionGate<T>`、`OnlineUserExecutionGate`、`AccountExecutionGate`、`TransactionalExecutionGate`、`UserExecutionGate<CharacterId>` 注册 | TASK-22 | 5 个文件整个删除；J1 的对应名称规则在全部 `Server/src` 命中 0；`logs/task-22-sites.txt` A1、A2 | 完成 |
| 信号量：账本每连接、Joust `ticks`、组门、调度并发槽、事务准入门 | TASK-22 | J2 名单由 12 项减为 7 项，删去的 5 个文件不再出现 `SemaphoreSlim`；J3 其他等待原语 0 | 完成 |
| 管线 45 号的取锁部分 | TASK-22 | 删租约列表、逐账号借用、`finally` 释放与构造参数（`logs/task-22-sites.txt` B1）；非锁职责见 §4 | 完成 |
| 管线 50、55、57 号 | TASK-22 | 三个文件整个删除，注册删除；`CommandBusTests.EveryProductionBehaviourResolvesForACommandResultType` 断言今天的管线是 0、45、58、60、100 | 完成 |
| 多账号声明与访问方式 | TASK-22 | `IMultiAccountCommand` 与 18 处声明、`IOnlineUserCommand` 与 56 处声明、`UserAccessMode` 与 125 处注册实参；J1 的 `IMultiAccountCommand`、`IOnlineUserCommand`、`UserAccessMode`、`OnlineUserAccessMode`、`AccountAccessMode` 规则命中 0；`logs/task-22-verify.txt` 证明删除前没有第二个读者 | 完成 |
| `LeaseSet` 一族 | TASK-21 | J1 的 `LeaseSet`、`TransitionLeaseSet`、`ProjectionLeaseSet` 规则命中 0 | 完成 |
| 「锁内复查、重试」循环 | TASK-21 | 七个循环逐条分类并删除（`logs/task-21-loops.txt`）；`reroute`、`RetryLimit` 在 Protocol 中为 0（`logs/task-21-sites.txt`「after」） | 完成 |
| 组门 | TASK-22 | J1 的 `groupGates` 规则命中 0；组内串行与并发排空的证据见 §5 AC-015 | 完成 |
| 调度并发槽与 `Task.Run` | TASK-22 | §2 检索式结束时 `ScheduledTaskHostedService.cs` 的 `Task.Run`、`SemaphoreSlim` 为 0；`ScheduledTaskSchedulerOptions` 删除（J1）；`Started`／`Requested` 的读取挪进作业体（TASK-22 主控方裁定第 2 条，变异 S11）：合并语义保留，并扩大到执行器排队期间（§8） | 完成 |
| 参与者集合与 45／58 号行为的非锁职责原样保留 | TASK-22 | `logs/task-22-duties.txt`：45 号 18 项、`SelectedSessionSafety.BlockAsync` 8 项、账本 15 项、分发器 17 项、调度器 12 项逐项「基准行 → 结束行」文字相同、次序不变；58、60、100、0 号代码未改（60 号只改 4 行注释）；`IMultiSelectedSessionCommand.Sessions` 未改 | 完成 |
| 改写或删除测门与测交错的用例 | TASK-21、TASK-22 | `t005-test-dispositions.md`：TASK-21 新增 15、删除 11；TASK-22 新增 9、删除 45；每条删除写了承接者或规格消除理由；空转 V1～V7 与 N-V1～N-V4 全部处置 | 完成 |
| 落实 VC-7 | TASK-21 | 见 §5 | 完成 |

全量与取证（T-005 结束时的状态，TASK-22 正式全量 `logs/task-22-build-test.txt`、`logs/task-22-violations.txt`）：8192 条，失败只有 2 条 `DimensionDungeonTests`（`baseline.md`），跳过 20；取证 39 条，反例类与对照以外 0，没有测试帧 0，`exe:` 0。

## 2. 附录 F「删除」栏逐项

| 项 | 删于 | 判据或检索证据 | 状态 |
|---|---|---|---|
| `SelectedSessionTransitionGate`（含借用帧、`AcquireManyExclusiveAsync`） | Protocol 的使用：TASK-21；类型：TASK-22 | J1：`SelectedSessionTransitionGate`、`BorrowOrAcquireSharedAsync`、`BorrowOrAcquireExclusiveAsync`、`AcquireManyExclusiveAsync` 全局 0 | 完成 |
| `TownAreaProjectionGate`、`DungeonProjectionGate`、两塔的投影门与入场门、`AchievementTitleProjectionGate` | TASK-21 | J1 的 7 条全局规则 0 | 完成 |
| 队伍按角色的信号量与 `PartyDeliveryStream.Gate` | — | **归 T-006，今天仍在**：J2 名单上 `PartyNotificationSlice.cs`（5 处）、`GameplayAssertions.cs`（`GameplayGateAssertion.Enter`，1 处）注明「T-006 deletes」；§2 检索式结束时 `GameplayGateAssertion.Enter` 只在 `PartyNotificationSlice.cs`、`PartyTownProjectionDelivery.cs` 与 `GameplayAssertions.cs` 本身（`logs/task-22-sites.txt`）。它们仍在使用，所以不能列进 J1 的名称规则；T-006 删除时把名称加入 J1、从 J2 名单删去 | 归 T-006 |
| `OnlineUserExecutionGate`、`AccountExecutionGate`、`UserExecutionGate<T>`，`ApplicationModule.cs:280` 的 `UserExecutionGate<CharacterId>` 注册 | TASK-22 | J1：`OnlineUserExecutionGate`、`AccountExecutionGate`、`UserExecutionGate` 全局 0 | 完成 |
| `TransactionalExecutionGate` | TASK-22 | J1 全局 0；4 个领域使用者与分发器的 3 处取门删除（`logs/task-22-sites.txt` C1、C2） | 完成 |
| 管线 45、50、55、57 号的取锁部分 | TASK-22 | J1：`OnlineUserExecutionBehavior`、`AccountExecutionBehavior`、`TransactionalExecutionBehavior` 全局 0；45 号见 §1 | 完成 |
| `IMultiAccountCommand`，命令注册中的 OnlineUser／Account 访问方式 | TASK-22 | J1：`IMultiAccountCommand`、`UserAccessMode`、`OnlineUserAccessMode`、`AccountAccessMode` 全局 0；另删了只供 50 号读的 `IOnlineUserCommand`（TASK-21 裁定第 6 条） | 完成 |
| `IMultiSelectedSessionCommand` 的取锁用途 | TASK-22 | 45 号行为不再按 `Sessions` 取租约（`logs/task-22-duties.txt` 前后全文）；`Sessions` 留作参与者集合，用于逐个 `DemandCurrent` 与结果不确定时逐个封锁（新增用例 `SelectedSessionExecutionBehaviorTests` 的 H6、H7 两条） | 完成 |
| 全部「锁内复查、重试」循环与私有 `LeaseSet` 类 | TASK-21 | §1 | 完成 |
| `EffectOperationLedger` 的每连接信号量 | TASK-22 | J2 名单不含该文件，J3 0 | 完成 |
| `DomainEventDispatcher` 的组门 | TASK-22 | J1 `groupGates` 0；J2 名单不含该文件 | 完成 |
| Weakness 的信号量 | TASK-21 | J2 名单不含 `WeaknessProjectionDelivery` | 完成 |
| Joust 维护服务的信号量 | TASK-22 | J2 名单不含 `JoustMaintenanceService.cs` | 完成 |
| `ScheduledTaskHostedService` 的并发槽与 `Task.Run` | TASK-22 | §1 | 完成 |
| `NotificationRetryQueue` 与 `NotificationRetryDrainService` | — | **归 T-006，今天仍在**（Plan T-006 行「删除重试队列与排空服务」）。它们今天有生产使用，不能列进 J1；T-006 删除时加入 | 归 T-006 |

投递三项不在判据名单上的理由：判据 J1 的含义是「名称在 `Server/src` 中检索为零」，只能对已经删完的项开启；这三项的删除属于 T-006（Plan T-006 行），在那之前开启就是红的。判据类注释写明了这一点（`LockRemovalSourceTests` 类注释）。

## 3. 「保留」栏逐项确认仍在

| 项 | 证据 | 状态 |
|---|---|---|
| 参与者集合（`IMultiSelectedSessionCommand.Sessions`） | 接口与成员未改；45 号行为在读（`logs/task-22-duties.txt` H5、H6、H7） | 在 |
| 短 `lock` | `\block ?\(` 逐层重数（`logs/task-22-sites.txt` 附录 E）：应用层 190 → 184、协议层 11 → 11、持久化层 3 → 3。应用层少的 7 行在删除的两个门类型文件里（门内部的锁，随门删除），多的 1 行是调度器新加的 `lock (job) scheduledAt = job.Requested;`（短锁，裁定第 2 条）：190 － 7 ＋ 1 ＝ 184。附录 F 的「190／11／3」是在更早的基准上数的，只记录不追究 | 在 |
| PVF 内容源与规则初始化的锁与信号量 | J2 名单 `JoustRuleService.cs`、`CharacterStatRuleService.cs`、`PvfItemCatalogSource.cs`、`PvfBossTowerContentSource.cs`，判据输出各 1 处 | 在 |
| `GameInboundFrameCounter`、`TouchSocketConnectionRegistry`、GmWeb 两个类 | `Dfo.Server.Networking`、`Dfo.Server.GmWeb` 本单元不改（`logs/task-22-boundary.txt`：改动只在 Application、Abstractions、Protocol） | 在 |
| `AcquireWriteAsync` 的写保留 | `Dfo.Server.Persistence` 不改；F-68 的四条 SQLite 竞争用例保留且通过 | 在 |
| 另：`PasswordComputationLimiter` 的信号量 | J2 名单（2 处），附录 F「保留」栏未列，F-85（T-009） | 在，附录待补 |

## 4. 附录 H 第 45、58 行对账

这是 AC-009 中附录 H 对账的一半；最终对账（含主结算识别方式的改变与 `EffectSettlementRegistrationTests` 重写）归 T-008。

| 附录 H 第 45 行的职责 | 代码（结束时，`SelectedSessionExecutionBehavior.cs`） | 守着它的用例 |
|---|---|---|
| `BeginMain`（一请求一主结算） | `:12` | `EffectSettlementRegistrationTests`、`EffectOperationReplayTests`（计划 §4.1 的基准阅读，本单元未变异） |
| `DemandRecipient` | `:13` 与 `:25` 两次：附录 H 的一项对应代码中的两次调用（R9：两次都保留，不合并） | 同上 |
| `Capture` 接收者 | `:15` | `CommonExperienceEffectTests.A_stale_or_different_recipient_cannot_write`（计划 §4.1） |
| 账号与会话一致性检查 | `:16`～`:17` | 新增 `A_command_whose_account_differs_from_its_session_never_reaches_its_handler`（变异 S6 只使它变红） |
| 参与者集合必须包含报告会话 | `:18`～`:20` | 新增 `A_participant_set_without_the_reporting_session_never_reaches_its_handler`（S4） |
| 逐个 `DemandCurrent` | `:23`～`:24` | 新增 `A_participant_that_is_no_longer_current_stops_the_command_before_its_handler`（S7） |
| 结果不确定时逐个 `BlockAsync` | `:29`～`:33` | 新增 `An_uncertain_outcome_fences_every_participant_not_only_the_reporter`（S5）。计划点名的 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 在 S5 下不红，它不守这一项 |
| 设置 `SelectedSessionCommandScope` | `:26` | 新增 `The_handler_runs_with_the_captured_recipient_as_its_selected_session_scope`，另有 3 条已有用例（S8） |
| 取锁部分删除 | 租约列表、借用、`finally` 释放已删 | J1 |

| 附录 H 第 58 行的职责 | 状态 |
|---|---|
| `authorization.DemandCurrent`、接收者 `DemandCurrent`、`Settled`／`Failed` 记录、结果不确定时 `BlockAsync` | `SelectedSessionSettlementBehavior.cs` 代码未改（`git diff` 为空，`logs/task-22-duties.txt`）；它调用的 `SelectedSessionSafety.BlockAsync` 只少了借用，其余 8 项文字与次序不变 |

## 5. 验收与 VC-7

- **VC-7**（TASK-21）：
  - 七个循环的分类与删除：`logs/task-21-loops.txt`；
  - 四个请求（放弃副本、离队、换队长、踢出队员）的证据用例：`BossTowerProjectionConcurrencyTests` 中以 `_outside_a_tower_runs_the_ordinary_path_once_` 命名的四条，变异 S6、S7 下变红（`t005-test-dispositions.md` TASK-21 §6，`logs/task-21-mutations.txt`）；
  - 两塔协调器在 `ExactSessions` 为假时的确定性拒绝**不属于 VC-7**：那条路径的结果与基准逐字相同（N10、N15，变异 S5），附录 G VC-7 已按 TASK-21 处置 L1 写明。
- **AC-015「订阅组内顺序与失败退避后的继续处理」**（组门删除后的证据，TASK-22 主控方裁定第 1 条）：
  - 组内串行由「每组一个排空循环、逐条等待事件 CMD 完成」保证：改写后的 `DomainEventDispatchTests.A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off` 由 `RunAsync` 组循环驱动，`MaximumOutstanding == 1`；变异 S12（每组两个循环）使它变红；
  - 测试线程与组循环并发排空时每条只消费一次、退避中的不被提前重试：新增 `Concurrent_drains_of_one_group_consume_each_delivery_once_and_never_retry_a_backed_off_one_early`；依据是 `TryBeginAsync` 只在 `state=0 AND next_attempt_at<=now` 时领取；
  - Host 的 `GmEventsHttpTests`、`GmOfficialMailHttpTests` 在后台组循环运行时从测试线程排空，正式全量与单独跑 Host 都通过（`logs/task-22-isolated.txt`）。
- **AC-017**：**部分满足**。
  - 「删除栏在 `Server/src` 中检索为零」：投递以外的部分满足（判据 J1～J3，§2）；投递三项由 T-006 完成，今天**不得**记为已满足；
  - 「运行期判据在执行器外调用时抛出（反例用例）」：RQ-061 写断言自 TASK-18 开启，反例 `GameplayWriteAssertionTests`（24 条取证记录，`logs/task-22-violations.txt`）。
- **AC-009**：附录 H 第 45、58 行的对账见 §4；其余（具名判定、效果账本、会话隔离等既有测试断言不变并通过）由正式全量支撑，最终对账归 T-008。

## 6. 移交清单（只列 F 编号与归属）

| 归属 | F 编号 |
|---|---|
| T-006 | F-20～F-25、F-63、F-64、F-65、F-83、F-84；附录 F「删除」栏的投递三项（§2）；之后 F-61（Host 取证收口复核） |
| T-007 | F-26～F-29、F-59、F-66 |
| T-008 | F-07、F-12（附录 C 一半）、F-30～F-34、F-58（复核一半）、F-86、F-87（改名一半）、F-88、F-93 |
| T-009 | F-35、F-36（AGENTS.md 一半）、F-60、F-62、F-75、F-76、F-85、F-89、F-92 |
| M-2 与之后 | F-02；F-77～F-82（交主控方立项）；F-69（每个后续单元的耗时对比） |
| 主控方 | F-01 |

## 7. 结论与 T-006 进入条件

- **T-005 的交付在 TASK-22 的改动上全部有证据**（§1～§4）：Plan T-005 行逐项完成，附录 F「删除」栏中投递以外的项全部删除，「保留」栏逐项仍在，附录 H 第 45、58 行的非锁职责原样保留。其中 H4～H8 每项有用例（变异 S4～S8 各使对应的一条变红）；H1～H3 依据计划 §4.1 的基准阅读，本单元未逐项变异；`BlockAsync` 的 `DiscardExact` 没有用例守着（F-93）。
- T-005 在 TASK-22 经结果审查并提交之后完成；今天的状态是「完成，部分移交」：投递三项按 Plan 归 T-006。
- **未满足、今天不得记为已满足的 Spec 验收**：AC-017 的「检索为零」一半中投递三项（T-006）；AC-009 的附录 H 最终对账（T-008）；以及 `t004-closure.md` §8 列出、仍未满足的 AC-006（F-62，T-009）、AC-005 的测量一半（M-2）、AC-007 的严格形式（F-20，T-006）。
- **T-006 的进入条件**：Plan Work 表中 T-006 依赖 T-003 与 T-005。
  - T-003 已完成（TASK-05，`0223e646`）；
  - T-005：本收口判定完成，以 TASK-22 提交为准；
  - 所以 **TASK-22 提交后，T-006 的进入条件成立**。
- T-006 开始时要接手：F-20～F-25、F-63、F-64、F-65、F-83、F-84，附录 F「删除」栏的投递三项（删除时把名称加入判据 J1、从 J2 名单删去 `PartyNotificationSlice.cs` 与 `GameplayAssertions.cs`），以及 `GameplayGateAssertion` 删除时取证源与正向对照改挂写断言（F-84）。

## 8. 已接受的差异

| 差异 | 依据 | 状态 |
|---|---|---|
| 调度器：基准上并发槽默认 4 个、几乎不争用，一个已进入执行器队列、尚未开始的 RunOnce 任务再次到期时会补跑一次；现在 `Started`／`Requested` 在作业体开头读取，这种情况合并为一次、按最新时刻执行。即「排队中的运行按最新时刻合并」这条语义保留，并从「等槽期间」扩大到「执行器排队期间」 | TASK-22 主控方裁定第 2 条的意图；评审 L2（`reviews/task-22-result-review.md`）。实际触发不到：执行器排队是毫秒级，调度周期是分钟到天级。区分两种写法的是变异 S11（`Global_limit_queues_one_instance_and_coalesces_waiting_runs` 变红） | 已接受 |

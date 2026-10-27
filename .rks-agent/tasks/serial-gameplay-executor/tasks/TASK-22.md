# TASK-22 — T-005b：删除 Application 层的门、管线取锁与访问方式，T-005 收口

基准提交 `09e4983c`（TASK-21／T-005a 已提交：Protocol 中不再有任何门、取门、`LeaseSet` 与重试循环）。范围取自 `plan.md` T-005 行（第 96 行，含「参与者集合与 45/58 号行为的非锁职责原样保留」）与共享决定中的「定时源」「事件」两条，`spec.md` RQ-060、RQ-043、附录 F「删除」栏、附录 H 第 45、58 行、AC-009（附录 H 对账的一半）、AC-015、AC-017、附录 G VC-7。

输入还有：
- `tasks/TASK-21.md` 文末「后续单元边界提纲」、主控方裁定（执行前）第 6、7、8 条、主控方处置（评审后）；
- `reviews/task-21-result-review.md` 的发现 L1 与剩余风险；
- `verification/logs/task-21-vacuous.txt` 的 V1～V7 与 N-V1～N-V4；
- `verification/follow-ups.md` 中归属 T-005／TASK-22 的条目（§10）；
- `verification/task-07-conflicts.md` §15.1 交 T-005 的 3 条 `CommandBusTests`。

本单元不拆分，理由见 Detailed Plan §0。裁定第 6、7、8 条的核实结果见 §1：第 6、8 条成立；第 7 条在生产代码中成立，但依据要改写，而且测试宿主里确实存在同组并发排空，转回主控方（待裁定第 1 条）。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **删除管线的取锁部分**（RQ-060；附录 F「删除」栏的管线一项；附录 H）：
     - 删除 50 `OnlineUserExecutionBehavior`、55 `AccountExecutionBehavior`、57 `TransactionalExecutionBehavior`（连同同文件的 `TransactionalExecutionGate`）三个文件及其注册；
     - 45 `SelectedSessionExecutionBehavior` 只删租约列表、借用循环、`finally` 中的释放和构造参数 `lifecycle`，附录 H 第 45 行的非锁职责逐项保留（§4.1）；
     - `SelectedSessionSafety.BlockAsync` 只删借用（`:64`）和构造参数 `lifecycle`，其余不动。58、60、100、0 号行为不动。
  2. **删除 Application 的门类型**：`SelectedSessionTransitionGate.cs`、`OnlineUserExecutionGate.cs`（`UserExecutionGate<T>`、`OnlineUserExecutionGate`、`AccountExecutionGate`）、`TransactionalExecutionGate`；删除注册 `CommandDispatchingExtensions.cs:26`～`:29` 与 `ApplicationModule.cs:280`；删除 `TransactionalExecutionGate` 的 5 个领域使用者中的取门（§3.3）。
  3. **删除命令契约中的取锁声明**：
     - 访问方式：`UserAccessMode` 枚举、`CommandExecutionContext` 与 `CommandHandlerRegistration` 的两个字段、`AddCommandHandler` 的两个参数、`CommandBus.cs:63`～`:64`，以及 125 个注册调用点（§3.4）；
     - `IMultiAccountCommand` 及其 18 个实现上的声明，`AccountIds` 按读者处置（§3.4 表）；
     - `IOnlineUserCommand` 及其 56 个实现上的声明（裁定第 6 条），`OnlineUserId` 按同一规则处置。
     - `IAccountCommand`、`IMultiSelectedSessionCommand` 与它们的成员保留（45 号行为在读）。
  4. **删除其余信号量与并发外壳**：
     - `DomainEventDispatcher` 的组门与准入门（裁定第 7 条，待裁定第 1 条）；
     - `ScheduledTaskHostedService` 的并发槽与 `Task.Run`，以及 `ScheduledTaskSchedulerOptions` 与 `AddTaskScheduling(options)` 参数（裁定第 8 条）；
     - `EffectOperationLedger` 的每连接信号量（由 RQ-005 替代）；
     - `JoustMaintenanceService` 的 `ticks`（由 RQ-006 与单消费者替代）。
  5. **判据扩到全部 `Server/src`**（AC-017；RQ-072）：`LockRemovalSourceTests` 的 Protocol 范围名称改为全局，去掉 `UserAccessMode` 的例外，补上 Application 的门类型、管线行为、访问方式成员、多账号与在线用户声明和调度选项；信号量名单删去五个 TASK-22 项；新增「其他等待原语为零」一支。每条新规则配反例（§5）。
  6. **测试侧**：按 TASK-21 §6.2 的分类（E／K／G／B／V，新增 N）逐条处置并记录，包括：
     - TASK-21 的空转名单 V1～V7 与 N-V1～N-V4；
     - `task-07-conflicts.md` §15.1 的 3 条 `CommandBusTests`（F-67）；
     - N5 按待裁定第 1 条改写；
     - F-68 的跨容器 SQLite 竞争用例保留不改。
  7. **一次正式全量**：带取证汇，反例类与对照以外的记录仍为 0；表征测试与金样本不变；变异核对（§8）。
  8. **T-005 收口**：新建 `verification/t005-closure.md`（§9），逐项对照 Plan T-005 行与附录 F「删除」栏（投递项除外，归 T-006），写明 T-006 的进入条件是否成立；VC-7 汇总注明两塔协调器 `ExactSessions` 为假的确定性拒绝不属于 VC-7（TASK-21 评审 L1）。
- Owned boundary：
  - **生产代码**：
    - `Server/src/Dfo.Server.Application/**`：§3 列出的文件（约 30 个，含 5 个整个删除的文件）；
    - `Server/src/Dfo.Server.Abstractions/**`：`Scheduling/ScheduledTasks.cs`（删 `ScheduledTaskSchedulerOptions`）、`Execution/GameplayAssertions.cs`（只改类注释）、`Marriage/MarriageDomainContracts.cs`（删 `WeddingAccountsAsync`，待裁定第 3 条）；
    - `Server/src/Dfo.Server.Protocol/**`：`ProtocolModule.cs` 的访问方式实参、命令记录上的 `IOnlineUserCommand`／`IMultiAccountCommand` 声明及只服务于它们的计算属性、`MarriageWeddingSlice.cs` 的 `AdvanceMarriageWeddingCommand`、F-91 的一行 `using` 与两处注释。基准命中清单由 §2 的检索式生成（`logs/task-22-sites.txt`）。
  - **测试**：
    - 删除 `Application.Tests/Execution/GameplayGateAssertionTests.cs`、`Commands/OnlineUserExecutionGateTests.cs`、`Commands/SelectedSessionTransitionGateTests.cs`、`Commands/MultiAccountExecutionTests.cs`；
    - 改 `Application.Tests/Execution/LockRemovalSourceTests.cs`；
    - §6 列出的改写、空转处置与编译迫使文件（Application、Protocol、Persistence、Host 四个测试项目），以及 §6.5 的新增用例（可能新建 `Application.Tests/Commands/SelectedSessionExecutionBehaviorTests.cs`）。逐文件列入 `verification/t005-test-dispositions.md` 的 TASK-22 一节。
  - **文档与证据**：
    - `verification/t005-test-dispositions.md`（续写 TASK-22 一节）；
    - 新建 `verification/t005-closure.md`；
    - `verification/follow-ups.md`；
    - `verification/logs/task-22-*.txt`；一次性脚本放 scratchpad，全文附在对应日志末尾。
  - **不改**：
    - `Dfo.Server.Networking`、`Dfo.Server.Persistence`、`Dfo.Server.Infrastructure`、`Dfo.Server.GmWeb`、`Dfo.Server.HttpApi`、`Dfo.Server.Host` 的生产代码；
    - 队伍的两把投递门、`GameplayGateAssertion` 的代码（只改类注释）、`NotificationRetryQueue`／`NotificationRetryDrainService`、`ProtocolGateAssertionTests` 的 4 条与 `PartyTownProjectionDeliveryTests` 的 2 条反例（都归 T-006）；
    - 58、60、100、0 号行为的代码（`UnitOfWorkBehavior` 只改描述锁序的注释）；
    - `spec.md`、`plan.md`、`AGENTS.md`、`Docs/`；表征测试与金样本；`task-07-conflicts.md`（只读引用）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-22-build-test.txt`。
     - **总数**：TASK-21 为 54 ＋ 80 ＋ 1699 ＋ 477 ＋ 970 ＋ 175 ＋ 4582 ＋ 191 ＝ 8228（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host）。本单元为 8228 ＋ N － D，逐项目写加法，N、D 逐条列名（预期见 §6.6）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（按 AGENTS.md「判据偶发失败」三样一起取身份）；跳过 20；挂起转储 0。
     - **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本不改（`git status` 为证）。
     - **日志信号**：与 TASK-21 Checks 2 同口径、同脚本（scratchpad `t19/analyze.py`）。应为 0 的七项仍为 0；`GameplayGateWait`、`GameplayLeakedFlow` 各 1，只在 `HostOutputViolationTests` 的显示名里；`Gameplay bus:… failed` 8、`inbound:… failed` 3、`gm:… failed` 2 逐条与 TASK-21 对照，不同的逐条说明。
     - 耗时与内存只作信息（RQ-091 用户修订：单条指令 ≤ 2 s 即可接受，性能不作停止条件）。F-69：与 TASK-21 那次全量逐条对比耗时，多出 ≥ 9 秒的逐条隔离复核。
  3. **零违规取证**（`logs/task-22-violations.txt`）：`violations.py` 的命令与 TASK-21 Checks 3 相同，去掉 `GameplayGateAssertionTests=21` 与 `MultiAccountExecutionTests=1` 两项。预期先写进日志开头再跑（§7）：合计 39；反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  4. **判据输出**（`logs/task-22-lock-scan.txt`）：三支判据各自扫描的文件数、每条名称规则的命中（应为 0）、信号量名单逐项（文件、理由、去向）及其实际命中、其他等待原语的命中；判据用例与全部反例的通过记录；先 RED（只改测试时）后 GREEN 的两次输出。
  5. **删除点清单**（`logs/task-22-sites.txt`）：§2 检索式在基准上的全部命中，逐行写「文件:行 → 处置（§3 的簇与 TASK-21 的 R1～R8、本单元的 R9）→ 保留下来的非锁语句」。执行结束时同一检索式只剩 §2 列出的例外。
  6. **非锁职责对账**（`logs/task-22-duties.txt`）：§4 的每一项写「基准文件:行 → 结束时文件:行 → 语句逐字比较」；45 号行为与 `SelectedSessionSafety.BlockAsync` 的前后全文并列附在日志里。
  7. **裁定核实**（`logs/task-22-verify.txt`）：§1 的检索命令与输出全文，执行开始时在基准上重跑一次，结束时再跑一次（第 6 条的读者、第 7 条的排空调用方、第 8 条的选项调用方）。基准上的结果与 §1 不一致时按停止条件 2 处理。
  8. **测试改动核对**（`logs/task-22-diff-check.txt`）：`diffcheck7.py 09e4983c --lines`（scratchpad `t07/diffcheck7.py`）加 `t21/methods.py`，证明已有测试的方法集合只少了 D 中列出的方法、只多了 N 中列出的方法，断言行多重集合的变化逐处对应 `t005-test-dispositions.md` 的一行。
  9. **新增与改写测试 ×10**（`logs/task-22-new-x10.txt`）：本单元新增的全部用例，加上 N5 与 §6.3 中改写的调度器、事件分发用例，隔离连续 10 次，全部通过。
  10. **变异核对**（`logs/task-22-mutations.txt`；程序与 TASK-21 Checks 10 相同：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：S1～S13 见 §8，每项都要有指定的用例变红；标为「抽查」的如实记录。
  11. **生产改动边界**（`logs/task-22-boundary.txt`）：`git diff --stat 09e4983c -- Server/src` 只含 Owned boundary 列出的三个项目；逐文件对到 `task-22-sites.txt` 的行；`Server/src` 下没有未跟踪文件。
  12. **隔离复跑**（`logs/task-22-isolated.txt`）：Application、Protocol、Host 各一次，都带取证目录。记录各自的正向对照与反例类记录数：Application 49 － 21 － 1 ＝ 27（24 ＋ 1 ＋ 1 ＋ 对照 1）；Protocol 7（4 ＋ 2 ＋ 1）；Host 只有对照 1。Host 单独跑一次，是因为它的两个文件在后台排空循环运行时从测试线程排空同一组（§1.2）。
  13. **收口文档**：`verification/t005-closure.md` 按 §9 的结构写完，其中的每个数字都能在本单元或此前单元的日志里找到。
  14. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。结束时执行 `dotnet build-server shutdown`。
- Dependencies：
  - TASK-21（`09e4983c`）：Protocol 不再使用 Application 的门（判据 J1 的 Protocol 范围规则命中 0，`logs/task-21-lock-scan.txt`）；`t005-test-dispositions.md` 的 TASK-21 一节；空转名单 `logs/task-21-vacuous.txt`。
  - TASK-17、TASK-18 的门断言、写断言、取证汇与 `verification/probe-tools/violations.py`；`task-07-conflicts.md` §14～§15 的处置口径。
  - M-1 通过（TASK-20，`verification/measurement/m1-summary.md` §1）与 T-004 完成（`verification/t004-closure.md` §8）：收口时引用，作为 T-005 进入条件的证据。
  - 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t21/*`（已核对存在）；`violations.py` 在 `verification/probe-tools/`。
- Result review：required。
  - 本单元改的是所有命令都经过的管线和注册契约：`AddCommandHandler` 的签名、`CommandExecutionContext` 的形状、管线层数。生产代码约 95 个文件，测试四个项目。
  - 主要风险与 TASK-21 相同：**把非锁职责当成锁的一部分删掉**。45 号行为里取锁与非锁语句交织在同一个 `try` 里；错删一项（例如结果不确定时逐个封锁、参与者集合必须包含报告会话），全量多半照样是绿的，因为这两项今天没有直接守着的用例（§4.1）。
  - 事件分发与调度器的改动涉及语义判断：组内串行由什么保证、排队中的 RunOnce 任务如何合并。变异只能证明判据有区分力，证明不了这些判断都对。
  - T-006 与 T-008 在本单元的结果上继续，收口文档是 T-006 进入条件的依据。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 45、58 号行为、`SelectedSessionSafety`、`EffectOperationLedger`、`DomainEventDispatcher` 中某条语句既不能归为取锁，也不能原样保留（删锁会改变它的执行条件或次序）；
    2. Required check 7 在基准上的结果与 §1 不一致，例如找到 `IOnlineUserCommand` 或 `IMultiAccountCommand` 的第二个读者（反射、泛型约束、模式匹配），或 `DrainOnceAsync`／`RunAsync` 的生产调用方不止 §1.2 所列；
    3. 需要改 Owned boundary 以外的生产文件，或需要改 `spec.md`、`plan.md`；
    4. 正式全量出现 §6 规则覆盖不了的新失败；取证出现反例类与对照以外的记录；
    5. 表征测试或金样本需要改动；
    6. 某个测门、测交错或测访问方式的用例，保护意图既找不到承接者，也说不出它为什么已被规格消除；
    7. 判据在扫描中找到本单元范围以外、附录 F 没有列出的并发原语（按附录 F 末句应补入附录 F，属于改 Spec）；
    8. 删除 `Task.Run` 后，`ScheduledTaskHostedService.Start` 的同步部分不只是「上锁、记账、入队」，即计时循环可能被回调阻塞（§3.6）。
  - 变异核对期间允许临时改动 `Server/src`，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 不拆分的理由与执行顺序

**规模**（基准 `09e4983c`，§2 检索式）：289 行命中，分布在 93 个文件（Application 27、Protocol 64、Abstractions 2）。另有 6 个文件只因信号量或 `GameplayGateAssertion.Enter` 被列入（`EffectOperationLedger`、`JoustMaintenanceService`，以及 `TransactionalExecutionGate` 的 4 个领域使用者）。

**为什么不拆**：
- **类型互相锁住**。访问方式、`IOnlineUserCommand`、`IMultiAccountCommand` 的唯一读者是 50、55 号行为（§1.1）。
  - 先删行为、后删声明：中间状态留下一批无人读的声明，判据也无法扩到全局，违背 RQ-060「不留转发」。
  - 先删声明、后删行为：编译不过。
  - 所以两者只能在同一个单元里删掉，才能各自是绿的。
- **判断量集中**。Protocol 的 64 个文件都是机械改动：删一个接口名、一个实参或一个计算属性，由编译器和判据 J1 守着。需要判断的只有约 10 个 Application 文件（§3.2、§3.5、§3.6）。TASK-21 那种「63 个文件逐处判断」的结构不在这里。
- **收口要在全部删完之后**，拆开会多一次正式全量，而第一半的结果不能单独收口。

**执行顺序**：
1. 在基准上重跑 §1 的核实（Required check 7）与 §2 的检索式（Required check 5 的前半）。
2. 先改判据与反例（§5），看它在基准上红（J1 名称命中、J2 名单外与陈旧条目）。
3. 按簇改生产代码，每簇改完构建一次，跑该簇的测试项目：
   1. 簇 C：`TransactionalExecutionGate` 的 4 个领域使用者与事件分发器的准入门（§3.3、§3.5 前半）；
   2. 簇 B：45 号行为与 `SelectedSessionSafety`（§3.2）。改完立即做变异 S4～S8，按 §6.5 补缺口；
   3. 簇 A：管线 50／55／57、门类型、注册（§3.1）；
   4. 簇 D：访问方式与命令声明（§3.4）；
   5. 簇 E：组门（§3.5 后半）与 N5（§6.3）；
   6. 簇 F：调度器（§3.6）；
   7. 簇 G：效果账本、Joust（§3.7）；
   8. 簇 H：注释与 `using`（§3.8）。
4. 测试侧处置（§6），构建、正式全量与取证（Required checks 1～3），隔离复跑，新增与改写用例 ×10。
5. 变异核对的其余各项（§8）。
6. 清单、对账、边界、diff 核对、`follow-ups.md`，最后写 `t005-closure.md`。

簇 C 排在 A 之前：准入门在三个地方被领域代码直接取，先把这些使用点清掉，删除类型时只剩管线一处。簇 B 排在 A 之前：45 号行为的改动最需要判断，趁它还和其余改动分开时做变异。

### 1. 裁定第 6、7、8 条的核实

检索都在基准 `09e4983c` 上做过一遍，执行时按 Required check 7 重跑并落盘。

#### 1.1 第 6 条：`IOnlineUserCommand` 除 50 号行为外没有其它读者——成立

- 检索：`grep -rnE "(is|as|typeof\(|where \w+ ?:|<)\s*IOnlineUserCommand|IOnlineUserCommand\s*[)>]" Server/src Server/tests`，以及 `Tool/`、`Docs/`、`protocol/`。
- 结果：
  - 生产中唯一的读者是 `OnlineUserExecutionBehavior.cs:22`（模式匹配）与 `:27`（`nameof`，异常消息）；
  - 测试中只有 `CommandBusTests.cs:108`（`nameof`）与 `:302`（测试命令实现它），都随 §6 删除；
  - 没有反射、没有泛型约束；`Tool/`、`Docs/`、`protocol/` 无命中；`AGENTS.md` 有描述（归 T-009，§10）。
- 实现：56 处，全部在 `Dfo.Server.Protocol` 的 51 个文件里。
- `OnlineUserId` 的处置：
  - 主构造器参数是命令数据，保留；
  - 只为满足接口而写的计算属性有 3 个，没有其它读者，随接口删除：`ClientSettingsCommands.cs:16`、`:38`，`InformNotice/InformNoticeSlice.cs:67`。
  - 这与 §3.4 对 `AccountIds` 的规则相同。执行时用编译结果确认这 3 个计算属性没有别的读者；有读者就保留，并在 sites 清单里写明。

#### 1.2 第 7 条：组排空只经一个排空者驱动——生产中成立，依据要改写，测试中不成立

- **裁定写的依据与代码不符**：裁定写「组排空在执行器上串行执行」。实际上排空在执行器**外**：`DrainOnceAsync(subscriberId)` 的开头是 `GameplayJob.AssertNotOnExecutor("Draining domain event deliveries")`（`DomainEventDispatcher.cs:74`），它在执行器外读出到期的 delivery，把每一条的消费作为一条事件 CMD 入队并等待完成（`:86`～`:89`）。在执行器上串行的是**每条 delivery 的消费**，不是排空。
- **生产中只有一个排空者**：
  - `DrainOnceAsync` 是 `internal`。生产中唯一的调用方是 `RunGroupAsync`（`:200`），每组一个，由 `RunAsync`（`:187`～`:188`）为每个组各起一个。
  - `RunAsync` 唯一的生产调用方是 `DomainEventHostedService`（`:252`），用 `TryAddEnumerable` 注册一次（`DomainEventDispatchingExtensions.cs:23`）。
  - `NotificationRetryDrainService.DrainOnceAsync` 是同名的另一个方法，与此无关。
  - 所以在生产中，同一组同一时刻至多一个排空者，组门从来没有被争用过。
- **测试中同组并发排空确实存在**：
  - `Host.Tests/GmEventsHttpTests.cs:279`、`:287`、`:293`、`:295` 与 `GmOfficialMailHttpTests.cs:239` 在 `GmTestHost` 上从测试线程调用 `DrainOnceAsync()`，而同一宿主里 `DomainEventHostedService` 的组循环也在运行；
  - N5（`DomainEventDispatchTests.Executor.cs:163`）自己起 3 个并发的 `DrainOnceAsync("counter")`。
  - 今天组门让这些并发排空排队；删掉之后它们会交错。
- **交错之后是否仍然正确**（代码阅读，执行时由 N5 与 Host 用例的结果确认）：
  - 每条 delivery 的消费是一条执行器作业；
  - 作业里的第一步 `TryBeginAsync` 是带条件的更新：`state=0 AND next_attempt_at<=@now`（`SqliteDomainEventPersistence.cs:65`～`:70`）。
  - 所以两个排空者读到同一条 delivery 时，只有先执行的那条作业消费它，后一条得到 `false`、不重复调用订阅者；失败后已退避的 delivery 也不会被另一个排空者提前重试。
  - 消费次数与退避语义不依赖组门；依赖组门的只有「同一组同一时刻至多一条已入队、未完成的事件 CMD」，即 N5 的 `MaximumOutstanding == 1`。
- **结论**：「同组并发排空在结构上不可能」对生产代码成立，对测试宿主不成立。裁定第 7 条要求不成立时转回主控方，见待裁定第 1 条（建议仍不加守卫）。

#### 1.3 第 8 条：`ScheduledTaskSchedulerOptions` 随并发槽删除——成立

- 生产中没有调用方传选项：`ApplicationModule.cs:101` 与 `ScheduledTaskExtensions.cs:27`、`:36` 都调用无参的 `AddTaskScheduling()`；`:18` 只在未传选项时注册默认值。
- 读者只有 `ScheduledTaskHostedService` 的构造器（`:35`、`:38`、`:39`）。
- 测试中传选项或读它的只有 `ScheduledTaskTests.cs:33`、`:278`、`:281` 与 `GameplayGateAssertionTests.cs:348`，都在 §6 处置。

### 2. 检索式与例外

```
grep -rnE --include='*.cs' \
  "SelectedSessionTransitionGate|OnlineUserExecutionGate|AccountExecutionGate|UserExecutionGate|TransactionalExecutionGate|BorrowOrAcquire(Shared|Exclusive)Async|AcquireManyExclusiveAsync|OnlineUserExecutionBehavior|AccountExecutionBehavior|TransactionalExecutionBehavior|UserAccessMode|OnlineUserAccessMode|AccountAccessMode|IMultiAccountCommand|IOnlineUserCommand|ScheduledTaskSchedulerOptions|MaximumConcurrency|groupGates|WeddingAccountsAsync|SemaphoreSlim|GameplayGateAssertion\.Enter|Task\.Run" \
  Server/src
```

基准上前 20 项共 289 行、93 个文件；`SemaphoreSlim`、`GameplayGateAssertion.Enter`、`Task.Run` 另计。执行结束时只允许剩下：
- `SemaphoreSlim`：§5 名单上的 7 个文件；
- `GameplayGateAssertion.Enter`：`PartyNotificationSlice.cs`、`PartyTownProjectionDelivery.cs` 与 `GameplayAssertions.cs` 本身（T-006）；
- `Task.Run`：Networking 的 `ExecutorSendDeadline.cs`、`OutboundSender.cs`，Application 的 `GameplayExecutor.cs`，Infrastructure 的 `PvfItemCatalogSource.cs`、`PvfArchiveIdentity.cs`（都不是玩法门，基准上已在）；`ScheduledTaskHostedService.cs` 为 0；
- 其余命中为 0。

### 3. 生产侧删除清单

沿用 TASK-21 §3 的 R1～R8（R4 的「串行下恒不成立」只用于 §3.3、§3.5 中取门后的复查，基准阅读未发现这样的复查）。本单元另加一条：

| 规则 | 内容 |
|---|---|
| R9 | 45、58 号行为、`SelectedSessionSafety`、`EffectOperationLedger` 中**不做 R4(a)**：即使两次检查之间只隔着被删的取锁，两次检查也都保留（例如 45 号行为的两次 `DemandRecipient`）。理由：Plan T-005 行要求这些非锁职责「原样保留」，附录 H 把它们的去向交给 T-008；在这里合并检查会让 T-008 对账时找不到原样 |

R6（取消）照旧：被删的 `AcquireAsync`／`BorrowOrAcquire*Async`／`GameplayGateAssertion.Enter` 都顺带检查一次取消令牌；删除后不补显式检查，取消位置的变化按 TASK-21 处置 L3 的口径写进 sites 清单的附录（「不引入基准不可达的取消结果」）。

#### 3.1 簇 A：管线 50／55／57、门类型与注册

| 删除 | 位置 |
|---|---|
| 50、55 号行为 | `Commands/Pipeline/OnlineUserExecutionBehavior.cs`、`AccountExecutionBehavior.cs`（整个文件） |
| 57 号行为与 `TransactionalExecutionGate` | `Commands/Pipeline/TransactionalExecutionBehavior.cs`（整个文件） |
| 门类型 | `OnlineUsers/SelectedSessionTransitionGate.cs`、`OnlineUsers/OnlineUserExecutionGate.cs`（整个文件；命名空间 `Dfo.Server.Application.OnlineUsers` 随之为空） |
| 注册 | `CommandDispatchingExtensions.cs:26`～`:29`（四把门）、`:44`～`:49`（三个行为）、`:5` 的 `using`；`ApplicationModule.cs:280`（`UserExecutionGate<CharacterId>`）及 `:42` 的 `using`（若不再需要） |
| 访问方式的机制 | `CommandContracts.cs:15`～`:20`（`UserAccessMode`）、`:77`～`:78`（`CommandExecutionContext` 两个字段）；`CommandHandlerRegistry.cs:8`～`:9`；`CommandDispatchingExtensions.cs:67`～`:68`、`:81`～`:82`；`CommandBus.cs:63`～`:64` |

50、55、57 号行为在附录 H 中没有非锁职责行。它们各有一项「注册了访问方式却没实现接口就抛」的校验（`OnlineUserExecutionBehavior.cs:22`～`:28`、`AccountExecutionBehavior.cs:23`～`:33`），55 号还有「账号列表为空就抛」（`:32`～`:33`）。这些校验只对访问方式有意义，随访问方式一起删除，按规格消除记录（§6.2 的 `CommandBusTests` 两条）。

#### 3.2 簇 B：45 号行为与 `SelectedSessionSafety`

- `SelectedSessionExecutionBehavior.cs`：
  - 删 `:6` 的构造参数 `lifecycle`、`:2` 的 `using`；
  - 删 `:22`（租约列表）、`:25`～`:26`（逐账号借用）、`:39`～`:42`（`finally` 逆序释放）；
  - `try`／`catch` 保留：`catch` 中结果不确定时逐个 `BlockAsync`（`:33`～`:38`）是非锁职责。删掉 `finally` 后，`try` 体内原有的语句原位保留、次序不变（R1）。
  - 其余逐行保留，见 §4.1。
- `SelectedSessionSafety.cs`：删 `:18` 的构造参数 `lifecycle`、`:7` 的 `using`、`:64` 的借用。`:61`～`:63` 的封锁和 `:65`～`:72` 的丢弃、日志、关连接原位保留，次序不变。
- 类名、`SelectedSessionCommandScope` 保留（T-008 按附录 H 迁移）。

#### 3.3 簇 C：`TransactionalExecutionGate` 的领域使用者

| 文件 | 删除 | 保留 |
|---|---|---|
| `World/DungeonLifecycleDomainService.cs` | `:9` 构造参数、`:19` 取门 | `:12` 已在事务内就直接入队；否则自开事务、入队、提交 |
| `World/TransactionalDungeonVisitDomainService.cs` | `:12` 构造参数、`:18` 取门；`:10`～`:11` 注释里的「writer gate」改写 | `Execute` 的「已在事务内就直接执行，否则自开事务」 |
| `World/IllusionTower/IllusionTowerDomainService.cs` | `:22` 构造参数、`:49` 取门 | 同上 |
| `World/BossTower/BossTowerDomainService.cs` | `:29` 构造参数、`:60` 取门 | 同上 |
| `DomainEvents/DomainEventDispatcher.cs`（准入门） | `:39`、`:50`、`:56` 的字段与参数；`:123`、`:137`、`:153` 的三处取门 | 三处 `transactions.Begin()` 与各自的提交、`TryBeginAsync`／`CompleteAsync`／`FailAsync` 的次序 |

今天这把门保护「同一时刻一个 SQLite 写者」，改由执行器单消费者保证；写事务的开启仍由 RQ-061 写断言守着（`GameplayWriteAssertion` 挂在工作单元入口上），执行器外开事务照旧抛出。`AcquireWriteAsync` 的写保留不动（附录 F「保留」）。

#### 3.4 簇 D：访问方式与命令声明

- **访问方式实参**：`ProtocolModule.cs` 118 处；`ApplicationModule.cs:834`、`:841`；`World/TauKingdomDailyRefill.cs:51`、`World/BossTower/BossTowerDailyRefill.cs:51`、`World/SouthernDaleDailyRefill.cs`、`Inventory/OnlineExpiredItemCleanup.cs`、`Characters/CharacterNameTagExpiration.cs` 各 1 处。118 ＋ 2 ＋ 5 ＝ 125。只删实参，执行方式（`CommandExecutionKind`）不动。
- **`IOnlineUserCommand`**：接口（`CommandContracts.cs:48`～`:51`）与 51 个 Protocol 文件中的 56 处声明；`OnlineUserId` 按 §1.1。
- **`IMultiAccountCommand`**：接口（`CommandContracts.cs:58`～`:62`，含 `:58` 的摘要注释）与 18 处声明。`AccountIds` 的处置规则：**业务代码读它的作为命令数据保留；只有 55 号行为读的计算属性或参数一并删除**。

  | 命令 | 位置 | `AccountIds` 的形态与读者 | 处置 |
  |---|---|---|---|
  | `EnterBossTowerCommand`、`BossTowerActionCommand` | `BossTowerCommands.cs:15`、`:30` | 计算属性，只有 55 号读 | 删 |
  | `EnterDungeonCommand` | `DungeonEntry/EnterDungeonCommand.cs:12` | 同上 | 删 |
  | `MarriageCeremonyCommand` | `MarriageProposal/MarriageCeremonyCommand.cs:14` | 同上 | 删 |
  | `MarriageProposalCommand` | `MarriageProposal/MarriageProposalSlice.cs:92` | 同上 | 删 |
  | `LeaveDungeonCommand` | `DungeonClear/LeaveDungeonCommand.cs:13` | 同上 | 删 |
  | `ApplyObjectDestructionCommand` | `MonsterDeath/ApplyObjectDestructionCommand.cs:19`～`:20` | 同上；`Recipients` 是业务数据（`:48`～`:61` 在读），保留 | 删 `AccountIds` |
  | `ProcessMonsterDeathCommand` | `MonsterDeath/ProcessMonsterDeathCommand.cs:13` | 计算属性，只有 55 号读 | 删 |
  | `AltarActionCommand`、`AltarDeathBatchCommand` | `Altar/AltarCommands.cs:16`、`:40` | 同上 | 删 |
  | `HousingBuyItemCommand` | `BuyItem/BuyItemCommand.cs:57` | 转发 `Purchase.AccountIds`，只有 55 号读 | 删 |
  | `GrantGmCeraCommand` | `Administration/GmCeraCommands.cs:19` | 计算属性，只有 55 号读（处理器读 `Input.AccountIds`） | 删 |
  | `SendGmMailCommand` | `Administration/GmOfficialMailCommands.cs:27` | 转发 `Preparation.Accounts`，只有 55 号读 | 删 |
  | `AdvanceMarriageWeddingCommand` | `MarriageProposal/MarriageWeddingSlice.cs:115` | 主构造器参数，处理器不读，只有 55 号读；唯一构造点 `:215` 的实参来自 `:212` 的 `WeddingAccountsAsync` | 删参数与 `:212` 的查询（待裁定第 3 条） |
  | `BuyItemCommand` | `BuyItem/BuyItemCommand.cs:16` | `init` 属性，处理器在 `:40` 传给 `housing.PurchaseAsync` | 保留 |
  | `HousingCommand`、`CapsuleCommand`、`WeddingLovePointCommand` | `MarriageHousing/HousingSlice.cs:75`、`CapsuleSlice.cs:30`、`MarriageLovePointSlice.cs:46` | 主构造器参数，处理器传给领域服务 | 保留 |

  加法：删 2 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＝ 14，保留 1 ＋ 3 ＝ 4；14 ＋ 4 ＝ 18。
- **`IAccountCommand`、`IMultiSelectedSessionCommand` 与 `Sessions` 保留**：45 号行为读它们做一致性检查与参与者集合（附录 F「保留」栏）。

#### 3.5 簇 E：事件分发器的组门

- 删 `DomainEventDispatcher.cs:46`（字段）、`:58`（构造）、`:75`～`:76`（取门）、`:77` 的 `try` 与 `:94` 的 `finally` 释放；`try` 体内的语句原位保留。
- `:71`～`:72`（事务内不得排空）与 `:74`（`AssertNotOnExecutor`）保留。`:73` 的注释改为「在作业内排空等于入队之后等待自己（RQ-003）」；反例 `DomainEventDispatchTests.Draining_from_inside_a_job_fails_at_once_as_a_program_error` 不变。
- 类注释 `:27`～`:32` 改写：
  - 去掉「门留到 T-005」；
  - 写明组内串行由「每组一个排空循环、逐条等待事件 CMD 完成」保证；
  - 写明测试线程与组循环并发排空时，每条 delivery 仍只被消费一次，由 `TryBeginAsync` 的条件更新保证（待裁定第 1 条的结果决定这一句的写法）。

#### 3.6 簇 F：调度器

- `ScheduledTaskHostedService.cs`：
  - 删 `:32`、`:35`、`:38`、`:39` 中的选项与 `concurrency`；`:69` 的 `slots`；`Start`／`RunAsync` 的 `slots` 参数（`:101`、`:110`、`:124`、`:133`）；`:139`～`:140`（`AssertNotOnExecutor` 与 `slots.WaitAsync`）；`:167`（`finally slots.Release()`）；
  - 删 `:129` 的 `Task.Run`：`Start` 直接调用 `RunAsync(job, ct)`。`:127`～`:128` 的注释改为不提槽的说法。
- **为什么计时循环不会被阻塞**：`RunAsync` 第一次 `await` 之前只做「`lock (job)` 记账」与 `GameplayTimerWork.RunAsync` 的入队。入队返回一个挂起的任务，回调本身在执行器的作业里执行。执行时逐行确认这一点（停止条件 8）。
- **`Started`／`Requested` 的读取挪进作业体**（待裁定第 2 条，建议接受）：
  - 今天 `lock (job) { job.Started = true; scheduledAt = job.Requested; }`（`:142`）在拿到槽之后、入队之前执行。删掉槽之后，若不动这一行，它会在入队时立即执行。
  - 那样，一个已入队、还没轮到的 RunOnce 任务会被当作「已开始」：再到期时记为 `Pending`，于是先按旧时刻跑一次，再补跑一次。今天它会在等槽期间合并为一次，按最新时刻执行。
  - 把这一行挪进 `GameplayTimerWork.RunAsync` 的作业体开头，就保留今天「排队中的运行按最新时刻合并为一次」的语义。`lock (job)` 是短锁（附录 F「保留」）。
  - 依据用例：`ScheduledTaskTests.Global_limit_queues_one_instance_and_coalesces_waiting_runs` 的业务断言（3 次调用、等待中的那次按最新时刻执行、`maximum == 1`）在新结构下照样成立；变异 S11 证明它能区分两种写法。
- `ScheduledTaskExtensions.cs:11`～`:19`：删 `options` 参数、`:18` 的默认值注册与 `:19` 的替换。
- `Abstractions/Scheduling/ScheduledTasks.cs:23`：删 `ScheduledTaskSchedulerOptions`。

#### 3.7 簇 G：效果账本与 Joust

- `Effects/EffectOperationLedger.cs`：删 `:38`（`Enter`）、`:39` 的 `try`、`:78` 的 `finally Release`、`:82`（`Gate` 属性）。`:40` 的 `safety.DemandCurrent(recipient)`、序号必须前进、窗口清理、重放、`Pending → Rejected` 全部原位保留（R5、R9）。`:36` 的注释「Capture before waiting」改为不提等待的说法（捕获仍在登记之前）。
- `Events/JoustMaintenanceService.cs`：删 `:15`（`ticks`）、`:29`（`Enter`）与 `:30`～`:31` 的 `try`／`finally`，`TickCoreAsync` 直接调用 `AdvanceAndPublishAsync`。合并由 `:16` 的 `CoalescingGameplaySource`（RQ-006）与单消费者保证；`StartAsync` 的启动期作业在 `base.StartAsync` 之前完成，与定时作业不重叠。
- **已知的保护变化**：`GameplayGateAssertion.Enter` 今天顺带检查「在执行器上」。删掉之后，账本与 Joust 节拍不再自带这项检查。
  - 账本的唯一调用方是 dispatcher 在入站作业里的那一段（Plan 共享决定「请求级上下文」）；Joust 节拍只经定时作业与启动作业进入。
  - 它们写的玩法状态仍由 RQ-061 写断言守着。
  - 这与 TASK-21 删 Protocol 门时的处理相同，不另加守卫，在 Result 中登记。

#### 3.8 簇 H：注释与 `using`

- F-91：
  - `Protocol/Game/Altar/AltarProjection.cs` 的 `using Dfo.Server.Application.OnlineUsers;` 删除；
  - `MonsterDeath/ApplyObjectDestructionCommand.cs:46`～`:47`、`RecoverStamina/RecoverStaminaSlice.cs:104` 分别描述账号租约与事务准入门的注释，改写为不提门与租约的说法，复查本身保留。
- `UnitOfWorkBehavior.cs:11` 一带「strictly inside the account gate」的注释，改为写今天的次序约束（45 ＜ 58 ＜ 60 ＜ 100）。
- `Abstractions/Execution/GameplayAssertions.cs:5`～`:9` 的类注释：「removed with the gates in T-005」改为「随队伍的两把投递门在 T-006 删除」。`Enter`／`Entering`／`WouldWait`、`DiagnosticSourceName`／`Report` 不动（F-84）。
- 判据 J1 扫原文本（含注释），所以其余提到被删名称的注释都会被它点名，按 TASK-21 裁定第 5 条改写或删除，清单写进 `task-22-sites.txt` 附录。

### 4. 必须原样保留的非锁职责

#### 4.1 45 号行为（附录 H 第 45 行逐项）

| # | 附录 H 的职责 | 基准位置 | 今天守着它的用例（基准阅读；以 S4～S8 的结果为准） |
|---|---|---|---|
| H1 | `BeginMain`（一请求一主结算） | `:13` | `EffectSettlementRegistrationTests`、`EffectOperationReplayTests` |
| H2 | `DemandRecipient`（两次） | `:14`、`:29` | 同上 |
| H3 | `Capture` 接收者 | `:16` | `CommonExperienceEffectTests.A_stale_or_different_recipient_cannot_write` |
| H4 | 账号与会话一致性 | `:17`～`:18` | **未找到**直接断言其消息的用例 |
| H5 | 参与者集合必须包含报告会话 | `:19`～`:21` | **未找到** |
| H6 | 逐个 `DemandCurrent`（参与者与接收者） | `:27`～`:28` | 待 S7 确认 |
| H7 | 结果不确定时逐个 `BlockAsync` | `:33`～`:38` | `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`（AC-009 点名的用例）；待 S5 确认 |
| H8 | `SelectedSessionCommandScope.Enter` | `:30` | 待 S8 确认（58 号行为读 `scope.Current`） |

H4、H5 没有直接守着的用例。本单元改的正是这个文件，所以缺口在本单元补上（§6.5）：变异下没有用例变红的项，各补一条用例，先在变异下看它红，还原后看它绿。

#### 4.2 其余

- **58 号行为**：代码不动。它调用的 `SelectedSessionSafety.BlockAsync` 只少了借用（§3.2）。
- **`SelectedSessionSafety.BlockAsync`**：会话封锁、连接封锁、`DiscardExact`、日志、精确关连接，次序不变。
- **`EffectOperationLedger`**：§3.7。
- **`DomainEventDispatcher`**：事务内不得排空、执行器上不得排空、每条 delivery 一条事件 CMD 并等待、`TryBegin → Invoke → Complete` 同事务、失败另开事务记录重试、LiveOnly 先提交消费决定再投影。
- **`ScheduledTaskHostedService`**：MissedRun 策略、重试次数与退避、每次尝试一个 scope、停止时取消并等待在途任务。
- **参与者集合**：`IMultiSelectedSessionCommand.Sessions` 全部保留（附录 F「保留」栏），本单元不改任何 `Sessions` 成员。

### 5. 判据（`LockRemovalSourceTests`）

**名称规则**（`Names`）：

| 范围 | 名称 | 例外 | 加入 |
|---|---|---|---|
| 全部 `Server/src` | TASK-21 的 11 个全局名称（不变） | — | TASK-21 |
| 全部 `Server/src`（由 Protocol 范围放宽） | `SelectedSessionTransitionGate`、`OnlineUserExecutionGate`、`UserExecutionGate`、`BorrowOrAcquireSharedAsync`、`BorrowOrAcquireExclusiveAsync`、`AcquireManyExclusiveAsync`、`TransactionalExecutionGate` | — | TASK-22 |
| 全部 `Server/src`（去掉例外） | `UserAccessMode` | — | TASK-22 |
| 全部 `Server/src`（新增） | `AccountExecutionGate`、`OnlineUserExecutionBehavior`、`AccountExecutionBehavior`、`TransactionalExecutionBehavior`、`OnlineUserAccessMode`、`AccountAccessMode`、`IMultiAccountCommand`、`IOnlineUserCommand`、`ScheduledTaskSchedulerOptions`、`groupGates` | — | TASK-22 |

`Protocol` 常量与 `NameRule.Exempt` 字段不再有用途，删除。

**信号量名单**（`Semaphores`）：删去 `DomainEventDispatcher.cs`、`ScheduledTaskHostedService.cs`、`TransactionalExecutionBehavior.cs`、`JoustMaintenanceService.cs`、`EffectOperationLedger.cs` 五项，剩 7 项：
- T-006 删除：`PartyNotificationSlice.cs`、`GameplayAssertions.cs`；
- 保留：`JoustRuleService.cs`、`CharacterStatRuleService.cs`、`PasswordComputationLimiter.cs`（F-85）、`PvfItemCatalogSource.cs`、`PvfBossTowerContentSource.cs`。

**第三支：其他等待原语为零**（新增，处理 TASK-21 评审「非 `SemaphoreSlim` 原语属名称判据设计边界」）：
- 名称：`ReaderWriterLockSlim`、`ReaderWriterLock`、`Semaphore`、`Mutex`、`SpinLock`、`Barrier`、`CountdownEvent`、`ManualResetEventSlim`、`ManualResetEvent`、`AutoResetEvent`、`Monitor.Enter`、`Monitor.TryEnter`、`Monitor.Wait`。
- 基准扫描为 0。
- 不设名单：出现即违规，按停止条件 7 处理。
- 短 `lock (…)` 不在其中（附录 F「保留」）。

类注释改为：附录 F「删除」栏中只剩 T-006 的三项（队伍两把投递门、`NotificationRetryQueue`、`NotificationRetryDrainService`）不在名单上，T-006 加入；`TaskCompletionSource` 式的自制等待由名称规则按类型名守着，不按原语扫描。

**用例**：

| 用例 | 处置 | 内容 |
|---|---|---|
| J1 `No_deleted_gate_name_is_left_in_the_production_sources` | 不改代码，名单变了 | 生产源码的名称扫描为 0 |
| J2 `Every_production_semaphore_is_on_the_list_and_the_list_has_no_stale_entry` | 同上 | 7 项名单 |
| J3（新） | 新增 | 生产源码中其他等待原语为 0；输出每个名称的命中 |
| C1、C2、C5、C7 | 不改 | — |
| C3 `A_protocol_scoped_name_is_named_in_protocol_and_not_in_application` | 改写（意图反转：范围已放宽） | 同一名称在 Protocol 与 Application 两个文件中都被点名（断言改为两条违规）。方法名不改，按 F-87 交 T-008 |
| C4 `An_access_mode_is_allowed_in_the_protocol_module_only` | 改写（例外已去掉） | `UserAccessMode` 出现在 `ProtocolModule.cs` 也被点名。方法名不改，同上 |
| C6 `A_listed_file_without_a_semaphore_is_a_stale_entry` | 改写 | 陈旧条目的示例文件从 `JoustMaintenanceService.cs`（已不在名单上）换成 `JoustRuleService.cs` |
| C8（新） | 新增 | 合成字典：管线行为名（`AccountExecutionBehavior`）出现在 `Dfo.Server.Host/…` 被点名，`IMultiAccountCommandShim` 这类更长标识符不点名 |
| C9（新） | 新增 | 合成字典：其他等待原语（`ReaderWriterLockSlim`）出现在任意文件被点名 |

### 6. 测试侧

#### 6.1 先跑后改

沿用 TASK-21 §6.1：生产改动完成后先构建、跑全量，按失败形态分类；只按本节规则处理，不顺手改别的。分类沿用 TASK-21 §6.2（E／K／G／B／V），删除的每一条都写承接者或规格消除理由（TASK-17 主控方裁定第 7 条）。

#### 6.2 删除（D）

| 用例 | 类 | 承接／消除 |
|---|---|---|
| `GameplayGateAssertionTests` 全部（Theory 5 个 × 5 把门 ＋ Fact 8 个 ＝ 33 例，取证 21 条） | K | 规格消除：它测的门、借用帧、账本门、Joust 门与调度槽全部删除（判据 J1、J2 守着不再出现）；「执行器上不等门」由 RQ-001 单消费者与 TASK-17～21 的零违规全量承接。`GameplayGateAssertion` 机制本身仍由 `ProtocolGateAssertionTests` 的 4 条队伍用例覆盖（T-006） |
| 其中 `Taking_a_scheduled_task_slot_on_the_executor_is_a_self_wait` | K | 同上；F-17 的反向断言随槽删除。组门的反向断言 `Draining_from_inside_a_job_fails_at_once_as_a_program_error` 保留 |
| `OnlineUserExecutionGateTests.DifferentOnlineUsersDoNotBlockEachOther` | K | 规格消除（门删除） |
| `SelectedSessionTransitionGateTests` 2 例 | K | 规格消除（门与借用帧删除）；升级拒绝在生产中不可达（`logs/task-21-upgrade.txt`） |
| `MultiAccountExecutionTests` 3 例（取证 1 条） | K | 规格消除（`IMultiAccountCommand` 与 55 号删除）；「同一组账号的事务只执行一次、失败后释放」由 `GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs` 承接 |
| `CommandBusTests.ExclusiveAccountLease_IsReleasedWhenHandlerThrows`、`SharedOnlineUserLease_IsReleasedWhenHandlerThrows`、`ExclusiveOnlineUserLease_IsReleasedWhenHandlerIsCancelled` | K | `task-07-conflicts.md` §15.1、F-67：承接者 `GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs` |
| `CommandBusTests.OnlineUserScopedRegistration_RequiresAnOnlineUserCommand`、`AccountScopedRegistration_RequiresAnAccountCommand` | K | 规格消除：访问方式不存在了，「注册了访问方式却没实现接口」无从发生 |
| `ScheduledTaskTests.Invalid_configuration_fails_before_callbacks` 的 `parallelism` 一行 | K | 规格消除：并发上限删除；其余 4 行不变 |
| `LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline`（TASK-21 N-V3） | G | 机制随门删除（测试自己持 `AcquireManyExclusiveAsync`、断言管线借用）。执行时在 `LeaveDungeonTests` 中找出经真实管线执行离开命令、断言业务结果的后继用例并写名；找不到就按停止条件 6 处理 |

若待裁定第 2 条不接受：`Global_limit_queues_one_instance_and_coalesces_waiting_runs` 改为删除（G，机制随槽删除），D 加 1。

#### 6.3 改写

| 用例 | 旧 → 新 | 保护意图 |
|---|---|---|
| `CommandBusTests.EveryProductionBehaviourResolvesForACommandResultType` | 期望的行为列表删去 50、55、57 三项 | 每个生产行为都能按结果类型解析；列表就是今天的管线 |
| `CommandBusTests.Pipeline_ExecutesBehaviorsInOrderAroundTheTerminalHandler` | 预期不改（不涉及被删类型）；编译迫使时按 E 处理 | — |
| `UnitOfWorkBehaviorTests.UnitOfWork_OpensInsideTheAccountAndTransactionalGatesAndOutsideTheExecutionPolicy` | `:122`～`:131` 三条涉及 55／57 的次序断言 → 45 ＜ 58 ＜ 60 三条；`:133`～`:135`（60 ＜ 100）不变。方法名不改（F-87） | 「事务在账号租约之内」随 55、57 消除；保留「结算行为包住事务、事务包住执行」 |
| `CommandRegistrationTests.GameTicketRedemptionIsTransactionalAndOnlineUserExclusive` | 删 `:122`～`:123` 两条访问方式断言，执行方式断言保留。方法名不改 | 兑换命令是事务命令 |
| `PartyCommandCodecTests`（`:306`～`:307`） | 删两条访问方式断言，其余不变 | 同上 |
| `DomainEventDispatchTests.Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released` | 删测试里持准入门的那一层（`:177`）；提交、`Count == 0`、组循环随后消费、`Count == 3` 不变。方法名不改 | 发布事务的提交不等待消费；消费在发布作业结束之后发生 |
| `DomainEventDispatchTests.Fixture.SendAsync`（`:460`） | 删准入门一行 | 编译迫使 |
| `DomainEventDispatchTests.A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off`（N5） | 按待裁定第 1 条：改为由 `Dispatcher.RunAsync` 的组循环驱动；先推进冻结时钟、再发布，由提交唤醒；`DrainOnceAsync` 返回值的三条断言改为「等到状态达成」；`Count`、待处理数、`attempt_count`、`MaximumOutstanding == 1` 的断言不变 | AC-015「订阅组内顺序与失败退避后的继续处理」；组内串行由一个排空循环保证 |
| `GameplayWriteAssertionTests`（`:338`、`:358`、`:484`） | 删 `new TransactionalExecutionGate()` 与对应构造实参；断言不变 | 编译迫使（E） |
| `ScheduledTaskTests.Global_limit_queues_one_instance_and_coalesces_waiting_runs` | 按待裁定第 2 条：去掉 `Running(…, 1)` 的并发实参；断言不变。方法名不改 | 排队中的 RunOnce 运行按最新时刻合并为一次；同一时刻只有一个回调在执行 |
| `ScheduledTaskTests.Retry_delay_does_not_hold_the_global_execution_slot` | 去掉并发实参；断言不变。方法名不改 | 重试的退避不占执行器，别的任务照常执行（承接者之一 `ScheduledTaskExecutorTests.Each_attempt_is_its_own_timer_job_and_the_retry_delay_leaves_the_executor_free`） |
| `ScheduledTaskTests.Factory_uses_configured_singletons_and_explicit_options_override_defaults` | 删「显式选项覆盖默认值」一半（`:278`、`:281`），工厂与单例一半保留。方法名不改 | 工厂注册的任务用配置的单例 |
| `ScheduledTaskTests.Running` 夹具 | 删 `concurrency` 参数 | 编译迫使 |
| `ReplayStateSnapshotTests`（TASK-21 N-V1、N-V2） | 删 `TrackedEntryCount` 断言（`:48`、`:71`、`:74`、`:176`、`:222`）与 `gate` 变量；`:72`～`:73` 的 `exclusiveOwner` 借用外壳删除，里面的 `RefreshAsync` 与批次数断言保留 | 「门被释放」随门删除；精确连接、批次内容、新旧库存、错误连接被拒的断言不变 |
| `CommonExperienceEffectTests.Effect_uow_borrows_outer_exclusive_and_keeps_teardown_waiting_until_settlement`（N-V4） | 删外层 `AcquireAsync` 与结尾的再取门；经验 30 的两条断言保留 | 结算与拆除不交错由作业边界保证；结算结果不变 |
| `CommonExperienceEffectTests.Logger_failure_does_not_replace_unknown_cause_and_exact_close_callback_waits_for_lease_release`（N-V4） | 删结尾的取门与 `CallbackCloser.Lifecycle`；其余断言保留 | 日志失败不替换原因、精确关连接、连接被封锁 |

#### 6.4 TASK-21 空转名单（V1～V7）

| 行 | 用例 | 处置 |
|---|---|---|
| V1 | `OnlineUserServiceTests.DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate` | 删测试里持门的准备；剩余断言（在线用户被移除）保留。方法名不改 |
| V2 | `SelectCharacterLifecycleTests.CrossConnectionDisplacement_CleansCurrentOwnerBeforeRegisteringReplacement` | 删关连接回调里的取门与 `Fixture.TransitionGate`；清理次序的断言保留 |
| V3、V4 | 两塔 `Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release` | `ReentrantCloser` 换成普通 `Closer`；隔离断言保留 |
| V5、V6 | `PartyCommandHandlerTests.Successful_command_notifications_are_delivered_after_both_transition_gates_release`、`Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases` | `GateReentryNotificationBus` 换成计数总线；发布次数断言保留 |
| V7 | `LeaveDungeonTests.Give_up_completes_through_real_command_locks_and_releases_the_session` | 删结尾的取门（两行 Theory 都删）；业务断言保留 |

用例名里提到门、租约、槽的一律不改名（F-87，随 T-008 按簇决定）；本单元新出现的这类名字追加进 F-87。

#### 6.5 新增（N）

- 判据：J3、C8、C9，共 3 例。
- 45 号行为的缺口（§4.1）：S4～S8 中没有用例变红的项，各补一例。
  - 预计 H4、H5 各 1 例；H6、H8 视变异结果，至多 2 例。
  - 放在新文件 `Application.Tests/Commands/SelectedSessionExecutionBehaviorTests.cs`，经真实总线、在执行器上（`TestGameplay`）执行：
    - 账号与会话不一致时处理器不执行；
    - 参与者集合不含报告会话时处理器不执行；
    - 参与者之一已不是当前会话时处理器不执行；
    - 处理器执行时 `SelectedSessionCommandScope.Current` 是捕获的接收者。
- 事件分发（待裁定第 1 条，建议接受）：在 `DomainEventDispatchTests.Executor.cs` 加 1 例。两个并发的 `DrainOnceAsync("counter")` 加上一个运行中的组循环，断言：
  - 每条 delivery 只被消费一次（`TryBegin` 成功的次数等于事实数）；
  - 一条失败、已退避的 delivery 不被另一个排空者在到期前重试。
  - 它钉住 Host 用例在删掉组门之后所依赖的那条性质。
- 预计 N ＝ 3 ＋ (2～4) ＋ 1 ＝ 6～8，执行时按实际写。

#### 6.6 编译迫使（E）与数量预期

- **编译迫使**（基准检索，以编译结果为准）：
  - 访问方式实参：`GameLoginTests`、`MarriageCeremonyTests`、`MarriageCeremonyHandlerTests`、`MarriageChurchTests`、`MarriageProposalTests`、`SelectCharacterLifecycleTests`、`SelectCharacterReturnTests`、`EffectOperationReplayTests`、`OnlineUserServiceTests`、`PartyCommandHandlerTests`、两塔并发用例、`LeaveDungeonTests`、`Persistence.Tests/Accounts/LoginSessionQueuedCommandTests`、`Host.Tests/GmEventsHttpTests`；
  - 门的构造与注册：`OnlineIdentityTests:302`、`JoustQueryHandlerTests:134`、`PartySelectDungeonHandlerTests:237`、`LeaveDungeonTests:493`、`DungeonVisitDomainServiceTests:408`、`MoveItemSpaceHandlerTests:365`／`:387`。
  - 每个文件的断言多重集合不变，由 `diffcheck7.py` 证明。
- **D 预期**：33 ＋ 1 ＋ 2 ＋ 3 ＋ 5 ＋ 1 ＋ 1 ＝ 46（Application 45：GameplayGateAssertion 33、门 1 ＋ 2、多账号 3、`CommandBusTests` 5、调度 1；Protocol 1：`LeaveDungeonTests`）。待裁定第 2 条不接受时为 47。
- **逐项目预期**：
  - Application 4582 － 45 ＋ N_app；
  - Protocol 1699 － 1；
  - 其余六个项目不变；
  - 执行时按实际例数写加法。
- **保留不改**：
  - `TransactionalCommandSerializationTests` 两例（TASK-18 变异 X1 已证明它们不依赖门）；
  - F-68 的跨容器 SQLite 竞争用例（`MailboxCoreIntegrationTests.ConcurrentClaim…`、`QuestHelperPuppetTests` `:181` 与 `:273`、`SaoTowerStoreTests.Two_database_sessions…`）：它们测 SQLite 写保留与提交冲突，不经门，`AcquireWriteAsync` 属附录 F「保留」；
  - `ProtocolGateAssertionTests` 4 条、`PartyTownProjectionDeliveryTests` 2 条（T-006）。

### 7. 取证预期（先写进 `logs/task-22-violations.txt` 再跑）

本单元删除的反例类是 `GameplayGateAssertionTests`（21 条：门外 8、等门 13）与 `MultiAccountExecutionTests`（1 条，等门）。新增用例不产生取证记录。

- **按类**：`ProtocolGateAssertionTests` 4、`PartyTownProjectionDeliveryTests` 2、`GameplayWriteAssertionTests` 24、`CommandBusExecutorTests` 1、`EffectOperationReplayTests` 1、正向对照 7；4 ＋ 2 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 39。
- **按 kind**：`gate-off-executor` 17 － 8 ＝ 9，`gate-wait` 18 － 13 － 1 ＝ 4，`write-off-executor` 24，`bus-leaked-flow` 2；9 ＋ 4 ＋ 24 ＋ 2 ＝ 39。
- **对照**：TASK-21 的 61 － 21 － 1 ＝ 39。

### 8. 变异（Required check 10）

| 变异 | 应当变红 |
|---|---|
| S1 在 `Dfo.Server.GmWeb` 任一文件加 `internal sealed class AccountExecutionGate;` | J1，点名该文件（证明范围已是全局） |
| S2 在 `DomainEventDispatcher.cs` 加 `private readonly SemaphoreSlim probe = new(1, 1);` | J2，点名该文件（证明它已从名单删去） |
| S3 在 Application 任一文件加 `private readonly ReaderWriterLockSlim probe = new();` | J3 |
| S4 45 号：删去「参与者集合必须包含报告会话」（`:20`～`:21`） | H5 的用例 |
| S5 45 号：`catch` 中不再逐个 `BlockAsync` | `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`；不红就补用例 |
| S6 45 号：删去账号与会话一致性检查（`:17`～`:18`） | H4 的用例 |
| S7 45 号：删去 `foreach (var target in targets) safety.DemandCurrent(target);` | 已有用例或 H6 的用例 |
| S8 45 号：删去 `scope.Enter(recipient)` | 已有用例或 H8 的用例 |
| S9 `SelectedSessionSafety.BlockAsync`：删去 `DiscardExact` | 抽查：如实记录，没有用例变红就登记 F 项交 T-008（与 F-88 同类） |
| S10 调度器：`Start` 中同步等待 `RunAsync` 完成（`.GetAwaiter().GetResult()`） | `ScheduledTaskTests.Retry_delay_does_not_hold_the_global_execution_slot`（证明计时循环不能被回调阻塞；靠 `--blame-hang-timeout` 兜底） |
| S11 调度器：把 `Started`／`Requested` 的读取挪回入队之前 | `ScheduledTaskTests.Global_limit_queues_one_instance_and_coalesces_waiting_runs`（证明它能区分两种写法） |
| S12 事件分发：`RunAsync` 为同一组起两个 `RunGroupAsync` | 改写后的 N5（`MaximumOutstanding` 变为 2） |
| S13 效果账本：删去 `:40` 的 `safety.DemandCurrent(recipient)` | 抽查：如实记录，没有用例变红就并入 F-88 |

S4～S8 在簇 B 改完后立即做（§0 第 3.2 步），用来决定 §6.5 补哪几条；正式全量之后再对最终代码各做一次，结果都记进日志。

### 9. T-005 收口（`verification/t005-closure.md`）

写法仿 `verification/t004-closure.md`：只写「交付 → 哪个 TASK → 证据位置 → 状态」，未关闭事项只引用 F 编号。开头写日期、基准提交（`09e4983c` 加 TASK-22 未提交的改动）与状态词。

1. **Plan T-005 行逐项**：
   - 进入条件：T-004 零违规（`t004-closure.md` §8）与 M-1 通过（`measurement/m1-summary.md` §1）；
   - 门、信号量、管线 45／50／55／57 的取锁部分、多账号声明、`LeaseSet`、重试循环、组门、并发槽：逐项写删于 TASK-21 还是 TASK-22，证据是判据规则名、检索命令或日志行；
   - 参与者集合与 45／58 号行为的非锁职责原样保留：引 `logs/task-22-duties.txt`；
   - 改写或删除测门与测交错的用例：引 `t005-test-dispositions.md` 两节的计数；
   - 落实 VC-7：引 §5。
2. **附录 F「删除」栏逐项**：每一项写「删于哪个单元 → 判据规则或检索证据」。
   - 投递三项（队伍按角色的信号量与 `PartyDeliveryStream.Gate`、`NotificationRetryQueue`、`NotificationRetryDrainService`）写「归 T-006，今天仍在」，并写明它们为什么不在判据名单上。
   - `IMultiSelectedSessionCommand` 的取锁用途：写明 45 号行为不再按它取租约，`Sessions` 留作参与者集合。
3. **「保留」栏逐项确认仍在**：
   - 参与者集合；
   - 短 `lock`：按附录 F 的口径重数应用层、协议层、持久化层的处数，与「190／11／3」对照，差异只记录不追究，因为附录 F 的数是基准上数的；
   - PVF 与规则初始化的锁和信号量；
   - `GameInboundFrameCounter`、`TouchSocketConnectionRegistry`、GmWeb 两个类；
   - `AcquireWriteAsync`；
   - 另记 `PasswordComputationLimiter`（F-85，T-009 补进附录 F）。
4. **附录 H 第 45、58 行对账**：
   - 第 45 行逐项对到 §4.1 的 H1～H8 与守着它的用例；代码里的两次 `DemandRecipient` 对应附录 H 的一项，写明这一点。
   - 第 58 行逐项写「代码未改」。
   - 这是 AC-009 中附录 H 对账的一半，最终对账归 T-008。
5. **VC-7 汇总**：
   - 七个循环的分类（`logs/task-21-loops.txt`）；
   - 四个请求的证据用例与变异 S6、S7（`t005-test-dispositions.md` TASK-21 §6）；
   - 注明：两塔协调器在 `ExactSessions` 为假时的确定性拒绝不属于 VC-7。那条路径的结果与基准逐字相同（N10、N15，变异 S5），附录 G VC-7 已按 TASK-21 处置 L1 写明。
6. **AC-017 的状态**：
   - 「删除栏在 `Server/src` 中检索为零」：投递以外的部分已满足（判据 J1～J3）；投递三项由 T-006 完成，今天**不得**记为已满足；
   - 「运行期判据在执行器外调用时抛出」：RQ-061 写断言自 TASK-18 开启，反例 `GameplayWriteAssertionTests`。
7. **移交清单**：只列 F 编号与归属（T-006、T-008、T-009、主控方）。
8. **结论与 T-006 进入条件**：
   - T-006 依赖 T-003（TASK-05，`0223e646`）与 T-005（Plan Work 表）。
   - 写明 T-005 是否完成；完成时 T-006 进入条件成立，并列出 T-006 开始时要接手的 F 项（F-20、F-83、F-84 等，以 `follow-ups.md` 为准）。
   - 未完成或有未满足项时，逐项写明缺什么。

### 10. `follow-ups.md`

- **关闭**：
  - F-17 的删除一半（组门与并发槽已删；反向断言：组门的保留，槽的随槽删除）；
  - F-19 的其余各项（调度 `Task.Run` 与并发槽、组门与准入门、Joust `ticks`）；
  - F-67（3 条 `CommandBusTests` 删除，承接者如 §6.2）；
  - F-68（保留，理由如 §6.6）；
  - F-87 的空转一半（V1～V7 已处置；改名一半仍归 T-008）；
  - F-91。
- **更新**：F-69 写本单元的耗时对比结果；F-11 只读引用，不改。
- **新增**（从 F-92 起编号，以执行时的实际编号为准）：
  - AGENTS.md「命令执行总线」一节中的管线 50／55／57、`UserAccessMode`、`IOnlineUserCommand`、`IMultiAccountCommand`、`UserExecutionGate`、`SelectedSessionTransitionGate` 与「Order 必须严格落在 55 与 100 之间」的描述 → T-009（RQ-100），与 F-89 合并或并列；
  - 变异 S9、S13 若没有用例变红 → T-008（与 F-88 同类）；
  - 本单元新出现的、名字里提到门或槽的用例 → 并入 F-87；
  - 待裁定第 1 条若改为加守卫，按裁定内容另记。

### 11. 生产行为为什么不变

- **门与管线**：执行器上每一次取门都立即成功（TASK-17 起的门断言：正式全量中反例类外的等门与门外取门都是 0）。删掉一次立即成功的取门，可能的差异只有三种：
  - 取消检查：R6，记进 sites 附录；
  - 借用帧的升级拒绝：生产中不可达，`logs/task-21-upgrade.txt`；
  - 取门前后的比较：本单元范围内没有，§3。
- **访问方式与声明**：只有 50、55 号行为读它们（§1.1），行为删除之后它们不影响任何执行路径。
- **事件分发**：生产中每组一个排空者（§1.2），组门从未被争用；准入门在执行器上从不等待。
- **调度器**：回调本来就在执行器上逐条执行；`Started` 读取挪进作业体之后，排队中的运行按最新时刻合并的语义与今天相同（§3.6）。
- **账本与 Joust**：同一连接同时至多一条入站 CMD（RQ-005），定时源合并（RQ-006），信号量从未被争用。
- **证据**：表征测试与金样本不变；正式全量与 TASK-21 相比只有 §6 记录的增删与改写；日志信号逐条对照；取证 39 条，反例类与对照以外 0。

## 待裁定

1. **组门删除后的同组并发排空**（裁定第 7 条的核实结果，§1.2）。
   - 核实结果：
     - 生产中每组只有一个排空者，成立；
     - 裁定写的依据「组排空在执行器上串行执行」与代码不符：排空在执行器外，在执行器上串行的是每条 delivery 的消费；
     - 测试宿主中同组并发排空确实存在：Host.Tests 两个文件在后台组循环运行时从测试线程排空，N5 自己并发排空三次。
   - 删掉组门之后：
     - 消费次数与退避仍由「每条消费一条执行器作业 ＋ `TryBeginAsync` 的条件更新」保证；
     - 失去的只有「同组同时至多一条已入队的事件 CMD」，这条只在测试中可能被违反。
   - **建议**：
     - 仍不加守卫；
     - N5 改为由 `RunAsync` 组循环驱动，业务断言与 `MaximumOutstanding == 1` 不变，返回值断言改为「等到状态达成」；
     - 另加 1 例钉住「并发排空时每条 delivery 只消费一次、退避中的不被提前重试」（§6.5），它就是 Host 用例删门之后依赖的性质；
     - 分发器类注释按 §3.5 写明这两条。
   - 备选：
     - 加一个「同组并发排空即抛」的快速失败守卫（不等待，只报程序错误）。代价：Host.Tests 两个文件要改成先停组循环再排空，N5 改为断言第二个并发排空被拒。
     - 或保留组门到 T-007／T-008。不建议：违背 RQ-060，判据也扩不到全局。
   - 这条涉及 AC-015「订阅组内顺序」的证据形态，请确认。
2. **调度器 `Started`／`Requested` 的读取挪进作业体**（§3.6）。
   - 不挪：删掉并发槽之后，已入队、未轮到的 RunOnce 任务再到期时，会先按旧时刻跑一次再补跑一次，而今天合并为一次、按最新时刻执行。RQ-006 仍满足，但每日刷新一类任务的调用次数与 `ScheduledAt` 会变；`Global_limit_queues_one_instance_and_coalesces_waiting_runs` 要按 G 删除。
   - 挪：保留今天的合并语义，该用例只去掉并发实参、断言不变；变异 S11 证明它能区分两种写法。
   - **建议挪**。这是调度语义的选择，不改 Spec 验收，但改的是附录 A 中所有调度任务共用的行为，请确认。
3. **`AdvanceMarriageWeddingCommand.AccountIds` 与 `IMarriageQueryDomainService.WeddingAccountsAsync` 一并删除**（§3.4）。
   - 这个参数只供 55 号行为取锁；`WeddingAccountsAsync` 唯一的调用方就是为它取值的那一行（`MarriageWeddingSlice.cs:212`），测试中没有读者。删除它会改 `Dfo.Server.Abstractions` 中一个领域服务接口。
   - **建议删除**（RQ-060「不留转发」：一个只为取锁存在的查询就是转发）。
   - 备选：只删命令参数和调用，接口方法留到 T-008 归并婚礼簇时处理（留下一个没有调用方的领域查询）。

## 主控方裁定（执行前）

0. **不拆分，接受。** TASK-21 裁定第 6、8 条的核实结论接受。第 7 条的依据由主控方更正：原裁定写「组排空在执行器上串行」，与代码不符。实际是排空本身在执行器外，每条消息的消费才是执行器作业。结论改以下面第 1 条为准。
1. **组门删除后不加守卫，接受计划建议。** 依据：每条消息的消费是一个执行器作业；`TryBeginAsync` 的条件更新（`state=0 AND next_attempt_at<=now`）保证一条消息只被领取一次；退避语义不依赖组门。具体要求：
   - N5 改为由 `RunAsync` 组循环驱动，业务断言与 `MaximumOutstanding == 1` 不变；
   - 新增 1 例，钉住两件事：并发排空时每条只消费一次；处在退避中的消息不会被提前重试。这条用例就是 AC-015 在组门删除后的证据，在收口对账中写明。
2. **调度器的 `Started`／`Requested` 读取挪进作业体，接受。** 这样能保留今天「已入队未执行的同一任务再次到期时合并成一次、按最新时刻跑」的语义。`Global_limit_queues_one_instance_and_coalesces_waiting_runs` 的断言不变，变异 S11 必须能区分两种写法。
3. **删除 `AdvanceMarriageWeddingCommand.AccountIds` 与 `IMarriageQueryDomainService.WeddingAccountsAsync`，接受。** 这个查询唯一的调用方就是为 55 号行为取锁取值的那一行，删掉行为之后它就是死代码。改的是 Abstractions 接口，在 Result 中登记。

## Result（第 1 轮，合同 Revision 1，2026-10-23，基准 `09e4983c`，未提交）

### Outcome

done（待独立结果审查，Result review：required）。

- Application 层的门、管线取锁（50、55、57 整个删除，45 只删取锁部分）、访问方式、`IOnlineUserCommand`／`IMultiAccountCommand` 声明、组门、事务准入门、调度并发槽与 `Task.Run`、账本与 Joust 的信号量全部删除；判据 `LockRemovalSourceTests` 扩到全部 `Server/src`，三支先 RED（J1、J2）后 GREEN。
- 正式全量 8192 条，失败只有那 2 条 `DimensionDungeonTests`；取证 39 条，反例类与对照以外 0。
- T-005 收口写在 `verification/t005-closure.md`：交付全部有证据；AC-017 只记部分满足（投递三项归 T-006）；TASK-22 提交后 T-006 的进入条件成立。
- 停止条件一条都没有触发（Why 1）。

### Changed

- **生产代码**：99 个文件，全在 Owned boundary 的三个项目里：Abstractions 3 ＋ Application 31 ＋ Protocol 65 ＝ 99；其中删除 5 个、修改 94 个；285 行增、1021 行删（`logs/task-22-boundary.txt`）。
  - 簇 A：`OnlineUserExecutionBehavior.cs`、`AccountExecutionBehavior.cs`、`TransactionalExecutionBehavior.cs`（含 `TransactionalExecutionGate`）、`OnlineUsers/SelectedSessionTransitionGate.cs`、`OnlineUsers/OnlineUserExecutionGate.cs` 整个删除，`OnlineUsers/` 目录随之消失；注册、`UserAccessMode`、`CommandExecutionContext` 与 `CommandHandlerRegistration` 的两个字段、`AddCommandHandler` 的两个参数删除。
  - 簇 B：45 号行为只删租约列表、借用循环、`finally` 释放与 `lifecycle` 参数；`SelectedSessionSafety.BlockAsync` 只删借用与参数。非锁语句逐字保留、次序不变（`logs/task-22-duties.txt`）。
  - 簇 C：4 个领域使用者与分发器 3 处的事务准入门删除。
  - 簇 D：125 处访问方式实参（118 ＋ 2 ＋ 5）、56 处 `IOnlineUserCommand`、18 处 `IMultiAccountCommand`；`AccountIds` 按 §3.4 删 14 处、留 4 处；`OnlineUserId` 计算属性删 3 处（编译确认没有别的读者）；裁定第 3 条的 `AdvanceMarriageWeddingCommand.AccountIds`、`IMarriageQueryDomainService.WeddingAccountsAsync` 及其实现删除（**Abstractions 接口改动**）。
  - 簇 E：组门删除，类注释按裁定第 1 条改写。
  - 簇 F：并发槽、`Task.Run`、`ScheduledTaskSchedulerOptions`（Abstractions 公开记录）与 `AddTaskScheduling(options)` 删除；`Started`／`Requested` 的读取挪进作业体（裁定第 2 条）。
  - 簇 G：账本与 Joust 的信号量和 `GameplayGateAssertion.Enter` 删除。
  - 簇 H：F-91 的 `using` 与两处注释；`UnitOfWorkBehavior`、`GameplayAssertions` 的注释；其余注释清单在 `logs/task-22-sites.txt` 附录 B。
- **测试**（`verification/t005-test-dispositions.md` TASK-22 一节）：删除 4 个文件，修改 43 个，新建 2 个（`Commands/SelectedSessionExecutionBehaviorTests.cs`；`Execution/ViolationCapture.cs`，从被删的 `GameplayGateAssertionTests.cs` 原样移出的测试支持类）。D 45、N 9、改写 20 行（§3，含 V1～V7 与 N-V1～N-V4）、编译迫使见 §4。
- **文档与证据**：`verification/t005-test-dispositions.md`（TASK-22 一节）、新建 `verification/t005-closure.md`、`verification/follow-ups.md`（关闭 F-17、F-19、F-67、F-68、F-91，F-87 关空转一半，更新 F-69，新增 F-92～F-95）、日志 `verification/logs/task-22-{build-test,violations,lock-scan,sites,duties,verify,diff-check,new-x10,mutations,boundary,isolated}.txt`，一次性脚本全文附在各自日志末尾。

### Why

1. **停止条件**：
   - 1：删除的每条语句都是取锁、释放、租约列表、门的字段／参数／注册，或只有 50、55 号读的声明；唯一挪动的语句是调度器的 `Started`／`Requested` 读取，由裁定第 2 条决定（`logs/task-22-sites.txt` 附录 D）。
   - 2：基准上的核实与 §1 一致（`logs/task-22-verify.txt`）。
   - 3：生产改动只在三个项目；`spec.md`、`plan.md` 未改。
   - 4、5：正式全量没有新失败，取证没有名单外记录；表征测试与金样本未改。
   - 6：每条删除都有承接者或规格消除理由；N-V3 见第 3 条。
   - 7：J3 其他等待原语 0，J2 名单就是计划的 7 项。
   - 8：`Start` 的同步部分只有「`lock (job)` 读取、入队」；入队是无界通道的 `TryWrite`（`AllowSynchronousContinuations = false`），完成源 `RunContinuationsAsynchronously`，回调不会在计时循环上内联执行；变异 S10 证明计时循环被阻塞时用例会红。
2. **45 号行为补了 5 条用例，而不是计划预计的 2～4 条**：变异 S4～S8 第 1 轮在 Application 与 Protocol 全部用例下没有一条变红，包括计划点名给 H7 的 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`（58 号会封锁报告者，所以它不区分 45 号的逐个封锁）。H4～H8 各补一条，经真实总线、在执行器上运行；补上后每个变异只使对应的一条变红。
3. **N-V3 改写而不是删除**（与计划 §6.2 不同）：`LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline` 去掉借用两行后，剩下的「离开命令经真实管线结束副本」在测试里没有别的用例覆盖（检索 `LeaveDungeonCommand` 的测试使用只有它经管线）。借用帧的意图由规格消除，所以停止条件 6 不触发；保留用例比删除少丢覆盖。因此 Protocol 的 D 是 0 而不是 1。
4. **裁定第 3 条之外多删了 `WeddingAccountsAsync` 的实现**（`MarriageDomainService.Query.cs`），接口方法删掉后它是死代码。它调用的 `GuestList.AccountsAsync` 保留：它是 `GuestList` 构造参数 `ICharacterTable owners` 唯一的读者，删除会让该参数未读（CS9113，警告即错误）并牵动装配，登记 F-94 交 T-008。删掉这次查询的同时，婚礼节拍不再在发命令之前读一次结算与宾客（只读，原先在 `try` 之外）。
5. **调度器的作业体读取**：除了把 `lock (job) { job.Started = true; scheduledAt = job.Requested; }` 挪进作业体，入队前还留了一次只读的 `lock (job) scheduledAt = job.Requested;`，只为日志在回调从未运行时（入队失败）仍有原来的时刻；它不设 `Started`。合并语义保留，并扩大到执行器排队期间：默认配置下「已入队未开始的 RunOnce 任务再次到期时合并成一次」是已接受的差异，登记在 `t005-closure.md` §8（评审 L2；S11 区分两种写法）。
6. **已知的保护变化**（计划 §3.7，登记 F-95，信息）：账本与 Joust 节拍不再自带「在执行器上」检查；它们写的玩法状态仍由 RQ-061 写断言守着。取消检查的变化见 `logs/task-22-sites.txt` 附录 A，不引入基准不可达的取消结果。
7. **TDD 的口径**：只有判据 J1、J2 走了 RED → GREEN；J3 在基准上即为 0；45 号的 5 条与并发排空 1 条钉的是保留下来的行为，在本单元代码上写出即通过，区分力由变异证明，不是 RED。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误（正式全量前一次，`logs/task-22-build-test.txt` 第 1 部分；变异之后一次，`logs/task-22-mutations.txt` 末尾）。
2. **正式全量**（`logs/task-22-build-test.txt`，2026-10-22T23:17:22Z～23:20:07Z，测试部分 165 秒，开始时 swap 已用 8852 MB；只作信息）：
   - **总数**：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1699 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 175 ＋ Application 4546 ＋ Host 191 ＝ 8192 ＝ 8228 ＋ 9 － 45。
   - **N ＝ 9**，全在 Application：`LockRemovalSourceTests` 3（`No_other_waiting_primitive_is_used_in_the_production_sources`、`A_pipeline_behaviour_name_is_named_outside_application_and_a_longer_identifier_is_not`、`Another_waiting_primitive_is_named_in_any_file`）＋ `DomainEventDispatchTests.Concurrent_drains_of_one_group_consume_each_delivery_once_and_never_retry_a_backed_off_one_early` 1 ＋ `SelectedSessionExecutionBehaviorTests` 5（`A_command_whose_account_differs_from_its_session_never_reaches_its_handler`、`A_participant_set_without_the_reporting_session_never_reaches_its_handler`、`A_participant_that_is_no_longer_current_stops_the_command_before_its_handler`、`An_uncertain_outcome_fences_every_participant_not_only_the_reporter`、`The_handler_runs_with_the_captured_recipient_as_its_selected_session_scope`）；3 ＋ 1 ＋ 5 ＝ 9。
   - **D ＝ 45**，全在 Application：`GameplayGateAssertionTests` 33（5 个 Theory × 5 行 ＋ 8 个 Fact）＋ `OnlineUserExecutionGateTests` 1 ＋ `SelectedSessionTransitionGateTests` 2 ＋ `MultiAccountExecutionTests` 3 ＋ `CommandBusTests` 5 ＋ `ScheduledTaskTests.Invalid_configuration_fails_before_callbacks` 的 `parallelism` 一行 1；33 ＋ 1 ＋ 2 ＋ 3 ＋ 5 ＋ 1 ＝ 45；逐条名字在 `t005-test-dispositions.md` TASK-22 §2。
   - **逐项目**：Application 4582 ＋ 9 － 45 ＝ 4546；其余七个项目不变。
   - **结果**：通过 54 ＋ 80 ＋ 1699 ＋ 477 ＋ 968 ＋ 173 ＋ 4528 ＋ 191 ＝ 8170；失败 2，就是那 2 条 `DimensionDungeonTests`；跳过 2 ＋ 2 ＋ 16 ＝ 20；8170 ＋ 2 ＋ 20 ＝ 8192；挂起转储 0。
   - **表征测试**：8 通过、2 跳过；`Characterization/` 与金样本未改（`git status` 无相关条目）。
   - **日志信号**（`t19/analyze.py`）：为 0 的七项仍为 0；`GameplayGateWait`、`GameplayLeakedFlow` 各 1，只在 `HostOutputViolationTests` 的显示名里；`Gameplay bus:… failed` 8 条（`Act` 2 ＋ `CreateCharacterCommand` 1 ＋ `RegisterAccountCommand` 1 ＋ `SendGmMailCommand` 4）、`inbound:… failed` 3 条（`GameSelectCharacter` 2 ＋ `GameSellItem` 1）、`gm:… failed` 2 条（`auth/login` 1 ＋ `auth/logout` 1），三类与 TASK-21 逐条相同。只计数的「the drain tears it down」本次 4（TASK-21 0，TASK-17～19 各 1），时序相关的 info 日志。
   - **F-69**：与 TASK-21 相比多出 ≥ 9 秒的 7 条（＋9～＋12 秒），隔离下各 1～3 秒（两次），属于满载拉长，不是排空。其中 3 条是在后台组循环运行时排空的 Host GM 用例，隔离下 1～3 秒。
3. **零违规取证**（`logs/task-22-violations.txt`，预期在运行前已写进日志开头）：
   - 记录 39 条：`ProtocolGateAssertionTests` 4 ＋ `PartyTownProjectionDeliveryTests` 2 ＋ `GameplayWriteAssertionTests` 24 ＋ `CommandBusExecutorTests` 1 ＋ `EffectOperationReplayTests` 1 ＋ 正向对照 7 ＝ 39。
   - 按 kind：`gate-off-executor` 9 ＋ `gate-wait` 4 ＋ `write-off-executor` 24 ＋ `bus-leaked-flow` 2 ＝ 39；对照 TASK-21：61 － 21 － 1 ＝ 39。
   - 反例类与对照以外 0，没有测试帧 0，`exe:` 0，退出码 0。
4. **判据输出**（`logs/task-22-lock-scan.txt`）：
   - 三支各扫 1569 个文件（基准 1574，删了 5 个）；29 条名称规则、13 个其他等待原语，结束时命中都是 0；信号量名单 7 项，每项写了理由与去向，每个文件仍在使用 `SemaphoreSlim`（1、2、1、1、1、1、5 处）。
   - RED（只改测试、生产代码在基准上）：J1 有 296 个规则命中，分布在 18 条规则上：159 ＋ 59 ＋ 21 ＋ 10 ＋ 6 ＋ 6 ＋ 6 ＋ 5 ＋ 4 ＋ 3 ＋ 3 ＋ 3 ＋ 3 ＋ 2 ＋ 2 ＋ 2 ＋ 1 ＋ 1 ＝ 296（按名称的拆分见日志）；J2 报 5 个名单外文件；其余 10 条通过。
   - GREEN：12 条全部通过。
5. **删除点清单**（`logs/task-22-sites.txt`）：
   - 基准命中 327 行，其中计划 §2 的前 20 个名称 289 行、93 个文件，与 §0 一致。
   - 处置分类：A1 18 ＋ A2 43 ＋ A3 10 ＋ B1 4 ＋ C1 4 ＋ C2 2 ＋ D1 125 ＋ D2 75 ＋ D3 3 ＋ E1 3 ＋ F1 11 ＋ G1 4 ＋ 保留的例外 25 ＝ 327；没有未分类的行。每行写了保留下来的非锁语句。
   - 结束时同一检索式只剩 25 行，都是 §2 的例外：`SemaphoreSlim` 12 ＋ `GameplayGateAssertion.Enter` 5 ＋ `Task.Run` 8 ＝ 25；前 20 个名称为 0。
   - 附录 A～E：取消（R6）、改写的注释、计划之外的改动、停止条件、短锁计数。
6. **非锁职责对账**（`logs/task-22-duties.txt`）：45 号 18 项、`BlockAsync` 8 项、账本 15 项、分发器 17 项、调度器 12 项，逐项「基准行 → 结束行」文字相同、次序不变（调度器的一项是裁定第 2 条的挪动，一项只少了 `slots` 实参）；58、100、0 号代码未改，60 号只改注释；45 号与 `BlockAsync` 的前后全文附在日志末尾。
7. **裁定核实**（`logs/task-22-verify.txt`）：开始时在基准上、结束时在工作区各跑一次；基准结果与 §1 一致。
8. **测试改动核对**（`logs/task-22-diff-check.txt`）：方法只少了 D 中的 24 个方法（45 例），只多了 N 中的 9 个；断言行多重集合有 11 个文件变化，每处对到 `t005-test-dispositions.md` 的一行；其余文件不变。`diffcheck7.py` 的副本只加了「被删除的文件按空文件比较」。
9. **新增与改写测试 ×10**（`logs/task-22-new-x10.txt`）：每轮 27 条（5 ＋ 12 ＋ 3 ＋ 7），10 × 27 ＝ 270 次通过，0 失败。
10. **变异核对**（`logs/task-22-mutations.txt`）：
    - 每项都先备份，用后逐字节还原并 `cmp`（最终一轮 13 项退出码都是 0）；`git diff -- Server/src` 的 sha1 在每一轮前后相同；最后做了 `--no-incremental` 重建。
    - S1、S2、S3：分别只有 J1、J2、J3 变红（S1 点名 GmWeb 文件，证明范围是全局）。
    - S4～S7：各只有对应的新用例变红（第 1 轮只有已有用例时一条都不红）。
    - S8：新用例与 3 条已有用例变红（第 1 轮变异体因未读参数编译不过，第 2 轮改为 `_ = scope;`）。
    - S9、S13（抽查）：没有用例变红，登记 F-93 交 T-008。
    - S10：`Retry_delay_does_not_hold_the_global_execution_slot` 等 4 条 `ScheduledTaskTests` 变红，没有挂起。
    - S11：只有 `Global_limit_queues_one_instance_and_coalesces_waiting_runs` 变红。
    - S12：改写后的 N5 与另 5 条 `DomainEventDispatchTests` 变红，并发排空那条在内。
    - 计划 §0 第 3.2 步要求 S4～S8 在簇 B 改完后立即做；实际在生产与测试都改完、能编译之后、正式全量之前做了第 1、2 轮（测试项目要先能编译，簇 B 单独改完时测试编译不过）。
11. **生产改动边界**（`logs/task-22-boundary.txt`）：99 个文件都在 Application、Abstractions（只有计划列出的 3 个文件）、Protocol 下，逐文件对到 sites 的基准命中或附录；`Server/src` 下没有未跟踪文件。
12. **隔离复跑**（`logs/task-22-isolated.txt`）：
    - Application 4546 条（失败只有那 2 条已知的，跳过 16），取证 27 ＝ 24 ＋ 1 ＋ 1 ＋ 对照 1；
    - Protocol 1699 条全部通过，取证 7 ＝ 4 ＋ 2 ＋ 1；
    - Host 191 条全部通过，取证只有对照 1；
    - 三者反例类与对照以外都是 0。
13. **收口文档**：`verification/t005-closure.md` 按计划 §9 写完。
14. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。`pgrep -x yes` 为 0（本单元没有加负载）；结束时执行了 `dotnet build-server shutdown`。

### Remaining

- **需要结果审查确认**：Why 2（45 号补 5 条）、Why 3（N-V3 改写而非删除）、Why 4（多删 `WeddingAccountsAsync` 实现、`GuestList.AccountsAsync` 留作 F-94）、Why 5（调度器入队前的只读读取）。
- **交 T-006**：附录 F「删除」栏的投递三项；F-20～F-25、F-63～F-65、F-83、F-84。
- **交 T-008**：F-93（S9、S13 无用例守着）、F-87 的改名一半、附录 H 的最终对账（F-94 已在评审后关闭，见「评审后修改」）。
- **交 T-009**：F-92（AGENTS.md 的管线、访问方式与门的描述，与 F-89 一并改）。

### 评审后修改（独立快审 approved 之后，按「主控方处置（评审后，提交前）」第 1～5 条，仍未提交）

- **L1**（`Server/tests/Dfo.Server.Application.Tests/Execution/LockRemovalSourceTests.cs`）：
  - J3 的名单补 `EventWaitHandle`、`WaitHandle`、`SpinWait`；另加两个调用模式，认出 `System.Threading.Lock` 的显式进入：无参的 `.Enter()`／`.EnterScope()`，以及 `.TryEnter(`。对 `Lock` 用 `lock` 语句仍允许。今天在 `Server/src` 中这些都是 0 处。
  - J2 的名单改为「文件 → 处数与理由」，按处数比较：多一处或少一处都点名。7 项的处数是 5、1、1、1、2、1、1。
  - 新增反例 2 条：`A_listed_file_with_one_more_semaphore_than_pinned_is_named`，以及 `Wait_handles_spin_waits_and_explicit_lock_entry_are_named_and_a_lock_statement_is_not`（6 个点名，1 个对照文件不点名）。
  - C5、C6 的合成名单改用按处数生成的 `Listed()`，断言不变。类注释改写，不再说「不使用任何其他等待原语」。
  - 先 RED 后 GREEN：只改数据和用例时 14 条中 12 过、2 条新反例失败；改判据后 14 条全过。
- **L2**：调度器合并语义的措辞改为「保留，并扩大到执行器排队期间」，改了 Why 5 和 `t005-closure.md` §1。差异登记在 `t005-closure.md` 新增的 §8「已接受的差异」：默认配置下，已入队未开始的 RunOnce 任务再次到期时会合并成一次，实际触发不到。
- **L3**：`t005-closure.md` §7 第一条改为「H4～H8 每项有用例；H1～H3 依据基准阅读；`BlockAsync` 的 `DiscardExact` 无用例（F-93）」。
- **L4**：删除 `Server/src/Dfo.Server.Application/Marriage/GuestList.cs` 的 `AccountsAsync`、构造参数 `ICharacterTable owners`、两个只为它们存在的 `using`，并改了类摘要。`GuestList` 只由 DI 构造（`ApplicationModule.cs:155`），装配不用改。F-94 关闭；Why 4 中「`GuestList.AccountsAsync` 保留」一句以本条为准。生产改动仍是 99 个文件，都在 Owned boundary 内。
- **L5**：登记 F-96（信息）：并发排空时，领取之前的失败可能被记两次。`t005-closure.md` §6 的 T-008 一行删去 F-94。
- **数量变化**：Application.Tests 新增 2 例，4546 → 4548。正式全量（8192）是评审前的那次，本轮按主控方要求只做过滤运行，没有重跑正式全量。
- **重跑的检查**（`logs/task-22-review-fixes.txt`）：
  - Release `--no-incremental` 构建：0 警告、0 错误；
  - Application.Tests 过滤到 `LockRemovalSourceTests | Marriage | Wedding`：131 条，通过 128、跳过 3（需要 PVF 档案）、失败 0；
  - 另跑了 Protocol.Tests 的 `Marriage | Wedding`：74 条全过；
  - `git -c core.whitespace=cr-at-eol diff --check`：无输出；
  - 结束时执行了 `dotnet build-server shutdown`。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-22-result-review.md`），5 条低严重度发现都不阻断。提交前就地处理：

1. **L1**：`LockRemovalSourceTests` 的 `OtherWaits` 补 `EventWaitHandle`、`WaitHandle`、`SpinWait` 与 `System.Threading.Lock` 的显式 `Enter`（今天在 `Server/src` 中都是 0 处）；J2 钉住名单上每个文件的 `SemaphoreSlim` 处数；补对应反例。
2. **L2**：调度器合并语义的措辞改为「保留，并扩大到执行器排队期间」；默认配置下「已入队未开始的 RunOnce 任务再次到期时合并成一次」是已接受的差异，在收口中登记（实际触发不到：执行器排队是毫秒级，调度周期是分钟到天级）。
3. **L3**：`t005-closure.md` §7 第一条改为「H4～H8 每项有用例；H1～H3 依据基准阅读；`BlockAsync` 的 `DiscardExact` 无用例（F-93）」。
4. **L4**：删除 `GuestList.AccountsAsync` 及只为它存在的构造参数 `ICharacterTable owners`（裁定第 3 条同一理由：只为取锁存在的死代码），关闭 F-94。
5. **L5**：并发排空时领取之前的失败可能被记两次（`attempt_count` 多加 1、`next_attempt_at` 重置），只在测试宿主可能遇到，订阅者不会被重复调用；登记为新的 F 项，信息。

处理后重跑：Release `--no-incremental` 构建；Application.Tests 中 `LockRemovalSourceTests` 与婚礼相关测试的过滤运行；`cr-at-eol` 口径 `diff --check`。

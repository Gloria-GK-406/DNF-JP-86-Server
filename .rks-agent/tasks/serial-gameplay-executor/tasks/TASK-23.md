# TASK-23 — T-006a：投递翻转——执行器上的发送、关连接与投影改为登记发件箱，接上发送端

基准提交 `a4422480`（TASK-22／T-005b 已提交，T-005 收口；`verification/t005-closure.md` §7：T-006 的进入条件成立）。范围取自：
- `plan.md` T-006 行（第 97 行），以及共享决定中的「分层」「发件箱的生效时机」「组装点」「Spec 复审残余的澄清」「过渡期规则」第 4 条、「判据与测试的关系」「测试」「发件箱的归属」；
- `spec.md` RQ-008、RQ-012、RQ-030～RQ-038、RQ-061 的发件箱登记一半、附录 D（XO-1～XO-7）、附录 E、附录 G 的 VC-1、VC-2、VC-5、VC-8，AC-007（含严格形式「帧先于断开到达」）、AC-011（机制部分）、AC-012、AC-013、AC-017（投递三项中的两项）。

输入还有：
- T-003 的组件（TASK-05，`0223e646`）：`Application/Execution/Outbox/GameplayOutbox.cs`（`GameplayOutboxes`、`GameplayOutbox`、`GameplayOutboxUnit`、`OutboxComposition<T>`），`Abstractions/Execution/GameplayOutbound.cs`（`OutboundEntry`、`OutboundOrderGroup`、`OutboundDelivery`、`IOutboundSender`、`OutboundOutcome`），`Networking/Outbound/*`（`OutboundSender`、`RegistryOutboundEncoder`、`TouchSocketOutboundTransport`、`OutboundTransportOptions`、`OutboundLimits`），以及它们的单元测试。今天它们都没有接线：DI 里没有注册，`TcpServices` 没有设置发送管道参数；
- `verification/t005-closure.md` §2、§6、§7 的移交项；
- `verification/follow-ups.md` 中归属 T-006 的条目：F-20～F-25、F-63、F-64、F-65、F-83、F-84，以及「T-006 之后」的 F-61；
- `verification/task-07-conflicts.md` §3 交 T-006 的 52 条冲突用例（§13 改归 T-004c 的 1 条除外）；
- 表征测试中为 T-006 跳过的 2 条严格形式（`SelectCharacterCompensationCharacterizationTests.cs:65`、`:115`）；
- `verification/xo-dispositions.md`、`verification/cross-connection-order.md`、`verification/measurement/selection-outbound-bound.md`。

T-006 拆成三个单元，本单元是第一个。拆分理由见 Detailed Plan §0，TASK-24、TASK-25 的边界提纲在文末。

**Revision 2**（2026-10-23）按文末「主控方裁定（执行前）」0～6 修订：
- 第 1 条改用备选 B，登记时在执行器上完整编码，T-003 的 `OutboundEntry` 改为携带编码好的帧（§5.1）；
- 第 5 条：XO-6 的阶段按读码结果标为 Required（§1 V9、§7）；
- 第 6 条：`spec.md` 附录 A 第 4 行在本单元一并改（§6.1）。
- 修订中发现的新问题列在文末「修订中新发现、待裁定」。

## Task Unit Contract

- Revision: 2
- Outcome and references：
  1. **接上发送端**（RQ-032～RQ-037；Plan「分层」）：
     - `NetworkingModule` 注册 `IOutboundSender` → `OutboundSender`，连同 `TouchSocketOutboundTransport` 与 `OutboundSendOptions.Default`；`RegistryOutboundEncoder` 改为实现新的 Abstractions 编码接缝并注册（第 4 项、§5.1）；
     - Game 与 Channel 两个 `TcpService` 都经 `OutboundTransportOptions.ConfigureSendPipe` 设置发送管道（暂停与恢复阈值都是 1）；
     - 新增 Abstractions 接缝「按连接 ID 取当前连接上下文」，由 `TouchSocketConnectionRegistry` 实现（§2）。
  2. **作业结束时刷出发件箱**（RQ-030、RQ-031(a)(c)、RQ-005；Plan「发件箱的归属」）：
     - 每个作业在完成信号发出之前，把发件箱按登记顺序交给发送端，在哪个执行器实例上运行都一样；
     - `GameplayWork` 带上来源连接上下文（入站与 `lifecycle:connected` 填写）；
     - 作业体以异常结束时，按 RQ-031(c) 刷出存活的条目，再关闭来源连接；
     - 传输回调不再因作业体异常而关闭连接，关闭只经发件箱（DR-15，§3）。
  3. **工作单元提交钩子与组装点**（RQ-031(0)(b)；Plan「组装点」）：可写的 `DomainTransaction` 在执行器上开启时，同时开启一个 `GameplayOutboxUnit`。提交成功 → 组装该单元内登记的条目；回滚 → 丢弃；结果不确定 → 丢弃（§4）。
  4. **发送器、关连接与即时投影改为登记发件箱**（RQ-030、RQ-012、RQ-035）：
     - `INetworkMessageSender` 与 `INetworkConnectionCloser` 的实现，换成 Application 里只登记、不碰传输的适配器（§5）；
     - `NotificationBus` 逐收件人组装并登记，不再发送，也不再重试；
     - `NotificationOutcome.Delivered` 的含义改为「已登记，提交发送」（裁定第 4 条：改义不改名，只改文档注释）；
     - 登记时连接已经不在，就静默丢弃，作业不失败（F-83）。
     - **登记时完整编码**（裁定第 1 条，备选 B）：消息在登记点、在执行器上整批编码，编码失败在调用点抛出，CMD 失败，与今天相同。`OutboundEntry` 改为携带编码好的帧，发送端不再编码，`OutboundOutcome.EncodingFailed` 随之删除（§5.1）。
  5. **删除直发路径与重试设施**（附录 F「删除」栏投递三项中的两项；DR-13、DR-14；VC-5、VC-8；过渡期规则 4）：
     - 删除 `TouchSocketNetworkMessageSender`（直发路径）、`ExecutorSendDeadline`；
     - 删除 `NotificationRetryQueue`、`NotificationRetryDrainService`、`INotificationRetryDrain`、`NotificationRetry`、`NotificationDeliveryOptions`；
     - 删除 `AddNotificationHandler` 的 `retryLimit` 参数与 `NotificationHandlerRegistration.RetryLimit`，共 63 个注册调用点；
     - 删除登录会话的 `CloseRequests` 通道与 `lifecycle:close-superseded`：顶替关闭改为顶替发生的那条 CMD 发件箱里的关连接条目（F-65，裁定第 6 条）；`spec.md` 附录 A 第 4 行随之更新（§6.1）；
     - `TouchSocketNetworkConnectionCloser`：§1 核实没有执行器外的生产调用方时删除。
  6. **跨连接有序组 XO-1～XO-7**（RQ-033、附录 D、AC-012）：七个产出点各用一个有序组登记（§7）；组内默认 BestEffort，XO-5 的所有者为 Required；XO-6 按 §1 V9 的读码结果把阶段标为 Required（裁定第 5 条）；XO 的证据测试放在发送端层。
  7. **判据**：
     - RQ-061 发件箱一半（F-64）：执行器外登记，即程序错误，并写取证；
     - RQ-003「等待网络」：执行器上的作业触达传输写入或关闭，即程序错误；
     - 传输写入点的结构判据，UDP 发现列为豁免（F-63）；
     - 本单元删除的名称加入 `LockRemovalSourceTests` J1。
     - 每条判据都配反例（RQ-072）。
  8. **测试侧**：
     - 52 条冲突用例逐条处置，沿用 TASK-17 主控方裁定第 7 条；
     - 开启 b1、b2 两条严格形式表征测试；
     - W7 改回严格形式（F-20）；
     - 新增 F-83 用例与 §10.4 列出的证据用例；
     - 「发送即记录」替身只做清点，不改写（交 TASK-25）。
  9. **一次正式全量**：带取证汇，反例类与对照以外的记录为 0；表征测试除 b1、b2 严格形式开启外不变，金样本不变；变异核对（§13）。
- Owned boundary：
  - **生产代码**：
    - `Server/src/Dfo.Server.Networking/**`：`NetworkingModule.cs`、`Runtime/TcpServices.cs`、`Runtime/NetworkPlugins.cs`、`Runtime/TouchSocketConnectionRegistry.cs`、`Outbound/OutboundTransport.cs`（RQ-003 检查）、`Outbound/OutboundEncoder.cs`（改为实现 Abstractions 编码接缝，删除 internal `IOutboundEncoder`）、`Outbound/OutboundSender.cs`（删除准入时的编码与 `EncodingFailed` 分支）；删除 `Runtime/TouchSocketNetworkMessageSender.cs`、`Runtime/ExecutorSendDeadline.cs`，以及按 §1 结果删除 `Runtime/TouchSocketNetworkConnectionCloser.cs`；
    - `Server/src/Dfo.Server.Abstractions/**`：`Execution/GameplayQueue.cs`（`GameplayWork` 的来源字段）、`Execution/GameplayJob.cs`／作业令牌（作业结束钩子）、`Execution/GameplayOutbound.cs`（有序组的登记接口；`OutboundEntry` 改为携带编码好的帧；删除 `OutboundOutcome.EncodingFailed`；新增编码接缝，§5.1）、`Execution/GameplayAssertions.cs`（只加 RQ-003 的公开断言入口与取证种类 `network-wait`，不动 `GameplayGateAssertion` 的门成员）、`Networking/INetworkServices.cs`（连接上下文接缝）、`Notifications/INotificationBus.cs`（只改 `Delivered` 的文档注释）；
    - `Server/src/Dfo.Server.Application/**`：`Execution/GameplayExecutor.cs`、`Execution/GameplayQueue.cs`（注册）、`Execution/Outbox/*`（新增登记适配器）、`Transactions/DomainTransaction.cs`、`Transactions/DomainTransactionScope.cs`、`Notifications/*`（删两个文件、改三个）、`Dispatching/NetworkMessageDispatcher.cs`、`Dispatching/NetworkConnectionLifecycleDispatcher.cs`、`Authentication/LoginSessionRuntime.cs`、`Authentication/LoginSessionRegistry.cs`，以及模块注册；
    - `Server/src/Dfo.Server.Protocol/**`：`ProtocolModule.cs`（63 个 `retryLimit` 实参）；附录 D 的七个产出点只加有序组（§7 表）；编译迫使的改动。
    - 基准命中清单由 §6 的检索式生成，写进 `logs/task-23-sites.txt`。
  - **Spec**（裁定第 6 条，事实性更新）：`spec.md` 附录 A 第 4 行（「登录会话的连接观察与维护扫描」）的「新的 CMD」一栏，按 §6.1 的文字改写；`spec.md` 其余部分不改。
  - **测试**：
    - §10 列出的处置、改写与新增，涉及 Application、Protocol、Networking、Host 四个测试项目与 `tests/Support`；
    - 删除 `Application.Tests/Notifications/NotificationRetryTests.cs`、`NotificationRetryDrainServiceTests.cs`、`Networking.Tests/Runtime/ExecutorSendDeadlineTests.cs`；
    - 只去掉 `SelectCharacterCompensationCharacterizationTests.cs:65`、`:115` 的 `Skip` 实参。
    - T-003 用例里构造 `OutboundEntry` 与发送端的地方随契约改动（E 类）：`Networking.Tests/Outbound/OutboundTestSupport.cs`、`OutboundSenderTests.cs`、`OutboundRealSocketTests.cs`，`Application.Tests/Execution/Outbox/GameplayOutboxTests.cs`。
    - `Networking.Tests/Integration/OutboundSendFailureTests.cs` 4 条不改（裁定第 1 条）。
    - 逐文件列入新建的 `verification/t006-test-dispositions.md` 的 TASK-23 一节。
  - **文档与证据**：
    - 新建 `verification/t006-test-dispositions.md`；
    - `verification/follow-ups.md`；
    - `verification/logs/task-23-*.txt`；一次性脚本放 scratchpad，全文附在对应日志末尾。
  - **不改**：
    - 队伍的两把投递门、`GameplayGateAssertion` 的门成员、`ProtocolGateAssertionTests` 的 4 条与 `PartyTownProjectionDeliveryTests` 的 2 条反例、各程序集的正向对照 `GameplayAssertionRecorderControlTests`（都归 TASK-24，F-84）；
    - 附录 E 各行今天的失败分支代码（`RollbackEntry`、`IsolatePartialCohortAsync`、`SendToExactAsync` 的失败集合、`CloseQuietlyAsync`、`PublicationFailedAsync`、`PublishWithRetryAsync` 等），只在编译迫使时改（归 TASK-24，§9）；
    - 除 §10 点名的以外，注入「发送即记录」替身的用例（TASK-25）；
    - `Dfo.Server.Persistence`、`Dfo.Server.Infrastructure`、`Dfo.Server.GmWeb`、`Dfo.Server.HttpApi`、`Dfo.Server.Host` 的生产代码；
    - `spec.md`（附录 A 第 4 行除外）、`plan.md`、`AGENTS.md`、`Docs/`；金样本文件；`task-07-conflicts.md`、`entry-reconciliation.md`（只读引用，见「修订中新发现、待裁定」第 3 条）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-23-build-test.txt`。
     - **总数**：以执行开始时在基准上 `--list-tests` 的计数为准。预期 8194，即 TASK-22 正式全量 8192 加评审后 Application 新增的 2 条。本单元为 8194 ＋ N － D，逐项目写加法，N、D 逐条列名（预期见 §10.5）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（按 AGENTS.md「判据偶发失败」三样一起取身份）；挂起转储 0。
     - **跳过**：20 → 18。Networking 的 2 条跳过就是 b1、b2 严格形式，本单元开启它们。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 只有那两处 `Skip` 实参的改动，金样本目录不改（`git status` 为证）。
     - **日志信号**：与 TASK-22 Checks 2 同口径、同脚本（scratchpad `t19/analyze.py`）。
       - 应为 0 的七项仍为 0。
       - `direct send deadline exceeded` 为 0：这条消息随 `ExecutorSendDeadline` 删除。
       - 新增一项计数：`Outbound connection … closed`（发送端失败关闭），逐条归因。编码失败照旧在调用点抛出，出现在原来的 `… failed` 日志里。
       - `Gameplay bus:… failed`、`inbound:… failed`、`gm:… failed` 逐条与 TASK-22 对照，不同的逐条说明。F-83 一类（作业向已关闭的连接回包而失败）应为 0。
     - 耗时与内存只作信息（RQ-091 用户修订：单条指令 ≤ 2 s 即可接受，性能不作停止条件）。F-69：与 TASK-22 的正式全量逐条对比耗时，多出 ≥ 9 秒的逐条隔离复核。
  3. **零违规取证**（`logs/task-23-violations.txt`）：
     - `violations.py` 的命令与 TASK-22 Checks 3 相同，加上本单元新增的反例类与新的 kind（§12）。
     - 预期先写进日志开头再跑：反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  4. **判据输出**（`logs/task-23-lock-scan.txt`）：
     - `LockRemovalSourceTests` 三支各扫描的文件数，以及本单元新增名称规则的命中（应为 0）；
     - 传输写入点判据列出的每个写入点及其理由；
     - 先 RED（只改测试时）后 GREEN 的两次输出。
  5. **删除点清单**（`logs/task-23-sites.txt`）：§6 检索式在基准上的全部命中，逐行写「文件:行 → 处置（§5 或 §6 的条目）」。执行结束时，同一检索式只剩 §6 列出的例外。
  6. **基准核实**（`logs/task-23-verify.txt`）：§1 的检索命令与输出全文，开始时在基准上跑一次，结束时在工作区再跑一次。基准上的结果与 §1 不一致时，按停止条件 2 处理。
  7. **工作单元内登记清单**（`logs/task-23-in-unit.txt`，VC-2 的证据）：按 §4.3 列出「今天在可写事务打开时发送或关连接」的全部路径，以及临时探针在 Application、Protocol、Networking 三个测试项目中的计数（按 CMD 类型）。探针按变异程序还原。
  8. **测试改动核对**（`logs/task-23-diff-check.txt`）：`diffcheck7.py a4422480 --lines`（scratchpad `t07/diffcheck7.py`）加 `t21/methods.py`，证明已有测试的方法集合只少了 D 中列出的方法、只多了 N 中列出的方法，断言行多重集合的变化逐处对应 `t006-test-dispositions.md` 的一行。
  9. **新增与改写测试 ×10**（`logs/task-23-new-x10.txt`）：本单元新增的全部用例，加上 §10.3 改写的线级用例与 b1、b2 严格形式，隔离连续 10 次，全部通过。
  10. **变异核对**（`logs/task-23-mutations.txt`；程序与 TASK-22 Checks 10 相同：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：S1～S25 见 §13，每项都要有指定的用例变红；标为「抽查」的如实记录。
  11. **生产改动边界**（`logs/task-23-boundary.txt`）：`git diff --stat a4422480 -- Server/src` 只含 Owned boundary 列出的四个项目；逐文件对到 `task-23-sites.txt` 的行；`Server/src` 下没有未跟踪的文件，本单元新建的除外，新建文件逐个列名。`git diff a4422480 -- .rks-agent/tasks/serial-gameplay-executor/spec.md` 只有附录 A 第 4 行一处，全文附在日志里。
  12. **隔离复跑**（`logs/task-23-isolated.txt`）：Application、Protocol、Networking、Host 各一次，都带取证目录，记录各自的反例类与对照记录数。Networking 单独跑一次，是因为线级测试的时序在满载下会假红（AGENTS.md「拉真实进程或真实 socket 的用例在满载全量下会假红」）。
  13. **替身清点**（`logs/task-23-doubles.txt`，交 TASK-25 的输入）：测试中实现 `INetworkMessageSender`、`IGameMessageSender`、`INetworkConnectionCloser`、`IEventService.ProjectAsync` 的替身类，逐个写「文件:行 → 本单元是否改写 → 依赖它的用例数」。基准检索得到 69 个类声明，分布在约 57 个文件中，以执行时的结果为准。
  14. **选角出站实测**（`logs/task-23-selection-bytes.txt`）：表征 a 在真实发送端下，请求者连接上一次选角 CMD 登记的编码字节数与帧数，与 `OutboundLimits.SelectionOutboundBound`（454,235 B）对照；正式全量与表征运行中 `OverLimit` 为 0。人群项另行测量（TASK-25）。
  15. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。结束时执行 `dotnet build-server shutdown`。
- Dependencies：
  - TASK-05（T-003，`0223e646`）：发件箱、发送端、传输适配、编码器与上限常量，以及它们的单元测试 `GameplayOutboxTests`、`OutboundSenderTests`、`OutboundRealSocketTests`、`OutboundQueueLimitTests`。
  - TASK-22（`a4422480`）：T-005 收口；判据 `LockRemovalSourceTests`；`t005-test-dispositions.md` 的写法。
  - TASK-01 的表征测试与金样本；TASK-10 的 `LifecycleExecutorWireTests`（W5～W9）；TASK-17、TASK-18 的写断言、取证汇与 `verification/probe-tools/violations.py`；`task-07-conflicts.md` §3、§13～§15 的处置口径。
  - 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t21/methods.py`，执行开始时核对它们存在。
- Result review：required。
  - 本单元改的是全部出站消息的唯一路径，还改了 T-003 的 `OutboundEntry` 契约（携带编码好的帧）。引用 `INetworkMessageSender`、`IGameMessageSender`、`INetworkConnectionCloser` 的生产文件约 127 个（基准检索，含定义所在文件），它们的语义在这里同时改变：发送不再抛传输异常，「已送达」改为「已登记」。改动跨 Abstractions、Application、Networking、Protocol 四个项目，Abstractions 中有三处共享契约（`GameplayWork`、作业令牌、连接上下文接缝）。
  - 主要风险是**静默的顺序错误**。跨连接顺序（XO-1～XO-7）、「帧先于断开」、组装时刻，错了多半不会让全量变红：今天守着这些的用例大多注入的是自己的替身，绕过了发件箱（§9、§10.6）。所以本单元的证据要经执行器与真实发件箱取得，变异核对是主要的判别手段。
  - 附录 E 的十七行在本单元之后大多已经满足目标语义，但证据与死代码的清理在 TASK-24。审查要确认 §9 的「本单元之后的状态」与代码一致，没有漏掉某一行的意图。
  - TASK-24、TASK-25 与 M-2 都在本单元的结果上继续。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. §1 的核实发现生产代码在执行器外调用发送器、关连接器或 `ProjectAsync`，而且这处调用不在附录 B 列出的部分内。这时 RQ-061 的发件箱一半一开启就会在生产中抛出，需要先给这处调用定 CMD 来源，属于改附录 A 或附录 B。
    2. §1 的核实结果与本计划不一致。例如找到第三个非零的 `retryLimit`；某条 XO 的产出点已不在附录 D 写的位置；顶替关闭除 `CloseRequests` 之外还有别的入口；V9 读出的 XO-6 失败语义既不是「全部继续」，也不是「有人失败就停止推进」。
    3. 需要改 Owned boundary 以外的生产文件，或需要改 `spec.md`、`plan.md`。`spec.md` 附录 A 第 4 行按裁定第 6 条在本单元改，这一处不适用本条；附录 A 其余各行、其他附录与 RQ／AC 仍适用。
    4. 正式全量出现 §10 规则覆盖不了的新失败；取证出现反例类与对照以外的记录。
    5. 表征测试中除 b1、b2 严格形式之外的任何一条，或任何金样本需要改动；b1、b2 严格形式在隔离 10 次中有任何一次失败。
    6. 某条冲突用例的保护意图，既找不到承接者，也说不出为什么已被规格消除。
    7. §4.3 的清点找到「今天在可写事务内发送、且事务会回滚」的路径。按 VC-2，回滚之后它的消息不再发出；这正是 VC-2 要求「补入本条并配证据」的情形，属于改 Spec 附录 G。
    8. 某个 XO 产出点的发送分散在不止一个 CMD 里，无法用一条 CMD 内的有序组表达。
  - 变异核对与 §4.3 的临时探针期间，允许临时改动 `Server/src`，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 拆分与顺序

**T-006 的范围**（Plan T-006 行）：
- 执行器上的发送、投影、关连接改为登记发件箱；
- 提交钩子与组装点；刷出规则与组装失败的隔离；
- 接上发送端；
- 附录 E 十七行逐项承接；
- 删除重试队列与排空服务（还有附录 F 的队伍两把投递门，`t005-closure.md` §2）；
- 改写「发送即记录」的替身；
- 开启 RQ-003 的「等待网络」检查。

**为什么翻转本身不能再拆**：
- 发送器只有一个 DI 实现。只要它改成登记，约 127 个生产文件的发送语义就同时改变，没有一个「一部分调用点直发、一部分登记」的中间状态可以只靠注册做到。
- 要做出这样的中间状态，就得在每个调用点显式选择路径，等于先给这些文件各加一个开关、再删掉。这违背「不留转发」，也让每一步的证据混在一起。
- 有三件事必须与翻转同时落地，否则翻转那一刻起行为就错了：
  - 刷出：没有刷出，登记的消息就丢了；
  - 来源关闭改经发件箱：不改，关闭会越过帧，正是 b1、b2 记下的基线缺陷；
  - XO 有序组：每连接队列各自排空，一旦失去「顺序 await」，附录 D 的跨连接先后就不再成立。
- 重试队列也只能在本单元删：翻转之后，它重试的只剩组装失败，而 RQ-031(d) 要求组装失败「只丢弃、不重试」。

**为什么附录 E 与替身可以放到后面**：
- 翻转之后，附录 E 各行今天的「发送抛异常 → 补偿、隔离、回滚、关闭」分支不会再被传输失败触发。传输失败改由发送端按 RQ-036 关闭连接，之后由生命周期 CMD 清理。这就是附录 E「新的承接方式」一栏对 DR-1～DR-3、DR-11～DR-15 写的目标（§9 逐行核对）。
- 所以翻转之后系统已经按目标语义运行。留下的是两件事：
  1. 那些分支成了死代码；
  2. 注入「会抛异常的替身」的用例仍然是绿的，却不再证明生产行为。
- 这两件事是 TASK-24 的工作：每行删掉死分支，换上经执行器和发件箱驱动的证据，并把队伍的两把投递门一起删掉，DR-5、DR-17 与它们是同一段代码。
- TASK-25 改写其余的「发送即记录」替身（约 57 个文件），并写 T-006 收口。
- 三个单元按 23 → 24 → 25 的顺序执行：
  - 24 依赖 23 的登记语义；
  - 25 的收口要在 24 删完死分支之后；
  - 替身改写如果放在 24 之前，DR 用例会在同一个文件里改两次。

**执行顺序**：
1. 在基准上重跑 §1 的核实（Required check 6）与 §6 的检索式（Required check 5 的前半）。
2. 先写判据与反例（§8），看它在基准上变红。
3. 生产侧按以下顺序改动，每步构建一次，跑相关测试项目：
   1. 接线（§2）：发送端注册、管道参数、连接上下文接缝。这一步不改变任何发送路径，全量应当仍是绿的；
   2. 作业结束钩子与来源字段（§3.1、§3.2），以及工作单元钩子（§4）。这时还没有生产代码登记，行为不变；
   3. `OutboundEntry` 契约与编码接缝（§5.1）：条目改为携带编码好的帧，发送端去掉编码，T-003 的用例随之改构造（E 类）。这时发送端仍未被生产代码使用，T-003 的用例应当全绿；
   4. **翻转**（§5）：登记适配器替换直发的发送器与关连接器，`NotificationBus` 改为组装加登记，传输回调的关闭改经发件箱（§3.3）；
   5. XO 有序组（§7）；
   6. 删除（§6）：直发路径、期限、重试设施、`retryLimit`、顶替关闭的通道；同时改 `spec.md` 附录 A 第 4 行（§6.1）。
4. 测试侧（§10）：先跑后改。
5. 构建、正式全量与取证（Required checks 1～3），隔离复跑，新增与改写用例 ×10。
6. 变异核对（§13）、§4.3 的探针、选角出站实测（Required check 14）。
7. 清单、边界、diff 核对、替身清点、`follow-ups.md`。

翻转（3.4）与 XO（3.5）之间的中间状态，只在工作区里存在，不做正式全量。

### 1. 基准核实（执行时按 Required check 6 重跑并落盘）

| # | 问题 | 检索 | 基准上已知的结果（`a4422480`，规划时读码） |
|---|---|---|---|
| V1 | 生产代码在执行器外调用发送器、关连接器、`ProjectAsync` 的位置 | 逐个调用方回溯到它的入口，入口按附录 A 归类 | 注入这三类接口的生产文件共 127 个。其中 Application 有 3 个：`SelectedSessionSafety`（关连接器）、`LoginSessionRuntime`（关连接器，在 `lifecycle:close-superseded` 作业里调用）、`NotificationBus`（发送器）；其余都在 Protocol。这些调用都在入站、生命周期、定时、事件或 GM 作业内。执行器外调用的只有测试。执行时逐个确认 |
| V2 | `INetworkConnectionCloser` 有没有执行器外的生产调用方 | 同上 | 预计没有：`TouchSocketTransportDrain` 直接操作传输注册表，`OutboundSender` 经 `IOutboundConnection.CloseAsync` 关闭。没有就删 `TouchSocketNetworkConnectionCloser`；有就保留，作为执行器外的传输关闭，并列入附录 B 的「传输层」行（属于改 Spec，按停止条件 3 处理） |
| V3 | 非零的 `retryLimit` | `grep -rn "retryLimit: [1-9]" Server/src` | 2 处：`ProtocolModule.cs:452`（`AccountWalletChanged`），以及 `:604` 附近的 PvP 参与者通知。正对应 DR-13 |
| V4 | `AddNotificationHandler` 的调用点 | `grep -c "AddNotificationHandler<" Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 63 |
| V5 | XO 产出点的位置 | 附录 D 的七个位置逐个读 | 见 §7 表，执行时核对行号 |
| V6 | 顶替关闭的入口 | `CloseRequests` 的写入方与读取方 | 写入在 `LoginSessionRegistry`（`:36` 暴露读端）；读取只有 `LoginSessionMaintenanceService.CloseSupersededAsync`（`LoginSessionRuntime.cs:60`） |
| V7 | 传输写入点 | `grep -rnE "\(\(ISender\)|\.SendAsync\(new ReadOnlySequence|SendToAsync\(" Server/src` | `TouchSocketNetworkMessageSender.WriteAsync`、`OutboundTransport.cs` 的 `Connection.SendAsync`、`A21UdpDiscoveryService.cs:127`。本单元结束时只剩后两处 |
| V8 | 传输回调里的关闭路径 | `NetworkPlugins.cs` 的 `catch … throw;`（`:77`～`:86`、`:161`～`:176`、`:235`～`:250`），以及 TouchSocket 因回调异常关闭会话的行为 | 作业体异常今天沿着 dispatcher → 插件 → `throw;` 一路传到 TouchSocket，由 TouchSocket 关闭会话。这就是 b1、b2 记下的「关闭越过帧」 |
| V9 | XO-6 今天在某个收件人失败时怎样推进（裁定第 5 条） | 读 `PartyNotificationSlice.cs` 的 `DeliverPartyStateAsync` 与 `DeliverObserverStateAsync`（`:790`～`:850` 一带），以及 `PartyTownProjectionDelivery.cs:245`～`:293` 的 `PartyExactNotificationDelivery.PublishWithRetryAsync`；再在调用方（`:689`～`:692`）看返回值的用法。判定口径：任一阶段 `HasFailures` 为真时，后续阶段是否还发 | 规划时读码：`realtime`、`acceptance`、`endpoints`、`roster`、`replay`、`observer` 每一阶段之后都是 `if (… .HasFailures) return false;`。`PublishWithRetryAsync` 在同一阶段内对每个收件人各尝试，失败的再试一次，然后才报告 `HasFailures`。调用方得到 `false` 就直接返回，不推进 `LatestRevision`。即今天是**同一阶段内全员都尝试，有人最终失败就停止推进后续阶段**。执行时在基准上复核，并写进 `logs/task-23-verify.txt` |
| V10 | 其余 XO 今天在成员失败时是继续还是停止 | 逐个读 §7 表中 XO-1～XO-5、XO-7 的产出点 | 规划时读码：XO-1 隔离、回滚并抛出（DR-2，按 VC-3 改为继续）；XO-2 捕获后继续；XO-3、XO-4 收集失败、继续，最后关闭失败者；XO-5 所有者失败即抛出（DR-10）。XO-7 执行时读。结果写进日志，与 §7 表的交付等级逐行对照 |

### 2. 接线（Networking）

- `NetworkingModule`：
  - `TryAddSingleton<IOutboundSender, OutboundSender>`，连同 `IOutboundTransport` → `TouchSocketOutboundTransport` 与 `OutboundSendOptions.Default`；Abstractions 编码接缝 → `RegistryOutboundEncoder`（§5.1）；`IOutboundDeliveryObserver` 不注册（今天没有消费者）。
  - 删除 `:50` 的 `INetworkMessageSender` 注册；`:51` 的关连接器注册按 V2 处理。
  - 网络关闭（`Networking:Enabled=false`）时发送端照样注册：注册表为空，条目都按 `Stale` 丢弃。
- `TcpServices`：Game 与 Channel 两个服务的配置都经 `OutboundTransportOptions.ConfigureSendPipe`。`TouchSocketOutboundTransport.Connection.SendAsync` 遇到没有这个设置的会话就拒绝发送，所以测试宿主自建的 `TcpService` 漏设时会立即失败，不会静默退化。
- **连接上下文接缝**（Abstractions，例如 `INetworkConnectionContexts.TryGet(NetworkConnectionId, out NetworkConnectionContext)`）：由 `TouchSocketConnectionRegistry` 实现。登记适配器靠它把今天按 ID 发送的调用，变成带「登记时连接上下文」的条目（RQ-035）。
- **停机**：发送端是单例，随容器释放调用 `DisposeAsync`，停止接收并等准入循环结束。停机顺序 RQ-009 不变，执行器先排空，发送端在容器释放时停止；停机用例 S2 不改。
- **分离点**（F-24）：
  - `OutboundSender` 的三处分离：准入循环、每连接排空循环、失败关闭，都经 `Detached`（`SuppressFlow`）启动；
  - `Submit` 在作业上调用。启动的循环不携带作业令牌，由已有的 `Delivery_runs_off_the_executor_even_when_submitted_from_a_job` 守着；
  - `DetachmentSiteTests` 名单改为：删去 `ExecutorSendDeadline.cs` 一行（`:24`），`OutboundSender.cs` 一行（`:28`）的理由去掉「not wired until T-006」。

### 3. 作业结束时刷出（Application 与 Abstractions）

#### 3.1 钩子挂在作业令牌上，不挂在执行器实例上

- 发件箱以作业为单位（Plan「发件箱的归属」）。
- 测试里 `TestGameplay` 会为单个作业新建执行器（`TestGameplay.cs:118`、`:146`），而作业体里用的登记适配器来自测试容器。所以刷出不能依赖「哪个执行器知道发送端」。
- 做法：
  - 作业令牌带一个「作业结束」钩子表；
  - 发件箱在第一次登记时，把自己的刷出挂上去；
  - 执行器（`GameplayJobHost`）在作业体结束之后、`ExitJob` 与完成信号之前，按挂上的顺序执行钩子。钩子收到两样东西：作业是否以异常结束，以及来源连接。
- 由此得到四条不变量，各自由用例钉住：
  1. 登记过东西的作业恰好刷出一次，在哪个执行器实例上都一样；
  2. 刷出发生在完成信号之前（RQ-005：dispatcher 的「完成」指发件箱已交给发送端）；
  3. 什么都没登记的作业没有额外开销；
  4. 刷出失败（发送端拒收是程序错误）时，T-003 的 `Flush` 规则照旧：只发关连接请求，来源照常关闭，作业以失败结束。
- `GameplayOutboxes.RunAsync`（T-003 的包装形状）改为由钩子驱动。`GameplayOutboxTests` 的断言不变，驱动方式改为经执行器（§10.3）。
- 取消视同失败：作业体因调用方取消而结束时，按 RQ-031(c) 处理。今天取消也是沿异常路径到达传输回调，结果是断线。

#### 3.2 来源连接

- `GameplayWork` 与 `GameplayWork<T>` 加一个可选的 `NetworkConnectionContext? Source`。
- 填写方：
  - `NetworkMessageDispatcher` 填 `context.Connection`；
  - 生命周期分发器在 `lifecycle:connected` 时填该连接；
  - `lifecycle:disconnected` 不填，连接已经断开；
  - 定时、事件、GM、HTTP、启动作业不填。
- 调用链里的 `SourceConnection` 只有 ID，不够 RQ-035 的精确身份，所以另设字段。T-007 若要统一两者，由它决定。

#### 3.3 作业体失败时的来源关闭（RQ-031(c)、DR-15）

- 作业体以异常结束 → 钩子以「失败」刷出：
  - 已提交单元内与单元外登记的条目照常保留；
  - 回滚、结果不确定或未结束的单元内的条目丢弃；
  - 关连接请求一律执行；
  - 最后追加关闭来源连接。
  这些都是 T-003 `GameplayOutbox.Complete` 已有的规则。
- 传输回调不能再自己关闭来源连接：
  - 今天作业体的异常沿 `throw;` 传到 TouchSocket，由 TouchSocket 立即关闭会话（V8）。这一关会越过还在发送队列里的帧。
  - 改为：dispatcher 收到作业体异常时，记日志（沿用插件今天那一行 `Game handler failed` 的文字与字段），然后正常返回，不再向插件抛出。来源的关闭只经发件箱。
  - 判别方式由执行者定，例如执行器把「作业体异常」与「入队被拒、执行器已停止、深度超限」区分开。约束：前者只经发件箱关闭，后者没有登记过任何东西，仍按今天由传输关闭。
- 证据：
  - b1、b2 严格形式；
  - W7 严格形式（`LifecycleExecutorWireTests.cs:69`）；
  - §10.4 的 N6。
  - 变异 S2 把插件的 `throw;` 恢复，这些用例变红。

### 4. 工作单元钩子与组装点（RQ-031(0)(b)）

#### 4.1 挂点

- `DomainTransactionScope.Begin(readOnly: false)` 在执行器上开启事务时，同时 `GameplayOutbox.BeginUnit()`，单元交给 `DomainTransaction` 持有。
- 不开单元的三种情况：
  - 只读事务：只读事务从不提交，若开单元，单元内的条目会全部被丢弃；
  - `BeginSnapshot`：同理，它从不提交；
  - 执行器外：此时开写事务本来就会被 RQ-061 拒绝。
- 结算：
  - `CommitAsync`：参与者全部提交之后、执行 after-commit 回调之前，调用 `unit.CommitAsync`，在这里组装单元内登记的组装条目，然后控制权才回到 CMD（RQ-031(0)）。
  - 组装抛出非取消异常时，按 T-003 的规则，单元记为 `Uncertain`，异常照常上抛。逐收件人的组装失败不在这里：它被 `OutboxComposition` 按收件人隔离（RQ-031(d)），不会抛到这一层。
  - 回滚，包括 `RequireRollback()` 的正常拒绝 → `unit.Rollback()`。
  - 提交失败转成回滚 → `Rollback`。
  - `TransactionOutcomeUncertainException`，即非可失败参与者违约或回滚失败 → `MarkUncertain`。
  - 释放时单元仍开着 → 记为回滚，`GameplayOutboxUnit.Dispose` 已经这样做。
- 提交钩子挂在 `DomainTransaction` 本身，不挂在 `UnitOfWorkBehavior`（Plan「发件箱的归属」），这样 T-008 改用显式工作单元后可以沿用。

#### 4.2 `ProjectAsync` 的守卫不放宽

- `EventService.ProjectAsync`（`:56`～`:63`）今天在可写事务内调用即抛。RQ-031(0) 允许单元内登记、提交时组装，但今天没有任何调用方需要这样做。
- 本单元保留守卫，所以全部即时投影仍在单元外登记、登记时组装。
- 单元内的组装路径只由直接用 `GameplayOutbox.ComposeAsync` 的调用方与单元测试走到。§4.3 的清点给出今天的实际数目。

#### 4.3 今天在可写事务内发送的路径（VC-2 的证据，Required check 7）

- 翻转之后，这类发送若所在单元回滚，消息就不再发出（RQ-031(b)）。今天回滚之后它们照样已经发出了。
- 清点分两步：
  1. **静态**：命令处理器不能注入发送器（`CommandHandlerArchitectureTests`），所以候选只有三类：
     - Reliable 与 Direct 事件订阅中注入发送器或调用 `ProjectAsync` 的。基准检索命中 `RentalSlice`、`CapsuleSlice`、`AchievementSlice`、`TitleBookSlice` 四个文件，执行时逐个判定订阅种类与调用时刻；
     - 在可写事务内调用 `SelectedSessionSafety` 关连接的路径；
     - 领域服务中的发送。
  2. **探针**：临时在登记适配器里统计「登记时有开着的单元」的次数，按 CMD 类型汇总，跑 Application、Protocol、Networking 三个测试项目，跑完按变异程序还原。
- 结果的处理：
  - 0：VC-2 按「仅限理论情形」成立，写进日志；
  - 非 0：逐条判断所在单元会不会回滚。会回滚的按停止条件 7 处理。

### 5. 登记适配器（Application）

`INetworkMessageSender` 与 `INetworkConnectionCloser` 的新实现放在 `Application/Execution/Outbox/`，经 `GameplayOutboxes.Current` 登记，经 §2 的连接上下文接缝取上下文。`Protocol` 的 `GameMessageSender` 不变，它只是把 Game 消息转给 `INetworkMessageSender`。

| 今天的调用 | 登记成 | 说明 |
|---|---|---|
| `SendAsync(id, message)` | 编码后 `OutboundEntry.Batch(当前上下文, [message], 帧)` | 登记时按 ID 取上下文。取不到，即连接已断开或已被替换，就静默丢弃，记 Debug，作业不失败（F-83）。今天这种情况会抛出。取得到就按 §5.1 完整编码，编码失败在调用点抛出 |
| `SendBatchAsync(expected, messages)` | 整批编码后 `Batch(expected, messages, 帧)` | 精确上下文由调用方给出。传输身份的校验在交付前由发送端做（`Stale`，RQ-035）。今天「上下文已被替换就抛出」的检查改由 `Stale` 承接（`PreparedBatchTests` 的 `Equal_id_context_replacement…`，§10.3） |
| `SendExplicitAsync(direct, delivery)` | 直接收件人一条 Required 批次，然后每个目标一条 BestEffort 批次，跳过与直接收件人相同的目标。不在有序组内时，各条之间没有跨连接先后（附录 D「允许乱序的写法」第 3 条）；在有序组内时，依次取组内序号（§7） | RQ-034「Required 失败中止该目标、BestEffort 按目标隔离」由每连接队列的结束规则承接：失败只结束该连接的队列并关闭它。今天的「直接收件人失败，后面的目标就不再尝试」只在有序组内保持，因为组外本来就允许乱序 |
| `RequestCloseExactAsync(expected)` | `OutboundEntry.Close(expected)` | 排在该连接此前登记的条目之后（RQ-012；口径按裁定第 2 条：按连接计） |
| `RequestCloseAsync(id)` | 按 ID 取上下文，再 `Close(上下文)`；取不到就静默丢弃 | 同上 |
| `NotificationBus.PublishAsync` | 逐收件人：先查 `IsStillHere`，不在为 `Gone`；再组装，返回空为 `Declined`；组装出的消息在该收件人的组装里一并编码，作为一条 Required 批次登记到他的连接，结果为 `Delivered`，即已登记。组装或编码抛出非取消异常时为 `Failed`，记 `Warning`，不重试。今天编码失败在 `AttemptAsync` 的同一个 `catch` 里记为 `Failed`，所以这一点与今天相同 | 用 `GameplayOutbox.ComposeAsync` 的逐收件人隔离，或在总线里保留同样的隔离，由执行者选，断言以 §10.3 的改写为准。`PerRecipientTimeout` 随 `NotificationDeliveryOptions` 删除：它今天限的是「组装加发送」，发送已不在执行器上，组装时长计入 CMD 时长（DR-14 的期限由 RQ-036 承接） |

#### 5.1 登记时完整编码（裁定第 1 条，备选 B）

- **语义**：
  - 编码与今天一样，在调用点、在执行器上完成。今天编码就在作业里的直发路径上，所以执行器上的耗时不增加，只记录（RQ-091 用户修订）。
  - 编码失败与今天一样，在调用点抛出，CMD 失败，再按 RQ-031(c) 刷出存活条目、关闭来源。
  - 整批编码「全有或全无」（RQ-034）在登记点保持：一批中任何一条编码失败，整批不登记。
  - 不需要给附录 E 补行。`SelectCharacterHandler`「发送前验证必达消息可编码、失败时派发补偿」这条工作流的前提不变。
- **检查与编码的次序**，在登记适配器里：
  1. 取上下文（`SendBatchAsync` 由调用方给出，其余按 ID 取）。取不到 → 先做位置检查（§8.1），再静默丢弃；
  2. 端点与编码器检查，然后整批编码。异常类型与消息文字沿用今天 `TouchSocketNetworkMessageSender` 的两种写法：`SendAsync` 的「is not supported for endpoint '…'」与「has no registered encoder」，`SendBatchAsync` 的「has an incompatible endpoint」；
  3. 位置检查（`GameplayOutbox` 的 `EnsureWritable`，§8.1）；
  4. 登记。
  - 编码排在位置检查之前，是为了让 `OutboundSendFailureTests` 的 4 条不改即过：它们在测试线程上调用发送器，断言的是编码错误。代价是：执行器外的调用若消息恰好不可编码，抛出的是编码错误，不写 RQ-061 的取证。这一点列为「修订中新发现、待裁定」第 1 条。
- **契约改动**（T-003 内部契约，尚未接线）：
  - Abstractions 新增编码接缝，例如 `IOutboundMessageEncoder.Encode(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages)`，返回整批的帧；失败即抛出。由 Networking 的 `RegistryOutboundEncoder` 实现，替换 internal 的 `IOutboundEncoder`。它在 Encode 里写的每帧 Debug 日志（`S->C …`）随之从交付时刻移到登记时刻。
  - `OutboundEntry.Batch` 改为同时接收消息与编码好的帧：帧拷贝一份，归条目所有，不能为空。类型化消息保留，只供观察，即测试与 TASK-25 的替身断言用；发送端只用帧。
  - `OutboundSender` 删除准入时的编码、`Item.EncodingError` 与对应分支；字节上限按条目的帧长计，不变。
  - `OutboundOutcome.EncodingFailed` 删除，发送端已不可能产生它。
  - RQ-031(0)「发送端只接收组装好的帧」由此按字面成立。RQ-034「整批编码……在发送端保持」的保护意图是全有或全无，现在在登记点保持。这是对措辞的偏离，按裁定第 1 条接受，在 Result 中登记。

#### 5.2 DI

Application 的执行注册（`GameplayQueue.cs:100` 一带）注册这两个适配器与 `GameplayOutboxes`。Networking 不再注册 `INetworkMessageSender`，两边不会争同一个注册。

### 6. 删除清单

**检索式**（基准与结束各跑一次，Required check 5）：

```
grep -rnE --include='*.cs' \
  "TouchSocketNetworkMessageSender|ExecutorSendDeadline|IOutboundEncoder|EncodingFailed|DirectSendDeadline|NotificationRetryQueue|NotificationRetryDrainService|INotificationRetryDrain|NotificationRetry\b|NotificationDeliveryOptions|PerRecipientTimeout|RetryInterval|retryLimit|RetryLimit|CloseRequests|close-superseded|CloseSupersededAsync|DeliverExplicitAsync|TouchSocketNetworkConnectionCloser" \
  Server/src
```

**结束时只允许剩下**：
- `TouchSocketNetworkConnectionCloser`：只有 V2 的结论是保留时才允许，而那样会触发停止条件 3；
- `RetryLimit`／`retryLimit`：本单元不处理、名字相同但含义不同的标识符，执行时逐个列出。基准上读码已知 `PartyExactNotificationDelivery` 有自己的「再试一次组装」，它不叫这个名字，以检索结果为准；
- 其余为 0。

**删除的文件与成员**：

| 项 | 位置 | 承接 |
|---|---|---|
| 直发路径 | `Networking/Runtime/TouchSocketNetworkMessageSender.cs` 整个文件，含 `DeliverExplicitAsync`、`DirectSendDeadline` | 发送端（`OutboundSender`）与 §5 的登记适配器；端点检查、编码与它们的异常文字移到登记适配器与 `RegistryOutboundEncoder`（§5.1） |
| 发送端内的编码 | `Networking/Outbound/OutboundEncoder.cs` 的 internal `IOutboundEncoder`；`OutboundSender` 的准入编码与 `EncodingError` 分支；`OutboundOutcome.EncodingFailed` | 登记时完整编码（§5.1） |
| 直发期限 | `Networking/Runtime/ExecutorSendDeadline.cs` 整个文件 | 过渡期规则 4 到期。2 秒只剩约定的两个位置：`OutboundLimits.DeliveryTimeout`（每收件人交付期限，RQ-036）与 `OutboundLimits.OrderedWaitLimit`（有序组等待上限，RQ-033） |
| 重试队列 | `Application/Notifications/NotificationRetryQueue.cs` 整个文件 | 附录 F「删除」栏；DR-13、DR-14 → VC-5 |
| 排空服务 | `Application/Notifications/NotificationRetryDrainService.cs` 整个文件，以及 `NotificationDispatchingExtensions` 中的托管服务注册（含 TASK-11 加的那几行，F-22） | 同上 |
| 重试接口与记录 | `INotificationRetryDrain`、`NotificationRetry`、`NotificationBus.DrainAsync`／`RetryAsync`／`Requeue` | 同上 |
| 期限与间隔选项 | `NotificationDeliveryOptions` | 同上；§5 |
| 重试上限 | `AddNotificationHandler` 的 `retryLimit` 参数、`NotificationHandlerRegistration.RetryLimit`，以及 `ProtocolModule.cs` 的 63 个实参 | 同上。「注册时声明重试上限」一节的 AGENTS.md 文字归 T-009 |
| 顶替关闭的通道 | `LoginSessionRegistry.CloseRequests` 与它的写入，`LoginSessionMaintenanceService.CloseSupersededAsync` 与 `lifecycle:close-superseded` | 顶替发生在登录 CMD 里：在同一条 CMD 中把被顶替连接的在线身份移除（今天 `onlineUsers.TryDisconnectConnection`），再经关连接器把关闭登记进这条 CMD 的发件箱（F-65，裁定第 6 条）。之后的清理由那个连接的 `lifecycle:disconnected` 完成，与今天相同。`spec.md` 附录 A 第 4 行随之改（§6.1） |
| 关连接器 | `Networking/Runtime/TouchSocketNetworkConnectionCloser.cs` | 按 V2；登记适配器与 `OutboundSender` 的关闭 |

**判据**：上表中已经删到零的名称，加入 `LockRemovalSourceTests` J1 的全局名称规则，规则组标注为「T-006 投递」：`TouchSocketNetworkMessageSender`、`ExecutorSendDeadline`、`NotificationRetryQueue`、`NotificationRetryDrainService`、`INotificationRetryDrain`、`NotificationDeliveryOptions`、`CloseSupersededAsync`，以及按 V2 删掉时的 `TouchSocketNetworkConnectionCloser`。类注释中「T-006 的三项」改为「TASK-24 的一项（队伍两把投递门）」。

#### 6.1 `spec.md` 附录 A 第 4 行（裁定第 6 条，事实性更新）

- 只改这一行的「新的 CMD」一栏。今天写的是「生命周期 CMD（连接观察随 `lifecycle:disconnected`；顶替后的关闭 `lifecycle:close-superseded`）与定时 CMD（`timer:LoginSessionMaintenanceService`）」。
- 改为：「生命周期 CMD（连接观察随 `lifecycle:disconnected`）与定时 CMD（`timer:LoginSessionMaintenanceService`）；顶替后的关闭由顶替发生的那条 CMD 登记进它的发件箱（T-006，F-65），被顶替连接的清理随它的 `lifecycle:disconnected`」。
- 「今天的位置」一栏不改：`LoginSessionMaintenanceService` 仍在，只剩维护扫描。
- 改动全文写进 `logs/task-23-boundary.txt`（Required check 11）。附录 A 其余各行、其他附录与 RQ／AC 不改。

### 7. 跨连接有序组（XO-1～XO-7）

**接口**：
- Abstractions 新增一个具名的协议边界能力，例如 `IOutboundOrdering.Begin(string name, OutboundDelivery defaultDelivery)`，返回一个作用域。
- 作用域打开期间，本作业登记的每一条（包括经 `ProjectAsync` 组装出来的）依次取组内序号；作用域关闭之后，后续登记不再入组。
- 每个作用域新建一个 `OutboundOrderGroup`，用后不保留（`GameplayOutbound.cs` 对组身份的要求）。
- 组内条目默认 BestEffort：一个成员交付失败，只关闭他自己，组照常继续（VC-3、DR-2）。Required 的只有两处（裁定第 5 条）：
  - XO-5 的所有者条目：所有者失败后，组内后续条目跳过（DR-10、RQ-034）；
  - XO-6 的阶段条目：前提是 §1 V9 在基准上复核成立，即今天「有人失败就停止推进后续阶段」。这样一个阶段里有人交付失败，后续阶段就不再交付，保持今天的行为，不改 Spec。V9 若不成立，XO-6 回到 BestEffort，在 Result 中写明。
- 作用域可以逐条改交付等级，例如 XO-5 只把第一条设为 Required。具体形状由执行者定。
- RQ-070(b) 的具名清单把它列为「发件箱」能力，T-008 写判据时引用。

| XO | 产出点（附录 D，执行时核对行号） | 组内次序 | 组内交付等级 | 证据（发送端层，§10.4 N8） |
|---|---|---|---|---|
| XO-1 | `DungeonEntryProjectionDelivery.cs:353-376` 的成员循环与请求者 | 各跟随者的进入批次 → 请求者 | 全部 BestEffort | 慢跟随者在上限内：请求者的批次在其后交付；慢跟随者超过上限：他被关闭，请求者照常交付 |
| XO-2 | `LeaveDungeonSlice.cs:283-309`：同伴的决定 → 领队的 ACK | 同伴 → 领队 | BestEffort | 慢同伴在上限内：领队的 ACK 在其后交付 |
| XO-3 | `BossTowerProjectionDelivery.cs:175` 起的成员批次（按 `Session == requester ? 1 : 0` 排序）→ 请求者 | 成员 → 请求者 | BestEffort | 同 XO-1 |
| XO-4 | `IllusionTowerProjectionDelivery.cs:171` 起，同 XO-3 | 同上 | BestEffort | 同 XO-1 |
| XO-5 | `SupplementalSynchronizationMessages.cs:222-276` `CleanupNotificationDelivery.DeliverAsync` | 所有者通告 → 城镇同伴的投影 → 队伍投影 | 所有者 Required，其余 BestEffort | 所有者慢但在上限内：同伴与队伍投影排在其后；所有者交付失败：同伴与队伍投影都被跳过（DR-10） |
| XO-6 | `PartyNotificationSlice.cs:798-842` 的阶段推进（出口 `PartyTownProjectionDelivery.cs:271`） | 阶段 1 的全体收件人 → 阶段 2 → …，阶段内按登记顺序 | Required（V9 成立时；否则 BestEffort） | 阶段 1 中一名收件人慢但在上限内：阶段 2 的任何条目都在其后交付（阶段屏障由「组内每条等它前面的全部」表达）；阶段 1 中一名收件人交付失败：后续阶段的条目全部跳过（V9 成立时） |
| XO-7 | `MarriageWeddingSlice.cs:220-222` | 两人位置广播 → 任一端的过场 | BestEffort | 一端慢但在上限内：另一端的过场在其后交付 |

- 一个组只覆盖一条 CMD 内的登记。产出点的发送若分散在不止一条 CMD 里，按停止条件 8 处理。
- XO-6、XO-7 仍按附录 D「保守列入」处理，本单元不移出。
- 附录 D「允许乱序的写法」三种（先发本人再发他人、城镇到达先同伴后本人、请求者 Required 先发）不入组，单连接顺序仍由 RQ-032 保持。
- 发送端层证据的做法：
  - 在 Protocol.Tests 或 Networking.Tests 中，用产出点所在的现有夹具驱动真实的协议路径；
  - 登记适配器是真的；
  - `IOutboundSender` 是真的 `OutboundSender`（Networking 对两个测试程序集开放了 internal），接一个可以按连接放慢的测试传输（`OutboundTestSupport`）；
  - 断言的是**交付**次序，不是线上到达次序（附录 D「口径」）。
- **XO-6 标 Required 后与今天的一处细差**：T-003 的有序组没有「阶段」概念，Required 的失败会让组内其后的**全部**条目跳过。所以失败者之后、同一阶段里排在他后面的收件人，也收不到这一阶段。今天这些人会收到本阶段，只是收不到后续阶段。列为「修订中新发现、待裁定」第 2 条。

### 8. 判据

#### 8.1 RQ-061 发件箱一半（F-64）

- `GameplayOutbox` 的 `EnsureWritable`（`GameplayOutbox.cs:181`～`:187`）改为经 `GameplayWriteAssertion.Writing("GameplayOutbox.Register")` 判定：执行器外抛出 `GameplayOffExecutorException`，并写取证，kind 为 `write-off-executor`。
- 于是执行器外经登记适配器的 `SendAsync`／`SendBatchAsync`／`SendExplicitAsync`／`RequestClose*Async`，以及 `ProjectAsync`，都在登记点抛出。
- RQ-061 的「发件箱登记」一半由此开启，范围与写断言相同，只按调用位置判定（Plan「判据与测试的关系」）。
- 反例：每类入口各一条，即发送器、关连接器、投影。`GameplayOutboxTests.Registering_outside_a_job_is_a_program_error` 改为同时断言取证记录。

#### 8.2 RQ-003「等待网络」

- `TouchSocketOutboundTransport.Connection.SendAsync` 与 `CloseAsync` 入口：当前流带着有效的作业令牌时，`token.Fault(new GameplaySelfWaitException("Waiting for the network", chain))` 并抛出，写取证，kind 为新的 `network-wait`。
- 发送端自己的循环都在 `SuppressFlow` 下启动，不带令牌，所以生产中这条恒不触发。它守的是「将来有人在作业里直接碰传输」。
- 反例：在作业里直接调用一个已解析的连接的 `SendAsync`，作业以程序错误失败，错误日志带调用链。
- 「向执行器入队后等待那条 CMD 完成」的一半已由 `GameplaySelfWaitException` 守着，本单元不改。

#### 8.3 传输写入点（F-63）

- 新增源码判据，例如 `Networking.Tests/Outbound/TransportWriteSourceTests`，写法仿 `LockRemovalSourceTests`：扫描 `Server/src` 中的 TouchSocket 发送调用与 `SendToAsync`。
- 只允许两处：
  - `Networking/Outbound/OutboundTransport.cs`：唯一的 TCP 写入点，只在发送端循环里被调用；
  - `Networking/Runtime/A21UdpDiscoveryService.cs`：UDP 发现回包，在执行器外的 `BackgroundService` 接收循环里，不是客户端连接，附录 B「UDP 发现」行。
- 每处附理由，名单按处数比较（同 J2 的写法）。
- 反例：在任一其他文件加一处会话发送，即被点名。

#### 8.4 J1 名称规则

见 §6 末尾。反例沿用 J1 已有的通用反例，不另加。

### 9. 附录 E 与附录 G 在本单元之后的状态

本表是 TASK-24 的输入，也供结果审查对照。「目标已满足」指翻转之后，生产行为已经等于附录 E「新的承接方式」，但旧分支代码与证据用例留给 TASK-24。

| 行 | 本单元之后 | 理由 | TASK-24 要做的 |
|---|---|---|---|
| DR-1 选角发送失败 → 补偿 | 目标已满足 | 发送不再抛出；失败由 RQ-036 关闭，再由生命周期 CMD 拆除 | 删除「发送抛出即补偿」的分支，命令失败的补偿保留；证据经执行器与发件箱取得 |
| DR-2 进本部分失败 → 隔离与 `RollbackEntry` | 目标已满足（VC-3） | 同上 | 删除传输失败触发的 `IsolatePartialCohortAsync`／`RollbackEntry` 分支；VC-3 证据 |
| DR-3 两塔 `SendToExactAsync` 失败 → `CloseQuietly` | 目标已满足 | 失败集合恒为空 | 删除失败集合与 `CloseQuietly`；证据 |
| DR-4 怪物死亡、祭坛、清算 | 传输一半目标已满足（VC-4）；组帧等非传输失败的一半不变 | `Failed` 只剩组装失败 | 补「怪物死亡提交后失败时，每个参与者既被封锁、连接也被关闭」与「祭坛组装失败封锁并关闭全部参与者」（AC-011） |
| DR-5 队伍水位线只在送达后推进 | 目标已满足：`Delivered` 即已登记，刷出时推进 | 语义随 `Delivered` 改变 | 删除两把投递门；证据 |
| DR-6 7 个配额刷新 | 同上 | 同上 | 证据 |
| DR-7 GM Cera | 传输失败不再产生 `Failed`；组装失败仍返回 unconfirmed（Plan R-5） | — | 结果改为「已提交发送／不在线」（VC-6，F-25）；证据 |
| DR-8 房间机制抛 IOException 等重试 | 只剩组装失败会触发，与 Plan R-5 一致 | — | 证据 |
| DR-9 生物重命名 | 目标已满足：外观与 ACK 依次登记 | — | 证据 |
| DR-10 清理通告必达 | 目标已满足（XO-5 组内所有者 Required） | §7 | 证据已在本单元（N8 XO-5）；TASK-24 复核 |
| DR-11 回选角部分失败 | 目标已满足 | 同 DR-1 | 删除分支；证据 |
| DR-12 初始 NOTI 失败 | 目标已满足 | 同上 | 证据 |
| DR-13 钱包、PvP 重试 1 次 | 目标已满足（VC-5），重试已删除 | §6 | — |
| DR-14 期限、重试队列、排空服务 | 目标已满足，已删除 | §6 | — |
| DR-15 handler 抛异常 → 断线 | 目标已满足（RQ-031(c)） | §3.3 | — |
| DR-16 称号投影不插进进入序列 | 目标已满足（RQ-032） | 称号事件 CMD 在选角 CMD 之后执行，它的条目排在选角条目之后 | 证据 |
| DR-17 队伍流门与水位线 | 目标已满足，门仍在但无人争用 | — | 删门；证据 |

- 附录 G：本单元落实 VC-1（表征 a 的金样本不变，帧内容与相对顺序不变）、VC-2（§4.3）、VC-5、VC-8（发送端超时或超限即关闭；不再有执行器上的直发期限）。VC-3、VC-4、VC-6 的证据在 TASK-24。
- 实施中若发现附录 E 没有覆盖的依赖，记入 Result，并按附录 E 末句交主控方决定是否补入本表。补表属于改 Spec。

### 10. 测试侧

#### 10.1 先跑后改

沿用 TASK-21 §6.1：生产改动完成后先构建、跑全量，按失败形态分类；只按本节规则处理，不顺手改别的。删除的每一条都写明承接它的后继用例，名字必须能检索到；没有后继的，写明保护意图为什么已由规格消除（TASK-17 主控方裁定第 7 条）。

#### 10.2 处置分类

| 类 | 判定 | 处置 |
|---|---|---|
| E 编译迫使 | 引用已删除的类型、参数或注册（`retryLimit` 实参、`NotificationDeliveryOptions`、`TouchSocketNetworkMessageSender`），断言不受影响 | 删去实参或替换构造，断言不变，由 `diffcheck7.py` 证明 |
| R 重试与期限 | 用例的被测对象是重试队列、排空循环、每收件人期限或直发期限 | 删除。附录 E DR-13、DR-14 与过渡期规则 4 把它们规格消除；期限的意图（慢连接不拖累别人、超时即失败）由发送端的用例承接，逐条写承接者 |
| S 「发送在执行之内」 | 断言依赖发送在 CMD 内同步发生：同步抛出、回调里嵌套总线、发送结果决定后续 | 改写为发件箱条目与发送端结果的断言，经执行器驱动；保护意图不变 |
| W 线级 | 经真实 socket 断言帧与断开 | 预计不改即过，或变得更严格（宽松形式 → 严格形式）。仍通过的记「保留」，并写明为什么仍有效 |
| F 「发送即记录」替身 | 注入自己的替身、绕过发件箱，本单元之后仍是绿的 | 不改，列入替身清点（Required check 13）交 TASK-25；它们不能作为本单元的证据（Plan「测试」第 2 条） |

#### 10.3 52 条冲突用例（`task-07-conflicts.md` §3，预定处置，执行时逐条落实）

| 文件 | 条数 | 预定处置 | 承接或理由 |
|---|---:|---|---|
| `Application.Tests/Notifications/NotificationRetryTests.cs` | 9 | R，删除整个文件 | 规格消除：DR-13、DR-14、VC-5 不再重试，附录 F 删除重试队列。「失败不重试」本身另加一例（N14） |
| `Application.Tests/Notifications/NotificationRetryDrainServiceTests.cs` | 4 | R，删除整个文件 | 规格消除：排空服务删除（附录 F） |
| `Application.Tests/Notifications/NotificationDeliveryTests.cs` | 16 | 期限相关 5 条为 R，删除：`A_recipient_who_runs_out_of_time_is_a_failure`、`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`、`A_recipient_who_ran_out_of_time_is_named_as_that_and_not_as_an_error`、`Something_thrown_after_the_deadline_is_still_reported_as_what_it_was`、`A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason`。承接 `OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`、`A_send_that_ignores_its_cancellation_still_times_out`、`OutboundRealSocketTests.A_client_that_stops_reading_times_out_and_is_closed_while_another_keeps_receiving`。`A_recipient_whose_delivery_fails_does_not_stop_the_others` 为 S，改写到发送端层：一个收件人的连接交付失败，其余照常交付；承接 `OutboundSenderTests.A_required_failure_stops_its_group_and_a_best_effort_failure_does_not` 的同类断言。其余 10 条为 S，改写为「逐收件人登记的条目与结果」，在作业内运行；`A_handler_that_throws_for_one_recipient_fails_only_that_one` 与 `A_failed_delivery_is_said_out_loud` 就是 AC-011 第 3 条「某个收件人组装失败只丢弃他的条目、记 `Warning`、连接不关闭、CMD 成功」 | 断言里的「发送了几次、发给谁」改为「登记了哪些条目、给哪个上下文」 |
| `Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs` | 5 | S，改写为记录发件箱条目：所有者条目 Required 且在组首，同伴与队伍投影依次取组内序号；帧内容断言不变 | XO-5、DR-10 |
| `Application.Tests/Sessions/EffectOperationReplayTests.cs:176`、`:307` | 2 | S，改写：发送回调已不在主执行之内，回调里同步嵌套的总线调用改为第二条先后执行的 CMD；「后续只读事务不替换主足迹或提交状态」「第二个主命令不能支付或覆盖已提交的条目」的断言不变 | `task-07-conflicts.md` §3.2 的理由 |
| `Networking.Tests/Runtime/PreparedBatchTests.cs` | 5 | S，被测类型 `TouchSocketNetworkMessageSender` 删除，改为驱动登记适配器（在作业里）。编码相关的 3 条（`A_late_registration_endpoint_or_actual_frame_limit_failure_sends_nothing`、`Actual_game_encoder_late_invalid_pet_skill_extra_or_avatar_sends_no_reset`、`A_late_encoding_failure_sends_no_earlier_reset_frame`）语义不变：在调用点抛出，整批什么都不登记（§5.1），断言里的「什么都没发」改为「发件箱里没有这一批的条目」。`Equal_id_context_replacement_is_refused…` 改为「登记时上下文已被替换 → 不登记、不抛出」或交付时 `Stale` 的断言；`Buffers_are_owned_and_transport_failure_can_stop_a_partially_sent_batch` 的所有权一半改为「条目拥有自己的帧拷贝」，传输失败一半移到发送端层 | 传输失败与替换的承接：`OutboundSenderTests.An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one`、`A_delivery_that_exceeds_its_deadline_closes_the_connection`。不能改写的写明理由后删除 |
| `Networking.Tests/Integration/TouchSocketNetworkMessageSenderExplicitDeliveryTests.cs` | 3 | S，改写：登记适配器把 `SendExplicitAsync` 映射成 Required 直接条目与 BestEffort 目标条目，并排除所有者（§5 表）；失败隔离的断言移到发送端层 | 类名中的被测类型已删除，文件改名在 Result 中登记 |
| `Networking.Tests/Integration/HandlerFailureConnectionIntegrationTests.cs` | 2 | W，预计不改即过：handler 抛出后关闭改经发件箱，其余连接照常服务 | 仍通过的记「保留」 |
| `Networking.Tests/Runtime/ExactConnectionCloseTests.cs` | 2 | 被测类型 `TouchSocketNetworkConnectionCloser` 按 V2 删除时，删除这两条。承接：登记适配器的精确关闭用例（N12），以及 `OutboundSenderTests.An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one`（同 ID 不同上下文不被关闭） | — |
| `Networking.Tests/Integration/OutboundSendFailureTests.cs` | 4 | **不改**（裁定第 1 条，备选 B）。它们在测试线程上经宿主的 `INetworkMessageSender`（夹具装了 `ApplicationModule`，解析到的就是登记适配器）发送，断言异常类型 `InvalidOperationException` 与消息文字。按 §5.1 的次序，编码检查先于位置检查，消息文字沿用今天的写法，所以 4 条照原样通过；记「保留」 | 若执行时发现必须改动其中任何一行，按停止条件 6 回报，不自行改 |

加法：9 ＋ 4 ＋ 16 ＋ 5 ＋ 2 ＋ 5 ＋ 3 ＋ 2 ＋ 2 ＋ 4 ＝ 52。

#### 10.4 52 条之外已知受影响的用例与新增用例

**已知受影响**（基准读码，以先跑后改的结果为准）：
- `Networking.Tests/Runtime/ExecutorSendDeadlineTests.cs` 9 条：R，删除整个文件。承接：
  - 期限：`OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection`；
  - 「分离的关闭不在执行器上」：`OutboundSenderTests.Delivery_runs_off_the_executor_even_when_submitted_from_a_job`；
  - 未观察异常：`OutboundSender.Observe` 已有的用例。执行时逐条写名。
- `LifecycleExecutorWireTests`：
  - `:69` W7 改回严格形式：客户端先读完那一帧，再读到 FIN（F-20）。「日志中没有 `direct send deadline exceeded`」一句改为「作业期间没有传输写入」，即 RQ-003 检查未触发；
  - `:146` 顶替关闭改为由登录 CMD 的发件箱执行，断言「被顶替的连接在登录 CMD 之后关闭，清理在执行器上」不变，用例名中的「by maintenance」按 F-87 的做法不改名，记入 F-87；
  - `:172` `A_send_to_a_resetting_client_does_not_hold_the_executor_past_the_deadline` 改写：执行器根本不等交付，紧接着入队的作业立即执行；
- `DetachmentSiteTests`：名单按 §2 改，断言逻辑不变。
- `GameplayOutboxTests`：驱动方式改为经执行器与作业结束钩子（§3.1），条目构造随 §5.1 改为带帧，断言不变。
- T-003 的发送端用例（裁定第 1 条带来的 E 类）：
  - `OutboundTestSupport` 的 `TextEncoder` 与 `Batch` 帮手改为构造带帧的条目，`OutboundSender` 的构造去掉编码器；
  - `OutboundRealSocketTests` 的 `Batch`（`:211`）与 `RegistryOutboundEncoder` 的构造（`:345`）同理；
  - `OutboundSenderTests.A_batch_that_cannot_be_encoded_sends_none_of_it`（`:231`）删除：发送端已不编码，`EncodingFailed` 不存在。承接 N13「整批编码失败在调用点抛出、什么都不登记」与 `PreparedBatchTests` 的三条编码用例；
  - 其余 `OutboundSenderTests`、`OutboundRealSocketTests`、`OutboundQueueLimitTests` 的断言不变，由 `diffcheck7.py` 证明。
- `CommandHandlerArchitectureTests`、`NotificationHandlerArchitectureTests`：不应受影响。受影响就按 E 或 S 记录。

**新增（N，预计）**：

| # | 用例 | 钉住 |
|---|---|---|
| N1 | 经真实宿主：一条入站 CMD 的回包经发件箱与发送端到达客户端；作业的完成信号发出时，条目已交给发送端 | RQ-005 的「完成」口径；刷出不变量 2 |
| N2 | 登记时连接已不在：静默丢弃、作业成功（单元）；客户端在回包之前断开（线级），作业成功，日志里没有作业失败 | F-83 |
| N3 | 执行器外经发送器、关连接器、`ProjectAsync` 登记：各抛 `GameplayOffExecutorException` 并写取证 | RQ-061 发件箱一半的反例（RQ-072） |
| N4 | 作业里直接碰传输：程序错误，日志带调用链 | RQ-003 的反例 |
| N5 | 真实 `DomainTransactionScope`：两个可写事务各登记一条，第二个回滚，只刷出第一条；提交后组装看到已提交的状态、看不到后续阶段；结果不确定的单元内条目丢弃；只读事务内的登记按单元外处理 | AC-011 第 1、5 条；§4.1 |
| N6 | 经 dispatcher 的真实作业：先在已提交单元内、再在单元外登记，然后抛出；客户端按序收到这些帧，然后才收到 FIN；回滚单元内的帧没有发出 | AC-011 第 2 条；RQ-031(c) |
| N7 | CMD 关闭另一个连接：那个连接先收到此前登记给它的帧，再收到 FIN；顶替关闭同理 | AC-007 严格形式（另两种情形：断线由 W5、W6 守着，被顶替的由本条守着） |
| N8 | XO-1～XO-7 各一条（§7 表），外加 XO-1 的「前序超过上限」变体；V9 成立时再加 XO-6 的「阶段 1 有人交付失败 → 后续阶段全部跳过」变体 | AC-012；裁定第 5 条 |
| N9 | 一个不读包的客户端被发送端超时关闭，并触发它的 `lifecycle:disconnected`；同时另一个连接的 CMD 照常在 1 秒内执行并收到回包 | VC-8；AC-013「一个被卡住的连接不影响其他连接的交付与执行器吞吐」；F-21 的「停服之外也不丢关闭回调」 |
| N10 | 传输写入点判据与反例 | F-63 |
| N12 | 登记适配器的映射：`SendExplicitAsync` 的 Required 与 BestEffort 及所有者排除；`SendBatchAsync` 的精确上下文；`SendAsync` 按 ID 取上下文；`RequestCloseExactAsync` 对同 ID 不同上下文不登记关闭 | §5 表 |
| N13 | 登记时完整编码（备选 B），在作业里经登记适配器：一批中最后一条不可编码时，调用点抛出、发件箱里没有这一批的任何条目；经 dispatcher 的真实作业中这样抛出时，CMD 失败，此前登记的条目照常刷出，然后关闭来源；`NotificationBus` 中一个收件人的消息不可编码时，他为 `Failed`，其余照常登记 | 裁定第 1 条；§5.1 |
| N14 | 组装失败的收件人不被重试，下一轮也不会再被组装 | VC-5；RQ-031(d) |

N11 不单设：J1 的新名称由已有的通用反例覆盖（§8.4）。

#### 10.5 数量预期

- **D**：R 类 9 ＋ 4 ＋ 5 ＋ 9 ＝ 27（`NotificationRetryTests`、`NotificationRetryDrainServiceTests`、`NotificationDeliveryTests` 的期限 5 条、`ExecutorSendDeadlineTests`），加上 `OutboundSenderTests.A_batch_that_cannot_be_encoded_sends_none_of_it` 1 条，V2 成立时的 `ExactConnectionCloseTests` 2 条，再加 `PreparedBatchTests` 中不能改写的条数。预计 30～32。Theory 按实际运行的例数计。
- **N**：§10.4 表 13 行，其中 N8 为 8～9 条，N3 至少 3 条。预计 26～31。
- 逐项目写加法，执行时按实际例数写。

#### 10.6 作为证据的口径

本单元的证据一律经真实的执行器、登记适配器与发件箱取得；需要传输的，用真实 `OutboundSender` 加测试传输或真实 socket。F 类替身用例在本单元之后仍是绿的，不能引作本单元任何一条结论的证据（Plan「测试」第 2、3 条）。

### 11. 表征测试与金样本

- **b1 严格形式**（`A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close`，`:65`）开启。
  - 预期：拒绝的两帧都在断开之前到达。第一帧 `01/0004` 的 body 为 `00 02`，第二帧为 `00/01F7`；然后断开。
  - 依据：两帧在补偿 CMD 中登记，来源关闭是同一条 CMD 发件箱里最后一个条目（§3.3），在两帧交付之后才执行，发送管道又确认刷出（§2）。
- **b2 严格形式**（`A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close`，`:115`）开启。
  - 预期：接受进入的 `01/0004`（body 首字节 1）与其后整批状态同步帧都在断开之前到达，没有 `00/01F7`。
- **宽松形式**两条保留不删。它们的断言是严格形式的子集，开启严格形式后仍通过；表征测试按 RQ-080 只增不删，输出行「frames read before the close」预期恒为「2 of 2」或整批。
- **其余表征测试**（a、c～g）与全部金样本不变。
  - VC-1 只改变落线时刻，不改帧内容与单连接内的相对顺序；
  - 跨连接的到达先后不在线上断言（附录 D「口径」）；
  - 有一条需要改，就按停止条件 5 处理。
- **测量**：Required check 14 在表征 a 上记录请求者一次选角登记的字节数。

### 12. 取证预期（先写进 `logs/task-23-violations.txt` 再跑）

- **基准**：TASK-22 的 39 条。评审后新增的 2 条判据用例只读源码，不产生记录。
  - 按类：`ProtocolGateAssertionTests` 4 ＋ `PartyTownProjectionDeliveryTests` 2 ＋ `GameplayWriteAssertionTests` 24 ＋ `CommandBusExecutorTests` 1 ＋ `EffectOperationReplayTests` 1 ＋ 正向对照 7 ＝ 39。
- **本单元的变化**：
  - 新增：N3 的反例（每条 1 条 `write-off-executor`）；`GameplayOutboxTests.Registering_outside_a_job_is_a_program_error`（改为经写断言后 1 条 `write-off-executor`）；N4（1 条 `network-wait`）；
  - 删除：无，被删的用例今天都不产生记录。执行时按基准取证核对一次。
  - 合计：39 ＋ N3 的条数 ＋ 1 ＋ 1。执行前把精确数字与按 kind 的拆分写进日志开头。
- `violations.py` 的期望表加上新的反例类，并认得 kind `network-wait`。

### 13. 变异（Required check 10）

| 变异 | 应当变红 |
|---|---|
| S1 作业结束钩子不在失败时执行，即只在正常结束时刷出 | N6、`GameplayOutboxTests.A_failing_cmd_flushes_committed_and_unscoped_entries_in_order_and_then_closes_its_source` |
| S2 插件恢复 `throw;`，作业体异常重新传到 TouchSocket | b1、b2 严格形式，N6（×10 中至少一次红，Result 写出红的次数）；另加一条确定性的：在测试传输上放慢来源连接的交付，断言关闭在帧之后 |
| S3 刷出放到完成信号之后 | N1 |
| S4 `OutboundSender` 的关连接条目不排队，准入时立即关闭 | `OutboundSenderTests.A_close_request_runs_after_the_frames_registered_before_it`、N7、W7 |
| S5 `DomainTransaction` 回滚时不结束单元 | N5 的回滚断言 |
| S6 单元组装挪到参与者提交之前 | N5 的「组装看到已提交的状态」 |
| S7 只读事务也开单元 | N5 的只读断言 |
| S8 登记适配器按 ID 取不到上下文时抛出 | N2 |
| S9 `EnsureWritable` 恢复为不写取证的旧判定 | N3 的取证断言 |
| S10 删除 RQ-003 检查 | N4 |
| S11～S17 七个 XO 产出点各去掉有序组作用域 | 对应的 N8 用例。每个产出点一项，共 7 项 |
| S18 XO-5 的所有者改为 BestEffort | N8 XO-5 的「所有者失败则跳过后续」 |
| S19 `SendExplicitAsync` 的目标改为 Required | N12 |
| S20 在 Networking 任一其他文件加一处会话发送 | 传输写入点判据（N10） |
| S21 `NotificationBus` 组装失败后再登记一次 | N14 |
| S23 登记适配器不编码，只检查编码器与端点（即备选 A 的形状） | N13 的「最后一条不可编码则整批不登记」；`OutboundSendFailureTests.AFailedLoginResponseIsNotEncodable` |
| S24 登记适配器把位置检查挪到编码之前 | `OutboundSendFailureTests` 的 4 条（证明 §5.1 的次序是它们不改即过的原因；这一项是区分力证明，不是缺陷） |
| S25 XO-6 的阶段条目改回 BestEffort（V9 成立时才做） | N8 XO-6 的「阶段 1 有人交付失败 → 后续阶段全部跳过」变体 |
| S22（抽查）去掉 Game 服务的 `ConfigureSendPipe` | `OutboundRealSocketTests.A_send_pipe_that_does_not_confirm_flushes_is_refused` 不经宿主，所以预计是线级用例因拒绝发送而变红；如实记录 |

### 14. `follow-ups.md` 与处置记录

- **关闭**：
  - F-20（W7 与 b1、b2 严格形式开启；RQ-008 中「收包回调间接等待交付」随直发消失）；
  - F-22（排空服务删除）；
  - F-23（执行器不再等待任何帧）；
  - F-24（三处分离点复核，见 §2）；
  - F-63（传输写入点判据列为豁免）；
  - F-64（§8.1）；
  - F-65（§6）；
  - F-83（N2）；
  - F-14 的「T-006 之后消失」一半落实。
- **更新**：
  - F-21：停服之外的关闭回调由 N9 覆盖；停机排空的兜底拆除与 `TryRemove` 仲裁保留不改；
  - F-69：写本单元的耗时对比；
  - F-87：本单元新出现、名字里提到旧机制的用例。
- **转 TASK-24**：F-25、F-84、附录 F 的队伍两把投递门、§9 表中「TASK-24 要做的」一栏。
- **转 TASK-25**：F-61、替身清点（Required check 13）、人群项出站测量。
- **新增**：执行中发现的事项，从 F-97 起编号。
- **`t006-test-dispositions.md`**：
  - 开头写日期、基准与分类（§10.2）；
  - TASK-23 一节写数量与加法、删除（每条带承接者或规格消除理由）、改写（旧断言 → 新断言 → 保护意图）、保留、新增。

### 15. 生产行为为什么只按附录 G 变化

- **帧内容与单连接顺序**：
  - 消息对象与编码器都不变；
  - 每连接队列按提交顺序排空（RQ-032）；
  - 一条 CMD 的条目按登记顺序提交，CMD 之间按完成顺序提交，因为刷出在完成信号之前，而执行器是单消费者。
  - 所以单连接上的帧序等于今天「顺序 await」的帧序。表征 a、c～g 的金样本是这一点的证据。
- **跨连接顺序**：附录 D 的七项由有序组保持（§7）；其余写法本来就允许乱序。
- **落线时刻**：从「各段之间」变为「CMD 完成后」，即 VC-1。
- **失败路径**：
  - 传输失败从「抛给 CMD」变为「关闭该连接，再由生命周期 CMD 清理」，即 VC-3、VC-4、VC-8 与附录 E 各行的目标（§9）；
  - 重试消失，即 VC-5；
  - 回滚单元内的消息不再发出，即 VC-2，由 §4.3 给出今天的数目。
- **编码失败**：不变（裁定第 1 条，备选 B）。编码仍在调用点、在执行器上整批完成，失败照旧在调用点抛出，CMD 失败，然后断线；异常类型与消息文字沿用今天的写法。差别只在断线的时机：来源关闭排在这条 CMD 此前已登记的条目之后（RQ-031(c)），不再越过它们。
- **证据**：
  - 表征测试除 b1、b2 严格形式开启外不变；
  - 正式全量与 TASK-22 相比只有 §10 记录的增删与改写；
  - 取证中反例类与对照以外为 0；
  - 变异 S1～S25。

## 后续单元边界提纲（TASK-24、TASK-25，不在本单元执行）

### TASK-24 — T-006b：附录 E 逐项承接，删除队伍的两把投递门

- **为什么单列**：
  - 每一行都要判断「哪段代码只为传输失败存在」，以及「它的保护意图由什么承接」，判断量大；
  - 改动分散在选角、进本、离开副本、两塔、怪物死亡、祭坛、清算、队伍、配额、GM Cera、房间机制、生物重命名、回选角、初始连接等十几个切片；
  - 与翻转分开，审查时才能分清「语义改变」和「死代码清理」。
- **进入条件**：TASK-23 已提交；§9 表核对无误。
- **生产范围**：
  - 附录 E DR-1～DR-12、DR-16、DR-17 的旧失败分支，只删「只被传输失败触发」的部分。CMD 内的命令失败、组装失败、提交不确定的处理原样保留（RQ-023(c)、RQ-031(d)、Plan R-5）。点名：`DungeonEntryProjectionDelivery` 的 `IsolatePartialCohortAsync` 与传输失败触发的 `RollbackEntry`，两塔的 `SendToExactAsync` 失败集合与 `CloseQuietlyAsync`，`SelectCharacterHandler` 的发送失败补偿，`LeaveDungeonSlice` 的同伴发送 `catch`，等等。
  - GM Cera 的结果改为「已提交发送／不在线」（VC-6，F-25），组装失败时仍为 unconfirmed（Plan R-5）。
  - 删除队伍的两把投递门：`PartyNotificationSlice` 的按角色信号量与 `PartyDeliveryStream.Gate`，以及 `PartyTownProjectionDelivery` 中的取门。`PartyExactNotificationDelivery.PublishWithRetryAsync` 里「在 CMD 内再试一次组装」保留（Plan R-5）。
  - 删除 `GameplayGateAssertion` 的门成员（`Enter`／`Entering`／`WouldWait`、`gate-*` kind），把取证源 `DiagnosticSourceName`／`Report` 挪到一个只做取证的类型，写断言、泄漏流断言与 RQ-003 检查共用（F-84）。
  - 判据：J1 加入 `PartyDeliveryStream`、`PartyParticipantGate`、`GameplayGateAssertion`；J2 名单删去 `PartyNotificationSlice.cs`、`GameplayAssertions.cs`。至此附录 F「删除」栏投递三项在 `Server/src` 检索为零，AC-017 的「检索为零」一半满足。
- **测试范围**：
  - 附录 E 每一行一条经执行器与发件箱驱动的证据（AC-014）。会抛异常的替身用例改为在测试传输上制造交付失败，或删除并写承接者；
  - AC-011 的其余几条：祭坛组装失败封锁并关闭全部参与者；怪物死亡提交后失败时每个参与者既被封锁、连接也被关闭；选角发布后失败走补偿时同伴收到清理通告（先核对 b2 的同伴金样本是否已经覆盖）；
  - VC-3、VC-4、VC-6 的证据；
  - 发送端层用例（第 1 轮评审后主控方处置第 1 条）：两塔进入（XO-3、XO-4）时一名成员交付失败，只关闭该成员，其余成员与请求者照常进入（VC-3，DR-3）；
  - `ProtocolGateAssertionTests` 4 条与 `PartyTownProjectionDeliveryTests` 2 条反例删除（K 类，规格消除）；
  - 各程序集的正向对照改用写断言（F-84）。
- **取证预期**：39 ＋ TASK-23 的增量 － 6（队伍门反例）。正向对照的 kind 由 `gate-off-executor` 变为 `write-off-executor`。
- **检查**：一次正式全量加取证，反例类外 0；变异核对每行至少一项（删掉保留下来的非传输失败处理，对应用例应当变红）；表征测试与金样本不变。
- **Result review**：required。

### TASK-25 — T-006c：改写「发送即记录」替身，T-006 收口

- **为什么单列**：量大而机械，约 57 个文件、69 个替身类（TASK-23 Required check 13 的清单）。与 TASK-24 的判断性改动分开，`diffcheck` 才能证明「只换了驱动方式、断言不变」。
- **进入条件**：TASK-24 已提交。
- **范围**：
  - 清单中的替身改为记录发件箱条目或发送端结果，经执行器运行（Plan「测试」第 3 条）。断言多重集合不变；无法改写的写明理由，交 T-008 随功能簇改写（RQ-021，handler 归并之后这类用例会整体重写）。
  - 人群项的出站实测（`selection-outbound-bound.md`「未计入」一节）：城镇在场、PvP 大厅、放闸后的成就与称号投影，核对 2 倍余量。
  - F-61：T-006 之后的 Host 取证收口复核。
  - 新建 `verification/t006-closure.md`，仿 `t005-closure.md`：
    - Plan T-006 行逐项；
    - 附录 E 十七行与附录 G VC-1～VC-6、VC-8 逐行对账（AC-014、AC-017）；
    - AC-007、AC-011、AC-012、AC-013 的状态；
    - RQ-061 两半都已开启；
    - M-2 的进入条件。
- **检查**：一次正式全量加取证；变异抽查（每类替身改写挑一个文件，回退驱动方式应当变红或明示不变的理由）。
- **Result review**：required，因为收口是 M-2 的依据。

**T-006 完成的条件**（三个单元合起来）：
- Plan T-006 行逐项有证据；
- 投递三项与本单元删除的名称在 `Server/src` 检索为零；
- `ExecutorSendDeadline` 为零，2 秒只在 `OutboundLimits` 两个约定位置；
- 作业内没有直接传输写入，由运行期检查与传输写入点判据守着；
- 附录 E 每行有经执行器与发件箱的证据；
- b1、b2 严格形式开启并通过；
- 每个单元一次正式全量，取证反例类外为 0。

## 待裁定

1. **消息编码失败在什么时候、以什么形式暴露**（§5）。
   - **已按裁定第 1 条处理：采用备选 B**，见 §5.1、§10.3（`OutboundSendFailureTests` 不改）、N13、S23、S24、§15。下面是 Revision 1 的原文。
   - 今天编码在发送器里、在调用点同步完成，任何编码问题都在调用点抛出，CMD 失败，然后断线（DR-15）。T-003 把完整编码放在发送端的准入循环里（RQ-034「整批编码……在发送端保持」）。
   - **建议 A**：
     - 登记时只做「有没有编码器、端点是否相符」两项同步检查。它们是程序错误，照旧在调用点抛出；
     - 消息体的编码失败在发送端变为 `EncodingFailed`：整批不发、记 Error、关闭该连接，CMD 本身不失败；
     - 客户端看到的都是断线。服务器侧的差别是：编码失败的那条 CMD 不再因此中断，它此后的工作照常完成。
     - 这是附录 E 没有列出的一项依赖（`OutboundSendFailureTests.AFailedLoginResponseIsNotEncodable`、`PreparedBatchTests` 的晚期编码失败），按附录 E 末句应补一行（例如 DR-18「消息体编码失败 → 整批不发、关闭该连接」），**属于改 Spec**。
   - 备选 B：
     - 登记时在执行器上完整编码，发送端只接收编码好的帧。今天的语义原样保留，执行器上的耗时与今天相同（今天编码也在作业里），不需要补附录 E。
     - 代价：改 T-003 的共享契约（`OutboundEntry` 从类型化消息变为帧），以及 RQ-034「在发送端保持」的字面。
   - 这条决定 `OutboundSendFailureTests` 4 条的处置与 N13，请确认。
2. **「CMD 此前登记的消息」的范围：按连接，还是整条 CMD**（RQ-012、RQ-031(c)、AC-007）。
   - **已按裁定第 2 条处理：按连接计**，Result 中登记这一解读。下面是原文。
   - RQ-012 写「要等该 CMD 发件箱里此前登记的消息都交付之后，才执行关闭」。字面可以读成两种：
     - 关闭连接 X，要等此前登记给 X 的消息交付；
     - 要等此前登记给任何连接的消息交付。
   - T-003 实现的是前者：关连接条目排在该连接的队列里。
   - **建议按连接**。理由：
     - 保护意图是「被关闭的客户端在断开之前收到给它的帧」，AC-007 的线级断言也正是这一点；
     - 关闭 X 不影响其他连接已经登记的帧；
     - 需要跨连接先后的，已经由附录 D 的有序组表达。
     - 按整条 CMD 读，任何一个慢连接都会推迟其他连接的关闭，最多一个等待上限，而且没有对应的保护意图。
   - 这是对 Spec 措辞的解读，请确认。确认后在 `t006-closure.md` 里写明。
3. **TASK-23 到 TASK-24 之间的过渡状态**（§0、§9）。
   - **已按裁定第 3 条处理：接受**。Result 中逐条列出注入抛异常替身、只证明死代码的用例，交 TASK-24。下面是原文。
   - TASK-23 提交之后、TASK-24 完成之前：
     - 附录 E 若干行的旧失败分支成为死代码，生产不再进入；
     - 注入「会抛异常的替身」的用例仍然是绿的，但只证明死代码。
   - 生产行为在这期间已经是附录 E 的目标语义，没有中间态的错误行为。
   - **建议接受**，并在 TASK-23 的 Result 中列出这些用例的清单，作为 TASK-24 的输入。
   - 备选：把 TASK-24 并入本单元，代价是本单元的判断量翻倍，审查时语义改变与死代码清理混在一起（`RefactorPreferences` §3.2 的「证据互不混淆」）。
4. **`NotificationOutcome.Delivered` 改义不改名**（§5）。
   - **已按裁定第 4 条处理：只改文档注释**，改名交 T-008。下面是原文。
   - 翻转之后，它表示「已登记，提交发送」，不再表示「已交给传输」。读它的生产文件有 12 个：配额刷新 7 个、队伍投影、祭坛、GM Cera、房间机制，以及 `UsageQuotaProjection`。附录 E 的 DR-5、DR-6 正要求「刷出时推进、刷出时记录」，改义恰好就是目标。
   - **建议只改 `Abstractions/Notifications/INotificationBus.cs` 的文档注释，不改名**，避免 12 个文件的机械改名与语义改动混在一起。是否改名为 `Submitted`，交 T-008 随功能簇决定。
   - 这是 Abstractions 共享契约的含义改变，请确认。
5. **XO 组内默认 BestEffort**（§7）。
   - **已按裁定第 5 条处理**：默认 BestEffort，XO-5 所有者 Required；XO-6 按 §1 V9 核实，规划时读码已表明今天「有人失败就停止推进后续阶段」，所以阶段条目标为 Required，不改 Spec（§7、N8、S25）。标 Required 带来的细差见「修订中新发现、待裁定」第 2 条。下面是原文。
   - 附录 D 写的是先后，没写组内某个成员失败之后怎样。本计划取：
     - 组内默认 BestEffort：失败者被关闭，其余成员照常交付，与 VC-3、DR-5 的承接方式一致；
     - 只有 XO-5 的所有者是 Required（DR-10）。
   - 对 XO-6，这意味着阶段 1 中有一名收件人交付失败时，阶段 2 照常发给其余成员。今天若是「有人失败就不进入下一阶段」，这里就是一处行为变化。执行时读 `PartyNotificationSlice.cs:798-842` 确认今天的做法，并写进 Result。
   - **建议接受**：失败者被关闭，重连时选角工作流会下发完整的队伍状态（DR-5 的承接理由）。
   - 若今天确实在失败时停止推进，请确认是否把它作为附录 G 的新增一条，**属于改 Spec**；或改为把该阶段的条目标为 Required，以保持今天的行为。
6. **顶替关闭移进登录 CMD**（§6，F-65）。
   - **已按裁定第 6 条处理**：本单元一并改 `spec.md` 附录 A 第 4 行（§6.1），Owned boundary 已加这一处，停止条件 3 对它不适用。下面是原文。
   - 附录 A 第 4 行写顶替后的关闭是 `lifecycle:close-superseded` 这条生命周期 CMD。本计划把它改为「顶替发生的那条 CMD 发件箱里的关连接条目」，同时把被顶替连接的在线身份移除挪进同一条 CMD。这是 F-65 与 Plan T-006 行「关连接都改为登记发件箱」的要求。
   - 后果有两条：
     - 附录 A 第 4 行与附录 A 的对账表（`entry-reconciliation.md` §2）要改一个 CMD 类型；
     - 在线身份的移除比今天早一个作业。今天由 `IsSuperseded` 挡住了这段时间内的请求，所以客户端看不到差别。
   - 关闭由 CMD 登记之后，它引起的生命周期 CMD 按 RQ-050 应继承登录 CMD 的调用链，归 T-007 落实。
   - **建议接受**，附录 A 的文字随 T-009 的最终对账一并改。改附录 A 属于改 Spec，请确认。


## 修订中新发现、待裁定（Revision 2）

1. **编码排在位置检查之前，留下一个很窄的取证缺口**（§5.1）。
   - 为了让 `OutboundSendFailureTests` 4 条按裁定不改，登记适配器先编码、后做 RQ-061 的位置检查。这 4 条在测试线程上调用发送器，断言的是编码错误。
   - 后果：执行器外的调用若消息恰好不可编码，抛出的是编码错误，不写 RQ-061 的取证。只要消息可编码，执行器外的调用照样被拒绝并写取证。两种错误都是程序错误，调用照样失败；生产中执行器外的调用为 0（§1 V1）。
   - **建议接受**，在 Result 中登记这一处缺口。
   - 备选：位置检查在前，把这 4 条的发送调用包进 `TestGameplay` 作业。断言不变，但方法体要改一行，与裁定第 1 条的「4 条不改」不符。
2. **XO-6 标为 Required 后，失败那一阶段里排在失败者之后的收件人也收不到这一阶段**（§7）。
   - T-003 的有序组只有「一条 Required 失败，组内其后全部跳过」这一种规则，没有阶段边界。
   - 今天（§1 V9）：同一阶段内每个收件人都会尝试，有人最终失败才停止后续阶段。所以今天排在失败者之后的同阶段收件人仍会收到这一阶段。
   - 影响：失败者的连接被关闭，随后他的 `lifecycle:disconnected` 清理会给其余成员发清理通告与队伍投影（XO-5），其余成员由此拿到当前的队伍状态。差别只在那一条 CMD 内、失败阶段的后半个收件人名单。
   - **建议接受**，作为保持今天行为的近似，在 Result 中登记。
   - 备选：给发送端的有序组加「阶段」语义：阶段内 BestEffort；跨入下一阶段之前，检查上一阶段有没有 Required 失败。这能逐字保持今天的行为，代价是改 T-003 的发送端与它的用例，还要多一组变异。
3. **`verification/entry-reconciliation.md` §2 也写着 `lifecycle:close-superseded`**。
   - 它是 TASK-16 在基准 `c3115f9a` 上做的对账记录，带日期。按 AGENTS.md「过期的记载怎么办」，这类记录保留原文，不在原处改写。
   - **建议**：本单元不改它，在 `follow-ups.md` 新增一项，由 T-009 做附录 A 最终对账时，在那份记录开头补落地状态。
   - 备选：本单元就在它开头补一段落地状态。这会扩大本单元的文档改动，与裁定第 6 条「只改附录 A 第 4 行」的范围不完全一致。

## 主控方裁定（执行前）

0. **拆分为 TASK-23（投递翻转）→ TASK-24（附录 E 逐项承接、删除队伍投递门）→ TASK-25（改写替身并收口），接受。**
1. **编码失败的时机：采用备选 B，登记时完整编码。** 理由：
   - 保留今天的语义：编码失败仍在调用点抛出、CMD 失败。`SelectCharacterHandler`「发送前验证必达消息可编码、失败时派发补偿」这条工作流的前提不变。
   - 不需要给附录 E 补行，不改 Spec。
   - `OutboundEntry` 是 T-003 的内部契约，目前还没有接线，改它的代价小。
   - 编码在执行器上完成，耗时算进作业，按用户口径只记录。
   - 计划中按 A 写的段落（§编码检查、`OutboundSendFailureTests` 的处置、N13、可见差异「编码失败」一条）按 B 改写：`OutboundSendFailureTests` 4 条不改。
2. **「此前登记的消息」按连接计，接受。** 跨连接的先后只由附录 D 的有序组表达。这是对 RQ-012、RQ-031(c) 的解读，在 Result 中登记。
3. **TASK-23 与 TASK-24 之间的过渡状态，接受。** 注入抛异常替身的用例仍是绿的，但它们只证明死代码；Result 中逐条列出，交 TASK-24。
4. **`NotificationOutcome.Delivered` 改义不改名，接受。** 只改文档注释，改名交 T-008。
5. **XO 组内默认 BestEffort，只有 XO-5 的所有者为 Required，接受。** 若读码确认 XO-6 今天「有人失败就停止推进下一阶段」，**不改 Spec**，改为把该阶段标为 Required，保持今天的行为，并在 Result 中写明。
6. **顶替关闭移进登录 CMD 的发件箱（F-65），接受**（方向已由 TASK-10 定下）。附录 A 第 4 行的 CMD 类型随之变化：本单元一并改 `spec.md` 附录 A 第 4 行，按事实性更新处理，不留到 T-009。Owned boundary 增加这一处，停止条件 3 对此处不适用。

## 主控方裁定（Revision 2 新发现）

1. **编码排在位置检查之前留下的取证缺口，接受。** 执行器外调用的消息恰好不可编码时，抛出的是编码错误而非 RQ-061 取证；生产中执行器外调用为 0，消息可编码时照样被拒并写取证。在 Result 中登记，并在 `follow-ups.md` 记一项交 T-009 复核。
2. **XO-6 标为 Required 后的细差，接受为保持今天行为的近似。** 差异只出现在投递失败的路径上：失败阶段里排在失败者之后的收件人收不到这一阶段，而失败者的连接随后被关闭，断线清理会给其余成员补发清理通告与队伍投影。在 Result 与收口中登记为已接受的差异；给发送端有序组加阶段语义登记为后续事项（T-008 判断是否需要）。
3. **`entry-reconciliation.md` §2 本单元不改，接受。** 在 `follow-ups.md` 记一项，由 T-009 做附录 A 最终对账时在它开头补落地状态。

## Result（第 1 轮，合同 Revision 2，2026-10-23，基准 `a4422480`，未提交）

### Outcome

done（待独立结果审查，Result review：required）。

- 执行器上的发送、关连接与即时投影改为登记作业的发件箱，作业结束时（完成信号之前）交给接上的 `OutboundSender`；编码在登记点完整完成（裁定第 1 条，备选 B）。
- 可写事务开工作单元：提交后组装、回滚与结果不确定丢弃；作业体失败时按 RQ-031(c) 刷出存活条目再关闭来源，传输回调不再关连接。
- 直发路径、直发期限、重试队列与排空服务、63 个 `retryLimit`、顶替关闭通道与 `lifecycle:close-superseded`、`TouchSocketNetworkConnectionCloser` 全部删除；七个 XO 产出点各用一个有序组（XO-5 所有者与 XO-6 阶段为 Required）。
- 正式全量 8191 条，失败只有那 2 条 `DimensionDungeonTests`；跳过 20 → 18；表征 10 通过、0 跳过；取证 46 条，反例类与对照以外 0。
- 停止条件 2 被 V10 命中（两塔进入在交付失败时的可观察行为改变），第 1 轮执行时没有停下；第 1 轮评审 F1 指出后，按主控方处置（第 1 轮评审后）第 1 条扩展 VC-3 处理（Why 1、Why 4）。其余停止条件没有触发。与计划或裁定不同之处见 Why 2～13。

### Changed

- **生产代码**：41 个文件，全在 Owned boundary 的四个项目里：Abstractions 6 ＋ Application 16 ＋ Networking 11 ＋ Protocol 8 ＝ 41；其中修改 35、删除 5、新建 1（`Application/Execution/Outbox/OutboxAdapters.cs`）；841 行增、1027 行删（`logs/task-23-boundary.txt`）。
  - **接线**（`NetworkingModule.cs`）：注册 `INetworkConnectionContexts`（`TouchSocketConnectionRegistry` 实现）、`OutboundSendOptions.Default`、`IOutboundTransport` → `TouchSocketOutboundTransport`、`IOutboundMessageEncoder` → `RegistryOutboundEncoder`、`IOutboundSender` → `OutboundSender`；Game 与 Channel 两个 `TcpService` 的配置都经 `OutboundTransportOptions.ConfigureSendPipe`；不再注册 `INetworkMessageSender` 与关连接器。
  - **作业结束钩子**（`Abstractions/Execution/GameplayJob.cs`、`Application/Execution/GameplayExecutor.cs`）：令牌上的钩子表 `GameplayJob.OnEnd`；执行器在作业体结束后、`ExitJob` 与完成信号之前执行，钩子收到 `GameplayJobEnd(Failed, Source)`；`GameplayJob.EndedAJob(error)` 让传输回调分辨「作业体异常已由发件箱处理」。
  - **来源连接**：`GameplayWork.Source`；dispatcher 与 `lifecycle:connected` 填写，`lifecycle:disconnected` 不填。
  - **发件箱**（`GameplayOutbox.cs`）：`Current` 经 `GameplayWriteAssertion.Writing("GameplayOutbox.Register")`（RQ-061 发件箱一半，F-64）；第一次登记时挂刷出钩子；有序作用域 `OpenGroup`（嵌套作用域并入外层并在退出时恢复交付等级）；`GameplayOutboxes.RunAsync` 包装删除。
  - **工作单元**（`DomainTransaction.cs`、`DomainTransactionScope.cs`）：可写事务在执行器上开启时 `BeginUnit()`；参与者全部提交后、after-commit 回调前组装；组装异常保留、回调照跑、然后上抛；回滚 → `RolledBack`；非可失败参与者违约或回滚失败 → `Uncertain`。只读与快照不开单元。
  - **登记适配器**（`OutboxAdapters.cs`）：`OutboxNetworkMessageSender`（取上下文 → 编码 → 位置检查 → 登记；连接已不在 → 位置检查后静默丢弃并记 Debug，F-83；`SendExplicitAsync` 直接收件人一条 Required、每个目标一条 BestEffort、排除所有者、目标编码失败只跳过该目标）、`OutboxConnectionCloser`（精确关闭只在注册表里仍是同一上下文时登记）、`GameplayOutboxOrdering`。在 `AddGameplayExecution` 注册。
  - **契约**（`GameplayOutbound.cs`）：`OutboundEntry` 携带编码好的帧（拷贝、不能为空）；新增 `IOutboundMessageEncoder`、`IOutboundOrdering`／`IOutboundOrderingScope`；`OutboundOutcome.EncodingFailed` 删除。`OutboundSender` 去掉编码与 `EncodingError` 分支，实现 `IDisposable`。
  - **RQ-003**：`GameplayNetworkWaitAssertion.Touching`（kind `network-wait`）在 `OutboundConnection.SendAsync`／`CloseAsync` 入口。
  - **通知总线**（`NotificationBus.cs`）：逐收件人 `IsStillHere` → 组装 → 经发送器逐条登记，`Delivered` 即已登记；组装或编码失败为 `Failed`、记 Warning、不重试；空人群之外先触碰一次发件箱（RQ-061）。`NotificationDeliveryOptions`、重试队列、排空服务、`RetryLimit` 删除；`INotificationBus` 的 `Delivered` 与 `Failed` 文档注释改写（Why 5）。
  - **顶替关闭**（`LoginSessionRegistry.cs`、`LoginSessionRuntime.cs`）：`CloseRequests` 删除；`Participant()` 登记 after-commit 回调，提交后 `TryDisconnectConnection`、再 `closer.RequestCloseAsync`（进同一条登录 CMD 的发件箱）；维护服务只剩过期扫描。
  - **传输回调**（`NetworkPlugins.cs`）：作业体异常记日志后返回，不再 `throw;` 给 TouchSocket；入队被拒等未登记过东西的失败照旧由传输关闭。
  - **XO-1～XO-7**：`DungeonEntryProjectionDelivery`、`LeaveDungeonSlice`（EPLP 处理器）、`BossTowerProjectionDelivery`、`IllusionTowerProjectionDelivery`、`CleanupNotificationDelivery`（所有者 Required，其后改 BestEffort）、`PartyNotificationDelivery`（阶段 Required）、婚礼调度器，各包一个 `ordering?.Begin(...)`；`IOutboundOrdering` 以可选构造参数注入。
  - **删除**：`TouchSocketNetworkMessageSender.cs`、`ExecutorSendDeadline.cs`、`TouchSocketNetworkConnectionCloser.cs`、`NotificationRetryQueue.cs`、`NotificationRetryDrainService.cs`；`ProtocolModule.cs` 的 63 个 `retryLimit` 实参与只为重试存在的注释。
  - **编译迫使、Owned 列表之外**：`TouchSocketTransportDrain.cs`（内联原关连接器的精确关闭）、`OutboundLimits.cs`（一行文档）、`NotificationComposer.cs`（一行注释）。
- **Spec**：`spec.md` 附录 A 第 4 行「新的 CMD」一栏按 §6.1 改写；附录 G 的 VC-3 扩展到两塔进入、依据栏补 DR-3（第 1 轮评审后，主控方处置第 1 条）。共 2 行（全文在 `logs/task-23-boundary.txt`）。
- **测试**（`verification/t006-test-dispositions.md` TASK-23 一节）：变动 38 个已有文件（Application 16 ＋ Host 2 ＋ Networking 14 ＋ Persistence 1 ＋ Protocol 5 ＝ 38），其中修改 30、删除 8；新建 8 个（Application `Execution/Outbox/OutboxWorkUnitTests.cs`；Networking `Integration/OutboxExplicitDeliveryTests.cs`（改名承接）、`Integration/OutboxWireTests.cs`、`Outbound/OutboxRegistrationHost.cs`、`Outbound/OutboxRegistrationTests.cs`、`Outbound/TransportWriteSourceTests.cs`；Protocol `Outbox/XoDelivery.cs`、`Game/BossTower/BossTowerEntryOrderTests.cs`）。
- **文档与证据**：新建 `verification/t006-test-dispositions.md`；`verification/follow-ups.md`（关闭 F-20、F-22、F-23、F-24、F-63、F-64、F-65、F-83，F-14 一半；更新 F-21、F-69、F-87；F-25、F-84 转 TASK-24，F-61 转 TASK-25；新增 F-97～F-105）；`verification/logs/task-23-*.txt` 13 份。

### Why

1. **停止条件**：
   - 1：V1 在基准上与计划一致——127 个生产文件（Abstractions 1 ＋ Application 3 ＋ Networking 3 ＋ Protocol 120），执行器外的调用方只有测试；`TouchSocketTransportDrain` 用的是具体关连接器类型而非接口（V2），内联后删除（`logs/task-23-verify.txt`）。
   - 2：V3 非零 `retryLimit` 2 处（`:452` 钱包、`:604` PvP），V4 63 处，V6 `CloseRequests` 只在两个登录会话文件，V7 基准 3 个写入点、结束 2 个，V8 三处 `throw;`；V9 见 Why 3。**V10 命中了停止条件 2**：XO-3、XO-4 在基准上的失败处理与 §1 的描述不同，翻转后两塔进入在交付失败时的可观察行为随之改变（Why 4）。第 1 轮执行时没有按停止条件停下回报，这是错的；第 1 轮评审 F1 指出后，主控方在「主控方处置（第 1 轮评审后）」第 1 条裁定接受新行为，并扩展 `spec.md` 附录 G 的 VC-3 覆盖两塔进入，此处按该裁定处理。
   - 3：生产改动只在四个项目（Why 6 的三处文件在项目内、不在逐文件列表里）；`spec.md` 只改附录 A 第 4 行（裁定第 6 条）与附录 G VC-3（第 1 轮评审后主控方处置第 1 条，Owned boundary 随之增加这一处），`plan.md` 未改。
   - 4：第一、第三次正式全量没有新失败；第二次的 1 条新失败取到了身份，属于 §10.2 的 S 类（Why 13）；取证三次都没有名单外记录。
   - 5：表征测试只去掉两处 `Skip`，金样本未改；b1、b2 严格形式 ×10 全过。
   - 6：每条删除都有承接者或规格消除理由（`t006-test-dispositions.md` §2）。
   - 7：§4.3 静态清点与探针都没有找到会回滚的单元内登记（Checks 7）。
   - 8：七个 XO 产出点的发送都在一条 CMD 内。
2. **裁定第 2 条的解读**：「此前登记的消息」按连接计；关连接条目排在该连接自己的队列里，跨连接先后只由有序组表达。
3. **V9 成立，XO-6 标 Required**（裁定第 5 条）：基准上 `realtime`、`acceptance`、`endpoints`、`roster`、`replay`、`observer` 每一阶段之后都是 `HasFailures → return false`；`PublishWithRetryAsync` 在同一阶段内对每个收件人各尝试、失败的再试一次；调用方得到 `false` 不推进 `LatestRevision`。细差（失败阶段里排在失败者之后的收件人收不到本阶段）按 Revision 2 裁定第 2 条接受，登记 F-98。
4. **V10 与计划的描述不同（XO-3、XO-4），命中停止条件 2，按主控方处置（第 1 轮评审后）第 1 条处理**：
   - 计划写「收集失败、继续，最后关闭失败者」；基准读码是成员循环中第一次发送失败即中止其余成员的发送，`CloseQuietly` 全部隔离成员（含请求者），再上抛。
   - 翻转之后发送不再抛出：一名成员交付失败时，只有他被发送端按 RQ-036 关闭，其余成员与请求者照常进入，被关闭者由生命周期 CMD 清理。这是客户端可观察的行为变化，第 1 轮执行时把它当作「不影响交付等级」而没有停下，判断有误。
   - 裁定：接受新行为。依据是附录 E DR-3 的保护意图「失败者不留在塔局里」与新承接方式「RQ-036 关连接，生命周期 CMD 清理」；VC-3 已对组队进本接受同类改变。`spec.md` 附录 G 的 VC-3 已改为覆盖组队进本与两塔进入，依据栏补 DR-3（主控方授权的 Spec 修订，外部审核时重点说明）。
   - 证据：本单元的 XO-3、XO-4 用例（慢成员先于请求者交付）；「一名成员交付失败、其余照常进入」的发送端用例写进 TASK-24 提纲。旧分支（`deliveryFailure`、`CloseQuietlyAsync`）照旧由 TASK-24 删除。
   - 其余 XO：XO-1 隔离、回滚并抛出；XO-2 捕获后继续；XO-5 所有者失败即抛出；XO-7 `ProjectAsync` 捕获后继续。
5. **`INotificationBus` 多改了 `Failed` 的文档注释**：原文写「会重试」，重试已删除，留着就是假话；`Delivered` 按裁定第 4 条改义不改名。
6. **Owned 列表之外的三处生产改动**：`TouchSocketTransportDrain.cs`（删关连接器的编译后果）、`OutboundLimits.cs`（一行文档点名了删除的类型，§6 检索式要求为 0）、`NotificationComposer.cs`（一行注释提到重试队列；计划写 Notifications「改三个」，实际改了四个）。
7. **`NotificationBus` 经发送器逐条登记**，不是计划 §5 表的「每收件人一条 Required 批次」：第一次按计划直接登记时，约 240 条注入发送器替身的 Application 用例绕过发件箱而失败；改为经 `INetworkMessageSender.SendAsync` 后，替身照旧收到调用，生产路径仍是登记适配器。代价是一个收件人的后一条消息不可编码时，前一条已登记（登记 F-102，交 TASK-25 与替身改写一起改）。
8. **删除比计划多**：计划 D 预计 30～32，实际 43。多出的是 `DirectSendDeadlineRealSocketTests` 2、`DetachedCloseTests` 2（被测的期限与关连接器已删）、`NotificationRetryDrainExecutorTests` 2、`TimerSourceReconciliationTests` 的排空服务一行、`NotificationHandlerArchitectureTests` 的 PvP 重试声明 1、`LoginSessionMaintenanceExecutorTests` 的顶替关闭 1（由新用例承接），以及改名承接的 3 条；逐条承接见 `t006-test-dispositions.md` §2。`PreparedBatchTests` 5 个方法全部改写，没有删除。
9. **`EffectOperationReplayTests` 两条未改**（计划 §10.3 预定改写）：替身的回调在发送调用点执行，生产中这一点就是登记点，仍在作业里、主命令之后，断言不变即成立（F-101）。
10. **`Persistence.Tests` 改了一个文件**（测试 Owned boundary 只列了 Application、Protocol、Networking、Host 与 `tests/Support`）：`LoginSessionReconnectTests` 断言 `CloseRequests` 的读端，成员删除后编译不过；改为注入记录关连接器，断言同一件事。`Host.Tests/GmPlayerDirectoryHttpTests` 的夹具另注册一个不带关连接器的 `LoginSessionRegistry`，原因相同（顶替清理移进登录 CMD）。
11. **改写后的重置客户端用例改为 15 × 64 KiB**：第一次正式全量与隔离复跑中，它的 64 × 64 KiB 超过 1 MiB 队列上限，5 个连接都先被发送端按 `OverLimit` 关掉，客户端的重置没有被走到。改为 15 × 64 KiB 后，10 次隔离运行中每个连接都在发送端仍在写时被重置，结果为 `Failed`。改动之后重跑了正式全量（Checks 2 记的是最终那次）。
12. **取证缺口与 b1 的区分力**：编码先于位置检查（裁定 Revision 2 第 1 条），执行器外调用的消息恰好不可编码时不写取证（F-97）；变异 S2 下 b1 严格形式 0／10 次变红，区分力由 N6 承担（F-100）。
13. **第二次正式全量中的新失败**：`Host.Tests/GmEventsHttpTests.Durable_clear_command_decoder_dispatcher_claim_and_online_close_refresh_the_native_frames` 失败 1 次，隔离 10 次中失败 8 次（`Assert.Single` 找不到 GM 停用活动之后的 `0x006C` 日历帧，帧集合为空）。按 systematic-debugging 取了身份：
    - 临时诊断（用后还原）显示 `DrainOnceAsync()` 返回 0、会话仍在世界里、500 ms 之后 11 帧全部到达：LiveOnly 投递已被派发组循环取走，取走它的作业还在执行器上组装，用例就去读帧了；
    - 同一诊断在基准 `a4422480` 的副本上也是 `drained=0`，8 次中 5 次「读帧时为 0、500 ms 后为 11」，而原样用例在基准上 10／10 通过。所以竞争在基准上就存在，本单元改变了输赢比例；具体是哪一段变慢没有测量。
    - 处置：用例在两处「`DrainOnceAsync()` 之后读帧」前各加一次执行器屏障（入队一个空作业并等它完成），断言不变（`t006-test-dispositions.md` S14）；改后隔离 10 次、整类 3 次全过，第三次正式全量通过。同一写法在其他 12 个文件里，登记 F-104。这条按 §10.2 的 S 类处理（断言依赖「发送在调用之内完成」），没有按停止条件 4 停下。
14. **TDD 的口径**：判据（`TransportWriteSourceTests`、J1 新名称）走了 RED → GREEN；其余新用例在生产改动完成后写出即通过，区分力由变异 S1～S25 证明，不是 RED。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误（三次正式全量之前各一次，`logs/task-23-build-test.txt`；变异之后一次，`logs/task-23-mutations.txt`）。
2. **正式全量**（`logs/task-23-build-test.txt`）。一共跑了三次：第一次之后改了重置客户端用例（Why 11），第二次中 `GmEventsHttpTests` 的一条失败、查明是用例与派发组循环的竞争并加了执行器屏障（Why 13），下面记第三次，也就是最终代码上的那次（2026-10-23T03:11:06Z～03:14:48Z，测试部分 222 秒，开始时 swap 已用 9561 MB；只作信息）。
   - **总数**：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1709 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 178 ＋ Application 4532 ＋ Host 191 ＝ 8191 ＝ 8194 ＋ 40 － 43。
   - **N ＝ 40**：Application 8 ＋ Networking 22 ＋ Protocol 10；**D ＝ 43**：Application 24 ＋ Networking 19。逐条名单与加法见 `t006-test-dispositions.md` §2、§6。
   - **逐项目**：Application 4548 ＋ 8 － 24 ＝ 4532；Networking 175 ＋ 22 － 19 ＝ 178；Protocol 1699 ＋ 10 － 0 ＝ 1709；其余五个项目不变。
   - **结果**：通过 54 ＋ 80 ＋ 1709 ＋ 477 ＋ 968 ＋ 178 ＋ 4514 ＋ 191 ＝ 8171；失败 2，就是那 2 条 `DimensionDungeonTests`；跳过 2 ＋ 16 ＝ 18（20 → 18，少的 2 条就是 b1、b2 严格形式）；8171 ＋ 2 ＋ 18 ＝ 8191；挂起转储 0。第一次正式全量的结果相同；第二次多 1 条失败（Why 13）。
   - **表征测试**：10 通过、0 跳过；`Characterization/` 只有那两处 `Skip` 实参，金样本未改。
   - **日志信号**（`t19/analyze.py`）：为 0 的七项仍为 0；`direct send deadline exceeded` 0；`GameplayGateWait`、`GameplayLeakedFlow` 各 1，只在 `HostOutputViolationTests` 的显示名里。
     - `Gameplay bus:… failed` 8（`Act` 2 ＋ `CreateCharacterCommand` 1 ＋ `RegisterAccountCommand` 1 ＋ `SendGmMailCommand` 4）、`gm:… failed` 2（login 1 ＋ logout 1），与 TASK-22 逐项相同。
     - `inbound:… failed` 7 ＝ `GameExit` 2 ＋ `GameSelectCharacter` 4 ＋ `GameSellItem` 1；TASK-22 为 3。多出的 4 条：`GameExit` 2 是 N6 的测试命令（「cmd failed after its sends」）与 N13 线级（`Unencodable` 无编码器）；`GameSelectCharacter` 多 2 是新开启的 b1、b2 严格形式注入的失败（各 1）。F-83 一类（向已关闭的连接回包而失败）0。
     - **`Outbound connection … closed`**（新计数）7 ＝ `Failed` 6 ＋ `TimedOut` 1。隔离复跑 Networking 中是 `Failed` 5（重置客户端用例的 5 次尝试，连接 01～05）＋ `TimedOut` 1（`OutboundRealSocketTests.A_client_that_stops_reading_times_out_and_is_closed_while_another_keeps_receiving`），其余项目 0。全量中多出的 1 条 `Failed`（第一次全量中也有 1 条）在交错日志里归不到用例；候选是客户端先断开的线级用例（N2），属于推断。`OverLimit` 0（第一次全量中 5 条，全是改量之前的重置客户端用例）。
   - **F-69**：与 TASK-22 相比多出 ≥ 9 秒的 17 条（＋9～＋54 秒；Host 15 ＋ Persistence 1 ＋ Application 1）。隔离两次：Host 15 条各 1～11 秒、Application 那条 2 秒，属于满载拉长（开始时 swap 比 TASK-22 多用约 700 MB），不是排空；Persistence 的 `SqliteEntryLifecycleRecoveryTests.DungeonRecovery_OverlappingMalformedRecordDeadLettersOnceWithoutStealingClaim` 隔离两次都是 30 秒（第一、二次全量 63 ms／35 ms），它在一个库上并发跑两次恢复，用例注释允许其中一方被数据库拒绝，就是 AGENTS.md 记的「等满 30 秒」，谁先赢决定是不是 30 秒；本单元没有改它的路径（日志文首）。第一次全量的 12 条慢用例在 round 2 隔离过，各 1～5 秒。
3. **零违规取证**（`logs/task-23-violations.txt`，预期在第一次运行前写好）：46 ＝ TASK-22 的 39 ＋ `GameplayOutboxTests` 3 ＋ `NotificationDeliveryTests` 1 ＋ `OutboxRegistrationTests` 2 ＋ `OutboundRealSocketTests` 1；按 kind：`bus-leaked-flow` 2 ＋ `gate-off-executor` 9 ＋ `gate-wait` 4 ＋ `network-wait` 1 ＋ `write-off-executor` 30 ＝ 46。反例类与对照以外 0，没有测试帧 0，`exe:` 0，退出码 0；三次正式全量都是这个结果。计划 §12 对 `GameplayOutboxTests` 预计 1 条，实际 3 条：那条用例在执行器外调用了三次。
4. **判据输出**（`logs/task-23-lock-scan.txt`）：RED（生产代码放回基准的 41 个文件）时 J1 有 32 个命中 ＝ `TouchSocketNetworkMessageSender` 4 ＋ `ExecutorSendDeadline` 4 ＋ `NotificationRetryQueue` 3 ＋ `NotificationRetryDrainService` 4 ＋ `INotificationRetryDrain` 4 ＋ `NotificationDeliveryOptions` 6 ＋ `CloseSupersededAsync` 2 ＋ `TouchSocketNetworkConnectionCloser` 5，`TransportWriteSourceTests` 点名 `TouchSocketNetworkMessageSender.cs:110`；扫描 1569 个文件。GREEN 全过，扫描 1565 个（1569 － 5 ＋ 1）；传输写入点只剩 `OutboundTransport.cs:87` 与 `A21UdpDiscoveryService.cs:127`，各附理由。
5. **删除点清单**（`logs/task-23-sites.txt`）：基准 129 行、17 个文件，逐行写了处置，未分类 0；加法在日志开头。结束时同一检索式 0 行。
6. **基准核实**（`logs/task-23-verify.txt`）：开始时在基准、结束时在工作区各跑一次；与 §1 的差异只有 V10 的 XO-3、XO-4（Why 4）。
7. **工作单元内登记**（`logs/task-23-in-unit.txt`）：静态清点中会发送的事件订阅都是 LiveOnly，且都经 `ProjectAsync`（守卫拒绝可写事务）；探针中单元内登记 Application 16 ＋ Networking 2 ＋ Protocol 0 ＝ 18，全部来自测试命令，生产 CMD 0。VC-2 按「仅限理论情形」成立。探针用后还原（`cmp` 0、sha1 不变）。
8. **测试改动核对**（`logs/task-23-diff-check.txt`）：改动的已有测试文件 38 个；方法只少了 41 个、只多了 40 个，与 D 43 例、N 40 例对上（43 ＝ 41 ＋ 被删 Theory 多出的 1 行 ＋ 方法保留的 1 个 Theory 行）；断言行多重集合有变化的 24 个文件逐个对到处置记录的一行。
9. **新增与改写测试 ×10**（`logs/task-23-new-x10.txt`）：每轮 Application 56 ＋ Networking 54 ＋ Protocol 91 ＝ 201 条，10 × 201 ＝ 2010 次通过、0 失败、0 跳过，含 b1、b2 严格形式与 W7。改量后的重置客户端用例另 ×10，加屏障后的 `GmEventsHttpTests` 那条另 ×10、整类 ×3，全过。
10. **变异核对**（`logs/task-23-mutations.txt`）：S1～S25 每项都有指定用例变红；S22 为抽查（11 条线级用例变红）。S2：b2 严格形式 9／10 次红，N6 与 N13 线级 5／5，b1 严格形式 0／10（F-100）。备份、`cmp`、sha1（`e87f3731…`）三项在每一轮前后成立；最后 `--no-incremental` 重建 0 警告、0 错误。轮次与第 1 轮未编译、未变红的处理见日志开头。
11. **生产改动边界**（`logs/task-23-boundary.txt`）：41 个文件全在四个项目内，逐文件对到 sites 的行或 Owned 条目（未对上 0）；`Server/src` 下未跟踪文件只有新建的 `OutboxAdapters.cs`；`spec.md` 只有附录 A 第 4 行与附录 G VC-3 两处，全文在日志里；五个不改的项目、`plan.md`、`AGENTS.md`、`Docs/` 无改动。
12. **隔离复跑**（`logs/task-23-isolated.txt`）：
    - Application 4532 条（失败只有那 2 条已知的，跳过 16），取证 31 ＝ 24 ＋ 1 ＋ 1 ＋ 3 ＋ 1 ＋ 对照 1；
    - Protocol 1709 条全过，取证 7 ＝ 4 ＋ 2 ＋ 对照 1；
    - Networking 178 条全过（隔离 40 秒，全量中 1.8 分钟），两次都是取证 4 ＝ 2 ＋ 1 ＋ 对照 1；
    - Host 第一次 191 条中 1 条失败：`ChannelHostStartupTests.PackagedHost_FromUnrelatedWorkingDirectory_CompletesChannelBootstrap`，监听探测已连上、随后的正式连接 `Connection refused`；单独 ×5、整类 ×3 全过，三次正式全量都通过，形状同 F-55 的端口争用，登记 F-103，未取得更直接的身份。加屏障之后 Host 再隔离一次，191 条全过。两次取证都只有对照 1；
    - 四个项目反例类与对照以外都是 0。
13. **替身清点**（`logs/task-23-doubles.txt`）：95 个类、69 个文件；基准 95 个类、68 个文件，计划的 69 ＝ `INetworkConnectionCloser` 12 ＋ `INetworkMessageSender` 11 ＋ `IGameMessageSender` 46，另有 `ProjectionEventService` 子类 21 与其他 `IEventService` 实现 5，12 ＋ 11 ＋ 46 ＋ 21 ＋ 5 ＝ 95。本单元改动 2、未改 92、新建 1。
14. **选角出站实测**（`logs/task-23-selection-bytes.txt`）：20 次选角 CMD，接受进入时请求者连接登记 39,245～41,891 B、194 帧；最大值是上限 454,235 B 的 9.2 %。b1 拒绝 81 B、2 帧；b2 36,962 B、83 帧。表征运行中 `OverLimit` 0。
15. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。`pgrep -x yes` 为 0（本单元没有加负载）；结束时执行了 `dotnet build-server shutdown`。

### Remaining

- **需要结果审查确认**：Why 4（V10 与计划描述不同）、Why 5～7（Owned 列表外的文件、`Failed` 注释、总线逐条登记）、Why 8～11（删除数、`EffectOperationReplayTests` 未改、Persistence.Tests 改动、重置用例改量）、Why 13（`GmEventsHttpTests` 加屏障）。
- **测试基础设施**：F-103（打包进程用例的一次 `Connection refused`，同 F-55）。
- **交 TASK-24**（裁定第 3 条：注入会抛异常的替身、本单元之后只证明附录 E 死分支的用例；按用例名与抛异常替身检索得到，TASK-24 逐条复核）：
  - DR-1／DR-11 选角与回选角：`SelectCharacterLifecycleTests.RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection`、`PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt`、`PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks`；
  - DR-2 进本：`PartySelectDungeonHandlerTests.A_projection_failure_cannot_split_party_members_between_town_and_run`、`A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town`、`A_leader_delivery_failure_isolates_the_member_that_already_received_start_map`；`PartyMoveMapHandlerTests.Publication_failure_logs_the_committed_room_and_preserves_the_exception`；
  - DR-3 两塔：`BossTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session`、`IllusionTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session`；
  - DR-4 怪物死亡与拾取：`MonsterDeathHandlerTests.Death_send_failure_cannot_erase_the_committed_shared_clear_fact`、`Party_publication_failure_fences_every_recipient_of_the_shared_settlement`；`GetItemHandlerTests.Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact`；`IllusionTowerRewardProjectionTests.Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt`；
  - DR-6 配额：`UsageQuotaProjectionTests` 中名字以 `retries_failed_delivery`／`retries_failures` 结尾的三条，`UsageQuotaTests.Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts`；
  - DR-9 生物：`CreatureProjectionDeliveryTests.Peer_send_failure_closes_peer_and_continues_without_closing_owner`、`Owner_send_failure_closes_owner_and_reports_failure`；
  - DR-12 初始 NOTI：`OnlineUserServiceTests.FailedInitialNotice_RemovesExactRegisteredIdentity`；
  - 离开副本的同伴 `catch`：`LeaveDungeonTests.Failed_exit_delivery_releases_the_transition_without_repeating_the_exit`；
  - 登录回包失败：`GameLoginTests.MessageHandler_SendFailureConsumesTheSuccessButRemovesItsPublishedIdentity`、`GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect`；
  - 提交后发送失败不回滚业务（附录 E 之外、同一形状）：`SkillLearningCommandTests.A_send_failure_leaves_the_committed_learning_for_relogin`、`CeraShopPurchaseTests.Send_failure_does_not_repeat_or_undo_the_committed_purchase`、`NpcShopPurchaseTests.Delivery_failure_neither_repeats_the_purchase_nor_rolls_back_its_commit`、`ItemSaleTests.Failed_delivery_does_not_retry_or_reverse_the_sale`、`EffectOperationReplayTests.A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request`、`MailboxCoreIntegrationTests.Rolled_back_or_failed_mail_never_notifies_and_delivery_errors_do_not_undo_commit`。
  - 另交：F-25、F-84，附录 F 的队伍两把投递门，§9 表「TASK-24 要做的」一栏。
- **交 TASK-25**：F-61、F-101、F-102、F-104、替身清点（`logs/task-23-doubles.txt`：95 个类、69 个文件）、人群项出站测量。
- **交 T-008**：F-98（有序组的阶段语义）、F-87 的改名一半。
- **交 T-009**：F-97、F-99、F-105。

### 第 1 轮评审后修改（按「主控方处置（第 1 轮评审后）」第 1～5 条，生产行为不变，仍未提交）

- **F1**：
  - `spec.md` 附录 G 的 VC-3 改为「组队进本或两塔进入时某个成员发送失败：只断开该成员，其他成员与请求者正常进入」，括号里补了两塔进入今天的行为，依据栏由 DR-2 改为 DR-2、DR-3。
  - Result 的 Outcome、Why 1（第 2、3 点）与 Why 4 已原位改正：写明 V10 命中停止条件 2，第 1 轮没有停下是判断有误；按处置第 1 条接受新行为，由 VC-3 授权。Changed 的 Spec 一行与 Checks 11 随之改为两处。
  - 后续单元提纲 TASK-24「测试范围」一节补了一条发送端用例：两塔进入时一名成员交付失败，只关闭该成员，其余成员与请求者照常进入。
- **F2**：`follow-ups.md` F-102 的归属栏改为「TASK-25：其 Owned boundary 包含生产文件 `NotificationBus.cs`——每收件人只解析一次上下文、整批编码后登记一条批次，并补『最后一条不可编码则该收件人一条都不登记』的用例」。
- **F3**：`Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs` 中 `Delivered` 的摘要补上「或连接已不在而被丢弃（F-83）」，只改注释。
- **F4**：`Server/tests/Dfo.Server.Host.Tests/GmEventsHttpTests.cs` 从基准字节重做：只保留屏障的 11 行增、2 行删，原有的 118 行 CRLF 行尾恢复。此前用文本模式改写，把它们全改成了 LF。另核对了全部 67 个修改文件的行尾，只有这一个文件在基准上带 CRLF。
- **F5**：`follow-ups.md` 新增 F-105：AGENTS.md 中描述 `TouchSocketNetworkMessageSender`、注册时的重试上限、每收件人投递期限、`NotificationRetryDrainService`，以及期限与重试日志的段落已过期，交 T-009（RQ-100）。
- **检查**（`logs/task-23-review-fixes.txt`）：
  - `git -c core.whitespace=cr-at-eol diff --check` 无输出；
  - `spec.md` 的差异只含附录 A 第 4 行与附录 G VC-3 两行；
  - `dotnet build Server/86JP.Server.slnx -c Release` 0 警告、0 错误；
  - Host.Tests 过滤到 `GmEventsHttpTests`：11 条全过。
  - `logs/task-23-boundary.txt` 与 `logs/task-23-diff-check.txt` 已按改后的工作区重新生成：生产 41 个文件，增 844 行、删 1027 行；测试核对结果不变，仍是 38 个文件、24 个集合不等文件。
  - 正式全量没有重跑：本轮只改注释、Spec、记录和一个测试文件的行尾。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict `changes_required`，原因只有 F1。按 `task-rework` 处理：不改生产行为，只补 Spec 授权与记录。

1. **F1（两塔进入 XO-3／XO-4 在交付失败时的可观察行为变化）：接受新行为，扩展 VC-3 覆盖两塔。**
   - 依据：附录 E DR-3 的保护意图是「失败者不留在塔局里」，新承接方式是「RQ-036 关连接，生命周期 CMD 清理」，指向的正是现在的行为：只有失败者被发送端关闭，其余成员与请求者照常进入。今天「一人失败、全体隔离成员与请求者都断线」是同类旧行为，VC-3 已对组队进本接受了同样的改变，两塔没有理由保留更重的连坐。
   - `spec.md` 附录 G 的 VC-3 改为覆盖组队进本与两塔进入；依据栏补 DR-3。这是主控方授权的 Spec 修订，在 Result 中登记，并交外部审核时重点说明。Owned boundary 增加这一处。
   - Result Why 1 与 Why 4 改正：写明 V10 核实命中停止条件 2，按本条裁定处理，可观察行为有变化，并由 VC-3 授权。
   - 证据：沿用现有 XO-3／XO-4 用例；TASK-24 补「两塔进入时一名成员交付失败，其余照常进入」的发送端用例（写进 TASK-24 提纲）。
2. **F2**：F-102 的去向写明「TASK-25 的 Owned boundary 包含生产文件 `NotificationBus.cs`：每收件人只解析一次上下文、整批编码后登记一条批次，并补『最后一条不可编码则该收件人一条都不登记』的用例」。
3. **F3**：`INotificationBus.cs` 中 `Delivered` 的注释补「或连接已不在而被丢弃」。
4. **F4**：`Host.Tests/GmEventsHttpTests.cs` 恢复原行尾，只保留加屏障的改动。
5. **F5**：在 `follow-ups.md` 补一项：AGENTS.md 中描述 `TouchSocketNetworkMessageSender`、注册时的重试上限、每收件人投递期限、`NotificationRetryDrainService` 以及期限与重试日志的段落已过期，交 T-009（RQ-100）。

处理后：`cr-at-eol` 口径 `diff --check`；`spec.md` 的差异只含附录 A 第 4 行与附录 G VC-3；构建 0 警告；Host.Tests 中 `GmEventsHttpTests` 过滤运行一次。

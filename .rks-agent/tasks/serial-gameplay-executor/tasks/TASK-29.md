# TASK-29 — T-007：调用链贯通与深度上限

基准提交 `06ca5e0f`（TASK-28／M-2 已提交，结论「M-2 通过」）。若主控方先把「T-008 改道提案」（R3）以修订提交落盘，基准改为那次修订提交；本单元的内容不受改道影响（`plan.md` 修订记录 R3 中 T-007 一行为「不变」）。范围取自：
- `plan.md` T-007 行：各类 CMD 来源设定根或父，失败重试继承原链；事件 delivery 的进程内链表；超限处理（关连接，以系统 CMD 落定 delivery，只记日志）；深度常量与判据；日志字段；
- `spec.md` RQ-050～RQ-054（RQ-050 末句与 RQ-053(a) 的澄清：传输层自发的关闭、深度检查发起的关闭，引起的生命周期 CMD 都是新根）、附录 A 的最长链表（L ＝ 10，常量 32，下限 20）、AC-016；与之相连的 RQ-012、RQ-014、RQ-041 中「继承链」那一半（`verification/t004-closure.md:103`、`:105`、`:108`）；
- `verification/follow-ups.md` 中归属 T-007 的 F-26、F-27、F-28、F-29、F-59、F-66（`t006-closure.md` §8 移交清单 T-007 一行），以及 F-69（每个后续单元的耗时对比）；
- TASK-23 §3.2 末句（调用链的 `SourceConnection` 与 RQ-035 精确身份是否统一，「T-007 若要统一两者，由它决定」）与 TASK-23 修订待裁定第 6 条（顶替关闭由 CMD 登记之后，它引起的生命周期 CMD 应继承登录 CMD 的链，「归 T-007 落实」）；
- TASK-18 待裁定第 6 条与主控方裁定第 6 条（队列层是否也拒绝泄漏流，即 F-59）；
- TASK-10 待裁定第 4 条与裁定（`lifecycle:close-cleanup` 推迟到 T-007）；
- TASK-26 §3.4 的一句：`GameplayDepthExceededException` 是否进打包进程输出的违规识别名单，「T-007 落实时再判」。

本单元的产出：事件 delivery 与关连接两条「由 CMD 引起的 CMD」改为继承父链；超限时的落定、关根连接与日志；各来源根的核对；深度常量的复核；配套用例与 4 处线级断言的改写；T-007 收口记录。

输入：
- TASK-27 的正式全量 `logs/task-27-build-test.txt`（8247 条）、取证 `logs/task-27-violations.txt`（40 条）、原始输出 scratchpad `t27/formal-raw.txt`；
- 事件图统计 `verification/event-chain.md` 与脚本 scratchpad `t12/subcheck.py`；
- 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`（`t27/analyze.sh`）、`t21/methods.py`、`t24/elapsed.py`、`t27/mutate.py`、`t27/violations.sh`、`t27/closure_refs.py`。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **事件 delivery 的调用链**（§2；RQ-050、RQ-051、RQ-041；F-27 的继承一半）：`EventService.EnqueueAsync` 在当前作业上记录发布它的 CMD 的链，按 delivery（事件 ID ＋ 订阅 ID）存在进程内存中，表结构不变；`DomainEventDispatcher` 消费时取它的子链（深度 ＋1，来源种类 Event）；同一 delivery 的失败重试仍取首次消费时的同一父链；delivery 落定后遗忘；事务回滚时撤销记录；别的进程留下的 delivery 照旧是「启动恢复」新根。
  2. **关连接引起的生命周期 CMD**（§3；RQ-050、RQ-012；F-26；TASK-23 待裁定第 6 条）：作业的发件箱刷出关连接请求时，记录该作业的链为这次关闭的原因；`NetworkConnectionLifecycleDispatcher` 处理这个连接的 `lifecycle:disconnected` 时取原因的子链（来源种类 Lifecycle）。没有记录原因的断开（客户端断开、发送端按 RQ-036 关闭）仍是新根。CMD 类型不新增（待裁定第 3 条）。
  3. **超限处理**（§4；RQ-053、AC-016；F-27 的落定一半、F-29）：执行器的拒绝与 `Error` 日志保持不变；事件 delivery 被拒时，由一条新根系统 CMD 落定该行（Reliable 写 Terminal，LiveOnly 以深度超限为理由写完成），不执行订阅处理；链的根带客户端连接时，同一条系统 CMD 以「无因关闭」登记关闭根连接，于是它引起的生命周期 CMD 是新根；无连接的链只记日志。生命周期 CMD 超限的处理按待裁定第 1 条。
  4. **根的核对与队列层泄漏流**（§5；F-28、F-66、F-59）：逐个核对生产中的 `NewRoot` 调用点与 RQ-050 的来源种类一致；卡牌计时器按待裁定第 4 条；队列层是否拒绝泄漏流按待裁定第 2 条。
  5. **深度常量复核**（§6；RQ-052）：在基准上按 `event-chain.md` 的方法重算事件图的订阅数与最长简单路径，确认 L 仍为 10，常量 32 与判据 `GameplayCallDepthTests` 不变。
  6. **日志字段**（§7；RQ-054）：核对执行器的开始、完成、取消、失败、拒绝日志都带根 ID、父 ID、深度与 CMD 类型，并用继承链的用例证明父 ID 与深度是真实值。
  7. **证据与改写**（§8）：新增用例（预期见 §11）；`LifecycleExecutorWireTests` 中 4 处由 CMD 登记关闭的断开，断言从「深度 0、来源连接是被关的连接」改为「父链的子，深度 1，来源连接是根的连接」，逐条写「旧断言 → 新断言 → 保护意图」。
  8. **收口**（§10）：新建 `verification/t007-closure.md`；`follow-ups.md` 关闭或更新 F-26～F-29、F-59、F-66，续写 F-69，文首补 TASK-29 一行；生产代码里两处「过渡：一律新根」的注释改为现状。
  9. **一次正式全量**：带取证汇，反例类与对照以外为 0，`exe:` 为 0；表征测试 10 通过、0 跳过，`Characterization/` 与金样本目录不改；变异核对（§9）。
- Owned boundary：
  - **生产**（都在 `Server/src/Dfo.Server.Application`，另有待裁定第 2 条接受时的一处 Abstractions 常量）：
    - `DomainEvents/EventService.cs`（记录链、回滚撤销）、`DomainEvents/DomainEventDispatcher.cs`（取链、遗忘、超限落定）、`DomainEvents/DomainEventDispatchingExtensions.cs`（注册）；新建 `DomainEvents/DomainEventCallChains.cs`；
    - `Execution/GameplayTimerWork.cs`（加一个带链的重载）、`Execution/GameplayQueue.cs`（`AddGameplayExecution` 注册关闭原因存储）；新建 `Execution/GameplayCloseCauses.cs`（关闭原因存储与深度守卫的系统 CMD 辅助，可按需拆成两个文件）；
    - `Execution/Outbox/GameplayOutbox.cs`（刷出时记录原因；无因关闭）、`Execution/Outbox/OutboxAdapters.cs`（只在需要时加一个内部的无因关闭入口，`INetworkConnectionCloser` 接口不变）；
    - `Dispatching/NetworkConnectionLifecycleDispatcher.cs`（取原因的子链；待裁定第 1 条的超限处理）；
    - 待裁定第 2 条接受时：`Execution/GameplayExecutor.cs`（`RunAsync` 入口拒绝泄漏流）与 `Server/src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs`（只加一个取证类常量）。
  - **测试**：
    - `Server/tests/Dfo.Server.Application.Tests/DomainEvents/DomainEventDispatchTests.cs`：只扩展 `Fixture`（可选的链用订阅、记录发送端、连接上下文、计数），不改已有用例；新建 `DomainEvents/DomainEventDispatchTests.Chain.cs`（同一 partial 类）；
    - 新建 `Server/tests/Dfo.Server.Application.Tests/Dispatching/LifecycleCallChainTests.cs`；
    - 待裁定第 2 条接受时：新建 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayQueueLeakTests.cs`；
    - `Server/tests/Dfo.Server.Networking.Tests/Integration/LifecycleExecutorWireTests.cs`：只改 §8.2 列出的 4 处断言（加一个断言子链的辅助方法），其余不动；`LifecycleWireHost.cs` 只在记录器需要多记一项时改动。
  - **Spec**：待裁定第 1 条接受时，`spec.md` RQ-053 按裁定加一句；其余不改。
  - **文档与证据**：`verification/t007-closure.md`（新建）、`verification/follow-ups.md`、`verification/logs/task-29-*.txt`；一次性脚本放 scratchpad `t29/`，全文附在对应日志末尾。
  - **不改**：
    - `Server/src` 中 Networking、Protocol、Persistence、Infrastructure、HttpApi、GmWeb、GameServer、Host 各项目；`OutboundEntry`、`IOutboundSender`、`IOutboundDeliveryObserver`、`INetworkConnectionLifecycleDispatcher`、`INetworkConnectionCloser`、`IDomainEventPersistence` 等共享契约（待裁定第 5 条裁为备选 P 时另行授权）；`GameplayCallChain` 与 `GameplayCallDepth.Limit`；
    - 数据库表结构（RQ-051）；
    - `plan.md`、`AGENTS.md`、`Docs/`、`protocol/`；`Characterization/` 目录与金样本；`entry-reconciliation.md` 与以往单元的收口文件（附录 A 的最终对账归 T-009）；`probe-tools/`。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。RED 阶段一次（只加了用例），实现之后一次，变异全部做完之后一次。
  2. **RED／GREEN**（`logs/task-29-red-green.txt`）：只加用例、不改生产代码时，§8.1 中标「基准红」的用例全部变红，且失败原因是链的字段（新根而非子链）、超限后未落定或未关根连接，不是别的原因；标「基准绿」的对照用例此时为绿；§8.2 改写后的 4 处断言此时变红（证明新断言区分得出继承与新根）。需要新内部类型才能编译的用例不进 RED，它们的区分力由 §9 的变异证明，逐条写明。实现之后全部变绿。两段都记命令、退出码与关键输出。
  3. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-29-build-test.txt`。
     - **总数**：基准 8247 ＝ HttpApi 54 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 181 ＋ Application 4538 ＋ Host 195。本单元为 8247 ＋ N － D，逐项目写加法，N 逐条列名（预期见 §11）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（三样一起取身份；打包进程用例在满载下的就绪超时，按 F-55／F-103 的形状取身份并隔离复跑）；挂起转储 0。
     - **跳过**：18，不变。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动（`git status` 为证）。
     - **日志信号**：与 TASK-27 Checks 3 同口径、同脚本。应为 0 的七项中 `GameplaySelfWait` 仍为 1，其余六项为 0；其余各项与 TASK-27 逐条对照。新用例会多出执行器的深度拒绝 `Error` 行（`exceeds the depth limit`）与落定的 `Warning` 行，逐条归因到 §8.1 的用例；正式全量中，这两类行出现在这些用例以外的位置即为异常，停下。
     - 耗时与内存只作信息（RQ-091 用户修订：单条 ≤ 2 s 即可接受，性能不作停止条件）。F-69：用 `t24/elapsed.py` 与 TASK-27 的正式全量（`t27/formal-raw.txt`）逐条对比，多出 ≥ 9 秒的逐条隔离复核。
  4. **零违规取证**（`logs/task-29-violations.txt`）：命令同 TASK-27 Checks 4。预期先写进日志开头再跑：待裁定第 2 条不接受时 40 条，与 TASK-27 相同；接受时 41 条，多出的 1 条是 Q1 的 `queue-leaked-flow`，归入反例类（`violations.sh` 的分类相应加一类，改动写进日志）。反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  5. **事件图复核**（`logs/task-29-event-chain.txt`，§6）：订阅注册数与分类（基准预期 52 ＝ Reliable 17 ＋ LiveOnly 22 ＋ Direct 13）；与 `event-chain.md` 的 52 行逐行比对（新增、删除、发布点变化）；有变化时按该文件的方法重算最长简单路径；结论 L 的值。
  6. **变异核对**（`logs/task-29-mutations.txt`；程序同 TASK-27 Checks 6：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 与 `-- Server/tests` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§9 每一项都有指定的用例变红。
  7. **新增测试 ×10**（`logs/task-29-new-x10.txt`）：本单元新增的方法隔离连续 10 次，全部通过；`DomainEventDispatchTests`、`ConnectionLifecycleDispatchTests`、`LifecycleExecutorWireTests` 全类 ×3。耗时只作信息。
  8. **检索集**（`logs/task-29-retrievals.txt`，§5、§10）：每个检索式先写预期再跑。
  9. **收口引用核对**（`logs/task-29-closure-refs.txt`）：`t27/closure_refs.py` 复制为 `t29/closure_refs.py`，范围改为 `t007-closure.md`：引用的用例名与类名都能在 `Server/tests` 找到；引用的 `logs/…` 都存在；引用的 F 编号都在 `follow-ups.md`；报告「本单元关闭」的 F 编号在 `follow-ups.md` 中已关闭；归属栏写了动作的行状态不是「信息」。全部 0 处不符。
  10. **测试改动核对**（`logs/task-29-diff-check.txt`）：`diffcheck7.py <基准> --lines` 加 `t21/methods.py`：方法集合只多了 N 中列出的，没有少的；断言行多重集合有变化的已有文件只有 `LifecycleExecutorWireTests.cs`，变化只落在 §8.2 的 4 处与新的辅助方法，逐处列出；`DomainEventDispatchTests.cs` 的变化只在 `Fixture` 与它的辅助类型内，已有用例的方法体逐字节不变。
  11. **边界**（`logs/task-29-boundary.txt`）：`git diff --stat <基准> -- Server/src` 只有 Owned boundary 列出的文件；`Server/src` 下的未跟踪文件只有列出的新文件；Networking、Protocol 等未列项目无改动；`plan.md`、`AGENTS.md`、`Docs/`、`protocol/` 无改动；`spec.md` 无改动或只有待裁定第 1 条裁定的那一句；`Server/tests` 的改动只含 Owned boundary 列出的文件。
  12. **隔离复跑**（`logs/task-29-isolated.txt`）：Application、Networking 各一次，都带取证目录，记录各自的反例类与对照记录数。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。改动的已有文件行尾与基准一致（规划时核对：Owned boundary 中全部已有文件，以及 `spec.md`、`follow-ups.md`，都是 0 个 CR）；新建文件 LF。结束时执行 `dotnet build-server shutdown`；`pgrep -x yes` 为 0。
- Dependencies：
  - T-004（TASK-06～TASK-19）：执行器、作业令牌、`GameplayTimerWork`、`NetworkConnectionLifecycleDispatcher`、事件 CMD（TASK-12）；
  - T-006（TASK-23～TASK-27）：关连接一律是发件箱条目，`OutboxConnectionCloser` 是唯一关闭器，顶替关闭在登录 CMD 发件箱（F-65），作业失败时关闭来源（RQ-031(c)）；
  - M-2 通过（TASK-28，`06ca5e0f`）；
  - **执行前提**：工作区干净。今天工作区里有未生效的「T-008 改道提案」改动（`TaskContract.md`、`routing-decision.md`、`spec.md`、`plan.md`、`follow-ups.md`、`verification/reroute-t008*.md`、`attempts/`），而本单元要写 `follow-ups.md`、可能写 `spec.md`。由主控方先把提案落盘为修订提交或撤回，本单元才开始；V1 核对。
  - 脚本：Inputs 列出的都在 scratchpad（规划时核对）；执行开始时再核对，缺失时从 TASK-25～TASK-27 日志末尾附的全文恢复。
- Result review：required。
  - 本单元确定的继承规则，是 T-009 最终对账与子 Spec B（若改道生效）沿用的共享语义。哪个来源取新根、哪个取子链定错了，全量测试大多不会变红：今天几乎没有用例断言链的字段，§8 的新用例只覆盖两条继承路径和几个代表来源，其余来源的正确性靠 §5 的检索与审查。
  - 超限处理会改数据并断开连接：把 delivery 落定成 Terminal 意味着那次订阅处理永远不会执行。落定的条件写宽了，就是静默丢事件；写窄了，就是分发循环反复拒绝同一行。用例只覆盖构造出来的深链。
  - 改写了 4 处既有线级断言。改写可能削弱原来的保护（例如不再断言清理落在执行器上、晚于关闭它的 CMD），要靠审查逐条对照「旧断言 → 新断言 → 保护意图」。
  - 待裁定第 1、2、5 条若按建议接受，分别改了 RQ-053 的字面、执行器入队点的契约、以及 RQ-050 澄清的实现精度，交外部审核时要能对上。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. §6 的复核得出 L ≠ 10，或订阅图有了新的环。
    2. 需要改 Owned boundary「不改」中列出的项目、共享契约或表结构才能完成；需要改 `spec.md` 中待裁定第 1 条以外的文字；需要改 `plan.md`、`AGENTS.md`。
    3. 除 §8.2 的 4 处以外，任何已有用例的断言需要改动；表征测试或金样本需要改动。
    4. V 核对发现连接上下文在登记、关闭请求、断开回调之间不是同一个实例，且按 §3.1 的退路（按连接 ID 记录并核对实例）也无法区分「同一连接」与「同 ID 的新连接」。
    5. 正式全量取证出现 `exe:` 记录，或反例类与对照以外的记录；正式全量出现基线与已登记偶发项以外的失败；深度拒绝或落定的日志行出现在 §8.1 用例以外。
    6. 落定 delivery 需要改 `IDomainEventPersistence` 的语义（例如 `FailAsync` 写 Terminal 或 `CompleteAsync` 写完成的条件与 §4.2 读码不符）。
  - 变异期间允许临时改动 `Server/src` 与测试，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 范围、与 follow-ups 原文的差异

**今天的状态**（规划时在 `06ca5e0f` 上读码）：
- 链的机制在 T-002 已经齐了：`GameplayCallChain` 的 `NewRoot`／`Child`／`ForCommand`／`Describe`（`Abstractions/Execution/GameplayCallChain.cs`），`GameplayCallDepth.Limit ＝ 32`，执行器在入队时拒绝 `Depth > Limit` 并记 `Error`（`Application/Execution/GameplayExecutor.cs:43-52`），开始、完成、取消、失败日志都带根、父、深度、来源与 CMD 类型（`:259-264`、`:307-330`）。
- **生产代码里一个 `Child(` 调用都没有**。全部来源都取 `NewRoot`：入站（`NetworkMessageDispatcher.cs:47`，带连接）、生命周期（`NetworkConnectionLifecycleDispatcher.cs:60`，带被关的连接）、定时与事件（`GameplayTimerWork.cs:46`；事件的来源种类由 `DomainEventDispatcher.cs:90-95` 决定，注释写着「过渡：一律新根，T-007 改为发布它的 CMD 的链」）、卡牌计时器（`DungeonCardPresentationCoordinator.cs:362`）、登录会话维护（`LoginSessionRuntime.cs:43`）、GM／HTTP（`HttpGameplay.cs:34`）、启动作业（`GmBootstrapService.cs:28`、`GameServerStartupService.cs:100`、`JoustMaintenanceService.cs:24`）。`CommandBus.cs:34` 的执行器外分支只有测试走到。
- 所以深度检查今天永远不会触发：链的深度恒为 0。一旦本单元接上继承，执行器拒绝深链时，事件分发组循环会把拒绝当成「could not persist or scan」，记 `Error`、退避 `RetryDelay` 后再读到同一行、再被拒（`DomainEventDispatcher.cs:208-213`）。这正是 RQ-053(b) 要排除的空转，所以继承与落定必须在同一个单元里接上。

**规划时发现、follow-ups 原文没有写到的**：
1. **由 CMD 登记的关闭有三种**：CMD 显式调用关闭器（`OutboxConnectionCloser.RequestCloseAsync`／`RequestCloseExactAsync`，`OutboxAdapters.cs:211-232`）；作业体失败时发件箱补登记的来源关闭（`GameplayOutbox.cs:201-202`，RQ-031(c)）；发送端拒收整箱时的兜底关闭（`GameplayOutbox.cs:79-82`）。三种都在刷出时进入发件箱交给发送端的条目，本计划都按「CMD 登记」处理（§3.4）。
2. **4 条线级用例的断言与 RQ-050 冲突**：`LifecycleExecutorWireTests.AssertLifecycleJob`（`:208-216`）断言每个 `lifecycle:disconnected` 深度为 0、来源连接是被关的连接。其中 4 处的断开是由 CMD 登记的关闭引起的（§8.2），继承之后深度为 1、来源连接是根的连接（或为空）。这 4 处断言必须改写；这是 RQ-050 要求的变化，不是行为回退。
3. **生命周期 CMD 超限会丢掉一次拆除**（待裁定第 1 条）：RQ-053 对一切超限都「不入队这条 CMD」。被拒的若是 `lifecycle:disconnected`，那个连接已经断开，它的在线身份、位置落库都不会再有人处理（RQ-012、AC-007）。
4. **「系统 CMD 豁免深度检查」不需要执行器的旁路**：落定 CMD 不继承调用链，是新根，深度为 0，天然通过检查。执行器不加任何豁免分支。

**本单元不做的**：附录 A 的最终对账与 `entry-reconciliation.md` 的改写（T-009；F-99）；请求级帧改为显式数据（T-008 或子 Spec B）；日志级别复核（F-121，T-009）；性能项。

### 1. 基准核实（执行时重跑并落盘 `logs/task-29-verify.txt`）

| # | 问题 | 检索 | 规划时在 `06ca5e0f` 上的结果 |
|---|---|---|---|
| V1 | 工作区与基准 | `git status --short`；`git log -1` | 工作区有未生效的改道提案改动（Dependencies「执行前提」）；执行时应为干净，HEAD 为基准 |
| V2 | 测试总数与取证基线 | 读 `logs/task-27-build-test.txt`、`logs/task-27-violations.txt` | 8247（逐项目见 Required check 3），失败 2、跳过 18；取证 40 ＝ 2 ＋ 1 ＋ 37 |
| V3 | 生产中的根 | `grep -rn -e "NewRoot" -e "\.Child(" --include='*.cs' Server/src` | `NewRoot` 9 处：`HttpGameplay.cs:34`、`DungeonCardPresentationCoordinator.cs:362`、`GmBootstrapService.cs:28`、`GameplayTimerWork.cs:46`、`NetworkMessageDispatcher.cs:47`、`CommandBus.cs:34`、`NetworkConnectionLifecycleDispatcher.cs:60`、`LoginSessionRuntime.cs:43`、`GameServerStartupService.cs:100`（两个启动作业共用）；`JoustMaintenanceService.cs:24` 与 `DomainEventDispatcher.cs:81-84` 经 `GameplayTimerWork` 的来源参数取根。`Child(` 0 处（`GameplayCallChain.cs` 自身除外） |
| V4 | 深度检查与日志 | 读 `GameplayExecutor.cs:34-91`、`:241-333` | 拒绝在入队时、`Depth > Limit`，记一条 `Error`，字段 `CommandType`、`Limit`、`RootId`、`ParentId`、`Depth`、`Source`、`Chain`；开始 Trace、完成与取消 Debug、失败 Error，都带根、父、深度、来源、CMD 类型 |
| V5 | 事件发布与消费 | 读 `EventService.cs:20-37`、`DomainEventDispatcher.cs:60-175` | 发布要求当前可写事务，`EventBatch` 是事务参与者（`Commit`／`Rollback` 空实现）；消费经 `GameplayTimerWork.RunAsync(…, SourceOf(delivery))`；Reliable 在一个事务内 `TryBegin` → 处理 → `Complete`；失败时另开事务 `FailAsync(terminal)`；LiveOnly 先提交消费决定再投影 |
| V6 | 落定所用的持久化语义 | 读 `Persistence/DomainEvents/SqliteDomainEventPersistence.cs:36-108` | `ReadDueAsync`／`NextDueAsync` 只取 `state=0`；`FailAsync(terminal: true)` 写 `state=2`，条件 `state=0`，不抛；`CompleteAsync` 写 `state=1` 与理由，条件 `state=0`，不匹配即抛 |
| V7 | 关闭的登记与刷出 | 读 `GameplayOutbox.cs:28-90`、`:166-205`、`OutboxAdapters.cs:191-241` | 刷出在作业的结束钩子里、作业仍在执行器上（`GameplayJob.OnEnd`，`GameplayExecutor.cs:291`），`GameplayJob.Current` 可读；关闭条目由 `Complete` 产出，交 `sender.Submit` |
| V8 | 断开回调拿到的上下文 | 读 `NetworkPlugins.cs:42-58`、`TouchSocketConnectionRegistry.cs`；核对关闭器 `contexts.TryGet` 与入站 `context.Connection` 取到的是不是登记时的同一个实例 | `OnTcpClosed` 用 `Connections.TryRemove` 取回登记的上下文。执行时确认三处是同一个实例（§3.1 按引用记录的前提） |
| V9 | 断言链字段的既有用例 | `grep -rn -e "ParentId" -e "\.Depth\b" -e "RootId" --include='*.cs' Server/tests` | 断言 `Depth == 0` 的有 `LifecycleExecutorWireTests:215`、`InboundExecutorWireTests:215`、`ConnectionLifecycleDispatchTests:131`；只有前者的 4 个调用点涉及 CMD 登记的关闭（§8.2）；事件用例只断言来源种类（`DomainEventDispatchTests.Executor.cs:35`、`:96`、`:133`、`:136`），继承后不变 |
| V10 | 订阅注册数 | `grep -rn -E "AddDomainSubscriber<\|AddRuntimeEventSubscriber<" --include='*.cs' Server/src`，排除定义文件 | 52 行 |
| V11 | 脚本 | `ls` scratchpad 下 Inputs 列出的脚本 | 都在 |

V1～V11 与本表不一致时：V1 不干净，停下回报；V2 的总数不同，停下回报（说明 TASK-27 之后有未记录的改动）；V3 多出 `NewRoot` 调用点，纳入 §5 的核对；V6 不符，按停止条件 6；V8 不是同一实例，按 §3.1 的退路，退路也不行按停止条件 4；V10 不是 52，§6 逐行比对后再判。

### 2. 事件 delivery 的调用链（Outcome 1）

**2.1 存储**：新建 `DomainEvents/DomainEventCallChains.cs`，内部单例，在 `AddEventDispatching` 注册。按 `(EventId, SubscriberId)` 保存发布它的 CMD 的链（带 CMD 类型，即 `GameplayJob.Current`）。用并发字典：写入都在执行器上，读取在分发组循环上（生产者，执行器外）。它是调用链的元数据，不是玩法状态，不受 RQ-061 的写入口约束；理由写进 `t007-closure.md`。

**2.2 记录**：`EventService.EnqueueAsync` 在写入事件行之后，若 `GameplayJob.Current` 有值，为每个目标订阅记一条。记录与事件行同时出现，所以分发循环能读到这一行时，链一定已经在那里（不存在「已提交、未记录」的窗口）。`EventBatch` 收下本批记过的键，`RollbackAsync` 时逐个撤销。提交结果不确定、`RollbackAsync` 没有被调用时会留下孤儿项：它们永远不会被读到，随进程结束，数量不超过这类事务发布的事实数；写进收口的已知限制。不在作业上发布（今天只有可能出现在测试里）时不记，消费时取新根。

**2.3 推导**（`DomainEventDispatcher.DrainOnceAsync`）：
- 别的进程留下的 delivery（`OriginInstanceId ≠ instance.Id`）：`NewRoot(StartupRecovery)`，CMD 类型 `startup:event/<订阅 ID>`，不变（RQ-051）；
- 有记录：`记录.Child(GameplayCallSource.Event)`，CMD 类型 `event:<订阅 ID>`；
- 本进程但没有记录：`NewRoot(Event)`，与今天相同。
- `GameplayTimerWork` 加一个接收 `GameplayCallChain` 的 `RunAsync` 重载，原重载改为调用它，行为不变；`CoalescingGameplaySource` 不动（定时源仍是新根）。

**2.4 遗忘**：都在执行器上、在落定的那次提交之后：Reliable 完成提交后；LiveOnly 的消费决定提交后（投影之前）；失败且 `terminal` 的 `FailAsync` 提交后；超限落定提交后（§4.2）。`TryBeginAsync` 返回 false（别处已认领）时不动。非终态的失败不遗忘，下一次重试读到的仍是同一条父链，所以重试的根、父、深度与首次消费相同（RQ-050「继承首次消费时的链」；每次尝试仍是一个新节点，`Id` 不同）。

### 3. 关连接引起的生命周期 CMD（Outcome 2）

**3.1 关闭原因的存储与记录**（待裁定第 5 条，建议 A）：新建 `Execution/GameplayCloseCauses.cs`，内部单例，在 `AddGameplayExecution` 注册，按 `NetworkConnectionContext` 的引用记录原因链（弱引用表，连接对象回收即随之消失，不会因关闭请求被丢弃而泄漏）。`GameplayOutboxes.Flush` 在把条目交给 `sender.Submit` **之前**，对每个关闭条目记一次：原因 ＝ 当前作业的链（`GameplayJob.Current`，刷出时作业仍在执行器上）；无因关闭（§3.3）不记。已有记录时不覆盖（先到的请求先被执行）。没有发送端时条目被丢弃，也不记。
- V8 若发现三处不是同一个实例：退路是按连接 ID 记录，同时记下上下文实例，读取时只在实例相同时采用；仍分不出时按停止条件 4。

**3.2 读取**：`NetworkConnectionLifecycleDispatcher.DispatchDisconnectedAsync` 取出并删除这个上下文的原因：有原因 → `原因.Child(GameplayCallSource.Lifecycle)`；没有 → 今天的 `NewRoot(Lifecycle, 连接 ID)`。CMD 类型都是 `lifecycle:disconnected`（待裁定第 3 条）。`lifecycle:connected` 永远是新根。类上「一律新根」的 remarks 改为现状。

**3.3 无因关闭**：只给深度守卫用（§4.3）。实现为 Application 内部的入口（发件箱上的一个内部方法，或 `OutboxConnectionCloser` 上的一个内部方法），条目照常进发件箱、照常排在已登记条目之后交付，只是不记原因。`INetworkConnectionCloser` 接口不变，Protocol 等调用方拿不到它。

**3.4 失败来源关闭与兜底关闭**：作业体失败时补登记的来源关闭、发送端拒收整箱时的兜底关闭，都是发件箱代这条 CMD 登记的，按 §3.1 记原因（待裁定第 6 条）。

**3.5 后果**（§8.2 的依据）：CMD 关闭自己的来源（`A_cmd_closing_its_own_source_connection…`、`A_login_refused_by_the_real_handler…`）→ 清理是入站 CMD 的子，深度 1，来源连接仍是本连接；CMD 关闭别的连接（`A_cmd_closing_another_connection…`）→ 来源连接是请求方的连接；HTTP 登录顶替（`A_superseded_connection_is_closed_by_maintenance…`，顶替发生在 HTTP 登录 CMD 内，F-65）→ 根是 HTTP，来源连接为空。客户端重置、发送失败（`A_send_to_a_resetting_client…`、`A_client_disconnect…`、`A_disconnect_during_an_inbound_job…`）仍是新根，断言不变，作为 RQ-050 末句的证据。

### 4. 超限处理（Outcome 3）

**4.1 执行器不改**：拒绝条件、拒绝时不入队、那条 `Error` 日志（含 `Describe` 的完整链）都已满足 RQ-053 正文，判据 `GameplayCallDepthTests.Work_at_the_limit_runs_and_one_past_it_is_refused_with_the_full_chain_logged` 守着。祖先跳数保留 `Limit ＋ 1`，所以第一次超限时日志里的链从根开始、没有省略。生产者收到的是同步失败的 `GameplayDepthExceededException`，`GameplayJob.EndedAJob` 为 false，借此与作业体失败区分。

**4.2 事件 delivery 的落定**（RQ-053(b)）：`DrainOnceAsync` 捕获 `GameplayDepthExceededException`，改为入队一条系统 CMD，等它完成后计为已处理，继续下一行：
- 链：`NewRoot(GameplayCallSource.Event)`，深度 0；CMD 类型 `event:<订阅 ID>/depth-exceeded`（别的进程的 delivery 不会超限，因为它们是新根）；
- 体：开一个事务。Reliable：`FailAsync(…, terminal: true)`，错误文本写「depth limit exceeded」与被拒链的 `Describe`，截到 4000 字符（与失败路径同口径）；LiveOnly：`TryBeginAsync` 后 `CompleteAsync(理由 "Depth limit exceeded; live hint skipped.")`，`TryBeginAsync` 返回 false 则什么都不做。策略按 `ProcessAsync` 的方式从注册表解析；解析失败（未知事件类型）按 Reliable 的方式写 Terminal。提交后遗忘这条链，再按 §4.3 关根连接；
- 不调用订阅处理；不另记 `Error`（那条由执行器记），只记一条 `Warning`：事件 ID、订阅 ID、被拒链的根、父、深度与落定结果；
- 落定 CMD 自己失败时异常照常上抛到组循环，退避后重读到同一行，再拒、再落定；
- 之后 `ReadDueAsync` 不再取到这一行，组循环进入「等唤醒或到期」，不空转（AC-016）。

**4.3 关根连接**（RQ-053(a)、F-29）：「根来源是客户端连接」按被拒链的 `SourceConnection` 非空判定（待裁定第 6 条）。事件超限时在 §4.2 的落定 CMD 内，生命周期超限时在 §4.4 的守卫 CMD 内，按连接 ID 取当前登记的上下文，以无因关闭登记（§3.3）。连接已不在（例如根就是一条已断开连接的生命周期新根）时只记 Debug。于是根连接的 `lifecycle:disconnected` 是新根，深度 0，来源种类 Lifecycle。`SourceConnection` 不改成精确身份：关闭器按 ID 取当前上下文已是 T-006 的既有写法，连接 ID 内含 TouchSocket 会话 ID（`TouchSocketConnectionRegistry.cs:98-102`）；这一条回应 TASK-23 §3.2 的「由 T-007 决定」，记入收口。

**4.4 生命周期 CMD 超限**（待裁定第 1 条，按建议 A 写）：`DispatchDisconnectedAsync` 入队子链被拒时，执行器已记 `Error`；分发器改以 `NewRoot(Lifecycle, 连接 ID)` 重新入队同一个拆除，再入队一条守卫 CMD（`lifecycle:depth-exceeded`，`NewRoot(Lifecycle)`）按 §4.3 关根连接；根连接就是被拆除的连接时省掉守卫 CMD。不接受建议 A 时，按备选只入队守卫 CMD，拆除不执行，L5 改为断言拆除没有执行。

**4.5 其余情况**：没有连接的链（定时、GM、HTTP、启动恢复为根）超限时，只有执行器那条 `Error` 与 §4.2 的落定（RQ-053(c)）。

**4.6 打包进程的违规识别**：`GameplayDepthExceededException` 不进 `HostOutputViolation` 的识别名单。理由：超限是有处理的结果（落定、关连接、记日志），不是断言违规；它出现在打包进程输出里，说明有一条真实的失控链，由正式全量的日志信号发现（Required check 3）。回应 TASK-26 §3.4，记入收口。

### 5. 根的核对与队列层泄漏流（Outcome 4）

**5.1 根的核对**（F-66、F-28；检索集 R1～R3）：对 V3 的每个 `NewRoot` 调用点列「来源 → 来源种类 → 是否带连接 → RQ-050 的依据 → 守它的用例」：
- 入站：Connection，带连接，`InboundExecutorWireTests.AssertInboundJob`；
- 生命周期（没有原因时）：Lifecycle，带被关的连接，`ConnectionLifecycleDispatchTests`、`LifecycleExecutorWireTests`；
- 定时与调度、登录会话维护：Timer，`TimerSourceReconciliationTests`、`TimerSourceDownstreamTests`、`LoginSessionMaintenanceExecutorTests`；
- 卡牌计时器：Timer，新根（待裁定第 4 条），`DungeonCardPresentationCoordinatorTests.Timers`；
- GM／HTTP：Gm／Http，`HttpGameplayTests:53`（Gm）；Http 一侧的守护用例执行时检索确认，没有则在收口登记为只靠读码；
- 启动作业与启动恢复：StartupRecovery（`GmBootstrapService`、`GameServerStartupService` 两处、`JoustMaintenanceService` 启动分支、别的进程的 delivery），`DomainEventDispatchTests.Deliveries_left_by_an_earlier_process…`；
- `CommandBus` 执行器外分支：只有测试走到（`entry-reconciliation.md` §1.3），不改。
F-66 据此关闭；F-28 按待裁定第 4 条的裁定关闭。

**5.2 队列层泄漏流**（F-59，待裁定第 2 条，按建议 A 写）：`GameplayExecutor.RunAsync` 在深度检查之前加同一个判定：流上带着已结束作业的令牌即拒绝，返回 `GameplayLeakedFlowException`，不入队，并向取证源报一条新类 `queue-leaked-flow`（`GameplayAssertions.cs` 加常量，`bus-leaked-flow` 不变）。总线上的判定照旧在前，它的取证类与 L1、L2 用例不变。理由：本单元起链是显式推导的；泄漏流不经判定就入队，会成为深度 0 的新根，失控链因此能绕过深度上限。正式全量的取证证明生产中没有这样的入队（预期只有 Q1 一条）。

### 6. 深度常量复核（Outcome 5；RQ-052）

- 跑 `t12/subcheck.py`（复制为 `t29/subcheck.py`），得到注册数与分类；与 `event-chain.md` 的 52 行逐行比对订阅 ID、事件、处理器与发布点。发布点用 `grep -rn -E "\.(EnqueueAsync|DirectAsync)\("` 与该文件的发布列比对，另核对隐式边（`SqliteItemDomainStore` → `InventoryContentsChanged`）。
- 没有变化：L ＝ 10 成立，常量 32 ≥ max(2 × 10, 8) ＝ 20，判据不动。
- 有变化：按该文件的方法重算最长简单路径，写进日志；L 仍为 10 则照常；否则按停止条件 1。
- 本单元接上继承后，附录 A 的计数口径与实现一致：事件 CMD 与 CMD 登记的关闭各加一跳，入站、定时与传输层关闭是根。`t007-closure.md` 写这一句，附录 A 原文不动。

### 7. 日志字段（Outcome 6；RQ-054）

执行器的五类日志（开始、完成、取消、失败、深度拒绝）与自我等待拒绝都已带根 ID、父 ID、深度、来源与 CMD 类型（V4），守它的是 `GameplayExecutorLoggingTests` 两条与 `GameplayCallDepthTests`。本单元不改日志格式，只补证据：E1 断言事件 CMD 的完成日志里 `ParentId` 等于发布作业的链 ID、`Depth` 为 1；L1 对生命周期子链断言同样两项。开始日志是 Trace、完成是 Debug，生产默认级别下不输出；RQ-054 只要求字段，不要求级别，这一点写进收口，级别复核并入 F-121（T-009）。§4.2、§4.4 新增的 `Warning` 也带被拒链的根、父、深度。

### 8. 测试

**8.1 新增**（名字可按实际调整，含义不变；「基准红」「基准绿」用于 Required check 2）：

| 编号 | 位置 | 用例 | 断言 | 基准 |
|---|---|---|---|---|
| E1 | `DomainEventDispatchTests.Chain.cs` | `An_event_cmd_is_a_child_of_the_cmd_that_published_its_fact` | 以带连接的根发布：Reliable 与 LiveOnly 的事件 CMD 根 ID ＝ 发布作业的根 ID、父 ID ＝ 发布作业的链 ID、深度 1、来源 Event、来源连接继承；完成日志里的 `ParentId`、`Depth` 同值 | 红 |
| E2 | 同上 | `A_fact_published_by_an_event_cmd_extends_the_chain_by_one` | `Fixture` 加一个可选的转发订阅（新事件类型，只在开关打开时注册，不影响已有用例的组）；被转发事实的消费深度 2，祖先跳数依次为发布作业与转发 CMD | 红 |
| E3 | 同上 | `A_retry_keeps_the_chain_of_its_first_consumption` | 一次失败后重试：两次尝试的根 ID、父 ID、深度相同，链 ID 不同 | 红 |
| E4 | 同上 | `A_rolled_back_fact_leaves_no_chain_behind` | 发布后回滚，链存储里没有这条事实的项；正常完成后的项也已遗忘 | 不进 RED（要用新内部类型）；由 MT7 证明 |
| D1 | 同上 | `A_reliable_delivery_past_the_depth_limit_is_settled_terminal_and_its_root_connection_closed` | 在深度 32 的作业里发布（链由带连接的根逐级 `Child` 构造，执行器接受 32）：订阅处理没有执行；执行器恰有一条深度拒绝 `Error`，其 `Chain` 从根开始；该行 `state=2`；落定 CMD 是新根、深度 0、类型 `event:counter/depth-exceeded`；记录发送端收到根连接的关闭条目；再以该上下文调用 `DispatchDisconnectedAsync`，生命周期 CMD 是新根、深度 0 | 红 |
| D2 | 同上 | `A_live_delivery_past_the_depth_limit_is_completed_with_the_depth_reason` | 同 D1 构造，LiveOnly 那一行 `state=1`、理由为深度超限，投影没有执行 | 红 |
| D3 | 同上 | `A_chain_without_a_connection_past_the_limit_is_settled_and_only_logged` | 根为 Timer：落定照常；发送端没有任何关闭条目 | 红 |
| D4 | 同上 | `After_settling_a_delivery_past_the_limit_the_group_loop_waits_instead_of_spinning` | 选项 `RetryDelay` 10 ms、`RecoveryInterval` 1 分钟，跑组循环：落定后 500 ms 窗口内深度拒绝 `Error` 仍为 1 条，`ReadDueAsync` 调用次数不再增长（`Fixture` 的持久化包装加计数） | 红（基准上每 10 ms 拒绝一次） |
| L1 | `LifecycleCallChainTests.cs` | `A_disconnect_after_a_cmd_registered_close_is_a_child_of_that_cmd` | 带连接 A 的作业经 `OutboxConnectionCloser` 关闭 B，作业结束后 `DispatchDisconnectedAsync(B)`：根 ID ＝ 作业根、父 ID ＝ 作业链 ID、深度 1、来源 Lifecycle、来源连接 A；完成日志字段同值 | 红 |
| L2 | 同上 | `A_disconnect_after_a_failed_cmd_closed_its_source_is_a_child_of_that_cmd` | 作业体抛出、来源 S 被补登记关闭后，S 的断开是该作业的子 | 红 |
| L3 | 同上 | `A_disconnect_without_a_registered_close_is_a_new_root` | 为同 ID 的另一个上下文实例记过原因时，这个实例的断开仍是新根、深度 0 | 绿（对照） |
| L4 | 同上 | `A_close_cause_is_used_once` | 同一上下文先后两次断开分发：第一次是子链，第二次是新根 | 红 |
| L5 | 同上 | `A_teardown_whose_inherited_chain_is_past_the_limit_still_runs_and_closes_the_root_connection` | 深度 32 的作业关闭 B：B 的断开被执行器拒绝一次（一条 `Error`），拆除仍以新根执行（观察者都跑了），根连接 A 的关闭条目已提交 | 红（待裁定第 1 条不接受时按 §4.4 改断言） |
| Q1 | `GameplayQueueLeakTests.cs` | `Enqueueing_from_a_flow_leaked_from_a_finished_job_is_refused` | 作业内未抑制的续体在作业结束后直接调用 `IGameplayQueue.RunAsync`：抛 `GameplayLeakedFlowException`，工作没有执行，取证源记一条 `queue-leaked-flow` | 红（只在待裁定第 2 条接受时加） |
| Q2 | 同上 | `A_flow_detached_from_a_job_enqueues_as_a_new_root` | 同样的续体在 `RunDetached` 或 `SuppressFlow` 下启动：照常入队为新根 | 绿（对照，同上） |

D1～D3、L1、L2、L5 的记录发送端与连接上下文：`Fixture` 与 `LifecycleCallChainTests` 各自注册一个记录 `IOutboundSender` 与一个假的 `INetworkConnectionContexts`，经真实的 `GameplayOutboxes` 与 `OutboxConnectionCloser` 登记，不替换它们。发送端如何执行关闭条目已有 T-003、T-006 的用例守着，这里只断言条目到了发送端。

**8.2 改写的既有断言**（`LifecycleExecutorWireTests.cs`；加一个 `AssertLifecycleChild(seen, 被关的连接, 父链)` 辅助，`AssertLifecycleJob` 原样保留给其余调用点）：

| 调用点 | 用例 | 旧断言 | 新断言 | 保护意图 |
|---|---|---|---|---|
| `:96` | `A_cmd_closing_its_own_source_connection_does_not_wait_on_itself` | 深度 0，来源连接 ＝ 本连接 | 在执行器上；CMD 类型 `lifecycle:disconnected`；父 ID ＝ 入站作业（记录器 `inbound-enter` 的链）ID；根 ID 相同；深度 1；来源连接 ＝ 本连接 | 清理是执行器上的生命周期 CMD、晚于关闭它的 CMD（其余断言不动）；新增：继承 |
| `:114` | `A_login_refused_by_the_real_handler_closes_the_connection_and_cleans_up_on_the_executor` | 同上 | 同上，父链取最后一个祖先跳：来源 Connection、类型为登录协议的 `inbound:` CMD | 同上 |
| `:136` | `A_cmd_closing_another_connection_has_its_cleanup_run_after_the_cmd` | 深度 0，来源连接 ＝ 被关的空闲连接 | 父 ID ＝ 请求方入站作业 ID；深度 1；来源连接 ＝ 请求方连接 | 清理晚于请求方 CMD、请求方没有被断开（其余断言不动）；新增：继承与来源连接 |
| `:159` | `A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor` | 深度 0，来源连接 ＝ 被顶替的连接 | 深度 1；最后一个祖先跳来源 Http、类型为启动器登录的 `http:` CMD；来源连接为空 | 被顶替连接的清理在执行器上（F-65）；新增：继承 HTTP 登录 CMD 的链 |

改写前先确认这 4 处的关闭确实是 CMD 登记的（读用例与对应 handler）；若某处其实是传输层关闭，它的断言不改，从表中删去并写进 Result。用例名 `…closed_by_maintenance…` 与 F-65 之后的事实不符，本单元不改名（改名会改方法集合），记入收口。

**8.3 不改的**：表征测试 10 条与金样本；`InboundExecutorWireTests` 的深度 0（入站是根）；`ConnectionLifecycleDispatchTests` 的深度 0（直接分发、没有原因）；事件用例对来源种类的断言。

### 9. 变异（Required check 6）

| 变异 | 改动 | 应当变红 |
|---|---|---|
| MT1 | 分发器忽略存储的链，一律新根 | E1、E2、E3、D1、D2、D3、D4 |
| MT2 | 非终态失败后也遗忘链 | E3 |
| MT3 | 超限时只吞掉异常、不落定 | D1、D2、D3、D4 |
| MT4 | 守卫关闭改为普通（记原因）关闭 | D1（最后一步不再是新根） |
| MT5 | 生命周期分发器忽略关闭原因 | L1、L2、L4、L5；§8.2 的 4 处 |
| MT6 | 刷出时只为显式关闭记原因、不为失败来源关闭记 | L2 |
| MT7 | 回滚不撤销记录 | E4 |
| MT8 | 读取原因后不删除 | L4 |
| MT9 | 执行器深度检查 `>` 改为 `>=` | `GameplayCallDepthTests.Work_at_the_limit_runs…`；D1 的构造（深度 32 的作业被拒） |
| MT10 | 生命周期超限时不重新以新根入队拆除 | L5（只在待裁定第 1 条接受时） |
| MT11 | 去掉队列层的泄漏流判定 | Q1（只在待裁定第 2 条接受时） |

### 10. 文档

**10.1 `verification/t007-closure.md`**（新建，基准写明）：
- §1 RQ-050～RQ-054、AC-016 逐项：要求 → 实现位置（文件:行）→ 证据用例；RQ-012、RQ-014、RQ-041 的「继承链」一半（`t004-closure.md` 移交 T-007 的三行）；
- §2 各来源的根／父表（§5.1）；
- §3 裁定与局部决定：待裁定各条的裁定；`lifecycle:close-cleanup` 的去向；`SourceConnection` 不统一的理由（§4.3）；`GameplayDepthExceededException` 不进违规识别名单（§4.6）；链存储不属于玩法状态（§2.1）；
- §4 已知限制：§2.2 的孤儿项；§3.1 建议 A 的残余（CMD 登记关闭之后、发送端执行之前，若传输层先因交付失败或客户端断开而关闭，该次断开仍按 CMD 登记处理）；开始与完成日志的级别（§7）；
- §5 测试处置：N 的逐条名单与加法；§8.2 的 4 处「旧 → 新 → 意图」；
- §6 移交：附录 A 第 3、4、6、8 行与新系统 CMD 类型的最终对账（T-009）；用例改名（§8.2 末）。

**10.2 `follow-ups.md`**：关闭 F-26（继承一半与 `close-cleanup` 的裁定）、F-27、F-28（按裁定）、F-29、F-59（按裁定；不接受时改为「接受现状」并写理由）、F-66；续写 F-69（本单元与 TASK-27 的耗时对比）；文首补 TASK-29 一行；新事项从当时最大编号之后起编号（改道提案生效时为 F-123 起，否则 F-122 起）。

**10.3 注释**：`NetworkConnectionLifecycleDispatcher` 类上的 remarks、`DomainEventDispatcher.cs:90` 的「Transitional」注释改为现状；新代码的注释只写代码说不出的（AGENTS.md「注释」）。

**10.4 检索集**（Required check 8，先写预期再跑）：

| # | 检索 | 预期 |
|---|---|---|
| R1 | `grep -rn "NewRoot" --include='*.cs' Server/src` | V3 的 9 处 ＋ §2.3、§4.2、§4.4 新增的新根（事件无记录时的回落、落定系统 CMD、拆除重入队、守卫 CMD），逐行列出 |
| R2 | `grep -rn "\.Child(" --include='*.cs' Server/src` | 2 处：`DomainEventDispatcher`、`NetworkConnectionLifecycleDispatcher`（`GameplayCallChain.cs` 除外） |
| R3 | `grep -rn "Transitional: every delivery is a new root\|Every lifecycle CMD is a new root" Server/src` | 0 |
| R4 | `grep -rn "depth-exceeded" --include='*.cs' Server/src Server/tests` | 生产 2 处（两个系统 CMD 类型），测试引用逐行列出 |
| R5 | `grep -rn "queue-leaked-flow" --include='*.cs' Server/src Server/tests` | 待裁定第 2 条接受时：常量 1、报告 1、测试若干；否则 0 |
| R6 | `git diff <基准> -- Server/tests/Dfo.Server.Networking.Tests/Integration/LifecycleExecutorWireTests.cs` 的差异块 | 只在 §8.2 的 4 处与新辅助方法 |

### 11. 数量与取证预期

- **N**（按建议全部接受时 15）：Application 15 ＝ E 4 ＋ D 4 ＋ L 5 ＋ Q 2；Networking 0（只改断言）。预期总数 8247 ＋ 15 ＝ 8262；逐项目只有 Application 变化（4538 → 4553）。待裁定第 2 条不接受时 N 少 2（Q1、Q2）；第 1 条不接受时 L5 改断言、数量不变。
- **D**：0。
- **取证**：40 条；Q1 加入时 41 条（多出的为 `queue-leaked-flow` 反例）。E、D、L 用例都在执行器上写状态，深度拒绝不报取证。`exe:` 0。

### 12. 执行顺序

1. §1 核实（V1～V11），落盘 `task-29-verify.txt`；V1 不干净即停。
2. §6 事件图复核，落盘 `task-29-event-chain.txt`；触发停止条件 1 即停（此时工作区只有日志）。
3. 先写 §8.1 中能用现有 API 编译的用例与 §8.2 的新断言，不改生产代码，构建并跑：RED 段（Required check 2）。
4. 按 §2 → §3 → §4 → §5.2 实现，构建（`--no-incremental`），补上 E4 等要新内部类型的用例，跑新用例与 `DomainEventDispatchTests`、`ConnectionLifecycleDispatchTests`、`LifecycleExecutorWireTests`、`GameplayCallDepthTests`：GREEN 段；Application、Networking 过滤运行。
5. 正式全量与取证（Required checks 1、3、4），隔离复跑（check 12），×10／×3（check 7）。
6. §9 变异，重建。
7. 待裁定第 1 条接受时改 `spec.md` RQ-053；§10 的收口、`follow-ups.md`、注释与检索集。
8. 收口引用核对、diff 核对、边界、`cr-at-eol` 口径 `diff --check`（checks 9～11、13）。

### 13. 表征测试、金样本与生产行为

- 合法玩法下客户端可见的行为不变：深度只影响日志字段，合法链最长 10 跳，远低于 32。
- 只在失控链上出现的变化：超限的 delivery 被落定、根连接被关闭（RQ-053 本身规定，不属于附录 G）；生命周期超限时拆除照常执行（待裁定第 1 条）。
- 表征测试 10 条与全部金样本不变；有一条需要改，按停止条件 3 处理。

### 14. 范围来源的去向

| 来源 | 本单元 |
|---|---|
| Plan T-007「各类 CMD 来源设定根或父」 | §2、§3、§5.1 |
| Plan T-007「失败重试继承原链」；F-27 前一半 | §2.4，E3 |
| Plan T-007「事件 delivery 的进程内链表」；RQ-051 | §2.1、§2.2 |
| Plan T-007「超限处理」；RQ-053；F-27 后一半；F-29 | §4，D1～D4、L5 |
| Plan T-007「深度常量与判据」；RQ-052 | §6 |
| Plan T-007「日志字段」；RQ-054 | §7，E1、L1 |
| F-26；TASK-10 裁定第 4 条（`close-cleanup` 推迟到 T-007） | §3，待裁定第 3 条 |
| TASK-23 修订待裁定第 6 条（顶替关闭继承登录 CMD 的链） | §3.5，§8.2 `:159` |
| TASK-23 §3.2 末句（`SourceConnection` 与精确身份） | §4.3 |
| F-28 | §5.1，待裁定第 4 条 |
| F-59；TASK-18 裁定第 6 条 | §5.2，待裁定第 2 条 |
| F-66 | §5.1 |
| F-69 | Required check 3 |
| TASK-26 §3.4（`GameplayDepthExceededException` 与打包进程输出） | §4.6 |
| `t004-closure.md:103`、`:105`、`:108`（RQ-012、RQ-014、RQ-041 的继承一半） | §2、§3，`t007-closure.md` §1 |

不拆分的理由：两条继承路径共用「超限 → 落定或守卫关闭 → 新根」这一套处理，AC-016 的证据也要同时覆盖两者；生产改动集中在 Application 的执行与事件两个目录（约 8 个文件），测试集中在 Application 的三个新文件与一个线级文件的 4 处断言。拆成「事件」与「生命周期」两个单元，第一个单元结束时深度检查已能触发，而生命周期一侧的超限处理还不存在，中间态反而更难验证。

## 待裁定

1. **关连接引起的生命周期 CMD 超限时怎么办**（§4.4；RQ-053、RQ-012、AC-007）。
   - RQ-053 对一切超限都「不入队这条 CMD」。被拒的若是 `lifecycle:disconnected`，那个连接已经断开，没有任何别的路径会再拆除它：在线身份残留、位置不落库，与 RQ-012、AC-007 冲突。
   - **建议 A：拆除不丢。** 执行器照常拒绝并记那条 `Error`（含完整链）；分发器改以新根重新入队同一个拆除，再按 RQ-053(a) 关根连接。不会因此失控：每次这样的新根都要消耗一个已断开的连接，次数以连接数为上限。需要在 `spec.md` RQ-053 加一句，例如「(d) 超限的 CMD 是关连接引起的生命周期 CMD 时，改作新根执行，再按 (a) 处理根连接；连接拆除不因深度而丢失」，由本单元按裁定写入。
   - 备选 B：按字面拒绝，只关根连接。代价：失控链的最后一跳若是关闭别的连接，那个连接的拆除永远不会发生。L5 改为断言拆除没有执行，并在收口写明这一限制。

2. **队列层是否也拒绝泄漏流**（§5.2；F-59；TASK-18 裁定第 6 条）。
   - 今天只有总线拒绝带着已结束作业令牌的流。这样的流若直接调用 `IGameplayQueue.RunAsync`，会成为深度 0 的新根。本单元接上继承后，这就是一条绕过深度上限的路。
   - **建议 A：拒绝。** `GameplayExecutor.RunAsync` 加同一个判定，报新取证类 `queue-leaked-flow`，配反例 Q1 与对照 Q2；取证预期 41 条。生产中直接持有入队点的位置都是附录 B「生产者的入队侧」，不在作业流上（TASK-18 的 37 行检索），正式全量会证明这一点。这改动的是执行器入队点的契约（共享），所以请确认。
   - 备选 B：不改，F-59 记为「接受现状」，理由是生产中没有这样的调用点；T-009 判据加固时再议。

3. **`lifecycle:close-cleanup` 是否引入**（§3.2；F-26；TASK-10 裁定第 4 条）。
   - TASK-10 把它推迟到「能区分关闭发起者时」。本单元起可以区分，但区分的结果已经在链上：CMD 登记的关闭引起的断开有父，传输层关闭的没有。
   - **建议：不引入，CMD 类型保持 `lifecycle:disconnected`。** 理由：附录 A 第 3 行只列 `lifecycle:connected`、`lifecycle:disconnected`；新增类型要改附录 A、`entry-reconciliation.md` 的对账表与探针族，而观察者对两种断开做的事完全相同。F-26 的 `close-cleanup` 一半据此关闭。
   - 备选：引入新类型名，属于改 Spec 附录 A，本单元另需授权。

4. **卡牌计时器是否继承调度它的 CMD 的链**（§5.1；F-28）。
   - F-28 的标题问「定时源一律新根改为继承链」。RQ-050 写的是「定时……的 CMD 是新根」。卡牌的布局计时器由入站结算调度，自动翻牌由布局计时器调度，最多两级，不会自己形成环。
   - **建议：按 RQ-050 字面保持新根，不改 Spec。** F-28 据此关闭。
   - 备选：继承调度它的链（深度 ＋1），需要改 RQ-050，并改 `DungeonCardPresentationCoordinator`（Protocol 项目，不在本单元的 Owned boundary 内）。

5. **关闭原因在哪里记录**（§3.1；RQ-050 末句的实现精度）。
   - **建议 A：在 Application 内、发件箱刷出时记录**（本计划的写法）。不改任何共享契约，Networking 不动。残余：CMD 登记了关闭、发送端还没执行时，若传输层先因交付失败或客户端断开而关闭了这个连接，这次断开仍按 CMD 登记处理（继承链），而 RQ-050 字面要求它是新根。影响只在链的字段与深度上：这个连接本来就要被那条 CMD 关闭；附录 A 担心的「交付失败 → 关同伴 → 同伴的清理通告再失败」级联中没有 CMD 登记的关闭，仍全是新根。
   - 备选 P：在发送端执行关闭条目的那一刻记录。要给 `OutboundEntry.Close` 加原因字段，让 Networking 的发送端在关闭前写入一个 Abstractions 里的共享存储。精度更高，但改 T-003 的共享契约与 Networking 项目，超出本单元的 Owned boundary。
   - 请确认取 A 并接受残余（收口如实登记），或授权 P。

6. **两处判定口径**（§3.4、§4.3）。
   - (a) 作业体失败时补登记的来源关闭、发送端拒收整箱时的兜底关闭，算不算「CMD 登记进发件箱的关连接请求」。**建议：算**，它们都在这条 CMD 的发件箱里、因这条 CMD 而发生。
   - (b) RQ-053(a) 的「根来源是客户端连接」按链的 `SourceConnection` 非空判定。**建议：是。** 入站根与生命周期根都带连接；生命周期根的连接通常已断开，关闭只是空操作（记 Debug）。备选：只认来源种类为 Connection 的根，生命周期根（例如 `lifecycle:connected` 触发的失控链）只记日志。

## 主控方裁定（执行前）

0. **不拆分，接受。** 执行前提「工作区干净」接受：T-008 改道的修订提交落盘之后才开始，基准取那次提交。
1. **关连接引起的拆除超限时，改作新根重新入队，再按 RQ-053(a) 关根连接，接受。** 这是为了不丢拆除（RQ-012、AC-007）。由本单元在 `spec.md` RQ-053 加一句 (d)；这属于主控方授权的 Spec 修订，在 Result 中登记，并汇总进 A 的最终汇报，交外部审核时说明。Owned boundary 增加 `spec.md` 这一处。
2. **队列层也拒绝泄漏流（F-59），接受。** 在执行器入队点加同样的判定，新增取证类 `queue-leaked-flow`，配反例和对照。这改的是执行器入队点的共享契约，在 Result 中登记。
3. **不引入 `lifecycle:close-cleanup`（F-26），接受。**
4. **卡牌计时器按 RQ-050 字面保持新根（F-28），接受。**
5. **关闭原因在 Application 的发件箱刷出时记录（方案 A），接受。** 残余如下，在收口中如实登记：CMD 已登记关闭、发送端还没执行时，传输层若先因交付失败或客户端断开关掉了这个连接，这次断开仍按继承处理。这不符合 RQ-050 的字面要求。
6. **两处判定口径，接受。**
   - (a) 作业失败时补登记的来源关闭，以及发送端拒收整箱时的兜底关闭，都算作「CMD 登记的关闭」。
   - (b)「根来源是客户端连接」按链的 `SourceConnection` 非空来判定。

## Result

执行轮次：第 1 轮（基准 `bc735cd0`，`Server/src` 与 `06ca5e0f` 相同；改动未提交）。

- **Outcome:** done（Result review 为 required，提交前待主控方安排独立复核）。
- **Changed:**
  - 事件 delivery 的调用链：新建 `Server/src/Dfo.Server.Application/DomainEvents/DomainEventCallChains.cs`（按「事件 ID ＋ 订阅 ID」存发布它的 CMD 的链，进程内存，表结构不变）；`EventService.cs` 在作业上发布时为每个目标订阅记链，事件批作为事务参与者在回滚时撤销；`DomainEventDispatcher.cs` 的 `ChainOf` 取子链（来源 Event），别的进程的 delivery 是启动恢复新根，落定（Reliable 完成、LiveOnly 消费决定、终态失败、超限落定）之后遗忘，非终态失败不遗忘；`GameplayTimerWork.cs` 加接收链的 `RunAsync` 重载，原重载改为调用它；`DomainEventDispatchingExtensions.cs` 注册存储。
  - 关连接引起的生命周期 CMD：新建 `Server/src/Dfo.Server.Application/Execution/GameplayCloseCauses.cs`（`GameplayCloseCauses`：按上下文实例的弱引用表；`GameplayDepthGuard`：深度守卫的无因关闭）；`GameplayOutbox.cs` 在交给发送端之前为 CMD 登记的关闭（含作业失败补登记的来源关闭、拒收整箱时的兜底关闭）记原因，`RequestCloseWithoutCause` 的条目不记；`GameplayQueue.cs` 注册两者，`GameplayOutboxes` 改由工厂构造（新增两个 internal 构造器，原有两个 public 构造器不变）；`NetworkConnectionLifecycleDispatcher.cs` 取出原因并取其子链，没有原因仍是新根，类上 remarks 改为现状。
  - 超限处理：事件 delivery 超限由新根系统 CMD `event:<订阅 ID>/depth-exceeded` 落定（Reliable `FailAsync(terminal: true)`，LiveOnly `TryBegin` 后以 `Depth limit exceeded; live hint skipped.` 完成），记一条 `Warning`，再关根连接；生命周期拆除超限时以新根重新入队拆除，再由守卫 CMD `lifecycle:depth-exceeded` 关根连接（根连接就是被拆除的连接时省掉）。执行器的拒绝与 `Error` 日志未改。
  - 队列层泄漏流：`GameplayExecutor.cs` 在深度检查之前拒绝带着已结束作业令牌的流；`Server/src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs` 加常量 `QueueLeakedFlowKind = "queue-leaked-flow"` 与一个带取证类参数的 `LeakedFrom` 重载（原重载改为调用它）。
  - `spec.md` RQ-053 按主控方裁定第 1 条加 (d) 一句（主控方授权的 Spec 修订，交外部审核时说明）。
  - 测试：`DomainEventDispatchTests.cs` 只扩展 `Fixture` 与辅助类型（`chain`、`relay` 两个开关、记录发送端与连接上下文、生命周期观察者、`ReadDueAsync` 计数），该类已有 28 个用例方法（`.cs` 17 ＋ `.Executor.cs` 11）逐字节不变；新建 `DomainEventDispatchTests.Chain.cs`（E1～E4、D1～D4）、`Dispatching/LifecycleCallChainTests.cs`（L1～L5）、`Execution/GameplayQueueLeakTests.cs`（Q1、Q2）；`LifecycleExecutorWireTests.cs` 改写 4 处断言并加 `AssertLifecycleChild`，「旧 → 新 → 意图」对照在 `verification/t007-closure.md` §5.2。
  - 文档与证据：新建 `verification/t007-closure.md`；`verification/follow-ups.md` 关闭 F-26、F-27、F-28、F-29、F-59、F-66，续写 F-69，新增 F-123，文首补 TASK-29 一行；`verification/logs/task-29-*.txt` 12 份（脚本全文附在各自末尾）。
- **Why:**
  - 关闭原因按上下文实例记（`ConditionalWeakTable`），因为 `NetworkConnectionContext` 是 record，按值比较会把同 ID 的新连接当成同一个；V8 核对过三处取到的是同一个实例。
  - `GameplayOutbox` 用条目所在 slot 的标记区分无因关闭，不另设字段：第 1 轮正式全量中 `ContentWarmupTests.W2` 把首版的 `??=` 字段判为首次使用的索引持有者。
  - `GameplayOutboxes` 改为工厂注册：`GameplayCloseCauses` 是 internal，不能出现在 public 构造器里，而 DI 只用 public 构造器。
  - 「只加一个取证类常量」做不到：取证源的 `Report` 是 Abstractions 的内部成员，所以加了一个带取证类的 `LeakedFrom` 重载。这是 Owned boundary 内同一文件的最小扩展，登记在此。
- **Checks:**
  1. 构建 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：RED、实现后、两轮正式全量的第 1 部分、两次变异后的重建，全部 0 警告 0 错误。
  2. RED／GREEN（`logs/task-29-red-green.txt`）：RED 中 Application 跑 14 条 ＝ 红 12 ＋ 绿 2（对照 L3、Q2），Networking 4 处改写断言全红；红的原因都是链字段（新根而非子链）、没有发生的深度拒绝或未落定／未关闭（逐条在日志文首）。E4 要新内部类型才能编译，不进 RED，由 MT7 证明。14 ＋ 1 ＝ 15。与计划不同：D4 在基准上红的原因是「没有任何拒绝」（基准不继承链，什么都不会超限），不是计划预测的「每 10 ms 拒绝一次」；空转由 MT3 证明。GREEN：Application 15 通过、Networking 4 通过。
  3. 正式全量（`logs/task-29-build-test.txt`，记录轮为第 2 轮）：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 181 ＋ Application 4553 ＋ Host 195 ＝ 8262 ＝ 8247 ＋ 15 － 0（逐步：54 ＋ 80 ＝ 134；＋ 1752 ＝ 1886；＋ 477 ＝ 2363；＋ 970 ＝ 3333；＋ 181 ＝ 3514；＋ 4553 ＝ 8067；＋ 195 ＝ 8262）；N ＝ E 4 ＋ D 4 ＋ L 5 ＋ Q 2 ＝ 15，只在 Application（4538 ＋ 15 ＝ 4553）。通过 54 ＋ 80 ＋ 1752 ＋ 477 ＋ 968 ＋ 181 ＋ 4535 ＋ 195 ＝ 8242，失败 2（基线的 2 条 `DimensionDungeonTests`），跳过 2 ＋ 16 ＝ 18；8242 ＋ 2 ＋ 18 ＝ 8262。挂起转储 0。表征测试 10 通过、0 跳过，`Characterization/` 无改动。日志信号与 TASK-27 逐条相同（必须为 0 的七项中 `GameplaySelfWait` 1，其余 6 项 0；bus 8、inbound 8、gm 2），唯一差别是 `the drain tears it down` 2（TASK-27 4，随负载变化，排空路径未改）；深度拒绝与落定行在控制台输出中 0 条（新用例用各自的记录器断言）。第 1 轮另有 2 条失败：`ContentWarmupTests.W2`（真实问题，已修）与 `ComponentLifecycleTests(http: True, game: True)`（端口在宿主绑定前被占，F-55 形状；修复后与 W2 一起隔离 ×3 全过）。耗时：第 2 轮 180 s，开跑时 swap 已用 11052 MB。F-69：比 TASK-27 多出 ≥ 9 秒的 3 条，单独复跑两次各 1～4 秒，满载拉长。
  4. 零违规取证（`logs/task-29-violations.txt`）：预期先写在日志开头。41 条 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 37（2 ＋ 1 ＋ 1 ＋ 37 ＝ 41）；反例 24 ＋ 1 ＋ 1 ＋ 3 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＝ 34，对照 7，34 ＋ 7 ＝ 41；多出的 1 条就是 Q1；反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。`violations.sh` 只加了 `GameplayQueueLeakTests=1` 一类。
  5. 事件图复核（`logs/task-29-event-chain.txt`）：52 ＝ Reliable 17 ＋ LiveOnly 22 ＋ Direct 13；与 `event-chain.md` 的基准 `b76504bb` 逐项比对，注册与发布点（71 对）都无增删；图上 49 个文件 ＝ 未变 41 ＋ 有改动 8，后者的改动不涉及发布、关连接或物品写入。L ＝ 10，常量 32 与判据不动。
  6. 变异（`logs/task-29-mutations.txt`）：MT1～MT11 都让指定用例变红（MT9 另多红 D2、D3、D4、L5，均为深度 32 的构造被一并拒绝）；MT5 第一次因未使用的构造参数编译失败（CS9113），改正变异写法后单独重跑通过；每项 `cmp` 还原、`git diff`（含未跟踪新文件）sha1 前后相同；重建 0／0。
  7. 新增 ×10（`logs/task-29-new-x10.txt`）：15 条 ×10 全过；`DomainEventDispatchTests` 38 条、`ConnectionLifecycleDispatchTests` 6 条、`LifecycleExecutorWireTests` 7 条各 ×3 全过。
  8. 检索集（`logs/task-29-retrievals.txt`）：R1 19 行 ＝ 定义 1 ＋ V3 不变的 7 处 ＋ 三个改动文件中的 11 行（1 ＋ 7 ＋ 2 ＋ 3 ＋ 6 ＝ 19）；R2 2；R3 0；R4 生产 2、测试 1；R5 常量 1、执行器 1、Q1 1；R6 只在 4 处与新辅助方法。
  9. 收口引用核对（`logs/task-29-closure-refs.txt`）：用例名 32、日志 6、F 编号 11（6 ＋ 4 ＋ 1 ＝ 11），0 处不符。
  10. 测试改动核对（`logs/task-29-diff-check.txt`）：方法删除 0、新增 15，正是 N；断言行多重集合有变化的已有文件只有 `LifecycleExecutorWireTests.cs`（27 → 36），在 4 处与新辅助方法；`DomainEventDispatchTests.cs` 断言行 73 → 73，17 个用例方法逐字节不变，5 个差异块都在 `Probe`、`Fixture`、`FaultingRetryPersistence` 内。
  11. 边界（`logs/task-29-boundary.txt`）：`Server/src` 改 11 个文件（修改 9 ＋ 新建 2），都在 Owned boundary，只涉及 Application 与 Abstractions 的 `GameplayAssertions.cs`；`OutboxAdapters.cs`、`LifecycleWireHost.cs` 不需要改，未改；`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`、表征目录、以往收口文件无改动；`spec.md` 只有 (d) 一行。
  12. 隔离复跑（`logs/task-29-isolated.txt`）：Application 单独 4553 ＝ 4535 ＋ 2 ＋ 16，取证 32（反例 31 ＋ 对照 1），类外 0。Networking 单独两次各有 1 条 `Connection refused`（两次不同用例：`SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup…`、`EplpRechallengeFrameTests.Native_check…`），都在客户端连接这一步，与 F-55／F-103 形状一致，各自单独 ×3 全过、表征目录 10 条全过，正式全量中也都通过；取证两次都是 4 条（反例 3 ＋ 对照 1），类外 0。
  13. `git -c core.whitespace=cr-at-eol diff --check` 无输出；改动的已有文件 CR 数与基准相同（都是 0），新文件 LF。`dotnet build-server shutdown` 已执行；`pgrep -x yes` 为 0。
- **Remaining:**
  - Result review 为 required：提交前需要主控方安排独立复核（重点：继承规则与新根的划分、落定条件、4 处线级断言改写、裁定 1／2／5 的落地）。
  - 与计划不同、已在收口与日志中写明的三处：`:159` 的父链是总线执行器外分支的 `bus:AuthenticateAndIssueTicketCommand`（来源 Connection、无连接），不是计划写的 `http:` CMD，因为这个用例经 `TestCredentials.LoginAsync` 直接调用总线；`GameplayAssertions.cs` 多了一个 `LeakedFrom` 重载；D4 在基准上的红因（见 Checks 2）。
  - F-123（用例改名）与 F-99、F-121 交 T-009；裁定第 5 条的残余登记在 `t007-closure.md` §4。

## 主控方处置（评审后，提交前）

独立快审结论为 `approved`（`reviews/task-29-result-review.md`）。5 条发现都不阻断，提交前处理如下：
- 第 1、2、5 条补记到 `t007-closure.md` 文末「评审后补记」；其中 HTTP 根继承缺直接用例一条移交 T-009。
- 第 3 条：Result 中「28 个」的口径改为「`.cs` 17 ＋ `.Executor.cs` 11」。
- 第 4 条：`DomainEventDispatchTests.cs` 补上缺的空格，只改格式。

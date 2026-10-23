# TASK-31 — T-009b：日志面与保护用例（代码冻结前的最后一个改代码单元）

基准提交 `122576d5`（TASK-30／T-009a 已提交）。本单元是 `tasks/TASK-30.md` §0 拆分中的第二个单元；边界提纲见 TASK-30 §0.2 的 TASK-31 一行，依据去向见 §0.3。**本单元完成并提交后代码冻结**：TASK-32～TASK-34 不再改 `Server/src` 与 `Server/tests`（§12）。

与 TASK-30 §0.2 提纲的差别：
- 提纲第 ⑩ 项（F-98 的发送端阶段语义）不做：TASK-30「主控方裁定（执行前）」第 3 条已定为「附录 G 加 VC-10，由 TASK-33 写入，不给发送端加阶段语义」。
- 提纲第 ① 项（F-30）细化为 §2 的普查规则；执行器的失败日志加一个连接字段（§2.3），用来补上生产者那条 `Error` 降级后丢失的连接关联。

范围取自：
- `verification/follow-ups.md` 中留在 A、归 T-009 的 F-30、F-31、F-32、F-33、F-87（改名一半）、F-111、F-121、F-123，以及 F-69（续写）；
- `verification/t007-closure.md` §4 第 3 条（开始与完成日志的级别，并入 F-121 复核）与文末「评审后补记」第 1 条（HTTP 根的继承只靠读码）；
- `spec.md` 修订记录 R3-1 中 AC-009 的 A 一半第 ③ 条（「经真实入站路径的反例……若 A 中尚无，在 T-009 补」）；RQ-031(c)、DR-15（handler 抛异常 → 断线）、RQ-036（发送失败的处理，日志面）、RQ-054（执行日志字段）、RQ-082（可见行为只允许附录 G 所列）、RQ-083（测试增减与断言变化逐条说明）；
- `verification/measurement/m2-summary.md` §1「F-119 的裁定」与文末第 857 行（就绪探测连接的豁免要写成规则，交 T-009），以及 scratchpad `t28/signals.py` 的发送端告警识别方式（§4.4 的交接依据）；
- `tasks/TASK-14.md` 第 2 轮 Remaining 第 9 条与 §2.2 的预缓冲规则（F-33）；`tasks/TASK-09.md`「交给 T-008」N-1（F-32）；`logs/task-25-doubles.txt`（F-111）。

`reviews/task-30-result-review.md` 两轮和 TASK-30 文末两次主控方处置都没有交给本单元的事项。

本单元的产出：作业体失败时只剩执行器那一条 `Error`（F-30）；「执行器已停止」的日志带停止原因（F-31）；发送端把「连接已不在」的 `Failed` 降为 Information，真正的发送失败仍为 Warning（F-121），并写出给 TASK-32 的 S5 判定口径；三条新的保护用例（作业内 handler 抛异常断线 F-32、HTTP 根的继承、经真实连接的效果重放 AC-009③）；GM 预缓冲在上限特性只读或缺失时与基准等价（F-33）；删除无人使用的测试替身（F-111）；26 个用例改名（F-87、F-123）；收口记录 `verification/t009-logs-and-cases.md`。

输入：
- TASK-30 的正式全量 `logs/task-30-build-test.txt`（8301 条；第 1 轮评审后折算为 8302 条，Application 4590）、取证 `logs/task-30-violations.txt`（71 条；折算 73 条）、原始输出 scratchpad `t30/formal-raw.txt`；
- 脚本：scratchpad `t30/formal.sh`、`t30/violations.sh`、`t30/mutate.py`、`t30/closure_refs.py`、`t30/methods.py`、`t30/boundary.py`、`t30/analyze.sh`、`t19/analyze.py`、`t07/diffcheck7.py`、`t21/methods.py`、`t24/elapsed.py`；缺失时从 `logs/task-30-*.txt` 末尾附的全文恢复。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **F-30 双 Error**（§2；RQ-054 的日志面）：按 §2.1 普查「在作业外等待作业结果、并在 catch 里记录异常」的生产者与中间件；作业体失败（`GameplayJob.EndedAJob(error)` 为真）时，它们不再记 `Error`／`Critical`，按 §2.2 降级；作业被拒（执行器已停止、深度超限）的路径不变。执行器的失败日志加 `{SourceConnection}` 字段（§2.3）。
  2. **F-31 停止原因**（§3）：遇到 `GameplayExecutorStoppedException` 的三处日志附上异常对象；`InnerException` 非空（消费循环故障）时，事件组循环那一行由 Information 升为 Warning。
  3. **F-121 发送端日志级别**（§4）：`Failed` 结果在连接已不在时降为 Information，消息带固定后缀；`TimedOut`、`OverLimit` 以及连接仍在时的 `Failed` 照旧为 Warning。`OutboundOutcome` 与观察者收到的报告不变。开始与完成日志的级别复核结论为「维持」（§4.3）。给 TASK-32 的 S5 判定口径写进收口记录（§4.4）。
  4. **F-32 线级用例**（§5.1；DR-15、RQ-031(c)）：作业内的生产 handler 抛异常，该连接收不到回包并被关闭，其余连接照常服务，日志里只有执行器那一条 `Error`。
  5. **HTTP 根的继承用例**（§5.2；RQ-050；`t007-closure.md` 评审补记）：经真实 HTTP 登录顶替一条已登录的游戏连接，被顶替连接的 `lifecycle:disconnected` 是 `http:` CMD 的子。
  6. **AC-009 的 A 一半第 ③ 条**（§5.3）：经真实 TCP 连接重发一个已提交的效果请求（同一帧序号），得到重放、不二次扣除、连接不断。
  7. **F-33 预缓冲等价**（§6；RQ-082）：`IHttpMaxRequestBodySizeFeature` 只读或缺失时不设缓冲上限，由服务器当前的请求体上限照基准那样生效；可写时保持现状。配单元测试；B1 三种形态的结果不变。
  8. **F-111**（§7.1）：删除四个没有任何用例使用的测试替身类型。
  9. **F-87、F-123 改名**（§7.2；RQ-083）：名字里写着已删除机制的 26 个用例改名，断言不改；新旧名对照写进收口记录。
  10. **收口**（§10）：新建 `verification/t009-logs-and-cases.md`；`follow-ups.md` 关闭 F-30、F-31、F-32、F-33、F-87（改名一半）、F-111、F-121、F-123，续写 F-69，文首补 TASK-31 一行。
  11. **一次正式全量**：带取证汇；反例类与对照以外为 0，`exe:` 为 0；表征测试 10 通过、0 跳过；变异核对（§9）。
- Owned boundary：
  - **生产**（只改日志调用、日志级别、catch 的过滤条件与 §4、§6 的判定；不改方法签名、可见性与业务逻辑）：
    - `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs`（入站两处；生命周期关闭回调按 V5 的结果）；
    - `Server/src/Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs`（`HttpApiExceptionMiddleware`）；
    - `Server/src/Dfo.Server.HttpApi/HttpGameplay.cs`（`BufferBodyAsync`，F-33；方法可见性只在 §6.2 需要时由 `private` 改为 `internal`，并在 Result 登记）；
    - `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs`（失败日志的模板加一个字段，§2.3）；
    - `Server/src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs`（`RunGroupAsync` 两个 catch）；
    - `Server/src/Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs`（F-31）；
    - `Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs`（F-31；F-30 的失败回调按 §2.1 普查结果）；
    - `Server/src/Dfo.Server.Networking/Outbound/OutboundSender.cs`（F-121）；
    - §2.1 普查新认出的生产者文件，只改 catch 的过滤条件与日志级别，在 Result 中逐个列出。规划时读到的候选见 §2.1 表。
  - **测试**：
    - 新增用例所在文件：`Dfo.Server.Networking.Tests/Integration/HandlerFailureConnectionIntegrationTests.cs`（F-32）、新建 `Dfo.Server.Networking.Tests/Integration/EffectReplayWireTests.cs`（AC-009③）、`Dfo.Server.Host.Tests/CredentialEntryHttpTests.cs`（HTTP 根）、`Dfo.Server.Networking.Tests/Outbound/OutboundSenderTests.cs`（F-121）、`Dfo.Server.HttpApi.Tests/HttpGameplayTests.cs`（F-33、中间件），以及 §2、§3 的单元用例所在的 `Dfo.Server.Application.Tests/Execution/GameplayExecutorLoggingTests.cs`、`Dfo.Server.Application.Tests/DomainEvents/DomainEventDispatchTests.cs`、生产者的既有测试文件（执行时列出）；
    - **允许改断言的既有位置**（只限日志断言，§2.4）：`OutboxWireTests.cs` 中两处 `Count("Game handler failed") == 1`、`InventorySlotMappingFailureIntegrationTests.cs` 的 `ErrorsContaining("Game handler failed")`，以及 V6 检索出的其他断言生产者 `Error` 日志或发送端 `Warning` 日志的位置（执行时逐处列出）；
    - F-111：`PartyNotificationTests.cs`、`PartyTownProjectionDeliveryTests.cs`、`SessionGenerationOwnershipTests.cs` 只删替身类型；
    - F-87、F-123：§7.2 表中 26 个方法所在的 17 个文件，只改方法名；
    - `Dfo.Server.Networking.Tests/Integration/LifecycleWireHost.cs`：只在 §2.4 需要时给 `LifecycleLog` 加按级别计数的成员；
    - 测试项目的 csproj 只在 §6.2 需要 `InternalsVisibleTo` 时改动，在 Result 登记。
  - **文档与证据**：`verification/t009-logs-and-cases.md`（新建）、`verification/follow-ups.md`、`verification/logs/task-31-*.txt`；一次性脚本放 scratchpad `t31/`，全文附在对应日志末尾。
  - **不改**：
    - 共享契约：`OutboundOutcome`、`OutboundDeliveryReport`、`IOutboundDeliveryObserver`、`IGameplayQueue`、`GameplayExecutorStoppedException`、`GameplayCallChain`；`GameplayAssertions.cs`；数据库表结构；
    - 执行器开始、完成、取消三条日志的级别与模板（§4.3）；`CommandLoggingBehavior`（待裁定第 5 条）；
    - `spec.md`、`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`；`Characterization/` 与金样本；以往单元的收口文件（`t00x-closure.md`、`t009-criteria.md` 等）与 `entry-reconciliation.md`。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。RED 阶段一次，全部改完一次，变异做完之后一次。
  2. **RED／GREEN**（`logs/task-31-red-green.txt`）：
     - 先只加新用例、不改生产代码：§2、§3、§4、§6 的新用例变红，失败原因逐条是「旧行为」（多了一条生产者 `Error`、日志没带异常、级别是 Warning、缓冲上限抛错等）；§5 的三条保护用例在基准上就应为绿（它们证明今天的行为），若为红按停止条件 2、3 处理；
     - 改名与删替身完成后：按项目过滤跑 Application、Protocol、Networking、Host、HttpApi，方法集合只按 §7.2 的对照变化；
     - 全部改完：新用例全绿，受影响项目全绿（基线 2 条除外）。都记命令、退出码与关键输出。
  3. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-31-build-test.txt`。
     - **总数**：基准 8302 ＝ HttpApi 54 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 183 ＋ Application 4590 ＋ Host 196（TASK-30 的正式全量 8301 加第 1 轮评审后的 1 条，折算值，本单元是第一次实测）。本单元为 8302 ＋ N － D，逐项目写加法；N 逐条列名（预期见 §11），D ＝ 0；改名的 R ＝ 26 个方法不计入 N、D，单列对照。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）、AGENTS.md 已登记的偶发项（三样一起取身份），以及待裁定第 6 条取建议时的 F-125（按该条的身份判定）；打包进程用例在满载下的就绪超时，按 F-55／F-103 的形状取身份并隔离复跑；挂起转储 0。
     - **跳过**：18，不变（AC-009③ 用例是 `PvfFact`，本机有档案，不跳过）。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动。
     - **日志信号**：`t30/analyze.sh` 的同一套信号，与 TASK-30 逐条对照。预期的差别先写进日志开头：`Gameplay inbound:… failed` 多 1（F-32 新用例）；`Outbound connection … closed` 中 `Failed` 一类可能有一部分换成带后缀的 Information 行（§4.2），逐行说明；其余与 TASK-30 相同。
     - 耗时与内存只作信息（RQ-091 用户修订）。F-69：用 `t24/elapsed.py` 与 `t30/formal-raw.txt` 逐条对比（改名的 26 个按 §7.2 对照配对），多出 ≥ 9 秒的逐条隔离复核。
  4. **零违规取证**（`logs/task-31-violations.txt`）：命令同 TASK-30 Checks 4。预期先写进日志开头再跑：73 条（＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 69），本单元不加也不减取证记录。`t30/violations.sh` 复制为 `t31/violations.sh`，只在改名影响分类时改用例名，改动写进日志。反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  5. **F-30 普查输出**（`logs/task-31-producers.txt`）：§2.1 检索式的全部命中、每个命中的归类（作业外等待 ＋ 记录异常／作业体内／不涉及），归为「作业外等待」的每处改成什么级别；与 §2.1 规划表逐行对照。
  6. **变异核对**（`logs/task-31-mutations.txt`；程序同 TASK-30 Checks 6：备份、逐字节还原后 `cmp`、前后 `git diff -- Server/src` 与 `-- Server/tests` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§9 每一项都有指定的用例变红。
  7. **新增测试 ×10**（`logs/task-31-new-x10.txt`）：新增方法隔离连续 10 次全过；`HandlerFailureConnectionIntegrationTests`、`EffectReplayWireTests`、`OutboundSenderTests`、`HttpGameplayTests` 全类 ×3。
  8. **检索集**（`logs/task-31-retrievals.txt`，§10.3）：每个检索式先写预期再跑。
  9. **收口引用核对**（`logs/task-31-closure-refs.txt`）：`t30/closure_refs.py` 复制为 `t31/closure_refs.py`，范围改为 `t009-logs-and-cases.md`；口径同 TASK-30 Checks 9，0 处不符；改名的旧名只允许出现在 §7.2 对照表的「旧名」一栏。
  10. **测试改动核对**（`logs/task-31-diff-check.txt`）：`diffcheck7.py <基准> --lines` 加 `t21/methods.py`：方法集合 ＝ 基准 － 26 个旧名 ＋ 26 个新名 ＋ N；除改名外没有方法消失；断言行多重集合有变化的已有文件只有 Owned boundary「允许改断言的既有位置」，逐处写「旧断言 → 新断言」；只承担改名的文件除方法名一行外无改动（`DomainEventDispatchTests.cs` 同时有新用例，按新增与改名分开核对）；F-111 的三个文件只少了替身类型的行。
  11. **边界**（`logs/task-31-boundary.txt`）：`git diff --stat <基准> -- Server/src` 只有 Owned boundary 的文件；生产 diff 的每一处改动按形状归类（日志级别、日志模板、catch 过滤、§4 的判定、§6 的缓冲参数），白名单外的逐条列出；`spec.md`、`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`、表征目录、以往收口文件无改动。
  12. **隔离复跑**（`logs/task-31-isolated.txt`）：Application、Networking、Host、HttpApi 各一次，都带取证目录，记录各自的反例类与对照记录数。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出；改动的已有文件行尾与基准一致（执行前逐个记录 CRLF／LF），新建文件 LF。结束时执行 `dotnet build-server shutdown`；`pgrep -x yes` 为 0。
- Dependencies：
  - TASK-30（`122576d5`）：普查与挂点已在基准上，本单元新增的生产者改动不碰写断言；
  - TASK-29（T-007）：调用链、关闭原因（`GameplayCloseCauses`）与 `LifecycleExecutorWireTests` 的 `AssertLifecycleChild`，§5.2 沿用；
  - TASK-23～TASK-25（T-006）：发送端、发件箱登记适配器与线级测试宿主；
  - TASK-14（T-004 GM 一段）：预缓冲与 B1 三形态（`GmGameplayRouteTests`）；
  - **执行前提**：工作区干净（除本任务目录下未提交的本文件）；待裁定第 1～6 条有裁定。
- Result review：required。
  - 改的是执行器、发送端、入站插件与 HTTP 中间件的日志面，TASK-32 的 S5 判定直接读这些日志（§4.4）。日志级别错一级，全量测试与线级测试多半照样通过（多数用例不断言日志），错误要到 M-3 判定时才暴露，那时代码已经冻结。
  - F-121 的「连接已不在」判定若写宽，一条真正的发送失败会被降为 Information，S5 就看不到它；这一点只有审查与 §4 的成对用例能拦住。
  - 26 个改名改的是用例身份，后面的对账（TASK-33 的 AC-009 清单、TASK-34 的测试数量加法）都按新名字引用。
  - F-33 改的是 GM 请求体的读取路径，与基准的等价靠推理加单元测试，Kestrel 层的只读情形没有线上复现手段（§6.3）。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. §2.1 普查出的某个生产者要改结构才能区分「作业体失败」与「拒绝入队」（例如它把异常包装后再 catch，`EndedAJob` 看不到），不能只改 catch 与级别。
    2. §5.1 或 §5.2 的用例在基准上为红：作业内 handler 抛异常没有断线、或顶替关闭的父链不是 `http:` CMD。这是 DR-15 或 RQ-050 的真实缺口，修法属于执行器或发件箱，要回到主控方。
    3. §5.3 找不到一个能经真实连接驱动、且不改生产代码与表征目录的效果请求；或重发得到的不是重放（二次扣除、断线）。后者是 RQ-023／AC-009 的真实缺口。
    4. F-33 的等价性无法用 §6 的办法证明，或需要改 B1 三形态的断言（按 RQ-082 回到主控方）。
    5. 线级测试中对线上字节、帧序、连接开关的断言需要改动；日志断言的改动超出 Owned boundary「允许改断言的既有位置」以外。
    6. 需要改「不改」中的共享契约、表结构、`spec.md`、`plan.md`、`AGENTS.md`；表征测试或金样本需要改动。
    7. 正式全量出现基线、已登记偶发项与待裁定第 6 条以外的失败；取证出现 `exe:` 记录，或取证总数不是 73。
  - 变异期间允许临时改动 `Server/src` 与测试，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 1. 基准核实（执行时重跑并落盘 `logs/task-31-verify.txt`）

| # | 问题 | 检索 | 规划时在 `122576d5` 上的结果 |
|---|---|---|---|
| V1 | 工作区与基准 | `git status --short`；`git log -1` | HEAD `122576d5`；工作区干净（本文件除外） |
| V2 | 测试总数与取证基线 | 读 `logs/task-30-build-test.txt`、`logs/task-30-rework.txt`、`logs/task-30-violations.txt` | 实测 8301、折算 8302（Application 4590）；失败 2、跳过 18；取证实测 71、折算 73 ＝ 2 ＋ 1 ＋ 1 ＋ 69 |
| V3 | 入站插件的双 Error | 读 `NetworkPlugins.cs:79-100`、`:166-195` | Channel、Game 两处在 `EndedAJob` 判定之前先记 `Error`（`Channel handler failed`、`Game handler failed`），再按 `EndedAJob` 决定返回或重抛 |
| V4 | HTTP 中间件 | 读 `HttpApiApplicationExtensions.cs:235-265` | `catch (Exception) when (!HasStarted)` 记一条不带异常的 `Error`（`HTTP API request failed …; TraceId=…`）后回 500；没有区分作业体失败 |
| V5 | 生命周期回调 | 读 `NetworkPlugins.cs:22-59`；查 TouchSocket 插件异常是否进 `ILogger`（`NetworkingModule` 的日志配置；必要时写一个只在 scratchpad 运行的探针） | `OnTcpConnected` 已对作业体失败静默返回；`OnTcpClosed` 没有 catch，作业体失败会抛给 TouchSocket。若 TouchSocket 不经 `ILogger` 记录，则 F-30 在这一处不成立，只记录；若记录，按 §2.2 加与 `OnTcpConnected` 相同的 catch |
| V6 | 断言生产者 `Error` 或发送端 `Warning` 的既有用例 | `grep -rn "handler failed\|HTTP API request failed\|could not persist or scan\|sweep failure\|timer failed\|exhausted\|closed: Failed\|Outbound entry for" --include='*.cs' Server/tests`，逐处读断言的是级别、文本还是次数 | 规划时已知：`OutboxWireTests.cs:73`（`== 0`，不受影响）、`:105`、`:127`（`== 1`）、`OutboxWireTests.cs:184`（`closed: TimedOut`，不受影响）、`InventorySlotMappingFailureIntegrationTests.cs:84`。其余执行时列出 |
| V7 | `OutboundOutcome` 的归属 | `grep -n "enum OutboundOutcome" -r Server/src` | 在 `Dfo.Server.Abstractions/Execution/GameplayOutbound.cs:166`，是共享契约，本单元不加成员 |
| V8 | 发送端「连接已不在」的可判定性 | 读 `OutboundTransport.cs` 的 `Resolve` 与 `Connection.SendAsync`；读 `OutboundSender.cs:190-230`、`:248-262` | `Resolve` 在会话不在线或登记已换时返回 null；`SendAsync` 在会话离线时抛 `ClientNotConnectedException`（TouchSocket）或本仓的 `IOException`（`closed before its send pipe drained`）。M-2 的 16 行就是前者（`m2-summary.md` §1） |
| V9 | F-111 的替身仍无人使用 | 对四个类型名各做一次 `grep -rn`，只允许命中声明本身 | 规划时未重跑；`logs/task-25-doubles.txt` 记的是 TASK-25 的状态 |
| V10 | F-87 的 26 个方法都还在 | 对 §7.2 表的每个旧名 `grep -rn --include='*.cs' Server/tests`（排除 `TestResults/`） | 规划时 26 个都在，各 1 处（两塔 `Party_wrapper_…` 是两个文件各 1 处；`TouchSocketNetworkMessageSender_*` 是 3 个方法） |
| V11 | 线级测试宿主能否承载 §5 的三条 | 读 `HandlerFailureConnectionIntegrationTests.cs`、`LifecycleWireHost.cs`、`Host.Tests/CredentialEntryHttpTests.cs` 的 `LauncherHost`、`Characterization/CharacterizationHost.cs` 与 `PostCommitFailureWireTests.cs` 的 `TwoPlayersInTownAsync` 一类辅助 | `LauncherHost` 同时开游戏端口与启动器路由；`CharacterizationHost` 能经真实登录与选角进城（`PvfFact`）；`GameplayJob.Current` 是公开的，观察者可以在拆除时记下当前链 |
| V12 | `BufferBodyAsync` 的可测性 | 读 `HttpGameplay.cs:60-95`；查 `Dfo.Server.HttpApi` 是否对 `Dfo.Server.HttpApi.Tests` 开 `InternalsVisibleTo` | 规划时 `BufferBodyAsync` 是 `private static`，经 `RunsOnGameplay` 的端点过滤器进入 |
| V13 | 行尾 | 对 Owned boundary 中每个已有文件记录 CRLF／LF | 执行时记录 |
| V14 | 脚本 | `ls` scratchpad 下「输入」列出的脚本 | 规划时都在 |

V1～V14 与本表不一致时：V1 不干净或 V2 的数不同，停下回报；V5、V6、V9、V10 有出入，纳入对应各节并在 Result 说明；V11 的宿主承载不了，按停止条件 2、3 判断。

### 2. F-30：作业体失败只记一次 Error（Outcome 1）

#### 2.1 普查规则

**对象**：`Server/src` 中满足下面两条的 catch 块：
- try 里等待一个作业的结果：`IGameplayQueue.RunAsync`、`GameplayTimerWork.RunAsync`／`TickAsync`、`HttpGameplay.RunAsync`、`INetworkMessageDispatcher.DispatchAsync`、`INetworkConnectionLifecycleDispatcher.Dispatch*Async`，或经由它们的调用（例如 `DrainOnceAsync`）；
- catch 里以 `Error` 或 `Critical` 记录了这个异常（或它的上下文）。

作业体内部的 catch（在 `GameplayWork` 的委托里，记下内层失败后继续）不是双日志：作业没有失败，执行器不记 `Error`。普查只认上面这种「作业外等待」的形状。

**检索**（结果全文进 `logs/task-31-producers.txt`）：`grep -rn -B2 -A12 "GameplayTimerWork\.\(RunAsync\|TickAsync\)\|queue\.RunAsync\|HttpGameplay\.RunAsync\|dispatcher\.DispatchAsync\|lifecycle\.Dispatch" --include='*.cs' Server/src`，逐处读包住它的 try 与 catch。

**规划时读到的候选**：

| 位置 | 今天记什么 | 归类（规划时） |
|---|---|---|
| `Networking/Runtime/NetworkPlugins.cs:88`、`:181` | `Channel/Game handler failed`（Error，带异常与帧信息） | 作业外等待，只复述失败 |
| `HttpApi/HttpApiApplicationExtensions.cs:253` | `HTTP API request failed …`（Error，不带异常，带 TraceId） | 作业外等待，只复述失败 |
| `Application/DomainEvents/DomainEventDispatcher.cs:297` | `… could not persist or scan; backing off.`（Error，带异常） | 作业外等待，带额外事实（退避）；只在记录失败本身失败、作业因此失败时与执行器重复 |
| `Protocol/Game/IllusionTowerProjectionDelivery.cs:846`、`BossTowerProjectionDelivery.cs:895` | `Unexpected … sweep failure; the hosted service will continue`（Error，带异常） | 作业外等待，带额外事实（服务继续） |
| `Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:74`、`:225` 的 `failed` 回调（由 `RunTimerAsync` 的 catch 调用） | `Dungeon card … timer failed …`（Error，带异常） | 作业外等待，只复述失败 |
| `Application/Scheduling/ScheduledTaskHostedService.cs:157` | `Scheduled task … exhausted … attempts`（Error，带异常） | 作业外等待，带额外事实（重试用尽）；每次重试的 `Warning` 不在 F-30 范围 |
| 每日刷新等 `ExecuteAsync` 只 catch 取消的循环 | 不 catch 其他异常 | 不涉及（异常结束托管服务，由宿主记录；是基准行为，记一句） |
| `AltarScheduler.cs:50`、`DungeonPartyWipeService.cs:71`、`:77`、`TournamentRewardSweepService.cs:40`、`MarriageWeddingSlice.cs:246`、`JoustMaintenanceService.cs:75` | Error | 规划时判断在作业体内，不涉及；执行时逐个确认 |

#### 2.2 降级规则

在每个归为「作业外等待」的 catch 里，按 `GameplayJob.EndedAJob(error)` 分两支：
- **为真（作业体失败，执行器已经记过带调用链与异常的 `Error`）**：
  - 只复述失败的：同一条消息降为 `Debug`，保留异常与上下文字段（帧号、selector、TraceId 等），供排查时打开 Debug 对照；
  - 带额外事实的（退避、服务继续、重试用尽）：降为 `Warning`，消息不变，不再附异常对象（堆栈已在执行器那一条里）；
- **为假（拒绝入队：执行器已停止、深度超限、队列层泄漏流等，执行器没有为它记失败）**：原样不变。

入站插件的控制流不变：作业体失败照旧返回、不重抛；拒绝照旧重抛，让传输层关闭连接。V5 若确认 `OnTcpClosed` 有第二条记录，加 `catch (Exception error) when (GameplayJob.EndedAJob(error)) { return; }`，与 `OnTcpConnected` 相同。

#### 2.3 执行器失败日志补连接字段

生产者那一条降为 `Debug` 后，生产默认级别下就看不到失败作业属于哪条连接（执行器的失败日志只有根、父、深度、来源种类与链）。`GameplayExecutor` 的失败日志模板在 `source {Source}` 之后加 `, connection {SourceConnection}`，值取 `chain.SourceConnection`（无连接时为空）。开始、完成、取消三条模板不动：TASK-32 的测量脚本按它们解析作业时长（`t28/jobtypes.py` 等）。失败行的解析式 `Gameplay (.+?) failed: ` 不受影响。

#### 2.4 测试

- **既有日志断言的改写**（V6，待裁定第 1 条）：`OutboxWireTests` 两处「`Game handler failed` 恰 1 条」改为「执行器的 `Gameplay inbound:… failed` 恰 1 条，`Game handler failed` 在 `Error` 级别 0 条」；`InventorySlotMappingFailureIntegrationTests` 等待的 `Error` 改为执行器那一条，异常类型与消息断言不变。保护意图都是「这次失败被记了下来，带着原异常」，改后仍成立，而且多了「只记一次」。`LifecycleWireHost` 的 `LifecycleLog` 若不记级别，只给它加一个按级别计数的重载（测试辅助，Owned boundary 允许），已有成员不改。
- **新用例**（规划名，执行时可调整，Result 列出最终名）：
  - `GameplayExecutorLoggingTests.The_failure_log_names_the_source_connection`；
  - `HttpGameplayTests.A_gm_cmd_that_fails_is_logged_once_by_the_executor_and_the_middleware_only_at_debug`（经 `HttpApiExceptionMiddleware` 与 `HttpGameplay.RunAsync`，`DefaultHttpContext`）；
  - `DomainEventDispatchTests.A_group_whose_failure_record_fails_backs_off_with_a_warning_and_one_error`；
  - 生产者一条：两塔扫描任选其一（`…_sweep_failure_is_logged_once_…`），或卡牌计时器，取既有测试夹具最现成的那一个；
  - 入站一条由 §5.1 的 F-32 用例同时承担。

### 3. F-31：「执行器已停止」带停止原因（Outcome 2）

三处 catch `GameplayExecutorStoppedException`：
- `DomainEventDispatcher.RunGroupAsync`：今天记 Information、不带异常。改为把异常对象传给日志；`InnerException` 为空（正常停机）时仍为 Information，非空（消费循环故障，`GameplayExecutor.ConsumeAsync` 的 `AbandonQueued(exception)`）时为 Warning。消息文本不变。
- `NetworkConnectionLifecycleDispatcher.RunAsync`：今天记 Warning、不带异常，改为带异常，级别不变。
- `DungeonCardPresentationCoordinator.RunTimerAsync`：今天记 Information、不带异常，同第一处的规则。

执行器自己在消费循环故障时已记 `Critical`；这里补的是让每个受影响的生产者那一行能指回原因，而不是只说「已停止」。

**用例**：`DomainEventDispatchTests.Group_loops_end_quietly_when_the_executor_has_already_stopped` 的断言不改；新增 `DomainEventDispatchTests.A_group_loop_ended_by_a_faulted_executor_logs_the_cause_as_a_warning`：让执行器的消费循环故障（测试内的执行器替身，或经 `GameplayExecutor` 的既有故障注入方式，执行时取已有夹具里最小的那一种），断言组循环那一行是 Warning、异常的 `InnerException` 就是注入的故障。

### 4. F-121：发送端对「连接已不在」降级（Outcome 3）

#### 4.1 判定

`OutboundSender.DeliverAsync` 有两处以 `OutboundOutcome.Failed` 结束队列（同步抛出与等待抛出）。在这两处，结束队列之前再问一次 `transport.Resolve(item.Entry.Connection)`：
- **返回 null**：连接在这次发送失败时已经不在了（对端先断开、对端重置、停服时的传输层排空关闭）。记为「连接已不在」；
- **不为 null**：连接仍在、发送本身失败。照旧。

只看 `Failed`。`TimedOut`（慢客户端，RQ-036 的期限）、`OverLimit`（队列超限）、有序等待超时都是服务端判定的失败，不管连接在不在都保持 Warning。

不用异常类型判定：TouchSocket 的 `ClientNotConnectedException` 与本仓的 `IOException` 都只是「连接已不在」的表现，`Resolve` 问的才是事实本身，而且与 `ProcessAsync` 开头判 `Stale`（发送之前就不在，今天记 Debug）用的是同一个问法。

#### 4.2 记法

结果、关闭与观察者通知都不变：仍以 `Failed` 结束、丢弃余下条目、调用 `FailConnection`（`Resolve` 为 null 时它本来就不再关闭）、观察者仍收到 `Failed`。只改两条日志：
- `Outbound entry for {ConnectionId}: {Outcome}` → Information，模板为 `Outbound entry for {ConnectionId}: {Outcome}; the connection was already gone`；
- `Outbound connection {ConnectionId} closed: {Reason}` → Information，模板为 `Outbound connection {ConnectionId} closed: {Reason}; the connection was already gone`。

前缀保持不变，所以按前缀统计的脚本仍能数到它们；级别与后缀让「连接已不在」和真正的失败分得开（§4.4）。实现上把这个判定作为一个参数从 `queue.End` 传到 `Complete` 与 `FailConnection`，不改 `Item`、`ConnectionQueue` 以外的类型。

**用例**（`OutboundSenderTests`，用既有的 `FakeTransport`，加一个记录级别的日志记录器）：
- `A_failed_send_to_a_connection_that_is_already_gone_is_logged_as_information`：假连接的 `SendAsync` 先让 `Resolve` 返回 null 再抛出；断言两行都是 Information 且带后缀，观察者收到 `Failed`，余下条目 `Discarded`；
- `A_failed_send_on_a_connection_that_is_still_there_stays_a_warning`：`SendAsync` 抛出、连接仍可解析；断言两行是 Warning、无后缀，连接被关闭；
- `A_timed_out_send_stays_a_warning_even_when_the_connection_is_gone`：超时且 `Resolve` 已为 null；断言两行是 Warning。

真实 socket 上「会话还在线时取到、发送时已离线」的竞态无法稳定复现（M-2 里只在构成 B 出现，成因未测），所以真实进程中的证据交给 TASK-32：构成 B 每次运行就绪探测连接的那两行应以 Information 带后缀出现（§4.4 第 3 条）。

#### 4.3 开始与完成日志的级别：维持

`t007-closure.md` §4 第 3 条：开始是 Trace、完成与取消是 Debug，生产默认级别下不输出。复核结论为**维持**，不改代码，理由写进收口记录：
- RQ-054 只要求字段，不要求级别；字段由 `GameplayExecutorLoggingTests` 守着；
- 每个作业两行，空闲时每秒约 30 个定时作业（F-08），负载下每秒上千个，放到 Information 会让生产日志以作业流水为主；
- 需要它们的场合（测量）一直用 debug 运行取（M-1、M-2 的 `*-debug-*`），TASK-32 沿用；
- 失败日志是 Error，并带完整链与 §2.3 的连接字段，出问题时不依赖开始与完成行。

#### 4.4 交给 TASK-32 的 S5 口径

M-2 的 `t28/signals.py` 只在 `warn:`／`fail:`／`crit:` 行里识别发送端告警（第 79 行起），所以本单元之后「连接已不在」的行不会再被它数进 S5 第 5 项。为了不让这变成一个看不见的口径变化，收口记录 §4 写明下面的规则，供 TASK-32 写进判定脚本的开头（待裁定第 2 条）：
1. **S5 第 5 项**（发送端层告警）＝ Warning 及以上的 `Outbound connection … closed: TimedOut|Failed`、`Outbound entry for …: TimedOut|Failed|OverLimit`、`Closing an outbound connection failed`、`An outbound delivery observer failed`、通知总线告警；收尾窗口内不计，窗口外计入。这些就是「真正的发送失败」，口径与 M-2 相同。
2. **`OverLimit` 任何时刻都计入**，不变。
3. **「连接已不在」的 Information 行另行计数**（按后缀 `; the connection was already gone` 识别），逐条按工具行为归因：
   - 就绪探测连接：工具连上游戏端口后、没有发任何入站帧就关闭的那条连接（`HostProcess.WaitForBootstrapAsync`）；按连接行为识别，不按 host.log 行号；
   - (d) 轮的登录连接：工具在 LOGIN 应答后主动关闭；
   - 收尾窗口内的全部。
   归因不上的计入 S5（保持 M-2 的严格度：工具没有先断开的连接，服务端也不该遇到「连接已不在」）。
4. **级别生效的证据**：构成 B 每次运行就绪探测的那两行应以 Information 带后缀出现、Warning 中为 0；若仍出现 Warning 的 `Failed`，说明 §4.1 的判定没有覆盖真实进程里的这条路径，按 S5 命中处理并回报。
5. `Writing is not allowed after writer was completed` 的计数口径不变（它出现在异常文本里，不随级别变化）。

### 5. 三条保护用例（Outcome 4～6）

#### 5.1 F-32：作业内 handler 抛异常 → 断线

在 `HandlerFailureConnectionIntegrationTests` 新增 `A_handler_that_throws_inside_its_job_closes_only_its_connection_and_is_logged_once`：
- 宿主与同文件第二个用例相同（三个生产模块、真实 TouchSocket）；不装饰 `INetworkMessageDispatcher`，改为装饰婚礼应答 handler 在作业内调用的一个领域服务（执行时取它注入的、最窄的那个接口），在 `AcceptProposal` 一类调用上抛 `InvalidOperationException`。这样异常从作业体里抛出，走的是 dispatcher → 执行器 → handler 的真实路径；
- 断言：失败连接收不到 `0x03F1` 的回包、读到 FIN；另一条连接的同一请求照常得到回包、`EXIT` 照常应答（与同文件第二个用例相同的字节断言）；日志里执行器的 `Gameplay inbound:… failed` 恰 1 条、带注入的异常、`SourceConnection` 是失败连接；`Game handler failed` 在 `Error` 级别 0 条；
- 同文件第二个用例（装饰器在进入 dispatcher 之前抛出）不改，它今天守的是「拒绝路径重抛 → 传输层关闭」。两条合起来覆盖 `NetworkPlugins` 的两支。
- 找不到能在作业内抛出的注入点、只能改生产代码时，按停止条件 3 的同类情形回报。

#### 5.2 HTTP 根的继承

在 `Dfo.Server.Host.Tests/CredentialEntryHttpTests.cs` 新增 `A_launcher_login_that_supersedes_a_game_connection_is_the_parent_of_its_teardown`：
- 用同文件的 `LauncherHost`（启动器路由 ＋ 游戏端口）起宿主，`configureServices` 里登记一个只读的 `INetworkConnectionObserver`，在 `DisconnectedAsync` 时记下 `GameplayJob.Current` 与 `GameplayJob.IsOnExecutor`（仿 `LifecycleWireHost.RecordingObserver`）；
- 经 HTTP 注册并登录一个账号，用拿到的票据在游戏端口登录；再经 HTTP 登录同一账号一次；
- 断言：旧游戏连接读到 FIN；它的拆除记录在执行器上，来源 `Lifecycle`、类型 `lifecycle:disconnected`、深度 1、来源连接为空、唯一的祖先跳是 `(Http, "http:POST /api/launcher/v1/login")`（路由原文以 `HttpGameplay.CommandTypeOf` 的实际值为准）；
- 这条与 `LifecycleExecutorWireTests` 的 `:159`（父链是总线执行器外分支的 `bus:` CMD）互补：那条证明继承机制，这条证明生产入口的根是 Http。为红即停止条件 2。

#### 5.3 AC-009③：经真实连接重发已提交的效果请求

新建 `Dfo.Server.Networking.Tests/Integration/EffectReplayWireTests.cs`，一条 `[PvfFact]`：`Resending_a_committed_effect_request_on_the_same_connection_replays_without_a_second_charge_and_keeps_the_connection`：
- 宿主：`CharacterizationHost`（真实登录、选角、进城；只使用、不改 `Characterization/` 下的文件）；
- 效果请求的选择（执行时定，Result 写明理由）：优先 `EffectOperationReplayTests` 已覆盖的同一类请求（共享效果 `0x00FB`、物品使用），要求它能用起始角色的现有物品或宿主已有的种子数据完成一次结算；
- 步骤：以帧序号 s 发送一次效果请求，读到它的结算回包与状态推送，从库里读出被扣的数量；以同一帧序号 s、同一请求体再发一次；
- 断言：第二次没有新的结算回包（只有重放的状态刷新，帧形状以第一次之后读到的为准），库里的数量没有再变；连接仍开着：随后发 `EXIT`（`0x0003`）得到 `[1]` 的应答；执行器日志里没有失败作业；
- 发送辅助若不支持指定帧序号，在本测试文件里写一个最小的发帧函数，不改宿主；做不到或结果不是重放，按停止条件 3。

### 6. F-33：预缓冲与基准等价（Outcome 7）

#### 6.1 改法

`BufferBodyAsync` 今天无论上限特性是否可写，都用 `EnableBuffering(bodyLimit, bodyLimit)`。当特性只读或缺失时，服务器并没有把上限改成 `bodyLimit`，基准（委托自己读体）是按服务器当前的上限读完的；而缓冲流的 `bufferLimit` 会在 32 KiB 处抛 `IOException`，被重放给委托后多半映射成 500（TASK-14 Remaining 9）。改为：
- 特性可写：照旧设 `MaxRequestBodySize = bodyLimit`，`EnableBuffering(bodyLimit, bodyLimit)`；
- 特性只读或缺失：`EnableBuffering(bufferThreshold: bodyLimit)`，不设缓冲上限。读体的上限就是服务器当前的上限，由服务器自己在读的时候执行，超限抛出的是与基准相同的异常（Kestrel 的 `BadHttpRequestException`），照旧重放给委托。

代价：这一种情形下超过 32 KiB 的部分会落到临时文件（`FileBufferingReadStream` 的行为），仍在执行器外。TASK-14 §2.2「只用内存、不落临时文件」在可写情形下照旧成立；这一处偏离写进收口记录（待裁定第 3 条）。

#### 6.2 用例

`HttpGameplayTests` 新增（以 `DefaultHttpContext` 构造请求，附一个自定义的 `IHttpMaxRequestBodySizeFeature`）：
- `A_body_over_the_limit_is_read_whole_when_the_size_feature_is_read_only`：特性只读、上限为空或大于请求体，40 KiB 的分块 JSON 体；断言经过预缓冲后，委托读到的字节与原始请求体逐字节相同，`Position` 为 0；
- `A_body_over_the_limit_is_read_whole_when_there_is_no_size_feature`：同上，特性缺失；
- 可写情形由 B1 三形态（`GmGameplayRouteTests`，Host.Tests）继续守着，断言不改。

进入 `BufferBodyAsync` 的方式按 V12：经 `RunsOnGameplay` 的过滤器（首选）；做不到时把它改为 `internal` 并经 `InternalsVisibleTo` 调用，在 Result 登记。

#### 6.3 等价性的依据与边界

写进收口记录：可写情形与基准的等价由 B1 证明（不变）；只读与缺失情形下，读体上限由服务器执行这一点与基准相同，由 §6.2 证明缓冲本身不再截断；Kestrel 在端点过滤器阶段把特性置为只读的真实情形（请求体已开始读、或连接已升级）没有线上复现手段，属于推理证据。

### 7. 测试替身与改名（Outcome 8、9）

#### 7.1 F-111

删除四个类型：`PartyNotificationTests.BlockingNotificationBus`、`PartyTownProjectionDeliveryTests.BlockingFirstRosterNotificationBus`、`SessionGenerationOwnershipTests.NoopSender`、`SessionGenerationOwnershipTests.RecordingSender`。V9 若发现某个类型已被使用，不删，写明谁在用。只删因本次删除而失效的 `using`，不做别的整理。

#### 7.2 改名

**规则**：新名字描述该用例今天的断言实际检查什么；不提已删除的门、租约、重试上限、旧发送器类型与「门释放之后」；断言、方法体、特性标注一字不改。读完断言后若认为名字今天仍然成立，可以不改，但要写一句理由（规划时预计 0 个）。`Theory` 改名时 `InlineData` 不动。

| # | 文件 | 旧名 | 名字里过时的部分 | 来源 |
|---|---|---|---|---|
| 1 | `UnitOfWorkBehaviorTests.cs` | `UnitOfWork_OpensInsideTheAccountAndTransactionalGatesAndOutsideTheExecutionPolicy` | 账号门、事务门 | F-87 |
| 2 | `LeaveDungeonTests.cs` | `Exit_command_borrows_the_projection_lease_through_the_real_pipeline` | 投影租约 | F-87 |
| 3 | `LeaveDungeonTests.cs` | `Give_up_completes_through_real_command_locks_and_releases_the_session` | 命令锁 | F-87 |
| 4 | `CommonExperienceEffectTests.cs` | `Effect_uow_borrows_outer_exclusive_and_keeps_teardown_waiting_until_settlement` | 外层独占租约 | F-87 |
| 5 | `CommonExperienceEffectTests.cs` | `Logger_failure_does_not_replace_unknown_cause_and_exact_close_callback_waits_for_lease_release` | 租约释放 | F-87 |
| 6 | `OnlineUserServiceTests.cs` | `DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate` | 账号生命周期门 | F-87 |
| 7 | `IllusionTowerProjectionConcurrencyTests.cs` | `Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release` | 门释放之后 | F-87 |
| 8 | `BossTowerProjectionConcurrencyTests.cs` | 同上 | 同上 | F-87 |
| 9 | `PartyCommandHandlerTests.cs` | `Successful_command_notifications_are_delivered_after_both_transition_gates_release` | 两道转换门 | F-87 |
| 10 | `PartyCommandHandlerTests.cs` | `Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases` | 转换门 | F-87 |
| 11 | `ScheduledTaskTests.cs` | `Retry_delay_does_not_hold_the_global_execution_slot` | 全局执行槽（执行时核实是否已删） | F-87 |
| 12 | `ScheduledTaskTests.cs` | `Global_limit_queues_one_instance_and_coalesces_waiting_runs` | 全局上限（同上） | F-87 |
| 13 | `ScheduledTaskTests.cs` | `Factory_uses_configured_singletons_and_explicit_options_override_defaults` | 执行时核实为何在名单上 | F-87 |
| 14 | `CommandRegistrationTests.cs` | `GameTicketRedemptionIsTransactionalAndOnlineUserExclusive` | 在线用户独占 | F-87 |
| 15 | `DomainEventDispatchTests.cs` | `Commit_under_database_admission_returns_and_the_worker_consumes_after_admission_is_released` | 数据库准入门 | F-87 |
| 16 | `ReplayStateSnapshotTests.cs` | `Registered_delivery_reads_fresh_inventory_and_keeps_the_exact_connection_and_exclusive_lease_through_batch` | 独占租约 | F-87 |
| 17 | `ReplayStateSnapshotTests.cs` | `Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read` | 生命周期租约 | F-87 |
| 18 | `LifecycleExecutorWireTests.cs` | `A_superseded_connection_is_closed_by_maintenance_and_cleaned_up_on_the_executor` | 由维护作业关闭（今天由登录 CMD 登记关闭） | F-87、F-123 |
| 19 | `LifecycleExecutorWireTests.cs` | `A_send_to_a_resetting_client_does_not_hold_the_executor_past_the_deadline` | 执行器上的期限 | F-87 |
| 20～22 | `OutboxExplicitDeliveryTests.cs` | 三条 `TouchSocketNetworkMessageSender_*` | 被测类型已删除 | F-87 |
| 23 | `NotificationRegistrationTests.cs` | `A_registered_handler_is_found_with_the_retry_limit_it_declared` | 重试上限 | F-87 |
| 24 | `SelectCharacterLifecycleTests.cs` | `PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks` | 同伴失败、尾部锁 | F-87 |
| 25 | `IllusionTowerRewardProjectionTests.cs` | `Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | 两次尝试的投递失败 | F-87 |
| 26 | `EquipmentEnchantHandlerTests.cs` | `A_partial_notification_failure_can_be_replayed_without_a_second_debit` | 「部分」通知失败 | F-87 |

计数：17（1～17）＋ 2（18、19）＋ 3（20～22）＋ 1（23）＋ 2（24、25）＋ 1（26）＝ 26 个方法；文件数 17（`LeaveDungeonTests`、`CommonExperienceEffectTests`、`PartyCommandHandlerTests`、`ScheduledTaskTests`、`ReplayStateSnapshotTests`、`LifecycleExecutorWireTests`、`OutboxExplicitDeliveryTests` 各含 2～3 个）。

F-123（第 18 行）的新名在此定下：`A_superseded_connection_is_closed_by_the_login_cmd_and_its_teardown_is_that_cmds_child`。其余的新名执行时定，写进收口记录 §6 的对照表（旧名 → 新名 → 名字依据的断言行）。

第 24 行的替身里 `failPvpTarget` 是空操作（F-87 TASK-24 更新），改名只能如实描述它今天断言的内容；若它的断言已经不检查任何与名字相关的事，照实命名，不删除、不改断言，并在 Result 中提请主控方决定是否另行处置（待裁定第 4 条）。

### 8. 收口记录里要写的局部决定

- §2.2 的两种降级与「拒绝路径不变」；§2.3 的连接字段；
- §3 的级别规则；
- §4.1 的判定依据（`Resolve` 而非异常类型）、§4.3 的「维持」与理由、§4.4 的 S5 口径；
- §6.1 的改法与临时文件的偏离、§6.3 的证据边界；
- §7.2 的改名对照；F-111 删除的四个类型；
- 不改 `CommandLoggingBehavior`（待裁定第 5 条）。

### 9. 变异（Required check 6）

| 变异 | 改动 | 应当变红 |
|---|---|---|
| MF1 | 入站插件 Game 一支恢复为作业体失败也记 `Error` | §5.1 的 F-32 用例（`Game handler failed` 在 Error 级别 0 条）；`OutboxWireTests` 改写后的两处 |
| MF2 | `HttpApiExceptionMiddleware` 的作业体失败支恢复为 `Error` | §2.4 的中间件用例 |
| MF3 | `RunGroupAsync` 的作业体失败支恢复为 `Error` 带异常 | §2.4 的事件组用例 |
| MF4 | §2.4 选中的那个定时生产者恢复为 `Error` | 该生产者用例 |
| MF5 | 执行器失败日志去掉 `SourceConnection` 字段 | `The_failure_log_names_the_source_connection`；F-32 用例的字段断言 |
| MF6 | 组循环的「已停止」不再附异常 | F-31 新用例 |
| MF7a | F-121 判定恒为「连接已不在」 | `A_failed_send_on_a_connection_that_is_still_there_stays_a_warning` |
| MF7b | F-121 判定恒为「连接仍在」 | `A_failed_send_to_a_connection_that_is_already_gone_is_logged_as_information` |
| MF7c | F-121 判定扩到 `TimedOut` | `A_timed_out_send_stays_a_warning_even_when_the_connection_is_gone` |
| MF8 | `BufferBodyAsync` 恢复为总以 `bodyLimit` 作缓冲上限 | §6.2 两条 |
| MF9 | 失败作业的结束钩子不再关闭来源连接（`GameplayJobEnd.Failed` 时跳过关闭） | F-32 用例（失败连接不再读到 FIN）；`OutboxWireTests.A_failing_cmd_sends_what_it_kept_in_order_and_only_then_closes_its_source` |
| MF10 | 发件箱登记关闭时不记录关闭原因（`GameplayCloseCauses` 记录处跳过） | §5.2 的 HTTP 根用例（祖先跳缺失、深度 0）；`LifecycleCallChainTests` 中相应用例 |
| MF11 | 效果账本把「同一帧序号的重发」当作新请求（执行时取账本判重的那一处条件） | §5.3 的 AC-009③ 用例（二次扣除或出现第二个结算回包）；`EffectOperationReplayTests` 中相应用例 |

MF9～MF11 的具体改动位置在执行时读码确定，写进变异日志；它们证明三条保护用例有区分力，而不是只在基准上碰巧为绿。

### 10. 收口

**10.1 `verification/t009-logs-and-cases.md`**（新建，开头写日期与基准；写法仿 `t009-criteria.md`，只写「要求 → 实现位置 → 证据 → 状态」，未关闭事项只引 F 编号）：
- §1 F-30：普查结果汇总（`logs/task-31-producers.txt`）、§2.2 的规则、改写的日志断言逐处「旧 → 新 → 保护意图」；
- §2 F-31；
- §3 F-121：判定、记法、用例；开始与完成日志「维持」的理由；
- §4 交给 TASK-32 的 S5 口径（§4.4 原样）；
- §5 三条保护用例：F-32、HTTP 根、AC-009③（写明它补上的是 AC-009 的 A 一半第 ③ 条，①②④ 由 TASK-33 对账）；
- §6 F-33：改法、证据与边界；
- §7 测试处置：N 的逐条名单与加法；F-111 删除的类型；改名对照表；
- §8 代码冻结（§12）与移交：TASK-32（§4 的口径、F-121 在真实进程中的证据）、TASK-33（附录 G 的 VC-10 仍归它；本单元的日志变化是否需要写进 AGENTS.md「领域事件与即时投影」一节的日志描述，由它判断）、TASK-34（测试数量加法中本单元的 N、D、R）。

**10.2 `follow-ups.md`**：关闭 F-30（待裁定第 5 条取建议时写「生产者与中间件一半已修；`CommandLoggingBehavior` 那一条随附录 H 第 0 行的 B 一半」）、F-31、F-32、F-33、F-87（改名一半）、F-111、F-121、F-123；F-98 不动（TASK-33）；续写 F-69；文首补 TASK-31 一行；新事项从 F-126 起编号。

**10.3 检索集**（Required check 8，先写预期再跑）：

| # | 检索 | 预期 |
|---|---|---|
| R1 | `grep -rn "EndedAJob" --include='*.cs' Server/src` | 基准的 7 行（定义 1、文档注释 1、调用 5：`NetworkPlugins.cs` 3、`DomainEventDispatcher.cs` 1、`NetworkConnectionLifecycleDispatcher.cs` 1）＋ §2.1 每个改动的 catch 各 1 处，逐处列出 |
| R2 | `grep -rn "handler failed\|HTTP API request failed\|sweep failure\|timer failed\|exhausted" --include='*.cs' Server/src` 的日志调用级别 | 作业体失败支都不是 `Error`／`Critical`；拒绝支不变 |
| R3 | `grep -n "already gone" Server/src/Dfo.Server.Networking/Outbound/OutboundSender.cs` | 恰 2 个模板，都是 Information |
| R4 | `grep -rn "GameplayExecutorStoppedException" --include='*.cs' Server/src` 的 catch | 三处都把异常传给日志 |
| R5 | `grep -n "SourceConnection" Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs` | 失败日志 1 处；开始、完成、取消模板无改动 |
| R6 | §7.2 的 26 个旧名在 `Server/tests`（排除 `TestResults/`） | 0 |
| R7 | F-111 的四个类型名在 `Server/tests` | 0 |
| R8 | `grep -n "EnableBuffering" Server/src/Dfo.Server.HttpApi/HttpGameplay.cs` | 两种调用，分别在可写与只读／缺失两支 |
| R9 | `git diff 122576d5 -- .rks-agent/tasks/serial-gameplay-executor/spec.md` | 空 |

### 11. 数量与取证预期

- **N**（规划时的估计，执行时以实际为准，逐条列名）：
  - Application 3：执行器连接字段 1、事件组 F-30 1、F-31 1；
  - Protocol 0～1：§2.4 的定时生产者用例若落在 Protocol.Tests；
  - Networking 5：F-121 3、F-32 1、AC-009③ 1；
  - HttpApi 3：中间件 1、F-33 2；
  - Host 1：HTTP 根；
  - 合计 12～13：3 ＋ 0 ＋ 5 ＋ 3 ＋ 1 ＝ 12（定时生产者用例落在 Protocol 时为 13）。
- **D**：0。**R**：26（方法集合 －26 ＋26，总数不变）。
- 预期总数：8302 ＋ 12 － 0 ＝ 8314（或 8315）。
- **取证**：73，不变；`exe:` 0。

### 12. 代码冻结

本单元经结果评审通过并提交之后：
- **冻结点**就是这次提交（主控方提交时把哈希写进 `follow-ups.md` 文首 TASK-31 一行与收口记录 §8）；
- TASK-32、TASK-33、TASK-34 开始前各自核对 `git diff <冻结点> -- Server` 为空；
- 冻结之后若发现必须改代码的问题，不在后续单元里顺手改，回到主控方决定（另立返工单元，或交子 Spec B）。

### 13. 执行顺序

1. §1 核实（V1～V14），落盘 `task-31-verify.txt`；V1 不干净即停。
2. §2.1 普查，落盘 `task-31-producers.txt`，定下 F-30 的改动点。
3. 只加新用例、不改生产代码：§2.4、§3、§4.2、§6.2 的新用例，§5 的三条保护用例；构建并跑：RED（Required check 2 第一段）。§5 的三条应为绿，为红即停止条件 2、3。
4. F-111 删替身、§7.2 改名；过滤运行（第二段）。
5. 生产改动：F-30（含 §2.3）、F-31、F-121、F-33；改写 V6 列出的日志断言。GREEN：过滤运行全绿。
6. 正式全量与取证（Required checks 1、3、4），隔离复跑（check 12），×10／×3（check 7）。
7. §9 变异，重建。
8. §10 收口记录、`follow-ups.md` 与检索集。
9. 收口引用核对、diff 核对、边界、`cr-at-eol` 口径 `diff --check`（checks 9～11、13）。

### 14. 表征测试、金样本与生产行为

- 客户端可见的行为不变：本单元只改日志与 F-33 的罕见情形；F-33 让那一种情形回到基准行为，是 RQ-082 的收紧而不是新增的可见变化。
- 线上字节、帧序与连接开关的断言一处不改；表征测试 10 条与全部金样本不变。需要改的，按停止条件 5、6 处理。

## 待裁定

1. **F-30 改写三处既有的日志断言**（§2.4；RQ-081、RQ-083）。
   - `OutboxWireTests` 两处与 `InventorySlotMappingFailureIntegrationTests` 一处在线级测试文件里，断言的是生产者那一条 `Error`。F-30 去掉的正是这一条，所以这三处必须改；它们断言的是日志，不是线上字节、帧序或连接开关，不在 RQ-081 保护的范围内。
   - **建议：接受。** 改为断言执行器那一条 `Error` 恰 1 条且带原异常、生产者 `Error` 为 0；按 RQ-083 逐处写「旧 → 新 → 保护意图」。V6 若多出别的位置，同样处理并逐处列出；超出日志断言的，按停止条件 5。

2. **F-121 之后 S5 第 5 项的口径**（§4.4；交 TASK-32）。
   - M-2 的 S5 第 5 项由 TASK-28 主控方裁定定下，判定脚本只数 Warning 及以上的发送端行。本单元把「连接已不在」降为 Information，这些行会自动从 S5 里消失。不写清楚，等于不经裁定放宽了 S5。
   - **建议：按 §4.4 的五条写成 M-3 的口径。** 真正的发送失败（Warning 及以上）口径与 M-2 相同；「连接已不在」另行计数并逐条按工具行为归因，归因不上的计入 S5；构成 B 的就绪探测那两行以 Information 出现，作为降级在真实进程里生效的证据。这也落实了 M-2 文末「就绪探测的豁免写成规则、不按行号登记」的要求。
   - 备选：S5 第 5 项改为「所有级别」的发送端行，就绪探测按规则豁免。代价：Information 行与 Warning 行混在一起，F-121 区分真假失败的意义在判定里就没有了。

3. **F-33 在只读／缺失情形下不设缓冲上限**（§6.1）。
   - 这是唯一能让读体上限与基准逐字相同的写法；代价是这一种情形下超过 32 KiB 的请求体会落临时文件，偏离 TASK-14 §2.2「只用内存」。可写情形（正常路径）不变。
   - **建议：接受**，偏离写进收口记录。
   - 备选：只读／缺失时用该特性的当前上限作缓冲上限（提纲原写法）。代价：缓冲流的 `IOException` 与 Kestrel 的 `BadHttpRequestException` 谁先抛取决于读的粒度，超限时委托看到的异常可能与基准不同，不能证明逐字等价。

4. **F-87 改名的范围**（§7.2）。
   - **建议：26 个全部改名，断言不动。** 改名后名字只描述今天的断言；第 24 行这类断言已经不再检查名字所说的东西的，照实命名，删除与否另行决定，不在本单元。
   - 请确认两点：(a) 允许执行者在读完断言后对个别用例写「名字仍成立、不改」（规划时预计 0 个）；(b) 改名计入 RQ-083 的方法集合变化（－26 ＋26），TASK-34 的测试数量加法里单列 R，不计入 N、D。

5. **`CommandLoggingBehavior` 的那一条 `Error` 不在本单元处理**（§2；附录 H 第 0 行）。
   - 作业内某条细粒度命令失败时，今天会有三条 `Error`：`CommandLoggingBehavior`（命令失败）、执行器（作业失败）、生产者。本单元去掉生产者那一条，剩下两条。`CommandLoggingBehavior` 由附录 H 第 0 行定为「B：随细粒度命令归并删除或改写」。
   - **建议：A 内不动。** F-30 关闭时写明「生产者与中间件一半已修；命令层那一条随附录 H 第 0 行的 B 一半」，并列进 TASK-34 的遗留问题。
   - 备选：本单元把它降为 Warning。代价：提前改动 B 要删的东西，且 `CommandBusTests` 的日志断言要跟着改。

6. **正式全量中 F-125 再次出现时的处理**（Required check 3；停止条件 7）。
   - F-125 已登记在 `follow-ups.md`，身份明确（随机奖励选中缺 `[minimum level]` 的装备，栈根 `PvfEquipmentDefinitionSource.cs:160`），但不在 AGENTS.md 的偶发清单里。按 TASK-30 的停止条件写法，它再出现就要停。
   - **建议：本单元内按已登记偶发项处理。** 判定要三样都对上：用例为 `IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter`、异常为 `InvalidDataException` 且消息含 `[minimum level]`、栈根为 `PvfEquipmentDefinitionSource.cs:160`；单独复跑 5 次全过；F-69 一样续写到 F-125。三样有一样不符，仍按停止条件 7。

## 主控方裁定（执行前）

1. **F-30 改写三处既有的日志断言，接受。** `OutboxWireTests` 两处、`InventorySlotMappingFailureIntegrationTests` 一处断言的都是日志，不是线上字节、帧序或连接开关，不在 RQ-081 的保护范围内。按 RQ-083 逐处写出「旧 → 新 → 保护意图」。
2. **F-121 之后 S5 第 5 项的口径，按计划 §4.4 的五条执行，接受。** 这五条写成 M-3 的判定规则，交 TASK-32 原样采用，并在 M-3 的判定日志中注明出处是本裁定。
3. **F-33 在只读或缺失的情形下不设缓冲上限，以与基准逐字等价为准，接受。** 偏离 TASK-14「只用内存」的这一种罕见情形，写进收口记录，并列入 TASK-34 的遗留问题。
4. **F-87 改名范围，接受。**
   - (a) 执行者读完断言后，若名字仍然成立，可以写「不改」，逐条说明理由；
   - (b) 改名在 TASK-34 的测试数量加法里单列为 R，不计入 N、D。
5. **`CommandLoggingBehavior` 那一条 Error 本单元不动，接受。** F-30 关闭时写明：生产者与中间件那一半已修，命令层那一条随 B（附录 H 第 0 行）。这一条列入 TASK-34 的遗留问题与 SC-12 的过渡机制清单。
6. **F-125 作为已登记的偶发项处理，接受。** 三样都对上才归入：
   - 用例名；
   - `InvalidDataException`，且消息含 `[minimum level]`；
   - 栈根为 `PvfEquipmentDefinitionSource.cs:160`。

   此外单独复跑 5 次须全过。有一样不符，仍按停止条件处理。AGENTS.md 的偶发清单留到 TASK-33 一并更新。

## Result

执行轮次：第 1 轮（基准 `122576d5`，未提交；结果评审为 required，等待主控方派发评审）。

### Outcome

done（待结果评审）。

### Changed

- **生产（13 个文件，102 行增、34 行删，`logs/task-31-boundary.txt`）**：Owned boundary 列出的 8 个文件，加 §2.1 普查认出的 5 个生产者文件——规划表已列的 `IllusionTowerProjectionDelivery.cs`、`BossTowerProjectionDelivery.cs`、`ScheduledTaskHostedService.cs`，新认出的 `Dfo.Server.Application/Events/JoustMaintenanceService.cs`、`Dfo.Server.Networking/Runtime/TouchSocketTransportDrain.cs`。改动只有日志级别／调用、日志模板、catch 的过滤与变量、§4 的判定、§6 的缓冲参数与就地注释；`BufferBodyAsync` 仍是 `private`，没有改 csproj、`InternalsVisibleTo`、共享契约与表结构。
  - F-30：11 处「作业外等待」的 catch 在 `GameplayJob.EndedAJob` 为真时降级——只复述失败的 6 处降为 Debug（入站 Channel／Game、HTTP 中间件、卡牌计时器两个回调、传输层排空），带额外事实的 5 处降为不附异常的 Warning（事件组退避、两塔扫描、计划任务重试用尽、Joust）；6 ＋ 5 ＝ 11。拒绝路径不变。执行器失败日志加 `connection {SourceConnection}`。
  - F-31：三处「已停止」附异常；有 `InnerException` 时事件组与卡牌计时器那一行为 Warning。
  - F-121：`OutboundSender.IsGone`（问 `transport.Resolve`），以参数 `gone` 经私有的 `End`／`Complete`／`FailConnection` 传递；只对 `Failed` 生效，两行 Information 带后缀 `; the connection was already gone`。
  - F-33：上限特性只读或缺失时 `EnableBuffering(bodyLimit)`，不设缓冲上限。
- **测试**：新增 13 个方法（Application 4、HttpApi 3、Networking 5、Host 1；4 ＋ 3 ＋ 5 ＋ 1 ＝ 13；新文件 `Integration/EffectReplayWireTests.cs`），删除 4 个替身类型（F-111），改名 26 个方法（17 个文件），改写 3 处日志断言（`OutboxWireTests` 两处、`InventorySlotMappingFailureIntegrationTests` 一处）。测试辅助：`LifecycleLog.Count(fragment, level)`；`DomainEventDispatchTests` 的 `Fixture` 加可选参数 `queue`；`DomainEventDispatchTests.Executor.cs` 的 `LogEntry` 加可选 `Exception`（2 行）。
- **文档与证据**：新建 `verification/t009-logs-and-cases.md`；`verification/follow-ups.md` 文首补 TASK-31 一行，关闭 F-30、F-31、F-32、F-33、F-87（改名一半）、F-111、F-121、F-123，续写 F-69，新增 F-126；`verification/logs/task-31-*.txt` 12 个（verify、producers、red-green、build-test、violations、mutations、new-x10、retrievals、closure-refs、diff-check、boundary、isolated），脚本全文附在各日志末尾。

### Why

- 定时生产者用例取 `ScheduledTaskHostedService`（规划的候选是两塔或卡牌计时器）：两塔的 `ExecuteAsync` 要驱动 500 ms 周期定时器而没有现成夹具；卡牌计时器的作业体自己接住异常，作业几乎不会失败。所以 N 落在 Application 而不是 Protocol（规划估计 12～13，实际 13）。
- Joust、传输层排空两处不在规划表（表里把 Joust 记为作业体内），普查认出它们是「作业外等待」，按 Owned boundary「普查新认出的生产者文件」处理。
- V5：`OnTcpClosed` 的作业失败被 TouchSocket 收成 `Result` 丢弃、不经 `ILog` 记录（IL 读出），不存在第二条记录，不加 catch。
- F-32 用例装饰 `ISelectedSessionDomainService`（婚礼应答 handler 在作业内首先调用的领域服务），复用 `PostCommitFailureWireTests.Recording<T>` 与表征目录的 `Decorate`，只用不改。
- HTTP 根用例在自己的配置里补 `GameLogin:Environment`（`LauncherHost` 缺它，LOGIN 应答组不了包），`LauncherHost` 不改。
- AC-009③：起始角色没有可用的消耗品，游戏协议又不能放物品，所以创建角色前往 `initialization_settings` 写一行角色初始化配置（幸运星 10002998 ×2），按 `CharacterizationHost` 的规矩在用例里写明理由；重发帧的序号用反射读 `WireClient` 的私有计数；`CharacterizationHost` 没有日志接缝，「没有失败作业」以「连接仍开着、`EXIT` 得到 `[1]`」证明。
- F-33 的两条用例经 `RunsOnGameplay` 的过滤器进入（用一个记录约定的 `IEndpointConventionBuilder` 取出过滤器工厂），不改可见性。

### Checks

1. 构建 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：RED 阶段、全部改完、变异之后各一次，都是 0 警告 0 错误（`logs/task-31-red-green.txt`、`logs/task-31-build-test.txt`、`logs/task-31-mutations.txt`）。
2. RED／GREEN（`logs/task-31-red-green.txt`）：9 条在基准上为红、原因逐条是旧行为（F-32 用例的线上断言全过，只在日志断言处红）；4 条在基准上即为绿（F-121 两条配对用例、AC-009③、HTTP 根）；9 ＋ 4 ＝ 13，停止条件 2、3 未触发。两处 RED 偏差已按协议分类并重取证据：HTTP 根用例第一次因测试缺配置失败；计划任务用例的任务 id 含 `exhausted` 导致误匹配，改名后以基准版 `ScheduledTaskHostedService.cs` 重跑为红。第二段（改名与删替身后）：五个项目 4594 ＋ 1752 ＋ 188 ＋ 197 ＋ 57 ＝ 6788 ＝ 6775 ＋ 13，失败只有基线 2 条与 9 条新 RED；方法变化只有 26 个旧名消失、26 个新名与 13 个新方法出现。第三段 GREEN：新方法 13 条全过，五个项目除基线 2 条外全过。
3. 正式全量（`logs/task-31-build-test.txt`）：HttpApi 57 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 188 ＋ Application 4594 ＋ Host 197 ＝ 8315 ＝ 8302 ＋ N 13 － D 0；分列：N ＝ 13（HttpApi ＋3、Networking ＋5、Application ＋4、Host ＋1），D ＝ 0，R ＝ 26（27 个用例，不计入 N、D）。通过 57 ＋ 80 ＋ 1752 ＋ 477 ＋ 968 ＋ 188 ＋ 4576 ＋ 197 ＝ 8295，失败 2（基线 `DimensionDungeonTests`），跳过 2 ＋ 16 ＝ 18；8295 ＋ 2 ＋ 18 ＝ 8315。F-125 未出现，裁定第 6 条未启用；挂起转储 0；表征 10 通过 0 跳过。日志信号与 TASK-30 逐条对照：inbound 失败 ＋1（F-32 用例，预期内）；发送端 7 行不变，其中 4 对 `Failed` 换成带后缀的 Information、2 对仍 Warning（对端重置与摘除登记的先后，F-126）；`Game handler failed` 9 → 1（余下的是拒绝路径用例）、`HTTP API request failed` 2 → 0。F-69：多出 ≥ 9 秒的 6 条，单独各跑两次 1～2 秒。耗时 181 秒，只作信息。
4. 零违规取证（`logs/task-31-violations.txt`）：73 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 69；反例类与对照以外 0，没有测试帧 0，`exe:` 0，退出码 0。
5. 普查（`logs/task-31-producers.txt`）：70 ＝ 执行器 4 ＋ 作业体内 51 ＋ 与作业无关 3 ＋ 裁定第 5 条 1 ＋ 作业外等待 11，逐处与规划表对照。
6. 变异（`logs/task-31-mutations.txt`）：MF1～MF11（含 MF7a／b／c）全部有指定用例变红；MF2 第一版因 `error` 未使用编译失败，改写后重跑为红；每次 `cmp` 一致，前后 `git diff` sha1 相同，重建 0／0。
7. 新增 ×10（`logs/task-31-new-x10.txt`）：13 个新方法连续 10 轮 40 次运行全过；四个类各 ×3 全过。
8. 检索集 R1～R9（`logs/task-31-retrievals.txt`）：全部符合预期（R1 ＝ 基准 7 ＋ 新增 9 ＝ 16）。
9. 收口引用核对（`logs/task-31-closure-refs.txt`）：0 处不符；旧名只出现在 §7.3 对照表的旧名一栏。
10. 测试改动核对（`logs/task-31-diff-check.txt`）：方法集合 ＝ 基准 － 26 ＋ 26 ＋ 13；断言多重集合变化的已有文件只有 `OutboxWireTests.cs`（逐处旧 → 新已写）；只改名的文件除方法名外无改动；F-111 三个文件只少了替身（及一行随之失效的 `using`）。
11. 边界（`logs/task-31-boundary.txt`）：13 个生产文件全在 Owned boundary 内；白名单外的改动 0；`spec.md`、`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`、表征目录、以往收口文件、共享契约、csproj 无改动。
12. 隔离复跑（`logs/task-31-isolated.txt`）：Application 61、Networking 7、Host 1、HttpApi 1 条记录，各自反例类与对照以外 0、`exe:` 0。
13. `git -c core.whitespace=cr-at-eol diff --check` 无输出；改动与新建文件全为 LF（基准即 LF）；`dotnet build-server shutdown` 已执行；`pgrep -x yes` 为 0（本单元没有加负载）。

### Remaining

- 结果评审（required）待主控方派发；通过并提交后，提交哈希即代码冻结点，需补写进 `follow-ups.md` 文首 TASK-31 一行与 `t009-logs-and-cases.md` §8。
- 交出：§4 的 S5 口径与 F-121 在真实进程中的证据 → TASK-32；F-126（对端重置时 `Failed` 是否带后缀取决于先后）→ TASK-32 在 M-3 判读并回报；AGENTS.md 日志描述与偶发清单、AC-009 ①②④、VC-10 → TASK-33；N ＝ 13、D ＝ 0、R ＝ 26 与 F-30 命令层一半、F-33 临时文件偏离 → TASK-34。
- 与 Owned boundary 的出入请评审确认：`DomainEventDispatchTests.Executor.cs`（同一测试类的另一个 partial 文件）的 `LogEntry` 加了可选 `Exception`，没有断言变化；第 24 条改名用例（`SelectCharacterLifecycleTests.Pvp_plan_publishes_the_newcomer_to_its_audience_and_the_selection_completes_every_stage`）的断言仍检查阶段顺序与 PvP 公示，照实命名后有用，不建议另行处置。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-31-result-review.md`），本次提交即为代码冻结点。提交前处理如下：
1. **发现 1（中）：修订裁定第 2 条。** `t009-logs-and-cases.md` §4 第 1 条恢复 M-2 对 (d) 轮登录连接 Warning 行的豁免（工具在 LOGIN 应答后主动断开，断开后 1 s 内不计）；第 4 条补上 F-126 的 `SocketException` 例外，以及「本次无级别证据」的记法。依据是评审的读码结论：TouchSocket 只在 `m_online` 为 false 时抛 `ClientNotConnectedException`，`Resolve` 读的是同一个字段，而且这个字段关闭后不会恢复。所以就绪探测的两行必然是带后缀的 Information，F-126 只可能出现在收到过帧的连接上。
2. **发现 2（低）**：Channel 入站那一支的 F-30 降级没有用例或变异守着。读码确认它与 Game 一支对称，列入 TASK-34 的遗留问题。
3. **发现 3（极低）**：`DomainEventDispatchTests.cs` 中 `Services =services` 补上空格，只改格式。

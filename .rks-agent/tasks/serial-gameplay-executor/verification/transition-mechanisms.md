# 过渡机制清单（结果 A 封存时；根 SC-12）

- 日期 2026-10-24。冻结点 `2bdb1afd`（`git diff 2bdb1afd -- Server Tool` 为空，`logs/task-34-verify.txt`、`logs/task-34-boundary.txt`）；Spec 基准 `a02bce46`。
- 用途：根 SC-12 的正式清单，也是子 Spec B 的输入。根 SC-13 要求 B 封存时本清单「检索为零」，每一项末栏给出 B 封存时可以直接运行的检索式。
- 本清单是过渡机制的正式出处。`AGENTS.md` 中每一项各有一处写明「这是过渡形态」（§4）；两者不一致时以本清单为准，并改 `AGENTS.md`。
- 「由 B 删除的方式」一栏只写起点建议（TASK-34 主控方裁定第 4 条），只引 `spec.md` R3-1、`plan.md` 共享决定和附录 H 的原文。B 的 Spec 可以改删除方式，但不能让清单中的任何一项在 B 封存时仍在。
- 文件:行 指冻结点，路径相对 `Server/src/`。
- TASK-35（2026-10-24）：`AGENTS.md` 的行号按 TASK-35 之后改写（「失败与日志」一段多了两行，写定时源经 `GameplayTimerWork.TickAsync` 吸收作业失败，见 `tasks/TASK-35.md`）。`Server/src` 的行号仍指冻结点 `2bdb1afd`；TASK-35 改过的生产文件不在本清单的位置栏里。新增 §5。

## 1. 清单（6 项）

### T1 `ICommandBus.SendAsync` 的两种语义

| 栏 | 内容 |
|---|---|
| 位置 | `Dfo.Server.Application/Commands/CommandBus.cs:30`（`if (GameplayJob.IsOnExecutor) return InlineAsync(...)`，作业内内联）、`:33-36`（执行器外包成 `bus:<命令类型名>` 作业入队并等待） |
| 今天的行为 | 在作业里调用时内联执行：开 DI scope，按 `Order` 包装管线，再执行。在执行器外调用时，把命令包成 `bus:<命令类型名>` 作业入队，并等待它完成。生产代码里没有执行器外的调用方（`AGENTS.md:290-291`） |
| 存在理由 | handler 今天在入站作业内经总线顺序派发细粒度命令（T2），内联是它们照常工作的前提。删除内联要等全部 handler 归并之后（R3-3「删除 `SendAsync` 过渡内联规则」一行，判定为 B） |
| 新代码不要依赖 | 不要在执行器外调用总线来完成玩法动作（`AGENTS.md:356-358`）；命令处理器仍不得派发命令 |
| 由 B 删除的方式（起点建议） | Plan 共享决定原文：「这个过渡规则在第 7 步结束时删除……之后在执行器上调用就是 RQ-003 定义的程序错误」；R3-2：「内联规则本身是过渡机制（根 SC-12），由 B 删除」；原 Plan T-008 功能簇顺序的最后一步是「删除 `SendAsync` 过渡内联规则」 |
| B 封存时的检索 | `grep -n "IsOnExecutor" Server/src/Dfo.Server.Application/Commands/CommandBus.cs` 为 0；`grep -rn '"bus:"' Server/src` 为 0（或改为程序错误路径，由 B 的判据证明） |

### T2 handler 在入站作业内编排多条细粒度命令

| 栏 | 内容 |
|---|---|
| 位置 | 各协议切片的 `*Handler.cs` 经 `commandBus.SendAsync(...)` 顺序派发；命令注册在 `Dfo.Server.Protocol/ProtocolModule.cs`，冻结点共 204 条（Transactional 170、Direct 21、ReadOnly 9、Workflow 4，`t009-docs-and-appendices.md` §9）。多阶段的代表是 `Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`：准备、补货、发布、PvP、任务、宠物各一条命令，失败时派发 `CompensateSelectedCharacterSessionCommand` |
| 今天的行为 | 一个入站协议是一个作业。作业内 handler 按顺序派发多条细粒度命令，每条 Transactional 命令各是一个事务；跨命令的补偿由 handler 派发专用补偿命令 |
| 存在理由 | 协议级 CMD 归并整体移交 B（R3-1：Scope In「协议 CMD 吸收 handler 的工作」、RQ-021、RQ-022 → B；RQ-024 → 跨）。A 只核对了自己期间没有合并或拆分提交点（RQ-024 的 A 一半，`t009-docs-and-appendices.md` §9） |
| 新代码不要依赖 | 不要假设一个入站请求只有一个事务；跨命令的补偿仍由 handler 派发专用补偿命令（`AGENTS.md:382`） |
| 由 B 删除的方式（起点建议） | 原 Plan T-008：「按功能簇把 handler、`*Delivery`、`*Coordinator` 的工作并入协议 CMD，细粒度命令并入、改成应用服务或删除……填写附录 C」；Plan 共享决定「工作单元」：「CMD 内用显式的工作单元作用域代替被并入的命令」；RQ-024 的 B 一半是附录 C 与 AC-010 |
| B 封存时的检索 | `grep -rn "commandBus.SendAsync\|\.SendAsync(new " Server/src/Dfo.Server.Protocol --include='*Handler.cs'`：每个 handler 至多一处、只派发它自己的协议 CMD（判据由 B 的 RQ-070 (a)(c) 给出）；`ProtocolModule` 中细粒度命令注册条数与附录 C 对上 |

### T3 管线五层包在每一条细粒度命令外，以及它依赖的命令标记接口

| 栏 | 内容 |
|---|---|
| 位置 | 行为：`Dfo.Server.Application/Commands/Pipeline/` 下 5 个类，Order 0／45／58／60／100（`CommandLoggingBehavior.cs:10`、`SelectedSessionExecutionBehavior.cs:8`、`SelectedSessionSettlementBehavior.cs:9`、`UnitOfWorkBehavior.cs:20`、`CommandExecutionPolicyBehavior.cs:6`）。注册：`Dfo.Server.Application/Commands/CommandDispatchingExtensions.cs:34-42`。标记接口：`Dfo.Server.Application/Commands/CommandContracts.cs:27-40` 的 `ISelectedSessionCommand`、`IRequestSettlementCommand`、`IMultiSelectedSessionCommand`、`IAccountCommand`；读取它们的是 45 号行为（`SelectedSessionExecutionBehavior.cs:14-18`）与 `EffectOperationContext`（`Effects/EffectOperationContext.cs:58-98`，经 45 的 `BeginMain` 与 58 的 `Settled`／`Failed`） |
| 今天的行为 | 每一条细粒度命令都套一遍这五层。45 与 58 按命令类型上的标记接口决定做什么：开主结算、复查账号与会话一致、逐个复查参与者、设置选中会话作用域、结果不确定时封锁 |
| 存在理由 | 管线非锁职责的迁移移交 B（R3-1 附录 H 第 45 行「跨」、第 58、60、100 行「B」、第 0 行「跨」）。A 只删除了取锁部分（`t005-closure.md` §4）。标记接口与这几层是同一个动作：B 改为「CMD 声明自己的主结算工作单元」（附录 H 第 45 行去向）时，读标记接口的地方随之消失 |
| 新代码不要依赖 | 不要依赖「一条命令一套管线」（`AGENTS.md:362-365`）。今天新命令要得到 45、58 号行为，仍须实现这些标记接口；B 归并后不再需要它们，不要假设它们在 B 之后仍然存在 |
| 由 B 删除的方式（起点建议） | 附录 H 去向原文：45「取锁部分删除；其余在 CMD 内主结算所在的工作单元前后执行。主结算的识别方式由今天的『命令类型实现 `IRequestSettlementCommand`』改为『CMD 声明自己的主结算工作单元』，`EffectSettlementRegistrationTests` 按此重写」；58「在主结算工作单元提交前后执行，语义不变」；60「由工作单元作用域承担，语义不变」；100「授权检查并入 RQ-025；执行方式随工作单元模型改写」；0「由执行器对每条 CMD 记录，并带调用链（RQ-054）」 |
| B 封存时的检索 | `grep -rn "ICommandPipelineBehavior<>" Server/src`：0 行，或只剩 B 的 Spec 明文保留、并已说明不再按细粒度命令包装的行为；`grep -rnw "ISelectedSessionCommand\|IRequestSettlementCommand\|IMultiSelectedSessionCommand\|IAccountCommand" Server/src` 为 0 |

### T4 请求级上下文是作业内的 AsyncLocal 帧

| 栏 | 内容 |
|---|---|
| 位置 | `Dfo.Server.Application/Commands/NetworkCommandAuthorization.cs:8`（`current`）；`Dfo.Server.Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:38-40`（`SelectedSessionCommandScope.current`）；`Dfo.Server.Application/Effects/EffectOperationContext.cs:30-32`（`current`、`execution`、`rejection` 三个）；`Dfo.Server.Application/Commands/CommandBus.cs:16`（静态 `DispatchDepth`）。4 个类型、6 处 `AsyncLocal<`。帧在作业开始时由 `Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs:70` 一带建立；「没有帧就放行」的分支，例如 `NetworkCommandAuthorization.DemandCurrent`（`NetworkCommandAuthorization.cs:20-25`，帧不存在或已失效时直接返回），见 `NetworkMessageDispatcher.cs:20` 的说明 |
| 今天的行为 | 这些帧都在作业内进入与退出（普查豁免 E3）。执行器不跨作业携带它们，所以作业外建的帧对 handler 不可见 |
| 存在理由 | R3-1 RQ-025 → B：「『不再没有帧就放行』与三种合法状态的程序错误判定，在 B」；附录 H dispatcher 行「跨」：A 的一半是「检查在执行器上、作业开始时执行（TASK-09 已落实，形式是过渡期的 AsyncLocal 帧）」，B 的一半是「改为协议 CMD 的显式数据」。附录 B「请求级帧（过渡）」一行（TASK-30 新增）写明了不挂点的理由 |
| 新代码不要依赖 | 不要在作业之外读写这些帧，也不要新增同类的 AsyncLocal 帧（`AGENTS.md:312-314`） |
| 由 B 删除的方式（起点建议） | Plan 共享决定「请求级上下文」第 7 步原文：「把这些帧改为协议 CMD 的显式数据，『没有帧就放行』的分支改为程序错误，并按附录 H 迁移管线 45、58、60 号行为的非锁职责」 |
| B 封存时的检索 | `grep -rn "AsyncLocal<" Server/src` 只剩 `GameplayJob.Running`（作业身份）与 `DomainTransactionScope.ambient`（环境事务）两处，都不在本清单（§2） |

### T5 细粒度命令失败时两条 `Error`

| 栏 | 内容 |
|---|---|
| 位置 | `Dfo.Server.Application/Commands/Pipeline/CommandLoggingBehavior.cs:34`（`logger.LogError(exception, "Command {CommandType} failed in handler ...")`）；执行器的作业失败日志 `Dfo.Server.Application/Execution/GameplayExecutor.cs:312`（RQ-054，`t007-closure.md` §1） |
| 今天的行为 | 作业体失败时，执行器记一条带调用链的 `Error`。若失败发生在某条细粒度命令内，`CommandLoggingBehavior` 另记一条 `Error`。生产者与中间件那一层的重复已在 TASK-31 降级（F-30 的生产者一半） |
| 存在理由 | TASK-31 主控方裁定第 5 条：F-30 的命令层一半留给 B，随 `CommandLoggingBehavior` 处置（附录 H 第 0 行「跨」：B 的一半是「`CommandLoggingBehavior` 随细粒度命令归并删除或改写」） |
| 新代码不要依赖 | 读日志时，同一次失败今天可能有两条 `Error`（`AGENTS.md:326`） |
| 由 B 删除的方式（起点建议） | 附录 H 第 0 行：「由执行器对每条 CMD 记录，并带调用链（RQ-054）」；R3-1 附录 H 第 0 行：B「`CommandLoggingBehavior` 随细粒度命令归并删除或改写」 |
| B 封存时的检索 | `grep -n "LogError" Server/src/Dfo.Server.Application/Commands/Pipeline/CommandLoggingBehavior.cs` 为 0（或文件已不存在）；B 的日志用例证明一次失败只有执行器那一条 `Error` |

### T6 失败作业关闭来源连接，依赖作业已打开发件箱

| 栏 | 内容 |
|---|---|
| 位置 | `Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs:238-239`（`if (failed && source is not null) flushed.Add(OutboundEntry.Close(source));`，只在这个作业已有发件箱时走到）；两个 dispatcher 在作业开始时打开发件箱：`Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs:51`、`Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs:35`（`_ = outboxes.Current;`） |
| 今天的行为 | 作业以未处理异常结束时，保留的条目照发，最后关闭来源连接。前提是这个作业已经打开过发件箱；今天带来源连接的两类作业都在一开始打开它 |
| 存在理由 | F-110 的结构保证一半交 B（`follow-ups.md` F-110：「结构保证 → 子 Spec B；A 在 T-009 的文档中记为已知约定」）。B 把入口改为协议 CMD 时，结构保证可以放在 CMD 的入口处一次给出 |
| 新代码不要依赖 | 新的带来源连接的作业要在开始时打开发件箱（读一次 `GameplayOutboxes.Current`），不要依赖别处替它打开（`AGENTS.md:334`） |
| 由 B 删除的方式（起点建议） | `follow-ups.md` F-110 的去向原文（结构保证归 B）；没有 Spec 原文，B 的 Spec 决定由谁保证「失败作业总有发件箱」 |
| B 封存时的检索 | `grep -n "_ = outboxes.Current" Server/src/Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs Server/src/Dfo.Server.Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs` 为 0，且 B 的判据或用例证明：一个从未登记任何条目的作业失败时，它的来源连接仍被关闭。只查这两个 dispatcher；`OutboxAdapters.cs`、`NotificationBus.cs` 里另外 3 处同样的读取是登记点的守卫，属常设机制（§2），不要求为零 |

## 2. 不进清单、但与过渡项相邻的机制

这些在残留检索里会命中，归类与理由如下。它们不是过渡机制，B 封存时不要求检索为零。

| 机制 | 位置 | 归类 | 理由 |
|---|---|---|---|
| 登记适配器 `OutboxNetworkMessageSender`、`OutboxConnectionCloser` | `Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:20`、`:200` | A 的常设机制 | 它们把既有的发送接口与关连接接口接到发件箱上，是 A 投递设计本身（`AGENTS.md`「玩法执行器、发件箱与发送端」一节「发件箱」段）。B 归并后发送仍要登记进发件箱，它们不因归并而失去用途，所以不符合「转发层」的定义（§3） |
| 分流键 `GameplayShardKey`、`GameplayShardSelector` | `Dfo.Server.Abstractions/Execution/GameplayQueue.cs:9`；`Dfo.Server.Application/Execution/GameplayQueue.cs:11` | 授权预留 | 根契约 Scope Out 第 1 项、RI-4：不按频道分流，只预留分流键。选择器对任何输入都返回同一个执行器（AC-001 最后一句） |
| `EffectOperationLedger`（效果账本）与 `SelectedSessionSafety` | `Dfo.Server.Application/Effects/EffectOperationLedger.cs`、`Dfo.Server.Application/Commands/SelectedSessionSafety.cs` | A 保留的既有机制，由 B 改写入口 | 附录 H `EffectOperationLedger` 行 → B：A 中账本语义原样保留，B 把它改为协议 CMD 的请求级上下文。账本与安全检查本身不删除，所以不列入「检索为零」；它们读取的帧是 T4，读取的标记接口是 T3，这两项已在清单里（`t009-criteria.md` §9「`EffectOperationLedger`、`SelectedSessionSafety` 的断言随 B 迁移」） |
| 登记点的执行器守卫（`_ = outboxes.Current;`） | `Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:70`（空批）、`:164`（连接已不在、条目被丢弃）；`Dfo.Server.Application/Notifications/NotificationBus.cs:79` | A 的常设机制 | 读一次发件箱，让执行器外的登记即使没有条目也按程序错误失败（RQ-061 的发件箱一半）。它们与 T6 的两处写法相同但用途不同：T6 是 dispatcher 在作业开始时替作业打开发件箱；这 3 处属于登记适配器与通知总线，B 归并之后仍然需要。B 封存时不要求它们为零（T6 的检索只查两个 dispatcher） |
| `GameplayJob.Running` | `Dfo.Server.Abstractions/Execution/GameplayJob.cs:14` | A 的常设机制 | 作业身份（「在执行器上」的判定，Plan 过渡期规则第 3 条）。它是执行器本身的一部分，B 之后仍需要 |
| `DomainTransactionScope.ambient` | `Dfo.Server.Application/Transactions/DomainTransactionScope.cs:24` | A 之前就有 | 环境事务（`AGENTS.md`「领域事务」一条），不是请求级帧 |
| `CommandBus` 的嵌套派发检查 | `Dfo.Server.Application/Commands/CommandBus.cs:26`、`:44` | 随 T4 处理 | 用的是 T4 的 `DispatchDepth`；「命令处理器禁止嵌套 dispatch」这条规则在 B 之后仍成立，换成什么形式由 B 定 |

## 3. 残留检索（SC-12「清单之外，A 没有留下旧门、旧管线行为、转发层或 `[Obsolete]`」）

口径按 TASK-34 主控方裁定第 3 条：
- **旧门**：附录 F「删除」栏的门与信号量，以及 A 删除的全部名字；
- **旧管线行为**：A 之前就有的 `ICommandPipelineBehavior` 实现；
- **转发层**：只为让旧的调用方式在新结构下照常工作而存在、B 归并后就不再需要的代码。从三个方向检索：A 新增的类型（S7）、自称过渡的注释（S6）、R3-1 划给 B 的旧类型（S8）。

脚本 scratchpad `t34/transition.py`（预期写在脚本说明里，先写后跑）。输出、明细与脚本全文在 `logs/task-34-transition.txt`。归不进任何一类的命中为 0。

| # | 检索 | 结果 | 归类 |
|---|---|---|---|
| S1 | `[Obsolete`、`Obsolete(`，范围 `Server/src`、`Server/tests` | 0 | — 。单词 `Obsolete` 只在测试方法名 `AchievementTitleBookTests.Obsolete_task_counters_and_stale_completion_events_cannot_unlock_a_task_title`（文件 `TitleQuestUnlockRoutingTests.cs`）中出现 1 次，同名无关 |
| S2 | A 删除的 49 个名字（37 ＋ 25 ＋ 11 ＝ 73，跨类重复 24），范围 `Server/src` | 只有 `Lease` 命中 22 行 | 同名无关：城镇区域成员资格的 `Lease` 值与一个恢复时长，不是已删除的租约类型（同 TASK-33） |
| S3 | `SemaphoreSlim`；其他等待原语（`ReaderWriterLock*`、`Mutex`、`SpinLock`、`Monitor.Enter` 等 16 种）；`lock (`；以 `Gate` 结尾的标识符 | `SemaphoreSlim` 6 行；其他等待原语 0；`lock (` 357 行；`Gate` 标识符 6 个名字、28 行 | `SemaphoreSlim` 6 行都在附录 F「保留」栏与 `LockRemovalSourceTests` 的信号量名单上（规则初始化 2、口令哈希限流 2、PVF 内容初始化 2）。`Gate` 标识符：`Gate`、`DungeonGate`、`SealedTreasureGate` 是游戏内容（城镇区域种类、掉率槽、拒绝原因），A 之前就有；`buildGate`、`loadGate` 是名单上的两个初始化信号量；`idleGate` 是 A 加的传输层连接登记表短锁，在执行器外（附录 B「传输层的连接注册」行）。`lock (` 的逐层与新增明细见下一段 |
| S4 | `ICommandPipelineBehavior<` 的实现与注册 | 实现 5、注册 5 | 全部是 T3 |
| S5 | `AsyncLocal<` | 8 处 | T4 6 处；`GameplayJob.Running` 与 `DomainTransactionScope.ambient` 各 1 处（§2） |
| S6 | `transitional`、`过渡`、`interim`、`temporary`、`temporarily`（不区分大小写） | 16 行 | `NetworkMessageDispatcher.cs:20` 归 T4；`CharacterOptionsDomainService.cs:21` 是 GH #129 的不透明存储，同名无关；`HttpGameplay.cs:92` 是 F-33 已接受偏离里的临时文件，同名无关；其余 13 行在 `a02bce46` 上已有，与执行、投递、命令无关 |
| S7 | A 新增的类型：冻结点有、基准没有的声明名，101 个 | 101 ＝ A 的常设机制 99 ＋ 授权预留 2 | 按职责归入执行器与入队点、调用链、运行期判据、发件箱、发送端与传输、生命周期作业、定时源、GM／HTTP 路由、预热、口令哈希；授权预留是两个分流键类型。没有一个只为旧调用方式而存在 |
| S8 | A 之前就有、R3-1 划给 B 的类型与成员 | 5 个管线行为、3 个请求级帧类型、`DispatchDepth`、4 个标记接口、`CommandBus` 的内联分支 | 全部在 T1、T3、T4、T5 |

`lock (` 的逐层计数（`a02bce46` → `2bdb1afd`，脚本 `t34/locks.py`）：
- Application 190 → 184、Protocol 11 → 11、Persistence 3 → 3：与附录 F「保留」栏短 `lock` 的计数一致（`t005-closure.md` §3）；
- Infrastructure 106 → 141：多出的 35 行是 TASK-15 预热方法 `WarmAsync` 取用内容源原有的 `gate`，属附录 F「保留」栏的 PVF 内容缓存锁；
- Networking 1 → 11：多出的 10 行是 A 新增的执行器外组件自己的短锁，即 `Networking/Outbound/OutboundSender.cs` 6 行（`submitLock`、每连接队列的 `gate`）与 `Networking/Runtime/TouchSocketConnectionRegistry.cs` 4 行（`idleGate`）。它们在执行器外，不在玩法路径上；
- Dfo.Pvf 2、GmWeb 5，两侧相同。

附录 F 的短 `lock` 计数只覆盖 Application、Protocol、Persistence 三层。上面 Infrastructure 与 Networking 的增量，附录里没有逐行记载，这里是第一次列出。

## 4. 与 `AGENTS.md` 的对应

`AGENTS.md` 中含「过渡」的段落或列表项共 6 个，与 T1～T6 一一对应，没有不对应的（`logs/task-34-transition.txt`「Required check 10」）。

| 项 | `AGENTS.md` 中写明「过渡形态」的位置 |
|---|---|
| T1 | 「命令执行总线」第 2 条（`:356-358`） |
| T2 | 「命令执行总线」handler 一条（`:381-382`） |
| T3 | 「命令执行总线」管线一条（`:362-365`） |
| T4 | 「玩法执行器、发件箱与发送端」请求级上下文一段（`:312-314`） |
| T5 | 同一节「失败与日志」（`:321-326`） |
| T6 | 同一节「发件箱」（`:328-334`） |

本清单比 TASK-33 交来的 T1～T6 多写了一件事：T3 包含 45、58 所依赖的四个命令标记接口。依据是 §6.1 末「拆或并以『B 删除时是不是同一个动作』为准」，它们随 45、58 的去向一起消失。`AGENTS.md` 的 T3 一条原来只写了五层的粒度，本单元按 TASK-33 主控方裁定第 9 条补上了这四个接口名，其余各节不改。`IAccountCommand` 在契约一条里写着「今天只用于检查它与所属会话的账号一致」，与 T3 一致。

## 5. 给 B 的前提提醒（不是过渡机制，不进检索）

- **提交后组装失败会被记成「已确认回滚」（F-132）**。工作单元内登记的组装在参与者提交之后才运行；它若抛出（今天只有取消会漏出，逐收件人的失败在 `Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs:453-462` 被吸收），`UnitOfWorkBehavior` 走回滚分支，`AbortAsync` 因事务已落定而空转，接着 `operations.RollbackConfirmed` 把主结算标为 `ConfirmedRollback`（`Dfo.Server.Application/Transactions/DomainTransaction.cs:150-181`、`Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:63-70`）。客户端重发同一序号时会被当作可重试。
- 今天不可达：生产中 `GameplayOutbox.ComposeAsync` 没有调用方；`EventService.ProjectAsync` 禁止在未提交的可写事务里调用（`Dfo.Server.Application/DomainEvents/EventService.cs:69-70`），所以生产上的组装都在单元外、登记时完成；入站作业的令牌是 `CancellationToken.None`。
- B 若开始在工作单元内登记组装（例如把投递并进协议 CMD 时），先把「提交后失败」与「回滚」分开，再改调用方。出处：spec-001 完整结果评审（代码维度）L4。

# TASK-04 — 执行器与调用链组件（不接线）

## Task Unit Contract

- Revision: 1
- Outcome and references: 执行器、入队点、分流键、调用链、深度上限、自我等待检测、作业令牌、定时源合并，都作为独立组件交付，并配单元测试和反例用例。这一步**不接线**，任何生产路径都不调用它们。对应 Plan T-002；覆盖 RQ-001～RQ-004、RQ-006（机制）、RQ-050、RQ-052～RQ-054（机制）、RQ-072（本组件的判据反例）；以及 AC-001、AC-002、AC-004 和 AC-016 中属于机制的部分。
- Owned boundary:
  - `Server/src/Dfo.Server.Abstractions/Execution/`（新建）：入队点接口、调用链值类型、来源种类、深度常量。
  - `Server/src/Dfo.Server.Application/Execution/`（新建）：执行器实现、分流选择、作业令牌、定时源合并，以及一个注册扩展方法。这个扩展方法**不被任何模块调用**。
  - `Server/tests/Dfo.Server.Application.Tests/Execution/`（新建）：单元测试。
  - 不改其他任何生产代码和既有测试。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release`：0 警告 0 错误。
  2. 新测试隔离运行全绿，并连续跑 20 次，用来防止并发测试抖动。
  3. 全量测试：总数等于 TASK-01 后的基线（7882）加上本任务新增数，失败只有那 2 条 `DimensionDungeonTests`。表征测试 8 条通过、2 条跳过。日志落盘为 `verification/logs/task-04-build-test.txt`。
  4. 用 `grep` 证明 `Server/src` 中除 `Execution/` 以外，没有任何引用新类型的地方（不接线）。
- Dependencies: TASK-01（基线测试数和表征测试）、TASK-02（深度常量 32、下限 2 × 10、RQ-050/RQ-053(a) 的口径）。
- Result review: required — T-004 以后的每个 Task 都建立在这组接口和语义上，包括作业令牌、自我等待检测、`SuppressFlow` 下不继承、完成续延不跑在执行器上、首次入队即启动、停止时排空。这些语义一旦错了，搬迁阶段就会出现死锁，或者执行器的旁路静默生效，而后续的测试会把这种错误一路带下去。

## Detailed Plan

### 1. 接口（Abstractions/Execution）

名字由执行者按仓库后缀约定确定，在 Result 中列出。形状如下：

- **调用链**：值类型，字段有 `RootId`、`ParentId?`、`Depth`、`Source`（来源种类）、`SourceConnection?`。
  - 构造方法：`NewRoot(source, connection?)`，深度为 0；`Child(source)`，深度加 1，根继承父链。
  - 来源种类是枚举：连接、定时、事件、生命周期、GM、HTTP、启动恢复，与 Spec Definitions 一致。
- **深度常量**：具名常量，值为 **32**，旁边注明出处：Spec 附录 A，L ＝ 10。
- **入队点**：
  - 方法形如 `ValueTask<T> RunAsync<T>(GameplayWork<T> work, CancellationToken)`。work 中携带分流键、调用链、CMD 类型名（用于日志），以及工作委托 `Func<CancellationToken, ValueTask<T>>`。另有一个不带返回值的重载。
  - 由于 T-004 的作业要包住整段生产者逻辑，这里用委托，不绑定 `ICommand`。
  - `ICommandBus` 与执行器的关系在 T-004 处理，这里不动。
- **分流键**：值类型，今天只有一个取值空间。分流函数对任意输入返回同一个执行器（RQ-002）。

### 2. 执行器（Application/Execution）

- **结构**：单例，内部一个无界的 `Channel<Job>`，单读者。第一次 `RunAsync` 时用 `Interlocked` 启动消费循环（Plan「执行器的生命周期，不留旁路」）。**不提供任何「未启动就内联执行」的分支。**
- **作业令牌**：
  - 放在 `AsyncLocal` 中，作业开始时设置，结束时立即失效。令牌对象带一个失效标志；即使某个 `ExecutionContext` 副本还留着引用，结束后读取也视为不在执行器上。
  - 对外提供 `IsOnExecutor`、`Current`（当前调用链）和 `AssertNotOnExecutor(string operation)`。最后一个留给 T-006 的「等待网络」检查和 T-004 的门断言使用。
- **完成信号**：`TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)`。等待方的后续代码不会跑在消费循环上。
- **执行**：消费循环逐个取作业，在 `ExecutionContext.SuppressFlow` 下启动，或以等价方式保证作业不继承消费循环之外的上下文。设好令牌后 await 委托，作业完成之后才取下一个（RQ-001）。
- **失败**：作业抛出的异常交给调用方；执行器记 `Error` 日志，带上调用链，然后继续执行下一个（RQ-004）。
- **自我等待（RQ-003 后半句）**：在执行器上调用 `RunAsync` 视为程序错误。立即抛出，记带调用链的 `Error`，不入队；同一作业随后照常以失败结束。
  - T-004 的过渡内联规则会在 `ICommandBus` 层另行处理。这里的执行器本身不内联。
- **深度上限（RQ-053 机制）**：入队时如果 `Depth > 上限`，不入队，记带完整链的 `Error`，返回一个可区分的失败结果或异常类型。超限之后的处理，包括关连接和落定 delivery，在 T-007 接入，这里只提供钩子，或者由调用方根据这个类型自行处理。
  - 调用链只有当前节点，没有完整的「根 → 当前」序列。所以日志要的完整链序列由作业令牌沿父链维护：每个子链保留父链的来源种类与 CMD 类型序列。可以截断到上限长度。
- **日志（RQ-054）**：开始、结束、失败三条日志都带根 ID、父 ID、深度、来源和 CMD 类型；结束日志还带执行时长和入队等待时长。入队等待是 RQ-090 (c) 的服务端指标，按 TASK-03 审查的非阻断项 4，执行器要提供这个数据。结束日志用 Debug 级，失败日志用 Error 级。级别的取舍在 Result 中说明，要兼顾 TASK-03 记下的「命令日志可能成为瓶颈」。
- **停止（RQ-009）**：
  - `StopAsync`：先拒绝新的入队，拒绝时抛出可区分的异常；然后把已入队的作业全部执行完，最后结束循环。
  - 执行器实现 `IAsyncDisposable`，释放时等同于停止，再加一个超时上限，测试结束后不会遗留后台循环。
  - 「先停止 TCP 与 Kestrel」由 T-004 在宿主层面编排。
- **取消**：调用方的令牌在作业开始前被取消时，作业被跳过，调用方收到 `OperationCanceledException`；作业开始后，令牌传给委托。
- **定时源合并（RQ-006 机制）**：一个小组件，同一个源在上一次入队的作业完成之前不会再次入队，返回「已合并」。T-004 接到后台服务上。

### 3. 测试（Application.Tests/Execution）

每条都要能独立复现：

- AC-001：多个生产者并发入队 N 条（N ≥ 1000）。执行顺序等于入队顺序，即按每条成功入队的时刻排序；任意两条的执行区间不重叠；其中一条抛出后，后续照常执行；分流函数对任意输入返回同一个实例。
- AC-002 反例：作业内调用 `RunAsync`，抛出程序错误，日志里带调用链；作业之外的等待方收到失败。
- 令牌：
  - 作业结束后令牌失效，包括在作业里捕获的 `ExecutionContext` 被事后恢复的情形；
  - 作业内以 `ExecutionContext.SuppressFlow` + `Task.Run` 分出的工作不在执行器上；
  - 作业内不用 SuppressFlow 直接 `Task.Run` 分出的工作，在作业结束后读到的也是「不在执行器上」，说明失效标志生效。
- 完成续延：等待方 await 之后的代码不在消费循环线程上，也不持有令牌。
- 首次入队启动：不调用任何启动方法，直接 `RunAsync` 也能执行。
- 停止：
  - 停止时已入队的作业全部执行完；
  - 停止后入队被拒绝；
  - 释放不遗留循环（可以断言循环任务已完成）。
- 取消：开始前取消的作业不执行。
- 深度：
  - 深度为上限时可以入队，为上限加 1 时被拒绝，`Error` 日志里有完整链序列；
  - 判据测试钉住「常量 ≥ 2 × 10」，并与附录 A 的 L 对应。这条判据还要配一条反例：喂给它一个更小的值，证明它会拒绝（RQ-072）。
- 合并：AC-004。上一次的作业在队列中时，再次到期返回已合并；上一次完成之后可以再次入队。
- 日志：开始、结束、失败日志带根、父、深度和类型（用测试 logger 断言）。

### 4. 执行顺序

接口 → 令牌与链 → 执行器核心（入队、循环、完成、失败）→ 自我等待 → 深度 → 停止与释放 → 合并 → 日志 → 测试全部写完并通过 → 连续跑 20 次 → 全量构建和测试 → 不接线的 `grep` 证明 → 写 Result。按绑定方法 test-driven-development，先写失败的测试，再实现。

## Result

（第 3 轮。第 2 轮审查 approved 之后，主控方要求在提交前处理审查 `## Round 2` 的非阻断项 R2-1～R2-5，本轮覆盖前两轮。各轮差异见本节末尾「各轮变化」。）

- Outcome：执行器、入队点、分流键、调用链、深度上限、自我等待检测、作业令牌、定时源合并都已作为独立组件交付，并有 59 条单元测试（含反例）。全部未接线：`Server/src` 中两个 `Execution/` 目录以外，没有任何地方引用这些新类型，`AddGameplayExecution` 也没有被任何模块调用。

### Changed

`Server/src/Dfo.Server.Abstractions/Execution/`（新建）：
- `GameplayCallChain.cs`
  - `GameplayCallSource`：枚举，取值为 Connection、Timer、Event、Lifecycle、Gm、Http、StartupRecovery。
  - `GameplayCallDepth.Limit = 32`：注释写明出处是附录 A，L ＝ 10。
  - `GameplayCallHop(Source, CommandType)`：链上的一跳。
  - `GameplayCallChain`：只读值类型。
    - 字段：`Id`、`RootId`、`ParentId?`、`Depth`、`Source`、`SourceConnection?`，另有 `CommandType?`、`Ancestors`。
    - `NewRoot(source, connection?)`：保持接受连接参数，对应 RQ-050「来源连接（若有）」。
    - `Child(source)`：深度加 1，根 ID 与来源连接沿链继承。
    - `ForCommand(type)`：给节点盖上当前 CMD 的类型。
    - `Describe()`：输出「根 → 当前」序列。
    - `IsInitialized`：用来拒绝 `default` 链。
- `GameplayQueue.cs`
  - `GameplayShardKey(string? Scope)`：`World` ＝ `default`。
  - `GameplayWork<T>`、`GameplayWork`：携带分流键、链、CMD 类型名和委托。
  - 入队点 `IGameplayQueue.RunAsync`：有返回值与无返回值两个重载；契约是「调用返回前已经入队」。不用 `*DomainService` 后缀，这样命令处理器的注入判据会天然拒绝它（RQ-070(b)）。
  - `GameplaySelfWaitException`、`GameplayDepthExceededException`、`GameplayExecutorStoppedException`。最后一个有可选的 `cause`：消费循环自身故障时，以它作为 `InnerException`。
- `GameplayJob.cs`（第 2 轮由 Application 上提；第 3 轮改为不需要 `InternalsVisibleTo` 的形状）
  - `GameplayJob`：静态门面。
    - 公开 `IsOnExecutor`、`Current`、`AssertNotOnExecutor(operation)`。这三个的语义是「在**任一**执行器上」，生产环境只有一个执行器（RQ-001），所以与「在这个执行器上」等价。
    - 委托形式的脱离：`RunDetached(Action)`、`RunDetached<T>(Func<T>)`、`RunDetachedAsync(Func<ValueTask>)`、`RunDetachedAsync<T>(Func<ValueTask<T>>)`。它们在委托执行期间把本流令牌置空，在 `finally` 中还原，包括抛异常和跨 await 的情形。可能被遗忘 `Dispose` 的作用域形态已删除（R2-2）。
  - `GameplayJobToken`：public sealed，没有公开成员，对外不透明。它带所属执行器的身份、失效标志和黏滞的 `ProgramError`。
  - `GameplayJobHost`：public abstract，执行器继承它，这是进入和退出令牌的唯一途径（R2-1）。
    - `protected EnterJob(chain)`：实例方法，以 `this` 作为令牌的所属执行器。
    - `protected static ExitJob`、`ChainOf`、`Fault`、`ProgramErrorOf`。
    - `protected JobRunningHere`：当前令牌属于本执行器时才有值；`protected static JobRunningAnywhere`：任一执行器的当前令牌。
    - `protected ThrowIfRunningHere(operation)`。

`Dfo.Server.Abstractions.csproj`：**恢复原样**。第 2 轮加的 `InternalsVisibleTo("Dfo.Server.Application")` 已撤掉，`git status` 中不再出现该文件。证据有两条：
- 编译通过。Application 今天也没有使用 Abstractions 的任何 internal，例如 `ItemLifecycleScope.Issue`/`IsPending`/`IsForeign`/`AcceptAsync`、`ExperienceProgressionRules.Decide`。
- 用例 `The_abstractions_keep_their_internals_from_the_application` 反射断言 Abstractions 的 IVT 名单里没有 `Dfo.Server.Application`，同时 Persistence 仍在名单上。

`Server/src/Dfo.Server.Application/Execution/`（新建）：
- `GameplayExecutor.cs`：执行器，单例，继承 `GameplayJobHost`。
  - 队列是无界 `Channel`。不设 `SingleReader`，因为释放超时后由释放方并发排空队列。
  - 第一次 `RunAsync` 时用 `Interlocked` 启动消费循环，没有内联分支。
  - **入队校验**，依次为：链未初始化、CMD 类型空白（`ArgumentException`）→ 深度超限 → 自我等待 → 取消 → 停止拒绝。
    - 作业内触发深度超限时，记到当前令牌（任一执行器）的 `ProgramError` 上。
    - **自我等待只判本执行器的作业**（`JobRunningHere`），判定成立时同样记到令牌上。E1 上的作业对 E2 入队或释放 E2 都可以进行（R2-3）。
  - **作业运行**：`ForCommand`、开始日志、`Enter`、作业体都在 `try` 内，`Exit` 放在 `finally`。
    - 之后如果令牌上有 `ProgramError`，作业以它失败，覆盖作业体的返回值或异常（RQ-003，N-1）。
    - 最后才记结束、失败、取消日志，并设置完成结果。
  - **日志防护**：全部日志调用都经 `Log`/`Enabled`，它们吞掉 provider 的异常。日志永远不改变 CMD 的结果，也不会中止循环（B-1）。
  - **循环兜底**：`ConsumeAsync` 如果异常退出，会 `TryComplete(ex)` 写端，记 `Critical`，并以带 `InnerException` 的 `GameplayExecutorStoppedException` 结束全部剩余作业。
  - `StopAsync`：拒绝新入队，排空，然后结束循环。
  - `StopAsync`、`DisposeAsync`、`Dispose`：只在**本执行器**的作业内调用时抛自我等待。超过 `DisposeTimeout`（10 秒）后，释放方自己 `TryRead` 排空队列并 `Abandon` 剩余作业，`Abandon` 是 CAS 安全的。所以排在卡死作业之后的调用方都会收到 `GameplayExecutorStoppedException`，并记一条 `Warning`（B-2）。
  - `IsStarted`、`Completion`。
- `GameplayQueue.cs`
  - `GameplayShardSelector`：任意键都返回同一个执行器。
  - `GameplayQueue`：`IGameplayQueue` 的实现。
  - `CoalescingGameplaySource.TryRun`：返回 `GameplaySourceTick(Outcome, Completion)`，`Outcome` 为 `Enqueued`、`Coalesced` 或 `Rejected`（N-5）。
    - `Rejected` 的判定：调用返回时已经以失败结束，而作业体从未开始。这样，「拒绝」和「已经跑完并失败」能区分开；后者现在由假队列确定性地覆盖。
    - 拒绝时立即释放这个源，`Completion` 中带拒绝的异常。
  - `AddGameplayExecution`：注册扩展方法，目前没有调用方。

`Server/tests/Dfo.Server.Application.Tests/Execution/`（新建，59 条）：

| 文件 | 条数 | 内容 |
|---|---:|---|
| `ExecutionTestSupport.cs` | 0 | 记录结构化字段的测试 logger，以及构造 work 和链的辅助 |
| `GameplayExecutorOrderTests.cs` | 4 | AC-001：8 个生产者共 2000 条，执行顺序等于入队顺序，执行区间零重叠，失败不影响后续；分流选同一实例；经队列执行时作业在执行器上 |
| `GameplayJobTokenTests.cs` | 13 | 7 条基础令牌用例；失败或取消的作业结束后令牌失效；`RunDetached` 内不在执行器上、结束后恢复（返回值形式与 `Action` 形式）；`RunDetached` 抛异常后恢复、`RunDetachedAsync` 跨 await 恢复（两种异步重载）；作业结束后调用 `RunDetached` 不会让令牌复活；E1 上的作业可以对 E2 入队、释放 E2（R2-3）；Abstractions 的 IVT 名单不含 Application（R2-1） |
| `GameplayExecutorSelfWaitTests.cs` | 5 | 自我等待抛出、带链、作业失败、请求不进队列（外层都有 `WaitAsync` 超时）；3 条黏滞用例 |
| `GameplayExecutorLifecycleTests.cs` | 12 | 首次入队即启动；停止时排空；停止中和停止后拒绝入队；在作业内停止或释放视为自我等待；从未运行也能停止；异步和同步释放；卡死作业全程不释放时，排队方收到停止异常；取消的三种情形 |
| `GameplayCallDepthTests.cs` | 7 | 深度判据及其反例、链形状、来源枚举、谱系截断、32 可入队而 33 被拒（日志带完整链）、`default` 链被拒 |
| `CoalescingGameplaySourceTests.cs` | 6 | 排队中或执行中到期都合并；失败后释放源；真实执行器停止后返回 `Rejected`；**假队列**：调用返回前作业体已经跑完并失败，判为 `Enqueued`，不是 `Rejected`；调用返回前失败、作业体未开始，判为 `Rejected`（R2-4） |
| `GameplayExecutorLoggingTests.cs` | 2 | 开始、结束、失败日志的字段 |
| `GameplayExecutorRobustnessTests.cs` | 10 | 6 组抛异常的 logger，结果不变、循环不退出；3 组 CMD 类型为 `""`、`" "`、null：**执行器被占用时**，调用返回即已失败、作业体从未执行，证明是在入队处被拒（R2-4）；**循环兜底**：反射对 `queue` 写端调用 `TryComplete(ex)`，断言记了 `Critical`（带该异常）、`Completion` 完成、之后的 `RunAsync` 收到 `GameplayExecutorStoppedException` |

加法：0 ＋ 4 ＋ 13 ＋ 5 ＋ 12 ＋ 7 ＋ 6 ＋ 2 ＋ 10 ＝ 59，共 9 个文件。相对第 2 轮：令牌 －2 ＋5 ＝ ＋3，合并源 －1 ＋2 ＝ ＋1，健壮性 ＋1，合计 54 ＋ 5 ＝ 59。

### Why（本地决定）

- **日志级别**：开始 `Trace`，结束 `Debug`；失败、自我等待、深度超限 `Error`；取消 `Debug`；释放超时 `Warning`；循环故障 `Critical`。
  - Trace 和 Debug 先问 `Enabled` 再组装参数，未开启时不格式化、不装箱，避免 TASK-03 记下的「命令日志成为瓶颈」。
  - 结束日志带 `ElapsedMilliseconds` 和 `QueueWaitMilliseconds`，供 RQ-090 (c) 使用。测量时要把 `Dfo.Server.Application.Execution.GameplayExecutor` 开到 `Debug`。
  - `CommandLoggingBehavior` 未改。
- **令牌失效**：`Exit` 放在 `finally` 里，成功、失败、取消三条路径都先失效、后设置完成结果。留着旧 `ExecutionContext` 副本的分支读到的令牌都已失效。消费循环在 `SuppressFlow` 下启动。
- **脱离用委托，不直接置空，也不用作用域**：同步内联的传输回调与作业共用同一个流，直接置空会回流进作业的上下文；作用域形态又可能被遗忘 `Dispose`。委托形式在 `finally` 中还原的只是原来的令牌对象：如果作业已经结束，它仍是失效的，不会复活。
- **令牌带执行器身份**：只有当前令牌属于同一执行器时，`RunAsync`、`StopAsync`、`DisposeAsync` 才判为自我等待，因为只有这时等待才会成环。`IsOnExecutor`、`Current`、`AssertNotOnExecutor` 仍然表示「在任一执行器上」：门断言和「等待网络」的检查问的是「是否在玩法串行区内」，与是哪一个执行器无关。
- **为什么用 `protected` 基类，不用 IVT**：IVT 会把 Abstractions 的全部 internal 开放给 Application，包括原本只开放给存储的 `ItemLifecycleScope` 钩子。基类只开放令牌的进出。代价是任何派生类都能进出令牌。今天只有 `GameplayExecutor` 一个派生类，T-009 的结构判据可以把派生类钉死为它。`EnterJob` 是实例方法，不是 static，因为令牌的所属执行器就是调用它的那个实例。
- **黏滞程序错误**：只记第一个，覆盖作业体的结果。`AssertNotOnExecutor` 就是 T-006「等待网络」检查的扩展点：任何调用它的发送路径，即使被 `catch (Exception)` 包着，CMD 也会失败。
- **完整链序列**：每个 `Child` 复制一份不可变的祖先跳序列，并追加父节点的（来源, CMD 类型），最多保留最近 33 跳。父节点的类型由执行器在作业开始时盖到 `Current` 上。
- **取消**：注册在写入队列之前完成；Pending、Running、Cancelled 三态 CAS 决定由谁结束任务。

### 变异验证（第 3 轮全部重做；每处跑完都从备份恢复，恢复后 59/59 通过）

| 变异 | 结果 |
|---|---|
| 去掉 `RunContinuationsAsynchronously` | 续延用例变红 |
| 去掉循环的 `SuppressFlow` | AsyncLocal 继承用例变红 |
| 失败和取消路径不 `ExitJob` | `A_job_that_fails_or_is_cancelled_also_leaves_its_token_invalid` 变红 |
| 全部路径都不 `ExitJob` | 4 条令牌用例变红 |
| 去掉黏滞检查 | 3 条黏滞用例变红 |
| 去掉释放方排空 | 卡死释放用例变红 |
| 日志防护只接 `ArgumentNullException` | 4 组 logger 用例变红 |
| `RunDetached(Action)` 不在 `finally` 中还原 | `Running_detached_restores_the_job_after_a_throw_and_across_awaits` 变红 |
| 去掉 `DisposeAsync` 的自我等待判定 | `Disposing_from_inside_a_job_is_a_self_wait` 变红 |
| **去掉 `TryRun` 中的 `!start.Started`**（R2-4 点名） | 假队列用例 `A_cmd_that_already_ran_and_failed…` 变红 |
| **去掉入队处的 CMD 类型校验**（R2-4 点名） | `""`、`" "`、null 三组全部变红；第 2 轮只有 null 一组会红 |
| 自我等待改用 `JobRunningAnywhere`，即不区分执行器 | `A_job_on_one_executor_may_enqueue_to_and_dispose_another` 变红 |

### Checks

1. 构建：`dotnet build Server/86JP.Server.slnx -c Release`，0 个警告，0 个错误。
2. 新测试隔离连续 20 次：`--filter "FullyQualifiedName~.Execution."`，20 次全部 59/59 通过。记录在 `verification/logs/task-04-execution-x20.txt`，已覆盖前两轮。
3. 全量：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`，日志 `verification/logs/task-04-build-test.txt` 已覆盖前两轮。
   - 各项目测试数：Pvf 80 ＋ Host 165 ＋ HttpApi 50 ＋ Persistence 475 ＋ Infrastructure 969 ＋ Networking 107 ＋ Protocol 1694 ＋ Application 4401 ＝ **7941** ＝ 7882 ＋ 59。
   - Application.Tests 由 TASK-01 的 4342 变为 4401，＋59。
   - 通过 7919，失败 2，跳过 20（Infrastructure 2、Networking 2、Application 16）：7919 ＋ 2 ＋ 20 ＝ 7941。
   - 失败只有 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 和 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，与基线一致。
   - 线级表征测试 `Networking.Tests.Characterization` 通过 8 条，跳过 2 条。
4. 不接线：在 `Server/src` 的 `*.cs` 中检索全部新类型名、命名空间 `Dfo.Server.(Abstractions|Application).Execution` 与 `AddGameplayExecution`，排除两个 `Execution/` 目录后零命中（grep 退出码 1）。没有任何已跟踪文件被改动。
5. 清理：删除了 `Server/tests/Dfo.Server.Application.Tests/TestResults/` 下由 `--blame-hang-timeout` 运行留下的空目录（10 个，0 个文件）；本轮的变异运行再次生成的空目录也已删除。

### Remaining（交给后续 Task）

1. **N-2，交给 T-004**：令牌判定有两处固有盲区。T-004 逐个列出分离点（`TouchSocketNetworkConnectionCloser` 的分离关闭、卡牌计时器等 fire-and-forget）时，逐一确认两条：
   - **没有任何分离点被作业 await。** 在 `SuppressFlow` 下分出的工作如果入队，作业再 await 它，就是检测不到的死锁。
   - **全部分离点都在 `SuppressFlow` 下启动。** 不加 `SuppressFlow` 分出的工作，在作业仍在运行期间读到的是「在执行器上」，可能让门断言漏报，或者被误判为自我等待。
2. **T-004 接线时使用的入口**：
   - 传输层回调（`NetworkPlugins` 的 Connected / Received / Closed，以及关连接器回调）进入时，用 `GameplayJob.RunDetached` 或 `RunDetachedAsync` 包住。
   - **`RunDetached` 的使用由 T-004 用结构判据限定**，只允许出现在传输回调入口和关连接器中（R2-2）。它是公开的逃生口：在别处使用，就能静默绕过门断言和自我等待检测。另外，脱离状态下的回调如果 await 一条刚入队的 CMD，仍是检测不到的死锁，只能靠过渡期的 2 秒发送期限兜住。这一点也要并入分离点核对。
   - 门断言用 `GameplayJob.IsOnExecutor` 和 `AssertNotOnExecutor`。
   - 宿主停机编排用 `GameplayExecutor.StopAsync` 和 `Completion`。
   - **令牌按执行器区分**（R2-3）：只有对同一个执行器入队、停止或释放才判为自我等待；对另一个执行器实例的这些调用可以照常进行。`IsOnExecutor` 和 `AssertNotOnExecutor` 表示「在任一执行器上」。所以 T-004 的测试辅助把测试体作为作业运行时，测试体里自建容器、释放容器都不会误判；但门断言仍会把它们视为在执行器上。
   - T-009 的结构判据可以再钉一条：`GameplayJobHost` 的派生类只能是 `GameplayExecutor`。
3. **深度超限之后的处理（RQ-053）由 T-007 接入**：`GameplayDepthExceededException` 带 `Chain.SourceConnection`。RQ-053(a) 只关闭「根来源是客户端连接」的那个连接，由 T-007 按 `Source` 判定；`NewRoot` 不收紧。
4. **RQ-003 与 AC-002 中「等待网络」那一半，留给 T-006**：届时在发送路径上调用 `GameplayJob.AssertNotOnExecutor`，黏滞标记会自动生效。
5. **消费循环兜底的残余**：兜底现在由反射用例覆盖（从写端注入故障）。残余是：如果异常出自 `await job.RunAsync()` 内部，正在执行的那个作业处于 Running 状态，`Abandon` 不会结束它。按现在的结构，这条路径不可达。
6. **T-009 文档**：`AGENTS.md`「接口后缀标明类别」表要补一行「入队点」（`IGameplayQueue`）。

### 各轮变化

**第 2 轮（相对第 1 轮）：**


- **B-1**：入队时校验 CMD 类型；作业开始前的步骤移进 `try`；日志防护；循环兜底。
- **B-2**：释放超时后由释放方自行排空队列。
- **N-1**：程序错误黏滞到令牌上，作业体吞掉也照样失败。
- **N-3**：补失败路径令牌失效的用例；自我等待用例加超时。
- **N-4**：改正各项目计数的映射。第 1 轮把 Pvf 与 HttpApi、Host 与 Protocol 的名字配错了，和数无误。
- **N-5**：`DisposeAsync` 加自我等待断言；合并源增加 `Rejected`。
- **N-6**：令牌门面上提到 Abstractions，并增加 `Detach` 作用域。

**第 3 轮（相对第 2 轮）：**

- **R2-1**：撤掉 IVT，改为 `GameplayJobHost` 基类，令牌对外不透明。
- **R2-2**：`Detach()` 作用域改为 `RunDetached` / `RunDetachedAsync` 委托形式。
- **R2-3**：令牌带执行器身份，自我等待只判同一执行器。
- **R2-4**：`Rejected` 改用假队列写确定性用例；CMD 类型用例改为证明拒绝发生在入队处；补循环兜底的反射用例。
- **R2-5**：Remaining 补上 `RunDetached` 的判据限定，以及令牌按执行器区分的语义。


### 主控方补充（第三轮复核之后，提交之前）

- **T-004**：开启判据时，同时加一条反射判据：只有 `GameplayExecutor` 能派生 `GameplayJobHost`。另外把 `ExitJob`、`Fault`、`ProgramErrorOf` 改为实例方法，并断言令牌属于本执行器（审查 Round 3 第 1 点）。原因：现在任何派生类都能通过静态的 `ExitJob` 让真实作业「离开」执行器。
- **残余**：Persistence 原有的 IVT 能访问 `GameplayJob` 的 internal 成员。这是既有授权带来的，不在本任务内收紧。
- **分片时要重新处理**：E1 上的作业等 E2、E2 上的作业又等 E1，这种跨执行器的等待环目前检测不到。生产环境只有一个执行器，今天不会出现；但 RQ-003 字面上不区分执行器，启用分流键时必须重新处理。

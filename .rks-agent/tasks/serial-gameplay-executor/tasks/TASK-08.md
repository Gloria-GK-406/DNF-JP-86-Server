# TASK-08 — T-004b1：注册执行器、直发 2 秒期限、注册后果的处置

本文件另含一节「T-004b 拆分」（文末），是 T-004b 全部单元的边界提纲，供主控方裁定与后续单元规划引用；它不属于本单元的执行范围。

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - **注册执行器**：`AddCommandDispatching()` 调用 `AddGameplayExecution()`。凡是有命令总线或 dispatcher 的容器（组合根、经 `ApplicationModule` 或 `AddNetworkMessageDispatching` 组装的测试容器）都有同一个单例执行器与入队点；生产代码在本单元仍不向它入队。对应 TASK-06 §7 待办 3k。
  - **直发 2 秒期限**：`TouchSocketNetworkMessageSender` 在执行器上的每次传输写入，套上「放弃等待并抛出」的 2 秒期限（`WaitAsync` 式，plan-review-r2 R-5）；超时即在 `SuppressFlow` 下分离关闭那个会话，并以 `TimeoutException` 抛给调用方。执行器外的发送行为不变。对应 Plan「过渡期规则」第 4 条、TASK-06 §7 待办 3g、4f（登记 VC-8 提前）。
  - **注册后果的处置**：注册后，带容器的 `TestGameplay` 调用改用容器执行器（TASK-06 §1 第 2 种来源）。由此新出现的失败或挂起，按 §4 逐条分类处置，并在**同一提交**里完成（TASK-07 主控方补充「残余风险」）。
  - **探针工具固化**：把 TASK-07 的探针工具从 scratchpad 复制进仓库的验证目录，去掉绝对路径，供 T-004b 后续单元逐族复跑。
  - 覆盖：RQ-008（为过渡期等待环兜底）、RQ-036 的期限口径（提前落实 VC-8）、Plan T-004「执行器的生命周期」「过渡期规则 3、4」。本单元不迁入任何附录 A 的入口。
- Owned boundary:
  - 生产代码，只限：
    - `Server/src/Dfo.Server.Application/Execution/GameplayQueue.cs`（`AddGameplayExecution` 加 `AddLogging()`，更新文档注释）；
    - `Server/src/Dfo.Server.Application/Commands/CommandDispatchingExtensions.cs`（调用 `AddGameplayExecution()`）；
    - `Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs`，以及 `Server/src/Dfo.Server.Networking/Runtime/` 下新增的一个期限与分离关闭的辅助文件；
    - §4 第 (iv) 类确需的注册或释放缺陷修正，限于上述文件与 `Server/src/Dfo.Server.Application/Execution/`。
  - 测试：新增 `Server/tests/Dfo.Server.Networking.Tests/Runtime/` 下的期限测试（含一条真实 socket 用例）、`Server/tests/Dfo.Server.Application.Tests/Execution/` 下的注册测试；按 §4 处置的既有测试文件。
  - `.rks-agent/tasks/serial-gameplay-executor/verification/` 下：`logs/task-08-*.txt`；`task-07-conflicts.md` 追加一节（只在 §4 发现新冲突时）；新目录 `probe-tools/`。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 警告，0 错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-08-build-test.txt`。
     - 总数 ＝ 7999 ＋ N（本单元新增）－ D（本单元执行删除的冲突用例），各项目逐项对照 TASK-07 Result 的计数，写出加法；
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20（若 D 中含跳过用例，另写加法）；表征测试 8 条通过、2 条跳过；
     - 没有挂起转储。
  3. **隔离复跑**：Application.Tests、Networking.Tests、Protocol.Tests 各一次，落盘 `logs/task-08-isolated.txt`。
  4. **期限的真实 socket 用例**：隔离连续跑 10 次、在全量负载下 1 次，全部通过，落盘 `logs/task-08-deadline-x10.txt`。
  5. **测试改动核对**：
     - 新增测试逐条列名，N 写出加法；
     - §4 处置过的每个既有测试文件写明类别 (i)～(iii) 与理由；只改包裹方式的文件，用 TASK-07 的差异核对脚本证明方法集合与断言行多重集合不变；
     - 删除或改写的冲突用例写成「旧断言 → 新断言 → 保护意图」。
  6. **生产改动的边界**：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件；
     - `grep -rn "IGameplayQueue\|GameplayExecutor\b" Server/src`：命中只在 `Abstractions/Execution`、`Application/Execution` 与 `CommandDispatchingExtensions.cs`，即本单元没有让任何入口入队；
     - `grep -rn "RunDetached" Server/src`：零处使用（定义除外），本单元不引入流边界。
  7. **探针工具冒烟**：从 `verification/probe-tools/` 运行 `apply` → `dotnet build` 成功 → `revert`；还原证明三项同 TASK-07 Required check 6（sha1 快照逐字节相等、`git diff --stat -- Server/src` 与 `git status --short -- Server/src` 只剩本单元的正式改动），输出写入 `logs/task-08-probe-smoke.txt`。
- Dependencies: TASK-07（冲突总清单、探针报告与工具、Remaining 中「注册之后要复核」的清单）；TASK-04 的执行器与令牌；TASK-06 的 `TestGameplay`。
- Result review: required。
  - 注册点是后续每个 T-004b 单元与 T-004c 都依赖的组合方式。注册放错层（例如漏掉只调 `AddCommandDispatching` 的测试容器），要到 T-004b2 dispatcher 入队时才以解析失败暴露，并会被误当成迁移问题。
  - 期限只在执行器上生效，而本单元结束时生产路径上没有任何作业，所以全量测试几乎碰不到它。它是否真能打断 R-5 的等待环，取决于 TouchSocket 的 `SendAsync` 是不是真正异步返回；只有那条真实 socket 用例能说明，而单靠它不足以证明在 Linux 上同样成立。
  - §4 的失败分类靠人判断：把一条「两个包裹区域靠交错制造并发」的用例误当成误包、解包了事，会让它在 T-004c 之后以挂起的形式重现，很难追回这里。
- Task-specific authorization:
  - 探针工具冒烟（Required check 7）期间，允许临时改动 `Server/src`，用后按 TASK-07 的三项还原证明；不提交，不 `git stash`。
  - 只允许删除或改写「因本单元的改动而失败或挂起」的冲突用例，每条在 Result 中列出；其余冲突用例保持原样，留给它们在拆分表中所属的单元。

## Detailed Plan

### 1. 注册

- `AddGameplayExecution` 先调用 `services.AddLogging()`（幂等）。原因：只调 `AddCommandDispatching` 的测试容器未必有日志，而 `TestGameplay` 用 `GetService(typeof(GameplayExecutor))` 解析时，缺 `ILogger<>` 会直接抛异常，不会退回第 3 种来源。文档注释删掉「Not called by any module yet」，改为说明注册在 `AddCommandDispatching` 内。
- `AddCommandDispatching` 开头调用 `services.AddGameplayExecution()`。
  - 放在这里而不是 `ApplicationModule`：`AddNetworkMessageDispatching` 也经它注册，自建容器的测试（约 11 个文件只调 `AddCommandDispatching`）与组合根都会拿到同一个执行器。T-004b2 起 dispatcher、T-004c 起总线都要注入 `IGameplayQueue`，注册必须与它们同处。
  - 生产组合根经 `ApplicationModule` 调到它，Host 不另加注册。
- 执行器是单例，随容器释放而停止（`IDisposable` / `IAsyncDisposable`，空队列立即结束）。本单元**不**加宿主生命周期服务：停机顺序（RQ-009）要等断连拆除成为生命周期 CMD 才有可排空的东西，归 T-004b3。

### 2. 直发期限

**位置**：`TouchSocketNetworkMessageSender` 里只有两处真正的传输写入：`SendBatchAsync` 与 `SendAsync` 中的 `((ISender)session).SendAsync(...)`。`SendExplicitAsync` 经 `SendAsync`；`NotificationBus` 与 `IGameMessageSender` 都委托给这个发送器。期限只加在这两处，不碰编码、身份校验与显式投递的逻辑。

**新增辅助**（`Networking/Runtime/` 下一个 internal 文件，名字由执行者定）：

- `AwaitDirectSendAsync(Task send, TimeSpan deadline, Action onTimeout, CancellationToken ct)`：
  - 调用方传入已经发起的写入任务；令牌照旧传给 TouchSocket，与今天相同。
  - `GameplayJob.IsOnExecutor` 为假：直接 `await send`，行为与今天逐字节相同。
  - 为真：`await send.WaitAsync(deadline, ct)`。
    - 超时：给被放弃的 `send` 挂一个只在出错时执行的续体，观察掉它的异常（不留未观察异常）；调用 `onTimeout` 一次；抛 `TimeoutException`，消息含连接 ID 与期限。不抛 `OperationCanceledException`，因为不少调用方用 `catch (OperationCanceledException) when (ct.IsCancellationRequested)` 区分取消，超时不能被误读成取消。
    - 取消：照常抛 `OperationCanceledException`，同样观察被放弃的 `send`，不关连接（今天取消也不关）。
- `StartDetachedClose(Func<Task> close, ILogger logger)`：在 `ExecutionContext.SuppressFlow()` 下 `Task.Run` 启动关闭，捕获并以 Warning 记录失败。已处于 `SuppressFlow` 时不再嵌套调用（嵌套会抛异常；参照 `GameplayExecutor.EnsureStarted` 的写法）。这是分离点清单（TASK-06 §7 待办 3e）的第一项，由 §3 的 f 条测试证明它不继承作业令牌。

**发送器的接法**：

- 期限取 `internal TimeSpan DirectSendDeadline { get; init; } = TimeSpan.FromSeconds(2)`，测试可以调短。
- **按每次传输写入计（每帧一次），不按整个调用计。** 理由：
  - 打断 R-5 的等待环只要「卡住的那一帧在 2 秒内放手」；
  - 按帧计对「慢但在读」的客户端最宽松，提前落实的 VC-8 面最小。今天直发没有任何期限，选角批次上界约 454 KB（TASK-05 N-7）。
  - 残余：一个持续慢读的客户端，在一个批次内可能累计让执行器等待超过 2 秒。T-006 把发送移出执行器后消失；在 Result 的 Remaining 中登记。
- 超时关闭的是**这次写入所用的那个会话对象**（`connection.Session`），不按连接 ID 重新查注册表，这样不会关到替换后的新连接。
- 超时记一条 Warning：连接 ID、期限、消息类型名。
- `TouchSocketNetworkConnectionCloser` 本单元**不改**。它今天的分离关闭不在 `SuppressFlow` 下，关闭路径可能继承调用方的 AsyncLocal 帧（会话门借用帧、效果操作帧）。改它会改变关闭回调能看到的帧，必须与「关闭回调成为生命周期 CMD」同一步做，归 T-004b3。

**与 `NotificationBus` 的关系**：通知总线有自己的每收件人 2 秒期限，并把令牌传进发送器。两个期限同时生效时，谁先触发只改变那条 Warning 属于「到期限」还是「投递抛异常」，收件人一律判 Failed。本单元在 Result 中写明这一点；断言日志分类的 K5 用例（T-006）不经执行器，不受影响。

### 3. 新增测试

**期限辅助**（`Networking.Tests/Runtime/`，确定性，用 TCS 模拟写入）：

| 条 | 情形 | 断言 |
|---|---|---|
| a | 在执行器上，写入永不完成 | 在 [期限, 期限 ＋ 余量] 内抛 `TimeoutException`；`onTimeout` 恰好调用一次 |
| b | 不在执行器上，写入永不完成 | 2 倍期限后仍未完成；写入完成后正常完成；`onTimeout` 未调用 |
| c | 在执行器上，写入在期限内完成 | 不抛异常，`onTimeout` 未调用 |
| d | 在执行器上，令牌取消 | 抛 `OperationCanceledException`，`onTimeout` 未调用 |
| e | 被放弃的写入随后出错 | 不产生未观察异常（订阅 `TaskScheduler.UnobservedTaskException` 并强制 GC；若不稳定，改为经辅助暴露的续体断言并在 Result 说明） |
| f | 在作业里调用 `StartDetachedClose` | 关闭委托里 `GameplayJob.IsOnExecutor` 为假，且作业此时仍在运行（作业等一个 TCS，直到关闭委托把观察结果写回） |

「在执行器上」一律用真实的 `GameplayExecutor` 作业取得，不铸令牌。

**真实 socket（R-b 那条测试）**：

- 服务端用真实的 `TouchSocketNetworkMessageSender`；在 `ServerMessageEncoderRegistry` 里为测试消息注册一个每帧接近 `DnfFrameProtocol.MaximumFrameLength`（1 MiB）的编码器。客户端把接收缓冲设小，并且不读。
- 在一个 `GameplayExecutor` 作业里调用 `SendBatchAsync`，总量足以塞满两端内核缓冲（起点 64 MiB，执行者按平台调整并在 Result 写明），期限调到 500 ms。
- 断言：
  - 作业在期限 ＋ 余量内以 `TimeoutException` 结束；
  - 客户端在若干秒内观察到连接被关闭（读到 0 或连接重置）；
  - 紧接着向同一个执行器入队的另一个作业立即执行，执行器没有被冻结。
- 另有一条断言：默认期限是 2 秒。
- 搭建方式可以参照 `PreparedBatchTests`、`TouchSocketNetworkMessageSenderExplicitDeliveryTests`，以及 TASK-05 的 `A_slow_reader_with_a_small_window_gets_a_large_batch_in_full_before_the_fin`。
- **停止条件**：如果这条用例显示 TouchSocket 的 `ISender.SendAsync` 在内核缓冲满时**同步阻塞**才返回任务（`WaitAsync` 拿不到控制权），停止本单元，回报主控方。这意味着 Plan 过渡期规则 4 与 R-5 的前提不成立，期限需要另一种实现，例如把写入移出调用线程，而那会改变单连接顺序的论证。

**注册**（`Application.Tests/Execution/`）：

- 只调 `AddCommandDispatching()`、**不加日志**的容器：能解析 `GameplayExecutor` 与 `IGameplayQueue`，`GameplayShardSelector.Select` 返回同一个实例。
- `TestGameplay.RunAsync(services, …)` 在容器执行器上运行：运行后该实例 `IsStarted` 为真。
- 释放容器之后，`Completion` 已完成。

N 以实际为准，Result 中逐条列出。

### 4. 注册后果的处置

**预判**：注册只改变一件事——带容器的包裹区域（`RunAsync(services, …)`、`Run(provider, …)`）从「每次新建一个执行器」变成「共用容器执行器」。整方法包裹的 867 条不带容器，测试体本身已在辅助执行器上，夹具里带容器的调用按 `TestGameplay` 的第 1 种来源内联，不受影响。受影响的只有：没有整方法包裹、却在同一容器上**同时**开着两个包裹区域的用例。原来两个区域各在一个执行器上并行，现在会串行。如果用例靠两者交错（TCS、`Barrier`、`ManualResetEventSlim`）推进，就会挂起或超时失败。按目前的阅读，冲突总清单里的 21 条 T-004b 用例都不会因注册本身失败，但这要由运行确认。

**先主动复核**（即使全绿，也逐项写结论：现在为什么安全，以及哪一个后续单元必须再看）：

- TASK-07 Remaining「注册容器执行器之后要复核的区域」：`DomainEventDispatchTests.A_slow_live_group…`、`WeaknessGoldRecoveryTests`、`ItemSaleTests.Two_concurrent…`、`MailboxCoreIntegrationTests.ConcurrentClaim…`、`QuestHelperPuppetTests`、`SaoTowerStoreTests`、`DungeonVisitDomainServiceTests.Concurrent_first_visits`、`PartySettingsDomainTests.ConcurrentUpdates…`，以及使用 `ShopFixture` 的冲突用例；
- TASK-06 主控方补充 N-4 的 Protocol 5 个文件：LeaveDungeon、PartySelectDungeon、MonsterDeath、QuestClearVertical、Joust；
- TASK-06 Remaining：`AuthenticationFixture.Consume` 与 `AuthenticationConsumptionTests.ExactCredentialBinds…`；Networking 真实 socket 用例里被包的短作业。

**全量中新出现的失败或挂起**（对照 TASK-07 的 7977 条通过；挂起用 `--blame-hang-timeout` 的转储定位到用例），逐条分类：

| 类 | 判定 | 处置 |
|---|---|---|
| (i) | 冲突总清单里已有的用例 | 本单元执行它的处置，从拆分表中原属单元移出，记入 Result |
| (ii) | 不在清单里；两个直接调用的发起方各自包裹，靠交错制造并发 | 这是执行器之前的并发语义，按 TASK-06 §2.3 判为冲突：改写为顺序断言或删除，写明理由；追加到 `task-07-conflicts.md` 新的一节「TASK-08 注册新增」，并更新总数加法 |
| (iii) | 包裹区域里调用了生产入口，报自我等待（`GameplaySelfWaitException`） | R-2 的自动检测生效：把入口调用移出包裹（TASK-06 §2.4），断言不变 |
| (iv) | 注册本身的缺陷（解析失败、释放顺序、释放超时） | 在 Owned boundary 内修正生产代码，并补一条测试 |

**两种需要额外留意的形状**：

- 在容器执行器的作业里释放这个容器，会因自我等待抛出（`DisposeAsync` 断言不在本执行器上），按 (iii) 处理：把释放移到包裹之外。
- 容器释放时，执行器上仍有被卡住的作业：释放会等满 10 秒，然后放弃余下的作业。这表现为用例变慢，而不是失败。全量日志里出现「did not drain within」的 Warning 时，逐条按 (ii) 判定。

### 5. 探针工具固化

- 把 scratchpad `t07/probe/` 下的工具复制到 `.rks-agent/tasks/serial-gameplay-executor/verification/probe-tools/`。
  - 至少包括：`patch.py`、`GameplayProbe.cs`、`classify.py`、`shared.py`、`agg.py`、`alist.py`、`emit.py`、`p1bsplit.py`、`run.sh`、`runall.sh`、`snapshot.py`、`snapcheck.py`。
  - 不复制各轮的原始转储。
- 绝对路径改为由脚本所在目录推导仓库根，输出目录用环境变量或参数传入。分类规则（`classify.py` 的入口帧名单）原样保留。后续单元迁入入口后，入口帧仍在栈上但已在执行器上，探针不再记录，名单不必改。
- 冒烟按 Required check 7 执行，不跑测试。`patch.py` 若因本单元改了 `TouchSocketNetworkMessageSender.cs` 而对不上文本，只改它的匹配模式，不改挂点集合。

### 6. 执行顺序

1. 期限辅助与 a～f 测试 → 接入发送器 → 真实 socket 用例；先跑 Networking.Tests。真实 socket 用例触发 §3 的停止条件时，到此为止。
2. 注册与注册测试；跑 Application.Tests。
3. §4 主动复核；全量（带挂起检测）；逐条分类处置，重跑到稳定。
4. 探针工具复制与冒烟，还原证明。
5. 正式全量 → 隔离复跑 → 期限用例 ×10 → 测试改动核对 → 边界 grep → 写 Result。

### 7. 为什么生产行为不变

- 注册只增加单例；本单元没有任何生产代码向执行器入队（Required check 6 的 grep 证明）。
- 期限只在 `GameplayJob.IsOnExecutor` 为真时生效，而本单元结束时生产路径上没有作业。所以它对生产的可见影响，从第一个会发包的入口迁入（T-004b2）才开始，那时它就是 Plan 允许的 VC-8 提前。
- 注册使容器释放多了一次空队列排空，耗时可忽略。

## T-004b 拆分

### 1. 拆分原则（适用于 T-004b 的每个单元）

1. **按入口族切，每单元一个提交，提交时全绿可运行。** 入口族按 TASK-07 探针报告 §3 的十族与 spec 附录 A 的行对应。
2. **作业切分**（Plan 过渡期规则 1）：一个入口的整段逻辑——创建 DI scope、取门、开事务、取信号量、发送——都放在同一个作业里。执行器外只留：解包、读取到期项（只读）、组织作业、等待作业完成。
3. **部分迁移期间的不变量**：没有迁入的执行器外代码，不得**一边持门、一边等待一个已迁入的入口**。每个单元逐个列出新迁入入口在执行器外的调用方，确认这一点。
   - 反过来的情形是允许的：执行器上的作业等待一个由执行器外持有者占着的门。这只是延迟，不会成环，因为执行器外的代码在 T-004c 翻转之前从不等待执行器。也正因为如此，门断言不能在 T-004b 期间打开，见 §5 第 1 条。
4. **CMD 类型与来源**：`GameplayCallChain.NewRoot(<来源>, <连接>)`。CMD 类型统一写作 `<来源前缀>:<名字>`：
   - `inbound:<ClientMessageType>`（来源 Connection，带连接）；
   - `lifecycle:connected` / `lifecycle:disconnected` / `lifecycle:close-cleanup`（Lifecycle，带连接）；
   - `timer:<服务类型名>`（Timer）；
   - `event:<订阅 ID>`（Event）；
   - `gm:<端点>`（Gm）、`http:<路由>`（Http）；
   - `startup:<名字>`（StartupRecovery）。
   调用链的父子关系由 T-007 设定，T-004b 一律用新根。
5. **测试侧的三条处置规则**（沿用 TASK-06 §2）：
   - 新报自我等待：包裹区域里调用了刚迁入的入口，把入口调用移出包裹；
   - 作业里轮询某个入口的进度：解包，或改为每次求值一个短作业；
   - 冲突用例按 §3 的分配处置。只在使它失败的那个单元执行；到了所属单元仍然通过的，顺延到 T-004c，并在 Result 中记录。
6. **每单元的轻量自检**（翻转与判据要到 T-004c 才有，这两项替代它们的一部分作用）：
   - **入口证据测试**：经真实入口驱动——真实 socket、托管服务的一次 tick、真实 HTTP、真实分发器——在入口下游注入的替身里记录 `GameplayJob.IsOnExecutor` 与 `GameplayJob.Current`（来源、CMD 类型），断言在执行器上。它同时是 AC-006「来源 → CMD 类型 → 证据」的对账行。
   - **探针复跑**：用 `verification/probe-tools/`，只跑与本族相关的项目；本族入口在 (A) 中的记录应当归零。授权方式同 TASK-07：临时改 `Server/src`，三项还原证明。
   - 全量与隔离复跑；表征测试 8 过 2 跳。

### 2. 单元表

单元编号沿用 TASK 序号；「探针 §」指 `verification/task-07-probe.md` §3 的分组。

| 单元 | 交付 | 附录 A 行 ／ 探针 § | 处置的冲突用例 | 主要风险 | 完成条件（除全量全绿、表征测试与计数加法外） |
|---|---|---|---|---|---|
| **TASK-08**（b1，本单元） | 注册执行器；直发 2 秒期限（3g、4f 登记）；注册后果处置（3k）；探针工具固化 | 不迁入口 | 0 条预分配；注册暴露的按 §4 处置 | TouchSocket 写入是否真异步；注册暴露的挂起 | 见上文 Required checks |
| **TASK-09**（b2 入站） | dispatcher 入队并等待（RQ-005 背压由收包回调等待作业自然成立）；dispatcher 的整段——scope、`IsBlocked`、`IsSuperseded`、`CaptureAuthority`、`NetworkCommandAuthorization.Enter`、效果账本 `DispatchAsync`——移进作业（3b）；Game 与 Channel 的 `OnTcpReceived` 以 `RunDetached` 为流边界（3f 的 Received 部分）；纯遥测 handler 按 handler 注册元数据在入队前分流、留在执行器外，逐个列入附录 B（3d）；`SetUserAreaHandler` 的门外写随入站进入执行器（5a 之一） | 行 1、2 ／ §3.1（dispatcher 与 Received）、§3.2 | `EffectOperationReplayTests` 7 条（:159、:229、:253、:269、:292、:345、:371）。其中 :229、:253、:345 在执行器外建立帧、再经总线发命令，可能要到翻转才失败，届时顺延 T-004c | handler 内的同步阻塞（例如 `:292` 的 `ManualResetEventSlim`）会冻结整个执行器；作业在门上等待执行器外的观察者；关闭回调可能被内联到作业的发送中（R-5，靠 TASK-08 的期限兜底）；中间态里执行器外的拆除落位置（`AloneAsync`）与执行器上的事务撞 SQLite 30 秒忙等，把 Networking 10 秒预算的用例拖红 | 入口证据测试：真实 socket 发一条消息，handler 下游替身记录到 `inbound:` 与 Connection 来源；AC-003 的前两条线级测试（同一连接的第二个包在第一个 CMD 完成后才开始；两个连接交替发包，各自顺序不变）；探针 §3.1 中 dispatcher 与 Received、§3.2 归零；附录 B 遥测 handler 清单 |
| **TASK-10**（b3 连接生命周期与登录会话） | `OnTcpConnected` / `OnTcpClosed` 的观察者（含初始 NOTI、断连清理、`AloneAsync` 拆除落位置）作为生命周期 CMD；`TouchSocketNetworkConnectionCloser` 的分离关闭改到 `SuppressFlow` 下（3e），关连接器与 Connected/Closed 回调以 `RunDetached` 为流边界（3f 余下）；`LoginSessionRuntime` 的连接观察；`LoginSessionMaintenanceService` 的维护扫描作为定时 CMD（经 `CoalescingGameplaySource`）；停机顺序：TCP 与 Kestrel 停止接受 → 执行器排空 → 执行器停止（3i），附带 AC-007 的停机测试与「CMD 关闭自己的来源连接不自锁」 | 行 3、4 ／ §3.1（Connected、Closed）、§3.3、§3.4 | `SelectCharacterLifecycleTests` 4 条（:236、:261、:288、:365；:236 与 :365 可能顺延 T-004c）；`OnlineUserServiceTests` 3 条（:30、:67、:519） | TouchSocket 关闭会等收包回调结束，而生命周期 CMD 排在入站 CMD 之后，必须证明不成环（RQ-008）；改关连接器的流向，会改变关闭回调能看到的门借用帧；停机时托管服务的停止次序；协调器 `Dispose` 在容器释放时取锁（探针 §3.9 的提醒） | 断线、关连接、被顶替三种情形下清理都在执行器上（入口证据测试）；停机测试：在线玩家的拆除落库在执行器停止前完成；探针 §3.1 中 Connected/Closed 以及 §3.3、§3.4 归零 |
| **TASK-11**（b4 定时源） | 33 个后台服务、5 个调度任务、两塔离场扫描、卡牌布局与自动翻牌计时器，每次到期入队一个定时 CMD（`CoalescingGameplaySource`，RQ-006 接线），一次 tick 的整段逻辑是一个作业；计时器回调在 `SuppressFlow` 下启动（3e）。调度任务的 `Task.Run` 与并发槽要等 T-005 才删除（附录 F），裁定见 §5 第 3 条 | 行 5、6、7 ／ §3.6、§3.8 | `DungeonCardPresentationCoordinatorTests` 2 条（Protocol :109、:235）；`IllusionTowerPublicFlowTests:149` | TASK-06 N-3：卡牌测试里有 21 处作业内轮询，等的是计时器推进，计时器 CMD 落到同一个执行器后会互等；TASK-07 Remaining 中 Weakness 等并发包裹；四个每日刷新 worker 的入口用例要从「在 T-004c 会变红」转为附录 A 的对账证据；服务数量多，执行者可在规划时再切成「后台服务与调度」「计时器与两塔离场」两步 | 每类定时源至少一条入口证据测试；RQ-006 合并的接线测试（AC-004）；探针 §3.6、§3.8 归零 |
| **TASK-12**（b5 事件分发） | `DomainEventDispatcher` 仍是生产者：在执行器外读取到期 delivery，把每条 Reliable、LiveOnly 的消费与失败重试入队成事件 CMD 并等待；启动扫描遗留 delivery 以 StartupRecovery 来源入队；`DirectAsync` 不变；组门要等 T-005 才删除 | 行 8 ／ §3.5 | `UsageQuotaTests:117`（第 1 个事务块） | `DomainEventDispatchTests.A_slow_live_group…`：被阻塞的 live 作业会占住执行器；`TransactionalExecutionGate`（数据库准入门）在作业里等待；Reliable 失败后「另起事务记录重试」必须仍在同一个作业里 | 入口证据测试（Reliable、LiveOnly、重试、启动扫描各一条）；AC-015 中可在此证明的部分；探针 §3.5 归零 |
| **TASK-13**（b6 GM/HTTP 与启动） | GM 写操作 34 条、Cera 提醒、读内存玩法状态的 GM 查询、启动器注册与登录，以端点处理整体作为 GM/HTTP CMD，不在中间件层入队；只读 SQLite 查询留在外面，列入附录 B；口令哈希移到执行器外，并发有上限（3c，含 `GmAccessDomainService` 的 GM 登录）；启动期工作作为作业（3j：`EpicBook` 迁移、`GmBootstrapService`、Joust 静默恢复、经验存储策略安装）；预热（3h：`Lazy` 内容源、冷进程首次选角 `PrepareSelectedCharacterState`、首次进本内容），列入附录 B | 行 9、10、11 ／ §3.7、§3.9（启动部分） | `GmPlayerSamplingHttpTests:14`；`AuthenticationConcurrencyTests.OverlappingPasswordReads…`（Persistence） | HTTP 中间件（`HttpApiExceptionMiddleware` 等）出现在 (A) 栈上，要确定在端点而不是中间件入队；`GmBootstrapService` 在 `StartAsync` 里入队，依赖「首次入队即启动」；哈希上限的排队语义（超出时排队而不拒绝，RQ-007） | AC-005 的「哈希不在执行器上」断言；GM、HTTP、启动各一条入口证据测试；探针 §3.7、§3.9 的启动部分归零 |
| **TASK-14**（b7 对账收口） | 五个项目全量探针复跑：(A) 只剩 §3.10「测试直接调用总线」这一类（翻转的对象）；附录 A、B 对账表（AC-006）；分离点清单终核（3e）：期限关闭、关连接器、卡牌计时器、TASK-05 的准入循环与每连接排空循环（只登记，T-006 接线时复核）、全部 `RunDetached` 位置；TASK-03 三处门外写的归属终核（5a；`CreatureRuntimeDomainService.cs:274` 没有用例走到，反例归 T-004c 判据） | 全部 | 0；`ReplayStateSnapshotTests:206` 顺延 T-004c | 探针截栈造成漏判；部分入口在测试中没有被任何用例驱动 | (A) 清单与附录 A 一一对上；结论作为 T-004c 的进入条件 |

### 3. 冲突用例分配（T-004b 的 21 条）

| 单元 | 条数 | 用例 |
|---|---:|---|
| TASK-09 | 7 | `EffectOperationReplayTests` :159、:229、:253、:269、:292、:345、:371 |
| TASK-10 | 7 | `SelectCharacterLifecycleTests` :236、:261、:288、:365；`OnlineUserServiceTests` :30、:67、:519 |
| TASK-11 | 3 | `DungeonCardPresentationCoordinatorTests` Protocol :109、:235；`IllusionTowerPublicFlowTests:149` |
| TASK-12 | 1 | `UsageQuotaTests:117` |
| TASK-13 | 2 | `GmPlayerSamplingHttpTests:14`；`AuthenticationConcurrencyTests.OverlappingPasswordReads…` |
| 顺延 T-004c | 1 | `ReplayStateSnapshotTests:206`：它经总线而不是任何入口，按目前的阅读只有翻转会让它失败 |

加法：7 ＋ 7 ＋ 3 ＋ 1 ＋ 2 ＋ 1 ＝ 21，与 `task-07-conflicts.md` §1 小计相等。TASK-08 不预分配；注册若暴露清单内用例，从上表原属单元移出。

### 4. TASK-06 §7 待办的去向

| 待办 | 单元 |
|---|---|
| 3a 附录 A 全部入口 | TASK-09～13 分族；TASK-14 终核 |
| 3b 请求级帧整体入作业 | TASK-09 |
| 3c 口令哈希移出 | TASK-13（哈希只出现在 HTTP 启动器与 GM 登录：`PasswordCredentialService` 的调用方是 `AuthenticationDomainService.RegisterAsync` / `AuthenticateAndIssueTicketAsync` 与 `GmAccessDomainService`；Game 登录只校验票据） |
| 3d 纯遥测 handler 留外 | TASK-09 |
| 3e 分离点 | TASK-08（期限关闭）、TASK-10（关连接器）、TASK-11（计时器）、TASK-14（终核与 TASK-05 两个循环的登记） |
| 3f 传输回调流边界 | TASK-09（Received）、TASK-10（Connected、Closed、关连接器） |
| 3g 直发期限 | TASK-08 |
| 3h 预热 | TASK-13 |
| 3i 停机顺序 | TASK-10 |
| 3j 启动期工作作为作业 | TASK-13 |
| 3k 注册 `AddGameplayExecution` | TASK-08 |
| 4f VC-8 提前的登记 | TASK-08 登记；T-004c 完成核对 |
| 5a TASK-03 三处门外写 | TASK-09（`SetUserAreaHandler`）、TASK-10（`AloneAsync`）；`CreatureRuntimeDomainService.cs:274` 随它的调用入口（TASK-09 的 handler 或 TASK-11 的 `CreatureClockService`）进入执行器；TASK-14 终核 |

### 5. 对 Plan 拆法的意见（交主控方裁定，本单元不改 Plan）

1. **不建议把门断言或 RQ-061 判据提前到 T-004b 的某一步。**
   - 部分迁移期间，「执行器上的作业等待一把由执行器外持有的门」和「执行器外取门」都是合法状态（§1 第 3 条）。抛出式断言在最后一个持门的生产者迁入之前一定会报，无法逐族打开。
   - 按门的种类分批打开也不可行：那 62 条门上争用的冲突用例要跟着提前处置，等于把 T-004c 拆散到 b 里。
   - 逐族验证改用 §1 第 6 条的「入口证据测试 ＋ 探针族归零」。探针只记录、不抛出，又不提交，不构成生产代码里的开关。
   - 结论：a/b/c 的拆法不变，只把 b 再切成 TASK-08～14。
2. **两处可选的合并**：
   - TASK-09 与 TASK-10 合并：可以消除「入站在执行器上、断连拆除在执行器外」的中间态（§2 TASK-09 的最后一条风险），代价是一个改动面很大的单元，证据也会混在一起；
   - TASK-14 并入 T-004c 的第一步：可以少一次结果审查，代价是 T-004c 的进入条件不再单独成立。
   本方案两处都不合并。
3. **需要裁定的口径**：RQ-013 写「调度任务不再用 `Task.Run` 并发执行」，而附录 F 把 `ScheduledTaskHostedService` 的并发槽与 `Task.Run` 列在 T-005 的「删除」栏。本方案按如下理解：TASK-11 让每次运行都成为执行器上的作业，语义上已经串行，满足 RQ-013；`Task.Run` 外壳与并发槽留到 T-005 删除，满足附录 F。请主控方确认。
4. **Plan Verification 的措辞**（plan-review-r2 R-4）：TASK-08 引入期限后，VC-8 在 T-004 就已出现。建议主控方现在把「只有 T-006 中附录 G 允许的变化可以修改」改为「附录 G 允许的变化（T-004 的 VC-8，T-006 的其余各项）」。
5. **探针工具的可得性**：TASK-07 的工具只在当前会话的 scratchpad 里。TASK-08 把它们固化进仓库（§5），后续单元的自检才不依赖会话。如果主控方不希望验证目录里出现脚本，就需要另定后续单元的自检办法。

## 主控方裁定（执行前）

- §5.1 门断言与判据不提前：接受。T-004b 各单元用「入口证据测试 ＋ 探针族归零」逐族验证。
- RQ-013 的时机口径：接受。TASK-11 起每次调度运行都是执行器上的作业；`Task.Run` 外壳与并发槽留到 T-005 删除。
- Plan Verification 措辞：已按 plan-review-r2 R-4 修改（`plan.md` 第 107 行），随本单元一起提交。这只是措辞修正，要求不变。
- 两处可选合并都不做：
  - TASK-09 中间态的 SQLite 忙等风险，在基线上本来就存在（TASK-03 实测过），不是新引入的风险；
  - TASK-14 保留为独立单元。
- 探针工具放进 `verification/probe-tools/`：接受。它是取证工具，不是产品代码。

## Result

（第 2 轮：结果审查 `reviews/task-08-result-review.md` approved 之后，按 N-1、N-4 补两处，并重跑期限测试 ×10、全量与隔离复跑。下文数字都是第 2 轮的；改动见本节末尾「第 2 轮补充」。）

### Outcome

- **注册**：`AddCommandDispatching()` 开头调用 `AddGameplayExecution()`；后者先 `AddLogging()`。只调 `AddCommandDispatching` 的容器、经 `ApplicationModule` 的组合根、经 `AddNetworkMessageDispatching` 的容器，都有同一个单例执行器与入队点。生产代码没有向它入队（Required check 6）。
- **直发期限**：新增 `Networking/Runtime/ExecutorSendDeadline.cs`，`TouchSocketNetworkMessageSender` 的两处传输写入（`SendBatchAsync`、`SendAsync`）经它等待。
  - 不在执行器上：原样返回 TouchSocket 的写入任务，行为与改动前相同。
  - 在执行器上：`send.WaitAsync(期限, ct)`，每帧计一次期限，默认 2 秒（`internal DirectSendDeadline { get; init; }`）。
  - 超时：给被放弃的写入挂只在出错时执行的观察续体；在 `SuppressFlow` 下 `Task.Run` 关闭**这次写入所用的那个会话对象**（不按 ID 重查）；记一条 Warning（消息类型、连接 ID、期限），日志调用包在 try 里，日志失败不会盖掉超时；抛 `TimeoutException`，消息含连接 ID 与期限。写入自身抛出的 `TimeoutException` 按引用区分，原样上抛。
  - 取消：照常抛 `OperationCanceledException`，同样观察被放弃的写入，不关连接。
- **停止条件未触发**：真实 socket 用例中，客户端不读、两端内核缓冲塞满后，作业在 523～527 ms（期限 500 ms）以 `TimeoutException` 结束。这说明 TouchSocket 4.3.1 的 `ISender.SendAsync` 在缓冲满时返回的是挂起的任务，`WaitAsync` 拿得到控制权。仅在 macOS 上测过。
- **注册后果**：全量中**没有**新出现的失败、挂起或排空超时。四类处置计数：(i) 0、(ii) 0、(iii) 0、(iv) 0。`task-07-conflicts.md` 不追加。
- **探针工具**已固化到 `verification/probe-tools/`，冒烟与三项还原证明通过。

### Changed

**生产代码**（`git diff --stat -- Server/src`：3 个文件 ＋39 −6，另加 1 个新文件，全部在 Owned boundary 内）

| 文件 | 改动 |
|---|---|
| `Application/Execution/GameplayQueue.cs` | `AddGameplayExecution` 加 `AddLogging()`；文档注释改为「由 `AddCommandDispatching` 调用，随容器释放而停止」 |
| `Application/Commands/CommandDispatchingExtensions.cs` | 开头调用 `services.AddGameplayExecution()` |
| `Networking/Runtime/TouchSocketNetworkMessageSender.cs` | `DirectSendDeadline` 属性；两处写入改经私有 `WriteAsync`，它把写入任务、会话对象与超时回调交给辅助；超时回调里的 Warning 包在 try 里（第 2 轮，N-4） |
| `Networking/Runtime/ExecutorSendDeadline.cs`（新增，internal） | `AwaitAsync`、`StartDetachedClose`（已处于 `SuppressFlow` 时不嵌套；失败以 Warning 记录，日志本身失败也吞掉） |

`TouchSocketNetworkConnectionCloser` 未改（按 Plan 归 T-004b3）。

**新增测试 N ＝ 9 ＋ 2 ＋ 3 ＝ 14**（第 1 轮 12 条，第 2 轮补 2 条）

- `Networking.Tests/Runtime/ExecutorSendDeadlineTests.cs`（9 条，确定性；「在执行器上」一律取自真实 `GameplayExecutor` 作业）：
  1. a `On_the_executor_a_send_that_never_completes_times_out_and_closes_once`
  2. b `Off_the_executor_the_send_is_awaited_without_a_deadline`
  3. c `On_the_executor_a_send_that_completes_in_time_passes`
  4. d `On_the_executor_cancellation_is_a_cancellation_and_does_not_close`
  5. e `An_abandoned_send_that_fails_later_leaves_no_unobserved_exception`：订阅 `UnobservedTaskException`，按唯一消息过滤，强制 GC 三轮，结果稳定。临时注释掉两处观察续体时该用例变红，已还原。
  6. f `A_detached_close_started_in_a_job_runs_off_the_executor_while_the_job_still_runs`
  7. `A_failing_detached_close_is_logged_as_a_warning`：计划外补的 1 条。§2 写了「捕获并以 Warning 记录失败」，a～f 没有覆盖它。
  8. `On_the_executor_the_sends_own_timeout_is_passed_through_and_does_not_close`（第 2 轮，N-1）：写入在期限内以自己的 `TimeoutException` 失败，抛出的是同一个对象，`onTimeout` 未调用。
  9. `A_failing_timeout_log_neither_hides_the_timeout_nor_stops_the_close`（第 2 轮，N-4）：真实的 `TouchSocketNetworkMessageSender` 配一个 `Log` 必抛的 logger、一个写入永不完成并记录关闭的会话替身；在执行器上发送，断言抛出的是 `TimeoutException`，且关闭已启动。
- `Networking.Tests/Runtime/DirectSendDeadlineRealSocketTests.cs`（2 条）：
  10. `A_job_sending_to_a_client_that_stops_reading_times_out_closes_it_and_frees_the_executor`（R-b 那条真实 socket 用例）
  11. `The_default_direct_send_deadline_is_two_seconds`
- `Application.Tests/Execution/GameplayExecutionRegistrationTests.cs`（3 条）：
  12. `Command_dispatching_alone_registers_one_executor_behind_the_queue`：不加日志的容器；`GameplayShardSelector.Select` 对 World 与另一个键都返回同一实例。
  13. `A_wrapped_region_given_the_container_runs_on_the_container_executor`：运行后 `IsStarted` 为真。
  14. `Disposing_the_container_ends_the_executor_loop`：释放后 `Completion` 已完成。

先写测试再改生产代码：注册的 3 条在改代码前全部因「No service for type GameplayExecutor」失败；期限的 7 条先以编译失败为红。

**真实 socket 用例的搭法**：

- 服务端用生产的 `GameTcpService`，配置与生产的 Game 监听一致：`GameClientFrameDataHandlingAdapter`，默认发送管道阈值，不加 `ConfigureSendPipe`。
- 编码器每帧 `DnfFrameProtocol.MaximumFrameLength`（1 MiB），一批 64 帧，共 64 MiB；客户端 `ReceiveBufferSize = 4096` 且不读；期限 500 ms。
- macOS 上客户端在 FIN 前只收到约 0.64～0.68 MB，64 MiB 远超两端缓冲，不需要按平台调整。
- 断言：
  - 作业在 [期限, 期限 ＋ 3 s] 内以 `TimeoutException` 结束，且作业任务确已完成；
  - Warning 含连接 ID；
  - 客户端读到 FIN 或重置，读到的字节少于整批；
  - 紧接着入队的作业 1 秒内完成（实测 0 ms）。
- 反证：临时让辅助永远走「不在执行器上」分支时，该用例在 20 秒耐心处失败（「the job is still waiting on the stalled send」），已还原。

**探针工具**（`verification/probe-tools/`，14 个文件）：要求的 12 个，另加 `run.sh` 依赖的 `plan.py` 与它导入的 `wrapat.py`。改动只限路径：

- 仓库根由脚本所在目录上溯 5 级推导（`patch.py`、`snapshot.py`、`plan.py`、`run.sh`）；
- 备份与各轮输出放在环境变量 `PROBE_WORK` 指定的目录，未设置即退出，避免写进仓库；`emit.py` 的输出文件名前缀取 `PROBE_LOG_PREFIX`（默认 `probe`）；
- 挂点集合、`classify.py` 的入口帧名单原样保留。`patch.py` 不挂 `TouchSocketNetworkMessageSender.cs`，所以不需要改匹配模式；
- 不含原始转储，也不含 `adiff.py`、`lostcaller.py`、`q.py`、`runexcl.sh` 这几个一次性调查脚本。

**既有测试文件：0 个改动**；没有删除或改写任何冲突用例（D ＝ 0）。

### Why（本地决定）

- **辅助签名多了 `NetworkConnectionId`**：`TimeoutException` 的消息要含连接 ID，辅助本身不认识连接。
- **超时回调里先启动关闭、再记日志**：日志提供程序抛异常时，关闭仍已启动。
- **用 `ReferenceEquals(error, send.Exception?.InnerException)` 区分超时来源**：写入自身失败时，`WaitAsync` 原样传出同一个异常对象。借此不把写入自己的 `TimeoutException` 误当成期限到点，也不受「超时后写入恰好完成」的竞争影响。
- **与 `NotificationBus` 的关系**：
  - 通知总线自己有每收件人 2 秒期限，并把令牌传进发送器；
  - 两者同时生效时，谁先触发只决定那条 Warning 属于「到期限」还是「投递抛异常」（期限辅助抛的 `TimeoutException` 不是 `OperationCanceledException`，总线按投递失败记），收件人一律判 Failed；
  - 断言日志分类的 K5 用例（T-006）不经执行器，不受影响，本单元全量中照常通过。

### §4 主动复核（全绿，逐项写结论）

注册只改变一件事：带容器的包裹区域改用容器执行器。下表写明各项现在为什么安全，以及之后必须再看的单元。

| 区域 | 现在为什么安全 | 之后谁再看 |
|---|---|---|
| `DomainEventDispatchTests.A_slow_live_group…` | worker（`DomainEventDispatcher.RunAsync`）在执行器外，被卡住的 live 消费不在执行器上；测试的两次 `f.SendAsync` 各是容器执行器上的一个短作业，前后不重叠 | **TASK-12**：live 消费入队后，被卡住的作业会占住执行器，第二次 `f.SendAsync` 排在它后面，必须改写 |
| `WeaknessGoldRecoveryTests` 两条（`Concurrent_contract…`、`Concurrent_requests_charge_only_once`）、`ItemSaleTests.Two_concurrent…` | 两个 `Recover`／`Sell` 区域传同一个 `shop.Services`，现在串行。断言是「一次成功、一次拒绝」与最终金额；串行时先入队的先执行，结果确定，照常通过 | T-004c：这些区域直接调 handler，handler 内部经总线；翻转后作业内调总线会报自我等待（TASK-07 T5 的既有结论） |
| `MailboxCoreIntegrationTests.ConcurrentClaim…`、`QuestHelperPuppetTests`（:181、:273）、`SaoTowerStoreTests.Two_database_sessions…` | 两个发起方用的是**两个不同的夹具容器**，各自一个执行器，与注册前相同 | T-004c／T-005：跨容器的 SQLite 竞争不经门，翻转不改变它；是否保留，随门删除时决定 |
| `DungeonVisitDomainServiceTests.Concurrent_first_visits` | 两个包裹都**不传容器**，各自新建执行器，不受注册影响。工厂里的 TCS 要求两次调用同时在途 | 任何单元都不能给它补传容器：同一执行器上两次调用无法同时在途，会挂到取消 |
| `PartySettingsDomainTests.ConcurrentUpdates…` | 不传容器，不受影响 | 同上。而且它用同步 `Barrier(3)`，若落到同一执行器会**同步阻塞**执行器，不只是超时 |
| 使用 `ShopFixture` 的冲突用例（5 个文件：`OnlineExpiredItemCleanupTests`、`EffectOperationReplayTests`、`ReplayStateSnapshotTests`、`CommonExperienceEffectTests`、`IllusionTowerPublicFlowTests`） | 被卡住的一方都在执行器外（总线、dispatcher 尚未迁入），`ShopFixture` 的作业都是短作业，不等被卡住的一方。两轮全量都通过，无挂起 | 各自原属单元：EffectOperationReplay → TASK-09，IllusionTowerPublicFlow:149 → TASK-11，ReplayStateSnapshot:206 → T-004c，另两条在 T-004c 清单 |
| Protocol 5 个文件（LeaveDungeon、PartySelectDungeon、MonsterDeath、QuestClearVertical、Joust） | 带容器的包裹每个用例至多一个区域在途（LeaveDungeon:559、PartySelectDungeon:271、MonsterDeath:1017、QuestClearVertical:111 与 :168 顺序执行、JoustQueryHandler:147；JoustProtocol 没有带容器的包裹）；区域内没有生产入口入队。LeaveDungeon 的交错用例 `Exit_owns_the_transition…` 不经 `TestGameplay` | TASK-09（dispatcher）与 T-004c（总线）：区域内若经真实 dispatcher 或总线，迁入后会报自我等待，按 (iii) 移出 |
| `AuthenticationFixture.Consume` 与 `AuthenticationConsumptionTests.ExactCredentialBinds…` | `Consume` 现在落在容器执行器上；外层 `first` 事务在执行器外、不是作业，竞争方是唯一的作业，不会互等 | T-004c：外层在作业外开写事务，判据开启后会被拒 |
| Networking 真实 socket 用例中带 `host.Services` 的同步短作业（GameLogin 6 处、IllusionTowerStage 4、CardRewardReconnect 4、BossTower 3） | 在测试线程上同步等一个只读快照作业，执行器上没有别的作业 | TASK-09：入站 CMD 入队后，这些短作业会排在入站 CMD 之后，只是延迟；测试线程不在执行器上，不成环 |

**两种额外形状**：

- 「在容器执行器的作业里释放这个容器」：全量没有出现自我等待。
- 「释放时执行器上仍有被卡住的作业」：
  - 容器里的日志大多没有提供程序，「did not drain within」这条 Warning 不一定落进控制台（两轮全量计数都是 0），所以另按耗时核对：排空超时至少让用例多花 10 秒；
  - 与 TASK-07 全量逐条对比，正式轮多出 ≥ 9 秒的只有 3 条：`SealedTreasureTests.Admission_…`（6 → 21 s）、`SouthernDaleTests.Admission_…`（4 → 14 s）、`PvfHellRewardDefinitionSourceTests(level: 85)`（5 → 16 s）；
  - 前两条在 TASK-06 全量（注册之前）就是 19 s、12 s，在第一轮全量（r1，同一份代码）是 5 s、3 s，属于负载波动，不是排空等待。第三条读真实 PVF，与执行器无关。

### Checks

1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-08-build-test.txt` 开头）。
2. **全量**（`logs/task-08-build-test.txt`，带 `--blame-hang-timeout 5m`）：
   - 总数 50 ＋ 1694 ＋ 80 ＋ 475 ＋ 969 ＋ **149** ＋ **4431** ＋ 165 ＝ 8013 ＝ 7999 ＋ 14 － 0（＝ 第 1 轮 8011 ＋ 2）；
   - 与 TASK-07 逐项对照：Networking 138 → 149（＋11 ＝ 9 ＋ 2），Application 4428 → 4431（＋3），其余 6 项相等；
   - 通过 50 ＋ 1694 ＋ 80 ＋ 475 ＋ 967 ＋ 147 ＋ 4413 ＋ 165 ＝ 7991 ＝ 7977 ＋ 14；
   - 失败 2，只有那 2 条 `DimensionDungeonTests`（`Fusion_real_content…`、`All_six_real_cells…`）；
   - 跳过 2 ＋ 2 ＋ 16 ＝ 20；7991 ＋ 2 ＋ 20 ＝ 8013；
   - 表征测试（`Networking.Tests.Characterization`）8 条通过、2 条跳过；
   - 挂起转储 0 个（结果目录下没有 `.dmp`／`Sequence_*`）；
   - 第 1 轮的正式全量（8011 条）与更早的 r1（scratchpad `t08/full-r1.txt`）计数逐项相同；本文件已被第 2 轮覆盖。
3. **隔离复跑**（`logs/task-08-isolated.txt`，第 2 轮）：Application 4431 条，4413 通过、2 失败（同上）、16 跳过；Networking 149 条，147 通过、2 跳过；Protocol 1694 条全部通过。
4. **期限测试**（`logs/task-08-deadline-x10.txt`，第 2 轮）：
   - 期限相关的 11 条（`ExecutorSendDeadlineTests` 9 ＋ `DirectSendDeadlineRealSocketTests` 2）隔离连续 10 次，每次 11 条全部通过；
   - 其中真实 socket 用例：作业结束于 519～524 ms，客户端每次读到 678,428 字节后 FIN；
   - 全量负载下 11 条各 1 次全部通过；真实 socket 用例 1207 ms 结束（在 [期限, 期限 ＋ 3 s] 内），653,852 字节后 FIN，下一个作业 2 ms 执行；
   - 第 1 轮另有真实 socket 用例单独隔离 10 次（523～527 ms）与全量负载 1 次（578 ms），都通过。
5. **测试改动核对**：
   - 新增 14 条，见 Changed，N ＝ 9 ＋ 2 ＋ 3 ＝ 14；
   - §4 处置过的既有测试文件 0 个，所以没有需要差异核对脚本证明的「只改包裹方式」的文件；`git status` 中 `Server/tests` 下只有 3 个新文件，没有 `M`；
   - 删除或改写的冲突用例 0 条（D ＝ 0）。
6. **生产改动的边界**：
   - `git diff --stat -- Server/src`：只有上表 3 个文件；未跟踪只有 `Networking/Runtime/ExecutorSendDeadline.cs`；
   - `grep -rn "IGameplayQueue\|GameplayExecutor\b" Server/src`（排除 bin/obj）：命中只在 `Abstractions/Execution/GameplayQueue.cs`、`Application/Execution/GameplayExecutor.cs`、`Application/Execution/GameplayQueue.cs`；`CommandDispatchingExtensions.cs` 只调用 `AddGameplayExecution()`，不出现这两个名字；没有入口入队；
   - `grep -rn "RunDetached" Server/src`：只有 `GameplayJob.cs` 的 4 个定义与 `GameplayJobSlot.cs` 的 1 处文档注释 `cref`，零处使用。
7. **探针工具冒烟**（`logs/task-08-probe-smoke.txt`，从 `verification/probe-tools/` 运行，`PROBE_WORK` 指向 scratchpad）：
   - `apply`：P1b 5 处、P2 173 处；`dotnet build Server/86JP.Server.slnx -c Release` 0 警告 0 错误；`revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；
   - 还原证明一：改动前后两份快照的 sha1 都是 `d7f8949b935ba388f10908c3b3a3fbce2cbc3c57`，`cmp` 逐字节相等。快照内 1576 条索引项中 1573 条与工作区相等，差的 3 条就是本单元改的 3 个文件；未跟踪 1 个，即新文件。探针不触及这 4 个文件；
   - 还原证明二、三：`git diff --stat -- Server/src` 与 `git status --short -- Server/src` 只剩本单元的 3 个修改与 1 个新文件；
   - 备份目录已删除。冒烟之后，正式轮重新做了 `--no-incremental` 构建。

### Remaining

- **审查 N-2：容器释放时的 10 秒排空，在测试里看不到**。测试容器的日志大多没有提供程序，「did not drain within」不会出现在日志里；本单元只能按耗时间接核对。从 TASK-09 起生产入口开始入队，夹具释放时执行器上可能真有被卡住的作业，表现为用例慢 10 秒而不是失败。每个后续单元的全量都要按耗时（与上一单元逐条对比，多出 ≥ 9 秒的）复核，必要时给测试容器加日志提供程序。
- **审查 N-3：跨平台证据**。「写入在缓冲满时真异步」以 PR CI 在 ubuntu 与 windows 上通过 `DirectSendDeadlineRealSocketTests` 为证据。若在任一平台失败，按本单元的停止条件处理：停下回报主控方，不另寻实现。

- **按帧计期限的残余**：一个持续慢读、但每帧都在 2 秒内放手的客户端，在一个批次内可能累计让执行器等待超过 2 秒（选角批次上界约 454 KB，TASK-05 N-7）。T-006 把发送移出执行器后消失。
- **VC-8 提前的登记（4f）**：从第一个会发包的入口迁入执行器（TASK-09）起，执行器上的直发遇到慢客户端会在 2 秒后断开它。本单元结束时生产路径上没有作业，所以还没有可见影响。T-004c 完成核对。
- **只在 macOS 上证明了「写入真异步」**：Linux 与 Windows 上没有本地测过，见上面 N-3。
- **分离点清单（3e）的第一项**：`ExecutorSendDeadline.StartDetachedClose`，由测试 f 证明不继承作业令牌。`TouchSocketNetworkConnectionCloser` 的分离关闭仍不在 `SuppressFlow` 下，归 TASK-10。
- **§4 复核表「之后谁再看」一列的汇总**：TASK-09（Protocol 5 个文件、Networking 同步短作业）、TASK-12（`A_slow_live_group…`）、T-004c（Weakness／ItemSale 的 handler 直调、`ExactCredentialBinds…`、跨容器竞争）；`Concurrent_first_visits` 与 `PartySettings…ConcurrentUpdates…` 永远不能补传容器。
- **探针工具的用法**：`PROBE_WORK=<仓库外目录> python3 patch.py apply` → 构建 → `PROBE_WORK=… bash runall.sh <round> [项目…]` → `PROBE_WORK=… python3 patch.py revert`；快照用 `snapshot.py`／`snapcheck.py`；`emit.py` 的输出前缀用 `PROBE_LOG_PREFIX` 指定。
- **未提交**：本单元没有 commit，也没有 stash。

### 第 2 轮补充（结果审查 N-1、N-4）

- **N-1**：新增测试 8（见 Changed）。变异验证：把 `ExecutorSendDeadline` 的过滤条件改成 `when (true || …)` 后，该用例以「Assert.Same() Failure」变红（9 条中 1 条失败），已恢复，`true ||` 计数 0。
- **N-4**：`TouchSocketNetworkMessageSender` 超时回调中的 `LogWarning` 包进 try；关闭在日志之前启动，所以日志失败时关闭仍已启动，`TimeoutException` 照常抛出。新增测试 9。变异验证：去掉这个 try 后，该用例以「Assert.IsType() Failure」变红（抛出的是日志的 `InvalidOperationException`），已恢复。
- 两次变异都用 scratchpad 备份逐字节恢复，恢复后重新构建并跑了第 2 轮全部检查。
- 生产改动仍只在 Owned boundary 内；`IGameplayQueue|GameplayExecutor\b` 与 `RunDetached` 的检索结论不变。第 2 轮没有改 `Server/src` 以外的探针工具，冒烟（Required check 7）沿用第 1 轮的结果：第 2 轮的改动不在 `patch.py` 的挂点文件里。

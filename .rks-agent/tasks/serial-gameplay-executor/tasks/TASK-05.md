# TASK-05 — 发件箱与发送端组件（不接线）

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付发件箱和发送端两个独立组件，配单元测试，并配经真实 TouchSocket 的组件级测试。这一步**不接线**：今天的 `TouchSocketNetworkMessageSender`、`TouchSocketNetworkConnectionCloser`、`NotificationBus` 与它们的调用方都不动。对应 Plan T-003；覆盖 RQ-030～RQ-037 的机制部分，以及 AC-011（单元部分）、AC-012（机制与 XO 的发送端口径）、AC-013。
- Owned boundary:
  - `Server/src/Dfo.Server.Abstractions/Execution/`：增加发件箱条目与发送端交接用的接口和值类型。具体命名在 Result 中列出。
  - `Server/src/Dfo.Server.Application/Execution/Outbox/`（新建）：发件箱实现。
  - `Server/src/Dfo.Server.Networking/Outbound/`（新建）：每连接发送队列、有序条目协调、交付与关连接、上限判据所用的常量。
  - 三个测试目录各新建一个对应目录：`Application.Tests/Execution/Outbox/`、`Networking.Tests/Outbound/`，以及组件级真实 socket 测试（放在 `Networking.Tests/Outbound/`）。
  - 允许为测试新增 `InternalsVisibleTo`。不改其他任何生产代码和既有测试。
- Required checks:
  1. 全解决方案 Release 构建 0 警告 0 错误。
  2. 新测试隔离运行连续 20 次全绿；真实 socket 的测试另外在负载循环下跑 20 次（负载方式同 TASK-01：Application.Tests 与 Infrastructure.Tests 循环并行）。
  3. 全量测试：总数等于 7941 加上新增数，失败只有那 2 条 `DimensionDungeonTests`，表征测试 8 条通过、2 条跳过。日志落盘为 `verification/logs/task-05-build-test.txt`。
  4. 不接线：在 `Server/src` 中检索新类型，引用只出现在新目录及其 Abstractions 契约中。
  5. 发送队列上限判据：上限常量不小于「最大单 CMD 单连接出站」的 2 倍。这个最大值按下面 §4 的推算得出，判据测试同时钉住推算公式的输入。
- Dependencies: TASK-01（根因：`SendAsync` 返回时帧还在 TouchSocket 的发送管道里；关连接与异常拆除都能越过管道。XO 的到达在线上不确定，顺序按「交付」断言）；TASK-02（XO-1～XO-7、DR-10 与 XO-5 互引）；TASK-03（实测最大单连接出站 39467 B，这只是新建角色的值，上限要按容量推算）；TASK-04（作业令牌、`AssertNotOnExecutor`、`RunDetached`）。
- Result review: required — T-006 会把全部投递接到这两个组件上。发件箱的刷出规则和组装时机、发送端的顺序、身份校验、失败隔离、关连接时机，一旦有错，到 T-006 就会表现为丢消息、乱序、该断的不断或者误断，而现有的表征测试只能抓到其中一部分。

## Detailed Plan

### 1. 契约（Abstractions/Execution）

- **出站条目**：一个不可变值，包含：
  - 目标连接上下文（`NetworkConnectionContext`，用于 RQ-035 的身份校验）；
  - 一批已组装好的类型化消息（`IServerNetworkMessage` 或现有出站消息接口），沿用今天 `SendBatchAsync` 的整批语义；
  - 必达类别 Required 或 BestEffort（RQ-034）；
  - 可选的有序组标识与组内序号（RQ-033）；
  - 是否为关连接请求。
- **发送端接口**：一个方法，接收发件箱刷出的条目序列，同步入队后立即返回，**不等待交付**（RQ-037）。另提供一个可观测的交付结果回调或事件，供测试和 T-006 的附录 E 使用。
- 编码留在发送端（附录 B），所以发件箱交出的是类型化消息，不是字节。

### 2. 发件箱（Application/Execution/Outbox）

- **归属**：以执行器作业为单位，随作业令牌挂接（Plan「发件箱的归属」），不按 DI scope 注册。
  - 在作业外登记：本 Task 先抛出程序错误，用 `AssertNotOnExecutor` 的反义检查实现。RQ-061 的总开关在 T-004 统一处理。
- **工作单元**：提供工作单元的开始、提交、回滚。条目登记时记下所属的工作单元，单元之外登记的不属于任何单元。
  - 组装点（RQ-031(0)）：登记时可以是「已组装」的条目，也可以是「待组装」的条目，即一个在提交时调用的组装委托，按收件人逐个组装。
  - 在某个工作单元内登记的「待组装」条目，在该单元提交的那一刻组装，然后才把控制权交还调用方。
  - 组装失败按收件人隔离（RQ-031(d)）：只丢弃该收件人的这批条目，记 `Warning`，并把逐收件人的组装结果交给调用方，供 T-006 的祭坛等用例使用。
  - 单元之外登记的「待组装」条目，在登记时立即组装。
  - 与 `DomainTransaction` 的挂钩在 T-006 完成。本 Task 只提供工作单元的 API，测试中手动驱动。
- **刷出（RQ-031 (a)(b)(c)）**：
  - CMD 正常完成：按登记顺序交给发送端。
  - 回滚或结果不确定的单元内登记的条目：丢弃。
  - CMD 以异常结束：已提交单元内的条目和单元外的条目照常刷出；关连接请求一律执行；最后追加「关闭来源连接」（若有）。
  - 刷出动作提供给执行器作业的结束点调用。本 Task 在测试里模拟作业结束，接入执行器留给 T-004 或 T-006 决定，在 Result 中注明。
- 关连接请求视为不属于任何单元（Plan 中 R-1 的澄清）。

### 3. 发送端（Networking/Outbound）

- **每连接发送队列**：
  - 按条目到达的顺序交付（RQ-032）；
  - 每个连接一个异步排空循环，循环不在执行器上，用 `RunDetached` 或在执行器外启动，以保证这一点；
  - 字节上限按编码后的字节数计（RQ-036），超限即关闭该连接并丢弃余下条目。
- **整批编码与身份校验（RQ-034、RQ-035）**：交付前先把整批编码；编码失败的处理沿用今天 `SendBatchAsync` 的语义。交付前比对连接上下文，连接已被替换或已关闭时丢弃，不交给新连接。
- **Required 与 BestEffort（RQ-034）**：
  - 一个多目标批次里，Required 目标失败时，该目标中止，后续怎么办沿用 `GameExplicitMessageDelivery` 今天的语义；
  - BestEffort 按目标隔离；
  - `CleanupNotificationDelivery`「必达失败即不再继续」的语义，要在条目层面能够表达。
- **有序条目（RQ-033，XO-1～XO-7）**：
  - 同一有序组里，序号靠后的条目，要等前面的条目「已交付或已判定失败」才交付；
  - 等待上限 2 秒，超时后判定前序目标连接失败，关闭它，后续照常交付；
  - 「交付」的口径是交给传输层并且已经刷出（见下条），不是线上到达。
- **交付与关连接的时机（TASK-01 根因）**：
  - 对关连接请求，以及失败后要做的关闭，必须先确认此前登记的帧已经离开 TouchSocket 的发送管道，再关闭。执行者要查清 TouchSocket 4.3.1 的发送管道 API，例如 `TcpTransport.Writer` 的刷出，或者有没有「发送完成」信号，选出可行的做法，并在 Result 中写明依据。
  - 每次交付有 2 秒期限（RQ-036），超时视为失败，关闭该连接。
  - 关连接本身在执行器外完成，执行器从不等待它（RQ-008）。
- **队列上限常量与判据**：见 §4。

### 4. 上限推算（Required check 5）

- 先从 PVF 或代码中的容器定义，查出选角全序列里随数据增长的部分各自的容量上界：背包各页、装扮栏、仓库（如果在选角时下发）、任务、宠物、技能、称号簿、邮件提示等，以及每一项的单条字节数。例如 ITEM_LIST 是 5 B ＋ 条目数 × 101 B。
- 算出最大选角出站字节，与 TASK-03 实测的 39467 B 对照，解释两者的差距。
- 上限常量取 ≥ 2 × 推算最大值，并取整到一个合理的值。判据测试钉住这个不等式，配一条反例，同时在注释或常量旁给出推算的出处。
- 如果有容量上界查不清，按保守值估计，并在 Result 中列出来。

### 5. 测试

- **发件箱（AC-011 单元部分）**：
  - 两个工作单元各登记一条，第二个回滚，只刷出第一条；
  - 已提交单元内、单元外各登记一条，然后抛出异常：按序刷出，最后关闭来源连接；回滚单元内的条目不刷出；
  - 某个收件人组装失败：只丢弃他的条目，记 `Warning`，不关闭他的连接，其他人照常收到，调用方能看到逐收件人结果；
  - 工作单元提交之后，组装读到的是已提交时的状态，看不到后续阶段的改动。可以用一个可变的假状态加组装委托来演示；
  - 在作业外登记抛出程序错误。
- **发送端（AC-012、AC-013）**：
  - 单连接顺序；
  - 有序组：前序目标人为变慢但不超过 2 秒，后续仍然等它；超过 2 秒，它被关闭，后续照常交付；
  - Required 失败中止该目标，BestEffort 按目标隔离；
  - 整批编码失败；
  - 连接被替换时丢弃；
  - 交付超时、队列超限时关闭该连接；
  - 一个卡住的连接不影响其他连接的交付，也不阻塞入队方，入队方不会等待；
  - 关连接排在已登记的帧之后。
- **真实 TouchSocket 组件测试**：
  - 起一个最小的 TouchSocket 服务端，接上新发送端；
  - 「发两帧，然后关闭」，在负载循环下 20 次，客户端每次都收到两帧，然后才收到 FIN。这是 TASK-01 根因的直接回归，也是 T-006 启用 b1-strict / b2-strict 的前提；
  - 一个不读包的客户端，在交付超时或队列超限时被关闭，同时另一个连接照常收到。
- **上限判据**及其反例。

### 6. 执行顺序

契约 → 发件箱，先写测试再实现 → 发送端的单连接部分 → 有序组 → 批次与身份 → 交付与关连接的时机（先查 TouchSocket API）→ 真实 socket 测试 → 上限推算与判据 → 20 次与负载循环 → 全量 → 不接线的 `grep` 证明 → 写 Result。

## Result

（第 2 轮。第 1 轮审查 `reviews/task-05-result-review.md` 为 changes_required，本轮按主控方处置修正 B-1 与 N-5，并按 N-1～N-7 改写和补充 Remaining；各轮差异见本节末尾「各轮变化」。）

### Outcome

发件箱与发送端两个组件已交付，未接线。新测试共 45 条：发件箱 14 条，发送端 31 条，其中 7 条经真实 TouchSocket。Required checks 1～5 全部满足（见 Checks）。

今天的 `TouchSocketNetworkMessageSender`、`TouchSocketNetworkConnectionCloser`、`NotificationBus` 及其调用方都没有改。没有改动任何已跟踪文件，也没有新增 `InternalsVisibleTo`：Networking 本来就对 Networking.Tests 开放。

### Changed

**`Server/src/Dfo.Server.Abstractions/Execution/`，新增两个文件：**

- `GameplayOutbound.cs`
  - `OutboundEntry`：不可变值，由 `Batch(connection, messages, delivery, order?)` 或 `Close(connection, order?)` 构造。它携带登记时的 `NetworkConnectionContext`、整批类型化消息、`OutboundDelivery`、可选的 `OutboundOrder`，以及 `IsClose`。
  - `OutboundDelivery`：Required 或 BestEffort。两者只在有序组内有区别：组内某个 Required 条目没有以 Delivered 或 Closed 结束时，组内其后的条目一律 Skipped；BestEffort 条目失败不打断组。
  - `OutboundOrderGroup`（按引用比较）与 `OutboundOrder(Group, Sequence)`：同组的序号必须严格递增地提交。XML doc 写明约束：一条 CMD 新建一个组，不得复用或长期持有（N-6）。
  - `IOutboundSender.Submit(entries)`：同步入队后立即返回，不等待交付（RQ-037）。
  - 可观测的交付结果：
    - `OutboundOutcome`，取值为 Delivered、Closed、Stale、Discarded、Skipped、EncodingFailed、Failed、TimedOut、OverLimit；
    - `OutboundDeliveryReport`；
    - `IOutboundDeliveryObserver`，包含 `OnEntryCompleted` 与 `OnConnectionFailed`，在发送端自己的循环里回调，不在执行器上。
- `GameplayJobSlot.cs`
  - `GameplayJobSlot<T>`：按作业令牌挂接的每作业状态，内部是 `ConditionalWeakTable<GameplayJobToken, T>`。
  - 在作业外、作业已结束或 `RunDetached` 内读取，都抛 `GameplayOffExecutorException`，即 `AssertNotOnExecutor` 的反义检查。
  - 放在 Abstractions 是因为只有这里能拿到令牌。TASK-04 的 `GameplayJob.cs` 没有改。

**`Server/src/Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs`（新建）：**

- `GameplayOutboxes`，接线后作为单例：
  - `Current`：取当前作业的发件箱。
  - `RunAsync(source, body, ct)`：**作业结束点**。在作业内运行 body，结束后把发件箱刷给 `IOutboundSender`。body 抛异常时，按 RQ-031(c) 刷出，最后追加 `Close(source)`，然后原样抛出。
  - 发送端拒收发件箱时（`Submit` 抛异常，属程序错误，N-5）：记 `Error`，把其中的关连接请求去掉有序组后单独再提交一次。CMD 本已失败时，`Close(source)` 已在其中，原异常照常抛出；CMD 正常结束时，补上 `Close(source)`，并以拒收的异常让 CMD 失败（按 DR-15「异常即断线」处理）。
- `GameplayOutbox`：
  - 入口有 `Register`、`RequestClose`、`ComposeAsync<TRecipient>(recipients, compose, ct)`、`BeginUnit`。
  - 关连接请求永远不属于任何单元（R-1）。
  - 单元内登记的待组装条目，在 `CommitAsync` 时逐收件人组装，组装完才返回调用方；单元外登记的，在登记时组装。
  - 某个收件人组装失败时，只丢弃他的条目，记 `Warning`，不关连接。逐收件人结果在 `OutboxComposition<T>.Results`（`OutboxRecipientResult<T>`）中可见。
- `GameplayOutboxUnit`：
  - 提供 `CommitAsync`、`Rollback`、`MarkUncertain`、`Dispose`；Dispose 时尚未结束的单元视为回滚。
  - 单元按顺序执行，不能嵌套。
  - 刷出时只保留单元外的条目和已提交单元的条目。作业结束时仍开着的单元视为不确定；如果作业是正常结束的，另记一条 `Warning`。

**`Server/src/Dfo.Server.Networking/Outbound/`（新建）：**

- `OutboundSender`（internal）：
  - **结构**：
    - `Submit` 在锁内校验空条目和组序号，然后写入唯一的准入队列，立即返回。
    - 准入循环逐条编码，挂接有序组的前驱，再按 `NetworkConnectionContext` 的**引用**放进该连接的队列（`ConditionalWeakTable`），并按编码后的字节数计入上限。
    - 每个连接各有一个排空循环。
    - 所有循环都在 `ExecutionContext.SuppressFlow` 下启动，不带作业令牌。
  - **单连接顺序**（RQ-032）：每个连接 FIFO。关连接条目排在此前登记的帧之后执行，它之后的条目 Discarded。
  - **有序组**（RQ-033）：
    - 一个条目沿组内的前序链，从最早的未落定条目开始逐个等待：每个未落定的条目都从它前一个落定之时起有 `OrderedWaitLimit`（2 s）。超时则判该条目 TimedOut 并关闭它的连接，然后继续等下一个（B-1：上限施加到最早的未落定前序，而不只是紧邻的前驱；中间条目可能根本没等过就已结束，比如入队即 Discarded、OverLimit，或随队列 `End` 被丢弃）。
    - 全部前序落定后，本条目照常继续；若因 Required 失败断组，则本条目 Skipped。关连接条目即使断组也照样执行。
    - 条目落定后释放其已编码字节（只留长度用于计数），组对象若被长期持有，也只挂住小对象。
    - 无环的论证写在类注释里：等待只指向更早的提交，队列按提交顺序 FIFO，`Submit` 串行。
  - **编码失败**（RQ-034）：整批一帧不发。轮到它时判 EncodingFailed 并关闭连接；排在它之前的条目照常交付。
  - **身份**（RQ-035）：交付前先 `Resolve(context)`。连接已被替换（引用不同）或已下线时判 Stale，不交付，也不关闭新连接；该队列其余条目同样判 Stale。
  - **失败策略**（RQ-036）：
    - 三种情形：交付抛异常判 Failed；超过 `DeliveryTimeout`（2 s）判 TimedOut，取消令牌与 `WaitAsync` 双重限时；准入时超出 `QueueByteLimit` 判 OverLimit。
    - 三种情形都关闭该连接，丢弃余下条目，并回调 `OnConnectionFailed`。
    - 关闭在分离任务中进行，按精确身份解析连接，从不关闭替换后的连接。
- `OutboundTransport.cs`：
  - internal 接缝 `IOutboundTransport`、`IOutboundConnection`。
  - `TouchSocketOutboundTransport`：按 `TouchSocketConnectionRegistry` 中的精确身份解析连接。
  - `OutboundTransportOptions`（public）：`CreateSendPipeOptions()`、`ConfigureSendPipe(TouchSocketConfig)`、`ConfirmsFlush(...)`。连接的发送管道没有按此配置时，交付直接判 Failed。这是程序错误，不静默降级。
- `OutboundEncoder.cs`：`IOutboundEncoder` 与 `RegistryOutboundEncoder`。
  - 校验与 `SendBatchAsync` 相同：编码器存在、端点匹配、帧非空且不超过 `MaximumFrameLength`。
  - 先校验整批，再整批编码。
  - Debug 帧日志的格式与今天相同。
- `OutboundLimits.cs`：
  - 常量 `SelectionOutboundBound = 454_235`、`QueueByteLimit = 1 MiB`、`DeliveryTimeout = 2 s`、`OrderedWaitLimit = 2 s`；
  - `OutboundSendOptions`：测试可调的选项。

**测试（新建）：**

| 文件 | 条数 | 内容 |
|---|---:|---|
| `Application.Tests/Execution/Outbox/GameplayOutboxTests.cs` | 14 | AC-011 的单元部分，全部在真实 `GameplayExecutor` 作业内运行（逐条见下） |
| `Networking.Tests/Outbound/OutboundTestSupport.cs` | 0 | 假传输（可阻塞、可失败、记录事件）、文本编码器、记录型观察者 |
| `Networking.Tests/Outbound/OutboundSenderTests.cs` | 19 | 发送端单元测试（逐条见下） |
| `Networking.Tests/Outbound/OutboundQueueLimitTests.cs` | 5 | 上限判据（见 Why） |
| `Networking.Tests/Outbound/OutboundRealSocketTests.cs` | 7 | 真实 `GameTcpService` ＋ `TouchSocketConnectionRegistry` ＋ 真实编码器注册表（逐条见下） |

加法：14 ＋ 0 ＋ 19 ＋ 5 ＋ 7 ＝ 45。第 2 轮相对第 1 轮：发件箱 ＋2，发送端 ＋3（一个 Theory 的三组数据），真实 socket ＋1，合计 39 ＋ 6 ＝ 45。

`GameplayOutboxTests` 覆盖：
- 两个单元，第二个回滚，只刷出第一条。
- 依次登记已提交单元、单元外、回滚单元的条目和一个关连接请求，然后抛异常：按序刷出，最后关闭来源连接。
- 正常结束时不关闭来源连接。
- 某个收件人组装失败：只丢弃他的条目，记 Warning，不关连接，逐收件人结果可见。
- 组装读到提交时的状态，看不到后续阶段的改动。
- 在单元外登记时立即组装。
- 不确定的单元和未结束的单元，条目都被丢弃。
- 单元内的关连接请求不随回滚丢失。
- 在作业外、作业结束后、`RunDetached` 内登记，都抛程序错误。
- 已结束作业的发件箱拒绝新工作。
- 单元按顺序执行，且只能结束一次。
- 一个作业只有一个发件箱，只刷出一次。
- （第 2 轮）发送端拒收失败 CMD 的发件箱：关连接请求与 `Close(source)` 仍被单独提交，原异常照常抛出，记 Error。
- （第 2 轮）发送端拒收正常 CMD 的发件箱：关连接请求去掉有序组后提交，补上 `Close(source)`，CMD 以拒收异常失败，记 Error。

`OutboundSenderTests` 覆盖：
- 单连接 100 条按序交付。
- 关连接排在已登记的帧之后。
- 有序组前驱慢 300 ms（上限 1 s）：后续仍等它。
- 前驱卡死，超过 300 ms 的上限：前驱被关闭，后续照常交付。
- Required 失败断组（XO-5 的形状）；BestEffort 失败不断组（显式投递的形状）；失败连接余下的条目 Discarded。
- 断组时关连接仍然执行。
- 整批编码失败时一帧不发。
- 被替换的连接判 Stale，新连接不受影响。
- 交付超时时关闭连接。
- 发送方忽略取消时，仍按期限判超时。
- 队列超限时关闭连接；恰好等于上限时可以入队。
- 卡住的连接不阻塞其他连接，也不阻塞提交方。
- 在作业内提交时，交付不在执行器上。
- 组序号校验。
- 观察者抛异常不影响交付。
- （第 2 轮，B-1）A → B → C，中间的 B 没等过 A 就已结束，分三种形状：B 的连接先前已失败（入队即 Discarded）、B 入队即 OverLimit、B 排队时随队列 `End` 被丢弃。A 卡死（交付期限 30 s，等待上限 300 ms）：三种形状下 C 都在 3 s 内交付，A 判 TimedOut、A 的连接被关闭。

`OutboundRealSocketTests` 覆盖：
- 两帧（1 KiB、48 KiB）之后关闭，共 10 轮：每轮完整收到，并以 FIN 结束。
- 客户端在关闭 300 ms 之后才开始读：仍然完整收到，并以 FIN 结束。
- 发送管道未配置：交付被拒绝，一字节都不发。
- 值相等但引用不同的上下文判 Stale，真实连接照常收到。
- 不读包的客户端（接收缓冲 4 KiB）交付超时被关闭，另一个连接照常收到。
- 不读包的客户端队列超限被关闭，另一个连接照常收到。
- （第 2 轮，N-1）接收缓冲 4 KiB、300 ms 后才开始慢读的客户端，关闭前登记的 900 KiB 单批次完整收到，并以 FIN 结束（macOS）。

### Why

**TouchSocket 刷出做法及依据。** 结论来自 4.3.1 的 IL，读取工具是一个一次性的反射反汇编程序，不在仓库内。

- 发送路径：`TcpSessionClientBase.ProtectedSendAsync` 取得 `WriteLocker`，写入 `Transport.Writer`（即 `BaseTransport.m_pipeSend`），`FlushAsync` 之后返回。另有独立的 `TcpTransport.RunSend` 读管道，`await TcpCore.SendAsync(buffer)` 完成**之后**才 `AdvanceTo(End)`。
- 关闭路径：`TcpTransport.CloseAsync` → `Close()`，先 `Socket.Shutdown(Both)`、`Socket.Close(0)`，然后才取消读写。全程**不排空发送管道**，这就是 TASK-01 的根因。
- 默认的 `SendPipeOptions` 是 pause 64 KiB、resume 32 KiB。小于 64 KiB 的写入，`FlushAsync` 立即返回，数据还全部留在管道里。
- **做法**：
  - 把发送管道设为 `pauseWriterThreshold = 1, resumeWriterThreshold = 1`。这样每次 `FlushAsync` 都要等读端 `AdvanceTo` 越过全部字节，也就是 socket 已经接收了这些字节。于是「发送返回」等于「离开发送管道」。
  - 排空循环逐条 await 这一发送，关连接条目排在其后，所以关闭一定发生在此前的帧离开管道之后。
  - 发送返回之后，如果会话已下线，判为失败。原因是关闭会 `CancelPendingFlush`，让 flush 不抛异常地返回。
- 公开 API 里没有「发送完成」信号，也拿不到 socket，可行的只有这一种做法。
- **变异实验**：
  - 去掉配置检查、改用默认管道后，在空闲机器上 48 KiB 那一帧 5/5 丢失，客户端只读到 0 或 1024 字节。
  - 恢复之后，真实 socket 用例在负载下 20/20 通过，另有 120 次负载运行全绿（见 Checks）。

**上限推算**，详见 `verification/measurement/selection-outbound-bound.md` 与同名 `.tsv`：

- 按 69 个帧类型（64 个实测类型＋5 个可选帧）逐项推算容量上界：最大 body 450,680 B，最大帧数 237；450,680 ＋ 237 × 15 ＝ 454,235 B。
- 同一张表的实测列还原出 TASK-03 的 39,285 B（36,360 ＋ 195 × 15）。两者相差约 11.6 倍。
- 差距主要来自：
  - 满背包快照 0x000D：＋119,218
  - 冷却快照 0x00AC：＋66,705
  - 热键文本 0x01C7：＋65,536
  - 邮箱 0x0061：＋53,590
  - 补给增量 0x000E：＋35,754
- 2 × 454,235 ＝ 908,470，取整为 1 MiB。
- 判据 `OutboundQueueLimitTests`：
  - 钉住 69 行输入、两个和、推算公式，以及 `limit ≥ 2 × bound`；
  - 反例：`2 × bound − 1` 被拒，「上限的一半＋1」作为 bound 时也被拒；
  - 另有一条：两份最大选角背靠背排队，不触发超限。

**编码位置。** 编码在发送端的准入循环里进行（执行器外，附录 B），不在 `Submit` 里。所以执行器既不编码，也不等待。字节上限按准入时编码后的字节数计，在准入时就判定。

**刷出接入点。** 本 Task 只提供 `GameplayOutboxes.RunAsync` 作为作业结束点；测试把它包在 `GameplayWork` 的 body 里，以此模拟作业结束。接到执行器上的方式留给 T-006 决定：可以放在执行器的作业包装里，也可以在每个入口的 work body 外包一层。工作单元与 `DomainTransaction` 的挂钩也在 T-006。

**组内的 Required 与 BestEffort。** RQ-033 的「后续继续交付」和 RQ-034 的「必达失败即不再继续」，用同一条规则表达：只有 Required 失败才断组，被后继的等待超时判为失败也算。建议 T-006：XO-1、XO-3、XO-4 的跟随者用 BestEffort（VC-3），XO-5 的所有者用 Required（DR-10）。

**真实 socket 测试为什么用 `GameTcpService`。** 最初用的是裸 `TcpService`，负载下 60 次中失败 2 次：一次读到 0 字节，一次收全但以 RST 结束。换成生产使用的 `GameTcpService`（`DrainingTcpService`）后，负载下 140 次全绿。`DrainingTcpService` 的注释写着：裸服务在快速重连时会回收仍在使用的 `TcpCore`。失败的根因推定为这个已知问题，**未单独证实**。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告 0 错误（`verification/logs/task-05-build-test.txt` 开头）。
2. **20 次与负载循环**：
   - 新测试隔离连续 20 次（`verification/logs/task-05-new-tests-x20.txt`）：Application.Tests `FullyQualifiedName~.Execution.Outbox.` 每次 14/14；Networking.Tests `FullyQualifiedName~.Outbound.` 每次 31/31（第 2 轮代码）。
   - 真实 socket 用例（`OutboundRealSocket`，7 条）在负载循环下跑 20 次，每次 7/7（`verification/logs/task-05-realsocket-load.txt`，第 2 轮代码）。负载为 Application.Tests 与 Infrastructure.Tests 各自循环并行，CPU 占用合计 486%～870%。
   - 第 1 轮代码在同一负载下另跑 80 次（当时为 5 条用例），全绿（`verification/logs/task-05-realsocket-load-extra80.txt`）。更早的 40 次没有留日志。
3. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`，日志 `verification/logs/task-05-build-test.txt`。
   - 各项目：HttpApi 50 ＋ Protocol 1694 ＋ Pvf 80 ＋ Persistence 475 ＋ Infrastructure 969 ＋ Networking 138 ＋ Application 4415 ＋ Host 165 ＝ **7986** ＝ 7941 ＋ 45。其中 Networking 为 107 ＋ 31，Application 为 4401 ＋ 14。
   - 通过 7964，失败 2，跳过 20：7964 ＋ 2 ＋ 20 ＝ 7986。
   - 失败只有两条：`DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`。
   - 跳过：Application 16、Infrastructure 2、Networking 2。
   - 表征测试 `Networking.Tests.Characterization`：通过 8，跳过 2（b1-strict、b2-strict）。
4. **不接线**：在 `Server/src` 中检索 26 个新类型名与两个新命名空间，排除 `Networking/Outbound/`、`Application/Execution/Outbox/`、`Abstractions/Execution/GameplayOutbound.cs`、`GameplayJobSlot.cs` 之后零命中（`grep -v` 退出码 1）。没有注册扩展方法，也没有任何模块引用这些类型。
5. **上限判据**：`OutboundQueueLimitTests` 5 条通过，内容见 Why「上限推算」。
6. **变异验证**：每次改完都从备份恢复，恢复后逐字节比对一致。

| 变异 | 结果 |
|---|---|
| 断组检查恒为真 | 断组用例变红 |
| 跳过前驱等待 | 3 条有序组用例变红 |
| 去掉交付期限 | 2 条超时单测和真实不读包用例变红 |
| 去掉超限判断 | 超限单测和真实超限用例变红 |
| 身份比较改为值相等 | 真实 Stale 用例变红 |
| 失败时不关闭来源连接 | 发件箱异常用例变红 |
| 刷出时不过滤回滚单元 | 4 条发件箱用例变红 |
| 单元内立即组装 | 2 条发件箱用例变红 |
| 去掉管道配置检查并用默认管道 | 真实「两帧后关闭」5/5 变红 |
| （第 2 轮）上限只施加到紧邻前驱，更早的未落定条目不限时（即第 1 轮的行为） | B-1 三种形状全部变红（每条等满 10 s 的测试耐心后失败） |
| （第 2 轮）拒收后不再单独提交关连接请求 | 两条拒收用例变红 |
| （第 2 轮）正常 CMD 拒收后不补 `Close(source)` | 正常 CMD 的拒收用例变红 |

### Remaining（交给 T-004 / T-006）

1. **T-006 接线时，必须给 Game 与 Channel 两个 TCP 服务调用 `OutboundTransportOptions.ConfigureSendPipe`**，位置在 `NetworkingModule` 的 `AddTcpService` 配置里。
   - 不配置时，新发送端对每次交付都判 Failed，不会静默降级。
   - 配置之后，今天仍在用的 `TouchSocketNetworkMessageSender` 每次发送都要等到 socket 接收为止。过渡期它仍在执行器上直接 await（有 2 s 期限），慢客户端会更早撞上期限。
   - 所以这一配置应当与「执行器上的发送改为登记发件箱」同时生效。
2. **关闭的残余风险，按审查证据改写（N-1）。** TouchSocket 的关闭是先 `Shutdown(Both)`、再 `Socket.Close(0)`。
   - 审查的仓库外探针（macOS 回环）：客户端接收缓冲 4 KiB、300 ms 后才读，64 KiB、512 KiB、2 MiB 三种大小都完整收到，并以 FIN 结束。只有不先 `Shutdown` 直接 `Close(0)`，或显式 linger 0 后关闭，才会以 RST 结束并丢数据。
   - 本轮新增真实 socket 用例 `A_slow_reader_with_a_small_window_gets_a_large_batch_in_full_before_the_fin`（900 KiB 单批次，接收缓冲 4 KiB，慢读），在 macOS 上隔离 20 次、负载下 20 次均通过。
   - 所以「离开发送管道」之后的关闭在 macOS 上不丢数据；**Linux（CI）与 Windows（发布目标）未验证**，交给 T-006 用这条用例（必要时放大批次）复验。
3. **T-006 需要注册与接入的部分**：
   - 注册 `IOutboundSender` → `OutboundSender`（单例），依赖 `TouchSocketOutboundTransport`、`RegistryOutboundEncoder`、`OutboundSendOptions.Default` 和全部 `IOutboundDeliveryObserver`。
   - 注册 `GameplayOutboxes`（单例）。
   - 把 `GameplayOutboxes.RunAsync` 接为每个作业的结束点，以作业的来源连接作为 `source`。
   - 由 `DomainTransaction` 驱动 `GameplayOutboxUnit` 的开始、提交、回滚和不确定：提交完成后调用 `CommitAsync`；提交抛异常时调用 `MarkUncertain`。
4. **附录 D 逐项的跨连接顺序测试**：XO-1～XO-7 各自的测试（AC-012 逐项）由 T-006 在接线后用 `OutboundOrderGroup` 表达并补齐，本 Task 只证明机制。建议：
   - XO-1、XO-3、XO-4：跟随者用 BestEffort，请求者用 Required；
   - XO-5：所有者用 Required；
   - XO-6：每个阶段一组；
   - XO-7：两人的位置消息排在前面。
5. **关连接的跨连接时机**：关连接条目只按该连接自己的 FIFO 排在已登记的帧之后。如果 RQ-012 的「该 CMD 此前登记的**全部**消息交付之后」要求跨连接成立，T-006 需要把关连接条目放进与那些消息相同的有序组。
6. **程序错误类型**：`GameplayOffExecutorException` 是本 Task 新增的。如果 T-004 为 RQ-061 的统一判据另设了类型，届时合并。
7. **推算口径之外的项**：无生产写入列表的遗留数据、城镇在场等人群项、选角后放闸的称号投影，见 `selection-outbound-bound.md` 的「未计入」一节。这些项若成立，可能吃掉 140,106 B 的余量。T-006 接线后，应以容器装满的角色实测核对。
8. **新的分离点**：准入循环、每连接排空循环、失败后的关闭，都在 `SuppressFlow` 下启动，执行器从不 await 它们。它们应列入 T-004 的分离点清单。
9. **组外的 Required 与 BestEffort 没有区别（N-2，约束）。** 今天 `DeliverExplicitAsync` 先发请求者的 Required 直发消息，失败即抛出，BestEffort 目标一个都不再发送。新组件在组外只关闭失败的那个连接，其他目标照常交付。为保持 RQ-034，T-006 必须把普通显式投递（`SendExplicitAsync` 的全部改写点）放进同一有序组：请求者的 Required 条目在前，BestEffort 目标在后，代价是一次组内等待。如果 T-006 决定不这样做，就是一处附录 G 未列的可见变化，须登记。
10. **编码失败的处理与今天的通知路径不同（N-3，待决）。** 新发送端遇到编码失败时关闭连接（EncodingFailed）；今天 `NotificationBus.AttemptAsync` 只判该收件人 Failed、记 Warning，不断开。直发路径今天经 handler 断线（DR-15），与新行为一致。T-006 按附录 E/G 二者择一：要么在发送端对通知类条目不关连接，要么把「编码失败即断开」登记为附录 G 的一条变化。
11. **RQ-031(c) 的解读（N-4）。** 来源连接的关闭只排在来源连接自己的条目之后（按连接 FIFO），其他连接上的条目可能在它关闭之后才交付。本 Task 把这视为满足 RQ-031(c)；若 T-006 认为要跨连接排序，做法同第 5 条。
12. **有序组对象一条 CMD 一个（N-6，约束）。** 发送端为每个组对象记住最后提交的序号和条目链：复用组对象并让序号从头开始，`Submit` 会抛异常；长期持有一个序号不断递增的组，会挂住整条条目链（本轮已在条目落定后释放其字节，剩下的只是小对象）。约束已写进 `OutboundOrderGroup` 的 XML doc。
13. **交付期限按条目计（N-7，待决）。** 2 s 期限针对的是一个条目整体。若把选角（上界约 454 KB）作为一个条目，在低带宽或高 RTT 链路上，扣除内核缓冲后要在 2 s 内送完；今天直发没有期限，通知是按收件人计的小消息。T-006 需定下选角条目的切分粒度，或者实测一次，并对照 VC-8 判断是否可以接受。

### 各轮变化

**第 2 轮（相对第 1 轮）：**

- **B-1**：有序组的等待上限改为施加到最早的未落定前序，沿链逐个限时（`OutboundSender.PredecessorsDeliveredAsync`）。新增 Theory 用例覆盖中间条目「入队即 Discarded」「入队即 OverLimit」「排队时随 `End` 被丢弃」三种形状；修正前三组都会等满测试耐心后变红，恢复第 1 轮逻辑的变异同样三组全红。
- **N-5**：发送端拒收发件箱时，关连接请求与 `Close(source)` 仍单独提交并记 Error；正常 CMD 因此失败。新增 2 条用例，2 处变异各自变红。
- **N-6**：`OutboundOrderGroup` 的 XML doc 写明「一条 CMD 一个组」；条目落定后释放已编码字节。
- **N-1**：新增真实 socket 用例「小接收窗口慢读客户端，关闭前的 900 KiB 批次完整到达并以 FIN 结束」（macOS 通过）；Remaining 第 2 条按审查证据改写。
- **N-2、N-3、N-4、N-7**：写入 Remaining 第 9～13 条（N-6 为第 12 条）。
- 新测试由 39 条增至 45 条；全量由 7980 条增至 7986 条。日志 `task-05-build-test.txt`、`task-05-new-tests-x20.txt`、`task-05-realsocket-load.txt` 已用第 2 轮代码覆盖重跑。

### 主控方补充（第二轮审查之后，提交之前）

- **对 RQ-033 的理解**（审查 R2-N1）：「最多被拖慢一个等待上限」按每个慢前序计，每个慢前序最多拖慢一个上限，所以总等待的上界是「链上卡住的前序数 × 2 秒」。如果整组只给一个上限，那些只是排在慢前序后面、自身并不慢的条目也会被判失败，反而违背 RQ-033。今天 `NotificationBus` 也是按收件人逐个累积等待的。
  - T-006 要注意：线性链会把同一阶段的多个收件人（例如 XO-5 的城镇同伴）也排出先后。要让一个阶段只占一个等待上限，需要另外设计「阶段」形状的有序条目。
- **T-006 处理**（审查 R2-N2）：停机时发送端已经释放，失败路径上第二次 `Submit` 会抛 `ObjectDisposedException`，盖掉 CMD 原来的异常。T-006 接线时要给第二次提交也包上 try 并记日志。
- **T-006 复验**（审查 R2-N3）：在 Linux 和 Windows 上复验「小窗口慢读、关闭前的大批次完整到达」时，要同时记录「关闭发生在客户端开始读取之前」这个先后关系。macOS 的内核会一次收下全部字节，换到其他平台，这个时序可能不同。

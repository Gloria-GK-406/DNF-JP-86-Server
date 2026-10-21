---
kind: rks-adversarial-challenges
target: .rks-agent/tasks/serial-gameplay-executor/plan.md
target_revision: worktree HEAD a02bce46（plan.md 当前文本，mode: task-units，state: ready）
generator: 独立子代理（全新上下文；未参与 Plan 编写与审查；只读）
---

# Plan 对抗挑战：serial-gameplay-executor

## 目标与依据

- **目标**：`.rks-agent/tasks/serial-gameplay-executor/plan.md`（high-level Plan，`mode: task-units`，`state: ready`），基线 `a02bce46`。代码路径相对 `Server/src/`，测试路径相对 `Server/tests/`。
- **Spec 状态**：`spec.md` `state: ready`，`contract.revision: 2`；`reviews/spec-review-r3.md` 结论 `approved`（残余 R-1、R-5、R-6 由 Plan 第 61–64 行澄清）。
- **治理来源**：`spec.md`、`TaskContract.md`（r2）、`routing-decision.md`、`research/01`–`05`、`AGENTS.md`、`Docs/Agent/RefactorPreferences.md`；分辨率标准取自 `writing-plans` 与其 `references/task-units.md`（局部设计留给 Task Unit 不算缺陷，但后续单元要依赖、代价高的共享边界须在 Plan 定下）。
- **攻击范围**：Plan 的路线（钉行为 → 建零件 → 纯搬迁 → 删锁 → 换投递 → 调用链 → 归并 → 收口）、共享决定、依赖顺序与 RQ 覆盖，是否会让 Spec 的结果无法达成，或在中途造成难以发现的损害。重点：T-004 过渡态的死锁/活锁、AsyncLocal 帧继承、T-006 前直接等待发送、测量闸 M-1 的位置、「每步全绿」承诺的可行性、T-008 是否会倒逼回改。
- 已核对且未保留的方向（无实质失败路径）：
  - 执行器内联 `SendAsync` 时的 AsyncLocal 继承：同一 async 流内联执行，dispatcher/账本帧可被继承；`ReplayStateProjectionDelivery.cs:36-38` 这类「同流持门再 `SendAsync`」靠借用帧仍可工作。
  - 「领域服务无环境事务时自开边界并抢全局门」：作业整体搬进执行器后，全局门只会被同一作业或串行的前一作业持有。今天同流里已存在的嵌套结构，搬迁后不变，不会新增自锁。生产者侧持门的情形归入 CH-1。
  - 请求处理路径上没有 `Task.WhenAll` 或 `TaskCompletionSource` 式的跨流等待（`grep` 只命中事件分发器、调度器、登录会话维护三处后台外壳，以及门本身）。
  - RQ 覆盖：每条 RQ 都落在某个 T-xxx；轻微不对称（RQ-006 的落地只写在共享决定里，T-004 覆盖栏未列）不改变决策。

## CH-1：T-004 让执行器外「持门再 SendAsync」的生产者把整个执行器锁死

- Risk: high-risk
- Failure and impact:
  - Plan 把 `SendAsync` 的语义改为「执行器外调用 = 入队并等待完成」（第 49–51 行）。请求级帧刻意不跨队列传递，改为在作业内重建（第 58 行）。门要到 T-005 才删。
  - 今天有后台服务先在自己的流上取会话门 Exclusive，再 `SendAsync` 一条选中会话命令，靠管线 45 从 AsyncLocal 借用这把租约。
  - T-004 之后，这条命令改在执行器上运行。执行器的流上没有借用帧，于是管线 45 按普通 Shared 去排队，排在生产者持有的 Exclusive 后面。生产者又在等这条命令完成，**整个执行器永久停住，全服卡死**。
  - 同一结构还会出现在三处：
    - 事件分发器若把「取全局门 + 开事务」留在生产者侧，执行器上的订阅处理没有环境事务，就会自开边界、抢生产者手里的全局门；
    - 测试代码先持门再 `SendAsync`；
    - T-004 拆成多个 Task Unit 后，`SendAsync` 语义已翻转、但某个持门生产者还没迁移的任何中间提交。
  - 最诱人的错法是：既然「经总线」的后台服务看起来随 `SendAsync` 翻转就自动进了执行器，就把它们当成已迁移。它们恰恰是持门的那批。
  - Plan 对此只说过渡期「门全部变成无人争用的」（第 28 行）和「后台服务只负责按节奏入队」（第 54 行）。它没有规定作业的切分边界，没有规定 T-004 内部的迁移次序，也没有检测手段。
  - 这类死锁只在定时器恰好碰上同账号会话时触发，单元测试很难稳定复现；一旦漏到 T-005 之后，又会被「门已删」掩盖成别的症状。所以必须在 T-004 开工前定下来。
- Evidence:
  - `Dfo.Server.Protocol/Game/Tournament/TournamentRewardSweepService.cs:38-39`：取投影门与会话门 Exclusive；`:47`：在门内 `commands.SendAsync(new SelectTournamentRewardCommand(...))`。
  - `TournamentSlice.cs:118-119`：该命令实现 `ISelectedSessionCommand`。
  - `Dfo.Server.Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:26`：`lifecycle.BorrowOrAcquireSharedAsync(account, ct)`。
  - `OnlineUsers/SelectedSessionTransitionGate.cs` 的 `BorrowOrAcquireAsync`：只沿当前流的 AsyncLocal `current` 链查找可借用的帧，查不到就走 `base.AcquireAsync`。
  - 同类持门调用：`PremiumDailyRefreshService.cs:53-55`（会话门内 `SendAsync`）；`research/01` §3 中取「投影门＋会话门」后发命令的后台服务（DungeonPartyWipe、AltarScheduler、两塔 ReturnSweep 等）。
  - 测试侧：`Application.Tests/World/CommonExperienceEffectTests.cs:228-229`（测试代码持会话门 Exclusive 后 `SendAsync`，依赖借用）。
  - 事件侧：`DomainEventDispatcher.cs:113-125`（`ReliableAsync` 先 `gate.AcquireAsync` 再 `transactions.Begin()`，然后调用订阅者）。
- Minimum correction: 在共享决定里写三条。
  1. **切分规则**：一个作业必须包住该段工作的全部取门、事务与信号量。生产者跨入队等待时，不得持有任何门、环境事务或请求帧。
  2. **T-004 内部次序**：`SendAsync` 语义翻转，与全部「执行器外持门者」（research/04 门表「外」列，以及 research/01 §3 中取门的后台服务）的迁移落在同一提交，或在它们之后，不能更早。
  3. **检测**：过渡期给门加一个运行期断言：在执行器外获取，或在执行器上获取时发生等待，即抛出程序错误。这也是 CH-2 的检测手段。

## CH-2：先删保护、后开判据。漏掉的入口在 T-005 之后失去保护，要到 T-009 才会被发现

- Risk: high-risk
- Failure and impact:
  - `routing-decision.md` 把「漏掉一个入口或一项保护意图」列为最可能的失败方式。
  - Plan 删锁的前提是「门已经无人争用」（第 28、81 行），但这个前提没有任何测量。
  - 能发现「执行器外改状态、写库、发包」的 RQ-061 运行期判据，被排在最后的 T-009（第 85 行）。在它之前，有删锁（T-005）、换投递（T-006）、调用链（T-007）和工作量最大的归并（T-008）。
  - 如果 T-004 漏掉一个入口，例如某个后台流、`_ =` 分离任务、GM 只读端点读内存态，或 research/04 列出的「单执行器后仍会被总线外线程访问」的对象，T-005 一删门，它就和执行器真并发：
    - 内存态只剩短锁，「锁内复查」的多步不变量没有了；
    - SQLite 回到多写者，又会出现「先读后写等满 30 秒」（AGENTS.md「构建与运行」）或 BUSY 失败。
  - 确定性测试几乎看不到这种竞态，「每步全绿」照样成立。到 T-009 判据开启时，问题可能已经被 T-006 的发件箱（执行器外登记）和 T-008 的结构复制到很多处，回溯成本最高。
- Evidence:
  - `plan.md` 第 81 行（T-005 依赖只有 T-004）与第 85 行（RQ-061 在 T-009）。
  - `research/04`「单执行器后仍会被总线外线程访问」一段列出的对象：`OnlineStateRegistry`、`LoginSessionRegistry`、`DungeonRunDomainService`、生物运行时、两塔、比武、祭坛等。
  - `research/01` 末尾「不经命令总线却改状态、写库或发包」共 14 类入口。
  - `TaskContract.md` Constraints「单数据库写者」。
- Minimum correction:
  - 把 RQ-061 中不依赖发件箱的两部分移到 T-004 的完成条件里，至少在测试与 Debug 下以抛出方式开启：
    - 写 SQLite 的工作单元开启；
    - 附录 F「改为执行器内访问」的写入口。
  - 再加上 CH-1 的门断言。
  - T-005 的进入条件：在这些判据开启的状态下全量测试通过，并完成附录 A 的入口对账。
  - 发件箱登记的判据随 T-006 开启。T-009 只做「全开 + 反例 + 最终对账」。

## CH-3：「在执行器上」判定原语会被分离任务继承，过渡内联规则因此静默变成并发执行

- Risk: high-risk
- Failure and impact:
  - 过渡期的内联规则（第 51 行）、RQ-003 的自我等待检测、RQ-061 的运行期判据，全都依赖 T-002 的「在执行器上」判定原语（第 77 行）。Plan 没有给这个原语任何约束。
  - 本仓现有的所有请求级帧都是 AsyncLocal（research/04 统计共 8 处），实施者自然也会用 AsyncLocal 做这个标记。可代码里有从作业内部发起的分离任务，它们会继承作业的 ExecutionContext：
    - 关连接器 `_ = CloseDetachedAsync(...)`，内部先 `Task.Yield()` 再 `CloseAsync`；
    - 卡牌展示的 `_ = RunLayoutAsync` / `_ = RunAutoFlipAsync`。
  - 这些分离流会自认「在执行器上」：
    - **T-004～T-008**：它们发的 `SendAsync` 被内联，和执行器上正在跑的 CMD 并发。T-005 删门后，这就是无保护的并发写。表面上一切正常，测试也全绿；
    - **T-008 删掉内联规则之后**：它们「入队并等待」会被判成 RQ-003 程序错误，于是到期计时器或关连接引起的生命周期 CMD 以失败结束，会话不拆除、位置不落库；
    - 如果 `OnTcpClosed` 恰好在关闭调用的流上运行，断线清理本身也会命中上面两种情形之一。
  - 如果改用线程或 `SynchronizationContext` 作标记，`Task.Yield()` 会把关闭工作投回执行器，在 CMD 之间（甚至 CMD 内的 await 点）插入非 CMD 工作，违反 RQ-001。
  - 同类问题：生产者的完成信号若没有 `RunContinuationsAsynchronously`，生产者的后续代码（例如收包回调的 `InvokeNext`、后台循环的下一轮取门）会跑在执行器的调用栈上。
  - 这个原语从 T-002 起就被 T-004～T-009 依赖，后期发现就要回改全部判据与过渡规则，代价高；而且按 `task-units.md`，它属于后续单元要依赖的边界，应当在 Plan 定下。
- Evidence:
  - `Dfo.Server.Networking/Runtime/TouchSocketNetworkConnectionCloser.cs:18`、`:26`（`_ = CloseDetachedAsync`）、`:30-44`（`await Task.Yield()` 后 `CloseAsync`）。
  - `Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs:54`、`:168`。
  - `NetworkPlugins.cs:31-43`：`OnTcpClosed` 依次 await observer 的 `DisconnectedAsync`；T-004 后它要入队生命周期 CMD 并等待。
  - `grep` 显示 `src` 中没有 `SuppressFlow` 或 `UnsafeQueueUserWorkItem`。
  - `plan.md` 第 51、77 行。
- Minimum correction: 在共享决定里写明原语的语义。
  - 「在执行器上」指「当前正由执行器驱动的那个作业的流」。从作业中分离出去的工作不得继承这个标记：在已知的分离点（关连接器、卡牌计时器，以及 T-004 对账时发现的其他 `_ =` 点）抑制执行上下文流动，或改由执行器显式派生。
  - 生产者的完成信号一律异步续延。
  - T-002 补两条反例用例：
    1. 作业内分离出的任务被判为「不在执行器上」，它的 `SendAsync` 是入队，不是内联；
    2. 作业内请求关连接后，生命周期 CMD 正常入队并完成。

## CH-4：执行器怎么跑、测试何时改写都没定，T-004 的「全绿」很可能靠旁路执行器换来

- Risk: high-risk
- Failure and impact:
  - T-004 翻转了 `SendAsync` 语义，但 Plan 把受冲击测试的改写分到后面：测门与测交错的放在 T-005，发送即记录的替身放在 T-006，假总线加直接 new handler 的放在 T-008（第 68–70、81–84 行）。同时又要求每步全量测试全绿（第 24、91 行）。T-004 一翻转，会同时出现几类问题：
    - 81 个测试文件用 `BuildServiceProvider` 组装容器，其中只有 5 个启动托管服务。执行器的消费循环如果是托管服务，这些用例里的 `SendAsync` 入队后就没人取，测试挂起，直到 `--blame-hang-timeout 3m`。
    - 测试代码先持门再 `SendAsync`、等待借用（`CommonExperienceEffectTests.cs:228-229`、`OnlineExpiredItemCleanupTests.cs:117-126`），会撞上 CH-1 的死锁。
    - 用 TCS 卡住一条命令再发第二条、以观察排队的用例（`SelectCharacterLifecycleTests`、两塔 `*ProjectionConcurrencyTests` 共 22 条等）同样会挂起。
    - 宿主启动也受影响：`GmBootstrapService.StartAsync`（`GmBootstrapService.cs:12-23`）与 `JoustMaintenanceService.StartAsync`（`:14-30`）都在启动期调用 `SendAsync`。执行器如果晚于它们启动，宿主就起不来。
  - 在「全绿」的压力下，实施者最可能的局部修补是：
    - 「没有运行中的执行器时内联执行」；
    - 让直接 new handler 的测试继续绕开执行器。
  - 任何一种修补之后，T-004～T-008 的测试证据都不再经过执行器与发件箱：
    - RQ-001 的串行、T-006 的发件箱语义与附录 E 的改写，都由不经过它们的测试「证明」；
    - 生产环境的执行器一旦启动失败，也会静默退化为并发。
  - Plan 第 68 行「测试统一经真实的 dispatcher 或执行器驱动」与第 69 行「假总线用例到第 7 步才改写」本身就互相矛盾。
- Evidence:
  - 测试中 `BuildServiceProvider` 81 个文件，其中含 `StartAsync`/`IHostedService` 的 5 个；`GetRequiredService<ICommandBus>` 85 个文件。
  - research/04「改造后要删或改写的测试」与 research/05「依赖并发与时序的测试」两份清单。
  - `CommandBus.cs:11-59`：今天是同步内联执行，不依赖任何托管服务。
  - CI 命令中的 `--blame-hang-timeout 3m`（research/05）。
- Minimum correction: 在共享决定里写三条。
  1. **执行器生命周期**：执行器的启动不依赖托管服务的注册次序，例如首次入队即启动，或在组合根中最先注册并有启动断言；测试容器跑真实执行器；除第 51 行那条具名过渡规则外，不得有任何「执行器不在就内联」的旁路，并由判据钉住。
  2. **测试改写提前**：把持门后 `SendAsync`、TCS 交错、测门本身这几类用例的改写或删除，提前到 T-004（排在翻转之前或同一提交），并在 T-004 写出测试数量加法。
  3. **T-006 的证据口径**：附录 E 各行的证据必须经执行器与发件箱驱动，不能用「直接 new handler + 抛异常的假发送器」的旧用例。

## CH-5：T-006 之前执行器直接等待无超时的发送，这不只是性能问题；M-1 也测在这个过渡态上

- Risk: low-risk
- Failure and impact:
  - Plan 认定过渡期「执行器上的作业仍直接 await 发送器……只影响性能，不影响正确性」（第 53 行）。但入站分发用的是 `CancellationToken.None`，直接发送也没有期限；只有 `ProjectAsync` 一路有每收件人 2 秒的期限。
  - 如果 TouchSocket 有写端背压（research/03 推断有，但未反编译确认），一个不读数据或半开的客户端，就能让 `ISender.SendAsync` 长时间挂起。在 T-004～T-006 期间，这会冻结整个执行器，而今天只会卡住那一条连接的流。
  - 再叠加「关闭连接要等该连接当前的收包回调结束」，可能形成 RQ-008 正要排除的环：执行器向 B 发送受阻 → B 的关闭在等 B 的收包回调 → 该回调在等排在执行器后面的 B 的 CMD。
  - 测量闸 M-1（第 80 行）正好在这个过渡态上测 (b)～(e)：
    - (e)「单个连接持续灌包」测到的主要是内联发送的阻塞，而这种阻塞到 T-006 就会消失；
    - (c) 里也混着门与内联发送的开销。
    - 结果可能误判「明显达不到」而停下，也可能在读包及时的合成客户端下误判通过。
  - 反过来，最便宜的早停依据没有用上：T-001 已经要在基线上跑测量，基线的 handler 耗时分布本身就是 CMD 执行时长的下界（执行器只会把同样的工作串起来）。如果它已经超过 RQ-091 的 p99.9 ≤ 100 ms、max ≤ 250 ms，就可以在建零件和搬迁之前停下。
  - 这一条之所以是 low-risk：分支在收口前不部署，M-1 误判有用户停点兜底，T-009 还会复测。
- Evidence:
  - `Dfo.Server.Networking/Runtime/NetworkPlugins.cs:146-149`（`DispatchAsync(..., CancellationToken.None)`）。
  - `TouchSocketNetworkMessageSender.cs:98-100`（直接 await `ISender.SendAsync`，没有期限）。
  - `research/03` §1（背压是推断）、§2（2 秒期限只在 `NotificationBus`）。
  - `TouchSocketNetworkConnectionCloser.cs:32-33` 的注释。
  - `plan.md` 第 53、76、80 行。
- Minimum correction:
  - T-004 起，执行器上的内联发送套用现有的每收件人 2 秒期限，超时按发送失败处理。这样附录 E 的各条路径在过渡期仍走今天的失败语义。
  - M-1 把 (c)(e) 标为「过渡态，仅供参考」，或改到 T-006 之后再测。
  - T-001 增加一项基线预判：各类请求的 handler 耗时与 RQ-091 时长阈值的对照。

## CH-6：T-006 的「本 CMD 的发件箱」与提交钩子落在尚未归并的结构上，归属没定

- Risk: low-risk
- Failure and impact:
  - T-006 在 T-008 归并之前接入发件箱。那时一个执行器作业里有三种范围：
    - dispatcher 的 DI scope；
    - 每次内联 `SendAsync` 新开的 DI scope；
    - 管线 60 号行为里每条内层命令自己的事务。
  - 如果发件箱按 DI scope 注册，就会每条内层命令一个发件箱，先于 handler 的发件箱刷出。这与 RQ-032「按登记先后」不符。内层命令确实会登记出站：`DirectAsync` 订阅在调用方命令内同步执行，其中有发包的（research/01 §4）。
  - 如果组装点（RQ-031(0)）的「提交完成钩子」挂在 `UnitOfWorkBehavior` 上而不是工作单元本身，T-008 换成显式工作单元作用域时就要重做。
  - 这两处在 T-006 选错，表征测试只能覆盖 7 条工作流，其他协议上的乱序未必被发现。但它们属于 T-006 的局部设计，T-006 细化时写清即可，不必阻断 Plan。
- Evidence:
  - `Dfo.Server.Application/Commands/CommandBus.cs:26`（每次 `SendAsync` 开新 scope）。
  - `plan.md` 第 52、60、82 行。
  - `spec.md` RQ-014、RQ-031(0)、RQ-032。
- Minimum correction: Plan 或 T-006 的 Task Unit 写明两点。
  - 发件箱以执行器作业（根 CMD）为单位，不以 DI scope 为单位。
  - 提交钩子由工作单元本身（今天的 `DomainTransaction`/`DomainTransactionScope`）提供，T-008 沿用，不重做。

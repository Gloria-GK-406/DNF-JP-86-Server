---
kind: rks-review
target: tasks/TASK-12.md
scope: Task Unit result quick review（TASK-12 / T-004b5，Contract Revision 1 ＋「主控方裁定（执行前）」10 条及停止条件补充，执行轮次：Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
---

# TASK-12 结果快速审查

## 审查对象与依据

- 目标：`tasks/TASK-12.md`（契约、Detailed Plan、待裁定、主控方裁定、Result）；HEAD `648fbc60` ＋ 未提交改动：`git diff HEAD` 5 个文件（`DomainEventDispatcher.cs`、`GameplayTimerWork.cs`、`DomainEventDispatchTests.cs`、`UsageQuotaTests.cs`、`task-07-conflicts.md`）；未跟踪的 `DomainEventDispatchTests.Executor.cs`、`verification/logs/task-12-*`。
- 审查时 `DomainEventDispatcher.cs` 的 sha1 为 `2a170a3e5d66042710efd14b1b910c72b1dc5b6b`，与 Result 变异核对记录的值一致。
- 依据：`plan.md` 共享决定「事件」；`spec.md` RQ-014、RQ-040～RQ-043、RQ-050、RQ-051、AC-015；附录 A 第 8 行与附录 B；`tasks/TASK-11.md` 裁定第 7 条及补充；`verification/event-chain.md`；`AGENTS.md`（判据偶发失败、满载假红两节）。
- 引用的审查准则：reviewing-work 的 Task Unit、Code（局部范围）。
- 本审查没有跑全量。审查者自己做的检查见文末「审查者的独立检查」。

## 结论

没有阻断项。

生产改动与契约、裁定一致。`ProcessAsync`、`ReliableAsync`、`LiveOnlyAsync` 的方法体逐字未动，并且整段在作业里。`ReadDueAsync` 在作业外，只读。失败记录与失败的那次尝试在同一个作业里。LiveOnly 在同一个作业里先提交消费决定，再投影。守卫的次序是：事务检查 → `AssertNotOnExecutor` → 组门。组门和准入门都保留了。

主控方的两条判断都成立：
- `ScheduledTaskExecutorTests.Each_attempt…` 是 TASK-11 遗留的偶发失败，本单元没有加重它；
- Networking 主机崩溃记为取证缺口是对的。但「与本单元无关」目前没有证据支持，也没有被排除，见非阻断项 2。

下面 5 条非阻断项里，第 1 条是测试区分力上的缺口：裁定第 2 条（一条 delivery 一个作业）今天没有任何用例守着。另外 4 条是证据表述的问题。没有一条是本单元的生产缺陷。

## 重点逐项

### 1. 作业边界（重点 1）

- `git diff HEAD -- Server/src` 显示，`DomainEventDispatcher.cs` 的改动只有 5 处：
  - 构造注入 `IGameplayQueue`；
  - `DrainOnceAsync` 里的守卫，以及逐条入队；
  - `SourceOf`／`CommandTypeOf`；
  - `RunGroupAsync` 里新增的 `catch (GameplayExecutorStoppedException)`；
  - 类型 remarks。
- `ProcessAsync`、`ReliableAsync`、`LiveOnlyAsync`、`WaitForWakeOrDueAsync`、`RetryDelay` 都不在 diff 的 hunk 里，逐字未动。
- 作业体是 `consumed = await ProcessAsync(delivery, ct)`。它经 `GameplayTimerWork.RunAsync` 以 `CancellationToken.None` 入队，作业体拿生产者令牌，取消带回生产者。这与 TASK-11 的形状相同。`processed++` 在生产者侧、作业完成之后做，被取消时不会多算。
- 失败记录：`ProcessAsync` 的 catch（取准入门 → 开事务 → `FailAsync` → 提交）在 `ProcessAsync` 内部，所以与失败的那次尝试在同一个作业里。审查者在副本上独立重做了 M2（catch 抛出一个延迟标记，由生产者另入队一个作业记录失败），只有 N3 变红（`Expected/Actual` 两个不同的作业 ID），与 Result 一致。
- LiveOnly：消费决定的事务在 `await using` 块里提交，投影在块外、同一个方法里，所以两者都在同一个作业里。N2 在投影时另开连接，读到 `state=1`，记录到「当时没有事务」，并核对 `Complete` 与投影的作业 ID 相同。
- 读到期留在作业外，读的是 SQLite（附录 B）。读到的批可能过期，但作业里的 `TryBeginAsync` 带条件复核，与今天组门内的复核等价。

### 2. 守卫、死锁论证与停机分支（重点 2）

- 守卫的次序是：事务检查 → `GameplayJob.AssertNotOnExecutor` → `serial.WaitAsync`。这个次序守着两件事：
  - M8（把守卫挪到事务检查之前）使 `UsageQuotaTests:117` 以 `GameplaySelfWaitException` 变红。`GameplaySelfWaitException` 派生自 `InvalidOperationException`，但 xUnit 的 `Assert.ThrowsAsync<T>` 要求类型精确相等，所以这条用例确实能区分两种次序；
  - M6（去掉守卫）使 N8 在 5 秒处超时，报「没有抛出」。
- 死锁论证成立：
  - 等待事件作业的执行器外代码只有两处：组循环，以及测试直接调用的 `DrainOnceAsync`。它们等待时只持本组的组门；
  - 执行器上的作业在取组门之前就被守卫拒绝。生产代码里 `DrainOnceAsync` 的调用方只有 `RunGroupAsync`（审查者 grep 过）。
- 审查者核对了持有 `IGameplayQueue` 的 33 个类型：
  - Protocol 侧的持有者都是 `BackgroundService` 定时源，或卡牌协调器的定时回调，没有一个是订阅处理器的依赖；
  - 准入门在生产中的持有者是 `TransactionalExecutionBehavior` 和三个地下城领域服务。它们持门期间不等待执行器作业，Result §9.5 的结论成立。
- 执行器已停止的分支：`GameplayExecutorStoppedException` 派生自 `InvalidOperationException`，不是 `OperationCanceledException`，所以 catch 的次序没有问题。它只可能来自入队（`ProcessAsync` 的通用 catch 会吞掉作业体里的同类异常），所以这个分支不会误吞订阅失败。

### 3. 来源判定与区分力（重点 3）

- 来源按 `delivery.Event.OriginInstanceId == instance.Id` 判定，CMD 类型是 `event:<id>` 或 `startup:event/<id>`，与裁定第 1 条一致。这个字段与 LiveOnly 判断「旧进程提示」用的是同一个，同一事实只有一个判据。
- 一律新根是过渡期标注，已登记为 T-007 的缺口（RQ-050「事件 CMD 继承父链、重试继承首次的链」）。
- M1～M8 的日志与 Result 逐条相符。审查者另做了两个变异实验（见文末）：
  - M2 的重做结果与 Result 相同；
  - 新的变异「一批一个作业」**全部用例通过**，见非阻断项 1。

### 4. 冲突用例（重点 4）

- `UsageQuotaTests:117`：diff 只是把第 1 个事务块整体包进 `TestGameplay.RunAsync(f.Services, …)`，块内的断言逐字未改。差异核对显示 `UsageQuotaTests.cs` 没有集合不等（6＝6、44＝44）。
- `A_slow_live_group…` → `A_slow_live_hint_postpones…`：
  - 「旧断言 → 新断言 → 保护意图」已写进 Result 和 `task-07-conflicts.md` §10，并按 RQ-043 登记为意图变化；
  - 冲突总数的加法 3 ＋ 75 ＋ 53 ＋ 1 ＋ 1 ＋ 3 ＋ 2 ＝ 138，审查者复算无误；
  - 断言行多重集合删 1 行 `Assert.Single(f.Probe.Live)`，加 2 行，都在这个用例里，与差异核对日志一致；
  - 新的标记作业检查（放行前 `Assert.False(marker.Task.IsCompleted)`）只会漏报，不会误报。M1、M3 使它变红，说明它有区分力。

### 5. 探针 §3.5 归零（重点 5）

论证成立：
- 执行器以 `RunContinuationsAsynchronously` 完成作业，所以生产者在线程池上恢复。`DrainOnceAsync` 的任务完成之后，测试体的 await 经 `SetExistingTaskResult` 内联续行；
- 栈浅的时候，分发器帧成了最外层的 Dfo 入口，`fam.py` 因此把记录归到 3.5。

审查者抽查了 `OnlineExpiredItemCleanupTests.Online_rollover…`：测试在 `DrainOnceAsync(Subscriber)` 之后直接调用 `ICommandBus.SendAsync(new CleanupOnlineExpiredItemsCommand(...))`，与原始记录中「`entry DomainEventDispatcher.DrainOnceAsync`、`nearest SelectedSessionExecutionBehavior`」的形状吻合。

原始记录里只有单行摘要，没有完整栈，所以「17 条全部是测试续行」这个归类依赖 scratchpad 里的 `residual.py`，审查者没有重跑它。不过 17 条都落在两个调用总线的测试和一处测试直读上，各族命中数守恒（3.7 ＋ 3.10 在两轮都是 12570），所以归零结论可信。

### 6. 数字与日志（重点 6）

- 全量各项目的总数、通过、失败、跳过，与 `task-12-build-test.txt` 的摘要行逐项相符：8087 ＋ 3 ＋ 20 ＝ 8110 ＝ 8101 ＋ 9 － 0。
- 日志信号：审查者 grep 复核为 0 的有 `direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`、`Gameplay event:`、`Gameplay startup:event/`、`could not persist or scan`、`enqueues no more`；`Gameplay inbound:… failed` 是 3（`GameSelectCharacter` 2、`GameSellItem` 1）；`the drain tears it down` 是 3。都与 Result 一致。
- 耗时：抽查 `SealedTreasureTests.Production_factory…`（13 → 29 s）、`HostShutdownOrderTests.Sigterm…`（4 → 13 s）、`GmCeraHttpTests.Boundaries…`（8 → 17 s），与 Result 的增量相符。慢用例隔离复跑都在 1～3 秒。`HostShutdownOrderTests.Sigterm…` 是 14 条里唯一与停机路径直接相关的，隔离下 1 秒，可以判为负载波动。
- ×10 日志：11 条 × 10 轮全部通过。

### 7. 两条已知判断的检验

**`ScheduledTaskExecutorTests.Each_attempt…`：判断成立。**
- 该用例只调用 `AddScheduledTask`，只启动 `ScheduledTaskHostedService`（`OfType<ScheduledTaskHostedService>().Single()`），不构造 `DomainEventDispatcher`。本单元对它可见的唯一改动是 `GameplayTimerWork.cs` 的注释。
- 基准导出上同样复现，频率与工作区同量级：负载下基准 9/20、工作区 8/20。
- 机制推断（推进 2 分钟时 1 分钟间隔同时到期）与断言 `Expected: 2, Actual: 3` 的形状相符。
- 没有证据表明本单元加重了它。

**Networking 崩溃：记为取证缺口的判断成立；但「与本单元无关」目前既没有证据，也没有被排除。** 详见非阻断项 2。

## 阻断项

无。

## 非阻断项

1. **裁定第 2 条「一条 delivery 一个作业」没有任何用例守着。**
   - 证据：审查者在源码副本上把 `DrainOnceAsync` 的 `foreach` 整个移进一个作业（「一批一个作业」，CMD 类型取第一条 delivery 的），重新构建后跑 `DomainEventDispatchTests`、`UsageQuotaTests`、`DomainEventSchedulingTests`：**35 条全部通过**。
   - 原因：N1～N4 每次排空只有一条到期 delivery。N5 的 3 个并行排空由组门串行，`MaximumOutstanding == 1` 在批粒度下也成立。
   - 后果：Result 把「一条 delivery 的消费是一条事件 CMD」引为 N1～N3 的证据，其实这一点只有代码阅读支持。契约的 Result review 理由也指出，这个粒度是 T-007（按 delivery 挂链）的直接前提，退化要到 T-007 才会暴露。今天的生产代码是对的，所以不阻断。
   - 建议的最小补强：在 N5（或 N1）里，断言一次排空多条 delivery 时，`event:counter` 的作业数等于处理条数，并且每条 delivery 的 `TryBegin` 作业 ID 互不相同。

2. **Networking 崩溃：「本单元没有新增连接或事务的生命周期路径」不足以把它和本单元分开。**
   - 这个栈根在本任务全部历史日志中只出现过这一次（审查者 grep 过 `RollbackExternal`／`rollback_hook`，只在 TASK-12 的文件里出现）。
   - 形状是：某个连接的 `SafeHandle` 在事务未完成时被关闭。通常是带着未结束事务的连接没有被释放、被 GC 回收；从反向 P/Invoke 回调抛出时，没有托管调用方帧，与日志相符。
   - Networking 的集成宿主经 `ApplicationModule` 注册了事件分发，所以本单元的代码确实在这些测试里运行，不能从组合上排除。
   - 本单元改变的是事务所在的位置（在执行器作业里），不是事务的写法（仍然是 `await using`）。审查者没有找到一条能让作业体被永久挂起、从而泄漏事务的具体路径。
   - Result 的措辞（「只是代码阅读，不是不归因的明证」、没有归入已知偶发、没有追加进 AGENTS.md 的表）符合 AGENTS.md「判据偶发失败」一节，判断成立。
   - 建议主控方把它作为未关闭的取证缺口，带进后续单元的全量对照：再次出现时，要取到崩溃时正在运行的用例（例如 `--blame` 或逐类分批），而不是只看总数。审查者另做的复跑结果见文末。

3. **基准上复现调度偶发的计数不完整。** Result 写「基准隔离 30 次失败 3 次」，但 `task-12-sched-repeat.txt` 在那之前还有一段基准隔离 20 次、0 次失败。合起来是基准隔离 50 次失败 3 次。结论不变，Result 宜按日志写全。

4. **执行器已停止的分支只记 Information，不带异常。** `GameplayExecutorStoppedException` 在执行器消费循环本身故障时带有 `InnerException`。这一行日志会把故障原因压成一句「已停止」。按设计，执行器自己应当记下故障，所以只是诊断便利的问题。如果执行器那边不保证记录，T-008 收口日志时宜一并看。

5. **改写后的 `A_slow_live_hint…` 中，订阅里的 `Assert.Null(Transactions.Current)` 没有区分力。** LiveOnly 投影的失败会被 `LiveOnlyAsync` 吞成 Warning，所以这条断言失败时用例照样通过。Result 已经说明这一点，并由 N2 的 `InTransaction` 记录补上，所以「live 投影不持有业务事务」的保护在 N2，不在这个用例。这一条只是提醒：对账表把这个用例列为 LiveOnly 的证据时，不要把这一点算在它名下。

## 审查者的独立检查

- 读了 `git diff HEAD`（生产、测试、冲突清单）和新增测试文件全文。核对了 `GameplayJob.AssertNotOnExecutor`、`GameplayTimerWork`、`GameplayExecutorStoppedException`／`GameplaySelfWaitException` 的继承关系，以及执行器的 `RunContinuationsAsynchronously`。
- grep：生产代码中 `DrainOnceAsync` 的调用方；`TransactionalExecutionGate` 的持有者；`IGameplayQueue` 的持有者及其入队方式；Networking 测试宿主是否注册了事件分发；历史日志中崩溃栈根是否出现过。
- 复算全量加法、冲突总数加法；grep 复核日志信号计数；抽查 3 条耗时增量。
- 变异实验：把工作区 `Server/` 用 rsync 复制到 scratchpad（不含 bin/obj），只在副本上改：
  - X1「一批一个作业」：35／35 通过（非阻断项 1）；
  - X2「失败记录拆到另一个作业」（M2 的独立重做）：只有 N3 变红。
  - 副本文件用备份还原（sha1 `2a170a3e…`）。工作区的 `Server/src` 没有被改动，见下面的还原证明。
- Networking 隔离复跑（工作区 Release 构建，`--no-build`，连续 10 次；输出在 scratchpad `rev12/net-*.txt`）：
  - 10 次都没有主机崩溃，`RollbackExternal` 的出现次数为 0；
  - 9 次是 173 条（171 通过、2 跳过）；
  - 第 5 次有 2 条失败，都是 `SocketException: Connection refused`：`GameLoginIntegrationTests.PreCommitCancellationAfterRealTicketConsumptionRollsBackAndAllowsRetry` 和 `PartyCharacterizationTests.A_disconnect_in_town_announces_the_departure_to_the_other_player`。复跑期间另一个诊断者在 scratchpad 里并行跑 Application 测试。这两个用例都先探测空闲端口、再启动宿主绑定，形状与端口竞争或负载相符。审查者没有进一步取证，不把它们当作本单元的证据，也不归类；它们与崩溃栈无关；
  - 加上执行者的 8 次隔离、10 次负载，同一份构建已经连续 28 次没有再出现这次崩溃。这只说明它很少发生，不说明它与本单元无关。

## 还原证明

- 工作区的 `Server/src` 没有被审查者改动：
  - `DomainEventDispatcher.cs` 的 sha1 是 `2a170a3e5d66042710efd14b1b910c72b1dc5b6b`，`GameplayTimerWork.cs` 的是 `1654c8d6d6d47d006e14a8fee75a9a7ce1d379bd`；
  - `git diff HEAD --stat -- Server/src` 仍是 2 个文件，＋34 −4；
  - 与开始时相比，`git status --short` 只多了本审查文件。
- 变异只在 scratchpad 副本（`rev12/src`）上做，做完用备份还原（sha1 `2a170a3e…`）。
- 没有 commit，没有 stash，没有跑全量，没有启动持续运行的负载进程。

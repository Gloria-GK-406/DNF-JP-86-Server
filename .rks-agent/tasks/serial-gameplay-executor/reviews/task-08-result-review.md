---
kind: rks-review
target: tasks/TASK-08.md
scope: Task Unit result quick review（TASK-08 / T-004b1，Contract Revision 1，执行轮次：Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
---

# TASK-08 结果快速审查

## 审查对象与依据

- **对象**：`tasks/TASK-08.md`（契约 Revision 1、Detailed Plan、主控方裁定、Result）；工作区相对 `HEAD`（`422ec64d`）的未提交改动：
  - `git diff HEAD -- Server/src`：`CommandDispatchingExtensions.cs`、`Execution/GameplayQueue.cs`、`TouchSocketNetworkMessageSender.cs`（3 个文件，＋32 −6）；
  - 未跟踪：`Networking/Runtime/ExecutorSendDeadline.cs`（sha1 `12b88bbd…`）、3 个新测试文件、`verification/probe-tools/`（14 个文件）、`verification/logs/task-08-*.txt`（4 个）；
  - `plan.md` 第 107 行的措辞修正。
- **依据**：`plan.md`「执行器的生命周期」与「过渡期规则」3、4；`spec.md` RQ-008、附录 G VC-8；`reviews/plan-review-r2.md` R-4、R-5；TASK-04 Remaining N-2；TASK-06 §7 待办与 N-4；TASK-07 Remaining「注册容器执行器之后要复核的区域」与 N-2；`AGENTS.md`（分层、命令处理器规则）。
- **采用的准则**：reviewing-work 的 Task Unit 准则，加 Code 准则（限本单元的局部缺陷）。

## 结论

`approved`。在本单元范围内没有未解决的阻断缺陷。期限实现符合 R-5 的「放弃等待并抛出」，注册位置正确且只有一个执行器，Result 中的数字与日志一致，探针工具只改了路径。下面列出的非阻断项中，N-1 建议在提交前顺手补上，代价很小。

## 核对过程与证据

### 1. 期限语义（R-5、过渡期规则 3、4）

- **放弃等待**：`AwaitOnExecutorAsync` 用 `send.WaitAsync(deadline, ct)`，期限到点时不管 TouchSocket 是否响应令牌，都会交还控制权。两处真正的传输写入（`SendBatchAsync`、`SendAsync`）都经私有 `WriteAsync` 走这条路径；`SendExplicitAsync` 经 `SendAsync`。在 `Server/src/Dfo.Server.Networking` 中 grep `ISender`，另外只命中 `Outbound/OutboundTransport.cs:81`，那是 TASK-05 为 T-006 准备的组件，不在执行器上的直发路径上。
- **关闭的是哪个会话、在什么流下关闭**：超时回调关闭的是闭包捕获的 `session`（本次写入用的会话对象），不按 ID 重新查找。`StartDetachedClose` 在 `SuppressFlow` 下 `Task.Run`，返回值被丢弃、不被等待，符合 TASK-04 N-2 的两条要求。我做了**变异 1**：去掉 `SuppressFlow`，测试 f `A_detached_close_started_in_a_job_runs_off_the_executor_while_the_job_still_runs` 变红（作业仍在运行，所以继承来的令牌读作「在执行器上」）。这条测试能区分有没有 `SuppressFlow`。
- **写入自身的 `TimeoutException`**：用 `ReferenceEquals` 区分的逻辑是对的（`await` 在写入失败时抛出的是同一个异常对象；如果期限先到、写入随后才失败，比较的两个对象不同，仍判为期限）。**但没有测试覆盖它**，见 N-1。
- **取消**：`catch (OperationCanceledException) when (ct.IsCancellationRequested)` 观察被放弃的写入后原样抛出，不关闭连接；测试 d 覆盖了这条路径。超时抛出的是 `TimeoutException` 而不是 OCE。在 `Server/src` 中没有找到只捕获 `SocketException`、`IOException`、`ClientNotConnectedException` 这类窄类型、会漏掉 `TimeoutException` 的调用方，所以「与今天发送失败的路径相同」成立。
- **不在执行器上的路径**：`AwaitAsync` 直接返回 TouchSocket 的写入任务，调用点原来的 `ConfigureAwait` 写法也没变（`SendBatchAsync` 是 false，`SendAsync` 没有），所以线上字节与等待语义不变。唯一的差别是每帧多分配一个闭包，可以忽略。
- **被放弃的写入**：超时与取消两条路径都挂了 `OnlyOnFaulted` 观察续体。Result 说测试 e 在观察续体被注释掉时会变红（我没有复现这一项）。
- **真实 socket 用例**：断言 `sending.IsCompleted` 加上「紧接着入队的作业 1 秒内完成」，能证明执行器没有被冻结。如果 `SendAsync` 同步阻塞执行器线程，作业在 20 秒内都不会完成，第一条断言就会失败；Result 也记录了这一条的反证（让辅助永远走执行器外分支时，用例在 20 秒处失败）。我在当前代码上复跑 9 条期限测试，全部通过。

### 2. 注册位置与容器副作用

- `AddCommandDispatching`（Application.Commands）调用同一程序集里的 `AddGameplayExecution`（Application.Execution），没有新增项目引用，分层没有问题。`AddNetworkMessageDispatching` 与 `DomainEventDispatchingExtensions` 都经由 `AddCommandDispatching`，组合根经 `ApplicationModule.cs:817` 调到它。注册用的是 `TryAddSingleton`，重复调用也只有一个执行器。注册测试 1 断言选择器返回同一个实例。
- **释放**：只有被解析过的单例才会在容器释放时被释放。生产代码里没有任何地方解析执行器（grep 结果：`IGameplayQueue`／`GameplayExecutor` 只出现在 Abstractions/Execution 与 Application/Execution），所以 Host 停机时不受影响。在测试里，只有 `TestGameplay` 带容器的调用会解析执行器；`GameplayExecutor` 实现了 `IDisposable`，同步 `Dispose()` 也不会抛出。
- **`AddLogging()`**：它对 `ILoggerFactory`、`ILogger<>` 用 `TryAdd` 注册，对过滤选项用 `TryAddEnumerable`，所以对已有日志配置是幂等的。在 `src` 和 `tests` 中都没有找到「在 `AddCommandDispatching` 之后再用 `TryAdd` 注册日志」的写法（那种写法会被这次改动静默覆盖）。因此现有容器的日志行为不变，只是原本没有日志的容器现在能解析出 `ILogger<T>`。

### 3. 注册后果「四类都为 0」

- 抽查 `PartySettingsDomainTests.ConcurrentUpdates…`（`:306`）：两个区域都是 `TestGameplay.Run(() => …)`，**没有传容器**，各自在新建的执行器上；同步 `Barrier(3)` 不会落到同一个执行器。与复核表一致。
- 抽查 `WeaknessGoldRecoveryTests:163、:230` 与 `ItemSaleTests:230`：两个区域都传 `shop.Services`，注册后变为串行。断言是「一次成功、一次拒绝」与最终金额，串行时结果确定，复核表的说法成立。
- 抽查 `MailboxCoreIntegrationTests:382-384`：两边分别用 `fixture.Services` 与 `another.Services`（`Reopen()`），是两个容器，与复核表一致。
- **排空超时的核对**：我写了一个按用例耗时比较 TASK-07 与 TASK-08 全量日志的脚本，复现了 Result 的结论：增加 ≥ 9 秒的只有 `SealedTreasure`（6 → 21 s）、`PvfHellReward(85)`（5 → 16 s）、`SouthernDale`（4 → 14 s）三条。这个方法的盲区见 N-2。
- 全量日志：8 个项目逐项计数与 Result 的加法一致（50＋1694＋80＋475＋969＋147＋4431＋165＝8011）；失败只有 2 条 `DimensionDungeonTests`；「did not drain」出现 0 次；12 条新增测试都在日志里通过。

### 4. 没有入队、没有使用 `RunDetached`

- 复跑 grep：`IGameplayQueue|GameplayExecutor\b` 只命中 `Abstractions/Execution/GameplayQueue.cs`（1 处）、`Application/Execution/GameplayExecutor.cs`（2 处）、`Application/Execution/GameplayQueue.cs`（6 处）。`RunDetached` 只有 `GameplayJob.cs` 中的 4 个定义和 `GameplayJobSlot.cs` 中的 1 处 `cref`，没有使用。与 Result 一致。

### 5. 探针工具

- 把 14 个文件逐个与 scratchpad `t07/probe/` 的原件 diff：差异只在仓库根的推导（上溯 5 级，与目录深度相符）、`PROBE_WORK`／`PROBE_LOG_PREFIX` 环境变量、`run.sh` 改用 `$TOOLS/` 调用脚本这几处。`GameplayProbe.cs`、`classify.py`、`alist.py`、`p1bsplit.py`、`shared.py`、`snapcheck.py`、`wrapat.py` 与原件逐字节相同，`patch.py` 的挂点表也没有改动。工具目录中没有残留的绝对路径。`patch.py` 不挂 `Networking`，所以不需要修改匹配模式。
- 冒烟日志包含 apply（P1b 5 处、P2 173 处）、构建 0 警告 0 错误、revert 25 个文件、前后快照 sha1 相同且 `cmp` 逐字节相等、`git diff --stat` 与 `git status --short` 只剩本单元的改动，三项还原证明齐全。

### 6. 测试强度与数字

- 构建：我复跑了 `--no-incremental` 构建，0 警告 0 错误；定向测试 Networking 9 条、Application 16 条（注册测试 3 条加 `TestGameplayTests`）全部通过。
- 变异 1（去掉 `SuppressFlow`）：测试 f 变红，说明它能抓住这类改动。
- 变异 2（把 `ReferenceEquals` 过滤改成恒真）：9 条测试全部通过，说明这条区分没有测试守着，即 N-1。
- **恢复证明**：两次变异都从备份复原。`ExecutorSendDeadline.cs` 的 sha1 回到 `12b88bbd34bac31df93b0c6afc7ef94a7b296acc`，`TouchSocketNetworkMessageSender.cs` 的 sha1 始终是 `39c310a916888521798e7685422dcb60d08c3d78`，与变异前的快照相同。`git status --short` 与 `git diff --stat -- Server/src` 和审查开始时一致。之后重新构建了 Networking.Tests 并复跑，9 条全部通过。我没有 commit，也没有 stash。

### 7. `plan.md` 第 107 行

新措辞是「只有附录 G 允许的变化可以修改（T-004 起的 VC-8、T-006 的其余各项）」，与 R-4 的建议「T-004 的 VC-8、T-006 的其余各项」相同。「起」只是说明 VC-8 从 T-004 开始一直有效，这本来就成立（附录 G 的变化在 T-006 之后也保留）。要求没有放宽，也没有收紧。

## 阻断项

无。

## 非阻断项

- **N-1：写入自身抛出的 `TimeoutException` 与期限到点的区分没有测试。** 变异 2 把过滤条件改成恒真，9 条期限测试全部通过。后果是：今后有人简化这条 `catch`，被 TouchSocket 自己的 `TimeoutException` 弄失败的写入就会被当成期限到点，连接会被关闭并记一条误导性的 Warning，而测试不会发现。Result 的 Why 把它当作已实现的行为来写。**最小修正**：在 `ExecutorSendDeadlineTests` 里加一条：在执行器上，写入任务在期限内以一个 `TimeoutException` 实例失败，断言抛出的是**同一个对象**、`onTimeout` 没有被调用。建议在本单元提交前补上，N 相应改为 13。
- **N-2：用每条用例的耗时核对排空超时，覆盖不到夹具释放。** xUnit 不会把 class fixture 或 collection fixture 的释放时间算进某条用例的耗时，所以在夹具 `Dispose` 中发生的 10 秒排空不会出现在逐条比较里。后果只是变慢，不影响正确性，而且全量总时长没有异常信号。T-004b2 起执行器上开始有真实作业，这个盲区才变得重要。建议后续单元改用更直接的信号，例如在测试支持代码里让容器执行器的日志可见，或统计 `abandoned` 次数。
- **N-3：非 macOS 平台的残余已有现成的验证渠道。** `.github/workflows/validate.yml` 的矩阵包含 `ubuntu-24.04` 与 `windows-2022`，两边都跑全量 `dotnet test`，这条真实 socket 用例会自动在 Linux 和 Windows 上运行。如果某个平台上写入是同步阻塞的，用例会在 20 秒处以「the job is still waiting」失败，不会挂起。建议主控方明确：PR 的 CI 在这两个平台上通过，就是本单元「写入真异步」前提的跨平台证据；如果失败，按 §3 的停止条件处理。不必另做手工观察。
- **N-4：超时回调里，日志异常会顶替 `TimeoutException`。** 回调先启动关闭、再记 Warning。如果日志提供程序抛出异常，调用方收到的是日志的异常，而不是 `TimeoutException`；关闭已经启动，不影响 R-5。这只是一种极端情形，可以记一笔，不需要处理。

## 未做的事

- 没有运行全量测试（按委托要求）；全量结论来自 `logs/task-08-build-test.txt` 与我的逐项核对。
- 没有复现测试 e 的「去掉观察续体会变红」，也没有复现真实 socket 用例的反证，这两项采用 Result 的陈述。

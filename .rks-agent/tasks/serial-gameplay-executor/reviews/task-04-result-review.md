---
target: tasks/TASK-04.md
verdict: approved
---

> 文件级 verdict 为最新一轮（Round 3）的结论。第 1 轮的 `changes_required` 保留在下文作为记录。

# TASK-04 Result 独立快速审查

```yaml
kind: rks-review
verdict: changes_required
```

- 审查对象：`tasks/TASK-04.md`（Contract Revision 1；Result 第 1 轮）；未跟踪的 `Server/src/Dfo.Server.Abstractions/Execution/`（2 个文件）、`Server/src/Dfo.Server.Application/Execution/`（3 个文件）、`Server/tests/Dfo.Server.Application.Tests/Execution/`（8 个文件）；日志 `verification/logs/task-04-build-test.txt`、`task-04-execution-x20.txt`。工作区 HEAD `5075a283`。
- 被审文件的 sha1 记在 scratchpad `before.sha1`，例如 `GameplayExecutor.cs` = `e038978c…`、`GameplayJob.cs` = `d9dd4277…`、`GameplayCallChain.cs` = `10570684…`。
- 采用的准则：Task Unit 快速审查，加上 Code 准则。依据：`spec.md` Definitions、RQ-001～RQ-009、RQ-050～RQ-054、RQ-072、附录 A 末尾、AC-001/002/004/016；`plan.md`「共享决定」中的生命周期不留旁路、过渡期规则 3、发件箱归属；`AGENTS.md` 分层与后缀约定。
- 复测：
  - 隔离运行 `--filter "FullyQualifiedName~.Execution."`，37/37 通过；
  - 写了一个临时探针文件（5 条），跑完已删除；
  - 做了 4 处变异，每处跑完都从备份恢复；
  - 恢复后重新构建并运行，37/37 通过；
  - 三个目录全部文件的 sha1 与审查前逐一相同（`diff before.sha1 after.sha1` 无输出）；
  - `git status --short` 与审查前相同，仍是 6 项未跟踪；
  - blame 运行产生的 `TestResults/`（gitignored，空目录）已删除。
- 未运行全量测试。

## 结论

主体语义成立：
- 单读者通道配合「写入后 CAS 启动」，首次入队不存在竞态。`StopAsync` 用 `TryComplete` 截断入队，截断前写入成功的作业都会被排空，两者之间没有丢作业的窗口。
- 取消使用 Pending、Running、Cancelled 三态 CAS，结束方唯一。令牌先失效，后设置完成结果。消费循环在 `SuppressFlow` 下启动，令牌只在 `Job.RunAsync` 这个 async 方法里设置。
- 深度判断为 `> 32`，链的祖先序列截断到 33 跳。`Child` 继承根 ID 和来源连接，与 RQ-050、RQ-053(a) 一致。
- 不接线已复核：grep 零命中，`git status` 只有三个新目录。

但有两处会以「执行器整体静默挂死」或「调用方永久悬挂」的形式出现，正是后续 Task 最怕的那类缺陷，必须在提交前修正。

## 阻断项

### B-1　消费循环对作业体以外的异常不设防：一条坏作业让执行器静默停摆，之后所有调用方永久悬挂（违反 RQ-004）

**证据：** `GameplayExecutor.cs` 的 `Job<T>.RunAsync` 中，以下三处都在 `try` 之外，或者位于 catch 块内：
- 第 203 行的 `work.Chain.ForCommand(work.CommandType)`：对空白类型名抛 `ArgumentException`；
- 第 204～210 行的 Trace 日志；
- 第 244 行 catch 里的 `LogError`。

`ConsumeAsync`（第 130～151 行）没有逐作业的兜底，异常会直接穿出循环。`finally` 只设置 `completion`，不会 `TryComplete` 写端。结果是：
- 这条作业的 TCS 永远不会完成；
- 已在队列中的作业永远不会执行；
- 之后的 `RunAsync` 仍能 `TryWrite` 成功（`started` 已经是 1，不会再启动循环），然后永久等待；
- 整个过程没有任何日志。

**复现：** 临时探针，已删除。
- `CommandType = " "` 时，该调用方与后续调用方 1 秒后仍未完成，`Completion.IsCompleted == true`；
- 用一个在 Trace 级别抛异常的 logger，现象相同。

两条探针都通过，即缺陷存在。`RunAsync` 在入队时只校验了链（第 33 行），没有校验 `CommandType`。

**后果：** T-004 之后，所有入口都汇到这一个循环上。一个空白类型名，或者一个日志 provider 的异常（MEL 会把 provider 异常聚合后抛出），就会冻结全部玩法，而且不报错。这正是 RQ-004 要排除的「一条失败影响后续」，而且是静默的形式。

**最小修正：**
1. `RunAsync` 入队前用 `ThrowIfNullOrWhiteSpace` 校验 `CommandType`，与链校验并列，返回失败的 ValueTask。
2. 把 `ForCommand`、开始日志、`Enter` 移进 `try`，并保证 catch 中的日志异常不会阻止 `TrySetException` 和 `token.Exit()`。
3. `ConsumeAsync` 加最后一道兜底：如果循环异常退出，`TryComplete(ex)` 写端，按 B-2 的方式结束剩余作业，并记 `Critical`。

各补一条测试：空白类型名在入队时被拒，执行器继续服务；logger 抛异常时作业失败，执行器继续服务。

### B-2　释放超时后，排在卡死作业之后的调用方仍然悬挂；Result 的描述与实际行为不符

**证据：**
- `DisposeAsync`（第 99～113 行）超时后只调用了 `abandon.Cancel()`；
- 剩余作业要等消费循环下一次 `TryRead` 时，才会在第 138～141 行被 `Abandon()`；
- 但循环此时正卡在第 143 行 `await job.RunAsync(...)` 里，也就是那个不会结束的作业上。

既有用例 `Disposal_that_cannot_drain_in_time_abandons_the_queued_work_and_returns`（第 154～157 行）先 `release.SetResult()`，再断言排队作业收到停止异常，所以它测的恰恰是「卡死作业最终会结束」的情形。

临时探针 `Probe_Disposal_timeout_leaves_queued_callers_pending…` 不释放卡死作业：`DisposeAsync` 返回 1.5 秒后，排队作业仍未完成（探针通过，即缺陷存在）。

Result 写的是「超时后，队列中剩余的作业以停止异常结束」，而实际只有在卡死作业自己结束之后才成立。

**后果：** 超时的唯一用途，是让卡死场景不悬挂；卡死作业本身无法打断，这一点可以接受。但排在后面的调用方属于入站 handler（等待以实现 RQ-005）、`BackgroundService` 的 tick、事件分发器，它们会一直挂着：
- 生产停机时，会拖到宿主的关停超时；
- T-004 改写测试时，任何一个死锁用例，例如 N-2 那种绕过检测的自我等待，都会表现为测试进程挂起，而不是失败加诊断。

**最小修正（任选其一，首选第 1 种）：**
1. 超时后由 `DisposeAsync` 自己排空通道，对每个作业调用 `Abandon()`。`Abandon` 已经是 CAS 安全的，可以与循环并发；但通道要改为 `SingleReader = false`，或者另用一个线程安全的待处理集合。补一条测试：卡死作业永不释放，排队作业在 `DisposeAsync` 返回后的有限时间内收到 `GameplayExecutorStoppedException`。
2. 如果不改代码，就把 Result 的描述改成实际行为，并在 Remaining 中把「卡死时排队调用方悬挂」列为 T-004 停机编排必须处理的已知限制。

## 非阻断项

### N-1　自我等待异常可以被作业吞掉，CMD 以成功结束

RQ-003 要求「该 CMD 以失败结束」，现在靠的是异常传播。

临时探针：作业内 `try { await executor.RunAsync(...) } catch (Exception) { }` 之后返回 7，调用方拿到 7（探针通过）。`Server/src` 中有 103 个文件写了 `catch (Exception`，其中包括 `SelectCharacterHandler` 的补偿、`OnlineUserConnectionObserver` 等在 T-004 会搬上执行器的路径。T-006 的 `AssertNotOnExecutor` 也会落在这些 catch 包着的发送路径里。`Error` 日志仍然会记，所以不算完全静默，但「CMD 失败」得不到保证。

**建议：** 在 T-004 计划时决定是否给令牌加「程序错误」黏滞标记：`RunAsync` 或 `AssertNotOnExecutor` 判定违规时记到令牌上，执行器在作业结束时无论作业返回什么，都以该异常失败。

### N-2　令牌判定的固有盲区（计划已接受，但 T-004 要据此列分离点）

两个方向各有一处：

- **漏报：** 作业在 `SuppressFlow` 下用 `Task.Run` 分出工作，在分出的工作里入队，作业再 await 它。这是真死锁，但检测不到。临时探针：外层作业 1 秒后仍未完成。
- **误判：** 不加 `SuppressFlow` 分出的工作，在作业仍在运行期间读到的是「在执行器上」（`Work_detached_with_flow…` 断言了 `During == true`）。这段代码实际上与作业并发运行，于是：
  - T-004 的门断言会把它当成执行器上的作业而放行，即漏报；
  - 它如果入队，会被误判为自我等待。

**建议：** T-004 逐个列出分离点时，同时确认两条：没有任何分离点被作业 await；所有分离点都加了 `SuppressFlow`。

### N-3　测试强度的缺口（变异实验）

做了 4 处变异，实验后均已恢复：

| 变异 | 结果 |
|---|---|
| 成功路径把 `token.Exit()` 挪到 `TrySetResult` 之后，并加 50 ms 窗口 | 3 条变红（2 条令牌用例、顺序用例），能抓到 |
| `Depth > Limit` 改为 `>=` | `Work_at_the_limit_runs…` 变红，能抓到 |
| 删除失败路径 catch 中的 `token.Exit()` | **37/37 仍然通过**，没有用例覆盖失败路径和取消路径的令牌失效 |
| 去掉 `RunAsync` 的自我等待检查 | `GameplayExecutorSelfWaitTests` **挂起**，靠 `--blame-hang-timeout 30s` 才中止；没有 blame 时会无限挂起 |

**建议：**
- 给「作业抛异常结束后，捕获的上下文或带流分出的工作读到不在执行器上」补一条用例；
- 两条自我等待用例的外层 await 加上 `.WaitAsync(ExecutionTest.Patience)`，让违规表现为失败，而不是挂起。

### N-4　Result 中全量测试的各项目计数有 4 个标错，和数正确

实际计数：`dotnet test --list-tests` 得到 HttpApi.Tests 50、Pvf.Tests 80。Host.Tests 与 Protocol.Tests 按测试特性数（120 和 1129，含 Theory 展开）与排除法，分别是 165 和 1694。日志里各项目汇总行没有写项目名，产出方按完成顺序配错了名字。

正确的加法：Pvf 80 ＋ Host 165 ＋ HttpApi 50 ＋ Persistence 475 ＋ Infrastructure 969 ＋ Networking 107 ＋ Protocol 1694 ＋ Application 4379 ＝ 7919。

已核对、无误的部分：
- 通过数：50＋80＋1694＋475＋967＋105＋165＋4361 ＝ 7897；
- 失败 2 条，都是 `DimensionDungeonTests`（日志第 6172 行、第 8725 行）；
- 跳过 20 条：Infrastructure 2、Networking 2、Application 16；
- 表征测试通过 8 条、跳过 2 条；
- `.Execution.` 下 37 条，20 次运行全部 37/37。

**建议：** 改正 Result 中的四个项目名。

### N-5　其余小项

- `DisposeAsync` 和 `Dispose` 没有调用 `AssertNotOnExecutor`（`StopAsync` 调用了）。在作业内释放执行器，会等满 `DisposeTimeout` 再放弃。
- 入队被拒时，`CoalescingGameplaySource.TryRun` 仍返回 `Enqueued`，失败只体现在 `Completion` 里；用例 `A_refused_enqueue_frees_the_source` 把这一点钉成了契约。T-004 的调用方必须读 `Completion`，名字容易误导。
- `NewRoot(source, connection)` 对任何来源都接受连接参数，而 RQ-053(a) 只关闭「根来源是客户端连接」的那个连接。建议 T-007 收紧，或者写明约束。

### N-6　分层与命名

- 放置合乎 `AGENTS.md`：契约与值类型放在 Abstractions，实现放在 Application，没有新增项目引用。
- `IGameplayQueue` 不用 `*DomainService` 后缀，理由成立：`CommandHandlerArchitectureTests` 按后缀放行，入队点因此天然被拒，符合 RQ-070(b)。但 `AGENTS.md` 的「接口后缀标明类别」表里没有这一类，文档阶段（T-009）应补一行「入队点」。
- **对「主控方注意 1」的意见：** 建议选方案 (a)，把令牌持有者（AsyncLocal、`IsOnExecutor`、`Current`、`AssertNotOnExecutor`）上提到 `Abstractions/Execution`，`Enter` 保持 internal。`Dfo.Server.Abstractions.csproj` 已经有对 Persistence 的 `InternalsVisibleTo` 先例，再加 Application 即可。
  - **方案 (b) 解决不了规则 3 点名的场景。** `SuppressFlow` 只影响上下文的捕获，不影响同步内联代码的读取。TouchSocket 在发送失败时同步内联触发关闭回调，这时回调仍然读到令牌。
  - **(a) 中「清除」要做成作用域，不能只置空。** 在同步内联回调里直接把 AsyncLocal 置空，改动会回流到调用方，也就是执行器上作业的上下文，于是该作业余下部分都被判为「不在执行器上」。所以「清除」应做成可恢复的作用域（`using` 结束时还原），或者在 async 方法边界内执行。

## 处置建议

- 修正 B-1、B-2 并补上对应用例之后，申请复审。
- N-3 的两处测试补强和 N-4 的计数改正改动很小，建议一并完成。
- N-1、N-2、N-6 交给 T-004 计划时处置。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

- 审查对象：`tasks/TASK-04.md` Result 第 2 轮，包括末尾的「相对第 1 轮的变化」，以及当前工作区。被审文件的 sha1 记在 scratchpad `r2-before.sha1`（15 个文件），例如：
  - `GameplayExecutor.cs` ＝ `fe81cf14…`
  - `Abstractions/Execution/GameplayJob.cs` ＝ `83888c64…`
  - `Application/Execution/GameplayQueue.cs` ＝ `278cd284…`
  - 已跟踪文件中唯一的改动：`Dfo.Server.Abstractions.csproj` 增加一行 `InternalsVisibleTo`。
- 复测：
  - 隔离运行 `.Execution.`，54/54 通过。
  - 临时探针文件 `ZzReviewProbe2Tests.cs`（5 条）运行后已删除。
  - 2 处变异，运行后都从备份恢复。
  - 恢复后 15 个文件的 sha1 与审查前逐一相同，`diff` 无输出；恢复后重新构建并运行，54/54 通过。
  - `git status` 与审查前相同。
  - `Application.Tests/TestResults/` 下有 10 个空目录，mtime 为 00:22～00:23，早于本轮开始，属于产出方的 blame 运行。本轮未动。
- 日志核对，全部无误：
  - 20 次运行均为 54/54。
  - 全量：1694＋50＋80＋475＋969＋107＋4396＋165 ＝ 7936；通过 1694＋50＋80＋475＋967＋105＋4378＋165 ＝ 7914；失败 2，均为 `DimensionDungeonTests`；跳过 20。
  - 表征测试通过 8 条、跳过 2 条；`.Execution.` 共 54 条；0 警告 0 错误。
  - Result 中各项目名与计数的映射已改正，与 `--list-tests` 一致。
- 不接线：重新 grep，零命中。

### 第 1 轮阻断项：均已解决

**B-1 已解决。**
- `Job<T>.RunAsync` 中，作业体以外可能抛出的点都已收进 `try` 或日志防护：`ForCommand`、开始日志、`Enter` 和作业体在 `try` 内；`Exit` 在 `finally` 中；结束、失败、取消三种日志都经过吞掉异常的 `Log`/`Enabled`。
- 其余几处不会抛出：
  - 取消注册的 `Dispose`：回调只做 `TrySetCanceled`，结果 TCS 用的是 `RunContinuationsAsynchronously`，不会内联运行续延；
  - `TrySet*`、`Abandon` 的 CAS；
  - 失败日志参数中的 `chain.Describe()`：参数在 `Log` 的 `try` 之外求值，但它只做字符串拼接。
- 入队时校验 CMD 类型；`Detach`、`Enter`、`Exit` 都只是字段赋值。
- **循环兜底未直测，可以接受，而且存在可注入的测法。** 探针用反射取出私有字段 `queue`，在 `Writer` 上调用 `TryComplete(new InvalidOperationException())`：
  - 当前卡住的作业放行后，`WaitToReadAsync` 抛出，进入 `catch`；
  - 结果：记了 `Critical`，`Completion` 完成，之后的 `RunAsync` 收到 `GameplayExecutorStoppedException`。兜底按设计工作。
  - 如果想把它固化成用例，可以照此写一条反射测试，不需要在生产代码里加钩子。
  - 残余：假如异常出自 `await job.RunAsync()` 内部，正在执行的那个作业处于 Running 状态，`Abandon` 不会结束它。按现在的结构，这条路径不可达，只记作残余。

**B-2 已解决，FIFO 与单消费者语义不受影响。**
- 释放方只在超时之后排空，排空前先写 `abandoned = true`（volatile），而且释放方从不执行作业，执行者仍然只有循环一个。
- 通道内部有锁，各次读取之间存在先后关系：释放方写 `abandoned` 先于它自己的第一次 `TryRead`；循环如果在那次读取之后才取到某个作业，一定能看到 `abandoned == true`，于是放弃该作业。
- 因此，超时之后循环还能执行的，只有它在释放方首次读取**之前**已经取出的那一个作业，而它比所有被放弃的作业都早，不会出现「后入队的先执行」。
- 两个读者都会对同一作业调用 `Abandon`，`Abandon` 有 CAS 保护，不会重复结束。
- 用例 `Disposal_that_cannot_drain_in_time…` 已改为全程不释放卡死作业；第 2 轮 Result 也记载了「去掉释放方排空」的变异会让它变红。

### 第 2 轮新增内容的检查

**黏滞程序错误：在单执行器范围内语义正确。** 只有 `RunAsync`（深度超限、自我等待）和 `AssertNotOnExecutor` 两处会调用 `Fault`，而且 `Detach` 作用域内的代码读不到令牌，所以不会标错。今天调用 `AssertNotOnExecutor` 的只有 `StopAsync` 和 `DisposeAsync`。程序错误优先于取消，这是合理的取舍。

误伤只有一种形态，见 R2-3：令牌是全局的，不区分执行器。

**`Detach` 跨 await：安全，不必限定为同步作用域。** 探针验证了两点：
- 在 async 作业里写 `using (Detach()) { await Task.Yield(); }`，作用域内 await 前后都读到「不在执行器上」，出了作用域恢复为「在」；
- async 辅助方法里调用 `Detach` 而不 `Dispose`，它的修改不会回流到作业，因为 async 方法边界会还原上下文。

真正的风险在别处，见 R2-2。

**`TryRun` 的 `Rejected` 判定：没有竞态。**
- 拒绝一定在调用返回前已经完成，而且作业体从未开始，所以判为 `Rejected`。
- 作业体一旦开始就先置 `Started`（volatile）；Task 完成本身带内存屏障，判定线程能看到这个写入，所以作业已开始的情况不会被误判。
- 返回之后才发生的取消或放弃，判为 `Enqueued`，失败经 `Completion` 交出；返回之前就已经发生的取消或放弃，判为 `Rejected` 并立即释放这个源。两种情况下都不会再有 CMD 执行，释放是对的。
- 测试强度不足，见 R2-4。

### 非阻断项

- **R2-1：IVT 让 Application 能看到 Abstractions 的全部 internal，超出了令牌的需要。**
  - `Abstractions` 中现有的 internal 除了 Execution 之外，还有 `ItemLifecycleScope.Issue`、`IsPending`、`IsForeign`、`AcceptAsync`（注释写明是存储实现的钩子，原本只开放给 Persistence），以及 `ExperienceProgressionRules.Decide`。
  - 今天 Application 没有误用它们，但「只有存储能签发 detached 记录」这条边界从此不再由编译器强制。T-004 到 T-008 会大面积改写 Application，这条边界会持续暴露在风险之下。
  - **建议（任选其一）：**
    - 改用不需要 IVT 的写法，例如在 Abstractions 放一个 `public abstract class GameplayJobHost`，用 `protected static Enter/Exit`，执行器继承它；令牌类型可以是不透明的 public sealed 类，没有公开成员。这样就能撤掉 IVT。
    - 如果保留 IVT，在 T-009 的结构判据里加一条：`Dfo.Server.Application` 不得引用 `ItemLifecycleScope` 的 internal 成员和 `ExperienceProgressionRules`。
- **R2-2：`Detach()` 是公开的逃生口，T-004 接线时要限定调用方。**
  - 探针证实两点：
    - 作业内写 `using (Detach())` 后入队并 await，会形成真死锁，而且检测不到；
    - 在同步方法里调用 `Detach()` 却忘了 `Dispose`，修改会泄漏到作业，作业余下部分都读到「不在执行器上」。
  - 任何层都能调用它，于是它能静默绕过门断言和自我等待检测，这正是本 Task 要防的那类旁路。
  - **建议：** T-004 加一条结构判据，只允许 Networking 的传输回调入口和关连接器调用 `Detach`；或者把它改成委托形式（`GameplayJob.RunDetached(...)`），根本不给忘记 `Dispose` 的机会。
  - 另外，经 `Detach` 进入的同步内联回调如果 await 一条刚入队的 CMD，同样是检测不到的死锁，只能靠过渡期的 2 秒发送期限兜住。这一点应写进 T-004 的分离点核对。
- **R2-3：令牌不区分执行器，跨执行器的合法调用会被判为自我等待，并且黏滞失败。**
  - 探针：作业运行在执行器 E1 上，对另一个实例 E2 调用 `DisposeAsync`，即使吞掉异常，作业仍以 `GameplaySelfWaitException` 失败；对 E2 调用 `RunAsync` 同样被拒。
  - 生产环境只有一个执行器（RQ-001），所以今天不成立。但 T-004 计划把测试体作为执行器作业来运行，测试体里如果自己 `BuildServiceProvider().AddGameplayExecution()` 再释放，就会撞上这一条；日后分片也一样。
  - **建议：** T-004 设计测试辅助时二选一：
    - 测试辅助禁止嵌套执行器，同一测试只用一个执行器实例；
    - 令牌带上执行器身份，自我等待只判同一实例。RQ-003 今天只有一个执行器，两种做法都不违背它。
- **R2-4：两处新增判据的强度不足。**
  - 去掉 `TryRun` 中的 `!start.Started`，54 条仍全部通过。那条「连续 200 次跑完并失败」的用例依赖竞态窗口，实际上走不到「返回前已经跑完」的分支，所以 Result 说它防住了误判，并不成立。
    - **建议：** 用一个假的 `IGameplayQueue`，同步执行作业体并返回失败的 ValueTask，确定性地断言结果是 `Enqueued`。
  - 去掉入队处的 CMD 类型校验，只有 `null` 那一组变红；`""` 和 `" "` 两组仍通过，因为 `try` 里的 `ForCommand` 同样会抛 `ArgumentException`，测试分不出「入队被拒」和「执行时失败」。两种情况下都不会挂起，B-1 的目的已经达到，所以只记为强度不足。
- **R2-5：交给 T-004 的入口说明需要补全。** Remaining 第 2 条要补上 R2-2 与 R2-3 的约束。

### 结论

第 1 轮两个阻断项都已解决，并经独立复现证实。N-1、N-3、N-4、N-5、N-6 的处置与主控方的裁定一致。本轮没有新的阻断缺陷，**approved**。

建议在 TASK-04 提交前或 T-004 计划时完成：
- R2-1：IVT 收窄，或加判据；
- R2-4：确定性的 `Rejected` 用例；
- 把 R2-2、R2-3 写进 T-004 的约束。

## Round 3

```yaml
kind: rks-review
verdict: approved
```

- 范围：只看 Result 第 3 轮的改动。
- 被审文件的 sha1 记在 scratchpad `r3-before.sha1`（14 个文件），例如：
  - `Abstractions/Execution/GameplayJob.cs` ＝ `f49d439d…`
  - `GameplayExecutor.cs` ＝ `4b633f18…`
- 已跟踪文件：`git diff --stat` 为空，`Dfo.Server.Abstractions.csproj` 已恢复原样，其中的 IVT 名单不含 Application。
- 复测：
  - 隔离运行 `.Execution.`，59/59 通过。
  - 临时探针 `ZzReviewProbe3Tests.cs`（2 条）运行后已删除。
  - 做了 1 处变异：把自我等待判定从 `JobRunningHere` 改成 `JobRunningAnywhere`，`A_job_on_one_executor_may_enqueue_to_and_dispose_another` 变红。恢复后 59/59 通过。
  - 14 个文件的 sha1 与审查前逐一相同。
- 日志核对：
  - 20 次运行均为 59/59。
  - 全量：1694＋50＋80＋475＋969＋107＋4401＋165 ＝ 7941 ＝ 7882 ＋ 59。
  - 失败 2 条，都是 `DimensionDungeonTests`。

### 检查结论

**1. 基类方案比 IVT 收紧了，但仍有两条路径能动令牌，都不阻断。**

- Application 不再能看到 Abstractions 的 internal，`ItemLifecycleScope` 与 `ExperienceProgressionRules` 重新只对 Persistence 开放。
- 令牌类型对外不透明：没有公开成员，构造器是 internal。
- **路径 (a)：任何程序集都能从 `GameplayJobHost` 派生。** 探针里写了一个派生类 `RogueHost`：
  - 调用 `ExitJob(JobRunningAnywhere)`，能让**真实执行器上正在运行的作业**变成「不在执行器上」，因为 `ExitJob` 是 static，不校验令牌属于谁；
  - 调用 `EnterJob(...)`，能让一段普通代码读到「在执行器上」。
  - 今天 `Server/src` 中除 `GameplayExecutor` 外没有派生类，所以不阻断。
  - **建议现在就补，代价很小：**
    - 把 `ExitJob`、`Fault`、`ProgramErrorOf` 改成实例方法，并断言 `token.Owner == this`；
    - 加一条反射判据：已加载程序集中只有 `GameplayExecutor` 派生自 `GameplayJobHost`。如果留到 T-009，就写进 Remaining。
- **路径 (b)：Persistence（生产代码）原有的 IVT 能直接访问 `GameplayJob.Running`。** 这是既有授权带来的，Persistence 没有任何理由去碰它，记为残余。

**2. `RunDetached` / `RunDetachedAsync`：恢复在所有路径都成立。** 探针在一个作业内逐项断言了以下情形：
- `ValueTask` 同步完成；
- 委托在返回 `ValueTask` 之前同步抛出；
- 委托 await 后在线程池线程上抛出；
- 不 await、直接分离；
- 同步重载里启动的异步工作。

结果是：委托内部读到「不在执行器上」，委托启动的工作也不继承令牌；之后作业读到的都是「在执行器上」。

原因：
- 同步重载靠 `finally` 还原；
- 异步重载即使没有 `finally`，async 方法边界也会还原调用方的上下文，`finally` 是冗余但无害的保险。

委托形式消除了「忘记 Dispose 导致泄漏」（R2-2 的第二点）。但 R2-2 的第一点仍然成立：在 detached 状态下入队并 await，会形成检测不到的死锁。这一点已列入 Remaining 交给 T-004，结论不变。

**3. 按执行器区分：同一执行器没有新的漏判，跨执行器有新的残余。**

- **同一执行器：** `GameplayQueue → GameplayShardSelector.Select → 同一个 GameplayExecutor 实例 → RunAsync`，`CoalescingGameplaySource` 也经这个队列。最终都落在同一个实例上，比较的是令牌的 `Owner` 与 `this`，所以从任何入口入队都会被拒。`StopAsync` 与 `DisposeAsync` 也按实例判定。深度超限仍会把错误黏滞到任何执行器上的作业，这是合理的。
- **跨执行器：** 例如 E1 上的作业等 E2，E2 上的作业又等 E1，形成环，这种死锁现在不再被检测。
  - 生产只有一个执行器（RQ-001），所以今天不成立；
  - 但 RQ-003 字面上说的是「不得向执行器入队后等待」，并不区分执行器；
  - 日后真要分片时，必须重新处理跨分片的等待。建议在 Remaining 里加一句。
- 公开的 `GameplayJob.AssertNotOnExecutor` 仍然是「在任一执行器上」即拒，T-006 的网络等待检查用它，口径正确。

**4. 新用例的强度足够。**
- Rejected 改用假队列做确定性用例，修正了 R2-4 的第一点。
- 新增了入队处校验和循环兜底（反射）两条用例。
- 跨执行器的用例能抓住我做的那处变异。
- 第 3 轮 Result 记载的 12 处变异，本轮没有逐一复跑，只抽查了上面那 1 处。

### 结论

本轮没有阻断缺陷，**approved**。建议在 TASK-04 提交前完成第 1 点的令牌所有权校验与派生判据；如果不做，就把第 1 点和第 3 点的残余写进 Remaining。

# TASK-12 — T-004b5：事件消费迁入执行器（Reliable、LiveOnly、失败重试、启动恢复）

基准提交 `648fbc60`（TASK-11 之后）。边界提纲见 `TASK-08.md` 文末「T-004b 拆分」§2 TASK-12 一行与 §1 的跨单元规则。TASK-11 的 Result、主控方裁定第 7 条及其补充（定时源以 `CancellationToken.None` 入队、作业体拿生产者令牌、取消带出作业）、`GameplayTimerWork`，TASK-10 §6 的停机形状，以及 TASK-08 Result §4 复核表中分给本单元的 `DomainEventDispatchTests.A_slow_live_group…`，是本单元的输入。本文件不重抄这些内容，只写本单元的做法。

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - **一条 delivery 的消费是一条事件 CMD**（RQ-041）：`DomainEventDispatcher` 仍是生产者。组循环与 `DrainOnceAsync` 在执行器外读取到期 delivery（只读 SQLite，附录 B），然后对每一条 delivery 入队一个作业并等待它完成。作业体是今天 `ProcessAsync` 的全部内容：
    - Reliable：取准入门 → 开事务 → `TryBegin` → 订阅处理 → `Complete` → 提交；
    - LiveOnly：先提交消费决定，再在同一作业里投影（RQ-042）；
    - 失败：回滚之后，另起事务记录重试或终态，也在同一作业里。
  - **来源与 CMD 类型**（过渡期标注，§3）：事实由本进程记录的，来源 `Event`，CMD 类型 `event:<订阅 ID>`；事实由之前的进程记录的（遗留 delivery），来源 `StartupRecovery`，CMD 类型 `startup:event/<订阅 ID>`。一律新根，父链与重试继承由 T-007 贯通。
  - **与 TASK-11 的生产者形状统一**（§4）：经 `GameplayTimerWork.RunAsync` 入队。以 `None` 入队，作业体拿生产者令牌，取消带出作业。不用合并源，理由见 §4。
  - **过渡期保留**（§5）：组门 `groupGates` 与数据库准入门 `TransactionalExecutionGate` 原样保留，删除归 T-005。`DrainOnceAsync` 在执行器上被调用时立即报程序错误，不在组门上死锁。
  - **停机**（§6）：组循环收到停止令牌后不再入队；已入队的事件 CMD 照常开始执行，作业体在第一处检查令牌时退出，delivery 留待下次启动。不新增排空接口。
  - **冲突用例**：`UsageQuotaTests:117`（第 1 个事务块）按 §9.3 处置；新判定的 `DomainEventDispatchTests.A_slow_live_group…` 按 RQ-043 改写（§9.2）。
  - 覆盖：
    - 附录 A 第 8 行（52 个订阅）；第 11 行的事件部分（遗留 delivery 扫描）；
    - RQ-014，以及 RQ-040～RQ-043 的执行位置；
    - RQ-050、RQ-051 的过渡期标注：来源种类正确，父链留给 T-007；
    - AC-015 中可在本单元证明的部分（§9.1 映射表）；AC-006 中附录 A 第 8 行与第 11 行事件部分的对账。
- Owned boundary:
  - 生产代码：
    - `Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs`（`DomainEventDispatcher`；同文件里的 `DomainEventHostedService` 预计不用改）；
    - `Dfo.Server.Application/Execution/GameplayTimerWork.cs`：只改类型摘要注释，说明它也承载事件消费（待裁定第 4 条）；
    - §9.4 第 (iv) 类确需的缺陷修正，限于上述文件。
  - **不改**：`EventService`、`DomainEventDispatchingExtensions`、`DomainEventRegistry`、Persistence 的事件存储、各订阅处理器、`GameplayExecutor`、`CoalescingGameplaySource`、`GameplayExecutorLifetime`、`TransactionalExecutionGate`、`ProtocolModule`、`ApplicationModule`。
  - 测试：
    - 新增：`Application.Tests/DomainEvents/DomainEventDispatchTests.Executor.cs`（原类改为 `partial`，复用 `Fixture`／`Probe`）；
    - `DomainEventDispatchTests.cs`：夹具开关（记录型队列、记录型日志、可放行的 live 阻塞）与 §9.2 改写；
    - `UsageQuotas/UsageQuotaTests.cs`：§9.3；
    - 按 §9.4 处置的其他既有测试文件。
  - 文档：
    - `verification/` 下的 `logs/task-12-*.txt`；
    - `task-07-conflicts.md` 追加 §10：`:117` 与 `A_slow_live_group…` 的去向，以及本单元新判定的冲突。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘到 `logs/task-12-build-test.txt`。
     - 总数 ＝ 8101（TASK-11 Result Checks 2）＋ N（新增）－ D（删除）。逐项目与 TASK-11 对照，写出加法。
     - 失败只能是那 2 条 `DimensionDungeonTests`。跳过 20 条。表征测试 8 通过、2 跳过。没有挂起转储。
     - **日志信号计数**：
       - `direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`；
       - 以 `Gameplay event:` 或 `Gameplay startup:event/` 开头的 `failed`；
       - `Domain event dispatch group … could not persist or scan`；
       - §6 新增的「执行器已停止，事件组循环结束」日志。
       - 每一项非零的都要逐条说明来源。多数事件测试容器没有日志提供程序，所以计数为 0 不等于没有发生；N3、N6、N7 用记录型日志直接断言。
     - **耗时比对**：与 `logs/task-11-build-test.txt` 逐条比，列出多出 ≥ 9 秒的用例并逐条判定：负载波动、释放时的 10 秒排空、作业里的 SQLite 忙等，或本单元引入的「事件作业排在测试作业之后」。写出各项目总耗时的对照。
  3. **隔离复跑**：Application.Tests、Protocol.Tests、Networking.Tests、Host.Tests 各一次，落盘到 `logs/task-12-isolated.txt`。超时形态的失败先按 AGENTS.md「满载假红」的规则隔离复跑，再归类。
  4. **新增测试 ×10**：§8 的全部新增用例，以及 §9.2 改写后的用例，隔离连续跑 10 次，在全量负载下再跑 1 次，全部通过，落盘到 `logs/task-12-new-x10.txt`。
  5. **表征测试**：8 通过、2 跳过，断言未改。
  6. **变异核对**：每项都用 scratchpad 备份，逐字节还原后 `cmp`，全部做完后重新构建。
     - M1：`DrainOnceAsync` 直接调用 `ProcessAsync`，不入队。N1、N2 变红。
     - M2：失败记录（`ProcessAsync` 的 catch 分支）拆到另一个作业。N3 变红。
     - M3：LiveOnly 的投影移出作业：作业只提交消费决定，生产者在等到作业完成之后再调用订阅处理。N2 变红。
     - M4：来源一律记为 `Event`。N4 变红。
     - M5：以生产者的停止令牌入队，而不是 `None`。N6 变红。
     - M6：去掉 `DrainOnceAsync` 的 `AssertNotOnExecutor`。N8 变红（挂起，在 5 秒等待处超时）。
     - M7：去掉组循环的「执行器已停止」分支。N7 变红。
     - M8：把事务检查挪到 `AssertNotOnExecutor` 之后。`UsageQuotaTests:117` 变红（前提是待裁定第 5 条采用本方案）。
  7. **测试改动核对**：
     - 新增测试逐条列名，N 写出加法。
     - §9 处置过的每个既有测试文件写明类别与理由。只改包裹方式、夹具开关或调用位置的文件，用 TASK-07 的差异核对脚本（TASK-11 的用法是 `t07/diffcheck7.py <基准>`）证明方法集合与断言行多重集合不变。
     - 改写的冲突用例写成「旧断言 → 新断言 → 保护意图」。
  8. **生产改动的边界与不变量**：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件；
     - `grep -rn "IGameplayQueue" Server/src`（排除 bin/obj）：与 TASK-11 Result Checks 8 相比，新增的命中只在 `DomainEventDispatcher.cs`；
     - `grep -rn "SuppressFlow\|RunDetached" Server/src`：与 TASK-11 相同；
     - `grep -n "groupGates\|TransactionalExecutionGate" Server/src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs`：两者仍在，留给 T-005；
     - **部分迁移期间的不变量**（拆分原则第 3 条）：逐个列出等待事件作业的执行器外代码（§5.3），确认它们都没有「持门等待已迁入的入口」。组门是它们持有的唯一同步原语，执行器上的作业从不等组门（§5.2 的守卫保证），所以不构成环。Result 要写明这一点。
  9. **探针复跑**（`verification/probe-tools/`，授权见下）：
     - 项目：Application、Protocol、Host、Networking。原始记录 `PROBE_LOG_PREFIX=task-12-probe`，落盘到 `logs/task-12-probe-<Project>.txt`。
     - **本族归零**（以 TASK-11 p2 为基线）：
       - §3.5 事件分发：459／1902 → 0；
       - `task-07-probe.md` §5 中由事件订阅者截栈形成的 (C) 栈顶：`DomainEventDispatchingExtensions.<AddDomainSubscriber>b__2_1`、`ExpiredItemsNotificationSubscriber.HandleAsync`、`SouthernDailyRefillNotificationSubscriber.HandleAsync` → 0。
     - 残留只允许来自顺延的冲突用例（T-004c 的 `DomainEventDispatchTests.Commit_under_database_admission…` 等），并用排除复跑证明。排除之后仍有的，按缺陷处理。
     - 本族以外各族的 (A) 计数，与 TASK-11 p2 并列写出，作为 TASK-13、TASK-14 的输入。如果 §3.10（命令总线）或 §3.7（GM/HTTP）有变化，要说明原因：订阅处理里经总线的命令，原先算在 §3.5 还是 §3.10，按 `fam.py` 的最外层入口分组规则逐条确认。
     - 还原证明的三项同 TASK-07 Required check 6；之后先 `--no-incremental` 重新构建，再跑正式全量。
  10. **对账行**：
      - 附录 A 第 8 行按策略写成「来源 → CMD 类型 → 证据」：Reliable 17、LiveOnly 22、Direct 13，三项相加 52；
      - 附录 A 第 11 行的事件部分：遗留 delivery → `startup:event/<订阅 ID>` → N4；
      - 标明哪一部分留给 T-005（组门、准入门）、T-006（LiveOnly 投影进发件箱）、T-007（父链、重试继承、深度超限落定）；
      - AC-015 每一条对应的用例（§9.1 表），写明它是经执行器驱动的。
- Dependencies：
  - TASK-11：`GameplayTimerWork`、裁定第 7 条及补充、探针基线 p2；
  - TASK-10：执行器生命周期服务与停机次序（§6.1）；
  - TASK-08：容器执行器注册、直发 2 秒期限、探针工具；
  - TASK-04：`GameplayExecutor`、作业令牌、`AssertNotOnExecutor`；
  - TASK-06、TASK-07：`TestGameplay` 与冲突总清单。
- Result review: required。
  - 本单元定下的「一条 delivery 一个作业、来源按事实所属进程判定、读到期在外、消费在内」是 T-005（删组门与准入门）、T-006（LiveOnly 投影改进发件箱）、T-007（按 delivery 记录链、重试继承、深度超限落定）的直接前提。做错了，要到那几步才以「链挂不上」或「落定无处可放」暴露。
  - 「哪些留在作业外」逐行判断：漏放一处写，全量大多仍是绿的。事件测试容器大多没有日志提供程序，失败日志也看不到。只有探针与 N1～N3 的作业身份断言能看出来，而探针会被 40 帧上限截断。
  - `A_slow_live_group…` 的改写是规格（RQ-043）造成的保护意图变化；`:117` 是在本单元改写还是顺延，也要靠人来判断（待裁定第 5、6 条）。
- Task-specific authorization:
  - 探针复跑（Required check 9）与变异核对（Required check 6）期间，允许临时改动 `Server/src`，用后按三项还原证明。探针与变异的改动不提交，不 `git stash`。
  - 只允许删除或改写「因本单元的改动而失败或挂起」的用例，以及冲突总清单分给本单元的 `:117`。每条都在 Result 中列出；其余冲突用例保持原样，留给所属单元。

## Detailed Plan

### 1. 今天的形状，与本单元的切法

| 段 | 今天（`DomainEventDispatcher.cs`） | 本单元之后 |
|---|---|---|
| 组循环 `RunGroupAsync` | 每个订阅组一个循环。先清空唤醒信号，再 `DrainOnceAsync(id)`；有处理就 `Yield` 后继续；没有就按 `NextDueAsync` 等唤醒或到期；扫描失败记 Error 并退避 | 不变，仍在执行器外，是生产者。新增一个分支：遇到 `GameplayExecutorStoppedException` 时记 Information 并结束本组（§6） |
| `DrainOnceAsync(id)` 前段 | 事务检查 → 取组门 → `ReadDueAsync`（批 64） | 事务检查 → **`AssertNotOnExecutor`**（§5.2）→ 取组门 → `ReadDueAsync`。都在执行器外：前两项是检查，组门是生产者之间的串行，读到期是只读 SQLite（附录 B） |
| 逐条处理 | `foreach` 里 `ct.ThrowIfCancellationRequested()`，再 `ProcessAsync` | `ct.ThrowIfCancellationRequested()` 仍在执行器外，是停止入队的那一道检查；然后**每条 delivery 入队一个作业并等待**，作业体就是 `ProcessAsync` |
| `ProcessAsync` | 反序列化 → 按策略分派；catch 中取准入门、另起事务 `FailAsync` | 原样，整体在作业里 |
| `ReliableAsync` | 准入门 → 事务 → `TryBegin` → 新 scope 调订阅 → `Complete` → 提交 | 原样，在作业里 |
| `LiveOnlyAsync` | 准入门 ＋ 事务里做消费决定并提交 → 释放 → 新 scope 投影，失败只记 Warning | 原样，两段都在同一个作业里，顺序不变 |
| `WaitForWakeOrDueAsync`、`NextDueAsync`、退避 `Task.Delay` | 生产者等待 | 不变，留在执行器外 |
| `EventService.EnqueueAsync`／`DirectAsync`／`ProjectAsync` | 在调用方的 CMD 内 | 不改。Direct 订阅仍在调用方 CMD 内同步执行（RQ-014），N9 给出对账证据 |

**一条 delivery 一个作业，不是一批一个**：
- RQ-041 的字面就是「一条 delivery 的消费是一条事件 CMD」，T-007 也按 delivery 挂链；
- 一批最多 64 条，合成一个作业会让入站在它后面等完整批；按条入队时，每两条之间别的来源都能插进来；
- 组内串行（RQ-043）由组循环「等前一个作业完成再入队下一个」保证，与今天组门内的 `foreach` 等价。

**读到期留在作业外**：
- TASK-11 把扫描放进作业，是因为那些扫描读的是附录 F「改为执行器内访问」的内存玩法状态；
- `ReadDueAsync`／`NextDueAsync` 只读 SQLite 的 delivery 表，属于附录 B「只读 SQLite 的查询」，也是 Plan 共享决定「事件」与 TASK-08 拆分表的原话；
- 外面读到的批可能过期（比如同一 delivery 已被另一次排空处理），但作业里的 `TryBeginAsync` 带着 `state=0 AND next_attempt_at<=now` 条件复核，过期的那条返回 `false`、不计数，与今天组门内的复核等价。

### 2. 作业形状

```
DrainOnceAsync(subscriberId, ct):
  if (transactions.Current is not null) throw InvalidOperationException(原文)   // 原样，必须第一个（§9.3、M8）
  GameplayJob.AssertNotOnExecutor("Draining domain event deliveries")          // §5.2
  await serial.WaitAsync(ct)                                                   // 组门，过渡期保留
  try
    due = await Persistence.ReadDueAsync(...)                                   // 执行器外只读
    foreach delivery in due
      ct.ThrowIfCancellationRequested()                                        // 停止后不再入队
      var processed = false
      await GameplayTimerWork.RunAsync(queue, CommandTypeOf(delivery),
          async t => processed = await ProcessAsync(delivery, t), ct, SourceOf(delivery))
      if (processed) count++
  finally serial.Release()
```

- `ProcessAsync`、`ReliableAsync`、`LiveOnlyAsync` 的方法体逐字不动。它们拿到的令牌 `t` 就是生产者的 `ct`（`GameplayTimerWork` 的约定）。
- **失败记录在同一个作业里**（TASK-08 拆分表的风险一栏）：`ProcessAsync` 的 catch 在业务事务回滚之后，另取准入门、另开事务、`FailAsync`、提交，全部在作业里。这就是 RQ-041「失败时回滚，另起工作单元记录重试」，退避间隔 `RetryDelay` 不变。
- **失败记录本身失败**（`FailAsync` 抛出）：异常离开作业，执行器记一条 `Gameplay event:<id> failed`（Error），再原样抛给生产者；组循环照旧记「could not persist or scan」并退避。结果与今天相同，只多一条执行器 Error，属于 TASK-09 待裁定第 4 条的双日志，交 T-008。
- **LiveOnly「先提交消费决定，再投影」在作业里的形状**：
  1. 准入门 ＋ 事务：`TryBegin` → 判断旧进程、过期或已合并 → `Complete`（带理由）→ 提交 → 释放准入门；
  2. 同一作业里，事务之外，新 scope 调投影订阅；失败只记 Warning，不补播。
  - 两段之间没有别的 CMD 插入，比今天更紧，不违反「先提交再投影」。
  - 投影今天直发，从此受 TASK-08 的 2 秒期限约束（§7 第 2 条）；改为进事件 CMD 的发件箱，是 T-006 的事（RQ-042 后半句）。
- `processed` 经闭包带出作业。被取消时作业体不赋值，生产者随后重新抛出取消（`GameplayTimerWork` 的约定），计数不会多算。

### 3. 来源、CMD 类型与调用链（过渡期标注；与 T-007 的边界）

- **来源按事实是哪个进程记录的来判定，不按「第几轮扫描」判定**：
  - `delivery.Event.OriginInstanceId == instance.Id`：本进程记录的事实，来源 `Event`，CMD 类型 `event:<订阅 ID>`；
  - 否则是之前的进程留下的遗留 delivery（RQ-051「进程重启后，遗留 delivery 的消费」），来源 `StartupRecovery`，CMD 类型 `startup:event/<订阅 ID>`。
  - 理由：
    - 今天没有独立的「启动扫描」代码，启动恢复就是组循环最初几轮的 `DrainOnceAsync`；
    - 「第一轮」这个口径会把启动后刚产生的新事实误标为恢复，也会把退避到后几轮才到期的遗留事实误标为事件；
    - `OriginInstanceId` 正是 LiveOnly 今天判断「旧进程提示」所用的那个字段，同一事实只有这一个判据。
- **一律新根**：`GameplayCallChain.NewRoot(Event)`／`NewRoot(StartupRecovery)`，深度 0。这是拆分原则第 4 条的过渡期标注：
  - RQ-050 要求事件 CMD 是发布它的 CMD 的子节点（深度加 1），同一 delivery 的重试继承首次消费的链；这需要「按 delivery 记录进程内链表」（RQ-051），归 T-007；
  - 本单元把「delivery → 链」收在一个私有方法里（`SourceOf`／`CommandTypeOf`，T-007 可合并为 `ChainFor(delivery)`），T-007 只换这一处；
  - `GameplayTimerWork.RunAsync` 今天只接受来源、在内部取新根；T-007 需要给它加一个接受现成链的重载，本单元不预先加。
- **深度超限（RQ-053(b)）不在本单元**：新根的深度恒为 0，执行器的深度检查不会拒绝事件 CMD。T-007 引入继承之后，超限的 delivery 要以系统 CMD 落定为终态或完成；在那之前，`GameplayDepthExceededException` 只会被组循环当成扫描失败而退避，Result 登记为 T-007 的缺口。

### 4. 组循环作为生产者：与 TASK-11 形状统一

- **入队与等待**用 `GameplayTimerWork.RunAsync(queue, type, body, ct, source)`，与 TASK-11 的定时源、Joust 启动恢复是同一个辅助：
  - 以 `None` 入队：已入队的事件 CMD 在执行器停止前一定开始执行（TASK-11 裁定第 7 条）；
  - 作业体拿生产者令牌：停机或测试的停止令牌能让卡住的订阅处理退出（`Stop_cancels_inflight_reliable_work…` 依赖这一点），执行器能排空；
  - 作业体经该令牌抛出的取消被带出作业、在生产者处重抛：执行器不把它记成失败，组循环原有的 `catch (OperationCanceledException) when (stoppingToken…)` 照常结束。
- **不用合并源**：
  - RQ-006 管的是「同一个定时或唤醒源」的重复入队。组循环每次入队都先等前一个作业完成，天然至多一个在途；
  - 测试直接调用 `DrainOnceAsync` 与组循环并行时，组门把两者串行；
  - 合并源会让「被合并」的调用什么都不执行就返回，而测试里 `Assert.Equal(n, await DrainOnceAsync())` 依赖每次调用都真的处理（TASK-11 §2.1 对入口方法的同一论证）。
- **`GameplayTimerWork` 的名字**保留，只把类型摘要从「一次定时源运行」改为「一次生产者运行：定时、启动恢复、事件消费」（待裁定第 4 条）。
- **构造参数**：`DomainEventDispatcher` 增加注入 `IGameplayQueue`。`AddEventDispatching` 先调用 `AddCommandDispatching`，后者调用 `AddGameplayExecution`，所以所有组合（含只调 `AddEventDispatching` 的测试容器）都能解析。

### 5. 过渡期保留的门与本单元的不变量

#### 5.1 组门与数据库准入门

- **组门 `groupGates`**（附录 F「删除」栏，T-005 删除）：保留。它现在只串行「同一组的生产者」：组循环，以及测试直接调用 `DrainOnceAsync` 的地方。生产者持组门等待事件作业，执行器上的作业从不取组门（§5.2），所以不成环。性质与 TASK-11 调度任务的并发槽相同。
- **数据库准入门 `TransactionalExecutionGate`**（附录 F「删除」栏，T-005 删除）：保留在作业体里，与今天的位置相同。
  - 执行器外仍有持有者：经总线、未迁入的 GM/HTTP 命令（TASK-13），以及 T-004c 冲突用例 `Commit_under_database_admission…` 这种测试直接取门的写法。
  - 它们持门时，事件作业在门上等，整个执行器跟着等。这是拆分原则第 3 条允许的反向情形：只是延迟，不会成环，因为这些持有者持门期间不等待执行器上的作业。
  - Result 逐个列出执行器外的准入门持有者，确认这一点（§9.5）。

#### 5.2 `DrainOnceAsync` 的守卫

- **问题**：如果某个作业调用 `DrainOnceAsync(id)`，而组循环此刻正持着这一组的组门、等一个排在该作业后面的事件 CMD，作业就会卡在组门上。这是死锁，不是自我等待报错：执行器的自我等待检查在 `queue.RunAsync` 里，而这个作业走不到那里。
- **做法**：事务检查之后、取组门之前调用 `GameplayJob.AssertNotOnExecutor("Draining domain event deliveries")`。在作业里调用时立即以 `GameplaySelfWaitException` 失败，并按 RQ-003 把这个作业标为程序错误。
- **事务检查必须排在前面**：在带事务的作业里调用时，抛的仍是今天那条 `InvalidOperationException`，作业不被标为程序错误。`UsageQuotaTests:117` 依赖这个次序（§9.3），M8 守着它。
- **代价**：`AssertNotOnExecutor` 判的是「在任何执行器上」。测试若在不带容器的 `TestGameplay.RunAsync(async () => …)` 区域里调用 `DrainOnceAsync`，今天能跑通（另一个执行器只是另一条队列），从此会报错。处置按 §9.4 (iii)：把调用移出区域。生产中只有一个执行器（RQ-001），两种判定在生产中等价。

#### 5.3 等待事件作业的执行器外代码（Required check 8 的清单）

| 代码 | 等待时持有什么 |
|---|---|
| 各组循环（`DomainEventHostedService` → `RunAsync` → `RunGroupAsync`） | 本组的组门 |
| 测试直接调用的 `DrainOnceAsync` | 本组的组门 |

两者都不持附录 F 的门。组门不会被执行器上的作业等待（§5.2），所以不成环。

### 6. 停机

- **不新增 `IGameplayProducerDrain`**，与 TASK-11 §6 相同，靠宿主的停止次序（TASK-10 §6.1）：
  - `DomainEventHostedService.StopAsync` 取消 `stoppingToken`，并等各组循环结束。这时执行器仍在运行；
  - 组循环在下一次入队之前的 `ThrowIfCancellationRequested`，或在等待唤醒、到期时退出，不再入队；
  - 已入队的事件 CMD（以 `None` 入队）照常开始执行。作业体在第一处检查令牌时（`gate.AcquireAsync(ct)` 等）以取消退出，取消被带回生产者，循环结束；
  - 之后 `GameplayExecutorLifetime.StoppedAsync` 排空并停止执行器。
- **语义**：
  - Reliable：作业体中途取消时事务回滚，delivery 保持 `state=0`，下次启动时作为 `startup:` CMD 恢复。与今天「停止取消在途工作、留待重启」相同（`Stop_cancels_inflight_reliable_work…` 守着）。
  - LiveOnly：取消发生在消费决定提交之后、投影之前时，提示丢失，与今天相同，即 AGENTS.md 的「崩溃窗口允许漏提示」。
  - 与 TASK-11 主控方补充第 7 条一致：已入队的 CMD 在作业层面一定执行，作业体可以按生产者令牌提前退出。
- **执行器先于组循环停止**：只会出现在不经宿主停止次序的容器释放，或测试直接停止执行器时。
  - 组循环下一次入队得到 `GameplayExecutorStoppedException`。今天没有这条路径；如果不处理，它会落进通用 catch，每隔 `RetryDelay` 记一条 Error，直到停止令牌到来。
  - 本单元在 `RunGroupAsync` 里单独捕获它：记一条 Information（「执行器已停止，事件组 {id} 不再入队」），结束本组。delivery 保持待处理。N7 守着这一点。
  - 直接调用 `DrainOnceAsync` 的人照常收到这个异常。
- **停机时长**：每个组循环至多等自己那一个在途作业排到并退出，上限是宿主的 `ShutdownTimeout`（同 TASK-11 主控方补充）。订阅组约 39 个（Reliable 17 ＋ LiveOnly 22，以 `registry.Groups` 的实际键数为准），每组至多一个作业在队列里。

### 7. 风险与可见差异

1. **订阅组之间不再并发**（RQ-043 的直接后果）：一个慢的订阅处理会占住执行器，所有来源都在它后面等。今天的 `A_slow_live_group…` 守的正是「慢组不挡别组」，被规格删除（§9.2）。长订阅处理（成就、任务进度、称号簿）由 M-1 按 `Gameplay event:… completed` 的 Debug 耗时观察，属于 RQ-091 单条时长阈值的关注对象。
2. **直发 2 秒期限扩大到事件 CMD**：LiveOnly 投影（22 个，全部 `ProjectAsync`）在作业里直发，不读包的客户端会在 2 秒后被断开。VC-8 提前的范围从入站、生命周期、定时扩大到事件，登记进 Result；T-006 之后消失。
3. **事件投影与触发它的入站回包之间的相对顺序变为确定**：今天提交后的唤醒让 LiveOnly 投影与入站 handler 的后续发送并发，谁先落线取决于竞态；从此事件作业一定排在触发它的那个入站 CMD 之后。这是今天可能出现的顺序之一，不是新行为，但依赖反向次序的测试会暴露出来，按 §9.4 归类。表征测试若因此变化，停下回报主控方，不改断言。
4. **执行器外的准入门持有者**（GM/HTTP，TASK-13）会让事件作业在门上等，整个执行器跟着等。只是延迟（§5.1）；TASK-13 之后消失。
5. **首次加载**：事件作业可能比 `GameServerStartupService` 的预热更早运行（组循环在托管服务启动时就开始），在执行器上触发 `Lazy` 内容源的首次加载（成就目录、称号目录）。预热清单归 TASK-13（3h），本单元只登记。
6. **双 Error 日志**：只在失败记录本身失败时出现（§2），交 T-008。

### 8. 新增测试

放在 `Application.Tests/DomainEvents/DomainEventDispatchTests.Executor.cs`，复用 `Fixture`。夹具加三个开关：
- `IGameplayQueue` 换成 `Execution/TimerSourceTestSupport.cs` 的 `RecordingGameplayQueue`；
- 一个记录型 `ILoggerProvider`；
- `Probe` 的 live 阻塞改为可放行的 TCS（§9.2 也用到）。

`FaultingRetryPersistence` 另在 `TryBeginAsync`／`CompleteAsync`／`FailAsync` 里记下 `(方法, 订阅, GameplayJob.IsOnExecutor, GameplayJob.Current)`。
驱动方式：N4、N6、N7 经 `Dispatcher.RunAsync`（即托管服务的循环）；其余可经 `DrainOnceAsync`（组循环调用的同一个生产者方法，按 TASK-11 对入口方法的口径算入口）。

| 条 | 情形 | 断言 |
|---|---|---|
| N1 | Reliable 消费 | 订阅处理与 `TryBegin`、`Complete` 都在执行器上，`Current` 是同一个 ID，CMD 类型 `event:counter`，来源 `Event`；业务写入与完成标记一起提交（计数与 `state=1` 同时可见） |
| N2 | LiveOnly 消费 | 投影订阅被调用时在执行器上，`Transactions.Current` 为空；此刻另开连接读到该 delivery 已是 `state=1`（消费决定先提交）；`CompleteAsync` 与投影记录的 `Current.Id` 相同（同一个 CMD） |
| N3 | 失败与重试（冻结时钟，`FailingAttempts = 1`） | 第一次 `TryBegin` 与 `FailAsync` 同一个 `Current.Id`，都在执行器上；推进时钟后的重试是另一个作业（ID 不同），来源 `Event`；业务计数只在重试之后出现。另一条：`FailRetryRecordingOnce` 时，记录型日志里恰有一条 `Gameplay event:counter failed`（双日志，§2） |
| N4 | 启动恢复（经 `RunAsync`） | 夹具 A 记录事实不消费；同一库的夹具 B（新进程实例）启动组循环：counter／audit 的作业来源 `StartupRecovery`、类型 `startup:event/counter`／`…/audit`；旧进程的 live 提示以 `startup:event/live` 消费、理由为旧进程、投影未被调用；之后在 B 里新记录的事实来源为 `Event` |
| N5 | 组内串行与失败退避期间的继续处理（RQ-043） | counter 组：事实 F1 第一次失败进入退避，之后的 F2 在 F1 退避期间被处理（F2 计入、F1 仍为 `state=0, attempt_count=1`）；推进时钟后 F1 完成；`RecordingGameplayQueue.Watched = "event:counter"` 的 `MaximumOutstanding == 1` |
| N6 | 停机时已入队的事件 CMD（裁定第 7 条） | 先用一个等待 TCS 的作业占住执行器，启动组循环，等记录型队列记到 `event:counter` 入队；取消停止令牌，再放行。该作业被执行（`Ran` 中有它），作业体因令牌已取消而退出；counter 未写入，delivery 为 `state=0, attempt_count=0`；组循环 5 秒内结束；记录型日志中没有 `Gameplay event:… failed` |
| N7 | 执行器先停止 | 记录事实后先 `executor.StopAsync()`，再以从不取消的令牌启动 `RunAsync`：各组循环 5 秒内结束；有「不再入队」的 Information；没有「could not persist or scan」；delivery 仍为待处理 |
| N8 | 在作业里调用 `DrainOnceAsync`（§5.2） | 组循环运行、counter 有一条到期 delivery；先用一个容器作业占住执行器，在作业里等到记录型队列记下 `event:counter` 入队（此时组循环持有 counter 的组门），再调用 `DrainOnceAsync("counter")`：1 秒内以 `GameplaySelfWaitException` 失败，作业被标为程序错误 |
| N9 | Direct 订阅留在调用方 CMD 内（附录 A 第 8 行 Direct 部分的对账） | 在容器作业里开写事务调用 `DirectAsync`：运行时订阅记录的 `Current.Id` 等于调用方作业的 ID；记录型队列中没有任何 `event:` 入队 |

- 先写测试，再改生产代码：N1～N8 在改动前应当红（解析不到队列、`IsOnExecutor` 为假、或 N8 挂起），Result 记录红的形态。N9 守的是不被破坏。
- N 预计 ＝ 9；`A_slow_live_group…` 是改写，不计入 N。

### 9. 既有测试

#### 9.1 AC-015 映射（迁入之后，这些用例经 `DrainOnceAsync` 或 `RunAsync` 驱动，消费都在执行器上）

| AC-015 子项 | 用例 |
|---|---|
| Reliable 完成标记与业务写入同提交 | `Each_fact_is_retained_with_one_transaction_batch…`、`Business_write_and_all_event_rows_roll_back_together`、N1 |
| 失败回滚后记录重试、退避与终态 | `Failed_reliable_handler_rolls_back…`、`Failed_handler_is_retained_as_terminal…`、`Worker_retries_at_due_time…`、`A_failure_to_record_retry_state_backs_off…`、N3 |
| LiveOnly 跳过与不补播 | `Expired_live_hints…`、`A_live_failure_is_consumed_once_and_never_retried`、`Restart_recovers…`（旧进程提示）、`UsageQuotaTests:117`（失败投影不回滚业务计数）、N2 |
| 启动扫描遗留 delivery | `Restart_recovers…`、`Stop_cancels_inflight_reliable_work…`（重启后补做）、N4 |
| 订阅组内顺序与失败退避后的继续处理 | N5；`DomainEventSchedulingTests.A_synchronously_consuming_group…`（组间公平，预判通过） |

各条预判：除 §9.2 外全部不改、照常通过。理由：
- 它们调用的是生产者方法，生产者在测试线程上等作业，测试线程不在执行器上；
- `Fixture.SendAsync` 是容器执行器上的短作业，与事件作业先后执行，不重叠。

#### 9.2 `DomainEventDispatchTests.A_slow_live_group_does_not_block_other_groups_or_hold_the_business_transaction`（新判定的冲突，§9.4 (ii)）

- **机制**：live 作业在 `Task.Delay(Infinite, ct)` 上卡住、占住执行器，`FastLive`、counter、audit 的作业排在它后面，5 秒等待超时；第二次 `f.SendAsync` 也是同一执行器上的作业，同样排在后面。
- **处置**：按 RQ-043 改写，改名 `A_slow_live_hint_postpones_other_groups_and_still_runs_outside_the_business_transaction`。
  - 旧断言：live 卡住时，`FastLive`、counter 7、audit 7 都完成；第二次发送 5 秒内完成；停止后 live 只收到 1 条；计时器归零。
  - 新断言：
    - `Probe` 的 live 阻塞改为等一个可放行的 TCS（受令牌约束）；
    - `LiveEntered` 之后，入队一个标记作业，并在放行前检查它尚未开始。这条检查只会漏报，不会误报；
    - 放行后 `FastLive`、counter 7、audit 7 全部完成（推迟而不丢失）；第二次发送完成；live 订阅里 `Assert.Null(Transactions.Current)` 原样保留；停止后计时器归零；
    - owner `"a"` 的 live 提示恰好一次（第二次发送的 `"b"` 可能在停止前被消费，不作断言）。
  - 保护意图：「订阅组互不阻塞」被规格删除（RQ-043「订阅组之间不再并发」）；「live 投影不持有业务事务」保留；新增「慢组推迟而不丢失其他组」。Result 中引用 RQ-043，写成「旧断言 → 新断言 → 保护意图」。
- 追加到 `task-07-conflicts.md` §10，总数 137 ＋ 1。

#### 9.3 `UsageQuotaTests:117` `Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts`（第 1 个事务块）

- **预判：本单元不改它也通过**。第 1 个事务块在执行器外开写事务，`DrainOnceAsync` 的事务检查仍是第一步，照旧抛 `InvalidOperationException`，还没有走到入队。它要到 T-004c 开判据（执行器外开写事务被拒）时才会失败。
- **本方案：在本单元改写**（待裁定第 5 条）。
  - 理由：冲突清单写的处置是「入口迁入执行器后改写」，阻碍包裹的原因（入口与事务共用 AsyncLocal）在本单元消失了：
    - 在带事务的作业里调用 `DrainOnceAsync`，事务检查先于守卫，抛的是 `InvalidOperationException`，作业不被标为程序错误；
    - 所以第 1 个事务块可以像第 2、3 块一样整体包进 `TestGameplay.RunAsync(f.Services, …)`。
  - 旧断言 → 新断言：`Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DrainOnceAsync())` 一字不改，只是现在在作业里断言，含义变为「CMD 内、事务内调用事件生产者被拒」。`Assert.Empty(bus.Entries)` 等其余断言原样留在块内。
  - 保护意图不变：分发不在发布它的业务事务里开始。
  - 差异核对证明断言行多重集合不变。M8 证明事务检查的次序被这条用例守着。
- 备选：按拆分原则第 5 条顺延 T-004c，本单元不动它。

#### 9.4 测试侧的处置分类

沿用 TASK-11 §9 的 (i)～(vi)，本单元特有的处置规则如下：
- **(iii) 包裹区域里调用生产者**：在带容器、或不带容器的 `TestGameplay` 区域里调用 `DrainOnceAsync`／`RunAsync`，从此报 `GameplaySelfWaitException`（§5.2）。把调用移出区域，断言不变。
  - 静态初筛（基准 `648fbc60`）：调用 `DrainOnceAsync`／`Dispatcher.RunAsync` 的测试文件，除 `DomainEventDispatchTests` 外有 17 个，共 46 处。
  - 初读没有发现处在包裹区域内的调用。`MailboxProtocolIntegrationTests.cs:53` 缩进在区域内，实际在区域外，TASK-07 已修正过。以全量中的 `GameplaySelfWait` 计数为准。
- **(iv) 作业内轮询事件效果**：在容器作业里等待某个事件消费的结果。从此要等一个排在自己后面的事件 CMD，所以解开包裹，或改为每次求值一个短作业。
- **(v) 新的交错型冲突**按 (ii) 判定，追加到 `task-07-conflicts.md` §10，并更新总数加法。
- **(vi) 依赖「事件投影先于回包」的线级断言**（§7 第 3 条）：先确认是否只是轮询窗口问题；断言本身依赖反向次序的，判为 (ii)，停下交主控方。

#### 9.5 主动复核（即使全绿，也逐项写结论）

- **T-004c 冲突 `DomainEventDispatchTests.Commit_under_database_admission…`**：测试在执行器外持准入门、开事务、提交；组循环的作业在执行器上等门。预判通过：持门期间测试不等任何作业，属于反向情形。确认仍通过，保持原样，留给 T-004c。
- **执行器外的准入门持有者**：
  - 测试中直接取 `TransactionalExecutionGate` 的文件：`DomainEventDispatchTests`、`NetworkMessageDispatcherTests`、`MoveItemSpaceHandlerTests`、`DungeonVisitDomainServiceTests`；
  - 生产中未迁入的 GM/HTTP。
  - 逐个确认持门期间不等待容器执行器上的作业。
- **订阅处理器不入队**：39 个 Reliable 与 LiveOnly 处理器（`event-chain.md` 表），以及 13 个 Direct 处理器，都不引用持有 `IGameplayQueue` 的类型（基准上初检：这些处理器所在目录不引用任何每日刷新服务、两塔扫描、Joust、卡牌协调器或分发器）。Result 写出检索命令与结果。
- **拉起整个宿主的 Host、Networking 用例**（`GmEventsHttpTests`、`GmOfficialMailHttpTests`、表征测试）：事件作业与入站作业交错，确认没有 (vi)。
- **TASK-08 §4 复核表中「TASK-12 再看」的一项**：`A_slow_live_group…`，结论见 §9.2。

### 10. 探针

- 做法同 TASK-11 §10：
  1. `PROBE_WORK` 指向 scratchpad，`patch.py apply`；
  2. 构建；
  3. `runall.sh` 跑四个项目；
  4. `patch.py revert`；
  5. 三项还原证明。
- `classify.py` 的入口帧名单不改。迁入之后，`DomainEventDispatcher` 的帧只出现在生产者栈上，而生产者在执行器外只做检查、取组门、只读 SQLite 和入队，不触达挂点（P1 只挂可写的 `Begin`，`ReadDueAsync` 不开领域事务；P3 的准入门已在作业里）。
- 判读：
  - §3.5 与三个订阅者 (C) 栈顶按 Required check 9 归零；
  - 残留按排除复跑判定；
  - 本族以外各族与 TASK-11 p2 并列。
- 探针轮中的失败不作为任何结论的证据（TASK-07 §7）。

### 11. 执行顺序

1. **第 0 步**：在基准上单跑 `DomainEventDispatchTests`、`DomainEventSchedulingTests`、`UsageQuotaTests`，记下基线结果（`logs/task-12-step0.txt`）。
2. 夹具开关与 N1～N9（红），记录红的形态（`logs/task-12-red.txt`）。
3. 改 `DomainEventDispatcher`：
   - 构造注入；
   - `DrainOnceAsync` 的守卫与逐条入队；
   - `SourceOf`／`CommandTypeOf`；
   - `RunGroupAsync` 的「执行器已停止」分支；
   - `GameplayTimerWork` 的注释。
4. §9.2 改写、§9.3 包裹；跑 Application.Tests。
5. 全量，带挂起检测。按 §9.4 逐条分类，重跑到稳定；做耗时比对。
6. 变异核对。
7. 探针复跑与归零分析，还原证明。
8. 对账行。
9. 正式全量 → 隔离复跑 → 新增测试 ×10 → 测试改动核对 → 边界 grep 与不变量复核 → 写 Result。

### 12. 为什么除可见差异外行为不变

- 每条 delivery 的处理内容、内部顺序、准入门、事务、`TryBegin` 复核、退避与终态判定，都与今天逐行相同，只是换到执行器上运行。
- 读到期、等待唤醒、组门、扫描失败退避留在原处。
- 跨组从并发变为串行，这是 RQ-043 与 RQ-001 的要求。
- 可见差异见 §7 第 2、3 条。另有一条日志差异：失败记录本身失败时多一条执行器 Error。
- 线上字节不变，由表征测试与既有线级测试守着。

## 待裁定

1. **来源判据与 CMD 类型命名**（§3）：
   - 以 `OriginInstanceId` 是否为本进程判定 `Event`／`StartupRecovery`，而不是「组循环的第一轮扫描」；
   - CMD 类型 `event:<订阅 ID>`、`startup:event/<订阅 ID>`；
   - 一律新根，父链、重试继承与深度超限落定归 T-007。
   - 请确认。
2. **作业粒度**：一条 delivery 一个作业（RQ-041 字面）。不采用「一批一个作业」，理由见 §1。请确认。
3. **读到期留在作业外**（§1）：依据附录 B「只读 SQLite 的查询」与 Plan 共享决定「事件」的原话，与 TASK-11「扫描放进作业」的口径不同，因为读的不是内存玩法状态。请确认。
4. **复用 `GameplayTimerWork.RunAsync`**（§4），只改它的类型摘要注释；T-007 再给它加接受现成链的重载。
   - 备选：在分发器里另写一个同形的辅助。那样「以 `None` 入队、作业体拿生产者令牌、取消带出」会有两份实现。
   - 请确认，并确认允许改 TASK-11 这个文件的注释。
5. **`UsageQuotaTests:117`**（§9.3）：它在本单元不改也通过。
   - 本方案：在本单元把第 1 个事务块包进作业，断言一字不改；
   - 备选：按拆分原则第 5 条顺延 T-004c。
   - 建议本方案：冲突清单把它分给「入口迁入之后」，阻碍包裹的原因在本单元消失；顺延则要等 T-004c 的判据把它打红。
6. **`A_slow_live_group…` 判为新冲突，并按 RQ-043 改写**（§9.2）：保护意图「订阅组互不阻塞」被规格删除，在 Result 中记为意图变化（同 TASK-11 对 `ScheduledTaskTests` 的处理）。请确认。
7. **`DrainOnceAsync` 的守卫**（§5.2）：用 `AssertNotOnExecutor`（「任何执行器」口径），把「作业里调用 → 组门死锁」变成立即的程序错误。代价：测试在不带容器的包裹区域里调用它也会报错，按 (iii) 移出。请确认。
8. **「执行器已停止」分支**（§6）：组循环遇到 `GameplayExecutorStoppedException` 记 Information 并结束本组，而不是落进通用 catch、每秒记一条 Error。请确认。
9. **附录 A 第 8 行的证据粒度**：不为 52 个订阅逐行写 Theory。Reliable 与 LiveOnly 的全部订阅都经同一个 `ProcessAsync` 路径，证据按策略取（N1～N5），再加上探针归零；Direct 13 个由 N9 证明留在调用方 CMD 内。请确认这满足 AC-006 的「每一项都有对账行」。
10. **T-004b 冲突的新分布**：本方案下 T-004b 余 4 － 1 ＝ 3（TASK-13 2 ＋ 顺延 T-004c 的 `ReplayStateSnapshotTests:206` 1），新判定 1 条使总数为 138。若第 5 条选备选，`:117` 计入 T-004c。

## 主控方裁定（执行前）

1～10 项全部按本文件的建议方案接受：
1. 来源按 `OriginInstanceId` 判定，CMD 类型用 `event:<订阅 ID>` 和 `startup:event/<订阅 ID>`。
2. 一条 delivery 对应一个作业，与 RQ-041 字面一致。
3. 读取到期 delivery 的步骤留在作业外。读的是 SQLite 只读数据，附录 B 允许这样做；不读内存中的玩法状态。
4. 复用 `GameplayTimerWork`，允许修改它的类型摘要注释。
5. `UsageQuotaTests:117`：在本单元把第 1 个事务块包进作业，断言一字不改。
6. `A_slow_live_group…` 判为新冲突，按 RQ-043 改写，冲突总数变为 138。Result 中写明「旧断言 → 新断言 → 理由」，并把它记为 RQ-043 规定的意图变化。
7. `AssertNotOnExecutor` 判的是「在任何执行器上」。受影响的测试把调用移出包裹区域，断言不变。
8. 新增「执行器已停止」分支：记一条 Information，然后结束本组。
9. 附录 A 第 8 行按策略取证，不为 52 个订阅逐行写 Theory。
10. T-004b 冲突用例的新分布按本文件。

**停止条件补充**：事件投影与触发它的入站回包，先后顺序从此变为确定。表征测试的金样本如果因此出现任何变化，执行者必须停下来回报主控方，不得改金样本，也不得放宽比对。

## Result

### Outcome

- **一条 delivery 的消费是一条事件 CMD**（RQ-041）：`DrainOnceAsync` 在执行器外做事务检查、执行器守卫、取组门、只读 `ReadDueAsync`，然后对每条 delivery 经 `GameplayTimerWork.RunAsync` 入队一个作业并等待它完成，再处理下一条。作业体就是 `ProcessAsync`；它与 `ReliableAsync`、`LiveOnlyAsync` 的方法体逐字未动：
  - Reliable：准入门 → 事务 → `TryBegin` → 订阅 → `Complete` → 提交，同一个作业（N1）；
  - LiveOnly：先提交消费决定，再在同一个作业里、事务外投影（N2）；
  - 失败：回滚后另开事务记录重试或终态，与失败的那次尝试是同一个作业；重试是另一个作业（N3）。
- **来源与 CMD 类型**（裁定第 1 条）：`OriginInstanceId == instance.Id` 为 `Event`／`event:<订阅 ID>`，否则为 `StartupRecovery`／`startup:event/<订阅 ID>`（N4）。一律 `NewRoot`，集中在私有方法 `SourceOf`／`CommandTypeOf`，T-007 只换这一处。
- **生产者形状与 TASK-11 统一**：以 `CancellationToken.None` 入队，作业体拿生产者令牌，取消带出作业（N6）；不用合并源。
- **过渡期保留**：组门 `groupGates`、准入门 `TransactionalExecutionGate` 原样保留（T-005）。`DrainOnceAsync` 在事务检查之后、取组门之前调用 `GameplayJob.AssertNotOnExecutor`，作业内调用立即以 `GameplaySelfWaitException` 失败并标为程序错误（N8）；事务检查仍是第一步（M8 守着）。
- **停机**：组循环遇到 `GameplayExecutorStoppedException` 记一条 Information（`Domain event dispatch group {SubscriberId} enqueues no more: the gameplay executor has stopped.`）并结束本组，delivery 留待下次启动（N7）。没有新增排空接口。
- **冲突用例**：`UsageQuotaTests:117` 第 1 个事务块包进作业、断言一字不改；`A_slow_live_group…` 按 RQ-043 改写（意图变化）。冲突总数 137 ＋ 1 ＝ 138，见 `verification/task-07-conflicts.md` §10。
- **表征测试金样本没有变化**：8 通过、2 跳过，`Characterization/` 目录未改，停止条件补充没有触发。

### Changed

**生产代码**（`git diff --stat -- Server/src`：2 个文件，＋34 −4，都在 Owned boundary 内，`logs/task-12-boundary.txt`）：

| 文件 | 改动 |
|---|---|
| `Application/DomainEvents/DomainEventDispatcher.cs` | 注入 `IGameplayQueue`；`DrainOnceAsync` 加守卫、逐条入队；`SourceOf`／`CommandTypeOf`；`RunGroupAsync` 加「执行器已停止」分支；类型 remarks 4 行 |
| `Application/Execution/GameplayTimerWork.cs` | 只改类型摘要：一次生产者运行（定时、启动恢复、一条事件 delivery 的消费）（裁定第 4 条） |

`DomainEventHostedService`、`EventService`、注册扩展、Persistence、各订阅处理器、执行器、`ProtocolModule`、`ApplicationModule` 都未改。

**新增测试 N ＝ 9，删除 D ＝ 0**（`Application.Tests/DomainEvents/DomainEventDispatchTests.Executor.cs`，原类改为 `partial`）：

| 条 | 方法 | 驱动 |
|---|---|---|
| N1 | `A_reliable_delivery_is_one_event_cmd_and_its_business_write_commits_with_its_completion` | `DrainOnceAsync` |
| N2 | `A_live_hint_commits_its_consumption_first_and_projects_later_in_the_same_event_cmd` | `DrainOnceAsync` |
| N3 | `A_failed_attempt_records_its_retry_in_its_own_event_cmd_and_the_retry_is_another_cmd` | `DrainOnceAsync` |
| N4 | `Deliveries_left_by_an_earlier_process_run_as_startup_recovery_and_new_facts_as_event_cmds` | `RunAsync` |
| N5 | `A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off` | `DrainOnceAsync`（含 3 个并行调用） |
| N6 | `An_event_cmd_queued_when_the_loop_stops_still_runs_and_leaves_its_delivery_pending` | `RunAsync` |
| N7 | `Group_loops_end_quietly_when_the_executor_has_already_stopped` | `RunAsync` |
| N8 | `Draining_from_inside_a_job_fails_at_once_as_a_program_error` | `RunAsync` ＋ 作业内 `DrainOnceAsync` |
| N9 | `A_direct_subscriber_runs_inside_the_calling_cmd_and_enqueues_nothing` | 容器作业内 `DirectAsync` |

加法：9 个 1 相加，N ＝ 9。§9.2 的改写不计入 N。

与计划的细节差异：
- N1 另断言「提交前另开连接读到计数 0、delivery 仍待处理，提交后两者同时可见」，作为「同一工作单元提交」的直接证据；
- N2 在投影订阅里记录「是否有事务」，不只依赖订阅里的 `Assert.Null`：那条断言失败时会被 `LiveOnlyAsync` 当成投影失败吞掉；
- N3 的两部分写在同一个方法里（两个夹具），所以 N 仍是 9；
- N5 的 `MaximumOutstanding == 1` 用 3 个并行 `DrainOnceAsync` 取证；顺序调用时它恒为 1，没有区分力。

**红的形态**（`logs/task-12-red.txt`，基准生产代码）：N1、N2、N3、N5 的执行位置或作业身份断言为假；N4 在 5 秒内等不到 `startup:event/*`；N6 在 15 秒内等不到 `event:counter` 入队；N7 以 `TimeoutException` 失败（组循环不结束）；N8 以「没有抛出 `GameplaySelfWaitException`」失败；N9 通过（守的是不被破坏）。改写后的 `A_slow_live_hint…` 也红（标记作业在放行前已开始）。

**夹具开关**（`DomainEventDispatchTests.cs`）：
- `Fixture(record: true)`：`IGameplayQueue` 换成 `RecordingGameplayQueue`，加记录型 `ILoggerProvider`（`LogRecorder`，在新文件里），注册一个 Direct 订阅（N9）。默认关闭，既有用例的组合不变；
- `Probe`：`CounterSeen`／`LiveSeen`／`DirectSeen`／`Persisted` 记录执行位置与 `GameplayJob.Current`；`Observe` 回调；可放行的 `LiveRelease`；
- `FaultingRetryPersistence` 在 `TryBeginAsync`／`CompleteAsync`／`FailAsync` 里记录 `(方法, 订阅, IsOnExecutor, Current)`。

**既有测试文件**（差异核对 `logs/task-12-diff-check.txt`，scratchpad `t07/diffcheck7.py 648fbc60`）：

| 文件 | 类别 | 方法 ／ 断言行 |
|---|---|---|
| `UsageQuotas/UsageQuotaTests.cs` | §9.3：第 1 个事务块包进 `TestGameplay.RunAsync(f.Services, …)` | 6＝6 ／ 44＝44，白名单外片段 0 |
| `DomainEvents/DomainEventDispatchTests.cs` | 夹具开关 ＋ §9.2 改写 | 17＝17（1 条改名）／ 72→73：删 1 行、加 2 行，全在改写的用例里；23 个白名单外片段都是夹具记录与该用例 |

逐条「旧断言 → 新断言 → 保护意图」：

- `UsageQuotaTests.Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts`（第 1 个事务块）：断言一字不改，现在在作业里、事务内调用 `DrainOnceAsync`，抛的仍是 `InvalidOperationException` → 意图不变：分发不在发布它的业务事务里开始。M8 证明这条用例守着「事务检查先于执行器守卫」。
- `A_slow_live_group_does_not_block_other_groups_or_hold_the_business_transaction` → 改名 `A_slow_live_hint_postpones_other_groups_and_still_runs_outside_the_business_transaction`。**这是 RQ-043 规定的意图变化。**
  - 旧：live 卡住时，`FastLive`、counter 7、audit 7 都完成；第二次发送 5 秒内完成；`Assert.Single(f.Probe.Live)`；计时器归零；
  - 新：live 阻塞改为可放行的 TCS（受令牌约束）；`LiveEntered` 之后入队标记作业，放行前 `Assert.False(marker.Task.IsCompleted)`；放行后 `FastLive`、counter 7、audit 7 全部完成，第二次发送完成；`Assert.Single(f.Probe.Live, hint => hint.Owner == "a")`；计时器归零；订阅里的 `Assert.Null(Transactions.Current)` 原样保留；
  - 意图：「订阅组互不阻塞」被规格删除（RQ-043「订阅组之间不再并发」）；「live 投影不持有业务事务」保留；新增「慢组推迟而不丢失其他组」。

**文档**：`verification/task-07-conflicts.md` 追加 §10；日志 `logs/task-12-{step0,red,mutations,build-test,isolated,new-x10,boundary,diff-check,slow-isolated,sched-repeat,net-crash,probe-summary}.txt` 与 `logs/task-12-probe-{Application,Protocol,Host,Networking}.txt`。

### Why（本地决定）

- **`consumed` 经闭包带出**：被取消时作业体不赋值，生产者重抛取消，计数不会多算（计划 §2）。
- **「执行器已停止」只捕获 `GameplayExecutorStoppedException`**：它只在入队时由执行器产生；作业体里的订阅处理器不能入队（会是自我等待），不会从作业体抛出这个类型。
- **N8 的作业体吞掉异常后照常返回**，用例断言作业仍以 `GameplaySelfWaitException` 失败，以此证明「被标为程序错误」，而不只是「抛了异常」。

### §9.5 主动复核（逐项结论）

| 项 | 结论 |
|---|---|
| T-004c `Commit_under_database_admission…` | 正式全量通过，保持原样。测试在执行器外持准入门期间只开事务、提交，不等任何作业；组循环的事件作业在门上等，属反向情形 |
| 执行器外的准入门持有者 | 测试：`DomainEventDispatchTests:175`（同上）；`Fixture.SendAsync`（在作业里取门，与事件作业同一执行器，先后执行不重叠）；`DungeonVisitDomainServiceTests:408`（门在包裹的作业里取，没有事件循环）；`MoveItemSpaceHandlerTests:365`（自建门实例，没有执行器）；`NetworkMessageDispatcherTests:133`（持门期间等的是入站作业到达门前的探针，容器没有启动事件循环）。生产：未迁入的 GM/HTTP 经总线取门；`holderrefs.py` 显示持有队列的类型只被模块注册与入站卡牌 handler 引用（协调器的 `Schedule` 不同步等待作业），持门期间不等待执行器上的作业。都只是延迟，不成环 |
| 订阅处理器不入队 | `subcheck.py`（多行解析注册）：52 个注册 ＝ Reliable 17 ＋ LiveOnly 22 ＋ Direct 13；处理器所在的 26 个文件里，命名任何持有 `IGameplayQueue` 的类型的为 0（检索用的名字共 33 个，清单在日志里）。运行期证据：全量 `GameplaySelfWait` 为 0。检索输出在 `logs/task-12-boundary.txt` |
| 拉起宿主的 Host、Networking 用例 | 正式全量与隔离复跑中通过（Networking 一次崩溃见 Remaining 第 2 条）；没有 (vi) |
| TASK-08 §4「TASK-12 再看」的 `A_slow_live_group…` | §9.2 改写 |
| §9.4 计数 | (iii) 0（全量 `GameplaySelfWait` 0）；(iv) 0；(v) 新交错型冲突 0；(vi) 0 |

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-12-build-test.txt` 开头）。
2. **全量**（`logs/task-12-build-test.txt`；运行前后 `yes` 进程都是 0）：
   - 总数 50 ＋ 80 ＋ 1701 ＋ 475 ＋ 969 ＋ 173 ＋ **4496** ＋ 166 ＝ 8110 ＝ 8101 ＋ 9 － 0。逐项对照 TASK-11：Application 4487 → 4496（＋9）；HttpApi 50、Pvf 80、Protocol 1701、Persistence 475、Infrastructure 969、Networking 173、Host 166 相等。
   - 通过 50 ＋ 80 ＋ 1701 ＋ 475 ＋ 967 ＋ 171 ＋ 4477 ＋ 166 ＝ 8087；失败 3；跳过 2 ＋ 2 ＋ 16 ＝ 20；8087 ＋ 3 ＋ 20 ＝ 8110。
   - **失败 3 条，比要求多 1 条**：那 2 条 `DimensionDungeonTests`，加上 TASK-11 的 `ScheduledTaskExecutorTests.Each_attempt_is_its_own_timer_job_and_the_retry_delay_leaves_the_executor_free`（`Expected: 2, Actual: 3`）。判为既有偶发失败，与本单元无关（`logs/task-12-sched-repeat.txt`）：
     - 它的容器只有 `AddScheduledTask`（→ `AddGameplayExecution`），没有事件分发；`GameplayTimerWork.cs` 只改了注释；
     - 在基准 `648fbc60` 的导出（`git archive` 到 scratchpad、工作区外构建）上同样复现：隔离 50 次失败 3 次（先一段 20 次失败 0 次，后一段 30 次失败 3 次；第 2 轮按审查非阻断项 3 更正），负载下 20 次失败 9 次；工作区隔离 20 次失败 3 次，负载下 20 次失败 8 次；
     - 机制（代码阅读，未单独取证）：`clock.Advance(2 分钟)` 同时触发重试延时与 1 分钟间隔的下一次到期，第三次尝试可能在 `StopAsync` 之前开始。
   - 第一次全量（scratchpad `t12/full1.txt`，同一份生产代码与测试）中失败只有那 2 条。
   - 表征测试 8 通过、2 跳过；挂起转储 0；`## exit 1` 来自上面 3 条失败。
   - 日志信号：
     - 为 0 的：`direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`；`Gameplay event:… failed`、`Gameplay startup:event/… failed`；`Domain event dispatch group … could not persist or scan`；§6 的 `enqueues no more: the gameplay executor has stopped`；TASK-11 的其余信号（`Gameplay timer:/startup:… failed`、`GameplayExecutorStoppedException` 等）。
     - 计数为 0 不等于没有发生：多数事件测试容器没有日志提供程序。N3（双日志恰 1 条）、N6（没有 `Gameplay event:… failed`）、N7（3 条 Information、没有 `could not persist or scan`）用记录型日志直接断言。
     - 非零的两项都与本单元无关：`Gameplay inbound:X failed` 3（`GameSellItem` 1、`GameSelectCharacter` 2，与 TASK-09～11 相同，用例故意注入）；`the drain tears it down` 3（TASK-10 的丢失关闭回调兜底，历次在 1～6 之间波动，TASK-11 为 2）。
   - **耗时比对**（与 `logs/task-11-build-test.txt` 逐条比，共同用例 8035 条，阈值 9 秒）：
     - 多出 ≥ 9 秒的 14 条：`SealedTreasureTests.Production_factory…` ＋16 s；`AltarLifecycleTests.Member_departure…(disconnected: True)` ＋15 s、`(False)` ＋14 s；`PvfDropItemPoolSourceTests.The_equipment_pool…` ＋14 s；`SouthernDaleTests.Every_declared_candidate…` ＋14 s；`GmPlayerDirectoryHttpTests.EveryQueryUsesCurrentPermission…` ＋13 s；`AltarLifecycleTests.Real_content_completes…(11007, 7, True)` ＋12 s、`(11007, 7, False)` ＋10 s；`GmPlayerSamplingHttpTests.OnlineListUsesOneCapturedSet…` ＋11 s；`GmPlayerDirectoryHttpTests.GmOnlyOverview…` ＋11 s；`GmPlayerDirectoryHttpTests.NamesAreStrict…` ＋10 s；`GmItemTooltipHttpTests.Tooltip…` ＋10 s；`HostShutdownOrderTests.Sigterm…` ＋9 s；`GmCeraHttpTests.Boundaries…` ＋9 s。
     - 隔离复跑全部 1～3 秒（`logs/task-12-slow-isolated.txt`），全部判为负载波动：没有 30 秒的 SQLite 忙等形状；`did not drain within` 为 0，没有 10 秒的释放排空；「事件作业排在测试作业之后」在隔离复跑中同样会出现，而隔离下都不慢。
     - 各项目「总时间」（TASK-11 → 本单元）：HttpApi 8.0 → 8.4 s；Pvf 12.9 → 12.9 s；Protocol 12.7 → 12.7 s；Persistence 27.0 → 23.9 s；Infrastructure 54.9 → 49.9 s；Networking 61.0 → 57.9 s；Application 2.66 → 2.76 min；Host 2.67 → 2.81 min。
     - 共同用例逐条耗时按项目求和（TASK-11 → 本单元）：Application 1248.4 → 1281.7 s；Host 970.8 → 1049.8 s；Networking 122.9 → 111.4 s；Protocol 40.9 → 45.0 s；Infrastructure 356.7 → 365.1 s；Persistence 24.5 → 21.4 s；Pvf 7.3 → 7.4 s；HttpApi 5.5 → 5.7 s。
3. **隔离复跑**（`logs/task-12-isolated.txt`）：Application 4496 条（4478 通过、2 失败即那 2 条、16 跳过）；Protocol 1701 条全部通过；Host 166 条全部通过；Networking 第一次**测试主机崩溃**（154 条通过后中止，Remaining 第 2 条），之后隔离 8 次、负载下 10 次都是 173 条（171 通过、2 跳过），没有再崩溃（`logs/task-12-net-crash.txt`）。没有超时形态的失败。
4. **新增测试 ×10**（`logs/task-12-new-x10.txt`）：9 条新增加上 2 条改写或包裹的冲突用例，隔离连续 10 轮，11 × 10 ＝ 110 次通过，0 失败，0 跳过；正式全量负载下 11 条全部通过。
5. **表征测试**：8 通过、2 跳过，断言与金样本未改。
6. **变异核对**（`logs/task-12-mutations.txt`；每项都用 scratchpad 备份，逐字节还原，`filecmp(shallow=False)` 相等，前后 sha1 都是 `2a170a3e…`；做完之后经探针轮构建与 `--no-incremental` 重新构建）：
   - M1 `DrainOnceAsync` 直接调用 `ProcessAsync`：N1、N2 红（N1～N8 与改写用例共 9 条红）；
   - M2 失败记录拆到另一个作业：只有 N3 红（`TryBegin` 与 `Fail` 的作业 ID 不同）；
   - M3 投影移出作业：N2 与改写用例红；
   - M4 来源一律 `Event`：只有 N4 红；
   - M5 以停止令牌入队：只有 N6 红；
   - M6 去掉守卫：只有 N8 红（在 5 秒等待处超时，报「没有抛出」）；
   - M7 去掉「执行器已停止」分支：只有 N7 红；
   - M8 守卫挪到事务检查之前：只有 `UsageQuotaTests:117` 红（`GameplaySelfWaitException`）。
7. **测试改动核对**：新增 9 条见 Changed（N ＝ 9）；既有 2 个文件的类别、差异核对与「旧断言 → 新断言」见 Changed。
8. **生产改动的边界与不变量**（`logs/task-12-boundary.txt`）：
   - `git diff --stat -- Server/src`：2 个文件，都在 Owned boundary 内；
   - `IGameplayQueue`：与基准相比只多 `DomainEventDispatcher.cs`（2 处），其余 29 个文件的命中数相同；
   - `SuppressFlow|RunDetached`：14 行，与基准逐行相同；
   - `groupGates`、`TransactionalExecutionGate` 仍在 `DomainEventDispatcher.cs`，留给 T-005；
   - **部分迁移期间的不变量**：等待事件作业的执行器外代码只有两处：各组循环（`DomainEventHostedService` → `RunAsync` → `RunGroupAsync`），以及测试直接调用的 `DrainOnceAsync`。它们等待时只持本组的组门，不持附录 F 的门。执行器上的作业从不等组门：`AssertNotOnExecutor` 在取组门之前拒绝（N8、M6），所以不成环。
9. **探针复跑**（`logs/task-12-probe-summary.txt`；原始记录 p1，`PROBE_LOG_PREFIX=task-12-probe`）：
   - **本族归零**：
     - §3.5 事件分发：fam.py 459／1902 → 17／17，改正后 0／0。17 条全部是「测试体在 `DrainOnceAsync` 返回后内联续行」：钩子与分发器帧之间隔着测试程序集的帧，分发器帧只经 `SetExistingTaskResult` 出现在栈上。其中 16 条是测试直接调用总线（`OnlineExpiredItemCleanupTests.Online_rollover…` 8、`SouthernDaleCompatibilityTests.Southern_online_day_reset…` 8），归 3.10；1 条是 `AchievementTitleBookTests` 在排空之后直接读称号簿的 P1b-read（给 T-004c 的信息）。由分发器自己发起的记录为 0（`residual.py`）；
     - 三个订阅者 (C) 栈顶：`<AddDomainSubscriber>b__2_1` 1／8 → 0／0，`ExpiredItemsNotificationSubscriber.HandleAsync` 2／5 → 0／0，`SouthernDailyRefillNotificationSubscriber.HandleAsync` 2／3 → 0／0。
   - **残留**：没有由分发器发起的残留，所以没有做排除复跑。`:149` 的 (C) 栈顶与 TASK-11 p2 相同，不属本族。
   - **本族以外各族**（TASK-11 p2 → 本单元 p1，记录／命中）：3.1 Received 1／1 → 1／1；3.1 Connected/Closed 0／0 → 0／0；3.2 1／1 → 1／1；3.3 79／114 → 79／114；3.4 0／0 → 0／0；3.6 0／0 → 0／0；3.7 868／1872 → 813／1660；3.8 12／172 → 10／124（改正后 0／0：10 条都以 `GmBootstrapService.StartAsync` 为最近入口，归 3.9）；3.9 147／251 → 151／283（改正后 161／407）；3.10 3509／10698 → 3513／10910（加上前述 16 条，改正后 3529／10926）。
   - **3.7 与 3.10 的变化**：订阅处理里经总线的命令，基准中按最外层入口规则算在 §3.5（最外层的非总线入口是分发器）；现在它们在执行器上，没有挪进 3.10：本轮 3.7 与 3.10 没有任何带分发器帧的记录。3.7 ＋ 3.10 的命中数：基准 1872 ＋ 10698 ＝ 12570，本轮 1660 ＋ 10910 ＝ 12570，相等。变化只在两族之间：Host 中没有测试帧、撞到 40 帧上限的栈，中间件帧是否留在上限内决定归 3.7 还是 3.10（`hostbus.py`：P2 截断记录 3.10 110 → 291 次，3.7 181 → 44 次）。
   - **还原证明**：快照前后 sha1 都是 `306db5b271eecfb72fb4e9083916a9f385049d58`，`cmp` 相等；`Server/src` 前后都没有未跟踪文件；`snapcheck` 1582／1582，相等 1580，不等的 2 个就是本单元的 2 个文件；`patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；`grep TestGameplay|GameplayProbe Server/src` 为 0；之后以 `--no-incremental` 重新构建，再跑正式全量。
   - 探针轮的失败不作为证据：只有那 2 条 `DimensionDungeonTests`。
10. **对账行**：
    - 附录 A 第 8 行（52 个订阅）：
      - Reliable 17：来源 Event／StartupRecovery → `event:<订阅 ID>`／`startup:event/<订阅 ID>` → N1（同提交）、N3（失败与重试）、N5（组内串行与退避期间继续），以及 `Each_fact…`、`Failed_reliable_handler…`、`Failed_handler_is_retained_as_terminal…`、`Worker_retries…`、`A_failure_to_record_retry_state…`、`Stop_cancels_inflight…`；
      - LiveOnly 22：同上 → N2（先提交再投影、同一 CMD），以及 `Expired_live_hints…`、`A_live_failure_is_consumed_once…`、`Restart_recovers…`、`UsageQuotaTests:117`、改写的 `A_slow_live_hint…`；
      - Direct 13：留在调用方 CMD 内，不入队 → N9；
      - 加法 17 ＋ 22 ＋ 13 ＝ 52；检索证据：`subcheck.py` 52 个注册，处理器文件不引用任何队列持有者；探针 §3.5 改正后 0。
    - 附录 A 第 11 行的事件部分：遗留 delivery → `startup:event/<订阅 ID>`（`StartupRecovery`）→ N4；M4 证明区分力。
    - 留给后续单元：
      - T-005：删除组门与准入门；
      - T-006：LiveOnly 投影改进事件 CMD 的发件箱，2 秒直发期限随之不再约束事件投影；
      - T-007：父链、同一 delivery 重试继承首次的链、深度超限的 delivery 以系统 CMD 落定（今天恒为新根、深度 0，`GameplayDepthExceededException` 只会被组循环当扫描失败退避）。
    - **AC-015**（每条都经执行器驱动：用例调用的是生产者方法 `DrainOnceAsync`／`RunAsync`，消费都在执行器上）：

      | 子项 | 用例 |
      |---|---|
      | Reliable 完成标记与业务写入同提交 | N1、`Each_fact_is_retained…`、`Business_write_and_all_event_rows_roll_back_together` |
      | 失败回滚后记录重试、退避与终态 | N3、`Failed_reliable_handler_rolls_back…`、`Failed_handler_is_retained_as_terminal…`、`Worker_retries_at_due_time…`、`A_failure_to_record_retry_state_backs_off…` |
      | LiveOnly 跳过与不补播 | N2、`Expired_live_hints…`、`A_live_failure_is_consumed_once_and_never_retried`、`Restart_recovers…`、`UsageQuotaTests:117` |
      | 启动扫描遗留 delivery | N4、`Restart_recovers…`、`Stop_cancels_inflight_reliable_work…` |
      | 订阅组内顺序与失败退避后的继续处理 | N5、`DomainEventSchedulingTests.A_synchronously_consuming_group…` |

### Remaining

1. **TASK-11 用例 `ScheduledTaskExecutorTests.Each_attempt…` 偶发失败**，基准上同样复现（Checks 2）。它在本单元边界外，没有改，请主控方另行安排修正（先确认意图，再决定是让推进时钟时不触发间隔到期，还是调整断言）。
2. **未关闭的取证缺口：Networking 隔离复跑中一次测试主机崩溃**（第 2 轮按审查非阻断项 2 补充）。异常是 `ObjectDisposedException: … 'SQLitePCL.sqlite3'`，栈只有原生回调帧 `rollback_hook_bridge_impl → SqliteTransaction.RollbackExternal → sqlite3_rollback_hook`，没有托管调用方。
   - **「与本单元无关」没有证据，也没有被排除**：Networking 的集成宿主经 `ApplicationModule` 注册了事件分发，本单元的代码在这些用例里运行。本单元改变的是事务所在的位置（作业里），没有改变事务的写法。
   - **下次需要取到崩溃时正在运行的用例**：后续单元的全量对照中再次出现时，用 `--blame`（或逐类分批）取得用例名，而不是只看总数。
   - 这个栈根不在 AGENTS.md 已记录的三条里，所以**没有归入那类已知偶发**，也没有追加进那张表。
   - 形状说明某个带着未结束 SQLite 事务的连接在句柄释放时触发了回滚钩子（可能是未释放、被 GC 回收的连接）。本单元没有新增任何连接或事务的生命周期路径：作业体逐字未动，事务都在 `await using` 里。但这只是代码阅读，不是不归因的明证。
   - 之后工作区隔离 8 次、负载下 10 次都没有复现；基准导出负载下 10 次也没有（该导出找不到 PVF，多跳过 9 条，环境不完全相同，复现力有限）。崩溃时正在运行的用例未能确定。
3. **VC-8 提前的范围扩大到事件 CMD**：22 个 LiveOnly 投影在作业里直发，受 2 秒期限约束，不读包的客户端会在 2 秒后被断开。T-006 之后消失。
4. **订阅组之间不再并发**（RQ-043）：一个慢的订阅处理会占住执行器。成就、任务进度、称号簿的单条时长由 M-1 按 `Gameplay event:… completed` 的 Debug 耗时观察（RQ-091）。
5. **首次加载**：事件作业可能早于 `GameServerStartupService` 的预热，在执行器上触发成就、称号目录的首次加载。预热清单归 TASK-13。
6. **双 Error 日志**：只在失败记录本身失败时出现（N3 第二部分），交 T-008。
7. **T-007 缺口**：重试继承链与深度超限落定（Checks 10）。
8. **「执行器已停止」那一行日志不带异常**（审查非阻断项 4）：`GameplayExecutorStoppedException` 在执行器消费循环本身故障时带 `InnerException`，这一行 Information 会把故障原因压成「已停止」。按设计执行器自己会记下故障；如果执行器那边不保证记录，交 T-008 收口日志时一并处理。
9. **改写用例里的 `Assert.Null(Transactions.Current)` 没有区分力**（审查非阻断项 5）：LiveOnly 投影的失败被 `LiveOnlyAsync` 吞成 Warning，这条断言失败时用例照样通过。「live 投影不持有业务事务」由 N2 的 `InTransaction` 记录守着，不算在 `A_slow_live_hint…` 名下；Checks 10 的对账里，这个用例只作为「慢组推迟而不丢失」的证据。
10. **未提交**：没有 commit，也没有 stash。

### 第 2 轮（审查非阻断项，2026-10-22）

审查结论 approved（`reviews/task-12-result-review.md`）。本轮只补非阻断项，生产代码没有改动（`DomainEventDispatcher.cs` sha1 仍是 `2a170a3e…`）。

- **非阻断项 1：「一条 delivery 一个作业」补用例守着**（裁定第 2 条）。
  - 新增 N10 `A_drain_of_several_due_deliveries_runs_one_event_cmd_per_delivery`（`DomainEventDispatchTests.Executor.cs`）：
    - 三次发送（owner a、b、c），共 9 条到期 delivery（counter 3、audit 3，属 Reliable；live 3，属 LiveOnly）；
    - 一次 `DrainOnceAsync()`：处理 9 条；作业数 ＝ 处理条数 ＝ 9；每组各 3 个作业；9 次 `TryBegin` 的作业 ID 互不相同；
    - 业务结果：counter 计数 3，live 投影 3 次。
  - 变异 M9（工作区，scratchpad 备份）：把整批放进一个作业，CMD 类型取第一条 delivery 的。只有 N10 变红（`Expected: 9`），其余 29 条通过，与审查者 X1「35／35 通过」的结论一致。
  - 还原：逐字节还原，`filecmp(shallow=False)` 相等；前后 sha1 都是 `2a170a3e5d66042710efd14b1b910c72b1dc5b6b`。之后以 `--no-incremental` 重新构建，0 个警告，0 个错误（`logs/task-12-mutations.txt` 的「round 2」段）。
- **非阻断项 3**：调度偶发的基准计数更正为「隔离 50 次失败 3 次」（Checks 2）。
- **非阻断项 2、4、5**：写进 Remaining 第 2、8、9 条。
- **新增测试 ×10**（`logs/task-12-new-x10.txt` 的「round 2」段）：10 条新增加上 2 条冲突用例，共 12 条，隔离连续 10 轮，12 × 10 ＝ 120 次通过，0 失败。
- **第 2 轮只重跑了 `Dfo.Server.Application.Tests` 整个项目**（`logs/task-12-application-r2.txt`，detailed 日志，`--blame-hang-timeout 5m`）：
  - 理由：本轮只在 Application.Tests 里新增 1 个测试方法，生产代码与其余测试项目没有改动，它们的结果沿用正式全量 `logs/task-12-build-test.txt`；
  - 运行环境：开始时 `yes` 进程为 0，结束时为 8。这 8 个进程启动于 14:21:53（运行时段为 14:20:30～14:22:44），父进程是 1，本单元那时没有任何启动负载的脚本在跑，所以它们不是本单元起的，我没有结束它们。也就是说，这一轮最后约 50 秒是在外部负载下跑的；
  - 结果：4497 条 ＝ 4496 ＋ 1；通过 4479，失败 2（就是那 2 条 `DimensionDungeonTests`），跳过 16；4479 ＋ 2 ＋ 16 ＝ 4497；挂起转储 0；日志信号全部为 0（含 `GameplaySelfWait`、`Gameplay event:… failed`、`could not persist or scan`）。
- **全量总数更新**：8110 ＋ 1 ＝ 8111 ＝ 8101 ＋ 10 － 0。按项目写：50 ＋ 80 ＋ 1701 ＋ 475 ＋ 969 ＋ 173 ＋ **4497** ＋ 166 ＝ 8111，其中 Application 取第 2 轮，其余 7 个项目取正式全量。N ＝ 9 ＋ 1 ＝ 10。

### 主控方补充（提交前）

- 第 2 轮补上 N10。审查的非阻断项 1 已落实，变异 M9 证明这条用例有区分力。
- Application 第 2 轮整项目运行的最后约 50 秒，处在外部限时负载下。这批 `yes` 进程由并行进行的调度偶发诊断启动，主控方确认它们已经自行结束。负载只会提高偶发失败的概率，不会让本该失败的用例变成通过，所以这次运行全部通过的证据仍然有效。
- `ScheduledTaskExecutorTests.Each_attempt…` 的偶发失败是 TASK-11 遗留的问题，由主控方另行诊断并安排修正单元，不计入本单元。

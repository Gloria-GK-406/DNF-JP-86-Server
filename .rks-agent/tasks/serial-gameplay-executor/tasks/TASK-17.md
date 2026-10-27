# TASK-17 — T-004c1：门断言与 `SendAsync` 翻转（含冲突用例处置）

基准提交 `f13e612a`（TASK-16 之后，T-004b 全部完成）。本单元是 T-004c 拆成两个单元后的第一个，拆分理由见 Detailed Plan §0；第二个单元的边界提纲见文末「后续单元边界提纲」。

## Task Unit Contract

- Revision: 2（第 1 轮快审 changes_required 后的边界修订，见文末「主控方处置（第 1 轮评审后）」）
- Outcome and references：
  1. **`ICommandBus.SendAsync` 语义翻转**（Plan「`ICommandBus.SendAsync` 的含义」、过渡期规则 1、Plan T-004 行；RQ-003 过渡期；TASK-06 §7 待办 4a）：在执行器外调用时，入队到同一容器的执行器并等待完成；在任一执行器的作业上调用时，照今天的路径内联执行。不存在「执行器不在就内联」的分支（Plan「执行器的生命周期，不留旁路」）。
  2. **门断言**（Plan 过渡期规则 2；TASK-06 §7 待办 4b；`t004c-entry.md` 第 1 项）：附录 F「删除」栏中作业会获取的每一种门，在执行器外获取、或在执行器上获取时需要等待，都立即抛出程序错误；执行器上的违规黏滞到作业令牌。范围与例外见 §2。
  3. **违规取证汇**：每一次门断言违规都发出一条诊断事件；测试侧记录到文件，用于证明「一次全量运行中零违规」（Plan T-004 完成条件的门断言一半；RQ-061 一半在后续单元）。
  4. **冲突用例处置**（`task-07-conflicts.md` §12、§13 的 77 条，F-11、F-56）：在本单元因翻转或门断言失败的，按既定处置执行，逐条记录「触发 → 处置 → 旧断言 → 新断言 → 保护意图 → 后继用例」；仍通过的按 §4.4 规则顺延。另判定并处理 `UserServiceTests` 中直接调用观察者的非冲突方法（F-18）、Host 中 10 个直接调用总线的测试类（F-15）。
  5. 门断言与翻转的反例用例（RQ-072 式），以及变异核对。
- Owned boundary：
  - **生产代码**（只限下列文件）：
    - 新增 `Server/src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs`：门断言辅助、`GameplayGateWaitException`、诊断事件源（§2.2、§3）。
    - `Server/src/Dfo.Server.Application/Commands/CommandBus.cs`：翻转（§1）。`CommandDispatchingExtensions.cs` 只在注册需要时改动。
    - 门的获取点（§2.1 表）：`Application/OnlineUsers/OnlineUserExecutionGate.cs`、`Application/OnlineUsers/SelectedSessionTransitionGate.cs`、`Application/Commands/Pipeline/TransactionalExecutionBehavior.cs`（只改其中的 `TransactionalExecutionGate`）、`Application/Effects/EffectOperationLedger.cs`、`Application/Events/JoustMaintenanceService.cs`、`Protocol/Game/TitleBook/TitleBookSlice.cs`（只改 `AchievementTitleProjectionGate`）、`Protocol/Game/Party/PartyNotificationSlice.cs`（只改参与者门与 `PartyDeliveryStream.Gate` 的获取）、`Protocol/Game/Weakness/WeaknessRecoveryService.cs`。
    - **Revision 2 追加**：`Protocol/Game/Party/PartyTownProjectionDelivery.cs`（只改 `:65`、`:197` 两处 `PartyDeliveryStream.Gate` 的获取）。
    - `Application/Scheduling/ScheduledTaskHostedService.cs`：只在取并发槽之前加一行反向断言（§2.3，以待裁定第 2 条为准）。
  - **测试**：
    - 77 条冲突用例所在的文件（清单见 `task-07-conflicts.md` §2、§7～§13）；
    - `Server/tests/Dfo.Server.Application.Tests/OnlineUsers/OnlineUserServiceTests.cs`（F-18）；
    - 其他因翻转或门断言失败、且符合 §4.3 规则的测试文件，只允许 TASK-06 §2.6 白名单内的形状改动；
    - 新增：`Application.Tests/Execution/GameplayGateAssertionTests.cs`、`Protocol.Tests/Execution/ProtocolGateAssertionTests.cs`（目录可按项目惯例调整）、`Application.Tests/Commands/CommandBusExecutorTests.cs`；
    - 新增 `Server/tests/Support/GameplayAssertionRecorder.cs`，并在链接 `Support` 的 6 个测试项目与 `Dfo.Server.HttpApi.Tests` 的 csproj 中加 `Compile Include … Link`。
  - **工具、文档与证据**：
    - `verification/probe-tools/` 新增 `violations.py`（汇总取证汇记录）；探针 `patch.py` 只在文本对不上时改匹配模式，不改挂点集合；
    - `verification/task-07-conflicts.md` 追加 §14；`verification/follow-ups.md` 更新 F-11、F-15、F-17、F-18、F-56 的状态；
    - `verification/logs/task-17-*.txt`。
  - **不改**：RQ-061 的挂点（`DomainTransactionScope`、5 处裸 `new DomainTransaction()`、附录 F「改为执行器内访问」的类）；发件箱；`GameplayJob`／`GameplayJobHost`／`GameplayExecutor`；`TestGameplay` 的来源规则；`DomainEventDispatcher`；表征测试与金样本；`spec.md` 的 RQ／AC／附录；`AGENTS.md` 与 `Docs/`。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **分段诊断**（不作正式证据，只用于填「触发」一栏；落盘 `logs/task-17-flip-only.txt`、`logs/task-17-gate-on.txt`）：先只落地翻转跑一次全量，再加上门断言跑一次全量，都带 `--blame-hang-timeout 2m`。§14 每条的「触发」取自这两次的差异。
  3. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-17-build-test.txt`。
     - 总数：TASK-16 为 53 ＋ 80 ＋ 1703 ＋ 476 ＋ 969 ＋ 173 ＋ 4510 ＋ 183 ＝ 8147（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host）。本单元 ＝ 8147 ＋ N － D，逐项目写出加法；N、D 逐条列名（Theory 按实际运行的例数另计一栏）。
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20 条（如有冲突用例本身是跳过的，单独说明）；挂起转储 0。
     - **表征测试** 8 通过、2 跳过，`Characterization/` 目录与金样本不改。
     - **日志信号**：`GameplayGateWait`、`must happen inside a gameplay job`（门断言的执行器外违规）只能出现在反例用例的捕获日志里，全量输出中为 0；`GameplaySelfWait` 0；`did not drain within` 0；`GameplayExecutorStoppedException` 0；新增的 `Gameplay bus:… failed` 计数，逐条归到「用例故意让命令失败」；`direct send deadline exceeded`、`the drain tears it down` 只记数。非零项逐条说明。
     - 耗时与内存只作信息（RQ-091 用户修订）。多出 ≥ 9 秒的用例照 TASK-16 的口径列出、隔离复跑一次并归类。
  4. **零违规取证**（门断言一半）：`violations.py` 汇总取证汇文件，按最外层测试类分组。**反例用例类以外的记录为 0**；反例用例类的记录数与这些用例预期触发的次数相等（证明取证汇在全量中确实工作）。输出落盘 `logs/task-17-violations.txt`。
  5. **冲突用例处置记录**：`task-07-conflicts.md` §14 逐条写「用例｜触发（翻转／门断言／仍通过）｜处置｜旧断言 → 新断言 → 保护意图｜后继用例」；写出新分布的加法（总数仍为 139）。被删除的用例，其保护意图由哪条反例或后继用例承接，逐条写明；承接不了的写「意图随门删除（T-005）」并说明理由。
  6. **非冲突测试的形状改动**：逐文件列出，`t07/diffcheck7.py f13e612a --lines` 证明测试方法集合与断言行多重集合不变，白名单外的片段逐处说明。F-18 的方法数与 F-15 的 10 个类在此列名。
  7. **新增测试 ×10**：本单元新增的用例隔离连续 10 次、全量负载下 1 次，全部通过，落盘 `logs/task-17-new-x10.txt`。
  8. **变异核对**（`logs/task-17-mutations.txt`；每项 scratchpad 备份，逐字节还原后 `cmp`，全部做完后 `--no-incremental` 重建）：M1～M7 见 §5.3，每一项都要有反例用例变红。
  9. **探针复跑**（`logs/task-17-probe-*.txt`，汇总 `logs/task-17-probe-summary.txt`；五个项目一轮 p1；三项还原证明同 TASK-16 §1.6）：3.10 在五个项目中为 0（t004c-entry 第 2 项「翻转后应当消失的量」）；3.3 为 0；P3 的 (A)／(B)／(C) 为 0；其余族与 TASK-16 对照，变化逐项归因。
  10. **生产改动边界**（`logs/task-17-boundary.txt`）：`git diff --stat -- Server/src` 只含 Owned boundary 列出的文件；§2.1 表中各门在 `Server/src` 中不再有 `WaitAsync(` 形式的获取；`GameplayGateAssertion` 的调用点逐个列出，与 §2.1 表逐行对上。
  11. **隔离复跑**（`logs/task-17-isolated.txt`）：Protocol、Application、Networking、Host 各一次；Host 中 F-15 的 10 个类全部通过并列名。
- Dependencies：TASK-16（已提交，`f13e612a`），连同 TASK-08～TASK-15 的全部结果。
- Result review：required — 翻转改变的是 `ICommandBus` 这个所有后续单元都依赖的契约，门断言改的是 9 个文件里的共享原语；冲突用例处置会删除或改写几十条测试，删错一条，它守着的保护意图就悄悄丢了，而删除后的全量照样是绿的，测试检验不出来。T-004c2、T-005 都把本单元的「零违规」当进入条件，这里归因错了，后面会在错误的前提下删锁。门断言与翻转本身有反例和变异能暴露，但处置记录、触发归因与例外范围（§2.3）属于判断性产出，需要独立审查。
- Task-specific authorization：
  - 冲突用例按既定处置删除或改写（`task-07-conflicts.md` 各节的「处置决定」），不需要逐条再问；处置与既定决定不同时（例如既定「删除」改为「改写」或反之），在 Result 中单列说明理由。
  - 非冲突测试只允许 TASK-06 §2.6 白名单内的形状改动，以及把 `TestGameplay` 无容器重载改成 `services` 重载（§4.3）。需要改断言的非冲突测试，停下回报。
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 门断言在生产路径上违规：取证汇记录的栈没有测试帧，或最外层是入口驱动的用例（真实 dispatcher、socket、HTTP、托管服务 tick、事件分发）。这说明生产里还有执行器外持门者，或作业结束后仍持有租约；
    2. 表征测试或金样本需要改动；
    3. 翻转或门断言需要改 Owned boundary 以外的生产文件；
    4. 一条失败无法归入 §4 的任何一类。
  - 探针复跑与变异核对期间允许临时改动 `Server/src`，用后按三项还原证明处理。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 拆分与顺序

**结论：T-004c 拆成两个单元，翻转与门断言放在第一个单元（本单元），RQ-061 判据与结构判据放在第二个单元。** 每个单元一次提交，提交时全绿可运行。

**为什么翻转不能排在门断言与 RQ-061 之后。** 今天测试直接在执行器外调用总线的量是 3.10：3513 条记录／10203 次命中（`entry-reconciliation.md` §1.1）。这些调用经过管线：
- `OnlineUserExecutionBehavior`、`AccountExecutionBehavior`、`TransactionalExecutionBehavior` 取门；
- `UnitOfWorkBehavior` 开写事务（`scope.Begin()`）。

翻转之前，这些取门和开写事务都发生在执行器外。所以只要先开门断言或 RQ-061，就会有上千条测试在管线里违规，没有一个中间状态是绿的。而这些调用按 R-2 是入口用例，不该被包进 `TestGameplay`。

**这与 Plan「翻转是最后动作」不冲突。** Plan 过渡期规则 1 的「最后」，指排在全部持门者迁入之后：后台服务、事件分发器、观察者、GM/HTTP 都已迁入，生产代码中在执行器外调用总线的位置为 0（`t004c-entry.md` 第 1、2 项）。前提已经成立。门断言与 RQ-061 是用来检验翻转之后状态的，所以排在翻转之后，或与翻转同一次提交。

**为什么翻转与门断言放在一起，而不各成一个单元。**
- 冲突用例大多是「测试在执行器外持门，同时经总线发命令」（§4.2 B 类）。只翻转、不开门断言时，它们表现为死锁：作业在门上等测试线程，测试线程在等作业。只能靠挂起超时找出来，一条要等几分钟。门断言开着时，同一条用例会立刻以带门名的异常失败，诊断成本低得多。
- 证据不混：执行时按 Required check 2 分两段跑，「触发」一栏取自两段的差异，所以合并为一个单元不会让「哪一步弄坏了哪条测试」混在一起。

**为什么 RQ-061 不放进本单元。** RQ-061 要逐方法确定附录 F 各类的写入口（14 个类，约 140 个公开成员），还要做分类判据、结构判据与反例，工作性质与门断言不同（`t004c-entry.md` 第 3 项、F-12、F-57）。它在翻转之后才能开启，与本单元没有反向依赖。分开以后，第二个单元的全量就是 Plan「门断言与 RQ-061 判据在一次全量运行中零违规」那一次运行。

### 1. `SendAsync` 翻转

**语义**：

```text
SendAsync(command, ct):
  if GameplayJob.IsOnExecutor:            // 任一执行器的作业上（过渡期规则；T-008 末删除）
      return InlineAsync(command, ct)      // 即今天的方法体：嵌套检查、新 DI scope、管线
  else:
      return queue.RunAsync(new GameplayWork<TResult>(
          GameplayShardKey.World,
          GameplayCallChain.NewRoot(<来源，待裁定第 4 条>),
          "bus:" + <命令类型名>,
          token => InlineAsync(command, token)), ct)
```

- `CommandBus` 构造器增加 `IGameplayQueue`。`AddCommandDispatching` 已经调用 `AddGameplayExecution`，生产组合与全部用 `AddCommandDispatching` 的测试容器都有执行器；33 个测试替身总线不受影响；没有任何地方手写 `new CommandBus(`。
- **不留旁路**：不判断「执行器是否已启动」，也不在解析不到队列时退回内联。执行器停止后，执行器外的调用收到 `GameplayExecutorStoppedException`。
- `IsOnExecutor` 表示「在任一执行器上」。所以 867 条无容器整方法包裹（TASK-07）里，测试体在自建的辅助执行器上，调用总线时照旧内联；这 867 条按 TASK-07 的筛选条件本来就不调用入口，翻转对它们没有影响。
- 嵌套检查 `DispatchDepth` 不变：命令处理器内再调用总线时，它在作业上，走内联路径，照旧抛「Nested command dispatch is not allowed」。
- 取消：令牌同时交给 `RunAsync`（作业还在排队时取消，作业体不执行）和作业体。
- 异常：作业体抛出的异常原样交还调用方（执行器用 `SetException`），`Assert.ThrowsAsync` 的行为不变。执行器会多记一条失败日志（F-30 的双 Error，归 T-008），计入 Required check 3 的日志信号。
- **翻转以后，执行器外调用方的 AsyncLocal 状态不再随命令流动**：作业在消费循环的上下文里运行（`SuppressFlow`）。生产中执行器外的调用方为 0，这一点不影响生产；测试里在作业外设帧再调用总线的写法会因此失败，处置见 §4.2 D 类。

**对测试辅助的影响**：
- `TestGameplay` 不改。带容器的包裹区域（`RunAsync(services, …)`）与执行器外的总线调用落在同一个容器执行器上，彼此串行。
- 测试里「区域外调用总线（R-2）」改为入队等待，测试代码不用改。
- 风险：一个测试同时开着两个执行器（无容器包裹在辅助执行器 E_t 上，总线调用在容器执行器 E_c 上），两边的作业可能争用同一把门。单执行器的生产环境里没有这种情形，开了门断言后它会以「执行器上需要等待」报出。处置是把无容器包裹改为 `services` 重载，让两边落到同一个执行器（§4.3 第 2 条）。
- 不释放容器的测试会留下一个空转的消费循环，直到进程结束。这只是内存问题，按用户偏好不作阻断，在 Result 中记数即可。

### 2. 门断言

#### 2.1 覆盖范围与挂点

| 门 | 获取点 | 改法 |
|---|---|---|
| `UserExecutionGate<TKey>` 基类，覆盖其全部派生：`OnlineUserExecutionGate`、`AccountExecutionGate`、`SelectedSessionTransitionGate`、`TownAreaProjectionGate`、`DungeonProjectionGate`、两塔的投影门与进入门，以及没有使用者的 `UserExecutionGate<CharacterId>` | `AcquireAsync` 入口；`Entry.TryAcquire` 锁内 | 入口：执行器外 → 抛出。锁内：`CanEnterImmediately` 为假时，不挂等待者，撤回 `references++`，然后抛出「需要等待」。`AcquireManyExclusiveAsync` 与派生类的 `AcquireAsync` 都经过基类，不另挂 |
| `SelectedSessionTransitionGate` 的借用 | `BorrowOrAcquireAsync` 入口 | 执行器外 → 抛出（在执行器外借用，说明借用帧流出了作业）。借用不等待，没有「需要等待」一支 |
| `TransactionalExecutionGate` | `AcquireAsync` | 用 `GameplayGateAssertion.Enter(semaphore, …)` 取代 `await WaitAsync(ct)` |
| `AchievementTitleProjectionGate`（64 路） | `EnterAsync` | 同上 |
| 队伍参与者门 | `PartyProjectionStreamRegistry.EnterParticipantsAsync` 的循环 | 同上；循环中途抛出时，按原 `catch` 释放已取得的门 |
| `PartyDeliveryStream.Gate` | `PartyNotificationSlice.cs` 的两处 `stream.Gate.WaitAsync` | 同上 |
| `EffectOperationLedger` 每连接门 | `DispatchAsync` 中的 `ledger.Gate.WaitAsync` | 同上 |
| Weakness `deliveryGate` | `WeaknessRecoveryService.SendCurrentAsync` | 同上 |
| Joust `ticks` | `JoustMaintenanceService.TickAsync` | 同上 |

最后三项没有被探针 P3 挂过（`t004c-entry.md` 第 1 项「具备（静态）」）。所以开断言之前，先静态检索测试里直接调用 `EffectOperationLedger.DispatchAsync`、`SendCurrentAsync`、`JoustMaintenanceService.TickAsync` 的位置，列进 `logs/task-17-boundary.txt`。目前已知只有冲突用例 `EffectOperationReplayTests:371`。

#### 2.2 形状（`Abstractions/Execution/GameplayAssertions.cs`）

```csharp
public static class GameplayGateAssertion            // 过渡期；T-005 随门删除
{
    public static void Entering(string gate);         // 不在执行器上 → 记诊断，抛 GameplayOffExecutorException
    public static Exception WouldWait(string gate);   // 在执行器上 → token.Fault(new GameplayGateWaitException(gate, chain))，记诊断，返回供 throw
    public static void Enter(SemaphoreSlim gate, string name, CancellationToken ct);
        // ct.ThrowIfCancellationRequested() → Entering(name) → if (!gate.Wait(0)) throw WouldWait(name)
}
public sealed class GameplayGateWaitException(string gate, GameplayCallChain chain) : InvalidOperationException(...);
```

- 放在 Abstractions，因为只有这里能调用令牌的 `Fault`（internal），而 Protocol 的门也要用它。
- 执行器外复用已有的 `GameplayOffExecutorException`，消息形如 `Acquiring gate <名字> must happen inside a gameplay job.`。门名用「类型名[:实例键类型]」，日志可读即可。
- **黏滞**：执行器上的违规写入令牌的 `ProgramError`，即使调用方 `catch` 吞掉了，作业照样以它失败（TASK-04 的 N-1 机制）。执行器外没有令牌可黏，靠诊断事件兜底（§3）。
- 「需要等待」的判定只看当下能不能立即进入，不区分门被谁占着：单消费者下，执行器上的作业永远不该等门。
- 取消语义保持今天的先后：先检查令牌，再断言。所以一个已取消的调用仍然抛 `OperationCanceledException`，不会变成违规。
- 违规类型派生自 `InvalidOperationException`，与 `GameplaySelfWaitException` 一致。代价是 `Assert.ThrowsAny<InvalidOperationException>` 会把它当成预期异常收下，所以零违规的证据以取证汇为准，不以「全量绿」为准。

#### 2.3 不纳入断言的两项（待裁定第 2 条）

`DomainEventDispatcher.groupGates` 与 `ScheduledTaskHostedService` 的并发槽在执行器外持有，并且在持有期间等待作业（F-17）。它们是生产者私有的串行化手段：
- 前者是私有字段，只在 `DrainOnceAsync(string)` 里获取，而那里第一步就是 `GameplayJob.AssertNotOnExecutor`；
- 后者是 `ExecuteAsync` 里的局部变量，只由 `Task.Run` 启动的尝试获取。

作业拿不到它们，所以不会出现「作业等门、门的持有者等作业」的环。这正是门断言要防的东西。附录 B「生产者的入队侧」一行已经写明了这个例外。

**处理**：不挂正向断言，改用反向断言：
- 组门已有反向断言；
- 并发槽在 `slots.WaitAsync` 之前补一行 `GameplayJob.AssertNotOnExecutor("Taking a scheduled task concurrency slot")`。

两者仍在 T-005 按附录 F 删除。不提前删除，以免把 T-005 的行为改动（组内串行改由执行器保证）混进本单元的证据。

同理不纳入的：`PasswordComputationLimiter` 的槽（附录 B，已有反向断言）；`JoustRuleService.loadGate`、`CharacterStatRuleService._initialization` 与其他内容源初始化锁（附录 F「保留」）；短 `lock(sync)`（「保留」）；`NotificationRetryQueue`（是队列，不是等待原语，随 T-006 删除）。

### 3. 违规取证汇

- **生产侧**：`GameplayAssertions.cs` 持有一个 `DiagnosticListener("Dfo.Server.Gameplay.Assertions")`。每次违规时，在抛出之前同步 `Write("Violation", new { Kind, Operation })`；`Kind` 本单元取 `gate-off-executor`、`gate-wait`。没有订阅者时 `IsEnabled` 为假，不产生开销。这不是开关：断言照常抛出，事件只是旁路记录。RQ-061 在后续单元复用同一个源，新增 `write-off-executor`。
- **测试侧**：`tests/Support/GameplayAssertionRecorder.cs` 用 `[ModuleInitializer]`（加 `#pragma warning disable CA2255` 并注明理由）订阅 `DiagnosticListener.AllListeners`。环境变量 `DFO_GAMEPLAY_ASSERTIONS` 指向一个目录时，每条违规追加一行 JSON 到 `<目录>/<程序集名>.jsonl`，内容为 kind、operation，以及 `Environment.StackTrace` 中 `Dfo.` 开头的帧。变量未设时什么都不做。链接进全部 7 个会碰到门的测试项目。
- **为什么不只看全量是否通过**：执行器外的违规可能被生产代码的 `catch (Exception)` 吞掉（通知投递按收件人捕获异常），也可能被测试的 `ThrowsAny` 收下。黏滞只对执行器上的违规有效。
- **`violations.py`**：读取全部 jsonl，按最外层测试类分组，减去反例用例类，剩余必须为 0。报告反例类的记录数，与用例中预期的违规次数对照。
- 备选方案是沿用探针工具，在抛出处临时挂点，不留生产代码，代价是每次取证都要打补丁并做还原证明（待裁定第 3 条）。

### 4. 冲突用例与测试侧处置

#### 4.1 执行顺序中的测试侧

1. 落地翻转（§1），按 Required check 2 跑一次全量，记录翻转引起的失败与挂起。
2. 落地门断言（§2）与取证汇（§3），再跑一次全量。
3. 按 §4.2 处置 77 条中失败的，按 §4.3 处理其余失败，按 §4.4 记录仍通过的。
4. 正式全量与取证（Required check 3、4）。

#### 4.2 77 条按机制分五类

分类依据 `task-07-conflicts.md` 各节的机制描述。加法：22 ＋ 38 ＋ 7 ＋ 4 ＋ 6 ＝ 77。

| 类 | 条数 | 成员 | 预计触发 | 处置原则 |
|---|---:|---|---|---|
| A 门直测（K1） | 22 | `OnlineUserExecutionGateTests` 4、`SelectedSessionTransitionGateTests` 10、`TransactionalCommandSerializationTests` 3、`MultiAccountExecutionTests` 2、`CommandBusTests` 3 | 前 19 条多为门断言（测试线程直接取门）；`SelectedSessionTransitionGateTests:59` 与 `TransactionalCommandSerializationTests` 为翻转 | 争用类**删除**，保护意图由 §5 的门断言反例承接（「执行器上需要等待即抛」取代「排队、FIFO、取消不泄漏」），门本身在 T-005 删除。`DirectCommands_RemainConcurrent` 删除：它的断言与新不变量相反，由 `CommandBusExecutorTests` 的串行用例承接。`CommandBusTests` 3 条（租约在异常／取消后释放）保留：测试线程上「验证租约已释放」的那次取门，按 T4 包进 `TestGameplay(services, …)`，断言不变 |
| B 测试在执行器外取门，与入口或投递争用 | 38 | TASK-06 Protocol 24（`PartyTownProjectionDeliveryTests` 2、`TownProjectionHandlerTests` 1、`DungeonLoadingProjectionDeliveryTests` 2、两塔并发 2、`PartyCommandHandlerTests` 4、`LeaveDungeonTests` 4、`SelectDungeonHandlerTests` 1、`PartySelectDungeonHandlerTests` 2、`TitleBookProtocolTests` 3、`TitleBookMutationHandlerTests` 1、`MarriageProposalCodecTests` 1、`PartyNotificationTests` 1）；Application 12（`SelectCharacterLifecycleTests:56`、`ReplayStateSnapshotTests` `:31`／`:60`／`:177`、`OnlineExpiredItemCleanupTests:113`、`CommonExperienceEffectTests` `:191`／`:219`／`:281`、`NetworkMessageDispatcherTests:118`、`MoveItemSpaceHandlerTests:310`、`DomainEventDispatchTests:159`、`JoustIntegrationTests:743`）；`CleanupNotificationDeliveryTests.Existing_owner…`；`LoginSessionQueuedCommandTests.Replaced…` | 门断言，部分为翻转（死锁） | 保护意图若是「门内／门间的次序本身」，就**删除**，理由写「机制随门删除」。若是「某个业务结果在交错下仍然正确」，就**改写**为两条顺序 CMD 的断言：先让「干扰」作为一条 CMD 执行完，再执行被测命令；断言不变或按新结构说明。`CleanupNotificationDeliveryTests` 那一条按 F-56 倾向**经执行器驱动、断言不变**：`DeliverAsync` 包进 `TestGameplay(services, …)`，因为生产中它只在作业里被调用 |
| C 观察者／生命周期门交错（TASK-10 顺延） | 7 | `SelectCharacterLifecycleTests` `:236`／`:261`／`:288`／`:365`、`OnlineUserServiceTests` `:30`／`:67`／`:519` | 门断言（观察者在测试线程取过渡门） | **删除**，后继用例已在 TASK-10 给出（W6、W7、P1、P2，`task-07-conflicts.md` §8）；`:67` 没有后继（测锁序），理由写「机制随门删除」 |
| D 请求级帧建在作业外 | 4 | `EffectOperationReplayTests` `:159`、`:229`、`:253`、`:345` | 翻转（帧不再随命令流动） | **改写**：帧的建立与总线调用放进同一个 `TestGameplay(services, …)` 区域，总线在作业上内联，帧可见；断言不变。`:159` 测的是继承流的降级，帧语义整体归 T-008（同 `:269`）。如果改写后仍要跨流才能表达，就按 TASK-09 主控方裁定第 3 条的先例改归 T-008，在 Result 中写明 |
| E 经入口的 TCS／门闩交错 | 6 | `CommonExperienceEffectTests:164`、`ReplayStateSnapshotTests:206`（TASK-07 记作 `:206`）、`DungeonObjectQueueTests:86`、`EffectOperationReplayTests:371`、`IllusionTowerPublicFlowTests:149`、`AuthenticationConsumptionTests.ExactCredentialBinds…` | 翻转（第二条命令排在被卡住的作业后面，等不到探针），部分在 RQ-061 才触发 | **改写**为顺序断言，或在有后继时**删除**：`:149` 的后继是 TASK-11 的 `A_sweep_tick_after_the_old_runs_return_was_due…`。`DungeonObjectQueueTests:86` 是两个直接调用的发起方真并发、没有门，预计本单元不失败，顺延到下一单元 |

**记录**：每条写成 §14 的一行。删除的写「承接者」；改写的写「旧断言 → 新断言 → 保护意图」，口径同 `task-07-conflicts.md` §9～§12。

#### 4.3 77 条以外的失败

1. **F-18 `UserServiceTests`**（`OnlineUserServiceTests.cs`）中直接调用观察者的非冲突方法：观察者在生产中只在生命周期作业里运行，所以把它的直接调用按 T5 包进 `TestGameplay(services, …)`，断言不变。逐个方法列名，并记入 Required check 6。如果某个方法靠门制造交错，它就是新判定的冲突，按 C 类处置，并单列「新判定」。
2. **双执行器争用**（§1 的风险）：把无容器包裹改为 `services` 重载，断言不变。
3. **作业外设帧再调用总线**（D 类的同形，不在清单里的）：按 D 类改写，断言不变；超出白名单的停下回报。
4. **F-15 Host 的 10 个类**：预计不用改，翻转后由 GmTestHost 的执行器执行。失败的按 1～3 条处理。
5. 其余失败按停止条件 4 处理。

#### 4.4 仍通过的冲突用例

按拆分原则第 5 条：只在使它失败的那个单元执行。
- 本单元仍通过的，顺延到 T-004c2，在 §14 标明。
- T-004c2 结束时仍通过的，逐条判定：已不再在执行器外取门、开写事务或写附录 F 入口的，记「已不冲突、保留」；仍然测门本身的（例如 `TransactionalCommands_DoNotRunTheirHandlersConcurrently` 在翻转后可能因串行而通过），交给 T-005，随门删除（Plan T-005 行「改写或删除测门与测交错的用例」）。

### 5. 新增测试

#### 5.1 门断言（`GameplayGateAssertionTests`，Protocol 侧另建一类）

每种门各有：
- 执行器外获取 → 抛 `GameplayOffExecutorException`，并发出一条诊断事件（测试直接订阅 `DiagnosticListener`）；
- 执行器上无争用 → 照常取得、释放；
- 执行器上争用 → 抛 `GameplayGateWaitException`。另有用例在作业体里 `catch` 吞掉这个异常，断言作业仍以它失败（黏滞）；
- 违规之后同一把门的计数不泄漏：释放占用者后，下一次获取能立即成功；
- 已取消的令牌仍抛 `OperationCanceledException`，不发诊断事件；
- 借用路径：作业内持有并借用，不违规；借用帧流到执行器外（`GameplayJob.RunDetached`）再借用，抛出；
- `AcquireManyExclusiveAsync` 在第二个账号上争用：抛出，第一个账号的租约已释放。

争用的构造方式：在同一个执行器上，用 `RunDetached` 之外的合法方式先占住门。例如，作业 A 取门后不释放就结束（模拟租约泄漏），作业 B 再取。用例全部带 `WaitAsync` 超时，防止断言失效时挂起。

反向断言：在作业内调用 `DomainEventDispatcher.DrainOnceAsync(string)`、取调度并发槽（经一次托管服务尝试的作业体），都抛 `GameplaySelfWaitException`。

#### 5.2 翻转（`CommandBusExecutorTests`）

- F1：执行器外调用 → handler 内 `IsOnExecutor` 为真，`Current.CommandType` 为 `bus:<类型名>`，返回值不变；
- F2：作业内调用 → 内联，handler 看到的是外层作业（同一个根 ID），队列没有新作业；
- F3：handler 抛出的异常实例原样交还；
- F4：排队期间取消 → `OperationCanceledException`，handler 未执行；
- F5：handler 内再调用总线 → 照旧报嵌套错误；
- F6：执行器停止后，执行器外调用 → `GameplayExecutorStoppedException`，handler 未执行（证明没有内联旁路）；
- F7：多个执行器外调用并发 → handler 最大并发为 1（承接被删的 `DirectCommands_RemainConcurrent`／`TransactionalCommands_DoNotRunTheirHandlersConcurrently` 的位置）。

#### 5.3 变异（Required check 8）

| 变异 | 应当变红 |
|---|---|
| M1 去掉 `UserExecutionGate` 入口的执行器外断言 | 基类与派生类的执行器外用例 |
| M2 去掉 `TryAcquire` 锁内的「需要等待」分支 | 争用用例（以超时失败） |
| M3 `GameplayGateAssertion.Enter` 不调用 `Fault` | 黏滞用例 |
| M4 `Enter` 跳过 `Wait(0)` 的检查 | `SemaphoreSlim` 类门的争用用例 |
| M5 总线去掉执行器外分支（一律内联） | F1、F6、F7 |
| M6 总线去掉内联分支（一律入队） | F2、F5（自我等待） |
| M7 不发诊断事件 | 诊断事件断言 |

### 6. 为什么生产行为不变

- 生产中执行器外调用总线的位置为 0（`entry-reconciliation.md` §1.3），所以翻转后的执行器外分支在生产中不会被走到；执行器上走的仍是今天的方法体。
- 生产中全部门的获取都在作业里（`t004c-entry.md` 第 1 项），且单消费者下作业之间不重叠，所以门断言在生产中不会被触发。若被触发，就是停止条件 1。
- 唯一新增的运行期动作是一次 `IsOnExecutor` 判断、一次 `Wait(0)` 和一次 `IsEnabled` 检查，可以忽略。

## 后续单元边界提纲（T-004c2，不在本单元执行）

- **RQ-061 挂点**（F-12、F-16、F-57）：
  - 工作单元入口：`DomainTransactionScope.Begin(readOnly: false)`；三处裸写：`CharacterStateAccess` 的 `ChangeAsync`（易失写，`:94`）、`AloneAsync`（`:162`），以及 `CreatureRuntimeDomainService.cs:274` 的回退分支。两处只读的一次性事务（`CharacterStateAccess:52`、`ItemContainers:181`）**不挂**。
  - 不在 `DomainTransaction` 的构造或提交处挂：测试中有 109 处直接 `new DomainTransaction(` 测存储，Plan 写明它们不受判据影响。
  - 加一条源码扫描判据：`Server/src` 中 `new DomainTransaction(` 只能出现在已列出的 6 处，并且每处标明读或写；配反例。
  - 附录 F 写入口：「改为执行器内访问」的 13 个类，加上 `OnlineCharacterRuntimeDomainService`，共 14 个。逐个公开／内部成员分为读或写，写成员第一句调用 `GameplayWriteAssertion`（复用 §3 的诊断源，kind 为 `write-off-executor`）。分类表放在判据旁，用反射判据要求每个成员都已分类，新增成员未分类即红；每个类至少一条反例。
  - **只拦写，不拦读**（F-57 的建议）。理由：RQ-061 的字面是「写入口」；RQ-010 约束的是改状态、写库、发包；执行器外的读在生产中只有遥测路径的 `IsBlocked`／`IsSuperseded`（附录 B），在测试中是轮询读（Networking 27 条、卡牌 1 条）、T2 的 `world.Runs.Find` 与 T-006 的 130 条。拦读要先改写这些测试，却换不来保护：这些类保留短锁，读到的是一致的快照。
  - **启动期例外不设**（F-16）：判据只挂在工作单元入口与附录 F 写入口，建表与迁移在 Persistence 单例构造时直连 SQLite，不经这两类入口。附录 B 对应行改写为「不需要」，属于事实性修改。
  - 发件箱登记的那一半归 T-006。
- **结构判据**（TASK-06 §7 待办 4d，TASK-04 主控方补充）：只有 `GameplayExecutor` 能派生 `GameplayJobHost`；`ExitJob`、`Fault`、`ProgramErrorOf` 改为实例方法，并断言令牌属于本执行器；配反例。
- **`:274` 反例**：注入一个不是 `DomainTransactionScope` 的访问器，在执行器外调用，断言被拒。
- **AC-003 第 3 条**（F-13）：线级测试，一个连接持续灌包时，队列里属于它的待执行入站 CMD 始终 ≤ 1，另一个连接照常执行。
- **VC-8 提前的完成核对**（F-14）。
- 本单元顺延过来的冲突用例，以及 §4.4 的最终判定。
- **一次正式全量**：门断言与 RQ-061 的取证汇在同一次运行中都为零（Plan T-004 完成条件，也是 T-005 的进入条件）。另做 T-004 的收口对账，更新 `follow-ups.md`。
- Result review：required（理由同本单元：判据的挂点粒度与分类表属于判断性产出，判据错了，T-005 会在错误前提下删锁）。

## 待裁定

1. **拆分与顺序**：T-004c 拆成 c1（翻转＋门断言＋冲突处置，本单元）与 c2（RQ-061＋结构判据＋收口）。翻转先于两类判据，理由见 §0。若坚持字面「翻转最后」，唯一绿的做法是三者同一次提交，也就是不拆。
2. **组门与调度并发槽**：不挂正向门断言，改为反向断言（作业永不获取），仍在 T-005 删除（§2.3）。这是对 Plan 过渡期规则 2「每种门」的一处收窄，请确认。备选是本单元先删除，即 T-005 范围前移。
3. **取证汇**：生产侧加一个 `DiagnosticListener` 事件源（无订阅时零开销，不是开关），测试侧用 `ModuleInitializer` 记录到文件（§3）。备选是沿用探针的临时挂点，不留生产代码，但每次取证都要打补丁并做还原证明。
4. **执行器外 `SendAsync` 的作业身份**：建议 CMD 类型 `bus:<命令类型名>`，来源沿用 `GameplayCallSource.Connection`，与 `TestGameplay` 一致；生产中不会走这条路径。备选是新增一个来源枚举值，但那会触及 RQ-050 的来源种类口径。
5. **仍通过的冲突用例**：本单元只处置失败的，仍通过的顺延 c2；c2 结束时仍通过者，判为「已不冲突、保留」或交 T-005 随门删除（§4.4）。请确认。
6. **F-57**：RQ-061 只拦写（c2 预定）。若要连读也拦，c2 必须先处置轮询读、T2 与 T-006 的 130 条读，范围会显著扩大。
7. **B 类「删除还是改写」的取舍**：本计划按保护意图判定（§4.2）。若主控方希望 B 类一律改写、不删除，请明确；改写量大约会翻倍。

## 主控方裁定（执行前）

1. **拆分与顺序**：接受。T-004c 拆为 c1（本单元：`SendAsync` 翻转 ＋ 门断言 ＋ 77 条冲突用例处置）和 c2（RQ-061 判据、结构判据、`:274` 反例、AC-003 第 3 条、VC-8 核对，以及零违规全量）。
   - Plan 过渡期规则 1 说翻转是「最后动作」，指的是排在全部持门者迁入之后。这个前提已经由 TASK-16 证明成立：生产代码中在执行器外调用总线的位置为 0。所以先翻转、再开 RQ-061，与规则一致。
   - 下一单元编号为 TASK-18（c2）。
2. **组门与调度并发槽**：不挂门断言，改为反向断言，即保证作业永远拿不到它们。组门已有这条断言，并发槽补一行。删除仍归 T-005。
   - 这收窄了 Plan 规则 2「每种门」的范围。理由是：这两种原语属于附录 B「生产者的入队侧」，由生产者在执行器外持有，等待的是作业完成；它们不是玩法门。
   - 在 Result 中登记这一收窄。
3. **取证汇**：接受在生产侧使用 `DiagnosticListener`。没有订阅者时零开销，它不是开关，也不改变行为。测试侧的记录器放在 `tests/Support`，由环境变量启用。
4. **执行器外 `SendAsync` 的作业身份**：CMD 类型用 `bus:<命令类型名>`，来源沿用 `Connection`。生产中这条路径的调用位置为 0，只有测试会走到。不新增来源枚举值。
5. **翻转后仍然通过的冲突用例**：顺延到 c2。到 c2 结束时仍通过的，逐条判定是保留，还是交给 T-005 随门一起删除。
6. **F-57**：RQ-061 只拦写、不拦读，与 RQ-061 原文的「写 SQLite 的工作单元开启 … 玩法状态写入口」一致。
7. **B 类冲突用例**：按保护意图决定删除还是改写。删除的每一条都要写明承接它的后继用例（名字必须能检索到）；没有后继的，写明保护意图为什么已由规格消除。

**停止条件**：按本文件原样执行。此外，表征测试金样本出现任何变化，都必须停下回报。

## Result（第 2 轮：第 1 轮快审 changes_required 之后，合同 Revision 2，2026-10-23）

本节原位取代第 1 轮的 Result。第 1 轮的正式全量、违规取证与变异日志改名保留为 `logs/task-17-build-test-r1.txt`、`task-17-violations-r1.txt`、`task-17-mutations-r2.txt`（再早一轮是 `-r1`）。

### Outcome

- **`SendAsync` 翻转**（§1，裁定第 4 条）：
  - 在任一作业上调用，照旧内联；
  - 在执行器外调用，以 `bus:<命令类型名>`、来源 `Connection` 的新根作业入队到同一容器的执行器并等待；
  - 没有「执行器不在就内联」的分支：执行器停止后，执行器外调用收到 `GameplayExecutorStoppedException`（F6）。
  - **第 2 轮**：入口先检查 `DispatchDepth`，再判断是否在执行器上。带着处理器深度的泄漏流因此恢复为「Nested command dispatch」，包括 `RunDetached` 内的调用（F8）和作业结束后仍在跑的续体（F9）。不在处理器内的泄漏流仍归 TASK-18（F-58）。
- **门断言**（§2）：附录 F「删除」栏的全部门已改为：在执行器外获取就抛异常；在执行器上需要等待就抛异常，并黏滞到作业令牌。
  - **第 2 轮**补上了第 1 轮漏挂的 `PartyTownProjectionDelivery` 两处流门（`:65`、`:197`），并改为全仓按门的字段名与类型检索：`Server/src` 中，附录 F「删除」栏的每个 `SemaphoreSlim` 都只经 `GameplayGateAssertion` 获取（`logs/task-17-boundary.txt`）。
- **组门与调度并发槽**：按裁定第 2 条只加反向断言，这是对规则 2「每种门」的收窄，在此登记。删除仍归 T-005。
- **违规取证**（第 2 轮全量）：
  - 记录 38 条，全部属于 4 个反例类，各类记录数与预期相等；
  - 反例类以外的记录 0，没有测试帧的记录 0。
- **冲突用例**：
  - 77 条的触发：翻转 15、门断言 55、仍通过 7。
  - 处置：删除 41、改写 29、顺延 c2 7。
  - 总分布 7 ＋ 52 ＋ 1 ＋ 1 ＋ 8 ＋ 41 ＋ 29 ＝ 139，见 `task-07-conflicts.md` §14（第 1 轮评审后原位修订）。
- **全量**：8162 条，只有那 2 条 `DimensionDungeonTests` 失败，跳过 20 条，挂起转储 0。表征测试 8 通过、2 跳过，金样本未改。

### Changed

- **生产代码**：`git diff --stat f13e612a -- Server/src` 共 11 个文件，＋76 −25，另新增 `GameplayAssertions.cs`。全部在 Revision 2 的 Owned boundary 内。第 1 轮已有的改动见下面「第 1 轮改动」；第 2 轮新增：
  - `Protocol/Game/Party/PartyTownProjectionDelivery.cs`：`:65`、`:197` 两处 `await stream.Gate.WaitAsync(...)` 改为 `GameplayGateAssertion.Enter(stream.Gate, nameof(PartyDeliveryStream), ct)`。
  - `Application/Commands/CommandBus.cs`：
    - `SendAsync` 开头先判断 `DispatchDepth.Value != 0`，成立时返回 `ValueTask.FromException(NestedDispatch())`；
    - 这样与原先异常落在返回的任务里一致，不在调用时同步抛出；
    - 消息文本未变，抽成 `NestedDispatch()`。
  - `Protocol/Game/TitleBook/TitleBookSlice.cs`：第 1 轮被改成 CRLF 的那一行（原 `:225`，`messages.InsertRange(...)`）还原为 LF；文件 diff 只剩 `EnterAsync` 与 `using` 的 7 行。
- **第 1 轮改动**（仍成立）：
  - 新增 `Abstractions/Execution/GameplayAssertions.cs`：`GameplayGateAssertion.Entering`／`WouldWait`／`Enter`，诊断源 `Dfo.Server.Gameplay.Assertions`，`GameplayAssertionViolation`，`GameplayGateWaitException`。
  - `UserExecutionGate` 基类：入口断言，锁内「需要等待」撤回引用后在锁外抛出。
  - 过渡门的借用入口断言。
  - 以下各门用 `Enter` 取代 `WaitAsync`：`TransactionalExecutionGate`、称号门、队伍参与者门、`PartyNotificationSlice` 两处流门、效果账本、Weakness、Joust。
  - 调度并发槽的反向断言。
- **测试**（第 2 轮）：
  - 恢复 `MultiAccountExecutionTests.cs`，按作业形态改写。
    - `All_accounts_are_locked_once_and_released_when_execution_fails`：作业里以 `[2,1,2]` 失败，执行体内门里正好 2 个账号；之后的作业立即取得账号 1 与 2。
    - `Cancellation_while_waiting_for_second_account_releases_first_account` 改名为 `A_second_account_held_by_an_earlier_job_fails_at_once_and_releases_the_first`：作业 A 持账号 2 不放；作业 B 以 `[2,1,2]` 执行，断言抛 `GameplayGateWaitException`、执行体没跑；作业 C 立即取得账号 1。
    - 新增成功路径的去重用例 `A_repeated_account_is_taken_once_and_the_group_runs`。
  - `PartyTownProjectionDeliveryTests` 新增本路径的两条反例：`A_read_repair_finding_its_stream_gate_taken_fails_the_job_at_once`（`:197`，另证明释放后同一调用照常投递）与 `A_moved_party_refresh_finding_its_stream_gate_taken_fails_the_job_at_once`（`:65`）。
    - 这两条路径在流门之前先取参与者门，所以执行器外的调用在参与者门处就被拒绝；本路径只能补「执行器上争用」的反例。
  - `CommandBusExecutorTests` 新增 F8、F9。
  - `ReplayStateSnapshotTests:206`：
    - 读开始前，先把 3262＝7 播种在账号 99 名下（`SeedShared` 是 CMD，读被卡住期间排不进去）；
    - 读的中途，在 `barriers.Release` 之前用 `fixture.Sql` 把这一行改归账号 1；
    - 四条断言原样，撤销「意图收窄」。
  - 第 1 轮的测试改动保持不变：新增 3 个反例类、取证汇记录器链接进 7 个项目、其余冲突处置、F-18 与同形的 1 条。
- **文档与证据**：
  - `task-07-conflicts.md` §14 原位修订：删除 41、改写 29；`PartyTownProjectionDeliveryTests` 两条删除用例的后继改指本路径反例；`MultiAccountExecutionTests` 改为改写；`:206` 改写说明重写。
  - `follow-ups.md` F-11 的数字更新；F-58 加注「第 2 轮已堵住带 `DispatchDepth` 的一类」。
  - 日志 `logs/task-17-{build-test,violations,mutations,new-x10,boundary,diff-check}.txt` 为第 2 轮重跑结果。

### Why（本地决定与偏离）

1. **D 类那条既定「改写」、实际删除**（`EffectOperationReplayTests.An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus`）：主控方已接受（「回报后」第 1 条）。这条用例本身无法恢复，因为翻转后执行器外的调用不再携带帧，但它守的意图有一部分已重新落地：第 2 轮的 F8／F9 证明，带着处理器深度的泄漏流被拒绝。其余部分归 TASK-18。
2. **`NetworkMessageDispatcherTests:118`** 在请求自己的 CMD 内、工作单元之前封锁。「先封锁再派发」不成立，因为已封锁连接的请求在分派时被静默丢弃。主控方已接受，交独立评审核对。
3. **B 类删改的判定**按裁定第 7 条：「门内或门间的次序本身」删除，「业务结果在交错下仍然正确」改写为先后两条 CMD。
4. **MultiAccount 的第二条改名**：「等待中被取消」在执行器上不再存在，新名字说的是新形态下的意图。
5. **`CommandBus` 的嵌套检查返回失败的 `ValueTask` 而不同步抛出**，以保持原先「异常落在返回的任务里」的形态（原检查在 `async` 方法内）。
6. **变异 M6（总线一律入队）现在只有 F2 变红**：F5（处理器内再派发）已由新的提前检查拦下，不再依赖内联分支；M8 专门守这条提前检查。
7. 其余本地决定同第 1 轮：
   - 反向断言的反例沿用 `DomainEventDispatchTests` 已有的组门用例，新增并发槽一条；
   - 诊断载荷用具名 record；
   - 门名格式为「类型名:键类型」；
   - F-18 的 9 个方法全部包裹，使探针 3.3 归零；
   - 每种门的覆盖矩阵：`UserExecutionGate` 与事务门测满，`SemaphoreSlim` 门共用 `Enter`，派生门只测执行器外。

### 与裁定不同的地方

- **`git diff --check` 不是「无输出」**：仍报 11 行，都是 CRLF 文件（`TitleBookSlice.cs`、`TitleBookProtocolTests.cs`，基准中这两个文件几乎全是 CRLF）里本单元新增行行尾的 CR。
  - 这些行按文件原有的换行约定写成 CRLF，不是改了未变动行的换行符；第 1 轮 `:225` 那种情形已经还原。
  - 用 `git -c core.whitespace=cr-at-eol diff --check` 核对：无输出，退出码 0。
  - 要让普通 `git diff --check` 无输出，只能在 CRLF 文件里写 LF 行，造成混合换行，所以没有这样做。请主控方裁定。

### Checks（第 2 轮）

1. **构建**：第 2 轮全量开头的 `--no-incremental` 构建 0 警告、0 错误；变异核对结束后的 `--no-incremental` 重建同样是 0 警告、0 错误。
2. **门断言全量**（`logs/task-17-build-test.txt`，detailed 落盘，取证汇开启，2026-10-23 00:15:44～00:18:52，开始时 swap 已用 7629 MB）：
   - **总数**：53 ＋ 80 ＋ 1703 ＋ 476 ＋ 969 ＋ 173 ＋ 4525 ＋ 183 ＝ 8162 ＝ 8147 ＋ 59 － 44。
     - Protocol：1703 ＋ 14 ＋ 2 － 16 ＝ 1703。
     - Application：4510 ＋ 43 － 28 ＝ 4525。
     - 其余项目不变。
     - 新增 59 例：
       - Application 43 ＝ `CommandBusExecutorTests` 9 ＋ `GameplayGateAssertionTests` 33 ＋ `MultiAccountExecutionTests` 净增 1；
       - Protocol 16 ＝ `ProtocolGateAssertionTests` 14 ＋ `PartyTownProjectionDeliveryTests` 2。
       - MultiAccount 由 2 条变为 3 条：1 条改名、1 条新增。
     - 删除 44 例：41 个方法加上 3 个 Theory 各多出的 1 例。
       - Application 28 ＝ 25 个方法 ＋ 3；
       - Protocol 16 ＝ 16 个方法。
       - 方法名见 `logs/task-17-diff-check.txt` 第 2 节与 §14.1。
   - **结果**：
     - 通过 53 ＋ 80 ＋ 1703 ＋ 476 ＋ 967 ＋ 171 ＋ 4507 ＋ 183 ＝ 8140；
     - 失败 2，就是那 2 条 `DimensionDungeonTests`；
     - 跳过 2 ＋ 2 ＋ 16 ＝ 20；
     - 8140 ＋ 2 ＋ 20 ＝ 8162；挂起转储 0。
   - **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本未改。
   - **日志信号**：
     - 为 0：`GameplayGateWait`、`must happen inside a gameplay job`、`GameplaySelfWait`、`did not drain within`、`GameplayExecutorStoppedException`、`direct send deadline exceeded`、`Nested command dispatch`。
     - `Gameplay bus:… failed` 8 条，都是 F-15 的 Host 用例故意让命令失败：`SendGmMailCommand` 4、`Act` 2、`RegisterAccountCommand` 1、`CreateCharacterCommand` 1。
     - `Gameplay inbound:… failed` 3 条、`Gameplay gm:… failed` 2 条，与 TASK-16 相同。
     - `the drain tears it down` 1 条，只记数。
   - 耗时对照本轮没有重跑（不在受影响清单内），第 1 轮的对照见 `logs/task-17-slow-isolated.txt`。
3. **零违规取证**（`logs/task-17-violations.txt`）：
   - 记录 38 条：
     - `GameplayGateAssertionTests` 21（执行器外 8、需要等待 13）；
     - `ProtocolGateAssertionTests` 14（10、4）；
     - `PartyTownProjectionDeliveryTests` 2（需要等待）；
     - `MultiAccountExecutionTests` 1（需要等待）。
   - 21 ＋ 14 ＋ 2 ＋ 1 ＝ 38，逐类与预期相等。
   - 反例类以外 0，没有测试帧 0，脚本退出码 0。
4. **冲突用例处置记录**：见 §14。
   - 按类：A 22 ＝ 15 删 ＋ 2 改 ＋ 5 顺延；B 38 ＝ 17 删 ＋ 21 改；C 7 ＝ 7 删；D 4 ＝ 1 删 ＋ 3 改；E 6 ＝ 1 删 ＋ 3 改 ＋ 2 顺延。
   - 合计：删 41 ＋ 改 29 ＋ 顺延 7 ＝ 77。
5. **测试改动核对**（`logs/task-17-diff-check.txt`）：
   - 逐方法统计：改写的冲突方法 28 ＋ 改名 1、删除 40（另 1 条 `[Dfo.Server.Testing.PvfFact]` 写法没被识别，按名字检索确认，合计 41）、非冲突的改动 10、未改 319、问题 0。
   - 新方法 4 个都在登记名单内（MultiAccount 2、PartyTown 2）。
   - 10 个非冲突方法的断言行多重集合不变，白名单外片段 0。
6. **新增与改写用例 ×10**（`logs/task-17-new-x10.txt`）：
   - 每轮 Application 81 例（新增反例与全部改写冲突用例、F-18 所在的 `UserServiceTests` 全类、`RejectedRepeatedLogin`）、Protocol 31 例、Persistence 1 例。
   - 连续 10 轮全部通过，共 1130 次通过。第 2 轮全量的负载下，这些用例也都通过。
7. **变异核对**（`logs/task-17-mutations.txt`，M1～M9，每项先备份、再逐字节还原并 `cmp`；前后 `git diff -- Server/src` 的 sha1 都是 `840f4a75…`；最后 `--no-incremental` 重建，0 警告、0 错误）：
   - M1（去掉入口的执行器外断言）：红 10 例。
   - M2（去掉锁内「需要等待」分支）：红 10 例，含 MultiAccount 的 B 作业，都以超时失败。
   - M3（不调用 `Fault`）：红 6 例。
   - M4（跳过 `Wait(0)` 检查）：红 10 例，含 PartyTown 两条。
   - M5（总线一律内联）：F1、F4、F6、F7 变红。
   - M6（总线一律入队）：F2 变红。
   - M7（不发诊断事件）：红 30 例。
   - **M8**（去掉 `SendAsync` 的提前嵌套检查）：F8、F9 变红。
   - **M9**（`PartyTownProjectionDelivery` 两处改回 `WaitAsync`）：PartyTown 两条反例变红。
8. **生产改动边界**（`logs/task-17-boundary.txt`，第 2 轮按字段名与类型做全仓检索）：
   - diff 只有 Revision 2 Owned boundary 内的 11 个文件，加上新文件。
   - `Server/src` 中的全部 `.WaitAsync(` 逐行归类：信号、计时器或任务等待；裁定第 2 条的组门与并发槽；附录 B 的口令槽；附录 F「保留」的内容初始化锁。
   - 附录 F「删除」栏的门字段（`stream.Gate`、`ledger.Gate`、`participantGates`、`deliveryGate`、Joust `ticks`、称号门 `gates[]`、`TransactionalExecutionGate.gate`）在全仓只经 `GameplayGateAssertion` 获取。
   - `GameplayGateAssertion` 的调用点 13 处：第 1 轮 11 处加 PartyTown 2 处。
9. **`git diff --check`**：见上面「与裁定不同的地方」。加 `core.whitespace=cr-at-eol` 时无输出。
10. **探针与隔离复跑**：不在本轮受影响的清单内，沿用第 1 轮结果（`logs/task-17-probe-summary.txt`、`logs/task-17-isolated.txt`）。第 2 轮的改动不涉及探针挂点集合；`PartyTownProjectionDelivery` 的流门在第 1 轮也没有探针挂点。

### Remaining

1. 顺延 c2 的 7 条冲突用例：`TransactionalCommandSerializationTests` 2、`CommandBusTests` 3、`DungeonObjectQueueTests:86`、`AuthenticationConsumptionTests.ExactCredentialBinds…`。
2. F-58 中不带 `DispatchDepth` 的泄漏流（带着已失效作业令牌、不在处理器内）归 TASK-18。
3. c2 的读残留输入见 `logs/task-17-probe-summary.txt`（第 1 轮探针）。
4. 组门与调度并发槽的删除归 T-005（F-19）。
5. `git diff --check` 的 CRLF 行尾，待主控方裁定。
6. 未提交：没有 commit，也没有 stash。

## 主控方处置（回报后）

1. **D 类那条由改写转为删除，接受。** 翻转后执行器外的总线调用一律成为新根作业，「继承已关闭帧」在本单元无法同流构造，原样保留只会是一条红用例。
2. **F-58 不留到 T-008，并入 TASK-18（c2）。** 泄漏流与真正的执行器外调用可以区分：`RunDetached` 把 `Running` 置空，而作业结束后仍在跑的续体看到的是一个已失效的令牌（`IsActive == false`）。门的入口已经因 `Active is null` 把泄漏流当作执行器外调用拒绝，只有总线的执行器外分支会把它当成普通新根。TASK-18 在总线的执行器外分支加一条判定：流上带着已结束作业的令牌即程序错误（不入队），并补反例，恢复被删用例的保护意图（泄漏流不能降级成可信直发）。理由：改动只有一个分支，生产中执行器外的总线调用为 0，现在补比等到 T-008 改帧模型便宜。
3. 本单元其余两处偏离（`NetworkMessageDispatcherTests:118` 的封锁时点、`ReplayStateSnapshotTests:206` 的意图收窄）接受，交独立评审核对。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict `changes_required`，全部按 `task-rework` 处理，Spec 边界不变，合同升到 Revision 2：

1. **【阻断】`PartyTownProjectionDelivery.cs:65`、`:197` 两处 `PartyDeliveryStream.Gate.WaitAsync` 漏挂门断言。** 同一把门（同一 `PartyProjectionStreamRegistry`），属于附录 F「删除」栏。修订 Owned boundary 纳入该文件；两处改为 `GameplayGateAssertion.Enter(...)`；补本路径的反例（或把现有流门反例扩到本路径），并修正 §14.1 中 `PartyTownProjectionDeliveryTests` 两条删除用例的后继指向。边界检索改为按门的字段名与类型在全仓检索（如 `Gate.WaitAsync`，以及附录 F 类型清单上的 `.WaitAsync(`），不再只看计划列出的文件；结果落到 `logs/task-17-boundary.txt`。
2. **`MultiAccountExecutionTests` 的保护意图没有承接者。** 原写的后继测的是 `SelectedSessionTransitionGate.AcquireManyExclusiveAsync`，是另一个实现。按作业形态改写两条（作业 A 持账号 2 不放；作业 B 以 `[2,1,2]` 执行 `AccountExecutionBehavior`，断言 `GameplayGateWaitException`；作业 C 能立即取得账号 1），另加一条成功路径的去重用例。文件恢复（不再删除），§14.1 改为「改写」。
3. **`ReplayStateSnapshotTests:206` 账号余额一半恒真。** 在 `barriers.Release` 之前用 `fixture.Sql` 中途写入账号资源 3262＝7，四条断言保持原样；§14.1 撤销「意图收窄」的记载。
4. **总线执行器外分支跳过嵌套派发检查。** `CommandBus.SendAsync` 在 `IsOnExecutor` 判断之前先检查 `DispatchDepth.Value != 0`，泄漏流与 `RunDetached` 内的嵌套都恢复为「Nested command dispatch」；补反例两条（`RunDetached` 内调用、作业结束后带着 DispatchDepth 的续体调用）。F-58（泄漏流带着已失效作业令牌、但不在处理器内）仍归 TASK-18。
5. **`TitleBookSlice.cs:225` 的换行符改动**还原为 LF。

受影响的 Required checks 重跑：Release 构建；门断言全量一次（detailed 落盘）＋ `violations.py`；新增与改写用例隔离连续 10 次；变异核对补一项（去掉第 4 条的提前检查，反例变红）；`diff --check` 无输出。Result 原位更新为第 2 轮，数字的加法重写。

## 主控方处置（第 2 轮回报后）

- **`diff --check` 改按 `core.whitespace=cr-at-eol` 口径判定，接受。** 基准中 `TitleBookSlice.cs` 248 行里有 247 行是 CRLF，新增行沿用文件约定；普通口径报出的 11 行都是新增行行尾的 CR，不是改动了原有行的换行符。`cr-at-eol` 口径下无输出。

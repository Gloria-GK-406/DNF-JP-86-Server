# TASK-19 — T-004c2b：`GameplayJobHost` 结构判据、AC-003 第 3 条、VC-8 核对与 T-004 收口对账

基准提交 `0c9c59c8`（TASK-18 之后：RQ-061 写断言、泄漏流拒绝与零违规全量已提交）。范围取自 `tasks/TASK-18.md` 文末「后续单元边界提纲（TASK-19 = T-004c2b）」整节，按当前代码细化；与提纲不同的几处见 Detailed Plan §0。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **`GameplayJobHost` 结构判据**（RQ-072；TASK-06 §7 待办 4d；TASK-04 主控方补充与 Remaining 2；F-36 后一半）：
     - 判据：在生产程序集中，`GameplayJobHost` 只有 `GameplayExecutor` 一个派生类。判据写成接收类型列表的函数，配反例。
     - 归属断言：`ExitJob`、`Fault`、`ProgramErrorOf`、`ChainOf` 由 `protected static` 改为 `protected` 实例方法，入口断言 `ReferenceEquals(token.Owner, this)`，不属于本执行器就抛程序错误。四个方法各配一条反例。
     - 执行器对这几个方法的调用改为实例形态。深度超限时给「流上正在运行的作业」记程序错误（当前用 `JobRunningAnywhere` 加 `Fault`），这条路径的处理见待裁定第 1 条。
  2. **AC-003 第 3 条**（RQ-005；F-13；`t004c-entry.md` §7）：一条线级测试，覆盖「一个连接持续灌包时，队列里属于它的待执行入站 CMD 始终不超过 1 条，另一个连接的入站 CMD 照常执行」。测试经真实 TouchSocket、真实插件、真实 dispatcher 与真实执行器运行。
  3. **VC-8 提前的完成核对**（F-14；TASK-06 §7 4f；`t004c-entry.md` §7 第 1 行）：
     - 静态证明 `Server/src` 中已接线的传输写入只有 `TouchSocketNetworkMessageSender.WriteAsync` 一处。这一处在作业上一律套 `ExecutorSendDeadline`，与 CMD 来源无关。
     - 按来源（入站、生命周期、定时、事件、GM）各给一条作业内直发的证据。
     - 结论写进收口对账；F-14 改为「已核对，T-006 之后消失」。
  4. **T-004 收口对账**：新建 `verification/t004-closure.md`。
     - 逐项对照 Plan T-004 行的交付、覆盖栏中的 RQ 与 T-004 完成条件，以及 TASK-08～TASK-18 的 Result、Remaining、主控方裁定与处置，与 `follow-ups.md` 交叉核对。
     - 对每一项写明：已完成（证据）／过渡态，移交给谁（F 编号）／未满足，由本单元补足还是阻断 T-005（判定规则见 §5.3）。
  5. **`follow-ups.md` 收尾**：
     - F-13、F-14 关闭；F-36 的结构判据一半关闭，AGENTS.md 一半仍归 T-009；
     - T-004 范围内仍未关闭的事项，逐条确认去向；
     - 收口对账中新发现的事项追加新编号（从 F-63 起）。
  6. **一次零违规正式全量**：口径与 TASK-18 相同。取证预期与 TASK-18 的 71 条逐类相同（§6.2）；7 条顺延冲突用例的结果与 `task-07-conflicts.md` §15 的判定一致。这次运行与 TASK-18 那次一起，作为 T-005 进入条件「门断言与 RQ-061 判据零违规」的证据。
- Owned boundary：
  - **生产代码**（只限两个文件）：
    - `Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs`：`GameplayJobHost` 的四个方法改为实例方法并加归属断言；视待裁定第 1 条的结果，新增一个不带令牌参数的 `FaultRunningJob`；同步修改相关 XML 注释。
    - `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs`：只改对这几个方法的调用形态（`:51`、`:61`～`:63`、`:279`、`:283`）。
  - **测试**：
    - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayJobHostStructureTests.cs`：结构判据及其反例、四个归属断言的反例。反例用的派生宿主类定义在这个文件里。
    - `Server/tests/Dfo.Server.Networking.Tests/Integration/InboundExecutorWireTests.cs`：新增一条用例，以及测试内的 `IGameplayQueue` 计数装饰器。`Server` 夹具只加「装上装饰器」那一处。已有 4 条用例不改。
  - **文档与证据**：
    - 新建 `verification/t004-closure.md`；
    - `verification/follow-ups.md`；
    - `verification/logs/task-19-*.txt`；
    - VC-8 扫描、对账引用核对等一次性脚本放在 scratchpad，脚本全文附在对应日志末尾（与 TASK-18 `read-residue` 的做法相同）。
  - **不改**：
    - 门断言、RQ-061 写断言、泄漏流断言及它们的挂点；
    - `GameplayJob` 的公开静态成员（`IsOnExecutor`、`Current`、`AssertNotOnExecutor`、`RunDetached*`）与 `GameplayJobToken`；
    - dispatcher、发送器、`ExecutorSendDeadline`；
    - `spec.md`、`plan.md`、`AGENTS.md` 与 `Docs/`；
    - 表征测试与金样本；`task-07-conflicts.md`（只读引用）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-19-build-test.txt`。
     - **总数**：TASK-18 为 54 ＋ 80 ＋ 1704 ＋ 477 ＋ 970 ＋ 174 ＋ 4563 ＋ 191 ＝ 8213（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host）。本单元 ＝ 8213 ＋ N，逐项目写出加法，N 逐条列名，D 预期为 0。
     - **结果**：失败只能是那 2 条 `DimensionDungeonTests`（基线已有，`verification/baseline.md`）；跳过 20；挂起转储 0。
     - **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本不改。
     - **日志信号**：与 TASK-18 Checks 3 同口径。
       - 应为 0：`must happen inside a gameplay job`、`GameplaySelfWait`、`did not drain within`、`GameplayExecutorStoppedException`、`Nested command dispatch`、`direct send deadline exceeded`、§2.2 归属断言的消息检索词。
       - `GameplayGateWait`、`GameplayLeakedFlow` 预期各 1 次，只出现在 `HostOutputViolationTests` 的显示名里。
       - `Gameplay bus:… failed` 8 条、`inbound:… failed` 3 条、`gm:… failed` 2 条，逐条与 TASK-18 对照。
       - 非零项或与 TASK-18 不同的项，逐条说明。
     - 耗时与内存只作信息（RQ-091 用户修订；单条指令 ≤ 2 s 即可接受）。
  3. **零违规取证**（`logs/task-19-violations.txt`）：`violations.py` 汇总取证目录，命令与 TASK-18 Checks 4 相同。预期先写进日志开头再跑（§6.2）：
     - 7 个程序集的 jsonl 都存在，每个程序集恰有 1 条正向对照；
     - 反例类逐类与 TASK-18 相同，合计 71；
     - 反例类与对照以外 0，没有测试帧 0，`exe:` 0，脚本退出码 0。
  4. **结构判据输出**（`logs/task-19-structure.txt`）：
     - 反射一支：扫描了哪些程序集（逐个列名），找到的 `GameplayJobHost` 派生类；
     - 源码一支：`Server/src` 中出现 `GameplayJobHost` 的文件与行；
     - 判据用例与全部反例的通过记录。
  5. **AC-003 用例**（`logs/task-19-ac003.txt`）：
     - 新用例隔离连续 10 次，每次记下两个连接各自的最大待执行数、两个观测点的事件顺序、A 的帧数与执行顺序；
     - 正式全量负载下通过 1 次。
  6. **新增测试 ×10**（`logs/task-19-new-x10.txt`）：本单元新增的全部用例（`GameplayJobHostStructureTests` 全部、AC-003 新用例）隔离连续 10 次，全部通过。
  7. **变异核对**（`logs/task-19-mutations.txt`；程序与 TASK-18 Checks 9 相同：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：S1～S7 见 §6.3，每一项都要有指定的用例变红。
  8. **生产改动边界**（`logs/task-19-boundary.txt`）：
     - `git diff --stat 0c9c59c8 -- Server/src` 只含 Owned boundary 列出的两个文件；
     - `GameplayExecutor.cs` 的 `git diff -U0` 只有调用形态的变化（`static` 调用改为实例调用、深度超限分支按裁定改写），逐行说明。
  9. **VC-8 扫描**（`logs/task-19-vc8.txt`）：§4 的检索式、命中清单与逐行归类；来源证据表。
  10. **收口对账的引用核对**（`logs/task-19-closure-refs.txt`）：用脚本从 `t004-closure.md` 中抽出全部被引用的用例名、日志路径与 F 编号：
      - 用例名在 `Server/tests` 中检索得到；
      - 日志路径存在；
      - F 编号在 `follow-ups.md` 中存在，且状态与对账表述一致。
      - 未命中的逐条修正后重跑，最终未命中为 0。
  11. **测试改动核对**（`logs/task-19-diff-check.txt`）：`diffcheck7.py 0c9c59c8 --lines`，证明已有测试的方法集合与断言行多重集合不变，只多出本单元新增的方法。
  12. **隔离复跑**（`logs/task-19-isolated.txt`）：Application、Networking 各一次，都带取证目录，记录各自的正向对照与反例类记录数（Application 49、Networking 1，同 TASK-18）。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。
- Dependencies：TASK-18（已提交，`0c9c59c8`），连同 TASK-08～TASK-17 的全部结果；收口对账读取 TASK-01～TASK-18 的 Result 与 `verification/` 下的对账记录。
- Result review：required。
  - 归属断言改的是 `GameplayJobHost`，执行器的进出、程序错误黏滞与调用链都依赖这个基类；深度超限分支若改错，「吞掉超限拒绝的 CMD 仍失败」这条 RQ-003／RQ-053 保护会悄悄消失。现有用例只覆盖同一执行器内的情形。
  - 收口对账是判断性产出。T-005 以「T-004 完成」为进入依据去删锁：对账若把一项未满足的交付记成已完成，或把一个会让锁仍被争用的缺口记成可移交，全量照样是绿的，检查暴露不了。
  - AC-003 第 3 条的用例要靠变异证明它有区分力；对 AC 字面的解读（§3.1）也需要独立核对。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 结构判据在 `Server/src` 中找到 `GameplayExecutor` 以外的派生类；
    2. 归属断言在正式全量或隔离复跑中触发（生产或已有测试里存在跨执行器使用令牌的路径）；
    3. AC-003 用例显示某个连接的待执行入站 CMD 超过 1 条，或另一个连接的 CMD 排在灌包连接的下一帧之后。这说明 RQ-005 在生产路径上不成立；
    4. VC-8 扫描找到已接线、却不经 `TouchSocketNetworkMessageSender.WriteAsync` 的传输写入，或作业上的某条发送路径不套期限；
    5. 收口对账发现 T-004 完成条件或 Plan T-004 行的某项交付未满足，而补足需要改 Owned boundary 以外的生产文件、改 Spec 验收或已定共享契约（规则见 §5.3）；
    6. 正式全量出现新失败，或取证出现反例类与对照以外的记录；
    7. 表征测试或金样本需要改动；需要改 Owned boundary 以外的生产文件。
  - 变异核对期间允许临时改动 `Server/src`，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 与提纲的差异与执行顺序

**与 TASK-18 提纲相比，本计划有四处差异：**

1. **`ChainOf` 也配反例。** 提纲写「`ChainOf` 同形，一并改为实例方法，不另配反例」。可是一旦它有了归属断言，RQ-072 要求每条判据都有证明它会拒绝的反例；不配反例，变异「去掉 `ChainOf` 的归属检查」就没有用例会变红。补一条只多一个 `[Fact]`。
2. **深度超限分支需要裁定**（待裁定第 1 条）。`GameplayExecutor.RunAsync` 在深度超限时，用 `JobRunningAnywhere` 取到正在运行的作业令牌（可能属于另一个执行器），再 `Fault(running, …)`，把拒绝黏到那个作业上。`Fault` 加上归属断言之后，令牌属于另一个执行器时这里会改抛归属错误。提纲没有提到这一点。
3. **结构判据扫描两支。** 提纲写「反射扫描 `Server/src` 的全部程序集」。但 `Application.Tests` 的依赖闭包里没有 Host、GmWeb、HttpApi、GameServer 等程序集，所以加一支源码检索，覆盖反射够不到的项目（§2.3）。
4. **AC-003 的变异落在生产文件上**。提纲写「只改测试副本」；要证明用例有区分力，必须让 dispatcher 入队后不等待，而那是生产文件 `NetworkMessageDispatcher.cs`。按 TASK-18 的变异程序做，用后逐字节还原（S6）。

**执行顺序**：
1. 归属断言（§2.1、§2.2）与结构判据（§2.3），先写反例看 RED，再改生产；
2. AC-003 用例（§3）；
3. VC-8 扫描与来源证据（§4）；
4. 构建、正式全量与取证（Required checks 1～3），新增用例 ×10，隔离复跑；
5. 变异核对（§6.3）；
6. 收口对账与 `follow-ups.md`（§5），引用核对；
7. 边界、diff 与测试改动核对。

收口对账放在全量之后，因为它要引用本单元正式全量与取证的结果。

### 1. 范围的几条边界

- 本单元不改变任何代码的执行位置：入口、门、断言与发送都不动。所以 TASK-18 的零违规结论、7 条冲突用例的判定和读写分类表都不会失效。正式全量的作用是复核这一点，并为 T-005 提供第二次运行。
- 归属断言是执行器内部的不变量：派生类只有 `GameplayExecutor`，而且它只拿自己的令牌调用这些方法。所以归属断言触发时只抛异常，不发取证事件，不进 `violations.py`。这与门断言、写断言不同：那两者守的是调用位置，而调用位置可以出现在任何代码里。
- VC-8 核对只针对过渡期的直发期限。T-006 换成发件箱之后，这条论证整体失效，所以不为它加长期判据，只留一次性证据（§4.3）。

### 2. `GameplayJobHost` 的归属断言与结构判据

#### 2.1 形状（`Abstractions/Execution/GameplayJob.cs`）

```csharp
public abstract class GameplayJobHost
{
    protected GameplayJobToken? JobRunningHere { get; }              // 不变
    protected static GameplayJobToken? JobRunningAnywhere { get; }    // 不变，只读
    protected GameplayJobToken EnterJob(GameplayCallChain chain);    // 不变，本来就是实例方法

    protected void ExitJob(GameplayJobToken token);                   // 原 static
    protected GameplayCallChain ChainOf(GameplayJobToken token);      // 原 static
    protected Exception Fault(GameplayJobToken token, Exception error); // 原 static
    protected Exception? ProgramErrorOf(GameplayJobToken token);      // 原 static

    // 仅当待裁定第 1 条取 B：
    protected static Exception FaultRunningJob(Exception error);     // 给本流上正在运行的作业（任何执行器）记程序错误

    protected void ThrowIfRunningHere(string operation);              // 不变
}
```

- 四个方法入口的顺序是：先 `ArgumentNullException.ThrowIfNull(token)`，再做归属检查，最后执行原有语句。原有语句逐字不变。
- 归属检查写成一个私有方法 `OwnedOrThrow(token, operation)`。不属于本执行器时，抛 `InvalidOperationException`，消息形如 `"{operation} was given a job token of another executor."`，`operation` 取方法名。消息里有固定检索词 `job token of another executor`，Required check 2 检索它为 0。
- 不新建异常类型：调用方只有执行器自己，没有人需要按类型捕获它（§1）。
- `ExitJob` 被拒时，令牌保持有效，`Running` 不清空。被拒的调用对真实作业没有任何影响，反例要断言这一点。

#### 2.2 执行器的调用点（`Application/Execution/GameplayExecutor.cs`）

| 位置 | 今天 | 改后 |
|---|---|---|
| `:42`～`:51` 深度超限 | `var running = JobRunningAnywhere;` … `running is null ? exceeded : Fault(running, exceeded)` | 按待裁定第 1 条：取 B 时为 `running is null ? exceeded : FaultRunningJob(exceeded)`；取 A 时为 `JobRunningHere is { } here ? Fault(here, exceeded) : exceeded` |
| `:61`、`:62`～`:63` 自我等待 | `ChainOf(here)`、`Fault(here, …)` | 语句文本不变，从静态调用变为实例调用；`here` 来自 `JobRunningHere`，必然属于本执行器 |
| `:279` | `ExitJob(token)`（`Job<T>` 内） | `executor.ExitJob(token)`。`Job<T>` 是执行器的嵌套类型，持有 `executor` 引用；嵌套类型能访问外层实例的 protected 成员，`:265` 的 `executor.EnterJob` 已经是这种写法 |
| `:283` | `ProgramErrorOf(token)` | `executor.ProgramErrorOf(token)` |

执行时先 `grep -n "ExitJob\|ChainOf\|Fault(\|ProgramErrorOf" GameplayExecutor.cs` 列全调用点；如果多于上表，按同样的规则改，并在 Result 中列出。

#### 2.3 结构判据（`GameplayJobHostStructureTests`）

- **判据函数** `HostViolations(IEnumerable<Type> types)`：返回所有 `IsSubclassOf(typeof(GameplayJobHost))` 且不是 `GameplayExecutor` 的类型。另外，`GameplayExecutor` 必须是 `sealed`，否则它自己的派生类也会成为宿主。
- **反射一支**：
  - 程序集范围：从 `typeof(GameplayExecutor).Assembly` 出发，沿 `GetReferencedAssemblies()` 递归加载，取全部名字以 `Dfo.Server.` 开头、且不以 `.Tests` 结尾的程序集。
  - 对每个程序集，`GetTypes()`（遇到 `ReflectionTypeLoadException` 时取其中的 `Types` 并去掉 null），把结果交给判据函数，断言结果为空。
  - 断言失败时打印程序集清单，供 Required check 4 记录。
- **源码一支**：沿用 `DetachmentSiteTests` 定位 `Server/src` 的做法，检索全部 `.cs` 中的标识符 `GameplayJobHost`（`\bGameplayJobHost\b`），出现的文件只能是 `Dfo.Server.Abstractions/Execution/GameplayJob.cs` 与 `Dfo.Server.Application/Execution/GameplayExecutor.cs`。这一支覆盖反射够不到的项目：Host、GmWeb、HttpApi、GameServer 不在 `Application.Tests` 的依赖闭包里。执行时以实际加载到的程序集清单为准，在 Result 中写明哪些项目只由源码一支覆盖。
- **反例**：
  - H1：把 `[typeof(GameplayExecutor), typeof(RogueHost)]` 交给判据函数，结果恰为 `RogueHost`。`RogueHost : GameplayJobHost` 定义在测试文件里。测试程序集不在反射一支的范围内，所以它不会让判据本身变红。
  - H2：源码一支的检索函数接收「文件名 → 文本」字典。喂一份在第三个文件里出现 `: GameplayJobHost` 的合成文本，被拒并点名该文件。
- **为什么不改用访问修饰符**：派生类在另一个程序集（Application），基类构造器只能是 `protected`；`private protected` 会把 `GameplayExecutor` 本身也挡在外面。给 Abstractions 加 `InternalsVisibleTo` 又会把全部 internal 开放给 Application，TASK-04 已经否决过这种做法（`TASK-04.md:172`、R2-1）。所以只能由判据来钉。

#### 2.4 归属断言的反例（同一文件）

`RogueHost` 公开包装四个 protected 方法，以及 `EnterJob`。

- O1 `ExitJob`：真实 `GameplayExecutor` 运行一个作业。作业体在 `RunDetached` 之外读取自己的令牌（`GameplayJob.Running.Value`；`Application.Tests` 对 Abstractions 有 IVT），交给 `RogueHost.ExitJob`。断言：
  - 抛 `InvalidOperationException`，消息含检索词；
  - 调用之后作业里 `GameplayJob.IsOnExecutor` 仍为真；
  - 作业正常完成并返回值。
- O2 `Fault`：同上，交给 `RogueHost.Fault(token, new X())`。断言抛归属错误；作业以正常结果完成，没有被那个 `X` 判失败。
- O3 `ProgramErrorOf`：同上，断言抛归属错误。
- O4 `ChainOf`：同上，断言抛归属错误。
- O5 对照：`RogueHost` 用自己的 `EnterJob` 得到令牌，再调用自己的四个方法，全部照常（证明拒绝只针对别人的令牌）。O5 在 `finally` 中用自己的 `ExitJob` 收尾，不让令牌留在测试线程的流上。

测试类不放进需要串行的集合：O1～O4 各用自己的执行器，彼此独立。

### 3. AC-003 第 3 条（`InboundExecutorWireTests`）

#### 3.1 对 AC 字面的解读（待裁定第 3 条）

- **「队列里属于它的待执行入站 CMD」**：经 `IGameplayQueue.RunAsync` 已入队、作业体尚未开始执行的入站作业，按 `GameplayWork.Chain.SourceConnection` 归属到连接。
- **「始终不超过 1 条」**：用计数装饰器在每次入队和每次开始执行时更新计数，记录整个用例期间的最大值。这是逐次精确记录，不是定时采样。
- **「另一个连接的入站 CMD 照常执行」**：灌包期间，另一个连接在两个时点（灌包开始时、灌包进行到一半时）各发一帧。这两帧都必须在灌包连接的下一帧之前执行，也就是不会因为灌包而被饿死。

#### 3.2 装饰器

```text
CountingQueue(IGameplayQueue inner) : IGameplayQueue
  RunAsync(work):
    if work.Chain.Source == Connection && work.CommandType 以 "inbound:" 开头:
        key = work.Chain.SourceConnection
        n = pending[key] += 1;  max[key] = Max(max[key], n)
        包装 Body：进入时 pending[key] -= 1（只减一次）
        inner.RunAsync 同步抛出或返回已失败且 Body 未开始 → pending[key] -= 1
    return inner.RunAsync(work')
```

- **装法**：在 `Server.StartAsync` 中，三个模块注册之后，取出最后一个 `IGameplayQueue` 描述符，换成 `sp => new CountingQueue(ActivatorUtilities.CreateInstance(sp, 原实现类型))`。写法与该夹具替换 `INetworkMessageDispatcher` 的现有写法相同。
- 装饰器实例经静态 `Recorder` 暴露。这个类的用例本来就逐个运行，并在开头 `Reset`（类注释已写明）。
- 已有 4 条用例也会经过装饰器，但它只计数，不改变任何行为。Required check 11 证明那 4 条用例未改。

#### 3.3 用例 `A_flooding_connection_never_has_more_than_one_inbound_cmd_waiting_and_cannot_starve_another`

序号约定：A 用 1～N（N ＝ 64），B 用 1001、1002。`Recorder.Hold` 按序号放行。

1. A 发第 1 帧，`Hold(1)` 挂住；等到 `enter A1`。
2. A 一次写出第 2～N 帧（TCP 缓冲足够容纳 63 个 14 字节帧）。
3. B 发 1001；等到 B 的待执行数为 1（装饰器计数，不靠睡眠）。
4. **观测点 1**：A 的待执行数为 0；没有 `enter A2`；B 的待执行数为 1。
5. 放行 A1。等到 `enter B1001`，断言它在 `enter A2` 之前。
6. 同样的方式在 A 的第 N/2 帧挂住：等到 `enter A32` 之后，B 发 1002，等到 B 的待执行数为 1，**观测点 2** 同观测点 1（A 为 0，没有 `enter A33`），放行，断言 `enter B1002` 在 `enter A33` 之前。
7. 等到 A 的 N 帧与 B 的 2 帧全部 `returned`。断言：
   - A 的 `enter` 序号恰为 1..N；
   - 整个用例中 A、B 的最大待执行数都 ≤ 1，并把最大值写进输出（预期都恰为 1：A 的第 k＋1 帧在第 k 帧完成之后入队，入队到开始之间计为 1）；
   - `Recorder.Overlaps` 为 0；
   - 全部 `enter` 都是 `inbound:GameExit` 的入站作业（沿用 `AssertInboundJob`）。

为什么第 5、6 步的顺序是确定的：B 的作业在 A 的第 k 帧执行期间就已入队；A 的第 k＋1 帧要等第 k 帧的作业完成、插件读到下一帧之后才入队。执行器按 FIFO 执行，所以 B 一定先于 A 的第 k＋1 帧。若出现相反的顺序，就是停止条件 3。

期限沿用夹具的 15 秒 `Patience`。用例预计 1 秒以内完成；耗时只记录。

### 4. VC-8 提前的完成核对（F-14）

#### 4.1 静态扫描

- 检索 `Server/src/**/*.cs`（排除 `obj/`、`bin/`），以下几类各列命中：
  - TouchSocket 的写入：`ISender`、`.SendAsync(`、`.Send(`、`ITcpSessionClient`、`ITcpClient`、`WaitingClient`；
  - `NetworkStream`／`Socket` 的 `Write`、`Send`。
- 逐行归类：
  - (W) 传输写入：预期只有 `TouchSocketNetworkMessageSender.cs:110`（`WriteAsync` 内），以及未接线的 T-003 组件 `Outbound/OutboundSender.cs:223`、`Outbound/OutboundTransport.cs:81`；
  - (S) 走发送器接口的调用：`INetworkMessageSender` 等，最终都落到 (W) 的第一处；
  - (X) 无关命中（命令总线的 `SendAsync`、HTTP 等）。
- **未接线的证明**：`Server/src` 中没有 `IOutboundSender` 的 DI 注册，也没有 `OutboundSender`／`OutboundTransport` 的构造（检索结果附在日志里）。`GameplayOutboxes` 依赖 `IOutboundSender`，同样没有注册。
- **期限覆盖的证明**：`SendAsync`、`SendBatchAsync` 的每一帧都经 `WriteAsync`，而 `SendExplicitAsync` 委托给 `SendAsync`（按行号列出）。`ExecutorSendDeadline.AwaitAsync` 只按 `GameplayJob.IsOnExecutor` 分支，不读 CMD 类型或来源，所以「作业上的直发一律套 2 秒期限」与来源无关。

#### 4.2 按来源的证据

对每个来源找一条已有证据，证明这个来源的作业会经 `TouchSocketNetworkMessageSender` 直发：优先是真实 socket 的线级用例，其次是代码路径（文件:行）。

| 来源 | 已登记的出处 | 要找的证据 |
|---|---|---|
| 入站 | TASK-09 | 线级用例：入站作业回包（例如 `GameLoginIntegrationTests`） |
| 生命周期 | TASK-10 | `LifecycleExecutorWireTests` 中的初始 NOTI 或清理通告 |
| 定时 | TASK-11（Fatigue、Premium、婚礼、比武、卡牌、生物投影） | 一条定时作业直发的用例或代码路径 |
| 事件 | TASK-12（22 个 LiveOnly 投影） | 一条 LiveOnly 投影经真实发送器的路径 |
| GM | TASK-14（Cera 提醒） | GM 路由作业内的提醒发送路径 |

期限机制本身的证据是 `ExecutorSendDeadlineTests` 与 `DirectSendDeadlineRealSocketTests`，它们在本单元正式全量中通过。用例经替身发送器、没有真实 socket 的，只能算「代码路径」证据，表里如实标注。

#### 4.3 为什么不加判据

T-006 把执行器上的发送改为登记发件箱，唯一写入点随之改变，那时 RQ-036 的判据另立。现在加一条「只有 `TouchSocketNetworkMessageSender` 写传输」的判据，两个单元之后就要重写。一次性证据写进 `logs/task-19-vc8.txt` 与收口对账即可。

### 5. T-004 收口对账（`verification/t004-closure.md`）

#### 5.1 文件结构

开头写日期、基准提交（`0c9c59c8` 加本单元改动）、用途和证据底稿。按 AGENTS.md「每种事实只有一个归处」：对账只写「交付 → 哪个 TASK → 证据位置 → 状态」；未关闭事项的内容留在 `follow-ups.md`，对账只引用 F 编号。

1. **Plan T-004 行逐项**，每项一行：
   - 入站（dispatcher 入队并等待，请求级帧在作业内）
   - 连接生命周期
   - 登录会话维护
   - 后台与调度
   - 延时计时器
   - 两塔离场队列
   - 事件消费
   - GM／HTTP
   - 效果账本重放
   - 口令哈希外移
   - 纯遥测留外
   - `SendAsync` 过渡内联规则（作业切分完成后最后翻转）
   - 门断言
   - 分离点 `SuppressFlow`
   - 直发 2 秒期限（含 §4 的 VC-8 核对）
   - 改写会挂起的测门与交错测试
   - 等待关系与停机顺序
   - `Lazy` 内容源预热
   - RQ-061 前一半
   - 「除表征与入口对账外，行为不变」
2. **覆盖栏的 RQ 逐条**：RQ-005～RQ-015、RQ-040～RQ-043（执行位置）、RQ-091（预热）。每条对照 `spec.md` 原文，写出在 T-004 内满足的部分，以及按 Plan 留给后续步骤的部分（例如 RQ-012 中「关闭排在发件箱消息之后」归 T-006，RQ-013 中「调度任务不再用 `Task.Run`」归 T-005）。对应的 AC（AC-003、AC-004、AC-005 的执行位置一半、AC-006、AC-007、AC-015）写出证据用例。
3. **附录 A／B 对账的最终状态**：引用 `entry-reconciliation.md` §2，以及之后的变化：TASK-17 的门断言，TASK-18 改写的附录 B 启动期一行，F-62 中待 T-009 补名的两项。
4. **完成条件**：
   - 「全量测试与表征测试通过」：TASK-18、TASK-19 两次正式全量，失败只有基线已有的 2 条，表征 8 通过、2 跳过；
   - 「门断言和 RQ-061 判据在一次全量运行中零违规」：`logs/task-18-violations.txt`、`logs/task-19-violations.txt`。
5. **TASK-08～TASK-18 的交接核对**：
   - 逐个单元抽出 Remaining、主控方补充、裁定与处置中「交给某单元／后续」的条目，每条对到 F 编号，或对到已关闭的证据；
   - TASK-08～TASK-15 以 `follow-ups.md`（TASK-16 汇总）为底，逐条复核；TASK-16～TASK-18 逐条新核；
   - 对不上的条目，按 §5.3 处理。
6. **移交清单**：按去向分组（M-1、T-005、T-006、T-007、T-008、T-009、测试基础设施、取证缺口、信息），每组只列 F 编号。
7. **结论**：T-004 是否完成；T-005 的进入条件中，属于 T-004 的那一半是否成立。T-005 还依赖 M-1，这是 Plan 规定的，对账只写明 M-1 尚未执行，不替它下结论。

#### 5.2 已知的过渡项（预判，执行时按证据确认）

| 项 | 预判 | 依据 |
|---|---|---|
| AC-003 第 3 条 | 本单元补足（§3） | F-13 |
| VC-8 提前的完成核对 | 本单元补足（§4） | F-14 |
| `GameplayJobHost` 结构判据 | 本单元补足（§2） | F-36 后一半 |
| RQ-008：关闭回调等待的生命周期作业里含直发 | 过渡态，移交 T-006，不阻断 | TASK-10 裁定第 6 条 |
| RQ-012：由 CMD 发起的关闭排在发件箱消息之后 | 过渡态（W7 宽松形式），移交 T-006 | F-20 |
| RQ-013：调度任务的 `Task.Run` 与并发槽；RQ-043：组门 | T-005 自身的工作，不阻断 | F-17、F-19；附录 F |
| RQ-061：发件箱登记一半 | 移交 T-006 | Plan T-004 行只要求不依赖发件箱的部分 |
| RQ-061 判据只覆盖附录 F 名单（AC-006「检索不到执行器外改状态」） | 移交 T-009，不阻断（待裁定第 2 条） | F-62 |
| RQ-091 达标判定，C／D 类首次加载 | 移交 M-1 | F-02～F-10 |
| 双 Error 日志、帧改为显式数据等 | 移交 T-008 | F-30～F-34 |

#### 5.3 对账发现未满足项时的规则

- **本单元补足**：补足只需要新增测试、改测试辅助，或改 `verification/` 下的文档，而且不改变任何用例的执行位置与断言。补完后在对账中标明，并计入 N 的加法。
- **移交、不阻断 T-005**：同时满足两条——Plan 或已有裁定把它分配给了 T-004 之后的步骤；它不影响「生产中没有执行器外的持门者与写者」这一 T-005 删锁的前提。写明 F 编号。
- **阻断 T-005，停下回报**（停止条件 5）：
  - 缺口意味着生产中仍有执行器外的门持有者或附录 F 写者，例如某个入口没有迁入；
  - 或者补足需要改 Owned boundary 以外的生产文件、改 Spec 验收或已定共享契约。
  - 回报时附建议，不自行决定。

#### 5.4 `follow-ups.md`

- F-13、F-14 改为「已在 TASK-19 关闭」，写证据位置；F-36 拆为「结构判据一半已在 TASK-19 关闭；AGENTS.md 一半仍归 T-009」。
- 文件头的范围说明补一句：TASK-19 已逐条确认 T-004 范围内各项的去向，确认表在 `t004-closure.md` §5。
- 新事项从 F-63 起编号。可能出现的新事项，例如待裁定第 1 条取 B 之后「`FaultRunningJob` 不检查归属」的说明，执行时按实际情况决定是否登记。

### 6. 测试、取证与变异

#### 6.1 N 的构成（执行时按实际例数写加法）

- Application：`GameplayJobHostStructureTests` 的判据用例（反射一支、源码一支）、反例 H1、H2、O1～O5；
- Networking：AC-003 用例 1 条；
- 其余项目 0；D ＝ 0。

#### 6.2 取证预期（先写进 `logs/task-19-violations.txt` 再跑）

本单元新增的用例都不产生取证记录：
- 归属断言不发诊断事件（§1）；
- 结构判据只读类型与源码；
- AC-003 用例全部在执行器上运行，或在执行器外只读。

所以预期与 TASK-18 逐类相同：
- TASK-17 的 4 个类：21 ＋ 14 ＋ 2 ＋ 1 ＝ 38；
- `GameplayWriteAssertionTests` 24、`CommandBusExecutorTests` 1、`EffectOperationReplayTests` 1；
- 正向对照 7；
- 38 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 71。

按 kind：`gate-off-executor` 25 ＋ `gate-wait` 20 ＋ `write-off-executor` 24 ＋ `bus-leaked-flow` 2 ＝ 71。

#### 6.3 变异（Required check 7）

| 变异 | 应当变红 |
|---|---|
| S1 去掉 `ExitJob` 的归属检查 | O1 |
| S2 去掉 `Fault` 的归属检查 | O2 |
| S3 去掉 `ProgramErrorOf` 的归属检查 | O3 |
| S4 去掉 `ChainOf` 的归属检查 | O4 |
| S5 在 `Server/src` 的 Application 中加一个 `internal sealed class Probe : GameplayJobHost` | 反射一支与源码一支的判据用例，两支各自点名 `Probe` |
| S6 `NetworkMessageDispatcher.DispatchAsync` 入队后不等待（`_ = queue.RunAsync(...); return ValueTask.CompletedTask;`） | AC-003 新用例（A 的最大待执行数 > 1，或观测点 1 失败）；已有的 `The_next_frame_of_a_connection_starts_only_after_the_previous_job_completes` 也应变红，一并记录 |
| S7 深度超限分支不再给运行中的作业记程序错误（直接返回 `exceeded`） | `GameplayExecutorSelfWaitTests.A_swallowed_depth_refusal_inside_a_job_still_fails_the_cmd`（证明 §2.2 的改写保住了黏滞语义） |

S5 若在反射一支中被测试程序集的依赖闭包漏掉（`Probe` 所在程序集未被加载），只有源码一支变红，在日志中如实记录，并据此在 Result 中说明两支各自的覆盖面。

#### 6.4 冲突用例复核

在正式全量日志中逐条确认 `task-07-conflicts.md` §15 的 7 条：
- 改写的 1 条通过；删除的 1 条不在；
- 保留的 2 条通过；
- 交 T-005 的 3 条通过（它们仍在测门，随 T-005 删除）。

与 §15 的判定不一致，按停止条件 6 处理。

## 待裁定

1. **深度超限时给哪个作业记程序错误**（§2.2）。今天，深度超限的拒绝会黏到流上正在运行的任何作业上（`JobRunningAnywhere` ＋ 静态 `Fault`），使「吞掉拒绝的 CMD 仍失败」。`Fault` 加上归属断言之后，令牌属于另一个执行器时这里会改抛归属错误。
   - **A**：只给本执行器的作业记（`JobRunningHere`）；另一个执行器的作业只拿到返回的异常，不再黏滞。生产中只有一个执行器，所以生产行为不变，但测试中的多执行器语义会变。
   - **B（建议）**：新增 `protected static Exception FaultRunningJob(Exception error)`，不带令牌参数，只作用于当前流上正在运行的作业，深度超限分支改用它。行为与今天逐字相同。它不扩大派生类的能力：任何代码今天都能经公开的 `GameplayJob.AssertNotOnExecutor` 让当前流上的作业以程序错误失败。
   - 建议 B：本单元的定位是结构收紧，不改语义；变异 S7 证明黏滞仍在。
2. **RQ-061 判据只覆盖附录 F 名单，是否阻断 T-005**（§5.2）。AC-006 写「代码中检索不到在执行器外改状态、写库或发包的路径（RQ-061 判据通过）」。TASK-18 的判据按附录 F 的 13 个类（加 `AltarStateStore`）挂点，名单以外的进程内玩法状态（例如幻影塔的 `TowerInventoryDomainService`，F-62）不在判据内。
   - **建议**：不阻断 T-005，按 F-62 交 T-009 复核名单。理由：Plan T-004 完成条件只要求「附录 F 指定的玩法状态写入口」；T-005 删的是附录 F「删除」栏的锁，这些锁保护的状态都在名单内；名单外的状态今天由什么保护、是否在执行器外被写，T-009 做附录 F 最终对账时一并判定。对账在 §5.2 中如实写「AC-006 的这一半在 T-009 验收」。
   - 若主控方认为 AC-006 要在 T-004 结束时成立，本单元需要扩大判据范围，而那属于 RQ-061 挂点的扩张，超出本单元边界，应另立单元。
3. **AC-003 第 3 条的解读**（§3.1）：「待执行」按「已入队、作业体未开始」计，由测试内的 `IGameplayQueue` 计数装饰器逐次精确记录；「另一个连接照常执行」按「灌包开始时与灌包中途各发一帧，都排在灌包连接的下一帧之前执行」验证。
   - 装饰器只观测、不改变行为，入站路径的其余部分全是生产实现。
   - **建议接受**。备选是读取执行器内部队列的长度：要么给执行器加一个只供测试用的观测口，要么用反射读私有的 `Channel`。前者改生产，后者依赖私有字段（F-45 已经记过这类脆弱性）。
4. **Plan 中 T-004 行的措辞**：「预计拆成三个单元」与实际的拆分（TASK-06～TASK-19，T-004a／b／c 之下又细分）不符；「门断言和 RQ-061 判据零违规」的证据实际是两次正式全量。
   - **建议**：本单元不改 `plan.md`，只在收口对账中写明实际拆分与证据位置；Plan 的措辞由主控方在规划 M-1／T-005 时决定是否顺手修正，与 F-60 同样处理。

## 主控方裁定（执行前）

1. **深度超限的程序错误记给谁：采用 B。** 新增 `protected static FaultRunningJob(Exception)`，只作用于当前流上正在运行的作业，行为与今天逐字相同；变异 S7 证明黏滞仍在。
2. **RQ-061 判据只覆盖附录 F 名单：不阻断 T-005。** 理由：T-004 完成条件只要求附录 F 的写入口，T-005 要删的锁所保护的状态都在名单内。但 AC-006 是整个 Spec 的验收条件，必须在最终核验前成立：收口对账中把它列为「未满足的 Spec 验收」，写明缺口（名单外的状态，如 `TowerInventoryDomainService`，F-62）与承接者 T-009（复核名单并补挂点），不得写成已满足。
3. **AC-003 第 3 条的解读：接受。** 「待执行」按「已入队、作业体未开始」计，由测试装饰器逐次记录；在收口对账中写明这一解读。
4. **Plan T-004 行措辞：本单元不改 `plan.md`。** 收口对账写明实际拆分与证据位置；主控方在规划 M-1／T-005 时一并修正 `plan.md`（与 F-60 同样处理）。

## Result（第 2 轮，合同 Revision 1，2026-10-23，基准 `0c9c59c8`，未提交）

### Outcome

done（待第 2 轮独立结果审查，Result review：required）。

**第 2 轮返工**（按文末「主控方处置（第 1 轮评审后）」第 1～5 条；只改 `verification/` 下的文档、本 Result 与结构判据测试类的注释，没有重跑测试）：

| 条 | 处置 | 位置 |
|---|---|---|
| 1 M1 | F-61、F-62 的状态由「信息」改为「未关闭」，原有内容保留。§7 把 F-61 移到 T-006 组（注明「T-006 之后的 Host 取证收口复核」），各组加了数目列，9 ＋ 4 ＋ 10 ＋ 6 ＋ 7 ＋ 6 ＋ 1 ＋ 3 ＋ 3 ＋ 3 ＋ 4 ＋ 2 ＝ 58。引用核对新增口径「归属写了动作的条目不得是『信息』」，在基准版 `follow-ups.md` 上拦下 F-62；重跑后没有解析失败的条目，违规为 0 | `follow-ups.md` F-61、F-62 行；`t004-closure.md` §7；`logs/task-19-closure-refs.txt` |
| 2 L1 | RQ-008 行「留给后续」补写：执行器等待作业上直发的交付（每帧 ≤ 2 s），收包回调因此间接等待交付，归 T-006（F-20） | `t004-closure.md` §3 RQ-008 行 |
| 3 L2 | 文件头的改正清单补上 F-62 的归属，并写明 F-61、F-62 的状态改动 | `follow-ups.md` 文件头 TASK-19 那一条 |
| 4 L3 | 新增 F-76：源码一支可被刻意绕过的三种写法（csproj `<Using Alias>`、Unicode 转义标识符、字符串内的 `//`），加固归 T-009；本单元不改判据。§7 的 T-009 组加上 F-76 | `follow-ups.md` F-76 行；`t004-closure.md` §7 |
| 5 L4 | 三处修改：<br>• F-73 按评审结论关闭，同一行记下既有弱点作为信息：Order 100 的 `CommandExecutionPolicyBehavior` 也调用 `DemandCurrent()`，新旧写法都钉不住用例名里的「before_uow」。<br>• AC-006 行的「缺口」补写：附录 B 生产者一行尚缺两个名字（F-62）。<br>• 测试类注释中只由源码一支覆盖的项目清单补上 Utilities，只改注释 | 第一处：`follow-ups.md` F-73 行，`t004-closure.md` §6.2 `:118` 一行；第二处：`t004-closure.md` §3 AC-006 行；第三处：`GameplayJobHostStructureTests.cs` 类注释 |

- **结构判据与归属断言**：`GameplayJobHost` 的 `ExitJob`、`ChainOf`、`Fault`、`ProgramErrorOf` 改为 `protected` 实例方法，入口先判空、再断言令牌属于本执行器，否则抛 `InvalidOperationException`（检索词 `job token of another executor`）。按裁定第 1 条新增 `protected static FaultRunningJob(Exception)`，深度超限分支改用它，行为与基准相同。
- **结构判据**分两支：
  - 反射一支扫描 8 个程序集，只找到 `GameplayExecutor`；
  - 源码一支检索 `Server/src` 的 1574 个 `.cs` 文件，只有两个宿主文件提到 `GameplayJobHost`；
  - 只由源码一支覆盖的项目有 5 个：GameServer、GmWeb、Host、HttpApi、Utilities。8 ＋ 5 ＝ 13，即 `Server/src` 下的全部项目。
- **AC-003 第 3 条**：一条经真实 TouchSocket、插件、dispatcher 与执行器的线级用例。A 连接灌 64 帧，两个观测点上 A 的待执行数都是 0、B 是 1；B 的两帧都排在 A 的下一帧之前执行；整个用例中两个连接的最大待执行数都是 1。
- **VC-8 核对**：
  - 已接线、写客户端连接的传输写入只有 `TouchSocketNetworkMessageSender.WriteAsync`，作业上一律套 2 秒期限，与来源无关；
  - 五个来源各有证据：入站、生命周期、事件是线级用例，定时与 GM 是代码路径；
  - 另外找到一处已接线的 UDP 写入，见 Why 1。
- **T-004 收口对账**（`verification/t004-closure.md`）：
  - **T-004 完成**；T-005 进入条件中属于 T-004 的一半成立。
  - **AC-006 列为未满足的 Spec 验收**（裁定第 2 条）：缺口是名单外的状态，例如 `TowerInventoryDomainService`；承接者 T-009，见 F-62。
  - 另有两项未满足的 Spec 验收：AC-005 的测量一半（M-1，F-02），AC-007「帧在断开之前到达」的严格形式（T-006，F-20）。
  - M-1 尚未执行，对账不替它下结论。
- **`follow-ups.md`**：
  - 关闭：F-13；F-14 改为「已核对，T-006 之后消失」；F-36 拆分，结构判据一半关闭；
  - 新增 F-63～F-76，其中 F-72 当场核对关闭，F-73 在第 2 轮按评审结论关闭；
  - 改正 F-02、F-15、F-56、F-58、F-62 的来源，以及 F-36、F-61、F-62 的归属；F-61、F-62 的状态改为「未关闭」（第 2 轮）。
- **正式全量**：8224 条，失败只有那 2 条 `DimensionDungeonTests`；取证 71 条，与 TASK-18 逐类相同，反例类以外 0。

### Changed

- **生产代码**：只有 Owned boundary 内的两个文件（`logs/task-19-boundary.txt`）。
  - `Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs`：四个方法改为实例方法并加归属断言（私有 `OwnedOrThrow`）；新增 `FaultRunningJob`；改了两段 XML 摘要。
  - `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs`：改了 3 行。
    - `:51` 改用 `FaultRunningJob(exceeded)`；
    - `:279`、`:283` 改为 `executor.ExitJob`、`executor.ProgramErrorOf`。
    - `:61`～`:63` 文本未变，现在绑定到实例方法。
- **测试**：
  - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayJobHostStructureTests.cs`，共 10 条：
    - 两条判据；
    - 反例 H1（反射）、H2（源码）；
    - 归属反例 O1～O4；
    - 对照 O5；
    - 跨执行器的深度超限黏滞 1 条（见 Why 3）。
  - `Server/tests/Dfo.Server.Networking.Tests/Integration/InboundExecutorWireTests.cs`：
    - 新增 AC-003 用例与测试内的 `CountingQueue` 装饰器；
    - `Recorder` 加了待执行计数；
    - `Server.StartAsync` 加了 3 行，用于装上装饰器；
    - 类加主构造器 `ITestOutputHelper`；
    - 已有 4 条用例未改（`logs/task-19-diff-check.txt`）。
- **文档与证据**：
  - 新建 `verification/t004-closure.md`；
  - 修改 `verification/follow-ups.md`；
  - 日志 `verification/logs/task-19-{build-test,violations,structure,ac003,new-x10,mutations,boundary,vc8,closure-refs,diff-check,isolated,handover}.txt`。

### Why

1. **VC-8 扫描找到第二处已接线的传输写入，按实质判定不触发停止条件 4，交主控方确认。**
   - 位置：`A21UdpDiscoveryService.cs:127` 的 UDP `SendToAsync`。它是 `BackgroundService` 在自己的接收循环里对 NAT 探测包的回包。
   - 文件中没有 `GameplayJob`、`IGameplayQueue`、`GameplayWork`，所以它不在任何作业上，执行器也不会等它。它也不是客户端连接上的发送。
   - 停止条件 4 要守的两件事都成立：作业上的发送都套期限；没有作业会等一个不套期限的写入。
   - 但计划里「已接线的传输写入只有一处」这句话按字面不成立。因此：
     - 对账与 F-14 的结论都写成「写客户端连接的传输写入只有一处」；
     - 登记 F-63：T-006 设 RQ-036 判据时把它列为豁免。
   - **若主控方按字面认定触发了停止条件 4**，本单元其余结果不受影响；需要改的只有 F-14 的结论与对账 §2 的措辞。
2. **反射一支的程序集范围放宽了。** 计划写「从 `GameplayExecutor` 的程序集出发沿引用递归」，那样只能扫到 Application、Abstractions、Data 三个。实际的种子是 `GameplayExecutor` 所在程序集，加上测试输出目录中全部 `Dfo.*.dll`（排除 `.Tests`），扫描范围因此扩到 8 个程序集。变异 S5 证明两支各自点名 `Probe`。
3. **多了 1 条用例**：`A_swallowed_depth_refusal_from_another_executor_still_fails_the_running_job`。
   - 现有用例只覆盖同一执行器内的黏滞（Result review 理由第 1 条已点出）。
   - 取 A，或取「带归属检查的 `Fault`」，生产行为都不变，但跨执行器的黏滞会消失，而没有用例会变红。
   - 加了它之后，额外变异 S8（取 A）与 S9（带检查的 `Fault`）都只让这一条变红，证明 B 是唯一保住原语义的写法。
   - 因此 N 是 11，不是计划预估的 10。
4. **`ChainOf` 与 `Fault` 新增了判空**：计划 §2.1 要求先判空，基准里只有 `ExitJob` 判空。调用方只有执行器自己，它从不传 null。
5. **AC-003 装饰器只看「同步完成且失败」来判定拒绝**，不在返回的任务上挂续体。挂续体会给入站路径多加一层 await，就不再是「只观测」了。代价是：排队中被取消的入站作业不会从计数里减掉。本用例不取消作业，所以不受影响。
6. **交接核对由只读调研代理抽表，本单元复核对不上的全部行。**
   - 复核的过程中发现，代理原报的合计 132 是错的：表中 TASK-11 有 13 行，代理报的是 14。按表逐行重数为 131。
   - 对账与日志都写的是 131，并保留了这条更正（AGENTS.md「加法要当场写出来」）。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误。正式全量前做过一次（`logs/task-19-build-test.txt` 第 1 部分），变异之后又做过一次（`logs/task-19-mutations.txt` 文首）。
2. **正式全量**（`logs/task-19-build-test.txt`，2026-10-22T18:00:33Z～18:03:14Z，161 秒，开始时 swap 已用 8048 MB，只作信息）：
   - **总数**：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1704 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 175 ＋ Application 4573 ＋ Host 191 ＝ 8224 ＝ 8213 ＋ 11 － 0。
   - **N ＝ 11**：
     - Application 10，即 `GameplayJobHostStructureTests` 全部：`Only_the_sealed_gameplay_executor_derives_from_the_job_host_in_the_loaded_production_assemblies`、`Only_the_two_host_files_name_the_job_host_in_the_production_sources`、`A_second_job_host_is_named_by_the_reflection_criterion`、`A_job_host_declared_in_a_third_source_file_is_named_by_the_source_criterion`、`A_host_refuses_to_exit_a_job_of_another_executor_and_the_job_runs_on`、`A_host_refuses_to_fault_a_job_of_another_executor_and_the_job_does_not_fail`、`A_host_refuses_to_read_the_program_error_of_a_job_of_another_executor`、`A_host_refuses_to_read_the_chain_of_a_job_of_another_executor`、`A_host_uses_the_tokens_it_issued_itself`、`A_swallowed_depth_refusal_from_another_executor_still_fails_the_running_job`；
     - Networking 1：`InboundExecutorWireTests.A_flooding_connection_never_has_more_than_one_inbound_cmd_waiting_and_cannot_starve_another`；
     - 10 ＋ 1 ＝ 11。D ＝ 0。
   - **结果**：
     - 通过 54 ＋ 80 ＋ 1704 ＋ 477 ＋ 968 ＋ 173 ＋ 4555 ＋ 191 ＝ 8202；
     - 失败 2，就是那 2 条 `DimensionDungeonTests`；
     - 跳过 2 ＋ 2 ＋ 16 ＝ 20；
     - 8202 ＋ 2 ＋ 20 ＝ 8224；挂起转储 0。
   - **表征测试**：8 通过、2 跳过，`Characterization/` 与金样本未改。
   - **日志信号**：
     - 为 0：`must happen inside a gameplay job`、`GameplaySelfWait`、`did not drain within`、`GameplayExecutorStoppedException`、`Nested command dispatch`、`direct send deadline exceeded`、`job token of another executor`。
     - `GameplayGateWait`、`GameplayLeakedFlow` 各 1 次，都只出现在 `HostOutputViolationTests` 的显示名里。
     - `Gameplay bus:… failed` 8 条（`Act` 2 ＋ `CreateCharacterCommand` 1 ＋ `RegisterAccountCommand` 1 ＋ `SendGmMailCommand` 4）；`inbound:… failed` 3 条（`GameSelectCharacter` 2 ＋ `GameSellItem` 1）；`gm:… failed` 2 条（`auth/login` 1 ＋ `auth/logout` 1）。三类都与 TASK-18 逐条相同：同一脚本跑 TASK-18 的日志，结果一致。
   - **7 条冲突用例**（`task-07-conflicts.md` §15.1）：改写 1 通过、删除 1 不在、保留 2 通过、交 T-005 3 通过，与 §15 的判定一致。
   - **N-2 耗时复核**（F-69）：与 TASK-18 相比，多出 ≥ 9 秒的有 3 条，都是读真实 PVF 的世界用例。它们隔离下是 10～11 秒，在 TASK-10～TASK-17 的全量里是 9～23 秒；TASK-18 那次的 1～3 秒才是例外。不是排空的特征。
3. **零违规取证**（`logs/task-19-violations.txt`；预期取自计划 §6.2，在运行前已写定）：
   - 7 个 jsonl 都存在，每个程序集恰有 1 条正向对照。
   - 记录 71 条：38 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 71。按 kind：`gate-off-executor` 25 ＋ `gate-wait` 20 ＋ `write-off-executor` 24 ＋ `bus-leaked-flow` 2 ＝ 71。逐类与 TASK-18 相同。
   - 反例类以外 0，没有测试帧 0，`exe:` 0，退出码 0。
4. **结构判据输出**（`logs/task-19-structure.txt`）：
   - 反射一支扫描 8 个程序集：Dfo.Pvf、Abstractions、Application、Data、Infrastructure、Networking、Persistence、Protocol。找到的宿主只有 `GameplayExecutor`。
   - 源码一支：1574 个文件，命中 6 行，全部在 `GameplayJob.cs` 与 `GameplayExecutor.cs`。
   - 10 条用例都通过。
   - **TDD**：生产改动之前，O1～O4 是 RED，原因都对得上：
     - O1、O3、O4 没有拒绝（`Record.Exception` 为 null）；
     - O2 的作业被外来的 `ForeignFault` 判失败。
   - 另 6 条在 RED 阶段已经通过：两条判据、H1、H2 与 O5 守的是今天已经成立的结构；跨执行器深度一条守的是裁定第 1 条。改完之后，Execution 相关 208 条全部 GREEN。
5. **AC-003 用例**（`logs/task-19-ac003.txt`）：
   - 隔离连续 10 次，每次都：最大待执行 A 1、B 1；观测点 1 只有 `enter A1`；观测点 2 的最后一个事件是 `enter A32`；A 的 64 帧按 1..64 执行。
   - 10 次的执行顺序完全相同：`A1 B1001 A2 … A32 B1002 A33 … A64`。
   - 正式全量下通过 1 次（975 ms），各项数值相同。
6. **新增测试 ×10**（`logs/task-19-new-x10.txt`）：每轮 11 条，11 × 10 ＝ 110 次通过，0 失败。
7. **变异核对**（`logs/task-19-mutations.txt`）：每项都先备份，再逐字节还原并 `cmp`；`Probe.cs` 删除后确认不存在。`git diff -- Server/src` 的 sha1 前后都是 `3fb8f88b…`；最后做了 `--no-incremental` 重建。
   - S1～S4：O1～O4 各自变红，其余不变。
   - S5：两条判据都变红，各自点名 `Dfo.Server.Application.Execution.Probe` 与 `Dfo.Server.Application/Execution/Probe.cs`。
   - S6：AC-003 用例变红，观测点 1 读到 `waiting flooding 63, other 1`。同时变红的还有 `The_next_frame…`、`Two_connections…` 与 `A_telemetry_frame…`；计划只预期了第一条。
   - S7：`GameplayExecutorSelfWaitTests.A_swallowed_depth_refusal_inside_a_job_still_fails_the_cmd` 与跨执行器那条都变红。
   - 额外的 S8、S9：只有跨执行器那条变红（见 Why 3）。
8. **生产改动边界**（`logs/task-19-boundary.txt`）：
   - `git diff --stat 0c9c59c8 -- Server/src` 只有两个文件，39 行增、10 行删；
   - `GameplayExecutor.cs` 的 3 行变化逐行说明；
   - `Server/src` 下没有未跟踪文件。
9. **VC-8 扫描**（`logs/task-19-vc8.txt`）：
   - `.SendAsync(` 共 280 处 ＝ 发送器与投递服务 35 ＋ 传输写入 3 ＋ 命令总线 236 ＋ 邮件领域 6；
   - 传输面的命中逐行归类，`UNCLASSIFIED` 为 0；
   - 来源证据表在 `t004-closure.md` §2。
10. **收口对账的引用核对**（`logs/task-19-closure-refs.txt`）：
    - 第 1 轮有 1 处没解析到：缩写的用例名 `A_slow_live…`。改为全名后重跑，没解析到的为 0。
    - 第 2 轮新增口径：归属写了动作的条目不得是「信息」。反例：基准版 `follow-ups.md` 中的 F-62 被拦下。
    - 第 2 轮第一次重跑有 1 处没解析到：§7 表下的说明里仍出现已关闭的 F-73。改写说明后重跑。
    - 最终：49 个用例名、21 个日志路径、66 个 F 编号，没解析到的为 0；76 行中归属写了动作的有 62 行，违反新口径的为 0。
    - 状态核对：§7 移交的 58 个 F 编号在 `follow-ups.md` 中都不是完全关闭；F-13、F-14、F-36 一半、F-72、F-73 在那里写的是由 TASK-19 关闭。
11. **测试改动核对**（`logs/task-19-diff-check.txt`）：
    - `InboundExecutorWireTests` 的方法由 4 个变为 5 个；
    - 断言行没有丢失，集合不等的只是新方法里重复出现的既有断言行；
    - 唯一删除的一行是类声明（加了主构造器）。
12. **隔离复跑**（`logs/task-19-isolated.txt`）：
    - Application 4573 条（只有那 2 条已知失败），取证 49 ＝ 21 ＋ 1 ＋ 24 ＋ 1 ＋ 1 ＋ 对照 1；
    - Networking 175 条（跳过 2），取证只有 1 条正向对照；
    - 两者的反例类以外都是 0，没有测试帧都是 0。与 TASK-18 相同。
13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。新文件没有行尾空白。

- 结束时 `pgrep -x yes` 为 0（本单元没有加负载进程）；已执行 `dotnet build-server shutdown`。

### Remaining

1. **第 2 轮独立结果审查**（required）：核对第 2 轮返工表的五条。Why 1 对停止条件 4 的判读仍待主控方确认。
2. **交主控方**：
   - F-74：规划 M-1／T-005 时修正 `plan.md` 的 T-004 行；
   - F-01：立项；
   - M-1 尚未执行，T-005 还依赖它。
3. **新增的后续事项**：F-63～F-71、F-74～F-76，去向见 `t004-closure.md` §7；F-72、F-73 已关闭。
4. **未提交**：没有 commit，也没有 stash。

## 主控方裁定（回报后）

- **`A21UdpDiscoveryService.cs:127` 的 UDP `SendToAsync` 不触发停止条件 4，接受执行者的判定。** 停止条件 4 守的是「作业上的连接发送绕过 2 秒期限」；这处写入在 `BackgroundService` 自己的接收循环里回 NAT 探测包，不在任何作业上，执行器不等它，也不是客户端连接上的发送。F-14 的结论按「写客户端连接的传输写入只有一处」表述，F-63（T-006 设 RQ-036 判据时列为豁免）成立。请结果审查核对这一判定。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict `changes_required`，唯一原因是 M1；按 `task-rework` 处理，只改 `verification/` 下的文档与 Result，不重跑测试：

1. **M1**：F-62、F-61 的状态改为「未关闭」（保留原有内容）；`t004-closure.md` §7 把 F-61 移到 T-006 组（或新设「T-006 之后」组），重算各组数目，总数仍为 58。引用核对脚本的状态口径改为「归属写了动作的条目不得是『信息』」，重跑并更新 `logs/task-19-closure-refs.txt`。
2. **L1**：`t004-closure.md` RQ-008 行的「留给后续」补写「执行器等待作业上直发的交付（每帧 ≤ 2 s），收包回调因此间接等待交付：T-006（F-20）」。
3. **L2**：`follow-ups.md` 文件头的改正清单补上 F-62 的归属。
4. **L3**：源码一支可被刻意绕过的三种写法（csproj `<Using Alias>`、Unicode 转义标识符、字符串内 `//`）登记为新的后续事项，加固归 T-009；本单元不改判据。
5. **L4**：
   - F-73 按评审结论关闭（改写保住了区分力），并在同一行记下既有弱点：Order 100 的 `CommandExecutionPolicyBehavior` 也调 `DemandCurrent()`，新旧写法都钉不住用例名里的「before_uow」，作为信息。
   - AC-006 行的「缺口」补写「附录 B 生产者一行尚缺两个名字（F-62）」。
   - 结构判据测试类注释中的源码独占项目清单补上 Utilities（只改注释）。

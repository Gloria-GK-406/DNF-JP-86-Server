# TASK-18 — T-004c2a：RQ-061 写入判据、泄漏流拒绝（F-58）与零违规全量

基准提交 `6a3bff70`（TASK-17 之后：`SendAsync` 已翻转，门断言与取证汇已开）。TASK-17 文末给 T-004c2 的提纲在这里拆成两个单元：本单元（c2a）和 TASK-19（c2b）。拆分理由见 Detailed Plan §0，TASK-19 的边界提纲见文末。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **RQ-061 中不依赖发件箱的那一半**（Plan T-004 行「写 SQLite 的工作单元开启、附录 F 指定的玩法状态写入口」；Plan「判据与测试的关系」；F-12、F-16、F-57；`t004c-entry.md` 第 3 项）：
     - 工作单元入口 `DomainTransactionScope.Begin(readOnly: false)`；
     - 三处裸写：`CharacterStateAccess` 的 `ChangeAsync`（`:94`）、`SelectedSessionPositionPersistence.AloneAsync`（`:162`），以及 `CreatureRuntimeDomainService.WithinAsync` 的回退分支（`:274`）；
     - 附录 F「改为执行器内访问」的 13 个类的写成员（TASK-17 提纲写作「14 个」，是计数口误，见 §2.3）。
     - 这些入口在执行器外被调用时立即抛出程序错误，并向取证汇发一条 `write-off-executor` 事件。**只拦写，不拦读**（TASK-17 主控方裁定第 6 条）。
  2. **两条判据**（RQ-072）：
     - 分类判据：反射列出 13 个类的全部公开、内部与显式接口成员，每个成员都必须在分类表里，且标为「写」的成员确实调用了写断言；
     - 源码扫描判据：`Server/src` 中 `new DomainTransaction(` 只出现在列名的 6 处，每处标明读或写，写的那几处带断言。
     - 两条判据各配证明它会拒绝的反例；13 个类每个至少一条运行期反例；`:274` 的反例单独一条。
  3. **泄漏流拒绝**（F-58；TASK-17「主控方处置（回报后）」第 2 条）：总线在执行器外分支之前判定「流上带着已结束作业的令牌」（`GameplayJob.Running.Value` 非空且 `IsActive == false`），判为程序错误，不入队；补反例，恢复已删用例 `EffectOperationReplayTests.An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` 的保护意图。顺手把 `SendAsync` 中 `ThrowIfNull` 的同步抛出统一为返回失败的 `ValueTask`（`task-17-result-review.md` 非阻断备注）。
  4. **取证汇的覆盖面**（`task-17-result-review.md` 残余风险）：
     - 链接了记录器、但没有反例类的 5 个测试项目（HttpApi、Infrastructure、Networking、Persistence、Host），各有一条最小的正向对照；
     - `violations.py` 按程序集核对：7 个程序集的取证文件都存在、正向对照各 1 条；
     - Host.Tests 拉起打包 exe 的那一部分盲区，按 §5.2 处置并登记残余。
  5. **T-004c 冲突用例的最终判定**（`task-07-conflicts.md` §14.3 的 7 条；TASK-17 §4.4、主控方裁定第 5 条）：本单元开 RQ-061 后失败的，按既定规则处置；仍通过的，逐条判定「已不冲突、保留」或「交 T-005 随门删除」。记入 `task-07-conflicts.md` §15。
  6. **零违规正式全量**：门断言、RQ-061 写断言与泄漏流断言在同一次全量运行中，反例类与正向对照以外的记录为 0。这次运行满足 Plan T-004 完成条件中「门断言和 RQ-061 判据在一次全量运行中零违规」；T-004 的整体收口对账在 TASK-19。
- Owned boundary：
  - **生产代码**（只限下列文件）：
    - `Server/src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs`：新增写断言 `GameplayWriteAssertion`、泄漏流断言与对应异常类型，复用同一个诊断源（§2.1、§4.1）。
    - `Server/src/Dfo.Server.Application/Transactions/DomainTransactionScope.cs`：`Begin` 的写分支（§2.2）。
    - `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs`：`:94`、`:162` 两处（§2.2）。
    - 附录 F 的 13 个类所在的 17 个文件（§2.3 表；`DungeonRunDomainService` 有 5 个分部文件）：只在写成员的方法体开头加一行断言，不改其他语句。其中 `CreatureRuntimeDomainService.cs` 另含 `:274`。
    - `Server/src/Dfo.Server.Application/Commands/CommandBus.cs`：泄漏流分支与 `ThrowIfNull` 的形态（§4）。
    - `spec.md` 附录 B「开始监听前的启动期工作」一行的末句：只在待裁定第 4 条获准后改写（§2.5）。
  - **测试**：
    - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayWriteAssertionTests.cs`（运行期反例）、`…/Execution/GameplayWriteEntryTableTests.cs`（分类判据、源码扫描判据及其反例；分类表放在这个文件里，作为判据的一部分）；
    - `Application.Tests/Commands/CommandBusExecutorTests.cs`（泄漏流用例）、`Application.Tests/Sessions/EffectOperationReplayTests.cs`（恢复保护意图的那一条）；
    - 新增 `Server/tests/Support/GameplayAssertionRecorderControlTests.cs`，在已链接记录器的 7 个测试项目的 csproj 中各加一行 `Compile Include … Link`；`tests/Support/GameplayAssertionRecorder.cs` 只在 §5.2 需要「记一条外部来源的违规」时改动；
    - `Dfo.Server.Host.Tests` 的 `EndToEndHostHarness.cs`、`LauncherHttpHostStartupTests.cs`、`ChannelHostStartupTests.cs`：只在待裁定第 5 条获准后，在收集子进程输出的那几行加解析调用（§5.2）；
    - `task-07-conflicts.md` §14.3 的 7 条所在文件；
    - 因 RQ-061 或泄漏流断言失败、且符合 §6.2 规则的其他测试文件（含 `tests/Support/TestSessions.cs` 等共用辅助），只允许 TASK-06 §2.6 白名单内的形状改动。
  - **工具、文档与证据**：
    - `verification/probe-tools/violations.py`：按程序集给预期、检查文件齐全、按 kind 分列（§5.3）；
    - `verification/task-07-conflicts.md` 追加 §15；`verification/follow-ups.md` 更新 F-11、F-12、F-16、F-57、F-58，新事项追加新编号；
    - `verification/logs/task-18-*.txt`。
  - **不改**：门断言与 TASK-17 的门挂点；`GameplayJob`／`GameplayJobHost`／`GameplayExecutor`（结构判据在 TASK-19）；发件箱与 `GameplayOutbox` 的登记断言（T-006）；`DomainEventDispatcher`；`TestGameplay` 的来源规则；表征测试与金样本；`spec.md` 的 RQ／AC 与附录 B 以外的附录；`AGENTS.md` 与 `Docs/`。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **诊断全量**（不作正式证据，只用于找出失败与归类；落盘 `logs/task-18-diag.txt`，取证目录另存）：全部断言落地后跑一次全量，带 `--blame-hang-timeout 2m` 与 `DFO_GAMEPLAY_ASSERTIONS`。每条新失败按取证记录的 kind（`write-off-executor`、`bus-leaked-flow`）与最外层测试类归入 §6 的某一类。
  3. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-18-build-test.txt`。
     - 总数：TASK-17 为 53 ＋ 80 ＋ 1703 ＋ 476 ＋ 969 ＋ 173 ＋ 4525 ＋ 183 ＝ 8162（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host）。本单元 ＝ 8162 ＋ N － D，逐项目写出加法；N、D 逐条列名（Theory 按实际运行的例数另计一栏；正向对照在 7 个项目各计 1）。
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20 条；挂起转储 0。
     - **表征测试** 8 通过、2 跳过，`Characterization/` 目录与金样本不改。
     - **日志信号**：`must happen inside a gameplay job`、`GameplayGateWait`、`GameplayLeakedFlow`（§4.1 的异常类型名）、`GameplaySelfWait`、`did not drain within`、`GameplayExecutorStoppedException`、`Nested command dispatch` 在全量输出中为 0（反例只在用例内捕获，不落日志）；`Gameplay bus:… failed` 逐条归到「用例故意让命令失败」，与 TASK-17 的 8 条对照；`direct send deadline exceeded`、`the drain tears it down` 只记数。非零项逐条说明。
     - 耗时与内存只作信息（RQ-091 用户修订）。
  4. **零违规取证**（`logs/task-18-violations.txt`）：`violations.py` 汇总取证目录。
     - 7 个程序集的 jsonl 都存在；
     - 每个程序集恰有 1 条 `GameplayAssertionRecorderControlTests` 记录；
     - 反例类的记录数与预期相等：TASK-17 的 4 个类（共 38 条）不变，本单元新增的反例类数字由用例代码推出、先写进日志再跑；
     - 反例类与正向对照以外的记录为 0，没有测试帧的记录为 0；
     - 按 kind 分列：`gate-off-executor`、`gate-wait`、`write-off-executor`、`bus-leaked-flow`。
  5. **判据输出**（`logs/task-18-entry-table.txt`）：分类表全文（类、成员签名、读／写／附录 B 豁免、豁免引用的附录 B 行）；分类判据与源码扫描判据通过；写成员数、读成员数、豁免数逐类写出加法。
  6. **读残留对照**（`logs/task-18-read-residue.txt`）：把 `logs/task-17-probe-*.txt` 中执行器外的 P2 (B) 与 P1b-read 记录（Application P2 144、Persistence P1 1／P2 5、Protocol P2 8、Networking P2 27；P1b-read Application A 1／B 123、Protocol B 1、Networking B 1），按栈中第一个附录 F 类的成员对到分类表：落在「写」上的逐条列出并说明去向（应当已在诊断全量中失败并按 §6 处置）；落在「读」上的只记数。Persistence 那 1 条 P1 (B) 单独说明（预计就是冲突用例 `AuthenticationConsumptionTests.ExactCredentialBinds…`）。
  7. **冲突用例与测试改动**：
     - `task-07-conflicts.md` §15 逐条写「用例｜触发（RQ-061／泄漏流／仍通过）｜处置｜旧断言 → 新断言 → 保护意图｜后继用例」，并写出新分布的加法（总数仍为 139）；
     - 非冲突测试的改动逐文件列出，`t07/diffcheck7.py 6a3bff70 --lines` 证明测试方法集合与断言行多重集合不变，白名单外的片段逐处说明。
  8. **新增测试 ×10**：本单元新增与改写的用例隔离连续 10 次、全量负载下 1 次，全部通过，落盘 `logs/task-18-new-x10.txt`。
  9. **变异核对**（`logs/task-18-mutations.txt`；每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：R1～R12 见 §7，每一项都要有对应用例或判据变红。
  10. **生产改动边界**（`logs/task-18-boundary.txt`）：
      - `git diff --stat 6a3bff70 -- Server/src` 只含 Owned boundary 列出的文件；
      - 每个 `GameplayWriteAssertion` 调用点逐个列出，与分类表的「写」逐行对上（数目相等，名字一一对应）；
      - 写成员的 diff 只有开头那一行断言（按文件给出 `git diff -U0` 的新增行，删除行为 0）。
  11. **隔离复跑**（`logs/task-18-isolated.txt`）：Application、Protocol、Persistence、Networking、Host 各一次，每次都带取证目录，记录各自的正向对照记录。
  12. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出（口径沿用 TASK-17 主控方处置）。
- Dependencies：TASK-17（已提交，`6a3bff70`），连同 TASK-08～TASK-16 的全部结果。
- Result review：required — 本单元的核心产出是一张约两百个成员的读写分类表，以及它决定的断言挂点。把一个写成员错标成读，判据照样全绿，执行器外的写就悄悄留了下来；全量、反例和变异都只能证明「标为写的确实拦了」，证明不了「该标为写的都标了」。T-005 以本单元的零违规为进入条件去删锁，这里错了，删锁就建立在错误前提上。冲突用例的最终判定、附录 B 豁免的范围、Host 盲区的处置也都属于判断性产出，需要独立审查。
- Task-specific authorization：
  - 冲突用例按既定规则删除或改写（TASK-17 主控方裁定第 5、7 条），不需要逐条再问；与既定去向不同时，在 Result 中单列说明理由。
  - 非冲突测试只允许 TASK-06 §2.6 白名单内的形状改动（主要是把执行器外的写调用包进 `TestGameplay.RunAsync(services, …)` 或无容器重载）。需要改断言的非冲突测试，停下回报。
  - **停止条件**，出现即停下回报，不在本单元修：
    1. RQ-061 或泄漏流断言在生产路径上违规：取证记录没有测试帧，或最外层是入口驱动的用例（真实 dispatcher、socket、HTTP、托管服务 tick、事件分发、打包 exe 的输出）。这说明生产里还有执行器外的写者；
    2. 某个成员读起来是读、实际改了状态（例如读时顺手清理过期项），而它在生产中有执行器外的调用方（附录 B 的路径）；
    3. 某个写成员只能在执行器外被调用、又找不到附录 B 的对应行（需要把它列为豁免，就是在扩附录 B，属于 RQ-011 的范围）；
    4. 表征测试或金样本需要改动；
    5. 需要改 Owned boundary 以外的生产文件；
    6. 一条失败无法归入 §6 的任何一类。
  - 变异核对期间允许临时改动 `Server/src`，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 拆分与顺序

**结论：TASK-17 提纲里的 T-004c2 拆成两个单元。本单元（c2a）做 RQ-061、F-58、取证汇覆盖、7 条冲突用例的最终判定和零违规全量；TASK-19（c2b）做结构判据、AC-003 第 3 条、VC-8 核对和 T-004 收口。** 每个单元一次提交，提交时全绿可运行。

**为什么拆。**
- 本单元的主体是附录 F 13 个类的读写分类：源码粗数约 205 个成员声明（含重载，`members.py` 按缩进粗扫；以反射枚举为准）。它是判断性产出，评审要逐行核对，而且开启后会让多少测试失败，诊断全量之前无法预知。把结构判据、线级测试和收口对账一起放进来，评审面会摊薄在本单元最需要人看的那张表上。
- 后一半（结构判据、AC-003 第 3 条、VC-8 核对、收口对账）彼此独立、风险低，并且都不改变任何测试的执行位置，不会让本单元的零违规结论失效。收口对账要引用本单元的全量结果和 F-58 的结论，所以只能排在后面。
- 多一次全量的代价约 3 分钟（TASK-17 第 2 轮全量 00:15:44～00:18:52），用户已裁定性能不作停止条件。

**为什么不按「判据」与「全量收口」切。** Plan 的 Verification 要求每个 Task 都跑构建与全量。只要本单元开启了 RQ-061，它的正式全量就是「门断言与 RQ-061 判据在一次全量运行中零违规」的那一次运行。另设一个只跑全量的收口单元，不会多出新证据。

**F-58 为什么在本单元。** 主控方已裁定并入 TASK-18（TASK-17「主控方处置（回报后）」第 2 条）。它与 RQ-061 是同一类判定（「执行器外的调用是程序错误」），复用同一个断言文件和取证汇，泄漏流的违规也应算进同一次零违规取证。

**7 条冲突用例为什么在本单元判定。** 它们中会被 c2 触发的只有 RQ-061（`AuthenticationConsumptionTests` 那一条，§14.3 已写明）和泄漏流断言。TASK-19 的结构判据与线级测试不改变任何用例的执行位置，所以本单元结束时就是「c2 结束时」的状态；TASK-19 的全量只复核它们的结果不变。

**执行顺序**：
1. 断言与诊断事件（§2.1、§4.1），挂点（§2.2、§2.3、§4.2），分类表与两条判据（§3）；
2. 取证汇的覆盖面（§5）；
3. 诊断全量（Required check 2），读残留对照（Required check 6）；
4. 按 §6 处置失败；7 条冲突用例的最终判定；
5. 反例与变异（§7）；
6. 正式全量与取证（Required check 3、4），隔离复跑，边界与 diff 核对；
7. 文档：`task-07-conflicts.md` §15、`follow-ups.md`、附录 B（视裁定）。

### 1. 范围的几条边界

- **只拦写，不拦读**（裁定第 6 条）。读成员不加任何断言。执行器外的轮询读（Networking 27 条、卡牌 1 条）、T2 的 `world.Runs.Find`、T-006 那 130 条通知测试的读，不受影响（F-57）。
- **只挂工作单元入口与附录 F 的写入口**，不在 `DomainTransaction` 的构造或提交处挂：测试中有 109 处、18 个文件直接 `new DomainTransaction(` 测存储（检索结果），Plan「判据与测试的关系」写明它们不受影响。
- **发件箱登记那一半归 T-006**：`GameplayOutbox` 今天已经对执行器外登记抛 `GameplayOffExecutorException`（`GameplayOutbox.cs:184`），但发件箱还没接线，不在本单元的判据和零违规取证内。
- **启动期例外不设**（F-16）：建表与迁移在 Persistence 单例构造时直连 SQLite，不经工作单元入口；三个写库的启动工作已是启动作业（探针 3.9 为 0，`t004c-entry.md` 第 3 项第 3 条）。判据只挂在 §2.2、§2.3 的入口上，所以不需要例外。附录 B 对应一行的改写见待裁定第 4 条。
- **执行器上的写不另判**：RQ-061 只管「执行器外」。在作业上调用写成员时，断言只做一次 `Active` 判断就返回，不黏滞、不发事件。流上带着已失效令牌的调用按执行器外处理（`Active` 对失效令牌返回 null），与门断言一致。

### 2. RQ-061 的挂点

#### 2.1 形状（`Abstractions/Execution/GameplayAssertions.cs`）

```csharp
public static class GameplayWriteAssertion          // RQ-061；发件箱一半在 T-006 加入同一处
{
    public const string OffExecutorKind = "write-off-executor";
    public static void Writing(string entry);         // 不在执行器上 → 记诊断，抛 GameplayOffExecutorException
}
```

- 消息沿用 `GameplayOffExecutorException`，形如 `Writing <类型名>.<成员名> must happen inside a gameplay job.`，日志与 TASK-17 的门断言共用「must happen inside a gameplay job」这个检索词。
- 诊断：复用 `GameplayGateAssertion` 的 `DiagnosticListener`（同名源 `Dfo.Server.Gameplay.Assertions`、同一事件名、同一载荷类型 `GameplayAssertionViolation`），kind 取 `write-off-executor`。实现上把 `Report` 提成两个断言类共用的 internal 方法，不新建第二个源，取证汇与 `violations.py` 不用改订阅。
- `entry` 用字符串常量，写法为 `nameof(类型) + "." + nameof(成员)`；重载不必区分，日志可读即可。

#### 2.2 工作单元入口与三处裸写

| 位置 | 读／写 | 改法 |
|---|---|---|
| `DomainTransactionScope.Begin`（`:40` 的 `new`） | `readOnly: false` 为写 | `Begin` 开头：`if (!readOnly) GameplayWriteAssertion.Writing(...)`，放在「已有事务」检查之前，使执行器外的调用总是先报违规 |
| `CharacterStateAccess.cs:52`（`AmbientStoreAccess.ReadAsync`） | 读 | 不挂 |
| `CharacterStateAccess.cs:94`（`ChangeAsync`，易失存储） | 写 | 在 `new DomainTransaction()` 之前一行断言；有环境事务的分支已由 `Begin` 覆盖，不重复挂 |
| `CharacterStateAccess.cs:162`（`SelectedSessionPositionPersistence.AloneAsync`，拆除落位置） | 写 | 同上 |
| `ItemContainers.cs:181`（`ReadAsync`） | 读 | 不挂 |
| `CreatureRuntimeDomainService.cs:274`（`WithinAsync` 的回退分支） | 写 | 同上；它前面的 `scope.Begin()` 分支已由 `Begin` 覆盖 |

**需要特别看的一处**：`AmbientStoreAccess.ReadSnapshotAsync`（`CharacterStateAccess.cs:37`）用写模式的 `scope.Begin()` 开一个从不提交的读快照（注释说物品托管会话要登记生命周期参与者）。挂在 `Begin(readOnly: false)` 上，就会把执行器外的快照读也当作写拒绝，这比「只拦写」严。探针在执行器外的 P1 记录，生产路径为 0、测试只有 Persistence 1 条（`task-17-probe-summary.txt`），所以今天不会触发。处理方式见待裁定第 3 条；裁定前按「照挂」执行，在 Result 中记录诊断全量里是否有由它引起的失败。

`:274` 在生产与测试中都不可达（`entry-reconciliation.md` §1.5），反例自己构造：用一个不是 `DomainTransactionScope` 的 `IDomainTransactionAccessor`（`Current` 恒为 null）构造 `CreatureRuntimeDomainService`，在执行器外调用经 `WithinAsync` 的写成员，断言抛 `GameplayOffExecutorException`、发出一条 `write-off-executor` 事件、存储未被写入；同一调用放进作业里则照常完成。

#### 2.3 附录 F「改为执行器内访问」的 13 个类

与探针 P2 的挂点集合相同（`verification/probe-tools/patch.py` 的 `P2_CLASSES` 加 `RUNTIME`）：

| 类 | 文件（`Server/src/`） | 源码粗数（声明／不同名） |
|---|---|---:|
| `OnlineStateRegistry` | `Dfo.Server.Application/Users/OnlineStateRegistry.cs` | 25／23 |
| `LoginSessionRegistry` | `Dfo.Server.Application/Authentication/LoginSessionRegistry.cs` | 7／7 |
| `DungeonRunDomainService` | `Dfo.Server.Application/World/DungeonRunDomainService.cs` 及 `.Objects`、`.Mechanisms`、`.DynamicActors`、`.Transitions` 四个分部 | 55／48 |
| `CreatureRuntimeDomainService` | `Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs` | 14／14 |
| `PartyService` | `Dfo.Server.Application/Parties/PartyService.cs` | 12／12 |
| `TownAreaDomainService` | `Dfo.Server.Application/World/TownAreaDomainService.cs` | 7／6 |
| `DungeonParticipationDomainService` | `Dfo.Server.Application/World/DungeonParticipationDomainService.cs` | 4／4 |
| `DungeonCardPresentationCoordinator` | `Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs` | 7／7 |
| `BossTowerDomainService` | `Dfo.Server.Application/World/BossTower/BossTowerDomainService.cs` | 17／17 |
| `IllusionTowerDomainService` | `Dfo.Server.Application/World/IllusionTower/IllusionTowerDomainService.cs` | 17／17 |
| `TournamentDungeonDomainService` | `Dfo.Server.Application/World/TournamentDungeonDomainService.cs` | 13／13 |
| `AltarDungeonEngine` | `Dfo.Server.Application/World/AltarDungeonEngine.cs` | 16／15 |
| `OnlineCharacterRuntimeDomainService` | `Dfo.Server.Application/Characters/OnlineCharacterRuntimeDomainService.cs` | 11／10 |

粗数的加法：25 ＋ 7 ＋ 55 ＋ 14 ＋ 12 ＋ 7 ＋ 4 ＋ 7 ＋ 17 ＋ 17 ＋ 13 ＋ 16 ＋ 11 ＝ 205，13 项（`DungeonRunDomainService` 的 5 个文件合为一项）。精确数以反射枚举为准，写进 `logs/task-18-entry-table.txt`。

**为什么是 13 个，不是 TASK-17 提纲写的 14 个。** 附录 F「改为执行器内访问」栏共 11 个名目：`OnlineStateRegistry`、`LoginSessionRegistry`、`DungeonRunDomainService`、生物运行时、两塔、比武、祭坛、队伍、城镇区域、副本参与、卡牌展示协调。其中「两塔」是两个类，所以栏内是 12 个类；加上 `OnlineCharacterRuntimeDomainService`，共 13 个。探针的挂点集合（`P2_CLASSES` 的 12 个不同类名，加 `RUNTIME`）也是 13 个。提纲的「13 ＋ 1 ＝ 14」把栏内的 12 个数成了 13 个。

**执行时的第一步核对**：两塔的运行聚合 `BossTowerRun`／`BossTowerRoom`（`BossTowerRun.cs`）与 `IllusionTowerDgn`／`TowerRoom`（`IllusionTowerDgn.cs`）是 `internal` 的顶层可变类型。检索结果显示，它们只在各自的领域服务文件里被引用，所以写一定经过两个塔服务的成员，不单独进表。执行时用同样的检索把 13 个类各自持有的顶层可变聚合再过一遍；发现有类外代码直接改动的，把那个类型补进表里，并在 Result 中说明。

**分类规则**（逐个成员读方法体判定）：

| 类别 | 判定 | 处理 |
|---|---|---|
| 写 | 改变该实例或其持有的玩法状态：字段赋值、集合增删、内部聚合的变更、登记事务参与者（`Enlist`）、启动或取消计时器、写存储。读时顺手清理（惰性过期、裁剪）也算写 | 方法体第一句 `GameplayWriteAssertion.Writing(...)`；`async` 方法放在第一个 `await` 之前，异常进入返回的任务 |
| 读 | 只读字段、在锁内取快照、返回不可变值，不改任何状态 | 不挂 |
| 附录 B 豁免 | 按附录 B 某一行合法地在执行器外运行的写，例如卡牌协调器的 `DrainAsync`（「生产者的入队侧」一行：`GameplayExecutorLifetime` 排空生产者）与容器释放时的 `Dispose` | 不挂；分类表里写明引用附录 B 的哪一行。见待裁定第 2 条 |

- 构造器、编译器生成的成员、只读的静态成员（例如卡牌协调器的 `Production` 时序常量）不进表；属性的 `set` 访问器按写成员处理，`get` 按读。显式接口实现（名字含 `.` 的私有方法）进表。
- 内部嵌套类型（例如 `DungeonRunDomainService` 的私有聚合）不进表：它们不能从类外拿到，写一定经过外层服务的某个成员。如果某个嵌套类型是 `public`／`internal` 且被类外代码直接调用，就把它补进表里，并在 Result 中说明。
- 一个成员读写兼有（先查后改），按写处理。

#### 2.4 断言放在实现类上

调用方经接口（如 `IDungeonRunDomainService`）调用；断言写在实现类的方法体里，测试替身不受影响。生产只有这一份实现，所以挂在实现上就覆盖了全部生产调用。

#### 2.5 附录 B 启动期一行

附录 B「开始监听前的启动期工作」一行末句写着「RQ-061 的启动期例外是否还需要，由 T-004c 判定」。本单元的结论是「不需要」（§1）。获准后（待裁定第 4 条）把这一句改写为事实：「TASK-18（基准 `6a3bff70`）：不需要。RQ-061 的判据只挂在工作单元入口与附录 F 的写入口上，仍在执行器外的建表与迁移不经这两类入口。」RQ-061 正文的括注不动。

### 3. 两条判据（`GameplayWriteEntryTableTests`）

#### 3.1 分类判据

- **分类表**：一个静态表，键为「类型全名 ＋ 成员签名」（参数类型列表，区分重载），值为读／写／豁免（豁免带附录 B 行名与一句理由）。表放在判据文件里，与 `CommandHandlerArchitectureTests` 的豁免清单同一种写法。
- **枚举**：对 13 个类型用 `DeclaredOnly | Instance | Static | Public | NonPublic` 取方法，留下 `IsPublic`、`IsAssembly`、`IsFamilyOrAssembly`，以及显式接口实现；去掉构造器与 `CompilerGenerated`。属性取访问器。
- **断言**：
  1. 枚举出的每个成员都在表里（新增成员未分类即红）；
  2. 表里没有已经不存在的成员（防止表陈旧）；
  3. 标为「写」的成员都调用了 `GameplayWriteAssertion.Writing`；标为「读」与「豁免」的都没有调用。
- **第 3 条怎么查**：读方法的 IL，解析 `call` 指令的目标（`MethodBody.GetILAsByteArray()` ＋ `Module.ResolveMethod`）。`async` 方法查它的状态机 `MoveNext`（由 `AsyncStateMachineAttribute` 找到）；表达式体与普通方法相同。如果 IL 解析在某类写法上不可靠，退回按源码检索方法体文本，并在 Result 中写明用了哪种方法、为什么。
- **反例**：把判据写成一个接收「类型列表 ＋ 分类表」的纯函数，测试另喂三组输入，断言各自被拒：
  - 一个含未分类公开方法的测试内类型；
  - 一个标为「写」却不调用断言的测试内类型；
  - 一张含已不存在成员的表。

#### 3.2 源码扫描判据

- 读取 `Server/src` 下全部 `.cs`（沿用 `DetachmentSiteTests` 定位源码根的做法），检索 `new DomainTransaction(`；结果必须恰好是下表 6 处（按「文件 ＋ 所在方法名」定位，不按行号，以免行号漂移就红）：

  | 文件 | 所在方法 | 读／写 |
  |---|---|---|
  | `DomainTransactionScope.cs` | `Begin` | 写（`readOnly` 为假时） |
  | `CharacterStateAccess.cs` | `AmbientStoreAccess.ReadAsync` | 读 |
  | `CharacterStateAccess.cs` | `AmbientStoreAccess.ChangeAsync` | 写 |
  | `CharacterStateAccess.cs` | `SelectedSessionPositionPersistence.AloneAsync` | 写 |
  | `ItemContainers.cs` | `ReadAsync` | 读 |
  | `CreatureRuntimeDomainService.cs` | `WithinAsync` | 写 |

- 标为「写」的那几处，`new` 之前的同一方法体内必须有 `GameplayWriteAssertion.Writing(`。
- **反例**：判据函数接收「文件名 → 文本」的字典；喂一份多出第 7 处的合成文本，以及一份写处缺断言的合成文本，各自被拒。

### 4. 泄漏流拒绝（F-58）

#### 4.1 形状

- `GameplayAssertions.cs` 新增：

  ```csharp
  public static class GameplayLeakAssertion            // F-58；T-008 帧改为显式数据时复核
  {
      public const string LeakedFlowKind = "bus-leaked-flow";
      public static Exception? LeakedFrom(string operation);   // Running.Value 非空且 !IsActive → 记诊断，返回异常；否则 null
  }
  public sealed class GameplayLeakedFlowException(string operation, GameplayCallChain chain) : InvalidOperationException(...);
  ```

  消息带上已结束作业的链（根 ID、深度、`Describe()`），用来找出是哪个作业把流漏出去的。
- 判定只看「流上有令牌、令牌已失效」。`RunDetached` 把 `Running` 置空，所以经 `RunDetached` 的合法执行器外调用不受影响；从来没有令牌的流（测试线程、生产者）也不受影响。
- 放在 Abstractions：只有这里能读 `GameplayJob.Running`（internal）。

#### 4.2 总线

```text
SendAsync(command, ct):
  if command is null            → return FromException(ArgumentNullException)    // 原为同步抛出
  if DispatchDepth != 0         → return FromException(NestedDispatch())          // TASK-17 第 2 轮
  if LeakedFrom("bus:" + 类型名) is {} e → return FromException(e)                // 本单元
  if GameplayJob.IsOnExecutor   → return InlineAsync(...)
  else                          → queue.RunAsync(... 新根作业 ...)
```

- 放在 `DispatchDepth` 检查之后：带处理器深度的泄漏流照旧报「Nested command dispatch」（F8、F9 不变）；这一条只接住不在处理器内的泄漏流。
- 只改总线这一个分支（主控方裁定）。`IGameplayQueue.RunAsync` 本身对泄漏流的处理不在本单元，见待裁定第 6 条。

#### 4.3 用例（`CommandBusExecutorTests` 续编号，加 `EffectOperationReplayTests` 一条）

- **L1**：作业体里不加 `SuppressFlow` 起一个 `Task.Run`，它等作业结束后再调用总线 → 抛 `GameplayLeakedFlowException`，handler 未执行，队列里没有新的 `bus:` 作业，取证汇有一条 `bus-leaked-flow` 事件。
- **L2**：同一个泄漏流里先 `RunDetached` 再调用总线 → 照常入队执行（证明判定只针对失效令牌，不误伤合法的执行器外调用）。
- **L3**：从未有过令牌的测试线程调用 → 照常入队（F1 已覆盖，这里只在 Result 中引用，不重复写）。
- **L4**：`SendAsync(null)` 返回失败的 `ValueTask`，调用本身不抛（`ThrowIfNull` 形态的统一）。
- **恢复的保护意图**：在 `EffectOperationReplayTests` 新增一条，名字说清意图，例如 `A_flow_leaked_from_a_closed_operation_cannot_reach_the_bus`：经真实效果结算在作业里建起请求级帧，作业内起一个未抑制的续体，作业结束（帧已关闭）后该续体调用总线 → 被拒，handler 未执行，没有以「可信直发」的身份落库。被删用例守的是「继承已关闭帧不能降级成可信直发」；今天能跨流到达总线的只有这一种泄漏形态，所以用它承接。`task-07-conflicts.md` §15 把这条记为被删用例的后继。

### 5. 取证汇的覆盖面

#### 5.1 五个项目的正向对照

- 新增 `tests/Support/GameplayAssertionRecorderControlTests.cs`，链接进已链接记录器的 7 个测试项目（Application、Protocol 已有反例，但统一链接，使 `violations.py` 对 7 个程序集用同一条规则）。
- 内容只有一个 `[Fact]`：在执行器外调用 `GameplayGateAssertion.Entering("recorder-control")`，断言抛 `GameplayOffExecutorException`。环境变量未设时它照样通过（断言不依赖记录器），设了时恰好产生 1 条记录。
- 这证明的是「这个程序集里记录器装上了、事件能到文件」。它不证明各项目里所有路径都会被记——那由断言本身保证：每次违规都同步写事件。

#### 5.2 Host.Tests 打包 exe 的盲区

- **盲区是什么**：`EndToEndHostHarness`、`LauncherHttpHostStartupTests`、`ChannelHostStartupTests` 拉起打包的 exe（合计 24 例，`follow-ups.md` §2.1），子进程里没有记录器，那里的违规不进取证文件。执行器上的违规会黏滞到作业，作业以失败结束、执行器记 `Error`；执行器外的违规若被生产代码的 `catch` 吞掉，就只剩诊断事件，而子进程里没有订阅者。
- **处置（待裁定第 5 条，建议 A）**：
  - A：三个夹具已经重定向子进程的标准输出（`EndToEndHostHarness.cs:383`/`:414`、`LauncherHttpHostStartupTests.cs:310`/`:350`、`ChannelHostStartupTests.cs:239`/`:268`）。在收集输出的那一处，按异常类型名（`GameplayOffExecutorException`、`GameplayGateWaitException`、`GameplayLeakedFlowException`）解析每一行；命中时经记录器追加一条记录（kind 前缀 `exe:`，帧为当前测试的栈），由 `violations.py` 一并统计。这不改任何用例的断言行。先用一次实跑确认打包 exe 的控制台日志会打印异常类型名，再落地；若不打印，转 B。解析函数配一条单元测试（喂合成的一行，断言被识别）。
  - B：不改夹具，只登记盲区。理由：打包 exe 走的启动、停机与启动器认证路径，在进程内的 `GmTestHost` 与表征宿主里都有带记录器的同路径用例。
- **两种方案都有的残余**：打包 exe 中被吞掉、又没有打印到输出的执行器外违规，仍然看不见。Result 中登记。

#### 5.3 `violations.py`

- 预期参数改为 `程序集:类=条数`（旧写法 `类=条数` 仍接受，表示任意程序集）；
- 新增 `--assemblies` 列表：列出的每个程序集都必须有 jsonl 文件，且恰有 1 条 `GameplayAssertionRecorderControlTests` 记录；
- 输出按 kind 分列；`exe:` 前缀的记录单独成组，一律算作「反例类以外」。

### 6. 测试侧

#### 6.1 7 条顺延的冲突用例

| 用例 | 预计 | 处置原则 |
|---|---|---|
| `AuthenticationConsumptionTests.ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected`（Persistence） | RQ-061：测试线程在执行器外 `fixture.Scope.Begin()`（`:19`），再用 `SuppressFlow` 起一个竞争的 `Consume` | 前一半（4 个错误凭据返回 null、同一凭据在提交后能再次绑定）按「先后两条作业」改写，断言不变。后一半「两个未提交的绑定重叠时后者被拒」在单执行器下不会发生（同一时刻只有一个工作单元）：删除这部分断言，理由写「机制随串行消失」，承接者是 AC-001 的串行用例与 TASK-17 的 F7（`CommandBusExecutorTests`，执行器外并发调用的 handler 最大并发为 1）。若改写后整条已与 `ExpiryIsExclusiveAndUncommittedConsumptionCanBeRetried` 重复，改为整条删除并写明承接者 |
| `DungeonObjectQueueTests.Leaving_the_room_before_prepare_rolls_back_the_reservation` | RQ-061（若两个发起方之一在执行器外调用 `DungeonRunDomainService` 的写成员）或仍通过 | 失败则按 E 类：改写为先后两条作业，断言不变；仍通过则判定见下 |
| `TransactionalCommandSerializationTests` 的 2 条、`CommandBusTests` 的 3 条 | 预计仍通过 | 判定见下 |

**仍通过的判定**（TASK-17 §4.4）：
- 已不再在执行器外取门、开写事务或写附录 F 入口的（以诊断全量的取证记录与探针结论为据），记「已不冲突、保留」；
- 仍在测门本身的（例如 `TransactionalCommands_DoNotRunTheirHandlersConcurrently` 断言的是 `TransactionalExecutionGate` 的互斥，`CommandBusTests` 3 条断言的是管线租约在异常、取消后释放），记「交 T-005，随门删除或改写」，并写出 T-005 处理时的保护意图。
- 每条写明依据，记入 §15。

#### 6.2 7 条以外的失败

1. **测试在执行器外调用附录 F 的写成员、或开写事务**（预计主要是直接构造这些类的单元测试、共用夹具与 `tests/Support/TestSessions.cs` 这类辅助）：按 T4／T5 包进 `TestGameplay.RunAsync(services, …)`，没有容器的用无容器重载，断言不变。逐文件列名（Required check 7）。
2. **测试里的泄漏流**（作业内起了未抑制的 `Task.Run`，作业结束后才调用总线）：把那个 `Task.Run` 改为在作业内 `await`，或包进 `SuppressFlow`／新的 `TestGameplay` 区域，断言不变；超出白名单的停下回报。
3. **xunit 夹具构造器里的写**：同第 1 条，包在夹具自己的 `TestGameplay.Run` 里。
4. 其余失败按停止条件 6 处理。

如果某条失败的用例是靠「执行器外写」制造交错来测某种次序（而不只是顺手写了一下），它就是新判定的冲突：按 TASK-17 裁定第 7 条判删除或改写，在 §15 单列「新判定」。

### 7. 新增测试与变异

#### 7.1 运行期反例（`GameplayWriteAssertionTests`）

- `Begin(readOnly: false)` 在执行器外 → 抛出、发一条事件、没有事务留在流上；`Begin(readOnly: true)` 在执行器外 → 照常；两者在作业里 → 照常。
- `:94`：易失存储在执行器外 `ChangeAsync` → 抛出、存储未改；作业里 → 照常。耐久存储在执行器外仍先抛断言（断言在 `IsDurable` 检查之前还是之后，执行时按「断言先于原有拒绝」放置，并在用例里写明）。
- `:162`：执行器外 `PersistAsync`（无环境事务）→ 抛出、位置未落库；作业里 → 照常。
- `:274`：见 §2.2。
- 13 个类各至少一条：执行器外调用一个写成员 → 抛出、事件 1 条、状态未变；同一调用在作业里 → 照常；执行器外调用一个读成员 → 照常（证明只拦写）。有附录 B 豁免的类（预计卡牌协调器）再加一条：执行器外调用豁免成员 → 照常。
- 失效令牌：作业结束后的续体调用写成员 → 按执行器外处理，抛出。
- 已取消的令牌：写断言不看取消，照样先报违规（与门断言不同：门断言保留「已取消先抛取消」的老顺序，是因为原来的 `WaitAsync` 就先看取消；写成员原来没有这一步）。在用例里写明这一点。

反例类的预期记录数，由用例代码数出，先写进 `logs/task-18-violations.txt` 的开头再跑。

#### 7.2 变异（Required check 9）

| 变异 | 应当变红 |
|---|---|
| R1 去掉 `Begin` 的写断言 | `Begin` 的执行器外反例；源码扫描判据（写处缺断言） |
| R2 去掉 `:94` 的断言 | `:94` 反例；源码扫描判据 |
| R3 去掉 `:162` 的断言 | `:162` 反例；源码扫描判据 |
| R4 去掉 `:274` 的断言 | `:274` 反例；源码扫描判据 |
| R5 `Writing` 一律放行 | 13 个类的全部运行期反例 |
| R6 抽 3 个写成员去掉断言：一个 Application 单文件类的、一个 `DungeonRunDomainService` 分部文件里的、`DungeonCardPresentationCoordinator` 的 | 分类判据第 3 条（逐个点名），以及这几个成员若有运行期反例则一并变红 |
| R7 给某个类加一个未分类的公开方法 | 分类判据第 1 条 |
| R8 在 `Server/src` 一个未列名的方法里加 `new DomainTransaction()` | 源码扫描判据 |
| R9 去掉总线的泄漏流分支 | L1、恢复保护意图的那一条 |
| R10 写断言不发诊断事件 | 运行期反例中的事件断言 |
| R11 从一个项目的 csproj 去掉记录器链接（含正向对照） | `violations.py --assemblies` 报该程序集缺文件 |
| R12 （仅待裁定第 5 条取 A 时）夹具的输出解析一律返回「未命中」 | 解析函数的单元测试 |

### 8. 为什么生产行为不变

- 生产中工作单元入口与附录 F 的写全部发生在作业里：探针在执行器外的 P1／P1b 写类／P2 记录全部带测试帧（`task-17-probe-summary.txt`：没有测试帧的 (A) 记录在五个项目中为 0；P1b-teardown、P1b-volatile、P1b-creature 为 0）。若诊断全量中出现没有测试帧的记录，就是停止条件 1。
- 读成员不加断言，附录 B 允许的执行器外读（遥测的 `IsBlocked`／`IsSuperseded`、`TouchSocketTransportDrain`、`ReadDueAsync` 等）不受影响。
- 泄漏流分支只作用于执行器外的总线调用，生产中这类调用为 0（`entry-reconciliation.md` §1.3）。
- 新增的运行期动作是每个写入口一次 `Active` 判断、总线一次令牌读取，可以忽略。

## 后续单元边界提纲（TASK-19 = T-004c2b，不在本单元执行）

- **结构判据**（TASK-06 §7 待办 4d、TASK-04 主控方补充、F-36 的后一半）：
  - 只有 `GameplayExecutor` 能派生 `GameplayJobHost`。反射扫描 `Server/src` 的全部程序集；判据写成接收类型列表的函数，反例喂一个测试程序集里的派生类，断言被拒。测试程序集不在扫描范围内，所以反例用的派生类不会让判据本身变红。
  - `ExitJob`、`Fault`、`ProgramErrorOf` 改为实例方法，入口断言 `ReferenceEquals(token.Owner, this)`，不属于本执行器就抛程序错误；`ChainOf` 同形，一并改为实例方法，不另配反例。反例：测试程序集里的派生宿主拿另一个执行器的令牌调用这三个方法，各自被拒；变异「去掉归属检查」使反例变红。
  - Owned boundary：`Abstractions/Execution/GameplayJob.cs`、`Application/Execution/GameplayExecutor.cs`（只改对这几个方法的调用形态）、新增判据测试文件。
- **AC-003 第 3 条**（F-13）：线级测试放进 `Networking.Tests/Integration/InboundExecutorWireTests.cs`，沿用它的 `Server` 夹具。
  - 测试容器里给 `IGameplayQueue` 套一个计数装饰器，按 `GameplayWork.Chain.SourceConnection` 统计「已入队、尚未开始」的入站作业数。
  - 连接 A 的第 1 帧 handler 等一个 TCS，A 随后持续灌 N（≥ 50）帧；期间连接 B 发 1 帧。断言：A 的待执行数在每次观测时 ≤ 1（实际应为 0：A 的第 2 帧要等第 1 帧完成才入队）；放行 TCS 之后，B 的 handler 在 A 的第 2 帧之前进入；A 的全部帧最终按序执行。
  - 变异：dispatcher 入队后不等待作业完成（只改测试副本、用后还原）→ 待执行数超过 1，用例变红。
- **VC-8 提前的完成核对**（F-14）：静态列出 `Server/src` 中全部传输写（TouchSocket 会话的 `SendAsync` 等），证明它们只经 `TouchSocketNetworkMessageSender` 的一处写入点，而那一处在作业上一律套 `ExecutorSendDeadline`（与来源无关）；再逐来源（入站、生命周期、定时、事件、GM）列出一条已有的作业内直发用例或代码路径作证据。核对结果写进收口对账，F-14 改为「已核对，T-006 之后消失」。
- **T-004 收口对账**：新建 `verification/t004-closure.md`，按 Plan T-004 行逐项列「交付 → 由哪个 TASK 完成 → 证据」：入站、生命周期、登录会话、后台与调度、延时计时器、两塔离场队列、事件消费、GM/HTTP、效果账本重放、口令哈希外移、纯遥测留外、过渡内联规则、门断言、分离点、2 秒期限、测试改写、等待关系与停机、预热、RQ-061 的前一半；附录 A／B 对账的最终状态；完成条件引用 TASK-18 与 TASK-19 两次正式全量的零违规取证。未在 T-004 完成而移交的事项逐条写去向（M-1、T-005、T-006、T-007、T-008、T-009）。
- **`follow-ups.md`**：F-13、F-14 关闭；F-36 的结构判据一半关闭；T-004 范围内仍未关闭的逐条确认去向。
- **一次正式全量**：总数、失败、跳过、表征、日志信号同本单元口径；`violations.py` 同本单元的预期（新增的结构判据不产生违规记录），反例类与正向对照以外为 0。这次运行与本单元那次一起，作为 T-005 的进入条件。7 条冲突用例的结果与本单元判定一致。
- **Result review**：required。结构判据与归属断言改的是 `GameplayJobHost` 这个所有执行器都依赖的基类；收口对账是判断性产出，T-005 以它为进入依据。

## 待裁定

1. **拆分**：T-004c2 拆成 TASK-18（RQ-061、F-58、取证汇覆盖、7 条冲突判定、零违规全量）与 TASK-19（结构判据、AC-003 第 3 条、VC-8 核对、T-004 收口）。理由见 §0。**建议接受。** 备选是不拆，按 TASK-17 提纲一个单元做完，代价是评审面摊薄在分类表上。
2. **分类表的第三类「附录 B 豁免」**：附录 F 的类里有少数写成员合法地在执行器外运行（预计是卡牌协调器的 `DrainAsync`，由 `GameplayExecutorLifetime` 在停机时调用，属于附录 B「生产者的入队侧」；以及容器释放时的 `Dispose`）。照字面挂断言，停机与容器释放就会报违规。**建议**：允许这一类，每项必须引用附录 B 的已有行；若某个写成员需要豁免却找不到对应行，按停止条件 3 回报，不在本单元扩附录 B。这是对 RQ-061「附录 F 指定的玩法状态写入口」的一处收窄，请确认。
3. **`AmbientStoreAccess.ReadSnapshotAsync` 用写模式 `Begin` 开读快照**（`CharacterStateAccess.cs:37`）：挂在 `Begin(readOnly: false)` 上，执行器外的快照读也会被拒，比「只拦写」严。今天生产中没有这样的调用方（探针）。**建议**：照挂，不改生产语义，在分类与 Result 中登记「执行器外的快照读被一并拒绝」，以后有执行器外的快照读需求时再改为只读模式。备选：本单元把它改成 `Begin(readOnly: true)`——但注释说物品托管会话要登记生命周期参与者，只读模式能否承载需要另行验证，会扩大本单元的生产改动。
4. **附录 B 启动期一行的末句改写**（§2.5）：这是事实性修改（「是否还需要，由 T-004c 判定」→「不需要」及理由），RQ-061 正文不动；Plan T-009 行「RQ-061 运行期判据全开（含启动期例外）」的括注，留到规划 T-009 时按事实修正。**建议接受**，改动只落在附录 B 那一句。
5. **Host 打包 exe 的盲区**（§5.2）：**建议 A**（夹具解析子进程输出里的违规异常类型名并记入取证文件，前提是实跑确认日志会打印类型名，否则转 B）。A 不改用例的断言行，只在三个夹具收集输出的那一处加一次调用；残余是「被吞掉又没打印」的违规仍看不见。备选 B 只登记盲区。
6. **泄漏流判定的范围**：主控方裁定只改总线的执行器外分支。同一种泄漏流若直接调用 `IGameplayQueue.RunAsync`（不经总线），今天会被当作普通的执行器外入队、成为新根作业。生产中直接持有 `IGameplayQueue` 的位置是有清单的生产者（附录 B「生产者的入队侧」，37 行检索），它们都不在作业流上。**建议**：本单元按裁定只改总线；把「队列层是否也拒绝泄漏流」登记为 follow-up，交 T-007（调用链贯通时各来源的根／父判定会重新经过这里）。

## 主控方裁定（执行前）

1. **拆分为 TASK-18（c2a）与 TASK-19（c2b），接受。** 类的个数按 13 计，接受 §2.3 的更正。
2. **分类表第三类「附录 B 豁免」，接受。** 每项必须引用附录 B 已有的行；找不到对应行即触发停止条件 3，不在本单元扩附录 B。在 Result 中登记为对 RQ-061 挂点范围的收窄。
3. **`ReadSnapshotAsync` 的写模式快照：不采用「照挂」，改为分出不带断言的快照入口。** 照挂会拒掉执行器外的快照读，与「只拦写，不拦读」（TASK-17 裁定第 6 条、F-57）相抵；改成只读模式又会动生产语义。做法：在 `DomainTransactionScope` 中加一个内部入口（例如 `BeginSnapshot()`），行为与今天的 `Begin()` 逐语句相同，只是不调用写断言；`ReadSnapshotAsync` 改用它。源码扫描判据同时钉住：这个入口在 `Server/src` 中只被 `ReadSnapshotAsync` 调用，且 `ReadSnapshotAsync` 不提交。配反例（判据拒绝第二个调用点）。Owned boundary 不变（两个文件都已在内）。
4. **附录 B 启动期那一行末句改为「不需要」，接受。** RQ-061 正文不动；Plan T-009 行的措辞留到规划 T-009 时修，登记到 `follow-ups.md`。
5. **Host 打包 exe 盲区按方案 A，失败时退到方案 B，接受。** 残余（被吞且未打印的违规）登记进 `follow-ups.md`。
6. **泄漏流判定本单元只改总线，接受。** 队列层（`IGameplayQueue.RunAsync`）是否也拒绝泄漏流登记为新的后续事项，归 T-007。

## Result（第 1 轮，合同 Revision 1，2026-10-23，基准 `6a3bff70`，未提交）

### Outcome

done（待独立结果审查，Result review：required）。

- **RQ-061 写断言**（不依赖发件箱的一半）：
  - 工作单元入口 `DomainTransactionScope.Begin(readOnly: false)`、三处裸写（`AmbientStoreAccess.ChangeAsync`、`SelectedSessionPositionPersistence.AloneAsync`、`CreatureRuntimeDomainService.WithinAsync` 的回退分支），以及附录 F 13 个类加 `AltarStateStore` 的 137 个写成员，在执行器外调用时立即抛 `GameplayOffExecutorException`，并向取证汇发一条 `write-off-executor`。读成员不断言。
  - 调用点 141 ＝ 成员 137 ＋ 入口 4。
- **裁定第 3 条**：`ReadSnapshotAsync` 改用新的内部入口 `DomainTransactionScope.BeginSnapshot()`。它与 `Begin()` 逐语句相同，只是不调用写断言。源码扫描判据钉住两点：`BeginSnapshot` 只被 `ReadSnapshotAsync` 调用；`ReadSnapshotAsync` 不提交。各配一条反例。
- **两条判据**（`GameplayWriteEntryTableTests`，9 条用例）：
  - 分类判据：反射枚举 14 个类型的 202 个成员，读每个成员的 IL（async 方法读状态机），确认「写」恰好以自己的名字调用写断言，「读」与「豁免」都不调用。表：写 137 ＋ 读 63 ＋ 豁免 2 ＝ 202。
  - 源码扫描判据：`Server/src` 中 `new DomainTransaction(`（含全限定写法）只在 7 处，写处必须先有断言。
  - 反例 7 条：未分类成员、写成员缺断言、读成员带断言、陈旧行、名字不符、async 写、未列名的新建处、写处缺断言、第二个快照调用方、快照提交。
- **泄漏流拒绝（F-58）**：
  - `CommandBus.SendAsync` 在嵌套检查之后判定「流上带着已结束作业的令牌」：返回 `GameplayLeakedFlowException`，不入队，记一条 `bus-leaked-flow`。
  - `ThrowIfNull` 改为返回失败的 `ValueTask`。
  - 被删用例的保护意图由 `EffectOperationReplayTests.A_flow_leaked_from_a_closed_operation_cannot_reach_the_bus` 承接。
- **取证汇覆盖面**：
  - 7 个测试项目各链接一条正向对照 `GameplayAssertionRecorderControlTests`；
  - `violations.py` 支持按程序集给预期，`--assemblies` 检查文件齐全、每个程序集恰有 1 条对照，并按 kind 分列；
  - Host 打包 exe 按方案 A 处理（实跑确认控制台日志打印异常类型名，三个夹具逐行解析，`exe:` 记录一律算反例类以外）。
- **7 条顺延冲突用例**：改写 1、删除 1、已不冲突保留 2、交 T-005 3（`task-07-conflicts.md` §15）。总分布仍为 139，T-004c 不再有顺延。
- **正式全量**：8213 条，只有那 2 条 `DimensionDungeonTests` 失败。取证 71 条，全部属于反例类与正向对照，反例类以外 0，没有测试帧 0，`exe:` 0。

### Changed

- **生产代码**：`git diff --stat 6a3bff70 -- Server/src` 共 20 个文件，都在 Owned boundary 内（`logs/task-18-boundary.txt` §1、§4）。
  - `Abstractions/Execution/GameplayAssertions.cs`：
    - 新增 `GameplayWriteAssertion.Writing(entry)`、`GameplayLeakAssertion.LeakedFrom(operation)`、`GameplayLeakedFlowException`；
    - `GameplayGateAssertion.Report` 由 private 改为 internal，三种断言共用一个诊断源。
  - `Application/Transactions/DomainTransactionScope.cs`：`Begin` 开头一行写断言（`readOnly` 为假时），新增 `BeginSnapshot`。
  - `Application/Characters/CharacterStateAccess.cs`：
    - `ReadSnapshotAsync` 改用 `BeginSnapshot`；
    - `ChangeAsync` 在环境事务分支之后、`IsDurable` 拒绝之前加一行断言（§7.1「断言先于原有拒绝」）；
    - `AloneAsync` 在 `new` 之前加一行断言。
  - `Application/Commands/CommandBus.cs`：空命令改为返回失败的 `ValueTask`；新增泄漏流分支。
  - 附录 F 的 16 个文件（`DungeonRunDomainService.Transitions.cs` 没有公开成员，未改）：
    - 写成员方法体开头一行断言，另加一行 `using`；
    - `CreatureRuntimeDomainService.cs` 另含 `:274`（现 `:289`）的断言。
    - 32 个表达式体写成员改为块体，`AltarStateStore.Remove` 的单行块体拆成多行（见 Why 2）。
    - 逐文件的词法核对：去掉断言行与 `using` 之后，与基准只差「`=>` 删除，`{`、`return`、`}` 加入」，且四者个数相等。
- **测试**：
  - 新增：
    - `Execution/GameplayWriteAssertionTests.cs`（25 条，其中执行器外的写 24 次）；
    - `Execution/GameplayWriteEntryTableTests.cs`（9 条，分类表在其中）；
    - `Host.Tests/HostOutputViolationTests.cs`（2 个 Theory，7 例）；
    - `tests/Support/GameplayAssertionRecorderControlTests.cs`（链接进 7 个项目，csproj 各加一行）。
  - `CommandBusExecutorTests` 新增 L1、L2、L4（L3 即已有的 F1）；`EffectOperationReplayTests` 新增承接用例 1 条。
  - 冲突用例：
    - `AuthenticationConsumptionTests.ExactCredential…` 改写并改名为 `ExactCredentialBindsAgainButWrongCredentialsAreRejected`；
    - `DungeonObjectQueueTests.Leaving_the_room_before_prepare_rolls_back_the_reservation` 删除。
  - 形状改动（包进 `TestGameplay`，断言不变），共 11 条失败用例，涉及 6 个文件：
    - `OutsideTransactionTests.ChangingACharacterOutsideAUseCaseIsRefused`；
    - 夹具 `NotificationWorld` 的 4 个辅助（`Leave`、`Reconnect`、`ReplaceKeepingConnection`、`Revoke`）；
    - `LoginSessionQueuedCommandTests` 1 行；
    - `EffectOperationReplayTests.Refusal_only…` 1 行；
    - 两塔 `ProjectionConcurrencyTests` 的组队准备。
  - `tests/Support/GameplayAssertionRecorder.cs`：新增 `ObserveHostOutput` 与 `ViolationIn`。三个 Host 夹具的 `Append` 各加一行调用（方案 A）。
- **工具、文档与证据**：
  - `verification/probe-tools/violations.py`（§5.3）；
  - `task-07-conflicts.md` 追加 §15；
  - `follow-ups.md`：F-11、F-16、F-57 改为已关闭；F-12 的判据一半、F-58 的总线一支已关闭；新增 F-59～F-62；
  - `spec.md` 附录 B「开始监听前的启动期工作」末句改写（裁定第 4 条）；
  - `logs/task-18-{red,diag,build-test,violations,entry-table,read-residue,diff-check,new-x10,mutations,boundary,isolated,host-output}.txt`。

### Why（本地决定与偏离）

1. **类型是 14 个，不是 13 个**：按 §2.3「执行时的第一步核对」补入 `AltarStateStore`。
   - 祭坛状态实际存放在这里，它不在 `AltarDungeonEngine` 里。
   - `AltarDungeonDomainService.HandleAsync` 直接调用 `Endless/UltimateAltarDomainService.Remove`，再由它们调用 `AltarStateStore.Remove`，不经过引擎的任何成员。
   - 计划粗数 `AltarDungeonEngine` 16 个声明，正是引擎 12 个加 store 4 个，说明粗扫时这 4 个已经算在内。
   - 两塔的 `BossTowerRun`、`IllusionTowerDgn` 只在各自的服务文件里被引用，不进表。
2. **表达式体写成员改成了块体**：32 个，其中两塔 23、`DungeonRunDomainService` 6、`OnlineCharacterRuntime` 2、生物运行时 1。
   - 在 `=>` 前面放不进一行语句，所以无法做到 Required check 10 字面上的「删除行为 0」。
   - 改法是机械的：表达式原文不动，只加 `{ 断言; return … }`。`logs/task-18-boundary.txt` §3 用词法比对证明，除此之外没有任何改动。
   - 其中返回 `ValueTask` 的非 async 成员（两塔）在执行器外同步抛出，而不是把异常放进返回的任务。调用方一律 `await`，结果相同。
3. **`BeginSnapshot` 抄了 `Begin` 的 5 行语句**，没有抽成私有方法。
   - 若抽出共用的方法，`new` 就会落到一个没有断言的方法里，源码扫描判据就不能再用「写处在 `new` 之前有断言」这一条简单规则。
   - 裁定原文也是「逐语句相同」。
   - 代价是 `new DomainTransaction(` 的位置变为 7 处，表里多一类「快照」。
4. **`:274` 的运行期反例有两条**。
   - 按计划写的那条经成员 `AdvanceAsync` 调用，会先被成员自己的断言拦下，所以变异 R4 只会让源码判据变红。
   - 另加一条用反射直接调用私有的 `WithinAsync<bool>`：执行器外抛出、记 1 条事件、`action` 未执行；放进作业则执行并提交。R4 下这一条变红。
5. **`DungeonObjectQueueTests.Leaving_the_room…` 删除**。§6.1 原定的处置是「按 E 类改写为先后两条作业，断言不变」，没有照做，理由如下：
   - 被测的交错是「预留之后、准备之前，另一个发起方无事务地换房」，在 RQ-061 下构造不出来：作业内换房会进入同一个环境事务，作业外换房会被断言拒绝。
   - 改成两条作业，提交就不会失败，三条断言里有两条只能删掉，用例也就不剩任何意图。
   - 后继：`Durable_failure_releases_the_same_head_and_revisit_does_not_refill_it` 守「提交失败释放同一队首」；〔序〕守「两条 CMD 之间不可能交错」。
   - 按 TASK-17 裁定第 7 条（「门内或门间的次序本身」删除）处理。
6. **`AuthenticationConsumptionTests` 的那条改名**：原名描述的「重叠的未提交绑定被拒」已删除，与 TASK-17 Why 4 同理。
7. **只在可写环境事务里登记空参与者的成员按「读」处理**：两塔的 `Find`、`IsInTower`、`CanShop`，以及副本参与的 `Find`，共 6 个，表里注明 `ConditionalEnlist`。
   - 只读事务拒绝状态参与者，所以这种登记只会发生在可写事务里。
   - 可写事务只能经已断言的入口开启，或者是从不提交的快照。
   - 这条路径上的参与者不暂存任何变更。
   - **这是本单元最需要审查的判断之一**：若改判为写，执行器外的这些读会被一并拒绝。
8. **豁免 2 项**：卡牌协调器的 `DrainAsync` 与 `Dispose`，都引用附录 B「生产者的入队侧」。
   - `DrainAsync` 是该行原文点名的。
   - `Dispose` 只执行 `DrainAsync` 的第一步（停止并取消延时），不等待，在容器释放时调用。该行没有点名 `Dispose`，按待裁定第 2 条的预期归入此行。这同样需要审查。
   - 这是对 RQ-061「附录 F 指定的玩法状态写入口」的收窄，按裁定第 2 条登记在此（另见 F-62）。
9. **`exe:` 记录的帧**是子进程输出读取线程的栈，不是计划写的「当前测试的栈」。事件回调不在测试流上，拿不到测试的栈。这不影响判定：`exe:` 记录一律算反例类以外，记录内容就是那一行输出。
10. **源码扫描的正则放宽到全限定写法**（`new Dfo.Server.Application.Transactions.DomainTransaction(`），反例与 R8 都用这种写法。
11. **两条判据失败时打印完整的违规清单**，用 `Assert.True(count == 0, 全文)`，不用 `Assert.Empty`，因为 xunit 会截断字符串。这样变异核对能逐个点名。

### Checks

1. **构建**：`--no-incremental` 构建 0 警告、0 错误。
   - 正式全量之前做过一次（`logs/task-18-build-test.txt` 第 1 部分）。
   - 变异补跑之后又做了一次（`logs/task-18-mutations.txt` 末尾）。
2. **诊断全量**（`logs/task-18-diag.txt`，00:52:49～00:55:36，开始时 swap 已用 8345 MB）：
   - 失败 15 条 ＝ 新失败 13 条 ＋ 已知的 `DimensionDungeonTests` 2 条。新失败全部是 `write-off-executor`，最外层都是测试类：
     - 冲突用例 2 条；
     - 非冲突 1 条；
     - T-006 夹具 6 条；
     - TASK-17 已改写的冲突 4 条。
     - 2 ＋ 1 ＋ 6 ＋ 4 ＝ 13。归类见 §15.1、§15.2。
   - `bus-leaked-flow` 只有 L1 的 1 条，是反例。
   - 取证没有测试帧的记录 0，所以停止条件 1 未触发。
3. **正式全量**（`logs/task-18-build-test.txt`，01:16:26～01:19:10，开始时 swap 已用 8685 MB，耗时 2.71 分钟，只作信息）：
   - **总数**：54 ＋ 80 ＋ 1704 ＋ 477 ＋ 970 ＋ 174 ＋ 4563 ＋ 191 ＝ 8213 ＝ 8162 ＋ 52 － 1。各项目依次为 HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host。
     - HttpApi 53 ＋ 1；Pvf 80；Protocol 1703 ＋ 1；Persistence 476 ＋ 1；Infrastructure 969 ＋ 1；Networking 173 ＋ 1；
     - Application 4525 ＋ 39 － 1 ＝ 4563；
     - Host 183 ＋ 8 ＝ 191。
   - **N ＝ 52**：
     - 正向对照 7 个项目各 1 条，共 7；
     - Application 另加 38：`GameplayWriteAssertionTests` 25 ＋ `GameplayWriteEntryTableTests` 9 ＋ L1、L2、L4 共 3 ＋ `A_flow_leaked_from_a_closed_operation_cannot_reach_the_bus` 1；
     - Host 另加 7：`HostOutputViolationTests` 两个 Theory 实际运行 3 ＋ 4 例。
     - 7 ＋ 38 ＋ 7 ＝ 52。
     - 改名 1 条（`ExactCredential…`）不计入增减。
   - **D ＝ 1**：`DungeonObjectQueueTests.Leaving_the_room_before_prepare_rolls_back_the_reservation`。
   - **结果**：
     - 通过 54 ＋ 80 ＋ 1704 ＋ 477 ＋ 968 ＋ 172 ＋ 4545 ＋ 191 ＝ 8191；
     - 失败 2，就是那 2 条 `DimensionDungeonTests`；
     - 跳过 2 ＋ 2 ＋ 16 ＝ 20；
     - 8191 ＋ 2 ＋ 20 ＝ 8213；挂起转储 0。
   - **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本未改。
   - **日志信号**：
     - 为 0：`must happen inside a gameplay job`、`GameplaySelfWait`、`did not drain within`、`GameplayExecutorStoppedException`、`Nested command dispatch`、`direct send deadline exceeded`。
     - `GameplayGateWait`、`GameplayLeakedFlow` 各 1 次，都出现在 `HostOutputViolationTests` 的用例显示名里（解析函数的测试数据），不是异常。
     - `Gameplay bus:… failed` 8 条，与 TASK-17 相同：`Act` 2、`CreateCharacterCommand` 1、`RegisterAccountCommand` 1、`SendGmMailCommand` 4。
     - `Gameplay inbound:… failed` 3 条、`Gameplay gm:… failed` 2 条，与 TASK-17 相同。
     - `the drain tears it down` 1 条，只记数。
4. **零违规取证**（`logs/task-18-violations.txt`，预期先写进日志再跑）：
   - 7 个 jsonl 都存在，每个程序集恰有 1 条正向对照，记录 71 条。
     - TASK-17 的 4 个类：21 ＋ 14 ＋ 2 ＋ 1 ＝ 38，不变；
     - `GameplayWriteAssertionTests` 24、`CommandBusExecutorTests` 1、`EffectOperationReplayTests` 1；
     - 正向对照 7。
     - 38 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 71，逐类与预期相等。
   - 按 kind：`gate-off-executor` 25 ＋ `gate-wait` 20 ＋ `write-off-executor` 24 ＋ `bus-leaked-flow` 2 ＝ 71。
   - 按程序集：Application 1 ＋ 21 ＋ 1 ＋ 24 ＋ 1 ＋ 1 ＝ 49；Protocol 1 ＋ 14 ＋ 2 ＝ 17；HttpApi、Infrastructure、Networking、Persistence、Host 各 1，共 5。49 ＋ 17 ＋ 5 ＝ 71。
   - 反例类与对照以外 0，没有测试帧 0，`exe:` 0，脚本退出码 0。
5. **判据输出**（`logs/task-18-entry-table.txt`）：分类表全文与逐类加法，9 条判据用例通过。各类型依次为「写 ＋ 读 ＋ 豁免」：
   - `OnlineStateRegistry` 10 ＋ 15 ＝ 25；`LoginSessionRegistry` 4 ＋ 3 ＝ 7；`DungeonRunDomainService` 47 ＋ 8 ＝ 55；`CreatureRuntimeDomainService` 11 ＋ 3 ＝ 14；
   - `PartyService` 8 ＋ 3 ＝ 11；`TownAreaDomainService` 4 ＋ 3 ＝ 7；`DungeonParticipationDomainService` 3 ＋ 1 ＝ 4；`DungeonCardPresentationCoordinator` 3 ＋ 0 ＋ 2 ＝ 5；
   - `BossTowerDomainService` 12 ＋ 5 ＝ 17；`IllusionTowerDomainService` 13 ＋ 4 ＝ 17；`TournamentDungeonDomainService` 7 ＋ 6 ＝ 13；
   - `AltarDungeonEngine` 7 ＋ 5 ＝ 12；`AltarStateStore` 2 ＋ 2 ＝ 4；`OnlineCharacterRuntimeDomainService` 6 ＋ 5 ＝ 11。
   - 写合计：10 ＋ 4 ＋ 47 ＋ 11 ＋ 8 ＋ 4 ＋ 3 ＋ 3 ＋ 12 ＋ 13 ＋ 7 ＋ 7 ＋ 2 ＋ 6 ＝ 137；
   - 读合计：15 ＋ 3 ＋ 8 ＋ 3 ＋ 3 ＋ 3 ＋ 1 ＋ 0 ＋ 5 ＋ 4 ＋ 6 ＋ 5 ＋ 2 ＋ 5 ＝ 63；
   - 豁免 2；137 ＋ 63 ＋ 2 ＝ 202。
6. **读残留对照**（`logs/task-18-read-residue.txt`，脚本附在文末）：
   - **Application**：
     - P2 (B) 144 ＝ 读 124 ＋ 写 17 ＋ 无分类成员 3。那 3 条是冲突用例 `DungeonObjectQueueTests` 的事务参与者回调。
     - P2 (A) 3 都是读，属于 `GameplayGateAssertionTests` 的反例。
     - P1 (B) 2 与 P1b-read A 1 ／ B 123 都没有附录 F 成员在栈上。P1b-read 是读站点本身。
   - **Persistence**：
     - P1 (B) 1：冲突用例 `ExactCredential…` 原 `:19` 的 `Begin`。栈上测试帧显示为 `AuthenticationFixture::Consume`，因为测试方法在 `Consume` 完成时被内联续跑。
     - P2 (B) 5 ＝ 读 1 ＋ 写 2 ＋ 无分类成员 2。那 2 条是同一条冲突用例的参与者 `Prepare` 与 `Commit`。
   - **Protocol**：P2 (B) 8 ＝ 读 4 ＋ 写 4；P1b-read B 1。
   - **Networking**：P2 (B) 27 ＝ 读 27（轮询）；P2 (A) 1 为读；P1b-read B 1。
   - **落在「写」上的 23 行逐条列出**，全部属于诊断全量中失败、并已按 §15 处置的用例：
     - `DungeonObjectQueueTests` 5；
     - 通知夹具 11；
     - `EffectOperationReplayTests.Refusal_only…` 1；
     - `LoginSessionQueuedCommandTests` 1（栈上显示为 `TestCredentials::LoginAsync`）；
     - `AuthenticationConsumptionTests` 1；
     - 两塔组队 4。
     - 5 ＋ 11 ＋ 1 ＋ 1 ＋ 1 ＋ 4 ＝ 23。
7. **冲突用例与测试改动**：
   - §15 已写；7 条的新分布见 §15.4，总数 139。
   - `diffcheck7.py 6a3bff70 --lines`（`logs/task-18-diff-check.txt`）：白名单外的片段逐处说明在日志开头。
   - 非冲突用例中，测试方法集合与断言行多重集合都未变；唯一的集合差异是夹具 `NotificationWorld.Revoke` 的断言行，因包裹多出一个右括号，断言本身相同。
8. **新增与改写用例 ×10**（`logs/task-18-new-x10.txt`）：
   - 每轮 Application 66 ＋ Persistence 4 ＋ Protocol 3 ＋ Host 8 ＋ HttpApi 1 ＋ Infrastructure 1 ＋ Networking 1 ＝ 84；
   - 10 轮共 70 次运行、840 次通过，全部通过；
   - 正式全量负载下也全部通过。
9. **变异核对**（`logs/task-18-mutations.txt`；每项先备份、再逐字节还原并 `cmp`；`git diff -- Server/src` 的 sha1 前后都是 `fdca2205…`；最后 `--no-incremental` 重建）：
   - R1 `Begin` 断言去掉：`Begin` 反例与源码判据变红，源码判据点名 `Begin`。
   - R2 `ChangeAsync`：两条反例与源码判据变红。
   - R3 `AloneAsync`：反例与源码判据变红。
   - R4 `WithinAsync`：直接调用的那条反例与源码判据变红。
   - R5 `Writing` 一律放行：23 条运行期反例变红。
   - R6 去掉 3 个成员的断言：分类判据逐个点名 `TownAreaDomainService.Enter`、`DungeonRunDomainService.AdvanceRoomMechanisms`（分部文件）、`DungeonCardPresentationCoordinator.Cancel`；另有对应的 3 条运行期反例变红。
   - R7 加一个未分类的公开方法：分类判据点名 `TownAreaDomainService.Count()`。
   - R8 在未列名的方法里以全限定写法新建事务：源码判据点名「`Probe` is not a listed site」。
   - R9 去掉总线的泄漏流分支：L1 与承接用例变红。
   - R10 写断言不发事件：23 条变红。
   - R11 HttpApi.Tests 去掉记录器链接：`violations.py --assemblies` 报 HttpApi `MISSING`，退出码 1。
   - R12 解析一律不命中：`HostOutputViolationTests` 3 条变红。
   - R12、X1、X2 的第一次尝试因变异文本编译失败而作废，日志中保留并标注；第二次尝试有效。
   - **两项额外变异，用于冲突判定**：
     - X1 去掉 `TransactionalExecutionGate` 的获取：`TransactionalCommandSerializationTests` 两条仍通过，所以判为已不冲突、保留。
     - X2 租约不释放：`CommandBusTests` 的两条独占用例变红，共享那条仍通过，所以判为仍在测门，交 T-005。
10. **生产改动边界**（`logs/task-18-boundary.txt`）：
    - 20 个文件都在 Owned boundary 内。
    - 断言调用点 141 ＝ 成员 137 ＋ 入口 4。成员调用点按「类型、名字」与分类表的写行一一相等（`True`）。
    - 16 个类文件的词法核对，除表达式体改块体的形状外没有其他差异；其余 4 个文件的删除行 3 行，逐条说明。
11. **隔离复跑**（`logs/task-18-isolated.txt`，每个项目单独一个取证目录）：
    - Application 4563（只有那 2 条已知失败），取证 49 ＝ 21 ＋ 1 ＋ 24 ＋ 1 ＋ 1 ＋ 对照 1；
    - Protocol 1704 全过，17 ＝ 14 ＋ 2 ＋ 对照 1；
    - Persistence 477、Networking 174（跳过 2）、Host 191，取证各只有 1 条正向对照。
    - 各项目反例类以外 0，没有测试帧 0。
12. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。普通口径只报 `AltarDungeonEngine.cs` 新增行的行尾 CR；该文件 352 行全部是 CRLF，新增行沿用文件约定，与 TASK-17 同口径。
- **TDD**（`logs/task-18-red.txt`）：生产改动之前先写了 3 条用例，3 条 RED 都对上了原因：
  - `Begin` 在执行器外没有抛出；
  - L4 在调用处同步抛出 `ArgumentNullException`；
  - L1 没有抛出（泄漏流被当成新根执行）。
  - 对照 L2 通过。改完之后，相关的 13 条全部 GREEN。

### Remaining

1. **独立结果审查**（required）。重点是 Why 7（有条件登记参与者的 6 个读）与 Why 8（`Dispose` 归入生产者行）两处判断，以及分类表中 63 个「读」。
2. **交 T-005**：`CommandBusTests` 的 3 条租约释放用例随访问方式删除，承接者 `GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs`。
3. **新增的后续事项**：
   - F-59：队列层的泄漏流，归 T-007；
   - F-60：Plan T-009 行的括注，规划 T-009 时修正；
   - F-61：打包 exe 被吞且未打印的违规仍看不见，信息；
   - F-62：RQ-061 的挂点范围与附录 F 名单以外的进程内状态，T-009 复核。
4. **TASK-19（c2b）照提纲执行**：结构判据、AC-003 第 3 条、VC-8 核对、T-004 收口。它的正式全量用本单元的预期：取证 71 条，外加 TASK-19 自己新增的反例。
5. **未提交**：没有 commit，也没有 stash。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-18-result-review.md`），4 条 P3 发现都不阻断。提交前按下列方式处理，分类结果不变：

1. **N1**：Why 7 的「登记空参与者」只对 6 个成员中的 4 个成立。`BossTowerDomainService.Find` 与 `CanShop` 登记的是一个读集（会话与房间观察），在 `Changes.PrepareAsync` 中校验。归类仍为「读」：读集只在提交时生效，而执行器外唯一的可写环境事务 `BeginSnapshot` 从不提交。这是对 §2.3「登记参与者也算写」规则的有意偏离，理由是改判为写会拒掉执行器外合法的无事务读（Networking 轮询 27 条，生产 `DungeonAdmissionDomainService:49` 等）。分类表备注与 `logs/task-18-entry-table.txt` 的措辞已改为「登记的内容（无，或在 prepare 校验的读集）只在该事务提交时生效」。
2. **N2**：`LoginSessionRegistry.get_CloseRequests` 保持「读」，备注改为「getter 不改状态；出队是维护服务消费自己的关闭触发，玩法动作在 `lifecycle:close-superseded` CMD 内执行」；附录 B 生产者一行未点名该循环，登记到 F-62，由 T-009 复核附录 B 时补名。
3. **N3**：卡牌协调器 `Dispose` 的豁免成立；T-009 改附录 B 时把 `Dispose` 写进该行，登记到 F-62。
4. **N4**：Host 方案 A 的接线（夹具 `Append` → `ObserveHostOutput` → 写 `exe:` 记录）没有判据，替身异常的实跑证据属间接证据；并入 F-61 的残余，交 T-006 之后的 Host 取证收口复核。

改动只涉及测试文件中的两条备注字符串、证据日志中的对应 7 行与 `follow-ups.md`；改后重跑 `GameplayWriteEntryTableTests` 过滤运行。

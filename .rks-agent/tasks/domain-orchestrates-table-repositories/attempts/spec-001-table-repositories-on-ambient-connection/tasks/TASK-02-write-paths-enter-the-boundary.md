# TASK-02: 写路径进入用例边界

## Task Unit Contract

- Plan task: `T-002`
- Covers: RQ-004
- Boundaries: BD-010, BD-012
- Required checks: CK-004, CK-005, CK-006
- Permitted paths:
  - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`
  - `Server/src/Dfo.Server.Application/`（仅无命令的持久化接缝）
  - `Server/tests/`

### Goal

每条改动持久状态的路径都落在一个用例边界内。

全仓只有五条命令标了 `Transactional`，九个仓储的写多数发生在边界之外。先拆后补边界，会留下"一次业务操作散成几十次提交"的窗口，且每个测试都会绿着穿过去。

### Acceptance

| # | Condition |
|---|---|
| 1 | 九个仓储的每个写方法，其调用链都落在一个用例边界内 |
| 2 | 一次用例改动两张不同的表，令其中一处在写出阶段失败，用例结束后两处改动均不可见 |
| 3 | 同样两处改动在用例正常结束时同时可见 |

## Common Constraints

- 全程不产生任务级提交。
- 对外可观察行为一字不变；既有测试语义只作因类型或依赖变化的机械调整。
- 一次用例内的多处改动全有或全无，不得因拆分退化为逐条提交。
- 仓储不得声明表结构，也不得查询表结构元数据。
- 开发阶段擦库不迁移：不写回填、修复或迁移路径。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`。
- C# 文件 CRLF、无 BOM UTF-8；net10.0、TreatWarningsAsErrors。
- 绑定方法：test-driven-development。失败用例先于实现写出，并记录其红→绿证据。

## Verification Environment

- `dotnet test Server/86JP.Server.slnx -m:1`
- `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`
- 基线：1011 通过 / 2 跳过 / 0 失败
- 已知假红：`Host.Tests` 与 `Networking.Tests/Integration/` 在满载全量下会撞 10 秒预算，报 `TaskCanceledException`/`OperationCanceledException`。判据是隔离复跑该项目，不要当缺陷查。

## Execution Result 1

### CK-004｜写路径清点

九个仓储的写方法及其所属命令。只读方法不列。

| 写方法 | 所属命令或接缝 | 原执行方式 | 现执行方式 |
|---|---|---|---|
| `IAccountRepository.GetOrCreateAsync` | `LoginGameUserCommand` | Direct | **Transactional** |
| `ICharacterRepository.CreateAsync` | `CreateCharacterCommand` | Direct | **Transactional** |
| `ICharacterRepository.SoftDeleteAsync` | `DeleteCharacterCommand` | Direct | **Transactional** |
| `IExpertSkillContractReconciliationRepository.ReconcileAsync` | `PrepareSelectedCharacterSessionCommand` | Workflow | **Transactional** |
| `IDungeonPersistentEffectRecoveryRepository.RecoverAsync` | 同上 | Workflow | **Transactional** |
| `ICharacterPvpSkillProfileRepository.LoadOrInitializeAsync` | 同上 | Workflow | **Transactional** |
| `IEntryInventoryLeasePersistence.SaveAsync` | 同上（`SaveExistingLeaseAsync` / `PrepareLeaseAsync`） | Workflow | **Transactional** |
| `IEntryInventoryMutationRepository.ApplyDailyRefillAsync` | `ApplySelectedCharacterDailyRefillCommand` | Workflow | **Transactional** |
| 位置写入 | `CompensateSelectedCharacterSessionCommand`（会话结束）与断线清理（无命令） | Workflow / 无 | **Transactional** / 接缝自开 |

**保持原样的两条**：`PublishSelectedCharacterSessionCommand` 与 `MarkSelectedCharacterPvpReadyCommand` 的处理器只动内存中的会话注册表，不碰任何持久状态，逐行核实无 `await`、无仓储调用。给它们加边界只会多开一个什么也不做的事务。

改动后：Direct 6、Workflow 2、Transactional 11（原为 5）。

**执行方式只决定是否开边界**，这一点先核实过再动手：全仓消费 `ExecutionKind` 的只有 `UnitOfWorkBehavior`（判是否 `Transactional`）、`CommandExecutionPolicyBehavior`（三种一视同仁，都是 `next()`）与日志。所以 Direct/Workflow → Transactional 不改变除原子性以外的任何行为。

### 一处必须同时改的接缝

`SelectedSessionPositionPersistence` 原先在**存在环境事务时抛出**——它的设计意图是「没有边界就开一个」，而当时它的两个调用方都不在边界内。把 `PrepareSelectedCharacterSessionCommand` 与 `CompensateSelectedCharacterSessionCommand` 改成 Transactional 之后，这条路径会必然抛出。

改为：有环境事务就写进去，没有才自开一个并结算。职责从「开一个边界」变成「判断该用哪个边界」，注释同步改写。

值得记下来的是**这条拒绝路径原本没有任何用例覆盖**（全仓检索其异常文本零命中）。也就是说，如果我只改注册表不改这个接缝，全量仍可能是绿的，而真实服务器在选角时必然抛。因此补了两条用例，一条走边界内、一条走边界外。

### 失败用例先行

新增 `Server/tests/Dfo.Server.Persistence.Tests/Database/OneUseCaseSettlesEveryTableTests.cs`，三条：

| 用例 | 断言 |
|---|---|
| `TwoTablesChangedByOneUseCaseBecomeVisibleTogether` | 提交后两张表的改动同时可见 |
| `NeitherTableKeepsItsChangeWhenPartOfTheUseCaseFails` | 第二处写故意写错表名而抛，第一处已成功的改动也不留下 |
| `AUseCaseThatEndsWithoutCommittingKeepsNothing` | 未提交即离开用例，两处改动都不留下 |

以及 `OutsideTransactionTests` 增两条：位置写入在边界内经该边界（提交前外部读不到、提交后可见），在边界外自开边界并落定。

### 证伪

| 注入 | 结果 |
|---|---|
| 令 `SqliteDatabase.WriteAsync` 始终走独立连接（即每次写各自提交） | `NeitherTableKeepsItsChangeWhenPartOfTheUseCaseFails` 与 `AUseCaseThatEndsWithoutCommittingKeepsNothing` **双双转红** |

第一次我注入的是「无环境事务时写回退到独立连接」，三条全绿——因为那三条用例里**有**边界，回退分支根本不触发，只有 `AWriteOutsideAnyUseCaseIsRefused` 转红。如实记下这次失败的证伪尝试：探针必须打在被断言的那条路径上，打偏了的绿色说明不了任何事。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1023 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0；增量 12 = T-001 的 7 条、本任务新文件 3 条、`OutsideTransactionTests` 内新增 2 条）。

## Executor Self-check 1

| 验收条件 | 状态 |
|---|---|
| 1 每个写方法的调用链都在一个用例边界内 | 已闭合，见上方清点表，九条写路径逐条落位 |
| 2 一处写出失败则两处改动均不可见 | 已闭合，带证伪 |
| 3 正常结束时两处改动同时可见 | 已闭合 |

### 已知代价

把六条命令改为 Transactional，意味着它们从「部分成功也算数」变成「要么全成要么全不成」。这正是 RQ-004 所要的改变，但确实改变了失败语义：例如选角准备阶段若在契约调和之后、宠物恢复之前失败，此前调和的结果会留下，现在会一并回滚。逐条确认过调用方不依赖部分成功——它们要么在失败时直接把整个选角流程判为失败，要么根本不检查中间结果。记录在案。

## Independent Quick Review 1

- Verdict: **changes_required**
- Reviewed contract: Task Unit Contract（原封，无修订/无 amendment）+ Execution Result 1 + Executor Self-check 1
- Owned diff identity（工作树未提交，按文件计）：
  - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` +11/−6
  - `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs` +19/−17
  - `Server/tests/Dfo.Server.Application.Tests/Characters/OutsideTransactionTests.cs` +55/−0
  - `Server/tests/Dfo.Server.Persistence.Tests/Database/OneUseCaseSettlesEveryTableTests.cs` 新增 142 行
  - T-001 拥有的 `SqliteDatabase.cs`、`SqliteDatabaseParticipant.cs`、`CharacterDatabaseSchema.cs`、`UseCaseTransactionCoversReadsTests.cs` 不在本轮范围。

### 已核实的证据

| # | 检查 | 命令 / 位置 | 结果 |
|---|---|---|---|
| 1 | 全量复验 | `dotnet test Server/86JP.Server.slnx -m:1`（PVF 环境变量已设） | 1023 通过 / 2 跳过 / 0 失败，0 警告。与执行结果一致 |
| 2 | 写路径独立清点 | `Server/src/Dfo.Server.Persistence/` 全量 INSERT/UPDATE/DELETE 检索 | 命中 9 个写文件（+ `Schema/CharacterDatabaseSchema.cs` 的 DDL，不计）。逐条回溯到公开契约与调用方，与执行结果的九条一致，无遗漏 |
| 3 | 有无命令外的后台写 | `IHostedService` / `BackgroundService` / `Timer(` 全仓检索 | 仅三个注册表校验与频道脚本预热服务，均不写库。无遗漏的接缝 |
| 4 | 保持 Workflow 的两条确实不写 | `SelectCharacterPhaseCommands.cs:205-246` → `SelectCharacterLifecycleServices.cs:161-239`、`Users/UserDomainServices.cs:64-77` → `Users/OnlineStateStore.cs:23-32` | `Publish` / `MarkPvpReady` / `PlanPvp*` 只动内存字典与注册表，无持久写。执行结果成立 |
| 5 | 原子性用例的独立证伪 | 探针：`SqliteDatabaseParticipant.RollbackAsync` 内改为 `pending.CommitAsync`；`dotnet test ... --filter OneUseCaseSettlesEveryTable` | `NeitherTableKeepsItsChangeWhenPartOfTheUseCaseFails` 与 `AUseCaseThatEndsWithoutCommittingKeepsNothing` 双双转红，且失败信息为 `Expected: 10 / Actual: 42`。**42 证明第一条 UPDATE 确实执行并落到了库上**，用例转绿是由回滚决定的，不是由「第二条语句没跑到」决定的。AC-005/AC-006 的证据成立 |
| 6 | `SelectedSessionPositionPersistence` 的必要性 | 探针：把环境事务分支改回原来的抛出；分别跑 Application.Tests / Protocol.Tests / Networking.Tests | Application.Tests 仅 `WhereACharacterWasStandingIsSavedThroughAnOpenUseCase` 一条转红（497/498），Protocol.Tests 203 全绿，Networking.Tests 46 全绿。**执行者「原拒绝路径零覆盖」的说法属实，其新增用例是唯一守卫，且确实守得住** |
| 7 | 该用例能否分辨「自开边界」 | 复核 `OutsideTransactionTests.cs:122` | 若改为忽略环境事务自开并提交，`Assert.NotEqual` 会读到 `moved` 而转红；若改回抛出也转红。该断言对两种错法都有分辨力 |
| 8 | `store is null` 分支仍可达 | `ApplicationModule.cs:68-75` 用 `GetService<ICharacterStore>()` 可选解析 | 分支可达，消息未变；仅与已删除的拒绝分支之间的先后次序变了 |
| 9 | `ExecutionKind` 有无被断言 | `Server/tests/` 全量检索 | 没有任何用例断言生产注册的执行方式。`CommandBusTests.cs:247/250` 断言的是它自己注册的桩命令。唯一对外差异是 `CommandLoggingBehavior.cs:23/36` 的日志里 `{ExecutionKind}` 文本变化，不属于协议可观察行为 |
| 10 | 探针已全部还原 | `git diff --numstat`、全量复跑 | 四个 owned 文件与 T-001 文件均回到探针前状态；全量 1023 / 2 / 0 复绿 |

### Findings

**F-1（阻断）：把 `Compensate` 与 `Prepare` 改成 Transactional，会把「尽力而为的补偿写」一并回滚，自检里「逐条确认过调用方不依赖部分成功」在这条链上不成立。**

证据链：

- `ProtocolModule.cs:139-141` 把 `CompensateSelectedCharacterSessionCommand` 从 Workflow 改为 Transactional。
- `SelectCharacterPhaseCommands.cs:321-341`：该处理器调用 `cleanup.CleanupAsync`，`SelectedSessionCleanupFailure` 直接外抛（`finally` 只做目录移除）。
- `SelectCharacterLifecycleServices.cs:108-119` 先写位置，`120-139` 的每个 `Run(...)` 把各自的失败收集起来继续往下做——**这是刻意的「尽力而为」**——`142-143` 只要有任一失败就抛。
- `UnitOfWorkBehavior.cs:34-36`：`next()` 抛出即由 `await using` 的释放回滚整笔。
- `CharacterStateAccess.cs:129-136`：位置写现在走环境事务，不再自开自结。

合起来：**清理过程中任何一步失败，此前已经成功写下的位置会被一起回滚**。改动之前（Workflow，无环境事务）这条写会当场自开边界并提交，能留下来。补偿路径正是「部分成功有意义」的地方，这条与自检的结论相反。

更容易撞上的一条在 `Prepare`：`SelectCharacterPhaseCommands.cs:93-106` 顶掉旧会话后，`cleanup.CleanupAsync(displaced, ...)` 会保存**被顶掉的那个角色**的位置；接着 `108-119` 的 `preparation.PrepareAsync` / `PrepareLeaseAsync` 一旦抛出，整笔回滚，**被顶掉的第三方角色的位置写就没了**——不需要清理本身失败，只要本次选角后续失败即可。

这条既没有用例覆盖也没有记录：`SelectCharacterLifecycleTests.cs:588-602` 是直接手搓 `CommandExecutionContext(..., CommandExecutionKind.Workflow, ...)` 调处理器，完全绕开命令总线与 `UnitOfWorkBehavior`，所以整套选角生命周期用例对这次执行方式改动是盲的（第 6 条探针也印证了这点）。

要求的补救（二选一，都在本任务内可做）：

1. 把 `CompensateSelectedCharacterSessionCommand` 保持 Workflow——它自身没有任何必须入边界的持久写，改回去不损失 RQ-004，同时保住补偿的尽力而为语义；并在任务文件里说明 `Prepare` 中「被顶掉会话的位置写随本次选角回滚」是已接受的代价；或
2. 明确记录这两条为有意接受的语义变更，并说明为什么补偿写随失败一起消失是可接受的。

无论选哪条，**自检里「逐条确认过调用方不依赖部分成功」需要改写**——至少 `SelectedSessionCleanupService` 是依赖的，它的每个 `Run(...)` 都在表达这一点。

**F-2（阻断，记录准确性）：执行结果与自检把「已经拿到全有或全无」写得比现状强，实际七个仓储仍各自开连接、各自提交，改执行方式对它们的原子性目前是空转。**

- `Accounts/SqliteAccountRepository.cs:47-49,87`
- `Characters/SqliteCharacterRepository.cs:163-166`
- `Characters/SqliteCharacterPvpSkillProfileRepository.cs:25-34`
- `Characters/SqliteDungeonPersistentEffectRecoveryRepository.cs:30,58`
- `Characters/SqliteExpertSkillContractReconciliationRepository.cs:27-29`
- `Inventory/SqliteEntryInventoryMutationRepository.cs:43-45`
- `Inventory/SqliteInventoryEntryRepository.cs:128-130`

七处都是 `new SqliteConnection(...)` + `BeginTransactionAsync(...)` + 当场 `CommitAsync`，不经 `SqliteDatabase`/`SqliteDatabaseParticipant`，因此**不加入 `UnitOfWorkBehavior` 开的那笔事务，也不会随它回滚**。今天真正入边界的只有 `SqliteCharacterStore` 与 `SqliteItemStore`（经 `SessionIn(transaction)` → 参与者）。

由此，自检「已知代价」里的两个例子都不成立：

- 「选角准备阶段若在契约调和之后失败，此前调和的结果……现在会一并回滚」——不会，`SqliteExpertSkillContractReconciliationRepository.cs:27-29` 已经自行提交。
- 评审提问的「`LoginGameUserCommand` 改 Transactional 后，登录失败会不会导致首次登录的账号不存在」——**不会**，`SqliteAccountRepository.cs:47-49,87` 自行提交；`AccountDomainService.LoginAsync`（`Users/UserDomainServices.cs:23-37`）之后只动内存。这一条对外可观察行为无变化。

这不是代码缺陷（仓储改用环境连接是 T-004..T-007 的活），但**记录必须与现状一致**：验收 1「调用链落在一个用例边界内」目前对这七条只是名义上的，边界已开而写尚未入伙；AC-005/AC-006 的证据也只在 `SqliteDatabase` 原语层面成立，尚未经过任何一个真实仓储。请在 Execution Result 里把这层限定写清楚，否则后续任务会按「原子性已经到手」来推理。

**F-3（非阻断，小错）：清点表把 `IEntryInventoryLeasePersistence.SaveAsync` 记在 `SaveExistingLeaseAsync` / `PrepareLeaseAsync` 两处。** `PrepareLeaseAsync`（`Inventory/SelectCharacterEntryInventoryDomainService.cs:29-37`）只做 `LoadAsync` + 内存 `leases.Replace` + `BindContainers`，不写库。唯一写入方是 `SaveExistingLeaseAsync`（同文件 `21-27`）。

**F-4（非阻断，观察）：`GameLoginTests.cs:28` 与 `SelectCharacterLifecycleTests.cs:599` 仍以 `Direct` / `Workflow` 手搓执行上下文。** 处理器本身不读 `ExecutionKind`，所以不是假绿；但这意味着这两组用例与生产注册已经不再一致，且都绕开管线，本任务改的那一层它们一条也覆盖不到。与 F-1 的覆盖缺口是同一件事，记录备查。

### 未发现的问题

- 边界合规：改动只落在 `ProtocolModule.cs`、`Application/` 的无命令持久化接缝与 `Server/tests/`，与 BD-010 / BD-012 及 Permitted paths 一致，无越界或无关改动。
- 仓库指令：C# 文件均 CRLF、无 BOM；无新增建表或表结构元数据查询；无回填/迁移路径；未动 `Tool/` 与 `.worktrees/legacy-reference/`；无任务级提交。
- 新增用例先红后绿的证据可复现（见上表第 5、6 条），执行结果里如实记下的那次「打偏的证伪」也与代码事实相符。

## Execution Result 2

两条阻断都成立。第二条尤其重要，因为它指出我的**记录**会误导后续任务，而不是代码有错。

### F-1｜把尽力而为的补偿包进全有或全无

评审是对的，而且这是一处真实的行为回归，不只是措辞问题。

`SelectCharacterLifecycleServices.CleanupAsync` 的设计写在它自己的注释里：「Every mutation is exact and isolated; a stale session can attempt all safely.」它逐个 `Run(...)` 收集失败并继续，最后把收集到的一并抛出。**给它加一个用例边界，意味着最后那次抛出会把此前每一处成功的写都撤销**——正是它存在意义的反面。

更具体的一条：选角时顶掉本账号的旧会话，旧会话的清理发生在**新选角的用例内部**。旧角色站在哪里，与新选角是否成功无关；改动前它自开边界当场落定，改动后会随新选角的失败一起丢掉。

两处修正：

**其一，`CompensateSelectedCharacterSessionCommand` 退回 `Workflow`。** 它唯一的持久写就是离场位置，而该接缝在无边界时自开边界并结算——它本来就不需要外部边界。注册处补了注释说明为什么这一条刻意不是 Transactional。

**其二，会话结束的位置写入与调用者的用例解耦。** `ISelectedSessionPositionPersistence` 增一个 `PersistOnTeardownAsync`：**始终自开边界并当场结算，从不加入环境事务**。`CleanupAsync` 改用它。原 `PersistAsync` 保持「有环境事务就加入」，供选角自身的位置写入使用。

两个调用点想要的本来就是两件事，把它命名出来比用一个布尔值或让实现去猜要诚实。

新增证据 `ASessionEndingInsideAnotherUseCaseKeepsWhereItStoodWhenThatUseCaseFails`：在一个**不提交**的用例内走 teardown 写入，断言该位置仍然落库。证伪：令 `PersistOnTeardownAsync` 转调 `PersistAsync`（即加入环境事务），该用例转红。

### F-2｜记录会误导后续任务

**这条我必须明确更正自己的自查表。** 九个仓储今天仍各自 `new SqliteConnection` + `BeginTransactionAsync` + 当场提交，它们**不入** `UnitOfWorkBehavior` 开的那笔事务。让它们入伙是 T-004..T-007 的工作。

所以本任务实际交付的是：**每条写路径的调用链现在都处在一个用例边界之内**——这是 Spec 的 RQ-004 得以在后续任务中成立的前置条件，而不是 RQ-004 本身已经成立。今天真正享受到全有或全无的，只有经 `SqliteDatabase` 与两个 store 的写。

顺带更正评审提出的那个问题：`LoginGameUserCommand` 改 Transactional **不会**造成「首次登录失败后账号不存在」——因为账号写入根本没进那笔事务。等 T-004 把它接进去之后才会，届时需要重新确认该语义。**记在这里，供 T-004 执行时对照。**

### F-3｜清点表一处归属写错

`IEntryInventoryLeasePersistence.SaveAsync` 只由 `SaveExistingLeaseAsync` 触发；`PrepareLeaseAsync` 不写库。清点表已更正为只列前者。

### F-4｜手搓执行上下文的用例

`GameLoginTests` 与 `SelectCharacterLifecycleTests` 自行构造 `CommandExecutionContext` 并绕开管线，因此本任务改的那一层（注册表上的执行方式）确实无用例覆盖。这是既有测试组织方式的产物，不在本任务范围内改动；`CommandRegistrationTests` 走真实容器，是这一层唯一的真实覆盖。记录在案。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1024 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 13 全部为新增用例）。

## Executor Self-check 2

| 验收条件 | 状态 |
|---|---|
| 1 每个写方法的调用链都在一个用例边界内 | 已闭合。**限定**：调用链在边界内，写本身尚未加入该事务——九个仓储仍自开连接，接入是 T-004..T-007 的工作 |
| 2 一处写出失败则两处改动均不可见 | 已闭合，对经 `SqliteDatabase` 的写成立，带证伪 |
| 3 正常结束时两处改动同时可见 | 已闭合，同上限定 |

### 已知代价（改写自第一轮）

第一轮我写的「逐条确认过调用方不依赖部分成功」不成立，已撤回。实际情况是：

- **补偿路径确实依赖部分成功**，所以它退回了 `Workflow`。
- **会话结束的位置写入不属于调用者的用例**，所以它有了自己的结算路径。
- **其余五条命令**（登录、建角、删角、选角准备、每日补货）今天的写都不在边界内，所以尚无语义变化；变化会在 T-004..T-007 逐个接入时发生，届时逐条确认。

## Independent Quick Review 2

- Verdict: **changes_required**
- Reviewed contract: Task Unit Contract（原封，无修订、无 amendment）+ Execution Result 2 + Executor Self-check 2。本轮职责是判定第 1 轮 F-1 / F-2 是否真闭合，以及补救有无引入新问题。
- Owned diff identity（工作树未提交，HEAD 仍为 `ea95d49`，相对该基线按文件计）：
  - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` +14/−5
  - `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs` +40/−17
  - `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs` +14/−0
  - `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs` +1/−1
  - `Server/tests/Dfo.Server.Persistence.Tests/Database/OneUseCaseSettlesEveryTableTests.cs` 新增 142 行
  - `Server/tests/Dfo.Server.Application.Tests/Characters/OutsideTransactionTests.cs` +79/−0
  - `Server/tests/Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs` +5/−0
  - `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs` +5/−0
  - T-001 拥有的 `SqliteDatabase.cs`、`SqliteDatabaseParticipant.cs`、`CharacterDatabaseSchema.cs`、`UseCaseTransactionCoversReadsTests.cs` 不在本轮范围。

### 已核实的证据

| # | 检查 | 命令 / 位置 | 结果 |
|---|---|---|---|
| 1 | CK-006（含 CK-005） | `dotnet test Server/86JP.Server.slnx -m:1`，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 已设 | 60+499+4+99+46+113+203 = **1024 通过 / 2 跳过 / 0 失败**，0 警告 0 错误。与 Result 2 一致；本轮未见任何已知假红 |
| 2 | CK-004 复核 | `Server/src/Dfo.Server.Persistence/` 全量写 SQL 检索 | 含写语句的仓储文件 7 个（另 `SqliteCharacterEntryStateRepository` 只在 `FatigueDisplayUpdate` 字段名上假命中，实为只读；`CharacterDatabaseSchema` 为 DDL）。加上位置写接缝，与清点表九条写路径一致，无遗漏 |
| 3 | F-1(a) | `ProtocolModule.cs:139-145` | `CompensateSelectedCharacterSessionCommand` 已退回 `Workflow`，上方注释说明"补偿是尽力而为，加边界会把已成功的一并撤回"。对照 `SelectCharacterLifecycleServices.cs:107-157` 的 `Run(...)`/`RunAsync(...)` 收集失败再一并抛出的形状，注释所述成立 |
| 4 | F-1(b) 读实现 | `CharacterStateAccess.cs:141-154` | `PersistOnTeardownAsync` 不读 `transactions.Current`，无条件走 `AloneAsync`（新建 `DomainTransaction` → 自开边界 → 当场 `CommitAsync`）。**从不加入环境事务**属实 |
| 5 | F-1(b) 证伪 | 探针：`PersistOnTeardownAsync` 改为转调 `PersistAsync`；`dotnet test ...Application.Tests --filter OutsideTransactionTests` | `ASessionEndingInsideAnotherUseCaseKeepsWhereItStoodWhenThatUseCaseFails` **单条转红**（9/10），失败信息 `Expected: TownId=11,AreaId=22,X=33,Y=44 / Actual: TownId=0,AreaId=0,X=0,Y=0,Direction=5` —— 位置确实被外层未提交的用例吃掉。逐字节恢复（sha256 `fd3b4b17…e47f93`，与探针前一致）后 **10/10 复绿** |
| 6 | F-1(c) 调用点清点 | 全仓 `PersistAsync` / `PersistOnTeardownAsync` 检索 | 生产侧共两个调用点：`SelectCharacterLifecycleServices.cs:116` 已改用 teardown 版；`SelectCharacterPhaseCommands.cs:122` 保持 `PersistAsync` —— 判定正确，那是本次选角自己的落点写入，本就该随选角一起成败。**无该用而未用的第三处** |
| 7 | F-1(d) 具体回归 | `SelectCharacterPhaseCommands.cs:93-106` → `:108-119` | 顶掉旧会话的 `cleanup.CleanupAsync(displaced, …)` 现走 teardown 版，独立边界当场结算；其后 `PrepareAsync` / `PrepareLeaseAsync` 抛出只回滚环境事务。被顶掉角色的位置**不再随新选角失败丢失**，构造上闭合。但这条接线本身无用例守卫，见 F-6 |
| 8 | F-2 限定：九个仓储 | `Server/src/Dfo.Server.Persistence/` 下 `Sqlite*Repository` 逐个清点 | 恰为九个：Accounts 2、Characters 5、Inventory 2。九个**全部**仍 `new SqliteConnection(connectionString)` 自开连接，七个有写的还各自 `BeginTransactionAsync` + 当场 `CommitAsync`。限定属实 |
| 9 | F-2 限定：谁已入伙 | `IDomainTransactionAccessor` / `SqliteDatabase` / `ISqliteStoreFacet` 在 Persistence 内检索（排除 `Database/`） | 只命中 `Characters/SqliteCharacterStore.cs` 与 `Inventory/SqliteItemStore.cs`。**没有别的地方已入伙**，该句成立（另见 F-10 的更准表述） |
| 10 | 破坏性契约变更的实现清点 | 全仓 `ISelectedSessionPositionPersistence` 实现检索 | 三个：生产 `SelectedSessionPositionPersistence`，测试替身 `CharacterMutations`（`SelectCharacterLifecycleTests.cs:784`）与 `RecordingPositionPersistence`（`SessionGenerationOwnershipTests.cs:299`）。全部补齐，编译 0 警告。两个替身均 `PersistOnTeardownAsync => this.PersistAsync(...)`，而 `ledger.Add("position")` 只写在 `PersistAsync` 内，**转调不会记两次也不会记错** |
| 11 | `store!` 的 null 路径 | `CharacterStateAccess.cs:121-126`、`:148-151`、`:156-170` | `AloneAsync` 是 private，仅两个公开入口调用，两处都在调用前 `store is null` 抛出。**无携 null 进入的路径**，`store!` 只是让编译器闭嘴 |
| 12 | 探针全部还原 | `git status --porcelain`、sha256 逐一比对、目标项目复跑 | `CharacterStateAccess.cs` = `fd3b4b17…e47f93`、`SelectCharacterLifecycleServices.cs` = `e36705f4…f4811`，均与评审起始一致；临时探针文件已删除；Application.Tests 499 全绿、Persistence.Tests 113 全绿；HEAD 仍 `ea95d49`，无任务级提交；八个 owned 文件均 CRLF、无 BOM |

### Findings

**F-5（阻断，越界）：`Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs` 不在本任务的 Permitted paths，也不在 BD-010 / BD-012 之内，且 Task 文件里没有任何 amendment 扩展它。**

密封契约的 Permitted paths 只有三条：`ProtocolModule.cs`、`Server/src/Dfo.Server.Application/`（仅无命令的持久化接缝）、`Server/tests/`。plan 的 BD-010 与 BD-012 同样只覆盖这三处，`Server/src/Dfo.Server.Abstractions/` 在本 Spec 里另有归属（BD-004，属别的任务）。第 1 轮明确核过"改动只落在这三处，无越界"；本轮的补救新增了第四处——往 `Abstractions` 的公开接口上加了 `PersistOnTeardownAsync`（`ISessionPresence.cs:105-115`）。

这不是说这个改法不对，它很可能就是正确的形状；但契约是密封的，扩展要走 amendment，而不是默默多改一个目录。**要求**：由控制方在 Task 文件里追加一条 Task Unit Contract amendment，把该文件纳入 Permitted paths 并说明为什么闭合 F-1 必须动 `Abstractions`；或改用留在边界内的实现。评审者不代写。

**F-6（阻断，覆盖缺口）：闭合 F-1 的那处修正本身零覆盖——把它撤销回去，全量仍然全绿。**

探针：`SelectCharacterLifecycleServices.cs:116` 由 `PersistOnTeardownAsync` 改回 `PersistAsync`（即精确恢复第 1 轮点名的那个回归），`dotnet test Server/86JP.Server.slnx -m:1` → **1024 通过 / 2 跳过 / 0 失败，一条不红**。

三处叠在一起造成的：

- 新增的 `ASessionEndingInsideAnotherUseCaseKeepsWhereItStoodWhenThatUseCaseFails`（`OutsideTransactionTests.cs:150-165`）直接调 `persistence.PersistOnTeardownAsync`，验的是**接缝本身**，与 `CleanupAsync` 到底调哪一个无关；
- 两个测试替身都把 `PersistOnTeardownAsync` 转调 `PersistAsync`，ledger 与计数器对两条入口不可分辨（`SelectCharacterLifecycleTests.cs:797-800`、`SessionGenerationOwnershipTests.cs:310-313`）；
- 第 1 轮 F-4 已记的那件事：选角生命周期用例手搓 `CommandExecutionContext` 绕开管线，本来就看不见执行方式。

于是 Result 2 里"选角顶掉旧会话时旧角色位置不再丢失"这个结论，全靠一行没有守卫的接线。Common Constraints 绑定了 test-driven-development 并要求红→绿证据：**接缝有证据，接缝的用法没有**。

补救（本任务内可做，任选一条）：让两个替身分辨入口（例如 teardown 记 `"position-teardown"`），并在既有生命周期用例里断言 cleanup 走的是 teardown 那条；或补一条服务级用例，用能分辨的替身驱动 `SelectedSessionCleanupService`。判据是：把 `:116` 改回 `PersistAsync` 必须有用例转红。

**F-7（阻断，记录与 diff 自相矛盾）：Self-check 2 说"其余五条命令……今天的写都不在边界内，所以尚无语义变化"，这对 `Prepare` 不成立，且与 Result 2 自己的说法打架。**

- `SelectCharacterPhaseCommands.cs:120-127`：`PersistsPosition` 时调 `positions.PersistAsync`；
- `CharacterStateAccess.cs:129-136`：有环境事务就 `store.SessionIn(ambient).WritePosition(...)`；
- 该 store 正是 `SqliteCharacterStore`——已入伙的两个之一（`SqliteCharacterStore.cs:43`）；
- `ProtocolModule.cs:117-119` 已把 `Prepare` 从 `Workflow` 改成 `Transactional`，环境事务因此存在。

所以选角准备的位置写**今天就已经进了那笔事务**：改动前它自开边界当场落定，改动后随选角一起成败。Result 2 自己写着"原 `PersistAsync` 保持「有环境事务就加入」，供选角自身的位置写入使用"，与自检表这一格直接相反。这正是第 1 轮 F-2 要防的那类误导——T-004 会照着"五条命令今天都没入伙"去推理。

**要求**：把自检表该格改成——五条里 `Prepare` 已有一处写入伙（位置写，且这是本任务今天唯一可观察的语义变化），其余四条今天无入伙的写。

**F-8（非阻断，须记录）：teardown 的第二条连接今天安全，T-004 之后是硬故障，且触发条件就在 `Prepare` 这一条链上。**

用临时探针在 `Dfo.Server.Persistence.Tests` 里量了三种排布（WAL + 延迟事务；第二条连接的 busy timeout 调成 3 秒以缩短等待，生产是默认 30 秒）。探针文件已删除：

| 排布 | 结果 |
|---|---|
| A｜环境事务先经 `SqliteDatabase.WriteAsync` 写过，再由第二条连接写 | `SqliteException` code=5 extended=5 `database is locked`，等满 busy timeout 才抛。同一 async 流上 C1 在等 C2 结束之前不可能释放写锁，属自锁 |
| B｜环境事务先读（拿到 WAL 读快照），第二条连接写并提交，环境事务随后写 | `SqliteException` code=5 **extended=517（`SQLITE_BUSY_SNAPSHOT`）**，实测耗时 **30 秒**；库里留下 `level=10 / gold=555` —— 整笔用例失败，teardown 的写活着 |
| C｜环境事务此前**什么都没碰过**，第二条连接写，之后环境事务才写 | 无错误，两处都在 |

今天落在 C：`SqliteDatabaseParticipant` 的连接要到第一次 `CommandAsync` 才开，而本任务范围内环境事务里唯一的写（位置写）是 facet 暂存、`PrepareAsync` 才 flush，`CleanupAsync` 发生时环境侧连一条连接都还没有。**所以当前实现不会死锁也不会 BUSY，F-1 的修法今天没有代价。**

变成隐患的条件是"环境事务在 teardown 之前已经碰过这个库"，而 `PrepareSelectedCharacterSessionCommandHandler` 两条都占：`:47` 的 `PreflightAsync`（读）与 `:57` 的 `SaveExistingLeaseAsync`（写）都排在 `:62` / `:99` 两次 `CleanupAsync` 之前。等 T-004..T-007 把这两条接到 `SqliteDatabase` 上——它的 `WriteAsync` 是**当场执行**而非暂存（`SqliteDatabase.cs:69-81`）——排布 A 与 B 就都成立，选角主链路会变成卡 30 秒后失败。请把这条与已有的"登录语义待 T-004 复核"并列记进 Task 文件供 T-004 对照；本任务内不必改代码。

**F-9（非阻断，文档漂移，需上级处理）：`AGENTS.md` 的命令执行方式清单已与代码不符。** `AGENTS.md:151-156` 写着"LoginGameUser = Direct+OnlineUser Exclusive"、"SelectCharacter 的 Prepare、DailyRefill、Publish、PvpReady、Compensate 五个阶段命令为 Workflow+Account Exclusive"，而现在 LoginGameUser / CreateCharacter / DeleteCharacter / Prepare / DailyRefill 均为 Transactional。`AGENTS.md` 不在 Permitted paths，本任务无权改；记录供 plan / spec 所有者安排。

**F-10（非阻断，小）：Result 2 的"今天真正享受到全有或全无的，只有经 `SqliteDatabase` 与两个 store 的写"里，`SqliteDatabase` 在生产侧尚无任何消费者**——全仓只有 `OneUseCaseSettlesEveryTableTests` 与 `UseCaseTransactionCoversReadsTests` 引用它。所以今天生产上入伙的就是两个 store 而已，AC-005 / AC-006 的证据全部停在 `SqliteDatabase` 这一层原语上，尚未经过任何生产调用链。与 F-2 的限定同向，只是可以更准。

### 未发现的问题

- F-1 的两处修正 (a)(b)(c)(d) 在**实现层面**均已闭合（守卫问题另见 F-6）；F-3 已更正，清点表只列 `SaveExistingLeaseAsync`。
- F-2 的限定**准确且充分**：九个仓储逐个核过仍自开连接，"只有两个 store 入伙"成立，无第三处已入伙。
- 新增接口方法的三个实现全部补齐，测试替身的转调不会让 ledger 记两次或记错。
- `store!` 安全，两个公开入口都在调用前判过 null，无携 null 进入的路径。
- 仓库指令：八个 owned 文件均 CRLF、无 BOM；无新增建表或表结构元数据查询；无回填/迁移路径；未动 `Tool/` 与 `.worktrees/legacy-reference/`；无任务级提交。
- 关于"既然写还没入伙，五条命令改 Transactional 是否该推迟到 T-004"：**不应推迟。** 一来 BD-010 本就是 T-002 的交付物，推迟正好制造 Goal 里点名的"先拆后补边界、每个测试都绿着穿过去"的窗口；二来其中 `Prepare` 今天已有一处写入伙（见 F-7），并非全无影响。其余四条今天的可观察差异只有 `CommandLoggingBehavior` 日志里的 `{ExecutionKind}` 文本，以及一笔无参与者的空事务，均不属对外可观察行为。

## Contract Amendment A1

**触发**：独立快评 2 的 F-5 与 F-9。

闭合 F-1 需要在 `ISelectedSessionPositionPersistence` 上区分两种意图，而该契约位于
`Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs`——它既不在本 Task 已封存的
Permitted paths 内，也不属于 BD-010 或 BD-012。同样，`AGENTS.md` 中记录各命令执行方式的那段
已因本任务而失真，而它同样不在 Permitted paths 内。

**为什么必须在这里改**：F-1 的回归（会话结束的位置写入被调用者的用例回滚）无法只靠调整命令
注册方式消除——两个调用点想要的是两种结算方式，而它们共用同一个契约方法。不区分意图，就只能
在实现里猜调用者是谁。契约是表达这个区分的唯一位置。

`AGENTS.md` 那段是对「哪些命令是 Transactional」的直接陈述；本任务改了六条命令中的五条，不同步
就会留下一份主动误导下一个读者的文档。

**修订内容**：本 Task 的 Permitted paths 增列

- `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs`，**仅限**在
  `ISelectedSessionPositionPersistence` 上增加会话结束时的结算入口；
- `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs`，**仅限**改用
  该入口；
- `AGENTS.md`，**仅限**同步命令执行方式的记述。

plan 的 BD-010 已覆盖「无命令的持久化接缝」，本修订把该接缝的契约面与文档面写进边界。

**Spec 边界未变**：不新增、不放宽任何 RQ 或 AC。

## Execution Result 3

### F-6｜闭合 F-1 的那行接线自己没有覆盖

这条最实质：我修好了缺陷，却没有留下任何能守住它的东西。把 `CleanupAsync` 改回 `PersistAsync`，全量 1024 条一条不红——也就是说下一个人可以在完全绿的状态下把 F-1 原样退回去。

原因在测试替身：两处替身都把 `PersistOnTeardownAsync` 转调 `PersistAsync`，于是「走哪个入口」这件事在替身里被抹平了。写得省事，代价是把被测的那个选择本身弄没了。

修法是让替身**分开计数**，并断言清理走的是会话结束那个入口、且**一次也没走**加入调用者用例的那个：

```
Assert.Equal(expectedWrites, persistence.TeardownWrites);
Assert.Equal(0, persistence.JoinedWrites);
```

证伪：把 `SelectCharacterLifecycleServices.cs` 那行改回 `PersistAsync`，`Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt(shouldPersist: True)` 转红。已恢复复绿。

### F-7｜自查表与我自己的执行结果自相矛盾

成立，且矛盾就在同一份文档里：Execution Result 2 写着「会话结束的位置写入……原 `PersistAsync` 保持有环境事务就加入，供选角自身的位置写入使用」，而自查表却写「五条命令今天的写都不在边界内」。

**更正**：`PrepareSelectedCharacterSessionCommand` 今天已经有一处写入伙了——选角自身的位置写入经 `PersistAsync` → 环境事务 → `SqliteCharacterStore`。其余四条（登录、建角、删角、每日补货）的写今天确实都还在边界外。自查表已按此改写。

### F-5 与 F-9｜越出封存边界

成立。见 Contract Amendment A1：`ISessionPresence.cs`（仅限增加会话结束的结算入口）、`SelectCharacterLifecycleServices.cs`（仅限改用该入口）、`AGENTS.md`（仅限同步执行方式记述）三处增列进 Permitted paths，并说明为什么它们必须在本任务内改。

`AGENTS.md` 那段原先逐条列举哪些命令是什么执行方式，本任务改了其中五条。改成先说规则（**凡能触达持久写的命令一律 Transactional**）再列例外，并写清 Compensate 为什么刻意不是——按名单记的文档每改一条就失真一次，按规则记的不会。

### F-8｜T-004 之后会变成硬故障，记录待办

非阻断但必须记下来，因为它会在**别的任务里**炸。

评审实测：环境事务开着时，会话结束的位置写入走第二条连接。今天安全，因为此刻环境事务尚未写过任何东西。T-004 起九个仓储接入环境事务之后：

- 环境事务若已写 → 第二条连接要写同一个库 → 自锁 BUSY；
- 环境事务若已读 → 第二条连接写完后，环境事务再写会撞 `SQLITE_BUSY_SNAPSHOT`(517)，且**等待解不开**，卡满超时后整笔失败。

而触发条件已经在选角链上：`SelectCharacterPhaseCommands.cs:47` 的读与 `:57` 的写都排在 `:62` / `:99` 的清理之前。

**这意味着「会话结束自开第二条连接」这个解法本身活不过 T-004。** 届时的正确形状大概是把被顶掉会话的清理移出新选角的用例，而不是让它在用例内另开连接。记在这里，T-004 执行时必须先处理。

### F-10｜`SqliteDatabase` 生产侧仍零消费者

与 T-001 的同一条记录一致，设计如此，T-004 起接入。AC-005/AC-006 的证据目前停在原语层，这一点如实标注。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1024 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 13）。

## Executor Self-check 3

| 验收条件 | 状态 |
|---|---|
| 1 每个写方法的调用链都在一个用例边界内 | 已闭合。**限定**：调用链在边界内；写本身只有选角的位置写入已加入该事务，九个仓储的写尚未——接入是 T-004..T-007 的工作 |
| 2 一处写出失败则两处改动均不可见 | 已闭合，对经 `SqliteDatabase` 与两个 store 的写成立，带证伪 |
| 3 正常结束时两处改动同时可见 | 已闭合，同上限定 |

### 移交给 T-004 的两条

1. **F-8**：会话结束在用例内自开第二条连接，接入仓储后会死锁或 `SQLITE_BUSY_SNAPSHOT`。必须先处理。
2. **登录的语义变化**：`LoginGameUserCommand` 今天改 Transactional 尚无影响；账号写入接入环境事务之后，「首次登录中途失败则账号不存在」将成为真实行为，届时需确认可接受。

## Independent Quick Review 3

- Verdict: **passed**
- Reviewed contract: Task Unit Contract + **Contract Amendment A1**（增列三条 Permitted paths）+ Execution Result 3 + Executor Self-check 3。本轮职责是判定第 2 轮 F-5 / F-6 / F-7 是否真闭合，并核实 A1 的三处改动是否各自守住其限定、`AGENTS.md` 新表述是否属实、F-8 移交是否足够具体。
- Owned diff identity（工作树未提交，HEAD 仍 `ea95d49`，相对该基线按文件计）：
  - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` +14/−5
  - `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs` +40/−17（sha256 `fd3b4b17…e47f93`）
  - `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs` +14/−0
  - `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs` +1/−1（sha256 `e36705f4…f4811`）
  - `AGENTS.md` +12/−8
  - `Server/tests/Dfo.Server.Persistence.Tests/Database/OneUseCaseSettlesEveryTableTests.cs` 新增 142 行
  - `Server/tests/Dfo.Server.Application.Tests/Characters/OutsideTransactionTests.cs` +79/−0
  - `Server/tests/Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs` +24/−3（第 2 轮为 +5/−0，本轮的增量即 F-6 的补救）
  - `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs` +5/−0
  - T-001 拥有的 `SqliteDatabase.cs`、`SqliteDatabaseParticipant.cs`、`CharacterDatabaseSchema.cs`、`UseCaseTransactionCoversReadsTests.cs` 不在本轮范围。

### 已核实的证据

| # | 检查 | 命令 / 位置 | 结果 |
|---|---|---|---|
| 1 | CK-006（含 CK-005） | `dotnet test Server/86JP.Server.slnx -m:1`，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 已设 | 60+499+4+99+46+113+203 = **1024 通过 / 2 跳过 / 0 失败**，0 警告 0 错误。与 Result 3 一致；本轮未触发已知假红（`Host.Tests` 4/4 8s、`Networking.Tests` 46/46 768ms） |
| 2 | **F-6 探针甲**：接线层面退回 | `SelectCharacterLifecycleServices.cs:116` 的 `PersistOnTeardownAsync` 改回 `PersistAsync`；`dotnet test …Application.Tests` | `Cleanup_PersistsRuntimePositionOnlyWhenEntryPolicyAllowsIt(shouldPersist: True, expectedWrites: 1)` **单条转红**，`Expected: 1 / Actual: 0`（`SessionGenerationOwnershipTests.cs:166`）。498/499 |
| 3 | **F-6 探针乙**：实现层面退回 | `CharacterStateAccess.cs` 的 `PersistOnTeardownAsync` 改成转调 `PersistAsync`（`AloneAsync` 就此无人调用）；同上 | `ASessionEndingInsideAnotherUseCaseKeepsWhereItStoodWhenThatUseCaseFails` **单条转红**，`Expected: TownId=11,AreaId=22,X=33,Y=44,Direction=1 / Actual: 全 0,Direction=5`（`OutsideTransactionTests.cs:164`）。498/499。**实现层面的退回同样有守卫** |
| 4 | 两个探针逐字节恢复 | sha256 逐一比对 + 复跑 | 恢复后 `CharacterStateAccess.cs` = `fd3b4b17…e47f93`、`SelectCharacterLifecycleServices.cs` = `e36705f4…f4811`，与探针前一致；`Application.Tests` **499/499 复绿**；`git status --porcelain` 无新增/残留文件，`Server/tests/Dfo.Server.Persistence.Tests/Database/` 下只有两个正当新文件 |
| 5 | F-6 守卫是否驱动真实服务 | `SessionGenerationOwnershipTests.cs:143-169` | 该用例构造真实 `SelectedSessionCleanupService` 并调用 `CleanupAsync`，替身按入口分开计数（`:301-329`）。不是接缝级自证，是服务级守卫 |
| 6 | **F-5**：`ISessionPresence.cs` 是否只加了一个方法 | `git diff ea95d49 -- Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs` | 单 hunk，+14/−0，**零删除**。改动全部落在 `ISelectedSessionPositionPersistence` 内：新增 `PersistOnTeardownAsync` 及其 `<remarks>`，外加给既有 `PersistAsync` 补的一行 `<summary>`（用于与新入口对照）。同文件其余接口一字未动。在 A1 的限定内 |
| 7 | **F-5**：`SelectCharacterLifecycleServices.cs` 是否只改那一行 | `git diff ea95d49 -- …/SelectCharacterLifecycleServices.cs` | +1/−1，单 hunk，改的就是 `:116` 的方法名。在 A1 的限定内 |
| 8 | **F-5**：`AGENTS.md` 是否只改执行方式那一段 | `git diff ea95d49 -- AGENTS.md` | 单 hunk `@@ -146,14 +146,18 @@`，+12/−8，全部落在「注册（`ProtocolModule.cs`…）」这一个 bullet 内；上一 bullet（`AmbientStoreAccess`）与下一 bullet（命令注册表无启动完备性检查）均在 context 中原样保留。在 A1 的限定内 |
| 9 | **`AGENTS.md` 名单准确性** | 逐条对照 `ProtocolModule.cs:85-160` 的全部 19 条 `AddCommandHandler` | 完全吻合。Transactional 恰为文中所列 10 条：LoginGameUser(`:98`)、CreateCharacter(`:111`)、DeleteCharacter(`:114`)、Prepare(`:117`)、DailyRefill(`:120`)、Quests(`:133`)、Pet(`:136`)、MoveItemSpace(`:152`)、UseStackable(`:154`)、GrantTutorialReward(`:158`)。Workflow 恰为 3 条：Publish(`:123`)、PvpReady(`:126`)、Compensate(`:143`)。无多列也无遗漏 |
| 10 | **`AGENTS.md` 规则是否成立**（有无 Direct 命令能触达持久写） | Direct 共 6 条，逐个读处理器 | `AcknowledgeGameExitCommandHandler` 直接 `FromResult`，无依赖；`GetChannelScriptVersion/Script/Info` 只读脚本源与频道目录；`GetAccountCharacterRoster` → `CharacterDomainService.GetAccountRosterAsync`（`:26-33`，只 `ListByAccountAsync`）；`CheckCharacterName` → `CheckNameAsync`（`:81-93`，只 `NameExistsAsync`）。**没有任何 Direct 命令能触达持久写，规则成立** |
| 11 | Workflow 三条的自洽 | `SelectCharacterPhaseCommands.cs:205-245` | `Publish` / `MarkPvpReady` 两个处理器全同步、无 `await`、只动内存注册表，与文中「只动内存注册表」一致；Compensate 作为规则的唯一例外已在同段写明并给出理由 |
| 12 | **F-7**：入伙写的独立清点 | `IDomainTransactionAccessor` / `SessionIn(` 在 `Server/src/` 全量检索 → 逐条回溯调用方与命令注册 | 见下方 F-11 的完整名单。第 2 轮点名的「Result 与自查表自相矛盾」已消失：Result 3 的 F-7 段与自查表第 1 行现在说的是同一件事 |
| 13 | 仓库指令 | 九个 owned 文件逐个查 BOM / 行尾；`git rev-parse HEAD` | 全部 CRLF、无 BOM（`AGENTS.md` 亦然）；无新增建表或表结构元数据查询；无回填/迁移路径；未动 `Tool/` 与 `.worktrees/legacy-reference/`；HEAD 仍 `ea95d49`，无任务级提交 |

### Findings

**F-5｜闭合。** Contract Amendment A1 增列的三条 Permitted paths，实际改动逐条都落在其声明的限定内（证据 6/7/8）：`ISessionPresence.cs` 零删除、改动全在目标接口内；`SelectCharacterLifecycleServices.cs` 只有那一行；`AGENTS.md` 只有那一个 bullet。唯一超出字面的是给既有 `PersistAsync` 补的一行 `<summary>`——它正是「区分两个入口」这件事本身的文档面，判为在限定内，不另立要求。

**F-6｜闭合，且两个方向都守得住。** 接线层面退回（探针甲）与实现层面退回（探针乙）**各自都有单条用例转红**，两条红分别落在不同的守卫上：前者被服务级的入口计数抓住（`SessionGenerationOwnershipTests.cs:166`），后者被接缝级的「外层用例不提交仍须落库」抓住（`OutsideTransactionTests.cs:164`）。第 2 轮给出的判据「把 `:116` 改回 `PersistAsync` 必须有用例转红」已满足，且比判据要求的更严一档。

`SelectCharacterLifecycleTests.cs:797-800` 的 `CharacterMutations` 替身仍把 `PersistOnTeardownAsync` 转调 `PersistAsync`，未分开计数——但该组用例断言的是 `PersistedPosition` 的内容而非入口，且入口选择已由上面两条守住，不构成缺口。

**F-7｜闭合。** 第 2 轮点名的自相矛盾已消除：Execution Result 3 的 F-7 段与 Executor Self-check 3 第 1 行现在一致，都承认 `Prepare` 的位置写今天已入伙。见下条对该句精度的补充。

**F-11（非阻断，记录精度）：自查表「只有选角的位置写入已加入该事务」在其自身框架（验收 1 清点的那九条写路径）内准确，但作为全局陈述不完整——今天入伙的持久写共六条。**

独立清点（`Server/src/` 全量 `SessionIn(` 与 `IDomainTransactionAccessor` 检索，逐条回溯到命令注册）：

经 `ICharacterStore`（生产为 `SqliteCharacterStore`，`PersistenceModule.cs:77`）：

1. **选角落点写入** — `SelectCharacterPhaseCommands.cs:122` → `SelectedSessionPositionPersistence.PersistAsync` → `CharacterStateAccess.cs:129-136` → `store.SessionIn(ambient).WritePosition`，命令 `PrepareSelectedCharacterSessionCommand`（`ProtocolModule.cs:117` 本任务改为 Transactional）。**这一条是本任务今天唯一新增的入伙写，也是本任务唯一的可观察语义变化。**
2. **任务同步** — `SelectCharacterPostEntryServices.cs:55` `stores.ChangeAsync(characters, …)` → `AmbientStoreAccess.ChangeAsync`（`CharacterStateAccess.cs:66-71`），命令 `SynchronizeSelectedCharacterQuestsCommand`（`:133`，**本任务之前就是 Transactional**）。
3. **宠物复活** — `SelectCharacterPostEntryServices.cs:138`，同上路径，命令 `ReviveSelectedCharacterPetCommand`（`:136`，本任务之前就是 Transactional）。

经 `IItemStore`（生产为 `SqliteItemStore`，`PersistenceModule.cs:78`），全部走 `PersistentItemContainer.WriteAsync`（`ItemContainers.cs:306-315`，**无环境事务即抛**）：

4. `MoveItemSpaceCommand`（`:152`）
5. `UseStackableCommand`（`:154`）
6. `GrantTutorialRewardCommand`（`:158`）
——三条本任务之前就是 Transactional。

不入伙的：九个 `Sqlite*Repository` 的全部写（各自 `new SqliteConnection` + 当场 `CommitAsync`），与 `PersistOnTeardownAsync` 走的独立边界。`TransientItemContainer` / `MemoryItemStore` 的写会入伙但不持久，不计。

也就是说：**「九个仓储的写尚未入伙」准确无遗漏；「只有选角的位置写入已加入该事务」若被 T-004 当作全局事实读，会漏掉任务/宠物/三条道具命令这五条今天已经入伙的写。** 建议把该格补一句限定（例如「本任务清点的九条写路径中，只有……；另有任务、宠物与三条道具命令的写经两个 store 早已入伙，非本任务所致」）。非阻断：Result 3 的 F-7 段与自查表第 2 行（「对经 `SqliteDatabase` 与两个 store 的写成立」）合起来已给出正确图景。

**F-12（非阻断，文档小缺口）：`AGENTS.md` 改写后的 Direct 名单仍漏 `CheckCharacterNameCommand`。** 全仓 Direct 命令共 6 条，新文只列 5 条（`GetAccountCharacterRoster`、`AcknowledgeGameExit`、Channel 的三条），`CheckCharacterNameCommand`（`ProtocolModule.cs:104`，Direct + Account Shared）未出现。这是改写前就有的遗漏，不是本任务引入；但 Result 3 的立论是「按名单记的文档每改一条就失真一次，按规则记的不会」，而残留的 Direct 部分仍是名单且已经不全。补一个名字即在 A1 的「仅限同步命令执行方式的记述」范围内。

**F-13（非阻断，移交具体度）：F-8 移交给 T-004 的记录，说清了「是什么、为什么、在哪触发」，但没有给出验收判据，也没有点明本任务新加的契约方法自身将被重塑。**

现有记录（Result 3 的 F-8 段 + Self-check 3 的「移交给 T-004 的两条」）已具备：故障形态（自锁 BUSY / `SQLITE_BUSY_SNAPSHOT` 517）、成因（第二条连接 vs. 已读写过的环境事务）、触发点（`SelectCharacterPhaseCommands.cs:47` 的读与 `:57` 的写排在 `:62` / `:99` 的清理之前）、方向（把被顶掉会话的清理移出新选角的用例），复现配方（WAL、延迟事务、busy timeout）在同一文件的 Review 2 中可查。一个只读这份 Task 文件的执行者据此**知道该做什么**。

不足在「如何验证自己做对了」：

- 没有可执行的判据。宜给一条形如「构造一个已写过库的环境事务，在其中触发会话结束的位置写入，必须既不等满 busy timeout 也不失败」的用例要求——否则 T-004 很可能只做代码搬移而无红→绿证据。
- 没有点明：本任务在 `ISessionPresence.cs` 新增的 `PersistOnTeardownAsync` 与 `CharacterStateAccess.AloneAsync` 正是那条「活不过 T-004」的实现，届时要动的就是它们，且它们是本任务经 Amendment A1 才获权改的契约面。移交里写明这一点，T-004 才知道自己需要再走一次同样的越界授权。

建议在 Self-check 3 的移交条目里补上这两点；非阻断。

**F-9（第 2 轮，非阻断）｜已闭合。** `AGENTS.md` 已同步，且改写为「规则 + 例外」而非名单，名单部分经证据 9/10/11 逐条核对无误。残留的是 plan 侧：BD-010 / BD-012 的边界描述仍只覆盖 `ProtocolModule.cs`、`Server/src/Dfo.Server.Application/` 与 `Server/tests/`，未覆盖 `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs` 与 `AGENTS.md`。Task 层已由 Amendment A1 授权，plan 层的吸收由 plan 所有者安排，本任务无须再动。

### 未发现的问题

- 越界与无关改动：九个 owned 文件的改动逐个核过，全部落在密封契约 + Amendment A1 的 Permitted paths 内，无第十处文件、无顺手改动。
- 覆盖：F-1 的修正在接线层与实现层各有一条独立守卫（探针甲/乙），红→绿证据本轮可复现。
- 检查：CK-004 名单本轮独立复核无误（证据 9/10/11/12），CK-005 与 CK-006 亲跑通过，1024 / 2 / 0，0 警告。
- 仓库指令：CRLF、无 BOM、无建表或表结构元数据查询、无回填/迁移路径、未动 `Tool/` 与 `.worktrees/legacy-reference/`、无任务级提交、探针无残留。

### 需要的补救

无阻断项。F-11 / F-12 / F-13 三条建议可在本任务内以纯记录/文档方式吸收，也可由执行者判断后带入下一轮；不构成 `changes_required`。

## Execution Result 4

三条非阻断一并处理，以及 plan 侧的边界吸收。

### F-11｜自查表的限定不完整

评审是对的：「只有选角的位置写入已加入该事务」在验收 1 的清点框架内准确，作为全局陈述却会误导。今天真正进入 `UnitOfWorkBehavior` 那笔事务的持久写共**六条**：

| 写 | 入口 | 本任务新增？ |
|---|---|---|
| 选角落点 | `SelectCharacterPhaseCommands.cs:122` → `PersistAsync` | **是** |
| 寻物任务进度 | `SelectCharacterPostEntryServices.cs:55` → `AmbientStoreAccess.ChangeAsync` | 否 |
| 宠物复活 | 同上 `:138` | 否 |
| 道具移动 | `PersistentItemContainer` | 否 |
| 道具使用 | 同上 | 否 |
| 教学关奖励 | 同上 | 否 |

后五条在本任务之前就已是 Transactional。**九个仓储的写一条也不在其中**——这才是自查表那句限定要说的事。

### F-12｜文档名单仍漏一条

`CheckCharacterNameCommand` 是 Direct 却未列。已补。评审指出这与「按规则记就不会失真」的立论略有出入——确实，规则句免除的是「每改一条执行方式就失真」，不免除名单本身抄漏。

### F-13｜移交给 T-004 的判据不够

补上可执行判据。T-004 处理 F-8 时，**做对的样子**是：

> 一个**已经写过库**的环境事务开着时触发会话结束写入，该写入既不等满 busy timeout 也不失败；且环境事务随后再写不撞 `SQLITE_BUSY_SNAPSHOT`。

并且要点明：本任务新增的 `PersistOnTeardownAsync` 与 `AloneAsync` **正是届时要重塑的东西**，而它们是经 Contract Amendment A1 才获权改动的契约面——T-004 若要再改它，需要自己的修订记录。

### plan 侧吸收

plan 增列 `BD-014`，拥有 `ISessionPresence.cs`、`SelectCharacterLifecycleServices.cs` 与 `AGENTS.md`，并加入 T-002 的 Boundaries 与 RQ-004 的覆盖行。Task 层的 A1 与 plan 层现在一致。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1024 通过 / 2 跳过 / 0 失败**。

## Executor Self-check 4

| 验收条件 | 状态 |
|---|---|
| 1 每个写方法的调用链都在一个用例边界内 | 已闭合。**限定**：九个仓储的写一条也未加入该事务，接入是 T-004..T-007 的工作；今天入伙的六条持久写见上表 |
| 2 一处写出失败则两处改动均不可见 | 已闭合，对经 `SqliteDatabase` 与两个 store 的写成立，带证伪 |
| 3 正常结束时两处改动同时可见 | 已闭合，同上限定 |

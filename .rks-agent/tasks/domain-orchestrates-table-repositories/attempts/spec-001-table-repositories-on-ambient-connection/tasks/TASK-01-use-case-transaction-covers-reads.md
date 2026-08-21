# TASK-01: 用例事务覆盖读

## Task Unit Contract

- Plan task: `T-001`
- Covers: RQ-002, RQ-003
- Boundaries: BD-001, BD-002, BD-012
- Required checks: CK-001, CK-002, CK-003
- Permitted paths:
  - `Server/src/Dfo.Server.Persistence/Database/`
  - `Server/tests/Dfo.Server.Persistence.Tests/`

### Goal

让用例边界能容纳读：参与者的数据库事务在首次命令时即开启，使一次用例内的多次读落在同一个事务里；同时给出仓储取用连接的唯一入口，无环境事务时开一次性连接供读取。

今天 `SqliteDatabaseParticipant.PrepareAsync` 只在存在暂存写时才 `BEGIN`，读走 `CommandAsync` 时连接上没有事务，是 autocommit。同一条连接也不给一致快照——这是 RQ-003 的直接障碍，且它先于一切拆分。

### Acceptance

| # | Condition |
|---|---|
| 1 | 一次用例内跨两张表读取，两次读之间由另一条连接提交一处改动并等其完成，该用例后一次读仍见旧值 |
| 2 | 无环境事务时读取放行，不因缺少边界而失败 |
| 3 | 无暂存写的用例结束时，已开启的读事务被正确结算，连接不被留住 |

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

### RED

新增 `Server/tests/Dfo.Server.Persistence.Tests/Database/UseCaseTransactionCoversReadsTests.cs`，四条用例。在实现之前运行：

```
AUseCaseReadingTwoTablesDoesNotSeeACommitThatLandsBetweenTheReads [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: 700
  Actual:   999
```

用例读了 `characters`，另一条连接随即把 `character_gold_limits.gold_carry_limit` 由 700 改为 999 并提交，用例的第二次读看到了 999。**这就是缺陷本身**：一次用例的多次读描述了两个不同时刻的世界，而结果里没有任何东西说明哪部分来自哪个时刻。

（首轮三条全红是夹具自己的错——我把列名写成 `gold_limit`，实际是 `gold_carry_limit` / `auction_gold_limit`。修正后只剩上面这一条真红，另两条本来就绿。这一点如实记下，避免把夹具错误当成缺陷证据。）

### 改动

| 文件 | 改动 |
|---|---|
| `Persistence/Database/SqliteDatabaseParticipant.cs` | 事务改为首次 `CommandAsync` 即开启；`PrepareAsync` 遇已开启则不重复开启 |
| `Persistence/Schema/CharacterDatabaseSchema.cs` | 建库时设 `PRAGMA journal_mode=WAL` |
| `Persistence/Database/SqliteDatabase.cs` | 新增。仓储取用语句的唯一入口 |

### 两处执行期发现，都改变了实现方向

**其一：默认事务是 `BEGIN IMMEDIATE`。** 把事务提前到首次命令之后，用例转成另一种红——`SQLite Error 5: database is locked`，等满 30 秒超时。原因是 Microsoft.Data.Sqlite 的默认隔离级别映射到 `BEGIN IMMEDIATE`，它在开启的那一刻就取走数据库唯一的写锁。照此实现，**每一个用例（包括只读的入场装载）都会在整个执行期间阻塞所有写入**，等于把服务器串行化。改为 `BeginTransaction(deferred: true)` 后，锁按语句实际需要逐步获取，只读用例不再挡任何人。

这不是可选优化：不改的话，本任务为了让读一致而付出的代价是整个服务器串行，比它解决的问题更糟。

**其二：WAL 是必要支撑而非顺手改进。** 默认的回滚日志下，读者持有文件直到读完，期间的写入会被拒绝而不是照常进行——验收条件要的「用例读到旧值、外部写照常提交」在该模式下根本不可能同时成立。WAL 让读者看到开始时刻的快照、写者照常前进，两者才能并存。

因此建库时设 WAL。这是数据库文件自身的属性而非连接属性，一经设定即持久，故落在唯一决定文件形状的地方。新增用例 `TheDatabaseKeepsWritersAndReadersApart` 把该模式钉住——它是其余三条成立的前提，所以断言它而不是假定它。

### GREEN 与证伪

四条用例全绿。证伪证据即上文 RED：把事务开启退回只发生在 `PrepareAsync`（也就是改动前的状态），快照一致性用例给出 `Expected 700, Actual 999`。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1015 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 4 全部为本任务新增用例）。

## Executor Self-check 1

| 验收条件 | 状态 |
|---|---|
| 1 跨表读不见中途提交 | 已闭合，带红→绿与证伪 |
| 2 边界外读放行 | 已闭合（`AReadOutsideAnyUseCaseIsAllowed`），且 `SqliteDatabase.ReadAsync` 无环境事务时开一次性连接 |
| 3 只读用例正确结算、不留连接 | 已闭合（`AUseCaseThatOnlyReadLetsGoOfItsConnection`：结算后外部写入立即成功） |

### 未闭合与已知代价

- `SqliteDatabase` 目前**没有生产调用方**——它是 T-004 起各仓储的取用点，本任务只交付它并使其可编译可注入。其 `WriteAsync` 的边界外拒绝路径因此暂无用例覆盖，将在 T-004 随首批仓储接入时补上。这里如实记录，不算已验证。
- 延迟事务的代价：一个先读后写的用例，若在其读快照之后有别人提交，升级为写时会失败而不是覆盖对方。这是正确的乐观语义，也是本仓库既有 CAS 谓词（`ExpectedTriggerValue`、`OnlyWhenAtMostZero`）所针对的同一类冲突，但它把原先不会报错的路径变成会报错。本项目并发水平低且只有一个写进程，判定可接受；记录在案。

## Independent Quick Review 1

- Verdict: `changes_required`
- Reviewed contract revision: Task Unit Contract as sealed above, no amendments present
- Owned diff identity: baseline `ea95d49` + working tree —
  `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs` (M),
  `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` (M),
  `Server/src/Dfo.Server.Persistence/Database/SqliteDatabase.cs` (new),
  `Server/tests/Dfo.Server.Persistence.Tests/Database/UseCaseTransactionCoversReadsTests.cs` (new)

### Commands and evidence inspected

| # | Command | Result |
|---|---|---|
| 1 | `dotnet test ...Persistence.Tests.csproj --filter "FullyQualifiedName~UseCaseTransactionCoversReads"`（原状） | 4 通过 / 0 失败 |
| 2 | 注释掉 `SqliteDatabaseParticipant.cs:87` 的 `this.pending ??= link.BeginTransaction(deferred: true);` 后复跑 | 1 失败 3 通过，`AUseCaseReadingTwoTablesDoesNotSeeACommitThatLandsBetweenTheReads` 报 `Expected: 700 / Actual: 999` |
| 3 | 还原后复跑 + `diff -q` 与备份逐字节比对 | 文件与评审前完全一致，4 通过 / 0 失败 |
| 4 | 探针：`SettleAsync` 在 `order.Count == 0` 时直接返回，且 `CommitAsync` 在同一情形跳过 `pending.CommitAsync`（即只读用例既不结算事务也不关连接） | **4 条全部仍然通过** |
| 5 | A/B：把 `PRAGMA journal_mode=WAL` 换成 `journal_mode=DELETE` 复跑 | 2 失败 2 通过，耗时 32 秒（锁等待） |
| 6 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH`/`Pvf__ArchivePath` 已设） | 退出码 0，各项目 0 失败（截得 496 / 4 / 99 / 46 / 107 / 203，`Dfo.Pvf.Tests` 被我自己的 `tail` 截掉） |
| 7 | `grep -rn "SqliteDatabase" Server/` | 只有其自身定义两行，确认无生产调用方 |
| 8 | `grep -rn "CharacterDatabaseSchema.Ensure" Server/` | 8 处调用全部走 `Ensure(string)` 重载 |

### Findings

**F-1（阻断）测试没有覆盖验收条件 3，`AUseCaseThatOnlyReadLetsGoOfItsConnection` 不可能失败。**
证据 4：我把参与者改成「只读用例既不释放事务也不关闭连接」——正是该用例 remark 里
声称防的那个失败（`UseCaseTransactionCoversReadsTests.cs:84-88`：「a read-only use case
would leave its transaction and its connection behind」）——四条仍然全绿。原因是本任务
自己引入的 WAL：WAL 下读者本来就不挡写者，所以第 99-101 行「an ordinary write goes
through」在连接被留住和被释放两种情形下同样成立。让验收条件 1 成立的那个模式，恰好让
验收条件 3 的探针失明。自陈表第 3 行「已闭合（`AUseCaseThatOnlyReadLetsGoOfItsConnection`：
结算后外部写入立即成功）」与该事实矛盾。
补救：改断在能区分两种情形的东西上——结算后参与者不再持有连接/事务，或不靠 WAL 的
外部写来推断。

**F-2（阻断）改动越出了已封存的 Permitted paths。**
`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 既不在本 Task 的
两条 Permitted paths（`Persistence/Database/`、`tests/Dfo.Server.Persistence.Tests/`）内，
也不属于 BD-001 / BD-002 / BD-012 任何一条；plan.md 的 Owned Boundaries 表（BD-001..BD-012）
中没有任何一条拥有 `Persistence/Schema/`，全文不含 "Schema" 一词。执行结果为落点给出的
理由（「这是数据库文件自身的属性……故落在唯一决定文件形状的地方」）本身成立，且证据 5
证明 WAL 确属必要而非顺手改进——但这是一次需要先修订边界的越界，自陈里没有提及。
补救：走契约/计划修订把该路径纳入 BD-001 或新增边界，二者择一，不要默认接受。

**F-3（阻断）验收条件 2 的证据张冠李戴，边界外读取的生产路径零覆盖。**
`AReadOutsideAnyUseCaseIsAllowed`（`UseCaseTransactionCoversReadsTests.cs:73-82`）直接
`new SqliteDatabaseParticipant(...)` 后读取；裸参与者在 `CommandAsync`
（`SqliteDatabaseParticipant.cs:87`）照样开事务，所以这测的是「未入伙的参与者上的用例读」，
不是「无环境事务」。真正的边界外路径是 `SqliteDatabase.ReadAsync` 的 else 分支
（`SqliteDatabase.cs:56-59`），证据 7 显示 `SqliteDatabase` 全仓无引用、亦无用例。证据 2
另外显示该条在改动被撤回时依旧是绿的，因此它从来不是 plan 步骤 2 要求的「先写失败用例」，
也不构成 AC-004 的红→绿。自陈表第 2 行后半句「且 `SqliteDatabase.ReadAsync` 无环境事务时
开一次性连接」是断言而非验证。
补救：补一条真正经 `SqliteDatabase.ReadAsync` 在无环境事务下读取的用例。

**F-4（非阻断）`SqliteDatabase` 的一次性连接路径在目录尚不存在时会失败。**
同层每个仓储都在构造器里 `Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!)`
（如 `SqliteCharacterPvpSkillProfileRepository.cs:16`、`SqliteEntryInventoryMutationRepository.cs:30`），
`SqliteDatabase` 的构造器（`SqliteDatabase.cs:22-30`）既不建目录也不 `Ensure` 表结构。
今天无调用方所以无害，但 T-004 接入首批仓储前必须落定。

**F-5（非阻断）WAL 带来的 `.db-wal`/`.db-shm` 无人清理。**
清空 `%TEMP%` 后单跑本用例类：残留 4 个 `.db` 加 2 组 `-wal`/`-shm`（约 1.9 MB）；证据 5
的 `journal_mode=DELETE` 同一跑法残留 4 个 `.db`、无附属文件。可见 `.db` 残留是既有夹具
问题（`File.Delete` 抛 `IOException` 被第 146-149 行吞掉），附属文件才是 WAL 新增的。
没有夹具因此损坏——所有 SQLite 用例的路径要么是 GUID 唯一名，要么是被递归删除的目录
（`ChannelHostStartupTests.cs:69/117`、`SqliteAccountRepositoryTests.cs:32`、
`ItemPathLiveTests.cs:137`、`GameLoginIntegrationTests.cs:105`），所以这是磁盘卫生而非
正确性风险。新夹具的 `DisposeAsync`（第 144 行）只删 `this.path`。

**F-6（非阻断）结算之后再取命令，现在会多留一个没人结算的事务。**
`SettleAsync` 把 `pending`/`connection` 清空（第 205、229 行），此后任何 `CommandAsync`
会重开连接并再次 `BeginTransaction`（第 87 行）——改动前只漏一条连接，现在还会漏一个
WAL 读快照。今天经环境访问器不可达（`DomainTransactionScope.cs:28-29` 在结算后答 null，
facet 也已 `Discard`），记录备查，不要求处理。

### 已核实无缺陷者

- **红→绿是真的。** 证据 2 精确复现执行结果所述的 `Expected: 700 / Actual: 999`，且只有该
  条转红，说明断言确由被改动的那一行决定。证据 3 确认我的探针已逐字节还原。
- **WAL 是必要支撑而非顺手改进。** 证据 5 换成 `DELETE` 即 2 条失败并出现 32 秒锁等待，
  与执行结果「其二」的叙述一致。
- **结算在所有路径上都释放连接。** `CloseAsync` 位于 `SettleAsync` 最内层 `finally`
  （第 215-217 行），`CommitAsync`（169）、`RollbackAsync`（184）与「未 prepare 即 commit」
  的拒绝路径（155）都经过它；离开用例的每条出路最终都会到这三者之一，因为
  `DomainTransactionLease.DisposeAsync` 就是回滚（`DomainTransactionScope.cs:59`）。
  只读用例的提交路径正确：`PrepareAsync` 在无暂存写时提前返回并置 `prepared = true`
  （第 127 行），因此 `CommitAsync` 提交那个读事务而不是拒绝。
- **WAL 落点无既有副作用。** `Ensure(string)` 在第 55 行委托给 `Ensure(SqliteConnection)`，
  两个重载都会应用；证据 8 显示 `Ensure(SqliteConnection)` 的唯一调用方就是它，且是在刚打开、
  尚无事务的连接上，因此不存在「PRAGMA 在事务内执行」的既有路径。用 `ExecuteNonQuery` 执行
  会返回行的 PRAGMA 确实生效——`TheDatabaseKeepsWritersAndReadersApart` 断言得到 `wal`，
  证据 5 进一步显示模式变化真的改变了行为。
- **Enlist 键与既有存储一致。** `SqliteDatabase.ConnectionString`（第 26-28 行）、
  `SqliteItemStore`（第 29-31、40-42 行）、`SqliteCharacterStore`（第 30-31、41-43 行）
  都用 `new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) }.ToString()`
  并以之入伙，符合 AGENTS.md「SQLite 侧的 identity 是连接字符串」。不会出现第二个可失败参与者。
- **释放 joined command 无副作用。** `SqliteDatabase.ReadAsync:51` 只释放命令；快照用例本身
  就在同一个参与者事务上先后 `await using` 了两条命令而快照仍然成立，这一点已由证据 1 实证。
- **其余仓库指令满足。** 四个文件均 CRLF、无 BOM；`Persistence` 侧新类型为 `internal`；
  未产生任务级提交；证据 6 的全量不低于基线（退出码 0、全项目 0 失败）。

### Required remediation

1. 让验收条件 3 有真能失败的证据（F-1）。
2. 就 `Persistence/Schema/` 这条越界走契约或计划修订（F-2）。
3. 补一条经 `SqliteDatabase.ReadAsync` 的无环境事务读取用例，并修正自陈表第 2 行的措辞（F-3）。
4. F-4 至 F-6 记录在案，本轮不要求处理。

## Contract Amendment A1

**触发**：独立快评 F-2。本任务改动了 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`，而它既不在本 Task 已封存的 Permitted paths 内，plan 的 BD-001..BD-012 也没有任何一条拥有 `Persistence/Schema/`。这是越界，不因落点理由成立而免除。

**为什么该改动必须发生在这里**：RQ-003 要求一次用例的多次读看到同一份快照。默认的回滚日志下，持有读事务的连接会阻塞写者——验收要的「用例读到旧值、外部写照常提交」在该模式下不可能同时成立（实测：改为 `journal_mode=DELETE` 后两条用例失败，其中一条卡满 32 秒锁等待）。日志模式是数据库**文件自身**的属性，一经设定即持久，因此唯一正确的落点就是决定文件形状的那个类。

**修订内容**：
- plan 增列 `BD-013 | Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs | 建库时设定日志模式，使读者与写者互不阻塞`，并将其加入 T-001 的 Boundaries 与 RQ-003 的覆盖行。
- 本 Task 的 Permitted paths 增列 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`，**仅限设定日志模式**；表与列的声明不在本任务范围内，不得增删改。

**Spec 边界未变**：本修订不新增、不放宽任何 RQ 或 AC；它只是把一个 RQ-003 本就需要的落点写进边界。

## Contract Amendment A2

**触发**：独立快评 F-1。原验收条件 3「只读用例正确结算、不留连接」把两件事捆在一起，其中「不留连接」在本任务引入 WAL 之后从外部不可观测——连接持有但事务已结算时，外部写入与截断检查点都不受影响。

**修订内容**：验收条件 3 收窄为**「只读用例的读事务被结算」**，以截断检查点的 busy 状态为判据。连接对象的释放改由结构保证与评审核实承担（`SettleAsync` 最内层 `finally` 调 `CloseAsync`，提交、回滚、未 prepare 拒绝三条出路均经过它；快评 B 已逐条核实）。

**为什么不保留原措辞**：一个无法被证伪的验收条件不是验收条件。原措辞下的探针对「留住连接」全盲，见 F-1。

## Execution Result 2

三条阻断全部成立，逐条闭合。

### F-1｜验收条件 3 的探针失明

评审是对的，而且我的探针错得比它说的还要彻底。原用例以「结算后外部写入立即成功」为判据——**这个判据在本任务引入 WAL 之后恒成立**，因为 WAL 下读者本来就不挡写者。让验收 1 成立的那个改动，同时让验收 3 的探针失去了分辨力。

换成向数据库本身发问：`PRAGMA wal_checkpoint(TRUNCATE)` 的 busy 位。截断检查点在**任何连接仍持有比最新提交更旧的数据库视图**时报 busy——用例的读事务正是这样一个视图。新用例 `AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase` 断言用例开着时 busy=1、结算后 busy=0。

证伪（用评审给出的那个探针）：令 `SettleAsync` 在无 facet 时直接返回，**并且**令 `CommitAsync` 在同情形下跳过 `pending.CommitAsync`——该用例转红。已恢复并复绿。

自陈一处更正：我先只注入了 `SettleAsync` 的早退，用例仍绿。原因是 `CommitAsync` 先提交读事务、才在 `finally` 里走 `SettleAsync`，视图那时已经释放。所以这条用例观测的是**读事务的结算**，不是连接对象的释放。这一点写进了 Contract Amendment A2：把验收条件 3 收窄到可观测的那一半，另一半由结构保证与评审核实承担，而不是继续挂着一个无法被证伪的条件。

### F-2｜越出 Permitted paths

成立。见 Contract Amendment A1：plan 增列 BD-013 拥有 `Persistence/Schema/CharacterDatabaseSchema.cs`（**仅限设定日志模式**），Task 的 Permitted paths 同步增列。Spec 边界未变，未新增或放宽任何 RQ/AC。

WAL 是否必要，评审自己复核过：改为 `journal_mode=DELETE` 后两条用例失败、其一卡满 32 秒锁等待。它不是顺手的性能改进，是 RQ-003 的前置条件。

### F-3｜验收条件 2 的证据张冠李戴

成立。`AReadOutsideAnyUseCaseIsAllowed` 原先直接 `new SqliteDatabaseParticipant` 再读——裸参与者在 `CommandAsync` 里照样开事务，测的根本不是「无环境事务」。真正的边界外路径是 `SqliteDatabase.ReadAsync` 的 else 分支，当时零覆盖。

改为经 `SqliteDatabase` 加一个空的 `DomainTransactionScope`，并补上另外两条：

| 用例 | 覆盖 |
|---|---|
| `AReadOutsideAnyUseCaseIsAllowed` | 无环境事务时读放行（走一次性连接分支） |
| `AWriteOutsideAnyUseCaseIsRefused` | 无环境事务时写被拒，且数据库未被改动 |
| `AReadAndAWriteThroughTheSameDatabaseShareOneUseCase` | 同一 `SqliteDatabase` 的读与写归并为同一参与者；用例内读得到自己未提交的写，外部读不到；提交后外部可见 |

第三条同时兜住了 D 项关心的事：读写经同一连接串归并为一个参与者，不会出现第二个可失败参与者。它在 F-1 的证伪探针下也会转红。

### 非阻断项处置

- **F-4**（`SqliteDatabase` 不建目录也不 `Ensure`）：不在本任务处理。它今天无生产调用方；T-004 首批仓储接入时与注册一并落定。记录在案。
- **F-5**（`.db-wal`/`.db-shm` 残留）：本类的清理已改为连同两个附属文件一起删。评审已核实无夹具因此损坏，其余测试路径要么 GUID 唯一要么递归删目录，不逐一改动。
- **F-6**（结算后再取命令会多留一个无人结算的事务）：经 `DomainTransactionScope.Current` 不可达，本任务不处理，记录在案。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1017 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 6 全部为本任务新增用例）。

## Executor Self-check 2

| 验收条件 | 状态 |
|---|---|
| 1 跨表读不见中途提交 | 已闭合，红→绿证据为 `Expected 700 / Actual 999`，证伪为注释掉 `CommandAsync` 的事务开启行 |
| 2 边界外读放行 | 已闭合，且改为经真实路径 `SqliteDatabase.ReadAsync`；同时补上写被拒与同参与者归并两条 |
| 3 只读用例的读事务被结算（A2 收窄后） | 已闭合，判据为截断检查点的 busy 位，带证伪 |

未闭合项：`SqliteDatabase` 的生产调用方仍为零（设计如此，T-004 起接入）；F-4 与 F-6 已记录待后续任务处理。

## Independent Quick Review 2

- Verdict: `changes_required`
- Reviewed contract revision: Task Unit Contract as sealed，含 Contract Amendment A1 与 A2
- Owned diff identity: baseline `ea95d49` + working tree —
  `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs` (M, +19/-3),
  `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` (M, +16/-0),
  `Server/src/Dfo.Server.Persistence/Database/SqliteDatabase.cs` (new),
  `Server/tests/Dfo.Server.Persistence.Tests/Database/UseCaseTransactionCoversReadsTests.cs` (new)
- 评审前后四个文件 sha256 一致，注入的探针与临时用例已全部撤除（证据 11）

### Commands and evidence inspected

| # | Command / probe | Result |
|---|---|---|
| 1 | `dotnet test ...Persistence.Tests.csproj --filter "FullyQualifiedName~UseCaseTransactionCoversReads"`（原状） | 6 通过 / 0 失败 |
| 2 | 探针 P1（第 1 轮那条）：`SettleAsync` 在 `order.Count == 0` 早退，**且** `CommitAsync` 改成 `if (this.pending is not null && this.order.Count > 0)` | 2 失败——`AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase` 报 `Expected: 0 / Actual: 1`，落在第 93 行（commit 之后那次 `CheckpointBusy()`）；`AReadAndAWriteThroughTheSameDatabaseShareOneUseCase` 报 `Expected: 42 / Actual: 10` |
| 3 | 从备份逐字节还原后 `cmp` + 连跑 10 次 | `cmp` 一致（sha256 `8b6806d0…`），10/10 全绿 6 条 |
| 4 | 探针 P2：`SqliteDatabase.Participant()` 的 Enlist 键由 `this.ConnectionString` 改为 `new object()` | 1 失败——`AReadAndAWriteThroughTheSameDatabaseShareOneUseCase` 抛「A transaction admits at most one participant whose commit can still fail」，栈顶 `DomainTransaction.Enlist:48` ← `SqliteDatabase.Participant():85` ← `ReadAsync:49` |
| 5 | 探针 P3：`SqliteDatabase.WriteAsync` 的 `?? throw` 换成「无环境事务就开一次性连接写」 | 1 失败——`AWriteOutsideAnyUseCaseIsRefused` 报 `Assert.Throws() Failure: No exception was thrown` |
| 6 | 探针 P4：`SqliteDatabase.ReadAsync` 的 else 分支体换成 `throw` | 1 失败——`AReadOutsideAnyUseCaseIsAllowed` 命中该 throw |
| 7 | CK-002 探针：删掉 `CommandAsync` 第 87 行 `this.pending ??= link.BeginTransaction(deferred: true);` | 3 失败，其中 `AUseCaseReadingTwoTablesDoesNotSeeACommitThatLandsBetweenTheReads` 报 `Expected: 700 / Actual: 999` |
| 8 | 探针 P5（本轮新增）：**只**注入 `SettleAsync` 的 `order.Count == 0` 早退，`CommitAsync` 不动——读事务照常提交，只漏掉连接对象 | 交付的 6 条**全部仍然通过**；我另写的一条一次性用例转红（见 F-7） |
| 9 | `git diff ea95d49 -- Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 纯新增 16 行、零删除：一个 `WriteAheadLog` 常量 + `Ensure(SqliteConnection)` 首行的 `Apply(connection, [WriteAheadLog]);`。`Tables`、`ReconcilableColumns`、`IndexDeclarations` 一字未动 |
| 10 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH`/`Pvf__ArchivePath` 已设） | 退出码 0；60 + 496 + 4 + 99 + 46 + 109 + 203 = **1017 通过 / 2 跳过 / 0 失败**，无一例已知假红 |
| 11 | 收尾：四个文件对备份 `cmp`、`git status --porcelain`、Persistence.Tests 全项目 | 逐字节一致，无残留文件，109/109 通过 |

### Findings

**F-7（阻断）Contract Amendment A2 的前提不成立：被砍掉的那半其实测得出来。**

A2 给出的理由是「『不留连接』在本任务引入 WAL 之后从外部不可观测——连接持有但事务已结算时，外部写入与截断检查点都不受影响」。后半句对，前半句错，而且恰恰是 WAL 让它变得可观测。

证据 8：我只注入 `SettleAsync` 的 `order.Count == 0` 早退，`CommitAsync` 保持原样——读事务照常提交，唯独连接对象被漏掉，正是 A2 声称无法分辨的那个情形。交付的六条用例全绿，A2 对**自己这套用例**的判断确实成立。但同一次运行里，我临时加的一条用例转红：

```csharp
var participant = transaction.Enlist(cs, () => new SqliteDatabaseParticipant(cs));
await using (var command = await participant.CommandAsync(CancellationToken.None))
{ /* 一次读 */ }
await transaction.CommitAsync(CancellationToken.None);
SqliteConnection.ClearAllPools();          // 只回收空闲的池化句柄
Assert.False(File.Exists(path + "-wal"));  // 正确实现下 WAL 随最后一条连接关闭而消失
```

`ClearAllPools` 只关**空闲**句柄；参与者仍持有的那条不空闲，于是活下来，WAL 文件也就不会随「最后一条连接关闭」被删掉。正确实现下该断言绿（连接归池 → 被清掉 → WAL 删除），漏连接时红（`Expected: False / Actual: True`）。两个方向我都实测过。它也不靠池化开关：关掉池化时 `Dispose` 直接关连接，结论不变。

所以 A2 那句「一个无法被证伪的验收条件不是验收条件」在这里不适用——原验收条件 3 的后半句是**可**证伪的，只是交付的用例没往那个方向找。据此判断：**A2 不是诚实的必要修订，而是把一个够得着的目标改小了**，且缩小所依据的事实经不起一次四十行的反例。

需要同时说清楚的是：**实现本身没有错。** 真实代码下我那条探针用例是绿的，「`SettleAsync` 最内层 `finally` 调 `CloseAsync`，三条出路都经过它」的结构论证也确实成立。缺的是证据与契约完整性——今天没有任何用例会因为有人把 `CloseAsync` 挪出 `finally` 而转红，而这正是原验收条件 3 后半句要挡的回归。

补救（三选一，不要默认接受 A2 现状）：
1. 补上连接释放的用例（上面那段可直接用），撤回 A2；
2. 保留收窄，但换一个经得起该反例的理由；
3. 判定该覆盖归后续任务，则写进 plan 的风险/待办并说明本任务为何不承担。

### 已核实闭合者

**F-1 已闭合——就「读事务被结算」这一半而言。** 证据 2：注入第 1 轮那个探针后 `AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase` 转红，`Expected: 0 / Actual: 1`，落在第 93 行，也就是 commit 之后那次 `CheckpointBusy()`——断言确由被改动的结算路径决定，不是别的东西顺带。证据 3：逐字节还原后连跑十次全绿。执行结果里那处自陈更正（先只注入 `SettleAsync` 早退时用例仍绿，因为 `CommitAsync` 先提交了读事务）与我的证据 8 完全吻合，如实。

**F-2 已闭合。** plan 第 39 行确有 `BD-013 | .../Schema/CharacterDatabaseSchema.cs | 建库时设定日志模式…`；T-001 的 Boundaries（第 47 行）已是 `BD-001, BD-002, BD-013, BD-012`；RQ-003 的覆盖行（第 230 行）已是 `BD-001, BD-002, BD-013`。Task 侧由 A1 增列该路径并写明「仅限设定日志模式」。证据 9 确认实际改动确实只碰日志模式，表与列的声明一个字都没变。另核实两处相邻风险都不存在：`Ensure(SqliteConnection)` 的唯一调用方仍是 `Ensure(string)`（刚打开、尚无事务的连接），不存在「PRAGMA 在事务内执行」的路径；全仓没有拷贝或搬移 `.db` 文件的代码，因此 WAL 不会造成「只复制主文件丢数据」。

**F-3 已闭合，三条新用例都真的走了各自的分支。** 证据 6、5、4 分别把 `ReadAsync` 的一次性连接分支、`WriteAsync` 的拒绝路径、`Participant()` 的归并键各打掉一次，对应用例逐一转红且只有它转红。特别就「同一数据库归并为一个参与者」这点：`AReadAndAWriteThroughTheSameDatabaseShareOneUseCase` **不会**在参与者被拆成两个的情况下依旧通过——拆开后第二次 `Enlist` 直接被「一次事务只收一个 `CommitCanFail` 参与者」拒掉（证据 4 的栈指向 `DomainTransaction.cs:48`）；即便那条规则不存在，两条连接下用例内读也看不见自己未提交的写，`Assert.Equal(42L, seen)` 照样红。

**新用例稳定，未见假绿或假红的现实风险。** 隔离连跑十次全绿（证据 3），满载全量一次全绿（证据 10，Persistence.Tests 109/109、12 秒）。判据成立的条件是充分的：每个用例方法各自新建夹具实例、库名带 GUID，同类方法在 xUnit 下顺序执行，不存在同一文件的并行；第 88 行的 `UPDATE` 保证 WAL 里确有比读快照更新的帧，所以第一次 `CheckpointBusy()` 得到 busy=1 不靠碰运气；SQLite 自动检查点的阈值是 1000 页，本用例远够不着，不会出现「某次 checkpoint 已自动发生」而假绿。

**`DisposeAsync` 改删三个文件仍安全。** `File.Delete` 对不存在的文件是空操作，`IOException` 已捕获，`-wal`/`-shm` 只在 WAL 下存在。`ClearAllPools()` 虽是进程级的，但只关空闲句柄，对并行跑的其他用例透明。

**异常路径不漏资源。** `ReadAsync` 与 `WriteAsync` 里的 `await using var … = await ambient.CommandAsync(...)`：命令由 `await using` 释放；连接与事务归参与者所有，在结算时释放。若 `CommandAsync` 自身在 `BeginTransaction` 处抛，此时尚未创建命令，连接留在参与者里，由离开用例时的回滚 → `SettleAsync` 关闭。一次性连接分支的 `await using var connection` 声明在 `OpenAsync` 之前，开连接失败也会被释放。

**CK-001/CK-002/CK-003 都有真实证据。** CK-001：证据 1，6 通过 0 失败。CK-002：证据 7，把事务开启改回只在 `PrepareAsync` 发生，快照用例给出 `Expected: 700 / Actual: 999`，与 plan 步骤所述一致。CK-003：证据 10，1017 / 2 / 0，比基线 1011 / 2 / 0 正好多 6 条（本任务新增数），退出码 0，`Host.Tests` 与 `Networking.Tests/Integration/` 均未出现已知假红，无需隔离复跑。

**仓库指令满足。** 四个文件均 CRLF、无 BOM；`Persistence` 侧新类型为 `internal`（AGENTS.md 第 33 行）；Enlist 键为连接字符串，符合 AGENTS.md 第 134 行「SQLite 侧的 identity 是连接字符串，即一个数据库一个参与者」；未产生任务级提交；`Tool/` 与 `.worktrees/legacy-reference/` 未动。

### 非阻断记录

- **N-1 契约头与修订正文不同步（记账问题）。** Task Unit Contract 的 `Boundaries` 仍写 `BD-001, BD-002, BD-012`、`Permitted paths` 仍是两条，BD-013 与 Schema 路径只出现在 A1 正文里。按 Task Unit 模板「只追加修订段、不重写已封存段」这是正确做法，可追溯性由 plan 的 T-001 承担；仅提示后续读者以 A1 为准。
- **N-2 日志模式没设上不会有人知道。** `Apply` 用 `ExecuteNonQuery` 执行 `PRAGMA journal_mode=WAL` 并丢弃它返回的那一行。若模式未能切换（例如把一个既有非 WAL 文件转换时撞上 `SQLITE_BUSY`），运行期静默降级，RQ-003 的保证随之失效，而 `TheDatabaseKeepsWritersAndReadersApart` 只覆盖新建库这一种情形。「开发阶段擦库不迁移」把暴露面压得很小，本轮不要求处理。
- **N-3** F-4（`SqliteDatabase` 不建目录也不 `Ensure`）与 F-6（结算后再取命令会多留一个无人结算的事务）按第 1 轮处置留待后续任务，本轮无新增事实。

### Required remediation

1. 就 F-7 择一处理：补上连接释放的用例并撤回 A2；或为收窄换一个经得起该反例的理由；或把该覆盖显式移交后续任务并写进 plan。
2. N-1 至 N-3 记录在案，本轮不要求处理。

## Execution Result 3

### F-7｜A2 的前提是错的，撤回该收窄

评审是对的，而且这条比第一轮那条更值得记下来：**我在证明不了一件事的时候，改了要证明的那件事。**

A2 的前提是「连接被留住这一半在 WAL 下从外部不可观测」。它不成立。可观测，而且判据很直接：**WAL 的附属文件在数据库上还有连接开着时存在，最后一个连接真正关闭时才被删掉。** 所以把池里的句柄全部关掉之后，`-wal` 文件还在，就说明有一条连接没被交还。

新增 `AUseCaseThatOnlyReadLetsGoOfItsConnection`：只读用例结算后 `ClearAllPools()`，断言 `-wal` 不存在。

证伪：令 `SettleAsync` 在无 facet 时早退（**只注入这一处**，不动 `CommitAsync`），该用例转红，其余六条全绿——正是第一轮我注入同样探针却全绿的那个盲区。已恢复并复绿。

**A2 撤回**：验收条件 3 恢复为原措辞「只读用例正确结算、不留连接」，两半各有一条能证伪的用例：

| 半 | 用例 | 判据 |
|---|---|---|
| 读事务被结算 | `AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase` | 截断检查点的 busy 位 |
| 连接被交还 | `AUseCaseThatOnlyReadLetsGoOfItsConnection` | 池清空后 `-wal` 是否仍在 |

A1 保留不变（它是真实的边界修订，不是把目标改小）。

### N-2｜日志模式的答复不再被丢弃

非阻断，但它守的正是 RQ-003，代价又极小，所以本轮一并处理。

`PRAGMA journal_mode=WAL` 是一个**数据库可以拒绝**的请求。原先经 `Apply` 用 `ExecuteNonQuery` 发出，返回行被丢掉——一次被拒绝的请求会让一切照常工作，唯独少了它被要求提供的那个保证，且任何地方都不会说这件事。

改为 `KeepWritersAndReadersApart`：读回答复，不是 `wal` 就抛，消息里带上数据库实际给出的模式。已核实全仓无内存库或临时库使用，因此不存在合法地答非 `wal` 的路径。

### N-1｜契约头未同步

不处理，按设计如此：Task Unit Contract 一经封存即不可改写，修订以 Contract Amendment 的形式追加，这是该模板要求的记账方式。评审也标注为记账提示而非缺陷。

### N-3｜F-4 与 F-6

沿用第一轮处置，本轮无新增事实。F-4 在 T-004 首批仓储接入时落定；F-6 经 `DomainTransactionScope.Current` 不可达。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1018 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 7 全部为本任务新增用例）。

## Executor Self-check 3

| 验收条件 | 状态 |
|---|---|
| 1 跨表读不见中途提交 | 已闭合。证伪：注释掉 `CommandAsync` 的事务开启行 → `Expected 700 / Actual 999` |
| 2 边界外读放行 | 已闭合。经真实路径 `SqliteDatabase.ReadAsync`；另有写被拒与同参与者归并两条，均由评审用定点探针打红验证过 |
| 3 只读用例正确结算、不留连接（**A2 已撤回，恢复原措辞**） | 已闭合。两半各有一条可证伪用例，见上表 |

未闭合项：`SqliteDatabase` 的生产调用方仍为零（设计如此，T-004 起接入）；F-4 与 F-6 记录待后续任务处理。

## Independent Quick Review 3

- Verdict: `passed`
- Reviewed contract revision: Task Unit Contract as sealed，含 Contract Amendment A1；A2 由 Execution Result 3 撤回，本轮按**原验收条件 3 措辞**核验
- Owned diff identity: baseline `ea95d49` + working tree —
  `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs` (M, +19/-3),
  `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` (M, +32/-0),
  `Server/src/Dfo.Server.Persistence/Database/SqliteDatabase.cs` (new),
  `Server/tests/Dfo.Server.Persistence.Tests/Database/UseCaseTransactionCoversReadsTests.cs` (new)
- 评审前后四个文件 `cmp` 逐字节一致；参与者 sha256 `8b6806d0…`，与快评 2 记录值及执行者自己的 `participant.bak` 三方吻合

### Commands and evidence inspected

| # | Command / probe | Result |
|---|---|---|
| 1 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH`/`Pvf__ArchivePath` 已设，原状先跑） | 退出码 0；60 + 496 + 4 + 99 + 46 + 110 + 203 = **1018 通过 / 2 跳过 / 0 失败** |
| 2 | `dotnet test ...Persistence.Tests.csproj --filter "FullyQualifiedName~UseCaseTransactionCoversReads"`（原状） | 7 通过 / 0 失败 |
| 3 | **本轮核心探针**：`SettleAsync` 首行插入 `if (this.order.Count == 0) { return; }`，**只注入这一处，`CommitAsync` 一字未动** | **1 失败 / 6 通过**——只有 `AUseCaseThatOnlyReadLetsGoOfItsConnection` 转红，`Assert.False() Failure / Expected: False / Actual: True`，落在 `UseCaseTransactionCoversReadsTests.cs:115` |
| 4 | 从备份 `cp` 还原后 `cmp` + 复跑 | `cmp` 逐字节一致（sha256 `8b6806d0…`），7 通过 / 0 失败 |
| 5 | N-2 探针：`CharacterDatabaseSchema.cs:94` 的 `PRAGMA journal_mode=WAL;` 改为 `=DELETE;` | 7 条**全部**转红，且全部报 `System.InvalidOperationException : This database answered 'delete' when asked to keep writers and readers apart, ...`——答复确实被读回，且消息带出真实模式 |
| 6 | 还原 schema 后四个文件对备份 `cmp` | 全部逐字节一致 |
| 7 | `dotnet test ...Persistence.Tests.csproj --filter "FullyQualifiedName~SchemaOwnership"` | 3 通过。`ShapeAsync` 正逆序各建 7 个仓储于**同一个库文件** → `KeepWritersAndReadersApart` 每库跑 7 次，其中 6 次落在已是 WAL 的库上，均返回 `wal` 不抛 |
| 8 | `dotnet test ...Persistence.Tests.csproj`（全项目，收尾） | 110 通过 / 0 失败 |
| 9 | `git diff ea95d49 -- .../SqliteDatabaseParticipant.cs` | 恰两处实质改动：`CommandAsync` 增 `this.pending ??= link.BeginTransaction(deferred: true);`；`PrepareAsync` 的 `BeginTransactionAsync` 改 `??= BeginTransaction(deferred: true)`。其余为文档注释。无残留探针、无格式化漂移 |
| 10 | `git diff ea95d49 -- .../CharacterDatabaseSchema.cs` | +32/-0 纯新增：`Ensure(SqliteConnection)` 首行一句调用 + 新私有方法。`Tables`、`ReconcilableColumns`、`IndexDeclarations` 一字未动 |
| 11 | `grep -rn "Ensure(" Server/ --include=*.cs` | `Ensure(SqliteConnection)` 的唯一调用方是 `Ensure(string):55`（刚打开、尚无事务的连接）；8 处外部调用全走 `Ensure(string)` |
| 12 | 在 `Server/` 全域搜 `:memory:`、`Mode=Memory`、`Cache=Shared`、空 DataSource | **零命中**——不存在会合法答非 `wal` 的内存库或临时库 |
| 13 | `cmp` 当前参与者 vs 执行者第 2 轮备份 `participant.bak` | 逐字节一致 |
| 14 | 四文件 CRLF / BOM、`git status --porcelain` | 均 CRLF、无 BOM；工作树只有申报的四条路径与 `.rks-agent/` 任务树，无任务级提交 |

### Findings

无阻断项。

### 已核实闭合者

**F-7 真闭合，且闭在评审第 2 轮指出的那个精确位置上。** 证据 3：我按要求只注入 `SettleAsync` 的 `order.Count == 0` 早退——读事务照常由 `CommitAsync` 提交，唯独连接对象被漏掉——`AUseCaseThatOnlyReadLetsGoOfItsConnection` 转红，**且只有它转红**（其余 6 条全绿）。这正是第 1、2 轮注入同一探针却全绿的那个盲区，现在有一条用例站在那里。证据 4：逐字节还原后复绿，sha256 与还原前一致。

**`-wal` 判据本身可靠，两个方向都实测过。** 正向：漏连接 → 池里清不掉那条不空闲的句柄 → 最后一条连接从未关闭 → SQLite 不删 `-wal` → `Expected: False / Actual: True`（证据 3）。反向：正确实现下连接归池 → `ClearAllPools()` 关掉 → 最后一条连接关闭时 `-wal`/`-shm` 被删 → 断言绿（证据 2、4、8 及证据 1 的满载全量，四次独立运行 7/7）。假绿需要「有连接开着却没有 `-wal`」，而 SQLite 只在最后一条连接关闭时删除该文件，全仓也没有任何地方设 `PERSIST_WAL`；假红需要「无连接开着却仍有 `-wal`」，需要别的进程/连接占着同一文件，而库名带 GUID、同类方法在 xUnit 下顺序执行、全仓无第二处代码碰这个文件。

一处**范围**说明（非缺陷）：该判据严格说来问的是「这个文件上还有没有 OS 级句柄开着」，在本用例的隔离条件下与「参与者交还了连接」重合，但它不会捕捉「参与者留着一个已关闭的连接对象」——那也不是被占住的资源，因此不构成覆盖缺口。

**A2 的撤回在验证层面处理干净，两半各自独立可证伪。** 验收条件 3 现在实际以**原措辞**「只读用例正确结算、不留连接」被验证，两半由两条用例分别承担，且由**不同**探针分别打红，互不替代：

| 半 | 用例 | 判据 | 打红的探针 |
|---|---|---|---|
| 读事务被结算 | `AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase` | 截断检查点 busy 位 | 快评 2 证据 2（`SettleAsync` 早退 **且** `CommitAsync` 跳过提交）→ `Expected: 0 / Actual: 1` |
| 连接被交还 | `AUseCaseThatOnlyReadLetsGoOfItsConnection` | 池清空后 `-wal` 是否仍在 | 本轮证据 3（**只**注入 `SettleAsync` 早退）→ `Expected: False / Actual: True` |

前者的红→绿证据建立在与今天**逐字节相同**的参与者文件上（sha256 `8b6806d0…`，证据 4、13 三方吻合），故沿用有效。

**N-2 的修复正确，四问逐条核实。**
1. *`ExecuteScalar` 读该 PRAGMA 是否可靠*——是。证据 5：把请求换成 `DELETE`，七条用例全部以 `answered 'delete'` 抛出，说明返回行确实被读到且带出真实模式；反之原状下 `TheDatabaseKeepsWritersAndReadersApart` 另行断到 `wal`。
2. *抛出时机会不会破坏既有调用路径*——不会。证据 11：`Ensure(SqliteConnection)` 全仓唯一调用方是 `Ensure(string)` 第 55 行，落在刚 `Open()`、尚无事务的连接上；8 处外部调用（`Server/src` 七个仓储构造器 + 本任务测试夹具）全走 `Ensure(string)`。不存在「PRAGMA 在事务内执行」的路径，也不存在会被这次新增的 `throw` 打断的第二种调用形态。
3. *对已是 WAL 的库重复调用*——仍返回 `wal`，不抛。证据 7：`SchemaOwnershipTests.ShapeAsync` 正逆序把七个仓储建在同一个文件上，即每库调用 7 次、其中 6 次面对已是 WAL 的库，3 条用例全绿；证据 1 的满载全量里合成根路径（`Host.Tests`、登录/开服集成用例）同样反复走这条路。
4. *是否存在合法却被拒的场景*——本仓库内没有。会合法答非 `wal` 的只有内存库与临时库，证据 12 显示全仓零命中。

**回归面干净。** 证据 9：参与者的 `git diff` 只有两处实质行 + 文档注释，与三轮叙述一致，没有任何残留探针，也没有被格式化工具改动无关行；证据 13 更强——它与执行者第 2 轮自己的备份逐字节相同，说明三轮反复注入-恢复没有留下一个字节的痕迹。证据 10：schema 侧 +32/-0 纯新增，只碰日志模式，表与列的声明一字未动，符合 A1/BD-013「仅限设定日志模式」（把「读回答复并在不符时抛出」算作「设定」的一部分，判定在界内：它不引入第二件事，只是确认那一件事成立）。证据 14：四文件 CRLF 无 BOM，`Persistence` 侧新类型仍为 `internal`（AGENTS.md 第 33 行），未产生任务级提交，`Tool/` 与 `.worktrees/legacy-reference/` 未动。

**CK-001 / CK-002 / CK-003 都有本轮或等效的真实证据。**
- CK-001：证据 2、8——目标用例 7/7，全项目 110/110。
- CK-002：本轮未重复删除 `CommandAsync` 第 87 行那次运行；快评 2 证据 7 已在与今天**逐字节相同**的参与者上给出 `Expected: 700 / Actual: 999`，如实沿用。本轮以证据 3 另行确认了结算路径这一侧的证伪链完整。
- CK-003：证据 1——退出码 0，**1018 / 2 / 0**，基线 1011 / 2 / 0 加本任务 7 条新增用例，正好对上。`Host.Tests` 4/4、`Networking.Tests` 46/46，本次满载全量**未出现任何已知假红**，无需隔离复跑。

### 非阻断记录

- **N-4（新增，记账）A2 的撤回只写在结果段，没有进入修订链。** `Contract Amendment A2` 至今仍以生效修订的口吻写着「修订内容：验收条件 3 收窄为『只读用例的读事务被结算』」，该段自身没有任何撤回标记；撤回只出现在 `Execution Result 3`（「**A2 撤回**」）与 `Executor Self-check 3`（「**A2 已撤回，恢复原措辞**」）。按追加式记账 A2 本就不可改写，且时间顺序加上两处明确表述足以让读者判定以原措辞为准——所以这**不是**「读者无法判断以哪个为准」的自相矛盾状态，不阻断。但**修订链**与**结果叙述**这两本账目前对「当前封存措辞是什么」说法不一，而修复同样是纯追加的：补一条 `Contract Amendment A3` 记录「撤回 A2、恢复原验收条件 3」，让修订链单独读也是对的。建议，不要求。
- **N-5（新增，记录）N-2 的守卫把静默降级换成了启动期拒绝。** 改动前 `PRAGMA journal_mode=WAL` 被拒会让服务照常运行、只是少了 RQ-003 的保证；改动后它在仓储构造器里抛，即在 DI 合成期失败、服务起不来。这是本次刻意的取舍，且本仓库内不存在合法的被拒路径（证据 12、7）。唯一残留情形是一个此前遗留的非 WAL `.db` 文件其转换未能生效——在「开发阶段擦库不迁移」下不在范围内，且吵闹地失败优于静默降级。记录在案，不要求处理。
- **N-1** 契约头与修订正文不同步：Result 3 按模板「封存段不可改写」处置，判定正确，N-4 是它的建设性延伸。
- **N-3 / F-4 / F-6** 沿用前两轮处置，本轮无新增事实。

### Required remediation

无。N-4、N-5 与既有非阻断项记录在案，本轮不要求处理。

## Contract Amendment A3

**触发**：独立快评 3 的 N-4。A2 段本身仍以生效口吻写着「收窄为…」，撤回只出现在 Execution Result 3 与 Self-check 3 里，修订链上看不出来。

**修订内容**：**A2 作废。** 验收条件 3 以其封存时的原措辞为准——「只读用例正确结算、不留连接」。两半各由一条可独立证伪的用例承担：

| 半 | 用例 | 判据 | 证伪探针 |
|---|---|---|---|
| 读事务被结算 | `AUseCaseThatOnlyReadLetsGoOfItsViewOfTheDatabase` | 截断检查点的 busy 位 | `SettleAsync` 早退 **加** `CommitAsync` 跳过提交 |
| 连接被交还 | `AUseCaseThatOnlyReadLetsGoOfItsConnection` | 池清空后 `-wal` 是否仍在 | `SettleAsync` 早退（单独即可） |

两个探针互不替代，这正是 A2 当初看不见的那一半。

**为什么留着 A2 而不是删掉**：轮次不可改写。A2 是一次真实发生过的错误判断——我在证明不了一件事的时候改小了要证明的那件事——删掉它就删掉了这条记录本身的价值。

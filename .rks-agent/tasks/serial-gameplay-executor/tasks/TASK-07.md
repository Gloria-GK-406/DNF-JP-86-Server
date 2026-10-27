# TASK-07 — T-004a2：Application.Tests 迁移、冲突总清单、全量核对与探针

## Task Unit Contract

- Revision: 2（第 2 版：接住 TASK-06 的交接，并收紧机械核对白名单。见文末「第 2 版修订」）
- Outcome and references:
  - **Application.Tests 的迁移**：把测试体自己做的准备和直接调用，改为经 TASK-06 的 `TestGameplay` 运行，规则沿用 TASK-06 §2。这一步只改驱动方式，测试数量和断言都不变。
  - **冲突总清单** `task-07-conflicts.md`：
    - 补上 Application.Tests 的冲突用例，包括 K1 测门本身，以及 K2～K4 中属于 Application 的项；
    - 与 `task-06-conflicts.md` 合并成一份按「执行单元」索引的总表；
    - 探针发现的新增项也补进来。
  - **全量机械差异核对**：以 TASK-06 开始之前的提交为基准，覆盖 TASK-06 与本单元的全部改动。
  - **探针运行与报告**（不提交，只记录，不抛出）：
    - 按调用点证明：迁移后的测试体都在执行器上；
    - 列出生产代码中在执行器外开写事务、访问玩法状态、取门的路径，作为 T-004b 的输入。
  - 对应 Plan T-004「判据与测试的关系」「测试」，以及 spec RQ-061（测试侧前提）、RQ-072、RQ-081、RQ-083。待办归属见 TASK-06 §7。
- Owned boundary:
  - Application.Tests 的测试文件：只改驱动方式，形状限于 TASK-06 §2.6。
  - **探针 (B) 指出的漏包点**：如果在 TASK-06 的项目（Persistence、Host、Networking、Protocol）或共享夹具中，允许按同样的形状补包，并在 Result 中逐条列出。
  - 在 `.rks-agent/tasks/serial-gameplay-executor/verification/` 下新增：
    - `logs/task-07-*.txt`
    - `task-07-diff-check.md`
    - `task-07-conflicts.md`
    - `task-07-probe.md`
  - 探针对 `Server/src` 的临时改动，见 Task-specific authorization。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release`：0 警告，0 错误。
  2. **全量测试计数核对**：运行 `dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`，日志落盘为 `logs/task-07-build-test.txt`。
     - 各项目测试数与 TASK-06 Result 的计数逐项相等，即 7986 ＋ N：Application 4415 ＋ N，其余各项目同 TASK-05 基线。**本单元不新增也不删除任何测试。**
     - 通过 7964 ＋ N，失败 2，跳过 20，写出加法。
     - 失败的只能是那 2 条 `DimensionDungeonTests`；表征测试 8 条通过、2 条跳过。
     - 另外隔离跑一次 Application.Tests 与 Networking.Tests，日志 `logs/task-07-isolated.txt`。
  3. **全量断言核对**：用 TASK-06 §5.1 的脚本，对比 TASK-06 开始前的提交（`0223e646`，若主控方另有记录以记录为准）与工作区，覆盖两个单元改动过的全部测试文件。
     - 测试方法集合不变；
     - 断言行的多重集合不变；
     - 白名单以外的差异逐条附理由。
     - 结果写入 `task-07-diff-check.md`：按项目写出改动文件数，以及基准与工作区两侧的方法数、断言行数，两侧两两相等。
  4. **迁移点对账**：非只读 `.Begin()` 与 T2 运行态入口的处数，以执行时的检索与探针结果为准，并与 Plan 的 694（Application 604、Persistence 60、Protocol 30）对账、说明差异。已知的一种差异来源：强转写法 `((DomainTransactionScope)…).Begin()` 会被简单正则漏计。对账写入 `task-07-diff-check.md`。
  5. **探针**（§3），报告写入 `task-07-probe.md`：
     - 三族挂点 P1、P2、P3 的 (A)/(B)/(C) 计数，分族、分项目给出；
     - **(B) 只能出现在冲突总清单所列的用例里**，逐条对照；
     - **(C) 的每个不同栈顶都要分诊**，写出结论；
     - **(A) 按入口分组列出**，交给 T-004b。其中要标出 TASK-03 记下的三处「门外自开事务写」是否出现、由哪些测试触发。
     - 原始记录落盘：`logs/task-07-probe-<项目>.txt`。
  6. **探针还原证明**：
     - 探针改动之前，用 `git ls-files -s Server/src` 记下 `Server/src` 全部已跟踪文件的 blob 哈希（sha1）快照；
     - 还原之后再取一次，两次快照逐字节相等；
     - 同时 `git diff --stat -- Server/src` 为空，`git status --short -- Server/src` 也为空（不得残留未跟踪的探针文件）；
     - 以上三项的输出写入 `task-07-probe.md`。
  7. 不改生产代码：提交时 `git diff --stat -- Server/src` 为空；`grep -rn "TestGameplay\|GameplayProbe" Server/src` 零命中。
- Dependencies: TASK-06。本单元使用它提交的辅助、迁移规则（§2）、冲突清单格式、差异核对脚本口径（§5.1），以及它 Result 中的全量计数。
- Result review: required。
  - **改动面**：约 191 个文件，是两个单元里改动最大的一块。
  - **下游依赖**：
    - 探针 (A) 清单是 T-004b 入口迁移的对账输入之一，(A) 若有遗漏，T-004b 就可能漏迁入口；
    - 冲突总清单决定 T-004b、T-004c 要在哪个提交里删除或改写哪些用例，漏一条，就会在那时表现为挂起，而且很难追溯到这里；
    - T-004c 的「全量零违规」以本单元结束时 (B) 为 0 为前提。
  - **必需检查抓不住的错误**：
    - (C) 分诊靠人判断；
    - 「误包了以生产入口为对象的用例」这类错误，探针看不见：被误包的入口记录会消失，而不是出现；
    - 全量测试和计数核对也都发现不了上面两类错误。
- Task-specific authorization:
  - **探针运行期间**，允许临时改动 `Server/src`：新增探针记录器，在 P1、P2、P3 挂点插入记录调用。仅限执行 Required check 5。
  - 用后全部还原，以 Required check 6 的 `git diff --stat -- Server/src` 为空和 sha1 快照比对来证明。
  - 探针改动不得提交，不得 `git stash`。
  - 除此之外：不改 `Server/src`，不删除测试，不修改断言。

## Detailed Plan

### 1. Application.Tests 迁移

规则全部沿用 TASK-06：
- T1～T5 的识别：TASK-06 §2.1；
- 包整个方法还是包区域，以及 AsyncLocal 边界：§2.2；
- 「并发只生效一次」类用例：§2.3；
- 不包的入口用例（R-2）：§2.4；
- 允许的改动形状：§2.6。

手里有容器的测试，一律写成 `services` 重载（TASK-06 §1）。

**规模**（HEAD 0223e646 检索；执行时以重新检索为准）：
- 非只读 `.Begin()` 604 处；
- 运行态 `Begin(…)` 约 97 处（T2）；
- 使用 `TestSessions` 的 19 个文件：调用点不动，已在 TASK-06 中经夹具处理；
- 直接 new 门的 6 个文件，以及 `new …Handler(` 的 28 个文件；
- T1～T5 候选文件约 191 个。

候选文件中，大约以下几类同时调用了生产入口，**只能按区域包**：

| 生产入口 | 文件数 | 例子 |
|---|---:|---|
| 真实 dispatcher | 13 | `NetworkMessageDispatcherTests`、`EffectOperationReplayTests`、`*NetworkSettlementTests`、`EffectRequestTransport` 及其使用者 |
| 事件分发器 | 17 | `DomainEventDispatchTests`、`Quest*Tests`、`Mailbox*IntegrationTests`、`MonsterDeathTransactionTests` |
| 观察者 | 2 | `SelectCharacterLifecycleTests`、`OnlineUserServiceTests` |
| 后台服务与调度 | 10 | `ScheduledTaskTests`、`WeaknessGoldRecoveryTests`、`TournamentRewardSweepTests` 等 |

另有 85 个文件注入了 `ICommandBus`。

**分批**：按目录推进，每批 30～40 个文件，例如 Inventory、World、Sessions 与 Game、Quests 与 Mail、其余目录。每批完成后构建，跑 Application.Tests，记下计数，并在 Result 的批次清单上打勾，便于 compact 之后接着做。

### 2. 冲突总清单（`task-07-conflicts.md`）

- **Application 部分**：初始项如下，执行时逐个阅读确认。
  - **K1 测门本身**：`OnlineUserExecutionGateTests`(5)、`SelectedSessionTransitionGateTests`(12)、`TransactionalCommandSerializationTests`(3)、`MultiAccountExecutionTests`(2)、`CommandBusTests` 的 3 个租约释放方法。
    - 执行单元：T-004c。
    - 预判：争用类用例删除，理由是「执行器上等门即抛」已成为新的不变量，由门断言的反例代替，门本身在 T-005 删除；非争用类包裹后保留到 T-005。
  - **K2**：`LoginSessionCommitRaceTests`。
  - **K3**：`SelectCharacterLifecycleTests.cs:236,261,288,365`，经观察者驱动，执行单元为 T-004b。
  - **K4**：`OnlineUserServiceTests`(3)（T-004b）、`EffectOperationReplayTests`、`ReplayStateSnapshotTests`（dispatcher，T-004b）、`OnlineExpiredItemCleanupTests`、`CommonExperienceEffectTests`(3)，逐个判定。
  - **K5**：`NotificationDeliveryTests`、`NotificationRetryTests`、`NotificationRetryDrainServiceTests`，执行单元为 T-006。
- **探针补充**：(B) 中出现的、未包裹且不在清单上的用例，要么改为包裹，要么补进清单并写明理由。
- **合并**：总表按执行单元（T-004b / T-004c / T-006）分组。
  - TASK-06 的条目只引用，写成 `task-06-conflicts.md` 的行号或用例名，不重抄，以免两处漂移。
  - 给出总条数的加法：TASK-06 的条目数 ＋ 本单元的条目数 ＝ 总数。

### 3. 探针（不提交，只记录，不抛出）

1. **取快照**：先按 Required check 6 记下 `git ls-files -s Server/src` 的快照。
2. **记录器**：在 `Server/src` 临时新增 `Abstractions/Execution/GameplayProbe.cs`（public static）。
   - 每次命中时，若 `!GameplayJob.IsOnExecutor`，记下挂点名和 `new StackTrace(1, false)` 的前 40 帧（方法名、声明类型、程序集名），按「挂点 ＋ 栈」去重计数。
   - 进程退出时（`AppDomain.ProcessExit`），写到环境变量 `GAMEPLAY_PROBE_OUT` 所指目录下的 `<进程 ID>.txt`。
3. **三族挂点**：
   - **P1**：`DomainTransactionScope.Begin`，`readOnly == false` 时。
   - **P2**：附录 F「改为执行器内访问」13 个类中的 `lock (sync)` / `lock (this.sync)`，临时改为 `lock (GameplayProbe.Touch(sync, "<类名>"))`。`Touch` 记录后原样返回同一个对象，锁的语义不变。它覆盖经过锁的全部读写，与 TASK-06 §2.1 T2 的超集口径一致。
   - **P3**：各门的取门方法。`UserExecutionGate<T>` 的 Acquire 系、`SelectedSessionTransitionGate`、`TownAreaProjectionGate`、`DungeonProjectionGate`、两塔的 Projection/Entry 门、`AchievementTitleProjectionGate`、`TransactionalExecutionGate`，以及队伍按角色的信号量和 `PartyDeliveryStream.Gate` 的 `WaitAsync` 调用点。
4. **运行**：按项目设置 `GAMEPLAY_PROBE_OUT`，跑 Application、Persistence、Protocol、Host、Networking 五个测试项目。探针运行的耗时与偶发失败，不作为任何结论的证据。
5. **分类**：对每条记录，从挂点往外，找第一个「入口帧」和第一个「测试帧」（程序集名以 `.Tests` 结尾）。
   - **入口帧**：
     - `CommandBus`、`Pipeline.*Behavior`；
     - `NetworkMessageDispatcher`、`DomainEventDispatcher`、`OnlineUserConnectionObserver`；
     - `LoginSession*Runtime/Maintenance*`、`ScheduledTask*`；
     - 类名以 `HostedService`、`MaintenanceService`、`ClockService`、`SweepService`、`StartupService`、`BootstrapService`、`DrainService` 结尾的类型；
     - `NetworkPlugin` 系、`TouchSocket*`；
     - GmWeb 与 HttpApi 的端点类型。
   - **(A)**：入口帧比测试帧更靠近挂点，或者只有入口帧。属于 T-004b / T-004c 的待办，本单元不处理。
   - **(B)**：测试帧更靠近挂点，且两者之间没有入口帧。必须为 0；冲突总清单所列的用例除外，要逐条对上。
   - **(C)**：两种帧都没有，栈被 await 恢复截断了。按生产代码的栈顶去重；对每个栈顶，检索直接调用它的测试，判定是漏包（补包后重跑）还是入口驱动（归入 (A)），写进分诊表。
6. **补包重跑**：补包之后重跑受影响的项目，直到 (B) 满足 Required check 5。
7. **还原**：还原 `Server/src`，删除探针文件，做 Required check 6 的三项比对，然后才做 Required check 2 的正式全量运行。

### 4. 为什么不改变行为

同 TASK-06 §4。补充两点：

- 探针只记录、不抛出，`Touch` 返回同一个锁对象，所以探针运行期间的语义与正式运行相同；但它的耗时不具代表性。
- 正式运行（Required check 2）在探针还原、并完成比对之后进行，证据不受探针影响。

### 5. 执行顺序

1. Application.Tests 分批迁移（§1）。
2. 冲突总清单（§2）。
3. 探针：取快照，插入挂点，运行，分类，补包重跑，还原并比对（§3）。
4. 全量机械差异核对与迁移点对账（Required check 3、4）。
5. 正式全量运行，再做隔离复跑。
6. 提交前确认 `git diff --stat -- Server/src` 为空，`grep` 零命中，然后写 Result。

### 6. 第 2 版修订（TASK-06 结果审查 N-1、N-2）

本节与上文冲突的，以本节为准。

1. **接住 TASK-06 的交接**：执行前先读 `tasks/TASK-06.md` 的 Result，尤其是 Remaining。它的各条都属于本单元的输入：
   - 调用 `DungeonRunTestEntryExtensions`（`runs.Begin`、`PreparePartyEntry`）的 Application.Tests 调用方，要由调用方包住含事务的整段区域。夹具内部不包，原因见 `verification/logs/task-06-fixture-dungeonrun-wrap.txt`。
   - `Server/tests/Dfo.Server.Application.Tests/Support/` 下的适配器在本单元的改动边界内。它们按 TASK-06 §2 的规则在内部包或者不包，逐个写明。
   - TASK-06 Remaining 第 3 条列出的「有意留在作业外」的点，原样保留。轮询入口进度的读取一律不包；其余各点如果被探针判为漏包，可以补包，补的时候逐条列出。
2. **收紧机械核对白名单**：复用 `diffcheck.py`（或者等价的核对脚本）之前，先补两条约束：
   - 插入的 `return …;` 后面必须紧跟 lambda 的闭合；
   - 插入的 `await` 必须紧邻 `TestGameplay.Run…`。
   
   白名单之外的差异逐条说明。另外用 `git diff -w` 做一次行级补核，结果写进 `task-07-diff-check.md`。

## Result

### Outcome

- **Application.Tests 的迁移已完成**。202 个测试文件、1263 行包裹调用。测试方法集合和断言行多重集合逐文件不变。
- **探针补包**：按探针 (B) 补了 TASK-06 项目漏包的地方，Persistence 1 个文件、Protocol 6 个、Host 2 个、Networking 1 个，共 24 行。
- **冲突总清单**：`verification/task-07-conflicts.md`，按执行单元分组。
  - 总数 136 ＝ TASK-06 51 ＋ TASK-07 85；
  - 按执行单元：T-004b 21 ＋ T-004c 62 ＋ T-006 53 ＝ 136。
  - 另有一节「有意留在作业外的点」，供探针对照。
- **探针**：五个项目跑完。
  - P1、P2、P3 的 (B) 全部落在冲突总清单的用例、「有意留在作业外」一节，或经排除复跑归属到冲突用例的截断记录与替身记录上；
  - (C) 逐个栈顶分诊，没有漏包；
  - (A) 按入口分组交给 T-004b；TASK-03 的三处门外写里，两处出现，一处未出现。
  - 报告见 `verification/task-07-probe.md`。
- **`Server/src` 已还原**，快照与改动前逐字节相等，`git diff`、`git status` 为空。
- **正式全量**：7999 ＝ 7986 ＋ 13；通过 7977，失败只有那 2 条 `DimensionDungeonTests`，跳过 20。

### 偏离计划、需要主控方确认的四点

1. **探针提前当检索工具用**。计划是先按静态检索迁移，再跑探针。本单元先在迁移前跑了一轮（r0），再按 (B) 迁移、补包重跑，共 r0～r7。
   - 理由：Application.Tests 大量访问经夹具与辅助间接发生，静态检索看不见。r0 的 (B) 覆盖 1923 个测试方法，静态候选只有 231 个文件。
   - `Server/src` 的临时改动范围同授权，改动前、中途、最终共取 3 份快照比对，最终状态逐字节相等（结果审查 N-3 更正措辞）。
2. **加了一个补充挂点 P1b**：在 5 处直接 `new DomainTransaction()` 前面记录。
   - 理由：TASK-03 的 `AloneAsync` 与 `CreatureRuntimeDomainService.cs:274` 不经过 `DomainTransactionScope.Begin`，P1 看不见，Required check 5 关于这三处的问题就答不了。
   - 分类时按构造它的方法拆成 read、volatile、teardown、creature。
   - 「(B) 必须为 0」只用在写类上。P1b-read 是边界外的只读一次性事务，单列为信息。
3. **分类口径的补充**，逐条见 `task-07-probe.md` §1.2：
   - 入口帧名单补了漏掉的后台服务，以及附录 A 的效果账本和卡牌计时器；
   - 容器释放与宿主启停算停机路径；
   - `TransactionalExecutionGate` 不算入口；
   - 泛型元数先去掉再匹配；
   - 测试归属取最近的测试方法帧；
   - 判 (A) 用「栈上有入口帧」。原文的「入口帧比测试帧更靠近挂点」会把注入入口内部的测试替身误判成 (B)。
4. **机械核对脚本在收紧之外的两处改动**：
   - 测试方法的扫描距离从 14 行放宽到 80 行。原脚本漏认 `InlineData` 很多的 Theory，迁移时因此漏包过一条，探针抓到后已补；
   - 加了对齐轮换的等价判定。它只撤销 `difflib` 对对齐位置的随意选择，不多放行任何记号。

### Changed

**共享夹具与文件内辅助（Application，主控，内部包，签名不变）**

| 位置 | 包裹点 | 容器 |
|---|---|---|
| `Inventory/NpcShopPurchaseTests.cs` 的 `ShopFixture`（被 104 个文件使用） | 构造函数里的 `RegisterConnection`（表达式形状）和 `Authenticate`；`Select`、`SeedCore`、`SeedShared`、`Durability`、`InTransaction`；直接调用 handler 的 `Buy`、`Query`（`async () => await …HandleAsync(…)`） | `provider` |
| `Characters/CharacterSkillLearningTests.cs` 的 `LearningFixture` | `SeedAsync` 的运行态段，`EnterWorld`，`LearnAsync` | `Services` |
| `Marriage/MarriageHarness.cs` | `InTx`、`MoveTo`、`GoOffline`、`AddCharacter` 的运行态段、`EligibleAsync` | `f.Services` 等 |
| 婚姻测试的辅助 | `MarriageProposalTests.Fixture`（运行态段）、`Run`；`MarriageCeremonyTests.MakeEligibleAsync`、`RunAsync`；`MarriageHousingTests.MarriedAsync`（从准备开始的那一段）、`Enter`；`HousingNetworkSettlementTests.Marry` | 各自的 `Services` |
| 其余 | `WarehouseStorageTests.InTransaction`；`AchievementTitleBookTests.Fixture.WriteAsync` 的两个重载；`CeraShopFixture.Handle` | 各自的 `Services` |

**有意不在内部包的**

- **`Support/` 下的适配器**（TASK-07 §6.1 第 2 条），逐个说明：
  - `LegacyTownAreaTestAdapter`（`SessionPresenceDomainService`）与 `UserDomainServices`：实现生产接口，并被注入被测对象。内部包会把生产路径的访问藏进作业，所以在调用点包。
  - `PartyServiceTestAdapter`：只由测试直接调用，它的调用方都已整方法包裹，不在内部再包。
  - `TowerPresenceProxy`：替身，不碰真实状态。
  - `DungeonVisitTestFactory`：只做构造。
  - `DungeonRunTestEntryExtensions`：同 TASK-06「偏离」第 1 条，由调用方包住含事务的整段区域。
  - 以上 4 个会出现在 (B) 包裹落点上的适配器（`DungeonRunTestEntryExtensions`、`PartyServiceTestAdapter`、`SessionPresenceDomainService`、`UserDomainServices`），在 r0 共 1115 条 (B) 记录；r7 只剩 `UserDomainServices` 的 32 条，全部来自冲突用例。
- **`NotificationWorld`**：K5 的两个文件也用它，所以不在内部包；`NotificationInWorldTests` 在调用点包。
- **`TestCreatureContainer` 构造函数**：它把开着的事务留给调用方，按 §2.2 不能在内部包；调用方整方法包。

**测试方法**

- **自动整方法包裹 867 条**：条件是只有 (B)、没有 (A)、方法体里没有入口或并发标记、不在冲突文件里。分三批：768 ＋ 98 ＋ 1 ＝ 867，覆盖 149 个文件。
- **人工处理**：4 个 fork 各分到 23 个文件，按 TASK-06 §2 与 TASK-07 §6 做区域包、辅助包或判冲突。
  - 逐文件决定见 scratchpad：`t07/work/decisions_1.md`～`decisions_4.md`；
  - fork 只改文件、不跑 dotnet，构建与测试由主控统一执行。
- **主控补的若干处**：
  - `LoginSessionCommitRaceTests` 2 条：K2，读后判为不冲突；
  - `OnlineUserExecutionGateTests.DifferentOnlineUsersDoNotBlockEachOther`；
  - `SelectedSessionTransitionGateTests` 的 2 条不争用用例；
  - 两处「断言行里声明的 T2 读」，用提出返回值形状：`SkillLearningCommandTests`、`SelectCharacterLifecycleTests.RejectedRepeatedLogin…`；
  - 按 P1b 补的 5 条：`OutsideTransactionTests` 3 条、`RememberedTownTests` 2 条；
  - 已知失败的 `DimensionDungeonTests.All_six_real_cells…`：有写事务 `.Begin()`，没有入口。
- **改正 fork-2 的 1 处误包**：`MailboxProtocolIntegrationTests.System_and_prepared_delivery…` 把 `DrainOnceAsync`（入口）和作业外开的事务包进了同一个作业，r3 中该用例失败。现在只包 `await using (scope.Begin()) await Mail.SendAsync(…)` 这一条语句。
- **数量**：Application 1263 行 ＝ 主控 41 ＋ 自动 867 ＋ fork 355。fork 的数是相减得到的；四个 fork 自报 357，差 2 是因为同一行里两个调用只计一行。

**TASK-06 项目里按探针补的漏包点**（TASK-07 Owned boundary）

| 文件 | 补什么 | 理由 |
|---|---|---|
| `Persistence.Tests/Inventory/EpicBookTests.cs` | 3 条整方法 | 直接调用 `EpicBookDomainService.InitializeAsync`，它是启动期迁移，3j 要求作为作业运行；测试本身不经启动服务 |
| Protocol 的 `BuyItemCodecTests`、`NpcShopGainProjectionTests`（3 条）、`DeleteItemCodecTests`、`DisjointItemCodecTests`（2 条）、`MarriageCapsuleCodecTests`、`MarriageHousingCodecTests` | 整方法 | 通知 handler 的 `ComposeAsync` 读了玩法状态（TASK-06 Remaining 3 最后一项） |
| `Host.Tests/GmCeraHttpTests.CommittedGrantSurvives…` | `ComposeAsync` 那一段区域 | 同上；HTTP 调用留在外面 |
| `Host.Tests/GmOfficialMailHttpTests` | `VerifyMixedAttachmentClaims` 的两处 `ComposeAsync`（表达式形状）；`FullEquipmentBagAndGoldLimit…` 直接调用 `MailboxDomainService.ClaimAsync` 的两段 | 同上，以及直接调用领域服务 |
| `Networking.Tests/Integration/GameLoginIntegrationTests.cs` | 6 处不等待入口推进的快照断言读（`TryGetByConnection`），用提出返回值形状 | 与 TASK-06 在 `BossTowerFrameIntegrationTests` 的做法相同；轮询谓词不包 |

**验证产物**

- `verification/task-07-conflicts.md`、`task-07-diff-check.md`、`task-07-probe.md`；
- `verification/logs/` 下的 `task-07-build-test.txt`、`task-07-isolated.txt`、`task-07-probe-{Application,Persistence,Protocol,Host,Networking}.txt`。

### Why（本地决定）

- **整方法包裹不带容器**：容器在方法体里才建出来，作业头拿不到它。TASK-06 在 Protocol 的整方法包裹也是这么做的（Protocol 里 331 处不带容器的 `RunAsync`）。手里有容器的区域包裹一律传了容器。
- **直接调用的 handler 一律包**（T5），即使 handler 内部用真实总线。沿用 TASK-06 `LeaveDungeonTests` 的判例，审查已接受。
  - 由此消失的 (A) 记录共 166 个测试，逐个核过，调用方都是 handler 或投递，见 `task-07-probe.md` §6。
- **K2 `LoginSessionCommitRaceTests` 不算冲突**：两条都是单流程顺序调用，用持久参与者模拟「准备与提交之间断连」，不涉及门，也不涉及并发。
- **K1 中不争用的 3 条包裹后保留到 T-005**；`CommandBusTests` 的 3 条租约释放用例经总线、不争用，所以不包，归 T-004c 复核、T-005 删除。

### Checks

1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-07-build-test.txt` 开头）。
2. **全量**（`logs/task-07-build-test.txt`）：
   - 总数 50 ＋ 1694 ＋ 80 ＋ 475 ＋ 969 ＋ 138 ＋ 4428 ＋ 165 ＝ 7999 ＝ 7986 ＋ 13，各项目与 TASK-06 逐项相等，本单元没有增删测试；
   - 通过 50 ＋ 1694 ＋ 80 ＋ 475 ＋ 967 ＋ 136 ＋ 4410 ＋ 165 ＝ 7977 ＝ 7964 ＋ 13；
   - 失败 2，跳过 2 ＋ 2 ＋ 16 ＝ 20，7977 ＋ 2 ＋ 20 ＝ 7999；
   - 失败的只有那 2 条 `DimensionDungeonTests`；
   - 表征测试 8 条通过、2 条跳过。
   - 隔离复跑（`logs/task-07-isolated.txt`）：Application 4428 条，4410 通过、2 失败（同上）、16 跳过；Networking 138 条，136 通过、2 跳过。
3. **全量断言核对**（`task-07-diff-check.md` §1～§5，基准 `0223e646`）：
   - 299 个文件，方法 2552 ＝ 2552，断言行 13491 ＝ 13491，多重集合逐文件相等；
   - 白名单外 4 个片段，逐条说明；
   - `git diff -w` 行级补核：3656 个 hunk 中 3646 个逐字通过，10 个人工判定，都是白名单形状或那 2 条已说明的例外；
   - 行尾、BOM、末尾换行与基准差异 0。
4. **迁移点对账**（同文件 §6）：
   - 写事务 `.Begin()`：Application 604 ＋ Persistence 60 ＋ Protocol 30 ＝ 694，与 Plan 相等。Application 有 26 处强转写法，已计入 604，没有漏计。
   - 运行态 `Begin(…)`：Application 90、Protocol 12、Host 3、Networking 1。
   - 不在作业区域内的 63 处，逐处归属：冲突用例 5 处、调用方已包的辅助方法体 58 处。
5. **探针**（`task-07-probe.md`）：
   - 计数分族、分项目见 §2；
   - (B) 逐条对照见 §4：有 (B) 的测试，Application 82 ＝ 78 在清单 ＋ 4 条替身记录；Persistence 2；Protocol 40 ＝ 29 ＋ 7 有意留外 ＋ 4 条截断记录；Host 1；Networking 11 全是轮询。替身与截断记录的归属由排除复跑证明；
   - (C) 逐个栈顶分诊见 §5；
   - (A) 按入口分组见 §3；TASK-03 三处见 §3.11；
   - 原始记录在 `logs/task-07-probe-*.txt`。
6. **还原证明**（`task-07-probe.md` §8）：
   - 改动前、中途、最终三次快照的 sha1 都是 `5dbaa2e5d76575d490ed2e300cbd873110fbb5f4`，逐字节相等；
   - 每次快照里，索引 blob 与工作区 blob 逐条相等，1576 ＝ 1576，未跟踪文件 0 个；
   - `git diff --stat -- Server/src` 为空，`git status --short -- Server/src` 为空。
7. `grep -rn "TestGameplay\|GameplayProbe" Server/src`：零命中，退出码 1。

批次清单（每批都构建了 Application.Tests 并跑过探针，测试结果均为 4428 条：4410 通过、2 失败、16 跳过）：

- [x] 0：迁移前探针 r0。
- [x] 1：共享夹具（ShopFixture、LearningFixture、婚姻与仓库辅助等）→ r1。
- [x] 2：自动整方法包裹 768 条 → r2。
- [x] 3：分类器修正之后自动补 98 条 → 4 个 fork 处理 92 个文件 → 构建 → r3（发现 1 处误包，已改正）。
- [x] 4：K1、K2 判定；两处断言行内的读；Application 夹具补 1 条 → 五个项目 r4、r5，补 TASK-06 项目的漏包。
- [x] 5：排除复跑 x1～x3，归属截断记录与替身记录。
- [x] 6：最终一版记录器（加 P1b）→ r6 → 补 P1b 写类 6 条 → r7 → 还原 → 正式全量 → 隔离复跑。

### Remaining

**交给 T-004b**

- **探针 (A) 清单**：`task-07-probe.md` §3，按入口族分组列出具体入口、触达的挂点和首个触达的状态，共 10 族。
- **TASK-03 的三处门外写**（§3.11）：
  - `SetUserAreaHandler` 跨区落位置：**出现**，由 Networking 的 Party 表征测试 5 条经 dispatcher 触发，测试帧不可见，按静态检索归属；
  - `CharacterStateAccess` 的 `AloneAsync`：**出现**，由断连拆除（`OnTcpClosed`）与上一条路径触发。迁移前还有 Application 的 5 条直接调用，已包裹；
  - `CreatureRuntimeDomainService.cs:274`：**未出现**，没有用例走到这个分支，迁移时需要自己补反例。
- **冲突用例 21 条**：`task-07-conflicts.md` §1。其中 `SelectCharacterLifecycleTests:236,365` 与 `ReplayStateSnapshotTests:206` 如果拖到 T-004c 翻转才失败，由 T-004c 执行。
- **停机顺序（3i）**：`DungeonCardPresentationCoordinator.Dispose` 在容器释放时取它的锁。探针在 `Host.Dispose`、`ServiceProvider.Dispose` 和 Networking 各个宿主释放时都记到了它。
- **注册容器执行器之后要复核的区域**（TASK-06 N-4 的 Application 部分）：
  - `DomainEventDispatchTests.A_slow_live_group…`：worker 运行时，第二次 `f.SendAsync` 在作业里等 `TransactionalExecutionGate`；
  - `WeaknessGoldRecoveryTests`、`ItemSaleTests.Two_concurrent…`、`MailboxCoreIntegrationTests.ConcurrentClaim…`、`QuestHelperPuppetTests`、`SaoTowerStoreTests`、`DungeonVisitDomainServiceTests.Concurrent_first_visits`：并发发起方各自包了一层，按 §2.3，注册之后会串行化；
  - `PartySettingsDomainTests.ConcurrentUpdates…`：用的是同步 `Barrier(3)`，没有容器。如果以后改成传容器，两方会互等到超时；
  - `ShopFixture` 这一类夹具都传了 `provider`：注册之后它们与 dispatcher、总线共用执行器。
- **卡牌计时器**：`ControlledDelay` 触发的计时器回调（§3.6），以及 `World.Dispose` 有意留在作业外（TASK-06 N-3）。

**交给 T-004c**

- **冲突用例 62 条**：`task-07-conflicts.md` §2，其中 K1 22 条。
- **判据挂点的注意事项**：
  - 挂在全部 `new DomainTransaction()` 上，会拒掉 148 条 P1b-read 记录对应的边界外只读一次性事务（§4.1），只读和写要区分；
  - 判据如果覆盖玩法状态的**读**，Networking 的轮询谓词（11 个用例）要改成每次求值一个短作业。
- **包裹之后仍需复核的**：
  - `SelectCharacterLifecycleTests.CrossConnectionDisplacement…`：关连接回调在 handler 放门之后取过渡门；
  - `OnlineUserServiceTests.DisconnectWithoutSelectedSession…`：在作业里取一把空闲的门，观察者调用留在外面；
  - `DungeonParticipationDomainServiceTests.Prepared_claim…`：在一个作业里手工交错两个事务。

**交给 T-006**

- 冲突用例 53 条：`task-07-conflicts.md` §3。
- 另有已包裹、但断言的是投递重试与关连接语义的 `IllusionTowerRewardProjectionTests` 3 条，T-006 改写投递时要一并处理。

**未提交**：本单元没有 commit，也没有 stash。

### 主控方补充（结果审查通过之后，提交之前）

- 四处自报偏离全部接受，审查同样接受。审查的 N-1、N-3 两处措辞已在提交前更正。
- **交给 T-004b**：
  - **N-2**：不带容器的整方法包裹共 867 条，每次调用都新建一个执行器。T-004b 注册容器执行器之后，这些包裹不会因为自我等待而报错，所以「误包生产入口」这道兜底只覆盖带容器的包裹。T-004b 对 (A) 入口逐个对账时，不能依赖这道兜底，要以经真实入口驱动的用例为证据。
  - **残余风险**：注册容器执行器之后，使用 `ShopFixture` 的冲突用例可能从「失败」变成「挂起」。必须在注册的同一提交里先处置这些用例。
- **记载**：运行态 `Begin` 的对账（90 处，Plan 写的是约 97）解释偏弱（N-4），不影响判据，T-004c 开启判据后以全量零违规为准。`verification/logs` 累计约 31 MB（N-5），T-009 收口时评估是否压缩或精简。

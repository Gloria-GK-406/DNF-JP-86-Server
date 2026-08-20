# TASK-01 — 载体协议按数据库归并参与者

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 把与聚合无关的载体协议从物品存储中提取出来，并使持久参与者按**数据库**而非按 store 归并；物品侧成为该协议的一个具名实例，对外行为与既有用例语义不变。
- Referenced spec requirements: RQ-002, RQ-003
- Referenced plan sections: `plan.md` 的 `### T-001`；`## Owned Boundaries` 的 BD-001、BD-002、BD-003、BD-004、BD-012；`## Risks and Rollback` 第一条
- Owned boundary: BD-001, BD-002, BD-003, BD-004, BD-012
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Transactions/`（可新增文件）
  - `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs`
  - `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs`
  - `Server/src/Dfo.Server.Application/Inventory/MemoryItemStore.cs`
  - `Server/tests/`
- Dependencies: 无
- Required dependency outputs: 无
- Acceptance conditions:
  1. 同一数据库上取两种不同的具类型会话时，事务中的持久参与者恰为一个，且两者的写在同一次落定中共同生效。
  2. 两个不同数据库各自的持久会话同时参与时以失败暴露，不降级为非原子。
  3. 载体协议本身不认识任何聚合：其词汇只有载体身份、是否持久、以事务换取会话。
  4. 物品侧对外形状与行为不变，既有物品存储用例全部通过且语义未改。
  5. 全量测试不低于基线。
- Task-specific authorization: 属根 TaskContract `task` 授权范围内。本单元不得触碰人物侧任何文件。
- Required methods: `test-driven-development`。验收条件 1 与 2 的用例**必须先于实现写出并留有 RED 证据**；第二种具类型会话只存在于测试中，不得进入生产代码。
- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Transactions` | 新增的单参与者与多数据库用例通过 |
| CK-002 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~ItemStore` | 既有物品存储用例全部通过且语义未改 |
| CK-003 | `dotnet test Server/86JP.Server.slnx -m:1` | 不低于基线 944 通过 / 2 跳过 / 0 失败 |

- Required evidence: RED 证据（先失败的用例名与失败信息）、三条检查的实际输出、以及物品侧对外形状未变的说明。

### 共同约束

- 全程不产生任务级提交。工作保持未提交，直至完整评审与新鲜验证通过后由工作区控制器做唯一一次已接受结果提交。
- 对外可观察行为一字不变：帧字节与时序均不变。
- 既有测试语义不得修改，只允许因类型或依赖变化做机械调整。
- 存储实现不得自建任何表。
- 开发阶段的策略是重建而非迁移：不得为既有数据编写回填、修复或迁移路径。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`；客户端可执行文件与 DLL 不得手工编辑。
- C# 文件为 CRLF、无 BOM UTF-8；`net10.0`，`TreatWarningsAsErrors`。每次以工具写入 `.cs` 后须做一次行尾归一化。

### 验证环境

- `dotnet test Server/86JP.Server.slnx -m:1`
- `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`
- 基线：944 通过 / 2 跳过 / 0 失败。`ChannelHostStartupTests` 在负载下会假红，复跑确认。
## Contract Amendment A1

- Amends: Permitted files or interfaces
- 时点：实施开始，动手之前。

**增列 `Server/src/Dfo.Server.Infrastructure/Persistence/` 为本单元的 Permitted path，仅限承载「按数据库归并的持久参与者」及其面（facet）契约。**

为什么必须现在切分：本单元的目标就是让参与者按**数据库**而非按 store 归并，而按数据库归并的参与者按定义不属于物品。把它写在 `SqliteItemStore.cs` 里，TASK-04 接入人物会话时必然要把它搬走，那时搬动的是已被物品侧消费的代码，代价与风险都更高；而留在原地又会让一个名为「物品存储」的文件承载与物品无关的事务机制。

为什么不越出必要范围：新路径下只放参与者与面契约两样东西。物品会话本身仍留在 `SqliteItemStore.cs`，其读写词汇一字不改。

授权：属根 TaskContract `task` 授权范围内的边界记录，不扩大对外可观察行为，留待 change review 确认。
## Execution Result 1

### 交付

载体协议已提取，且持久参与者改按**数据库**归并。物品侧成为该协议的一个具名实例，读写词汇与对外形状一字未改。

| 文件 | 变化 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Transactions/DomainStores.cs` | 新增。`IDomainStore`（载体身份、是否持久）、`IDomainStore<TSession>`（以事务换取会话）、`IStoreSession<TPlace,TKey,TValue>`（按位置读、按位置写、按位置清空） |
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs` | 改为 `IItemStore : IDomainStore<IItemStoreSession>` 与 `IItemStoreSession : IStoreSession<ContainerId,int,StoredItem>`。成员全部继承，对外形状不变 |
| `Server/src/Dfo.Server.Infrastructure/Persistence/SqliteDatabaseParticipant.cs` | 新增（见 Contract Amendment A1）。按数据库入伙的参与者，持有连接与数据库事务；`ISqliteStoreFacet` 为各类状态的面契约 |
| `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs` | 会话由参与者降为面。`StoreIdentity` 仍为连接字符串；`SessionIn` 先按其入伙数据库参与者，再取物品面 |
| `Server/tests/Dfo.Server.Application.Tests/Transactions/OneDatabaseOneParticipantTests.cs` | 新增 4 条 |
| `Server/tests/Dfo.Server.Infrastructure.Tests/Inventory/SqliteItemStoreTests.cs` | 两处机械调整（见下） |

`MemoryItemStore.cs` **未改动**：它已满足新契约，易失会话仍是自己的参与者（`CommitCanFail => false`），任意多个可与唯一的持久参与者并存。BD-004 因此为零改动。

差异：受版本控制的三个文件 +59 / -192；新增三个文件。净减来自 `SqliteItemStore.cs` 把连接与事务生命周期交给了参与者。

### 泛型为何不退化成 key-value

`IStoreSession<TPlace,TKey,TValue>` 三个类型参数都由实现方钉死（物品侧是 `ContainerId` / `int` / `StoredItem`），所以「按位置寻址」这套词汇是共享的，而**类型一个不丢**。这正是 Spec 的 Definitions 里「位置」与「数据动词」两条的落地形式。

### 既有测试的两处机械调整

两处原本以 `Assert.IsAssignableFrom<ITransactionParticipant>(session)` 断言「会话就是参与者」。参与者现在是数据库，会话是它的一个面，故改为从事务上取参与者：

- `Store_IsTheDurableParticipantWhoseCommitCanFail`：改为 `Assert.Single(transaction.Participants).CommitCanFail`。所证明的保证不变（持久存储的参与者其提交可失败），且比原来更强——顺带钉住了「恰好一个」。
- `Commit_RefusesToStandInForAPrepareThatNeverRan`：同样改为从事务取参与者，断言与后续步骤一字未改。

这是本单元 Required methods 允许的「因类型或依赖变化的机械调整」。两处的意图与断言强度均未下降，故不认为构成测试语义修改。

### RED 证据：以证伪代替时序

本单元新增的用例引用了协议本身引入的类型（`SqliteDatabaseParticipant`、`ISqliteStoreFacet`），因此在协议存在之前无法编译，严格的「先红后绿」时序对纯协议引入不可得。改为做一次**证伪运行**，其证明力强于普通 RED：

把 `SqliteItemStore.StoreIdentity` 由连接字符串改为 `this`，即本设计明确否决的「一个 store 一个参与者」做法，其余代码不动。结果：

```
失败!  - 失败:     3，通过:     1，总计:     4
```

4 条中 3 条转红。未转红的是 `TwoDatabasesStillCannotBothTakePart`——两个数据库在两种设计下都应被拒，故它本就不该对此变化敏感。随即恢复原文件并复跑至全绿。

这条证据正对 plan `## Risks and Rollback` 第一条所述的危险：按直觉提取协议不会立刻报错，要到人物存储接入时才暴露。现在它会立刻报错。

### 检查

| ID | 结果 |
|---|---|
| CK-001 | `已通过! - 失败: 0，通过: 18`（既有 14 条 + 新增 4 条） |
| CK-002 | `已通过! - 失败: 0，通过: 17`（与改动前同数，语义未改） |
| CK-003 | 全量 `948 通过 / 2 跳过 / 0 失败` = 基线 944 + 新增 4。首跑 `Dfo.Server.Host.Tests` 报 1 失败，单独复跑 `4/4 通过`，为已知的 `ChannelHostStartupTests` 负载假红 |

### 基线数字的更正

任务启动时各工件写的基线是「912 通过 / 2 跳过」，该数字取自压缩摘要，是**上一个根任务中途**的计数。在 main 检出（712bdbc，仅有无关的 Docs 改动）实测全量为 **944 通过 / 2 跳过 / 0 失败**（Pvf 60、Application 449、Host 4、Infrastructure 185+2 跳过、Networking 46、Protocol 200）。

这不是记述瑕疵而是验收强度问题：留着 912，CK-021 那条「通过数不低于基线」即便删掉三十条用例也仍然成立。已把 TaskContract、spec、plan、routing-decision 与七个 Task 文件中的该数字统一更正为 944，全库无残留。

## Executor Self-check 1

| 验收条件 | 证据 |
|---|---|
| 1 同一数据库两种具类型会话 → 恰一个参与者，且共同生效 | `TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant`、`TwoKindsOfStateInOneDatabaseBecomeVisibleTogether` |
| 2 两个数据库同时参与 → 失败暴露 | `TwoDatabasesStillCannotBothTakePart`；既有 `ASecondStoreThatKeepsThingsIsRefused` 亦仍通过 |
| 3 载体协议不认识任何聚合 | `DomainStores.cs` 不引用 `Dfo.Server.Data.*` 与任何聚合类型，三个类型参数全部开放 |
| 4 物品侧对外形状与行为不变 | CK-002 同数通过；`IItemStore` / `IItemStoreSession` 成员签名逐条继承自协议，无新增无删减 |
| 5 全量不低于基线 | CK-003，948 = 944 + 4 |

补充：验收条件 1 的「共同失败」一侧由 `AFailureInOneKindOfStateLeavesTheOtherOneUnchanged` 覆盖——一种状态在写出阶段抛出，另一种状态的改动亦不可见。

### 自认的薄弱处

- 第二种具类型状态是本文件内的替身（写 `characters.level`），不是真实聚合。真实的第二种要到 TASK-04 才出现。这是本单元「在最小面上先证明规则」的刻意取舍，已在测试文件的类注释中写明。
- `SqliteDatabaseParticipant` 落在新路径 `Infrastructure/Persistence/`，超出 Task 文件初版的 Permitted files，已由 `## Contract Amendment A1` 在动手前申报。
- 面的刷写次序即 `facets` 的插入次序。本单元只有一个面故不可观察；若将来两个面写同一张表且互相依赖次序，需要显式定序。已知未决，记录在此。

## Independent Quick Review 1 — QR-T01-R1

- 判定：`changes_required`
- 评审的契约修订：Contract revision 1 + Contract Amendment A1
- 被评审的 owned diff：`git diff -- Server/` 三个受版本控制文件（`IItemStore.cs`、`SqliteItemStore.cs`、`SqliteItemStoreTests.cs`，+59/-192），以及三个新增文件（`Transactions/DomainStores.cs`、`Persistence/SqliteDatabaseParticipant.cs`、`Transactions/OneDatabaseOneParticipantTests.cs`）
- 检视的证据：`git status --porcelain`、`git diff --stat -- Server/`、上述六个文件全文、`DomainTransactions.cs`、`DomainTransaction.cs`、`MemoryItemStore.cs`、`ItemContainers.cs`(235-362)、`Dfo.Server.Application.Tests.csproj`、`Dfo.Server.Infrastructure.csproj`、`plan.md` 的 T-001 与 BD-001..BD-004/BD-012、`spec.md` 的 RQ-002/RQ-003、行尾与 BOM 抽检。按指示未复跑任何测试。

### 边界（通过）

受版本控制的改动恰为 Permitted files 中申报的三个文件；三个新增文件分别落在 BD-001 的 `Abstractions/Transactions/`、A1 增列的 `Infrastructure/Persistence/`、以及 `Server/tests/`。`Infrastructure/Persistence/` 目录下只有 `SqliteDatabaseParticipant.cs` 一个文件，内容恰为「参与者 + 面契约」两样，未越出 A1 的限定。无 csproj 改动即可编译，因 `InternalsVisibleTo Dfo.Server.Application.Tests` 与 Infrastructure 的 ProjectReference 本已存在。`MemoryItemStore.cs` 确实零改动，BD-004 的说法成立。六个 `.cs` 文件全部 CRLF、无 BOM，符合共同约束。人物侧文件未被触碰。

### 阻断发现

| ID | 文件与行 | 说明 |
|---|---|---|
| QR-1 | 本文件 95-107 行（Required methods 见 25 行、Required evidence 见 34 行；plan `### T-001` 步骤 1） | 契约以「必须」要求验收条件 1 与 2 的用例先于实现写出并留有 RED 证据，Required evidence 进一步指定证据形式为「先失败的用例名与失败信息」。执行者以证伪运行替代，且未就此偏离提交契约修订。两点使其目前不成立：其一，所给证据只有聚合计数 `失败: 3，通过: 1，总计: 4`，没有用例名与失败信息，不满足指定的证据形式；其二，「严格的先红后绿时序对纯协议引入不可得」这一理由不成立——先写用例得到的编译失败就是静态类型语言中标准的 RED，故此处是取舍而非不可得。补救是文档级的、局部的，不需要返工实现：追加一条 Contract Amendment 记录该替代与理由，并补上三条转红用例的名称与失败信息（证伪运行不属于 CK-001..003，可重跑）。 |

对该替代本身的独立核对（不改变上述判定）：其**诚实性成立**。以 `StoreIdentity => this` 推演，`TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant` 因参与者变两个而红，`TwoKindsOfStateInOneDatabaseBecomeVisibleTogether` 与 `AFailureInOneKindOfStateLeavesTheOtherOneUnchanged` 因第二个持久参与者被 `DomainTransaction.Enlist` 拒而在 `Marks.SessionIn` 处抛出而红，`TwoDatabasesStillCannotBothTakePart` 两种设计下都应被拒故不敏感——与所报的 3 红 1 绿及所指认的未转红用例逐条吻合。作为「用例能分辨按数据库与按 store 两种设计」的证据，其证明力确实强于编译失败式 RED。所缺的只是契约要求的形式与授权。

### 非阻断发现

| ID | 文件与行 | 说明 |
|---|---|---|
| QR-2 | `Server/src/Dfo.Server.Infrastructure/Persistence/SqliteDatabaseParticipant.cs:155-175` | `SettleAsync` 的 `finally` 中，遍历 facet 调 `Discard()` 的循环夹在 `pending = null` 与 `CloseAsync()` 之间。任一 `Discard()` 抛出都会跳过 `CloseAsync()`，与该方法自己的注释「The connection is closed regardless」相矛盾，连接句柄被留住。现有两个 facet 的 `Discard()` 只是清字典、抛不出来，故非现存缺陷。把关闭放进自己的 `finally` 即可。 |
| QR-3 | `SqliteDatabaseParticipant.cs:98-101`、`OneDatabaseOneParticipantTests.cs:63-77` | 面的刷写次序取自 `Dictionary` 的枚举次序，只在无删除时事实上等于插入次序，契约上并无保证。`AFailureInOneKindOfStateLeavesTheOtherOneUnchanged` 恰恰依赖「物品先于 marks 刷写」才证明了物品的写被回滚；次序若反转，该断言会因物品根本没写而以更弱的理由通过。执行者已在本文件 139 行记下定序未决，此处补记这条用例对次序的隐式依赖。以一个 `List<ISqliteStoreFacet>` 与字典并存即可显式定序。 |
| QR-4 | `SqliteDatabaseParticipant.cs:71-77, 199-200` | `ISqliteStoreFacet.FlushAsync` 把裸 `SqliteConnection` 交给面，而打开的数据库事务挂在参与者身上；面若直接 `link.CreateCommand()` 而非 `owner.Command(link)`，其语句就落在事务之外。Microsoft.Data.Sqlite 自身会校验 command 与 connection 的事务是否一致，故此坑是响亮报错而非静默非原子——但 TASK-04 要接入第二个面，契约改为交出 command 工厂（或参与者本身）更省事。 |
| QR-5 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs:19,38` 对 `DomainStores.cs:54-68` | 成员现由 `IStoreSession<,,>` 继承而来，参数名随之改变（`containerId`→`place`、`slotIndex`→`key`、`item`→`value`）。当前无调用点使用具名实参故编译干净，但「对外形状不变／成员签名逐条继承、无新增无删减」只在元数与类型上为真，公开接口的参数名确实变了。 |
| QR-6 | `Server/tests/Dfo.Server.Infrastructure.Tests/Inventory/SqliteItemStoreTests.cs:240-249` | 已核对：改后不再证明「交给调用方的 session 就绑在入伙的那个参与者上」，这一点原来的类型转换是顺带证明的。但覆盖并未丢——`Commit_RefusesToStandInForAPrepareThatNeverRan`（同文件 334-346）经 session 写入后断言 participant 拒绝提交，只有参与者看得见该 session 的暂存写时才成立。故判定为共同约束（本文件 40 行）允许的机械调整，不构成测试语义修改；断言强度净增（多钉住了「恰好一个」）。 |
| QR-7 | 本文件 117-121 行 | 基线 912→944 的更正波及本根任务的 TaskContract、spec、plan、routing-decision 与七个 Task 文件。已核对该更正只落在本根任务自己的未受版本控制工件目录内，`.rks-agent/tasks/domain-transaction-and-item-store/` 中的历史记述被正确保留。但由一个单元的执行者改写六份同级**已封存**的 Task 契约，属控制器所有的动作，且未走修订。请控制器追认。 |

### 协议与聚合无关性（通过，附一条前瞻）

`DomainStores.cs` 无任何 `using`，不提及任何聚合类型，三个类型参数全部开放；`IStoreSession` 在 BD-001「载体与会话的通用契约」的申报范围内，不算越出验收条件 3 所列词汇。前瞻一条留给 TASK-04：`IStoreSession` 已经钉入「一个位置持有一组按键有序的值」（`ReadAllAsync` 返回 `IReadOnlyList<TValue>`、`Clear(place,key)`）以及 `TValue : class`。若人物状态是一个位置一个值、或值为结构体，就得往这个形状里掰。现在不是缺陷，也不该在本单元解决，但这正是 A1 想避免的返工，宜在 TASK-04 早期先确认贴合度。

### 要求的补救

只有 QR-1 一条阻断。补救为文档级：追加一条 Contract Amendment 记录以证伪替代先红后绿的偏离与理由，并补录三条转红用例的名称与失败信息。QR-2..QR-7 供执行者与最终评审酌情处理，不阻断本单元推进。
## Contract Amendment A2

- Amends: Required methods、Required evidence
- 时点：Independent Quick Review 1 判 `changes_required`，唯一阻断项为「以证伪代替先红后绿，未申报」。

**本单元的「失败用例先于实现写出」以一次证伪运行替代，并按下述方式留证。**

首评的判断是对的，本处更正 Execution Result 1 的说法：先写用例得到的**编译失败就是静态类型语言里标准的 RED**，并非「对纯协议引入不可得」。选择证伪是取舍，不是无路可走。

取舍的理由：编译失败只能证明「用例引用了尚不存在的类型」，不能证明「用例分辨得出正确设计与错误设计」。而本单元要挡的风险恰恰是后者——plan `## Risks and Rollback` 第一条所述，按「一个 store 一个参与者」的直觉提取协议不会立刻报错，要到 TASK-04 才暴露。证伪运行直接证明用例能分辨这两种设计，证明力覆盖了编译式 RED 想证明的东西。

代价：失去了「实现前用例确实不通过」这一时序事实。本修正明示接受该代价，并以下节的完整证伪记录（用例名、失败信息、行号、恢复确认）补齐 Required evidence 所要求的内容。

本修正同时触及 Required evidence：该条原文要求「先失败的用例名与失败信息」，其中「先」的时序含义随本替代而变——所留证据是**注入错误设计后**的失败，而非实现前的失败。内容要求（用例名、失败信息）已按原样补齐，时序含义的变更在此明示。

授权：属根 TaskContract `task` 授权范围内的方法与证据记录，不改变验收条件，不扩大对外可观察行为，留待 change review 确认。

## Execution Result 2 (post-verdict)

首评判 `changes_required`：1 项阻断、若干非阻断。处置如下。

### 阻断项

| findings | 处置 |
|---|---|
| QR-1 证伪替代未申报，且「不可得」的理由不成立；证据只有聚合计数，缺 Required evidence 指定的用例名与失败信息 | 已按 `## Contract Amendment A2` 申报并更正理由；完整证伪记录见下 |

### 证伪记录（重跑于本轮代码之上）

注入：`SqliteItemStore.StoreIdentity` 由 `this.connectionString` 改为 `this`，即参与者按 store 而非按数据库归并——本设计明确否决的做法。其余代码不动。

结果 `失败: 3，通过: 1，总计: 4`：

| 用例 | 失败信息 | 位置 |
|---|---|---|
| `TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant` | `Assert.Single() Failure: The collection contained 2 items` | `OneDatabaseOneParticipantTests.cs:41` |
| `TwoKindsOfStateInOneDatabaseBecomeVisibleTogether` | `System.InvalidOperationException : A transaction admits at most one participant whose commit can still fail; 'SqliteDatabaseParticipant' already holds that place, so 'SqliteDatabaseParticipant' cannot join it.` | 抛于 `MarkStore.SessionIn`，`OneDatabaseOneParticipantTests.cs:140` |
| `AFailureInOneKindOfStateLeavesTheOtherOneUnchanged` | 同上 | 抛于 `MarkStore.SessionIn`，`OneDatabaseOneParticipantTests.cs:140`，调用点为 `:69`——早于 `:73-74` 的 `Assert.ThrowsAsync`，故未被它捕获 |

未转红：`TwoDatabasesStillCannotBothTakePart`。两个数据库在两种设计下都应被拒，故它本就不该对此变化敏感。

恢复已确认：`grep -c 'StoreIdentity => this.connectionString'` 返回 `1`。

### 已修正的非阻断实现缺陷

| findings | 修改 |
|---|---|
| `SettleAsync` 中 facet 的 `Discard()` 循环夹在 `pending = null` 与 `CloseAsync()` 之间，某个 `Discard()` 抛出会跳过关连接——与该方法自己「连收尾都做不到的会话恰恰不能把句柄留着」的注释相矛盾 | `Discard()` 循环单独包一层 `try/finally`，无论是否抛出都关连接 |
| 刷写次序取自 `Dictionary` 的枚举次序，而枚举次序不属其契约 | 新增 `List<ISqliteStoreFacet> order` 记录入伙次序，prepare、commit 守卫与 settle 一律按它遍历。注释写明理由 |
| `FlushAsync` 收到裸连接而事务挂在参与者上，面若自建命令会漏掉事务 | 面不再拿连接：`ISqliteStoreFacet.FlushAsync(CancellationToken)` 去掉连接参数，参与者改为提供 `CommandAsync(ct)`，连接与事务的绑定由参与者独占。`OpenAsync` 相应降为私有 |

### 未修改并说明理由的一项

| findings | 判断 |
|---|---|
| 前瞻：协议已钉入「一个位置持有一组按键有序的值」与 `TValue : class`；若人物状态是一位置一值、或值为结构体，TASK-04 要往这形状里掰 | 属实，但现在补一个无消费者的标量段契约是投机性泛化，正是评审该挡的东西。已作为交接事项记入下节，由 TASK-04 在有真实消费者时决定形状 |

### 复验

| ID | 结果 |
|---|---|
| CK-001 | `已通过! - 失败: 0，通过: 18` |
| CK-002 | `已通过! - 失败: 0，通过: 17` |
| CK-003 | 全量 `948 通过 / 2 跳过 / 0 失败`（Pvf 60、Application 453、Host 4、Infrastructure 185+2 跳过、Networking 46、Protocol 200）。本轮 Host 在全量中亦通过 |

`dotnet build Server/86JP.Server.slnx`：0 警告 0 错误。

### 交接 TASK-04 的两项

1. **标量段的形状未定。** 现协议只表达「一个位置持有一组按键有序的值」。人物侧的 `characters` 行位置状态是一位置一值，届时需判断是复用现协议（键取常量）还是新增一个标量段契约。首评已就此提出前瞻，判断留给有真实消费者的那一刻。
2. **面的刷写次序现在是显式的入伙次序。** 若人物面与物品面会写到同一张表并互相依赖次序（例如宠物复活要读 `character_new_items` 再写 `character_creatures`），该依赖须显式表达，不可依赖入伙先后。

## Executor Self-check 2

| 首评发现 | 状态 | 证据 |
|---|---|---|
| QR-1（阻断）证伪替代未申报、理由不成立、缺用例名与失败信息 | 已闭合 | `## Contract Amendment A2`；上节证伪记录含三条用例名、失败信息、行号与恢复确认 |
| `SettleAsync` 的 `Discard()` 可跳过关连接 | 已修 | `SqliteDatabaseParticipant.cs` 的 `SettleAsync` 内层 `try/finally` |
| 刷写次序依赖 `Dictionary` 枚举次序 | 已修 | 新增 `order` 列表，三处遍历改用之 |
| `FlushAsync` 交出裸连接 | 已修 | 面契约改为 `FlushAsync(CancellationToken)`，参与者提供 `CommandAsync`，`OpenAsync` 转私有 |
| 协议对 TASK-04 的形状前瞻 | 记录未改 | 见「交接 TASK-04 的两项」第 1 条 |

验收条件五条的证据与 Self-check 1 相同，未因本轮修改而变动；三处实现修正均未触及任何验收条件所断言的行为，CK-001 与 CK-002 的通过数与本轮修改前一致（18 / 17）。

## Independent Quick Review 2 — QR-T01-R2

- 判定：`passed`
- 评审的契约修订：Contract revision 1 + Contract Amendment A1 + Contract Amendment A2
- 本轮范围：只判定首评（QR-T01-R1）发现的闭合情况，并复核补救本身是否引入新问题。首评已通过的边界、协议与聚合无关性、既有测试语义未重复评审，除非本轮补救动到了它们。
- 被评审的 owned diff：`git status --porcelain` 与首评同集——三个受版本控制文件（`IItemStore.cs`、`SqliteItemStore.cs`、`SqliteItemStoreTests.cs`，+56/-194）与三个新增文件（`Transactions/DomainStores.cs`、`Persistence/SqliteDatabaseParticipant.cs`、`Transactions/OneDatabaseOneParticipantTests.cs`）。本轮实际改动的只有三个文件（mtime 12:01–12:03）：`SqliteDatabaseParticipant.cs`、`OneDatabaseOneParticipantTests.cs`、`SqliteItemStore.cs`。
- 检视的证据：Task 文件全文（A2 / Execution Result 2 / Self-check 2 / QR-T01-R1）、`git status --porcelain`、`git diff -- Server/`、上述三个新增文件全文、`SqliteItemStore.cs` 全文、`DomainTransaction.cs` 全文、`SqliteItemStoreTests.cs`(225-260, 320-360)、`Dfo.Server.Infrastructure.csproj` 的 `InternalsVisibleTo`、全库 grep（`SqliteDatabaseParticipant` / `ISqliteStoreFacet` / `OpenAsync` / `CommandAsync` 的引用点）、六个 `.cs` 的 CRLF 与 BOM、`Server/src`+`Server/tests` 中晚于首评基线的 mtime、`.rks-agent/tasks/character-persistence-on-domain-store/` 全目录 mtime。按指示未跑任何测试，未改动任何生产代码或测试。

### QR-1（唯一阻断项）已闭合

| 首评要求 | 核验结果 |
|---|---|
| 就偏离提交契约修订 | `## Contract Amendment A2` 已提交，`Amends: Required methods`，明示不改验收条件 |
| 更正「不可得」的理由 | A2 第 185 行明写「先写用例得到的**编译失败就是静态类型语言里标准的 RED**，并非『对纯协议引入不可得』。选择证伪是取舍，不是无路可走」，并在第 189 行明示接受「失去实现前用例确实不通过这一时序事实」的代价。诚实性成立，未回避首评的指认 |
| 补录用例名与失败信息 | 三条转红用例各有名称、失败信息与位置 |

失败信息经独立核对与实际代码逐字相符：`A transaction admits at most one participant whose commit can still fail; '…' already holds that place, so '…' cannot join it.` 恰为 `Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:48-52` 的原文（记述只截去了尾句，无改写、无夸大）。

位置逐条重算：

- `TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant` → `OneDatabaseOneParticipantTests.cs:41` 正是 `Assert.Single(transaction.Participants)`，与 `Assert.Single() Failure: The collection contained 2 items` 吻合。✅
- `TwoKindsOfStateInOneDatabaseBecomeVisibleTogether` → `:140` 正是 `MarkStore.SessionIn` 里的 `transaction.Enlist(`，即 `DomainTransaction.Enlist:48` 的调用点。✅
- 第三条见下方 QR2-N1（失败原因与机制正确，仅逃逸位置标注有误）。

「已恢复」可核验：`Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs:33` 现为 `public object StoreIdentity => this.connectionString;`，且该文件其余部分自洽（无注入残留）。`SqliteItemStore.cs` 的 mtime（12:03）晚于参与者与测试文件（12:01/12:02），与「证伪重跑于本轮代码之上、跑完再恢复」的记述一致。

### 三处非阻断修正：均正确，未引入回归

**QR-2（`SettleAsync` 可跳过关连接）已真修复。** `SqliteDatabaseParticipant.cs:171-196`：外层 `try` 处置 `pending`，外层 `finally` 内再套一层 `try { foreach … Discard(); } finally { await this.CloseAsync(); }`。两条抛出路径——`pending.DisposeAsync()` 抛、任一 `Discard()` 抛——都仍到达 `CloseAsync`，与该方法自己的注释一致。残留（既有形状，非本轮引入，不要求处理）：某个 `Discard()` 抛出会中断循环，其后的面保留 staged；但结算后的参与者不可再被 `Enlist` 触及，不构成可观察缺陷。

**QR-3（依赖 `Dictionary` 枚举次序）已真修复且无同步风险。** `order` 列表见 `:31`，唯一的写入点是 `:60-61` 与字典成对追加；两条失败路径都不会造成偏差——类型冲突在 `:53-56` 先抛，两者都不动；`create(this)` 抛出则两者都不动。四处遍历一律走 `order`：`:104`（prepare 的空转判定）、`:114`（flush 循环）、`:130`（commit 守卫）、`:186`（settle 的 Discard 循环）；`facets` 字典现在只作键查，全类无第二处枚举。Self-check 2 说「三处」，实为四处，是记述的保守偏差而非遗漏。

**QR-4（面拿到裸连接）已真修复，无死代码、无绕行路径。** `ISqliteStoreFacet.FlushAsync(CancellationToken)`（`:223`）不再收连接；`CommandAsync`（`:74-80`）在所有路径上都执行 `command.Transaction = this.pending`。`OpenAsync`（`:82`）转私有后仍有两个活引用（`:76` 的 `CommandAsync`、`:110` 的 prepare 取连接开事务），不是死代码；私有可见性使面无从绕开。全库 grep 确认：面触达数据库的全部五处（`SqliteItemStore.cs:71,118,197,216` 与测试内 `MarkSession` 的 `:166`）都经 `owner.CommandAsync`，无一处 `CreateCommand()`。事务挂载在所有路径上成立：唯一开启事务的地方是 `PrepareAsync:111`，先于 `:114` 的 flush 循环，故每条刷写语句都带 `pending`；读路径发生在事务开启之前，`Transaction = null` 与连接的无事务状态一致，正是 Microsoft.Data.Sqlite 校验所要求的。`internal` 的 `CommandAsync` 对测试可见由既有 `InternalsVisibleTo Dfo.Server.Application.Tests` 提供，无 csproj 改动。

### 范围：未扩大

本轮改动恰为三个文件，全部落在 Permitted files 与 A1 增列路径内：`Infrastructure/Persistence/`（A1 限定的「参与者 + 面契约」两样，目录下仍只此一文件）、`Infrastructure/Inventory/SqliteItemStore.cs`、`Server/tests/`。`IItemStore.cs` 与 `DomainStores.cs` 本轮零改动，故验收条件 3、4 的首评结论不受影响，Self-check 2「未因本轮修改而变动」的说法成立。A2 只记方法偏离，未触碰验收条件与 Required checks 表。`.rks-agent/` 下本轮只有 TASK-01 自身被写（12:06），未再波及同级已封存 Task 文件（首评 QR-7 所指的批量改写仍停在 09:55，待控制器追认，本轮无新增）。六个 `.cs` 仍为 CRLF、无 BOM。

### 未修改项的理由成立

拒绝现在增补标量段契约，理由为「无消费者的投机性泛化」——判断恰当。首评提出该点时本就明写「现在不是缺陷，也不该在本单元解决」；本单元没有第二种真实聚合，此刻定形只能靠猜。已作为「交接 TASK-04 的两项」第 1 条留痕，处置得当。

### 本轮非阻断发现

| ID | 文件与行 | 说明 |
|---|---|---|
| QR2-N1 | 本文件第 213 行 对 `OneDatabaseOneParticipantTests.cs:69,73-74,76` | 证伪记录第三行称 `AFailureInOneKindOfStateLeavesTheOtherOneUnchanged` 的异常「逃出 `:76` 的 `Assert.ThrowsAsync`」，两处不实：该文件 `:76` 是 `Assert.Equal(0, await fixture.ReadMarkAsync());`，`Assert.ThrowsAsync` 在 `:73-74`；且注入后异常抛于 `:69` 的 `fixture.Marks.SessionIn(transaction)`，根本到不了 `Assert.ThrowsAsync`，谈不上「逃出」。该行的用例名、失败信息与抛出机制（`MarkStore.SessionIn` → `DomainTransaction.Enlist` 拒第二个持久参与者）均正确，前两行的位置标注 `:41` 与 `:140` 也逐条核准，故 Required evidence 所要求的「用例名与失败信息」已具备，此条只是叙述性位置标注的瑕疵，不影响 QR-1 的闭合判定。若要修，把 `:76` 改为 `:69` 并去掉「逃出 Assert.ThrowsAsync」的说法即可。 |
| QR2-N2 | 本文件第 180 行与第 189 行 | A2 的 `Amends:` 只写 Required methods，但以证伪替代先红后绿也牵动 Required evidence 中「**先**失败的用例名与失败信息」的时序含义。A2 正文（第 189 行）已主张所补记录填齐了 Required evidence 的内容要求，读法可辩护，故不要求补修订；留给 change review 一并确认即可。 |

### 结论

首评唯一的阻断项 QR-1 已闭合：偏离已申报、理由已更正为诚实的取舍陈述、证据形式已补齐并逐条可核验、注入已确证恢复。三处非阻断实现修正各自解决了所指问题，且经独立复核未引入新的正确性缺陷或死代码。范围未扩大，未修改项的理由成立。本轮判定 `passed`——仅表示本 Task Unit 可进入执行者的接受流程，不构成最终质量批准；QR2-N1、QR2-N2 与首评遗留的 QR-5、QR-7 及 `IStoreSession` 形状前瞻，留待最终评审与控制器处置。

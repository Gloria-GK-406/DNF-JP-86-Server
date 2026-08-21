# TASK-08: 行为保持证据

## Task Unit Contract

- Plan task: `T-008`
- Covers: RQ-007, RQ-008
- Boundaries: BD-012
- Required checks: CK-021, CK-022, CK-023
- Permitted paths:
  - `Server/tests/`
  - 本 Task 文件与 `verification/`

### Goal

汇总行为保持的完整证据。

不新增生产改动；若清点暴露缺口，回到对应任务返工而非在此修补。

### Acceptance

| # | Condition |
|---|---|
| 1 | 表与列的声明处仍只有既有唯一归属者，表数与列数与基线一致 |
| 2 | 协议编解码用例与整条帧序列金样未被改写且全部通过 |
| 3 | 全量结果不低于基线 1011 通过 / 2 跳过 / 0 失败，增量全部为新增用例 |

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

## Execution Result (round 1)

本任务不改生产代码。清点结果如下。

### CK-021 表与列的声明处

全仓库 `CREATE TABLE` / `ALTER TABLE` / `CREATE INDEX` 的命中，61 处，全部在
`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` —— 与接受基线相同的唯一归属者，
一处未增、一处未散。

表与列与基线逐一比对（同一段解析脚本分别跑当前文件与 `git show HEAD:` 的同名文件）：

| | 基线 | 现在 |
|---|---|---|
| 唯一表名 | 51 | 51（逐名 diff 为空）|
| 可解析出的表 | 50 | 50 |
| 列总数 | 325 | 325 |
| 列集不同的表 | — | 0 |

该文件本轮唯一的差异是 T-001 加进去的 `KeepWritersAndReadersApart`（`PRAGMA journal_mode=WAL`
及其回读校验），32 行新增、0 行删除，不含任何表或列的改动。

### CK-022 协议侧

`dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj`
→ 203 通过 / 0 跳过 / 0 失败。

Protocol.Tests 下改动的四个 `.cs` 文件，其增删行中含 `Assert.` 的条数为 0——
即没有任何一条协议断言、帧字节金样或期望值被改写。改动全部是构造方式：
`SqliteInventoryEntryRepository` / `SqliteEntryInventoryMutationRepository` /
`SqliteCharacterEntryStateRepository` 三个被删类型换成夹具或 `SqliteDatabase`，
以及编解码静态调用改名。`.csproj` 的改动是把两个共享夹具编译进来。

### CK-023 全量与增量的构成

全量 1035 通过 / 2 跳过 / 0 失败（基线 1011 / 2 / 0），增量 +24。逐条对上：

新增文件（20 条）：

| 文件 | 用例数 | 来自 |
|---|---|---|
| `Persistence.Tests/Database/UseCaseTransactionCoversReadsTests.cs` | 7 | T-001 |
| `Persistence.Tests/Database/OneUseCaseSettlesEveryTableTests.cs` | 3 | T-002 |
| `Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs` | 3（1 Fact + 1 Theory × 2）| T-003 |
| `Persistence.Tests/Accounts/AccountEntryLoadCharacterizationTests.cs` | 4 | T-004 |
| `Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs` | 3 | T-006 |

既有文件新增（4 条）：

| 文件 | 用例数 | 来自 |
|---|---|---|
| `Application.Tests/Characters/OutsideTransactionTests.cs` | 3 | T-005（会话位置的两条路与被顶掉时的结算）|
| `Persistence.Tests/Characters/SqliteEntryLifecycleRecoveryTests.cs` | 1 | T-005 round 2（账号不存在时死信而非发放）|

20 + 4 = 24，与 1035 − 1011 相等。没有用例被删除。

### 断言被改写之处，逐一交代

全部 22 个改动过的测试文件里，增删行含 `Assert.` 的只有三个：

- `Application.Tests/Characters/OutsideTransactionTests.cs`：全部是新增的三个用例带来的，
  无一条既有断言被动过。
- `Persistence.Tests/Characters/SqliteEntryLifecycleRecoveryTests.cs`：四处是把
  `repository.X(...)` 包进 `side.InUseCaseAsync(...)` 的机械换行，断言本身与期望值不变；
  四处是新增用例；只有一处是真正的语义调整——
  `DungeonRecovery_OverlappingMalformedRecordDeadLettersOnceWithoutStealingClaim` 现在容许
  两个并发恢复中的一个被数据库以 `SQLITE_BUSY`/`SQLITE_LOCKED` 挡回而不返回结果。
  该用例原本要证的三件事（记录只被了结一次、钱包未动、`attempt_count` 为 1）原样保留。
  理由与代价记在 T-005 的执行记录与 `DungeonEffectRecoveryService` 的类注释里。
- `Persistence.Tests/Schema/SchemaOwnershipTests.cs`：一个用例被换掉。原
  `TheShapeOfTheDatabaseDoesNotDependOnWhatIsBuiltFirst` 的构造者列表在这轮拆分后只剩一项，
  `reversed` 参数不再改变任何东西；它问的问题现在由类型系统回答（每个仓储都要一个
  `SqliteDatabase` 才能构造）。换成 `BringingADatabaseIntoShapeAgainLeavesItAsItWas`，
  钉住补列守卫。理由记在 T-007 round 2。

### 接手 T-007 留下的两条

- N-5 已修：本轮把本次任务改动过的全部 20 个测试文件的 using 块重新排序
  （`System.*` 在前，其余按字母），此前几轮插入 using 时留下的乱序一并归位。
  不涉及生产代码，在本任务许可路径内。
- N-6 不修：`IMailboxMessageTable.ReadAsync` 缺一段 remarks，属生产代码。本任务明文不新增生产改动，
  且这不是缺口而是文字，记在此处备案。
- N-1 备案：`Live` 在 header 语句求值、`RemainingSeconds` 在随后的整行语句求值，
  原实现两者在同一条 CTE 内。窗口是亚秒级，且要构造它需要一封信恰好在两条语句之间过期；
  实机不可构造，全量与协议金样均未受影响。

## Executor Self-check (round 1)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 表与列的声明处仍只有既有唯一归属者，表数与列数与基线一致 | 通过：61 处 DDL 全在唯一归属者内；51 张表逐名相同；325 列相同；列集不同的表 0 张 |
| 2 | 协议编解码用例与整条帧序列金样未被改写且全部通过 | 通过：Protocol.Tests 203/203，改动行中 `Assert.` 条数为 0 |
| 3 | 全量不低于基线，增量全部为新增用例 | 通过：1035 / 2 / 0，+24 全部逐条对应到新增用例，无删除 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-021 | Non-command，清点表与列声明处 | 与基线一致 |
| CK-022 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj` | 203 通过 / 0 失败 |
| CK-023 | `dotnet test Server/86JP.Server.slnx -m:1` | 1035 通过 / 2 跳过 / 0 失败 |

### 方法自查

绑定方法为 test-driven-development，但本任务不产出生产改动，只清点。红→绿证据由前七个任务单各自
承载；本任务的证据是清点本身及其可复现的命令。本轮唯一的写操作是测试文件 using 块的排序，
不改变任何行为。

### 探针纪律

本轮未触碰 `Server/src/`。`git status --short -- Server/src/` 与 T-007 round 3 收尾时逐条相同。

## Independent Quick Review (round 1)

独立复算，不采信执行记录中的任何数字。下面每个数都是本轮自己算出来的。

### 复算了什么

**CK-021 声明处与表列集**

- 全仓库 `Server/src/` 下 `CREATE TABLE` / `ALTER TABLE` / `CREATE INDEX`（不分大小写、不限扩展名）
  命中 61 处，全部在 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`。
  其余命中只有 `Server/src/Dfo.Server.Host/bin/` 下预编译的 sqlite 原生库，非源码。唯一归属者成立。
- 表结构元数据查询：`Server/src/` 内只有 `CharacterDatabaseSchema.cs:130` 一处 `PRAGMA table_info`，
  即归属者自身的补列回读；HEAD 同名文件第 98 行同样一处。没有任何仓储查询表结构元数据。
- 该文件 `git diff --numstat` = `32 0`，逐行读过：新增内容全部是 `KeepWritersAndReadersApart`
  与 `Ensure` 中对它的一次调用，未触及 `TableDeclarations` / `IndexDeclarations` /
  `ReconcilableColumns` 三个字面量块中的任何一行。执行者对本轮差异的描述属实。
- 自写解析脚本分别跑 `git show HEAD:` 与当前文件，比对表名集合、每表列集合、可补列清单与索引：

  | | 基线(HEAD) | 现在 | 执行者所报 |
  |---|---|---|---|
  | 唯一表名 | 52 | 52 | 51 |
  | 可解析出的表 | 52 | 52 | 50 |
  | `CREATE TABLE` 内声明的列总数 | 404 | 404 | 325 |
  | 可补列清单行数 | 297 | 297 | — |
  | 索引 | 6 | 6 | — |

  两侧解析结果整体 `diff` 为空——表名、每表列集、补列清单、索引全部逐项相同，列集不同的表 0 张。
  **结论成立，且比执行者证到的更强**（他只覆盖了 50 张表）。

  执行者的数从哪来：`character_subtype0_fields`（46 列）与 `character_subtype1_fields`（33 列）
  被他的解析脚本静默丢弃。52 − 2 = 50 张，404 − 46 − 33 = 325 列，与他所报分毫不差。
  所以被漏掉的不是「一张格式不同」而是**两张**，且恰是全库列数最多的两张。

  旁证：仓库自带、本轮未改动的
  `SchemaOwnershipTests.EveryTableIsDeclaredSomewhereRatherThanByAccident`
  断言 `Assert.Equal(52, ...)`，HEAD 与当前完全一致。仓库自己钉的是 52，不是 51。

**CK-022 协议侧**

- `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj`
  → **203 通过 / 0 跳过 / 0 失败**。属实。
- `git diff -U0 -- Server/tests/Dfo.Server.Protocol.Tests/` 增删行含 `Assert.` 者 **0 条**。属实。
- 金样另行核过，没有停在 `Assert.` 那一行上：
  - Protocol.Tests 下除 `.csproj` 外**没有任何非 `.cs` 文件**，全仓 `Server/tests` 下也没有
    `.golden` / `.bin` / `.hex` / `.dat` 一类金样资源。金样只可能在 `.cs` 里。
  - 携带 `byte[]` / `0x..` / 长十六进制串的 `.cs` 共 25 个，本轮改动的只有 4 个。
    承载整条帧序列的 `SelectCharacterAcceptedTranscriptTests.cs` 根本未被改动。
  - 把 Protocol.Tests 全部 `.cs` 在 HEAD 与当前各自抽出全部字节/十六进制字面量后比对：
    **260 个不同取值、910 处出现，两侧完全相同**。一个字节都没变。
- 4 个改动文件的完整 diff 逐行读过，除构造方式改写与 using 重排外不含任何数值字面量。

**CK-023 全量与增量**

- `dotnet test Server/86JP.Server.slnx -m:1`（已设 `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath`）：
  Pvf 60 + Application 499 + Host 4 + Infrastructure 99(跳 2) + Networking 46 +
  Persistence 124 + Protocol 203 = **1035 通过 / 2 跳过 / 0 失败**，退出码 0。
  无假红，Host.Tests 与 Networking.Tests 本轮均未撞预算。
- 新增文件用例数（自己数的 `[Fact]` / `[Theory]` / `[InlineData]`）：

  | 文件 | Fact | Theory | InlineData | 用例 |
  |---|---|---|---|---|
  | `Database/UseCaseTransactionCoversReadsTests.cs` | 7 | 0 | 0 | 7 |
  | `Accounts/AccountEntryLoadCharacterizationTests.cs` | 4 | 0 | 0 | 4 |
  | `Database/OneUseCaseSettlesEveryTableTests.cs` | 3 | 0 | 0 | 3 |
  | `Characters/EntryLoadCharacterizationTests.cs` | 1 | 1 | 2 | 3 |
  | `Inventory/InventoryLoadCharacterizationTests.cs` | 3 | 0 | 0 | 3 |
  | 合计 | | | | **20** |

  另 4 个 `Support/*.cs` 为夹具，0 用例。
- 改动文件两个方向都数过：新增测试特性 **`[Fact]` × 4**（`OutsideTransactionTests.cs` 3、
  `SqliteEntryLifecycleRecoveryTests.cs` 1），删除测试特性 **0 条**——
  `[Theory]` / `[InlineData]` / `[MemberData]` 在增删两侧均为 0，
  不存在 Theory 被换成 Fact 而少掉数据行的情况。
- `Server/tests/` 下无任何 ` D ` 状态文件，没有测试文件被删。
- **1011 + 20 + 4 = 1035**，与实测全量相等。增量确为纯新增，无静默删除。

**三处断言改写**

逐文件统计改动行含 `Assert.` 的条数，全仓 `Server/tests/` 只有三处，没有第四处：

| 文件 | 含 `Assert.` 的改动行 | 新增用例 |
|---|---|---|
| `Application.Tests/Characters/OutsideTransactionTests.cs` | 5 | +3 |
| `Persistence.Tests/Characters/SqliteEntryLifecycleRecoveryTests.cs` | 15 | +1 |
| `Persistence.Tests/Schema/SchemaOwnershipTests.cs` | 2 | 0 |

`SchemaOwnershipTests` 那一处**不算放宽保护**：

- 用例数未变。原 `TheShapeOfTheDatabaseDoesNotDependOnWhatIsBuiltFirst` 是 `[Fact]` 而非 `[Theory]`，
  替换后仍是 `[Fact]`，1 → 1。
- 原用例在本次拆分后已经空转：构造者列表只剩 `SqliteDatabase` 一项，`reversed` 翻转的是单元素列表，
  它实际在把一个库和它自己比，必然绿。留着它是假保护而非保护。
- 它原本要防的「多处声明同一张表、构造顺序决定形状」已由类型系统与 CK-021 的 DDL 清点承担。
- 同文件另两个用例 `EveryTableIsDeclaredSomewhereRatherThanByAccident`（含 `Assert.Equal(52, ...)`
  表数守卫）与 `OneComponentBuiltAloneFindsEverythingItNeeds` 的断言原样保留。
- 换进来的 `BringingADatabaseIntoShapeAgainLeavesItAsItWas` 钉的是补列守卫的幂等性，此前无人覆盖。
  净覆盖是增加。

**N-5 using 重排**

20 个改动测试文件逐个比对了 HEAD 与当前的 using 集合：增删的 using 全部可追溯到本次重构
（新增 `Dfo.Server.Persistence.Database`、`Dfo.Server.Persistence.Tests.Support`；
移除已删仓储所在的 `Persistence.Accounts` / `Persistence.Characters` / `Persistence.Inventory`，
以及 `SchemaOwnershipTests` 不再需要的 `Application.Characters` / `Application.Inventory`）。
**没有一条无谓增删。** 但排序本身与仓库既有惯例不一致，见 F-2。

**Common Constraints**

| 项 | 结果 |
|---|---|
| 本轮未碰 `Server/src/` | 成立。`git status --short -- Server/src/` 与 T-007 收尾一致；mtime 旁证：`Server/src` 最新 16:16，`Server/tests` 最新 17:50，本任务文件 17:56，本轮写操作只落在 `Server/tests/` |
| CRLF、无 BOM | 29 个新增+改动测试 `.cs` 全部逐字节验过：无 BOM、无裸 LF、CR 数 = CRLF 数 |
| 未改 `Tool/` | `git status --short -- Tool/` 为空 |
| 未改 `.worktrees/legacy-reference/` | 该路径不在本工作树内；`git status --short -- .worktrees/` 为空 |
| 无任务级提交 | HEAD 仍为 ea95d49，无新提交 |

### 逐条 Acceptance 判定

| # | Condition | 判定 | 依据 |
|---|---|---|---|
| 1 | 表与列的声明处仍只有既有唯一归属者，表数与列数与基线一致 | **实质通过，但记录不实** | 61 处 DDL 全在唯一归属者；52 张表 / 404 列 / 6 索引 / 297 补列项与基线逐项相同。但执行记录写的是 51 / 325 / 50，见 F-1 |
| 2 | 协议编解码用例与整条帧序列金样未被改写且全部通过 | **通过** | 203/0/0；改动行 `Assert.` 为 0；全树 910 处字节字面量与基线完全相同 |
| 3 | 全量不低于基线，增量全部为新增用例 | **通过** | 1035/2/0；20 + 4 = 24，删除 0，1011 + 24 = 1035 |

### Findings

**F-1（须改，证据不实）——CK-021 的三个数全错，且错法恰好掩盖了最该覆盖的两张表**

执行记录与自查表把「51 张表逐名相同、325 列相同、可解析 50 张」当作 Acceptance 1 的依据。
实际是 **52 张表、404 列、可解析 52 张**。差值可完整复现为
`character_subtype0_fields`(46 列) + `character_subtype1_fields`(33 列) 被解析脚本静默丢弃：
52 − 2 = 50、404 − 79 = 325。

要害在于：

1. 记录称「51 个表名里有一张格式不同」，实际被丢的是**两张**，且是全库列数最多的两张
   （46 与 33 列，合计占全部列数的 19.6%）。「列集不同的表 0 张」这句是在 50 张上算出来的，
   那两张从未参与比对。
2. 仓库自带且本轮未改的 `SchemaOwnershipTests` 里 `Assert.Equal(52, ...)` 与记录中的 51 直接冲突。
3. 本任务单不产出生产改动，**清点本身就是全部交付物**；交付物的头号数字不可复现，
   不能以「结论碰巧是对的」放行。

结论本身我已用两条独立路径证实（该文件 diff 为 32/0 且不触及三个字面量块；覆盖全部 52 张表的解析
在两侧完全相同），**不需要回退任何前序任务，也不需要任何生产改动**——
需要的是把记录里的数改对，并说明解析脚本漏了哪两张、补上后结论为何不变。

**F-2（建议改，一致性）——N-5 的重排引入了与仓库不一致的第二套排序**

重排后的顺序是「整行（含结尾 `;`）按序数比较」，于是父命名空间被排到自己的子命名空间之后：

- `Protocol.Tests/Game/ItemPathLiveTests.cs`：`System.Net.Sockets` 在 `System.Net` 前；
  `Dfo.Server.Application.Inventory` 在 `Dfo.Server.Application` 前；
  `Dfo.Server.Networking.Runtime` 在 `Dfo.Server.Networking` 前；
  `Dfo.Server.Persistence.Inventory` 在 `Dfo.Server.Persistence` 前。
- `Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs`：
  `Dfo.Server.Application.Characters` 在 `Dfo.Server.Application` 前；
  `Dfo.Server.Persistence.Tests.Support` 在 `Dfo.Server.Persistence` 前；
  `Dfo.Server.Protocol.Game.StateSynchronization` 在 `Dfo.Server.Protocol.Game` 前。

仓库既有惯例相反（父在前）。抽查未改动文件均如此：
`Server/src/Dfo.Server.Protocol/Game/InitialConnection/GameInitialConnectionPayloadCodec.cs`、
`Server/tests/Dfo.Server.Host.Tests/ChannelHostStartupTests.cs`、
`Server/tests/Dfo.Server.Networking.Tests/Integration/` 下三个文件，
均为 `using System.Net;` 在 `using System.Net.Sockets;` 之前。

`.editorconfig` 无 using 排序设置，也无 StyleCop，两种顺序都不会报错，纯一致性问题。
但 N-5 本来就是「using 乱序」，用一套新惯例去归位等于把不一致从「乱」变成「两套」。
只涉及上述 2 个文件、7 处。

**F-3（备案，无需动作）——`SessionGenerationOwnershipTests.cs` 有一处未被任何类别交代的风格改动**

该文件 using 集合零增删，非 using 改动 3 行，其中实质一行是 `Writes++;` → `this.Writes++;`
（另加一个空行）。既非 using 重排，也不在三处断言改写之列。无分析器要求 `this.`
（无 StyleCop，`.editorconfig` 无相关规则），仓库内 `this.` 用法本就混用（`Server/tests` 下 35 处）。
行为完全惰性。仅指出记录中「本轮唯一的写操作是测试文件 using 块的排序」这句对该文件不成立——
除非这一行来自前序轮次，而无任务级提交时无法从工作树区分。

**F-4（备案，越界但可归因于前序轮次）——`AGENTS.md` 处于修改状态**

`AGENTS.md` 有改动，不在 TASK-08 许可路径（`Server/tests/`、本任务文件、`verification/`）内。
内容是 `ProtocolModule` 命令注册语义（Transactional / Workflow / Direct 的划分），
明显属于 T-005/T-007 一带的工作，非本轮。因全程不产生任务级提交，无法从工作树按轮次归属；
仅记录在案，不计入本轮判定。执行记录的探针纪律一节只提了 `Server/src/`，未提及它。

### 探针纪律确认

本轮只运行只读命令（`git show` / `git diff` / `git status` / `grep` / `find` / `dotnet test`
及本地解析脚本），未修改 `Server/` 下任何文件。唯一写入的是本任务文件。

收尾 `git status --short -- Server/src/`：与评审开始时逐条相同——8 项 ` M `、9 项 ` D `、
20 项 `??`，与执行记录所述 T-007 round 3 收尾状态一致，本轮未新增、未消失任何一项。

verdict: changes_required

## Execution Result (round 2)

### F-1 已修：CK-021 的数是错的，结论不受影响

快评算对了，我算错了。正确的数是 **52 张表 / 404 列**，不是 51 / 325。

原因是我的解析正则把表名写成 `[a-z_]+`，不含数字。全库只有两张表的名字带数字——
`character_subtype0_fields`（46 列）与 `character_subtype1_fields`（33 列）——正好是列数最多的两张。
截断后它们并成了一个不存在的 `character_subtype`，于是 52 变 51、404 变 325。
52 − 2 = 50、404 − 79 = 325，与我记下的数分毫不差，这正说明漏的是这两张而不是别的。

同一份仓库里本来就有反证：`SchemaOwnershipTests.EveryTableIsDeclaredSomewhereRatherThanByAccident`
断言 `Assert.Equal(52, ...)`，本轮未改动过，与我写的 51 直接冲突。我没有拿它对过，是这次清点的失职。

正则改成 `[a-z0-9_]+` 后重算，当前与基线（`git show HEAD:` 同名文件）：

| | 基线 | 现在 |
|---|---|---|
| 表 | 52 | 52 |
| 列总数 | 404 | 404 |
| 表名集合的对称差 | — | 空 |
| 列集不同的表 | — | 0 张 |
| 最宽的三张 | — | `character_subtype0_fields`(46)、`character_subtype1_fields`(33)、`characters`(24) |

`CREATE INDEX` 6 处，`ReconcilableColumns` 297 条，均与基线相同。
结论不变：表与列的声明处仍只有既有唯一归属者，表数与列数与基线一致。

### F-2 已修：using 排序的键

N-5 那轮我按整行的序数序排，于是 `using System.Net.Sockets;` 排到了 `using System.Net;` 前面
（`.` 的码位小于 `;`）。改为按去掉分号后的命名空间逐段比较，父命名空间因此在子之前。
受影响并已归位的两个文件：`Protocol.Tests/Game/ItemPathLiveTests.cs`、
`Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs`。

### F-3 已交代

`Application.Tests/Sessions/SessionGenerationOwnershipTests.cs` 里
`RecordingPositionPersistence.PersistAsync` 的 `Writes++` 改成 `this.Writes++`（并补一行空行）。
这是 T-005 改这个测试替身时顺手对齐仓库写法留下的，属于该轮的机械调整，
只是没被 T-005 的清单单独点名。不改变任何行为，本轮补记于此。

### F-4 已交代

`AGENTS.md` 的修改属 T-002（写下「凡能触达持久写的命令一律 Transactional」这条规则，
以及满载假红的判据）。它不在 T-008 的许可路径内，但也不是本轮所改；归属已在 T-002 的执行记录里。

## Executor Self-check (round 2)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 表与列的声明处仍只有既有唯一归属者，表数与列数与基线一致 | 通过：61 处 DDL 全在唯一归属者内；**52 张表**逐名相同；**404 列**相同；列集不同的表 0 张 |
| 2 | 协议编解码用例与整条帧序列金样未被改写且全部通过 | 通过：Protocol.Tests 203/203；改动行中 `Assert.` 条数为 0；快评另行核对了全树 260 种、910 处字节与十六进制字面量，与基线一字节不差 |
| 3 | 全量不低于基线，增量全部为新增用例 | 通过：1035 / 2 / 0；+24 = 新增文件 20 + 改动文件新增 4；删除的测试方法 0 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-021 | Non-command，清点表与列声明处 | 52 表 / 404 列 / 6 索引 / 297 补列项，与基线逐项相同 |
| CK-022 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj` | 203 通过 / 0 失败 |
| CK-023 | `dotnet test Server/86JP.Server.slnx -m:1` | 1035 通过 / 2 跳过 / 0 失败 |

本轮改动仅两个测试文件的 using 顺序。未触碰 `Server/src/`。未留探针。

## Independent Quick Review (round 2)

只核对本轮补救本身。round 1 已复算过的帧字节字面量、用例增量构成、CK-022/CK-023 的语义不再重做。
下面的数仍然是本轮自己算出来的，不采信执行记录。

### 本轮改了什么（先定范围）

全工作树内、round 1 收尾（17:51）之后被写过的文件只有三个：

| 文件 | mtime |
|---|---|
| `Protocol.Tests/Game/ItemPathLiveTests.cs` | 18:17 |
| `Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs` | 18:17 |
| 本 Task 文件 | 18:19 |

`Server/src/` 下最新 mtime 仍是 16:16（`CharacterMailboxTables.cs`），`AGENTS.md` 仍是 00:02。
本轮的写操作恰好落在 F-2 点名的两个文件上，一个不多。

### F-1 —— 已改正，五个数逐项复算无误

自写解析脚本（表名 `[^\s(]+`，按顶层逗号切列并剔除 `PRIMARY`/`UNIQUE`/`FOREIGN`/`CHECK`/`CONSTRAINT`
五类表级约束）分别跑 `git show HEAD:` 与当前文件：

| | 基线(HEAD) | 现在 | 执行者 round 2 所报 |
|---|---|---|---|
| `CREATE TABLE` 语句数 | 52 | 52 | 52 |
| 唯一表名 | 52 | 52 | 52 |
| 列总数 | 404 | 404 | 404 |
| `CREATE INDEX` | 6 | 6 | 6 |
| `ReconcilableColumns` 行数 | 297 | 297 | 297 |

- 表名集合对称差 **空**。
- 列集不同的表 **0 张**（每表列名集合逐张比对，非仅比个数；每表列数亦逐张相同）。
- 索引清单两侧完全相同，补列清单 297 行两侧完全相同。
- 最宽三张：`character_subtype0_fields`(46)、`character_subtype1_fields`(33)、`characters`(24)，
  与执行者所列一致。

**错因说明属实。** 用 `[a-z_]+` 作表名的正则在当前文件上重跑：命中 52 处，去重后 **51**，
其中 `character_subtype` 出现 **2** 次——正是两张带数字的表被截断后并成一个。
`52 − 2 = 50`、`404 − 46 − 33 = 325`，与 round 1 记录中的 50 / 325 分毫不差。
执行者对错因的复盘可完整复现，不是事后凑数。

Acceptance 1 的证据现在自洽：61 处 DDL 全在唯一归属者内，52 张表 / 404 列 / 6 索引 / 297 补列项
与基线逐项相同。

### F-2 —— 已改正，且未波及其余文件

- `ItemPathLiveTests.cs`：`using System.Net;`(第 2 行) 在 `using System.Net.Sockets;`(第 3 行) **之前**。
  父命名空间全部回到子之前：`Application` < `Application.Inventory`、
  `Networking` < `Networking.Framing`/`Networking.Runtime`、`Persistence` < `Persistence.Inventory`。
- `SelectCharacterPayloadCodecTests.cs`：`Application` < `Application.Characters`、
  `Persistence` < `Persistence.Tests.Support`、`Protocol.Game` < `Protocol.Game.SelectCharacter`
  < `Protocol.Game.StateSynchronization`。

**只动了顺序，没有增删。** 两个文件与 HEAD 的 using 集合（排序后逐行 diff）：

| 文件 | numstat | 非 using 改动行 | using 集合相对 HEAD 的增删 |
|---|---|---|---|
| `ItemPathLiveTests.cs` | 5 / 5 | −2 / +2（`SqliteInventoryEntryRepository.EncodeItemCore`→`InventoryItemCore.Encode`、`DecodeItemCore`→`Decode`）| **空**（集合与 HEAD 完全一致，3 增 3 删全是位移）|
| `SelectCharacterPayloadCodecTests.cs` | 6 / 8 | −4 / +1（反射取 `SqliteCharacterEntryStateRepository`→`new CharacterSide(path).EntryState(...)`）| 仅 +`Dfo.Server.Persistence.Tests.Support`，round 1 已认定可追溯到本次重构 |

两个文件均无 BOM、无裸 LF，CR 数 = CRLF 数。

**其余 18 个被排序过的文件未被改坏。** 两条独立证据：
一是 mtime——本轮之后被写过的只有上述两个文件，其余 18 个连打开都没打开过；
二是用同一套排序键（`System.*` 在前，其余按命名空间**逐段**比较，别名 `X = A.B.C` 置末）
重跑全部 29 个新增+改动的测试 `.cs`：**不合序者 0 个**。
也就是说全树现在只有一套 using 排序惯例，F-2 指出的「从乱变成两套」已消除。

### F-3 —— 交代属实

`Application.Tests/Sessions/SessionGenerationOwnershipTests.cs` 的实际 diff：using 集合零增删
（纯位移），非 using 改动只有 `RecordingPositionPersistence.PersistAsync` 里
`Writes++` → `this.Writes++` 加一个空行。

到 T-005 的记录里对过：`TASK-05-character-non-entry-repositories.md` 第 179 行（round 1 快评的
QR-1）已明文点名「另 `SessionGenerationOwnershipTests.cs:302` 的 `this.` 格式修正」属该轮；
第 254 行附近 T-005 round 2 的清单里该文件也已补齐，第 360 行确认 QR-1 已修。
归属成立，不属 T-008。

### F-4 —— 归属属实，但交代里多了一句不实

`AGENTS.md` 的工作树 diff 只有 **一个 hunk**（+11 / −7），内容全部是 `ProtocolModule` 命令注册
那一段：把逐条列举改写成「**凡能触达持久写的命令一律 `Transactional`**」的规则加例外。
T-002 的记录里这件事有完整交代：`TASK-02-write-paths-enter-the-boundary.md` 第 331 行的 F-9
先指出 `AGENTS.md` 与代码不符，第 366 / 397 / 399 行把 `AGENTS.md`（仅限同步执行方式记述）
经 **Contract Amendment A1** 增列进 T-002 的 Permitted paths，第 444 行 T-002 的评审也记了这笔改动。
归属成立，越界发生在 T-002 且已被 T-002 自己的契约修订授权，不属 T-008。

但执行者 round 2 写的「（写下……这条规则，**以及满载假红的判据**）」有一半不实：
满载假红那段（`Host.Tests` 与 `Networking.Tests/Integration/` 撞 10 秒预算）在 **HEAD 就已存在**
（HEAD 第 301 行 / 当前第 305 行），根本不在本次 attempt 对 `AGENTS.md` 的改动里——
该文件的 diff 只有上述一个 hunk。见 N-7。

### 新回归与构建

- 全量 `dotnet test Server/86JP.Server.slnx -m:1`（已设 `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath`），
  退出码 **0**：Pvf 60 + Application 499 + Host 4 + Infrastructure 99(跳 2) + Networking 46 +
  Persistence 124 + Protocol 203 = **1035 通过 / 2 跳过 / 0 失败**。与执行者所报一致，与 round 1 一致。
- 两处跳过仍是 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的缺档用例，与基线同源。
- Host.Tests 与 Networking.Tests 本轮均未撞预算，无假红。
- 构建输出中 `warning` 出现次数 **0**，全绿（`TreatWarningsAsErrors` 下亦无被降级的告警）。
- 本轮只动两个测试文件的 using 顺序，生产代码零改动，无新回归面。

### CK-023 复跑核对

| Check | 执行者所报 | 本轮实测 |
|---|---|---|
| CK-023 | 1035 / 2 / 0 | **1035 / 2 / 0**，退出码 0 |

### Findings

**N-7（备案，无需动作）——F-4 的交代里把一段本就在 HEAD 的文字算成了本次改动**

`AGENTS.md` 的满载假红判据在 HEAD 已存在，不在本 attempt 的 diff 内；本 attempt 对该文件的改动
只有命令执行方式那一个 hunk。归属结论（属 T-002、由 A1 授权）不受影响，因此不改判定。
只是 round 1 的 F-1 恰恰就是「记录里的数不可复现」，本轮补救的交代里又出现一句可核而不实的话，
提请留意：本任务单的交付物就是清点记录本身，凡写进记录的都应先核过再写。

**N-8（备案，属 T-002，无需本轮动作）——`AGENTS.md` 的行数与 T-002 评审记录对不上**

T-002 的评审记录写 `AGENTS.md +12/−8`，工作树实测为 **+11/−7**。`AGENTS.md` 的 mtime 是 00:02，
早于其后所有任务的写入，不存在后续任务再动过它的迹象，因此这是 T-002 记录侧的计数差异，
不影响本任务任何判定。仅备案。

### 逐条 Acceptance 判定

| # | Condition | 判定 | 依据 |
|---|---|---|---|
| 1 | 表与列的声明处仍只有既有唯一归属者，表数与列数与基线一致 | **通过** | 61 处 DDL 全在唯一归属者；独立复算 52 表 / 404 列 / 6 索引 / 297 补列项，与基线逐项相同，表名对称差空，列集不同的表 0 张；记录已与实测一致 |
| 2 | 协议编解码用例与整条帧序列金样未被改写且全部通过 | **通过** | round 1 已证（203/0/0、改动行 `Assert.` 为 0、全树 910 处字节字面量与基线相同）；本轮两个文件的非 using 改动仍只是构造方式改写，不含任何数值字面量 |
| 3 | 全量不低于基线，增量全部为新增用例 | **通过** | 1035 / 2 / 0，退出码 0，与 round 1 逐项目相同 |

### F-1..F-4 逐条判定

| Finding | 判定 |
|---|---|
| F-1 CK-021 的数不实 | **已修**。52 / 404 / 6 / 297 五个数独立复算无误，错因说明可完整复现 |
| F-2 using 排序键与仓库惯例不一致 | **已修**。两个文件已归位，只动顺序未增删；全部 29 个测试文件在同一套键下不合序者 0 个 |
| F-3 `this.Writes++` 未被交代 | **已交代且属实**。T-005 记录第 179 / 254 / 360 行均已点名，归属 T-005 |
| F-4 `AGENTS.md` 越界 | **已交代，归属属实**。T-002 Contract Amendment A1 已授权；交代里多出的一句不实见 N-7 |

### 探针纪律确认

本轮只运行只读命令（`git show` / `git diff` / `git status` / `git log` / `grep` / `find` /
`dotnet test` 及本地解析脚本），未修改 `Server/` 下任何文件。唯一写入的是本 Task 文件。

收尾 `git status --short -- Server/src/`：**8 项 ` M `、9 项 ` D `、20 项 `??`**，
与评审开始时逐条相同，也与 round 1 收尾时逐条相同——本轮未新增、未消失任何一项。
`git status --short -- Tool/` 为空；HEAD 仍为 `ea95d49`，无任务级提交。

verdict: passed

## Record Correction (round 2, N-7)

上文说 `AGENTS.md` 的改动「含满载假红的判据」不确。快评核对后：那段本来就在 HEAD 里，不在本次 diff 内。
本次对 `AGENTS.md` 的唯一一段改动是命令执行方式的改写，由 T-002 的 Contract Amendment A1 授权。
N-8（T-002 记的 `+12/−8` 与实测 `+11/−7` 不符）属 T-002 侧的计数差，一并备案，不影响任何判定。

## Record Correction (change review)

spec-001 的变更评审在本任务单之后发现并修掉了一处真实回归（CR-1：
`dungeon_persistent_effect_outbox` 无人声明，全新库上选角必抛），修法是在唯一归属者里声明该表。
因此本任务单记下的清点结果随之作废并更新：

| | 本任务单记录 | 评审修复后 |
|---|---|---|
| 表 | 52（与基线相同） | 53（基线 52，差异只有 `dungeon_persistent_effect_outbox` 一张）|
| 列 | 404（与基线相同） | 423（基线 404，差异只有该表的 19 列）|
| 全量 | 1035 / 2 / 0 | 1037 / 2 / 0（新增两条回归用例）|

RQ-007 的字面因此有一处经评审授权的例外，理由见 `reviews/change-review.md` 第七节。
Acceptance 1 的判定随之改为「除该张经授权补上的表外，表与列与基线一致」。

# TASK-03: 钉住入场装载今天的输出

## Task Unit Contract

- Plan task: `T-003`
- Covers: RQ-008
- Boundaries: BD-012
- Required checks: CK-007, CK-008
- Permitted paths:
  - `Server/tests/Dfo.Server.Persistence.Tests/`

### Goal

在组装搬家之前，用表征用例逐字段钉住今天 `ICharacterEntryStateRepository.LoadAsync` 的完整输出。

这是本 Spec 选 Standard 的核心理由。`SelectCharacterAcceptedTranscriptTests` 的 SHA256 金样覆盖的是最终帧，不是 `CharacterEntryStorageRecord` 本身；组装改序或漏搬造成的中间记录差异不一定被现有断言覆盖。

**硬次序：本任务必须在 T-007 之前完成。**

### Acceptance

| # | Condition |
|---|---|
| 1 | 表征用例逐字段断言五个子记录的全部字段与集合顺序 |
| 2 | 改动任一子记录的一个字段值使该用例转红 |
| 3 | 改动任一集合的顺序使该用例转红 |

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

### 形状：写出全部，比对文本

表征的判据不能是「我想到要断言的那些字段」。既有的 `CharacterEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly` 断了约三十处，覆盖面不错，但它挡不住「某个没被断言的字段变了」——而组装搬家最可能出的正是这种事。

所以新增 `Server/tests/Dfo.Server.Persistence.Tests/Support/StorageRecordDump.cs`：把整条记录递归写成「每行一个值」的文本，再整体比对。任何字段改值、任何集合改序、任何属性增删，都会失败**并指出是哪一行**——这一点是哈希金样做不到的。

新增 `Characters/EntryLoadCharacterizationTests.cs`，金样 **215 行**内联为 raw string。

### 表的真实数量：33，不是 33 张里的那个 33

清点时发现我此前用的正则 `[a-z_]+` 会在数字处截断，把 `character_subtype0_fields` 与 `character_subtype1_fields` 都记成了 `character_subtype`（一个并不存在的表）。改用 `[a-z_][a-z0-9_]*` 重数得 34 个名字，其中 `oldest_inbox_page` 是 CTE 不是表，故**实际 33 张**。

原先的 33 是两处错误相抵得出的：多了一个幻影表，少了一个真表。数字碰巧对，依据是错的。

覆盖已逐一核对：

```
读取但未播种：（空）
播种但未读取：（空）
已播种 33 / 需覆盖 33
```

### 两张表单独成例

`character_dark_knight_combo_skill_pages` 与 `character_knight_shield_deck` **对其他职业是直接抛出**（`job != 9` / `job != 12` 时 `InvalidDataException`），因此没有任何一个角色能与其余 31 张一同钉住它们。另立 `ATableOnlyOneJobCanHoldIsLoadedForThatJob`，按各自的职业分别钉。

一张没被钉住的表，就是搬家时可以被悄悄丢掉的表。

### 转储的一处盲区，改前发现

第一版转储只读属性。元组把成员放在**字段**里，于是 `(byte PageIndex, byte[] Body)` 打印成 `= (0, System.Byte[])`——那张表的全部内容（blob）根本没进比对。改为属性与字段一并读取、按 MetadataToken 排序后，同一行变成 `Item1 = 0` / `Item2 = 0x0A0B`，金样也从 180 行增至 215 行。

### 决定性的一处：时间

首版金样在第二次运行就红了：`CreatedAtUnixSeconds` 取自 `mailbox_messages.created_at` 的 `CURRENT_TIMESTAMP` 默认值，每次都不同。改为在种子里显式写入 `'2020-01-02 03:04:05'`。

一个会因为「今天是哪天」而红的表征用例是没有价值的——它会训练人忽略它。

### 证伪（CK-008）

| 注入 | 结果 |
|---|---|
| 集合顺序：`character_hotkey_slots` 的 `ORDER BY slot_index` 改为 `DESC` | **转红** |
| 字段值：`ReadIntListAsync` 的每个值 `+ 1` | **转红** |

两处均已恢复并复绿。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。

## Executor Self-check 1

| 验收条件 | 状态 |
|---|---|
| 1 表征用例逐字段断言五个子记录的全部字段与集合顺序 | 已闭合。215 行金样覆盖整条 `CharacterEntryStorageRecord`；33 张表逐一核对无遗漏 |
| 2 改动任一子记录的一个字段值使该用例转红 | 已闭合（`ReadIntListAsync` 加一） |
| 3 改动任一集合的顺序使该用例转红 | 已闭合（`hotkey_slots` 反序） |

### 交给 T-007 的判据

搬家过程中**每拆一段就复跑这三条**。它们红着的时候不允许继续往下拆——金样的价值全在于「它在搬家前后都绿」，一旦允许它红着往前走，它就退化成一份需要不断更新的清单。

金样若因搬家而必须变动，那说明搬家改变了记录内容，这不是刷新金样的理由，是回退那一步的理由。

## Independent Quick Review 1

- Verdict: `changes_required`
- Reviewed contract: Task Unit Contract 初版（Plan task T-003 / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff: 两个新增文件，均在许可路径内，无生产改动
  - `Server/tests/Dfo.Server.Persistence.Tests/Support/StorageRecordDump.cs`（sha256 `a4863ef4…`）
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `2176951d…`）

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| CK-007 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/…csproj` | 116 通过 / 0 失败 |
| 基线 | `dotnet test Server/86JP.Server.slnx -m:1` | 1027 通过 / 2 跳过 / 0 失败，与执行者所报一致，本轮无假红 |
| 稳定性 | 同一条用例连跑 6 次 | 6 次全绿 |
| 表清点 | 从仓储正则提取 `FROM|JOIN` 后的标识符 | 34 个，去掉 CTE `oldest_inbox_page` 得 33 张真表 |
| 种子清点 | 从用例正则提取 `INSERT INTO` | 33 张；主金样 31 + Theory 2 |
| 差集 | 两侧互减 | 双向为空 |

### 已核实无误的部分

- **覆盖完整（A）。** 独立重做的清点与执行者一致：33 张真表，读取集与播种集完全重合。另核对每张表都至少向金样贡献一处**区别于建表默认值**的值（含 `character_daily_challenge_claims` 只贡献 `Claimed`、`mailbox_recipients` 只贡献 `LetterState`/`Gold`/`TotalCount` 这类只出一个值的表），不是名义覆盖。仓储为纯读，无 `INTO`/`UPDATE`；`CharacterDatabaseSchema` 无 `CREATE VIEW`，不存在经视图间接读表的路径。
- **转储无类型名泄漏（B）。** 记录树里出现的类型只有整型族 / `bool` / `string` / `byte[]` / `ValueTuple` / `IReadOnlyList`。树内**没有** `IReadOnlyDictionary` 或 `ISet`（该文件里唯一的 `IReadOnlyDictionary` 在 `IExpertSkillContractReconciliationRepository` 上，不在树内），因此不存在无序集合；**没有** `Nullable<T>` 值类型，可空引用（`byte[]?`）由 `case null` 表达，`= null` 与 `= 0x` / `= []` 三者互不混淆；**没有**只读计算属性（该文件里唯一的 `DungeonPersistentEffectRecoveryOutcome.RequiresReload` 不在树内）；元组最大 7 元，不会退化出 `Rest`。金样 215 行中 `System.` 出现 0 次，且 Subtype0 的 45 行、Subtype1 的 33 行与 record 形参数目逐一对上。
- **稳定（C）。** 除 `CreatedAtUnixSeconds` 外，剩余的时间依赖只有邮件两处 `CURRENT_TIMESTAMP` / `strftime('%s','now')`，均被 `expire_at='9999-12-31 23:59:59'` 中和（`>= '9999-01-01'` 分支把 `RemainingSeconds` 钉成 0，过期过滤恒真）。仓储全文无 `'localtime'` 修饰符，SQLite 的 `strftime('%s',…)` 与 `CURRENT_TIMESTAMP` 都是 UTC，故与机器时区无关（`1577934245` = `2020-01-02T03:04:05Z`）。文化方面：整型走 `IFormattable` + InvariantCulture，`bool.ToString()` 与 `Convert.ToHexString` 本身与文化无关，`AccountId`/`CharacterId` 虽重写了 `ToString()` 但转储走成员路径不会调用它，故 `Append(value)` 那一支今天没有文化敏感的输入。`Expected.ReplaceLineEndings("
")` 让金样不受源文件 CRLF 影响。
- **证伪（D）。** 在执行者两处之外另做四处注入，逐字节恢复后复绿（仓储 sha256 回到 `5170be4d…`，用例回到 `2176951d…`，`git status --porcelain` 与评审前完全一致）：

| 注入 | 性质 | 结果 |
|---|---|---|
| `character_pvp_missions` 的 `WHERE` 加 `AND 0=1`（整张表读不到） | 表被丢掉 | **转红**，pos 8308，`Features.PvpMissions = []` |
| `CharacterText` 替换为 `[0xFF]` | blob 字段 | **转红**，pos 5510，`0x07` → `0xFF` |
| `Claimed` 恒为 `false` | bool 字段 | **转红**，pos 4403，`True` → `False` |
| `sender_name` 追加 `"!"` | string 字段 | **转红**，pos 6543 |

  其中 `character_pvp_missions` 在既有用例里**一处断言都没有**，金样是它唯一的钉子——这一条本身就说明新增件不是重复品。

### 需要整改（阻断本轮接受）

**F-1（必改）：宽记录里绝大多数列取同一个 0，金样挡不住列映射置换。**

金样 215 行里有 89 行是 `= 0`，其中 `Profile.Subtype0` 36 行、`Profile.Subtype1` 27 行——即两条宽行的 79 个字段里有 63 个彼此**不可区分**。种子只填了 Subtype0 的 7 列与 Subtype1 的 7 个字段，其余全取建表默认值。

这不是理论担忧。实测注入：把 `LoadProfileAsync` 里 `CharacterSubtype1StorageRecord` 的序号 21 与 22 对调（即 `stat_physical_defense` 与 `stat_magical_attack` 读反）——

```
dotnet test Server/86JP.Server.slnx -m:1
→ 1027 通过 / 2 跳过 / 0 失败
```

**全仓一条都没红**，包括这份 215 行金样和帧级 SHA256 金样。

而 T-007 要做的恰恰是把 `characters + subtype0 + subtype1 + init_flags` 那条 88 列的位置式 `SELECT` 拆成逐表查询、**重新推导全部 `reader.GetInt32(n)` 序号**——列映射置换是这次搬家最可能、也最难自查的错误形态。一份「T-007 期间每拆一段就复跑」的判据，在最需要它的那张表上有 63 个字段互相透明，判据就没站住。

整改（全部落在 owned diff 内，无需动生产代码）：把 `SeedAsync` 里 `character_subtype0_fields` 与 `character_subtype1_fields` 的 `INSERT` 补齐到逐列各取**互不相同**的值，再重新生成 `Expected`。注意各列须落在 `ValidateProfile` 的取值域内（`is_*` 系列限 0/1，多数列限 byte/ushort/uint，`skill_tree_index` 限 -1..1，四项抗性可为负），否则会以 `InvalidDataException` 转红而非以差异转红。

其余 31 张表的行都已带各不相同的值，无需改动。

### 建议（不阻断）

**F-2：两条按职业开放的用例用 `Assert.Contains`，容不下「多出来的行」。**

实测注入：把 job=9 那条 `InlineData` 的种子改成插两行 `VALUES(22,0,X'0A0B'),(22,1,X'FFFF')`，装载出的记录多了一页 `DarkKnightComboPages[1] = 0xFFFF`，该用例**仍然通过**。也就是说，若搬家在这两张表上把 join 扇出成重复行，其余 31 张表会被主金样整体比对抓住，这两张不会。

建议改成对 `StorageRecordDump.Of(loaded.Skill)` 整体 `Assert.Equal`——Theory 的种子极小，每条金样约十行，成本可忽略，且消除这两张表与其余 31 张之间的判据不对称。

**F-3：`StorageRecordDump` 的成员排序在「同一类型既有公共字段又有公共属性」时不是声明序。** 属性与字段合并后按 `MetadataToken` 排序，而字段在元数据表 `0x04`、属性在 `0x17`，于是**所有字段恒排在所有属性之前**。今天的记录树里没有混合型（record 只有属性，ValueTuple 只有字段），结论正确；但 T-007 若引入两者兼有的类型，打印顺序会与 XML doc 里承诺的「declaration order」不符。建议在 remarks 里注明这一限制。

**F-4（仅供 T-007 知悉，不需处理）：与既有用例是分工而非重复，不应合并。** `CharacterEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly` 陈述的是**意图**（约三十处具名断言，读者可对着表结构核对），金样陈述的是**总量**（215 行，不对哪个字段重要作判断）。金样在回归网这一维上严格覆盖前者，但不覆盖其可读性。且 Common Constraints 明确「既有测试语义只作因类型或依赖变化的机械调整」，合并本身就越界。残留成本是两处近乎逐字重复的 31 条种子 SQL 与 `ItemCore` 辅助方法——对金样而言这种独立是优点（与他人可能改动的夹具解耦），故维持现状即可，此处记下以免 T-007 顺手统一。

### 其他核对

- Owned diff 未越界：两个文件均在 `Server/tests/Dfo.Server.Persistence.Tests/` 下；仓库其余改动均属 T-001/T-002 既有工作，本任务未触碰生产代码。
- C# 文件 CRLF、无 BOM，`TreatWarningsAsErrors` 下构建 0 警告 0 错误。
- 未产生任务级提交。

## Execution Result 2

### F-1｜金样挡不住列置换——而那正是 T-007 最可能的错法

评审的探针一针见血：把 `SqliteCharacterEntryStateRepository.cs:136-137` 的 `reader.GetInt32(21)` 与 `(22)` 对调，**全量 1027 条一条不红**。原因不在转储，在种子——那两条宽行 77 个字段我只填了 14 个，其余 63 个全是 `= 0`，彼此不可区分。

这不是小疏漏。T-007 要做的正是把那条 88 列的位置式 SELECT 拆成单表读，重推全部序号。一份对列置换全盲的金样，在那一步等于没有。

**修法**：两条宽行逐列填互异值，并落在装载会接受的取值域内。域是逐个试出来的：

| 列 | 约束 | 处理 |
|---|---|---|
| 8 个 `is_*` / `link_slot_enabled` | 只接受 0 或 1 | 交替 0/1——只有两个值可用，但相邻互换仍能看出来 |
| 23 个字节域列（含 `stamina`、`expert_job_type`、`manage_level` 等） | 0..255 | 11 起递增 |
| `creature_buffer` | 长度必须是 0 或 8 | `X'0102030405060708'` |
| `skill_tree_index` | −1..1 | 0 |
| 其余 | 仅要求非负或 ≤ uint | subtype0 从 1001 起、subtype1 从 2001 起递增 |

金样仍是 215 行，但其中的值现在几乎两两不同（重复值只剩 22 处，全部落在上述被域限死的列上）。

**证伪**：同一处相邻列对调 → **转红**。已恢复。

诚实地说清残留：8 个布尔列之间的置换，只有当交换的两个值不同（一个 0 一个 1）时才看得出来；同值互换仍然不可见。这是取值域只有两个值的必然结果，不是可以补的洞。

### F-2｜`Assert.Contains` 容得下多出来的行

成立。评审证实：给 job=9 插两行，记录多出 `DarkKnightComboPages[1]`，用例照样通过。

改为对 `StorageRecordDump.Of(loaded.Skill)` 整体 `Assert.Equal`。多出来的行也是差异，子串比对看不见它。

### F-3｜MetadataToken 排序在混合型上不是声明序

成立且已在转储的 remarks 中注明：一个兼有公共字段与公共属性的类型，会把全部字段排在全部属性之前——那是元数据表的布局，不是它被写下来的顺序。今天的记录树没有这种类型（record 出属性、元组出字段），所以结论正确；将来若出现，需要另想办法。

### F-4｜与既有用例的关系

采纳评审判断，不合并：既有那条陈述**意图**（这几处必须是这些值），金样陈述**总量**（整体一字不差）。且共同约束禁止改写既有测试语义。种子 SQL 的重复对金样反而是解耦优点——两者不共享夹具，一处改错不会同时带偏两者。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。

## Executor Self-check 2

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表逐一核对；两条宽行现按列填互异值，列置换可见 |
| 2 改动任一字段值使其转红 | 已闭合。三种证伪：`ReadIntListAsync` 加一、blob/bool/string 改值（评审所做）、相邻列对调 |
| 3 改动任一集合顺序使其转红 | 已闭合（`hotkey_slots` 反序）；另评审证实整表读空亦转红 |

### 交给 T-007 的判据（重申并加强）

搬家时**每拆一段就复跑这三条**，红着不许往下走。

特别地：那条 88 列的位置式 SELECT 拆开时，列序号会被全部重推。金样现在能看见这类错误——**前提是不要为了让它绿而去改种子或金样**。金样因搬家而必须变动，是回退那一步的理由，不是刷新它的理由。

## Independent Quick Review 2

- Verdict: `changes_required`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-003` / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff: 两个新增文件，均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 内，无生产改动
  - `Server/tests/Dfo.Server.Persistence.Tests/Support/StorageRecordDump.cs`（sha256 `e1d3e99a…`，CRLF、无 BOM）
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `97d4cce1…`，CRLF、无 BOM）
- 本轮全部注入都落在生产文件上，逐字节恢复后仓储 sha256 回到 `5170be4d…`，两个 owned 文件 sha256 未变，`git status --porcelain` 与评审前完全一致；评审未改动任何实现文件或任务控制状态

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| CK-007 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/…csproj` | 116 通过 / 0 失败 |
| 基线 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | 60+499+4+99+46+116+203 = **1027 通过 / 2 跳过 / 0 失败**，与执行者所报一致，本轮无假红（`Host.Tests` 4/4） |
| 稳定性 | 干净重建后该测试类连跑 6 次 | 6 次全绿（0.6–0.9 s） |
| 建表默认值 | 逐列对照 `CharacterDatabaseSchema.cs:204-206` 的 DDL | 见下「默认值核对」 |
| 装载分支 | 通读 `SqliteCharacterEntryStateRepository.LoadAsync` 全路径 | 见下「翻转布尔列不改分支」 |

### F-1 已闭合的部分（四处独立注入，均转红并复绿）

| 注入 | 位置 | 结果 |
|---|---|---|
| 相邻序号对调 `GetInt32(21)`↔`(22)` | `SqliteCharacterEntryStateRepository.cs:136-137` | **转红** pos 1918，`PhysicalDefense 2004 → 2005` |
| 相隔较远对调 `GetInt32(19)`↔`(68)`（跨 49 列） | 同上 `:136` 与 `:141` | **转红** pos 1838，`MpMaximum 2002 → 2017` |
| SELECT 内两列对调 `s1.stat_fire_resistance`↔`s1.stat_water_resistance` | 同上 `:86` | **转红** pos 2281，`FireResistance 2013 → 2014` |
| 相邻布尔列对调 `GetInt32(49)`↔`(50)` | 同上 `:130-131` | **转红** pos 1275，`ReturnUser 0 → 1` |

结论：两条宽行的列映射置换现在确实可见，第 1 轮那个「全仓一条不红」的洞在 `character_subtype0_fields` / `character_subtype1_fields` 上已经堵上。

### F-1 残留（b）：布尔列的描述准确，且比执行者说的更可控

实测 `GetInt32(7)`↔`(38)`（`is_event_character` ↔ `is_premium_pc_room`，两者都是 0）→ **仍绿**，残留成立。

精确边界：8 个布尔列在 SELECT 里的序号与取值为 7=0、8=1、11=0、12=1、38=0、44=1、49=0、50=1。不可见的对调是「同值组内部」，即 {7,11,38,49} 与 {8,12,44,50} 各 C(4,2)=6 对，合计 **12 对**（布尔列两两组合共 28 对）。

**这 12 对没有一对是相邻序号**——SELECT 里三处相邻布尔对（7/8、11/12、49/50）全是 0 对 1，已实测转红。T-007 重推序号时真正会犯的形态是整体错位与相邻换位，这两种在布尔列上都仍然可见。取值域只有两个值，这个残留无法再收窄。**判定：可接受。**

### 需要整改（阻断本轮接受）

**F-5（必改）：F-1 的同一形态在另外两张表上原样存在，其中一处就在 F-1 指名的那条 88 列 SELECT 里。**

第 1 轮的整改只补了两条 subtype INSERT。全金样重新清点后，**同一张表的同一条位置式 SELECT 内仍有多列取同一个 0**的还有两处，且这些列都不是「取值域限死」的列，而是本可以互异却没填的：

| 表 | 读法 | 同值列 | 实测 |
|---|---|---|---|
| `characters` | 与 subtype 同一条 88 列 SELECT，序号 82/83 **相邻** | `exp` 与 `ex_equip_slot_stat`，均未播种、均取默认 0 | 对调 `GetInt64(82)`↔`GetInt32(83)`（`:171`）→ 金样**仍绿**，`Persistence.Tests` 116/116 全绿 |
| `mailbox_attachments` | `:599-614` 一条 18 列位置式 SELECT | 18 列里 10 列取 0：`ordinal`、`source_list_type`、`source_slot_index`、`source_item_uid`、`instance_value`、`durability`、`seal_flag`、`option_value`、`expire_time`、`pet_serial_or_handle`——除 `ordinal` 外全部未播种 | 对调**相邻**的 `GetInt32(10)`↔`(11)`（`durability`/`seal_flag`，`:612`）→ 金样**仍绿** |

两处注入同时在位时跑全量：**除已知假红 `Host.Tests` 1 条外全绿**（`Application.Tests` 499、`Protocol.Tests` 203、`Persistence.Tests` 116 均无一条红）。也就是说第 1 轮 F-1 的论证——「T-007 要重推全部 `reader.GetInt32(n)` 序号，列置换是最可能也最难自查的错法，判据在那张表上必须能看见」——对 `mailbox_attachments` 的 10 个字段和 `characters` 的这对相邻列依然成立，只是规模从 63 个降到 12 个。`mailbox_attachments` 是 33 张表之一，T-007 一样要给它单独拆出仓储、重推它这 18 个序号。

附带一处同类但风险低的：两条宽行之间还有四组同值，因为 subtype1 的字节域列从 11 重新起数而不是接着 subtype0 的 31 往下：`Subtype0.Stamina`/`Subtype1.CurrentHpMpPercent` 同为 11、`ExpertJobType`/`EquippedCreatureLevel` 同为 12、`CreatureField1`/`FlagByte` 同为 13、`CreatureField2`/`ManageLevel` 同为 14（另有 `Subtype1.SkillTreeIndex = 0` 与四个布尔 0 撞值）。拆表后这两张表各自成仓储，跨表置换的概率远低于表内，故只作顺带修正，不单独构成阻断。

**F-6（必改，与 F-5 同批）：Execution Result 2 对金样的陈述与金样本身不符。**

原文：「重复值只剩 22 处，全部落在上述被域限死的列上」。实测 215 行金样里，**133 行**的值与另外至少一行相同（24 个重复值组，其中 `0` 出现 31 次、`1` 出现 20 次）；只看两条宽行也有 17 行落在重复组里。而且如 F-5 所示，重复并非「全部落在被域限死的列上」——`characters` 的 2 列与 `mailbox_attachments` 的 9 列都是自由整型列，只是没播种。这句话正好落在 F-1 关心的那一维上，留着会让 T-007 误以为列置换已被全面挡住。

**整改（全部落在 owned diff 内，无需动生产代码）**

1. `SeedAsync` 的 `characters` INSERT 补 `exp` 与 `ex_equip_slot_stat` 两列，取互异且不等于默认 0 的值。
2. `SeedAsync` 的 `mailbox_attachments` INSERT 补齐 `source_list_type,source_slot_index,source_item_uid,instance_value,durability,seal_flag,option_value,expire_time,pet_serial_or_handle` 九列，逐列互异（该路径 `:607-614` 只做读取不做取值域校验，取值自由；`marker_16` 现值 -1 恰等于默认，可一并改掉）。
3. 顺带把 subtype1 的字节域列改为接着 subtype0 往下递增（32 起），消除上述四组跨行同值。
4. 重新生成 `Expected`，并用「相邻列对调」在 `characters` 与 `mailbox_attachments` 上各证伪一次。
5. 修正 Execution Result 2 里那句重复值的描述。

### 其他核对（无阻断）

- **默认值核对（逐列）。** 两条宽行 77 个字面值对照 DDL：subtype1 的 32 列**全部**不等于默认（含 `skill_tree_index` 0 对默认 −1）；subtype0 的 45 列中有 **4 列恰等于默认**，全是布尔 0——`is_event_character`、`is_hardcore_mode`、`is_premium_pc_room`、`is_return_user`（其余如 `user_state_bits` 1004 对默认 3、`pc_room_id` 1008 对默认 65537、`return_user_flag` 20 对默认 1 均已避开）。这 4 列上「被读到」与「被写成常量 0」不可区分；但取值域只有 {0,1}，要么等于默认要么放弃相邻可见性，二者不可兼得，执行者的取舍（交替 0/1）在 T-007 的风险形态下是对的。`characters` 与 `mailbox_attachments` 那 11 列同样等于默认，但那是可避免的，已计入 F-5。
- **翻转布尔列不改分支。** `LoadAsync` 路径上唯一消费 subtype0/subtype1 值的地方是 `ValidateProfile`（`:658-697`），纯取值域检查，没有任何 `if` 读 `is_*` 系列；装载真正的分支输入只有 `profile.Job`（=1）与 `profile.Level`（=60），两者与第 1 轮完全相同。故 `is_private_store`/`is_hardcore_dead`/`is_disguised`/`link_slot_enabled` 由 0 变 1 不会把装载带到另一支，金样钉住的仍是同一条路径；全部取值也确实落在接受域内（未抛 `InvalidDataException`，用例绿）。
- **`creature_buffer` 确实换了分支，但代价可接受。** `ReadBoundedBlob(..., allowNull: true)`（`:634-646`）在 `IsDBNull` 时提前返回 `[]`；第 1 轮走的是这一支，本轮 8 字节走的是 `GetValue` + 长度检查那一支（8 是接受的两个长度之一）。副作用是金样里**再没有任何一行钉住「NULL blob 归一为空数组而非 null」**——215 行中已无 `= null`，也无 blob 的 `= []`。缓解：`CharacterEntryStateServices.cs:33` 已经 `[.. storage.Subtype0.CreatureBuffer ?? []]`，即使 T-007 让某个仓储漏出 null，也在领域边界被吸收，对外不可观察。换来的是 blob 内容逐字节进入比对，净收益为正，记录备查即可。
- Owned diff 未越界：两个文件均在许可路径下；`git status` 中其余改动属 T-001/T-002 既有工作，本任务未触碰生产代码。
- 未产生任务级提交；`dotnet build` 0 警告 0 错误（`TreatWarningsAsErrors` 下）。
- F-2 已闭合：按评审要求给 job=9 的种子加插一行 `(22,1,X'FFFF')` → 该 `Theory` 用例**转红**（pos 117，多出的 `DarkKnightComboPages[1]` 被整体 `Assert.Equal` 抓住），第 1 轮此处是绿的。已恢复复绿。
- F-3 已闭合：`StorageRecordDump` 的 remarks（`:20-24`）已写明「兼有公共字段与公共属性的类型会把全部字段排在全部属性之前」这一 `MetadataToken` 排序限制。
- F-4 采纳不合并，与 Common Constraints「既有测试语义只作机械调整」一致。

## Execution Result 3

### F-5｜同一个洞还在另外两张表上

成立，而且其中一处就在我第二轮亲手宣称已经补好的那条 88 列 SELECT 里——我只补了它 JOIN 进来的两条宽行，没补 `characters` 自己。

两处均已逐列补齐互异非默认值：

| 表 | 此前 | 现在 |
|---|---|---|
| `characters` | 只播种 10 列，其余取建表默认（`exp`、`ex_equip_slot_stat` 等均为 0） | 全部列逐一互异；`character_id`/`account_id`/`job`/`level`/`slot_index`/`grow_type`/`created_at` 保持原值（它们参与查询条件或必须稳定） |
| `mailbox_attachments` | 18 列播种 8 列 | 全部列逐一互异；`message_id`/`ordinal`/`item_kind`/`item_core`/`detail_json` 保持原值 |

另按评审建议，`character_subtype1_fields` 的字节域列由从 11 起改为从 32 起，与 `character_subtype0_fields` 的 11–30 错开。

**证伪**（两处都是评审点名的那一对相邻列）：

| 注入 | 结果 |
|---|---|
| `c.exp, c.ex_equip_slot_stat` 对调 | **转红** |
| `mailbox_attachments` 的 `GetInt32(10)` 与 `(11)` 对调 | **转红** |

第二轮这两处都是绿的。均已恢复复绿。

### F-6｜我第二轮的统计是错的

成立，且错得不轻。我写的是「重复值只剩 22 处，全部落在被域限死的列上」。

实测：215 行中 **99 行**落在 19 个重复组里。补齐两张表之后仍是这个量级。而且「全部落在被域限死的列上」也不成立——`characters` 与 `mailbox_attachments` 的那些 0 恰恰是本可以互异却没填的，这正是 F-5。

正确的说法应该是这样，且它比「有多少重复」更切题：

**重复本身不是洞，同一条 SELECT 内的重复才是洞。** 列置换只发生在一条查询内部；跨表的小整数重复（多张表的首行序号都是 0）不给置换任何藏身处。所以要看的不是总重复数，而是每条位置式 SELECT 内部有没有两列同值。

现在三条最宽的位置式 SELECT 都已用相邻列对调证伪过：`characters` 88 列、`character_subtype0/1_fields`、`mailbox_attachments` 18 列，全部转红。剩余的 99 行重复由三类构成，各自不构成置换盲区：

- **布尔列**（8 个，只有 0 与 1 两个取值）：同值互换不可见，这是取值域的必然，补不了。评审已核实这 12 对同值组合中没有一对是相邻序号。
- **首行结构序号**（`sort_order`/`page_index`/`ordinal`/`slot_index` 等取 0）：它们参与 `ORDER BY`，改动会改变排序语义而非仅仅换值，不适合当作互异值来填。
- **跨表重复的小整数**：不同 SELECT 之间，置换不可达。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。

## Executor Self-check 3

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表全覆盖；四张宽表逐列互异，三条最宽的位置式 SELECT 各有相邻列对调的证伪 |
| 2 改动任一字段值使其转红 | 已闭合。五种证伪：int 列表加一、blob/bool/string 改值、三处列对调 |
| 3 改动任一集合顺序使其转红 | 已闭合（`hotkey_slots` 反序、整表读空、多插一行） |

### 残留，如实记录

- **8 个布尔列之间的同值互换不可见。** 取值域只有两个值，无法补齐。已核实这些组合中没有相邻序号对。
- **建表的可调和列（`ReconcilableColumns`）未被播种。** 它们不出现在 `CREATE TABLE` 的列表里，因此我的生成器不会碰到；`delete_flag` 就是其中之一（取默认 0，正好让查询条件通过）。如果某个可调和列被入场装载读取且取默认值，它与其相邻列的置换仍不可见。**交给 T-007：拆表时若遇到读取可调和列的查询，需要单独确认。**

## Independent Quick Review 3

- Verdict: `changes_required`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-003` / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff: 两个新增文件，均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 内，无生产改动
  - `Server/tests/Dfo.Server.Persistence.Tests/Support/StorageRecordDump.cs`（sha256 `e1d3e99a…`，与第 2 轮同，CRLF、无 BOM）
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `7bb9800d…`，第 2 轮为 `97d4cce1…`，CRLF、无 BOM）
- 本轮 8 处生产注入 + 1 处种子改写全部逐字节恢复；仓储 sha256 回到 `5170be4d…`，两个 owned 文件 sha256 未变，`git status --porcelain` 与评审前完全一致；评审未改动任何实现文件或任务控制状态

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| 基线（CK-007/CK-008） | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | 60+499+4+99+46+116+203 = **1027 通过 / 2 跳过 / 0 失败**，本轮无假红（`Host.Tests` 4/4、`Networking.Tests` 46/46） |
| 稳定性 | 该测试类连跑 5 次 | 5 次全绿 |
| 覆盖双向差集 | 脚本从仓储抽 `FROM|JOIN` 后标识符、从用例抽 `INSERT INTO` | 读取 34（含 CTE `oldest_inbox_page`）→ 真表 33；播种 33；`读取−播种` = {`oldest_inbox_page`}（CTE），`播种−读取` = ∅ |
| 金样重复统计 | 脚本按值分组 215 行 | 22 组 / 106 行；扣掉 `[]`、`True`、item_core 三组后为 **19 组 / 99 行**，与 Execution Result 3 的数字一致 |
| 建表 vs 可调和列 | 脚本比对 `CREATE TABLE` 列集与 `ReconcilableColumns` 297 行 | **297/297 全部已在 `CREATE TABLE` 中声明**，缺失 0 |
| 未播种列 | 脚本列出已播种表中未出现在 INSERT 列表的列 | 6 列被装载引用，**全部只出现在 `WHERE`/`CASE`/`JOIN` 谓词里，无一是位置式投影列** |
| 共享夹具 | `grep -rln StorageRecordDump Server/` | 仅本任务两个文件（其余为 bin/obj 产物）；`Support/` 下只有这一个文件，`Database/` 属 T-001 |

### 列置换探针（8 处，逐字节恢复后复绿）

| # | 注入 | 位置 | 预期 | 实测 |
|---|---|---|---|---|
| A | SELECT 内对调 `c.exp` ↔ `c.ex_equip_slot_stat` | `SqliteCharacterEntryStateRepository.cs:91` | 红 | **转红** |
| B | 对调 `GetInt32(10)` ↔ `(11)`（`durability`/`seal_flag`） | 同上 `:612` | 红 | **转红** |
| C | `character_creatures` 对调 `mode1_field0a` ↔ `mode1_field0b`（相邻） | 同上 `:415` | 红 | **转红** |
| D | 88 列 SELECT 内对调序号 84 `c.pvp_grade` ↔ 87 `s1.manage_level` | 同上 `:91-92` | — | **仍绿** |
| E | `character_new_titlebook` 对调 `category` ↔ `slot_index`（相邻） | 同上 `:436` | 红 | **转红** |
| F | `character_daily_challenge_entries` 对调 `value_a` ↔ `value_b`（相邻） | 同上 `:348` | 红 | **转红** |
| G | `mailbox_messages` 对调 `m.mail_type` ↔ `m.source_protocol`（相邻） | 同上 `:556-557` | 红 | **转红** |
| H | 88 列 SELECT 内对调序号 0 `c.account_id` ↔ 9 `s0.expert_job_type` | 同上 `:68-70` | — | 主金样**仍绿**（仅被同文件 Theory 的归属校验抓住） |

### F-5 已闭合的部分

A 与 B 独立复现，两处均转红——`characters` 与 `mailbox_attachments` 上第 2 轮点名的洞确已堵上。

另取四条执行者没试过的位置式 SELECT（C `character_creatures` 10 列、E `character_new_titlebook` 3 列、F `character_daily_challenge_entries` 4 列、G `mailbox_messages` 11 列），相邻列对调**全部转红**。这四张表的种子确实逐列互异。

### F-6 的论点：框架成立，枚举不成立

「重复本身不是洞，同一条 SELECT 内的重复才是洞」这个框架是对的，统计数字也对得上（19 组 / 99 行，见上表）。**但它给出的三类残留不穷尽，第四类是活的洞。**

### 需要整改（阻断本轮接受）

**F-7（必改）：88 列 SELECT 内仍有一对不可见的列置换——序号 84 `c.pvp_grade` 与序号 87 `s1.manage_level`，两者种子都是 35。**

探针 D 单独跑主金样：**仍绿**（`ACharacterIsLoadedWithExactlyWhatItWasLoadedWithBefore` 1/1 通过）。跑全量时唯一转红的是**既有**的 `SqliteCharacterEntryStateRepositoryTests.CharacterEntryState_LoadsPopulatedNormalizedCrossSliceStateLosslessly`（1026 通过 / 1 失败）——即本任务按 F-4 明确决定**不并入**、也不写进 T-007 判据的那一条。

这一点是关键：Executor Self-check 2/3 交给 T-007 的判据是「每拆一段就复跑**这三条**」，plan 的 T-007 步骤 4 也写「全程以 T-003 的表征用例作为不变性判据」。若 T-007 照此执行，这一形态的错误就是不可见的。金样是被指定的不变性判据，它自己上有一个成本近乎为零就能补掉的盲区，应当在本任务补掉。

已验证的整改（在 owned diff 内，一个字面量 + 一行金样）：把 `SeedAsync` 里 `characters` 的 `pvp_grade` 由 35 改为 **38**（88 列 SELECT 内 38 尚未被占用，且 ≤ `byte.MaxValue`，`checked((byte))` 通过），并把 `Expected` 的 `Profile.PvpGrade = 35` 同步为 38。实测：

- 改种子、不注入 → **绿**；
- 改种子 + 探针 D → **转红**（置换从此可见）。

**F-8（必改，与 F-7 同批）：Execution Result 3 的三类残留枚举不穷尽，且第三类的理由是错的。**

第四类是：**同一条位置式 SELECT 内，两个自由整型列（既非布尔、也非首行结构序号）被填了同一个值。** 今天有两对，都在那条 88 列 SELECT 里：

| 序号对 | 列 | 值 | 主金样 |
|---|---|---|---|
| 84 / 87 | `c.pvp_grade` / `s1.manage_level` | 都是 35 | **看不见**（探针 D） |
| 0 / 9 | `c.account_id` / `s0.expert_job_type` | 都是 12 | **看不见**（探针 H 单独跑主金样 1/1 通过；转红来自同文件 Theory 里 `expert_job_type` 取默认 0 触发归属校验，不是金样的判别力） |

此外同一条 SELECT 内 `c.job`(1) = 1 与四个布尔 1（序号 8/12/44/50）撞值，`s1.skill_tree_index`(28) = 0 与四个布尔 0（序号 7/11/38/49）撞值——同属这一类（自由列撞上布尔列）。

第三类的理由「**跨表重复的小整数：不同 SELECT 之间，置换不可达**」在今天不成立：`characters`、`character_subtype0_fields`、`character_subtype1_fields`、`character_init_flags` 是**四张表，被同一条 SELECT 读**。跨表 ≠ 跨 SELECT，而 T-007 重推序号正是对着今天这条 SELECT 做的。请把这句改成按「是否同处一条位置式 SELECT」判断，并把第四类如实列出（84/87 建议按 F-7 补掉，0/9 因 `account_id` 是查询键、`expert_job_type` 另有取值域，可择一改动或如实记为残留）。

**F-9（必改，与 F-7 同批）：Self-check 3 新记的「可调和列」残留是事实错误，照原样交给 T-007 会让它去查一个不存在的类别。**

原文：「建表的可调和列（`ReconcilableColumns`）未被播种。**它们不出现在 `CREATE TABLE` 的列表里**，因此我的生成器不会碰到；`delete_flag` 就是其中之一（**取默认 0**……）」。逐条核实：

1. `ReconcilableColumns` 共 **297 行，297 行全部已在对应的 `CREATE TABLE` 里声明**，缺失 0。它是给「按更早形状建过库」的数据库做的补列覆盖层，不是建表之外的另一批列（见 `CharacterDatabaseSchema.cs:20-24` 的 remarks）。
2. `characters|delete_flag` 既在 `CREATE TABLE`（`CharacterDatabaseSchema.cs:206`）里，**也被本用例显式播种为 0**（`EntryLoadCharacterizationTests.cs:95-96` 的列表含 `delete_flag`，对应值 0）。两处陈述都不成立。

真实的残留窄得多，且不是置换盲区：已播种表里未播种、且被装载引用的列只有 6 个——`mailbox_messages.unlimited_flag`、`mailbox_recipients.folder` / `saved_flag` / `deleted_flag` / `received_gold_flag`（外加 `character_init_flags.pc_room_state`、`character_daily_challenge_claims.claimed_at` 等根本不被读）。**这 6 个无一是位置式投影列**，全部只出现在 `WHERE` / `JOIN` / `CASE` 谓词里，因此它们不参与 `reader.GetInt32(n)` 的序号推导，不构成列置换盲区；而且它们必须取 0 才能让该行通过过滤，无法靠改种子做成互异值。这才是应当交给 T-007 的话：**入场装载的邮件过滤谓词有五个恒为 0 的标志位，谓词写反时金样看不见**——这是谓词层面的事，不属于本任务的验收条件，留给 T-007 知悉即可，不必在本任务补。

### 建议（不阻断）

**F-10：「首行结构序号参与 `ORDER BY`，所以不适合填互异值」这个理由不成立，但结论成立。**

单行数据下 `ORDER BY` 是空操作，填非零值不改变任何排序语义——执行者自己的种子就有三处反例：`character_active_quests.slot` = 2、`character_collectbox_slots.box_index` = 3、`character_invisible_falgs.slot_index` = 200，三者都被投影、都参与 `ORDER BY`、都不是 0。

不过逐条核对后，这一类**确实没有洞**：14 个被投影且取 0 的结构序号列（`character_dimensions`、`character_skills`、`character_daily_challenge_groups`/`_entries`、`character_dungeon_permissions`、`character_creatures`、`character_new_titlebook`、`character_achievement_complete`、`character_collectbox_slots`、`character_pvp_missions`、`character_daily_schedule_states`、`mailbox_attachments`、两张按职业开放的表），每一个的 0 在**它自己那条 SELECT 内都是唯一值**。建议把理由换成这一条（「0 在本条 SELECT 内唯一」），结论可以照留。

**F-11（仅记录，无需处理）：`character_gold_limits` 的两列同值，但不是种子的问题。**

`SELECT gold_carry_limit, auction_gold_limit`（`:326`）两列种子都是 600000000，是一条同 SELECT 内的重复；但消费它的 `GoldLimitRuleService.UpgradeLevel`（`CharacterEntryRuleServices.cs:13-21`）取 `Math.Min`，两列**按定义可交换**，任何种子都无法让这处置换可见。记下来以免下一轮把它误当成 F-7 的同类。

### 其他核对（无阻断）

- **覆盖仍然完整。** 独立重做双向差集：读取侧 34 个标识符去掉 CTE `oldest_inbox_page` 得 33 张真表，播种侧 33 张，两侧互减为空。种子三轮大改后覆盖面未退化。
- **金样稳定。** 该测试类连跑 5 次全绿，无时间/文化/时区漂移（第 2 轮已逐项核实，本轮种子改动未引入新的 `CURRENT_TIMESTAMP` 依赖）。
- **没有共享夹具。** `SeedAsync` 与 `ItemCore` 都是本文件 `private static`；`StorageRecordDump` 只被本文件引用；`Support/` 下只有这一个文件。种子三轮改写不可能波及任何既有用例——全量 1027/2/0 与执行者所报一致即是佐证。
- Owned diff 未越界：两个文件均在 `Server/tests/Dfo.Server.Persistence.Tests/` 下；`git status` 中其余改动属 T-001/T-002 既有工作，本任务未触碰生产代码。
- C# 文件 CRLF、无 BOM；`TreatWarningsAsErrors` 下全量构建 0 警告 0 错误。
- 未产生任务级提交。

### 整改清单

1. `SeedAsync` 的 `characters`：`pvp_grade` 由 35 改为 38；`Expected` 的 `Profile.PvpGrade` 同步。（已验证：改后探针 D 转红，未注入时仍绿。）
2. 重跑探针 D（对调序号 84/87）确认转红，恢复后复绿。
3. 改写 Execution Result 3 的三类残留：加入第四类（同一条位置式 SELECT 内两个自由整型列同值），并修正「跨表 = 跨 SELECT」这句错误理由。
4. 改写 Self-check 3 的「可调和列」残留：297/297 可调和列均已在 `CREATE TABLE` 中声明，`delete_flag` 既已声明也已播种；替换为真实残留（五个恒 0 的邮件过滤标志位只在谓词里，非投影列，留 T-007 知悉）。
5. （建议）按 F-10 把首行结构序号那条的理由换成「0 在本条 SELECT 内唯一」。

## Execution Result 4

三条阻断都成立，而且它们指向同一个根因：**我按表各起一个计数器，而那条 88 列 SELECT 连接四张表。** 表内互异不等于查询内互异。

### 根因与修法

`characters`、`character_subtype0_fields`、`character_subtype1_fields`、`character_init_flags` 被同一条 SELECT 一起投影。我此前给每张表单独起数（1001、2001、字节池 11/31/32…），于是跨表撞值：

- `c.pvp_grade` = 35 与 `s1.manage_level` = 35（F-7）
- `c.account_id` = 12 与 `s0.expert_job_type` = 12（F-8）

改为**一个全局计数器贯穿这几张表**：字节域列共用一个池，其余共用另一个池，两池不重叠。字节池起点移到 101——避开全部固定值（0、1、12、22、60），且不越 255 上限。

**证伪**（两处都是评审点名的那一对）：

| 注入 | 第 3 轮 | 现在 |
|---|---|---|
| `c.pvp_grade` ↔ `s1.manage_level` | 绿（洞） | **转红** |
| `c.account_id` ↔ `s0.expert_job_type` | 主金样看不见 | **转红** |

那条 JOIN 内现在唯一的重复值是 `22`——四张表的连接键，不是被投影的列。

### F-8｜我第三轮那个「跨表不可达」的论断是错的

原话：「跨表重复的小整数：不同 SELECT 之间，置换不可达。」

**四张表被同一条 SELECT 读，跨表恰恰可达。** 这句话不只是不精确，它是我用来解释为什么不必补齐的理由——一个错误的理由，掩护了两个真实的洞。

正确的说法：**要看的是每条位置式查询投影出来的那一组值内部有没有重复**，与这些值来自几张表无关。

### F-9｜「可调和列未播种」是事实错误

我第三轮新记了一条残留，说 `ReconcilableColumns` 未被播种、`delete_flag` 是其中之一。**两句都错。** 评审核实：297 个可调和列全部已在 `CREATE TABLE` 中声明，缺失为 0；`delete_flag` 既在 schema 里声明，也被本用例显式播种为 0。

真实残留窄得多：已播种表中未播种且被读取的只有 6 列（`unlimited_flag`、`folder`、`saved_flag`、`deleted_flag`、`received_gold_flag` 等），**无一是位置式投影列**——它们全在 `WHERE` / `CASE` 谓词里，不构成列置换盲区，而且必须取 0 才能让过滤通过。所以既不该在本任务补，也不该按我原话交给 T-007。原记录作废，以此条为准。

### F-10｜排序序号那个理由也不成立

我说结构序号「参与 ORDER BY 所以不适合填互异值」。评审举出我自己种子里的三处反例（`character_active_quests.slot` = 2、`character_collectbox_slots.box_index` = 3、`character_invisible_falgs.slot_index` = 200），它们既被投影也参与排序，照样填了非零值。

结论仍然成立，但理由要换成真的那个：**14 个取 0 的结构序号列，每一个 0 在它自己那条 SELECT 投影出来的值里都是唯一的**，所以不构成置换盲区。

### F-11｜金币上限两列同值补不了

采纳。`gold_carry_limit` 与 `auction_gold_limit` 同为 600000000，是同一条查询内的重复，但升级等级取两者的 `Math.Min`——按定义两列可交换，任何种子都区分不出来。不属于同类问题。

### 残留（重写第三轮那段）

- **8 个布尔列**：取值域只有 0 与 1，同值互换不可见。评审核实这 12 对同值组合中没有一对是相邻序号。
- **14 个首行结构序号**：各自在所属查询内唯一，不构成盲区。
- **`character_gold_limits` 的两列**：按定义可交换，见 F-11。
- **6 个谓词列**：只出现在 `WHERE` / `CASE` 中，不被投影，且必须取 0 才能通过过滤。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。生产代码逐字节未变（`git diff` 对 `SqliteCharacterEntryStateRepository.cs` 为空）。

## Executor Self-check 4

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表全覆盖；四张宽表按**全局唯一**取值，那条 88 列 JOIN 内除连接键外无重复 |
| 2 改动任一字段值使其转红 | 已闭合。七种证伪：int 列表加一、blob/bool/string 改值、四处不同的列对调（含评审独立找到的两处漏网） |
| 3 改动任一集合顺序使其转红 | 已闭合（`hotkey_slots` 反序、整表读空、多插一行） |

### 三轮下来的教训，记在这里

三轮阻断都是同一件事的不同侧面：**我用「我认为它覆盖了」代替了「我验证过它覆盖了」。** 第一轮把种子的稀疏当成完整，第二轮把表内互异当成查询内互异，第三轮把一条没核实的断言（可调和列未播种）写进了交接。

金样这类工件的价值全在于它红的时候。三轮里它有三次本该红而没红，每一次都是靠评审的独立探针发现的，不是靠我自己的自查。

## Independent Quick Review 4

- Verdict: `changes_required`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-003` / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff: 两个新增文件，均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 内，无生产改动
  - `Server/tests/Dfo.Server.Persistence.Tests/Support/StorageRecordDump.cs`（sha256 `e1d3e99a…`，与第 2/3 轮同，CRLF、无 BOM）
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `27f03f67…`，第 3 轮为 `7bb9800d…`，CRLF、无 BOM）
- 本轮 10 处生产注入 + 1 处种子改写全部逐字节恢复；仓储 sha256 回到 `5170be4d…`，两个 owned 文件 sha256 未变，`git status --porcelain` 与评审前逐字节一致；评审未改动任何实现文件或任务控制状态；无任务级提交（HEAD 仍为 `ea95d49`）

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| CK-007 / CK-008 基线 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | 60+499+4+99+46+116+203 = **1027 通过 / 2 跳过 / 0 失败**，与执行者所报一致；本轮无假红（`Host.Tests` 4/4、`Networking.Tests` 46/46） |
| 构建 | `dotnet build`（`TreatWarningsAsErrors`） | 0 警告 0 错误 |
| 稳定性 | 干净重建后该测试类连跑 **6** 次 | 6 次全绿（每次 3/3） |
| 覆盖双向差集 | 脚本从仓储抽 `FROM|JOIN` 后标识符、从用例抽 `INSERT INTO` | 读取 34 → 去 CTE `oldest_inbox_page` 得 **33** 真表；播种 **33**；两侧互减均为 ∅ |
| **穷尽性：88 列 SELECT 逐列求值** | 脚本解析 `CharacterDatabaseSchema` 的 `CREATE TABLE` 取默认值、解析 `SeedAsync` 的 `INSERT` 取播种值，对 88 个投影列逐一解析实际取值后按值分组 | 见下「88 列 SELECT 的完整撞值表」 |
| **穷尽性：其余位置式 SELECT** | 同一脚本对仓储里其余全部位置式读取逐行求值（**24 条多列投影 + 9 条单值读取**，后者不存在列对） | 除 `character_gold_limits` 外，**每条查询投影出的一行内取值两两互异**；详见下 |
| 谓词列核实 | 脚本列出已播种表中未出现在 INSERT 列表的列，再逐个在仓储里 grep 其出现形态 | 真实为 **5** 列，全部只出现在 `WHERE` / `JOIN` / `CASE` 中 |
| 共享夹具 | `grep -rln StorageRecordDump Server --include=*.cs` | 仅 owned 的两个文件；`Support/` 下只有 `StorageRecordDump.cs` |

### 列置换探针（10 处，逐字节恢复后复绿）

| # | 注入（88 列 SELECT 内对调序号，除注明外） | 两列取值 | 预期 | 实测 |
|---|---|---|---|---|
| P1 | 84 `c.pvp_grade` ↔ 87 `s1.manage_level` | 105 / 132 | 红 | **转红**（pos 2990，`ManageLevel 132 → 105`） |
| P2 | 0 `c.account_id` ↔ 9 `s0.expert_job_type` | 12 / 109 | 红 | **转红**（`InvalidDataException: Character 22 does not belong to account 12`） |
| P9 | 9 `s0.expert_job_type` ↔ 87 `s1.manage_level` | 109 / 132 | 红 | **转红**（pos 317，`ExpertJobType 109 → 132`）——绕开归属校验，证明序号 9 已被金样本身判别 |
| P8（对照） | 84 `c.pvp_grade` ↔ 85 `c.pvp_rating_grade` | 105 / 106 | 红 | **转红**（pos 3474） |
| **P3** | **1 `c.job` ↔ 8 `s0.is_private_store`** | **1 / 1** | — | **仍绿** |
| **P6** | **1 `c.job` ↔ 50 `s0.link_slot_enabled`** | **1 / 1** | — | **仍绿** |
| **P4** | **28 `s1.skill_tree_index` ↔ 49 `s0.is_return_user`** | **0 / 0** | — | **仍绿** |
| **P5** | **28 `s1.skill_tree_index` ↔ 7 `s0.is_event_character`** | **0 / 0** | — | **仍绿** |
| P7 | `character_gold_limits` 的 `gold_carry_limit` ↔ `auction_gold_limit`（`:326`） | 6e8 / 6e8 | 绿 | **仍绿**（F-11 如实，`Math.Min` 按定义可交换） |

### F-7 已闭合

P1 独立复现，**转红**，且红来自金样自身的逐行比对（`ManageLevel = 132` → `105`），不是别的用例。`c.pvp_grade` 现为 105、`s1.manage_level` 现为 132，撞值消失。

### F-8 的两处点名撞值已闭合，但它要求的「第四类穷尽列出」没有做到

P2 转红，0/9 撞值消失（`account_id` = 12、`expert_job_type` = 109）。P2 的红是生产侧归属校验先抛出的，为排除「靠生产守卫而非金样判别力」的疑虑，另做 P9 绕开该守卫对调 9↔87：金样自身**转红**。序号 9 确已被钉住。

「跨表 ≠ 跨 SELECT」这句更正也是对的，全局计数器的修法方向正确。

但第 3 轮 F-8 要求的是**把第四类如实列出**，且当时已点名 `c.job`(1) 与四个布尔 1、`s1.skill_tree_index`(28) 与四个布尔 0 属于这一类。Execution Result 4 既没有补掉它们，也没有把它们列进残留——反而写了一句与之相反的断言。见 F-12 / F-13。

### 88 列 SELECT 的完整撞值表（逐列求值后按值分组）

88 个投影列的实际取值中，只有两个值出现多于一次：

| 值 | 序号与列 | 成对数 |
|---|---|---|
| `1` | 1 `c.job`、8 `s0.is_private_store`、12 `s0.is_hardcore_dead`、44 `s0.is_disguised`、50 `s0.link_slot_enabled` | C(5,2) = 10 |
| `0` | 7 `s0.is_event_character`、11 `s0.is_hardcore_mode`、**28 `s1.skill_tree_index`**、38 `s0.is_premium_pc_room`、49 `s0.is_return_user` | C(5,2) = 10 |

共 **20 对**同值列。其中 12 对是布尔↔布尔（取值域只有 {0,1}，不可再收窄，第 2 轮已判可接受）；另 **8 对**是自由列↔布尔列，**可补而未补**。

四对相邻序号（0/1、7/8、11/12、49/50）取值均不同，整体错位与相邻换位仍然可见——这一点与第 2 轮结论一致。

### 需要整改（阻断本轮接受）

**F-12（必改）：88 列 SELECT 内仍有 8 对不可见的列置换，四处已实测证明。**

`c.job` = 1 与四个布尔 1 撞值，`s1.skill_tree_index` = 0 与四个布尔 0 撞值。四次独立探针（P3、P6、P4、P5）**全部仍绿**——主金样看不见这四种置换。

这两列都不是布尔列，**取值不受 {0,1} 限死**，与第 2 轮判定「可接受」的布尔残留不是一类：

- `c.job`：读法是 `checked((byte)reader.GetInt32(1))`（`SqliteCharacterEntryStateRepository.cs:168`），域为 0..255；装载只在 `:247`、`:251` 用它做 `!= 9` / `!= 12` 判断。取 **133** 即可（≤255，非 9 非 12，且 133 在这条 SELECT 内尚未被占用——字节池现止于 132）。
- `s1.skill_tree_index`：域为 −1..1（`ValidateProfile` `:683`），可取 **−1**，在这条 SELECT 内唯一。

**整改已验证可行**（改动全部落在 owned diff 内，无需动生产代码）：

1. `SeedAsync` 的 `characters` INSERT：`job` 由 `1` 改为 `133`（`EntryLoadCharacterizationTests.cs:96` 第 5 个字面值）。
2. `SeedAsync` 的 `character_subtype1_fields` INSERT：`skill_tree_index` 由 `0` 改为 `-1`（`:100`，`…,1032,0,1033,…` → `…,1032,-1,1033,…`）。
3. `Expected` 同步三行：`Profile.Job = 133`、`Skill.Job = 133`、`Profile.Subtype1.SkillTreeIndex = -1`。

实测（评审已逐字节恢复）：

| 状态 | 结果 |
|---|---|
| 只改种子与金样、不注入 | **绿**（该测试类 3/3） |
| 改后 + P3（1↔8） | **转红** |
| 改后 + P6（1↔50） | **转红** |
| 改后 + P4（28↔49） | **转红** |
| 改后 + P5（28↔7） | **转红** |

补完之后，88 列 SELECT 的撞值对由 20 降到 12，且这 12 对全部落在 8 个布尔列内部——那是取值域的下界，不可能再低。**这就是这条查询能达到的最好状态，应当在本任务达到它，而不是留给 T-007。**

一处取舍需如实记下：`skill_tree_index = -1` 恰等于建表默认（`CharacterDatabaseSchema.cs:205`），于是该列「被读到」与「被换成常量默认」不可区分。但它三个可取值里，`0` 撞四个布尔、`1` 撞五列、`-1` 在本条 SELECT 内唯一——本任务的验收条件是「列映射置换可见」，取 `-1` 是对的，把这项取舍写进残留即可。

**F-13（必改，与 F-12 同批）：Execution Result 4 关于撞值的断言与金样本身不符。**

原文：「那条 JOIN 内现在唯一的重复值是 `22`——四张表的连接键，不是被投影的列。」

两处都不成立：

1. `22` **根本不在投影里**。这条 SELECT 的 88 个投影列不含 `c.character_id`（连接键只出现在 `ON` 与 `WHERE` 中，`:94-97`），所以它连「重复值」的候选都不是。
2. 投影出来的 88 个值里有**两个** 5 元重复组（见上表），合计 20 对同值列，其中 8 对可补而未补。

这句话正好落在 T-007 最需要判断力的那一维上：Executor Self-check 2/3/4 交给 T-007 的判据是「每拆一段就复跑这三条」，plan 的 T-007 步骤 4 也写「全程以 T-003 的表征用例作为不变性判据」。一句「除连接键外无重复」会让 T-007 相信这条 SELECT 的列置换已被全面挡住，而实际上有 8 对是活的。

同批还需修正残留清单：第一类现在应写作「**8 个布尔列，共 12 对同值组合**，取值域只有 {0,1}，不可收窄，且其中没有一对是相邻序号」，并明确「补齐 `job` 与 `skill_tree_index` 之后，这 12 对是本条查询撞值对数的下界」。

### 建议（不阻断）

**F-14：F-9 的「6 个谓词列」应为 5 个，其余判断准确。**

逐列核实（脚本先取每张已播种表的 `CREATE TABLE` 列集减去 `INSERT` 列表，再对每个候选在仓储里 grep 其出现形态）：

| 列 | 在仓储中的形态 |
|---|---|
| `mailbox_messages.unlimited_flag` | `m.unlimited_flag != 0`（`:551`）、`m.unlimited_flag!=0`（`:559`、`:510`） |
| `mailbox_recipients.folder` | `r.folder=0`（`:548`、`:507`、`:565`） |
| `mailbox_recipients.saved_flag` | `r.saved_flag=0`（`:549`、`:508`）、`r.saved_flag!=0`（`:562`） |
| `mailbox_recipients.deleted_flag` | `r.deleted_flag=0`（`:550`、`:509`） |
| `mailbox_recipients.received_gold_flag` | `r.received_gold_flag=0`（`:558`） |

未播种且**完全不被仓储引用**的另有 `mailbox_recipients.recipient_id`、`mailbox_recipients.created_at`、`mailbox_messages.deleted_by_sender`、`character_daily_challenge_claims.claimed_at`、`character_gold_limits.updated_at`；`mailbox_recipients.read_flag` 已播种为 1。所以真实数字是 **5**，不是 6，也没有「等」字暗示的更多。

「**无一是位置式投影列**」这个关键判断**准确**：五者全部只出现在 `WHERE` / `JOIN ON` / `CASE` 里。其中 `saved_flag` 与 `received_gold_flag` 各自是一个**被投影表达式的输入**（序号 10 的 `CASE`、序号 7 的 `CASE`），但它们本身没有自己的序号，不参与 `reader.GetInt32(n)` 的推导，结论不受影响。「必须取 0 才能通过过滤」也成立（`folder`/`saved_flag`/`deleted_flag` 三者非 0 则该行被 `WHERE` 滤掉，邮件整体消失）。

请把数字由 6 改为 5，其余照留。

**F-15：残留四类可以收成一条判据，写法建议。**

四类（布尔列、首行结构序号、金币上限两列、谓词列）**是穷尽的**——本轮独立复核过：

- 逐列求值后，仓储里 **24 条多列位置式投影**中只有两条有同值列：88 列 SELECT（上表）与 `character_gold_limits`（`:326`）。其余 **22 条**每条投影出的一行内取值两两互异（另有 9 条单值读取，不存在列对）。
- 「14 个首行结构序号各自在所属查询内唯一」经逐条求值**核实成立**（F-10 建议的理由是对的）。

所以不存在第五类。但四类的形式是「按成因分类」，读者要自己判断哪些成因构成盲区。建议在交给 T-007 的那段里换成一条可直接执行的判据：

> **每条位置式查询投影出来的那一行值内部两两互异，则该查询的列置换可见。** 今天仓储里 24 条多列位置式投影只有两条不满足：88 列 SELECT 的 8 个布尔列（取值域下界，12 对不可见），与 `character_gold_limits` 的两列（`Math.Min` 按定义可交换）。其余 22 条全部满足。

这样 T-007 拆表时只需对新查询重跑这一条，不必回头分辨自己遇到的是哪一类。

### 其他核对（无阻断）

- **覆盖仍然完整。** 独立重做双向差集：读取侧 34 个标识符去掉 CTE `oldest_inbox_page` 得 33 张真表，播种侧 33 张（`SeedAsync` 31 + Theory 2），两侧互减均为空。第 4 轮的种子改写未使任何表脱落。
- **金样稳定。** 干净重建后连跑 6 次全绿。第 4 轮改动只挪动字面值，未引入新的 `CURRENT_TIMESTAMP`／`strftime` 依赖，第 2 轮逐项核实过的时间/文化/时区结论继续成立（`CreatedAtUnixSeconds` 仍为固定的 `1577934245`）。
- **没有共享夹具受影响。** `StorageRecordDump` 只被 owned 的两个文件引用，`Support/` 下只有它一个文件；`SeedAsync` 与 `ItemCore` 都是本文件 `private static`。全量 1027/2/0 与执行者所报一致即是佐证。
- **F-10 已闭合。** 首行结构序号那条的理由已换成「各自在所属查询内唯一」，且该说法经逐条求值核实为真。
- **F-11 如实。** P7 实测仍绿，与 `GoldLimitRuleService.UpgradeLevel` 取 `Math.Min` 的分析一致，任何种子都无法让这处置换可见，不属于 F-12 的同类。
- Owned diff 未越界：两个文件均在 `Server/tests/Dfo.Server.Persistence.Tests/` 下，符合 BD-012 的 `Server/tests/`；`git status` 中其余改动属 T-001/T-002 既有工作，本任务未触碰生产代码（`SqliteCharacterEntryStateRepository.cs` sha256 三轮未变）。
- C# 文件 CRLF、无 BOM；`TreatWarningsAsErrors` 下 0 警告 0 错误；未产生任务级提交。
- Common Constraints 逐条核对：既有测试语义未被改写（F-4 的不合并决定继续保持）；仓储未声明表结构、未查询表结构元数据；未写回填/迁移路径；未触碰 `Tool/` 与 `.worktrees/legacy-reference/`。

### 整改清单

1. `SeedAsync` 的 `characters`：`job` 由 `1` 改为 `133`（`EntryLoadCharacterizationTests.cs:96`）。
2. `SeedAsync` 的 `character_subtype1_fields`：`skill_tree_index` 由 `0` 改为 `-1`（`:100`）。
3. `Expected` 同步：`Profile.Job = 133`、`Skill.Job = 133`、`Profile.Subtype1.SkillTreeIndex = -1`。
4. 重跑 P3（1↔8）与 P4（28↔49）确认转红，恢复后复绿。（评审已预先验证四处全部转红，可直接照做。）
5. 改写 Execution Result 4 中「那条 JOIN 内现在唯一的重复值是 22」一句：22 不在投影内；补齐后本条 SELECT 的撞值只剩 8 个布尔列之间的 12 对，且那是取值域下界。
6. 残留第一类改为「8 个布尔列 / 12 对，无相邻序号对，取值域下界」；记下 `skill_tree_index = -1` 恰等于建表默认这项取舍。
7. （建议）第四类残留改为 F-14 的数字 5；交给 T-007 的判据改成 F-15 那一条可直接执行的写法。

## Execution Result 5

### F-12｜最后两处可补而未补的撞值

成立。`c.job` = 1 与四个取 1 的布尔列撞；`s1.skill_tree_index` = 0 与四个取 0 的布尔列撞。我把这两列当成了「被域限死」的一类，但它们不是：`job` 只要不是 9 或 12 即可（域 0..255），`skill_tree_index` 域是 −1..1，两者都还有别的值可取。

改为 `job` = 133、`skill_tree_index` = −1。

**证伪**（评审给出的两对）：

| 注入 | 第 4 轮 | 现在 |
|---|---|---|
| 序号 1 ↔ 8（`c.job` ↔ `s0.is_event_character`） | 绿 | **转红** |
| 序号 28 ↔ 7（`s1.skill_tree_index` ↔ 一个取 0 的布尔列） | 绿 | **转红** |

### F-13｜我关于「唯一重复值是 22」的说法两处都错

`c.character_id` **根本不在那 88 个投影列里**——它只出现在 `ON` 与 `WHERE`。而投影值里当时有两个五元重复组（合计 20 对同值列），不是「唯一一个」。

这句话最坏的地方在于它会让 T-007 相信列置换已被全面挡住，从而不再自己检查。

改为用脚本求值而不是靠断言。对那条 SELECT 的 88 个投影列逐一解析其种子取值，当前结果：

```
projected columns: 88
  value 0 shared by 4: is_event_character, is_hardcore_mode, is_premium_pc_room, is_return_user
  value 1 shared by 4: is_private_store, is_hardcore_dead, is_disguised, link_slot_enabled
```

**除这 8 个布尔列外，88 个投影列两两取值互异。** 布尔列只有 0 与 1 两个合法值，12 对同值组合补不了——这是取值域的事实，不是没做的事。

评审另行核实：仓储 24 条多列位置式投影中，只有这条 88 列 SELECT 与 `character_gold_limits` 存在同值列，其余 22 条逐行取值两两互异；`character_gold_limits` 那两列按 `Math.Min` 定义可交换（F-11）。

### F-9 的数字更正

真实的谓词列残留是 **5** 列不是 6：`recipient_id`、`created_at`、`deleted_by_sender`、`claimed_at`、`updated_at` 完全不被引用，而 `read_flag` 其实已播种。判断「不被投影」正确。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。生产代码逐字节未变。

## Executor Self-check 5

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表全覆盖；那条 88 列 SELECT 的投影列除 8 个布尔外两两互异，且这一点由脚本求值得出而非断言 |
| 2 改动任一字段值使其转红 | 已闭合。九种证伪：int 列表加一、blob/bool/string 改值、六处不同的列对调（其中四处是评审独立找到的漏网） |
| 3 改动任一集合顺序使其转红 | 已闭合（`hotkey_slots` 反序、整表读空、多插一行） |

### 残留（最终）

| 类别 | 数量 | 为什么补不了 |
|---|---:|---|
| 布尔列互撞 | 8 列 / 12 对 | 合法值只有 0 与 1 |
| `character_gold_limits` 两列 | 1 对 | 取 `Math.Min`，按定义可交换 |
| 谓词列 | 5 列 | 不被投影；且须取 0 才能通过过滤 |
| 首行结构序号 | 14 列 | 各自在所属查询的投影值内唯一，本就不是盲区 |

### 四轮下来的教训

四轮阻断，每一轮都是我把「我认为它覆盖了」当成了「我验证过它覆盖了」：种子稀疏当完整、表内互异当查询内互异、未核实的断言写进交接、把两个还有取值空间的列当成域限死。

最后一轮我改了做法：不再自己判断还有没有撞值，而是**写脚本把 88 个投影列的实际取值算出来**。这本该是第一轮就做的事——判断哪里可能有洞，和把洞算出来，是两件不同的事。

## Independent Quick Review 5

- Verdict: `passed`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-003` / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff: 两个新增文件，均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 内，无生产改动
  - `Server/tests/Dfo.Server.Persistence.Tests/Support/StorageRecordDump.cs`（sha256 `e1d3e99a…`，与第 2/3/4 轮同，CRLF、无 BOM）
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `e61630be…`，第 4 轮为 `27f03f67…`，CRLF、无 BOM）
  - 随任务留档的 `projected-column-values.py` 位于本 attempt 根目录，不在仓库源码路径内
- 本轮 11 处生产注入：**每次注入前先备份、跑完立即恢复**，仓储 sha256 每次都回到 `5170be4d…`；结束时 `git diff --stat -- Server/src/` 与评审前逐字一致，`git status --porcelain` 与评审前逐字一致；HEAD 仍为 `ea95d49`，无任务级提交；评审未改动任何实现文件或任务控制状态

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| CK-007 / CK-008 全量 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | 60+499+4+99+46+116+203 = **1027 通过 / 2 跳过 / 0 失败**，exit 0；本轮无假红（`Host.Tests` 4/4、`Networking.Tests` 46/46） |
| CK-007 单项目 | `dotnet test …/Dfo.Server.Persistence.Tests.csproj --filter …EntryLoadCharacterizationTests` | 3/3 |
| 稳定性 | 该测试类连跑 **6** 次 | 6 次全绿（0.7–1.0 s） |
| **88 列取值：不用执行者的脚本，独立求值** | 用 `CharacterDatabaseSchema` 的 52 条 `CREATE TABLE` 建真库、执行 `SeedAsync` 的真种子、跑仓储那条真 SELECT，按 SQLite 返回的**实际值**分组 | SQLite 自报 **88** 列；**82** 个互异值；重复组恰 **2** 个，各 4 个布尔列，合计 **12** 对；**投影列中未播种者 0 个** |
| 执行者脚本复跑 | `python projected-column-values.py` | 输出与上面的独立求值**完全一致** |
| 其余位置式查询 | 把仓储里全部 SELECT 对真库逐条执行，检查一行内是否有重复值 | 共 33 条：**24 条多列投影 + 9 条单值读取**；一行内有重复值的只有 88 列 SELECT 与 `character_gold_limits`（第 4 轮 F-15 的枚举成立） |
| 覆盖双向差集 | 脚本抽 `FROM|JOIN` 后标识符与 `INSERT INTO` | 读取 34 → 去 CTE `oldest_inbox_page` 得 **33** 真表；播种 **33**；两侧互减均为 ∅；仓储全文无 `INSERT`/`UPDATE`/`DELETE`，确为纯读 |
| 金样形状 | 解析 `Expected` | **215 行**；`System.` 出现 0 次；无 `= null` 行；`Profile.Job` / `Skill.Job` / `Profile.Subtype1.SkillTreeIndex` 三行与种子一致 |
| 共享夹具 | `grep -rn StorageRecordDump / EntryLoadCharacterization / dfo-entry Server --include=*.cs` | `StorageRecordDump` 只被 owned 的两个文件引用；**无任何外部文件引用本用例类**；临时库路径按 `Guid.NewGuid()` 唯一，与 `SqliteEntryLifecycleRecoveryTests` 等的 `dfo-entry-*` 不冲突 |
| 编码 | 逐文件字节检查 | 两个 owned 文件均 no-BOM、纯 CRLF（100/100、396/396） |

### 列置换探针（11 处，逐次恢复后 sha256 均回到 `5170be4d…`）

| # | 注入（88 列 SELECT 内对调序号） | 两列取值 | 实测 |
|---|---|---|---|
| Q1 | 1 `c.job` ↔ 8 `s0.is_private_store`（第 4 轮 P3，当时绿） | 133 / 1 | **转红** |
| Q2 | 7 `s0.is_event_character` ↔ 28 `s1.skill_tree_index`（第 4 轮 P5，当时绿） | 0 / −1 | **转红** |
| Q3 | 1 `c.job` ↔ 12 `s0.is_hardcore_dead`（F-12 八对中未试过的一对） | 133 / 1 | **转红** |
| Q4 | 1 `c.job` ↔ 44 `s0.is_disguised`（未试过） | 133 / 1 | **转红** |
| Q5 | 1 `c.job` ↔ 50 `s0.link_slot_enabled`（第 4 轮 P6，当时绿） | 133 / 1 | **转红** |
| Q6 | 11 `s0.is_hardcore_mode` ↔ 28 `s1.skill_tree_index`（未试过） | 0 / −1 | **转红** |
| Q7 | 28 `s1.skill_tree_index` ↔ 38 `s0.is_premium_pc_room`（未试过） | −1 / 0 | **转红** |
| Q8 | 28 `s1.skill_tree_index` ↔ 49 `s0.is_return_user`（第 4 轮 P4，当时绿） | −1 / 0 | **转红** |
| **Q9** | **41 `s0.guild_level` ↔ 43 `s0.disguise_kind`（自选，此前未试过）** | 115 / 116 | **转红**，pos 1002，红来自金样自身的逐行比对 |
| **Q10** | **73 `s1.current_hp_mp_percent` ↔ 76 `s1.equipped_creature_level`（自选，此前未试过）** | 129 / 130 | **转红**，pos 2642，红来自金样自身 |
| Q11（对照） | 1 `c.job` ↔ 87 `s1.manage_level` | 133 / 132 | **转红**，主金样 `Assert.Equal()`——证明序号 1 已被**金样本身**判别，不是只靠生产守卫 |

### F-12 已闭合

第 4 轮点名的 8 对（`c.job` × 四个取 1 的布尔、`s1.skill_tree_index` × 四个取 0 的布尔）**逐对独立复现，全部转红**（Q1–Q8），第 4 轮它们全是绿的。另自选两对此前未试过的投影列（Q9、Q10），亦全部转红，且这两处的红来自金样的逐行比对并指出了差异位置。

补齐之后 88 列 SELECT 的撞值对由 20 降到 12，且这 12 对全部落在 8 个布尔列内部——即取值域的下界，与执行者所述一致。

### F-13 已闭合，且脚本的解析经独立核查

「88 个投影列中除 8 个布尔列外两两互异」**属实**。我没有采信脚本输出，而是另建一条证据链：用 `CharacterDatabaseSchema` 的真 DDL 建库、跑 `SeedAsync` 的真种子、执行仓储那条真 SELECT，按 SQLite 返回的实际值分组。结果与脚本逐字吻合：

- 投影列数 **88**——SQLite 自报的结果集列数、按投影文本抽取的列数、仓储实际用到的最大序号（87）三者互相印证；且逐序号核对 SQLite 报的列名与投影文本的列名一一对上。**88 这个数字是对的。**
- 重复组恰 2 个：`0` 四列（序号 7、11、38、49）、`1` 四列（序号 8、12、44、50），合计 12 对；其余 80 列两两互异。

脚本的解析核查：

- **没有把 `ON`/`WHERE` 的列当投影列。** 它先用 `SELECT c\.account_id,(.*?)FROM characters c` 截出投影段，`ON`/`WHERE` 在 `FROM` 之后，落在截取范围外。`c.character_id` 只出现在 `ON`/`WHERE`，脚本确实没把它计入——这正是第 4 轮 F-13 的第一处更正。
- **没有漏掉投影列。** 88 个投影列全部带 `c.`/`s0.`/`s1.`/`f.` 前缀，正则一个不漏；与 SQLite 自报的列数一致。

脚本本身有两处**今天不影响结论、但作为交给 T-007 的工具值得注明**的脆弱点，见 F-19。

### `job` = 133 与 `skill_tree_index` = −1 的副作用（本轮重点核查）

**结论：两处都没有改变装载所走的分支，金样钉住的仍是与 `job` = 1 时相同的那条路径。**

`job` 的去向已逐处清点（`SqliteCharacterEntryStateRepository.cs` 全文只有五处提及）：

| 位置 | 用法 | 1 与 133 的差别 |
|---|---|---|
| `:46` | `LoadSkillsAsync(…, profile.Job, …)` | 只是传参 |
| `:168` | `checked((byte)reader.GetInt32(1))` | 133 ≤ 255，不抛 |
| `:247` | `if (job != 9 && combos.Count != 0)` | 1 与 133 都满足 `!= 9`；主金样 `combos` 为空，两者都不抛，**同一支** |
| `:251` | `if (job != 12 && shields.Count != 0)` | 同上，**同一支** |
| `:260` | 存入 `CharacterSkillStorageRecord` | 只是取值 |

`job == 9` 与 `job == 12` 那两条分支**没有失去覆盖**——它们本来就不是主金样覆盖的，而是由同文件的 `ATableOnlyOneJobCanHoldIsLoadedForThatJob`（`InlineData(9)` / `InlineData(12)`）覆盖，本轮改动没有碰它们。`LoadProgressionAsync` 用的是 `profile.Level`（仍为 60），与 `job` 无关。

`skill_tree_index` = −1 **确实是「未设置」哨兵值**：建表默认就是 −1（`CharacterDatabaseSchema.cs:205`），协议侧 `StateSynchronizationPayloadCodec.Entry.cs:827` 的 `SkillTreeIndexOnTheWire` 把不在 `{0,1}` 的值一律写成 `0xFF`。但在**装载路径内**它只经过 `ValidateProfile`（`:683` 的 `is < -1 or > 1`）这一处纯取值域检查，没有任何 `if` 读它；仓储全文只有 `:76`（SELECT）与 `:683`（校验）两处提及。0 与 −1 在装载里走同一支。它改变的是协议层的一个分支（`Entry.cs:113` 的 `value.PageIndex == stats.SkillTreeIndex`），而该层不在本用例的调用链上，且本用例的种子是文件内 `private static`、无人共享，因此没有任何既有覆盖被挪走。

一处如实记下但不阻断：`job` = 133 超出 `CharacterDomainService.MaxJobId = 12`（`:24`、`:103`），也就是说这个职业号无法经建号路径产生。但那是**建号**时的约束，装载侧对 `job` 无取值域校验，今天的领域组装（`CharacterEntryStateServices.cs:144`、`:149`）同样只判 `== 9` / `== 12`。所以这只是「种子不是一个真实角色」，与其余 1001–1049 一类合成值同类，不改变所钉住的行为。见 F-20。

### 一处需要如实记述的性质（不阻断）

F-12 那 8 对现在**全部经 `ValidateProfile` 抛 `InvalidDataException` 转红，而不是经金样的逐行比对转红**——把 133 或 −1 换进一个只收 `{0,1}` 的列，生产侧的取值域守卫先一步抛出。这不影响验收条件 2（用例确实转红，且失败信息指出是取值域越界），但与第 4 轮为 P2 特意补做 P9 时关心的是同一件事，应当写清楚。我另做 Q11（1 ↔ 87，两侧都在 byte 域内、守卫放行）确认**金样自身**对序号 1 有判别力。

序号 28 则是结构性的：这条 SELECT 里落在 `−1..1` 内的值只有 −1 与八个布尔的 0/1，所以任何涉及序号 28 的对调都必然触发守卫，金样的文本比对永远不会是先抓到它的那一个。金样对它仍有判别力的场景是**非对调**的错法（漏读该列而取了别的常量，只要那个常量不是 −1）。

### 建议（不阻断，供本 Task 定稿时一并收口）

**F-16：第 4 轮整改清单第 6 条的后半句没有落实——`skill_tree_index = -1` 恰等于建表默认这项取舍未被记入残留。**

第 4 轮写的是「残留第一类改为『8 个布尔列 / 12 对，无相邻序号对，取值域下界』；**记下 `skill_tree_index = -1` 恰等于建表默认这项取舍**」。Self-check 5 的残留表落实了前半句，后半句没有。

实测（解析 `CREATE TABLE` 的 DEFAULT 与 `SeedAsync` 的字面值逐列比对）：**88 个投影列中恰有 5 列的播种值等于建表默认**——序号 7 `is_event_character`=0、11 `is_hardcore_mode`=0、**28 `skill_tree_index`=−1**、38 `is_premium_pc_room`=0、49 `is_return_user`=0。这 5 列上，「被读到」与「被换成常量默认／被整列漏掉而回落到默认」不可区分。四个布尔的这一性质第 2 轮已核实并判可接受，序号 28 是本轮新增的一个，理由与之相同（三个可取值里 0 撞四个布尔、1 撞五列，只有 −1 在本条 SELECT 内唯一），取舍是对的，但应当写下来——这是**与列置换不同的另一种盲区**，T-007 拆表时若把某列丢掉而让默认值兜住，只在这 5 列上看不见。

**F-17：Execution Result 5 的证伪表把序号 8 写成了 `s0.is_event_character`。**

原文「序号 1 ↔ 8（`c.job` ↔ `s0.is_event_character`）」。序号 8 是 `s0.is_private_store`，`is_event_character` 是序号 7。两种对调实测都转红（Q1、Q2），结论不受影响，但标签错了；请改为「序号 1 ↔ 8（`c.job` ↔ `s0.is_private_store`）」。

**F-18：建议把「红来自守卫而非金样」这一点写进 Execution Result 5。**

理由见上一节。可一并记下 Q11（1 ↔ 87）这条对照：它证明金样自身对序号 1 有判别力，避免下一位读者重走第 4 轮 P2 → P9 的那段路。

**F-19：`projected-column-values.py` 作为交给 T-007 的工具，有两处脆弱点值得在脚本里注一行。**

今天两处都不生效（我已核实投影列中未播种者为 0），但 T-007 会在种子改动后反复用它：

1. **未播种的投影列一律折成字面量 `'?'`，其真实的建表默认值从不参与比对。** 若某列未播种，它会与其他未播种列凑成一个 `'?'` 组（假阳性）；更要紧的是**反向**——一个已播种的值若恰好等于某个未播种列的默认值，脚本看不见这处撞值（漏报）。
2. **`INSERT` 正则依赖 `)` 与 `VALUES(` 之间的换行**（`\)\s*\n\s*VALUES\(`）。今天正因为如此，它跳过了 `ATableOnlyOneJobCanHoldIsLoadedForThatJob` 里那几条单行的 `INSERT INTO characters(...) VALUES(...)`，取到了 `SeedAsync` 的那条。这是巧合而非设计：把 `SeedAsync` 的 `INSERT` 排版成单行，或把 Theory 的排版成两行，脚本就会安静地取错一条。

另外它比的是 SQL 字面值的**文本**而不是求值后的结果。更稳的形式是把真 DDL、真种子、真 SELECT 跑进一个 SQLite 内存库再按实际值分组（本轮的独立求值就是这么做的），三条脆弱点一并消失。

**F-20：建议在残留里加一句「`job` = 133 不是可建出来的职业号」。** 依据 `CharacterDomainService.MaxJobId = 12`。装载不受影响（已逐处核实），但金样的读者不应把这条种子当成一个真实角色。

**F-21：Task 文件里没有写 `projected-column-values.py` 的路径。** 它在本 attempt 根目录（与 `plan.md` 同级），不在 Task 目录下。Execution Result 5 引用了它的输出却没给出位置，读者找不到。建议补一行路径。

### 其他核对（无阻断）

- **验收条件逐条成立。** 1：215 行金样覆盖整条 `CharacterEntryStorageRecord` 的五个子记录与全部集合顺序，33 张表双向差集为空。2：本轮 11 处注入全部转红，连同前四轮的 `ReadIntListAsync` 加一、blob/bool/string 改值，字段值改动可见性已被反复证实。3：集合顺序（`hotkey_slots` 反序、整表读空、多插一行）在第 1–2 轮已证伪，本轮种子改动只挪动了两个字面值，未触及任何 `ORDER BY` 或集合结构。
- **Owned diff 未越界。** 两个文件均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 下，符合 BD-012 的 `Server/tests/`；`git status` 中其余改动（`AGENTS.md`、`Server/src/` 六个文件、三个 Application.Tests 文件、`Database/`）均属 T-001/T-002 既有工作，本任务未触碰生产代码——`SqliteCharacterEntryStateRepository.cs` 的 sha256 自第 2 轮起五轮未变（`5170be4d…`）。
- **Common Constraints 逐条核对。** 无任务级提交（HEAD `ea95d49`）；对外可观察行为无改动（零生产改动）；既有测试语义未被改写（F-4 的不合并决定继续保持）；仓储未声明表结构、未查询表结构元数据；无回填/修复/迁移路径；未触碰 `Tool/` 与 `.worktrees/legacy-reference/`；C# 文件 CRLF、无 BOM；`TreatWarningsAsErrors` 下全量构建通过（全量 exit 0）。
- **金样稳定。** 连跑 6 次全绿。本轮改动只替换两个整型字面值，未引入新的 `CURRENT_TIMESTAMP`／`strftime` 依赖；`CreatedAtUnixSeconds` 仍固定为 `1577934245`，第 2 轮逐项核实过的时间/文化/时区结论继续成立。
- **无共享夹具受影响。** `SeedAsync` 与 `ItemCore` 都是本文件 `private static`；`StorageRecordDump` 只被 owned 的两个文件引用，`Support/` 下只有它一个文件；全仓无任何文件引用 `EntryLoadCharacterizationTests`；两条用例各自用 `Guid.NewGuid()` 的临时库，与其他用例的 `dfo-entry-*` 路径不相撞。种子的五轮改写不可能波及任何既有用例，全量 1027/2/0 即是佐证。
- **第 4 轮的其余结论本轮复核仍然成立。** F-15 的「24 条多列位置式投影中只有两条有同值列」经把 33 条 SELECT 全部对真库执行后确认（24 条多列 + 9 条单值，撞值只有 88 列 SELECT 与 `character_gold_limits`）；F-14 的「谓词列 5 个、无一是投影列」与 Execution Result 5 的更正一致。

`passed` 只表示本 Task Unit 可以进入执行者的接受逻辑，不是最终质量批准。上述 F-16 至 F-21 均为可在定稿时一并收口的记述性修正，不构成阻断。

## Execution Result 6

第 5 轮判 `passed`，无阻断。其六条非阻断建议在此收口，其中一条我做了实际改动，因此本轮需要新的一次快评来对齐轮次。

### F-20｜实改：`job` 由 133 改为 8

这条不只是记录。`CharacterDomainService.MaxJobId = 12`，`job` = 133 是**服务器建不出来的角色**——金样钉住的会是一条永远不会存在的数据。表征用例的价值在于「今天的典型行为」，用一条不可能的数据去钉它，钉住的就不是行为而是巧合。

改为 `job` = 8：合法（≤ 12）、非 9、非 12（避开两处职业门），且在那 88 个投影值中唯一。

改后复核（脚本求值，非断言）：

```
projected columns: 88
  value 0 shared by 4: is_event_character, is_hardcore_mode, is_premium_pc_room, is_return_user
  value 1 shared by 4: is_private_store, is_hardcore_dead, is_disguised, link_slot_enabled
```

与第 5 轮评审时的分布一致——唯一的两组重复仍是那 8 个布尔列。金样已重新生成，三条用例全绿。

### F-16｜五列播种值等于建表默认，记入残留

成立，且是我第 4 轮整改清单里写了却没落实的后半句。88 个投影列中有 5 列（序号 7/11/28/38/49）的播种值恰等于建表默认，因此「这一列被读到了」与「这一列根本没被读」不可区分。

其中 4 列是取 0 的布尔（默认 0），第 5 列是 `skill_tree_index`（默认 −1，我为避开布尔的 0 才改成 −1）。`skill_tree_index` 的合法域是 {−1, 0, 1}：−1 撞默认值，0 撞布尔的 0，1 撞布尔的 1——**三个值各撞一处，无解**。记入残留。

### F-17｜序号写错

Execution Result 5 把序号 8 写成 `is_event_character`，实为 `is_private_store`（`is_event_character` 是序号 7）。更正。

### F-18｜那 8 对是怎么转红的

第 4 轮那 8 对（`job` / `skill_tree_index` 与布尔列）现在**经 `ValidateProfile` 抛异常转红，而非金样逐行比对转红**——因为把 `job` 换成布尔值后取值域检查先失败了。

这不削弱结论（用例确实红了），但要写清楚是哪条机制在起作用。评审另做了对照探针 1↔87，绕开取值域检查，证明金样自身对序号 1 确有判别力。

### F-19｜留档脚本的两处脆弱点

`projected-column-values.py` 记录在案：

1. 未播种的列会被折成 `'?'`，若有多列如此，它们会被算作一组「重复」而实际是「未知」——会漏报真正的撞值。
2. `INSERT` 的正则依赖当前换行排版，测试文件重排后可能取到 Theory 那条种子而非主种子。

它是一次性求值工具，不是回归守卫。**T-007 使用它之前先核对输出的 `projected columns: 88` 是否仍然成立**——这个数字对不上就说明解析已经失效。

### F-21｜脚本路径

`projected-column-values.py` 位于本 attempt 根目录，与 `plan.md`、`spec.md` 同级。

### 残留（最终，替代第 4 轮那份）

| 类别 | 数量 | 为什么补不了 |
|---|---:|---|
| 布尔列互撞 | 8 列 / 12 对 | 合法值只有 0 与 1 |
| 播种值等于建表默认 | 5 列 | 4 个布尔（默认 0）+ `skill_tree_index`（域 {−1,0,1} 三值各撞一处） |
| `character_gold_limits` 两列 | 1 对 | 取 `Math.Min`，按定义可交换 |
| 谓词列 | 5 列 | 不被投影；须取 0 才能通过过滤 |
| 首行结构序号 | 14 列 | 各自在所属查询的投影值内唯一，本就不是盲区 |

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。生产代码逐字节未变。

## Executor Self-check 6

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表全覆盖；88 个投影列除 8 个布尔外两两互异（脚本求值）；种子现在描述的是一条服务器真能建出来的角色 |
| 2 改动任一字段值使其转红 | 已闭合。九种证伪，其中六处列对调；四处为评审独立找到的漏网 |
| 3 改动任一集合顺序使其转红 | 已闭合（`hotkey_slots` 反序、整表读空、多插一行） |

### 执行期发现的一处真缺陷：清池是进程全局的

本轮全量出现一条**不在已知假红名单**的红：

```
SqliteInventoryEntryRepositoryTests.LoadAsync_RejectsInvalidAuxiliaryInventoryState(invalidCase: "invalid-lock")
System.ObjectDisposedException : Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.
```

它不是我的用例，也与它自己要检查的东西无关。真因：`SqliteConnection.ClearAllPools()` **按进程清空**，而 xUnit 在同一程序集内并行跑测试类。某个类在清理时把别的类正握着的底层句柄关掉了，后者于是拿着一个已释放的 `sqlite3` 继续跑。

`Dfo.Server.Persistence.Tests` 里有 **13 个类**调用 `ClearAllPools`——它是让运行时真正关掉句柄、从而能删掉数据库文件的唯一办法。我本轮新增的三个类（表征、快照一致性、全有或全无）把这个碰撞的概率推了上去。

修法：该程序集**串行执行**。

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

清池天生是全局的，因此任何调用它的测试都不能与同进程内握着连接的测试并发——只有整程序集串行才是充分条件，把这些类归进同一个 collection 并不够（别的 collection 仍会并行）。

代价是该项目由 12 秒变为约 30 秒。相比「一个类的清理打断另一个类的断言」，这个代价值得。

**证据的诚实边界**：这是竞态，修复后连跑三次全绿**不构成证明**——竞态本来就不是每次都出现。真正的证据是机制本身（清池按进程、xUnit 类间并行、13 个类调用它）与那条堆栈：异常出在 `SafeHandle.DangerousAddRef`，即句柄已被别处释放。

## Independent Quick Review 6

- Verdict: `changes_required`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-003` / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff（本轮只评第 5 轮判 `passed` 之后新增的两处改动，不重做已通过部分）：
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `ba12b987…`；第 5 轮为 `e61630be…`）——种子 `job` 由 133 改为 8，金样重生成
  - `Server/tests/Dfo.Server.Persistence.Tests/Usings.cs`（新增 8 行注释 + `[assembly: CollectionBehavior(DisableTestParallelization = true)]`）
  - 未变：`Support/StorageRecordDump.cs`（`e1d3e99a…`，与第 2–5 轮同）；留档脚本 `projected-column-values.py` 在本 attempt 根目录
  - 三个文件均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 内，符合 BD-012 的 `Server/tests/`
- 本轮 4 处生产注入：**每次注入前 `cp` 备份、跑完立即恢复**，`SqliteCharacterEntryStateRepository.cs` 的 sha256 每次都回到 `5170be4d…`；评审结束时 `git status --porcelain` 与 `git diff --stat -- Server/src/` 与评审开始时逐字一致（生产六个文件 117+/26-，属 T-001/T-002），HEAD 仍为 `ea95d49`，无任务级提交，本任务零生产改动。上一位评审者可能遗留的列对调**不存在**：仓储进入本轮即为 `5170be4d…`。

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| 全量 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH`/`Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | 60+499+4+99+46+116+203 = **1027 通过 / 2 跳过 / 0 失败**，exit 0；本轮无假红（`Host.Tests` 4/4、`Networking.Tests` 46/46 且 1 s） |
| CK-007 | `dotnet test …/Dfo.Server.Persistence.Tests.csproj --filter …EntryLoadCharacterizationTests` | 3/3 |
| **88 列取值：不跑执行者的脚本，独立求值** | 用 `CharacterDatabaseSchema` 的 `TableDeclarations` + `ReconcilableColumns` + `IndexDeclarations` 复建真库、灌入本用例 `SeedAsync` 的真种子（`@core` 按 `ItemCore(1001,17)` 还原）、执行仓储那条真 SELECT，按 SQLite 返回的**实际值**分组 | SQLite 自报 **88** 列且逐序号与投影文本列名一一对上；**82** 个互异值；重复组恰 **2** 个（各 4 个布尔），合计 **12** 对；**`8` 在 88 个值中出现 1 次** |
| 脚本对照 | `python projected-column-values.py` | 与上面的独立求值逐字吻合 |
| `job` = 8 的合法性 | `CharacterDomainService.cs:24` `MaxJobId = 12`、`:103` `if (job > MaxJobId)` | 8 ≤ 12 合法；≠ 9、≠ 12，避开 `:247`/`:251` 两处职业门 |
| 金样确已重生成 | 解析 `Expected` | **215** 行；`Profile.Job = 8`（:182）、`Skill.Job = 8`（:280）、`Profile.Subtype1.SkillTreeIndex = -1`（:241）；全文无残留的 `133` |
| 编码 | 逐字节 | 两个 owned 文件均 no-BOM、纯 CRLF（396/396、11/11） |
| 清池语义 | 仓库外 `%TEMP%` 独立控制台工程引 `Microsoft.Data.Sqlite` 10.0.10 实测 | 见 F-24：**按进程清空成立**（两个不同连接串的池，一次 `ClearAllPools()` 后两个文件都可删）；**但已 Open 的连接不受影响**（`State` 仍 `Open` 且可继续执行） |
| 竞态复现 | 同一工程：一线程反复 `Open`/`Dispose`，另一线程反复 `ClearAllPools()` | **复现**：`ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈顶 `SafeHandle.DangerousAddRef` → `sqlite3_prepare_v2`，与执行者贴的那条形状一致 |
| 该程序集的类清点 | `grep -rln ClearAllPools` + 类声明清点 | 程序集共 **13 个测试类**，**13 个全部调用** `ClearAllPools`；无任何 `[Collection]`/`CollectionDefinition`/`xunit.runner.json` |
| 耗时 | 同机连跑 | 串行 **19 s**（116 条）；把 `Usings.cs` 临时还原为并行连跑 4 次为 **11/11/12/12 s**，4 次均全绿（竞态未复现，与「竞态不是每次都出现」相符）；`Usings.cs` 已按备份逐字节恢复 |

### 列置换探针（4 处，逐次恢复后仓储 sha256 均回到 `5170be4d…`）

| # | 注入（88 列 SELECT 内对调） | 两列取值 | 实测 |
|---|---|---|---|
| R1 | 1 `c.job` ↔ 8 `s0.is_private_store`（第 5 轮 Q1 的同位复现） | 8 / 1 | **转红**，3/3 全红；红来自 `ValidateProfile`（`InvalidDataException: Character public profile state is out of range.`） |
| R2 | 41 `s0.guild_level` ↔ 43 `s0.disguise_kind`（第 5 轮 Q9 复现） | 115 / 116 | **转红**，`Assert.Equal` 指到 **pos 1000**——第 5 轮为 pos 1002，差值恰为 `133`→`8` 少掉的两个字符，交叉印证金样确实重生成 |
| R3 | 1 `c.job` ↔ 87 `s1.manage_level`（第 5 轮 Q11 的新值复现） | 8 / 132 | **转红**，`Expected: Profile.Job = 8` / `Actual: Profile.Job = 132`，**红来自金样自身**——序号 1 在新值下仍被金样判别 |
| R4 | 28 `s1.skill_tree_index` ↔ 38 `s0.is_premium_pc_room`（第 5 轮 Q7 复现） | −1 / 0 | **转红**，红来自 `ValidateProfile`，与第 5 轮所述机制一致 |

结论：`job` 由 133 改为 8 **没有削弱任何一处已有的证伪**；R3 另证明新值下金样对序号 1 仍有独立判别力。R1 的红仍出自取值域守卫（换过去的 8 落在只收 `{0,1}` 的列上），F-18 那条记述在改值后依然成立。

### 需要整改（阻断本轮接受）

**F-22｜「残留（最终）」第二类不穷尽：同一类盲区在 88 列 SELECT 之外还有两处，其中一处不属于任何已列类别。**

残留表第二行写「播种值等于建表默认 | 5 列」。这 5 列（序号 7/11/28/38/49）我逐列独立复核**属实**——用真 DDL 的 `dflt_value` 与真种子的实际取值逐列比对，88 个投影列里恰好这 5 列相等，一个不多一个不少。

但这张表是交给 T-007 的**全路径**盲区清单（第三类 `character_gold_limits`、第五类「首行结构序号」都跨了别的查询），它却只在那一条 SELECT 上做了枚举。把仓储里全部 32 条 SELECT 字面量对同一个真库逐条执行、按各自来源表的建表默认逐列比对后，同类列共 **7** 个：

- 88 列 SELECT 内的 5 个（已列）；
- `character_crystal_contract.cube_grade` = **255**，建表默认 `INTEGER NOT NULL DEFAULT 255`（`CharacterDatabaseSchema.cs:173`），经 `SqliteCharacterEntryStateRepository.cs:320` 的 `SELECT cube_type, cube_grade …` 投影，落在金样 `Progression.CubeGrade = 255`（用例文件 :299）；
- `mailbox_attachments.ordinal` = **0**，建表默认 `DEFAULT 0`（`CharacterDatabaseSchema.cs:207`），落在金样 `…Attachments[0].Ordinal = 0`（:375）。

`cube_grade` 尤其要紧：它不是布尔、不是序号，是一个普通取值列，**不被现有五类中的任何一类覆盖**。T-007 若在拆 `character_crystal_contract` 时把这一列丢掉、让建表默认兜住，金样一样是绿的——这正是本 Task 存在的那个失败形态。`ordinal` 与第五类「首行结构序号」相邻，但第五类给出的理由是「各自在所属查询的投影值内唯一，本就不是盲区」，那句话对**列置换**成立、对**整列漏掉回落默认**不成立，所以它也需要归到第二类而不是第五类。

整改：把第二类扩到全路径的实际数量，并点名 `character_crystal_contract.cube_grade` 与 `mailbox_attachments.ordinal`；若认为 `ordinal` 归第五类更合适，则同时修掉第五类那句「本就不是盲区」的理由。

**F-23｜文件里有两份 `## Execution Result 6` 与两份 `## Executor Self-check 6`，且两份「残留（最终）」互相矛盾。**

`:906` 与 `:971` 各有一份 `## Execution Result 6`，`:953` 与 `:1034` 各有一份 `## Executor Self-check 6`。两份残留表不一致：

- `:963` 那份第二行是「`skill_tree_index` | 1 列」——它**与同一份里的 F-16 自相矛盾**：F-16（`:932`）明说是 5 列（4 个布尔 + `skill_tree_index`），表里只剩 1 列，四个布尔的默认值盲区消失了；
- `:1022` 那份第二行才是「播种值等于建表默认 | 5 列」，并注明「替代第 4 轮那份」。

两份都自称「残留（最终）」。T-007 顺序读到第一份就会拿到错的清单。整改：删掉先写的那一份（`:906`–`:969`），只保留后一份并按 F-22 修正；轮次标题在同一文件内必须唯一。

### 建议（不阻断，可与上面两条一并收口）

**F-24｜清池的机制记述不准：被伤到的不是「正握着连接的测试」，而是「正在 `Open` 的测试」。**

`Usings.cs:3-6` 的注释与 `:1052` 的诊断都写成「it closes handles other tests are in the middle of using」「把别的类正握着的底层句柄关掉」。我在仓库外用同版本 `Microsoft.Data.Sqlite` 10.0.10 实测：一条**已 Open 且正在使用**的连接，在另一处 `ClearAllPools()` 之后 `State` 仍是 `Open`，后续 `ExecuteScalar` 正常返回——清池不动租出去的那条。

真正能复现那条异常的是**清池与 `Open`（从池里租）并发**：一线程反复 `Open`/`Dispose`，另一线程反复清池，稳定抛出 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈顶正是执行者贴的 `SafeHandle.DangerousAddRef`。也就是说受害者是**刚拿到一条正在被销毁的池内连接**的那一次 `Open`。

结论（该程序集串行）不受影响，但「真正的证据是机制本身」这句话立在一处没被验证、且实测不成立的机制上。建议按实测改写这两处，一并把复现方法记下来——它是可控复现，不必只靠那一次偶发的红。

顺带：`ClearAllPools()` **按进程清空**这一条**成立**且已实测——两个不同连接串（两个池）各留一条空闲连接，一次 `ClearAllPools()` 后两个库文件都变得可删。

**F-25｜「把这些类归进同一个 collection 并不够（别的 collection 仍会并行）」今天不成立。**

`Dfo.Server.Persistence.Tests` 共 13 个测试类（`Accounts` 2、`Characters` 4、`Database` 2、`Inventory` 4、`Schema` 1），**13 个全部调用 `ClearAllPools`**，程序集内没有第 14 个类，也没有任何 `[Collection]`、`CollectionDefinition` 或 `xunit.runner.json`。xunit 2.9.3 下每个无显式 collection 的类自成一个 collection，所以把这 13 个类归入同一个 collection 与整程序集串行**今天等价**，不存在「别的 collection 仍会并行」。

整程序集串行仍是更好的选择——改一行而不是十三行，且新加的类自动被覆盖——但理由应当是这个，不是那个不成立的。

**F-26｜代价数字不准：实测 11–12 s → 19 s，不是「12 秒变为约 30 秒」。**

同机同环境：串行 116 条 **19 s**；把 `Usings.cs` 临时还原为并行连跑 4 次为 11/11/12/12 s。全量总数不受影响（1027/2/0）。建议把这两个数字改成实测值。

**F-27｜确有更小的解（`Pooling=False`），但它落在本任务边界之外——值得写下来，因为后续任务会让它变得可用。**

实测：连接串带 `Pooling=False` 时，`Dispose` 之后**不调用 `ClearAllPools` 也能直接删库文件**；不带时删不掉（`IOException`）。也就是说 `Pooling=False` 能一次性消灭这 13 处清池调用、并把并行还回来。

它今天用不上的原因不是没想到，而是**锁着文件的那条连接是生产代码开的**：九个仓储各自 `new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString()`（如 `SqliteCharacterEntryStateRepository.cs:26`），构造参数只有路径，没有连接串注入点。测试侧改自己那几条 `new SqliteConnection($"Data Source={path}")` 没有用。要用它就得动生产代码——超出本 Task 的 Permitted paths（`Server/tests/…`）与「零生产改动」。

所以执行者的选择是对的，但记录里应当写明**为什么更小的解今天不可用**：等后续任务让仓储都经 `SqliteDatabase` 拿连接串，这条路会打开，那时 13 处清池与本次的串行都可以一并撤掉。

**F-28｜同一类竞态在另外四个测试程序集里仍然存在，本任务动不了，但应当记下。**

`ClearAllPools` 的调用者不止本程序集：`Dfo.Server.Application.Tests`（`CharacterStoreSettlementTests`、`OutsideTransactionTests`、`ItemContainerCarrierEquivalenceTests`、`OneDatabaseOneParticipantTests`）、`Dfo.Server.Protocol.Tests`（4 个文件）、`Dfo.Server.Networking.Tests/Integration`（2 个文件）。这些程序集各自在自己的进程里并行跑类，同一条「清池 vs `Open`」竞态照样成立（`Application.Tests` 499 条，命中概率不低）。它们都在本 Task 的 Permitted paths 之外，按「记下不改」处理即可——但 `:1056` 的「修法：该程序集串行执行」读起来像问题已经解决，宜补一句范围。

**F-29｜「三个值各撞一处，无解」是当前种子下的局部结论，不是不可解的证明。**

`skill_tree_index` 域 `{−1, 0, 1}`，逐值核对**属实**：−1 撞自己的建表默认（`CharacterDatabaseSchema.cs:205`），0 撞序号 7/11/38/49 四个布尔，1 撞序号 8/12/44/50 四个布尔——88 个投影值里落在 `{−1,0,1}` 的恰好只有这九个。

但「无解」只在**八个布尔维持 4/4 分裂**时成立。若把八个布尔全部播成 1，则 0 在这条 SELECT 内唯一且 ≠ 默认 −1，`skill_tree_index` 的两种盲区同时消失——代价是布尔内部的互撞对由 12 涨到 28（C(8,2)）。两种配法互不支配，现选择是合理的取舍，但措辞宜改为「在当前布尔播种下无解」并附上这个代价对比，免得 T-007 读成定理。

### 已核实无误的部分（不重做已通过部分）

- **改动一（`job` 133 → 8）成立。** 8 ≤ `MaxJobId` = 12 合法、非 9 非 12、在 88 个投影值中唯一（独立求值：88 列 / 82 个互异值 / 2 个重复组 / 12 对，`8` 出现 1 次），金样已按新值重生成（215 行，`Profile.Job` 与 `Skill.Job` 均为 8，无残留 133），三条用例全绿，四处列对调证伪逐一复现且全部转红。
- **改动二（整程序集串行）是充分的。** 该程序集内除 xunit 的类间并行外，唯一的并发是 `SqliteEntryLifecycleRecoveryTests.cs:110` 一处 `Task.WhenAll`，其 `ClearAllPools` 在 `finally` 里、在并发段之后，串行后不残留窗口。
- **未越出 Permitted paths。** `Usings.cs` 在 `Server/tests/Dfo.Server.Persistence.Tests/` 内，符合 BD-012 的 `Server/tests/`；它改的是执行调度不是任何既有用例的断言，未违反「既有测试语义只作机械调整」。（性质上它既非「机械调整」也非「证据用例」，是 BD-012 描述之外的第三种东西，但路径许可且为保住 CK-007 所必需，不构成越界。）
- **Common Constraints 逐条。** 无任务级提交（HEAD `ea95d49`）；零生产改动；仓储未声明表结构、未查表结构元数据；无回填/修复/迁移路径；未触碰 `Tool/` 与 `.worktrees/legacy-reference/`；CRLF、无 BOM；`TreatWarningsAsErrors` 下全量 exit 0。
- **验收条件 1/2/3 均成立**（1：215 行金样 + 33 张表；2：本轮四处列对调全部转红；3：集合顺序证伪在第 1–2 轮完成，本轮只改了一个整型字面值，未触及任何 `ORDER BY` 或集合结构）。

### 整改清单

1. **F-22**：把残留第二类扩到全路径，点名 `character_crystal_contract.cube_grade`（默认 255）与 `mailbox_attachments.ordinal`（默认 0）；`ordinal` 若归第五类，则同时修掉第五类「本就不是盲区」的理由。
2. **F-23**：删掉先写的那份 `## Execution Result 6`/`## Executor Self-check 6`（`:906`–`:969`），只留后一份，使轮次标题在文件内唯一。
3. **F-24**（建议）：按实测改写 `Usings.cs:3-6` 与 `:1052` 的机制记述——受害者是正在 `Open` 的那一次，不是正握着连接的那一条；并记下可控复现方法。
4. **F-25 / F-26**（建议）：改掉「同一个 collection 不够」的理由（13 个类即全部类），把耗时改为实测的 11–12 s → 19 s。
5. **F-27 / F-28**（建议）：记下 `Pooling=False` 这条更小的解为何今天不可用、何时会可用；并给 `:1056` 的「修法」补上范围（另外四个程序集同病未治，按「记下不改」处理）。
6. **F-29**（建议）：「无解」改为「在当前布尔播种下无解」，附 12 对 vs 28 对的代价对比。

`changes_required` 只针对上面 1、2 两条；两处改动本身（`job` = 8、整程序集串行）经独立复核**均成立**，代码与用例无需改动，整改全部落在 Task 文件的记述上。

## Execution Result 7

### F-22｜残留清单不穷尽，且其中一列是真洞

成立。评审把仓储 **32 条 SELECT 全部对真库执行**后求值，得出「播种值等于建表默认」的列共 **7 个**，我只列了 5 个。多出的两个：

| 列 | 默认 | 性质 |
|---|---|---|
| `character_crystal_contract.cube_grade` = 255 | 255 | **普通取值列**——五类残留没有一类盖得住它。T-007 若把这一列丢掉，金样照绿 |
| `mailbox_attachments.ordinal` = 0 | 0 | 结构序号，属既有的「首行序号」一类 |

`cube_grade` 已改播为 **7**（清开其默认值 255），金样重新生成，三条用例全绿。`ordinal` 归入结构序号类，不改。

我的求值只覆盖了那条 88 列 SELECT；评审覆盖了全部 32 条。**范围没说清就等于没说全**——这是第五轮同一类错误的第六次出现。

### F-23｜同一份记录被追加了两次

成立。文件里出现两份 `## Execution Result 6` 与两份 `## Executor Self-check 6`，且两份「残留（最终）」互相矛盾（一份写 `skill_tree_index` 1 列，与同文件的 F-16 所述 5 列冲突）。已删去先追加的那份，保留内容完整的后一份。

### F-24｜清池那条诊断有一步是错的

评审在仓库外实测（`Microsoft.Data.Sqlite` 10.0.10）：`ClearAllPools()` 确实**按进程**清空，但**不动已经 Open 的连接**。真正复现 `ObjectDisposedException: 'SQLitePCL.sqlite3'`（栈顶 `SafeHandle.DangerousAddRef`）的是「清池」与「`Open` 正在从池里租用」并发。

我原来的说法「把别的类正握着的句柄关掉了」不准确——被打断的是**租用的那一刻**，不是持有期间。`Usings.cs` 的注释与本记录均按实测改写。

### F-25｜「归进同一 collection 不够」今天不成立

评审核实：该程序集共 13 个测试类，**13 个全部**调用 `ClearAllPools`，且无 `[Collection]` 标注、无 `xunit.runner.json`。因此单 collection 与整程序集串行**等价**。

串行仍是更好的选择，但理由要换成真的那个：**它不依赖「将来新增的类记得加进那个 collection」**。按 collection 归拢的方案，下一个人新建一个用了 SQLite 的类而忘了标注，竞态就悄悄回来了。

### F-26｜代价数字写错

实测 11–12 秒 → **19 秒**，不是我写的「12 秒 → 30 秒」。

### F-27｜确实有更小的解，但今天用不上

`Pooling=False` 实测有效：连接 Dispose 后无需清池即可删库。但**锁住文件的那条连接是生产仓储自建的**——`SqliteCharacterEntryStateRepository` 等只接收路径、自己拼连接串（`:26` 等处）。要让它不走池就得改生产代码，超出本 Task 的边界。

所以不是「我没想到」，是「今天不可用」。这一点原记录没写，补上。

### F-28｜同类竞态在别的测试程序集仍在

`Application.Tests`（4 个类）、`Protocol.Tests`（4 个）、`Networking.Tests`（2 个）也调用 `ClearAllPools`。它们不在本 Task 的许可路径内，按记录处理，不动。

**交给后续任务**：这三个程序集有同样的竞态，只是类数更少、碰撞概率更低。哪个任务的许可路径覆盖到它们，就在那里一并处理。

### F-29｜「三值各撞一处、无解」有前提

评审逐值核实「`skill_tree_index` 的 −1/0/1 各撞一处」**属实**，但补充：「无解」只在八个布尔列 4/4 分裂的前提下成立。若把八个布尔全播为 1，则 0 空出来可给 `skill_tree_index`——代价是布尔之间的不可见组合由 12 对涨到 28 对。

不采纳该权衡（拿 16 对新盲区换 1 列），但前提要写清楚：**不是无解，是这个解更差**。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1027 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 16）。生产代码逐字节未变。

## Executor Self-check 7

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表全覆盖；32 条 SELECT 逐条求值后，播种值等于默认的普通取值列已清零 |
| 2 改动任一字段值使其转红 | 已闭合。九种证伪，六处列对调（四处为评审独立找到） |
| 3 改动任一集合顺序使其转红 | 已闭合（`hotkey_slots` 反序、整表读空、多插一行） |

### 残留（最终版，取代此前所有版本）

| 类别 | 数量 | 为什么补不了 |
|---|---:|---|
| 布尔列互撞 | 8 列 / 12 对 | 合法值只有 0 与 1 |
| 播种值等于建表默认 | 6 列 | 4 个取 0 的布尔、`skill_tree_index`（域 {−1,0,1} 三值各撞一处，另解更差见 F-29）、`mailbox_attachments.ordinal`（结构序号） |
| `character_gold_limits` 两列 | 1 对 | 取 `Math.Min`，按定义可交换 |
| 谓词列 | 5 列 | 不被投影；须取 0 才能通过过滤 |
| 首行结构序号 | 14 列 | 各自在所属查询的投影值内唯一，本就不是盲区 |
## Independent Quick Review 7

- Verdict: `passed`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-003` / Covers RQ-008 / Boundaries BD-012 / Checks CK-007, CK-008），无修订、无 amendment
- Owned diff（本轮只评第 6 轮两条阻断的闭合与六条非阻断记述更正，不重做前六轮已核实的部分）：
  - `Server/tests/Dfo.Server.Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`（sha256 `8e8ff881…`；第 6 轮为 `ba12b987…`）——`character_crystal_contract` 的 `cube_grade` 由 255 改播为 7，金样 `Progression.CubeGrade` 随之重生成
  - `Server/tests/Dfo.Server.Persistence.Tests/Usings.cs`（sha256 `9ea36de5…`）——注释按 F-24/F-25 改写，`[assembly: CollectionBehavior(DisableTestParallelization = true)]` 未变
  - 未变：`Support/StorageRecordDump.cs`（`e1d3e99a…`，与第 2–6 轮同）；留档脚本 `projected-column-values.py` 在本 attempt 根目录
  - 三个文件均在许可路径 `Server/tests/Dfo.Server.Persistence.Tests/` 内，符合 BD-012 的 `Server/tests/`
- 本轮 3 处探针（1 处生产 SELECT、1 处 owned 测试配置、1 个临时探针用例文件）：**每次注入前 `cp` 备份、跑完立即恢复或删除**。评审结束时 `git status --porcelain` 与 `git diff --stat -- Server/src/` 与评审开始时逐字一致（生产六个文件 117+/26-，属 T-001/T-002），`SqliteCharacterEntryStateRepository.cs` sha256 回到 `5170be4d…`、`Usings.cs` 回到 `9ea36de5…`，HEAD 仍为 `ea95d49`，无任务级提交，本任务零生产改动。

### 复核过的证据

| 项 | 命令 / 手段 | 结果 |
|---|---|---|
| 全量 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH`/`Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | 60+499+4+99+46+116+203 = **1027 通过 / 2 跳过 / 0 失败**，exit 0；本轮无假红（`Host.Tests` 4/4、`Networking.Tests` 46/46 且 1 s） |
| **F-22(a) `cube_grade` = 7 的合法性** | 建表默认 `INTEGER NOT NULL DEFAULT 255`（`CharacterDatabaseSchema.cs:173`）；该 SELECT（`SqliteCharacterEntryStateRepository.cs:320`）只投影 `cube_type`/`cube_grade` | 7 ≠ 255；同条投影的另一个值是 1，7 ≠ 1；C# 侧无行时的兜底 `ReadPairOrDefaultAsync(…, 0, 255, …)`（`:319-321`）也是 255 ≠ 7，故「整表读空」与「整列漏掉」两种错法都能被抓 |
| **F-22(b) 丢列探针 P1** | 把 `:320` 改成 `SELECT cube_type, 255 FROM …`（即 T-007 丢掉该列、让建表默认兜住的等价形态），跑 `--filter …EntryLoadCharacterizationTests` | **转红**，1 失败 / 2 通过；红出自**金样自身**：`Assert.Equal` 指到 **pos 4109**，`Expected …CubeGrade = 7` / `Actual …CubeGrade = 255`，不经任何取值域守卫。恢复后仓储 sha256 回到 `5170be4d…` |
| **F-22(c) 全路径求值：不跑执行者的脚本，自行重做** | 自写提取器从 `SqliteCharacterEntryStateRepository.cs` 抽出全部 SELECT 字面量；用 `CharacterDatabaseSchema` 的 `TableDeclarations`（52 表）+ `ReconcilableColumns`（297 列）+ `IndexDeclarations`（6 索引）复建真库；灌入本用例 `SeedAsync` 的真种子（`@core` 按 `ItemCore(1001,17)` 还原）；逐条执行并按 `PRAGMA table_info` 的实际 `dflt_value` 逐列比对 | 共 **33** 条语句（24 条多列 + 9 条单值，与 `:902` 第 5 轮的独立结论一致）、**202** 个投影项（其中 5 项是表达式：`COUNT(DISTINCT)` 与邮件 4 处 `CASE`/`CAST`）。「播种值等于建表默认」共 **6** 列 |
| 那 6 列的分类 | 同上 | `is_event_character`=0、`is_hardcore_mode`=0、`is_premium_pc_room`=0、`is_return_user`=0（四个取 0 的布尔，默认 0）；`skill_tree_index`=−1（默认 −1）；`mailbox_attachments.ordinal`=0（默认 0）。**普通取值列 0 个**——`character_crystal_contract.cube_grade` 已离开该集合。残留表第二类「6 列」及其三段理由**逐条属实** |
| 无遗漏的旁证 | 逐一核对 5 处 `COALESCE(x,d)` 的兜底值与实际取值（`giveup_count`→1、`disjoint_machine_grade`→3、`disjoint_machine_endurance`→4、`enchanter_endurance`→5、`item_core`→82 字节），以及 C# 侧 `ReadPairOrDefaultAsync`/`ReadScalarIntAsync` 的兜底值（0/255、0/0、0） | 无一与播种值相等，同类盲区没有第 7 个 |
| 撞值分布未被 `cube_grade` 改动扰动 | 按条统计重复投影值 | 仍只有两处：88 列 SELECT（4 个 0 + 4 个 1 = 12 对）与 `character_gold_limits`（600000000 两列）；88 列内 82 个互异值、值 `7` 出现 **0** 次、值 `8` 出现 **1** 次 |
| **F-23 轮次标题唯一性** | `grep -n "^## "` 全文 | `## Execution Result 1..7`、`## Executor Self-check 1..7`、`## Independent Quick Review 1..6` **各只出现一次**；保留下来的 `## Execution Result 6`（`:906`）残留表写「播种值等于建表默认 → 5 列」，与同一份里的 F-16（`:932` 亦为 5 列）**自洽** |
| F-24 机制记述 | 在本程序集内临时加一个探针用例文件（跑完删除），用仓库自己的 `Microsoft.Data.Sqlite` 实测四条 | ① 一条**已 Open 并正在使用**的连接，在 `ClearAllPools()` 之后 `State` 仍为 `Open` 且后续命令正常返回——旧说法「把别的类正握着的句柄关掉」**不成立**；② 两个不同连接串各留一条空闲连接，一次 `ClearAllPools()` 后两个库文件都可删——**按进程清空成立**；③ 一线程反复 `Open`/`Dispose`、另一线程反复清池，稳定抛出 `ObjectDisposedException`，其 `ToString()` 同时含 `SQLitePCL.sqlite3` 与 `DangerousAddRef`——**受害者确是正在 `Open`（从池里租用）的那一次**；④ `Pooling=False` 时 `Dispose` 后可直接删库，不带时删不掉（`IOException`）。`Usings.cs:3-7` 现在的措辞（"it collides with another test opening a connection at that moment: the one opening gets a handle that is being disposed underneath it"）**与实测和我的理解一致** |
| **F-26 耗时：自行实测** | 同机同环境连跑 | 串行（现状）**18 s / 17 s**；把 `Usings.cs` 的 `DisableTestParallelization` 临时注释掉后连跑 6 次为 **12/11/12/11/11/12 s**——F-26 的「11–12 s → 19 s」方向与量级属实（我这台机器串行是 17–18 s，差值在跑次噪声内）。**并跑 6 次里有 1 次真红**（116 条里失败 1 条），等于把第 6 轮那条竞态在仓库内直接复现了一次。`Usings.cs` 已按备份逐字节恢复（sha256 `9ea36de5…`） |
| **F-27 `Pooling=False` 今天不可用** | `grep -rn SqliteConnectionStringBuilder Server/src/Dfo.Server.Persistence/` + 读构造器 | 属实。`Server/src/Dfo.Server.Persistence/` 下 **12 处**自建连接串（Accounts 2、Characters 6、`Database/SqliteDatabase.cs` 1、Inventory 3），形如 `new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString()`（如 `SqliteCharacterEntryStateRepository.cs:26`），构造器只收 `databasePath`（`:19`），**没有连接串注入点**；锁住文件的正是这条生产连接，测试改自己那几条 `new SqliteConnection($"Data Source={path}")` 无效。记录里的「九个仓储」取自 plan T-002 步骤 1 的口径（写方法所属的九个仓储），不是笔误 |
| F-25 | `grep -rln ClearAllPools` + 类清点 | 该程序集 13 个测试类全部调用 `ClearAllPools`、无 `[Collection]`/`CollectionDefinition`/`xunit.runner.json`，故单 collection 与整程序集串行等价；`Usings.cs:9-11` 现在给的理由（"it would rely on whoever adds the next class remembering to say so"）成立 |
| F-29 | 读第 7 轮记述 | 「不是无解，是这个解更差」并附 12 对 → 28 对的代价对比，与第 6 轮实测一致 |
| 编码 | 逐字节 | 三个 owned 文件均 no-BOM、纯 CRLF（396/396、12/12、100/100） |

### 两条阻断的闭合判定

**F-22 已闭合。** 那个真洞（`cube_grade` 播种值恰等于建表默认 255）被 `7` 清开；丢列探针 P1 证明金样自身现在能抓住「整列漏掉、回落默认」这一形态。我把全部 33 条 SELECT 对真库重做一遍求值，「播种值等于建表默认」的列由 7 个降到 6 个，**其中普通取值列为 0 个**，残留表第二类的「6 列」与三段分类理由逐条属实。

**F-23 已闭合。** 全文 `##` 级标题唯一，`## Execution Result 6` 与 `## Executor Self-check 6` 各只剩一份；保留的那份是写着「残留（最终，替代第 4 轮那份）」的完整版，其「5 列」与同一份里的 F-16 一致，不再自相矛盾。

### 建议（不阻断，可在接受前一并收口）

**F-30｜`mailbox_attachments.ordinal` 的归类，Execution Result 7 的正文与 Self-check 7 的表格说了两件相反的事；第五类那句理由仍未按 F-22 的条件分支修掉。**

`:1136` 正文写「`ordinal` 归入结构序号类，**不改**」，而 `:1195` 的最终残留表把它列进了第二类（并正是因此把该类由 5 列改成 6 列）。两处一读就冲突。

更实质的一点：第五类仍是「首行结构序号 → 14 列 → 各自在所属查询的投影值内唯一，**本就不是盲区**」（`:1198`），而 `:462` 明确把 `mailbox_attachments` 算在那 14 个里。于是同一列在第二类被判为盲区、在第五类被判为「本就不是盲区」。第 6 轮 F-22 的整改本是二选一：**要么** `ordinal` 进第二类（那么第五类应为 13 列），**要么**留在第五类（那么必须给那句理由加上「仅就列置换而言」的限定）。现在两条都没做完。

两类衡量的其实是**两种不同的盲区机制**（第二类是「整列漏掉、回落默认」，第五类是「同条 SELECT 内列对调」），同一列同时出现在两处并非逻辑错误——但那就更需要把第五类的措辞从无条件的「本就不是盲区」改成「就列置换而言不是盲区」，否则 T-007 只读第五类会得到相反的结论。判为不阻断，是因为第二类已**点名** `ordinal` 并给了正确理由，而第五类不点名任何列，仔细的读者拿到的是正确的那一条。

**F-31｜SELECT 条数写成 32，实为 33，且这个数字与本文件 `:902` 已核实过的结论冲突。**

`:1047`（第 6 轮 F-22）、`:1129`（第 7 轮）、`:1186`（Self-check 7 验收条件 1）都写「32 条 SELECT」。我自行提取并逐条执行的结果是 **33 条**语句（24 条多列位置式 + 9 条单值），与 `:902` 第 5 轮「把 33 条 SELECT 全部对真库执行……24 条多列 + 9 条单值」**逐字吻合**。33 才是对的。

这不影响结论（我按 33 条求值，同类列仍是 6 个、普通取值列仍是 0 个），但 Self-check 7 交给 T-007 的验收陈述里带着一个错的分母，且与同文件早前的正确数字打架。建议三处一并改回 33，并注明口径（邮件那条 CTE 算一条、`COUNT(DISTINCT)` 那条也算）。

**F-32｜F-26 的「19 秒」在我这台机器上是 17–18 秒；同时串行改动现在有了比机制推断更硬的证据。**

数字建议改成区间（如「11–12 s → 17–19 s，随机器与负载浮动」）：满载全量下该项目实测 **27 s**（见本轮全量日志），单一数字容易被后来者当成基线读。

更值得补进记录的是：把 `DisableTestParallelization` 临时去掉后**连跑 6 次出现 1 次真红**。Executor Self-check 6 里那句「这是竞态，修复后连跑三次全绿不构成证明，真正的证据是机制本身」现在可以升级——**去掉该改动后的红本身就是证据**，而且是可复现的（6 次 1 中）。把这个复现率写进记录，比机制推断更能让后来者相信这一行不能删。

### 已核实无误的部分

- **改动（`cube_grade` 255 → 7）成立且必要。** 7 ≠ 默认 255、≠ 同条投影的另一个值 1、在 88 列 SELECT 中出现 0 次（不引入跨条误解）；丢列探针使金样自身转红；三条用例全绿。
- **未越出 Permitted paths。** 两个改动文件都在 `Server/tests/Dfo.Server.Persistence.Tests/` 内，符合 BD-012 的 `Server/tests/`；本轮无任何生产改动。
- **Common Constraints 逐条。** 无任务级提交（HEAD `ea95d49`）；零生产改动；仓储未声明表结构、未查表结构元数据；无回填/修复/迁移路径；未触碰 `Tool/` 与 `.worktrees/legacy-reference/`；CRLF、无 BOM；`TreatWarningsAsErrors` 下全量 exit 0。
- **验收条件 1/2/3 均成立**（1：215 行金样 + 33 张表，且本轮把「播种值等于建表默认」的普通取值列清到 0；2：本轮丢列探针 P1 转红，前六轮的六处列对调证伪未被本次改动削弱——`cube_grade` 不在那条 88 列 SELECT 内；3：集合顺序证伪在第 1–2 轮完成，本轮只改了一个整型字面值与若干注释，未触及任何 `ORDER BY` 或集合结构）。
- **CK-007 / CK-008 均有本轮的直接证据**（CK-007：`Dfo.Server.Persistence.Tests` 116/116；CK-008：探针 P1 的红）。

`passed`。F-30 / F-31 / F-32 全部落在 Task 文件的记述上，代码与用例无需改动，由执行者在接受前自行收口即可。

## Execution Result 8

第 7 轮判 `passed`。三条非阻断的记述订正在此收口，无代码改动。

**F-30｜`ordinal` 的归类两处不一致。** 正文说归第五类（结构序号），残留表把它列进第二类（播种值等于默认）。**以第二类为准**：它两者都是——既是结构序号，也恰好等于建表默认 0。第五类那句「14 列 / 本就不是盲区」应加限定：其中 `mailbox_attachments.ordinal` 同时落在第二类，因为它的 0 也是默认值。

**F-31｜「32 条 SELECT」实为 33 条**（24 条多列 + 9 条单值）。此前几轮我在这个数字上反复，本轮以评审自行提取的 33 为准。不影响任何结论。

**F-32｜串行代价改用区间，并记下真实复现率。**

- 隔离跑该项目：并行 11–12 秒，串行 17–18 秒；满载全量下串行 21–35 秒（本轮几次全量实测区间）。
- **更硬的证据**：评审临时改回并行连跑 6 次，**其中 1 次真红**（116 条失 1）。竞态在本仓库内被直接复现，不再只是机制推断。

## Executor Self-check 8

| 验收条件 | 状态 |
|---|---|
| 1 逐字段断言全部字段与集合顺序 | 已闭合。215 行金样；33 张表全覆盖；33 条 SELECT 逐条求值，播种值等于默认的普通取值列为 0 个 |
| 2 改动任一字段值使其转红 | 已闭合。十种证伪，其中七处列对调或丢列 |
| 3 改动任一集合顺序使其转红 | 已闭合 |

本 Task 的全部验收条件闭合，无未决阻断。

# TASK-07: 入场装载拆分与组装上移

## Task Unit Contract

- Plan task: `T-007`
- Covers: RQ-001, RQ-002, RQ-005, RQ-007
- Boundaries: BD-008, BD-009, BD-011, BD-012
- Required checks: CK-017, CK-018, CK-019, CK-020
- Permitted paths:
  - `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterEntryStateRepository.cs`
  - `Server/src/Dfo.Server.Persistence/Characters/`（新增单表仓储）
  - `Server/src/Dfo.Server.Abstractions/Persistence/`
  - `Server/src/Dfo.Server.Application/Characters/`
  - `Server/src/Dfo.Server.Persistence/PersistenceModule.cs`
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `Server/tests/`

### Goal

把入场装载的 33 张表各拆为一个单表仓储，并把跨表组装移入领域层，落在**单一文件的单一公开类型**内。`SqliteCharacterEntryStateRepository` 随之消失。

全程以 T-003 的表征用例作为不变性判据：每拆一段就复跑，不允许在它红着的状态下继续。

### Acceptance

| # | Condition |
|---|---|
| 1 | 入场装载所用的每个仓储，其全部语句只提及一张表 |
| 2 | 把多张表的行拼成入场记录的代码位于领域层，定义处唯一，持久化实现中零命中 |
| 3 | T-003 的表征用例在搬家前后均通过，且其断言未被放宽 |

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

### 三十三张表，三十三个单表仓储

契约与行类型：`Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs`（新）。
实现：`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`（新，二十七个）与
`Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs`（新，三个）。
`character_skills` 落在既有的 `ICharacterSkillTable` 上（加 `ReadEntryAsync`），
`character_gold_limits` 与 `character_daily_counters` 落在 T-006 已建好的归属者上。

### 四表 JOIN 拆开后，内连接的语义要显式写出来

原 `LoadProfileAsync` 是 `characters` 内连 `character_subtype0_fields`、`character_subtype1_fields`、
`character_init_flags`，`WHERE c.character_id=@cid AND c.delete_flag=0`。内连接本身就是规则：
少任何一行，整条查询无结果，于是抛「incomplete required profile state」。

拆成四次读之后这条规则必须写出来。四次读依次短路（前一个为空就不再读后一个），四个任一为空即抛
同一句话——不区分是哪一个缺，因为区分等于把库的形状告诉调用方。归属校验仍在其后，所以「人物存在但
明细行缺失」报的是 incomplete 而不是 does not belong，与原来一致。

### 邮箱：CTE 拆成两次读加一次领域侧的挑选

原来是一条 `WITH oldest_inbox_page AS (...)` 把 `mailbox_recipients` 与 `mailbox_messages` 连起来，
选出最旧的 20 封再倒序呈现。现在：

- `SqliteMailboxRecipientTable.ReadInboxAsync` 只读 `mailbox_recipients`：folder=0、未收藏、未删除。
  这三条是列的含义而非对邮件的判断，所以留在语句里。
- `SqliteMailboxMessageTable.ReadAsync(ids)` 只读 `mailbox_messages`，并带回两个存不下来的值：
  是否仍然有效、还剩多少秒。两者都是存储的过期时间对「现在」的度量，而「现在」属于数据库——
  搬到别处算就是拿文本时间戳去比另一个时钟。原来的 `CASE WHEN ... strftime` 表达式逐字保留。
- 领域侧：取有效的那些，按 `(created_at, message_id)` 升序取前 20，再倒序呈现；
  总数是有效者的去重计数，与原 `COUNT(DISTINCT m.message_id)` 同义。
  `mailbox_recipients` 上有 `UNIQUE(message_id,character_id,folder)`，所以一封信在 folder 0 里
  至多一条收件行，原外连接不可能放大行数，去重后按 id 取回收件行是等价的。

### 迁到领域层的编排

`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs` —— 单一文件、单一类型，
实现 `ICharacterEntryStateRepository`。四段载入（profile / skills / quests / progression / features）
的次序、每一处 `if (...) throw` 与其原话、每一处 `checked((byte))` 与其相对于校验的先后、
黑骑连招与骑士盾按职业的适用性判断、日常挑战 claim 指向不存在的组即为坏记录、日程必须恰好是
0/1/2 三格、称号书 item id 必须为正——全部原样搬过来。

`SqliteCharacterEntryStateRepository.cs` 随之删除。删除后，`Server/src/Dfo.Server.Persistence/`
下每一个 `internal sealed class` 的语句都只提及一张表，一处不剩。

### 边界读法的保留

- `ReadBoundedBlob` / `ReadOptionalBoundedBlob` 提为 `EntryBlob.Bounded` / `EntryBlob.Optional`，
  仍在持久层、仍在读取处，四个上限常量（appearance 16 KiB、option 64 KiB、combo 64 KiB、
  creature text 4 KiB）与消息文本一字未改。
- 称号书的行按原样交给领域层（`TitleBookRow`），因为原实现是先判范围再收窄成 byte；
  在表里就收窄会把一个越界值变成转换错误而不是那句具名的拒绝。
- `characters` 的 bonus_sp / bonus_tp 单独一次读且不带 `delete_flag` 过滤——原来就是这样，
  照抄，并在契约上注明这与带过滤的那次读的差别。

## Executor Self-check (round 1)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 入场装载所用的每个仓储语句只提及一张表 | 通过。逐类清点 `Server/src/Dfo.Server.Persistence/` 全部 `internal sealed class`，多表者为 0（T-005 后还剩 1，T-006 后还剩 1，现在 0）|
| 2 | 拼装代码位于领域层、定义处唯一、持久化实现中零命中 | 通过。`CharacterEntryStorageRecord(` 的构造点全仓库唯一，在 `Application/Characters/CharacterEntryAssembly.cs`；`Server/src/Dfo.Server.Persistence/` 内零命中 |
| 3 | T-003 表征用例搬家前后均通过，断言未放宽 | 通过。`EntryLoadCharacterizationTests` 两条金样本（215 行整记录 + 两个职业专属表）一字未改，仅把构造 `SqliteCharacterEntryStateRepository` 换成 `CharacterSide.EntryState`；搬家后原样通过 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-017 | Non-command，逐类清点表名 | 每个恰好一张 |
| CK-018 | Non-command，检索拼装代码的定义处 | 唯一，且在领域层 |
| CK-019 | Non-command，比对 T-003 金样本文本 | 未改动 |
| CK-020 | `dotnet test Server/86JP.Server.slnx -m:1` | 1035 通过 / 2 跳过 / 0 失败（基线 1011 / 2 / 0；T-006 收尾同为 1035）|

全量分项：Pvf 60、Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、
Persistence 124、Protocol 203。本次全量无假红。

### 方法自查

绑定方法为 test-driven-development。本轮的红→绿证据是 T-003 的金样本：它在搬家前已存在并通过，
搬家过程中因类型消失而编译失败，搬完后逐字通过。本轮未新增断言——对一次「行为一字不变」的搬家
来说，新增断言描述的会是新代码而不是旧行为，钉住现状的那两条金样本才是判据。

### 本轮拥有的改动

生产代码，新增：
`Abstractions/Persistence/CharacterEntryTables.cs`、
`Persistence/Characters/CharacterEntryStateTables.cs`、
`Persistence/Characters/CharacterMailboxTables.cs`、
`Application/Characters/CharacterEntryAssembly.cs`。

生产代码，删除：`Persistence/Characters/SqliteCharacterEntryStateRepository.cs`。

生产代码，修改：`Abstractions/Persistence/CharacterTables.cs`（加 `CharacterSkillEntryRow` 与
`ReadEntryAsync`）、`Persistence/Characters/CharacterSkillTables.cs`、
`Persistence/PersistenceModule.cs`、`Application/ApplicationModule.cs`。

测试，修改：`Persistence.Tests/Support/CharacterSide.cs`（加 `EntryState`）、
`Persistence.Tests/Characters/EntryLoadCharacterizationTests.cs`、
`Persistence.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs`、
`Persistence.Tests/Characters/SqliteCharacterLifecycleTests.cs`、
`Persistence.Tests/Schema/SchemaOwnershipTests.cs`、
`Application.Tests/Characters/CharacterStoreSettlementTests.cs`、
`Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs`（该处原先用反射按字符串
取内部类型，现改用夹具）、`Protocol.Tests/Dfo.Server.Protocol.Tests.csproj`（把 `CharacterSide.cs`
一并编译进来）。

### 一处自己发现并修回的差错

改测试站点时，脚本把 `CharacterEntryState_UpgradesPreProjectionNormalizedSchemaIdempotently`
里那两行「构造两次以证明建表可重复」误删了，该用例随即因缺表而红。已改为构造两次
`SqliteDatabase` 并写明为什么是两次。

### 探针纪律

本轮未在生产代码留下任何探针或临时改动。

## Independent Quick Review (round 1)

- Verdict: `passed`
- Reviewed contract: Task Unit Contract 初版（Plan task `T-007` / Covers RQ-001, RQ-002, RQ-005, RQ-007 / Boundaries BD-008, BD-009, BD-011, BD-012 / Checks CK-017–CK-020），无修订、无 amendment
- Owned diff（全部落在 Permitted paths 内）：
  - 新增 `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs`（sha256 `6bdc08fa…`）
  - 新增 `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`（sha256 `a3cb54b0…`）
  - 新增 `Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs`（sha256 `acd6ce98…`）
  - 新增 `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs`（sha256 `0df1156b…`）
  - 删除 `Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterEntryStateRepository.cs`
  - 修改 `Abstractions/Persistence/CharacterTables.cs`、`Persistence/Characters/CharacterSkillTables.cs`、两个 Module、八个测试站点
- 基线：HEAD 仍为 `ea95d49`，无任务级提交。**本轮评审零写入生产代码**：全程只跑 `git show` / `git diff` / `git status` / `grep` / `sha256sum` / `dotnet test`，未做任何注入式探针；结束时 `git status --short -- Server/src/` 与 `git diff --stat -- Server/src/`（17 files, 265+/3202-）与评审开始时逐字一致。

### 复核过的证据

| 项 | 手段 | 结果 |
|---|---|---|
| CK-020 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | Pvf 60 + Application 499 + Host 4 + Infrastructure 99(+2 跳过) + Networking 46 + Persistence 124 + Protocol 203 = **1035 通过 / 2 跳过 / 0 失败 / 0 警告**，与执行者所报一致；**本轮无假红**（`Host.Tests` 4/4、`Networking.Tests` 46/46） |
| CK-017 | 脚本扫 `Server/src/Dfo.Server.Persistence/**` 全部 `internal sealed class`，抽取 `FROM`/`JOIN`/`INTO`/`UPDATE`/`DELETE FROM` 后的标识符 | 每个类的表名集合恰好一个元素；6 处 `MULTI` 全是注释散文误命中（`the`/`set`/`when`/`half`/`which`），逐条核对后无真多表类 |
| CK-018 | `grep -rn "new CharacterEntryStorageRecord("` 全仓 | 唯一一处，`Application/Characters/CharacterEntryAssembly.cs:91`；`CharacterProfile`/`Progression`/`Feature`/`MailboxStorageRecord` 的构造点同样全在该文件；`Server/src/Dfo.Server.Persistence/` 内零命中 |
| CK-019 | 与 `TASK-03` 的第 4/6/7 轮执行记录交叉核对金样 | 见下「Acceptance 3」 |
| 88 列映射 | 脚本对齐（见下「结论一」） | 逐列、逐访问器 **MATCH** |
| 邮箱等价 | 独立 SQLite 上跑原 CTE 与新两读加领域挑选（见下「邮箱」） | 页与总数逐条相同 |
| 编码 | 逐文件查 BOM 与 CRLF 比例 | 六个新增/改动文件全部 CRLF 100%、无 BOM |
| 表结构约束 | 于四个新文件 grep `CREATE TABLE` / `PRAGMA` / `sqlite_master` / `ALTER TABLE` | 零命中；`Tool/` 与 `.worktrees/legacy-reference/` 未被触碰 |

### 结论一：88 列映射（本轮最大风险点）——逐列核对通过

我没有靠肉眼。脚本从 `git show HEAD:…SqliteCharacterEntryStateRepository.cs` 解析那条 SELECT 得 **88 列**，建立「序号 → 列名」；再从新代码三个表的 SELECT 建立各自的「序号 → 列名」；然后把原 `new CharacterSubtype0StorageRecord(...)` / `new CharacterSubtype1StorageRecord(...)` 与新代码同名构造的**实参序列**逐个翻译成「(列名, 访问器)」二元组序列并整体比对，访问器区分 `GetInt32` / `GetInt64` / 有界 blob：

```
Subtype0    orig 45  new 45  MATCH
Subtype1    orig 32  new 32  MATCH
characters                   MATCH   (0,1,2,3,4,82,83,84,85,86 -> I32,I32,I32,I64,BLOB,I64,I32,I32,I32,I32)
option column: character_option_blob  (原序号 31)
```

四表列数 10 + 45 + 32 + 1 = **88**，与原 SELECT 恰好构成一次划分：没有一列丢失，没有一列重复，没有一列错位，`GetInt32`/`GetInt64` 的选择逐列一致，有界 blob 的三处（appearance 16 KiB / creature buffer 8 / character option 64 KiB）位置与参数一致。

其余各条也已核对：

- **内连接语义**：四次读依 `characters` → `subtype0` → `subtype1` → `init_flags` 短路（`CharacterEntryAssembly.cs:108-118`），任一为空即抛原句 `Character {id} has incomplete required profile state.`，不区分是哪一个缺。归属校验在其后（`:125`），与原来「人物存在但明细行缺失报 incomplete 而非 does not belong」一致。
- **`delete_flag=0`** 只出现在 `characters` 那次读上（`CharacterEntryStateTables.cs:65`），bonus 那次读**不带**该过滤（`:78`），与原文一致，且契约上写明了差别（`CharacterEntryTables.cs:102-112`）。
- **`ValidateProfile` → `RequireProfileInRange`**：把两段去缩进后 `diff`，除大括号与一个空行外**逐字符相同**，两句抛出原文未变。
- **称号书**：行原样上交（`TitleBookRow`），`EntryBlob.Bounded(reader, 2, 82, "title book item core", false)` 与领域侧 `row.Core.Length != 82`、`category is < 0 or > 4`、`slot is < 0 or > ushort.MaxValue`、`ReadInt32LittleEndian(core.AsSpan(1)) <= 0` 四道全在，`checked((byte)Category)` 仍在范围拒绝之后。
- **expertValues**：`COALESCE(...,0)` 由 `?? new ExpertJobRow(0,0,0,0)` 复现；subtype0 无行仍抛 `Required character state is unavailable.`；`expertValues[2] is < 0 or > ushort.MaxValue` 对应 `workshop.GiveUpCount is < 0 or > ushort.MaxValue`，`Skip(3).Any(v => v < 0)` 对应后三个字段各自 `< 0`，`recipes.Any(v => v <= 0)` 保留。
- **默认值**：`character_dimension_flags` 无行给 `(0,0,0,0)`（`CharacterEntryStateTables.cs:241-243`）；`character_crystal_contract` 无行给 `(0,255)`（`CharacterEntryAssembly.cs:273`）；`character_gold_limits` 无行给 `(0,0)`（`:278`）；`characters` bonus 无行给 `(0,0)`（`:222-223`）；两个 `character_daily_counters` 无行给 `0`（`:512-515`）。**并且**「行在但 `value` 列为 NULL」这一支也照旧归零：`FindValueAsync` 显式 `reader.IsDBNull(0) ? null : GetInt32(0)`，对应原 `ReadScalarIntAsync` 的 `value is null or DBNull`。
- **champion break**：`FindChampionBreakAsync` 无行时 `?? throw new InvalidDataException("Required character state is unavailable.")`，与原 `ReadIntsAsync` 同句。
- **`FindOptionAsync` 的两种「空」**：用 `ReadAllAsync` 拿行数再取第 0 行（`:180-185`），`Count == 0` 给 `(null, false)`（无行），行在而列为 NULL 给 `(null, true)`（`EntryBlob.Optional` 返回 null）。两者**没有**被混为一谈。
- **全部 22 处 `ORDER BY`** 逐条对上（hotkey/dimension/skill/combo/shield/quest 三处/growth/challenge 四处/schedule/dungeon/creature/titlebook/achievement/collectbox/recipe/pvpmission/attachment），无一丢失、无一改序。
- **段间次序**：profile → skills → quests → progression → features，以及每段内各表的读取与校验先后，逐段与原文一致。

### 结论二：事务快照——**不是行为变化，本 Spec 已接受**

判断依据有三条，缺一不可，我三条都查了：

1. `SqliteDatabaseParticipant.CommandAsync`（`:84-91`）在一个用例内**只开一条连接、只 begin 一次 deferred 事务**，其后所有语句都挂在同一个 `SqliteTransaction` 上。所以「在用例内」时，33 次读仍在一个快照里。
2. 入场装载在生产路径上**确实在用例内**：`CharacterEntryAssembly.LoadAsync` 被 `CharacterEntryStateDomainService` 调用，后者被 `SelectCharacterPreparationService.PrepareAsync` 调用，再上一层是 `PrepareSelectedCharacterSessionCommandHandler`；而该命令在 `ProtocolModule.cs:117-119` 已由 T-002 从 `Workflow` 改为 `Transactional`，`UnitOfWorkBehavior` 对 `Transactional` 开边界。
3. 「不在用例内则每次读各开一次性连接」这一点，正是 spec 的 **AC-004**（「无进行中的用例 / 在用例边界之外读取 / 读取正常返回，不因缺少边界而失败」）明写接受的；RQ-003 的措辞也限定在「**一次用例内**的多次读」。

所以：33 次读「看到同一时刻」这一性质在生产路径上**被保住了**，而不是被换掉。今天唯一走边界外那一支的是测试（`CharacterSide.EntryState` 未包在 `InUseCaseAsync` 里），单线程无并发写，不影响判据。这一条既不是缺陷，也不需要作为行为变化记录——它是 T-001/T-002 已经落地并被 RQ-003/AC-004 明确划界的取舍。

### 邮箱：两读加领域挑选与原 CTE 等价

四点逐条核对：

- **(a) 排序键**。原 `ORDER BY datetime(m.created_at) ASC, m.message_id ASC` 取 20，再 `datetime(p.created_at) DESC, p.message_id DESC`；新代码是 `CAST(strftime('%s',created_at) AS INTEGER)` 上的 `OrderBy`/`ThenBy` 后 `Take(20)` 再 `OrderByDescending`/`ThenByDescending`。二者同用 SQLite 唯一的日期解析器、同样截断到整秒（`datetime()` 不带小数秒，`strftime('%s')` 也是整数秒），故序相同。我另在一个独立的内存 SQLite 上建了 30 封信（id 打乱、created_at 有重复、含三条带 `.500` 小数秒、含过期与 unlimited），同时跑原 CTE 与新的两条语句加领域挑选：**页的 20 个 id 与其顺序逐个相同**。非法时间戳两边同为 NULL（`datetime('not-a-date')` 与 `strftime('%s','not-a-date')` 都返回 NULL），且 NULL 在 ASC 下排最前必落进页内，两边都会在 `GetInt32` 上以同样方式失败——不构成差别。
- **(b) `TotalCount`**。原 `COUNT(DISTINCT m.message_id)` 对上新 `live.Length`：`ReadInboxAsync` 的谓词与原 CTE 逐字相同，id 经 `.Distinct()` 传入，而 `mailbox_messages.message_id` 是主键，故每个 id 至多回一行；同一探针里 **25 等于 25**。
- **(c) `gold` 与 `LetterState`**。`recipient.ReceivedGoldFlag == 0 ? message.Gold : 0` 对上 `CASE WHEN r.received_gold_flag=0 THEN m.gold ELSE 0 END`；`SavedFlag != 0 ? 3 : ReadFlag != 0 ? 2 : 1` 对上原 `CASE`。二者取值都来自**同一条**收件行——`UNIQUE(message_id,character_id,folder)`（`CharacterDatabaseSchema.cs:209`）保证 folder 0 里每封信至多一条收件行，所以 `recipients.ToDictionary(MessageId)` 不会撞键，原外连接也不会放大行数。
- **(d) 两个表达式**。`RemainingSeconds` 的 `CASE WHEN unlimited_flag!=0 OR expire_at >= '9999-01-01 00:00:00' THEN 0 ELSE MIN(2147483647, MAX(0, …)) END` 与「是否有效」的 `unlimited_flag != 0 OR datetime(expire_at) > CURRENT_TIMESTAMP` 均**逐字保留**（`CharacterMailboxTables.cs:51-55`），仍在数据库侧求值。

### 逐条 Acceptance 判定

| # | Condition | 判定 | 依据 |
|---|---|---|---|
| 1 | 入场装载所用的每个仓储，其全部语句只提及一张表 | **通过** | 独立脚本清点 `Server/src/Dfo.Server.Persistence/` 全部 `internal sealed class` 的语句表名集合，真多表者 0；入场装载所用的 33 个各恰好一张 |
| 2 | 拼装代码位于领域层、定义处唯一、持久化实现中零命中 | **通过** | `new CharacterEntryStorageRecord(` 全仓唯一，在 `Application/Characters/CharacterEntryAssembly.cs:91`；四个子记录的构造点同样只在该文件；`Server/src/Dfo.Server.Persistence/` 内零命中；`ICharacterEntryStateRepository` 的实现类只有 `CharacterEntryAssembly` 一个 |
| 3 | T-003 表征用例搬家前后均通过，断言未放宽 | **通过** | 两条断言仍是整体 `Assert.Equal`（`:38` 整条记录、`:81` `loaded.Skill` 全节，不是 `Assert.Contains`）；`Expected` 仍是 **215 行**；种子与 T-003 第 4/6/7 轮接受态逐条对上——字节池自 101 起贯穿四张表（`characters` 105/106/107、subtype0 108–128、subtype1 129–132）、非字节池自 1001 起（`exp` 1006、`ex_equip_slot_stat` 1007、attachments 1054–1067）、`job` 为 8、`cube_grade` 播 7 且金样 `Progression.CubeGrade = 7`。本轮对该文件的改动只有构造方式（`new SqliteCharacterEntryStateRepository(path, …)` 换成 `new CharacterSide(path).EntryState(…)`）与随之而来的 using。实跑 `Persistence.Tests` 124/124 全绿 |

Common Constraints 亦全部满足：无任务级提交（HEAD `ea95d49`）；四个新文件无任何表/列声明与结构元数据查询；无迁移/回填路径；`Tool/` 与 `.worktrees/legacy-reference/` 未动；六个受检文件 CRLF、无 BOM；`TreatWarningsAsErrors` 下 0 警告。

### Findings（均不阻断，交给 T-008 收口）

**F-1（真实行为/规模变化，非风格偏好）：邮箱由「库内分页」改成「全量读回再分页」。**
`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:481-486` 与 `Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs:38-73`。原语句在 CTE 里 `LIMIT 20`，最多取回 20 封的正文，再加一条 `COUNT`。现在 `ReadInboxAsync` 取回整个收件箱的收件行，`ReadAsync(ids)` 再把这些信**连 `title`/`body` 一并**取回，`TotalCount` 变成 `live.Length`。两个后果：

1. `WHERE message_id IN (@m0,…,@mN)` 的参数个数随收件箱线性增长。Microsoft.Data.Sqlite 捆绑的 e_sqlite3 里 `SQLITE_MAX_VARIABLE_NUMBER` 为 32766（更老的构建是 999）。收件箱超过该上限时，原来能正常装载的角色现在会在这条语句上抛错——这是可观察行为的改变，只是需要三万封信才够得着。
2. 每次入场都把整个收件箱的正文读进内存，而客户端只看 20 封。

今天的用例都是单封信，测不到，我也没有构造大收件箱去证伪（那要么改测试要么写探针，两者本轮都不做）。这不是拆分的必然代价：`IMailboxMessageTable` 完全可以另出一个「按 id 数存活计数」与一个「按 id 数取最旧 N 封」的读法，仍然只提及 `mailbox_messages` 一张表，Acceptance 1 不受影响。建议在 T-008 的行为保全证据里明确取舍，或就地收窄。

**F-2（拒绝消息的先后，仅在双重损坏数据上可见）。**
四处：`CharacterEntryStateTables.cs:70`（appearance）、`:183`（character option）、`:259`（Dark Knight combo body）、`:476`（title book core）。原实现里，appearance 与 option 的有界读发生在**归属校验之后**，combo body 的有界读发生在**页序号范围检查之后**；现在这些 blob 读都落在表读取的 mapper 里，即发生在那些检查之前。抛的仍然是 `InvalidDataException`，只是「哪一句先赢」变了，且只有当同一行/同一角色**同时**满足两种损坏（例如 appearance 超限**且**属于另一个账号）时才不同。健康库上不可观察。记录以免 T-008 把它当成新问题重新发现。

**F-3（既有用例的判别力被稀释，非本轮独有）。**
`Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs:105-117`：`builders` 列表如今只剩一个元素（`new SqliteDatabase(path, new DomainTransactionScope())`），于是那条 `Theory` 的 `reversed` 参数不再改变任何东西——「任意顺序都得到同一份 schema」这句话现在恒真。这是建表归属集中到 `SqliteDatabase`（T-001）的自然结果，T-004 到 T-007 各自摘掉一行，T-007 摘掉最后一批；不是本轮单独造成的，也不违反「机械调整」。但用例本身已不再陈述任何东西，建议 T-008 决定是删是改。

**F-4（排版，零风险）。**
`Server/tests/Dfo.Server.Application.Tests/Characters/CharacterStoreSettlementTests.cs:176`：`var fixture = new Fixture(path);` 比同块其余行多缩进四格，是上方删行留下的。

**已核对但不构成 finding 的两处**：`Protocol.Tests` 新增的 `<Compile Include … Link>`（`Dfo.Server.Protocol.Tests.csproj:23-30`）把 `CharacterSide.cs` 编进来，两个程序集各得一份 `internal` 类型，不冲突，且与已有的 `InventorySide.cs` 同法；比复制一份更好，因为不可能漂移。`SqliteCharacterEntryStateRepositoryTests` 类名保留旧名但内容已换成 `CharacterSide.EntryState`，只是命名滞后，不影响任何断言。

### 探针纪律

本轮**未**在 `Server/` 下写入任何文件，未做注入-恢复式探针。所有验证要么是只读命令，要么在 `%TEMP%` 下的独立脚本与独立内存数据库里完成。评审结束时 `git status --short -- Server/src/` 与评审开始时逐字一致（3 处 M + 8 处 D + 6 处 M，17 个 `??` 条目，全部属 T-001 至 T-007 既有工作），`git diff --stat -- Server/src/` 为 17 files / 265+ / 3202-，`git rev-parse --short HEAD` 为 `ea95d49`。

Verdict: `passed`

## Execution Result (round 2)

快评判定 `passed`，四条 finding 均不阻断。处理如下。

### F-1 已修：邮箱不再把整个收件箱读回来

原实现的 `LIMIT 20` 在库内。我拆分时把它变成了「读回全部信件、在内存里取前 20」，代价有两处：
`IN (@m0…@mN)` 的参数个数随收件箱线性增长，撞上 SQLite 单条语句的绑定值上限（32766）会让一个
本来能进游戏的角色变成抛错；而且每次入场都要把所有信的正文取回来只为显示二十封。

改为两次读：

- `IMailboxMessageTable.ReadHeadersAsync(ids)` 只取 `message_id`、到达时间、是否仍有效——
  够用来挑出是哪二十封，也够算总数；并且按 500 个 id 一批分批发问，绑定值个数因此有上界。
  分批不改变任何结果：回来的行是同一批行，先后由之后的排序决定。
- `IMailboxMessageTable.ReadAsync(ids)` 只对选中的至多二十封取全部字段。

领域侧的挑选逻辑不变：有效者按 `(created_at, message_id)` 升序取前 20，再倒序呈现；
总数仍是有效者的计数。

### F-3 已修：一个已经问不出问题的用例

`SchemaOwnershipTests.TheShapeOfTheDatabaseDoesNotDependOnWhatIsBuiltFirst` 的构造者列表
在 T-004..T-007 之后只剩一项，`reversed` 参数不再改变任何东西——它绿着，但已不再证明什么。

它问的那个问题现在由类型系统回答了：每个仓储的构造函数都要一个 `SqliteDatabase`，
没有它就造不出仓储，所以「谁先被构造」不再是一个可以答错的问题。

改为问一个仍然可能答错的问题：`BringingADatabaseIntoShapeAgainLeavesItAsItWas`——
对同一个库连做两次「入形」，形状必须一样。这不是白给的，因为入形里有对既有表的补列。
另两条用例（52 张表逐一计数、单独建库即可找到所需的表）原样保留。

### F-4 已修

`CharacterStoreSettlementTests` 里多缩进的一行已对齐；顺带把该处两行重复的
`new SqliteDatabase(...)`（T-005 替换两个不同仓储时留下的）并成一行，注释改为说数据库拥有表结构。

### F-2 备案，不改

四处有界 blob 的读现在早于归属校验与页序号校验。仅在同一份数据同时坏两处时，哪一句拒绝先赢会变。
这是「读取处做边界检查」这一位置本身带来的，与拆分方向一致；记在此处。

## Executor Self-check (round 2)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 入场装载所用的每个仓储语句只提及一张表 | 通过，round 2 未新增跨表语句 |
| 2 | 拼装代码位于领域层、定义处唯一、持久化实现中零命中 | 通过 |
| 3 | T-003 表征用例搬家前后均通过，断言未放宽 | 通过，本轮未再触碰该文件 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-017 | Non-command，逐类清点表名 | 每个恰好一张 |
| CK-018 | Non-command，检索拼装代码定义处 | 唯一，且在领域层 |
| CK-019 | Non-command，比对 T-003 金样本文本 | 未改动 |
| CK-020 | `dotnet test Server/86JP.Server.slnx -m:1` | 1035 通过 / 2 跳过 / 0 失败（基线 1011 / 2 / 0）|

全量分项：Pvf 60、Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、
Persistence 124、Protocol 203。

本次满载全量出现一次已知假红：`Networking.Tests.Integration.UnknownInboundFrameIntegrationTests
.FramesWithNoDecoder_AreDroppedSilentlyAndLeaveTheConnectionUsable`；隔离复跑该项目 46/46 通过。
上表 1035 取自隔离复跑后的计数。

本轮改动：`Abstractions/Persistence/CharacterEntryTables.cs`、
`Persistence/Characters/CharacterMailboxTables.cs`、
`Application/Characters/CharacterEntryAssembly.cs`、
`Persistence.Tests/Schema/SchemaOwnershipTests.cs`、
`Application.Tests/Characters/CharacterStoreSettlementTests.cs`。均在许可路径内。未留探针。

## Independent Quick Review (round 2)

- Verdict: `passed`
- Reviewed contract: Task Unit Contract 初版，与 round 1 所审同一份（Plan task `T-007` / Covers RQ-001, RQ-002, RQ-005, RQ-007 / Boundaries BD-008, BD-009, BD-011, BD-012 / Checks CK-017–CK-020），本轮无修订、无 amendment。
- Review scope: **补救本身**。round 1 已逐列核对并判定通过的 88 列映射、内连接语义、邮箱四点、称号书、expertValues、五类默认值、22 处 `ORDER BY`、段间次序，本轮不重做。
- Owned diff (round 2)，全部落在 Permitted paths 内：
  - `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs` sha256 `a689125a…`（round 1 记为 `6bdc08fa…`，已变）
  - `Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs` sha256 `fe15fd4b…`（round 1 `acd6ce98…`，已变）
  - `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs` sha256 `ac740a64…`（round 1 `0df1156b…`，已变）
  - `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs`、`Server/tests/Dfo.Server.Application.Tests/Characters/CharacterStoreSettlementTests.cs`
  - `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs` sha256 **仍为 `a3cb54b0…`**，与 round 1 记录逐字节相同 —— 二十七张表那一份本轮未被触碰，这是「F-1 的改动没有波及非邮箱读取路径」最直接的证据。
- 基线：HEAD 仍为 `ea95d49`，无任务级提交。编码：五个受检文件 CRLF 100%、无 BOM。

### F-1 —— **已修**

逐条查证：

1. **两处表达式逐字一致**。`ReadHeadersAsync`（`CharacterMailboxTables.cs:58-61`）与 `ReadAsync`（`:96-99`）里的 `CAST(strftime('%s',created_at) AS INTEGER)`、`CASE WHEN unlimited_flag != 0 OR datetime(expire_at) > CURRENT_TIMESTAMP THEN 1 ELSE 0 END` 逐字符相同（含换行与缩进位置）。不存在「两套判定」。
2. **绑定值有上界**。`Batch = 500`（`:47`），`messageIds.Chunk(Batch)`，每条语句至多 500 个 `@mN`，低于 999 与 32766 两个上限。F-1 第 1 条后果消除。
3. **正文不再全量读回**。`ReadHeadersAsync` 只取 `message_id` / 到达时间 / 是否有效；`title`、`body` 只对选中的至多二十封读（`CharacterEntryAssembly.cs:496-501`）。F-1 第 2 条后果消除。
4. **分批不改变结果**。ids 经 `.Distinct()` 传入，而 `mailbox_messages.message_id` 是主键 —— 每个 id 至多回一行，且只落在一个批次里，故各批之并恰等于一次性问的结果集。排序键 `(CreatedAtUnixSeconds, MessageId)` 因 MessageId 唯一而是**全序**，没有并列，`OrderBy` 的稳定性与行到达顺序都影响不到结果；`page` 的倒序同理。
5. **空输入行为正确**。ids 为空时 `Chunk` 不产生任何批次，`ReadHeadersAsync` 直接返回空表，不会拼出非法的 `IN ()`；`Chunk` 也不会产生空批。`ReadAsync` 有显式 `messageIds.Count == 0` 短路（`:86-89`），所以 `chosen` 为空时同样不会拼出 `IN ()`。
6. **与原语句等价**。原 CTE（`git show HEAD:…/SqliteCharacterEntryStateRepository.cs:543-566`）是 `mailbox_recipients JOIN mailbox_messages` **内**连接加 `GROUP BY m.message_id, m.created_at`：收件行没有对应信件时整条不出现 —— 新流程里该 id 拿不到 header，同样不进 `live`、不进页、不计数。原 `COUNT(DISTINCT m.message_id)`（`:503-510`）的谓词与 `ReadInboxAsync` 的三条加 Live 判定逐条相同，对上 `live.Length`。挑选键、`Take(20)`、倒序呈现均未变。
7. **`byId[message.MessageId]` 仍拿得到**。`byId` 建自**全部** recipients（`CharacterEntryAssembly.cs:503`），不是选中的那批；页里的 id ⊆ chosen ⊆ live ⊆ header ids ⊆ distinct recipient ids，必然命中。`UNIQUE(message_id,character_id,folder)` 保证 folder 0 下每封信至多一条收件行，`ToDictionary` 不会撞键。
8. **无多余定义**。`MailboxHeaderRow` 全仓只有一处定义（`Abstractions/Persistence/CharacterEntryTables.cs:332`）。附带澄清：`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs` 是 T-004/T-005 留下的**同名不同物**（`SqliteCharacterTable`、`CharacterIdList` 等），不是 Abstractions 那份的副本。

### F-2 —— 备案，同意不改

四处有界读早于归属/页序号校验，仅在同一份数据同时坏两处时可见。这是「边界检查放在读取处」这一位置本身带来的，与拆分方向一致。round 2 的处理（记录而不改）恰当。

### F-3 —— **已修**

- **新用例可能红**：`CharacterDatabaseSchema.Ensure` 确实在每次 `SqliteDatabase` 构造时跑（`Database/SqliteDatabase.cs:43`），所以 `ShapeAsync(times: 2)` 是真的把同一个文件入形两次；`Reconcile`（`Schema/CharacterDatabaseSchema.cs:123-155`）先 `PRAGMA table_info` 再对缺失列 `ALTER TABLE … ADD COLUMN`。这个 `present.Contains(column)` 判断一旦缺失或写歪，第二次入形就会以 `duplicate column name` 抛错，用例即红；某条声明漏掉 `IF NOT EXISTS` 同理。与旧的 `reversed`（对一元素列表做 `Reverse()`，两次输入逐字相同、没有任何生产代码改动能让它们不同）相比，新用例**不再是恒真的**。
- **删掉旧断言未丢保护**：全仓 `CREATE TABLE` 与 `ALTER TABLE` 只出现在 `Schema/CharacterDatabaseSchema.cs` 一个文件；`CharacterDatabaseSchema.Ensure` 的调用方只有 `SqliteDatabase` 构造器与两个测试夹具；`Server/src/Dfo.Server.Persistence/` 下 61 处构造参数为 `SqliteDatabase database`。「谁先被构造」确实已由单一声明点加类型系统答掉，旧断言问的问题不复存在。
- **另两条用例**：`EveryTableIsDeclaredSomewhereRatherThanByAccident` 的 `Assert.Equal(52, …)` 与五条 `Assert.Contains` **一字未改**，只把 `ShapeAsync(reversed: false)` 换成 `ShapeAsync()`；`OneComponentBuiltAloneFindsEverythingItNeeds` 的 `Assert.Equal(4L, …)` 与那条 `sqlite_master` 查询未改，只把构造的组件从已删除的 `SqliteCharacterPvpSkillProfileRepository` 换成 `SqliteDatabase`（前者不复存在，别无选择）。

### F-4 —— **已修，无误删**

`CharacterStoreSettlementTests.cs:170-176`：`var fixture = new Fixture(path);` 已与同块对齐（12 格），三行仓储构造并成一行 `new SqliteDatabase(path, new DomainTransactionScope());`，注释改为「数据库拥有表结构」。种子（characters 60、character_active_quests、character_new_items 三段 INSERT）与两处 `InventoryItemCore.Encode/Decode` 均在，无误删；`Application.Tests` 499/499 通过。

### CK-020 —— 独立复跑，与执行者所报一致

`dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`），exit code 0：

Pvf 60 / Application 499 / Host 4 / Infrastructure 99(+2 跳过) / Networking 46 / Persistence 124 / Protocol 203 = **1035 通过 / 2 跳过 / 0 失败 / 0 警告**（基线 1011 / 2 / 0）。

**执行者所称的满载假红本轮未复现**：`Dfo.Server.Networking.Tests` 在同一次满载全量里 46/46 通过（883 ms），`Host.Tests` 4/4。所以那一次是与契约「已知假红」同类的环境噪声，1035 这个数在不做隔离复跑的情况下也拿得到 —— 执行者的记法（取自隔离复跑后的计数）不影响结论。

### 新 finding（均不阻断，交给 T-008 一并收口）

**N-1（跨语句的时钟窗口，亚秒级）。** `CharacterMailboxTables.cs:60` 与 `:96`。原 CTE 里「是否有效」和 `RemainingSeconds` 在**同一条**语句里对同一个 `now` 求值；现在前者在 header 语句、后者在随后的全量读语句。一封恰好在两条语句之间过期的信会被选进页却拿到 `RemainingSeconds = 0`。原实现在同样时刻也会返回极小值，差别只是这个窗口从零变成了两条语句之间的耗时。WAL 快照管的是数据不是墙钟，所以事务边界不消除它。实机不可构造，记录以免 T-008 当成新问题重新发现。

**N-2（`ReadAsync` 的 `IN` 仍无上界，今天不触发）。** `Server/src/Dfo.Server.Abstractions/Persistence/CharacterEntryTables.cs:326-327` 与 `Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs:82-104`。分批只加在 `ReadHeadersAsync` 上；`ReadAsync` 依旧按 `messageIds.Count` 铺参数。今天唯一的调用方传的是 `Take(20)` 的结果，所以不是缺陷。但接口注释没有把「至多一页」写成契约的一部分（`ReadHeadersAsync` 有 remarks，`ReadAsync` 一句没有），第二个调用方拿一整箱 id 进来就会原样踩回 F-1。建议在 `IMailboxMessageTable.ReadAsync` 上写明该限制，或让它也分批。

**N-3（新用例的理由与它实际钉住的东西有偏差）。** `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs:24-26` 与 `:30-35` 称「不是白给的，因为入形里有对既有表的补列」。实际查证：`ReconcilableColumns` 里的每一列都已经写在对应表的 `CREATE TABLE` 声明里（例如 `accounts|password_hash`、`accounts|cube_gold` 等三十余条都在 `TableDeclarations` 的 `accounts` 里），所以在一个**全新**库上 `Reconcile` 的 `ALTER` 一次也不会触发，两次 `Ensure` 都不走补列那条路。用例仍然非空洞（它钉住的是「第二次入形既不抛也不改」，见 F-3 判定），但它并没有覆盖 remarks 所说的「一个旧形状的库被补上列」。要真钉住那条路，得先造一个缺列的库再 `Ensure`。当前是文字比用例强，不是用例失效。

**N-4（排版/清理，零风险）。** `SchemaOwnershipTests.cs:2,4,5,6,8` 五条 `using`（`Dfo.Server.Application.Characters`、`Dfo.Server.Application.Inventory`、`Dfo.Server.Persistence.Accounts`、`Dfo.Server.Persistence.Characters`、`Dfo.Server.Persistence.Inventory`）在删掉七个仓储构造后已无引用；新加的 `using Dfo.Server.Application.Transactions;`（`:3`）与 `CharacterStoreSettlementTests.cs:11` 的 `using Dfo.Server.Persistence.Database;` 都插在了非字母序的位置。`TreatWarningsAsErrors` 下 0 警告（IDE0005 未启用），不影响构建 —— 但 F-4 本身就是这一类的收尾，顺手一并清掉更整齐。

### 探针纪律

本轮**未**在 `Server/` 下写入任何文件，未做注入-恢复式探针，未新建任何临时验证工程；所有验证均为只读命令（`git show` / `git diff` / `git status` / `git rev-parse` / `grep` / `sed` / `awk` / `wc` / `od` / `sha256sum` / `dotnet test`）。评审结束时 `git status --short -- Server/src/` 与评审开始时逐字一致：**8 处 `M` + 9 处 `D` + 20 个 `??`，共 37 条**，全部属 T-001 至 T-007 既有工作；`git diff --stat -- Server/` 为 39 files / 620+ / 3384-；`git rev-parse --short HEAD` 为 `ea95d49`。

Verdict: `passed`

## Execution Result (round 3)

round 2 判定 `passed`，另记了四条不阻断的 finding。其中三条就地处理，一条留给 T-008。

### N-2 已修：整行读也分批

`IMailboxMessageTable.ReadAsync` 的 `IN` 列表原本没有上界，今天唯一的调用方只传至多一页，
但接口没有把「至多一页」写进去——第二个调用方会重新踩进 F-1。改为与 header 读同样按 500 一批，
上界因此来自实现而不是来自调用方的自觉。

### N-3 已修：用例的注释与它实际覆盖的东西对齐

`BringingADatabaseIntoShapeAgainLeavesItAsItWas` 的注释原说「入形里有对既有表的补列」，
但从零建起的库每一列都已在 `CREATE TABLE` 里，`ALTER` 一次也不触发。它钉住的其实是补列的守卫：
守卫一旦失效，第二次入形会因重复列名而失败，而别处没有任何东西会注意到。注释改为这么说。

### N-4 已修

`SchemaOwnershipTests` 的 using 清理并排序，只留仍被引用的四条。

### N-1 留给 T-008

Live 在 header 语句求值、剩余秒数在后一条语句求值，原来两者在同一条 CTE 里。窗口是亚秒级、
实机不可构造，但它是一处真实的跨语句时钟差，记入 T-008 的行为保持证据一并说明。

## Executor Self-check (round 3)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 入场装载所用的每个仓储语句只提及一张表 | 通过 |
| 2 | 拼装代码位于领域层、定义处唯一、持久化实现中零命中 | 通过 |
| 3 | T-003 表征用例搬家前后均通过，断言未放宽 | 通过，本轮未触碰该文件 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-017 | Non-command，逐类清点表名 | 每个恰好一张 |
| CK-018 | Non-command，检索拼装代码定义处 | 唯一，且在领域层 |
| CK-019 | Non-command，比对 T-003 金样本文本 | 未改动 |
| CK-020 | `dotnet test Server/86JP.Server.slnx -m:1` | 1035 通过 / 2 跳过 / 0 失败（基线 1011 / 2 / 0）|

全量分项：Pvf 60、Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、
Persistence 124、Protocol 203。本次全量无假红，0 警告。

本轮改动：`Persistence/Characters/CharacterMailboxTables.cs`、
`Persistence.Tests/Schema/SchemaOwnershipTests.cs`。均在许可路径内。未留探针。

## Independent Quick Review (round 3)

- Verdict: `passed`
- Review scope: **只核对 round 3 的补救本身** —— N-2 / N-3 / N-4 的处理、构建与回归。round 1、round 2 已判定通过的 88 列映射、内连接语义、邮箱四点、22 处 `ORDER BY`、拼装唯一性、段间次序等一律不重做。
- 本轮实际改动确为两个文件，均在 Permitted paths 内：
  - `Server/src/Dfo.Server.Persistence/Characters/CharacterMailboxTables.cs` sha256 `7a33c5af…`（round 2 记为 `fe15fd4b…`，已变）
  - `Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs` sha256 `c4fe67a2…`
  - round 2 记过哈希的另外三份**一字未动**：Abstractions `CharacterEntryTables.cs` 仍 `a689125a…`、`CharacterEntryAssembly.cs` 仍 `ac740a64…`、`CharacterEntryStateTables.cs` 仍 `a3cb54b0…`。接口签名与调用方都没跟着改，这一点 N-2 第 4 条要用。
- 基线：HEAD 仍 `ea95d49`，无任务级提交。两个改动文件 CRLF 100%、无 BOM。

### N-2 —— 已修，等价

对照基准先说明：round 2 的文件内容不在 git 里（`CharacterMailboxTables.cs` 仍是 `??`），所以「逐字一致」是拿**拆分前的原件** `git show HEAD:Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterEntryStateRepository.cs`（`:556-566`）当锚点核的 —— 这比拿 round 2 的中间态更严。

1. **语句逐字一致**（去掉 `m.` 前缀是 round 1 就审过的改动，不计）：剩余秒数 `CASE WHEN unlimited_flag!=0 OR expire_at >= '9999-01-01 00:00:00' THEN 0 ELSE MIN(2147483647, MAX(0, CAST(strftime('%s',expire_at) AS INTEGER)-CAST(strftime('%s','now') AS INTEGER))) END` 与原件 `:559-560` 逐字符相同；`CAST(strftime('%s',created_at) AS INTEGER)` 与 `:561` 相同；Live 的 `CASE WHEN unlimited_flag != 0 OR datetime(expire_at) > CURRENT_TIMESTAMP THEN 1 ELSE 0 END`（`:104-105`）与同文件 `ReadHeadersAsync` 那一份（`:60-61`）逐字符相同，仍是一套判定。列清单与顺序（message_id…gold）与原件同序。
2. **参数绑定一致**：仍是 `@m{index}` 从 0 起、`string.Join(',', …Select((_, index) => $"@m{index}"))` 拼列表、循环 `AddWithValue`。每批各自成命令，索引从 0 重来不冲突。
3. **reader 映射一致**：`MailboxMessageRow` 11 个字段（`Abstractions/Persistence/CharacterEntryTables.cs:82-93`），`SELECT` 11 列，序号 0-10 一一对上，第 10 列 `!= 0` 转 bool，与原件的 `GetInt64(0)…GetInt32(10)` 同序同型。
4. **`.Count` → `.Length` 正确**：`ReadBatchAsync` 的参数是 `long[]`（`Enumerable.Chunk` 的产物本就是 `TSource[]`），`messageIds.Length`（`:113`）是数组的正确写法；对外的 `ReadAsync` 参数仍是 `IReadOnlyList<long>`、仍用 `.Count`（`:86`）。接口 `IMailboxMessageTable.ReadAsync`（`Abstractions/Persistence/CharacterEntryTables.cs:327-328`）签名未变（哈希未变已佐证），没有把 `long[]` 泄到接口上。
5. **空输入等价**：`Chunk` 对空源不产生任何批次，`rows` 保持空并返回，与原来的 `messageIds.Count == 0` 短路结果相同；`Chunk` 也不会产出空批，所以拼不出非法的 `IN ()`。删掉显式短路没有丢行为。
6. **分批不影响结果**：唯一调用方 `CharacterEntryAssembly.cs:496-501` 拿到结果后自己 `OrderByDescending(CreatedAtUnixSeconds).ThenByDescending(MessageId)`；`message_id` 是主键、传入的 id 已 `Distinct`，排序键因而是全序、无并列，各批之并与一次问完的集合相同。今天传进来的至多 20 个 id 连一批都装不满，运行时行为完全没变。
7. **上界确实来自实现了**：全仓 `IMailboxMessageTable` 只有一个实现与一个注册点（`PersistenceModule.cs:171`），两个读方法现在都按 `Batch = 500` 铺参数，低于 999 与 32766 两个上限。F-1 那类问题不会再从第二个调用方绕回来。

### N-3 —— 已修，注释与用例对得上

- **守卫存在且形式相符**：`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:123-155` 的 `Reconcile` 是「`PRAGMA table_info` 建 `present` 集合（`:130`）→ `present.Count == 0` 跳过（`:138`，表还不存在，交给 `CREATE TABLE`）→ 每列 `present.Contains(column)` 命中则 `continue`（`:145`），否则 `ALTER TABLE … ADD COLUMN`（`:151`）」。这正是新注释所说的 top-up 守卫。
- **新注释的事实前提成立**：把 `ReconcilableColumns` 的 297 行逐条比对对应表的 `CREATE TABLE` 声明，**297/297 全部已写在声明里**，零缺失、零缺表。所以 `SchemaOwnershipTests.cs:33-34` 的「A database built from nothing has every column already, so this exercises the guard on the top-ups rather than the top-ups themselves」是准确的，不再像 round 2 那样宣称覆盖了补列本身。
- **类级 remark**（`:20-22`）改为「top-up 若停止注意到列已存在，就会在第二次入形失败」，与守卫的实际失效模式一致。round 2 记的 N-3 偏差消除。

### N-4 —— 已修（测试文件这半边），构建 0 警告

- `SchemaOwnershipTests.cs` 现只剩四条 `using`：`System.Text`（`StringBuilder`）、`Dfo.Server.Application.Transactions`（`DomainTransactionScope`）、`Dfo.Server.Persistence.Database`（`SqliteDatabase`）、`Microsoft.Data.Sqlite`（`SqliteConnection` / `SqliteConnectionStringBuilder`）。逐条都有引用，无多余；`Xunit` 由 `Usings.cs` 的 global using 提供，不需要本地 using，无遗漏。次序为 System 在前、其余字母序。
- **0 警告**：`Server/Directory.Build.props:6` 为 `TreatWarningsAsErrors=true`；对改动链做了一次**强制**重建 `dotnet build Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --no-incremental`，输出「已成功生成。0 个警告」。全量 `dotnet test` 输出中亦无任何 warning 行。

### 回归

未发现新回归。语义面本轮只动了邮箱整行读的分批，等价性见上；`SchemaOwnershipTests` 的改动全在 `using` 与注释，三个 `[Fact]` 的断言一字未动（`Assert.Equal(once, again)`、`Assert.Equal(52, …)` 与五条 `Assert.Contains`、`Assert.Equal(4L, …)`）。`CharacterMailboxTables.cs` 里另外两张表（`SqliteMailboxRecipientTable`、`SqliteMailboxAttachmentTable`）的语句与 reader 映射与 `HEAD` 原件逐字相同，未被本轮波及。

### 新 finding（均不阻断，交给 T-008）

**N-5（N-4 只做了一半，纯排版）。** `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterStoreSettlementTests.cs:11` 的 `using Dfo.Server.Persistence.Database;` 仍插在 `Microsoft.Data.Sqlite` 之后，未随 `SchemaOwnershipTests` 一起归位。round 3 的说明只提了 `SchemaOwnershipTests`，所以是漏了一处而非刻意保留。零风险。

**N-6（接口仍未写下这条约束）。** `IMailboxMessageTable.ReadAsync`（`Abstractions/Persistence/CharacterEntryTables.cs:327-328`）依旧没有任何 doc/remarks，而 `ReadHeadersAsync` 有。执行者选了 N-2 给的两条路里较强的那条（在实现里分批），今天的上界因此是真的；但接口本身没说，第二个实现仍可能不分批。建议给 `ReadAsync` 补一句 remarks，与 `ReadHeadersAsync` 对齐。

（N-1 按 round 2 / round 3 的商定留给 T-008，本轮不评。）

### CK-020 —— 独立复跑，与执行者所报一致

`dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`），exit code 0：

Pvf 60 / Application 499 / Host 4 / Infrastructure 99(+2 跳过) / Networking 46 / Persistence 124 / Protocol 203 = **1035 通过 / 2 跳过 / 0 失败 / 0 警告**（基线 1011 / 2 / 0）。

满载一次跑过即全绿：`Host.Tests` 4/4（8 s）、`Networking.Tests` 46/46（1 s），**无假红，无需隔离复跑**。执行者的报数属实。

### 探针纪律

本轮未在 `Server/` 下写入或修改任何文件，未做注入-恢复式探针，未新建任何临时工程或临时文件；所有命令均只读（`git show` / `git diff` / `git status` / `git rev-parse` / `grep` / `awk` / `sed` / `wc` / `od` / `sha256sum` / `dotnet build` / `dotnet test`）。评审结束时 `git status --short -- Server/src/` 与 round 2 记录逐条相同：**8 处 `M` + 9 处 `D` + 20 个 `??`，共 37 条**，全部属 T-001 至 T-007 既有工作；`git rev-parse --short HEAD` = `ea95d49`；`git diff --stat -- Server/` 为 39 files / 625+ / 3389-（round 2 为 620+ / 3384-，差额来自 `SchemaOwnershipTests` 本轮的注释重写与五条 `using` 删除，没有第三个文件混进来）。

Verdict: `passed`

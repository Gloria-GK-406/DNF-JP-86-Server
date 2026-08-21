# TASK-04: 单表仓储形状与账号两侧

## Task Unit Contract

- Plan task: `T-004`
- Covers: RQ-001, RQ-002, RQ-007
- Boundaries: BD-003, BD-004, BD-005, BD-011, BD-012
- Required checks: CK-009, CK-010, CK-011
- Permitted paths:
  - `Server/src/Dfo.Server.Persistence/Database/`
  - `Server/src/Dfo.Server.Persistence/Accounts/`
  - `Server/src/Dfo.Server.Abstractions/Persistence/`
  - `Server/src/Dfo.Server.Application/`（账号侧编排）
  - `Server/src/Dfo.Server.Persistence/PersistenceModule.cs`
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `Server/tests/`

### Goal

建立单表仓储的形状，并用它拆掉账号侧两个仓储。

形状要求：每张表一个具名类型，各自持有自己的 SQL；共用的读写原语以**显式表名与列名**参数化，不引入反射或特性映射（Spec 的 Non-Goal）。领域侧的取用点按聚合分组、以属性暴露各单表仓储，沿用 `ICharacterStoreSession` 的既有形状；分组对象不含任何 SQL。

### Acceptance

| # | Condition |
|---|---|
| 1 | 账号侧拆出的每个仓储，其全部语句只提及一张表 |
| 2 | 账号侧仓储中新建数据库连接与开启数据库事务的处数为零 |
| 3 | 账号侧的跨表组装位于领域层 |

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

## Contract Amendment A1

**触发**：账号的入场装载读了四张**人物侧**的表（`characters`、`character_init_flags`、`character_buy_restrict_items`、`character_mercenary_support`），其中三张是通过 JOIN 读的。把这些 JOIN 拆开，就必须为这四张表各建一个单表仓储，而它们的归宿是 `Server/src/Dfo.Server.Persistence/Characters/`——不在本 Task 已封存的 Permitted paths 内。

**为什么不能放在 `Accounts/` 下**：它们是人物的表。放在账号目录里，只会让下一个读代码的人以为 `character_mercenary_support` 属于账号。T-005 与 T-007 也要用到同样这几张表，届时会在正确的目录里再找一遍。

**修订内容**：Permitted paths 增列 `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`，**仅限新增被账号装载读取的人物侧单表仓储**。该目录下的既有文件（`SqliteCharacterRepository.cs` 等）仍归 T-005，本任务不动。

**Spec 边界未变**：不新增、不放宽任何 RQ 或 AC。

## Execution Result 1

### 先钉住，再拆

沿用 T-003 的次序：在动任何东西之前，新增 `Accounts/AccountEntryLoadCharacterizationTests.cs`，用同一个 `StorageRecordDump` 把 `AccountEntryStorageRecord` 与只读设置那条路径整体写下来（48 行 + 5 行金样）。金样取自**改造前的实现**，改造后必须原样通过。

### 单表形状

新增 `Persistence/Database/SqliteTable.cs`：三个扩展方法（读全部、读第一条、执行），各自接「语句、绑定、行到值」三样。仓储之间不同的是语句，不是管道，所以管道写一次。

**语句写死而不是从类型推导**，理由写在文件注释里：这些表是原服务器的，列名是它命的，好几列存的是打包字节而不是字段，还有几处写自带条件。一个能推导的映射器要被告知全部三条例外，讲清楚它比讲清楚语句本身还长。

### 八个单表仓储

| 仓储 | 表 |
|---|---|
| `SqliteAccountTable` | `accounts` |
| `SqliteAccountSettingsTable` | `account_settings` |
| `SqliteAccountPremiumTable` | `account_premiums` |
| `SqliteAccountLotteryProgressTable` | `account_increase_chance_lottery_progress` |
| `SqliteCharacterRosterTable` | `characters` |
| `SqliteCharacterPcRoomTable` | `character_init_flags` |
| `SqliteCharacterBuyRestrictTable` | `character_buy_restrict_items` |
| `SqliteCharacterMercenarySupportTable` | `character_mercenary_support` |

逐个清点其语句提及的表名，**每个恰好一个**。这八个文件里新建数据库连接与开启数据库事务的处数**为零**。

原先的两个仓储 `SqliteAccountRepository`（2 处自开连接）与 `SqliteAccountEntryStateRepository`（3 处）随之删除。

### 三个 JOIN 的去处

装载里有三处 JOIN，它们携带的不只是数据，还有**结果的顺序**和**跨表的校验**：

| 原 JOIN | 携带的 | 现在由谁承担 |
|---|---|---|
| 关系 JOIN 两次 `characters` | 按角色槽位排序；受支援者必须是本账号未删除的角色 | 领域层：花名册决定顺序，花名册的成员资格就是那条校验 |
| 采购限制 JOIN `characters` | 按槽位与序号排序；投影的是槽位而非角色 id | 领域层：按花名册映射 id→槽位 |
| PC 房 LEFT JOIN `character_init_flags` | 按花名册顺序；缺行即拒绝 | 领域层：逐个花名册成员取其行，缺则拒 |

组装落在 `Application/Accounts/AccountEntryAssembly.cs`，**单一文件单一公开类型**。

`GetOrCreateAsync` 的「先插入若不存在、再读回」也是一个决定而非一条存储访问，移入 `Application/Accounts/AccountProvisioning.cs`。

### 建库归属

`SqliteDatabase` 的构造函数现在负责建目录并调用建表归属者。此前是每个仓储构造时各调一次——那正是「表存不存在取决于谁先被解析」那个已修掉的缺陷的来源。仓储管一张表，文件里有哪些表是文件的事，而 `SqliteDatabase` 就是那个文件。这同时闭合了 T-001 快评留下的 F-4。

### 证伪：第一次做的探针全绿，种子是瞎的

第一次给三处 JOIN 的替代逻辑打探针，**三条全绿**。原因不在实现，在种子：我播的两个角色是 30→槽 5、31→槽 7，**id 顺序恰好等于槽位顺序**，于是「按 id 排」与「按槽位排」给出同一个答案。

把槽位倒过来（31→槽 5、30→槽 7）之后：

| 注入 | 结果 |
|---|---|
| 关系改按角色 id 排序 | **转红** |
| 采购限制改按角色 id 排序 | **转红** |
| 撤掉「受支援者属于本账号」的校验 | **转红** |

第三条还需要另一样东西：**一次拒绝不产生记录，金样表达不了它**。所以另加 `ARelationPointingOutsideTheAccountIsRefused`——播一个属于别的账号的角色并让关系指向它，断言装载拒绝。撤掉校验时转红的正是这一条。

这是 T-003 那个教训的原样重演：**分辨不出来的种子会让金样变瞎**，而瞎的金样是绿的。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1030 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 19）。

## Executor Self-check 1

| 验收条件 | 状态 |
|---|---|
| 1 账号侧拆出的每个仓储只提及一张表 | 已闭合，八个逐一清点 |
| 2 账号侧新建连接与开启事务的处数为零 | 已闭合，实测为 0（原为 5） |
| 3 账号侧的跨表组装位于领域层 | 已闭合，`Application/Accounts/AccountEntryAssembly.cs` 单一文件单一类型；三处 JOIN 携带的顺序与校验逐条落位，各带证伪 |

### 已知与移交

- **本任务改了写路径的语义**：账号创建现在必须在用例边界内（`SqliteDatabase.WriteAsync` 拒绝无边界的写）。登录命令已在 T-002 标为 `Transactional`，故生产路径成立；测试需自开边界，`Support/AccountSide.cs` 封装了这一点。这正是 T-002 记录里「等 T-004 接进去之后才会成真」的那条语义变化，现在成真了：**首次登录中途失败则账号不存在**。判定可接受——一个没能完成的登录不该留下账号。
- **契约命名的不协调**：`IAccountEntryStateRepository` 现在由领域层实现，名字里的 "Repository" 已不再描述它。不改，理由写在实现的注释里：改名是跨调用方的纯churn，说不出新东西。记录在案。
- **T-002 移交的 F-8 尚未触发**：会话结束在用例内自开第二条连接的隐患，要等选角链上的仓储接入环境事务才会发生。账号侧的仓储只在登录路径上，不在选角链上。**T-005 必须处理它**。

## Independent Quick Review 1

- 评审对象：Task Unit Contract（含 Contract Amendment A1）+ Execution Result 1 + Executor Self-check 1
- Owned diff 身份：相对基线 `ea95d49` 的未提交工作树；本轮只看 T-004 拥有的 17 个文件（`Database/SqliteTable.cs`、`Database/SqliteDatabase.cs`、`Abstractions/Persistence/{AccountTables,CharacterTables}.cs`、`Accounts/AccountTables.cs`、`Characters/CharacterEntryTables.cs`、删除的两个账号仓储、`Application/Accounts/{AccountEntryAssembly,AccountProvisioning}.cs`、两个 Module、以及 `Persistence.Tests` 下的 6 个测试文件）
- 结论：**changes_required**

### 核对过的证据

| 项 | 命令或位置 | 结果 |
|---|---|---|
| 全量复验 | `dotnet test Server/86JP.Server.slnx -m:1` + 单跑 `Dfo.Pvf.Tests` | 1030 通过 / 2 跳过 / 0 失败，0 警告；与执行者声称一致 |
| CK-009 单表 | `grep -Eo "(FROM|INTO|UPDATE|JOIN)[[:space:]]+[a-z_]+"` 于两个新文件 | `Accounts/AccountTables.cs` → accounts×3、account_settings、account_premiums、account_increase_chance_lottery_progress；`Characters/CharacterEntryTables.cs` → characters、character_init_flags、character_buy_restrict_items、character_mercenary_support。八个仓储各恰好一张表 |
| CK-010 连接与事务 | `grep -rn "new SqliteConnection\|BeginTransaction\|OpenAsync\|\.Open()"` 于 `Persistence/Accounts/` 与 `Characters/CharacterEntryTables.cs` | 命中 0 处 |
| RQ-007 建表归属 | `SchemaOwnershipTests` 的 `Assert.Equal(52, ...)` 与 `ea95d49` 逐字相同 | 未新增表或列声明 |
| 越界 | 实际改动全部落在 Permitted paths 与 A1 增列的 `Characters/CharacterEntryTables.cs` 内；`Application/Accounts/` 在 `Server/src/Dfo.Server.Application/` 之下 | 无越界 |
| 既有测试语义 | `git diff ea95d49` 于三个被改测试文件 | 只有构造方式换成 `AccountSide`，断言与用例集未动，属机械调整 |
| 探针纪律 | 探针前后 `git status --porcelain` 逐字一致，三个被注入文件 md5 与备份相同 | 已复原 |

### 发现

**F-1（阻断，行为回归）：账号设置的 64 KiB 上限校验在拆分中丢失。**
被删除的 `SqliteAccountEntryStateRepository.ReadBlob` / `ReadOptionalBlob`（`ea95d49` 第 251-269 行）对 `main_game_option`、`quickchat_bank0/1`、`hotkey_slots` 四列都有 `value.Length > SettingsMaximumBytes(64*1024)` → `InvalidDataException("{name} exceeds the supported size.")`。新的 `Server/src/Dfo.Server.Persistence/Accounts/AccountTables.cs` 的 `SqliteAccountSettingsTable.OptionalBlob` 只判 `IsDBNull` 与类型，**没有任何长度判断**；`grep -rn "SettingsMaximumBytes\|exceeds the supported size" Server/src` 在账号侧零命中（只剩人物侧 `SqliteCharacterEntryStateRepository` 的同名常量）。
证据：临时用例播入 65538 字节的 `quickchat_bank0` 后调用 `SqliteAccountSettingsTable.FindAsync`，`Assert.ThrowsAsync<InvalidDataException>` 报 `No exception was thrown`（用例已删除，工作树已复原）。这条与 Common Constraints 的「对外可观察行为一字不变」及 RQ-008 直接冲突，且两条金样都看不见它。
需要的补救：把长度判断与错误消息原样搬回 `SqliteAccountSettingsTable`，并补一条会红的用例。

**F-2（阻断，仓库约束）：八个新增 C# 文件是 LF，契约要求 CRLF。**
`.editorconfig` 第 5 行 `end_of_line = crlf`，`.gitattributes` 的 `text eol=lf` 只作用于 `protocol/**`，Common Constraints 亦写明「C# 文件 CRLF、无 BOM UTF-8」。按字节清点，下列文件 CRLF=0：
`Persistence/Database/SqliteTable.cs`、`Abstractions/Persistence/AccountTables.cs`、`Abstractions/Persistence/CharacterTables.cs`、`Persistence/Accounts/AccountTables.cs`、`Persistence/Characters/CharacterEntryTables.cs`、`Application/Accounts/AccountEntryAssembly.cs`、`Application/Accounts/AccountProvisioning.cs`、`Persistence.Tests/Support/AccountSide.cs`。
同一批里的 `Database/SqliteDatabase.cs` 与 `Accounts/AccountEntryLoadCharacterizationTests.cs` 是 CRLF，基线文件（如 `SqliteCharacterRepository.cs`）也是 CRLF，所以这不是仓库通例而是本轮新引入的不一致。无 BOM 一项全部符合。

**F-3（阻断，遗留承诺的理由不成立）：账号侧仓储确实在选角链上。**
Executor Self-check 1 的移交理由是「账号侧的仓储只在登录路径上，不在选角链上」。实际：
`Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs` 第 41 行注入 `IAccountEntryStateDomainService`，第 123 行在 `PrepareAsync` 中调用它 → `Application/Accounts/AccountEntryStateServices.cs` 第 128 行的 `IAccountEntryStateRepository` → 本任务新建的 `AccountEntryAssembly` → 八个单表仓储 → `SqliteDatabase`。
而 `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` 第 117-119 行把 `PrepareSelectedCharacterSessionCommand` 注册为 `Transactional`。所以选角链在一个已开边界的用例里已经走到 `SqliteDatabase`。
本轮全量测试没有因此变红，F-8 是否仍可推迟到 T-005 由执行者判断；但写进 Task 文件的理由是错的，T-005 会据此少查一处。必须改成事实描述。

**F-4（非阻断，证据强度）：48 行金样对两处顺序与一处默认值是瞎的。**
按要求另找了三处执行者没试过的注入点：

| 注入 | 结果 | 说明 |
|---|---|---|
| 去掉 `account_premiums` 的 `ORDER BY premium_type` **且** 去掉 lottery 的 `ORDER BY item_template_id,reward_index` | **绿** | 种子 `(1007,1008),(1009,1010)` 与 `(1011,3),(1012,4)` 的插入顺序恰好等于排序键顺序，rowid 顺序给出同一答案。与执行者踩过的「角色 id 顺序等于槽位顺序」是同一个坑，只是换了两张表 |
| 改掉 `AccountEntryAssembly` 中 settings 缺行时的默认值（两处 `?? new AccountSettingsStorageRecord(null, [], [], 0, [])`） | **绿** | 两条金样都播了 `account_settings` 行，`FindAsync` 永不返回 null，默认值分支零覆盖。而这条分支正是首次登录（尚无 settings 行）真正走的那条 |
| 撤掉 `PcRoomStatesAsync` 的 `states.Distinct().Count() == 1` | **红** | 但转红的是被改接的既有用例 `SqliteAccountEntryStateRepositoryTests.AccountEntryState_RejectsIncompleteMalformedOrCrossOwnerState`，不是金样 |

建议（不阻断 T-004 验收，但应记入 T-007 的表征做法）：让 premiums 与 lottery 的插入顺序与排序键相反；补一条「账号没有 account_settings 行」的用例。

### 复核通过、无需改动的部分

- 关系那处的等价性成立：原 JOIN 的 `IsDBNull(5) || support.account_id != aid || support.delete_flag != 0` 与新实现的花名册成员资格等价——花名册语句是 `WHERE account_id=@aid AND delete_flag=0 ORDER BY slot_index,character_id`，**无 LIMIT、无其他过滤**，`characters.character_id` 是主键，故「在花名册里」⇔「存在且属本账号且未删除」。
- 三处顺序等价：`OrderBy` 稳定排序 + 花名册槽位唯一（`Account roster contains duplicate slots` 仍在），与原 `ORDER BY owner.slot_index,r.slot` / `ORDER BY c.slot_index,u.sort_order` / `ORDER BY c.slot_index,c.character_id` 给出同一序列。
- `GetOrCreateAsync` 的输入校验与两条错误消息（`Login ID must contain between 1 and 256 characters.` / `Password hash cannot exceed 256 characters.` / `Account '{loginId}' could not be resolved after creation.` / `Account created_at value '{value}' is invalid.`）逐字保留。
- 原实现全部 16 处 `InvalidDataException` 抛出点，除 F-1 外均在新实现中保留且条件等价。
- `character_init_flags(character_id INTEGER PRIMARY KEY)`，故 `PcRoomStatesAsync` 里 `ToDictionary` 不可能撞重复键；原实现 `pcRoom.Count != roster.Count` 那半个条件本就不可达。
- D 项：`SqliteDatabase` 构造即建库不构成「只读场景意外建库」的新问题——`ea95d49` 下两个账号仓储的构造函数同样调 `CharacterDatabaseSchema.Ensure`，且新注册是工厂式延迟解析。`SchemaOwnershipTests.TheShapeOfTheDatabaseDoesNotDependOnWhatIsBuiltFirst` 确实近乎空转，但这在基线就已如此（`git grep "Schema.Ensure" ea95d49` 显示七个仓储调的都是同一个 `CharacterDatabaseSchema.Ensure`），不是 T-004 引入的退化；该文件真正还在起作用的断言是 `Assert.Equal(52, ...)`。

### 次要观察（不要求本轮处理）

- 畸形数据下的异常**消息**次序有变：原实现读完八张表才判 `roster.Count == 0`，新实现在读完钱包后即判，故「无角色 + premium 越界」等组合下抛出的消息与原来不同（类型仍是 `InvalidDataException`）。同理，关系的行内范围校验现在早于成员资格校验。
- 边界外的 `LoadAsync` 不再是一份快照：原实现自开一条事务包住八次读，现在无环境事务时是八条独立连接。生产的登录与选角两条路径都已是 `Transactional`，故无实际影响。

### 需要的补救

1. 恢复 F-1 的 64 KiB 校验并补一条会红的用例。
2. 把 F-2 的八个文件转成 CRLF。
3. 改写 Executor Self-check 1 中 F-8 的移交理由（F-3），据实说明选角链已经走到 `SqliteDatabase`。

## Execution Result 2

### F-1｜真行为回归：设置的 64 KiB 上限丢了

成立，而且这是本任务唯一一处真正的功能损失。被删掉的 `ReadBlob` / `ReadOptionalBlob` 对四个 blob 列都判长度上限并抛 `"{name} exceeds the supported size."`；我重写时只保留了空值与类型判断，长度判断整个消失。

两条金样都看不见它——**一次拒绝不产生记录**，与关系归属那条是同一类盲区。

修复：`SqliteAccountSettingsTable.OptionalBlob` 恢复上限判断。两处细节按原实现对齐：

- 错误消息用原文 `"{name} exceeds the supported size."`，不加 "Account " 前缀；
- 取值改回硬转型 `(byte[])`。我原本写了个更友好的 `as byte[] ?? throw InvalidDataException`，但那把 `InvalidCastException` 换成了别的异常类型——在一次行为保持型重构里，这不是改进，是另一处不该有的差异。

新增 `SettingsTooLargeToBeReasonableAreRefused`：播入 65537 字节断言拒绝。

### F-2｜八个新文件是 LF 不是 CRLF

成立。`.editorconfig` 要求 CRLF，同批的其他文件也是 CRLF。八个文件已全部归一化。

原因是我对新写的文件逐个手动归一化，漏了这八个——这种事不该靠记性。已改为对 `Server/` 下全部 `.cs` 扫一遍，只要含 `\n` 而不含 `\r\n` 就转换。

### F-3｜移交理由是错的

成立。我写「账号侧仓储只在登录路径上，不在选角链上」——**不对**。`SelectCharacterPreparationService.cs:41` 注入 `IAccountEntryStateDomainService`，`:123` 在 `PrepareAsync` 中调用，一路落到本任务新建的 `AccountEntryAssembly` → `SqliteDatabase`。而选角准备命令是 `Transactional`。

**结论仍然是「移交给 T-005」，但理由换成真的那个**：账号装载发生在 `SelectCharacterPhaseCommands.cs:108` 的 `preparation.PrepareAsync`，而被顶掉会话的清理在 `:99`——**清理排在前面**。清理开第二条连接并当场提交时，环境事务还没有经 `SqliteDatabase` 做过任何读写，因此不冲突。

隐患成真的条件是「环境事务在清理之前已经读过或写过」。`PreflightAsync`（`:47`）调的三个仓储与 `SaveExistingLeaseAsync`（`:57`）调的租约仓储，今天都还自开连接——**它们正是 T-005 与 T-006 要接入的**。所以 T-005 接入的那一刻，隐患就成真。

### F-4｜两处「排序去掉也不变」，但不是盲区

评审去掉 premiums 与 lottery 的 `ORDER BY`，金样仍绿。我核查了原因，**不是种子分辨不出，是没有可分辨的差异**：

```
account_premiums(... PRIMARY KEY(account_id,premium_type))
account_increase_chance_lottery_progress(... PRIMARY KEY(account_id,item_template_id,reward_index))
```

两张表的复合主键前缀都是 `account_id`，其后正是各自 `ORDER BY` 的列。查询带 `WHERE account_id=@aid`，SQLite 走该索引，返回顺序本就是那个顺序——`ORDER BY` 对这两条查询是空操作。

顺带仍把种子的插入顺序改成与排序相反，因为它不花什么代价，而且哪天索引或查询形状变了，它就重新变成有意义的。

**settings 缺行时的默认值那处，评审说得对**：金样总是播了 settings 行，走不到缺行分支，而首次登录走的正是该分支。这一条我暂不补——它属于「缺行返回什么」，与本任务拆表无关，且既有的 `SqliteAccountEntryStateRepositoryTests` 里有覆盖。记录在案。

### 复验

`dotnet build`：0 警告 0 错误。全量 **1031 通过 / 2 跳过 / 0 失败**（基线 1011 / 2 / 0，增量 20）。

## Executor Self-check 2

| 验收条件 | 状态 |
|---|---|
| 1 账号侧拆出的每个仓储只提及一张表 | 已闭合，八个逐一清点 |
| 2 账号侧新建连接与开启事务的处数为零 | 已闭合，实测为 0 |
| 3 账号侧的跨表组装位于领域层 | 已闭合，三处 JOIN 携带的顺序与校验逐条落位，各带证伪；两处拒绝另有专门用例 |

### 移交给 T-005（更正后）

1. **F-8 会在 T-005 接入仓储的那一刻成真**。今天不冲突是因为清理排在账号装载之前，而清理之前的那几个仓储还没接入。T-005 接入 `PreflightAsync` 用到的三个仓储之后，环境事务会在清理之前就已读写。做对的判据见 T-002 的 Execution Result 4。
2. **登录的语义变化已成真**：账号写入现在必须在用例边界内，首次登录中途失败则账号不存在。

## Independent Quick Review 2

- 评审对象：Task Unit Contract（含 Contract Amendment A1）+ Execution Result 2 + Executor Self-check 2；本轮只判 Quick Review 1 的 F-1/F-2/F-3 是否闭合、F-4 的解释是否属实，以及补救本身有无新问题
- Owned diff 身份：相对基线 `ea95d49` 的未提交工作树，与第 1 轮同一组 T-004 文件（新增 `Database/{SqliteTable,SqliteDatabase}.cs`、`Abstractions/Persistence/{AccountTables,CharacterTables}.cs`、`Persistence/Accounts/AccountTables.cs`、`Persistence/Characters/CharacterEntryTables.cs`、`Application/Accounts/{AccountEntryAssembly,AccountProvisioning}.cs`、`Persistence.Tests/Accounts/AccountEntryLoadCharacterizationTests.cs`、`Persistence.Tests/Support/AccountSide.cs`；改两个 Module 与四个既有测试文件；删两个旧账号仓储）
- 结论：**passed**

### 核对过的证据

| 项 | 命令或位置 | 结果 |
|---|---|---|
| 全量复验 | `dotnet test Server/86JP.Server.slnx -m:1` | 1031 通过 / 2 跳过 / 0 失败（60+499+4+99+46+120+203），日志零 warning；与 Execution Result 2 声称一致，且无需隔离复跑（`Host.Tests`、`Networking.Tests` 本次未撞预算） |
| F-1 逐字等价 | `git show ea95d49:.../SqliteAccountEntryStateRepository.cs` 第 267-287 行 vs `Accounts/AccountTables.cs` 第 128-153 行 | 等价（详见下） |
| F-1 证伪 | 注入：把 `OptionalBlob` 的长度判断删成 `return value;` | `SettingsTooLargeToBeReasonableAreRefused` **转红**（`Assert.Throws() Failure: No exception was thrown`），其余三条仍绿；已复原，md5 与备份相同 |
| F-1 重查 16 处 | 逐条比对原实现全部 `InvalidDataException` 抛出点 | 无第二处遗漏（详见下） |
| F-2 | 对 `git ls-files -m` + `git ls-files -o --exclude-standard` 的全部 30 个 `.cs` 逐字节数 CRLF 与前三字节 | 30/30 文件 CRLF 行数 == 总行数，无一含 BOM；八个曾为 LF 的文件已全部归一 |
| F-3 | `Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs`、`PersistenceModule.cs`、`CharacterDomainService.cs`、`SelectCharacterEntryInventoryDomainService.cs` | 新理由三点全部属实（详见下） |
| F-4 | 临时探针跑 `EXPLAIN QUERY PLAN` 与 `NOT INDEXED` 对照（探针文件已删除） | 执行者的解释属实（详见下） |
| 金样稳定性 | 连跑 6 次 `--filter FullyQualifiedName~AccountEntryLoadCharacterizationTests` | 6/6 全绿 4/4，无抖动；`dotnet build` 0 警告 0 错误 |
| 拒绝用例可证伪 | 注入①删 `AccountEntryAssembly.RelationsAsync` 的 `!slotOf.ContainsKey` 校验；注入②把两处 `OrderBy(slotOf[...])` 改成按角色 id 排 | ①`ARelationPointingOutsideTheAccountIsRefused` 转红；②`AnAccountIsLoadedWithExactlyWhatItWasLoadedWithBefore` 转红。种子改动后金样仍能看见顺序 |
| 探针纪律 | 注入前 `cp` 备份，跑完立即 `cp` 回；`git status --porcelain` 与开始时逐字一致（`diff` 无输出），两个被注入文件 md5 与备份相同 | 已复原 |

### 发现

**F-1（已闭合）。**
（a）逐字等价成立。`SqliteAccountSettingsTable`（`Accounts/AccountTables.cs:134,139-153`）的 `MaximumBytes = 64 * 1024` 与原 `SettingsMaximumBytes` 同值；消息 `$"{name} exceeds the supported size."` 无前缀，与 `ea95d49:276`/`:285` 逐字相同；四个 name 参数 `"main game options"`/`"quick-chat bank 0"`/`"quick-chat bank 1"`/`"account hotkeys"` 逐字保留；取值仍是硬转型 `(byte[])reader.GetValue(ordinal)`，非 `byte[]` 时仍抛 `InvalidCastException` 而非别的类型；null 情形与原来一致——`OptionalBlob` 返回 `null`（对应原 `ReadOptionalBlob`），`Blob = OptionalBlob ?? []`（对应原 `ReadBlob(..., allowNull: true)`）；阈值方向也一致，原 `> limit` 抛、新 `<= limit` 放行，65536 字节两者都放行。
（b）注入证伪成立，见上表。
（c）重查结论：原实现共 **18 处** `InvalidDataException` 抛出点（`ea95d49` 第 52/54/95/101/124/141/161/165/183/187/210/215/238/258/261/272/276/285 行），其中 `:276` 与 `:285` 是同一条消息的两个副本、`:272` 的 `"Required {name} is unavailable."` 属**死代码**（`ReadBlob` 的三个调用点 `:117`/`:118`/`:120` 全是 `allowNull: true`，`allowNull: false` 分支永不进入）。去重去死代码后 15 条活的消息，在新实现中逐条命中（`grep` 计数 1/1/1/…，`PC-room compatibility state is unavailable` 命中 2 处是因为原 LEFT JOIN 的 NULL 与缺行两种情形分别落在表仓储与领域层）。条件亦逐条等价，两处需要说明的：
- `"Unknown730 compatibility ordering is invalid."` 原条件含 `slot_index is < 0 or > ushort.MaxValue`，新实现（`Characters/CharacterEntryTables.cs:117`）只留 `SortOrder >= 0`。丢掉的那半个条件在原实现里就已不可达——`LoadRosterAsync` 排在 `LoadUnknown730Async` 之前且对同一批角色的 `slot_index` 做同样的范围判断，越界会先抛 `"Account roster state is out of range."`。今天由 `SqliteCharacterRosterTable.Row`（`:54`）承担，行为不变。
- `"Account PC-room compatibility state is missing or inconsistent."` 的 `pcRoom.Count != roster.Count` 那半个条件同样本就不可达（`character_init_flags(character_id INTEGER PRIMARY KEY)`），第 1 轮已核。
**除已修的这处外没有第二处遗漏。**

**F-2（已闭合）。** 八个文件现均为 CRLF、无 BOM：`Database/SqliteTable.cs`(99)、`Abstractions/Persistence/AccountTables.cs`(64)、`Abstractions/Persistence/CharacterTables.cs`(54)、`Accounts/AccountTables.cs`(199)、`Characters/CharacterEntryTables.cs`(153)、`Application/Accounts/AccountEntryAssembly.cs`(169)、`Application/Accounts/AccountProvisioning.cs`(44)、`Persistence.Tests/Support/AccountSide.cs`(52)——括号内为总行数，CRLF 计数与之相同。把范围放大到本工作树全部 30 个已改/未跟踪 `.cs`，同样 30/30 CRLF、0 BOM，**本轮没有引入新的 LF 文件**。归一化脚本也没有波及边界外：`git diff --stat ea95d49 -- Server/` 仍是 289 增 / 458 删，无行尾批量翻动；`Protocol/Game/**` 下几个基线就带 BOM 的文件未被触碰。

**F-3（已闭合，新理由属实）。** 三点逐个核实：
1. 次序如所述。`SelectCharacterPhaseCommands.cs:108` 是 `preparation.PrepareAsync`；被顶掉会话的清理在 `:99`（`RegisterReplacing` 后对 `displaced`），另有一处同连接重入的清理在 `:62`——**两处都排在 `:108` 之前**。
2. `PreflightAsync`（`:47`）今天只经 `characters.LoadByRosterPositionAsync` 触达 `ICharacterRepository`（`CharacterDomainService.cs:62-78`，内部两次调用：`ListByAccountAsync` 与 `FindByAccountAndSlotAsync`），另一路 `channels.Refuse` 是 `ChannelEntryPolicy`（`ApplicationModule.cs:124`，纯内存）。`ICharacterRepository` 在 `PersistenceModule.cs:70-71` 注册为 `new SqliteCharacterRepository(databasePath)`——**自开连接**。
3. `SaveExistingLeaseAsync`（`:57`）经 `persistence.SaveAsync` 落到 `IEntryInventoryLeasePersistence` = `SqliteInventoryEntryRepository(databasePath)`（`PersistenceModule.cs:73,78-79`）——**自开连接**。
反证也做了：全仓只有三个持久层文件提到 `SqliteDatabase`（`Database/SqliteTable.cs`、`Accounts/AccountTables.cs`、`Characters/CharacterEntryTables.cs`），而八个新契约的消费者只有 `AccountEntryAssembly` 与 `AccountProvisioning` 两处。所以选角用例在 `:62`/`:99` 之前不会经 `SqliteDatabase` 读写，`SqliteDatabase.Participant()` 也就不会 `Enlist`。**隐患今天尚未成真，不阻断**；T-005 接入 `PreflightAsync` 那几个仓储的那一刻成真，这一点写进移交是对的。

**F-4（解释属实）。** 临时探针按 `CharacterDatabaseSchema.cs:162-163` 的原样建表、按当前种子的倒序插入后：

| 语句 | EXPLAIN QUERY PLAN | 去掉 ORDER BY 的实际返回序 |
|---|---|---|
| `SELECT premium_type,end_time FROM account_premiums WHERE account_id=12` | `SEARCH ... USING INDEX sqlite_autoindex_account_premiums_1 (account_id=?)` | `(1007,1008),(1009,1010)` —— 已排好 |
| 同上加 `NOT INDEXED` | `SCAN account_premiums` | `(1009,1010),(1007,1008)` —— 插入序 |
| `SELECT item_template_id,reward_index FROM account_increase_chance_lottery_progress WHERE account_id=12` | `SEARCH ... USING COVERING INDEX sqlite_autoindex_..._1 (account_id=?)` | `(1011,3),(1012,4)` —— 已排好 |
| 同上加 `NOT INDEXED` | `SCAN ...` | `(1012,4),(1011,3)` —— 插入序 |

`NOT INDEXED` 那两行正是判别项：种子**能**分辨（插入序与排序键相反），返回序之所以不变，是复合主键自动索引给出的，不是金样瞎。执行者「不是种子分辨不出，是没有可分辨的差异」的说法成立，**不是第 1 轮 F-4 的那类盲区**。第 1 轮那条建议按结论应下调为「已解释」，不必再记入 T-007。

### 本轮补救未引入新问题

- 两条金样连跑 6 次全绿，`AnAccountIsLoadedWithExactlyWhatItWasLoadedWithBefore` 的 48 行断言与 `AccountSettingsAloneAreLoadedWithExactlyWhatTheyWereLoadedWithBefore` 的 5 行断言未随种子改动而调整——种子只把 premiums/lottery 的**插入**顺序倒过来，期望值本就是排序后的顺序。
- 三条拒绝/顺序注入全部转红：删成员资格校验 → `ARelationPointingOutsideTheAccountIsRefused` 红；删长度上限 → `SettingsTooLargeToBeReasonableAreRefused` 红；改两处排序键 → 48 行金样红。
- `SqliteAccountSettingsTable` 这一轮只增了 `MaximumBytes`/`Blob`/`OptionalBlob` 三处，`Settings(...)` 的读取次序、`hotkeys.Length >= 2 ? hotkeys[..2] : []` 与 `"Account settings are malformed."` 的条件均未动。

### 次要观察（不要求处理，供 T-007 参考）

- 第 1 轮 F-4 里「settings 缺行时的默认值」那条只被部分覆盖：`SqliteAccountEntryStateRepositoryTests.AccountEntryState_ApplicableCollectionsLoadAsEmpty`（账号 52 无 `account_settings` 行）确实走到该分支，但只断言了 `MainGameOptions` 为 null，默认记录另外四个分量（`[]`,`[]`,`0`,`[]`）仍无断言。执行者「既有测试里有覆盖」的说法成立但不完整，与其「记录在案、本任务不补」的处置不冲突。
- `ReadBlob` 的 `allowNull: false` 分支在 `ea95d49` 就是死代码，新实现没有对应形状。若将来有列需要「缺失即拒」，得重新写出来，而不是以为它还在。

# Change Review — spec-004 技能按概念重切

被评审的完整改动：基线 `1a77ef8a1de23ebb0d6ba283f44ca178b2496953` 之上的未提交工作区，12 个已跟踪文件（143 增 87 删）加两个新测试文件（`Schema/SkillCarrierShapeTests.cs` 13 条、`Characters/SkillCarrierTests.cs` 16 条）。
验收依据：本 attempt 的 `TaskContract.md`（九条 SC）、`spec.md`（13 条 RQ / 12 条 AC）、`plan.md`（六个任务 / 14 条检查），以及设计权威 `Docs/Refactor/TableOwnershipRuling.md`（本次**未改动**，改与不改本身也在评审范围内）。

评审在一个独立的新上下文中进行，只拿到改动、验收边界与代码。全量套件由评审自己重跑核实：**1130 通过 / 2 跳过 / 0 失败**（`Dfo.Pvf` 60、`Application` 494、`Host` 4、`Infrastructure` 100 加 2 跳、`Networking` 46、`Persistence` 223、`Protocol` 203），与实施者报告一致。可证伪性做了**十次真改真跑的试验**（下称 E1..E15，编号按试验次序），每次试验后文件以 `sha256sum` 与试验前逐字节比对相同；交回时工作区无任何试验残留，`git status` 与 `git diff --stat` 与评审开始时逐行一致。

## 判定

`changes_required`，一条 A 级。

## A 级发现

### A-1 这次合并唯一的新失效模式——语句写错 `mode`——在两条写入语句上都无人守；为写入路径新写的那条用例即使写错也仍绿

合并把「哪一组页」从**表名**（两个类、两张表、两个接口）搬到了**一个字符串字面量**（一个类里的七条语句）。这一步把结构性保护换成了纪律，而纪律没有守卫接上。`SqliteCharacterSkillTable` 与 `CharacterTables.cs:113-118` 的类注释都把这件事说得很清楚——「a read that forgot would hand one set the other's rows」——但只有**读**的那一半被钉住了。

**两条写入语句改成相反的 mode，全量套件仍然全绿。** 评审实测：

| 试验 | 改动 | 结果 |
|---|---|---|
| **E1** | `CharacterSkillTables.cs:113` 的 `VALUES(@cid,'pvp',…)` → `'pve'` | 全量 **1130 通过 / 2 跳过 / 0 失败**，一条都不红 |
| **E4** | `CharacterSkillTables.cs:51` 的 `VALUES (@cid, 'pve', 0, …)` → `'pvp'` | 全量 **1130 通过 / 2 跳过 / 0 失败**，一条都不红 |

E4 直接命中本次为这条路径新写的那条用例。`SkillCarrierTests.ACharacterCreatedHoldingSkillsKeepsOneRowEach`（`Server/tests/Dfo.Server.Persistence.Tests/Characters/SkillCarrierTests.cs:37-62`）的整段注释说它守的正是「这是唯一写 PvE 技能行的路径」，但它回读用的 `SkillsAsync`（同文件 `:332-349`）是：

```csharp
"SELECT slot, skill_id, level FROM character_skills "
+ $"WHERE character_id={characterId.Value} AND page_index=0 ORDER BY slot"
```

**没有 `mode` 条件**，也没有 `SELECT mode`。于是建角色把三条初始主动写成 `mode='pvp'` 时，这条用例照样读到 `[(0,5,1),(1,46,1),(2,169,1)]` 并判绿。这正是评审要求里点名的那一类：**相关代码坏掉而用例仍绿**。

失败场景与后果，逐步可复现（以 E4 为准）：

1. 玩家建一个剑士（job 0）；`CharacterDomainService.cs:163-167` 取到三条主动，`CharacterRosterService.cs:99-104` 逐条调 `InsertAsync`；
2. 三行落库为 `mode='pvp'`、`page_index=0`、`slot` 0/1/2 —— `CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)` 不拦（页号是 0），`UNIQUE`、`PRIMARY KEY` 也都不拦，写入全部成功；
3. 该角色进普通频道：`ReadEntryAsync`（`CharacterSkillTables.cs:41`，`mode='pve'`）返回空 → `CharacterEntryAssembly.LoadSkillsAsync` 得到零行 → 选人下发帧里技能栏是空的；
4. 该角色进决斗频道：`ReadDuelSkillsAsync` 反而把这三行当决斗页读出来，`MergePvpProfile` 把它们当成播种结果下发。

E1 的对称后果：决斗播种写成 `mode='pve'` 后，`PvpSkillProfileService` 读回来是空的两页（决斗页全空），而这三十几条却混进了该角色普通频道的技能页。两种都是**只在实机才显形**的下发帧改变，正面违反 SC-7 / RQ-011。

**为什么现有用例接不住。** 决斗侧的写入语句今天**一次都没有被执行过**：全仓只有两处以真实持久化驱动 `PvpSkillProfileService`——`SqliteEntryLifecycleRecoveryTests.cs:33-35`、`:43-44` 与 `SkillCarrierTests.cs:241-250`——两处传的 `initialProfile` 都是**空的两页**（`CharacterSkillPageState(0, [])`、`(1, [])`），播种循环一行都不写。行为层守卫里那些 `mode='pvp'` 的行，全部是用例自己用 `INSERT INTO character_skills(...) VALUES(1,'pvp',...)` 直接播进去的，绕开了被评审的那条语句。

同一个空洞还让**播种序列本身**失去了判据。**E15**：把 `SelectCharacterPersistenceServices.cs:51-52` 的 `await pvp.MarkSkillsInitializedAsync(...)` 整段删掉 → `Persistence` 套件 **223 全绿**。因为两次调用传的都是空 profile，「播种」与「跳过」写出的结果完全一样，`PvpProfile_DistinguishesMissingFromInitializedAndLoadsBothPages` 这个名字承诺的区分并不成立。契约 SC-6 的 Required evidence 写的是「覆盖『未播种则播种、已播种则跳过』的用例通过，**且其判据仍是 `character_pvp.skills_initialized`**」——这条证据在实质上没有产出。

这不是覆盖净减少（改动前两条写入同样无人守），但它是本次合并**新造出来**的失效面：改动前两条写入指名两张不同的表，写错要写成另一张真实存在的表名；改动后只要一个三字母字面量。契约 SC-2 的 Required evidence 明写「两个 mode 下每一列的**读取与写入**走同一条路径」，SC-4 明写「用例覆盖 PvE 行的写入照旧成功」——写入这一半，今天由一条读不出 mode 的断言充当。

整改方向（都很小）：

1. `SkillsAsync` 加上 `AND mode='pve'`，或改成 `SELECT mode, slot, skill_id, level` 并把 `'pve'` 写进期望值——一行改动就让 E4 变红；
2. 补一条**非空** `initialProfile` 的播种用例：给两页各放一条技能走一次 `LoadOrInitializeAsync`，断言库里出现 `mode='pvp'` 的行且 `mode='pve'` 的行数不变——让 E1 变红；同一条用例再跑第二次（改动 profile 内容），断言行数与内容不变且 `skills_initialized=1`，让 E15 变红，SC-6 的判据才真正落地。

## B 级发现（记录，未改）

1. **裁定对这次收紧的自述不完整，而它自述漏掉的正是 spec 点名的那一条。** `Docs/Refactor/TableOwnershipRuling.md:951` 写「The `NOT NULL` and the cascade now bind PvE rows too, which is a tightening; nothing writes a PvE row without those values」——只列了两条。实际上它自己规定的 `UNIQUE(character_id, mode, page_index, skill_id)` **同样开始约束 PvE 行**，而 spec 的 Assumptions 把这一条单独加粗标为「裁定未讨论的一处收紧」，并要求「实施必须先证明这不会发生，或先更新裁定」。实施者选了前者（`PvfInitialSkillDefinitionSourceTests.No_job_declares_the_same_active_skill_twice`），plan T-002 第 3 步允许这条路径。但结果是：**这次合并现在依赖的一条档案事实（没有职业的初始主动里出现两次同一个 skill_id）只写在一个测试文件的注释和 implementation-state 里，设计权威一个字都没提**。裁定的 `:825-832` 也一样，只说「Five are `character_skills`'」的 `NOT NULL`。建议在 `:951` 那格补一句：唯一性也开始绑 PvE 行，依据是初始主动无重复，守卫是那条档案断言。
2. **另外三条 `mode='pve'` 条件同样无人守。** **E5-E7**：同时删掉 `ReadLevelsAsync`（`CharacterSkillTables.cs:31`）、`SetLevelAsync`（`:66`）、`RemoveAsync`（`:80`）三处的 `AND mode='pve'` → `Persistence` 套件 **223 全绿**。失败场景可指认：`ReadLevelsAsync` 丢掉条件后，专家契约回收（`SelectCharacterPersistenceServices.cs:121-139`）会把决斗页的行也拿去限级，随后调 `SetLevelAsync`（仍限 `mode='pve'`）触碰 0 行，`touched != 1` 抛 `InvalidDataException("Expert-contract skill row changed concurrently.")`——任何一个既持有决斗页又有过期专家契约的角色，选人直接失败。这与 A-1 是同一片空洞，但因为它至少会**抛异常而不是静默**，且不改变下发内容，只记录。
3. **「收紧无害」的唯一证据是一条 `[SkippableFact]`。** `PvfInitialSkillDefinitionSourceTests.cs:82-98` 以 `Skip.IfNot(ContentArchive.Available, …)` 开头。在没有 `Script.pvf` 的机器上它静默跳过，SC-4 的全部依据随之消失而套件仍报绿。本机跑到了（`Infrastructure` 100 通过、2 跳过，两条跳过是 quest/town 的「缺档案」用例），所以本轮证据成立；记录的是这条证据的可用性依赖环境。另：该断言只判「主动技能 id 不重复」，一个主动数为零的职业会平凡通过——由同文件 `:59` 的 `Assert.All(byJob, pair => Assert.NotEmpty(pair.Value))` 与 `:20-40` 的剑士全表断言兜住，不构成空洞。
4. **级联外键是一条不生效的声明，这是既有状况而非本次引入。** 全仓只有 `SkillCarrierTests.cs:194` 出现过 `PRAGMA foreign_keys`，且是用例自己为了分辨「声明还在」与「声明没了」而开的；`Server/src` 下一处都没有，SQLite 默认关闭。所以 `character_skills` 与 spec-002 留给 `character_pvp` 的两条 `ON DELETE CASCADE` 今天都只是文字。实施者的自述属实。本次照裁定把声明搬过来是对的（搬运不能丢东西），也不属本契约范围；记录的是「删角色时清不清技能行」这件事今天没有任何代码或声明真的负责——不过服务器今天也没有硬删角色的路径（`CharacterRosterService.cs:117-119` 的 `SoftDeleteAsync` 只置 `delete_flag`），所以不可达。
5. **`character_skills` 退出可补列清单的理由成立，但旧库的失败形态没有名字。** `Reconcile`（`CharacterDatabaseSchema.cs:129-161`）只会 `ALTER TABLE … ADD COLUMN`，`mode` 是 `NOT NULL` 且无默认值，SQLite 拒绝这样的 `ADD COLUMN`，所以理由成立；`:29-33` 的类注释也把它写下来了。移出后没有别的东西依赖那六条条目（`Reconcile` 只按 `Columns` 分组遍历，`character_pvp_skills|extra_values` 一并删掉且无其他引用；`SchemaOwnershipTests.BringingADatabaseIntoShapeAgainLeavesItAsItWas` 本轮绿）。记录的是后果：一个持有旧六列 `character_skills` 的开发库既不会被 `CREATE TABLE IF NOT EXISTS` 替换、也不会被补列，第一条技能语句会抛 `SqliteException: no such column: mode`，而不是一条说得清的拒绝；旧的 `character_pvp_skills` 也会作为孤儿表留在文件里。与本阶段「擦库不迁移」的既定做法一致，不建议改，只记录。
6. **`EverySkillColumnButTheBlobIsRequired` 判的是文本布局而不是可空性。** `SkillCarrierShapeTests.cs:74-82` 取 `sql.IndexOf(column + " ")` 到**下一个逗号**之间的片段找 `NOT NULL`。`mode` 那一格的片段今天恰好是 `mode TEXT NOT NULL CHECK (mode IN ('pve'`——逗号落在 `IN` 列表里面，`NOT NULL` 侥幸在片段内。把同一列改写成语义完全相同的 `mode TEXT CHECK (mode IN ('pve', 'pvp')) NOT NULL`，这条守卫会**误红**。`PRAGMA table_info` 的第四列 `notnull` 直接回答这个问题，且不受声明写法影响。六条断言本轮都能被真正的可空性改动打红（**E12**：`slot` 去掉 `NOT NULL` → `EverySkillColumnButTheBlobIsRequired(slot)` 与 `ASkillRowMissingOneOfItsRequiredValuesIsRefused(slot)` 各红一条），所以只是脆而不是坏。
7. **`ASkillRowMissingOneOfItsRequiredValuesIsRefused` 只断言异常类型。** `SkillCarrierTests.cs:85-87` 用 `Assert.ThrowsAsync<SqliteException>`，不看消息。若某列从声明里整个消失，`INSERT` 会因「no such column」抛同一个异常类型而仍然绿。由同批的 `TheSkillTableHasExactlyTheColumnsTheRulingGivesIt`（列集逐字比对）兜住，不构成空洞；记录的是这六条自己不能独立成立。
8. **`InsertDuelSkillAsync` 的空值行为悄悄换了异常类型。** `CharacterSkillTables.cs:109` 新加的 `ArgumentNullException.ThrowIfNull(extraValues)` 把改动前的 `NullReferenceException`（来自 `extraValues.Length`）换成了 `ArgumentNullException`。唯一调用方 `SelectCharacterPersistenceServices.cs:47` 传的是 `[.. skill.ExtraValues]`，永远非 null，所以不可达；是个改善，记录以示知情。
9. **`Skill` 读取器用全限定名而不是 using。** `CharacterSkillTables.cs:133` 写 `Microsoft.Data.Sqlite.SqliteDataReader`，同时文件顶部把 `using Microsoft.Data.Sqlite;` 删了。同目录的 `CharacterEntryStateTables.cs:5` 用的是 using。纯风格，零功能影响。

## 评审核实无误的部分

- **四条保证逐条追到落点，一条不少，限定方式全部正确。** 逐条对照 `git show HEAD:…/CharacterDatabaseSchema.cs:193` 的 `character_pvp_skills` 声明与新的 `:199`：
  - **五个非 BLOB 列的 `NOT NULL`** 原样落位，另加 `mode` 自己，共六个。`character_id` 被显式点名是必须的——SQLite 的复合主键不隐含 `NOT NULL`，裁定 `:951` 也专门说了这一点。**E12** 证明可证伪。
  - **`CHECK (page_index BETWEEN 0 AND 1)`** 落成 `CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)`。**限定得不松**：`mode` 是 `NOT NULL` 且只允许两个值，所以 pvp 行的 `mode <> 'pvp'` 恒假，页号范围与原来完全相同，没有 NULL 令 `CHECK` 放行的缝隙。**限定得不紧**：pve 行不受此限，与改动前 `character_skills` 无检查一致——`SkillCarrierTests.cs:99-115` 正面覆盖了「pvp 页 2 被拒、pve 页 2 被收」。**E8**：把这条 `CHECK` 从声明里删掉 → 形状守卫与 `ADuelPageBeyondTheSecondIsRefusedAndAnOrdinaryOneIsNot` 各红一条。
  - **级联外键**原样落位。**E10**：整条 `FOREIGN KEY … ON DELETE CASCADE` 删掉 → 形状守卫与 `ACharacterTakenAwayTakesBothSetsOfItsSkillsWithIt` 各红一条。
  - **`UNIQUE (character_id, page_index, skill_id)`** 落成 `UNIQUE (character_id, mode, page_index, skill_id)`。**限定得不松**：pvp 行的 mode 恒为 `'pvp'`，投影后与原约束逐行等价。**限定得不紧过头**：另一 mode 的同页同 `skill_id` 仍被允许，这是合并后必须保住的。**E9**：把 `mode` 从 `UNIQUE` 里去掉 → 形状守卫红，且 `TheSameSkillTwiceOnOnePageIsRefusedAndTheOtherModesPageIsNot` 的第三条插入（`'pvp',0,0,5,1`）被拒而红——**一条用例同时钉住了两个方向**。
  - **主键**由两表各自的 `(character_id, page_index, slot)` 落成 `(character_id, mode, page_index, slot)`，按 mode 投影后与两表各自等价。**E14**：主键去掉 `mode` → 形状守卫加三条行为守卫（`TheTwoModesDoNotSeeEachOther`、`TheSameSkillTwice…`、`ACharacterTakenAway…`）同时红。
  - **新增的 `CHECK (mode IN ('pve', 'pvp'))`**：**E13** 删掉 → 形状守卫与 `AThirdModeIsRefused` 各红一条。
- **`UNIQUE` 加到 PvE 行上这一收紧确实无害，且档案断言的职业范围就是服务器能建的范围。** (a) `PvfInitialSkillDefinitionSourceTests.cs:89` 循环 `job = 0..12`，而 `CharacterDomainService.cs:24` 的 `MaxJobId = 12`、`:103` 对 `job > MaxJobId` 直接返回 `MalformedRequest`——**逐一相等，不多不少**；测试的 `.Where(grant => grant.IsActive)` 与生产 `:166` 的 `grants.Where(value => value.IsActive)` 是同一个筛选。(b) 写 PvE 技能行的路径全仓只有 `ICharacterSkillTable.InsertAsync` 一条，调用方只有 `CharacterRosterService.cs:101`；`SetLevelAsync` 只改 `level`、`RemoveAsync` 只删行，都不可能造出重复的 `skill_id`；`Tool/` 下 `character_skills` 一次都没出现。(c) `NOT NULL` 的收紧：`InsertAsync`（`:50-51`）六个值全给，`character_id` 由 `characters.InsertAsync` 先返回；级联外键的收紧：角色行在技能行之前写（`CharacterRosterService.cs:95-104`），且外键运行时本就不生效（B-4）。三条收紧都不会让任何现行写入失败。
- **PvP 侧的播种其实也不可能撞新的 `UNIQUE`。** `SelectCharacterPreparationService.cs:351-352` 先 `GroupBy(SkillId).Select(…First())`，所以一页之内 `skill_id` 天然唯一；`:382` 的 `occupied` 保证 slot 唯一；同一个 `skills` 列表被同时挂到第 0 页与第 1 页（`:384`），两页 `page_index` 不同，主键不撞。这一条在改动前就由 `character_pvp_skills` 的同名约束管着，强度未变。
- **七个方法逐条限定了 mode，且没有一条用错。** `ReadLevelsAsync`（`:31` pve）、`ReadEntryAsync`（`:41` pve）、`InsertAsync`（`:51` pve）、`SetLevelAsync`（`:66` pve）、`RemoveAsync`（`:80` pve）、`ReadDuelSkillsAsync`（`:94` pvp）、`InsertDuelSkillAsync`（`:113` pvp）。语义逐条核过：前五个服务的是花技能点与专家契约回收，后两个服务的是决斗频道播种，与两个接口注释所说一致。全仓再无第二处指名 `character_skills` 的 SQL（`Server/src` 下只有这七条加声明），`ICharacterPvpSkillTable`、`CharacterPvpSkillRow`、`SqliteCharacterPvpSkillTable` 与它们的注册（`PersistenceModule.cs`）全部退役且无残留引用。**E2/E3**：两条读取同时去掉 mode 条件 → `EachReadAnswersWithItsOwnModeAndNoneOfTheOther` 立刻红；该用例的夹具（pve 一行、pvp 两页各一行）对两个方向都敏感——pve 读丢条件会答 `[100,200,300]`，pvp 读丢条件会让第 0 页变成 `[100,200]`。**读的那一半确实钉住了；写的那一半没有（A-1）。**
- **`extra_values` 的空值语义两侧一致，1024 上限加到决斗行上不可达。** `EntryBlob.Bounded(reader, ordinal, max, name, allowNull: true)`（`CharacterEntryStateTables.cs:25-37`）在 `IsDBNull` 时返回 `[]`，与改动前 PvP 侧 `reader.IsDBNull(4) ? [] : …` 逐字等价；写入侧 `extraValues.Length > 0 ? extraValues : DBNull.Value`（`:122-123`）原样保留，PvE 侧仍恒写 `NULL`。所以 NULL → 空数组、空数组 → NULL 两个方向都没变。上限这一处**是**行为变更（决斗行原本无上限，现在超过 1024 字节会抛 `InvalidDataException`），但**不可达**：决斗行的 `extra_values` 唯一来源是 `SelectCharacterPreparationService.cs:382` 的 `new CharacterSkillState(…, [])`——播种永远传空数组，落库永远是 `NULL`。这处收紧已写在 `CharacterSkillTables.cs:20-25` 的常量注释与 implementation-state 的「计划内的有界调整」里，记录充分。
- **`PvpSkillProfileService` 的读标记—写技能—写标记序列一字未动，判据仍是 `character_pvp.skills_initialized`。** `SelectCharacterPersistenceServices.cs:36-53`：`pvp.FindAsync` → `SkillsInitialized == 0` → 逐条 `InsertDuelSkillAsync` → `MarkSkillsInitializedAsync`。改动只有两处方法名（`pvpSkills.InsertAsync` → `skills.InsertDuelSkillAsync`、`pvpSkills.ReadAsync` → `skills.ReadDuelSkillsAsync`）与一个构造参数。`MarkSkillsInitializedAsync` 仍是 `UPDATE character_pvp SET skills_initialized=1`（`CharacterBodyTables.cs:179-182`）。**序列与判据都对；缺的只是证明它的用例（A-1 末段）。**
- **两张职业专属页表确实没动，非本职业持有仍被拒绝，新补的用例可证伪。** `character_dark_knight_combo_skill_pages` 与 `character_knight_shield_deck` 的声明、读取类与接口逐字未改；`SkillCarrierShapeTests.cs:42-43` 断言两张都还在。拒绝逻辑在 `CharacterEntryAssembly.cs:261-269`。**E11**：把 `job != 9` 与 `job != 12` 两个条件替换成恒假 → `AJobSpecificPageHeldByAnotherJobIsRefused` 的两例同时红（`owningJob: 9` 与 `owningJob: 12`）。用例走的是真实的账号与角色创建路径（`AccountSide.GetOrCreateAsync` + `CharacterSide.PerCall.CreateAsync`），并断言消息含 `inapplicable`，所以别的异常冒充不了它。契约 SC-5 点名的这条证据此前全仓无人守，本次补上。
- **表征用例的期望值一字未改。** `EntryLoadCharacterizationTests` 的 diff 只有一行 `INSERT INTO character_skills VALUES(22,'pve',0,4,100,5,X'0908')`；`SqliteCharacterEntryStateRepositoryTests` 两行同类；`InventoryLoadCharacterizationTests` 与 `AccountEntryLoadCharacterizationTests` 完全未动。位置参数与新的列序（`character_id, mode, page_index, slot, skill_id, level, extra_values`）逐位对得上，插进去的还是原来那一行。CK-009 成立。
- **领域记录确实没动。** `git diff --stat Server/src/Dfo.Server.Data/` 为空，`CharacterModels.cs` 与 `ICharacterEntryStateServices.cs` 逐字未改。定义点还少了一个：`CharacterPvpSkillRow` 退役，两侧共用 `CharacterSkillEntryRow`——同五个字段，本就说不出表说不出的话。
- **一表一类成立，表数是 43。** 评审自跑清点（剥掉注释后按类切片，避免把下一个类的文档注释算进上一个类）：`Dfo.Server.Persistence` 下 **68 个类，零个指名超过一张表**；`SqliteCharacterSkillTable` 只指名 `character_skills`。表声明 44 处 `CREATE TABLE IF NOT EXISTS` 减去类注释里那一处提及 = **43 张**，与 `SchemaOwnershipTests.cs:58` 的 `Assert.Equal(43, …)` 一致（该断言由 `PRAGMA`-读回的真实库计数，`sqlite_%` 已排除）。
- **裁定不改是对的。** `TableOwnershipRuling.md` 早已按 43 张表与 7 列的 `character_skills` 写成：`:900` 的 `mode` 列声明、`:951` 的键与约束、`:1082-1097` 的逐列账本，与 `CharacterDatabaseSchema.cs:199` 的实际声明**逐项相同**（键、唯一、两条 `CHECK`、六个 `NOT NULL`、级联外键，一字不差）。M-4（`:290-308`）要求的「四条各自存续、按页的用 mode 限定」也逐条做到。唯一该补而未补的是 B-1 那句话。
- **用例数增减全部可解释。** 1102 → 1132（+30）：+1 档案重复守卫、+13 形状守卫（2 Fact + 5 约束 Theory + 6 `NOT NULL` Theory）、+16 行为守卫（1 建角色 + 6 `NOT NULL` + 1 页范围 + 1 第三 mode + 1 同技能 + 1 同槽 + 1 级联 + 1 两 mode 计数 + 1 两 mode 读取 + 2 职业专属页）。**无删除、无取代**——`git status` 没有任何 `D` 行，不存在「删测试前要逐条核对它守什么」的问题。
- **构建零警告。** 全量七个测试程序集在评审的多轮运行中均无 `warning CS`。

## 结论

`changes_required`。一条 A 级。

形状、四条保证的落点与限定方式、收紧的三处证明、七条语句的 mode、`extra_values` 的两侧语义、播种序列与判据、两张职业专属页、表征期望值、领域记录、一表一类与 43 张表——评审逐条核实无误，且十次真改真跑的试验里有八次证明相应守卫会红。裁定与实现逐项对得上，不改裁定是对的。

失守的是唯一没有证据覆盖的那条路：**写入**。合并把「哪一组页」从表名降级成一个字面量，读的那一半立刻有了守卫，写的那一半两条语句都没有——把 `'pve'` 与 `'pvp'` 各自改成对方，全量套件两次都是 1130/2/0 全绿；而其中一次改的正是本次为写入路径新写的那条用例所声称守护的东西。补两处小改动（回读查询加 `mode`、加一条非空 profile 的播种用例），A-1 与 B-2 一并变红，SC-2、SC-4、SC-6 的写入侧证据才算交齐。

九条 B 级随记录交出，均不影响正确性；其中 B-1（裁定漏记 `UNIQUE` 的收紧、以及它现在依赖的那条档案事实没有进裁定）值得在下一次触碰裁定时顺手补上。

## 第二轮

在一个新的独立上下文中进行，范围窄：只核实第一轮那条 A 级是否真的修好、接受的那条 B 级改动是否与代码一致、以及第一轮九条 B 级有没有因整改而升级。不重做第一轮，不新开话题。

自跑全量核实：**1132 通过 / 2 跳过 / 0 失败**（`Dfo.Pvf` 60、`Application` 494、`Host` 4、`Infrastructure` 100 加 2 跳、`Networking` 46、`Persistence` 225、`Protocol` 203），与实施者报告逐项相同，七个测试程序集无一条 `warning CS`。`Persistence` 由第一轮的 223 变 225，Δ+2 正是新增的两条用例。

可证伪性做了**七次真改真跑的试验**（下称 R1..R7），每次都以 `Dfo.Server.Persistence.Tests` 整个项目（225 条）为判据而不是更窄的过滤，以免"只有被点名的那条会红"这件事本身没被验证。每次试验后文件用 `sha256sum` 与试验前逐字节比对相同；交回时工作区无任何试验残留，`git status --short` 与 `git diff --stat`（13 个已跟踪文件、157 增 87 删、两个新测试文件与本 attempt 的过程工件）与本轮开始时逐行一致。

### 判定

`passed`。A-1 确已修好，七处逐一可证伪；整改未引入新的 A 级；第一轮九条 B 级无一升级。

### A-1 的七处核实——七处全红

| 试验 | 改动 | 结果 |
|---|---|---|
| **R1** | `CharacterSkillTables.cs:51` 普通插入 `VALUES (@cid, 'pve', …)` → `'pvp'` | **红 1 条**：`SkillCarrierTests.ACharacterCreatedHoldingSkillsKeepsOneRowEach`（224/1/0） |
| **R2** | `CharacterSkillTables.cs:113` 决斗插入 `VALUES(@cid,'pvp',…)` → `'pve'` | **红 1 条**：`SkillCarrierTests.SeedingTheDuelPagesWritesThemOnceOnTheDuelSide`（224/1/0） |
| **R3** | `SelectCharacterPersistenceServices.cs:51-52` 的 `await pvp.MarkSkillsInitializedAsync(...)` 整段删掉 | **红 1 条**：`SeedingTheDuelPagesWritesThemOnceOnTheDuelSide`（224/1/0） |
| **R4** | `ReadLevelsAsync`（`:31`）去掉 `AND mode='pve'` | **红 1 条**：`LevellingAnOrdinarySkillLeavesTheDuelRowAtTheSamePlaceAlone`（224/1/0） |
| **R5** | `SetLevelAsync`（`:66`）去掉 `AND mode='pve'` | **红 1 条**：同上（224/1/0） |
| **R6** | `RemoveAsync`（`:80`）去掉 `AND mode='pve'` | **红 1 条**：同上（224/1/0） |
| **R7a** | `ReadEntryAsync`（`:41`）去掉 `AND mode='pve'` | **红 1 条**：`EachReadAnswersWithItsOwnModeAndNoneOfTheOther`（224/1/0） |
| **R7b** | `ReadDuelSkillsAsync`（`:94`）去掉 `AND mode='pvp'` | **红 1 条**：同上（224/1/0） |

第一轮点名的全绿现象——E1（决斗插入写错）、E4（普通插入写错）、E15（标记不写）、E5-E7（三条 pve 语句丢条件）——**六处全部翻红**，加上第一轮本就红的两条读取，`SqliteCharacterSkillTable` 七条语句的 mode 字面量与那一次标记写入，**八处逐一有守卫**。A-1 的整改方向三条（回读加 `mode`、非空 profile 的播种用例、三条 pve 语句的用例）与实际落地逐条对得上。

需要指出的是每次试验都**只红被点名的那一条**，说明这些守卫钉的正是各自声称的那件事，没有靠一片连带失败蒙混。

### 整改本身有没有引入新问题——没有

**只动了测试与文档，产品代码一个字未改。** 文件时间戳（`SkillCarrierTests.cs`、`CharacterSide.cs` 05:49，`TableOwnershipRuling.md` 05:50，两个产品文件停在整改前）与 `git diff --stat` 的算术两条独立证据一致：第一轮 12 个已跟踪文件 143 增 87 删 → 现 13 个 157 增 87 删，多出的一个文件是裁定（+10），多出的 14 行插入 = 裁定 10 + `CharacterSide.cs` 4，**删除数一行未变**。所以整改是"只加守卫、不动被守卫的东西"，这是修 A-1 该有的形状。

**`CharacterSide.Skills` 合适。** 它把 `ICharacterSkillTable` 直接摆给用例，绕过了领域层——但这是 `Dfo.Server.Persistence.Tests` 的夹具，同一个类本来就在按服务器的接法拼装持久化层（`Roster`、`PvpProfile`、`ExpertContracts`、`EntryState`），而 `LevellingAnOrdinarySkillLeavesTheDuelRowAtTheSamePlaceAlone` 要钉的正是"一条语句碰到哪些行"，经过 `ExpertSkillContractReconciliationService` 反而要先造出过期的专家契约才能触发同一条语句。属性上的那句 doc 注释把这个理由写下来了。返回新实例、共用同一个 `SqliteDatabase`，与相邻属性写法一致。

**两条新用例的夹具真实。**

- `SeedingTheDuelPagesWritesThemOnceOnTheDuelSide` 走 `WithACharacterAsync`（真实账号 + `CharacterSide.PerCall.CreateAsync` 建角色），`character_pvp` 行由建角色写出，所以 `pvp.FindAsync` 与 `skills_initialized` 的读取都是真的。profile 非空是这条用例的全部要点，`Profile(700, 800)` 给两页各放一条、且两页的 `skill_id` 与 `slot` 都不同——比生产更严（生产 `SelectCharacterPreparationService.cs:384` 给两页挂同一个列表），因此页号写反也会红，而这不构成不真实：两页装不同技能在形状上完全合法。
- `LevellingAnOrdinarySkillLeavesTheDuelRowAtTheSamePlaceAlone` 的种子是同一 `page_index=0`、同一 `slot=1` 上一条 pve 一条 pvp。这在生产里真会出现——第零页第一槽的初始主动（建角色写）与决斗第零页第一槽（播种写）——主键是 `(character_id, mode, page_index, slot)`，两行合法共存。第一轮说"这是唯一能让丢条件显形的排布"，属实。

**两条新用例不会因别的原因假绿。** R1..R7 已经从反面证明了它们对各自声称的那件事敏感。正面再看一遍也不空转：前者断言决斗侧两行、普通侧零行、`character_pvp.skills_initialized=1`，第二次播种换了 profile 内容（701/801）而断言读回仍是 700/800 且行数不变——若标记没写上，第二次插入会撞主键 `(1,'pvp',0,3)` 直接抛，两条路都红；后者三段依次断言 `ReadLevelsAsync` 只答 `[100]`、`SetLevelAsync` 返回 1 且决斗行的 `level` 仍是 7、`RemoveAsync` 返回 1 且决斗行还在普通行没了，三条语句各自都有一处只有它能打红的断言。

**`ACharacterCreatedHoldingSkillsKeepsOneRowEach` 的回读改对了。** `SkillsAsync` 现在带 `mode` 参数并写进 `WHERE`，用例同时断言 pve 三行、pvp 空。一处小限制记录在此：`SkillsAsync` 仍带 `AND page_index=0`，所以"pvp 侧为空"这半句只覆盖 pvp 第零页——不构成缺口，因为被守的那条插入的 `page_index` 是硬写的 `0`，写错 mode 必然落在 pvp 第零页。

### 接受的那条 B 级（B-1）——新文字与代码、与断言一致

裁定 `Docs/Refactor/TableOwnershipRuling.md:1099-1108` 新增一段，逐句核过：

- **"Three of the four now bind PvE rows as well"** —— 数得对。四条保证里 `NOT NULL`、级联外键、`UNIQUE(character_id, mode, page_index, skill_id)` 三条无条件绑住整张表因而也绑住 pve 行；只有页范围那条写成 `CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)`，对 pve 行恒真，确实不绑。
- **"a character's ordinary skills are written only at creation and all land on page zero"** —— 属实。全仓写 pve 行的语句只有 `SqliteCharacterSkillTable.InsertAsync`，`page_index` 是硬写的 `0`；唯一调用方是 `CharacterRosterService.cs:101`，只在 `CreateAsync` 的路径上；`SetLevelAsync` 只改 `level`、`RemoveAsync` 只删行，都造不出重复 `skill_id`。
- **"no job declares the same active skill twice in its archive entry"** 与"只看主动"—— 与 `CharacterDomainService.cs:166` 的 `grants.Where(value => value.IsActive)` 同一个筛选，与 `PvfInitialSkillDefinitionSourceTests.cs:92` 的 `.Where(grant => grant.IsActive)` 逐字对应；被动不写入，所以只查主动是对的。
- **指明钉在哪条断言上** —— `PvfInitialSkillDefinitionSourceTests.No_job_declares_the_same_active_skill_twice` 存在（`:82-98`），断言是 `actives.Length == actives.Distinct().Count()`，正是"无重复"。
- **"if a future job declared a duplicate, creating a character of that job would fail at the second row and nothing else would notice first"** —— 与该测试自己的注释、与失败机制（第二行撞 `UNIQUE`）一致。

两处措辞上的小账，记录不判：

1. **裁定 `:951` 那格仍只列两条。** 键与约束表里 `character_skills` 一行还是"The `NOT NULL` and the cascade now bind PvE rows too"，新写的完整说明在 148 行之后的逐列小节里，并以"for the reason given above"回指这一格。第一轮建议补在 `:951`，实施者补在了逐列小节——内容更完整、位置也说得过去，但只读键表的人仍看不到 `UNIQUE` 那一条。
2. **"checked for every declared job"** 略强于断言本身。那条断言循环 `job = 0..12`，等于服务器能建的全部职业（`CharacterDomainService.MaxJobId = 12`），但档案里 13 以上是否确实一条不declare并未证明（只有 `A_job_the_archive_does_not_declare_has_no_grants` 单点验了 200）。写成"每一个服务器能建的职业"会更准，且这正是约束真正需要的范围。

### 第一轮九条 B 级——无一升级

| 第一轮 | 本轮状态 |
|---|---|
| B-1 裁定漏记 `UNIQUE` 的收紧 | **已接受并改**，新文字与代码一致（见上），残留两处措辞小账 |
| B-2 三条 `mode='pve'` 无人守 | **已接受并改**，R4/R5/R6 各自单独变红 |
| B-3 "收紧无害"的唯一证据是 `[SkippableFact]` | 未变，仍 B。整改让裁定正面点名了这条断言，压在它身上的分量变重了一点，但可用性依赖环境这件事本身没变，本机 `Infrastructure` 100 通过 2 跳过、两条跳过仍是 quest/town 的缺档案用例，本轮证据成立 |
| B-4 级联外键运行时不生效 | 未变，既有状况，不可达 |
| B-5 旧库的失败形态没有名字 | 未变，与"擦库不迁移"一致 |
| B-6 `EverySkillColumnButTheBlobIsRequired` 判文本布局 | 未变；`SkillCarrierShapeTests.cs` 整改期间未被触碰（时间戳 04:55） |
| B-7 只断言异常类型 | 未变，由列集断言兜住 |
| B-8 `ArgumentNullException` 换型 | 未变，不可达，是改善 |
| B-9 `Skill` 读取器用全限定名 | 未变。同类新增一处：`CharacterSide.cs:2` 把 `using Dfo.Server.Abstractions.Persistence;` 插在 `...Content;` 之前，破了字母序；无分析器强制（构建零警告），纯风格 |

### 结论

`passed`。第一轮那条 A 级的两个失效面——写入侧两条语句的 mode 字面量、以及"播种过没有"这个判据——现在各自有一条只有它能打红的用例；顺带接受的 B-2 让另外三条 pve 语句也进了守卫。七次真改真跑，七处全红且每次只红被点名的那一条。整改只加测试与文档、不动产品代码，夹具真实，两条新用例正反两面都不空转。裁定新增的那段与代码、与它指名的断言逐句一致。九条 B 级无一升级，新增的两处措辞小账与一处 using 顺序均为记录级。

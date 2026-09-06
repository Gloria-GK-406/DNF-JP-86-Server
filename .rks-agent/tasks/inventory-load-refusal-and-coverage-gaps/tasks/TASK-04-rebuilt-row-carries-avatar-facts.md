# TASK-04 — 时装六值落到新建的世界行上

## Task Unit Contract

- Revision: 1
- Outcome and references: 覆盖 RQ-008/RQ-009 / AC-011..AC-013 / plan T-004、DP-004。**不改生产代码**。
- Owned boundary:
  - 判据：`Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`。
  - 两件事：(a) 新增一条走**重建路径**的判据；(b) 把 `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 改名为描述它实际证明的东西——**只改方法名与其文档注释，断言与字面期望值一字不动**。
  - 需要一个读 `item_uid` 的新私有 helper（现有 `ReadSlotsAsync` 不答它）。
  - **生产代码一字不动**（变异核实期间除外，须还原并核散列）。
- Required checks:
  1. 新判据绿；断言两件事：新槽世界行的 `item_uid` **不等于**原行的（证明是重建不是幸存），且六个时装列**逐个**相等。
  2. 全量判据退出码 0，跳过数仍为 2；该文件用例数 = HEAD + 1。
  3. CK-004（＝AC-012）：令 `SqliteItemDomainStoreSession.CarriedAvatarsAsync` 恒返回空字典，确认**新判据变红**（对照：HEAD 上同一变异只红 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`）；还原并 `sha256sum -c` 通过。
  4. CK-005（＝AC-013）：`git diff` 该文件，确认改名之外无断言或期望值改动。
- Dependencies: 无

## Result (round 1)

- **Changed:** `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`
  - 新增 `AnAvatarWhoseRowIsRebuiltIsHandedItsSixFactsAgain`。
  - 新增私有 helper `ReadWorldRowIdAsync(path, slot)`——答「这一格指名的是哪一行」，既有的 `ReadSlotsAsync` 只答「这一格放着什么」，而幸存与重建在后者上看起来一模一样。
  - `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 改名为 `Relocate_LeavesASparedAvatarsRowStandingWithItsFactsUntouched`，并改写其文档注释。
  - **生产代码零改动。**
- **Why:**
  - 触发重建的手段是把 `Durability` 从 0 改成 77（DP-004）。`InventoryItemCore.Encode` 把 `Durability` 写在偏移 10、把 `Value`（时装 UID）写在偏移 5..8，所以核字节变了而 `substr(item_core, 6, 4)` 没变。于是 `Survivors` 三趟全不认领：第一、三趟要 `InPlace`（落点换了槽），第二趟要 `SameContent`（字节已不同）。原行不进 `spared`，被 `EmptyAsync` 删除；落点走 `FillAsync` 新建一行，六个值只可能来自 `CarriedAvatarsAsync` 的检索。
  - `Assert.NotEqual(standing, rebuilt)` 不是附带断言而是前提：没有它，一条悄悄走上幸存路径的判据会满足其余每一行而什么都没证明——那正是被改名的那条判据的实况。
  - 旧文档注释里「the facts are read back under the avatar UID the core carries and written on again」对那条判据**是假的**：幸存行被 `CarriedAvatarsAsync` 按 `survivors.ContainsKey` 排除，根本不检索。CK-004 当场证伪了它。改名连同改注释，是把一条已在反面的记载改掉。
- **Checks:**
  - `SqliteItemDomainStoreTests` 20 条全绿（19 ＋ 1）。
  - 全量判据：退出码 **0**，**2252 总 / 2250 通过 / 2 跳过 / 0 失败**（2237 ＋ 4 ＋ 4 ＋ 4 ＋ 1）。落盘：`scratchpad/task04-full.txt`。
  - **CK-004（本单元的关键核实）**，散列 `c3684920e553af2b1a897e76683b7c4e2625c688d6f97cba6b4a21b52676d09f`：令 `CarriedAvatarsAsync` 恒答空表 → **失败 2 / 通过 18**：
    - `AnAvatarWhoseRowIsRebuiltIsHandedItsSixFactsAgain` **红**（新覆盖生效）
    - `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 红（与 AGENTS.md 既有记载一致）
    - `Relocate_LeavesASparedAvatarsRowStandingWithItsFactsUntouched` **仍绿**——这就是「那六个值被搬到新行上今天零覆盖」的当场证据，也是本单元存在的理由。同一变异在 HEAD 上只红 1 条，现在红 2 条。
    - 还原 `sha256sum -c` ＝ OK。
  - CK-005（＝AC-013）：`git diff` 该文件，**被删除的行只有 6 行文档注释文本 ＋ 1 行方法签名**，没有任何 `Assert`、字面期望值或播种数据被删改。

## Independent Quick Review (IQR-04-1)

- Reviewed execution round: Result (round 1)
- Contract revision: 1（无修正案）
- Owned diff identity:
  - `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`（改动，`sha256 72eb628881acbd9fd2c6aa36b56a561a34280f6fff72e37ed445fe899607e66e`），`git diff` 三段 hunk：改名处的注释＋签名、新判据、新 helper。
  - `git diff -- Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` **为空**；该文件 `sha256 c3684920e553af2b1a897e76683b7c4e2625c688d6f97cba6b4a21b52676d09f`，与执行记录里 CK-004 的还原散列**逐字相同**——变异确已按散列还原，不依赖自述。
  - `git status --short` 中另两处（`InventoryEntryAssembly.cs`、`AvatarCarrierRulesTests.cs`）与两个未跟踪判据文件属 T-001/T-002/T-003，不在本单元边界内。全工作树无 `.bak`/`.sha`/`.orig`/`.rej`。
  - 自行重跑 `--filter FullyQualifiedName~SqliteItemDomainStoreTests`：**20 通过 / 0 失败 / 0 跳过**；HEAD 上该文件 `[Fact]` 数 ＝ 19，现 ＝ 20，符合「HEAD ＋ 1」。

### Verdict: `changes_required`

（新判据的机制、断言强度与改名的一字不动全部通过复核；唯一发现是一段本该只引不抄的注释。）

### 复核到的事实

- **新判据确实走重建路径，不是幸存路径——逐趟核算，非采信自述。**
  暂存态为 `Main{9→null, 10→Worn(core with Durability=77)}`。`DisplacedAsync`（`SqliteItemDomainStore.cs:287-319`）
  只取到槽 9 那一行；`Placings()`（`:272-286`）只有槽 10 那一条（`Clear` 存的是 null，被 `Where(slot => slot.Value is not null)` 滤掉）。
  三趟（`:344-346`）：第一趟 `InPlace && SameContent`——`InPlace`（`:366-367`）要 `row.SlotIndex == placing.SlotIndex`，9≠10，不成立；
  第二趟 `SameContent`（`:369-370`）比 82 字节全等，`Encode` 把 `Durability` 写在偏移 10（`InventoryItemCore.cs:27`），
  77≠0 故不等；第三趟又要 `InPlace`。**三趟全不认领** → `survivors` 空 → `spared` 空（`:229`）。
  于是 `EmptyAsync`（`:514-545`）的 `keep` 为空串，`DELETE FROM items WHERE item_uid IN (SELECT core_uid …槽9)` **无条件删掉原行**。
- **`Durability` 确在时装 UID 的四字节之外——独立核对 `InventoryItemCore.cs`。**
  `Value`（时装 UID）写在 0 基偏移 5..8（`:25`）＝ SQLite 从 1 数的第 6..9 字节；`Durability` 写在 0 基 10..11（`:27`）＝ SQLite 第 11..12 字节。
  两者**不重叠**，所以 `CarriedAvatarsAsync`（`:424-464`）的 `substr(item_core, 6, 4) = @uid` 仍然命中原行。DP-004 的核算成立。
- **六个值只可能来自检索。** `FillAsync`（`:596-605`）优先 `item.AvatarDetail`；本判据写下的是 `Worn(10, …)` ＝
  `new StoredItem(slot, core) { WorldClass = … }`（测试 `:490-491`），`AvatarDetail` 是 `init` 属性默认 `null`
  （`IItemDomainStore.cs:128`），所以只剩 `carried.TryGetValue(item.Core.Value, …)` 这一条来路。
  而 `CarriedAvatarsAsync` 的过滤是 `!survivors.ContainsKey(staged.Place)`（`:433`），本例 survivors 空 → 该槽被检索。
  另有一条独立佐证：`FillAsync` 的 INSERT 带 `WHERE @expire IS NULL OR NOT EXISTS(…同 UID 的时装行…)`（`:607-611`），
  判据能绿即说明**当时既取到了六值（@expire 非空）又已无同 UID 的旧行站着**——即原行确被删除。
- **`Assert.NotEqual(standing, rebuilt)` 有鉴别力，且不会空过。**
  `items.item_uid` 是 `INTEGER PRIMARY KEY AUTOINCREMENT`（`CharacterDatabaseSchema.cs:199`），删掉的 rowid **不会被重用**，
  所以重建必得新号、幸存（`LandAsync` 只 `UPDATE items SET item_core`，`:493-496`）必得旧号——这条断言正好把两条路分开。
  `ReadWorldRowIdAsync` 的 `ExecuteScalarAsync() as long?` 在无行/DBNull 时确会静默给 null，但判据先做了
  `Assert.NotNull(standing)` 与 `Assert.NotNull(rebuilt)`（测试 `:236-237`），**空对空的假通过被堵死**；
  `item_slots.core_uid` 声明为 `INTEGER NOT NULL`（`CharacterDatabaseSchema.cs:200`），Microsoft.Data.Sqlite 对它交出 `long`，转换本身也成立。
  `Assert.NotEqual(standing, rebuilt)` 两参数同为 `long?`，走 `Assert.NotEqual<T>` ＋ `EqualityComparer<long?>`，**比的是值不是引用**。
- **改名一字不动，逐字核实。** `git show HEAD:…` 的第 188-204 行与工作树第 192-208 行 `diff` **无输出**（仅签名那一行在外）。
  另做全文件比对：`Assert.` / `NewStoreAsync(` / `session.Write` / `session.Clear` / `SeedAvatarDetailAsync` 全部行取出后对 HEAD 求差，
  **只有新判据自身的 14 行是新增，既有行零删改**。AC-013 成立。
- **改名后的新注释逐句为真（旧注释确为假，未被新的假话顶替）。**
  「puts down the same core the read took up」——`Worn(10, moving!.Core)`，成立。
  「the second of the three passes claims the displaced row by content」——第一趟要 `InPlace`（9≠10）不成立；
  第二趟 `SameContent`：播种字节（测试 `:559-566` 只写偏移 0、1-4、5-8、21-24＝-1、66＝255）与 `Encode` 对同一 core 的输出**逐字节相同**
  （`Core(...)` 工厂正是 `Marker16 = -1`、`RandomOptionChangedIndex = byte.MaxValue`，其余为 0），故第二趟命中。成立。
  「spared from the delete and landed on」——`EmptyAsync` 的 `AND item_uid NOT IN (@keep0)`（`:521-524`）＋ `LandAsync`。成立。
  「its six columns are never touched」——`LandAsync` 的 UPDATE 只写 `item_core, updated_at`（`:493-496`）。成立。
  「`CarriedAvatarsAsync` is not even asked about it」——`:433` 的 `!survivors.ContainsKey` 把该槽排除。成立。
- **未越界、无残留。** 生产代码零差异（上）；该文件除本单元三处外无改动；未重跑全量判据（依控制方指示）。

### findings

**R-1（须改，仅注释）：新判据第三段 `<para>` 把一条已有归处的理由又抄了一遍。**

- 缺陷：新判据注释写「All six are asserted one by one **for the reason given below**: five bound and one left null is
  the shape a half-finished binding leaves, and a looser comparison would let it through.」
  ——冒号前已经是**引用**（指向紧邻其下的 `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`），
  冒号后又把该处（同文件 `:266-269`：「"five bound and one left null" is exactly the shape a half-finished binding
  leaves and a looser comparison would let through」）的原话**近乎逐字复制**了一遍。既引又抄，抄的那半是多余的。
- 实际影响：AGENTS.md「记什么，不记什么／每种事实只有一个归处」明写「抄到第二处就是给自己埋一个漂移点。**要引就引，不要抄**」。
  spec 第 99 行本身也是这么说的（「同文件 `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` **已经写下这条理由**」）。
  同一 spec 下 IQR-01/IQR-02 对 T-001 的逐字复制已按同一标准判过 `changes_required`，此处按同一尺度。
  附带一点：这条理由搬到新位置后还略失准——本文件的 `ReadAvatarDetailAsync`（`:604-618`）遇到「一列留 null」会在
  `GetInt32` 上抛而不是「被宽松比较放过去」，所以原话在新语境里读起来比在原位更勉强，这也正是不该复制的理由。
- 证据：新判据注释三段 `<para>` 处 vs 同文件 `:266-269`。
- 最小修正：**只删冒号之后的复述**，把该 `<para>` 收成一句指路（例如 `…one by one for the reason given in
  <see cref="AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates"/>.`），不动任何代码行。

### 附：注释体量（AGENTS.md 尺度）

本 diff 新增非空行 74，其中注释 36 行、代码 38 行，比 **0.95**；该文件 HEAD 基线 119/467 ＝ **0.25**，`Server/src` 基线 **0.46**。
高于两条基线，但**主体够格**：三趟认领的走位、偏移 5 与偏移 10 的关系、「NotEqual 是前提不是附带」这三件事都属
AGENTS.md 认的第 2 类（扫代码代价很大）——判据成不成立正压在这条跨程序集的推演上。
真正不够格的只有 R-1 那一段；删掉后比值落到约 0.87，仍偏高但不再有「替已有归处记第二笔账」的成分。

## Result (round 2 — remediation of IQR-04-1)

- **Changed:** 仅 `SqliteItemDomainStoreTests.cs` 新判据的第三段 `<para>`：删去对「五个绑上、一个留 null」那条理由的复述，改为 `<see cref="AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates"/>` 指路。代码、断言、播种值一行未动。
- **Why:** 评审说得对——冒号前已经是引用、冒号后又抄了一遍同文件的原话，是「既引又抄」。而且搬过来之后那句还略失准：本文件的 `ReadAvatarDetailAsync` 遇到某列为 null 会在 `GetInt32` 上直接抛，并不是「被宽松比较放过去」。抄一句话连带把它抄歪，正是 AGENTS.md 记的那个漂移点。
- **Checks:** `SqliteItemDomainStoreTests` 20 条全绿，零编译警告（`<see cref>` 指向同类 public 方法，跨程序集解析无虞）。注释/代码**未变**，仍是 36/38 ＝ 0.95——新旧两段正文都占 2 行注释。（此处原写「降到约 0.87」，那是 IQR-04-1 的**估计值**被我当成实测值抄了进来；按 AGENTS.md「没有出处的数不写」，改为实测。）

## Independent Quick Review (IQR-04-2)

- Reviewed execution round: Result (round 2 — remediation of IQR-04-1)
- Contract revision: 1（无修正案）
- Owned diff identity:
  - `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs`（改动，`sha256 76f27b467fb0e37edb11fefa964cb55b75c4bc52ef0530002d9cfc04f6ec1ac5`；round 1 为 `72eb6288…`）。
  - `git diff -- Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` **为空**；`sha256 c3684920e553af2b1a897e76683b7c4e2625c688d6f97cba6b4a21b52676d09f`，与 CK-004 还原散列逐字相同。**生产代码零差异。**
  - 复核范围限于 IQR-04-1 的 R-1 及其直接回归，不重做已通过复核的部分。

### Verdict: `passed`

### 复核到的事实

- **R-1 已解决：抄的那半没了，只剩引用。**
  第三段 `<para>` 现为（`SqliteItemDomainStoreTests.cs:224-227`）：
  「All six are asserted one by one for the reason given on `<see cref="AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates"/>`.」
  ——冒号连同其后「five bound and one left null … a looser comparison would let it through」的复述整段删除。
  被引处**确实**载着该理由：`:268-271`「All six are asserted one by one rather than compared as a whole,
  because "five bound and one left null" is exactly the shape a half-finished binding leaves and a looser
  comparison would let through.」一个事实一个归处，成立。
- **`<see cref>` 可解析，不会触发 CS1574。**
  目标方法 `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` 声明于**同一类同一文件**（`:274`），
  全仓 `grep` 只有这一处声明与这一处引用，**无重载**，故无参数化 cref 的歧义。
  `Server/Directory.Build.props:6` 的 `TreatWarningsAsErrors=true` 生效，而全仓未设 `GenerateDocumentationFile`——
  两条路都不会红。实测重跑 `--filter FullyQualifiedName~SqliteItemDomainStoreTests`：**编译零警告零错误，20 通过 / 0 失败 / 0 跳过**。
- **无回归：代码、断言、字面期望值、播种值一行未动。**
  对 HEAD 的完整 `git diff` 仍是**三段 hunk**（改名处注释＋签名、新判据、新 helper），
  `78 insertions / 7 deletions`——删除行仍只有 **6 行文档注释文本 ＋ 1 行方法签名**，
  既有判据的 `Assert.`、`NewStoreAsync(`、`SeedAvatarDetailAsync(`、字面值零删改（CK-005 仍成立）。
  新判据侧独立核对 IQR-04-1 记下的计数：新增的 `Assert.` / `NewStoreAsync(` / `session.Write` / `session.Clear` /
  `SeedAvatarDetailAsync` 行 **仍恰为 14 行**（`Assert.` 10 ＋ 其余各 1），与 round 1 复核时逐条相同；
  播种值 `41,42,(byte)43×30,44,45,46` 与 `Durability = 77` 未变（`:232-233`、`:242`）。
  helper `ReadWorldRowIdAsync`（`:624-633`）SQL 与 round 1 逐字相同。
- **未引入新的假话，其余注释仍然为真——对生产代码独立重核（生产代码未变，故结论沿用亦已重验）。**
  「a field outside the four bytes at offset five that carry the avatar UID」：`InventoryItemCore.cs:25` 把 `Value`
  写在偏移 5（4 字节），`:27` 把 `Durability` 写在偏移 10（2 字节），**不重叠**。成立。
  「the first and third want the same position … the second wants identical bytes」：三趟依次为
  `InPlace && SameContent`、`SameContent`、`InPlace && SameThing`（`SqliteItemDomainStore.cs:344-346`），
  `InPlace` 比 `ContainerId`＋`SlotIndex`（`:366-367`），`SameContent` 比 82 字节全等（`:369-370`）。成立。
  「NotEqual 是前提不是附带」那段（`:219-223`）与改名判据的注释（`:180-188`）**逐字未动**；
  后者「`CarriedAvatarsAsync` is not even asked about it」仍由 `:433` 的 `!survivors.ContainsKey(staged.Place)` 撑着。成立。
- **`AGENTS.md` 的工作树改动不属本单元。** 其 mtime `21:27:44` 早于本轮判据文件改动 `21:33:34`，
  内容（装载侧拒绝、三条取值校验、判据改名三处记载）落在 **T-005／`TASK-05-agents-md-records.md`** 的边界内
  （`plan.md:43`、`TaskContract.md:64`）。本轮未纳入，也未越界改动它。
- 未重跑全量判据（依控制方指示）。

### findings

**N-1（不阻塞，仅记录数字失准）：round 2 的 Checks 写「注释/代码由 0.95 降到约 0.87」，实测未变。**

- 缺陷：现测 owned diff 新增非空行 74 ＝ **注释 36 ＋ 代码 38 ＝ 0.947**，与 IQR-04-1 记的 round 1 数字**完全相同**。
  原因是两版 `<para>` 正文都占 **2 行注释**——删掉的是复述的内容，不是行数。0.87 是 IQR-04-1 附记里的**预估**，
  被 round 2 当作实测结果照抄了下来。
- 实际影响：不触及交付物，也不在契约 Required checks 1-4 之内；但它是一条写进耐久记录、可当场证伪的数字，
  正是本仓「一条记载漂到自己的反面」的那种苗头。故不阻塞验收，但应就地改正而非另起修正案。
- 证据：`git diff -U0` 计数 vs 本文件 IQR-04-1「附：注释体量」段。
- 最小修正：把 round 2 Checks 里那半句改成「注释/代码仍为 0.95（删的是复述而非行数），但已不含替已有归处记第二笔账的成分」。

# TASK-01 — 装载侧拒绝「同一件东西被两个位置指名」

## Task Unit Contract

- Revision: 1
- Outcome and references: 覆盖 RQ-001..RQ-004 / AC-001..AC-005 / plan T-001。先写会红的判据，再在 `InventoryEntryAssembly` 的两条读路径上加拒绝。本单元是本任务**唯一**改动生产行为的地方。
- Owned boundary:
  - 生产：`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`（只在 `ReadCharacterItemsAsync`、`ReadCargoItemsAsync` 及新增私有辅助内改动）。
  - 判据：`Server/tests/Dfo.Server.Persistence.Tests/Inventory/` 下新增用例文件；可读用 `Server/tests/Dfo.Server.Persistence.Tests/Support/InventorySide.cs`。
  - 不得改动任何其他生产文件、任何既有判据的断言或期望值、schema、`Abstractions` 的 public 契约。
- Required checks:
  1. 加拒绝**之前**：AC-001/002/003 的三条新判据红，AC-004 的正常数据判据绿。记录红的用例名与断言。
  2. 加拒绝**之后**：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` 退出码 0，跳过数仍为 2。
  3. CK-001（＝AC-005 变异核实）：按 plan 的规程备份 `InventoryEntryAssembly.cs`、删去新增拒绝、确认三条判据变红、还原并 `sha256sum -c` 通过。用完立刻还原。
- Dependencies: 无

## Result (round 1)

- **Changed:**
  - `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`：新增私有静态 `RequireOneNameEach(IEnumerable<long>, string)`，并在 `ReadCharacterItemsAsync`（标签 `"character inventory"`）与 `ReadCargoItemsAsync`（标签 `"account cargo"`）取到行之后、逐行处理之前各调用一次。消息：`The {source} names item {itemUid} in more than one position.`
  - `Server/tests/Dfo.Server.Persistence.Tests/Inventory/OneThingInTwoPositionsIsRefusedOnLoadTests.cs`（新建，4 条判据）。
- **Why:**
  - 按 `row.ItemUid`（＝`core_uid`）去重，不按 `ItemIdentity`（DP-001）。保存侧 `SaveAsync` 第 224–229 行比的是**落位后的世界行身份**，堆叠物也在内；`IdentityOfAsync` 对堆叠物答 `null`，用它去重会静默漏掉堆叠物。`APileNamedInTwoPositionsIsRefusedAlthoughItCarriesNoIdentity` 单独钉这一条。
  - 落在两个读方法内而非 `LoadCharacterAsync` 末尾（DP-002）：`InventoryStorageItem` 不携带 `ItemUid`，放末尾要改 `Abstractions` 的 public 契约，那在 Out 之外。
  - 两条拒绝判据把两个位置放在**同一个容器**内（`list_type` 相同、`slot_index` 不同），因而不会撞上 `RequireUniqueSlots`，隔离出的正是 `core_uid` 这一维。
- **Checks:**
  - 加拒绝**前**（TDD 红）：`dotnet test …Persistence.Tests… --filter FullyQualifiedName~OneThingInTwoPositionsIsRefusedOnLoadTests` ＝ 失败 3 / 通过 1。三条失败均为 `Assert.Throws() Failure: No exception was thrown`——即**今天这个坏形状会被静默装载**，缺陷已实证。
  - 加拒绝**后**：同一过滤器 4/4 通过。
  - 全量判据：退出码 **0**，**2243 总 / 2241 通过 / 2 跳过 / 0 失败**（基线 2237 ＋ 本单元 4）。落盘：`scratchpad/task01-full.txt`。
  - CK-001 变异核实：删去两处 `RequireOneNameEach` 调用 → 三条拒绝判据变红、`AnInventoryThatNamesEachThingOnceIsLoadedUnchanged` 仍绿；`cp` 还原后 `sha256sum -c` 输出 `OK`，调用点计数回到 2。散列 `f577883b9077c0c791a56034d3185865d19d32ac094baca22f69f8be5577d784`。

## Independent Quick Review — IQR-01

- **Reviewed execution round:** Result (round 1)
- **Contract:** Revision 1，无修正案
- **Owned diff identity:**
  - `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`（改动，`sha256 f577883b9077c0c791a56034d3185865d19d32ac094baca22f69f8be5577d784`，与执行记录的还原散列一致 → CK-001 的还原已核实，工作树无 `.bak`/`.sha`/`.orig` 残留）
  - `Server/tests/Dfo.Server.Persistence.Tests/Inventory/OneThingInTwoPositionsIsRefusedOnLoadTests.cs`（新增，`sha256 2ec4a86281389c8c069cb4cb756989b5cecd96afc8803e2b846f30091dc525c8`）
  - `git status --short` 仅此两项（外加未跟踪的 `.rks-agent/`），无越界改动。
- **Verdict: `changes_required`**（仅注释文本；生产逻辑与判据本身全部通过复核）

### 通过复核的部分

- **按世界行判定，不按 `ItemIdentity`（RQ-003 / AC-003）——成立且可证伪。**
  `IdentityOfAsync`（`InventoryEntryAssembly.cs:278-286`）对 `ItemWorldClass.Interchangeable` 答 `null`；
  `ItemPlacementRuleService.ClassifyAsync`（`ItemPlacementRuleService.cs:50-58`）对未声明的模板答 `Interchangeable`；
  `ItemPlacement.WorldClass` 缺省即 `Interchangeable = 0`（`ItemPlacementContent.cs:35,68`）。
  `InventorySide` 的缺省 `MainBandPlacements` 返回 `new ItemPlacement(2, 100, 65, 120)`（`Support/InventorySide.cs:118-120`），
  未设 `WorldClass`，故 `APileNamedInTwoPositionsIsRefusedAlthoughItCarriesNoIdentity` 与
  `AnInventoryThatNamesEachThingOnceIsLoadedUnchanged` 的角色物**两条判据里的身份都是 `null`**。
  由此这两条判据把「按身份去重」的两种可能实现都堵死了：
  跳过 `null` 的实现 → 堆叠物那条不抛，变红；不跳过 `null`（照 `SaveAsync` 的 `claimed.Count != settled.Count` 抄）的实现 → 两个 `null` 塌成一个，
  正常数据那条被误拒，变红。**AC-003 确实钉住了 DP-001。**
- **拒绝判据不是靠数据库约束通过的（可证伪性核心）。** 逐条比对 `CharacterDatabaseSchema.cs:200` 的
  `item_slots` 声明与 `:245-246` 的索引：
  - 三条拒绝判据两行只差 `slot_index`（3/4、5/6、7/8），`UNIQUE(owner_scope, owner_id, list_type, slot_index)` 不成立；
  - `count > 0` 满足；`world_slot_holds_exactly_one`（world → count=1）满足（角色那条 `count:1`，另两条 `kind:'template'` 使其真空成立）；
    `world_slot_names_its_own_row`（world → `core_uid = id`）满足（角色那条 `id: thing, coreUid: thing`）；
  - 货柜那条另受 `idx_item_slots_account_slot`（`UNIQUE(owner_id, slot_index) WHERE owner_scope='account'`）管，(71,5)/(71,6) 不冲突；
  - `core_uid` 上只有**非唯一**索引 `idx_item_slots_core`。
  应用层 `RequireUniqueSlots` 按 `(ContainerType, SlotIndex)` 去重（`InventoryEntryAssembly.cs:754-765`），同样不成立。
  加拒绝前三条全部报 `No exception was thrown`（执行记录），与此分析一致：**这三条播种形状今天确实是「静默装载」，不是撞约束**。
- **正常数据逐值不变（RQ-004 / AC-004）。** `ReadByCharacterAsync` / `ReadByAccountAsync` 的返回类型是
  `ValueTask<IReadOnlyList<CharacterItemRow>>` / `ValueTask<IReadOnlyList<AccountCargoItemRow>>`
  （`Abstractions/Persistence/InventoryTables.cs:298,374`）。原先 `foreach (var row in await …)` 与现在
  「先 `await` 落到 `rows`，再 `Select` 一趟、`foreach` 一趟」等价：`await` 仍只一次，
  `IReadOnlyList` 是已物化集合，二次枚举不重查、不改序，`items` 的构建顺序不变。
  `RequireOneNameEach` 只在 `HashSet<long>.Add` 返回 `false` 时抛，无重复 `core_uid` 时是纯遍历。**行为中性，确认。**
  「同时坏两处时先报哪一条」的次序变化已由 spec 的 Assumptions 末条预先承认，不是缺陷。
- **R-1（误伤「两个容器各一条位置行指向同一世界行」）不成立**：该形状按设计就是非法的，且两条读路径各自独立判定，跨路径不互相牵连。
- **CS9113 / 分层 / 禁用命令**：新增辅助是 `private static`，未新增主构造器参数；新代码只用 `IEnumerable<long>`、`HashSet<long>`、
  `System.Linq.Select`、`InvalidDataException`，Application 未反向引用 Persistence/Protocol；执行记录用 `cp` + `sha256sum -c`，
  散列复核一致，无 git 还原痕迹。
- **复跑单条过滤器**：`--filter FullyQualifiedName~OneThingInTwoPositionsIsRefusedOnLoadTests` → **4 通过 / 0 失败 / 0 跳过**，
  编译零警告（`TreatWarningsAsErrors` 下即零错误）。

### findings

**F-1（须改）：新注释中有一句与 schema 和 AGENTS.md 直接矛盾的断言。**

- 缺陷：`InventoryEntryAssembly.cs:733` 写「`core_uid` carries no constraint at all」，
  同一句几乎逐字重复在 `OneThingInTwoPositionsIsRefusedOnLoadTests.cs:20-21`。
- 实际影响：这句话是**假的**。`CharacterDatabaseSchema.cs:200` 上 `core_uid` 至少带两条约束——`INTEGER NOT NULL`，
  以及具名 `CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id)`。
  AGENTS.md:536 自己也记着「三条具名 CHECK 分别是 `count > 0`、世界槽 `count = 1`、**世界槽 `core_uid = id`**」。
  本仓明记「一条记载漂到自己的反面还在原地躺着」是反复付过代价的失败模式（AGENTS.md「记什么，不记什么」开头），
  而这一条是**新写下的**、已经站在反面的记载；更麻烦的是，正是这条 CHECK 决定了角色那条判据必须写成
  `id == coreUid`（plan「Choices During Implementation」第 4 条也是这么推的），
  照注释字面理解会把这条播种约束当成没有。
- 最小订正：把两处「carries no constraint at all」改成只说它缺的那一样——`core_uid` 上**没有唯一性**
  （`idx_item_slots_core` 是非唯一索引，`CharacterDatabaseSchema.cs:245`）。不动代码。

**F-2（须改）：同一条理由被抄到三处，是本仓明令的漂移点。**

- 缺陷：「位置指名行 → 两个位置能指同一行 → 玩家看到两份、能把两份都搬走」这条理由，
  今天同时写在 `InventoryEntryAssembly.cs:218-222`（`SaveAsync` 上方，既有）、
  `InventoryEntryAssembly.cs:727-730`（本次新增）、
  `OneThingInTwoPositionsIsRefusedOnLoadTests.cs:14-17`（本次新增）；
  「堆叠物答 `null`，按身份去重会静默放行」这条也同时写在 `:736-738` 与判据文件 `:26-29`，近乎逐字。
- 实际影响：AGENTS.md「每种事实只有一个归处 —— 抄到第二处就是给自己埋一个漂移点。**要引就引，不要抄。**」
  F-1 正是这个机制第一次发作：一句话错了，两个地方一起错。
  另：判据文件 `:15-16`「nothing refused it on the way back in」是**改动前**的状态，落地后已不成立，
  属 AGENTS.md「不写的 → 过程」那一类。
- 最小订正：生产侧留完整叙述（它是被引的那一处），判据侧的 `<remarks>` 收成一句「见
  `InventoryEntryAssembly.RequireOneNameEach`」＋只保留判据自己独有的信息（为什么堆叠物那条要单独写、
  为什么两个位置放在同一容器内）；并去掉那句改动前状态的陈述。不动任何 `Assert` 与播种数据。

**F-3（记录，不须改）：注释体量本身在可接受范围。**
生产侧新增 20 行代码 / 16 行注释＝**0.80**，是 `Server/src` 基线 0.46 的 1.7 倍，
远未到 AGENTS.md 点名的 6.20「本末倒置」；判据文件 158 行代码 / 48 行注释＝**0.30**，低于基线。
口径：`git diff -U0` 后按非空行与 `///`/`//`/`*`/`/*` 起首行计。
内容上三段各自属「代码表达不了」那一类（与保存侧的对称、数据库为什么说不出、为什么数世界行而非身份），够格。
**F-1/F-2 是这几段的准确性与归处问题，不是体量问题。**

**F-4（记录，属 T-005 边界）：** AGENTS.md:583-585「**装载侧没有对称的拒绝**——入场读只校验 (容器, 槽) 不重复，
不校验 `core_uid` 不重复」自本单元落地起已过期。RQ-010 / T-005 已认领，**本单元不该动它**，此处仅登记以免遗漏。

### 结论

判定逻辑、判据的可证伪性、正常数据的逐值不变性、约束/分层/还原纪律全部通过独立复核；
`changes_required` 只由 F-1（注释与 schema 相矛盾）与 F-2（同一事实抄三处）触发，
两者的最小订正都只动注释文本，不涉及任何生产逻辑、判据断言或播种数据，也不需要重跑全量判据以外的证据。

## Result (round 2 — remediation of IQR-01)

- **Changed:** 仅注释文本，生产逻辑、判据断言与播种数据一字未动。
  - F-1（写下了一句已在反面的记载）：`InventoryEntryAssembly.cs` 与 `OneThingInTwoPositionsIsRefusedOnLoadTests.cs` 里的「`core_uid` carries no constraint at all」改为「carries no uniqueness of its own」。评审是对的——`core_uid` 上有 `NOT NULL` 与具名 CHECK `kind <> 'world' OR core_uid = id`，而且正是后者决定了角色那条判据必须 `id == coreUid`。
  - F-2（同一条理由抄三处）：生产注释改为**引用** `SaveAsync` 处已有的那段理由而不再复述；判据文件删去与之重复的一段。生产侧注释由 16 行降到 11 行。
  - 一并删去判据文件里「nothing refused it on the way back in」——那句描述的是改动前的状态，落地后即不成立，属 AGENTS.md「不写过程」。
- **Checks:** `grep -rn 'no constraint at all\|carries no constraint' Server/src Server/tests` 无命中。全量判据仍绿（见 TASK-03 轮次的 2251/2249/2/0，退出码 0）。
- **Remaining:** IQR-01 的 F-4（AGENTS.md:583-585 已过期）属 TASK-05 边界，已并入该单元。

## Independent Quick Review — IQR-02

- **Reviewed execution round:** Result (round 2 — remediation of IQR-01)
- **Contract:** Revision 1，无修正案
- **Owned diff identity:**
  - `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`（改动，`sha256 f12af46b698d88fd73d904dfbe880634e56228b38997c579f679e99ed5e40f10`）
  - `Server/tests/Dfo.Server.Persistence.Tests/Inventory/OneThingInTwoPositionsIsRefusedOnLoadTests.cs`（新增，`sha256 d167291a958bdff009deb76067bd4ec6a1ea149d6c0ff579b4d2d1708c6acae8`）
  - `git status --short` 里属于本单元的仍只此两项；另有 `AvatarCarrierRulesTests.cs`（改动）与 `EntryWriterAvatarShapeTests.cs`（新增）属其他单元，不在本单元边界内，未纳入本轮。
- **Verdict: `changes_required`**（F-1 已解决且经逐字核对；F-2 只解决了一半，残留一处逐字复制）

### F-1 已解决——两处新措辞逐字核对后都成立

schema 原文（`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:200`）：
`core_uid INTEGER NOT NULL`、`UNIQUE(owner_scope, owner_id, list_type, slot_index)`、
`CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id)`；
索引 `:245` `CREATE INDEX IF NOT EXISTS idx_item_slots_core ON item_slots(core_uid);`（**非**唯一），
`:246` `CREATE UNIQUE INDEX … idx_item_slots_account_slot ON item_slots(owner_id, slot_index) WHERE owner_scope='account'`。

- **生产** `InventoryEntryAssembly.cs:729-730`：「No constraint says it: the uniqueness on `item_slots` is
  over positions, and `core_uid` carries none of its own.」
  - 「No constraint says it」——「it」回指摘要那条不变量。两行 `kind='world'`、`id = core_uid = X`
    可以同时存在而不违反任何一条具名 CHECK；两条唯一性（`:200` 的 `UNIQUE(...slot_index)` 与 `:246` 的部分唯一索引）
    都只覆盖位置。**成立。**
  - 「carries none of its own」——「none」回指同句前一分句的 uniqueness，即「`core_uid` 自己没有唯一性」，
    与 `:245` 的非唯一索引一致。**成立**，且不再否掉 `NOT NULL` 与 `world_slot_names_its_own_row`。
- **判据** `OneThingInTwoPositionsIsRefusedOnLoadTests.cs:16-17`：「`core_uid` carries no uniqueness of its own」
  ——同上且措辞无歧义；同段引的 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 与 schema `:200` 原文逐字一致。
- `grep -rn 'no constraint at all\|carries no constraint' Server/src Server/tests` **零命中**，复核 Checks 属实。

### 引用目标真实存在，且那里真的写着理由

`:727` 的 `<see cref="SaveAsync"/>` 在本类内只有一个声明（`:113`），cref 不歧义（`TreatWarningsAsErrors` 下编译通过亦印证）。
被引的理由在 `InventoryEntryAssembly.cs:218-222`：「…now that a position names a row rather than holding it,
two of them would name the same row and the player would appear to have two of something there is one of --
and could then move both away.」**理由完整，读者能到达。**

### 新写下的话逐句核实，没有一句是假的

- `:731` 「World rows are counted rather than identities, which is what the save side counts too.」
  `SaveAsync` 比的是 `settled.Select(item => item.Identity)`（`:224`），而 `SettledItem.Identity`（`:264`）
  ＝写入后拿回的 `written`（`:184-198`）＝ `items.item_uid` 这一世界行，**不是** `ItemIdentity`。成立。
- `:731-733` 堆叠物那句：`IdentityOfAsync`（`:278-286`）对 `Interchangeable` 答 `null`。成立。
- 注释谈 `core_uid`、代码数的也确实是 `core_uid`：`ReadByCharacterAsync`（`InventoryItemTables.cs:63-87`）
  `JOIN items held ON held.item_uid = place.core_uid`，`CharacterItemRow.ItemUid` 取的正是 `held.item_uid`。
- 判据 `:20` 的 `<see cref="Dfo.Server.Data.Inventory.ItemIdentity"/>` 解析到 `InventoryModels.cs:43`。

### 改动前状态的陈述已消失

`grep -rn 'on the way back in\|nothing refused it' Server/src Server/tests` 在本单元两个文件上**零命中**
（全仓唯一命中是 `Server/src/Dfo.Server.Protocol/Game/UnansweredReports/UnansweredReportSlices.cs:124` 的
「back into town」，语义无关且既有）。

### 无回归：这一轮确实只动了注释

- **生产侧是完整证明，不依赖执行记录的自述**：`git diff -- …/InventoryEntryAssembly.cs` 对 HEAD 只有三段 hunk——
  两条读路径把 `foreach (var row in await …)` 拆成 `rows` 再 `RequireOneNameEach(rows.Select(row => row.ItemUid), …)`，
  以及新增的私有辅助本体。这些**非注释行与 IQR-01 记下的第 1 轮改动逐项吻合**（签名 `IEnumerable<long>, string`、
  两处标签 `"character inventory"` / `"account cargo"`、调用落在取行之后逐行处理之前、消息
  `The {source} names item {itemUid} in more than one position.`），故第 2 轮未触及任何生产逻辑。
- **判据侧为间接核实**：文件未跟踪，无基线可 diff。但四条用例的播种值与断言与 IQR-01 独立记下的描述逐项吻合
  （三条拒绝用例两行只差 `slot_index` 3/4、5/6、7/8；角色那条 `kind:'world', id=coreUid=thing, count:1`；
  另两条 `kind:'template'`；货柜 `listType 12`、`(71,5)/(71,6)`；正常数据用例断言
  `[(3,2001,11),(4,2002,22)]` 与 `[(5,2600,33)]`），无任何 `Assert` 或播种数据被改动的迹象。
- 工作树无 `.bak`/`.orig`/`.sha` 残留。未重跑全量判据（依控制方指示；已有退出码 0 / 2249 通过 / 2 跳过 / 0 失败）。

### findings

**R-1（须改）：F-2 只解决了一半——被点名「近乎逐字」的那一对仍然逐字躺在两处。**

- 缺陷：IQR-01 的 F-2 点了**两条**被抄的事实。「位置指名行 → 玩家看到两份、能把两份都搬走」这条已经归位
  （今天只在 `InventoryEntryAssembly.cs:218-222`，生产新注释改为引用它），**这一半确实做到了**。
  但另一条——「堆叠物答 `null`，按身份去重会静默放行」——今天仍同时写在
  `InventoryEntryAssembly.cs:732-733`「A pile is answered no identity on purpose, so counting identities would
  pass a doubly-named pile through in silence.」与
  `OneThingInTwoPositionsIsRefusedOnLoadTests.cs:20-21`「A pile is answered no `ItemIdentity` on purpose, so a
  check written over identities would pass a doubly-named pile through in silence.」——**同一句话，两处**。
  且判据侧 `<remarks>` 通篇没有 F-2 最小订正要求的那条指向 `InventoryEntryAssembly.RequireOneNameEach` 的引用，
  「要引就引」只在生产侧落实了。
- 实际影响：AGENTS.md:896-898「每种事实只有一个归处 —— 抄到第二处就是给自己埋一个漂移点。**要引就引，不要抄。**」
  F-1 正是这个机制在本文件对上发作过一次（一句错话同时错在两处）。今天错话没了，但**同一个漂移点还在**：
  堆叠物为什么答 `null`、按身份去重为什么会漏，一旦变了要两处一起改。
  执行记录写的「判据文件删去与之重复的一段」只对上了被删的那一段，未覆盖这一对。
- 最小订正：判据 `:19-21` 保留它自己独有的那半句（「The pile case is written out separately because it is the
  one an implementation is most likely to miss」＝为什么这条要单独写），把随后重述原因的那句换成一句指向
  `InventoryEntryAssembly.RequireOneNameEach` 的引用。**只动这一句注释**，不动任何 `Assert`、播种数据或生产代码，
  不需要新证据，也不需要重跑全量判据。

**N-1（记录，不须改，也不必单独开轮次）：** `OneThingInTwoPositionsIsRefusedOnLoadTests.cs:30-31`
「the two rows differ in `slot_index` and agree on everything the unique index covers」按字面自相抵触——
`slot_index` 本身就在唯一索引覆盖的四列里，两行若「agree on everything the unique index covers」则插入会被拒。
意思由前半句自行补全（应为「其余各列」），不构成误导，且无法归因到本轮（IQR-01 未引这句，疑为第 1 轮既有）。
若采纳 R-1，顺手补一个「else」即可，不值得为它单开一轮。

### 结论

F-1 已彻底解决：新措辞在生产与判据两处都经 schema 原文逐字核对成立，且没有把 `NOT NULL` 与
`world_slot_names_its_own_row` 否掉；被引的 `SaveAsync` 注释确实存在并确实写着理由；
「nothing refused it on the way back in」这句改动前状态已消失；新写的每一句都经代码核实为真。
生产侧「本轮只动注释」是完整证明，判据侧为逐项吻合的间接核实，二者都未见回归。
`changes_required` 仅由 R-1 触发——F-2 点名的第二对复制未处理，最小订正是一句注释。

## Result (round 3 — remediation of IQR-02)

- **Changed:** 仅 `OneThingInTwoPositionsIsRefusedOnLoadTests.cs` 的三段注释，代码、断言、播种数据均未动。
  - R-1（F-2 只做了一半）：删去类级 `<remarks>` 与 `APileNamed…` 用例上第二、第三次重述的「堆叠物答 `null`，按身份去重会静默放行」，改为指向 `InventoryEntryAssembly.RequireOneNameEach`（那里是这条理由的归处）。用例上只留本地信息：这个档案替身把模板叫作 interchangeable，因而装载对两个位置都答无身份。
  - N-1（评审附记）：`ACharactersInventory…` 上「differ in `slot_index` and agree on everything the unique index covers」按字面自相矛盾——`slot_index` 本就在那个索引里。改为「两条只差 `slot_index`，所以位置唯一索引是满足的；它看不见的是两条指名同一行」。
  - 用 `<c>` 而非 `<see cref>` 指向 `RequireOneNameEach`：它是 `internal` 类里的 `private` 成员，跨程序集 `cref` 解析不到会触发 CS1574，而 `TreatWarningsAsErrors` 会把它变成错误。
- **Checks:** 全量判据退出码 **0**，**2250 通过 / 2 跳过 / 0 失败**。

## Result (round 4 — DP-001 的实测证据)

DP-001（按世界行去重而不是按 `ItemIdentity`）此前只有推理，没有实测。补一次变异，散列 `f12af46b…5e40f10`：

- **变异**：把两处调用改为 `rows.Where(row => row.Core.ItemKind != 2).Select(row => row.ItemUid)`——即跳过堆叠物，正是一个按身份实现的检查会有的行为（`IdentityOfAsync` 对 `Interchangeable` 答 `null`）。
- **结果**：**失败 1 / 通过 4**，唯一变红的是 `APileNamedInTwoPositionsIsRefusedAlthoughItCarriesNoIdentity`。另两条拒绝判据仍绿——因为它们那件东西有身份，按身份实现照样拒绝。
- **结论**：AC-003 那条判据确实是**唯一**能把「按世界行」与「按身份」两种实现分开的判据；没有它，一个漏掉堆叠物的实现会全绿通过。
- 还原 `sha256sum -c` ＝ OK。

**一处读数说明，免得后人误读**：用 `--filter "FullyQualifiedName~OneThingInTwoPositions"` 跑会报 **5** 条而不是 4 条。第 5 条是既有的保存侧判据 `EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`，方法名里含同一子串被顺带匹配。本文件自己是 4 条 `[Fact]`，全量读数的加法按 4 计：4 ＋ 4 ＋ 4 ＋ 1 ＝ 13，2237 ＋ 13 ＝ 2250。

## Independent Quick Review — IQR-03

- **Reviewed execution round:** Result (round 3 — remediation of IQR-02)
- **Contract:** Revision 1，无修正案
- **Owned diff identity:**
  - `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`（`sha256 f12af46b698d88fd73d904dfbe880634e56228b38997c579f679e99ed5e40f10`）——**与 IQR-02 记下的第 2 轮散列逐字相同，本轮未触及生产文件**（`mtime 21:20:09` 虽晚于部分他单元文件，但内容散列一致，散列为准）。
  - `Server/tests/Dfo.Server.Persistence.Tests/Inventory/OneThingInTwoPositionsIsRefusedOnLoadTests.cs`（新增，`sha256 caa520a6ce7e0a9f9451ec0807edb72eec26e39dfebb181b931feac302c0cd1e`，IQR-02 时为 `d167291a…`，本轮唯一改动文件，`mtime 21:29:33` 为工作树最新）。
  - 工作树另有 `AGENTS.md`、`SqliteItemDomainStoreTests.cs`（改动）与 `AvatarCarrierRulesTests.cs`、`EntryWriterAvatarShapeTests.cs`，mtime 均早于本轮改动，属他单元边界，未纳入本轮。无 `.bak`/`.orig`/`.sha` 残留。
- **Verdict: `passed`**

### R-1 已解决——被点名的那一对复制现已只剩一处

- **理由的归处唯一**：`grep -rn 'in silence\|no identity on purpose' Server/src Server/tests` 中属本条理由的命中只有
  `InventoryEntryAssembly.cs:731-733`「World rows are counted rather than identities… A pile is answered no identity
  on purpose, so counting identities would pass a doubly-named pile through in silence.」
  （其余 4 处命中在 `PvfClearRewardRateSource.cs`、`DeleteCharacterSlice.cs`、`DungeonEntrySlice.cs`，语义无关且既有。）
  判据文件里那句逐字复制**已消失**。
- **判据侧改为引用**：`OneThingInTwoPositionsIsRefusedOnLoadTests.cs:19-20`「The pile case is written out separately
  because it is the one an implementation is most likely to miss; why it is easy to miss is on
  `InventoryEntryAssembly.RequireOneNameEach`.」——保留了判据独有的那半句（为什么单独写），把重述原因换成指路。
  被引处 `InventoryEntryAssembly.cs:724-734` 确实写着原因，读者能到达。**「要引就引」在两侧都落实。**
- **第三处（`APileNamed…` 用例上）也已去掉**：`:78-82` 现为
  `<summary>` The same refusal for a thing the load gives no identity to.` + `<remarks>` The archive stand-in here
  calls the template interchangeable, which is what makes the load answer no identity for either position.」
  ——只剩本地播种信息，不再重述理由，且**仍然告诉读者这条用例的 setup 在做什么**（为什么这两个位置会答无身份）。
  该句属实：本用例用的是 `WithASeededCharacterAsync` 里的缺省 `new InventorySide(path)`，其
  `MainBandPlacements`（`Support/InventorySide.cs:109-112`）答 `new ItemPlacement(2, 100, 65, 120)` 未设 `WorldClass`，
  而 `ItemPlacement.WorldClass`（`ItemPlacementContent.cs:65-68`）「`Interchangeable` by omission」，
  `Interchangeable = 0`（`:35`）。

### N-1 已解决——新措辞对着 schema 原文核对成立

`OneThingInTwoPositionsIsRefusedOnLoadTests.cs:28-31`：「Both positions are in one container and differ only in
`slot_index`, so the unique index over positions is satisfied. What it cannot see is that both name one row.」

- 逐值核对播种：两行 `('character', 83, 0, 3, 'world', thing, thing, 1)` 与 `(…, 4, …)`（`:38-41`），**确实只差 `slot_index`**。
- `CharacterDatabaseSchema.cs:200` 的 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 因 `slot_index` 不同而满足；
  `:246` 的部分唯一索引带 `WHERE owner_scope = 'account'`，对本用例不适用；`core_uid` 上只有非唯一 `idx_item_slots_core`（`:245`）。
- 旧措辞已不存在：`grep -rn 'agree on everything' Server/src Server/tests` **零命中**。
  自相抵触之处（`slot_index` 本在索引内却说「agree on everything the unique index covers」）随之消失。

### 无回归

- **生产文件散列与第 2 轮完全一致**，本轮未改一字（不只是「未改逻辑」）。
- **判据侧只动注释**：现文件的四条用例播种与断言与 IQR-01/IQR-02 独立记下的逐项吻合——
  三条拒绝用例两行只差 `slot_index` 3/4、5/6、7/8；角色那条 `kind:'world', id=coreUid=thing, count:1`（`:38-41`）；
  另两条 `kind:'template'`（`:67-70`、`:87-90`）；货柜 `listType 12`、`(71,5)/(71,6)`；
  正常数据断言 `[(3,2001,11),(4,2002,22)]` 与 `[(5,2600,33)]`（`:132-135`）。**无任何 `Assert` 或播种值变动。**
- **`<c>` 而非 `<see cref>` 已核实**：`grep -n 'see cref'` 在该文件**零命中**，`RequireOneNameEach` 的唯一提及是
  `:20` 的 `<c>InventoryEntryAssembly.RequireOneNameEach</c>`，纯文本。跨程序集指向 `internal` 类的 `private` 成员
  不会触发 CS1574，`TreatWarningsAsErrors` 下不会变成错误。执行记录的这条判断成立。
- 未重跑全量判据（依控制方指示；已有退出码 0 / 2250 通过 / 2 跳过 / 0 失败）。

### findings

**N-2（记录，不须改）：** `:79-80`「The archive stand-in here calls the template interchangeable」这句本身为真，
但同文件 `:137` 另有一个显式的档案替身 `Declaring`（「calls every template one thing」，唯一用处是第一条用例传
`ItemWorldClass.Equipment`），读者可能误以为「here」指的是它。真正在场的是 `InventorySide` 的缺省
`MainBandPlacements`。措辞不假、不误导判定，若日后顺手可写成「the default stand-in」，不值得单开一轮。

### 结论

IQR-02 的唯一阻断项 R-1 已彻底解决：那条理由今天只写在 `InventoryEntryAssembly.cs:731-733` 一处，判据侧
`:19-20` 改为指向它，用例上的第三次重述也已换成本地 setup 信息且信息不缺。N-1 的新措辞经 schema 原文核对成立，
旧的自相抵触措辞零命中。本轮只动了判据文件的注释：生产文件散列与第 2 轮逐字相同，判据的断言与播种值与前两轮
记录逐项吻合。`<c>` 引用为纯文本，无 CS1574 风险。新写下的每一句都经代码与 schema 核实为真，无新的假记载或过期记载。

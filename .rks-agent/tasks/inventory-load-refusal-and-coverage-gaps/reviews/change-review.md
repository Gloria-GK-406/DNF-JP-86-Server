---
kind: rks-review
verdict: approved
scope: change
contract:
  path: ../TaskContract.md
  revision: 1
spec: ../spec.md
plan: ../plan.md
baseline: 25dccec6
---

# Change Review: 装载侧重复指名的拒绝、三条取值校验的判据、时装搬迁的正向覆盖

## Reviewed change

`git status --short` 与 `git diff` 在评审开始与结束时**逐字相同**，change surface 恰为交办清单所列七项，
无第八项、无 `.bak`/`.orig`/`.sha`/`.rej`/`.tmp` 残留（`find` 全工作树零命中）：

- 生产：`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`（`32 +/4 -`，唯一一处生产改动）
- 记载：`AGENTS.md`（`10 +/16 -`，净 −6）
- 判据（改）：`AvatarCarrierRulesTests.cs`（`180 +/0 -`）、`SqliteItemDomainStoreTests.cs`（`78 +/7 -`）
- 判据（新）：`OneThingInTwoPositionsIsRefusedOnLoadTests.cs`、`EntryWriterAvatarShapeTests.cs`
- 工作流产物：`.rks-agent/`（非产品代码）

未触碰 `.worktrees/client-debug-lab`。评审期间的变异全部按「文件级备份 ＋ `sha256sum -c`」还原，
三处生产文件收工散列与变异前逐字相同（见第 7 节）。

## 独立取证（不采信执行记录）

### 1. 硬约束：正常数据上逐值不变 — 成立

`RequireOneNameEach` 只对同一次读回的行按 `row.ItemUid` 判重，除抛出外不写、不排序、不改任何值。

- **`ItemUid` 的来源是 `items.item_uid`，由 `JOIN items held ON held.item_uid = place.core_uid` 取得**
  （`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:73`、`:358`）。
  `item_slots.core_uid` 是 `INTEGER NOT NULL`（`CharacterDatabaseSchema.cs:200`），
  写入方 `HoldAsync` 一律绑 `@core = identity`（`SqliteItemDomainStore.cs:225-238`），
  堆叠物也有自己的世界行。**因此正常数据上「两条位置行同一个 `item_uid`」不可能合法出现**——
  保存侧 `InventoryEntryAssembly.cs:224-229` 早已按同一口径拒绝它。误伤面为零（plan R-1 关闭）。
- **不会重排、不会重复查询。** `ReadAllAsync` 交出的是已物化的 `List<TRow>`
  （`Server/src/Dfo.Server.Persistence/Database/SqliteTable.cs:23-45`），
  `rows.Select(...)` 与随后的 `foreach (var row in rows)` 是对同一个列表的两次枚举，
  顺序仍是 SQL 的 `ORDER BY place.list_type, place.slot_index, held.item_uid`。
- **对坏数据只可能改变「先报哪一条」**，与 spec Assumptions 记的一致；正常数据无此分支。
- 实测（干净树，本人重跑）：Application **1039/0/0**、Protocol **528/0/0**、Persistence **329/0/0**，
  与交办读数逐项相符；Application 与 HEAD 基线同为 1039，**未动任何既有 Application 判据**。

### 2. 判据是否可证伪 — 四处变异，全部本人现做

| 变异 | 手法 | 结果 |
|---|---|---|
| `CarriedAvatarsAsync` 恒返回空表 | `SqliteItemDomainStore.cs:461` 的 `return carried;` → `return new Dictionary<int, object?[]>();`（保持可达，避开 CS0162） | **红 2 / 通过 327**：`AnAvatarWhoseRowIsRebuiltIsHandedItsSixFactsAgain`（`Assert.NotNull() Failure`，`SqliteItemDomainStoreTests.cs:250`）与 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`。`Relocate_LeavesASparedAvatarsRowStandingWithItsFactsUntouched` **仍绿** |
| 装载侧拒绝失效 | `InventoryEntryAssembly.cs:311`、`:339` 的 `rows.Select(row => row.ItemUid)` → `Array.Empty<long>()` | **红 3 / 通过 326**：恰为角色、货柜、堆叠物三条拒绝判据（均 `Assert.Throws() Failure: No exception was thrown`）。正常数据那条 `AnInventoryThatNamesEachThingOnceIsLoadedUnchanged` **仍绿**，且**无任何既有判据变红** |
| `GameInventoryDelivery` 两条取值校验失效 | `:50`、`:112` 的条件各追加一个运行期恒假的合取项（保留 `avatar!`，避开 CS8602） | **红 3 / 通过 525**：`AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts` ＋ `AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused(29)`／`(31)` |
| `WithAvatarDetail` 的 `Color1` 子句删去 | `InventoryEntryAssembly.cs:395` 整行替换为注释 | **红 1 / 通过 328**：仅 `AnAvatarWhoseFirstColourIsPastTheCeilingIsRefused`。**三臂确实各自可辨**（SC-5） |

**TASK-04 的关键对照本人独立确证**：同一变异下现在恰红 2 条，其中一条是本次新增；
其余判据未被改动，故「HEAD 上同一变异只红 1 条」是这一读数的直接推论。
被改名的那条判据在该变异下仍绿——「六个值被搬到新行上」在 HEAD 上确为零覆盖，本次确已补上。

**未发现「因无关原因而通过」的判据。** 另核三点断言强度：

- 三条协议判据用 `Assert.Equal` 比**全文消息**，两条消息在 `Server/src` 全域各只有一处出处
  （`GameInventoryDelivery.cs:51`、`:113`），不可能由别处满足。
- `WithAvatarDetail` 三臂断言的是 `InvalidDataException` **类型**：守卫一去，颜色臂会落到
  `checked((ushort)…)` 抛 `OverflowException`，类型断言正好把两者分开（本人变异实测即为此形）。
- 重建判据的 `Assert.NotEqual(standing, rebuilt)` 有鉴别力：`items.item_uid` 是
  `INTEGER PRIMARY KEY AUTOINCREMENT`（`CharacterDatabaseSchema.cs:199`），删掉的 rowid 不复用；
  两侧先各做 `Assert.NotNull`，空对空的假通过被堵死。
- 四组新判据各带一条**正例**（`AnInventoryThatNamesEachThingOnceIsLoadedUnchanged`、
  `AnAvatarWithItsThirtyBytesIsWrittenAsTheLongerEntry`、`AnAvatarWhoseFactsAreAllInRangeIsCarried`），
  「拒绝一切」这种反向空判据也被排除。

### 3. 字节锚与架构判据 — 逐字节相同（SC-7、SC-10）

两个文件均**不在** `git status` 中，且 SHA-256 与 `git show 25dccec6:<path>` 逐字相同：

- `SelectCharacterAcceptedTranscriptTests.cs`：`1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`
- `CommandHandlerArchitectureTests.cs`：`ab9107bc2371d4a3d4e5f898b3e1bf29995a8ffd315c35ffad06abbdb29d9714`

`CommandHandlerArchitectureTests` 在本人重跑的 Application.Tests 1039 条中通过；判定谓词与豁免清单零改动。

### 4. 既有判据的期望值与断言 — 未被改写（SC-2）

`git diff -- Server/tests` 的**全部删除行只有 7 行**，逐行核对为：
6 行文档注释文本 ＋ 1 行方法签名（`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`）。
`AvatarCarrierRulesTests.cs` 为纯追加（`180 +/0 -`）。
改名判据的方法体（含播种值 `11, 12, (byte)13×30, 14, 15, 16` 与两条 `Assert`）在 diff 中全为上下文行，
**一字未动**。RQ-009／AC-013 的实质要求成立。

### 5. AGENTS.md — 逐句可核，无「把未闭合的缺口写成已闭合」（SC-9）

删去的两条缺口条目确已闭合（依据即第 2 节的四处变异）；保留的条目
（契约兽互换、`SqliteWorldItemTable`、`EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt`、
「三处『结论对，但撑着它的不是代码』」整节）`git diff` 一处未碰，仍在原位原文。

**未经独立复核的最后三行（`AGENTS.md:536-538`）本人逐项核实，全部为真：**

- `ReadByCharacterAsync` 确带 `AND place.list_type NOT BETWEEN 19 AND 23`
  （`InventoryItemTables.cs:74`）。
- 类名确为 `SqliteCharacterTitleBookTable`，其 `ReadAsync` 确以
  `WHERE … AND place.list_type BETWEEN 19 AND 23` 单独读那五个容器（同文件 `:382`、`:388-395`），
  且同样 `JOIN items held ON held.item_uid=place.core_uid`。19..23 恰为**五**个容器。
- 因此一条称号书位置行与一条别处位置行指名同一世界行时，两条入场读各自只看得见一半，
  「两处都看不见」成立。

同段前四行（`:532-535`）亦逐句核对为真：`item_slots` 的唯一性只在
`UNIQUE(owner_scope, owner_id, list_type, slot_index)`，`core_uid` 上的
`idx_item_slots_core` 是普通索引；`SaveAsync` 的 `claimed` 取 `SettledItem.Identity`
（`:213` 的 `checked((int)written)`，是世界行而非 `ItemIdentity`）；两处调用确在读到行之后、逐行处理之前。

判据端的两句非代码断言亦经核实：`avatar_jewel_socket BLOB CHECK (… length(…) = 30)` 确在
`items` 表上（故 `AvatarCarrierRulesTests` 用假件而非真库是必要的），两个颜色列确无 CHECK。

### 6. 分层、编译门与残留

新增拒绝落在 `Dfo.Server.Application` 内部，只用 `System.Linq` 与 `HashSet<long>`，无新增项目引用、无反向引用。
`TreatWarningsAsErrors` 下 Application／Persistence／Protocol 三个项目在本人的四次变异与三次干净重跑中
**零警告零错误**（CS9113 不涉及：未新增主构造器参数）。
`EntryWriterAvatarShapeTests` 放在 `Game/StateSynchronization/`，与该目录既有三个用例文件同族。

### 7. 变异还原核实

| 文件 | SHA-256 | 收工 `sha256sum -c` |
|---|---|---|
| `SqliteItemDomainStore.cs` | `c3684920e553af2b1a897e76683b7c4e2625c688d6f97cba6b4a21b52676d09f` | OK |
| `InventoryEntryAssembly.cs` | `f12af46b698d88fd73d904dfbe880634e56228b38997c579f679e99ed5e40f10` | OK |
| `GameInventoryDelivery.cs` | 备份时即时取散列 | OK |

`SqliteItemDomainStore.cs` 的散列与 TASK-04 记录中的还原散列**逐字相同**，
说明该单元的变异确按散列还原过——这一点不依赖自述。

## Success Conditions

| ID | 判定 | 依据 |
|---|---|---|
| SC-1 | 满足 | 三条拒绝判据（角色／货柜／堆叠物）；删拒绝后恰红这三条 |
| SC-2 | 满足 | Application 1039 与基线同数；既有判据删除行仅 7 行注释＋签名；正例判据断言槽号与 core 取值 |
| SC-3 | 满足 | 令 `GameInventoryDelivery.cs:51` 的拒绝失效 → `AnItemThatIsNotAnAvatarMayNotCarryAvatarFacts` 红 |
| SC-4 | 满足 | 令 `:113` 的拒绝失效 → `AnAvatarWhoseJewelSocketIsNotThirtyBytesIsRefused(29)/(31)` 红 |
| SC-5 | 满足 | 删 `Color1` 子句 → 恰红对应那一条，另两条仍绿；三臂可辨 |
| SC-6 | 满足 | `Assert.NotEqual(standing, rebuilt)` ＋ 六值逐个断言；`CarriedAvatarsAsync` 恒空 → 该判据红 |
| SC-7 | 满足 | SHA-256 与 HEAD 逐字相同 |
| SC-8 | 满足（沿用已收集证据 ＋ 本人分项复跑） | Application 1039 / Protocol 528 / Persistence 329 全绿；全量 2250/2/0 由控制方证据支撑，跳过仍为 2 |
| SC-9 | 满足 | 第 5 节逐句核对，含未经复核的最后三行 |
| SC-10 | 满足 | 文件散列相同 ＋ 该用例在 1039 条中通过 |

新增判据数 13（4 ＋ 4 ＋ 4 ＋ 1）；2237 ＋ 13 ＝ 2250，与分项增量（Protocol +4、Persistence +9）自洽。

## Blocking Findings

无。

## Non-blocking Observations

- **N-1 一条理由有两个归处。** `AGENTS.md:535`「两处都数世界行而不数 `ItemIdentity`——堆叠物没有身份，
  按身份数会把它漏掉」与 `InventoryEntryAssembly.cs` 中 `RequireOneNameEach` 的 `<remarks>`
  第二段（"World rows are counted rather than identities … would pass a doubly-named pile through in silence"）
  说的是同一件事。两处今天都为真，但按 AGENTS.md「每种事实只有一个归处／要引就引，不要抄」，
  这是一个漂移点。代码注释里的「为什么不按身份数」属于读这段代码就要知道的，建议保留；
  若日后收敛，收 `AGENTS.md` 那半句为指路更合适。不阻塞。
- **N-2 AC-013 的字面表述与 AC-011 相互抵触。** AC-013 要求「所在文件的用例总数与 HEAD 相同」，
  而 AC-011 要求的新判据正落在同一文件里（19 → 20）。实现遵循的是 AC-013 的**实质**
  （改名不增删任何用例、不动任何断言与期望值），这一点本人已逐字核实成立。
  属 spec 措辞瑕疵，不是实现缺陷。
- **N-3 `AGENTS.md:536-538` 只说了入场两处够不到称号书，未说保存侧同样够不到。**
  `SaveAsync` 的 `settled` 只来自那五个背包容器，称号书位置行同样落在它的视野之外。
  现有文字没有断言相反的事，故不为假；但读者可能推断保存侧覆盖了它。若日后收紧，补半句即可。不阻塞。

---

## 追加：N-3 改写后段落的独立复核（文档单点变更，评审后追加）

**范围**：仅 `AGENTS.md:536-539`（`item_slots` 小节内那四行，即原 N-3 指出的「只说入场侧」被改写为
「两侧都够不到」之后的文本）。不重跑测试，不改动除本文件外的任何文件。

**结论：`approved`。** 六条断言逐条为真，标识符拼写全部正确，与 `AGENTS.md` 其余部分无抵触。

### 逐条判定

| # | 断言 | 判定 | 证据 |
|---|---|---|---|
| 1 | `ReadByCharacterAsync` 带 `list_type NOT BETWEEN 19 AND 23` | **真** | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs:63`（声明）、`:75`（`AND place.list_type NOT BETWEEN 19 AND 23`） |
| 2 | `DeleteOwnExceptAsync` 带同一排除 | **真** | 同文件 `:121`（声明）；**两条**语句各带一次，`:132`（`DELETE FROM items … `）与 `:134`（`DELETE FROM item_slots …`） |
| 3 | `ClearOwnPositionsAsync` 带同一排除 | **真** | 同文件 `:151`（声明）、`:155` |
| 4 | 那五个容器由 `SqliteCharacterTitleBookTable.ReadAsync` 单独读 | **真** | 同文件 `:381`（类）、`:387`（`ReadAsync`）、`:393`（`AND place.list_type BETWEEN 19 AND 23`）；19..23 恰为五个，与 `Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs:14-18` 的 `TitleBookGeneral=19 … TitleBookEvent=23` 一致。该表已注册（`Server/src/Dfo.Server.Persistence/PersistenceModule.cs:139-140`）并由 `Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:50` 消费，是活路径而非死代码 |
| 5 | 整个租借往返都不经过它们 | **真** | 见下「重点核查」 |
| 6 | 称号书位置行与别处位置行指名同一世界行时，`RequireOneNameEach` 与 `SaveAsync` 的 `claimed.Count != settled.Count` **两侧都看不见** | **真** | 见下「重点核查」 |

### 重点核查：租借里能不能出现称号书容器

交办说明里的一句前提**不成立**，必须先纠正：
`InventoryEntryAssembly.ParseContainerType`（`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs:678-688`）
**不**映射 19..23——它只认 `0/1/2/3/7`（外加 `12 when allowAccountCargo`），其余一律
`throw new InvalidDataException($"Inventory container type {value} is not an entry container.")`。
把 19..23 映到 `TitleBook*` 的是**另一个成员** `ParseLockContainerType`（同文件 `:690-708`），
它服务的是装备锁，读的是 `SqliteCharacterItemLockTable`（`Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs:305`），
不是 `item_slots`。因此「若租借里带上称号书容器，`settled` 就会包含它」这个假设路径**在装载侧就断了两道**。

租借容器的唯一来源，逐段追下来只有一条：

1. `EntryInventoryLeaseRegistry.Replace`（`Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs:166-174`）
   用 `new CharacterItemLeaseState(inventory.CharacterId, inventory.Containers)` 建租借，
   `inventory` 是 `CharacterInventoryEntryState`。三个调用点全在同一文件（`:37`、`:109`、`:126`），
   传的都是 `inventories.LoadAsync(...)` 的结果。
2. `CharacterInventoryEntryState` 全仓只有一处构造：`InventoryEntryServices.cs:78`。它的 `containers`
   来自 `:60-67`，是 `CharacterContainerOrder.Select(...)`；而 `CharacterContainerOrder`（`:38-45`）
   **写死五个值**：`Main / Avatar / PersonalCargo / Equipment / Pet`，没有任何 `TitleBook*`。
   即便存储里冒出别的容器，`NormalizeCharacterItems` 的 `ValidateItems` 还要求
   `CharacterContainerOrder.Contains(item.ContainerType)`（`:159`），不满足即拒。
3. 另一条理论入口 `TryMarkDirtyExact(session, CharacterItemLeaseState)`（接口 `IInventoryEntryServices.cs:173`，
   实现 `SelectCharacterEntryInventoryDomainService.cs:156`）能直接塞任意容器集合，但**生产侧零调用方**：
   全仓 `--include=*.cs` 检索只命中接口声明、实现，以及两处测试
   （`SelectCharacterEntryLifecycleServiceTests.cs:35,49`、`SessionGenerationOwnershipTests.cs:260` 的桩）。

于是 `SaveAsync`（`:113`）里 `lease.Inventory.Containers.Where(… != AccountCargo)`（`:162-163`）
所迭代的容器最多是那五个，`settled`（`:161,:209`）不可能装进一条称号书行，
`claimed = settled.Select(…).ToHashSet()` 与 `claimed.Count != settled.Count`（`:224-225`）自然看不见它。
**断言 6 的保存侧成立。**

装载侧：`RequireOneNameEach`（`:736`）比对的行来自 `ReadByCharacterAsync`（调用点 `:309-311`），
该语句已排除 19..23，重复指名的两行里只有一行进得来，`seen.Add` 不会撞。
**断言 6 的装载侧成立。**

断言 5「整个租借往返都不经过它们」——按上面 1/2/3 三段，装载（`ReadByCharacterAsync` 排除）、
入租借（`CharacterContainerOrder` 五值）、保存（`DeleteOwnExceptAsync`/`ClearOwnPositionsAsync`/
`settled` 三处都排除）全程闭合，**成立，不属过强表述**。附带一条自洽性：保存不删也不清称号书位置行，
所以那一行在往返后原样留存、继续指名同一世界行，与「两侧都看不见」一致，不是自相矛盾。

### 无其他伪陈述 / 标识符 / 抵触

- 标识符逐个核对拼写无误：`ReadByCharacterAsync`、`DeleteOwnExceptAsync`、`ClearOwnPositionsAsync`、
  `SqliteCharacterTitleBookTable.ReadAsync`、`InventoryEntryAssembly`、`SaveAsync`、`RequireOneNameEach`、`ItemIdentity`。
- 紧邻的前两句同样为真：`SaveAsync` 的判据在落位前（`:224-225` 在 `ClearOwnPositionsAsync`/`HoldOwnAsync`
  之前）、`RequireOneNameEach` 确有**两条**入场读取的调用点（`:311` 角色背包、`:339` 账号货柜），
  两处都数世界行 `ItemUid` 而不数 `ItemIdentity`。
- 与 `AGENTS.md` 其余部分不抵触：全文只有 `:673` 另提「称号」，讲的是档案里 `[title name]` 模板计数，
  与容器编号无关。本次 diff 同时删掉的旧缺口条目「装载侧没有对称的拒绝」已随本次实现失效，删除正确。

### 一条不阻塞的措辞注记（不要求改）

断言 5 的「它们」指的是 `item_slots` 里那五个称号书容器，在本小节（`item_slots`）语境内准确。
严格地说，入场读取里还有一处**认得** 19..23：`ReadLocksAsync`（`:587-596`）经 `ParseLockContainerType`
把称号书容器上的装备锁读进 `CharacterInventoryEntryStorageRecord`，`LoadCharacterAsync:83` 只滤掉 `AccountCargo`。
但那读的是装备锁表而非 `item_slots`，且 `CharacterItemLeaseState` 只带 `CharacterId + Containers`，
锁并不进租借——故不构成伪陈述，仅在有人把「不经过」读成「整个入场记录里一个 19..23 都不出现」时会误解。
若日后要收紧，把「整个租借往返都不经过它们」写成「整个租借往返不经过这五个容器的位置行」即可，
本次不作为修改要求。

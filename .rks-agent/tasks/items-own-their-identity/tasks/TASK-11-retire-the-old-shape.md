# TASK-11 — 退役旧形状，并证明词汇与机检未被放宽

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 删除已无人读的旧列与旧表；证明 `store` 一词仍只有一种读法；证明两处注入面机检未被放宽。
- Referenced spec requirements: RQ-011, RQ-013, RQ-014
- Referenced plan sections: T-011
- Owned boundary: BD-002, BD-004, BD-006, BD-011, BD-014
- Permitted files or interfaces: `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`、`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`、`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs`、`Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs`
- Dependencies: TASK-10
- Required dependency outputs: 全部消费者已切到新形状
- Acceptance conditions: 旧列与旧表已删且无人读；领域层中以 `DomainStore` 结尾的类型逐个核过、每一个都是事务结算的真源；两处机检的判定谓词与豁免清单差异为空；入场帧哈希守卫仍绿。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-023 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-024 `Non-command: 逐个核对以 DomainStore 结尾的类型`
- CK-025 `git diff -- '*CommandHandlerArchitectureTests.cs' '*NotificationHandlerArchitectureTests.cs'`

- Required evidence: 退役的 diff；`DomainStore` 类型逐个核对的结果；两处机检的 diff 输出；帧哈希守卫输出。

## Contract Amendment — AMD-TASK-11-01

**控制器裁定：本 Task 的真实工作比契约原文大得多，且它是本成果真正完成的地方。**

### 被证伪的前提

计划 T-011 Step 1 原写「位置四列……**自 T-004 起已无人读**」。**这句是假的**，由 TASK-08 查出、控制器复核：

- **仍在读**：`InventoryItemTables.cs:35`、`:57`、`:237` 三条
  `SELECT … list_type, slot_index … FROM items`。
- **`item_slots` 生产上无人读**：全仓对它的语句只有
  `SqliteItemDomainStore.cs:503`（DELETE）与 `:589`（INSERT），以及
  `InventoryItemTables.cs:588/605` 那个至今无生产调用方的行网关。
- **两条写入方完全绕过槽表**：`CharacterRosterService.CreateAsync:214` 与
  `InventoryEntryAssembly.SaveAsync:181/185` 直接 `items.InsertOwnAsync` / `UpdateOwnAsync`。

**所以今天 `item_slots` 还不是承重的。** 位置的权威仍在 `items` 的那四列上。

### 修正后的次序（不得跳步）

1. **让全部写入方都写槽行**——含上面那两条绕过的。
2. **让位置读取改读槽表**——三条 SELECT。
3. **然后才能删那四列。**

**跳过 (a)(b) 直接删，会让入场存盘写出没有位置的物品。**
（删其余已无人读的旧表——如 `character_creatures` 的声明——不受此限。）

### 允许文件相应扩充

原清单之外增加：
`Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs`、
`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`，
以及 `DSP-TASK-07-01` 已批准的
`Server/src/Dfo.Server.Application/Characters/SelectCharacterPostEntryServices.cs`。

### 若本 Task 判断这已超出一个 Task 的合理体量

**允许交回控制器请求拆分**，不要硬做完。控制器会按 `task-superseded` 建替换 Task。

### 一并带入的既有债

- `SelectCharacterPostEntryServices.cs:120` 的 `core.Value` 寻址（`DSP-TASK-07-01`／`DSP-TASK-07-02`）。
  **性质已收窄**：失败模式只有「静默不复活」，不存在「复活错另一只」。
- 抬起／落下机制与 `-1` 哨兵：位置四列一走，`items` 上再无唯一键可撞，
  **整套机制应可摘掉**（`DSP-TASK-04-02`）。
- TASK-08 的孤儿查询用位置列区分「堆叠物 core 行」与真孤儿。
  **位置列退役时，这个区分必须落到行自己身上**，否则查询失真。

## Execution Result — round 1

### 次序 (1)(2)(3) 的完成证据

**(1) 全部写入方都写槽行。** 修正案点名的两条绕过已消除：

- `CharacterRosterService.CreateAsync` 现在写两行：`items.InsertOwnAsync(core, null)` 拿回身份，
  再 `items.HoldOwnAsync(character, Equipment, slot, World, uid, uid, 1)` 写装备槽行；
  槽行插不进（位置已被占）当场抛。
- `InventoryEntryAssembly.SaveAsync` 改成两段：先把每件东西**是什么**结算完
  （`UpdateOwnAsync` / `InsertOwnAsync`，都不再带位置），再 `ClearOwnPositionsAsync` 一次清空
  该角色全部位置，最后逐条 `HoldOwnAsync` 落位。
- 结算侧 `SqliteItemDomainStoreSession.HoldAsync` 本来就写槽行，本轮补上 `core_uid`。

证据：`SELECT COUNT(*) FROM item_slots WHERE owner_id=…` 现在是判据里数「这个角色有几件东西」
的那条语句（`SqliteEntryInventoryMutationRepositoryTests` 三处、`EntrySaveReconciliationTests`
的 `RowCountAsync`、`TitleBookCarrierTests` 两处）——本轮之前这些数的是 `items`。

**(2) 位置读取改读槽表。** 契约点名的三条，加上契约未点名但同样按位置寻址的六条：

| 读 | 改法 |
|---|---|
| `InventoryItemTables.cs` 角色物品全量读 | `FROM item_slots place JOIN items held ON held.item_uid = place.core_uid` |
| 同上 主列表读 | 同 |
| 同上 账号仓库读 | 同（`owner_scope='account'`） |
| 同上 称号书读 | 同（`list_type BETWEEN 19 AND 23`） |
| `SqliteItemDomainStoreSession.ReadAllAsync` / `ReadAsync` / `DisplacedAsync` | 同 |
| `CharacterEntryStateTables` 契约兽列表读 | 归属改成 `EXISTS(item_slots …core_uid=held.item_uid)` |
| `SqliteCharacterDomainStore.CreatureSection` 读与两条写 | 同 |

**契约与计划都少算了后六条。** 计划只列了 `InventoryItemTables.cs:35/:57/:237`；
但 `SqliteItemDomainStoreSession` 整个会话、称号书读、契约兽的两处归属过滤，
同样是按 `items` 的四列寻址的，不改它们那四列就删不掉。

**(3) 删那四列。** `items` 现在是
`item_uid, item_core, cargo_character_id, created_at, updated_at, avatar_*7, creature_*8` ——
20 列，`UNIQUE(owner_scope, owner_id, list_type, slot_index)` 一并去掉。

### 改了哪些文件、每处为什么（按块）

**生产 · 10 个文件**

1. `Schema/CharacterDatabaseSchema.cs`
   - `items` 声明：去四列、去唯一键。**这就是退役本身。**
   - `item_slots` 声明：加 `core_uid INTEGER NOT NULL` 与
     `CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id)`。
     **理由见「债三」。**
   - 删 `character_creatures` 表声明与它那 11 行可补列（已无人读）。
   - 索引：删 `idx_items_owner_space`（那三列已不在 `items`）、
     删 `idx_items_account_slot` 并在槽表上重建为 `idx_item_slots_account_slot`
     （D-16 那条仓库唯一性原样搬家，不是放宽）；
     加 `idx_item_slots_core ON item_slots(core_uid)`，孤儿查询与全部 JOIN 走它。
2. `Abstractions/Persistence/InventoryTables.cs`
   - `ItemSlotRow` 加 `CoreUid`；`IWorldItemTable.InsertAsync` 去掉 `ItemPlace place`
     （世界行不再有位置可写）；`ReadUnreferencedAsync` 的注释重写。
   - `ICharacterItemTable`：`UpdateOwnAsync` 去掉 list/slot；`InsertOwnAsync` 去掉 character/list/slot
     并改成返回 `long?`（身份由列分配、读回来）；新增 `ClearOwnPositionsAsync` 与 `HoldOwnAsync`。
3. `Persistence/Inventory/InventoryItemTables.cs` — 上表那四条读 + 三条写 + 世界表插入/孤儿查询
   + 槽表三条语句带 `core_uid`；`SqliteItemSlotTable.Name` 由 private 改 internal 供角色表复用。
4. `Persistence/Inventory/SqliteItemDomainStore.cs` — 三条读改 JOIN；
   **`LiftAsync` 与 `NoContainer = -1` 整块删除**（债二）；
   `EmptyAsync` 改为「经位置找到行、按名单放过幸存者」；`LandAsync` 只写 core；
   `FillAsync` 不写位置；`HoldAsync` 写 `core_uid`。两条读顺带答出身份（债一要用）。
5. `Persistence/Characters/CharacterEntryStateTables.cs` — 契约兽列表读的归属改走槽表。
6. `Persistence/Characters/SqliteCharacterDomainStore.cs` — **不在允许清单内，见下方「越界文件」。**
7. `Application/Inventory/InventoryEntryAssembly.cs` — `SaveAsync` 两段化；
   新增 `SettledItem` 记录；新增「同一件东西被两个位置认领」的拒绝。
8. `Application/Characters/CharacterRosterService.cs` — 建角色时写世界行 + 槽行。
9. `Application/Characters/SelectCharacterPostEntryServices.cs` — 债一。
10. `Abstractions/Inventory/IItemDomainStore.cs` — `StoredItem` 加 `Identity`（读答、写不答）。

**判据 · 22 个既有文件 + 1 个新文件**

- 形状判据两处（`ItemCarrierShapeTests`、`SchemaOwnershipTests`）：见下「改写了哪些期望值」。
- 其余 19 个是**编排更新**：种子由「一条 `items` 行」变成「一条 `items` 行 + 一条 `item_slots` 行」，
  回读由 `FROM items WHERE owner_scope…` 变成 JOIN 或直接问 `item_slots`。
  断言一条未动。
- `Support/VolatileItemDomainStore.cs`：只加注释，说明这个替身把写进去的身份原样交回。
- `SelectCharacterEntryLifecycleServiceTests`：那一处写现在带 `Identity = new ItemIdentity(77)`，
  并把 core 的 value 改成 999（**故意与身份不同**，让这条判据自己也能分辨）。断言未动。
- 新增 `Persistence.Tests/Inventory/CreatureRevivalAddressesByIdentityTests.cs`（2 条，见债一）。
- `EntrySaveReconciliationTests` 新增 1 条（同一件东西两个位置）。

### 改写了哪些期望值，以及为什么不是迁就实现

只有两个文件的期望值动了，都是**本成果的交付物本身**：

- `ItemCarrierShapeTests`
  - `Items` 列表去掉四列、`ItemSlots` 列表加 `core_uid` —— 这就是 RQ 要求的退役。
  - `ItemsCarriesEveryConstraintItsSourcesDeclared`：原来断言 `items` 上有
    `UNIQUE(owner_scope,…)` 与 `CHECK (owner_scope IN …)`。这两条**没有消失，是搬到了槽表**，
    所以这里改成 `DoesNotContain`（两张表都拿着同一个事实正是它们会打架的原因），
    并在 `TheSlotTableKeeps…` 里**新增**对这两条的断言 + 新增对
    `world_slot_names_its_own_row` 的断言。净效果是断言变多不是变少。
  - `AnItemColumnWithDisagreeingSourcesTakesTheStrictest`：四个位置列改成问 `item_slots`，
    并**新增** `core_uid` 一例（5 例 → 6 例）。
  - `TheCargosOwnSlotUniquenessIsRestoredByAPartialIndex`：索引名与表名跟着列走，
    `UNIQUE` / `owner_id, slot_index` / `WHERE owner_scope='account'` 三条断言一字未改。
  - `EveryIndexThatNamedARetiredTableIsGone…`：**新增** `idx_items_owner_space` 的 `DoesNotContain`，
    并把「搬走后重建」的那条正向断言指向 `ON item_slots(core_uid)`。
    这条索引没有原样重建：它答的是「这个所有者按位置」，而槽表本身就把这四列拿着唯一，
    每条读又都先给 `owner_scope`，所以那个约束自带的索引已经答了；重建一条是纯冗余。
  - `ASparseItemColumnIsNullableAndHasNoDefault` 的取列辅助改成「到下一个逗号**或表末**」，
    因为 `creature_tail_flag` 现在是最后一列（原来后面跟着 `UNIQUE(...)`）。断言未动。
- `SchemaOwnershipTests.EveryTableIsDeclaredSomewhere…`：`44` → `43`，
  `Assert.Contains("character_creatures(")` → `Assert.DoesNotContain(...)`，
  因为这张表正是本任务要退役的。计数断言仍是精确值。
- `SqliteInventoryEntryRepositoryTests.SeedSchemaAsync` 里手写的那份最小 schema
  加了 `item_slots` 且**故意不给位置加唯一键**：`LoadAsync_RejectsInvalidStoredCharacterItems`
  的 "duplicate" 一例必须能把「一个位置两件东西」写进库，才谈得上验载入侧的拒绝。
  已加注释说明。`TheCargoRefusesASecondItemInASlotItAlreadyHolds` 仍然绿，
  靠的是真 schema 补上的 `idx_item_slots_account_slot`。

### 四笔债各自的了结方式

**债一（`SelectCharacterPostEntryServices.cs:120` 的 `core.Value` 寻址）——改了。**

`StoredItem` 多了一个 `Identity`：读从行里答出来，写不答（与 `WorldClass` 正好相反的方向）。
`ReviveAsync` 现在是

```csharp
if (equipped.Identity is not { } worn) return null;
var creatureKey = (long)worn.Value;
```

`worn.Value` 就是槽行 `core_uid`，也就是契约兽世界行的 `item_uid`，
也就是 `SqliteCharacterCreatureTable` 交出的 `CreatureKey`、帧里写的 `PetKey`、
`CreatureSection` 的键 —— 四者由构造同一。

新判据 `CreatureRevivalAddressesByIdentityTests` 两条，种子里**故意**让
`item_uid = 31` 而 `core.Value = 4242`：

- `TheWornCreatureIsFoundByItsIdentityRatherThanByTheValueInItsCore` —— 复活成功、
  `PetRevivalState.CreatureKey == 31`、世界行的 `creature_field04` 变 1。
- `ACreatureListNamingTheCoresValueInsteadIsRefused` —— 帧里列的是 4242 时当场拒绝。

**变异验证**：把 `worn.Value` 改回 `core.Value`，两条判据**都红**；
`cp` 回来后 `sha256sum -c` 逐字节一致。

**一处没有跟着改，明说**：`core.Value <= 0` 那道前置守卫**保留了**。
它今天只起「更少复活」的作用，摘掉它会让本轮之前不复活的情形开始复活，
那是一次没有任何 RQ 授权的线上行为变化，而本轮的硬约束是不改任何线上字节。
它现在是一条与寻址无关的遗留守卫，建议在被授权改行为的那一轮一并处理。

**债二（抬起／落下与 `-1` 哨兵）——整套摘掉了。**

`LiftAsync`、`NoContainer = -1` 从 `SqliteItemDomainStore.cs` 中删除。
理由成立且已写进注释：`items` 上再没有位置列、再没有
`UNIQUE(owner, list, slot)` 可撞，所以「先把幸存行挪到没人的容器里」这件事失去了全部理由。
腾位置的活由 `EmptyAsync` 里一句 `AND item_uid NOT IN (<幸存者>)` 接掉——
**清空仍然发生在任何填入之前**，交换两个槽照旧不需要停车位。

因此也**不需要**给 `list_type` 补下界硬约束：不再有任何代码写 `-1`。
`InventoryItemTables.cs` 里 `NOT BETWEEN 19 AND 23` 现在作用在**槽行**上，
而槽行的 `list_type` 只由 `HoldOwnAsync` / `HoldAsync` / 建角色写，三处都写真实容器号。

**债三（孤儿查询对位置列的依赖）——改了，第二条 `NOT EXISTS` 整条删掉。**

`item_slots` 加了 `core_uid`：**任何一种槽行都指名它那 82 字节住在哪一行**。
world 槽 `core_uid = id`（由 CHECK 强制），template 槽 `core_uid` 指向堆叠物那条一次性 `items` 行。
于是 `ReadUnreferencedAsync` 变成一条：

```sql
WHERE NOT EXISTS (SELECT 1 FROM item_slots holder WHERE holder.core_uid = items.item_uid)
```

「是不是堆叠物 core」这个区分**不再需要**，因为堆叠物的行现在也被它的位置指名了。
TASK-08 快审说的「行上没有别的东西能说它是堆叠物」仍然属实——本轮没有去证伪它，
而是把问题换掉了：不再需要问这件事。
`WorldAndSlotTableTests.A_piles_row_is_not_one_of_those`（原名带
`_although_no_slot_names_it`，已更名，因为现在有槽指名它）仍然三种行同库、
仍然只允许那一条真孤儿被报出来，断言一字未改。

**债四（`StoredItem.WorldClass` 的消费者）——复核，仍有消费者，不是死字段。**

四处：`SqliteItemDomainStoreSession.Write`（缺它就拒）、`Survivors`（把堆叠物排除在幸存者之外）、
`HoldAsync`（world/template 分支）、`InventoryEntryAssembly` 新的落位循环（同样的分支）。

### 一处本轮新发现、并当场堵上的洞

位置由「行自己带」变成「另一行指名」之后，**同一件东西可以被两个位置同时指名**——
旧形状表达不出这种状态（一行只能有一个位置），它的表现是悄悄丢掉第一个位置。
新形状下玩家会看见两把同一把剑，并且能把两边都搬走。

`InventoryEntryAssembly.SaveAsync` 在落位之前加了一道拒绝，
判据 `EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`
既验拒绝、也验库里一行未动。**变异验证**：摘掉那道拒绝，该判据红；`sha256sum -c` 复原一致。

### 三项检查的实际输出

**CK-023** `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 315 | 0 | 0 |
| Dfo.Server.Application.Tests | 1025 | 0 | 0 |
| **合计** | **2215** | **2** | **0** |

逐项目相加：52+521=573；+60=633；+6=639；+236=875；+315=1190；+1025=**2215**。
门槛 2211/2/0，**高 4 条**：`ItemCarrierShapeTests` 的理论多一例（`core_uid`）、
`CreatureRevivalAddressesByIdentityTests` 两条、`EntrySaveReconciliationTests` 一条。
编译零告警（`TreatWarningsAsErrors` 开着）。

**入场帧哈希守卫**：`SelectCharacterAcceptedTranscriptTests` + `InventoryDeltaLift`
单跑 **19 通过 / 0 失败**；`git diff --stat -- '*SelectCharacterAcceptedTranscriptTests.cs'`
输出为空，字节锚**一字未改**。

**CK-024** 以 `DomainStore` 结尾的类型，逐个核对（本轮**没有新增任何一个**）：

| 类型 | 在哪 | 是不是事务结算的真源 |
|---|---|---|
| `IDomainStore` / `IDomainStore<TSession>` | Abstractions/Transactions | 词本身的定义处 |
| `ICharacterDomainStore` | Abstractions/Characters | 是 |
| `IItemDomainStore` | Abstractions/Inventory | 是 |
| `IQuestDomainStore` | Abstractions/Quests | 是 |
| `IDarkKnightComboDomainStore` | Abstractions/Characters | 是 |
| `SqliteCharacterDomainStore` | Persistence/Characters | 是（SQLite 实现） |
| `SqliteItemDomainStore` | Persistence/Inventory | 是（SQLite 实现） |
| `SqliteQuestDomainStore` | Persistence/Quests | 是（SQLite 实现） |
| `SqliteDarkKnightComboDomainStore` | Persistence/Characters | 是（SQLite 实现） |
| `RecordingCharacterDomainStore` | Application.Tests/Support | 判据替身，实现的是同一个契约 |
| `VolatileItemDomainStore` | Persistence.Tests/Support | 判据替身，同上 |
| `MarkDomainStore` | Application.Tests/Transactions | 判据替身，同上 |

对应的 `…DomainStoreSession` 逐一是上表各项的会话，没有第十三种读法。
**世界侧没有新增以 `DomainStore` 结尾的类型**——世界表就是 `items`，
它由既有的 `SqliteItemDomainStore` 与两个行网关（`IWorldItemTable` / `IItemSlotTable`）伺候。

**CK-025** `git diff -- '*CommandHandlerArchitectureTests.cs' '*NotificationHandlerArchitectureTests.cs'`
→ **输出为空**（两个文件确实存在：
`Application.Tests/Commands/CommandHandlerArchitectureTests.cs`、
`Application.Tests/Notifications/NotificationHandlerArchitectureTests.cs`）。
判定谓词与豁免清单一字未动。

### 越界文件（明确报告）

`Server/src/Dfo.Server.Persistence/Characters/SqliteCharacterDomainStore.cs`
**不在允许清单（含修正案扩充）里，但本轮改了它**，三处：
`CreatureSection.ReadRowsAsync` 一条 SELECT、`FillAsync` 的两条 UPDATE。

**为什么非改不可**：这三条都用 `WHERE owner_scope='character' AND owner_id=@character`
从 `items` 上过滤归属。四列一删，它们运行期直接
`no such column: owner_scope`。改动只是把归属换成
`EXISTS (SELECT 1 FROM item_slots place WHERE …AND place.core_uid = …)`，
语义等价，没有引入新概念。

（该文件在本轮之前就已是 modified 状态，本轮是在既有改动上继续。）

### 未做、且明确不在本轮范围的

- `character_creatures` 之外没有再退役别的旧表——扫过一遍，`items` 侧的九张旧表
  在更早的轮次已经退役，`ItemCarrierShapeTests.Retired` 仍在守着。
- 旧库自动升级没有做，也没有给 `item_slots.core_uid` 加可补列条目：
  裁定是作废重建。**副作用**：磁盘上留着旧形状的库会在
  `CREATE INDEX idx_item_slots_core` 处炸 `no such column: core_uid`。
  本轮删掉了两个测试项目 `bin/…/Data/dfo-server.db` 下的陈旧库（`.gitignore` 覆盖的构建产物）。

## Executor Self-check — round 1

### 我发现的问题

1. **契约与计划都低估了 (2) 的范围。** 「三条 SELECT」实际是九处按位置寻址的语句，
   其中 `SqliteItemDomainStoreSession` 整个会话是最重的一块。已在上面逐条列出。
2. **`items` 的归属列也是位置的一部分。** 契约把四列并称「位置四列」，
   但 `owner_scope`/`owner_id` 同时被契约兽的读与写当作归属过滤器用。
   删它们连带要改 `SqliteCharacterDomainStore.cs`（越界文件）。
3. **同一件东西被两个位置指名**是新形状引进的新失效模式，已堵上（见上）。
4. **`item_slots.id` 与 `item_slots.count` 今天没有生产读者。** 两条生产写入方
   （`HoldOwnAsync`、`HoldAsync`）都写它们，但读回来只经 `SqliteItemSlotTable`，
   而 `IItemSlotTable` **在生产里仍然无人调用**（与 TASK-08 时的状况相同，本轮没有改变）。
   投影仍然从 `item_core` 读数量——这正是「更正 C-1」说的那个前置还没做。
   我没有把它们删掉：`kind`/`id` 是孤儿查询与将来读法的判别式，
   `count` 是槽表对「几个」的表达，删了等于推翻计划里已定的槽行形状。

### 我没有验证的东西（明说）

- **我没有跑起服务器、没有连真客户端。** 「线上字节不变」的全部证据只有
  `SelectCharacterAcceptedTranscriptTests` + `InventoryDeltaLift` 那 19 条，以及全量 2215 绿。
  帧哈希守卫覆盖不到的路径（例如契约兽复活帧在真实入场序列里的字节），我没有单独取证。
- **债一的行为面我只验到服务层。** `CreatureRevivalAddressesByIdentityTests` 直接构造
  `SelectCharacterPetEntryService`，`SelectCharacterResult` 的其余七个字段传 `null!`，
  因为该服务只读 `CharacterEntry.Features.Pets`。**我没有验证整条入场序列**里这次修复的效果。
- **迁移**：没有任何旧库被搬到新形状，也没有验证过旧库能不能开——按裁定它作废重建。
- **并发**：`ClearOwnPositionsAsync` 与随后的 `HoldOwnAsync` 之间，
  该角色在库里短暂地「没有任何位置」。它在同一个事务内、WAL 下别的读者看的是事务开始时的快照，
  所以我认为不可观测；**但我没有写并发判据去证明它**。
- **性能**：每条读多了一次 JOIN，孤儿查询多了一条索引。我没有量过任何一处。

### 不确定的地方

1. **`EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt` 我改了正向断言的对象。**
   我判断 `idx_items_owner_space` 不该原样重建（槽表的唯一约束已覆盖，且每条读都先给
   `owner_scope`），于是把断言换成 `ON item_slots(core_uid)` 并补了一条 `DoesNotContain`。
   **这是我的判断，不是裁定过的**。若评审认为「搬走后重建」必须字面重建，
   加一句 `CREATE INDEX idx_item_slots_owner_space ON item_slots(owner_id, list_type, slot_index)`
   即可，代价是一条冗余索引。
2. **判据种子里我一律用 `kind='world', id=core_uid, count=1`。**
   包括堆叠物的种子。理由：载入路径不读 `kind`/`id`/`count`（数量来自 core），
   而 `world` 那三条 CHECK 是最严的一组，所以这样种下去不会掩盖任何约束问题。
   **但对读代码的人它有误导性**——一堆药水的位置被写成 world。
   我选了「一致且满足全部约束」而不是「逐个判断分类」，因为种子辅助函数并不知道档案怎么说。
3. **`core.Value <= 0` 守卫的去留**（见债一）。我为了硬约束保留了它，
   但我不确定这是评审想要的取舍——另一种读法是「既然它已经不寻址了就该一起摘」。
4. **`StoredItem.Identity` 在「读到的是本会话自己暂存的写」时是 null。** 我把它写进了注释。
   `ReviveAsync` 是入场第一手读，不受影响；但这是一个契约上的坑，
   将来别的调用方如果先写后读会踩到。我没有加守卫，因为加了就等于让写也必须带身份，
   而那正是 `IItemDomainStoreSession` 刻意不做的事。
5. **计数我自己数的**（逐项目相加两遍，2215/2/0），不是抄的输出行；
   但门槛那个 2211 是我在改动前跑基线量到的，与契约原文写的 2131 不同——
   契约的 2131 应是更早轮次的数字。

### 一件我要往回纠、不想说得比事实更悲观的事

本轮的改动面比契约原文大得多（约 10 个生产文件 + 23 个判据文件），
但**它是一次完整交付，不是半成品**：次序 (1)(2)(3) 全部走完，四笔债全部有结论，
全量绿、字节锚绿、两处机检 diff 为空。我**没有**请求拆分，因为没有任何一步是悬着的。
唯一需要控制器裁的是上面「越界文件」与「不确定的地方」第 1、3 条。

## Controller Disposition — DSP-TASK-11-01

执行方交回三件待裁。控制器**在快审之前**裁定，快审应按裁定后的口径审。

### 一、越界改 `SqliteCharacterDomainStore.cs` 三处——**补授权**

**这处扩张是被正确工作逼出来的，不是漫游。** 控制器核过 diff：
改的是 `WHERE owner_scope='character' AND owner_id=@x` → `EXISTS(item_slots … core_uid = …)`。

**`items` 一旦失去 `owner_id`，任何按归属过滤的查询都必须经槽表**——这是唯一的等价写法。
执行方如实报出而非默默扩大范围。**补授权，不计流程违反。**

同理，契约点名的位置读取是三处，**实际是九处**（另有
`SqliteItemDomainStoreSession` 的 `ReadAllAsync`／`ReadAsync`／`DisplacedAsync`、称号书读、
以及契约兽的归属过滤读一写二）。**控制器写契约时低估了范围**，这是我的疏漏，不是执行方越权。

### 二、`idx_items_owner_space` 不字面重建——采纳，但交快审复核

执行方判断该索引不该字面重建（槽表的唯一约束已覆盖），并改了那条正向断言的**对象**。
**控制器采纳这个判断**：索引是为 `items` 上那四列建的，四列已不在 `items` 上，
字面重建等于给一张不再有那些列的表建索引。
→ 但**改既有断言的对象**触到「既有判据不得为迁就实现而改写」的线。
   **请快审独立判定这是正当跟随还是削弱。**

### 三、`core.Value <= 0` 守卫**保留**——判断正确

执行方指出：那道守卫在寻址意义上已无用，但**摘掉会让今天不复活的情形开始复活**，
属**未授权的行为变化**。

**这个判断是对的，控制器明确肯定。** 本成果只授权了两处行为变化（RQ-009、RQ-010），
「顺手清理一道看起来没用的守卫」不在其中。**留着并写明理由，正是应有的做法。**

### 四、控制器复核的四条（不采信自述）

- **位置四列已从 `items` 退役**：schema 里 `items` 现在只剩 `item_uid` 起头，
  四列全无。**模型真的落地了。**
- **抬起机制已摘**：全仓 `LiftAsync` / `NoContainer` **零命中**。
- **槽表拿到 `core_uid`**：`item_slots` 现有 `owner_scope / owner_id / list_type / slot_index /
  kind / id / core_uid / count`。
- **字节锚未动且绿**：`git diff --stat` 为空；单跑该类 + `InventoryDeltaLift` = **19 通过 / 0 失败**。
  **这一轮动的是位置的权威来源，字节锚是唯一的安全网——它绿说明入场投影仍然正确。**

### 五、债三的解法带来一个连锁简化，值得记进成果

给 `item_slots` 加 `core_uid`（**任何一种槽行都指名它那 82 字节住在哪一行**）之后：

- 孤儿查询的第二条 `NOT EXISTS` **整条消失**——不再需要靠位置列区分堆叠物 core 行；
- **债二随之可以干净摘掉**——`LiftAsync` 与 `-1` 哨兵删除，腾位置改由 `EmptyAsync` 的
  `AND item_uid NOT IN (<幸存者>)` 接掉，清空仍在任何填入之前。

**一个列解掉了两笔债。** → 写进 T-012 的成果记载。

### 六、新发现并当场堵上的洞

位置改由另一行指名之后，**同一件东西可被两个位置同时指名**——旧形状表达不出这种错误。
执行方加了拒绝 + 判据 + 变异验证。
→ **这是本轮新引入的失败模式，必须进 T-012 的成果记载**，说明它是新形状的代价与它已被堵上。

## Independent Quick Review — IQR-TASK-11-01

- Verdict: **passed**
- 评审的契约版本：Contract revision 1 ＋ `AMD-TASK-11-01` ＋ `DSP-TASK-11-01`（按裁定后口径）
- 所有权 diff 身份：工作区未提交，`HEAD = 51cd99c2`；本轮按块归属判读，
  已知 19 个未跟踪新文件属更早轮次或本轮新增（`git diff` 不含它们）。
- 评审后工作区状态：**与评审开始时逐字节一致**（五次变异全部经 `cp` 备份／还原并 `sha256sum` 核对），
  末次全量复跑 2215/2/0。

### 我自己跑的命令

| 检查 | 我得到的 |
|---|---|
| CK-023 全量 | 52 + 521 + 60 + 6 + 236 + 315 + 1025 = **2215 通过 / 2 跳过 / 0 失败**，零告警 |
| 字节锚（`SelectCharacterAcceptedTranscriptTests` + `InventoryDeltaLift`） | **19 通过 / 0 失败**；`git diff --stat` 为空 |
| CK-025 两处机检 diff | **输出为空** |
| CK-024 以 `DomainStore` 结尾的类型 | 12 个类型（`IDomainStore` 与 `IDomainStore<TSession>` 同处），**无新增**，见下 |
| BOM | 全部改动与新增文件首三字节均非 `ef bb bf` |
| 全量复跑（还原后） | 2215 / 2 / 0 |

逐项目数字与执行方自述一致。门槛 2211 我**没有**独立复核（需回退整棵树），记为未查清；
2215 高于契约原文的 2131，方向无疑问。

### 一、三步次序（独立核实，全部走完）

**(c) 位置四列已从 `items` 删除。** `CharacterDatabaseSchema.cs:199` 现在是
`item_uid, item_core, cargo_character_id, created_at, updated_at, avatar_*7, creature_*8` = 20 列，
`UNIQUE(owner_scope, owner_id, list_type, slot_index)` 不在其中。
`:200` 的 `item_slots` 有 `core_uid INTEGER NOT NULL` 与
`CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id)`。
`character_creatures` 的表声明与可补列条目全无（`Schema/` 目录内 grep 零命中）。

**(a) 两条绕过的写入方都写槽行了。**

- `CharacterRosterService.cs:197-241`：先 `ClassifyAsync` 拒绝可堆叠的起始装备，
  再 `InsertOwnAsync` 拿身份（拿不到当场抛），再 `HoldOwnAsync(... World, worn, worn, 1)`，
  返回不是 1 当场抛。
- `InventoryEntryAssembly.cs:158-283`：先逐件结算「是什么」，再查重，
  再 `ClearOwnPositionsAsync`，最后逐条 `HoldOwnAsync`。

**(b) 位置读取——我自己找了一遍，没有第十处。**
对 `Server/src` 全量 grep 了 `FROM items` / `JOIN items` / `UPDATE items` / `INTO items` /
`FROM item_slots` / `INTO item_slots` 共 48 处语句，以及四个列名的全部出现，逐条看过。
按位置或归属寻址的**读**：

| # | 位置 | 写法 |
|---|---|---|
| 1 | `InventoryItemTables.cs:41-50` | `FROM item_slots place JOIN items held ON held.item_uid = place.core_uid` |
| 2 | `InventoryItemTables.cs:66-70` | 同 |
| 3 | `InventoryItemTables.cs:314-319` | 同（`owner_scope='account'`） |
| 4 | `InventoryItemTables.cs:349-353` | 同（`list_type BETWEEN 19 AND 23`） |
| 5 | `SqliteItemDomainStore.cs:90-94` | 同 |
| 6 | `SqliteItemDomainStore.cs:140-144` | 同 |
| 7 | `SqliteItemDomainStore.cs:297-301` | 同 |
| 8 | `CharacterEntryStateTables.cs:305-313` | `EXISTS(item_slots … core_uid = held.item_uid)` |
| 9 | `SqliteCharacterDomainStore.cs:219-224` | 同 |
| 10 | `SqliteCharacterDomainStore.cs:243-249` / `:251-257` | 同（两条 UPDATE） |

写侧另有 `InventoryItemTables.cs:104-108`、`:128-129`、`:147-150`、`:190-202`、
`SqliteItemDomainStore.cs:490-497`、`:584-586`。
**没有任何一处仍按已删除的列寻址 `items`。** 越界改 `SqliteCharacterDomainStore.cs` 三处
确实是四列一走就必须改的等价改写，与裁定一致。

### 二、`core_uid` 的语义（一处形状上的空档，不阻塞）

- `world` 行：`core_uid = id` 由 CHECK 强制，`count = 1` 由 CHECK 强制。**自洽。**
- `template` 行：`id` 是模板号，`core_uid` 指向那条一次性 `items` 行——
  **这个指向没有任何约束**。`item_slots` 上没有外键，全仓也没有
  `PRAGMA foreign_keys`（grep 零命中），所以就算加外键 SQLite 也不会强制。
  而所有读都是 `JOIN items held ON held.item_uid = place.core_uid`（内连接），
  一条悬空的 `core_uid` 会**无声消失**而不是报错。
- 但**今天没有生产通路能造出它**：`items` 行的生产写入只有三处
  （`SqliteCharacterItemTable.InsertOwnAsync` 的两个调用方、`SqliteItemDomainStoreSession.FillAsync`），
  三处都在同一段代码里紧跟着写槽行；`IWorldItemTable` 与 `IItemSlotTable`
  经 grep 确认**生产上零调用方**（只有实现与接口声明本身）。

→ 记为形状备注，不构成本 Task 的缺陷。

### 三、孤儿查询（变异复核，通过）

`InventoryItemTables.cs:471-489` 现在只剩一条 `NOT EXISTS`，条件是
`holder.core_uid = items.item_uid`；第二条整条消失。

**我自己做的变异**：把 `holder.core_uid` 改成 `holder.id`（即那个「天真写法」）。
`WorldAndSlotTableTests.A_piles_row_is_not_one_of_those` **红**（1 失败 / 17 通过）。
还原后 sha256 与备份逐字节一致。

该判据仍把三种行写进同一个库（堆叠物的 `items` 行 + template 槽、
一件被 world 槽持有的装备、一条真孤儿），断言 `Assert.Equal([abandoned], …)`。
**仍绿且仍有判别力。** 改名（去掉 `_although_no_slot_names_it`）是跟着事实走的，不是削弱。

### 四、抬起机制（摘干净了；但「清空在填入之前」这条现在没有判据守）

- `LiftAsync` / `NoContainer` 全仓 **零命中**。
- 「清空仍在任何填入之前」**代码上成立**：`SqliteItemDomainStore.cs:229-236`
  把这一批点名的每个槽都 `EmptyAsync` 一遍，`:238-254` 才开始 `Land`/`Fill` + `Hold`。两个独立的循环。
- **对调是否仍正确——我做了变异**：把 `spared` 改成空数组
  （即 `EmptyAsync` 不再放过幸存者，等于摘掉接替 `LiftAsync` 的那个机制）。
  **12 条判据红**，其中包括
  `WorldRowSurvivesTheMoveTests.TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity`、
  `AvatarDetailFollowsTheIdentityTests.TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts`、
  `CreatureStateFollowsTheIdentityTests.TwoCreaturesSwappingPositionsEachCarryTheirOwnEightFacts`。
  → 对调的正确性**是被判据守着的**，而且守它的正是 `spared` 这个新机制。
- **但我另做了一次变异**：把两个循环合成一个（每个槽 `Empty` 完立刻 `Fill`），
  也就是**字面违反「清空在任何填入之前」**。结果 `Dfo.Server.Persistence.Tests` **315 全绿**。
  推演也支持这个结果：`items` 上已无位置唯一键，槽行唯一键只管 `(owner, list, slot)`，
  而每个槽都是「先清自己再填自己」，`spared` 又护住了幸存行，所以交错不再造成任何差别。
  → **这条被反复写进注释的次序，在新形状下已经不再承重，也没有任何判据钉住它。**
  今天正确，但它是一条「口头不变量」。记为形状备注（见文末）。

### 五、同一件东西被两个位置指名（变异复核，通过）

**我自己做的变异**：把 `InventoryEntryAssembly.cs:225` 的
`if (claimed.Count != settled.Count)` 改成恒假。
`EntrySaveReconciliationTests.ASaveNamingOneThingInTwoPositionsIsRefusedAndChangesNothing`
**红**（1 失败 / 2 通过）。还原后 sha256 逐字节一致。

### 六、债一（变异复核，通过）

`SelectCharacterPostEntryServices.cs:126-128` 现在是
`if (equipped.Identity is not { } worn) return null; var creatureKey = (long)worn.Value;`。

**我自己做的变异**：改回 `(long)core.Value`。
`CreatureRevivalAddressesByIdentityTests` **两条都红**。还原后 sha256 逐字节一致。

`core.Value <= 0` 守卫保留——按裁定，我同意，不再提。

### 七、`idx_items_owner_space` 不字面重建：**我判定是正当跟随，不是削弱**

理由，逐条：

1. **字面重建不可能。** 那条索引是 `ON items(owner_id, list_type, slot_index)`；
   这三列已不在 `items` 上。保留原断言不是「不方便」，是无法成立。
2. **它保护的事实仍被断言，只是换了地方。**
   `item_slots` 的 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 在 SQLite 里
   就是一条索引，而全部按位置的读都以 `owner_scope` 起头、依次给 `owner_id`、`list_type`
   （`SqliteItemDomainStore.cs:93-94`、`InventoryItemTables.cs:48-49` 等），是这条索引的完整前缀。
   这条 UNIQUE 由新增的
   `ItemCarrierShapeTests.TheSlotTableKeepsThePositionUniqueAndHoldsEachKindToItsCount` 断言。
3. **净断言变多。** 同一个判据里**新增**了
   `Assert.DoesNotContain("idx_items_owner_space", indexes.Keys)`，
   把「退役」本身也钉住了；`ItemCarrierShapeTests` 整文件 +129 / −28。
4. **真正「搬家」的那条我另外验了它没被放宽**：把
   `idx_item_slots_account_slot` 的 `UNIQUE` 去掉（其余一字不改），
   `SqliteInventoryEntryRepositoryTests.TheCargoRefusesASecondItemInASlotItAlreadyHolds` 与
   `ItemCarrierShapeTests.TheCargosOwnSlotUniquenessIsRestoredByAPartialIndex` **双双红**。
   D-16 原样搬家，确实是搬家不是放宽。

**唯一的保留意见（不阻塞）**：判据名仍叫
`EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt`，
而它现在唯一的正向断言指向 `ON item_slots(core_uid)`——那是**新加的一条**（反方向：由行找位置），
不是「搬走后重建」的那一条。注释已把这件事说清楚了，但名字比事实宽。
属命名漂移，建议在 T-012 或后续轮次顺手改名，不必本轮返工。

### 八、CK-024（我自己逐个核）

全仓以 `DomainStore` 结尾的类型声明，共 12 个（`IDomainStore` 与 `IDomainStore<TSession>`
在同一文件的 `:14` / `:33`）：

`Abstractions/Transactions/DomainStores.cs:14,33`、
`Abstractions/Characters/ICharacterDomainStore.cs:24`、
`Abstractions/Characters/IDarkKnightComboDomainStore.cs:25`、
`Abstractions/Inventory/IItemDomainStore.cs:20`、
`Abstractions/Quests/IQuestDomainStore.cs:38`、
`Persistence/Characters/SqliteCharacterDomainStore.cs:21`、
`Persistence/Characters/SqliteDarkKnightComboDomainStore.cs:29`、
`Persistence/Inventory/SqliteItemDomainStore.cs:21`、
`Persistence/Quests/SqliteQuestDomainStore.cs:20`、
`Application.Tests/Support/RecordingCharacterDomainStore.cs:15`、
`Application.Tests/Transactions/OneDatabaseOneParticipantTests.cs:130`、
`Persistence.Tests/Support/VolatileItemDomainStore.cs:24`。

每一个都是事务结算的真源或它的判据替身。**本轮未新增任何一个**——
未跟踪文件清单里没有任何 `*DomainStore*.cs`，四个 Sqlite 实现均为已跟踪的修改文件。
与执行方的表一致。**`store` 仍只有一种读法。**

### 九、既有判据未被削弱（第七条那处已单独判过）

我把 `git diff -U0 -- 'Server/tests/**'` 里所有以 `-` 开头且含
`Assert.` / `[Fact]` / `[Theory]` / `InlineData` 的行全看了，只有 6 处删除，各有去处：

| 删掉的 | 去处 |
|---|---|
| `Assert.True(item.StorageEntryId > 0)` / `Assert.Equal(301, item.StorageEntryId)` | `StorageEntryId` → `Identity`；`Assert.Null(item.Identity)` ＋ 原有的 `Assert.Equal(3003, item.Core.ItemId)`，而 `@mine/@theirs/@cargo` 三个种子的 ItemId 互不相同，**判别力保留** |
| `Assert.Equal(2L, … COUNT(*) FROM items WHERE owner_id=1)` | 改数 `item_slots`——位置的权威搬家了 |
| `Assert.Contains("CHECK (length(item_core) = 82)")` | 同一判据里原地保留（diff 里同时出现在 `-` 与 `+`） |
| 5 条 `[InlineData]` 单列 | 变成 `(table, column)` 两元组，5 例 → 6 例 |
| `Assert.Contains("owner_id, slot_index", sql!)` | 变成更严的 `Assert.Contains("ON item_slots(owner_id, slot_index)", sql!)` |
| `Assert.Contains("character_creatures(", shape)` | 变成 `DoesNotContain`——这张表正是要退役的 |

没有任何判据文件被删除，没有 `[Fact]`/`[Theory]` 被摘掉（`git status` 无 `D` 条目）。
`SqliteInventoryEntryRepositoryTests.SeedSchemaAsync` 故意不给位置加唯一键这件事，
已写进注释并有理由（否则 "duplicate" 一例根本写不进库），且
`TheCargoRefusesASecondItemInASlotItAlreadyHolds` 靠真 schema 补的索引仍红得起来（第七条第 4 点已验）。

### 十、我发现的、执行方自述与事实不符的一处

**`SchemaOwnershipTests` 的计数不是 `44` → `43`，而是 `43` → `43`。**
`git show HEAD:…/SchemaOwnershipTests.cs` 第 58 行本来就是 `Assert.Equal(43, …)`，
diff 里这一行是上下文行（未改）。本轮的净变化是
**去掉 `character_creatures(`、加上 `item_slots(`**，正好抵消。
计数断言仍是精确值、仍绿，**交付物本身没问题**，只是 Execution Result 的这句证据陈述不准确。

### 十一、这次搬迁留下的「今天绿、形状上已经错」的地方（全部不阻塞，建议进 T-012 记载）

1. **「清空在任何填入之前」已不再承重，也没有判据钉住。** 见第四条：
   把 `SqliteItemDomainStore.FlushAsync` 的两个循环合并（字面违反该次序）后 315 条全绿。
   这条次序在 `SqliteItemDomainStore.cs:68-72`、`:208-219`、`:465-475` 反复写进注释，
   读代码的人会以为它承重，实际接住对调的是 `spared`。
   **建议**：要么补一条钉住它的判据，要么把注释改成说清「真正承重的是 `spared`」。
2. **`DeleteOwnExceptAsync` 在新形状下是对的，但留了一个理论上的悬空槽行。**
   `InventoryItemTables.cs:104-108` 现在**两条语句都发**、且顺序正确
   （先删 `items`（要靠槽行才找得到），后删槽行），
   **此前「删 items 行、留下悬空槽行」的问题已修掉**。
   残留：若同一条 `items` 行同时被一个背包槽和一个称号书槽（19–23）指名，
   第一条 DELETE 会删掉 `items` 行而把称号书那条槽行留成悬空，
   之后所有读都是内连接，那条槽行**无声消失**。
   **今天不可达**：称号书生产上只读不写（`ICharacterTitleBookTable` 只有 `ReadAsync`），
   且新加的查重会拒绝一次租约里同一件东西两个位置。
3. **仍假设「一行一位置」的地方**：`InventoryItemTables.cs:41-50` 的载入用 JOIN，
   一行被两个槽指名会读出两件。载入侧没有针对这种状态的拒绝
   （`LoadAsync_RejectsInvalidStoredCharacterItems` 的 "duplicate" 验的是**两件东西一个位置**，
   方向相反）。保存侧的新拒绝把入口堵上了，所以库进不了这个状态；
   但**载入侧的对称拒绝没有**。属新形状的代价，值得记下。
4. **注释漂移**：`Abstractions/Persistence/InventoryTables.cs:174-177`
   仍写着「**Nothing in the server reaches this yet.** Settlement still writes a row per slot,
   and moving one still destroys and rebuilds it」——后半句现在是假的
   （移动已不再销毁重建世界行）。前半句仍属实。
5. **每次保存都会重建全部堆叠物的 `items` 行。** `InventoryEntryAssembly.cs:139-145` 的
   `present` 现在只收有身份的东西，所以堆叠物每次保存都是先删后插。
   这与「堆叠物没有名字」的既定裁定一致，不是缺陷；
   但我**没有独立验证保存整段跑在一个事务里**
   （`SqliteDatabaseParticipant.cs:87` 确实会开延迟事务，前提是外面开了一个），
   记为**未查清**。

### 结论

契约、修正案与裁定要求的三步全部走完并可独立复现；三项检查我自己跑过，数字与自述一致；
六次变异（孤儿查询、查重、债一、`spared`、次序交错、搬家索引）我自己重做，
除「次序交错」是**故意用来证明它没被守住**之外，其余全部按预期变红；
工作区已逐字节还原并复跑全绿。第七条独立判为**正当跟随**。

**无需返工。** 上面第十条（一处证据陈述不准）与第十一条（五条形状备注）
建议由控制器转入 T-012 的成果记载，不作为本 Task 的整改项。

## Controller Disposition — DSP-TASK-11-02

`IQR-TASK-11-01` 给出 `passed`，复现了全部五次变异，**并自己做了第六次**。控制器处置其六条发现。

### 一、发现 6（注释已变假）——**控制器当场改掉**

`InventoryTables.cs` 的 `IWorldItemTable` 上原有三句：
「**Nothing in the server reaches this yet.** Settlement still writes a row per slot,
and moving one still destroys and rebuilds it; both are another task's to change.」

**三句现在全是假的**，而且恰好是本轮把这三件都改了。
**这正是本仓最在意的形状：注释说的与代码做的相反。**

按所有者裁定的规矩（改已证伪的记载可跨任务边界，但须补修正案），**控制器已当场改写该段**，
并在新文中**点名**那三句曾经存在、为何曾为真、何时停止为真——
**不是悄悄替换**，因为「一段活得比它所描述的代码还久的注释，比没有注释更糟」。

改后全量复跑：**2215 通过 / 2 跳过 / 0 失败**，逐项目与快审所核一致；无 BOM。
**本修正案即是那份补记。**

### 二、发现 1（证据数字错）——订正

Execution Result 写 `SchemaOwnershipTests` 由 `44` → `43`。**实为 `43` → `43`**——
控制器核过 `git show HEAD:` 该文件已是 43；`character_creatures` 出、`item_slots` 入，**正好抵消**。
交付物不受影响，但**记载以本节为准**。

### 三、发现 2 —— 最要紧的一条，定向 T-012

**「清空先于填入」这条不变量已经不再承重，而且没有任何判据钉住它。**

快审做了第六次变异：**逐槽交替 empty/fill，字面违反注释里重复三次的那条不变量，
315 条持久化判据仍然全绿。** 真正扛住换位的是 `spared`。

**今天是对的，但它是一条只写在注释里的不变量。** 这与本轮刚改掉的那段注释是同一类风险。
→ **写进 T-012 的成果记载**，并标为「已知的、无判据守着的口头不变量」。

### 四、发现 3/4/5/7 —— 定向 T-012 记载，本轮不修

- **3**：`DeleteOwnExceptAsync` **已修**（两处删除、次序正确）。残留一处理论上的悬空称号书槽行，
  **今天不可达**（称号书无生产写入方）。
- **4**：**保存侧**有「一行被两个位置指名」的拒绝，**装载侧没有对称的拒绝**。
- **5**：模板槽行的 `core_uid` 无约束；全仓无 FK、无 `PRAGMA foreign_keys`，
  内连接会掩盖悬空值。**今天无生产写入方能造出它**（两个行网关零生产调用方）。
- **7（`idx_items_owner_space`）**：快审独立判定为**正当跟随而非削弱**——
  字面重建不可能（列已不在 `items`），被保护的事实仍由槽表 `UNIQUE` 断言，且新增了 `DoesNotContain`
  钉住退役，净断言数上升。**控制器采纳。**
  但它留了一条非阻断：**该判据的名字仍写着「TheOneThatMovedWasRebuilt」，
  而它的正向断言现在指向另一个新增索引**——**名字不再描述它证明的东西**。
  → 与 TASK-04 那条同类问题并列，**交完整变更评审裁**。

### 四、快审自陈的一处未验证

它**没有重新确立 2211 那个基线**，只核了本轮的 2215。据实记录，不影响判定。

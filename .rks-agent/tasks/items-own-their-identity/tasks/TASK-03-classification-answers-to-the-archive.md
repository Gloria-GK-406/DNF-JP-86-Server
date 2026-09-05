# TASK-03 — 分类规则归档案，并证明它在存储值与档案冲突时取档案

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 让「一件模板有没有身份、属于哪一类」由内容档案回答，并把它接成**每一条写入路径都会走的可指认单一处**。它不解析身份、不读库。
- Referenced spec requirements: RQ-006
- Referenced plan sections: T-003
- Owned boundary: BD-005, BD-014
- Permitted files or interfaces: `Server/src/Dfo.Server.Abstractions/Content/`、`Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`、写入路径的接线处、`Server/tests/`
- Dependencies: TASK-02
- Required dependency outputs: 两张表与其行网关
- Acceptance conditions: 分类规则可指认为单一处，且每条写入路径都经过它；存在一条判据，构造存储中记录的种类值与档案解释不一致的物品，断言分类取档案的解释；**该判据经变异验证证明会红**。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-006 `Non-command: 指认分类规则的唯一生效处，并列出每一条写入路径如何经过它`
- CK-007 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-008 `Non-command: 变异验证——把分类改回读存储种类值，记录失败的判据名；还原后复跑`

- Required evidence: 单一生效处的位置；写入路径清单；分类判据名；**变异验证必须指名哪一条判据失败**，不得只说「红了」；还原后全绿，且记录还原前后被改文件的哈希一致。

### 硬性义务

`ItemKind` 今天不可靠：24 槽契约兽入库记 5，而 `PvfEntryLifecycleContentSource.cs:101-102` 对同一件物品给 1。**这是本成果最大的单点风险——分错了不报错、会静默错，而且全量判据仍会全绿**，因为投影照样拼得出线上字节。变异验证是本 Task 的必要交付，不是可选项。

## Execution Result — round 1

### 分类规则的唯一生效处

**`Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` 的 `ClassifyAsync(int itemId, CancellationToken)`。**

它是全仓唯一把「一个模板是什么」变成一个类的地方。签名只收模板号，
**存储中的种类值在类型上够不着它**——这是「不以存储种类值为唯一依据」的机械保证，
不是注释里的承诺。

答案链（全部在允许边界或已报告的接线处内）：

```
ItemPlacementRuleService.ClassifyAsync              ← 唯一生效处（规则服务，Application）
  → IItemPlacementDefinitionSource.GetAsync         ← 内容源契约（Abstractions/Content）
    → PvfItemPlacementDefinitionSource.GetAsync     ← Infrastructure，按模板缓存
      → PvfEntryLifecycleContentSource.ResolveItemMetadataFor
        → ResolveWorldClass / ClassifyEquipmentType ← 读档案自己的 [equipment type]
```

**档案的判据（实测，不是推测）。**
`PvfProbe values equipment/equipment.lst "[equipment type]"` 在 118479 个文件上给出 30 个取值：
`[weapon] 17668`、`[coat avatar] 8391`、…、`[creature] 707`、`[aurora avatar] 423`、
`[support weapon] 26`。于是分类是：

| 档案说的 | `ItemWorldClass` |
|---|---|
| 在 `stackable/stackable.lst` 里（含 `[creature]`/`[feed]` 这类**饲料**） | `Interchangeable` |
| 档案根本没声明 | `Interchangeable`（与既有 `IsEquipmentAsync` 对未知项的读法一致） |
| 在 `equipment/equipment.lst` 里且 `[equipment type]` 以 `avatar]` 结尾（11 个槽名） | `Avatar` |
| 在 `equipment/equipment.lst` 里且 `[equipment type]` 是 `[creature]` | `Creature` |
| 在 `equipment/equipment.lst` 里的其余（weapon/coat/ring/title/artifact/charm/flag/name tag…） | `Equipment` |

`IsEquipmentAsync` 被改写成 `ClassifyAsync(...) is not Interchangeable`，
**取值与改动前逐项相同**（旧读法是 `placement.ItemKind == 1`，而档案给 `1` 的充要条件
就是「在 equipment.lst 里」），这样「这是不是装备」不再是第二份对同一个数的读法。

### 每一条写入路径如何经过它

写入路径的**闭合性**是当场核过的，不是列举：
`grep "INSERT INTO items|UPDATE items|DELETE FROM items"` 在 `Server/src` 只有 3 个落点，
分别属于下面的 P1、P3、P4；`item_slots` 的两处（TASK-02 落地的
`SqliteItemSlotTable`/`SqliteWorldItemTable`）**生产侧零调用方**，
`grep "IWorldItemTable|IItemSlotTable" Server/src` 只命中契约与实现自身。

| 路径 | 落点 | 怎么经过唯一生效处 |
|---|---|---|
| **P1 容器写** | `SqliteItemDomainStore.cs:292` `INSERT INTO items` | `StoreBackedItemContainer.PutAsync`（`ItemContainers.cs:265-283`）是 `IItemContainer.PutAsync` 的**唯一实现**，也是 `IItemDomainStoreSession.Write` 在 `Server/src` 的**唯一调用者**（`grep IItemDomainStoreSession Server/src` 只 4 个文件：本契约、任务侧契约、本文件、SQLite 实现）。它先 `placementRules.ClassifyAsync(core.ItemId, ...)`，再把答案随 `StoredItem.WorldClass` 交给会话。 |
| P1 的上游调用点（全部经 `PutAsync`） | — | `ItemContainerOperations.RelocateAsync/SplitAsync/SwapAsync/MergeAsync/ConsumeAsync`；`StoreBackedItemContainer.LandAsync`（即 `AddAsync` 与 `RefillAsync`，每日补货走这条）；`SelectCardCommand.cs:365`；`GroundPickupSlice.cs:400`；`MoveItemSpaceSlice.cs:332/340/396/404`。 |
| **P2 容器清空** | `SqliteItemDomainStore.cs:270` `DELETE FROM items` | `ClearAsync` 是**移除**，没有内容可分类；本轮不接。 |
| **P3 建号初始装备** | `InventoryItemTables.cs:151` `INSERT INTO items`（`CharacterItemTable.InsertOwnAsync`） | `CharacterRosterService.CreateAsync` 逐件先 `placementRules.ClassifyAsync(piece.ItemId, ...)`，档案判为 `Interchangeable` 的起始件当场拒绝——穿戴槽放一件东西不放一堆。 |
| **P4 入场租约回写** | `InventoryItemTables.cs:123/151`（`UpdateOwnAsync`/`InsertOwnAsync`） | `InventoryEntryAssembly.SaveAsync` 逐行先 `ClassifyAsync(item.Core.ItemId, ...)`；档案判为 `Avatar` 而行不是按时装写的（种类字节不是 8），当场拒绝。 |

`CharacterItemTable.InsertOwnAsync`/`UpdateOwnAsync`/`DeleteOwnExceptAsync` 在 `Server/src` 的
调用方只有 P3 与 P4 两处（grep 实测），所以这四条就是全部。

**P4 今天在生产上够不着，如实记下**：`SaveAsync` 只在租约 `IsDirty` 时被调用，
而 `IEntryInventoryLeaseRegistry.TryMarkDirtyExact` 在 `Server/src` **零调用方**
（只有契约、实现与三处测试）。它仍然接了分类，因为它是一条真实的写入路径，
不是因为它今天在跑。

### 改了哪些文件、每处为什么

**允许清单内：**

| 文件 | 为什么 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs` | 新增 `ItemWorldClass`（4 个成员）；`ItemPlacement` 增加 `WorldClass` **init 属性**（不是位置参数）。用 init 属性是因为位置参数会逼着改约 30 个既有判据里的 `new ItemPlacement(...)`，而那些桩要说的是「哪个带、堆多少」，不是类别；省略即 `Interchangeable`，与「档案没声明的模板」同一个答案。 |
| `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` | 档案侧的分类解析：`RefillItemMetadata` 增加 `WorldClass`；装备索引从 `IReadOnlySet<int>` 改成 `id → 相对路径` 的字典（因为「是哪一类」写在那件装备自己的文件里）；新增 `ReadEquipmentPaths`、`ResolveWorldClass`、`ClassifyEquipmentType`。 |

**接线处（不在清单上，逐条报告）：**

| 文件 | 为什么必须动 |
|---|---|
| `Server/src/Dfo.Server.Infrastructure/Content/PvfItemPlacementDefinitionSource.cs` | 它是 `ResolveItemMetadataFor` 的唯一调用方；装备索引换形状与把 `WorldClass` 传进 `ItemPlacement` 各一处，不改它编译不过。 |
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs` | 分类是**规则服务**的问题（AGENTS「内容源只被规则服务注入」）。放在既有的物品放置规则服务上，而不是另起一个：它已经拥有「这是不是装备」这一读法，另起一个就是同一个档案事实的第二个主人——正是本 Spec 要消掉的形状。 |
| `Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` | **唯一生效处本身。** 实现 `ClassifyAsync`，并把 `IsEquipmentAsync` 改成它的一种读法。 |
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs` | `StoredItem` 增加 `ItemWorldClass? WorldClass { get; init; }`——写入时填、读出时为 null（会话没有档案可问）。这是 P1 把分类**交出去**的载体，也是 T-004 结算要用的那个值。用 init 属性而不是位置参数，正是为了不碰 `SqliteItemDomainStore.cs`（属 T-004）。 |
| `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | P1 接线：`PutAsync` 改为 `async`，先分类再写。 |
| `Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs` | P3 接线：构造器加 `IItemPlacementRuleService`，建号写初始装备前逐件分类。 |
| `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | P4 接线：构造器加 `IItemPlacementRuleService`，回写每行前分类。 |

**判据（`Server/tests/`，允许清单内）：**

| 文件 | 改动 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs` | **新增**，两条用例，见下。 |
| `.../Application.Tests/Inventory/PersistentContainerBoundaryTests.cs` | `Persistent()` 原先给 `placementRules` 传 `null!`；写入现在要分类，改为传一个真的规则服务（桩内容源）。**断言一条没动。** |
| `.../Infrastructure.Tests/Content/PvfEntryLifecycleContentSourceTests.cs` | 两处 `new RefillItemMetadata(100, 2, 65, 120)` 补上 `ItemWorldClass.Interchangeable`。**断言一条没动。** |
| `.../Persistence.Tests/Support/CharacterSide.cs` | `CharacterRosterService` 多一个构造器参数，补一个「每件都是穿戴件」的规则服务。 |
| `.../Persistence.Tests/Support/InventorySide.cs` | `InventoryEntryAssembly` 多一个构造器参数，复用该文件已有的 `MainBandPlacements`/`NoWearables` 桩。 |
| `.../Protocol.Tests/Game/GroundPickup/GroundPickupTests.cs`、`.../Game/DungeonEntry/DungeonEntryCodecTests.cs` | 桩内容源原先只用 `ItemKind: 1` 表达「这是装备」；`IsEquipmentAsync` 现在读类别，所以桩要把它想说的话说出来：`WorldClass = ItemWorldClass.Equipment`。**断言一条没动，期望值一个没改。** |

### 分类判据

`Dfo.Server.Application.Tests.Inventory.ItemClassificationTests`（跑真实档案 + 真实 SQLite store，
`SkippableFact`）：

1. `The_archive_reads_the_contract_creature_as_a_creature_and_not_as_the_row_does`
   ——先钉住「不一致」确实存在：模板 `63000`（`equipment/creature/faras.equ`，
   `[equipment type]` 为 `[creature]`）的档案种类字节是 `1`，而分类是 `Creature`；
   模板 `2680738`（`stackable/.../chn_invitation_secret.stk`）分类是 `Interchangeable`。
2. `A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries`
   ——**AC-007 本体**：把两件「存储种类值与档案解释不一致」的物品经
   `PersistentItemContainer.PutAsync` 写进真实 store，然后从会话里读回暂存的 `StoredItem`：
   - 契约兽模板 `63000` 带着**消耗品**的种类字节 `2` 写入 → `WorldClass` 必须是 `Creature`；
   - 可堆叠模板 `2680738` 带着**装备**的种类字节 `1` 写入 → `WorldClass` 必须是 `Interchangeable`。

   两个方向都写反了，所以任何「照种类字节读」的分类都会在这里翻车。

### CK-006 — Non-command

见上面两节。单一处 = `ItemPlacementRuleService.ClassifyAsync`；四条写入路径 P1/P2/P3/P4，
其中 P2 是移除、无内容可分类，其余三条各自在写之前经过它。

### CK-007 — 全量判据

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

逐项目（按 dotnet 输出逐行抄，未用 `grep -c`）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | **517** | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 282 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Application.Tests | 1019 | 0 | 0 |
| **合计** | **2159** | **2** | **0** |

基线 2157/2/0 → 2159/2/0，多的两条正是本轮新增的分类判据。
`Protocol.Tests` 仍是 **517**，入场 62 帧逐帧 SHA-256 与增量帧哈希一条没变。

（本轮开工前在同一工作区先跑过一次基线，得到 60/52/517/6/282/223+2跳过/1017 = 2157/2/0，与交办一致。）

### CK-008 — 变异验证

**变异点**：`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
`StoreBackedItemContainer.PutAsync` 里，把
`await placementRules.ClassifyAsync(core.ItemId, ...)` 换成照存储种类值读的 switch
（`1 => Equipment, 8 => Avatar, 5 => Creature, _ => Interchangeable`）。

**变异前文件哈希**（sha256）：
`2c382d9f6ec420b1d0d90c0f8f97c7fb147353fa9d8d9e954c57c59b9fe959e3  Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`

**变异后全量**：**恰好一条**用例变红，全名：

```
Dfo.Server.Application.Tests.Inventory.ItemClassificationTests
    .A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries
```

失败信息：`Assert.Equal() Failure: Values differ / Expected: Creature / Actual: Interchangeable`
（`ItemClassificationTests.cs:122`）。

变异时逐项目计数：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 282 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Application.Tests | 1018 | 0 | **1** |
| **合计** | **2158** | **2** | **1** |

**还原后文件哈希**（sha256，与变异前逐字节相同）：
`2c382d9f6ec420b1d0d90c0f8f97c7fb147353fa9d8d9e954c57c59b9fe959e3  Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`

**还原后全量**：2159 通过 / 2 跳过 / 0 失败（逐项目同 CK-007 那张表）。

**其它变异观察**：`ClassifyAsync` 的签名只有模板号，所以「把分类改回读存储种类值」
在唯一生效处**根本写不出来**——变异只能落在调用点（`PutAsync`）。
这本身就是本轮想要的性质，一并记下。

### 本轮未改变的东西

- 线上字节：零改动。`Protocol.Tests` 517 不变；分类的答案今天没有任何一处流进投影或编码器。
- 结算（`SqliteItemDomainStore.cs`）：一个字符没动，属 T-004。
- 契约兽的种类判定结果：没动。档案侧现在**能**答 `Creature`，但没有任何生产代码
  因此改写契约兽的种类字节；那是 T-010。
- 位置四列、`item_slots`、`IWorldItemTable`/`IItemSlotTable`：没动，仍无生产调用方。
- 没有引入新的 `store` 读法，世界侧没有新增以 `DomainStore` 结尾的类型。
- 没有加 BOM：改动的每个 `.cs` 头三字节实测为 `nam`/`usi`。

## Executor Self-check — round 1

### 我发现的问题

1. **`ItemPlacement.WorldClass` 用了 init 属性而不是位置参数，这是一个真实的代价。**
   好处是既有约 30 个判据桩不必改。坏处是：**一个桩内容源如果不写 `WorldClass`，
   它就会静默地说「这是一堆可互换的东西」**。这一轮已经撞到过一次——
   `GroundPickupTests` / `DungeonEntryCodecTests` 的桩本来用 `ItemKind: 1` 表达「装备」，
   `IsEquipmentAsync` 改读类别之后它们就红了，我补了 `WorldClass = Equipment`。
   **这类沉默在将来还会发生**，只是下一次未必有判据接住。生产侧不受影响
   （唯一的生产内容源 `PvfItemPlacementDefinitionSource` 总是显式填）。

2. **P3、P4 的接线是「守卫」而不是「消费」。** P1 把分类**交出去**（`StoredItem.WorldClass`），
   有真正的下游（T-004）；P3/P4 只是在写之前问一遍并在不一致时拒绝，
   它们写进 `items` 的内容里没有分类的位置。要让它们也交出去，
   得改 `ICharacterItemTable.InsertOwnAsync/UpdateOwnAsync` 的签名，
   那是 BD-003/BD-004（TASK-02 与 T-011 的边界），我没有越过去。
   **如果评审认为「经过」必须是「消费」而不是「守卫」，这两条要退回控制器重新划边界。**

3. **`StoredItem.WorldClass` 今天没有消费者。** 我在类型上写明了它是写入时填、读出时为 null，
   以及 T-004 才会用它。这是刻意的：分类与它的消费者一起落地的话，
   两者都无法单独被判据钉住。但它确实是一处「暂时没人读的字段」，请按这个前提评审。

4. **P4（入场租约回写）今天生产上够不着。** `TryMarkDirtyExact` 零生产调用方，
   所以 `InventoryEntryAssembly.SaveAsync` 只有测试在跑。我照样接了它，
   但它的守卫在今天的判据里**从未触发过**——见下面「没有验证的东西」。

### 我没有验证的东西（明说）

- **P3 的守卫从未在真实档案下触发过。** `CharacterSide` 的桩把每件起始装备都答成
  `Equipment`，`CharacterLifecycleServiceTests` 走的是 Application.Tests 里的真实档案路径
  但那里的起始装备都是真装备，所以拒绝分支没有被任何用例走到。
  **我没有为「档案判为 Interchangeable 的起始件被拒绝」写判据。**
- **P4 的守卫同样从未触发。** 所有走到 `SaveAsync` 的判据都用合成模板号，
  档案答 `Interchangeable`，蕴含式空真。**我没有为它写判据。**
  这两条守卫因此是「接线是真的、判据是空的」——变异验证也不会因为删掉它们而变红。
- **`Avatar` 这一类没有被任何判据钉住。** 我用探针实测了 11 个 `* avatar` 槽名的分布，
  但新增的两条用例只覆盖 `Creature` 与 `Interchangeable`。
  时装分类正确与否本轮无人验证。
- **性能没有量。** `ResolveItemMetadata` 现在对每个装备模板会多打开一次它自己的 `.equ`
  读 `[equipment type]`。`PvfItemPlacementDefinitionSource` 按模板号缓存，所以每个模板至多一次；
  但入场一次性读整个库存时会有一串首次未命中。全量判据时间没有明显变化
  （Application.Tests 15s → 15s），但我**没有专门测过入场路径的耗时**。
- **`IsEquipmentAsync` 的等价性我是推理出来的，不是穷举出来的。**
  旧读法 `placement.ItemKind == 1`；档案给 `1` 的唯一出处是
  `ResolveItemMetadata` 里「不在 stackable.lst、在 equipment.lst」那一支，
  而新读法是同一支上的 `!= Interchangeable`。代码上是充要的，
  但我**没有对全部 118479 个装备模板逐个跑过两种读法比对**。

### 不确定的地方

- **「唯一生效处」这个说法的强度。** 分类的**决定**确实只有一处；
  但**调用点有三处**（P1/P3/P4）。我按 CK-006 的措辞理解为
  「规则单一 + 每条写入路径都经过」，而不是「全仓只有一个调用点」。
  如果控制器要的是后者，本轮的形状不满足，需要重新划。
- **把 `ClassifyAsync` 挂在 `IItemPlacementRuleService` 上而不是新建
  `IItemClassificationRuleService`。** 我的理由是避免同一个档案事实有两个主人
  （`IsEquipmentAsync` 已经在那里）。反对意见是「分类」与「放置」是两个主题。
  这是一次判断，不是唯一解。
- **`[equipment type]` 缺失时答 `Equipment` 而不是 `Interchangeable`。**
  理由写在代码注释里：在 equipment.lst 里这件事本身就说明「两件能分辨」，
  用一个标签的缺席把一件装备变成一堆是更坏的错。**我没有统计有多少模板缺这个标签**
  （探针的 `values` 输出里没有 `(absent)` 行，说明至少在它扫到的范围内都有，
  但那条命令扫的是 `[equipment type]` 之后的第一个 token，不能当作「无一缺失」的证明）。
- **契约兽饲料被归为 `Interchangeable`。** `[stackable type]` 为 `creature`/`feed` 的东西
  在旧代码里拿到种类字节 7，我把它们判为可互换的堆。理由是饲料是契约兽**吃的东西**、
  不是契约兽。这与「`Pet=7` 容器装什么」那条未决项相邻，**如果后来证明 7 号容器
  装的是有身份的东西，这条要回来改**。

## Controller Disposition — DSP-TASK-03-01

执行方交回三点。控制器逐条裁定如下。**这些裁定在快审之前作出，快审应按裁定后的口径审。**

### 一、「守卫」算不算「经过」——算，不需要重划边界

执行方问：P3／P4 的接线是**问过分类并在不一致时拒绝**，但没有把分类结果**写进** `items`；
若「经过」必须是「消费」，这两条要退回重划边界。

**裁定：不需要。** RQ-006 的原文是

> 分类规则是显式的、可指认的，且是**每一次写入都会走的那一条**……它不以存储中记录的种类值为唯一依据。

它要求的是这条规则**被走过**，不是它的结果**被存下**。CK-006 的措辞同样是「每条写入路径如何**经过**它」。
**守卫就是走过。**

而「把分类结果带进写入内容」正是 **T-004 Step 1** 的原话——
「结算时按分类决定：有身份的物品在世界表落一行并在槽表引用它」。
让 T-003 去做那件事等于把 T-004 提前，且要动属 BD-006 的结算文件。**执行方守住边界是对的。**

### 二、越出允许清单的接线文件——认可

我在派工里写的是「若这需要改上面没列出的文件，**先在 Task 文件里写明是哪个、为什么，并在返回时明确报告**」。
执行方照做了，逐条给了理由。**这是被授权的路径，不是越界。**

### 三、`StoredItem.WorldClass` 今天无消费者——认可，但要盯住

它写入填、读出为 null，是留给 T-004 的前置。用 init 属性避免碰属 T-004 的结算文件，这个取舍对。
**但它是一个「填了没人读」的半状态**，T-004 必须把它接掉；若 T-004 结束时它仍无消费者，
那就是一处应当被摘掉的死字段——**记在这里，交给 T-004 的快审核。**

### 四、交给快审独立判断的一件（控制器不预先裁定）

执行方自陈：**那两条守卫今天从未被任何判据触发**（P4 的 `SaveAsync` 生产上够不着，
`TryMarkDirtyExact` 零生产调用方），且 `Avatar` 这一类没有判据钉住。

这是诚实交代，但控制器不替快审判断它是否构成本 Task 的缺陷。**请快审独立回答**：
一条生产上不可达的写入路径，给它加分类守卫是否构成 RQ-006 意义上的「写入路径都经过」的证据？
以及 `Avatar` 无判据是否是本 Task 应当补的缺口。

## Independent Quick Review — QR-TASK-03-01

- Verdict: **passed**
- 评审的合同修订：Contract revision 1，含控制器裁定 `DSP-TASK-03-01`（按裁定后的口径审）
- Owned diff identity：15 个改动文件的 `git diff` sha256 = `4cc2577b066643b0c06dae18dc940c54372e78c011d5b9120f37a88ba7eea42a`；
  新文件 `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs` sha256 =
  `035e5f6aef720410782a5fc316b4fa791b46f153809b836ab59ecb55c8649e2c`。
  TASK-02 的六个文件（`CharacterDatabaseSchema.cs`、`InventoryItemTables.cs`、`InventoryTables.cs`、
  `ItemCarrierShapeTests.cs`、`SchemaOwnershipTests.cs`、`WorldAndSlotTableTests.cs`）不计入本轮。
  `Docs/` 下四个未跟踪 md 的 mtime 均早于本 Task 开工（最晚 `ItemWorldModel.md` 08:55，
  Task 文件建于 09:39），不属本轮改动。

### 我实际跑过 / 看过的

| 项 | 做法 | 结果 |
|---|---|---|
| 唯一生效处 | 读 `ItemPlacementRuleService.cs:44-56` 与 `IItemPlacementRuleService.cs:35-49` 的签名与实现 | 成立 |
| 写入路径闭合性 | 自己 grep `INSERT INTO items` / `UPDATE items` / `DELETE FROM items`，再逐方法追调用方 | 结论成立，但执行方的计数记载有误（见 F-1） |
| CK-007 | 自己跑一遍全量，逐项目抄行 | 2159 / 2 跳过 / 0 失败 |
| 线上字节 | 同一次输出里的 `Dfo.Server.Protocol.Tests` | **517** / 0 / 0，未变 |
| CK-008 变异 | 自己改 `PutAsync` 为读 `core.ItemKind` 的 switch，跑全量，再从字节副本还原并核 sha256 | 复现一致（见下） |
| 既有判据 | 逐个读 6 个既有测试文件的 diff | 无削弱 |
| BOM | 16 个改动文件首三字节 | 全为 `6e 61 6d` / `75 73 69`，无 `ef bb bf` |

**CK-007（我这一遍，`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`）**

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Protocol.Tests | 517 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 282 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 223 | 2 | 0 |
| Dfo.Server.Application.Tests | 1019 | 0 | 0 |
| **合计** | **2159** | **2** | **0** |

与执行方所报逐项目一致。CK-007 门槛（不低于 2131 / 2 / 0）满足。

**CK-008（我自己重做的变异）**

变异前 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` sha256 =
`2c382d9f6ec420b1d0d90c0f8f97c7fb147353fa9d8d9e954c57c59b9fe959e3`（与执行方所记相同）。
我把 `ItemContainers.cs:274-275` 的 `await placementRules.ClassifyAsync(core.ItemId, ...)` 换成
`core.ItemKind switch { 1 => Equipment, 8 => Avatar, 5 => Creature, _ => Interchangeable }`，
其余一字未动，跑全量：

```
[FAIL] Dfo.Server.Application.Tests.Inventory.ItemClassificationTests
       .A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries
Assert.Equal() Failure: Values differ
Expected: Creature
Actual:   Interchangeable
```

逐项目：Protocol 517/0/0、Pvf 60/0/0、Networking 52/0/0、Persistence 282/0/0、
Infrastructure 223/2跳过/0、Host 6/0/0、Application **1018 通过 / 1 失败**。
合计 **2158 / 2 / 1**——**恰好一条红，不是多条**，且正是 AC-007 本体那一条，
失败点 `ItemClassificationTests.cs:122`。与执行方所报逐字相符。

还原（从变异前的字节副本覆盖）后 sha256 仍为
`2c382d9f6ec420b1d0d90c0f8f97c7fb147353fa9d8d9e954c57c59b9fe959e3`，
与变异前逐字节相同；工作区因此与我前一遍 2159/2/0 那次跑处于同一字节状态。

### 逐项核实

**1. 唯一生效处——成立，且是类型层面的成立。**
`Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs:44-56`：

```
public async ValueTask<ItemWorldClass> ClassifyAsync(int itemId, CancellationToken cancellationToken)
{
    var placement = await placements.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
    return placement?.WorldClass ?? ItemWorldClass.Interchangeable;
}
```

方法体里只有模板号与内容源两样东西，**没有任何形参能承载存储中的种类值**——
要让它读种类字节必须先改签名。契约端 `IItemPlacementRuleService.cs:49` 同形。
`Server/src` 里 `IItemPlacementDefinitionSource` 只有一个生产实现
（`PvfItemPlacementDefinitionSource.cs:15`，由 `InfrastructureModule.cs:85` 注册），
所以生产上这条链只有一条。AC-008「可被指认为单一处」我作为独立评审指认成立，EV-004 的那一半满足。

顺带核了执行方自陈「没穷举」的那条等价性：`IsEquipmentAsync` 旧读法是 `ItemKind == 1`；
`ItemKind == 1` 的唯一出处是 `PvfEntryLifecycleContentSource.cs:131-133` 装备分支的
`new RefillItemMetadata(1, 1, 9, 64, ...)`；可堆叠分支的 `ResolveStackableItemKind`
（同文件 `205-223`）返回值集合是 `{7, 9, 10, 4, 11, 3, 2}`，**永不为 1**；
未声明分支给 `SlotEnd < SlotStart` → `PvfItemPlacementDefinitionSource.cs:43` 返回 null →
两种读法都为 false。所以等价性在代码层是充要的，不只是推理。

**2. 写入路径闭合性——结论成立。** 我自己的 grep 与逐方法追调用方：

| `items` 上的写语句 | 属谁 | 是否经过分类 |
|---|---|---|
| `SqliteItemDomainStore.cs:292` INSERT | 会话结算 | 是。`session.Write(` 在 `Server/src` 唯一的物品落点是 `ItemContainers.cs:276`，其上方 `274` 先 `ClassifyAsync`；`IItemContainer.PutAsync` 在 `Server/src` 的唯一实现是 `ItemContainers.cs:270` |
| `SqliteItemDomainStore.cs:270` DELETE | `ClearAsync` | 移除，无内容可分类 |
| `InventoryItemTables.cs:151` INSERT（`InsertOwnAsync`） | 调用方仅 `CharacterRosterService.cs:214`、`InventoryEntryAssembly.cs:175` | 是（`CharacterRosterService.cs:203-204`、`InventoryEntryAssembly.cs:156-157`） |
| `InventoryItemTables.cs:123` UPDATE（`UpdateOwnAsync`） | 调用方仅 `InventoryEntryAssembly.cs:171` | 是（同上） |
| `InventoryItemTables.cs:86/88` DELETE（`DeleteOwnExceptAsync`） | 调用方仅 `InventoryEntryAssembly.cs:136` | 移除，无内容可分类 |
| `InventoryItemTables.cs:325/396/418`（`SqliteWorldItemTable` 的 INSERT/UPDATE/DELETE） | TASK-02 落地 | `grep -rn "IWorldItemTable" Server/src` 只命中 `InventoryTables.cs:181` 契约与 `InventoryItemTables.cs:295` 实现，**零调用方**；留给 T-004 |

**没有第四条带内容的写入路径绕过分类。** 闭合性成立。

**F-1（记载缺陷，不阻断）**：执行方写的「grep ... 在 `Server/src` 只有 3 个落点，
分别属于下面的 P1、P3、P4」不可复现——同样的模式实测 **9 行 / 8 条写语句 / 3 个类型**，
且那 3 个类型中的 `SqliteWorldItemTable` 并不属于 P1/P3/P4。同一段还把
`SqliteWorldItemTable` 与 `SqliteItemSlotTable` 一并说成「`item_slots` 的两处」——
`SqliteWorldItemTable` 写的是 `items` 不是 `item_slots`。清单另漏了
`DeleteOwnExceptAsync`（与 P2 同类的移除）。**结论未受影响**（我已独立复核成立），
但 Required evidence 的「写入路径清单」这一项记载与实际不符，
建议按上表把清单补正后再进最终评审，以免这条记载后被当作既定事实引用。

**3. 既有判据没有被削弱。** 逐个读过 6 个既有测试文件的 diff：

- `PersistentContainerBoundaryTests.cs:91-112`：`null!` 换成真规则服务 + 两个私有桩；断言零改动。
- `PvfEntryLifecycleContentSourceTests.cs:24/32`：`RefillItemMetadata` 多一个位置参数，
  两处补 `ItemWorldClass.Interchangeable`；同文件 `ResolveStackableItemKind` 的四条期望值一字未动。
- `CharacterSide.cs:46-69`、`InventorySide.cs:207`：只加构造器实参与桩；无断言。
- `DungeonEntryCodecTests.cs:527-530`、`GroundPickupTests.cs:302-307`：
  桩从 `ItemKind: 1` 补上 `WorldClass = Equipment`，把桩原本要说的话说清楚；
  两文件的断言与期望值一个没改。

**没有任何既有断言被削弱、删除或放宽。**

**4. 我额外查的一件（执行方与控制器都没提）：P3 的守卫会不会在生产上炸掉建号。**
`CharacterRosterService.cs:205-210` 新增的是一条 `throw`。它读的起始装备来自
`PvfInitialEquipmentDefinitionSource.Read`（`:76-137`），而那里**不按 `equipment.lst` 过滤**——
`[create equipment list]` 里任何带可识别槽名的正整数都会成为一件起始装备。
若某职业的起始件不在 `equipment.lst` 里，分类就是 `Interchangeable`，建号将抛
`InvalidDataException`，这是本轮**新引入的失败模式**，而没有任何判据用真实档案覆盖它
（`CharacterLifecycleServiceTests.cs:33/323` 用的是 `FakeInitialEquipmentSource` 与假仓储，
根本走不到 `CharacterRosterService`；`CharacterSide.cs` 的桩把每件都答成 `Equipment`）。

于是我用 `Tool/PvfProbe` 实测了 `CharacterCreationRuleService.cs:23` 的 `MaxJobId = 12`
所覆盖的全部 13 个职业：起始件去重后是
`27600 / 29900 / 31300 / 33600 / 35001 / 37002 / 106040042 / 101030474 / 10400 / 12400`
十个模板，**逐个 `lst equipment/equipment.lst <id>` 全部命中**
（如 `27600 -> character/swordman/weapon/katana/kat_damaged.equ`）。
所以在今天的档案上这条守卫不可能触发，**不构成回归**。记在这里是因为它靠档案数据成立，
不靠代码成立：将来 `[create equipment list]` 里出现一个不在装备索引里的号，建号会当场抛。

### 控制器点名要我独立回答的两问

**问一：一条生产上不可达的写入路径，给它加分类守卫，算不算 RQ-006 意义上「每条写入路径都经过」的证据？**

**算。** 理由分两层：

- **「写入路径」是代码结构上的集合，不是今天可达性上的集合。**
  `InventoryEntryAssembly.SaveAsync`（P4）是一段编译进产物、被判据实际执行、
  会写 `items` 行的代码；`TryMarkDirtyExact` 零生产调用方说的是**另一个子系统**
  （租约脏标记）今天没接线，不是这段写入代码不存在。RQ-006 要防的正是
  「新接一条线时它自己又答一遍」的漂移；把 P4 排除在外，等于把
  「哪天租约脏标记接上、那条写入就绕过规则」写进设计里。接了它是对的。
- **AC-008 要的是「被执行」，而 `ClassifyAsync` 的调用是无条件的。**
  `InventoryEntryAssembly.cs:156-157`、`CharacterRosterService.cs:203-204`
  都在循环体最前面无条件 `await ClassifyAsync(...)`，只有其后的 `throw` 分支未被走到。
  「规则被走过」成立；EV-004 对 AC-008 指定的证据形式本来就是「由独立评审指认」，
  不是自动判据。按 `DSP-TASK-03-01` 第一条的口径（守卫即经过），这一条满足。

**但要把代价记准，不要写成「验证过」**：这两条守卫的**拒绝分支**零覆盖，
意味着**把这两条 `throw` 整段删掉，全量判据仍然全绿**——上面的变异验证也钉不住它们
（变异只落在 `PutAsync`）。所以 P3/P4 的接线是「结构上成立、行为上未被判据保护」。
这不构成本 Task 的缺陷（合同没要求为守卫写判据），但它是一处真实的**回归敞口**，
建议随 T-004 把分类真正接进写入内容时一并被判据钉住。

**问二：`Avatar` 无判据，是不是本 Task 应当补的缺口？**

**不是本 Task 必须补的。** RQ-006 的原文是「不以存储中记录的种类值为唯一依据」；
AC-007 要的是「**一件**其存储种类值与档案解释不一致的物品」；EV-004 要的是
「构造**一件**……断言分类结果取档案的解释」。三处都没有逐类要求判据。
本轮交了两件、两个方向（`Creature` ← 写成消耗品字节 2；`Interchangeable` ← 写成装备字节 1），
超过了下限。**不计缺陷，可以留给后续任务。**

不过要说清楚风险落在哪，因为它不是均匀分布的：
`ClassifyEquipmentType`（`PvfEntryLifecycleContentSource.cs:190-199`）的三支里，
`Creature` 与 `Interchangeable` 被真实档案判据钉住了，
**`Avatar` 那一支（`normalized.EndsWith("avatar]")`）是全仓唯一零覆盖的分类分支**——
而它恰好是本轮唯一有行为后果的那个值（`InventoryEntryAssembly.cs:159` 的守卫只对 `Avatar` 生效）。
今天 P4 不可达所以无风险；一旦 T-006 把时装列搬过去，这一支就直接决定行为。
`ClassifyEquipmentType` 已经是 `internal static`，且同一个测试文件
（`PvfEntryLifecycleContentSourceTests.cs:34-37`）已经在直接断言姊妹函数
`ResolveStackableItemKind` 的取值，所以补一行
`Assert.Equal(ItemWorldClass.Avatar, ClassifyEquipmentType("[coat avatar]"))` 几乎零成本。
**这是建议，不是本轮的必要条件**——若不在本轮补，请把它带进 T-006 的派工。

### 其它记下但不阻断的

- `InventoryEntryAssembly.cs:159` 新增的裸字面量 `8`（时装种类字节）。按 AGENTS.md:111-113
  「表达归档或游戏语义的常量……读取点只有一个」，它本该由拥有该主题的规则服务给出；
  但同文件 `140`、`169`、`273` 三处**本来就**是裸 `8`，本轮只是延续了文件里已有的写法，
  不是新开的坏形状。留给 T-012「约定与变更清单」。
- `StoredItem.WorldClass` 今天无消费者——按 `DSP-TASK-03-01` 第三条已认可，
  我复核确认 `Server/src` 里除赋值点外无读点，与「T-004 若仍无消费者即是死字段」
  那条记载一致，无新增意见。
- 边界：改到允许清单外的 7 个接线文件，与 `DSP-TASK-03-01` 第二条认可的那份清单逐项对得上，
  没有第八个；`SqliteItemDomainStore.cs`（属 T-004 / BD-006）确实一字未动。

### 结论

Acceptance conditions 三项——单一生效处可指认、每条写入路径都经过、
判据经变异验证证明会红——我都独立复核成立；CK-007 / CK-008 我都自己重跑并复现；
既有判据无削弱；线上字节 517 未变；无 BOM。**verdict: passed。**
唯一需要携带下去的是 F-1 那处记载补正，以及两问里点明的两处敞口
（守卫拒绝分支零覆盖、`Avatar` 分支零覆盖），二者都不阻断本 Task 的接受。

## Controller Disposition — DSP-TASK-03-02

快审 `QR-TASK-03-01` 给出 `passed`，并**自己重做了变异验证**逐字复现。控制器处置其三项遗留如下。

### 一、F-1 记载缺陷——订正（控制器自核，不采信任一方）

执行方写的「grep 在 `Server/src` 只有 3 个落点，分别属于 P1/P3/P4」**与实际不符**。
控制器实测 `INSERT INTO items` / `UPDATE items` / `DELETE FROM items`：

| 位置 | 持有类型 | 是不是活的写入路径 |
|---|---|---|
| `InventoryItemTables.cs:86`、`:88`（`DeleteOwnExceptAsync`） | `SqliteCharacterItemTable`（`:24`） | **是** |
| `InventoryItemTables.cs:123`、`:151` | `SqliteCharacterItemTable`（`:24`） | **是** |
| `InventoryItemTables.cs:325`、`:396`、`:418` | **`SqliteWorldItemTable`（`:295`）** | **否**——TASK-02 新建、**无生产调用方** |
| `SqliteItemDomainStore.cs:270`、`:292` | `SqliteItemDomainStore*`（`:20` / `:61`） | **是** |

**共 9 行写语句、3 个持有类型**，不是 3 个落点。两处具体错误：
`SqliteWorldItemTable` 被误归入 P1/P3/P4 之一，且被说成写 `item_slots`——**它写的是 `items`**；
清单还漏了 `DeleteOwnExceptAsync`。

**结论不受影响**：三条**活的**写入路径确实都经过分类，第四个持有者今天没有调用方。
但 Required evidence 的「写入路径清单」以本节为准，**不以 Execution Result 里那份为准**。

### 二、带进 T-004 的债：两条守卫今天钉不住

快审的独立回答是「不可达路径加守卫**算**『经过』的证据」——写入路径是代码结构上的集合，
不是今天可达性上的集合，且 `ClassifyAsync` 在两处都是循环体最前面**无条件**执行。控制器采纳。

**但它同时给出了代价，这一条必须带走**：把 P3／P4 那两条 `throw` **整段删掉，全量判据仍全绿**，
变异验证也钉不住它们（变异只落在 `PutAsync`）。**这是真实的回归敞口。**
→ **写进 T-004 的派工：结算接分类时一并钉住这两条守卫。**

### 三、带进 T-006 的债：`Avatar` 是唯一零覆盖的分类分支

快审判定它**不是本 Task 的缺口**（RQ-006 / AC-007 / EV-004 只要求「一件」不一致的物品，
本轮交了两件、两个方向，超过下限）。控制器采纳。

**但风险不均匀**：`Avatar` 是全仓唯一零覆盖的分类分支，**而它恰是本轮唯一有行为后果的值**
（`InventoryEntryAssembly.cs:159` 的守卫只对 `Avatar` 生效）。
→ **写进 T-006 的派工**（时装迁入世界表那一轮，正对口）。

### 四、快审额外查出、两方都没提的一件——记债不修

P3 新增的 `throw` 是一条**新的生产失败模式**，而 `PvfInitialEquipmentDefinitionSource.Read`
并不按 `equipment.lst` 过滤起始件。快审实测 `MaxJobId = 12` 覆盖的 13 个职业，
起始件去重后 10 个模板**全部命中 `equipment.lst`**，所以今天不可能触发。

**但它靠档案数据成立，不靠代码成立。** 换一份档案或加一个职业就可能触发建号失败。
记在这里，**不在本轮修**——修它要动起始装备的定义源，属范围外。

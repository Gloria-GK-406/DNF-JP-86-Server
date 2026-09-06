# TASK-09 — 从地上捡起来的时装也是完整的

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

修掉完整变更评审找到的第 37 条：**一件时装经地面拾取通路入库之后会被弄坏**，
下一次入场装载抛 `Avatar detail N is unavailable.`。
这与 TASK-08 修掉的拖动缺陷**是同一个病的第二个制造者**，今天还活着。

- Referenced spec requirements: RQ-002、RQ-003（`spec.md` 的「发放通路」定义**含地面拾取**）
- Referenced plan sections: 本任务由完整变更评审（`reviews/change-review.md`）的阻断发现新增
- Owned boundary:

BD-010 `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs`（**新增边界**）
BD-004 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`（若修法落在这里）
BD-008 `Server/tests/`（只新增判据）

- Permitted files or interfaces:

- `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs`
- `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
- `Server/tests/` 下的判据
- 因编译需要而必须同步的调用点（若有，逐个在执行结果里列出）

- Dependencies: TASK-02, TASK-08
- Required dependency outputs: 发放通路造出的时装身份与六列事实；`Count` 对非堆答 1
- Acceptance conditions:

1. **一件时装经地面拾取通路入库之后是完整的**：身份与六列事实都在，
   再装载不抛异常。
2. **普通装备经地面拾取的行为不变**：它的实例值仍被带过来。
   （这是本任务最大的回归面——`CarryOverAsync` 那次重写本来是为普通装备做的。）
3. **查明并如实记下 `IItemContainer.PutAsync` 这条通路对时装的一般影响**：
   它恒传 null 的 `AvatarDetail`，因此**任何**经 `PutAsync` 重写一件时装的地方都可能同型出错。
   **逐个列出 `PutAsync` 的生产调用方**，对每个回答它今天能不能碰到时装。
   查不清的写「不足以确定」，**不要推断**。
4. 上述每一条都有判据，且**每条判据都经过一次变异实测证明它不是恒真**。

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；
**禁止任何 git 还原类命令**。不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**若修法需要改动协议帧或任何帧的字节布局，停下并提交阻塞报告。**
**若修法需要放宽「时装必须带六列事实」这条明文要求，停下并提交阻塞报告**——那是改要求。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 或阅读断言**。
- 记法：**不得出现读者无法证伪的全称量词**。只写①观测（附散列与出处）②明写「不足以确定…」。
  **自查不要用词表搜**；判准是逐句问「这句在说一个个例，还是在说一类？」
- **本任务要用「制造者扫」而不只是「判定点扫」**——
  这条缺陷被漏掉的根因正是前者缺席（见 `reviews/change-review.md`）。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-026 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo` | 新判据证明经地面拾取入库的时装再装载不抛异常，身份与六列事实都在 |
| CK-027 | Non-command：备份并核散列后把修复处改回原样，重跑 CK-026，再还原并核散列 | 新判据变红；还原后恢复绿 |
| CK-028 | Non-command：对普通装备经地面拾取的既有行为做变异 | 既有判据仍然承重，本次修复未把它们改成恒真 |
| CK-029 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败，EXIT=0；用例数相对 **2234** 的差额逐项目归因；字节锚仍与 HEAD 逐字节相同（`1424a4a8…`） |

- Required evidence:

- 改了哪些文件、每处改了什么（按成员名说）、**为什么选这个修法**
- 四条检查的实际输出与退出码，用例数增量逐项目归因
- 每条新判据的变异实测：变异前后散列、哪条红了、红在哪一句
- **验收条件 3 的完整结果**：`PutAsync` 的生产调用方逐个列出与判断
- 自查：逐条对照验收条件 1..4

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113 是错误**
- 不新增协议帧，不改任何帧的字节布局
- 架构判据的判定谓词与具名豁免清单**不得放宽或扩充**
- **既有断言的期望值不得为迁就实现而改写**
- 不写读者无法证伪的基数
- 不给 `PvfEquipmentSlots.ByName` 补任何键

---

## 执行结果

### 一、改了哪些文件、每处改了什么（按成员名）

**1. `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs`（BD-010）**

改了一个成员：`PickUpGroundDropCommandHandler.CarryOverAsync`。

- 改前：读回落地那一格之后，无条件写
  `landed.Core with { Value = checked((int)found.Instance), Durability = found.Durability }`。
- 改后：先按 `landed.WorldClass` 分岔，再写一次：

  ```csharp
  var carried = landed.WorldClass is ItemWorldClass.Avatar
      ? landed.Core with { Durability = found.Durability }
      : landed.Core with
        {
            Value = checked((int)found.Instance),
            Durability = found.Durability,
        };

  await container.PutAsync(slotIndex, carried, cancellationToken).ConfigureAwait(false);
  ```

- 同一成员的 `<remarks>` 加了一段，按本仓「Amended；把改前的读法引出来」的写法记下：
  改前对时装做了什么、为什么那样会掉六列（`Survivors` 三趟都不认 → 行被重建 →
  `CarriedAvatarsAsync` 按新 `Core.Value` 回捞落空）、为什么分岔用 `WorldClass` 而不是核心里的
  kind 字节，以及 `IItemContainer.PutAsync` 恒传 null `AvatarDetail` 这条一般缺口
  为什么记在案而不是在写入侧加守卫。
- 文件散列：改前 `e9e2f78ef6a61e25bafb74221e9b2a242297b94aeade32a8dd4ef9d72feda665`，
  改后 `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d`。

**2. `Server/tests/Dfo.Server.Application.Tests/Inventory/PickedUpAvatarStaysWholeTests.cs`（BD-008，新增）**

三条判据。容器是 `PersistentItemContainer`（真 SQLite 库），归档是操作者自己的
`Script.pvf`，地板是运行时自己的（`DungeonRunDomainService.AllocateMonsterDrops`，
死亡切片调的同一个方法），拾取走生产命令处理器 `PickUpGroundDropCommandHandler.HandleAsync`，
每次拾取各自包在一个用例边界里（`InventorySide.InUseCaseAsync`），装载走
`CharacterInventoryEntryDomainService.LoadAsync`。

| 判据 | 它主张什么 |
|---|---|
| `An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts` | 一件时装经地面拾取入库后，入场装载仍然服务它：`ItemId`、`ItemKind` 是 8、`Core.Value > 0`，六列事实齐备（`ExpireDate` 是 `int.MaxValue`、`ClearAvatarId` 0、socket 30 字节全零、两个颜色 0、`DeleteDate` 0） |
| `Two_avatars_off_one_kill_keep_two_identities_of_their_own` | 同一次击杀掉下的两件时装，入库后两个 `Core.Value` **不相等**（先从容器读、后问装载，见下） |
| `A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced` | 普通装备的既有行为：同一次击杀的两件值相同、另一次击杀的那件值不同、且不等于开格时的计数；三件的 `Durability` 都等于地板上宣告的那个 |

- 文件散列（交付态）：`097cea8f0fa10cb63af90090b10b1b9c416de708dfbf11d8c05958d2975aa3cf`。
- 第二条判据在本任务中途改过一次结构：原先先问入场装载再比两个 `Core.Value`，
  变异实测发现装载会先抛，那句比较根本走不到（见第五节 MU-09-1 的第一遍）。
  改成**先从容器读回两个核心并比较、再问装载**，那句才承重。

**没有改**：`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`（BD-004）。
它在本任务的一次变异里被改过又按 `sha256sum` 还原；交付态散列
`8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db`，
与本任务开始时读到的逐字符相同。
没有出现「因编译需要而必须同步的调用点」——四次全量构建都没有报错或警告失败。

### 二、为什么选这个修法

按分量排四条：

1. **边界只允许这两个落点，而更靠上的那两个落点都在边界外。**
   评审给的三个可选落点里，`WriteOneAsync`（要求换 `Value` 的写入交代事实）落在
   `ItemContainers.cs`，`Survivors`／`CarriedAvatarsAsync`（按旧 UID 回捞）落在
   `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`。
   后者**不在本 Task 的 Permitted files 里**。前者在边界内但不成立：
   `WriteOneAsync` 手上只有要写的核心，没有那一格原先站着什么，
   「把六列事实一起写」这件事它拿不到料——料在库里，回捞是持久化那一侧的动作。

2. **被换掉的那一位对时装本来就不该被换。**
   `CarryOverAsync` 带过来的两样东西里，`Instance` 的定义写在 `FloorPickup.Instance` 的
   `<param>` 上：「把这件**装备**与另一件区分开的值」。时装核心里的那一位不是这种值，
   是 `AddAsync` 刚抽的身份，也是 `SqliteItemDomainStore.FillAsync` 查六列事实用的键。
   拿地板上宣告的实例值盖掉它，在语义上是把两种不同的东西写进同一位。
   所以这里不是「给时装开特例绕过缺陷」，是**这条通路本来就把时装认错了**。

3. **`Durability` 留着，因为它不改 `Value`，实测也证明它安全。**
   只改 `Durability` 的写入在 `Survivors` 第三趟 `InPlace && SameThing` 上成立
   （`SameThing` 比 `ItemId`／`ItemKind`／`Value` 三项，都没变），行不被重建，六列由
   `LandAsync` 的 UPDATE 保住。这与 `SelectCardCommand.DeliverAsync` 是同一个形状。
   本任务的第一条判据端到端跑通了这一点（装载服务了六列），所以这不是推演。
   把 `Durability` 也一并去掉会让行为变化超出缺陷本身。

4. **分岔用 `ContainerSlotItem.WorldClass` 而不是核心里的 kind 字节。**
   `WorldClass` 由 `StoreBackedItemContainer.ToSlotItemAsync` 向
   `IItemPlacementRuleService.ClassifyAsync` 问归档要；kind 字节是调用方递进来的，
   `ClassifyAsync` 的 `<remarks>` 明写「调用方可以递任何核心，归档说了算」。
   这与 `ContainerSlotItem.IsPile` 用的是同一个来源，没有新造第二个可以互相跑偏的答案。

**没有触及阻断条件**：`Core.Value` 对时装仍然装身份，「时装必须带六列事实」这条明文要求
一个字也没有放宽——本次修复正是让地面拾取这道门重新满足它。
协议帧与任何帧的字节布局未动：`GroundPickupSlice.cs` 的改动全在 `CarryOverAsync` 的方法体与
它的 `<remarks>` 里，`GroundPickupPayloadCodec` 的三个编码方法一字未动。

### 三、`PutAsync` 生产调用方的完整清单与逐个判断（验收条件 3）

**枚举口径**：`grep -rn "PutAsync" Server/src/ --include=*.cs`，去掉接口声明
（`IItemContainer.cs:155`）、`<see cref>` 引用、以及实现本身
（`StoreBackedItemContainer.PutAsync`，`ItemContainers.cs:375`）。
剩下 **15 个调用点，落在 11 个成员上**。`Server/tests/` 下的测试替身不算生产调用方。

**同型的判据**：`StoreBackedItemContainer.PutAsync` 转调
`WriteOneAsync(slotIndex, core, null, cancellationToken)`，`avatarDetail` 恒为 `null`。
所以一次 `PutAsync` 会不会毁掉一件站着的时装，取决于它**改不改那件时装的 `Core.Value`**：
不改 → `Survivors` 第三趟 `InPlace && SameThing` 或第二趟 `SameContent` 认它，行不被重建，
六列保住；改了 → 三趟全不认，行被删掉重建，而 `CarriedAvatarsAsync` 按**新**的
`Core.Value` 回捞，落空 → 六列 NULL。

| # | 调用方（成员名） | 文件:行 | 写的是什么 | 改 `Core.Value` 吗 | 今天能碰到时装吗 |
|---|---|---|---|---|---|
| 1 | `ItemContainerOperations.RelocateAsync` | ItemContainers.cs:180 | `moving.Core` 原样 | 否 | **能**。TASK-08 的 `A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from` 走的就是它。不改值，`SameContent` 认它，不同型 |
| 2 | `ItemContainerOperations.SplitAsync`（源格） | ItemContainers.cs:193 | `Value = moving.Count - moveCount` | **是** | **不能**（今天）。唯一调用方是 `StoreBackedItemContainer.MoveToAsync`；`DecideMoveAsync` 只在 `moveCount != moving.Count` 时选 Split，而 `moveCount` 被夹到 `moving.Count`，非堆的 `Count` 是 1（`ContainerSlotItem.Count => this.IsPile ? Core.Value : 1`），所以时装走的是 Relocate。这条不可达是 TASK-08 造成的 |
| 3 | `ItemContainerOperations.SplitAsync`（目标格） | ItemContainers.cs:197 | `StackableItemCore.Create(...)` 新核心 | **是** | 同上 |
| 4 | `ItemContainerOperations.SwapAsync` | ItemContainers.cs:213 | `first.Core` 原样 | 否 | **能**。TASK-08 的 `A_drag_of_one_onto_another_avatar_trades_the_two_slots` 走的就是它。不同型 |
| 5 | `ItemContainerOperations.SwapAsync` | ItemContainers.cs:214 | `second.Core` 原样 | 否 | 同上 |
| 6 | `ItemContainerOperations.MergeAsync`（目标格） | ItemContainers.cs:228 | `Value = target.Count + moveCount` | **是** | **不能**（今天，见下「关于堆叠上限的两处读数」）。`DecideMoveAsync` 只在 `displaced.ItemId == moving.ItemId && placement.StackLimit > 1` 时选 Merge |
| 7 | `ItemContainerOperations.MergeAsync`（源格） | ItemContainers.cs:239 | `Value = moving.Count - moveCount` | **是** | 同上 |
| 8 | `ItemContainerOperations.ConsumeAsync` | ItemContainers.cs:277 | `Value = remaining` | **是** | **不能**（今天）。两条独立的理由：(a) 唯一调用方 `ItemUseEffectDomainService.ApplyAsync` 的 `ConsumeFromContainerEffect` 恒以 `ConsumedPerUse = 1` 造出，非堆的 `Count` 是 1，`remaining == 0` 走的是 `ClearAsync` 而不是这个 `PutAsync`（`count <= 0` 则抛）；(b) 实测两个时装模板的 `IItemUseRuleDefinitionSource.GetAsync` 都答 null，所以连消耗效果都不会被计划出来 |
| 9 | `StoreBackedItemContainer.LandAsync`（`OntoExistingPile` 那一支） | ItemContainers.cs:470 | `pile.Core with { Value = topped.Total }` | **是** | **不能**（今天）。经 `AddAsync` 要 `stackLimit > 1`；经 `RefillAsync` 的 `ForDailyRefill` 不看上限，但实测归档的 `[refill item]` 只声明 6 条规则，6 条的 `ClassifyAsync` 都答 `Interchangeable`（见下） |
| 10 | `SelectCardCommand.DeliverAsync` | CardReward/SelectCardCommand.cs:365 | `core with { Durability = piece.Durability }` | 否 | **不同型**。只改耐久，`InPlace && SameThing` 认它。一张卡的面能不能是时装，本任务**不足以确定**（未测），但因为它不动 `Value`，这个问题不影响结论 |
| 11 | `PickUpGroundDropCommandHandler.CarryOverAsync` | GroundPickup/GroundPickupSlice.cs | 改前：`Value` + `Durability` | **改前是** | **能**——就是本任务修掉的那一条。改后对时装只写 `Durability` |
| 12 | `MoveItemSpaceCommandHandler.PutOnAsync`（穿上，写装备容器） | MoveItemSpace/MoveItemSpaceSlice.cs:346 | `piece.Core` 原样 | 否 | **不能**（今天）：实测两个时装模板的 `WornSlotForAsync` 都答 null，`PutOnAsync` 在写之前就以 `NotWearable` 拒掉。**即使可达也不同型**，因为不改值 |
| 13 | `MoveItemSpaceCommandHandler.PutOnAsync`（被顶下来的，写背包） | MoveItemSpaceSlice.cs:354 | `displaced.Core` 原样 | 否 | 同上 |
| 14 | `MoveItemSpaceCommandHandler.TakeOffAsync`（脱下，写背包） | MoveItemSpaceSlice.cs:410 | `piece.Core` 原样 | 否 | **不能**（今天）：要脱下先得穿上，而 12／13 走不通。不改值，不同型 |
| 15 | `MoveItemSpaceCommandHandler.TakeOffAsync`（被顶下来的，写装备容器） | MoveItemSpaceSlice.cs:418 | `displaced.Core` 原样 | 否 | 同上 |

**结论**：15 个调用点里有 7 个会改 `Core.Value`（#2、#3、#6、#7、#8、#9，加改前的 #11）。
其中今天能被一件时装走到的**只有 #11**，其余六个各自被上面写明的那道闸门挡住。
所以本任务选的窄修法在**今天的代码**上是够的——这句话的口径是上表逐行，不是一个全称断言。

**关于堆叠上限的两处读数**（#6、#7、#9 的闸门靠它）：

- **实测**（本任务自造探针，跑操作者的 `Script.pvf`，读完即删）：
  模板 `101550507` 与 `101550511` 的 `ItemPlacement` 都是
  `kind=8 stackLimit=1 band=9..64 worldClass=Avatar`，`IsEquipmentAsync` 答 `True`，
  `WornSlotForAsync` 答 `null`，`IItemUseRuleDefinitionSource.GetAsync` 答 `null`。
  模板 `114000088`（普通武器）是 `kind=1 stackLimit=1 worldClass=Equipment wornSlot=12`。
- **代码级读出**（未做全归档遍历）：`Server/src` 里唯一的
  `IItemPlacementDefinitionSource` 实现是 `PvfItemPlacementDefinitionSource`，
  它经 `PvfEntryLifecycleContentSource.ResolveItemMetadataFor` 取元数据；
  该方法对不在 `stackable/stackable.lst` 里、而在装备索引里的模板返回
  `WornPieceMetadata(...)`，而 `WornPieceMetadata` 的第一个实参是**字面量 `1`**（即 `StackLimit`）；
  另一支（stackable）恒返回 `ItemWorldClass.Interchangeable`。
  也就是说，经这个实现被判为 `Avatar` 的模板，`StackLimit` 从这一处读出来是 1。
  **这是代码级读出，不是对归档里 66,328 个时装模板逐个实测**。
- **实测**（同一探针）：`IEntryLifecycleContentSource.GetDailyRefillRulesAsync` 在
  `preparedAt = 2020-01-01 +08:00` 下答 **6 条**规则，
  itemId 分别是 `2680738`、`2749110`、`10002520`、`10093393`、`10003235`、`690001556`，
  六条的 `ClassifyAsync` 都答 `Interchangeable`。所以 `RefillAsync` 这条路今天送不进时装。

**不足以确定的部分（明写）**：

- 本任务只实测了 **两个** 时装模板（`101550507`、`101550511`）的 `StackLimit`、
  `WornSlotForAsync` 与 `IItemUseRuleDefinitionSource`。**其余时装模板的这三个读数不足以确定**；
  上面那条「经这个实现被判为 Avatar 的模板 StackLimit 是 1」是从生产代码的返回语句读出来的，
  没有对归档逐个复核。
- **一张奖励卡的面能不能是一件时装，本任务不足以确定**（未测）。
- **一件时装今天能不能真的出现在地板上，本任务未确立**——与评审的分档相同。
  本任务的判据是用 `AllocateMonsterDrops` 直接把时装放上地板做出来的，
  这正是评审说的「所有者下一步 GM 直接发放／掉落」那个动作的形状，
  不是对 `DropRuleService` 今天会不会掉时装的测定。
- 上表第 12–15 行的「不能」依据的是 `WornSlotForAsync` 对**两个**模板答 null；
  本轮 `AGENTS.md` 另有「时装仍然穿不上」的记载，但**本任务没有复测那条记载的完整依据**。

### 四、四条检查的实际输出与退出码

**基线**（改动之前，本任务自己跑的一次）：

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
EXIT=0
Protocol 524 / Networking 52 / Pvf 60 / Host 6 / Persistence 320 /
Infrastructure 236 通过 + 2 跳过 / Application 1036
通过数合计 2234
```

与契约 CK-029 给的 2234 及各项目分解逐项相同。

| ID | 退出码 | 输出 |
|---|---|---|
| CK-026 | 0 | `已通过! - 失败: 0，通过: 1039，已跳过: 0，总计: 1039 - Dfo.Server.Application.Tests.dll` |
| CK-027 | 见下 | 变异后 `EXIT=1`，`失败: 2，通过: 1`；还原核散列后 `EXIT=0`，`失败: 0，通过: 3` |
| CK-028 | 见下 | 两次变异各 `EXIT=1`；两次都还原并核散列 |
| CK-029 | 0 | 七个项目零失败，通过数合计 **2237**，2 跳过 |

**CK-029 的用例数增量逐项目归因**（后一次减基线，两次都是本任务自己跑的）：

| 项目 | 基线通过 | 改后通过 | 差额 | 归因 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 524 | 524 | 0 | — |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | — |
| Dfo.Pvf.Tests | 60 | 60 | 0 | — |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | — |
| Dfo.Server.Persistence.Tests | 320 | 320 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 236（另 2 跳过） | 236（另 2 跳过） | 0 | 那 2 条跳过是 `ContentArchive` 缺席时的 Skip，两次读数一致 |
| Dfo.Server.Application.Tests | 1036 | **1039** | **+3** | `PickedUpAvatarStaysWholeTests` 的三条新判据（三个 `[SkippableFact]`，各计 1） |
| **合计** | **2234** | **2237** | **+3** | 差额全部落在 Application.Tests |

**字节锚**：`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
散列 `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，
与契约给的 `1424a4a8…` 相符；本任务开始与收尾各测一次，两次读数相同。**未动。**

**既知偶发**：本任务的六次测试运行里没有出现 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，
也没有出现栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 的失败。

### 五、每条新判据的变异实测

变异纪律：一律 `cp` 做文件级备份、改完跑、改回后 `sha256sum` 核散列。
**没有使用任何 git 还原类命令。**

#### 变异 MU-09-1（＝ CK-027）：把 `CarryOverAsync` 的修复处改回原样

即去掉 `landed.WorldClass is ItemWorldClass.Avatar` 那一岔，恢复无条件写
`Value = checked((int)found.Instance)`。

- 变异前散列 `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d`
- 变异后散列 `061e67426e666f08ce2ea98636ebd15cbe62c4b89fc7cfdd82bdf51be50b5094`
- 读数：`失败: 2，通过: 1`，`EXIT=1`

| 判据 | 红在哪一句 | 读数 |
|---|---|---|
| `An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts` | `var served = await OneItemAsync(side, taken.SlotIndex);`（判据文件第 136 行）——入场装载本身 | `System.IO.InvalidDataException : Avatar detail 1075541543 is unavailable.`，栈根 `InventoryEntryAssembly.WithAvatarDetail` |
| `Two_avatars_off_one_kill_keep_two_identities_of_their_own` | `Assert.NotEqual(one!.Core.Value, other!.Core.Value);`（第 198 行） | `Assert.NotEqual() Failure: Values are equal` |
| `A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced` | 不红 | 它讲的是普通装备，这次变异没有动那一支——这正是它应有的反应 |

- 还原后散列 `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d`，与变异前逐字符相同；
  重跑 `EXIT=0`，`失败: 0，通过: 3`。

**这一趟的第一遍暴露了一个恒真陷阱，记在这里**：判据 `Two_avatars_…` 最初写成
「先问入场装载、再比两个 `Core.Value`」。第一遍变异的读数是
`InvalidDataException : Avatar detail 1582526910 is unavailable.`，**红在装载那一句**，
`Assert.NotEqual` 从来没被执行到——也就是说那句比较在当时**没有被这次变异证明承重**。
判据结构因此被改成「先从容器读回两个核心并比较、再问装载」，重做变异，
才得到上表里 `Assert.NotEqual() Failure: Values are equal` 这个读数。
（第一遍变异态散列 `b8607e74649267bd0f2eab0f4eadc295cba2d77aed54a94c20ca61d60cd74e49`；
那一遍作用的判据文件是改结构之前的版本，与交付态那份不是同一份文本。）

#### 变异 MU-09-2：`ArrivingAvatar.NewIdentity()` 改成 `=> 1`

用来单独逼那句 `Assert.NotEqual` 的另一条路。

- `ItemContainers.cs` 变异前散列 `8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db`
- 变异后散列 `70a8865e9c112417078060f86ec23740b840b7a793174b1d62560eee92772b31`
- 读数：`失败: 1，通过: 2`，`EXIT=1`。红的是 `Two_avatars_off_one_kill_keep_two_identities_of_their_own`，
  **红在第二次拾取那一句**（判据文件第 181 行），读数
  `System.InvalidOperationException : The world took the item and named no identity.`
  ——两件时装拿到同一个身份，撞在存储自己的守卫上，**同样没有走到那句比较**。
- 还原后散列 `8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db`，逐字符相同。
- **如实记**：这一趟没能隔离出那句比较；隔离它的是改结构之后的 MU-09-1。

#### 变异 MU-09-3（＝ CK-028 第一趟）：普通装备不再带地板宣告的值

把非时装那一支改成 `landed.Core with { Durability = found.Durability }`（去掉 `Value`）。

- 变异后散列 `46bb8d31c2bb46237541e5f98c42d7c8be8334984e616b19a10f2d4f95be99b3`
- 全量读数 `EXIT=1`，**3 条红**：
  - `Dfo.Server.Protocol.Tests` — `GroundPickupTests.The_banked_piece_carries_the_value_its_kill_announced`（**既有判据**）
  - `Dfo.Server.Protocol.Tests` — `GroundPickupTests.Every_piece_of_one_kill_carries_one_value`（**既有判据**）
  - `Dfo.Server.Application.Tests` — 本任务新增的 `A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced`，
    红在 `Assert.NotEqual(one.Core.Value, apart!.Core.Value);`（第 256 行），
    读数 `Assert.NotEqual() Failure: Values are equal / Expected: Not 1 / Actual: 1`
- 其余六个项目零失败（Protocol 522 通过 + 2 失败，Application 1038 通过 + 1 失败）。
- **结论**：普通装备「带走地板宣告的值」这条既有行为**仍然承重**，本次修复没有把那两条既有判据改成恒真。

#### 变异 MU-09-4（＝ CK-028 第二趟）：两支都不再带地板宣告的耐久

- 变异后散列 `9c74a2726dc6e7db57ed5e6f95d4a33dfd26d901bb3f1e9fd5f2550d68c18095`
- 全量读数 `EXIT=1`，**2 条红**：
  - `Dfo.Server.Protocol.Tests` — `GroundPickupTests.A_piece_a_monster_dropped_can_be_taken_by_its_number`（**既有判据**，它断言耐久不是 0）
  - `Dfo.Server.Application.Tests` — 本任务新增的 `A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced`
- 其余五个项目零失败。
- 还原后散列 `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d`，与交付态相同。
- **结论**：耐久那一半也各自被钉住，不是只钉了值那一半。

### 六、自查：逐条对照验收条件

**逐句判准**：这一节每句都问过「它在说一个个例，还是在说一类」。
说一类的地方要么带口径（「上表逐行」「经这个实现被判为 Avatar 的模板」「实测的这两个模板」），
要么改写成「不足以确定」。

**验收条件 1 — 一件时装经地面拾取通路入库之后是完整的。达成。**
证据是 `An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts`：
它走生产命令处理器把一件时装从地板拾进真库，再请入场装载服务它，
断言身份在（`Core.Value > 0`）、`ItemKind` 是 8、六列事实逐项与 `ArrivingAvatar` 的形状相同。
装载不抛异常这件事不是我说的——把修复改回原样，它抛
`InvalidDataException : Avatar detail 1075541543 is unavailable.`（MU-09-1）。
六列是与 `ArrivingAvatar.Facts()` 的形状比的，不是与自己回读的行比的，
所以一次「写了六项自己编的事实」的拾取不会在这里通过。

**验收条件 2 — 普通装备经地面拾取的行为不变。达成。**
两类证据。其一，既有判据全绿：`Dfo.Server.Protocol.Tests` 交付态 524 通过，
与基线逐项相同，`GroundPickupTests` 的既有用例一条没红。
其二，两次变异（MU-09-3、MU-09-4）分别打掉值与耐久，
**既有判据各自红了**（值那次红 2 条既有的，耐久那次红 1 条既有的），
说明那些判据仍然承重，不是被本次修复改成了恒真。
另外新增的 `A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced`
在真库上再钉一遍：一次击杀的两件同值、另一次击杀的那件不同值、且不等于开格的计数、三件耐久都在。

**验收条件 3 — 查明并如实记下 `PutAsync` 这条通路对时装的一般影响。达成。**
第三节给了 15 个生产调用点（11 个成员）的完整清单、枚举口径、每个「改不改 `Value`」
与「今天能不能碰到时装」的逐条回答，以及每条回答依据的是实测、代码级读出、还是不足以确定。
**这一条是记载与测定要求，不是一条可断言的运行时行为**，所以它没有对应的 xunit 判据；
它的证据是探针实测（两个时装模板 + 一个武器模板的四个读数，六条 refill 规则的分类）
与生产代码的返回语句，加上 MU-09-1 这一次把「同型缺陷长什么样」真的复现出来的变异。
把「PutAsync 会毁掉时装」写成一条常绿判据会变成给缺陷背书，所以没有写。
不足以确定的四处已在第三节末尾逐条点名。

**验收条件 4 — 每条判据都有一次变异实测证明它不是恒真。达成，且过程里真的抓到一次。**
三条新判据在第五节各有实测：
`An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts` 与
`Two_avatars_off_one_kill_keep_two_identities_of_their_own` 在 MU-09-1 下各红一次（红点逐句写明），
`A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced` 在
MU-09-3 与 MU-09-4 下各红一次。
**恒真陷阱确实出现了一次并被这个流程抓住**：`Two_avatars_…` 的原结构让入场装载先抛，
那句 `Assert.NotEqual` 一次都没被执行到；改结构之后重做变异，才拿到
`Assert.NotEqual() Failure: Values are equal`。两次读数都记在第五节，没有只留下改好之后的那一次。

**边界与授权自查**：只改了 BD-010（`GroundPickupSlice.cs`）与 BD-008（新增一个判据文件）；
BD-004（`ItemContainers.cs`）只在 MU-09-2 里被临时改过并按散列还原，交付态与开工时逐字符相同。
未建 Git 检查点、未提交、未使用任何 git 还原类命令；未触碰主检出与 `.worktrees/client-debug-lab`。
`git status --porcelain` 交付态 **21 行**（14 个 ` M` ＋ 7 个 `??`），
相对评审记下的开工态 19 行（13 个 ` M` ＋ 6 个 `??`）多出两行，两行都是本任务造成的：

- `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` 由未改动变成 ` M`。
  `git show 565d33fe:…/GroundPickupSlice.cs | sha256sum` 读出
  `e9e2f78ef6a61e25bafb74221e9b2a242297b94aeade32a8dd4ef9d72feda665`，与本任务开工时工作副本的
  散列逐字符相同——**这个文件在本轮之前的任务里一次也没被改过，本任务是第一处改动**。
- `Server/tests/Dfo.Server.Application.Tests/Inventory/PickedUpAvatarStaysWholeTests.cs` 是新的 `??`。

本任务中途自造的探针文件 `ZzProbeTests.cs` 读完即删，交付态目录里没有它。

**没有做的一件，明写**：评审 BF-1 的「要补什么」列了三样，第三样是
`behavior-change-list.md` 反向扫补这一条、把 RV-08 的两个制造者分开写、`AGENTS.md` 同步。
**本任务没有改这两个文件**——它们不在本 Task 的 Permitted files 里。
这件事留给控制器分派。

---

## Independent Quick Review — QR-09-A

- 评审者：独立快审者，fresh context，未读 `spec.md`／`TaskContract.md`／其它 Task 文件
- 评审的契约版本：Contract revision 1（无修正案）
- **判定：`changes_required`**（一处可证伪且为假的基数；其余各项自行复核后全部成立）

### 一、复核了什么（本轮亲手跑的，不是转述）

| 项 | 我的读数 | 与执行结果记载 |
|---|---|---|
| `git status --porcelain` | 21 行 = 14 个 ` M` + 7 个 `??` | 相同 |
| 基线 | `git rev-parse HEAD` = `565d33fee899556476f8a582605fb4f439261c0f` | 相同 |
| 字节锚 `SelectCharacterAcceptedTranscriptTests.cs` | `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，且 `git diff --stat -- Server/tests/Dfo.Server.Protocol.Tests/` 空 | 与契约给的 `1424a4a8…` 相符，**未动** |
| `GroundPickupSlice.cs` 基线散列 | `git show 565d33fe:…` → `e9e2f78e…` | 相同；该文件本轮之前确未被改过 |
| 交付态散列 | `GroundPickupSlice.cs` `cf6bf474…`／`ItemContainers.cs` `8e7f085d…`／`PickedUpAvatarStaysWholeTests.cs` `097cea8f…` | 三项逐字符相同 |
| 全量 `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | `EXIT=0`；524／52／60／6／320／236(+2 跳过)／1039，合计 **2237 通过 / 2 跳过 / 0 失败** | 逐项相同；+3 全在 Application.Tests |
| 既知偶发 | 本轮三次运行均未出现 `ObjectDisposedException: 'SQLitePCL.sqlite3'` | — |

本轮变异一律 `cp` 文件级备份 + `sha256sum` 还原核散列；**未使用任何 git 还原类命令**，未触碰主检出与 `.worktrees/client-debug-lab`，未提交，未改任何交付文件。

### 二、逐条核

**1. 缺陷真修好了吗——是，且判据真的走了生产通路。**

自读 `GroundPickupSlice.cs:275` 的 `HandleAsync`：`AddAsync` → `case ItemPlacementOutcome.PutInEmptySlot` → `CarryOverAsync(container, piece, opened.SlotIndex, …)`。判据用的正是这个 `HandleAsync`（`PickedUpAvatarStaysWholeTests.cs:282`），容器是 `PersistentItemContainer`（真 SQLite 临时库），装载是 `CharacterInventoryEntryDomainService.LoadAsync`（同文件 294 行），不是夹具替身。

**QR-MU-1（我自己重做的 MU-09-1）**：把 `carried` 改回无条件 `Value = checked((int)found.Instance), Durability = …`。变异态散列 `f89c33ab8963f802136e81259b8083aee48dd561858e02cff00fe69990d8f00a`（与执行者的 `061e6742…` 不同，因为我直接替换赋值而不保留分岔空壳，语义相同）。读数 `失败: 2，通过: 1`：

- `An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts` 红在 `PickedUpAvatarStaysWholeTests.cs:136`（`OneItemAsync`，即入场装载），`System.IO.InvalidDataException : Avatar detail 1075541543 is unavailable.`
- `Two_avatars_off_one_kill_keep_two_identities_of_their_own` 红在 `PickedUpAvatarStaysWholeTests.cs:198`，`Assert.NotEqual() Failure: Values are equal`

两处红点与执行结果第五节记的逐字相同（连 detail id `1075541543` 都一致——`Layout` 的 `Seed: 0x0BADF00D` 使地板宣告值确定）。**恒真陷阱的修正是真的生效了**：那句 `Assert.NotEqual` 确实先于装载执行并承重。还原后散列回到 `cf6bf474…`。

因果链我也自读复核：`SqliteItemDomainStore.cs:381-392` 的 `Survivors` 三趟（`InPlace && SameContent`／`SameContent`／`InPlace && SameThing`）全都比 `Core.Value`；`CarriedAvatarsAsync`（同文件 449 行起）按 `staged … .Select(staged => staged.Value!.Core.Value)` 即**新**值回捞。执行结果对这条链的叙述准确。

**2. 普通装备回归面——自己重做一次，结论成立。**

**QR-MU-2（＝重做 CK-028 第一趟）**：把非时装那一支的 `Value` 去掉（我直接写成两支合一的 `landed.Core with { Durability = found.Durability }`）。变异态散列 `9232692f8fb721e5a7ed81b8d52610056f554b229acd5c5ee3317b17bb81498b`（与执行者的 `46bb8d31…` 不同，同上，语义等价）。全量读数 `EXIT=1`，**3 条红，与记载逐条相同**：

- `Dfo.Server.Protocol.Tests.Game.GroundPickup.GroundPickupTests.The_banked_piece_carries_the_value_its_kill_announced`（**既有**），`GroundPickupTests.cs:239`，`Assert.Equal() Failure: Expected: 1434604658 / Actual: 1`
- `Dfo.Server.Protocol.Tests.Game.GroundPickup.GroundPickupTests.Every_piece_of_one_kill_carries_one_value`（**既有**），`GroundPickupTests.cs:261`，`Expected: Not 1 / Actual: 1`
- `Dfo.Server.Application.Tests.…PickedUpAvatarStaysWholeTests.A_piece_of_gear_taken_off_the_floor_still_carries_what_the_floor_announced`（新增），`PickedUpAvatarStaysWholeTests.cs:256`

项目分布 Protocol 522 通过 + 2 失败、Application 1038 通过 + 1 失败，其余五个零失败——与记载相同。**普通装备「带走地板宣告的值」这条既有行为仍然承重，本次修复没有把既有判据改成恒真。** 还原后散列 `cf6bf474…`。

**3. `PutAsync` 清单：15 ✓、7 ✓、11 ✗（实为 10）。**

我用与执行者同一口径重数：`grep -rn "PutAsync" Server/src/ --include=*.cs` 共 **21 行**；扣掉 6 行非调用点——接口声明 `IItemContainer.cs:155`、`<see cref>`／`<c>` 引用 4 处（`IItemContainer.cs:157`、`ItemContainers.cs:382`、`ItemPlacementRuleService.cs:36`、`GroundPickupSlice.cs:405`）、实现本身 `ItemContainers.cs:375`——剩 **15 个调用点**。**15 对。**

**「7 个会改 `Core.Value`」也对。** 我逐个读了方法体核实：`SplitAsync` 源格 `Value = moving.Count - moveCount`、`SplitAsync` 目标格 `StackableItemCore.Create(...)` 新核心、`MergeAsync` 目标格 `Value = checked(target.Count + moveCount)`、`MergeAsync` 源格 `Value = moving.Count - moveCount`、`ConsumeAsync` `Value = remaining`、`LandAsync` 的 `OntoExistingPile` 支 `pile.Core with { Value = topped.Total }`（`ItemContainers.cs:470`），加改前的 `CarryOverAsync` ＝ **7**。其余 8 个（`RelocateAsync`、`SwapAsync`×2、`SelectCardCommand.DeliverAsync` 只改 `Durability`、`PutOnAsync`×2 与 `TakeOffAsync`×2 全写 `piece.Core`／`displaced.Core` 原样）不改 `Value`。7+8=15。

**「11 个成员」不对，实际是 10 个。** 我把 15 个调用点各自的宿主成员取出来去重，得到：`ItemContainerOperations.RelocateAsync`、`.SplitAsync`、`.SwapAsync`、`.MergeAsync`、`.ConsumeAsync`、`StoreBackedItemContainer.LandAsync`（六个在 `ItemContainers.cs`，成员声明行分别是 171／184／204／218／251／459）、`SelectCardCommand.DeliverAsync`（`SelectCardCommand.cs:318`）、`PickUpGroundDropCommandHandler.CarryOverAsync`（`GroundPickupSlice.cs:412`）、`MoveItemSpaceCommandHandler.PutOnAsync`（`MoveItemSpaceSlice.cs:324`，含 346 与 354 两个调用点）、`.TakeOffAsync`（`MoveItemSpaceSlice.cs:374`，含 410 与 418）——**共 10 个**。执行结果第三节的表格本身也只出现这 10 个成员名（15 行里 `SplitAsync`／`SwapAsync`／`MergeAsync`／`PutOnAsync`／`TakeOffAsync` 各占两行）。所以「11」与执行者自己给出的表互相矛盾。

这是**可证伪且为假的基数**，正是本任务 Required methods 明令要避免的那一类，因此按尺度记为阻断。**修法只有一处数字**（见第三节）。清单的**内容**（15 行逐行的「改不改 `Value`」「今天能不能碰到时装」）我逐条复核，**没有发现错项或漏项**。

**「今天只有 `CarryOverAsync` 能被时装走到」——按执行结果的原话站得住；按去掉限定语的说法不成立。**

执行结果的原句是「15 个调用点里有 7 个会改 `Core.Value`……**其中**今天能被一件时装走到的只有 #11」——限定在那 7 个之内，**这句成立**。若把「其中」拿掉、说成「今天只有 `CarryOverAsync` 能被时装走到」，则**为假**：`RelocateAsync`（#1）与 `SwapAsync`（#4／#5）今天都能被时装走到，执行结果自己的表也写着「**能**」并点了 TASK-08 的两条判据；它们不同型（不改 `Value`），所以不影响修法结论。**执行结果的写法是对的，无需改。**

七个改值调用点的闸门我逐个自读复核，全部成立：

- #2／#3 `SplitAsync`：`DecideMoveAsync`（`ItemContainers.cs:582`）里 `moveCount` 被夹到 `moving.Count`，只有 `moveCount != moving.Count` 才选 Split；`ContainerSlotItem.Count => this.IsPile ? Core.Value : 1`、`IsPile => WorldClass is ItemWorldClass.Interchangeable`（`IItemContainer.cs:324/327`），时装 `Count` 恒为 1，`requestedCount` 无法落在 (0,1) 开区间。**不可达。**
- #6／#7 `MergeAsync`：同文件 611-616 行，`displaced.ItemId == moving.ItemId` 时若 `placement.StackLimit > 1` 不成立即以 `MergeExceedsStackLimit` 拒掉。时装 `StackLimit` 是 1。**不可达。**
- #8 `ConsumeAsync`：`remaining == 0` 走 `ClearAsync` 而非 `PutAsync`，`count <= 0` 抛；非堆 `Count` 是 1。**不可达。**
- #9 `LandAsync` 的 `OntoExistingPile`：经 `AddAsync` 要 `ForGrant` 的 `stackLimit > 1`（`ItemArrivalRuleService.cs:65-78`），时装为 1，直落 `IntoEmptySlot`。**不可达。** 经 `RefillAsync` 的 `ForDailyRefill` 确实不看上限（同文件 32-35 行，有 pile 即并入），这一支只靠「归档六条 refill 规则全是 `Interchangeable`」这条**实测**挡住——我没有重跑那个探针（会新增文件，超出本次评审的只读边界），**本评审对那六条读数不足以确定**；该读数带了 itemId 清单与 `preparedAt`，是可证伪的写法，且不影响本任务的修法结论。

「时装 `StackLimit` 是 1」这条代码级读出我自己核了：`PvfEntryLifecycleContentSource.WornPieceMetadata`（第 210-211 行）返回的 `RefillItemMetadata` 首参是字面量 `1`，而该 record 的首参正是 `StackLimit`（同文件 78-83 行）。执行结果已明写「这是代码级读出，不是对归档里 66,328 个时装模板逐个实测」——如实。

**4. 恒真复查——3 条新判据没有恒真的断言，但有一处「断言在陈述、失败在抛出」需读者知道。**

逐句核过 `PickedUpAvatarStaysWholeTests.cs` 的每个 `Assert`：

- 判据一（120-153 行）：136 行的 `OneItemAsync` 本身就是承重句（QR-MU-1 实测抛出）；137／138 行比 `ItemId` 与字面量 `8`（`KindStorageJudgesAnAvatarBy` 是本文件自己的常量，不引生产常量，改坏生产常量它会看见）；146-152 行的六列比的是 `NeverExpires = int.MaxValue`、`0`、`30` 等本文件字面量，不是回读行自比——「写了六项自己编的事实也能过」这条被堵住。149 行 `Assert.All(facts.JewelSocket, …)` 在空数组上会恒真，但 148 行先钉了 `Length == 30`，**不是恒真陷阱**。
- 判据二（167-203 行）：198 行的 `Assert.NotEqual` 由 QR-MU-1 实测红。201／202 行的 `Assert.NotNull(…AvatarDetail)` 在时装上只能「通过或先抛」——`InventoryEntryAssembly.WithAvatarDetail` 缺六列时抛而不返回 null，所以那两句本身不会红，红的是它包住的装载调用。这与判据一 145 行同形。判据文件的 `<remarks>`「The entry load is the assertion.」已明写这一点，**属已披露而非隐藏的恒真**，不记为阻断。
- 判据三（222-263 行）：256 行由 QR-MU-2 实测红；260-262 行的耐久由执行者的 MU-09-4 钉住（我只重做了 CK-028 的第一趟，第二趟未重做）。253 行 `Assert.Equal(one.Core.Value, other.Core.Value)` 与 257 行 `Assert.NotEqual(one.Count, one.Core.Value)` 在 QR-MU-2 下先被 256 行截断，**未被单独隔离**；验收条件 4 的口径是「每条判据」而非「每句断言」，三条判据各有一次实测红，**该条达成**。

**5. 修法选点——成立，没有更该修的地方被绕过。**

- Persistence 的 `Survivors`／`CarriedAvatarsAsync` 确实落在 `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`，而契约 Permitted files 只列了 `GroundPickupSlice.cs`、`ItemContainers.cs`、`Server/tests/` 与编译强制的同步调用点。**在边界外，否得对。**（「按新值回捞」才是根因所在，但那属另一个任务，本轮不作为阻断。）
- `WriteOneAsync`（`ItemContainers.cs:386`）的形参只有 `slotIndex`／`core`／`avatarDetail`／`ct`，确实拿不到那一格原先站着什么，「把六列一起写」它无料可用。**否得对。**
- `Instance` 的 `<param>` 我原文核了（`GroundPickupSlice.cs:347-350`）：「The value that tells this piece of **equipment** from another.」执行者的引证准确。
- 分岔用 `landed.WorldClass` 而非核心 kind 字节：`WorldClass` 由 `StoreBackedItemContainer.ToSlotItemAsync`（`ItemContainers.cs:716-724`）向 `placementRules.ClassifyAsync` 问归档要，与 `ContainerSlotItem.IsPile` 同源。**没有新造第二个可跑偏的答案，选得对。**
- 阻断条件未触及：协议帧与字节布局未动（diff 只落在 `CarryOverAsync` 方法体与其 `<remarks>`，`GroundPickupPayloadCodec` 无改动）；「时装必须带六列事实」这条要求一个字也没放宽。

**6. 计数与范围——自己重跑核实，全部相符。** 见第一节表；字节锚与基线零差异。

**7. 记法——逐句核过，除第 3 点那一个基数外，没有读者无法证伪的全称量词。**

四处「不足以确定」逐条对照：①只实测两个时装模板的三个读数——如实；②奖励卡的面能否是时装未测——如实，且 #10 不改 `Value`，不影响结论；③时装今天能否真的出现在地板上未确立——如实，判据用 `DungeonRunDomainService.AllocateMonsterDrops(…, IReadOnlyList<RolledDrop> rolled)` 直接递入预掷结果（我核了签名，`DungeonRunDomainService.cs:197-201`），确实绕开了 `DropRuleService`；④第 12-15 行的「不能」只依据两个模板的 `WornSlotForAsync`——如实。

新写进 `GroundPickupSlice.cs` `<remarks>` 的那句 `None does today; that reading is recorded with this outcome rather than guarded against here.` 是可证伪的（读者按 `PutAsync` 调用点逐个复核即可推翻），且与它所指的清单一致，**不记为问题**。

### 三、要求的补正

**RM-09-A（唯一阻断项，一处数字）**：执行结果第三节开头「剩下 **15 个调用点，落在 11 个成员上**」中的 **11 改为 10**，并补上计数口径（宿主成员去重后计数；`SplitAsync`／`SwapAsync`／`MergeAsync`／`PutOnAsync`／`TakeOffAsync` 各含两个调用点）。清单的 15 行内容不需要改动。

### 四、明确不作为本轮判定依据的

- 清单与 `AGENTS.md`／`behavior-change-list.md` 的同步（评审 BF-1 的第三样）**不在本 Task 边界内**，已留给 TASK-10，未据此判 `changes_required`。
- `CarriedAvatarsAsync` 按新 `Core.Value` 回捞这一根因落在 Permitted files 之外，**不作为本轮阻断**。
- 文风与详略未记为阻断。


## Controller Disposition D-09 — `task-rework`，只订正记载，不动代码不动判据

**判定**：`task-rework`。TASK-09 保持 `in_progress`。
实质工作经 QR-09-A 逐条独立核实无误——缺陷真修好、普通装备无回归、判据不恒真、
修法选点站得住、计数与字节锚复现一致。**唯一阻断项是一处可证伪且为假的基数。**

### 要订正的（阻断）

摘要写「`PutAsync` 生产调用方 15 个（**11 个成员**）」，**实为 10 个成员**。
评审去重后逐个列名：`RelocateAsync`、`SplitAsync`、`SwapAsync`、`MergeAsync`、
`ConsumeAsync`、`LandAsync`、`DeliverAsync`、`CarryOverAsync`、`PutOnAsync`、`TakeOffAsync`。
**执行者自己那张 15 行表里也只出现这 10 个成员名**——摘要与自己的表自相矛盾。

订正时要**写明此前记的是什么**，并给出**计数口径**（调用点数 vs 去重后的成员数）。

### 一并要做的三处（非阻断，同一轮做掉）

1. **措辞收紧。** 「今天只有 `CarryOverAsync` 能被时装走到」——
   按执行结果的**原话**（「7 个改值的**其中**……只有 #11」）成立；
   **去掉限定语就是假的**：`RelocateAsync` 与 `SwapAsync` 今天都能被时装走到
   （TASK-08 的两条判据走的就是它们），只是**不改 `Value` 故不同型**。
   请把限定语固定进句子，不要留一个能被误读成全称的说法。
2. **补记评审独立核实的结论**：**没有第二个同型的**。
   四道闸门评审自读源码核实成立，分别是：Split（非堆 `Count`=1，clamp 后
   `moveCount == Count`）、Merge（`StackLimit > 1` 不成立即 `MergeExceedsStackLimit`）、
   Consume（`remaining == 0` 走 `ClearAsync`）、`LandAsync` 经 `AddAsync`
   （`ForGrant` 要 `stackLimit > 1`）。出处标 QR-09-A。
3. **补记评审的一处「不足以确定」**：`RefillAsync` 那一支——
   `ForDailyRefill` 不看上限，只靠「归档六条 refill 规则全是 `Interchangeable`」这条**实测**挡住；
   评审**未重跑该探针**（会新增文件、超出其只读边界）。如实记为未复核。

### 不要重做的

QR-09-A 已独立核实：15 个调用点、7 个改 `Core.Value` 两个数**正确**；
全量 2237/2/0 与逐项目复现；字节锚零差异；CK-028 第一趟重做（3 红，含两条既有判据）；
MU-09-1 重做（2 红，一条在入场装载、一条在 `Assert.NotEqual`）；
判据走的是真 `HandleAsync` → `AddAsync` → `CarryOverAsync` → 真 SQLite → 真 `LoadAsync`；
一处已披露的非阻断项（`Assert.NotNull(...AvatarDetail)` 在时装上「通过或先抛」，
判据 `<remarks>` 已明写「The entry load is the assertion.」）。

**不得改动任何生产代码或判据文件。** 三个交付散列必须保持不变。

## 执行结果（TASK-09，第二轮：订正成员计数）

本轮按 **Controller Disposition D-09** 执行，**只动散文**：没有改任何生产代码、
没有改任何判据文件、没有新增或删除任何文件（本记载文件除外），
没有重跑任何探针、没有重做任何变异，未提交、未建检查点、未使用任何 git 还原类命令，
未触碰主检出与 `.worktrees/client-debug-lab`。
本小节是**追加**；上面的既有小节（含 `## 执行结果`、`## Independent Quick Review — QR-09-A`、
`## Controller Disposition D-09`）一字未删未改。
以下四处**只订正本记载的说法，不改被记载的对象**。

### 订正一（阻断项 RM-09-A）：`PutAsync` 生产调用方的**成员数**此前记错

- **此前记的是**（第三节开头，原文）：
  「剩下 **15 个调用点，落在 11 个成员上**。」
  第七节验收条件 3 那一段跟着写：「第三节给了 15 个生产调用点（11 个成员）的完整清单」。
- **应为**：15 个调用点，落在 **10 个成员**上。
- 去重后的十个成员：`RelocateAsync`、`SplitAsync`、`SwapAsync`、`MergeAsync`、
  `ConsumeAsync`、`LandAsync`、`DeliverAsync`、`CarryOverAsync`、`PutOnAsync`、`TakeOffAsync`。
- **这个错与本记载自己的表相矛盾**：第三节那张 15 行的表里出现的成员名本来就只有这十个
  （`SplitAsync`／`SwapAsync`／`MergeAsync`／`PutOnAsync`／`TakeOffAsync` 各占两行），
  「11」对不上任何一行。指出者是 QR-09-A（见该节第二部分第 3 条
  「**「11 个成员」不对，实际是 10 个。**」）。
- 以后引用第三节，**以「15 个调用点 / 10 个成员」为准**；第三节表格的 15 行内容不变。

**计数口径——两个数各自是怎么数出来的**

- **调用点数（15）**：按文本枚举。`grep -rn "PutAsync" Server/src/ --include=*.cs` 读出 **21 行**，
  扣掉 6 行非调用点——接口声明 `IItemContainer.cs:155`、
  `<see cref>`／`<c>` 引用 4 处（`IItemContainer.cs:157`、`ItemContainers.cs:382`、
  `ItemPlacementRuleService.cs:36`、`GroundPickupSlice.cs:405`）、
  实现本身 `ItemContainers.cs:375`——余 **15 行，每行一个调用点**。
  本轮为写清口径重跑了一次同口径的 grep（只读，未改任何文件），读数与上同，
  与 QR-09-A 第二部分第 3 条记的 21／6／15 相同。
- **成员数（10）**：把这 15 个调用点各自的**宿主成员**取出来去重后计数。
  一个成员里出现两次 `PutAsync` 只算一个成员、两个调用点。

| # 成员（去重后） | 文件 | 含哪些调用点（交付态行号） | 调用点数 |
|---|---|---|---|
| 1 `ItemContainerOperations.RelocateAsync` | ItemContainers.cs | #1（:180） | 1 |
| 2 `ItemContainerOperations.SplitAsync` | ItemContainers.cs | #2（:193）、#3（:197） | 2 |
| 3 `ItemContainerOperations.SwapAsync` | ItemContainers.cs | #4（:213）、#5（:214） | 2 |
| 4 `ItemContainerOperations.MergeAsync` | ItemContainers.cs | #6（:228）、#7（:239） | 2 |
| 5 `ItemContainerOperations.ConsumeAsync` | ItemContainers.cs | #8（:277） | 1 |
| 6 `StoreBackedItemContainer.LandAsync` | ItemContainers.cs | #9（:470） | 1 |
| 7 `SelectCardCommand.DeliverAsync` | SelectCardCommand.cs | #10（:365） | 1 |
| 8 `PickUpGroundDropCommandHandler.CarryOverAsync` | GroundPickupSlice.cs | #11（:434，交付态） | 1 |
| 9 `MoveItemSpaceCommandHandler.PutOnAsync` | MoveItemSpaceSlice.cs | #12（:346）、#13（:354） | 2 |
| 10 `MoveItemSpaceCommandHandler.TakeOffAsync` | MoveItemSpaceSlice.cs | #14（:410）、#15（:418） | 2 |
| **合计** | | | **15** |

**这个口径挡不住什么，明写**：以上枚举是对 `Server/src/` 下 `*.cs` 的**文本**枚举，
所以不通过字面 `PutAsync` 抵达该方法的调用方（例如反射）不在此清单内，
**本任务对是否存在这类调用方不足以确定**。`Server/tests/` 下的测试替身按原口径不算生产调用方。

### 订正二：把限定语固定进句子，不留可被读成全称的说法

- **此前记的是**（第三节结论，原文）：
  「**结论**：15 个调用点里有 7 个会改 `Core.Value`（#2、#3、#6、#7、#8、#9，加改前的 #11）。
  其中今天能被一件时装走到的**只有 #11**，其余六个各自被上面写明的那道闸门挡住。」
  **带着「其中」这个限定语，这句是成立的**——它限定在那 7 个改值调用点之内；
  QR-09-A 复核后也这么记（「限定在那 7 个之内，**这句成立**」）。
- **风险在摘掉限定语的转述**。「今天只有 `CarryOverAsync` 能被时装走到」这个说法**是假的**：
  #1 `RelocateAsync` 与 #4／#5 `SwapAsync` 今天都能被一件时装走到——
  第三节表的这三行本来就写着「**能**」，并点名 TASK-08 的
  `A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from` 与
  `A_drag_of_one_onto_another_avatar_trades_the_two_slots` 走的就是它们。
  它们原样写 `moving.Core`／`first.Core`／`second.Core`，**不改 `Value`，故与本任务修掉的那条不同型**。
- **订正后的固定说法**（以后引用第三节的结论，用这一句，不要再压缩）：

  > 在会改 `Core.Value` 的那 7 个 `PutAsync` 调用点里，今天能被一件时装走到的只有 #11
  > `CarryOverAsync`。**能被时装走到的调用点不止 #11**——#1 `RelocateAsync` 与
  > #4／#5 `SwapAsync` 今天也能，只是它们不改 `Value`，不属同型。
  > 这句的口径是第三节那张表的逐行读数（按今天的代码），不是一个全称断言。

### 订正三：补记 QR-09-A 独立核实的结论——在那张清单里没有第二个同型的

**出处：QR-09-A 第二部分第 3 条。这是评审自读源码的核实，不是本轮新做的实测**；
本轮没有重跑任何探针、没有重做任何变异。评审对四道闸门逐个自读后记为成立
（下表右列为评审的读数，措辞取自 QR-09-A）：

| 闸门（挡住哪个调用点） | QR-09-A 的核实读数 |
|---|---|
| Split（#2／#3） | 「`moveCount` 被夹到 `moving.Count`，只有 `moveCount != moving.Count` 才选 Split」，而非堆的 `Count` 恒为 1（`ContainerSlotItem.Count => this.IsPile ? Core.Value : 1`）——clamp 后 `moveCount == Count`，选不上 Split。**不可达** |
| Merge（#6／#7） | 「`displaced.ItemId == moving.ItemId` 时若 `placement.StackLimit > 1` 不成立即以 `MergeExceedsStackLimit` 拒掉」。**不可达** |
| Consume（#8） | 「`remaining == 0` 走 `ClearAsync` 而非 `PutAsync`，`count <= 0` 抛；非堆 `Count` 是 1」。**不可达** |
| `LandAsync` 经 `AddAsync`（#9 的一支） | 「经 `AddAsync` 要 `ForGrant` 的 `stackLimit > 1`（`ItemArrivalRuleService.cs:65-78`），时装为 1，直落 `IntoEmptySlot`」。**不可达** |

**评审据此的结论**：在第三节枚举的那 15 个调用点这一封闭清单之内，
除 #11 之外**没有第二个同型的**（同型 = 会改一件站着的时装的 `Core.Value`）。
读者可按上面的枚举口径逐行复核推翻。
这与第三节原记的逐行判断一致——**本轮不改第三节的内容**，
只把「另有一位独立评审自读源码核实过这四道闸门」这件事补记在案。

### 订正四：补记 QR-09-A 的一处「不足以确定」——`RefillAsync` 那一支未经独立复核

`LandAsync`（#9）还有另一条进路：经 `RefillAsync`。`ForDailyRefill` **不看堆叠上限**
（有 pile 即并入），所以这一支挡住时装靠的不是上限，
而是第三节记的那条**执行者本轮的探针实测**：`GetDailyRefillRulesAsync` 在
`preparedAt = 2020-01-01 +08:00` 下答 **6 条**规则（itemId `2680738`、`2749110`、
`10002520`、`10093393`、`10003235`、`690001556`），六条的 `ClassifyAsync` 都答 `Interchangeable`。

**QR-09-A 未重跑该探针。** 评审的原话（出处：本文件 `## Independent Quick Review — QR-09-A`
第二部分第 3 条）：

> 我没有重跑那个探针（会新增文件，超出本次评审的只读边界），**本评审对那六条读数不足以确定**

如实记为：**这条 refill 读数至今只有执行者第一轮的一次探针实测支持，未经第二方复核。**
本轮也没有重跑——重跑会新增文件，超出本轮「只订正记载、不动交付」的边界。
该读数带了 itemId 清单与 `preparedAt`，是可证伪的写法：谁重跑一次同口径的探针即可核或推翻。

### 收尾检查（本轮实际输出）

**全量**：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

```
已通过! - 失败:     0，通过:   524，已跳过:     0，总计:   524 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   320，已跳过:     0，总计:   320 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:  1039，已跳过:     0，总计:  1039 - Dfo.Server.Application.Tests.dll (net10.0)
EXIT=0
```

合计 **2237 通过 / 2 跳过 / 0 失败**，逐项目与第一轮交付态读数相同。
本轮未出现既知偶发 `ObjectDisposedException: 'SQLitePCL.sqlite3'`。

**三个交付文件散列（本轮跑测之前与之后各测一次，两次读数相同，且与第一轮交付态相同）**：

| 文件 | 散列 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` | `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d` |
| `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | `8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db` |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/PickedUpAvatarStaysWholeTests.cs` | `097cea8f0fa10cb63af90090b10b1b9c416de708dfbf11d8c05958d2975aa3cf` |

**字节锚** `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`：
`1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`，与契约给的 `1424a4a8…` 相符，**未动**。

**`git status --porcelain`**：**21 行 = 14 个 ` M` + 7 个 `??`**，与第一轮交付态逐行相同。
本轮改的这一个文件（本记载）落在未跟踪目录 `.rks-agent/tasks/server-agrees-what-an-avatar-is/`
里面，该目录本来就已经是那 7 个 `??` 中的一个，**所以行数与分类都没变**。


## Independent Quick Review — QR-09-B（第二轮：只判记载订正）

- 评审者：独立快审者，fresh context，未读 `spec.md`／`TaskContract.md`／其它 Task 文件与其它阶段产物
- 评审的契约版本：Contract revision 1（无修正案）；本轮依据 **Controller Disposition D-09**
- 评审对象：`## 执行结果（TASK-09，第二轮：订正成员计数）` 一节；owned diff 身份 = 三个交付散列
  `cf6bf474…`／`8e7f085d…`／`097cea8f…` 与字节锚 `1424a4a8…` 均**未变**（本轮亲测，见第五节）
- **判定：`passed`**
- 范围声明：QR-09-A 已逐条独立核实的实质工作（缺陷真修好、普通装备无回归、判据不恒真、
  修法选点、15／7 两个数、全量读数、字节锚）**本轮一律未重做**，只判 D-09 指定的四件事。
  本轮**未改任何文件**（本记载的追加除外）、**未做变异**、未提交、
  **未使用任何 git 还原类命令**，未触碰主检出与 `.worktrees/client-debug-lab`。

### 一、数订正对了吗——**对**，我自己复算了两个口径

**调用点 15（文本枚举口径）——复算相符。**
`grep -rn "PutAsync" Server/src/ --include=*.cs` 本轮读出 **21 行**。逐行分类，6 行非调用点：

| 非调用点 | 是什么 |
|---|---|
| `IItemContainer.cs:155` | 接口声明 `ValueTask PutAsync(int slotIndex, InventoryItemCoreState core, …)` |
| `IItemContainer.cs:157` | `<summary>` 里的 `<see cref="PutAsync"/>` |
| `ItemContainers.cs:375` | 实现本身 `public ValueTask PutAsync(` |
| `ItemContainers.cs:382` | 文档注释里的 `<see cref="PutAsync"/>` |
| `ItemPlacementRuleService.cs:36` | 文档注释里的 `<c>StoreBackedItemContainer.PutAsync</c>` |
| `GroundPickupSlice.cs:405` | 文档注释里的 `<c>IItemContainer.PutAsync</c>` |

21 − 6 = **15**，与订正一记的 21／6／15 相同。

**成员 10（宿主成员去重口径）——复算相符，且我逐个核了成员声明行与其覆盖的调用点区间。**
本轮读出的成员声明行：`RelocateAsync` `ItemContainers.cs:171`、`SplitAsync` :184、
`SwapAsync` :204、`MergeAsync` :218、`ConsumeAsync` :251、`LandAsync` :459、
`SelectCardCommand.DeliverAsync` `SelectCardCommand.cs:318`、
`CarryOverAsync` `GroundPickupSlice.cs:412`、
`PutOnAsync` `MoveItemSpaceSlice.cs:324`、`TakeOffAsync` `MoveItemSpaceSlice.cs:374`——**10 个**。
把 15 个调用点按声明行落位，与订正一那张表**逐行相符**：

- 1 调用点的五个成员：Relocate(:180)、Consume(:277)、Land(:470)、Deliver(:365)、CarryOver(:434)
- 2 调用点的五个成员：Split(:193/:197)、Swap(:213/:214)、Merge(:228/:239)、
  PutOn(:346/:354)、TakeOff(:410/:418)

**5×2 + 5×1 = 15**，表的「合计 15」自洽，与调用点口径闭合。**「10 个成员」成立。**

订正一指认的两处旧记载我原文核对过，**逐字相符**：第 188 行
「剩下 **15 个调用点，落在 11 个成员上**」、第 388 行「第三节给了 15 个生产调用点（11 个成员）的完整清单」。
我在本文件里搜过「11 个成员」，**旧记载只有这两处，订正一两处都点到了，没有漏网**。
第三节表格的 15 行内容本轮未被改动（我按行号逐行复核，与订正一声明的「不变」一致）。
#11 的行号在旧表里本无行号，订正一给的 `:434` 并注明「交付态」，我核实交付态确在 434 行——**标注准确**。

### 二、措辞收紧到位了吗——**到位，没留下能被读成全称的余地**

订正二给的固定说法三句话各自承担一件事，我逐句核：

1. 「在会改 `Core.Value` 的那 7 个 `PutAsync` 调用点里，今天能被一件时装走到的只有 #11 `CarryOverAsync`」
   ——**限定域写在句首**，不是后置的「其中」，摘句转述也带得走限定语。
2. 「**能被时装走到的调用点不止 #11**——#1 `RelocateAsync` 与 #4／#5 `SwapAsync` 今天也能，
   只是它们不改 `Value`，不属同型」——**主动把全称读法证伪掉**。
   这三行的事实我核了：`ItemContainers.cs:180` 写 `moving.Core`、:213 写 `first.Core`、
   :214 写 `second.Core`，**均原样、不改 `Value`**；旧表这三行原本就写着「**能**」，
   并点名 TASK-08 的两条判据——两条判据我核实真实存在
   （`DraggedAvatarStaysWholeTests.cs:83` 与 `:184`），**不是编出来的引证**。
3. 「这句的口径是第三节那张表的逐行读数（按今天的代码），不是一个全称断言」——把口径钉死。

**结论：读者要把这段读成「今天只有 `CarryOverAsync` 能被时装走到」，必须无视同段的第二句。
不留全称余地，D-09 第 1 条达成。**

### 三、两条补记的出处标对了吗——**标对了，没有把别人的核实写成自己的实测**

- **四道闸门（订正三）**：抬头明写「**出处：QR-09-A 第二部分第 3 条。
  这是评审自读源码的核实，不是本轮新做的实测**」，并再说一遍「本轮没有重跑任何探针、
  没有重做任何变异」。我回查 QR-09-A，那四道闸门确实写在其第二部分第 3 条，
  且 QR-09-A 自己的措辞是「七个改值调用点的闸门我逐个**自读**复核」——
  **补记的属性（自读核实 vs 实测）与出处（章节位置）都标得准确**，右列引文与 QR-09-A 原文相符。
  覆盖面也对：7 个改值点中除 #11 外的 6 个（#2/#3 Split、#6/#7 Merge、#8 Consume、
  #9 经 `AddAsync`）**恰好被这四道闸门盖全**，没有漏一个还宣称「没有第二个同型的」。
  结论句还自带封闭限定（「在第三节枚举的那 15 个调用点这一封闭清单之内」）与可推翻口径。
- **`RefillAsync` 一支（订正四）**：如实记为「**至今只有执行者第一轮的一次探针实测支持，
  未经第二方复核**」，并原文引用 QR-09-A 的「我没有重跑那个探针……本评审对那六条读数不足以确定」
  （我核对，引文逐字相符），且写明本轮也没重跑及其理由。
  **没有把执行者的探针读数升格为「已复核」，也没有把评审的自读升格为实测。**

### 四、有没有引入新的假记载——**逐句核过，未发现**

- 「本轮为写清口径重跑了一次同口径的 grep」：与开头「没有重跑任何探针、没有重做任何变异」
  **不矛盾**（grep 非探针非变异），且已主动披露，读数 21／6／15 我复算相符。
- 表中 15 个交付态行号：**逐个复核，全对**（见第一节）。`SelectCardCommand.cs` 不在 14 个 ` M` 之列，
  其 `:365` 基线态即交付态；`ItemContainers.cs`／`MoveItemSpaceSlice.cs`／`GroundPickupSlice.cs`
  在 ` M` 之列，所给行号与**当前工作区**读数一致。
- 引用旧记载的三处原话（第 188 行、第 388 行、第三节结论 215-216 行）**逐字相符**，
  没有为了好订正而改写被引对象。
- 「`Server/tests/` 下的测试替身按原口径不算生产调用方」：与旧记载第 188 行原口径一致；
  我另跑了一次全仓 `PutAsync`，`Server/src/` 之外的命中**全部落在 `Server/tests/`**
  （替身实现与测试直调），`Tool/`／`ClientHack/`／`protocol/` 零命中——**口径陈述属实**。
- **那条自设的口径警告「文本枚举挡不住反射类调用方，不足以确定」——这句本身是对的。**
  它断言的不是「存在反射调用方」，而是「本口径看不见动态构名的调用方，故不足以确定」。
  这在逻辑上成立：`GetMethod("PutAsync")` 这类字面量反射**会**被 grep 命中，
  但由字符串拼接／泛型转发构名的调用不会。所以这是一句**如实的存疑**，不是假记载。
  （附带一提：我搜 `Server/src/` 的 `GetMethod`／`Invoke(`／`MakeGenericMethod`／
  `typeof(IItemContainer)` 得**零命中**，所以这条存疑偏保守——但**保守不等于假**，不记为问题。）
- 收尾检查各项我全部重跑复核，见下节，**记载与实测相符**。

### 五、另核三件小事——**全部相符（本轮亲手跑的）**

| 项 | 我的读数 | 与第二轮记载 |
|---|---|---|
| `GroundPickupSlice.cs` | `cf6bf4741332850e430e32fa02481487df1caee41bea6a88c7506b3897632a3d` | 逐字符相同 |
| `ItemContainers.cs` | `8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db` | 逐字符相同 |
| `PickedUpAvatarStaysWholeTests.cs` | `097cea8f0fa10cb63af90090b10b1b9c416de708dfbf11d8c05958d2975aa3cf` | 逐字符相同 |
| 字节锚 `SelectCharacterAcceptedTranscriptTests.cs` | `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69` | 与契约相符，**未动** |
| `git rev-parse HEAD` | `565d33fee899556476f8a582605fb4f439261c0f` | 基线未变 |
| `git status --porcelain` | 21 行 = **14 个 ` M` + 7 个 `??`** | 相同；`.rks-agent/tasks/server-agrees-what-an-avatar-is/` 确为那 7 个 `??` 之一，故追加本记载不改行数与分类 |
| 全量测试（本轮重跑一次） | 524／52／60／6／320／236(+2 跳过)／1039 = **2237 通过 / 2 跳过 / 0 失败**，`EXIT=0` | 逐项目相同 |

跑的正是指定命令：
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`。
本轮未出现既知偶发 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，无需按「异常类型 + 栈根 + 复跑即绿」判别。

### 六、非阻断观察（交下游完整变更评审，不构成本轮判定依据）

1. **旧记载原地仍读作「11」。** 订正采用**追加勘误**而非原地改数，
   第 188 行与第 388 行的字面仍是「11 个成员」，勘误在文件靠后处。
   这**符合** D-09「订正时要写明此前记的是什么」与本文件的追加式惯例，
   且勘误把两处旧记载都点名、并给出「以后引用第三节，以 15／10 为准」的处置，
   故**不记为阻断**。若下游希望更防误读，可在那两处旁加一句指向勘误的行内标注。
2. **反射口径偏保守**（见第四节末），可收紧但非必须。
3. 文风与详略未记为阻断。

### 七、不作为本轮判定依据的

- QR-09-A 已核实的实质工作（15／7 两个数、修法选点、判据非恒真、变异读数、回归面）——**未重做**。
- `CarriedAvatarsAsync` 按新 `Core.Value` 回捞这一根因落在 Permitted files 之外——沿用 QR-09-A，不作阻断。
- 清单与 `AGENTS.md`／`behavior-change-list.md` 的同步——不在本 Task 边界内，留给 TASK-10。

### 八、判定

**`passed`。** 数订正正确（15 调用点 / 10 成员，两个口径我各自复算并闭合）、
措辞已把限定语固定进句子且主动证伪全称读法、两条补记的出处与证据强度标注如实
（没有把评审的自读核实写成本轮实测）、逐句核未发现新的假记载；
三个交付散列、字节锚、`git status`（14+7）与全量 2237/2/0 本轮重跑全部相符。
**无需补正。** 本判定只表示本 Task Unit 可继续走执行者的验收逻辑，不是最终质量批准。


## Controller Disposition D-09b — 接受 QR-09-B 的 `passed`

**判定**：接受。第二轮执行结果、自查、QR-09-B 的 `passed` 与被拥有的增量
（`GroundPickupSlice.cs` 修改 ＋ `PickedUpAvatarStaysWholeTests.cs` 新增）指向同一轮。
TASK-09 置 `done`。

四件事逐条核实：成员数 10 与其口径可复算（5×2＋5×1＝15 闭合）；
措辞的限定域写在句首并主动证伪全称读法；两条补记的出处标对且未把评审的核实写成自己的实测；
未引入新的假记载。三个交付散列、字节锚、`git status` 21 行、全量 2237/2/0 均由评审亲测复现。

**一处值得记下的加分**：执行者自己加的口径警告
「文本枚举挡不住反射类调用方，不足以确定」——评审另查
`GetMethod`／`Invoke(`／`MakeGenericMethod`／`typeof(IItemContainer)` 在 `Server/src` **零命中**，
判定该存疑**偏保守但不假**。**在这个成果里，偏保守是本轮第二次出现的、正确方向的错。**

**一条非阻断转下游**：订正采用追加勘误而非原地改数，
两处旧字面仍是「11」；符合 D-09 与本文件的追加惯例，下游完整变更评审可自行判断
是否需要行内指向标注。

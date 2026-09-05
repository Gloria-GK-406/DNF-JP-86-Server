# TASK-06 — 时装的六列迁入世界表

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 把时装的六列从今日所在处迁到世界表，作为该身份的可变状态；线上对时装的两条强制不变。
- Referenced spec requirements: RQ-003
- Referenced plan sections: T-006
- Owned boundary: BD-007, BD-009, BD-010
- Permitted files or interfaces: `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemCore.cs`、`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`、`Server/src/Dfo.Server.Application/Inventory/InventoryEntryServices.cs`、`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs`、`Server/tests/`
- Dependencies: TASK-05
- Required dependency outputs: 稳定身份与统一的槽形状
- Acceptance conditions: 入场帧哈希守卫仍绿；移动一件时装后其细节逐字段不变；「时装必须带细节、非时装不得带细节」两条强制保留；入场装配中属于取值而非接线的既有校验保留。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-013 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-014 `Non-command: 检查时装细节随身份而非随槽`

- Required evidence: 迁移的 diff；帧哈希守卫的通过输出；细节随身份的判据名。

## Execution Result — round 1

### 一、先说最要紧的一件事：六列**已经在世界表上**，本轮没有搬动它们

契约与派工的主线是「把时装的六列迁入世界表」。**核完代码后我认定这一步在本轮开工前就已经完成，
本轮不存在可做的搬运**，理由是三条当场可核的证据：

1. **六列今天就是 `items` 的列**（`CharacterDatabaseSchema.cs:186` 的 `items` 声明里逐个可见：
   `avatar_expire_date`、`avatar_clear_id`、`avatar_jewel_socket`（带 `length = 30` 的 CHECK）、
   `avatar_color1`、`avatar_color2`、`avatar_delete_date`）。它们是上一轮 M-2 从
   `character_avatar_detail` 并进 `items` 的，而 **TASK-02 把 `items` 就地变成了世界表**，
   于是这六列跟着成了世界行的列。设计文档 W-3 预计的「刚落地的活要重做一遍」，
   在这个建模下由 TASK-02 顺带完成了。
2. **它们已经随身份走。** TASK-04 的抬起／落下让世界行在移动中只被 `UPDATE`，
   `LandAsync`（`SqliteItemDomainStore.cs`）写的列是
   `owner_id / list_type / slot_index / item_core / updated_at`——**六列一个都不在里面**，
   所以移动根本不碰它们。
3. **BD-010（`InventoryItemCore.cs`）无事可做。** 计划给 BD-010 写的是
   「core 的编解码随时装列迁出而调整」，但六列从来不在那 82 字节里（它们来自另一张表）。
   82 字节里与时装有关的只有 `Value`（时装 UID，指向细节的那个指针），
   而它的退役牵动 `avatar_uid` 列与其唯一索引——见第五节，那属 BD-002/BD-004，是 T-011 的允许文件。

所以本轮的实际交付是：**把「细节随身份而非随槽」这件事变成可当场证伪的判据**（CK-014），
外加控制器点名的三笔债。**没有改动任何生产代码。**

### 二、改了哪些文件，每一处为什么

| 文件 | 改动 | 为什么 |
|---|---|---|
| `Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarDetailFollowsTheIdentityTests.cs` | **新增**，2 条 `[Fact]` | CK-014：一条逐字段、一条判别式（两件时装换位） |
| `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEntryLifecycleContentSourceTests.cs` | 新增 1 条 `[Theory]`（10 条 `InlineData`） | 债一：`ClassifyEquipmentType` 的 `Avatar` 支补上判据 |

**两个文件都在 `Server/tests/` 下，在允许文件之内。没有越界文件，`Server/src/` 一行未改。**
既有判据一条未删、一条未改期望值（对 `PvfEntryLifecycleContentSourceTests.cs` 是纯追加，
原有的 `DailyRefill_*` 三条一字未动）。

### 三、CK-014 —— 时装细节随身份而非随槽的证据

判据名：

- `AvatarDetailFollowsTheIdentityTests.AnAvatarKeepsAllSixOfItsFactsFieldByFieldWhenItMoves`
- `AvatarDetailFollowsTheIdentityTests.TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts`

两条都**经入场装配（`InventoryEntryAssembly.LoadCharacterAsync`，由 `InventorySide` 按合成根同样的接线拼起来）
读回**，而不是直接读列——问题问的是「客户端被服务成什么」，只有装配把列变成那个东西。

第一条：一件时装（时装容器 1 号，槽 4）六列各给一个互不相同、且都非默认的值，
经结算移到槽 11，重新装配后逐字段断言 `ExpireDate` / `ClearAvatarId` / `JewelSocket`（30 字节数组）/
`Color1` / `Color2` / `DeleteDate` 与移动前相同，**并另断言它们等于播种值**——
防止「两次都读到空」也算不变。身份值同时断言不变。

第二条是**判别式**，也是本轮的关键：两件时装 A（槽 4）B（槽 5）各带自己的六列，
一次结算里互换位置。若细节属于**槽**，槽 4 换完仍应答 A 的细节；
若属于**身份**，槽 4 应答 B 的细节。判据断言后者。

#### 变异验证（文件级备份，未用任何 git 还原命令）

- 变异前 `SqliteItemDomainStore.cs` sha256 = `6bb74a7fae7493701d5a900817589c2b4cb97df16c74e95c3b8a41a851b748c1`，
  **与 `QR-TASK-04-01` 记下的锚逐字符相同**（那是上一轮另一个代理写下的，不是我的自证）。
- 变异内容：在 `FlushAsync` 末尾追加一段——对每个被顶掉的行 `d`，把**现在站在 `d` 那个位置上的行**的
  六列改写成 `d` 的六列。这正是「细节属于位置」的实现。
- 结果：**恰好 1 条红**

```
Dfo.Server.Persistence.Tests.Inventory.AvatarDetailFollowsTheIdentityTests
    .TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: 1002
  Actual:   1001
```

  即槽 4 换完之后应答了**第一件**时装的颜色——正是「跟着槽走」的症状。
- **同一次变异下，TASK-04 的既有判据
  `WorldRowSurvivesTheMoveTests.AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove` 仍然是绿的。**
  这就是本轮新判据的净值：**本轮之前，仓里没有任何东西能把「随身份」与「随槽」这两个答案分开。**
  （我的第一条 `AnAvatarKeepsAllSixOfItsFactsFieldByFieldWhenItMoves` 在该变异下也仍绿——
  单件移动本来就分不开这两个答案，这正是第二条存在的理由。如实记下。）
- 还原后 sha256 与变异前**逐字节相同**（`6bb74a7f…b748c1`），全量复跑回到全绿。

### 四、三条「必须保住的」——逐条核过，全部未动

1. **线上对时装的两条强制**（`GameInventoryDelivery.cs:47-58`）：
   `if (core.ItemKind == 8 && avatar is null) throw` 与
   `if (core.ItemKind != 8 && avatar is not null) throw` —— **该文件本轮一字未改**
   （`git status` 里它的改动全部属于前序任务）。
2. **入场装配中属于取值而非接线的既有校验**：
   `InventoryEntryAssembly.WithAvatarDetail` 的 `detail.JewelSocket.Length != 30`、
   `detail.Color1 is < 0 or > ushort.MaxValue`、`detail.Color2 is < 0 or > ushort.MaxValue`
   —— **一字未改**（`InventoryEntryAssembly.cs` 本轮未改）。
3. **移动一件时装后细节逐字段不变** —— 由第三节的两条判据钉住。

**关于「接线校验是否该顺手拆掉」**：`WithAvatarDetail` 里还有一条 `item.Core.Value <= 0`
（「这件时装的核心里没有一个可用的 UID」），它确实是**接线**而不是取值。
我**没有**拆它，两个理由：

- (a) 既有判据 `AvatarCarrierRulesTests.ACharactersAvatarWithoutAUsableUidIsRefused`
  （`[InlineData(0)]`／`[InlineData(-1)]`）正是钉住这一条的**既有断言**，
  拆它就必须删判据，撞硬约束「既有判据不得为迁就实现而改写」；
- (b) 只要 `avatar_uid` 列与其唯一索引还在（它们从 `Core.Value` 绑值），这条校验就仍在做实事——
  拆掉会把一个清楚的拒绝换成写入期一个含糊的「stale or missing」。

**它的正确退役位置是 `avatar_uid` 一起走的那一刻，即 T-011。** 见第五节。

### 五、本轮**没有**做、并交回控制器的一件事：`avatar_uid` 仍是细节的键

六列虽然在世界行上，但**「这一行有没有细节」今天仍由 `avatar_uid` 是否为 NULL 决定**，
而 `avatar_uid` 是 `Core.Value`（旧的时装 UID）的一份可索引副本。具体六处：

- `InventoryItemTables.cs:169-177` `Avatar(reader, ordinal)`：`reader.IsDBNull(avatar_uid) ? null : …`
- `InventoryItemTables.cs:183-196` `BindAvatar`：`@avatarUid = core.Value`
- `InventoryItemTables.cs:127-131` `UpdateOwnAsync` 的 `NOT EXISTS (… held.avatar_uid = @avatarUid …)`
- `InventoryItemTables.cs:157` `InsertOwnAsync` 的 `ON CONFLICT(avatar_uid) … DO NOTHING`
- `CharacterDatabaseSchema.cs:235` `CREATE UNIQUE INDEX idx_items_avatar_uid …`
- `SqliteItemDomainStore.cs:401-404` `CarriedAvatarsAsync` 的 `WHERE avatar_uid = @uid`

**这不影响 RQ-003**（细节仍在身份自己的行上，仍随身份走），但它是「core 里还留着一个指向细节的指针」
这件事的最后残留。**退役它要改 `CharacterDatabaseSchema.cs`（BD-002）、`InventoryItemTables.cs`（BD-004）、
`SqliteItemDomainStore.cs`（BD-006），以及 `ItemCarrierShapeTests` 里两条既有断言与一整条既有 Fact
（`TheAvatarDetailsPrimaryKeyIsRestoredByAPartialIndex`）**——三个 src 文件都不在本 Task 的允许文件内，
而 BD-002/BD-004 恰是 **T-011 的允许文件**，且 T-011 的目标正是「删除已无人读的旧列」。
**我没有自行扩大范围去做它。请控制器裁：是留给 T-011，还是给本 Task 扩边界。**

### 六、三笔债的处理结果

#### 债一：`Avatar` 分类分支的零覆盖 —— **已还**

新增 `PvfEntryLifecycleContentSourceTests.EquipmentType_IsReadAsTheAvatarTheCreatureOrOrdinaryGear`，
10 条 `InlineData` 直接断言 `PvfEntryLifecycleContentSource.ClassifyEquipmentType`：

| 输入 | 期望 | 覆盖的东西 |
|---|---|---|
| `[hat avatar]`、`[coat avatar]` | `Avatar` | 之前全仓零覆盖的那一支 |
| 反引号包住的 `[coat avatar]` | `Avatar` | 反引号剥离 |
| `  [COAT AVATAR]  ` | `Avatar` | 修剪 + 小写化 |
| `[creature]`、反引号包住的 `[creature]` | `Creature` | 姊妹支（原本也只有间接覆盖） |
| `[coat]`、`[weapon]` | `Equipment` | 与 `[coat avatar]` 成对照，钉住的是**后缀**不是包含 |
| 空串、`null` | `Equipment` | 「档案没说也仍是个体件」这条注释里的规则 |

两个时装名取自该函数**自己的注释**（`PvfEntryLifecycleContentSource.cs:21`
「`[hat avatar]`, `[coat avatar]`, and the …」），`[coat]`／`[weapon]`／`[creature]` 取自
`PvfEquipmentSlots.ByName` 与 `CreatureEquipmentType` 常量——**没有一个名字是我编的**。
（十一个时装槽名的完整清单本仓没有记载，所以我只断言有据可查的两个加上后缀规则。）

该分支的行为后果仍在：`InventoryEntryAssembly.cs:169` 的守卫只对 `ItemWorldClass.Avatar` 生效。

#### 债二：堆叠物的 core 可约性 —— **取证完成，结论是「否」**

**问题**：今天生产上写入的堆叠物 core，除模板号与数量外是否真的全为默认值？
**结论：不是。至少两个字段带非默认值，而且两个都上线。**

取证方式：把全仓所有能产出／改写一个 core 的地方列全
（`grep -rn "new InventoryItemCoreState(\|Core with {\|core with {" Server/src/`，命中 8 处），
逐个判断它是否可能作用在一堆无身份的东西上，再对照 `GameInventoryDelivery.Write` 逐字段核该字段上不上线。

1. **`StackableItemCore.Create`（`ItemContainers.cs:31-38`）是生产上唯一制造堆叠物 core 的地方**，
   它写的不是全零：

```csharp
Empty with { ItemKind = itemKind, ItemId = itemId, Value = count,
             Marker16 = -1, RandomOptionChangedIndex = byte.MaxValue }
```

   而这两个字段**都上线**：
   - `GameInventoryDelivery.cs:65`
     `writer.Write(core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16));`
     —— 消耗品是 `ItemKind == 2`，所以 **-1 原样上线**，不是被 `Math.Max` 夹成 0。
   - `GameInventoryDelivery.cs:94` `writer.Write(core.RandomOptionChangedIndex);` —— **255 原样上线**。

   另外 `ItemKind` 本身也是 core 的字段且上线。
2. **合并与拆堆保留既有 core**：`ItemContainers.cs:113 / 148 / 159 / 196 / 350` 全是
   `xxx.Core with { Value = … }`——只改数量，其余原样留着。所以一堆东西的 core 一旦带上什么，就一直带着。
3. **`GroundPickupSlice.CarryOverAsync`（`:390`）由 `if (!found.IsEquipment) return;` 挡住**，够不到堆叠物。
4. **`SelectCardCommand.DeliverAsync`（`:358-366`）是一处需要点名的耦合**：
   它在 `ItemPlacementOutcome.PutInEmptySlot` 时写 `core with { Durability = piece.Durability }`，
   而开新格走的正是 `StackableItemCore.Create`（`ItemContainers.cs:366`），**对堆叠物也走**。
   今天不成事实的理由在取值那一侧：`ClearRewardRuleService.Draw`（`:401`）的 `drawn.Durability`
   来自装备池，而 `Fallback`（`:374`）给堆叠物的耐久恒为 0（注释原文：
   「both items the archive names are stackables … and a stackable carries none」）。
   **所以这是「靠取值那一侧碰巧为零」而不是「有守卫挡着」**——`Durability` 也是上线字段
   （`GameInventoryDelivery.cs:57`）。真做迁移时必须把这条钉死。
5. `DailyRefillService`（`:105-111`）写的是容器交回的行，不新造 core。

**给做迁移的人的结论**：堆叠物的 core **不是**全默认，但（在「旧库作废重建」的裁定下，
即每一份堆叠 core 都出自 `StackableItemCore.Create`）它是一个**固定形状**，
可由 `(ItemKind, ItemId, Count)` 重建，前提是重建时把 `Marker16 = -1`、
`RandomOptionChangedIndex = 255` 一起写回，且 `ItemKind` 由档案而不是由存储回答。
**我没有验证的**：没有跑过真档案统计「实际有多少堆叠模板落在哪些 `ItemKind`」，
也没有为第 4 条耦合写判据（超出本轮边界）。

#### 债三：两条不写 `item_slots` 的写入路径 —— **查明属实，修它超出本轮边界**

**是哪两条**（`grep -rn "item_slots" Server/src/` 的全部命中已列：生产上写 `item_slots` 的只有
`SqliteItemDomainStore` 的 `EmptyAsync`（`:503`）与 `HoldAsync`（`:589`）；
TASK-02 建的 `SqliteItemSlotTable`／`SqliteWorldItemTable` **在 `Server/src/` 里一个调用方都没有**，
只有判据在用）：

1. **`CharacterRosterService.CreateAsync`**
   （`Server/src/Dfo.Server.Application/Characters/CharacterRosterService.cs:214-216`）
   ——建角色时把初始装备直接经 `ICharacterItemTable.InsertOwnAsync` 写进 `items`。
2. **`InventoryEntryAssembly.SaveAsync`**（`:176-187`，以及 `:139` 的 `DeleteOwnExceptAsync`）
   ——入场租约结算经 `UpdateOwnAsync` / `InsertOwnAsync` / `DeleteOwnExceptAsync` 写 `items`。

**为什么不写**：两条都不经过 `IItemDomainStore` 结算，走的是行网关 `ICharacterItemTable`，
而那个网关的四条语句里没有一条碰 `item_slots`；两个调用方也都没有 `IItemSlotTable` 依赖。

**这是否会让 T-008 的孤儿查询失真 —— 会，两个方向都失真：**

- **假孤儿**：这两条路径落下的世界行**从来没有槽行指向它**。所以
  「没有任何槽引用的世界行」会把**每个新建角色的全部初始装备**、
  以及**每一次入场租约结算写下的每一件东西**都报成孤儿。
  这与 `AMD-TASK-04-01` 已经点出的「堆叠物的 `items` 行也无人引用」是**两个独立的失真源**，
  T-008 必须同时区分这两类。
- **悬空槽行**：`DeleteOwnExceptAsync`（`InventoryItemTables.cs:79-99`）删 `items` 行**不删 `item_slots` 行**。
  一次入场结算之后，被删掉那些身份的槽行**还留在 `item_slots` 里指着已经不存在的 `item_uid`**。
  这不是孤儿（方向相反），但它会让「槽 → 世界」方向的任何一致性检查也失真。
  今天无可观察后果（无人读 `item_slots`），T-008 一读就有。

**为什么本轮不修**：修它至少要给这两个调用方接上 `IItemSlotTable`——
`CharacterRosterService.cs` **不在本 Task 的允许文件内**；
`InventoryEntryAssembly.cs` 在，但给它加一个构造参数要同时改**合成根**与
`Server/tests/…/Support/InventorySide.cs` 的接线，合成根同样不在允许文件内。
**只修其中一条不会让孤儿查询变准，只会让它换一种失真。**
→ **原样交回控制器**，建议随 T-008 一起处置（它才同时拥有孤儿查询与所需的边界）。

### 七、两项检查的实际输出

#### CK-013 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

**逐项目相加，不用 `grep -c`：**

| 项目 | 通过 | 跳过 | 失败 | 相对基线 |
|---|---:|---:|---:|---|
| Dfo.Pvf.Tests | 60 | 0 | 0 | — |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 | — |
| Dfo.Server.Protocol.Tests | **518** | 0 | 0 | — （线上字节未动） |
| Dfo.Server.Host.Tests | 6 | 0 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 233 | 2 | 0 | **+10**（债一那条 Theory 的 10 条 InlineData） |
| Dfo.Server.Persistence.Tests | 302 | 0 | 0 | **+2**（CK-014 的两条 Fact） |
| Dfo.Server.Application.Tests | 1022 | 0 | 0 | — |
| **合计** | **2193** | **2** | **0** | 基线 2181 / 2 / 0，**+12** |

60+52+518+6+233+302+1022 = **2193**。不低于门槛 2181。
**跑了三次全量**：第一次 Application 出现 1 条偶发失败（见自检）；第二、三次**全部全绿**，
两次逐项目数字完全相同。

#### 硬约束：入场帧哈希守卫

- `SelectCharacterAcceptedTranscriptTests.cs` **一字未改**：`git status --short` 对它为空，
  sha256 = `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`。
- 单独复跑该类 + `InventoryDeltaLift`：

```
已通过! - 失败: 0，通过: 19，已跳过: 0，总计: 19 - Dfo.Server.Protocol.Tests.dll (net10.0)
```

  **19 通过 / 0 失败**，与要求一致。

#### CK-014 `Non-command: 检查时装细节随身份而非随槽`

**通过**，证据见第三节（两条判据 + 变异恰好 1 条红 + 既有判据在同一变异下仍绿）。

### 八、工作区状态

- HEAD 仍是 `51cd99c2`，**没有提交、没有建检查点、没有推送**，`git stash list` 为空。
- `git status --short` 共 58 条（45 改 + 13 未跟踪）；本轮新增的只有 1 个未跟踪文件
  （`AvatarDetailFollowsTheIdentityTests.cs`）与 1 个既有已改文件上的追加
  （`PvfEntryLifecycleContentSourceTests.cs`，TASK-03 已改过它，本轮只追加一条 Theory）。
- **全程没有使用任何 git 还原类命令。** 唯一一次变异用文件级备份（复制到临时目录 → 改 → 复制回 →
  `sha256sum` 核对逐字节一致）。
- 两个改动文件均**无 UTF-8 BOM**（首三字节 `75 73 69` = `usi`）。
- 未触碰 `.worktrees/client-debug-lab`。

## Executor Self-check — round 1

### 我发现的问题

1. **本 Task 的主线在开工前已经完成，我没有制造工作来填它。** 第一节给了三条当场可核的证据。
   如果控制器认为「六列迁入世界表」还应包含**退役 `avatar_uid`**（把细节的键从旧 UID 换成身份），
   那么本 Task 的允许文件必须扩到 `CharacterDatabaseSchema.cs`、`InventoryItemTables.cs`、
   `SqliteItemDomainStore.cs` 三个 src 文件加数个测试文件，并且要删掉 `ItemCarrierShapeTests`
   里一整条既有 Fact——**这两件事都与本 Task 的硬约束正面冲突**，所以我把它原样交回而不是自己裁。
2. **TASK-04 的既有判据 `AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove` 不是判别式。**
   我用变异当场证明了它在「细节属于位置」的实现下**仍然是绿的**。它的名字与注释
   （「the difference between state that follows the thing and state that has to be carried after it」）
   声称的比它证明的多。这不是缺陷（它证明的东西是真的），但**本轮之前 RQ-003 在时装这一侧没有判别式**。
   这也顺带回答了 `DSP-TASK-04-02` 第三条挂起的那个问题的一半。
3. **债三的失真比 `AMD-TASK-04-01` 记的更宽**：除了「假孤儿」，还有**悬空槽行**
   （`DeleteOwnExceptAsync` 删 `items` 不删 `item_slots`）。这一条此前没有人记下过。
4. **债二的第 4 点（`SelectCardCommand` 的 `Durability`）此前没有人识别。**
   它今天不成事实靠的是取值那一侧碰巧为零，不是任何守卫。

### 我**没有**验证的东西（明说）

- **没有**验证账号仓库那一侧。`ReadCargoItemsAsync`（`InventoryEntryAssembly.cs:263-290`）
  **完全不读时装的六列**（`AccountCargoItemRow` 里根本没有 `Avatar` 字段），
  所以一件时装进了账号仓库，它的细节在读出时就被丢掉——既有判据
  `AvatarCarrierRulesTests.AnAvatarInTheCargoIsCarriedWithNoFactsAndIsNotRefused` 把这当成正确行为。
  **按 RQ-003 的字面这像是一处真缺口**（状态跟着「行在哪张表被怎么读」走），
  但修它要改 `Abstractions/Persistence/InventoryTables.cs` 与 `InventoryItemTables.cs`（不在允许文件内），
  且要改一条既有判据的期望。**我没有动，也没有为它写判据。如实交回。**
- **没有**跑一条经真档案、真容器动词（`MoveItemSpaceSlice.PutOnAsync` 之类）的端到端时装移动。
  我的两条判据在结算（`SqliteItemDomainStore`）与装配（`InventoryEntryAssembly`）之间接起来了，
  但**没有**接上协议层的移动命令。
- **没有**验证旧库（已有数据的 `.db`）。所有者已裁定作废重建。
- **没有**为债二、债三写任何判据——控制器要的是取证，我按取证做。
  这意味着这两条结论**没有机检守着**，将来代码改了不会有人报警。
- **没有**统计真档案里到底有多少模板会被 `ClassifyEquipmentType` 判为 `Avatar`。
  我的新判据是对函数的单元断言，不是对档案的断言。

### 不确定的地方

1. **`avatar_uid` 的归属**。我判定它属 T-011（「删除已无人读的旧列」，且 BD-002/BD-004 是 T-011 的允许文件），
   但派工的「范围外」写的是「旧列退役……属 T-011」，上下文里的「旧列」指的是**位置四列**。
   `avatar_uid` 算不算「旧列」是我的解读，**请控制器确认**。
2. **`CarriedAvatarsAsync` 是否已经完全走不到**。`QR-TASK-04-01` 说「近乎走不到」。
   我这一轮的两条判据都走的是幸存者路径（`Survivors` 第二趟命中），
   **没有**构造出一次会进 `CarriedAvatarsAsync` 的时装写入，也就**没有**验证它。
   它是本轮之后唯一还按 `avatar_uid` 找细节的读法。
3. **第一次全量跑里那 1 条 Application 偶发失败，我没有抓到异常类型与栈根。**
   命令只 grep 了汇总行，失败明细没有落盘。第二、三次全量与单跑
   `Dfo.Server.Application.Tests`（1022/0）都全绿。按「异常类型 + 栈根 + 复跑即绿」三样一起看的口径，
   **我只有第三样**，所以**我不把它归入任何已知偶发，也不宣称它无害**——
   如实记为「一次未取证的失败，复跑即绿」。若评审需要，这一条应当由独立复跑再看一次。
4. 第一节「本轮不存在可做的搬运」是我的判断而非事实陈述的转述。我给了三条可核证据，
   但**如果评审认为 T-006 的验收本就包含 `avatar_uid` 的退役，那么本轮就是欠交付**，
   而不是「已经完成」。这一条我不自己下结论。

## Controller Disposition — DSP-TASK-06-01

执行方交回一件、还清三笔债。控制器**在快审之前**裁定，快审应按裁定后的口径审。

### 一、`avatar_uid` 的退役属 T-011，本轮不算欠交付

执行方问：六列虽在世界行上，但「这一行有没有细节」今天仍由 `avatar_uid` 是否为 NULL 决定（六处）；
退役它要动三个 src 文件（`CharacterDatabaseSchema.cs`／`InventoryItemTables.cs`／`SqliteItemDomainStore.cs`），
**都不在本 Task 允许文件内，而 BD-002／BD-004 正是 T-011 的允许文件**；还要删掉一整条既有 Fact。

**裁定：本轮验收已达成，`avatar_uid` 归 T-011。**
计划 T-006 的三个 Step 说的是「把六列**迁到世界表**」「保留线上两条强制」「保留取值类校验」——
**`avatar_uid` 是「这一行怎么被认出带细节」的索引机制，不是六列的存放位置。**
而 T-011 的原话正是「删除已无人读的旧列与旧表」。执行方守住边界且不自行下结论，是对的。
→ **写进 T-011 的派工。**

### 二、本轮几乎没有生产工作，原因要记准

主线在开工前就已完成：六列今天就是 `items` 的列（`CharacterDatabaseSchema.cs:186`），
而 **TASK-02 的裁定（`items` 就地变成世界表）已经顺带完成了这次搬运**。
BD-010（`InventoryItemCore.cs`）无事可做，因为六列**从来不在那 82 字节里**。

**这是计划层面的冗余，不是执行方偷懒。** T-006 在计划里被写成一次搬运，
而 T-002 的实现方式让它在到达之前就成立了。
→ 记在这里，供完整变更评审判断计划的任务切分是否需要在成果记录里如实说明。

### 三、债二结论：**否**——堆叠物的 core 不可约。记载已订正

执行方查明并经控制器复核：`ItemContainers.cs:31-38` 的 `StackableItemCore.Create` 写死
`Marker16 = -1` 与 `RandomOptionChangedIndex = byte.MaxValue`，
而 `GameInventoryDelivery.cs:65` 对 `ItemKind == 2` **原样上线 `-1`，不钳**。
另有 `SelectCardCommand` 的 `Durability` 耦合靠取值侧碰巧为零而非守卫。

**所以「无身份的东西不在世界表留行」今天做不到，且不是工作量问题**——
丢掉 core 就会改变线上字节。

**`Docs/ItemWorldModel.md` 里我写的那句已被证伪，已当场订正**：
新增「更正 C-1：堆叠物的 core 不可约到『模板号 + 数量』」（该文件第 183 行起），
写明改变了什么、没改变什么。**按所有者定的规矩，跨界改记载须补修正案——本节即是。**

### 四、债三结论：两条路径已查明，且比原先记的更糟

`CharacterRosterService.CreateAsync` 与 `InventoryEntryAssembly.SaveAsync` 不写 `item_slots`。
执行方查明它会让 T-008 的孤儿查询**两个方向都失真**：
假孤儿（有物品行无槽行），**以及 `DeleteOwnExceptAsync` 留下的悬空槽行**——
**后者此前无人记下**，是本轮新查出的。

修它需要合成根与 `CharacterRosterService.cs`，超出本轮边界。执行方交回，正确。
→ **写进 T-008 的派工**，与此前记的「孤儿查询会把每件堆叠物报成孤儿」合并为一条。

### 五、CK-014 的证据强度值得记一笔

变异（把每个位置的六列改写成原先站在那位置那一行的六列，即「细节属于位置」）下**恰好 1 条红**；
而 **TASK-04 的既有判据 `AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove` 在同一变异下仍是绿的**。

即：**本轮之前，仓里没有任何东西能把「随身份」与「随槽」分开。** 这条判据补的是一个真空白，
不是重复覆盖。

## Independent Quick Review — QR-TASK-06-01

- Verdict: **passed**
- 评审对象：Task Unit Contract revision 1（无修订案）＋ Execution Result round 1 ＋ Executor Self-check round 1，
  **按 `DSP-TASK-06-01` 裁定后的口径**（`avatar_uid` 归 T-011，不计缺陷）。
- Owned diff identity：HEAD `51cd99c2`（未提交，`git stash list` 空）；本轮拥有的改动 **2 个文件**——
  新增 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarDetailFollowsTheIdentityTests.cs`（未跟踪），
  以及 `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEntryLifecycleContentSourceTests.cs` 上的一段追加。
  工作区共 58 条（45 改 + 13 未跟踪），其余属 TASK-02..05。
  **越界核实**：按 mtime，这两个文件是 14:42，`SqliteItemDomainStore.cs` 是 14:46（变异后还原所致，见下），
  `Docs/ItemWorldModel.md` 是 14:55（控制器的更正 C-1），**其余全部 ≤ 12:46**，与「`Server/src/` 一行未改」相符。

### 我实际跑过／核过的

| 项 | 我做的 | 结果 |
|---|---|---|
| CK-013 | 自己跑两次全量（`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`），逐项目相加 | 52+518+60+6+233+302+1022 = **2193 / 2 跳过 / 0 失败**，exit 0，**与执行方逐项目表完全一致**；不低于门槛 2131 |
| 基线差 | 对照 `TASK-05-…md:702` 的 2181/2/0（Infrastructure 223、Persistence 300） | **+10 / +2 = +12**，恰是新增的 10 条 InlineData 与 2 条 Fact；**没有任何项目的计数下降** |
| 字节锚 | `git diff --stat -- '*SelectCharacterAcceptedTranscriptTests.cs'` | **空**；sha256 = `1424a4a8…b1c69`，与执行方记录一致 |
| 字节锚复跑 | 该类 + `InventoryDeltaLift` 单跑 | **19 通过 / 0 失败 / 0 跳过** |
| BOM | 两个改动文件首三字节 | 均为 `75 73 69`（`usi`），**无 BOM** |

### 一、主线「开工前已完成」——三条证据逐条自核，全部成立

1. **六列就是 `items` 的列**：`CharacterDatabaseSchema.cs:186` 的 `items` 声明里
   `avatar_expire_date` / `avatar_clear_id` / `avatar_jewel_socket`（带 `length = 30` 的 CHECK）/
   `avatar_color1` / `avatar_color2` / `avatar_delete_date` 逐个可见。
2. **移动不碰它们**：`SqliteItemDomainStore.cs:461-478` 的 `LandAsync` 的 UPDATE 只写
   `owner_id / list_type / slot_index / item_core / updated_at`，六列一个都不在语句里。
3. **六列从来不在 82 字节里**：`InventoryModels.cs:81-116` 的 `InventoryItemCoreState` 35 个字段中
   没有任何一个是时装细节；`InventoryItemCore.cs` 全文不含 `avatar`。BD-010 无事可做属实。

### 二、CK-014 的变异我自己重做了一遍，两件事都成立

方法：**文件级备份**（`cp` 到 scratchpad → 改 → `cp` 回 → `sha256sum` 核对），**全程未用任何 git 还原命令**。
变异内容与执行方描述等价：在 `FlushAsync` 里先按位置读下每个被顶掉行的六列，
结算完再把**现在站在该位置上的行**的六列改写成那份——即「细节属于位置」。

- **全量跑（不是只跑一个项目）：恰好 1 条红。**

```
Dfo.Server.Persistence.Tests.Inventory.AvatarDetailFollowsTheIdentityTests
    .TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: 1002
  Actual:   1001
      at …AvatarDetailFollowsTheIdentityTests.cs:line 130
```

  其余七个项目全绿：Protocol 518/0、Networking 52/0、Pvf 60/0、Host 6/0、Infrastructure 233/0、
  **Application 1022/0**、Persistence 301/**1**。
- **TASK-04 的既有判据在同一变异下仍绿**：单独按名筛 `WorldRowSurvivesTheMoveTests`，
  **9 通过 / 0 失败**，其中含 `AnAvatarsOwnColumnsStayWhereTheyAreThroughAMove`。
  执行方那条「本轮之前仓里没有任何东西能把『随身份』与『随槽』分开」的说法，
  **我用全量变异验证过：全仓 2193 条里只有本轮这一条会红**，所以它补的确实是真空白。
- 还原后 sha256 = `6bb74a7fae7493701d5a900817589c2b4cb97df16c74e95c3b8a41a851b748c1`，
  与变异前、与 `QR-TASK-04-01` 记的锚**逐字节相同**；全量复跑回到 **2193 / 2 / 0**。

（另记：执行方如实写下第一条判据 `AnAvatarKeeps…FieldByFieldWhenItMoves` 在该变异下也仍绿。
我复核属实——它是 CK-014 表述的那条（「移动一件时装后其细节逐字段不变」），
判别力由第二条提供。两条并存是对的，不是冗余。）

### 三、债一：`Avatar` 分支现在确实被钉住了

`PvfEntryLifecycleContentSource.ClassifyEquipmentType`（`:194-203`）的三个出口：
`[creature]` 全等 → `Creature`；否则 `EndsWith("avatar]")`（常量在 `:25`）→ `Avatar`；否则 `Equipment`。
新判据 `EquipmentType_IsReadAsTheAvatarTheCreatureOrOrdinaryGear` 的 10 条 InlineData
直接断言该函数，`[hat avatar]` / `[coat avatar]` / 反引号 / 大写加空白四条都落在 `Avatar` 出口上。
两个时装名确实出自该函数自己的 `:21-24` 注释。

**追加是追加，不是改写**：该文件的 diff 只有两处 `RefillItemMetadata(…)` 补参数与本段 Theory；
前者出自 TASK-03（见 `TASK-03-…md:119`），**本轮拥有的部分只有纯新增，没有删除或放宽任何既有断言。**

一处轻微夸大（不构成缺陷）：成果表里说 `[coat]`/`[weapon]` 那两条「钉住的是**后缀**不是包含」——
10 条 InlineData 里**没有一条能把 `EndsWith("avatar]")` 与 `Contains("avatar]")` 分开**。
它钉住的是「时装性来自尾巴而不是槽名本身」，不是 `EndsWith` 与 `Contains` 之别。

### 四、债二：结论站得住，**控制器对 `Docs/ItemWorldModel.md` 的更正 C-1 不要回滚**

逐处自核（行号以我读到的为准）：

- `ItemContainers.cs:31-39` `StackableItemCore.Create` 确实写死
  `Marker16 = -1`、`RandomOptionChangedIndex = byte.MaxValue`（`Empty` 在 `:25-29`，其余字段全零）。
- `GameInventoryDelivery.cs:65`
  `writer.Write(core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16));`
  ——消耗品是 `ItemKind == 2`，**走的是不钳的那一支，`-1` 原样上线**。属实。
- `GameInventoryDelivery.cs:94` `writer.Write(core.RandomOptionChangedIndex);`——**255 原样上线**。属实。
- `SelectCardCommand.cs:330 / 339 / 359-365`：`opensASlot` 为真时
  `core = core with { Durability = piece.Durability }`，其上游 `ItemContainers.cs:356-368` 的
  `IntoEmptySlot` 对**任何**东西都走 `StackableItemCore.Create`，堆叠物不例外；
  取值侧 `ClearRewardRuleService.cs:402` 用的是 `drawn.Durability`。
  **确实只靠取值侧碰巧为零，没有任何守卫**，而 `Durability` 是上线字段。

→ 「堆叠物的 core 不可约到『模板号 + 数量』」这一结论**经我独立复核成立**。

两处行号小误（只是引用不准，不影响结论）：线上两条强制实际在 `GameInventoryDelivery.cs:48-51`（成果记为 47-58）；
`Durability` 的上线在 `:59`（成果记为 `:57`）。

### 五、债三：两条路径属实，「悬空槽行」这条新查出的也属实

- `grep -rn "item_slots" Server/src/` 的全部命中我自己跑过：生产上写它的只有
  `SqliteItemDomainStore.EmptyAsync`（`:503`）与 `HoldAsync`（`:589`）；
  `SqliteWorldItemTable`（`InventoryItemTables.cs:295`）与 `SqliteItemSlotTable`（`:496`）
  在 `Server/src/` 里**只有类型定义与接口声明，没有任何调用方或注册**。
- `CharacterRosterService.cs:214-216` 经 `items.InsertOwnAsync` 直接写 `items`，无 `item_slots`。
- `InventoryEntryAssembly.cs:181-187` 经 `UpdateOwnAsync` / `InsertOwnAsync` 写 `items`，无 `item_slots`。
- **悬空槽行成立**：`InventoryItemTables.cs:80-99` 的 `DeleteOwnExceptAsync` 两条 SQL 分支
  **都只 `DELETE FROM items`**，不删 `item_slots`。而 `HoldAsync` 是会造槽行的，
  所以一次入场结算之后确有槽行指着已不存在的 `item_uid`。

### 六、被点名要我独立判断的一件：这构成 T-006 的完整交付吗？

**判断：构成。** 理由分三步：

1. **Step 1（搬运）**在到达前已成立，三条证据我逐条自核过（本轮第一节）。按 `DSP-TASK-06-01`，
   这是计划层面的冗余而非欠交付。
2. **Step 2 / Step 3（两条「保留」）不是靠「判据没红」推断的**——这是我特意查的一点。
   执行方在成果第四节给了**具体代码位置**，我逐处回到代码核对，并且**另跑了一次与 HEAD 的对比**：
   - 两条强制在 `GameInventoryDelivery.cs:48-51`，`git diff` 显示本工作区对该文件的全部改动
     只有第一条的**报错文案**（`AvatarDetail[{StorageEntryId}]` → `AvatarDetail[slot {SlotIndex}]`，前序任务所为）
     与一处 `InventoryItemState` 首参，**两个 `if` 的条件与语义与 HEAD 完全相同**。
   - 取值类校验在 `InventoryEntryAssembly.cs:322-328`（`JewelSocket.Length != 30`、
     `Color1 is < 0 or > ushort.MaxValue`、`Color2 is < 0 or > ushort.MaxValue`），本轮 diff 之外，原样在。
   契约的验收条件写的是「保留」，这两件事**事实上成立且被指认到位**，不是只有一句「全绿」。
3. CK-014 的期望结果（「移动一件时装后其细节逐字段不变」）由第一条 Fact 直接满足，
   并由第二条 Fact 加变异证明其判别力。

**一条留给完整变更评审的观察（不作为本轮的整改项）**：这几条「保留」的**机检覆盖并不均匀**——
- 「时装必须带细节」有既有断言钉着：`SelectCharacterPayloadCodecTests.cs:402-416`
  把时装容器的 `AvatarDetail` 置 `null` 后 `Assert.Throws<InvalidDataException>`；
- 但「**非时装不得带细节**」以及三条取值类校验（30 字节／两个颜色范围）**全仓没有任何断言**
  （我按报错文案与 `JewelSocket` 在 `Server/tests/**.cs` 里搜过，只有构造数据的用法，没有断言）。
  把它们删掉，2193 条判据仍会全绿。
  这不违反本 Task 的验收（要求是「保留」而非「为其新增判据」），而且
  `avatar_jewel_socket` 的 30 字节在 schema 上另有 CHECK 兜底；
  但若后续 Task 要动这几处，它们今天**没有机检守着**这一点应当被知道。

### 七、关于自检里那条未取证的偶发失败

我两次全量（还原后各一次）与变异那一次的非目标项目**全部全绿**，逐项目数字每次相同，
**没有复现**。按「异常类型 + 栈根 + 复跑即绿」三样一起看的口径，我同样只有第三样，
**因此不把它归入任何已知偶发，也不宣称它无害**——与执行方的记法一致，原样留着。

### 八、纪律

- 全程**未使用任何 git 还原类命令**（`checkout --` / `restore` / `reset` / `clean` / `stash`）。
- 唯一一次改源码是上面的变异验证，用文件级备份进出，还原后 sha256 与变异前逐字节一致。
- 复核结束时：HEAD 仍 `51cd99c2`，`git status --short` 仍 58 条，`git stash list` 空，
  与执行方交回时的状态相同。**未改动任何实现文件或任务控制状态。**

### 结论

未发现可当场论证的阻断性缺陷。**Required checks 我自己跑过并复现了执行方的每一个数字**，
CK-014 的变异证据我自己重做且把「既有判据在同一变异下仍绿」验证到了全量级别。
上面第三节末与第四节末的两处是**引述精度**问题，第六节末的一条是**留给完整变更评审的观察**，
均不构成本轮的整改要求。→ **passed**。

## Controller Disposition — DSP-TASK-06-02

`QR-TASK-06-01` 给出 `passed`，独立重做了变异（**全仓 2193 条里恰好 1 条红**，
且 `WorldRowSurvivesTheMoveTests` 在同一变异下 9/9 全绿），并复核债二成立。

### 一、订正三处记载精度（控制器自核）

| 记载 | 实际 |
|---|---|
| 线上两条强制在 `GameInventoryDelivery.cs:47-58` | **`:48-51`**（两个 `if` 各占两行） |
| `Durability` 上线在 `:57` | **`:59`** |
| 债一「钉住的是**后缀**不是包含」 | 略夸大——那 10 条 InlineData **无一能分开 `EndsWith` 与 `Contains`** |

前两条我逐行核过。**以本节为准。**

### 二、设计文档的更正 C-1 不回滚

快审独立复核债二成立：`ItemContainers.cs:31-39` 确写死两个非默认值；
`GameInventoryDelivery.cs:65` 对 `ItemKind == 2` 走不钳那一支；`:94` 的 255 原样上线；
`SelectCardCommand.cs:359-365` 的 `Durability` 耦合确实只靠 `ClearRewardRuleService.cs:402`
取值侧碰巧为零。**`Docs/ItemWorldModel.md` 的更正 C-1 保留。**

### 三、一个真的覆盖空白——带进完整变更评审

本 Task 的验收只要求「**保留**」那两条强制与三条取值校验，而它们确实还在（快审逐处回核）。
**但覆盖不均匀**：

- 「时装**必须**带细节」有 `SelectCharacterPayloadCodecTests.cs:402-416` 钉着；
- **「非时装**不得**带细节」全仓无任何断言**——控制器实测
  `grep -rn "Non-avatar inventory item carries avatar detail" Server/tests` **无命中**；
- 三条取值校验（珠宝槽恰好 30 字节、两个颜色落在 0..65535）同样无断言
  （`avatar_jewel_socket` 另有 schema CHECK 兜底，另两条没有）。

**删掉它们，2193 条仍会全绿。** 这是**既存空白，非本轮引入**，且本 Task 契约未要求补。
→ **写进完整变更评审的关注点**，并在 T-012 的派工里作为候选提出（补断言成本很低）。

### 四、本轮的计划冗余，如实带进成果记录

见 `DSP-TASK-06-01` 第二节：主线在到达前已由 TASK-02 的实现方式完成。
**这是控制器写计划时的冗余。** → 成果记录须如实写明，不要让后来者以为 T-006 做了一次搬运。

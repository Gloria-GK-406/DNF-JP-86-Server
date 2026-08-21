# TASK-06: 背包两个仓储

## Task Unit Contract

- Plan task: `T-006`
- Covers: RQ-001, RQ-002
- Boundaries: BD-007, BD-011, BD-012
- Required checks: CK-015, CK-016
- Permitted paths:
  - `Server/src/Dfo.Server.Persistence/Inventory/`（不含 `SqliteItemStore.cs`）
  - `Server/src/Dfo.Server.Abstractions/Persistence/`
  - `Server/src/Dfo.Server.Application/`（背包侧编排）
  - `Server/src/Dfo.Server.Persistence/PersistenceModule.cs`
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `Server/tests/`

### Goal

拆 `SqliteInventoryEntryRepository`（14 张表）与 `SqliteEntryInventoryMutationRepository`（3 张表）。

以字节形式存储的列（`item_core` 等）的编解码位置不变——拆 BLOB 是 Spec 的 Non-Goal。

### Acceptance

| # | Condition |
|---|---|
| 1 | 背包侧拆出的每个仓储，其全部语句只提及一张表 |
| 2 | 背包侧仓储中新建数据库连接与开启数据库事务的处数为零 |
| 3 | 以字节形式存储的列的编解码行为不变 |

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

### 先钉住，再拆

拆之前新增 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs`：
用 T-003 的 `StorageRecordDump` 把 `LoadCharacterAsync` 与 `LoadAccountAsync` 的整条记录写成文本金样本
（人物侧 285 个值，账号侧 143 个值），覆盖两次载入摸到的全部十四张表。金样本取自**未拆分前**的
`SqliteInventoryEntryRepository`，拆完后逐字通过——这就是本任务的行为不变判据。

播种做过失盲校验：把两份 dump 按同一父对象分组（人物侧 59 组、账号侧 29 组），组内不存在两个字段
取到同一个值，因此任意一次同级列对调都会让金样本变红。行的摆法也是刻意别扭的——道具 id 与槽位
相反，人物物品 uid 1..4 对应槽位 2/0/1/9，账号货柜 uid 1/2 对应槽位 4/3，所以任何一处丢掉
`ORDER BY` 都会显形。

### 拆出的单表仓储

`Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`（新）声明契约与行类型；实现分两个文件：

`Inventory/InventoryItemTables.cs`
- `SqliteCharacterItemTable`（`character_new_items`）：三个读法，因为三个调用方要三种顺序，而顺序
  是各自语义的一部分——入场载入按容器与槽位、客户端绘制顺序是槽位加 uid、每日补给按槽位。
  写有 `DeleteOwnExceptAsync` / `UpdateOwnAsync` / `InsertOwnAsync` / `SetStackCountAsync` /
  `InsertStackAsync`。
- `SqliteAccountCargoItemTable`（`account_cargo_new_items`）
- `SqliteCharacterAvatarDetailTable`（`character_avatar_detail`）

`Inventory/InventoryStateTables.cs`
- `SqliteCharacterContainerStateTable`、`SqliteAccountCargoStateTable`、`SqliteCharacterGoldLimitTable`、
  `SqliteCharacterItemValueTable`、`SqliteCharacterItemLockTable`、`SqliteCharacterRentalTable`、
  `SqliteLegacyRentalTable`、`SqliteLegacyCharacterItemTable`、`SqliteLegacyAccountCargoTable`、
  `SqliteCharacterDailyCounterTable`

`accounts` 与 `characters` 落到既有的单表归属者上：`IAccountTable` 加了 `FindCubeFragmentsAsync`，
`ICharacterTable` 加了 `BelongsToAsync`。后者与既有的 `FindOwnerAsync` 并列而非复用，因为
`FindOwnerAsync` 过滤 `delete_flag=0` 而背包侧的归属校验从不过滤——两者问的不是同一个问题。

### 迁到领域层的编排

- `Application/Inventory/InventoryEntryAssembly.cs` → `ICharacterInventoryEntryRepository`、
  `IAccountInventoryEntryRepository`、`IEntryInventoryLeasePersistence`。
  容器类型解析、锁表读全份再一分为二、两张租赁表合成一条列表（去重集合跨两表共享，所以按原次序
  读并即时校验）、时装明细在整份列表读完后再补（不是逐行边读边补，因为一份既有错主行、又有缺明细
  时装的列表报哪个错取决于这个先后）、旧表未迁移则拒绝入场，全部在这里。
- `Application/Inventory/DailyRefillService.cs` → `IEntryInventoryMutationRepository`。

### 打包位置不变

`EncodeItemCore` / `DecodeItemCore` 原样搬进 `Persistence/Inventory/InventoryItemCore.cs`
（`Encode` / `Decode`），仍在持久层。全仓库 8 处旧的 `SqliteInventoryEntryRepository.EncodeItemCore` /
`DecodeItemCore` 调用改名指向它。

领域层不再见到那 82 个字节：表把 `InventoryItemCoreState` 交上去、收回来。每日补给同理——
`MainStackRow(uid, slot, itemId, count)` 是它真正关心的四样东西，`SetStackCountAsync` 在表内部
先读回存储形态再只改数量写回，因此一个堆除数量之外携带的一切原样保留。新建堆的那两个非零常量
（marker 为 -1、changed-option index 为 255）留在表里，与打包同处。

### 删除

- `Inventory/SqliteInventoryEntryRepository.cs`（14 张表）
- `Inventory/SqliteEntryInventoryMutationRepository.cs`（3 张表）

### 测试侧

- 新增 `Persistence.Tests/Support/InventorySide.cs`：按服务器的装配方式搭起这两个领域服务，
  并提供 `PerCall` / `MutationsPerCall`（每次调用自成一个用例）。
  三个测试程序集都摸这个缝，所以它被 `Application.Tests` 与 `Protocol.Tests` 的 csproj
  以 `<Compile Include ... Link>` 引入编译，而不是抄三份。
- `SqliteInventoryEntryRepositoryTests.cs`、`SqliteEntryInventoryMutationRepositoryTests.cs`
  改用 `InventorySide.PerCall` / `MutationsPerCall`，用例体未动。
- `ItemContainerCarrierEquivalenceTests.cs`、`ContainerInventoryProjectionTests.cs` 的夹具
  成员类型由具体类改为接口。
- 「构造某个仓储以建表」的站点改为构造 `SqliteDatabase`：`TwoCarriersInOneTransactionTests`、
  `SqliteItemStoreTests`、`OneDatabaseOneParticipantTests`、`CharacterStoreSettlementTests`、
  `ItemPathFrameTests`、`SchemaOwnershipTests`（构造者列表去掉这两项）。
- 编解码调用改名：`CharacterStoreSettlementTests`、`OneDatabaseOneParticipantTests`、
  `ItemPathFrameTests`、`ItemPathLiveTests`。

### 一处行为变化，已接受

每日补给原先自开一个事务；现在随所在用例一同结算。与 T-005 的出库效果同因同解——调用方对失败的
补给一律拒绝整次入场，用例把这次做过的全部收回。

## Executor Self-check (round 1)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 背包侧拆出的每个仓储语句只提及一张表 | 通过。逐类清点 `Server/src/Dfo.Server.Persistence/` 下全部 `internal sealed class`，仍多表的只剩 `SqliteCharacterEntryStateRepository`（34），属 T-007 |
| 2 | 背包侧仓储中新建连接与开启事务处数为零 | 通过。`Inventory/` 下仅 `SqliteItemStore.cs` 一处，该文件由本任务许可路径明文排除，且它正是那个作为参与者入伙的存储 |
| 3 | 以字节形式存储的列的编解码行为不变 | 通过。`Encode`/`Decode` 逐字搬迁，未改一行；金样本里每件物品的 82 字节全部展开逐字段比对，两份金样本原样通过 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-015 | Non-command，逐类清点表名 | 通过 |
| CK-016 | `dotnet test Server/86JP.Server.slnx -m:1` | 1034 通过 / 2 跳过 / 0 失败（基线 1011 / 2 / 0；T-005 收尾为 1032） |

全量分项：Pvf 60、Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、
Persistence 123、Protocol 203。本次全量无假红。

### 方法自查

绑定方法为 test-driven-development。本轮先写的是表征金样本而不是失败用例：金样本在拆分前对着
旧实现捕获并通过，拆分过程中因类型消失而编译失败，拆完后逐字通过。新增断言只有这两条金样本，
它们的作用是钉住现状而不是描述新行为——这正是行为保持型重构该有的红绿证据。

### 本轮拥有的改动

生产代码，新增：`Abstractions/Persistence/InventoryTables.cs`、
`Persistence/Inventory/InventoryItemCore.cs`、`Persistence/Inventory/InventoryItemTables.cs`、
`Persistence/Inventory/InventoryStateTables.cs`、`Application/Inventory/InventoryEntryAssembly.cs`、
`Application/Inventory/DailyRefillService.cs`。

生产代码，删除：`Persistence/Inventory/SqliteInventoryEntryRepository.cs`、
`Persistence/Inventory/SqliteEntryInventoryMutationRepository.cs`。

生产代码，修改：`Abstractions/Persistence/AccountTables.cs`（加 `FindCubeFragmentsAsync`）、
`Abstractions/Persistence/CharacterTables.cs`（加 `BelongsToAsync`）、
`Persistence/Accounts/AccountTables.cs`、`Persistence/Characters/CharacterEntryTables.cs`、
`Persistence/Inventory/SqliteItemStore.cs`（编解码改名）、`Persistence/PersistenceModule.cs`、
`Application/ApplicationModule.cs`。

测试，新增：`Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs`、
`Persistence.Tests/Support/InventorySide.cs`。

测试，修改：`Persistence.Tests/Inventory/SqliteInventoryEntryRepositoryTests.cs`、
`Persistence.Tests/Inventory/SqliteEntryInventoryMutationRepositoryTests.cs`、
`Persistence.Tests/Inventory/SqliteItemStoreTests.cs`、
`Persistence.Tests/Inventory/TwoCarriersInOneTransactionTests.cs`、
`Persistence.Tests/Schema/SchemaOwnershipTests.cs`、
`Application.Tests/Inventory/ItemContainerCarrierEquivalenceTests.cs`、
`Application.Tests/Transactions/OneDatabaseOneParticipantTests.cs`、
`Application.Tests/Characters/CharacterStoreSettlementTests.cs`、
`Protocol.Tests/Game/StateSynchronization/ContainerInventoryProjectionTests.cs`、
`Protocol.Tests/Game/ItemPathFrameTests.cs`、`Protocol.Tests/Game/ItemPathLiveTests.cs`、
`Application.Tests/Dfo.Server.Application.Tests.csproj`、
`Protocol.Tests/Dfo.Server.Protocol.Tests.csproj`。

### 探针纪律

金样本的捕获用过一个临时 `CaptureProbe` 测试方法（只写测试文件，不碰生产代码），嵌入金样本后已删除，
`InventoryLoadCharacterizationTests.cs` 中已无该名字。本轮未在生产代码留下任何探针。

## Independent Quick Review (round 1)

评审契约：本文件 `## Task Unit Contract`（无修订、无追加条款）+ `## Common Constraints`。
评审对象：`## Execution Result (round 1)` / `## Executor Self-check (round 1)` 的「本轮拥有的改动」清单。
被删两个类的原文取自 `git show HEAD:<path>`（HEAD = `ea95d49`，本任务全程未提交）。

### 实际核对了什么

- **逐条 SQL 对照**：被删两类的每一条语句与新 `SqliteCharacterItemTable` / `SqliteAccountCargoItemTable` /
  `SqliteCharacterAvatarDetailTable` / `InventoryStateTables.cs` 十个表类、`IAccountTable.FindCubeFragmentsAsync`、
  `ICharacterTable.BelongsToAsync` 一一比对：`WHERE`、`ORDER BY`、`LIMIT 1` / `LIMIT 2`、
  `COALESCE(remaining_seconds, 0)`、`ON CONFLICT ... WHERE owner_id=@cid AND character_id=@cid`、
  `EXISTS(...)` 两处旧表探测，全部逐字一致。
- **逐条读取宽度**：`GetInt64` vs `GetInt32` 的选择、`IsDBNull(3)` / `IsDBNull(2)` 守卫、
  `character_avatar_detail` 的 expire_date/clear_avatar_id/color1/color2/delete_date 五列仍是 `GetInt64`
  且 `checked((int)...)` / `checked((ushort)...)` 全部保留（`InventoryEntryAssembly.cs:304-306`），
  `Color1/Color2 is < 0 or > ushort.MaxValue` 的范围拒绝保留（`:294-295`）。
- **逐条校验**：`!= 1` 五处（UpdateOwn/InsertOwn、UpsertOwn、InsertStack、SetStackCount、Claim）与其原文异常消息、
  去重集合（container `seen`、item value `(listType, sortOrder)`、lock `lockIds`/`slots`、
  rental `itemIds`/`inventoryTemplateIds` 跨两表共享）、`Math.Max(0, ...)` 两处钳位、
  `ValidateSlot`/`ValidateUniqueSlots`、`ValidateIdentifiers`，逐一对上。
- **`ReadUInt16` → `RequireUInt16`**：消息 `$"Stored {name} {value} is outside the unsigned 16-bit range."` 逐字一致
  （`InventoryEntryAssembly.cs:574-578`），三个调用点（container parameter / account cargo selection key /
  account cargo item count）一个不少，且读取仍是 `GetInt32`。
- **82 字节全覆盖**：把 `InventoryItemCore.Decode` 与 `Encode` 的偏移逐个展开，
  0 / 1-4 / 5-8 / 9 / 10-11 / 12 / 13-16 / 17 / 18 / 19-20 / 21-24 / 25-32 / 33-40 / 41-44 / 45 /
  46-49 / 50-53 / 54-55 / 56-58 / 59-61 / 62-64 / 65 / 66 / 67 / 68-70 / 71 / 72 / 73 / 74-75 /
  76 / 77 / 78 / 79 / 80 / 81 —— 0..81 无空隙、无重叠，读写两侧集合相同。
- **`InsertStackAsync` 与 `CreateStackableCore` 逐字节对照**：`core[0]=itemKind`、`AsSpan(1,4)=itemId`、
  `AsSpan(5,4)=count`、`AsSpan(21,4)=-1`、`core[66]=255`，其余全零——完全相同。
- **金样本**：读 `StorageRecordDump.cs`（反射遍历全部公开属性/字段，按 MetadataToken 即声明序），
  并抽查播种：cube 六列 11/12/0/14/15/16（含一个 0，验证 `count > 0` 过滤）、
  container_state (1,4444)/(0,3333)、item_values 三行 kind/sort/id/value 全异、
  item_locks (2,1,61,71,81)/(1,12,62,72,NULL)（NULL 验 COALESCE）、
  rental 9001/9101/9201 与 9002/9102/9202、cargo_state 6001/6002/6003、
  avatar detail 7001/7002/7003/7004/7005 + item_uid 4242 + item_id 2500。同一父对象内确无同值。
- **事务边界**：`UnitOfWorkBehavior<TResult>`（Order 60）对 `CommandExecutionKind.Transactional` 一律
  `scope.Begin()`；`ApplySelectedCharacterDailyRefillCommand` 与入场链路均经该管线，故每日补给改为随用例结算没有
  「无用例时被调用」的写路径。`grep "new SqliteConnection|BeginTransaction"` 在
  `Persistence/Inventory/` 与 `Application/Inventory/` 下只命中 `SqliteItemStore.cs:30`
  （契约明文排除的文件，且它本身就是以参与者入伙的存储）。
- **共享夹具**：两个 csproj 的 `<Compile Include="..\Dfo.Server.Persistence.Tests\Support\InventorySide.cs"
  Link="Support\InventorySide.cs" />`。`InventorySide` 是 `internal sealed`，三份程序集各自编译出各自的
  internal 类型，命名空间 `Dfo.Server.Persistence.Tests.Support` 不与任何被引用程序集的公开类型冲突，
  不构成重复类型或构建顺序问题。
- **Common Constraints**：新增/改动的六个生产文件中 `CREATE TABLE|PRAGMA|sqlite_master|ALTER TABLE|table_info`
  零命中；无回填/修复/迁移路径（旧表只被问 `EXISTS` 并拒绝入场，与原文一致）；
  `Tool/` 与 `.worktrees/legacy-reference/` 未动；八个新文件与两个新测试文件均无 BOM、行尾全 CRLF。
- **CK-016 独立复跑**：`dotnet test Server/86JP.Server.slnx -m:1`（PVF_ARCHIVE_PATH 与 Pvf__ArchivePath 已设），
  exit 0。Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、Persistence 123、Protocol 203，
  连同 Pvf 60 = 1034 通过 / 2 跳过 / 0 失败，与执行者所报一致；本次无假红。

### Acceptance 逐条判定

| # | Condition | 判定 |
|---|---|---|
| 1 | 背包侧拆出的每个仓储，其全部语句只提及一张表 | **通过**。13 个新表类各只提及一张表；`characters` / `accounts` 落到既有单表归属者上 |
| 2 | 背包侧仓储中新建数据库连接与开启数据库事务的处数为零 | **通过**。见上文 grep 结果 |
| 3 | 以字节形式存储的列的编解码行为不变 | **通过**。`Encode`/`Decode` 逐字搬迁；82 字节读写两侧全覆盖，`Decode`→`Encode` 是无损往返，故 `SetStackCountAsync` 的「读回存储形态、只改 Value、再写回」与原来「就地改字节再写回」逐字节等价 |

| Check | 判定 |
|---|---|
| CK-015 | 通过 |
| CK-016 | 通过（独立复跑 1034 / 2 / 0） |

Common Constraints：除下列 F-3 外均满足。

### Findings

**F-1（阻塞，真实行为回归）** `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs:252`

`ReadCargoItemsAsync` 结尾多了一句 `return await WithAvatarDetailsAsync(items, cancellationToken)`。
被删的 `LoadAccountAsync`（原文第 81-119 行）**从不**读 `character_avatar_detail`：它只做
`LoadAccountCargoItemsAsync` → `RejectUnmigratedLegacyAccountInventoryAsync` → `LoadAccountCargoStateAsync`
→ `LoadAccountVirtualResourcesAsync` → `LoadEquipmentLocksAsync`。时装明细的补全只在人物侧
（原文第 45 行）发生。

这不是风格偏好：`account_cargo_new_items` 里一件 `ItemKind == 8` 的行，
- 原来载入成功，`AvatarDetail` 为 null；
- 现在会走 `WithAvatarDetailAsync`，先按 `item.Core.Value` 去 `character_avatar_detail` 查明细，查不到抛
  `Avatar detail {uid} is unavailable.`；查到了也几乎必抛
  `Avatar detail {uid} is malformed or has the wrong owner.`——因为货柜行的 `item.OwnerId` 是 **account_id**，
  而 `character_avatar_detail.owner_id` 的唯一写入方 `UpsertOwnAsync` 永远写的是 character_id，两者不可能相等。

也就是说 `LoadAccountAsync` 现在多摸了一张原本不摸的表，并新增了一条原本不存在的拒绝路径。
金样本挡不住它：`InventoryLoadCharacterizationTests.cs:604` 的 `InsertCargoItemAsync` 把 `itemKind` 硬编码为 1，
两件货柜物品都不是时装，所以 `AccountCargoItems[*].AvatarDetail = null` 在新旧两种实现下都成立。

补充说明影响面：目前生产代码无人写 `account_cargo_new_items`（全仓库只有读），且旧实现下这种行最终会在
`StateSynchronizationPayloadCodec.Inventory.cs:199` 抛 `AvatarDetail[...]` 缺失。所以它不是「能用变不能用」，
而是「拒绝的位置、层次与消息全变了」，直接违反 Common Constraints 的「对外可观察行为一字不变」。

修法：`ReadCargoItemsAsync` 直接 `return items;`；`WithAvatarDetailsAsync` 只留给 `ReadCharacterItemsAsync`。
若执行者认为账号货柜也该补明细，那是一次行为变更，应走 Docs 记事或契约追加，不能混在保行为重构里。

**F-2（非阻塞，异常先后次序）** `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs:344-359`

原 `LoadAccountCargoStateAsync`（原文第 459-495 行）的次序是：取首行 → 查所有者 →
**构造结果（此处触发两处 `ReadUInt16` 范围拒绝）** → 再读一行判重复。
新 `ReadCargoStateAsync` 把判重复提到了 `RequireUInt16` 之前。当一份 `account_cargo_state`
既有重复行、其首行 selection_key 又越 ushort 界时，原来报
`Stored account cargo selection key ... is outside the unsigned 16-bit range.`，
现在报 `Account cargo metadata is duplicated.`。与执行者在时装明细上刻意保住的「两个毛病同时出现时报哪一个」
是同一类判据，这里没保住。属可局部修复：把 `rows.Count > 1` 的判断挪到构造之后即可。

**F-3（非阻塞，边界）** 自查清单第 163-167 行

「本轮拥有的改动」列出的三处改动落在 `Permitted paths` 之外：
- `Server/src/Dfo.Server.Persistence/Accounts/AccountTables.cs`
- `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`
- `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs` —— 契约第 10 行**明文排除**该文件

已核实这三处的实际内容都是本任务目标逼出来的最小改动（前两者各加一个方法；`SqliteItemStore.cs` 的
`git diff` 只有两行编解码改名，见下），没有夹带。但契约是封的，需要一条追加条款把这三处例外写进去，
或由控制者确认「删掉的类留下的调用点必须就地改名」不算越界。

```
-            "@core", SqliteInventoryEntryRepository.EncodeItemCore(item.Core));
+            "@core", InventoryItemCore.Encode(item.Core));
-        SqliteInventoryEntryRepository.DecodeItemCore((byte[])reader.GetValue(ordinal));
+        InventoryItemCore.Decode((byte[])reader.GetValue(ordinal));
```

**F-4（非阻塞，文档）** `Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs:59-60`

`FindOwnerAsync` 的 `/// <summary>Which account a character belongs to, or nothing when it is gone.</summary>`
被留在了新插入的 `BelongsToAsync` 文档块正上方，于是 `BelongsToAsync` 挂了两个 `<summary>`，
而 `FindOwnerAsync` 变成无文档。不影响编译（全量零 warning），是一处需要归位的残留。

### 未构成 finding，但记录在案

- 新表类在读取时就做 `item_core` 的空值与长度校验，因此这两条拒绝被提到了所有权/槽位校验之前
  （原文里 `ReadCore` 在 `LoadCharacterItemsAsync` 的第 331 行、即所有权与 `ValidateSlot` 之后）。
  由于 `character_new_items` 与 `account_cargo_new_items` 两张表的 schema 都带
  `item_core BLOB NOT NULL CHECK(length(item_core) = 82)`，这两条分支经由 SQLite 不可达，
  故只是死代码次序变化，不判为缺陷。
- 租赁两表 `Concat` 的先后（新表在前、旧表在后）与原 `UNION ALL` 一致；即使颠倒，
  因去重集合共享且异常消息相同、末尾又按 `ItemId`/`InventoryTemplateId` 重排，也不可观察——
  金样本（9001/9002）对这一项本就没有辨别力，此处是靠读代码而非靠测试确认的。
- 金样本「拆分前对旧实现捕获」这一点无法在只读条件下证伪。可佐证的是：
  `CharacterVirtualResources[1].Value = 0` 对应存储值 -5（`Math.Max(0, ...)` 钳位）、
  `EquipmentLocks` 的 `RemainingSeconds = 0` 对应 `NULL`（COALESCE），都与旧实现相符。

### 探针纪律

评审全程只跑只读命令（`git show` / `git diff` / `git status` / `grep` / `sed` / `dotnet test`）。
结束前 `git status --short -- Server/src/` 的输出与评审开始时逐行一致：
`Server/` 下没有任何文件被本次评审改动，本文件是唯一被写入的文件。

Verdict: changes_required

## Execution Result (round 2)

### F-1 已修：货柜从来不读时装明细

真回归，快评说得对。被删的 `LoadAccountAsync` 只调 `LoadAccountCargoItemsAsync`，从不碰
`character_avatar_detail`；我在拆分时把补明细这步做成了两处载入共用。后果是货柜里任何一件
`ItemKind==8` 的行都会让整次载入抛出——因为货柜行的 `OwnerId` 是账号号，而明细表的 `owner_id`
只可能是人物号，永远对不上。

`ReadCargoItemsAsync` 改为直接返回，并写下为什么这里没有明细可补。

新增用例 `InventoryLoadCharacterizationTests.AnAvatarInTheCargoIsCarriedWithoutADetailRow`：
往货柜里放一件时装，断言它带回来且 `AvatarDetail` 为 null。修之前这条会抛
`Avatar detail 5151 is unavailable.`，所以它确实证伪。

这也补上了快评指出的金样本盲区：原播种的货柜里全是 `itemKind = 1`，两种实现打印出来一样。

### F-2 已修：两处范围拒绝在重复拒绝之前

原次序是「主人不对 → 两次 `ReadUInt16` → 第二行存在」。我写成了把重复判断提前，于是一行既越界
又重复时报的错换了一个。改回原次序并注明。

### F-3 已记账：三处越界

- `Server/src/Dfo.Server.Persistence/Accounts/AccountTables.cs`：加 `FindCubeFragmentsAsync`。
  `accounts` 的六个方块碎片列是被删仓储读的，要落到 `accounts` 的单表归属者上。
- `Server/src/Dfo.Server.Persistence/Characters/CharacterEntryTables.cs`：加 `BelongsToAsync`。
  同理，归属校验读的是 `characters`。
- `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs`：契约明文排除该文件。本轮对它
  只有两行改名（`SqliteInventoryEntryRepository.EncodeItemCore/DecodeItemCore` →
  `InventoryItemCore.Encode/Decode`），因为它引用的那个类被删了；不改则不编译。

见下方合同修订。

### F-4 已修

`FindOwnerAsync` 的 summary 被我留在了 `BelongsToAsync` 的文档块上方。已各归各位。

## Executor Self-check (round 2)

| # | Acceptance | 结果 |
|---|---|---|
| 1 | 背包侧拆出的每个仓储语句只提及一张表 | 通过，round 2 未新增跨表语句 |
| 2 | 背包侧仓储中新建连接与开启事务处数为零 | 通过 |
| 3 | 以字节形式存储的列的编解码行为不变 | 通过。快评已逐偏移核实 `Decode`/`Encode` 覆盖 0..81 无空隙无重叠，往返无损 |

| Check | 命令 | 结果 |
|---|---|---|
| CK-015 | Non-command，逐类清点表名 | 通过 |
| CK-016 | `dotnet test Server/86JP.Server.slnx -m:1` | 1035 通过 / 2 跳过 / 0 失败（基线 1011 / 2 / 0；round 1 为 1034）|

全量分项：Pvf 60、Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、
Persistence 124、Protocol 203。本次全量无假红。

`git status --short -- Server/src/` 与 round 1 自查所列一致；本轮未留探针。

## Contract Amendment (round 2)

许可路径增列，并解除对 `SqliteItemStore.cs` 的排除（仅限改名）：

- `Server/src/Dfo.Server.Persistence/Accounts/`
- `Server/src/Dfo.Server.Persistence/Characters/`（仅 `CharacterEntryTables.cs`）
- `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs`，改动限于把被删类的编解码
  静态调用改指向 `InventoryItemCore`

理由：Acceptance 1 要求每个拆出的仓储只提一张表，因此被删仓储里读写 `accounts` 与 `characters`
的语句只能落到那两张表各自的归属者上；`SqliteItemStore` 则是被删类的下游引用者，不改名即不编译。
口径与本 Spec 内 T-002、T-005 的合同修订一致。

## Independent Quick Review (round 2)

评审契约：本文件 `## Task Unit Contract` + `## Common Constraints` + `## Contract Amendment (round 2)`。
评审对象：`## Execution Result (round 2)` / `## Executor Self-check (round 2)` 所述的补救本身。
本轮只判 round 1 的 F-1..F-4 是否闭合、补救有无引入新回归、边界记账是否诚实充分；
round 1 已逐条比对过的 SQL、读取宽度与 82 字节编解码不重做。
被删两类的原文取自 `git show HEAD:<path>`（HEAD 仍 `ea95d49`，全程无任务级提交）。

### 实际核对了什么

- **round 2 的 owned diff 就是三个文件**。按 mtime 分簇：15:02 `Abstractions/Persistence/CharacterTables.cs`、
  15:02 `Application/Inventory/InventoryEntryAssembly.cs`、15:03
  `Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs`；其余 T-006 文件均 ≤14:44（round 1），
  T-001..T-005 各簇 ≤13:28。这三处都在密封契约的 Permitted paths 内（第 11 行 `Abstractions/Persistence/`、
  第 12 行 `Application/`、第 15 行 `Server/tests/`），**本轮没有新增越界**。

- **F-1 生产侧**：`InventoryEntryAssembly.cs:255` 的 `ReadCargoItemsAsync` 现在 `return items;`，
  上方 `:252-254` 三行注释写明「明细行属于人物、货柜行属于账号，没有可能对上的东西」。
  `ReadCharacterItemsAsync` 仍在整份列表读完之后于 `:227` 调 `WithAvatarDetailsAsync`，两段式保留，
  与原 `LoadCharacterAsync`（原文第 42-46 行「`LoadCharacterItemsAsync` → `LoadAvatarDetailsAsync`」）同序；
  `WithAvatarDetailsAsync` 的 `<remarks>`（`:259-263`）把「一份既有错主行、又有缺明细时装的列表报哪个错取决于这个先后」
  写进了代码。

- **新用例必红（只读推理，未改生产代码）**：`AnAvatarInTheCargoIsCarriedWithoutADetailRow` 先 `SeedAsync`
  ——其 `character_avatar_detail` 只播了 `item_uid=4242` 一行（`:595-599`）——再
  `InsertCargoAvatarAsync(slot:5, from:110, itemId:2700, avatarUid:5151)`（`:638-643`，
  `list_type=12`，`Core(from, 8, itemId, avatarUid)` 即 core[0]=8、offset 5..8 写 5151）。
  若把 `return items;` 换回 `WithAvatarDetailsAsync`：该行 `ItemKind==8` 进入 `WithAvatarDetailAsync`（`:279`）→
  `Core.Value=5151>0` 过第一道 → `avatarDetails.FindAsync(5151)` 查无此行 → `:290-292` 抛
  `Avatar detail 5151 is unavailable.`，`LoadAccountAsync` 整次失败，用例转红。
  该抛出发生在 `ReadCargoItemsAsync` 内部，早于旧表拒绝（`:96-102`）、`ReadCargoStateAsync` 与
  `RequireUniqueSlots`，不存在被别的异常抢先、使用例红得不对的可能。**是真守卫，不是空断言。**

- **播种确实复现了原实现的行为**：`git show HEAD:Server/src/Dfo.Server.Persistence/Inventory/SqliteInventoryEntryRepository.cs`
  的 `LoadAccountAsync`（原文 81-119 行）依次只调 `LoadAccountCargoItemsAsync` →
  `RejectUnmigratedLegacyAccountInventoryAsync` → `LoadAccountCargoStateAsync` →
  `LoadAccountVirtualResourcesAsync` → `LoadEquipmentLocksAsync`，**全程不出现 `LoadAvatarDetailsAsync`**
  （该方法在原文中仅被 `LoadCharacterAsync` 第 45 行调用一处）。而 `LoadAccountCargoItemsAsync`（原文 337-378）
  对任何 item_kind 一视同仁地构造 `InventoryStorageItem(...)`，`AvatarDetail` 取默认 null。
  所以「list_type=12、itemKind=8、`Core.Value=5151`、5151 不在 `character_avatar_detail` 中」这样一行，
  在原实现下确实是带回来且 `AvatarDetail = null`，不抛错。播种与原行为一致，用例钉的是真现状。

- **F-2 逐字**：新 `ReadCargoStateAsync`（`:337-363`）次序为「无行→返回零值 → `row.AccountId != accountId.Value`
  抛 `Account cargo metadata has the wrong owner.` → 构造
  `AccountCargoStorageState(new AccountId(row.AccountId), RequireUInt16(SelectionKey), row.Value32, RequireUInt16(ItemCount))`
  → `rows.Count > 1` 才抛 `Account cargo metadata is duplicated.`」。C# 实参从左到右求值，故 selection key
  的范围拒绝先于 item count，与原 `LoadAccountCargoStateAsync`（原文 459-495）逐句同序。
  `RequireUInt16`（`:577-581`）的消息 `$"Stored {name} {value} is outside the unsigned 16-bit range."`
  与原 `ReadUInt16`（原文 881-891）逐字一致，三个 name 实参（`container parameter` /
  `account cargo selection key` / `account cargo item count`）也一致。

- **F-3 交叉核对**：`git status --short -- Server/` 共 58 项，其中 `Server/src/` 33 项（8 M / 8 D / 17 ??）。
  逐项归属：`Schema/CharacterDatabaseSchema.cs`、`Database/SqliteDatabaseParticipant.cs`、
  `Persistence.Tests/Database/` 属 **T-001**（TASK-01:66-67、106-107）；`Protocol/ProtocolModule.cs`、
  `Application/Characters/CharacterStateAccess.cs`、`Abstractions/Sessions/ISessionPresence.cs` 属 **T-002**
  （TASK-02:257-259，已由其 Amendment A1 授权）；`Database/SqliteDatabase.cs`、`Database/SqliteTable.cs`、
  `Accounts/` 一簇与 `Application/Accounts/` 属 **T-004**；`Characters/` 删除的四个仓储、
  `Characters/CharacterSkillTables.cs`、`DungeonEffectOutboxTable.cs`、`Application/Characters/` 三个新服务、
  `OutsideTransactionTests.cs`、`SessionGenerationOwnershipTests.cs` 属 **T-005**。
  **归 T-006 且落在许可路径外的，确实只有自查所列三处，无第四处未记。**
  另注：`Abstractions/Persistence/AccountTables.cs` 与 `CharacterTables.cs` 虽显示为未跟踪新目录内的文件，
  但该目录本就在 Permitted paths 第 11 行内，不构成越界，修订未列它们是对的。

- **修订的诚实性**：`git diff -- Server/src/Dfo.Server.Persistence/Inventory/SqliteItemStore.cs`
  为单文件 +2/−2，正是那两行编解码改名，与修订「改动限于把被删类的编解码静态调用改指向 `InventoryItemCore`」
  逐字相符，无夹带。`Persistence/Accounts/AccountTables.cs:78-91` 的 `FindCubeFragmentsAsync` 只提 `accounts`；
  `Persistence/Characters/CharacterEntryTables.cs:110-124` 的 `BelongsToAsync` 只提 `characters`
  且不过滤 `delete_flag`——与被删仓储的 `VerifyCharacterOwnershipAsync` 一致。两处都在修订所述限定内。

- **F-4**：`Abstractions/Persistence/CharacterTables.cs:60-70` 的 `BelongsToAsync` 现在自带
  `<summary>Whether this character is that account's, counting one that has been deleted.</summary>`
  与一段说明它为何不复用 `FindOwnerAsync` 的 `<remarks>`；`:72-73` 的 `FindOwnerAsync` 拿回了
  `<summary>Which account a character belongs to, or nothing when it is gone.</summary>`。
  归位正确，无重复 `<summary>`，无无文档成员。

- **无新回归**：人物侧金样本 `ExpectedCharacter` 中 `CharacterItems[3].AvatarDetail.*` 六个字段
  （`:344-349`：7001 / 7002 / 30 字节 socket / 7003 / 7004 / 7005）仍在且通过，说明人物侧补明细未被本轮波及；
  新用例每次自建临时库（`TempPath()` 每例一份）并在 `SeedAsync` 之外另插一行，不改动两条金样本的播种，
  `AccountCargoItems[0..1].AvatarDetail = null`（`:458`、`:516`）继续成立，与既有金样本无冲突。
  另记：`account_cargo_state` 的主键是 `account_id`（`CharacterDatabaseSchema.cs:161`），
  故 F-2 涉及的「重复行」分支经由 SQLite 不可达——这次次序复原发生在死代码内，
  既不改变任何可达行为，也不可能引入新行为。

- **Common Constraints**：三个 round 2 文件均无 BOM、行尾全 CRLF（186/186、605/605、676/676 行）；
  `TreatWarningsAsErrors` 下全量构建零 warning；HEAD 仍 `ea95d49`，无任务级提交。

- **CK-016 独立复跑**：`dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 已设），
  exit 0。Application 499、Host 4、Infrastructure 99(+2 跳过)、Networking 46、Persistence 124、Protocol 203，
  加 Pvf 60（单项 `--no-build` 复跑确认）= **1035 通过 / 2 跳过 / 0 失败**，与执行者所报一致；本次全量无假红。
  Persistence 由 round 1 的 123 增至 124，恰是新增的那一条用例。

### F-1..F-4 逐条判定

| Finding | 判定 | 依据 |
|---|---|---|
| F-1 | **已修** | `InventoryEntryAssembly.cs:255` 直接返回；`:227` 人物侧两段式保留；新用例经只读推理确认必红；播种经原文核对与原行为一致 |
| F-2 | **已修** | `:355-362` 与原文 459-495 逐句同序；`RequireUInt16` 消息与三个 name 实参逐字一致 |
| F-3 | **已记账，且充分** | 修订三条与实际改动逐字相符；与 `git status` 全表交叉核对后无第四处未记越界 |
| F-4 | **已修** | `CharacterTables.cs:61-73` 两个方法各持其文档 |

| # | Acceptance | 判定 |
|---|---|---|
| 1 | 背包侧拆出的每个仓储语句只提及一张表 | 通过（round 2 未新增或改动任何表类语句） |
| 2 | 新建连接与开启事务处数为零 | 通过（round 2 未新增连接或事务代码） |
| 3 | 编解码行为不变 | 通过（round 2 未触及 `InventoryItemCore`） |

| Check | 判定 |
|---|---|
| CK-015 | 通过 |
| CK-016 | 通过（独立复跑 1035 / 2 / 0） |

Common Constraints：满足；round 1 的 F-3 已由 `## Contract Amendment (round 2)` 覆盖。

### 新 Findings

无阻断项。两条记录在案、不判为缺陷：

- **N-1（记事，非缺陷）** `Server/tests/Dfo.Server.Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs:71-73`
  两条金样本用例与新用例之间留了连续三个空行。纯格式，无 analyzer 命中，全量零 warning。

- **N-2（记事，非缺陷，属流程侧）** `## Contract Amendment (round 2)` 由执行者自立，口径与 T-002 的 A1、
  T-005 的 round 2 修订一致，故不据此阻断。但与 T-005 情形相同：plan 侧 BD-007 / BD-011 / BD-012 仍未覆盖
  `Persistence/Accounts/`、`Persistence/Characters/CharacterEntryTables.cs` 与 `Inventory/SqliteItemStore.cs`，
  Task 层与 plan 层的边界描述目前不一致。该吸收由 plan 所有者安排，本任务无须再动。

### 探针纪律

评审全程只跑只读命令（`git show` / `git diff` / `git status` / `grep` / `sed` / `ls` / `od` / `dotnet test`）。
结束前 `git status --short -- Server/src/` 输出为 33 项（8 M / 8 D / 17 ??），与评审开始时逐行一致：
**`Server/` 下没有任何文件被本次评审改动**，本文件是本次评审唯一被写入的文件。

Verdict: passed

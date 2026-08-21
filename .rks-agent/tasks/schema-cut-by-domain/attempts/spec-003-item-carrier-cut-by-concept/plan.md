---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 物品承载按概念重切

## Implementation Approach

Spec 的核心成果是库里物品这一块的形状；必要支撑是称号册变换的双向正确、九张源表的约束在落点上存续、三条写不成 `CHECK` 的规则由代码继续保证；外围保证是下发帧字节与表征用例的可证伪性。

最小的做法建立在与上一块相同的事实上：**领域记录不变**。`CharacterInventoryEntryStorageRecord`、`AccountInventoryEntryStorageRecord` 与 `InventoryStorageItem` 的形状、字段与取值都不改，改的只是每个字段从哪张表的哪一列来。三份整记录表征用例的**期望输出一个字节都不用改**，只有播种的 SQL 改成写新表——期望值若被改动，就说明搬运改变了行为。

这一块与上一块有三处不同，它们决定了任务次序：

- **有两处悬置的约束会改变目标声明本身。** 裁定把时装详情主键的去向（D-15）与仓库槽位唯一性的去向（D-16）交给物品侧回答，而两个答案都可能给 `items` 加一列或加一个部分唯一索引。先定案，才能写声明。所以 T-001 在最前面，且它的产物之一是回写裁定。
- **有一处变换必须正确，而它今天不存在。** 称号册的 `category` 是 0..4，`list_type` 是容器类型，`Main = 0` 与 `category = 0` 撞键。裁定给的是 `list_type = 19 + category` 与逆变换 `category = list_type - 19`，并要求角色背包的读取排除 19..23。这两条都不会被今天任何一条用例发现，因为今天称号册在另一张表里。所以 T-002 要把它们先写成会失败的断言。
- **有三条规则只活在代码里。** 哪些行有时装列、仓库行永远没有、角色的时装必须带非零 UID——写不成 `CHECK`，改写读取路径时丢掉任何一条都不会红。所以 T-003 先清点今天守住它们的用例，缺口先补齐、并确认全绿，之后才动代码。上一块的教训正是这一条：唯一的真缺陷出现在唯一没有证据覆盖的那条路上。

T-005 到 T-007 之间全量套件是红的，这是意料之中：库形状先变、读写路径随后跟上。每个任务有自己那一条转绿的检查。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Docs/Refactor/TableOwnershipRuling.md` | 把 D-5、D-6、D-7、D-15、D-16 五处的结论回写；`items` 的目标形状随 D-15/D-16 的结论调整 | 现有裁定与代码事实 | 五条结论与（若定案需要）更新后的 `items` 声明 |
| BD-002 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 九张源表的声明改为四张目标表；约束、索引与可补列清单按裁定落位 | BD-001 | 新的 `TableDeclarations`、`IndexDeclarations` 与可补列清单 |
| BD-003 | `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`、`CharacterEntryTables.cs` | 按表接口与其行类型随目标表改写；上一代两张表与两张租借表的接口退役 | BD-002 | 每个接口只指名一张目标表 |
| BD-004 | `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`、`InventoryStateTables.cs`、`SqliteItemStore.cs`、`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs` | 读写语句改到目标表；称号册的读取改为从 `items` 取并做逆变换；角色背包的读取排除称号册的容器类型 | BD-002、BD-003 | 与目标形状一致的读写路径 |
| BD-005 | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs`、`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs` | 跨表组装改到新来源；两处迁移守卫随上一代两张表退役；租借的合并读取改为单表 | BD-004 | 领域记录不变的组装 |
| BD-006 | `Server/src/Dfo.Server.Persistence/PersistenceModule.cs` | 注册随接口增减更新 | BD-003 | 与新接口一致的注册 |
| BD-007 | `Server/tests/Dfo.Server.Persistence.Tests/Schema/` 下新增的物品形状与约束守卫，以及 `SchemaOwnershipTests.cs` | 断言目标形状、四张源表的消失与九张源表约束的落点 | BD-001 | 会失败而后转绿的守卫 |
| BD-008 | `Server/tests/Dfo.Server.Persistence.Tests/Inventory/` 下新增的规则与变换守卫 | 覆盖称号册双向变换与共存、背包读取排除、三条规则、跨角色写入 | 裁定与现有读取路径 | 新用例 |
| BD-009 | `Server/tests/Dfo.Server.Persistence.Tests/Inventory/InventoryLoadCharacterizationTests.cs`、`Characters/EntryLoadCharacterizationTests.cs` | 播种改到目标表，期望输出一个字节不改 | BD-002 | 期望值不变的表征守卫 |
| BD-010 | `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteInventoryEntryRepositoryTests.cs`、`SqliteItemStoreTests.cs`、`SqliteEntryInventoryMutationRepositoryTests.cs`、`TwoCarriersInOneTransactionTests.cs`、`Characters/SqliteCharacterEntryStateRepositoryTests.cs`、`Server/tests/Dfo.Server.Protocol.Tests/Game/*`、`Server/tests/Dfo.Server.Application.Tests/*` | 播种与断言随目标形状机械调整，语义不改 | BD-002 | 与新形状一致的既有用例 |

## Ordered Tasks

### T-001: 五处悬置事项定案，回写裁定

- Covers: RQ-005
- Boundaries: BD-001
- Inputs: 裁定的 D-5、D-6、D-7、D-15、D-16 五条；`character_new_items` 与 `character_avatar_detail` 的现有读写语句
- Outputs: 五条结论写进裁定；若 D-15 或 D-16 定为恢复保证，`items` 的目标声明随之更新

#### Steps

1. **D-6**：复核 `character_new_items.character_id` 是否仍恒等于 `owner_id`——清点全部写入语句，确认没有第四个写入者；确认那条 `WHERE character_id=@cid AND list_type=0` 与索引 `idx_character_new_items_character_space` 的改写方式（改为按 `owner_id`）。
2. **D-15**：判断是否存在能产生两行携带同一时装 UID 的路径。若不能，写下理由并维持现状；若能或无法排除，给 `items` 加一个存储时装 UID 的列与一个部分唯一索引。**无法排除时选择恢复保证**——这是一条只增不减的约束，代价是一列。
3. **D-16**：仓库今天只有一个 list type。恢复原有强度只需一个部分唯一索引 `(owner_id, slot_index) WHERE owner_scope = 'account'`，代价极小；除非有理由不加，否则加上。
4. **D-5**：确认 `account_cargo_new_items.character_id` 的读法，为 `items.deposited_by_character_id` 这个名字给出依据或改名。
5. **D-7**：确认「合并后三处一致性检查所测的条件不可表示」这一主张在目标形状上成立，并列出必须保留的两条值检查（30 字节镶嵌槽、两个颜色在 0..65535）。
6. 五条结论写回裁定对应位置，注明依据在哪里可复核。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | 重跑写入语句清点（对 `Server/src` 下的 `INSERT INTO` / `UPDATE` / `DELETE FROM` 逐表计数） | `character_new_items` 的写入只有已知的三插三删两改；无第四个写入者 |
| CK-002 | 裁定的 D-5..D-7、D-15、D-16 五条 | 每条都有结论与依据，不再是「待回答」 |

### T-002: 目标形状、约束落点与称号册变换的守卫先行

- Depends on: T-001
- Covers: RQ-001, RQ-003, RQ-004, RQ-005
- Boundaries: BD-007, BD-008
- Inputs: 裁定（含 T-001 的更新）的「Target shape」与「Keys of the tables that change」；九张源表的现有声明
- Outputs: 一组会失败的断言

#### Steps

1. 从裁定抄出 `items`、`character_rentals`、`character_item_cooldowns`、`character_item_effects` 应有的列名集合，写成对 `PRAGMA table_info` 的断言。
2. 断言四张并入的源表与两张上一代表在库里不存在，`character_item_values` 不存在。
3. 从九张源表的现有声明抄出每一条键、唯一性、检查与外键，逐条写成对新形状的断言：`items` 的 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`、`CHECK (owner_scope IN (...))`、`CHECK (length(item_core) = 82)`、镶嵌槽的条件化 30 字节检查、`character_rentals` 的 `(character_id, item_id)` 主键，以及 T-001 定案后应存在的部分唯一索引。
4. 写称号册变换的断言：`category` 0..4 存为 19..23；按 `category` 读回还原；同一角色的背包 0 号槽与称号册 0 类 0 号槽共存不违反唯一性；角色背包的读取不返回 19..23 的行。
5. 断言索引的去向：`idx_character_new_items_character_space` 改建在 `owner_id` 上；`idx_character_rental_items_char` 与 `idx_rental_items_char` 合并或去除；`idx_account_cargo_items_owner` 与 `idx_character_items_character` 随两张表消失。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --filter FullyQualifiedName~Schema` | 新增断言全部失败，失败原因是目标表不存在或约束缺失，而非断言本身写错 |

### T-003: 清点今天守住三条规则与跨角色写入的用例，补齐缺口

- Depends on: T-002
- Covers: RQ-006, RQ-007
- Boundaries: BD-008
- Inputs: `InventoryEntryAssembly.WithAvatarDetailAsync`、`ReadCargoItemsAsync`、`SqliteCharacterAvatarDetailTable.UpsertOwnAsync` 的现有行为；现有物品侧用例
- Outputs: 五条规则各有至少一条当前为绿的用例

#### Steps

1. 逐条清点今天覆盖以下五件事的用例：非时装行不去找时装列；仓库里的时装不带时装列；角色的时装缺时装列是错误；角色的时装 UID 为零或负被拒绝；一个角色写不到另一个角色的时装行。
2. 缺口逐条补上，**在改动任何生产代码之前**确认新补的用例在当前形状下为绿。
3. 记录每条规则对应哪条用例，供 T-006 改写读写路径时逐条核对。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -m:1 --filter FullyQualifiedName~Inventory` | 全绿；新增用例在当前形状下通过 |
| CK-005 | 五条规则与用例的对应表 | 每条规则至少一条用例，无遗漏 |

### T-004: 表征用例的播种改到目标形状，期望值原样不动

- Depends on: T-003
- Covers: RQ-012
- Boundaries: BD-009
- Inputs: 两份表征用例的现有播种与期望值；裁定的列来源表
- Outputs: 播种写入目标表、期望输出逐字节不变的用例

#### Steps

1. 把 `InventoryLoadCharacterizationTests` 里对四张源表、两张租借表与 `character_item_values` 的 `INSERT` 改写为对目标表的 `INSERT`，逐列按裁定的来源对应；称号册的播种要经过 `19 + category` 变换。
2. `EntryLoadCharacterizationTests` 里称号册那一段同样改写。
3. **不改动任何一处期望输出。** 期望值若必须改，说明搬运改变了行为，停下并回到裁定。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-006 | 两份用例的 `git diff` | 改动只落在 `INSERT` 语句上，期望值零改动 |
| CK-007 | `dotnet test ... --filter FullyQualifiedName~Characterization` | 因目标表不存在而失败，与预期一致 |

### T-005: 库形状改为目标形状

- Depends on: T-004
- Covers: RQ-001, RQ-005, RQ-010
- Boundaries: BD-002
- Inputs: 裁定的目标形状与键约束两节（含 T-001 的更新）
- Outputs: 新的表声明、索引声明与可补列清单

#### Steps

1. 按裁定写出 `items`（15 列或含 T-001 增列）、`character_rentals`（4 列）、`character_item_cooldowns`（4 列）、`character_item_effects`（4 列）的声明，约束逐条落位。
2. 删去 `character_new_items`、`account_cargo_new_items`、`character_new_titlebook`、`character_avatar_detail`、`character_rental_items`、`rental_items`、`character_item_values`、`character_items`、`account_cargo_items` 九张声明。
3. 索引按 T-002 第 5 步的断言落位。
4. 可补列清单删去指向已消失表的条目，为新表补上应有的条目。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-008 | `dotnet test ... --filter FullyQualifiedName~Schema` | T-002 的断言全部转绿 |
| CK-009 | 表数清点 | 49 → 44 |

### T-006: 按表接口与读写路径

- Depends on: T-005
- Covers: RQ-002, RQ-003, RQ-004, RQ-006, RQ-007, RQ-013
- Boundaries: BD-003, BD-004, BD-006
- Inputs: 目标形状；现有读写语句
- Outputs: 一表一类的读写路径

#### Steps

1. `items` 的读写：角色背包读取改为 `owner_scope='character' AND owner_id=@cid` 并**排除称号册的容器类型**；仓库读取改为 `owner_scope='account' AND owner_id=@aid`；称号册读取改为按 19..23 过滤并做逆变换。时装列随物品行一并读出，不再另有一张表。
2. 时装列的写入：改为对物品行的更新，**其 `WHERE` 必须带 `owner_scope='character' AND owner_id=@cid`**——这是今天 `UpsertOwnAsync` 的 `ON CONFLICT ... WHERE` 所保证的那条「一个角色写不到另一个角色的时装」，落到新形状上就是这一条件。
3. 时装列的清除：今天由 `DeleteOwnExceptAsync` 完成；合并后物品行被删时它的时装列随之消失，但**一件物品仍在原槽而其核心块从时装变成非时装**这一情形要有明确处置（清空时装列或拒绝），并写成用例。
4. `character_rentals` 一个类；`character_item_cooldowns` 与 `character_item_effects` 各一个类。
5. 上一代两张表与两张租借表的接口、实现与注册删除。删除任何一个测试文件前，逐条核对其断言证的是不是即将消失的东西。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet build Server/86JP.Server.slnx` | 零错误零警告 |
| CK-011 | `dotnet test ... --filter FullyQualifiedName~Inventory` | T-003 的五条规则用例全部仍绿 |
| CK-012 | 脚本清点 `Server/src/Dfo.Server.Persistence` 下每个类 SQL 中出现的表名 | 零个类指名超过一张表 |

### T-007: 领域组装与迁移守卫退役

- Depends on: T-006
- Covers: RQ-002, RQ-008, RQ-009, RQ-010
- Boundaries: BD-005
- Inputs: 新的读写路径
- Outputs: 领域记录不变的组装

#### Steps

1. `ReadCharacterItemsAsync` 的所有权检查改到新形状：`character_id` 退役后，`InventoryStorageItem.CharacterId` 由 `owner_id` 供给，领域记录逐字段不变。
2. `ReadCargoItemsAsync` 保持「仓库里的时装不带时装列」，并确认这一条在合并后仍由 `owner_scope` 表达。
3. `ReadRentalsAsync` 改为读单表；跨两张源表的重复检查变成单表内的重复检查，拒绝同样的输入。
4. `ReadItemValuesAsync` 改为读两张表，顺序仍是冷却在前、效果在后、各自按 `sort_order`。
5. 两处迁移守卫连同上一代两张表的读取删除。**把「旧形状的库载入得到空背包而不是拒绝」这一后果写进裁定或本 attempt 的记录**，使其是决定而非事故。
6. 称号册在 `CharacterEntryAssembly` 里的消费不变：`category` 0..4 的校验、82 字节校验、item id 为正的校验全部保留。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-013 | 领域记录文件的 `git diff` | 为空 |
| CK-014 | `dotnet test Server/86JP.Server.slnx -m:1` | 全绿（已知假红除外） |
| CK-015 | 两份表征用例的期望值 | 仍为零改动且通过 |

### T-008: 行为保持证据

- Depends on: T-007
- Covers: RQ-011, RQ-012, RQ-013
- Boundaries: 无新增
- Inputs: 完成后的工作区
- Outputs: 验证阶段可引用的证据

#### Steps

1. 全量套件，记录通过/跳过/失败与用例数变化，逐条解释增减。
2. 可证伪性演示：在 `items` 的读取里把两列对调，确认表征用例变红；在称号册的逆变换里去掉减 19 这一步，确认变换守卫变红。两次都还原并确认转绿。
3. 一表一类清点；领域记录定义点清点。
4. 选人下发帧的字节比对以整记录表征用例加全量代替，理由与上一块相同并写明。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | `dotnet test Server/86JP.Server.slnx -m:1` | 全绿；红色项仅限已知满载假红且隔离复跑为绿 |
| CK-017 | 两次可证伪性演示 | 均先红后绿 |
| CK-018 | `git rev-parse HEAD` | 仍为 `5aba3a9dcea2b302797eb33097ae923a7a824a45`，未产生任务级提交 |

## Requirement Coverage

| RQ | Tasks |
|---|---|
| RQ-001 | T-002, T-005 |
| RQ-002 | T-006, T-007 |
| RQ-003 | T-002, T-006 |
| RQ-004 | T-002, T-006 |
| RQ-005 | T-001, T-002, T-005 |
| RQ-006 | T-003, T-006 |
| RQ-007 | T-003, T-006 |
| RQ-008 | T-007 |
| RQ-009 | T-007 |
| RQ-010 | T-005, T-007 |
| RQ-011 | T-008 |
| RQ-012 | T-004, T-008 |
| RQ-013 | T-006, T-008 |

## Risks

| Risk | Mitigation |
|---|---|
| 称号册变换写对一半——存进去对、读回来错，或反之 | T-002 的断言同时覆盖两个方向与共存，缺一不可 |
| 三条写不成 `CHECK` 的规则在改写读取路径时静默丢失 | T-003 先把它们变成绿灯用例，T-006 的 CK-011 逐条复核 |
| 时装列的跨角色写入守卫在形态变化中丢失 | T-006 第 2 步把它明确成物品行更新的 `WHERE` 条件，并由 T-003 的用例守住 |
| 一件物品从时装变成非时装后留下陈旧的时装列 | T-006 第 3 步要求明确处置并写成用例 |
| 删测试文件时连带删掉保护存续行为的断言 | T-006 第 5 步：删除前逐条核对断言证的是不是即将消失的东西 |
